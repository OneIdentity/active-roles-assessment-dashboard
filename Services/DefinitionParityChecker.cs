using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Shadow-evaluates KPIs whose definitions have been migrated to expressions and compares the
/// result with the value produced by the hand-written logic. The legacy value is still the one
/// displayed; mismatches are logged and kept for diagnostics so migrations can be verified
/// against live data before the legacy code is removed.
/// </summary>
public sealed class DefinitionParityChecker(DefinitionRegistry registry, KpiEvaluator evaluator, ILogger<DefinitionParityChecker> logger)
{
    private readonly object _gate = new();
    private IReadOnlyList<KpiParityResult> _last = [];

    public IReadOnlyList<KpiParityResult> LastResults
    {
        get { lock (_gate) return _last; }
    }

    /// <summary>Maps dataset ids to the items already collected for the summary.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> CollectedDatasets(DashboardSummary summary)
    {
        var map = new Dictionary<string, IReadOnlyList<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        if (summary.ADUserAccounts.Error is null && summary.ADUserAccounts.Items.Count > 0)
            map["ADUserAccounts"] = summary.ADUserAccounts.Items;
        return map;
    }

    public static EvaluationContext CreateContext(ActiveRolesConfig config, DateTimeOffset now) => new()
    {
        Now = now,
        Parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["staleDays"] = (double)(config.StaleAccountThresholdDays > 0 ? config.StaleAccountThresholdDays : 90),
            ["passwordAgeDays"] = (double)(config.PasswordAgeThresholdDays > 0 ? config.PasswordAgeThresholdDays : 365),
        }
    };

    public IReadOnlyList<KpiParityResult> Check(DashboardSummary summary, KpiSettings settings, EvaluationContext context)
    {
        var datasets = CollectedDatasets(summary);
        var results = new List<KpiParityResult>();

        foreach (var kpi in registry.Set.Kpis.OfType<DerivedKpi>())
        {
            if (kpi.Evaluation.Type != EvaluationType.Expression || kpi.SourceDatasetId is null) continue;
            if (!datasets.TryGetValue(kpi.SourceDatasetId, out var items)) continue;
            if (!settings.IsKpiEnabled(kpi.CategoryId, kpi.Id)) continue;

            var (legacy, error) = summary.GetKpiResult(kpi.Id);
            if (error is not null) continue;

            long? generic;
            string? failure = null;
            try
            {
                generic = evaluator.Evaluate(kpi.Evaluation, items, context);
            }
            catch (ExpressionException ex)
            {
                generic = null;
                failure = ex.Message;
            }

            var result = new KpiParityResult(kpi.Id, legacy, generic, failure);
            results.Add(result);
            if (!result.Matches)
                logger.LogWarning("Definition parity mismatch for KPI {Kpi}: legacy {Legacy}, definition {Generic} {Failure}", kpi.Id, legacy, generic, failure);
        }

        if (results.Count > 0)
            logger.LogInformation("Definition parity: {Matched}/{Total} migrated KPIs match", results.Count(r => r.Matches), results.Count);

        lock (_gate) _last = results;
        return results;
    }
}

public sealed record KpiParityResult(string KpiId, long Legacy, long? Definition, string? Error)
{
    public bool Matches => Error is null && Definition == Legacy;
}
