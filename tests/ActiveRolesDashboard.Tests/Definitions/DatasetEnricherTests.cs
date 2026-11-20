using System.Text.Json;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Services.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public class DatasetEnricherTests
{
    private sealed class FakeLookup : IDirectoryLookup
    {
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int GuidCalls;
        public Task<bool> GuidExistsAsync(string guid) { Interlocked.Increment(ref GuidCalls); return Task.FromResult(Existing.Contains(guid)); }
        public Task<string> ResolveSidAsync(string sid) => Task.FromResult(sid == "S-1-5-21-1" ? "CORP\\alice" : "");
        public Task<bool> IsUserSidAsync(string sid) => Task.FromResult(sid == "S-1-5-21-1");
        public Task<bool> IsRootScopeAsync(string guid) => Task.FromResult(guid == "root");
        public Task<string> ResolveGuidNameAsync(string guid) => Task.FromResult(guid == "root" ? "corp.local" : "Unknown");
        public Task<int> CountDescendantsAsync(string dn) => dn == "bad" ? throw new InvalidOperationException() : Task.FromResult(7);
        public Task<IReadOnlyList<string>?> MailboxGranteesAsync(string objectGuid, string? ownerSid, string right) =>
            Task.FromResult<IReadOnlyList<string>?>(objectGuid == "none" ? null : right == "SendAs" ? ["S-1-5-21-1", "S-1-5-21-9", "S-1-5-21-1"] : []);
    }

    private static DatasetDefinition Dataset(string name, params (string Key, string Value)[] args) => new()
    {
        Id = "d",
        Retrieval = new RetrievalDefinition(),
        Enrichments = [new EnrichmentDefinition { Name = name, Args = args.ToDictionary(a => a.Key, a => a.Value) }]
    };

    private static List<JsonElement> Items(params object[] items) => items.Select(i => JsonSerializer.SerializeToElement(i)).ToList();

    [Fact]
    public async Task UnresolvedGuids_lists_missing_guids_and_caches_lookups()
    {
        var g1 = Guid.NewGuid().ToString();
        var g2 = Guid.NewGuid().ToString();
        var lookup = new FakeLookup();
        lookup.Existing.Add(g1);

        var result = await DatasetEnricher.EnrichAsync(Dataset(EnrichmentNames.UnresolvedGuids, ("attribute", "cond")),
            Items(new { cond = $"<a>{{{g1}}}</a><b>{g2}</b>" }, new { cond = g2 }, new { other = 1 }), lookup);

        Assert.Equal([g2], result[0].GetProperty("unresolvedGuids").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, result[1].GetProperty("unresolvedGuids").GetArrayLength());
        Assert.Equal(0, result[2].GetProperty("unresolvedGuids").GetArrayLength());
        Assert.Equal(2, lookup.GuidCalls);
    }

    [Fact]
    public async Task Trustee_and_rootScope_add_resolved_attributes()
    {
        var ds = new DatasetDefinition
        {
            Id = "d", Retrieval = new RetrievalDefinition(),
            Enrichments = [new() { Name = EnrichmentNames.Trustee }, new() { Name = EnrichmentNames.RootScope }]
        };
        var result = await DatasetEnricher.EnrichAsync(ds,
            Items(new { edsaTrusteeSID = "S-1-5-21-1", edsaSecObjectGUID = "root" }, new { edsaSecObjectGUID = "x" }), new FakeLookup());

        Assert.Equal("CORP\\alice", result[0].GetProperty("trustee").GetString());
        Assert.True(result[0].GetProperty("trusteeIsUser").GetBoolean());
        Assert.True(result[0].GetProperty("isRootScope").GetBoolean());
        Assert.False(result[1].GetProperty("trusteeIsUser").GetBoolean());
        Assert.False(result[1].GetProperty("isRootScope").GetBoolean());
    }

    [Fact]
    public async Task ObjectName_resolves_guid_into_named_attribute()
    {
        var result = await DatasetEnricher.EnrichAsync(Dataset(EnrichmentNames.ObjectName, ("attribute", "edsaSecObjectGUID"), ("as", "directoryObject")),
            Items(new { edsaSecObjectGUID = "root" }, new { edsaSecObjectGUID = "x" }, new { other = 1 }), new FakeLookup());

        Assert.Equal("corp.local", result[0].GetProperty("directoryObject").GetString());
        Assert.Equal("Unknown", result[1].GetProperty("directoryObject").GetString());
        Assert.Equal("", result[2].GetProperty("directoryObject").GetString());
    }

    [Fact]
    public async Task DescendantCount_records_count_or_note()
    {
        var result = await DatasetEnricher.EnrichAsync(Dataset(EnrichmentNames.DescendantCount),
            Items(new { distinguishedName = "ok" }, new { distinguishedName = "bad" }), new FakeLookup());

        Assert.Equal(7, result[0].GetProperty("objectCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, result[1].GetProperty("objectCount").ValueKind);
        Assert.Contains("unavailable", result[1].GetProperty("note").GetString());
    }

    [Fact]
    public async Task MailboxPermissions_resolves_distinct_trustees()
    {
        var result = await DatasetEnricher.EnrichAsync(Dataset(EnrichmentNames.MailboxPermissions, ("right", "SendAs")),
            Items(new { objectGUID = "m1" }, new { objectGUID = "none" }), new FakeLookup());

        Assert.Equal(["CORP\\alice", "S-1-5-21-9"], result[0].GetProperty("trustees").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("CORP\\alice, S-1-5-21-9", result[0].GetProperty("trustee").GetString());
        Assert.Equal(0, result[1].GetProperty("trustees").GetArrayLength());
    }

    [Fact]
    public void Validator_rejects_unknown_enrichment()
    {
        var set = new DefinitionSet { Datasets = [Dataset("nope")] };
        Assert.Contains(DefinitionValidator.Validate(set), e => e.Contains("unknown enrichment 'nope'"));
    }
}
