using ActiveRolesDashboard.Models.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public sealed class DefinitionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ar-defs-" + Guid.NewGuid().ToString("N"));

    public DefinitionStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static IReadOnlyList<DefinitionSet> Embedded() => DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly);

    [Fact]
    public void Embedded_definitions_are_valid_and_complete()
    {
        var sets = Embedded();
        var store = new DefinitionStore(sets, null);

        Assert.Empty(DefinitionValidator.Validate(store.Registry.Set));
        Assert.Equal(Models.KpiInfo.All.Count, store.Registry.Kpis.Count);
        Assert.Equal(Models.CategoryInfo.All.Count, store.Registry.Categories.Count);
        Assert.False(store.OverridesApplied);
        Assert.False(store.HasPendingChanges);
    }

    [Fact]
    public void Valid_override_replaces_item_with_same_id()
    {
        var embedded = Embedded();
        var kpi = embedded.SelectMany(s => s.Kpis).First();
        File.WriteAllText(Path.Combine(_dir, "custom.definitions.json"),
            $$"""{ "kpis": [ { "kind": "Composite", "id": "{{kpi.Id}}", "categoryId": "{{kpi.CategoryId}}", "dataSource": "ActiveRoles", "enabled": false, "sources": ["{{kpi.Id}}"] } ] }""");

        var store = new DefinitionStore(embedded, _dir);

        Assert.True(store.OverridesApplied);
        Assert.Empty(store.OverrideErrors);
        Assert.IsType<CompositeKpi>(store.Registry.Kpis[kpi.Id]);
        Assert.False(store.Registry.Kpis[kpi.Id].Enabled);
        Assert.Equal(embedded.Sum(s => s.Kpis.Count), store.Registry.Kpis.Count);
    }

    [Fact]
    public void Invalid_override_falls_back_to_embedded()
    {
        File.WriteAllText(Path.Combine(_dir, "bad.definitions.json"),
            """{ "kpis": [ { "kind": "Composite", "id": "NewKpi", "categoryId": "NoSuchCategory", "dataSource": "ActiveRoles", "sources": ["Missing"] } ] }""");

        var store = new DefinitionStore(Embedded(), _dir);

        Assert.False(store.OverridesApplied);
        Assert.Contains(store.OverrideErrors, e => e.Contains("NoSuchCategory"));
        Assert.False(store.Registry.Kpis.ContainsKey("NewKpi"));
    }

    [Fact]
    public void Malformed_override_falls_back_to_embedded()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.definitions.json"), "{ not json");

        var store = new DefinitionStore(Embedded(), _dir);

        Assert.False(store.OverridesApplied);
        Assert.Single(store.OverrideErrors);
    }

    [Fact]
    public void Override_changes_after_load_are_pending()
    {
        var store = new DefinitionStore(Embedded(), _dir);
        Assert.False(store.HasPendingChanges);

        File.WriteAllText(Path.Combine(_dir, "new.definitions.json"), "{}");

        Assert.True(store.HasPendingChanges);
    }

    [Fact]
    public void Invalid_embedded_definitions_throw()
    {
        var bad = new DefinitionSet { Kpis = [new CompositeKpi { Id = "X", CategoryId = "Missing" }] };

        Assert.Throws<InvalidOperationException>(() => new DefinitionStore([bad], null));
    }

    [Fact]
    public void Duplicate_ids_across_embedded_files_are_rejected()
    {
        var cat = new CategoryDefinition { Id = "C", DashboardId = "D" };

        Assert.Throws<InvalidOperationException>(() => new DefinitionStore(
            [new DefinitionSet { Categories = [cat] }, new DefinitionSet { Categories = [cat] }], null));
    }
}
