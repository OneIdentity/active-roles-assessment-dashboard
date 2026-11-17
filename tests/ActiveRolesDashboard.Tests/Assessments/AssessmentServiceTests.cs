using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Reporting;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ActiveRolesDashboard.Tests.Assessments;

[Trait("Stage", "Assessments")]
public class AssessmentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ard-tests", Guid.NewGuid().ToString("N"));
    private readonly ActiveRolesConfig _config = new();
    private readonly AssessmentService _service;

    public AssessmentServiceTests()
    {
        Directory.CreateDirectory(_root);
        var monitor = new TestOptionsMonitor<ActiveRolesConfig>(_config);
        _service = new AssessmentService(new TestEnvironment(_root), monitor, new KpiThresholdService(monitor), new KpiExclusionService(monitor));
    }

    public static TheoryData<AssessmentType> AllTypes()
    {
        var data = new TheoryData<AssessmentType>();
        foreach (var t in Enum.GetValues<AssessmentType>()) data.Add(t);
        return data;
    }

    // Known defect: some rule IDs are defined more than once, so a run can contain several checks per ID.
    private static AssessmentCheck Check(AssessmentResult r, string ruleId)
    {
        var checks = r.Categories.SelectMany(c => c.Checks).Where(c => c.RuleId == ruleId).ToList();
        Assert.NotEmpty(checks);
        Assert.All(checks, c => Assert.Equal(checks[0].Status, c.Status));
        return checks[0];
    }

    private static AssessmentStatus ExpectedFor(AssessmentRule rule, int count) => rule.Comparison == AssessmentComparison.AtMost
        ? count <= rule.FailThreshold ? AssessmentStatus.Fail : count <= rule.WarnThreshold ? AssessmentStatus.Warning : AssessmentStatus.Pass
        : count >= rule.FailThreshold ? AssessmentStatus.Fail : count >= rule.WarnThreshold ? AssessmentStatus.Warning : AssessmentStatus.Pass;

    private static DashboardSummary WithPasswordNotRequired(int count, string? error = null) => new()
    {
        PasswordNotRequired = new ADUserAccountDetailSummary { TotalCount = count, Error = error }
    };

    // --- Rule library -----------------------------------------------------------

    [Fact]
    public void Rule_ids_are_unique()
    {
        var ids = AssessmentRuleLibrary.All.Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Rules_are_well_formed()
    {
        Assert.All(AssessmentRuleLibrary.All, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title), r.Id);
            Assert.False(string.IsNullOrWhiteSpace(r.KpiKey), r.Id);
            Assert.False(string.IsNullOrWhiteSpace(r.Recommendation), r.Id);
            Assert.NotEmpty(r.Types);
        });
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Every_assessment_type_has_rules(AssessmentType type)
    {
        Assert.NotEmpty(AssessmentRuleLibrary.ForType(type));
    }

    // --- Evaluation -------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Empty_environment_evaluates_every_rule_consistently(AssessmentType type)
    {
        var result = _service.Evaluate(new DashboardSummary(), type);
        var rules = AssessmentRuleLibrary.ForType(type).ToList();

        Assert.Equal(type, result.Type);
        Assert.Equal(rules.Count, result.TotalChecks);
        Assert.Equal(result.TotalChecks, result.PassCount + result.WarnCount + result.FailCount + result.NotApplicableCount);
        Assert.InRange(result.Score, 0, 100);
        Assert.Contains(result.Grade, new[] { "A", "B", "C", "D", "F" });
        foreach (var check in result.Categories.SelectMany(c => c.Checks))
        {
            if (check.Error == null)
                Assert.Contains(rules.Where(r => r.Id == check.RuleId), r => ExpectedFor(r, check.Count) == check.Status);
            else
                Assert.Equal(AssessmentStatus.NotApplicable, check.Status);
        }
    }

    [Theory]
    [InlineData(0, AssessmentStatus.Pass)]
    [InlineData(1, AssessmentStatus.Fail)]
    [InlineData(int.MaxValue, AssessmentStatus.Fail)]
    public void At_least_rule_boundaries(int count, AssessmentStatus expected)
    {
        var result = _service.Evaluate(WithPasswordNotRequired(count), AssessmentType.ActiveDirectory);
        Assert.Equal(expected, Check(result, "SEC-PasswordNotRequired").Status);
    }

    [Fact]
    public void Configured_thresholds_produce_warning_band()
    {
        _config.KpiThresholds["PasswordNotRequired"] = new KpiThresholdConfig { Medium = 2, High = 5 };

        Assert.Equal(AssessmentStatus.Pass, Check(_service.Evaluate(WithPasswordNotRequired(1), AssessmentType.ActiveDirectory), "SEC-PasswordNotRequired").Status);
        Assert.Equal(AssessmentStatus.Warning, Check(_service.Evaluate(WithPasswordNotRequired(4), AssessmentType.ActiveDirectory), "SEC-PasswordNotRequired").Status);
        Assert.Equal(AssessmentStatus.Fail, Check(_service.Evaluate(WithPasswordNotRequired(5), AssessmentType.ActiveDirectory), "SEC-PasswordNotRequired").Status);
    }

    [Fact]
    public void Errored_kpi_is_not_applicable_and_excluded_from_score()
    {
        var baseline = _service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory);
        var result = _service.Evaluate(WithPasswordNotRequired(50, "LDAP timeout"), AssessmentType.ActiveDirectory);
        var check = Check(result, "SEC-PasswordNotRequired");

        Assert.Equal(AssessmentStatus.NotApplicable, check.Status);
        Assert.Equal("LDAP timeout", check.Error);
        Assert.Equal(baseline.NotApplicableCount + PnrRules, result.NotApplicableCount);
        Assert.True(result.Score >= baseline.Score);
    }

    [Fact]
    public void Failing_critical_rule_lowers_score()
    {
        var clean = _service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory);
        var failing = _service.Evaluate(WithPasswordNotRequired(3), AssessmentType.ActiveDirectory);

        Assert.True(failing.Score < clean.Score);
        Assert.Equal(clean.FailCount + PnrRules, failing.FailCount);
    }

    [Fact]
    public void Exclusions_apply_before_evaluation()
    {
        _config.KpiExclusions.Global = ["svc_*"];
        var summary = new DashboardSummary
        {
            PasswordNotRequired = new ADUserAccountDetailSummary { TotalCount = 1, Items = [new() { Name = "svc_x", Dn = "CN=svc_x" }] }
        };

        Assert.Equal(AssessmentStatus.Pass, Check(_service.Evaluate(summary, AssessmentType.ActiveDirectory), "SEC-PasswordNotRequired").Status);
    }

    [Fact]
    public void Categories_ordered_worst_first_and_failures_lead_checks()
    {
        var result = _service.Evaluate(WithPasswordNotRequired(3), AssessmentType.ActiveDirectory);

        var keys = result.Categories.Select(c => (c.FailCount, c.WarnCount)).ToList();
        Assert.Equal(keys.OrderByDescending(k => k.FailCount).ThenByDescending(k => k.WarnCount).ToList(), keys);
        foreach (var cat in result.Categories)
        {
            var firstNonFail = cat.Checks.FindIndex(c => c.Status != AssessmentStatus.Fail);
            if (firstNonFail >= 0)
                Assert.DoesNotContain(cat.Checks.Skip(firstNonFail), c => c.Status == AssessmentStatus.Fail);
        }
    }

    [Theory]
    [InlineData("  Q1  ", "Q1")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Label_is_trimmed_or_null(string? label, string? expected)
    {
        Assert.Equal(expected, _service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory, label, "me").Label);
    }

    [Fact]
    public void Stale_threshold_placeholder_is_substituted()
    {
        _config.StaleAccountThresholdDays = 45;
        foreach (var type in Enum.GetValues<AssessmentType>())
            Assert.All(_service.Evaluate(new DashboardSummary(), type).Categories.SelectMany(c => c.Checks),
                c => Assert.DoesNotContain("{StaleThresholdDays}", c.Recommendation));
    }

    // --- Persistence ------------------------------------------------------------

    [Fact]
    public async Task Save_load_list_delete_round_trip()
    {
        var result = _service.Evaluate(WithPasswordNotRequired(2), AssessmentType.ActiveDirectory, "run");
        await _service.SaveAsync(result);

        var loaded = await _service.LoadAsync(result.Id);
        Assert.NotNull(loaded);
        Assert.Equal((result.Score, result.Grade, result.FailCount, result.TotalChecks), (loaded!.Score, loaded.Grade, loaded.FailCount, loaded.TotalChecks));
        Assert.Equal(AssessmentStatus.Fail, Check(loaded, "SEC-PasswordNotRequired").Status);

        Assert.Single(await _service.ListAsync(AssessmentType.ActiveDirectory));
        Assert.Empty(await _service.ListAsync(AssessmentType.Entra));

        Assert.True(_service.Delete(result.Id));
        Assert.False(_service.Delete(result.Id));
        Assert.Null(await _service.LoadAsync(result.Id));
    }

    [Fact]
    public async Task List_is_newest_first_and_skips_corrupt_files()
    {
        Assert.Empty(await _service.ListAsync());

        var older = _service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory);
        older.GeneratedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = _service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory);
        await _service.SaveAsync(older);
        await _service.SaveAsync(newer);
        var folder = Path.Combine(_root, "App_Data", "Assessments");
        await File.WriteAllTextAsync(Path.Combine(folder, "bad.json"), "{ nope");

        var list = await _service.ListAsync();
        Assert.Equal([newer.Id, older.Id], list.Select(h => h.Id).ToList());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../appsettings")]
    [InlineData(@"..\appsettings")]
    [InlineData("a/b")]
    [InlineData("bad|name")]
    public async Task Path_traversal_ids_are_rejected(string id)
    {
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{}");
        Assert.Null(await _service.LoadAsync(id));
        Assert.False(_service.Delete(id));
        Assert.True(File.Exists(Path.Combine(_root, "appsettings.json")));
    }

    [Fact]
    public async Task Absolute_assessment_directory_is_honoured()
    {
        _config.AssessmentDirectory = Path.Combine(_root, "custom");
        await _service.SaveAsync(_service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory));
        Assert.Single(Directory.GetFiles(_config.AssessmentDirectory, "*.json"));
    }

    // --- Comparison -------------------------------------------------------------

    [Fact]
    public void Compare_classifies_improved_worsened_unchanged()
    {
        var bad = _service.Evaluate(WithPasswordNotRequired(3), AssessmentType.ActiveDirectory);
        var good = _service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory);

        var improved = _service.Compare(bad, good, toIsCurrent: true);
        var row = improved.Categories.SelectMany(c => c.Rows).Single(r => r.RuleId == "SEC-PasswordNotRequired");
        Assert.Equal((CheckDelta.Improved, CheckDeltaSentiment.Good), (row.Delta, row.Sentiment));
        Assert.True(improved.ImprovedCount >= 1);
        Assert.Equal(good.Score - bad.Score, improved.ScoreChange);
        Assert.Equal(-PnrRules, improved.FailChange);
        Assert.True(improved.ToIsCurrent);
        Assert.Equal(DistinctRuleIds(bad) - improved.ImprovedCount, improved.UnchangedCount);

        var worsened = _service.Compare(good, bad);
        Assert.Equal(improved.ImprovedCount, worsened.WorsenedCount);
        Assert.Equal(CheckDeltaSentiment.Bad, worsened.Categories.SelectMany(c => c.Rows).Single(r => r.RuleId == "SEC-PasswordNotRequired").Sentiment);
    }

    [Fact]
    public void Compare_not_applicable_ranks_between_fail_and_warning()
    {
        var failing = _service.Evaluate(WithPasswordNotRequired(3), AssessmentType.ActiveDirectory);
        var errored = _service.Evaluate(WithPasswordNotRequired(0, "down"), AssessmentType.ActiveDirectory);

        var row = _service.Compare(failing, errored).Categories.SelectMany(c => c.Rows).Single(r => r.RuleId == "SEC-PasswordNotRequired");
        Assert.Equal(CheckDelta.Improved, row.Delta);
    }

    [Fact]
    public void Compare_reports_added_and_removed_rules()
    {
        var full = _service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory);
        var empty = new AssessmentResult { Type = AssessmentType.ActiveDirectory };

        Assert.Equal(DistinctRuleIds(full), _service.Compare(empty, full).AddedCount);
        var removed = _service.Compare(full, empty);
        Assert.Equal(DistinctRuleIds(full), removed.RemovedCount);
        Assert.All(removed.Categories.SelectMany(c => c.Rows), r => Assert.Null(r.ToStatus));
        Assert.Empty(_service.Compare(empty, empty).Categories);
    }

    private static int DistinctRuleIds(AssessmentResult r) =>
        r.Categories.SelectMany(c => c.Checks).Select(c => c.RuleId).Distinct().Count();

    private static int PnrRules =>
        AssessmentRuleLibrary.ForType(AssessmentType.ActiveDirectory).Count(r => r.KpiKey == "PasswordNotRequired");

    // --- Report builder ---------------------------------------------------------

    [Fact]
    public void Report_builder_maps_scorecard_and_checks()
    {
        var result = _service.Evaluate(WithPasswordNotRequired(0, "down"), AssessmentType.ActiveDirectory, "Q1");
        var model = new AssessmentReportBuilder().Build(result, "");

        Assert.Contains("Q1", model.Subtitle);
        Assert.Contains($"Score {result.Score}/100", model.Subtitle);
        Assert.Equal(result.GeneratedBy ?? "", model.GeneratedBy);
        var tiles = model.Sections.Single(s => s.Heading == "Summary").Tiles;
        Assert.Equal(result.PassCount, tiles.Single(t => t.Label == "Passed").Value);
        Assert.Equal(result.NotApplicableCount, tiles.Single(t => t.Label == "N/A").Value);

        var rows = model.Sections.SelectMany(s => s.Tables).Where(t => t.Columns.Count == 5).SelectMany(t => t.Rows).ToList();
        Assert.Equal(result.TotalChecks, rows.Count);
        Assert.Contains(rows, r => r[3] == "N/A" && r[2] == "\u2014" && r[4] == "down");
        Assert.All(rows.Where(r => r[3] == "Pass"), r => Assert.Equal("No action required", r[4]));
    }

    [Fact]
    public void Report_builder_omits_na_tile_when_none()
    {
        var result = new AssessmentResult { Score = 100, Grade = "A" };
        var model = new AssessmentReportBuilder().Build(result, "me");

        Assert.Equal("me", model.GeneratedBy);
        Assert.DoesNotContain(model.Sections.SelectMany(s => s.Tiles), t => t.Label == "N/A");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ActiveRolesDashboard";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
