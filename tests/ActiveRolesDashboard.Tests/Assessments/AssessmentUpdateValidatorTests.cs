using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.AssessmentUpdates;

namespace ActiveRolesDashboard.Tests.Assessments;

[Trait("Stage", "Assessments")]
public class AssessmentUpdateValidatorTests
{
    private static readonly Version AppVersion = new(2, 0, 0);
    private static readonly IReadOnlyList<AssessmentRule> Rules = AssessmentRuleLibrary.All;
    private static readonly IReadOnlySet<string> Kpis = KpiInfo.All.Select(k => k.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static AssessmentRule First => Rules[0];

    private static string Pack(string operations, string extra = "") =>
        AssessmentUpdateHasher.Seal($$"""
        { "schemaVersion": 1, "id": "test.pack", "version": "1.0.0", "title": "Test" {{extra}}, "operations": [ {{operations}} ] }
        """);

    private static AssessmentUpdateValidationResult Validate(string json) =>
        AssessmentUpdateValidator.Validate(json, Rules, Kpis, AppVersion);

    [Fact]
    public void Hash_IgnoresFormattingAndKeyOrder()
    {
        var a = """{"id":"x","version":"1.0","sha256":"abc","operations":[]}""";
        var b = "{\n  \"operations\": [],\n  \"version\": \"1.0\",\n  \"id\": \"x\"\n}";
        Assert.Equal(AssessmentUpdateHasher.Compute(a), AssessmentUpdateHasher.Compute(b));
    }

    [Fact]
    public void SealedPack_IsValid()
    {
        var result = Validate(Pack($$"""{ "op": "modify", "ruleId": "{{First.Id}}", "rule": { "severity": "critical" } }"""));
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void TamperedPack_FailsIntegrityCheck()
    {
        var json = Pack($$"""{ "op": "modify", "ruleId": "{{First.Id}}", "rule": { "failThreshold": 5, "warnThreshold": 1 } }""")
            .Replace("\"failThreshold\": 5", "\"failThreshold\": 500");
        var result = Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHA-256"));
    }

    [Fact]
    public void MissingHash_IsRejected()
    {
        var json = $$"""{ "schemaVersion": 1, "id": "p", "version": "1.0", "title": "T", "operations": [ { "op": "retire", "ruleId": "{{First.Id}}" } ] }""";
        Assert.Contains(Validate(json).Errors, e => e.Contains("sha256"));
    }

    [Fact]
    public void UnknownRule_And_UnknownKpi_AreRejected()
    {
        Assert.False(Validate(Pack("""{ "op": "modify", "ruleId": "NoSuchRule", "rule": { "title": "x" } }""")).IsValid);
        Assert.False(Validate(Pack("""{ "op": "add", "ruleId": "NEW-1", "rule": { "title": "x", "kpiKey": "NoSuchKpi", "types": ["activeDirectory"], "recommendation": "r" } }""")).IsValid);
    }

    [Fact]
    public void Add_WithKnownKpi_IsValid_ButDuplicateIdIsNot()
    {
        var kpi = First.KpiKey;
        Assert.True(Validate(Pack($$"""{ "op": "add", "ruleId": "NEW-1", "rule": { "title": "x", "kpiKey": "{{kpi}}", "types": ["activeDirectory"], "recommendation": "r" } }""")).IsValid);
        Assert.False(Validate(Pack($$"""{ "op": "add", "ruleId": "{{First.Id}}", "rule": { "title": "x", "kpiKey": "{{kpi}}", "types": ["activeDirectory"], "recommendation": "r" } }""")).IsValid);
    }

    [Fact]
    public void InconsistentThresholds_AreRejected()
    {
        var result = Validate(Pack($$"""{ "op": "modify", "ruleId": "{{First.Id}}", "rule": { "comparison": "atLeast", "warnThreshold": 10, "failThreshold": 2 } }"""));
        Assert.Contains(result.Errors, e => e.Contains("warnThreshold"));
    }

    [Fact]
    public void IncompatibleAppVersion_IsRejected()
    {
        var result = Validate(Pack($$"""{ "op": "retire", "ruleId": "{{First.Id}}" }""", ", \"minAppVersion\": \"9.0\""));
        Assert.Contains(result.Errors, e => e.Contains("9.0"));
    }

    [Fact]
    public void DuplicateRuleInPack_IsRejected()
    {
        var result = Validate(Pack($$"""{ "op": "retire", "ruleId": "{{First.Id}}" }, { "op": "modify", "ruleId": "{{First.Id}}", "rule": { "title": "x" } }"""));
        Assert.Contains(result.Errors, e => e.Contains("more than once"));
    }

    [Fact]
    public void EveryBuiltInRule_AcceptsTitleOnlyModify()
    {
        foreach (var rule in Rules)
        {
            var result = Validate(Pack($$"""{ "op": "modify", "ruleId": "{{rule.Id}}", "rule": { "title": "Changed" } }"""));
            Assert.True(result.IsValid, $"{rule.Id}: {string.Join("; ", result.Errors)}");
        }
    }

    [Fact]
    public void Merger_OnlyChangesSpecifiedFields()
    {
        var merged = AssessmentRuleMerger.Apply(First, new AssessmentRulePatch { FailThreshold = First.FailThreshold + 3 });
        Assert.Equal(First.Title, merged.Title);
        Assert.Equal(First.KpiKey, merged.KpiKey);
        Assert.Equal(First.FailThreshold + 3, merged.FailThreshold);
    }
}
