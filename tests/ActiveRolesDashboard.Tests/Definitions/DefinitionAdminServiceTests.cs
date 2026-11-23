using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Pages;
using ActiveRolesDashboard.Services.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public sealed class DefinitionAdminServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ar-defadmin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private DefinitionAdminService Create() =>
        new(new DefinitionStore(DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly), _dir));

    private static string CustomCategory(string id = "Custom_Cat") =>
        $$"""{ "id": "{{id}}", "dashboardId": "{{Models.DashboardInfo.ActiveDirectory.Key}}", "fallbackText": "Custom" }""";

    private static string CompositeKpi(string id, string categoryId, string source) =>
        $$"""{ "kind": "Composite", "id": "{{id}}", "categoryId": "{{categoryId}}", "dataSource": "ActiveRoles", "sources": ["{{source}}"] }""";

    [Fact]
    public void Save_creates_custom_file_with_category_and_kpi()
    {
        var admin = Create();
        var existingKpi = admin.KpiIds()[0];

        Assert.Empty(admin.Save(DefinitionKind.Category, CustomCategory(), null));
        Assert.Empty(admin.Save(DefinitionKind.Kpi, CompositeKpi("Custom_Kpi", "Custom_Cat", existingKpi), null));

        var set = DefinitionStore.ReadFile(Path.Combine(_dir, DefinitionAdminService.CustomFileName));
        Assert.Single(set.Categories);
        Assert.IsType<CompositeKpi>(Assert.Single(set.Kpis));

        var entries = admin.List();
        Assert.Contains(entries, e => e.Id == "Custom_Kpi" && e.Origin == DefinitionOrigin.Custom && e.Editable);
        Assert.Contains(entries, e => e.Id == "Custom_Cat" && e.Kind == DefinitionKind.Category);
    }

    [Fact]
    public void Save_rejects_invalid_reference_without_writing()
    {
        var admin = Create();

        var errors = admin.Save(DefinitionKind.Kpi, CompositeKpi("Custom_Kpi", "NoSuchCategory", "Missing"), null);

        Assert.NotEmpty(errors);
        Assert.False(File.Exists(Path.Combine(_dir, DefinitionAdminService.CustomFileName)));
    }

    [Fact]
    public void Remove_rejects_category_still_in_use()
    {
        var admin = Create();
        admin.Save(DefinitionKind.Category, CustomCategory(), null);
        admin.Save(DefinitionKind.Kpi, CompositeKpi("Custom_Kpi", "Custom_Cat", admin.KpiIds()[0]), null);

        Assert.NotEmpty(admin.Remove(DefinitionKind.Category, "Custom_Cat"));
        Assert.Empty(admin.Remove(DefinitionKind.Kpi, "Custom_Kpi"));
        Assert.Empty(admin.Remove(DefinitionKind.Category, "Custom_Cat"));
        Assert.DoesNotContain(admin.List(), e => e.Origin != DefinitionOrigin.System);
    }

    [Fact]
    public void Dependents_lists_every_referencing_kpi()
    {
        var admin = Create();
        admin.Save(DefinitionKind.Category, CustomCategory(), null);
        admin.Save(DefinitionKind.Kpi, CompositeKpi("Custom_A", "Custom_Cat", admin.KpiIds()[0]), null);
        admin.Save(DefinitionKind.Kpi, CompositeKpi("Custom_B", "Custom_Cat", "Custom_A"), null);

        Assert.Equal(["KPI 'Custom_A'", "KPI 'Custom_B'"], admin.Dependents(DefinitionKind.Category, "Custom_Cat").Order());
        Assert.Equal(["KPI 'Custom_B'"], admin.Dependents(DefinitionKind.Kpi, "Custom_A"));
        Assert.Empty(admin.Dependents(DefinitionKind.Kpi, "Custom_B"));

        var error = Assert.Single(admin.Remove(DefinitionKind.Category, "Custom_Cat"));
        Assert.Contains("Custom_A", error);
        Assert.Contains("Custom_B", error);
        Assert.DoesNotContain("unknown", error);
    }

    [Fact]
    public void Cloned_system_kpi_with_same_id_is_an_override()
    {
        var admin = Create();
        var id = admin.List().First(e => e.Kind == DefinitionKind.Kpi).Id;
        var json = admin.GetJson(DefinitionKind.Kpi, id, system: true)!;

        Assert.Empty(admin.Save(DefinitionKind.Kpi, json, null));

        Assert.Contains(admin.List(), e => e.Id == id && e.Origin == DefinitionOrigin.Override);
        Assert.NotNull(admin.GetJson(DefinitionKind.Kpi, id, system: false));
    }

    [Fact]
    public void Rename_replaces_item_and_duplicate_add_is_rejected()
    {
        var admin = Create();
        admin.Save(DefinitionKind.Category, CustomCategory("Cat_A"), null);

        Assert.NotEmpty(admin.Save(DefinitionKind.Category, CustomCategory("Cat_A"), null));
        Assert.Empty(admin.Save(DefinitionKind.Category, CustomCategory("Cat_B"), "Cat_A"));

        var custom = admin.List().Where(e => e.Origin != DefinitionOrigin.System).Select(e => e.Id).ToList();
        Assert.Equal(["Cat_B"], custom);
    }

    [Fact]
    public void Fields_are_applied_to_json_and_preserve_other_properties()
    {
        var doc = JsonNode.Parse("""{ "kind": "Search", "id": "K", "charts": [ { "id": "c" } ], "retrieval": { "base": "x" } }""")!.AsObject();
        var fields = DefinitionFields.From(doc);
        fields.Id = "K2";
        fields.DatasetId = "Users";
        fields.EvaluationFilter = "enabled == true";
        fields.Attributes = "name, mail";

        fields.ApplyTo(doc, DefinitionKind.Kpi);

        Assert.Equal("K2", (string?)doc["id"]);
        Assert.Equal("Users", (string?)doc["datasetId"]);
        Assert.Null(doc["retrieval"]);
        Assert.Equal("enabled == true", (string?)doc["evaluation"]!["filter"]);
        Assert.Equal(2, doc["attributes"]!.AsArray().Count);
        Assert.NotNull(doc["charts"]);
    }
}
