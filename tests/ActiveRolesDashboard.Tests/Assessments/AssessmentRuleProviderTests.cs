using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.AssessmentUpdates;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Assessments;

[Trait("Stage", "Assessments")]
public class AssessmentRuleProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ard-tests", Guid.NewGuid().ToString("N"));
    private readonly AssessmentUpdateStore _store;
    private readonly AssessmentRuleProvider _provider;
    private readonly AssessmentService _service;

    public AssessmentRuleProviderTests()
    {
        Directory.CreateDirectory(_root);
        _store = new AssessmentUpdateStore(Path.Combine(_root, "updates"));
        _provider = new AssessmentRuleProvider(_store);
        var monitor = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig());
        _service = new AssessmentService(new TestEnvironment(_root), monitor, new KpiThresholdService(monitor), new KpiExclusionService(monitor), _provider);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static readonly AssessmentRule Rule =
        AssessmentRuleLibrary.ForType(AssessmentType.ActiveDirectory).First(r => r.KpiKey == "PasswordNotRequired");

    private static DashboardSummary Summary(int count) => new()
    {
        PasswordNotRequired = new ADUserAccountDetailSummary { TotalCount = count },
        KpiResults = { ["PasswordNotRequired"] = new Models.Definitions.KpiResult(count, null) }
    };

    private static string Pack(string operations) => AssessmentUpdateHasher.Seal($$"""
        { "schemaVersion": 1, "id": "p1", "version": "1.0", "title": "T", "operations": [ {{operations}} ] }
        """);

    private AssessmentCheck CheckFor(string ruleId) =>
        _service.Evaluate(Summary(Rule.FailThreshold), AssessmentType.ActiveDirectory)
            .Categories.SelectMany(c => c.Checks).Single(c => c.RuleId == ruleId);

    private sealed class TestEnvironment(string root) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "ActiveRolesDashboard";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
    }

    [Fact]
    public void NoPacks_ProviderMatchesBuiltInLibrary()
    {
        foreach (var type in Enum.GetValues<AssessmentType>())
        {
            Assert.Equal(AssessmentRuleLibrary.ForType(type).Select(r => r.Id), _provider.ForType(type).Select(r => r.Id));
            Assert.Equal(AssessmentRuleLibrary.DependsOnEntraGroupMembership(type), _provider.DependsOnEntraGroupMembership(type));
        }
    }

    [Fact]
    public void ModifiedThreshold_ChangesEvaluation_AndDisableRestoresIt()
    {
        Assert.Equal(AssessmentStatus.Fail, CheckFor(Rule.Id).Status);

        var high = Rule.FailThreshold + 100;
        _store.Import(Pack($$"""{ "op": "modify", "ruleId": "{{Rule.Id}}", "rule": { "warnThreshold": {{high}}, "failThreshold": {{high}} } }"""), "u", true);
        Assert.Equal(AssessmentStatus.Pass, CheckFor(Rule.Id).Status);

        _store.SetEnabled("p1", false, "u");
        Assert.Equal(AssessmentStatus.Fail, CheckFor(Rule.Id).Status);
    }

    [Fact]
    public void RetiredRule_IsNotEvaluated()
    {
        _store.Import(Pack($$"""{ "op": "retire", "ruleId": "{{Rule.Id}}" }"""), "u", true);
        var result = _service.Evaluate(Summary(1), AssessmentType.ActiveDirectory);
        Assert.DoesNotContain(result.Categories.SelectMany(c => c.Checks), c => c.RuleId == Rule.Id);
    }

    [Fact]
    public void AddedRule_IsEvaluated()
    {
        _store.Import(Pack("""{ "op": "add", "ruleId": "UPD-NEW-1", "rule": { "title": "New check", "categoryName": "Updates", "kpiKey": "PasswordNotRequired", "types": ["activeDirectory"], "severity": "high", "warnThreshold": 1, "failThreshold": 1, "recommendation": "Fix it" } }"""), "u", true);
        var check = CheckFor("UPD-NEW-1");
        Assert.Equal(AssessmentStatus.Fail, check.Status);
        Assert.Equal(AssessmentSeverity.High, check.Severity);
    }

    [Fact]
    public void Result_RecordsFingerprintAndAppliedPacks()
    {
        var baseline = _service.Evaluate(Summary(1), AssessmentType.ActiveDirectory);
        Assert.False(string.IsNullOrEmpty(baseline.RuleSetFingerprint));
        Assert.Empty(baseline.AppliedUpdates);

        _store.Import(Pack($$"""{ "op": "modify", "ruleId": "{{Rule.Id}}", "rule": { "title": "Updated title" } }"""), "u", true);
        var updated = _service.Evaluate(Summary(1), AssessmentType.ActiveDirectory);

        Assert.NotEqual(baseline.RuleSetFingerprint, updated.RuleSetFingerprint);
        var applied = Assert.Single(updated.AppliedUpdates);
        Assert.Equal("p1", applied.Id);
        Assert.Equal("1.0", applied.Version);
        Assert.Matches("^[0-9a-f]{64}$", applied.Sha256);
    }

    [Fact]
    public void PackLocalizedText_TakesPrecedence_AndChangedFieldFallsBackToPersistedText()
    {
        _store.Import(Pack($$"""{ "op": "modify", "ruleId": "{{Rule.Id}}", "rule": { "title": "Updated title", "localized": { "de": { "title": "Neuer Titel" } } } }"""), "u", true);
        var check = CheckFor(Rule.Id);
        Assert.True(check.TitleFromUpdate);
        Assert.False(check.RecommendationFromUpdate);

        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("Neuer Titel", AssessmentLocalizer.Title(check));

            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fr-FR");
            Assert.Equal("Updated title", AssessmentLocalizer.Title(check));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task SavedResult_KeepsProvenanceAndPackText_AfterPackRemoved()
    {
        _store.Import(Pack($$"""{ "op": "modify", "ruleId": "{{Rule.Id}}", "rule": { "title": "Updated title", "localized": { "de": { "title": "Neuer Titel" } } } }"""), "u", true);
        var result = _service.Evaluate(Summary(1), AssessmentType.ActiveDirectory);
        await _service.SaveAsync(result);
        _store.Remove("p1");

        var loaded = await _service.LoadAsync(result.Id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(result.RuleSetFingerprint, loaded.RuleSetFingerprint);
        Assert.Equal("p1", Assert.Single(loaded.AppliedUpdates).Id);
        var check = loaded.Categories.SelectMany(c => c.Checks).Single(c => c.RuleId == Rule.Id);
        Assert.True(check.TitleFromUpdate);

        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("Neuer Titel", AssessmentLocalizer.Title(check));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task LegacyResultWithoutProvenance_StillLoads()
    {
        var result = _service.Evaluate(Summary(1), AssessmentType.ActiveDirectory);
        await _service.SaveAsync(result);
        var path = Directory.GetFiles(_root, $"{result.Id}.json", SearchOption.AllDirectories).Single();
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var key in node.Select(p => p.Key).Where(k =>
                     k.Equals("ruleSetFingerprint", StringComparison.OrdinalIgnoreCase) ||
                     k.Equals("appliedUpdates", StringComparison.OrdinalIgnoreCase)).ToList())
            node.Remove(key);
        var schemaKey = node.Select(p => p.Key).First(k => k.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase));
        node[schemaKey] = 1;
        File.WriteAllText(path, node.ToJsonString());

        var loaded = await _service.LoadAsync(result.Id);

        Assert.NotNull(loaded);
        Assert.Null(loaded.RuleSetFingerprint);
        Assert.Empty(loaded.AppliedUpdates ?? []);
        var check = loaded.Categories.SelectMany(c => c.Checks).Single(c => c.RuleId == Rule.Id);
        Assert.False(check.TitleFromUpdate);
        Assert.Equal(AssessmentLocalizer.Title(check.RuleId, check.Title), AssessmentLocalizer.Title(check));
    }
}
