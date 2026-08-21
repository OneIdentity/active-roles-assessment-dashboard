using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Evaluates the <see cref="AssessmentRuleLibrary"/> against a populated
/// <see cref="DashboardSummary"/> and produces a scored, categorized result.
/// Also persists, lists, loads and deletes assessment results on disk.
/// </summary>
public class AssessmentService
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly IWebHostEnvironment _env;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<ActiveRolesConfig> _config;

    public AssessmentService(IWebHostEnvironment env, Microsoft.Extensions.Options.IOptionsMonitor<ActiveRolesConfig> config)
    {
        _env = env;
        _config = config;
    }

    private string AssessmentFolder
    {
        get
        {
            var configured = _config.CurrentValue.AssessmentDirectory;
            if (string.IsNullOrWhiteSpace(configured))
                configured = "App_Data/Assessments";
            return Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(_env.ContentRootPath, configured);
        }
    }

    public AssessmentResult Evaluate(DashboardSummary summary, AssessmentType type, string? label = null, string? generatedBy = null)
    {
        var result = new AssessmentResult
        {
            Type = type,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            GeneratedBy = generatedBy
        };

        var categories = new Dictionary<string, AssessmentCategory>(StringComparer.OrdinalIgnoreCase);

        var staleThresholdDays = _config.CurrentValue.StaleAccountThresholdDays > 0
            ? _config.CurrentValue.StaleAccountThresholdDays
            : 90;

        double weightedPenalty = 0;
        double maxPenalty = 0;

        foreach (var rule in AssessmentRuleLibrary.ForType(type))
        {
            var (count, error) = summary.GetKpiResult(rule.KpiKey);
            var check = new AssessmentCheck
            {
                RuleId = rule.Id,
                Title = rule.Title,
                CategoryName = rule.CategoryName,
                Severity = rule.Severity,
                Count = count,
                Error = error,
                Recommendation = rule.Recommendation.Replace("{StaleThresholdDays}", staleThresholdDays.ToString())
            };

            if (error != null)
            {
                check.Status = AssessmentStatus.NotApplicable;
                result.NotApplicableCount++;
            }
            else
            {
                check.Status = Evaluate(rule, count);
                switch (check.Status)
                {
                    case AssessmentStatus.Pass: result.PassCount++; break;
                    case AssessmentStatus.Warning: result.WarnCount++; break;
                    case AssessmentStatus.Fail: result.FailCount++; break;
                }

                // Score contribution: each applicable rule can lose up to its severity weight.
                var weight = SeverityWeight(rule.Severity);
                maxPenalty += weight;
                weightedPenalty += check.Status switch
                {
                    AssessmentStatus.Fail => weight,
                    AssessmentStatus.Warning => weight * 0.5,
                    _ => 0
                };
            }

            if (!categories.TryGetValue(rule.CategoryName, out var category))
            {
                category = new AssessmentCategory { Name = rule.CategoryName };
                categories[rule.CategoryName] = category;
            }
            category.Checks.Add(check);
            result.TotalChecks++;
        }

        // Order categories by worst outcome first, then rules by severity/status.
        result.Categories = categories.Values
            .OrderByDescending(c => c.FailCount)
            .ThenByDescending(c => c.WarnCount)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var category in result.Categories)
        {
            category.Checks = category.Checks
                .OrderByDescending(c => (int)c.Status == (int)AssessmentStatus.Fail)
                .ThenByDescending(c => c.Severity)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        result.Score = maxPenalty <= 0
            ? 100
            : (int)Math.Round(100 * (1 - (weightedPenalty / maxPenalty)));
        result.Grade = GradeFor(result.Score);

        return result;
    }

    private static AssessmentStatus Evaluate(AssessmentRule rule, int count)
    {
        if (rule.Comparison == AssessmentComparison.AtMost)
        {
            // Inverted: a lower count is worse (adoption/coverage rules).
            if (count <= rule.FailThreshold)
                return AssessmentStatus.Fail;
            if (count <= rule.WarnThreshold)
                return AssessmentStatus.Warning;
            return AssessmentStatus.Pass;
        }

        if (count >= rule.FailThreshold)
            return AssessmentStatus.Fail;
        if (count >= rule.WarnThreshold)
            return AssessmentStatus.Warning;
        return AssessmentStatus.Pass;
    }

    private static double SeverityWeight(AssessmentSeverity severity) => severity switch
    {
        AssessmentSeverity.Critical => 5,
        AssessmentSeverity.High => 4,
        AssessmentSeverity.Medium => 3,
        AssessmentSeverity.Low => 2,
        _ => 1
    };

    private static string GradeFor(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };

    // -----------------------------------------------------------------------
    // Persistence
    // -----------------------------------------------------------------------

    public async Task SaveAsync(AssessmentResult result)
    {
        Directory.CreateDirectory(AssessmentFolder);
        var path = Path.Combine(AssessmentFolder, result.Id + ".json");
        await using var stream = File.Create(path);
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, result, _jsonOptions);
    }

    /// <summary>Returns assessment headers (newest first), optionally filtered by type, without loading full bodies.</summary>
    public async Task<List<AssessmentHeader>> ListAsync(AssessmentType? type = null)
    {
        var headers = new List<AssessmentHeader>();
        if (!Directory.Exists(AssessmentFolder))
            return headers;

        foreach (var file in Directory.EnumerateFiles(AssessmentFolder, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var result = await System.Text.Json.JsonSerializer.DeserializeAsync<AssessmentResult>(stream, _jsonOptions);
                if (result == null)
                    continue;
                if (type != null && result.Type != type.Value)
                    continue;

                headers.Add(new AssessmentHeader
                {
                    Id = result.Id,
                    Type = result.Type,
                    Label = result.Label,
                    GeneratedBy = result.GeneratedBy,
                    GeneratedUtc = result.GeneratedUtc,
                    Score = result.Score,
                    Grade = result.Grade,
                    FailCount = result.FailCount,
                    WarnCount = result.WarnCount,
                    PassCount = result.PassCount
                });
            }
            catch
            {
                // Skip unreadable/corrupt files rather than failing the whole list.
            }
        }

        return headers.OrderByDescending(h => h.GeneratedUtc).ToList();
    }

    public async Task<AssessmentResult?> LoadAsync(string id)
    {
        var path = ResolvePath(id);
        if (path == null || !File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<AssessmentResult>(stream, _jsonOptions);
    }

    public bool Delete(string id)
    {
        var path = ResolvePath(id);
        if (path == null || !File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Compares a baseline assessment run (<paramref name="from"/>) against another
    /// run (<paramref name="to"/>), keyed by rule id. Checks present on only one side
    /// are reported as Added/Removed. Status transitions are classified as Improved,
    /// Worsened, or Unchanged with a good/bad/neutral sentiment. Pure, no I/O.
    /// </summary>
    public AssessmentRunComparison Compare(AssessmentResult from, AssessmentResult to, bool toIsCurrent = false)
    {
        var comparison = new AssessmentRunComparison
        {
            From = ToHeader(from),
            To = ToHeader(to),
            ToIsCurrent = toIsCurrent,
            Type = from.Type,
            ScoreChange = to.Score - from.Score,
            FailChange = to.FailCount - from.FailCount,
            WarnChange = to.WarnCount - from.WarnCount,
            PassChange = to.PassCount - from.PassCount
        };

        // Index every check on each side by rule id.
        var fromChecks = from.Categories.SelectMany(c => c.Checks)
            .GroupBy(c => c.RuleId).ToDictionary(g => g.Key, g => g.First());
        var toChecks = to.Categories.SelectMany(c => c.Checks)
            .GroupBy(c => c.RuleId).ToDictionary(g => g.Key, g => g.First());

        // Preserve category ordering: baseline categories first, then any
        // categories that only exist in the "to" run.
        var categoryOrder = new List<string>();
        foreach (var cat in from.Categories)
            if (!categoryOrder.Contains(cat.Name))
                categoryOrder.Add(cat.Name);
        foreach (var cat in to.Categories)
            if (!categoryOrder.Contains(cat.Name))
                categoryOrder.Add(cat.Name);

        var rowsByCategory = new Dictionary<string, List<AssessmentComparisonRow>>();

        void AddRow(string categoryName, AssessmentComparisonRow row)
        {
            if (!rowsByCategory.TryGetValue(categoryName, out var list))
            {
                list = new List<AssessmentComparisonRow>();
                rowsByCategory[categoryName] = list;
            }
            list.Add(row);
        }

        // Union of all rule ids, preserving baseline order then added rules.
        var ruleOrder = new List<string>();
        foreach (var cat in from.Categories)
            foreach (var chk in cat.Checks)
                if (!ruleOrder.Contains(chk.RuleId))
                    ruleOrder.Add(chk.RuleId);
        foreach (var cat in to.Categories)
            foreach (var chk in cat.Checks)
                if (!ruleOrder.Contains(chk.RuleId))
                    ruleOrder.Add(chk.RuleId);

        foreach (var ruleId in ruleOrder)
        {
            fromChecks.TryGetValue(ruleId, out var f);
            toChecks.TryGetValue(ruleId, out var t);

            var reference = t ?? f!;
            var row = new AssessmentComparisonRow
            {
                RuleId = ruleId,
                Title = reference.Title,
                Severity = reference.Severity,
                FromStatus = f?.Status,
                ToStatus = t?.Status,
                FromCount = f?.Count,
                ToCount = t?.Count
            };

            if (f == null && t != null)
            {
                row.Delta = CheckDelta.Added;
                row.Sentiment = CheckDeltaSentiment.Neutral;
                comparison.AddedCount++;
            }
            else if (f != null && t == null)
            {
                row.Delta = CheckDelta.Removed;
                row.Sentiment = CheckDeltaSentiment.Neutral;
                comparison.RemovedCount++;
            }
            else
            {
                var rank = StatusRank(t!.Status) - StatusRank(f!.Status);
                if (rank > 0)
                {
                    row.Delta = CheckDelta.Improved;
                    row.Sentiment = CheckDeltaSentiment.Good;
                    comparison.ImprovedCount++;
                }
                else if (rank < 0)
                {
                    row.Delta = CheckDelta.Worsened;
                    row.Sentiment = CheckDeltaSentiment.Bad;
                    comparison.WorsenedCount++;
                }
                else
                {
                    row.Delta = CheckDelta.Unchanged;
                    row.Sentiment = CheckDeltaSentiment.Neutral;
                    comparison.UnchangedCount++;
                }
            }

            AddRow(reference.CategoryName, row);
        }

        foreach (var categoryName in categoryOrder)
        {
            if (!rowsByCategory.TryGetValue(categoryName, out var rows) || rows.Count == 0)
                continue;
            comparison.Categories.Add(new AssessmentComparisonCategory
            {
                Name = categoryName,
                Rows = rows
            });
        }

        return comparison;
    }

    /// <summary>
    /// Ranks a status so that a HIGHER rank is a BETTER outcome. Used to decide
    /// whether a check improved or worsened between two runs.
    /// NotApplicable sits between Fail and Warning (data unavailable is neutral-ish).
    /// </summary>
    private static int StatusRank(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Fail => 0,
        AssessmentStatus.NotApplicable => 1,
        AssessmentStatus.Warning => 2,
        AssessmentStatus.Pass => 3,
        _ => 1
    };

    private static AssessmentHeader ToHeader(AssessmentResult result) => new()
    {
        Id = result.Id,
        Type = result.Type,
        Label = result.Label,
        GeneratedBy = result.GeneratedBy,
        GeneratedUtc = result.GeneratedUtc,
        Score = result.Score,
        Grade = result.Grade,
        FailCount = result.FailCount,
        WarnCount = result.WarnCount,
        PassCount = result.PassCount
    };

    /// <summary>Resolves an assessment file path from an id, guarding against path traversal.</summary>
    private string? ResolvePath(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var fileName = id + ".json";
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..") ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return Path.Combine(AssessmentFolder, fileName);
    }
}
