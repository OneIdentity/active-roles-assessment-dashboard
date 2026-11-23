using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.AssessmentUpdates;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Assessments;

[Trait("Stage", "Assessments")]
public class AssessmentUpdateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ard-tests", Guid.NewGuid().ToString("N"));
    private readonly AssessmentUpdateStore _store;
    private static AssessmentRule First => AssessmentRuleLibrary.All[0];

    public AssessmentUpdateStoreTests() => _store = new AssessmentUpdateStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Pack(string id, string version, string operations) =>
        AssessmentUpdateHasher.Seal($$"""
        { "schemaVersion": 1, "id": "{{id}}", "version": "{{version}}", "title": "T", "operations": [ {{operations}} ] }
        """);

    private static string Retire(string id = "p1", string version = "1.0") =>
        Pack(id, version, $$"""{ "op": "retire", "ruleId": "{{First.Id}}" }""");

    [Fact]
    public void NoPacks_MatchesBuiltInRules()
    {
        var set = _store.BuildRuleSet();
        Assert.Equal(AssessmentRuleLibrary.All.Select(r => r.Id), set.Rules.Select(r => r.Id));
        Assert.Empty(set.Packs);
    }

    [Fact]
    public void Preview_DoesNotInstall()
    {
        Assert.True(_store.Import(Retire(), "u", commit: false).IsValid);
        Assert.Empty(_store.BuildRuleSet().Packs);
    }

    [Fact]
    public void Import_Disable_Enable_Remove_RoundTrips()
    {
        var baseline = _store.BuildRuleSet().Fingerprint;
        var changes = 0;
        _store.Changed += () => changes++;

        Assert.True(_store.Import(Retire(), "u", commit: true).IsValid);
        var applied = _store.BuildRuleSet();
        Assert.DoesNotContain(applied.Rules, r => r.Id == First.Id);
        Assert.NotEqual(baseline, applied.Fingerprint);
        Assert.Equal(AssessmentUpdatePackStatus.Active, applied.Packs.Single().Status);

        Assert.True(_store.SetEnabled("p1", false, "u"));
        var disabled = _store.BuildRuleSet();
        Assert.Equal(baseline, disabled.Fingerprint);
        Assert.Equal(AssessmentUpdatePackStatus.Disabled, disabled.Packs.Single().Status);

        Assert.True(_store.SetEnabled("p1", true, "u"));
        Assert.NotEqual(baseline, _store.BuildRuleSet().Fingerprint);

        Assert.True(_store.Remove("p1"));
        Assert.Equal(baseline, _store.BuildRuleSet().Fingerprint);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void RevertAll_RestoresBuiltIn()
    {
        var baseline = _store.BuildRuleSet().Fingerprint;
        _store.Import(Retire(), "u", true);
        _store.Import(Pack("p2", "1.0", """{ "op": "add", "ruleId": "NEW-1", "rule": { "title": "n", "kpiKey": "%K%", "types": ["activeDirectory"], "recommendation": "r" } }""".Replace("%K%", First.KpiKey)), "u", true);
        Assert.Equal(2, _store.BuildRuleSet().ActivePacks.Count());

        _store.RevertAll("u");
        var set = _store.BuildRuleSet();
        Assert.Equal(baseline, set.Fingerprint);
        Assert.All(set.Packs, p => Assert.Equal(AssessmentUpdatePackStatus.Disabled, p.Status));
    }

    [Fact]
    public void TamperedFileOnDisk_IsMarkedInvalid_AndNotApplied()
    {
        var baseline = _store.BuildRuleSet().Fingerprint;
        _store.Import(Retire(), "u", true);
        var file = Directory.GetFiles(_root, "p1_*.json").Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"title\": \"T\"", "\"title\": \"X\""));

        var set = _store.BuildRuleSet();
        Assert.Equal(AssessmentUpdatePackStatus.Invalid, set.Packs.Single().Status);
        Assert.Contains(set.Packs.Single().Errors, e => e.Contains("SHA-256"));
        Assert.Equal(baseline, set.Fingerprint);
    }

    [Fact]
    public void Reimport_RequiresHigherVersion()
    {
        _store.Import(Retire(version: "1.0"), "u", true);
        Assert.False(_store.Import(Retire(version: "1.0"), "u", true).IsValid);
        Assert.True(_store.Import(Retire(version: "1.1"), "u", true).IsValid);
        Assert.Equal("1.1", _store.BuildRuleSet().Packs.Single().Pack!.Version);
    }

    [Fact]
    public void LaterPack_BuildsOnEarlierPack()
    {
        var kpi = First.KpiKey;
        _store.Import(Pack("p1", "1.0", $$"""{ "op": "add", "ruleId": "NEW-1", "rule": { "title": "n", "kpiKey": "{{kpi}}", "types": ["activeDirectory"], "recommendation": "r" } }"""), "u", true);
        Assert.True(_store.Import(Pack("p2", "1.0", """{ "op": "modify", "ruleId": "NEW-1", "rule": { "severity": "critical" } }"""), "u", true).IsValid);
        Assert.Equal(AssessmentSeverity.Critical, _store.BuildRuleSet().Rules.Single(r => r.Id == "NEW-1").Severity);

        // Disabling the base pack invalidates the dependent one rather than partially applying it.
        _store.SetEnabled("p1", false, "u");
        var set = _store.BuildRuleSet();
        Assert.Equal(AssessmentUpdatePackStatus.Invalid, set.Packs.Single(p => p.State.Id == "p2").Status);
        Assert.DoesNotContain(set.Rules, r => r.Id == "NEW-1");
    }

    [Fact]
    public void PartialRetire_RemovesOnlyListedTypes()
    {
        var rule = AssessmentRuleLibrary.All.First(r => r.Types.Count > 1);
        var drop = rule.Types[0];
        _store.Import(Pack("p1", "1.0", $$"""{ "op": "retire", "ruleId": "{{rule.Id}}", "types": ["{{System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(drop.ToString())}}"] }"""), "u", true);
        var updated = _store.BuildRuleSet().Rules.Single(r => r.Id == rule.Id);
        Assert.DoesNotContain(drop, updated.Types);
        Assert.Equal(rule.Types.Count - 1, updated.Types.Count);
    }
}
