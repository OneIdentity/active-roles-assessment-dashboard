using System.Text.Json;
using ActiveRolesDashboard.Models.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public class DefinitionModelTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private const string Json = """
    {
      "schemaVersion": 1,
      "categories": [
        { "id": "ADGroupsCategory", "dashboardId": "ActiveDirectory", "kind": "Standard", "kpiSortOrder": "CustomThenAtoZ",
          "charts": [ { "id": "GroupTypes", "type": "Bar", "series": [ { "kpiId": "EmptyGroups", "color": "amber" } ] } ] }
      ],
      "datasets": [
        { "id": "ADGroups", "dataSource": "ActiveDirectory",
          "retrieval": { "method": "Rest", "target": "Search", "base": "{DefaultADDN}", "filter": "(objectClass=group)" },
          "attributes": [ "name", "member" ] }
      ],
      "kpis": [
        { "kind": "Search", "id": "ADGroups", "categoryId": "Overview", "dataSource": "ActiveDirectory", "datasetId": "ADGroups" },
        { "kind": "Derived", "id": "EmptyGroups", "categoryId": "ADGroupsCategory", "dataSource": "ActiveDirectory",
          "sourceDatasetId": "ADGroups", "isRiskKpi": true, "severity": "Medium",
          "evaluation": { "type": "Expression", "filter": "isEmpty(member)" },
          "charts": [ { "id": "EmptyByDomain", "type": "Doughnut", "series": [ { "datasetId": "ADGroups", "groupBy": "edsaDomainNetbiosName" } ] } ] },
        { "kind": "Objects", "id": "ManagedObjects", "categoryId": "Licensing", "dataSource": "ActiveRoles", "datasetId": "ManagedStats",
          "retrieval": { "target": "Objects", "objects": [ "CN=Managed Object Statistics,{DefaultARConfigDN}" ] },
          "evaluation": { "type": "Function", "function": "ManagedObjectCount", "args": { "includeHistory": true } } },
        { "kind": "Composite", "id": "TotalUsers", "categoryId": "Overview", "dataSource": "ActiveDirectory", "sources": [ "AdUsers", "EntraUsers" ] }
      ]
    }
    """;

    [Fact]
    public void Definition_set_deserializes_all_kpi_kinds()
    {
        var set = JsonSerializer.Deserialize<DefinitionSet>(Json, Options)!;

        Assert.Single(set.Categories);
        Assert.Equal(CategoryKind.Standard, set.Categories[0].Kind);
        Assert.Equal("Cat_ADGroupsCategory", set.Categories[0].EffectiveLocalizationKey);
        Assert.Single(set.Categories[0].Charts);

        Assert.Equal(RetrievalTarget.Search, set.Datasets[0].Retrieval.Target);

        Assert.IsType<SearchKpi>(set.Kpis[0]);
        var derived = Assert.IsType<DerivedKpi>(set.Kpis[1]);
        Assert.Equal("isEmpty(member)", derived.Evaluation.Filter);
        Assert.True(derived.HasCharts);
        Assert.False(derived.RetrievesFromSource);
        Assert.Equal("Kpi_EmptyGroups", derived.EffectiveLocalizationKey);

        var obj = Assert.IsType<ObjectKpi>(set.Kpis[2]);
        Assert.Equal(EvaluationType.Function, obj.Evaluation.Type);
        Assert.True(obj.Evaluation.Args["includeHistory"].GetBoolean());
        Assert.True(obj.RetrievesFromSource);

        var composite = Assert.IsType<CompositeKpi>(set.Kpis[3]);
        Assert.Equal(CompositeAggregate.Sum, composite.Aggregate);
    }

    [Fact]
    public void Definition_set_round_trips()
    {
        var set = JsonSerializer.Deserialize<DefinitionSet>(Json, Options)!;
        var again = JsonSerializer.Deserialize<DefinitionSet>(JsonSerializer.Serialize(set, Options), Options)!;

        Assert.Equal(set.Kpis.Select(k => k.GetType()), again.Kpis.Select(k => k.GetType()));
        Assert.Equal("ManagedObjectCount", ((ObjectKpi)again.Kpis[2]).Evaluation.Function);
    }
}
