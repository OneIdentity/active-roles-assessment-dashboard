using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public sealed class SummaryDictionaryTests
{
    private static JsonElement User(int uac) =>
        JsonDocument.Parse($"{{\"distinguishedName\":\"CN={uac}\",\"userAccountControl\":{uac}}}").RootElement;

    private static DefinitionRegistry Registry(params KpiDefinition[] kpis) => new(new DefinitionSet
    {
        Categories = [new CategoryDefinition { Id = "Cat", DashboardId = "Main" }],
        Kpis = kpis
    });

    private static DerivedKpi Disabled(string id = "Disabled", string dataset = "ADUserAccounts") => new()
    {
        Id = id,
        CategoryId = "Cat",
        SourceDatasetId = dataset,
        Evaluation = new EvaluationDefinition { Filter = "hasFlag(userAccountControl, 0x2)" }
    };

    private static EvaluationContext Context() => new() { Now = DateTimeOffset.UtcNow };

    [Fact]
    public void Datasets_view_reflects_typed_summaries_and_collected_datasets()
    {
        var summary = new DashboardSummary
        {
            ADUserAccounts = new ADUserAccountsSummary { Items = [User(512)] },
            ADGroups = new ADGroupsSummary { Error = "No data" },
        };
        summary.CollectedDatasets["Extra"] = new DatasetResult([User(2)], null);

        var datasets = summary.Datasets;

        Assert.Single(datasets["adUserAccounts"].Items);
        Assert.Equal("No data", datasets["ADGroups"].Error);
        Assert.Empty(datasets["Computers"].Items);
        Assert.Single(datasets["Extra"].Items);
    }

    [Fact]
    public void Calculator_evaluates_expression_kpis_and_skips_legacy_and_disabled_ones()
    {
        var legacy = new DerivedKpi
        {
            Id = "Legacy", CategoryId = "Cat",
            Evaluation = new EvaluationDefinition { Type = EvaluationType.Function, Function = KpiFunctionRegistry.Legacy }
        };
        var settings = new KpiSettings { DisabledUsersEnabled = false };
        var summary = new DashboardSummary { ADUserAccounts = new ADUserAccountsSummary { Items = [User(2), User(512), User(514)] } };

        new DefinitionKpiCalculator(Registry(Disabled(), Disabled("DisabledUsers"), legacy), new KpiEvaluator(new KpiFunctionRegistry()))
            .Populate(summary, settings, Context());

        Assert.Equal(["Disabled"], summary.KpiResults.Keys);
        Assert.Equal(new KpiResult(2, null), summary.KpiResults["Disabled"]);
    }

    [Fact]
    public void Calculator_propagates_dataset_errors_and_expression_failures()
    {
        var broken = new DerivedKpi
        {
            Id = "Broken", CategoryId = "Cat", SourceDatasetId = "ADUserAccounts",
            Evaluation = new EvaluationDefinition { Filter = "noSuchFunction(x)" }
        };
        var summary = new DashboardSummary
        {
            ADUserAccounts = new ADUserAccountsSummary { Items = [User(2)] },
            Computers = new ComputersSummary { Error = "No data (boom)" },
        };

        new DefinitionKpiCalculator(Registry(broken, Disabled("FromComputers", "Computers")), new KpiEvaluator(new KpiFunctionRegistry()))
            .Populate(summary, new KpiSettings(), Context());

        Assert.NotNull(summary.KpiResults["Broken"].Error);
        Assert.Equal("No data (boom)", summary.KpiResults["FromComputers"].Error);
    }

    [Fact]
    public void Required_dataset_ids_exclude_typed_datasets_and_disabled_kpis()
    {
        var calculator = new DefinitionKpiCalculator(
            Registry(Disabled(), Disabled("FromExtra", "Extra"), Disabled("DisabledUsers", "Other")),
            new KpiEvaluator(new KpiFunctionRegistry()));

        Assert.Equal(["Extra"], calculator.RequiredDatasetIds(new KpiSettings { DisabledUsersEnabled = false }));
    }

    [Fact]
    public void Collected_datasets_survive_session_serialization()
    {
        var summary = new DashboardSummary();
        summary.CollectedDatasets["Extra"] = new DatasetResult([User(2)], null);

        var copy = JsonSerializer.Deserialize<DashboardSummary>(JsonSerializer.Serialize(summary.ToSessionCacheSafe()))!;

        Assert.Single(copy.Datasets["extra"].Items);
    }

    [Fact]
    public void Kpi_results_survive_session_serialization_and_are_marked_stale_by_segment_filters()
    {
        var summary = new DashboardSummary();
        summary.KpiResults["Disabled"] = new KpiResult(3, null);

        var copy = JsonSerializer.Deserialize<DashboardSummary>(JsonSerializer.Serialize(summary.ToSessionCacheSafe()))!;
        Assert.Equal(3, copy.KpiResults["disabled"].Value);

        copy.ApplySegmentFilter(new SegmentFilterState());
        Assert.Equal(DashboardSummary.StaleKpiResultError, copy.KpiResults["Disabled"].Error);
        Assert.Equal(DashboardSummary.StaleKpiResultError, copy.GetKpiResult("Disabled").Error);
    }
}
