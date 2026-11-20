using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;
using ActiveRolesDashboard.Models.Reporting;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

/// <summary>
/// Builds a full dashboard summary against the fake Active Roles server and checks that every
/// KPI migrated to a definition expression produces the same value as the legacy logic.
/// </summary>
public sealed class DefinitionParityTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();

    public void Dispose() => _server.Dispose();

    private static DefinitionRegistry Registry() =>
        new DefinitionStore(DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly), null).Registry;

    private async Task<DashboardSummary> BuildSummaryAsync()
    {
        var config = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig
        {
            ApiBaseUrl = _server.ApiBaseUrl,
            RstsUrl = _server.RstsUrl,
        });
        var factory = new TestHttpClientFactory(TimeSpan.FromSeconds(10));
        var tokens = new ServiceAccountTokenProvider(
            new RstsAuthService(config, factory),
            new ServiceAccountSecretProtector(new EphemeralDataProtectionProvider()),
            config,
            NullLogger<ServiceAccountTokenProvider>.Instance);
        var registry = Registry();
        var calculator = new ActiveRolesDashboard.Services.Definitions.DefinitionKpiCalculator(registry, new KpiEvaluator(new KpiFunctionRegistry()));
        var collector = new ActiveRolesDashboard.Services.Definitions.DatasetCollector(registry, NullLogger<ActiveRolesDashboard.Services.Definitions.DatasetCollector>.Instance);
        var service = new ActiveRolesService(factory, config, NullLogger<ActiveRolesService>.Instance, tokens, kpiCalculator: calculator, datasetCollector: collector);
        return await service.GetDashboardSummaryAsync(FakeActiveRolesServer.ValidToken);
    }

    [Fact]
    public void Every_migrated_kpi_is_an_expression_in_the_embedded_files()
    {
        var registry = Registry();

        foreach (var (id, (dataset, filter)) in LegacyDefinitionExporter.Migrated)
        {
            var kpi = Assert.IsType<DerivedKpi>(registry.Kpis[id]);
            Assert.Equal(EvaluationType.Expression, kpi.Evaluation.Type);
            Assert.Equal(dataset, kpi.SourceDatasetId);
            Assert.Equal(filter, kpi.Evaluation.Filter);
        }

        foreach (var id in LegacyDefinitionExporter.MigratedSearches)
        {
            var kpi = Assert.IsType<SearchKpi>(registry.Kpis[id]);
            Assert.Equal(EvaluationType.Expression, kpi.Evaluation.Type);
            Assert.Null(kpi.Evaluation.Filter);
        }

        foreach (var (id, filter) in LegacyDefinitionExporter.MigratedSearchFilters)
        {
            var kpi = Assert.IsType<SearchKpi>(registry.Kpis[id]);
            Assert.Equal(EvaluationType.Expression, kpi.Evaluation.Type);
            Assert.Equal(filter, kpi.Evaluation.Filter);
        }

        foreach (var (id, (function, args)) in LegacyDefinitionExporter.MigratedSearchFunctions)
        {
            var kpi = Assert.IsType<SearchKpi>(registry.Kpis[id]);
            Assert.Equal(EvaluationType.Function, kpi.Evaluation.Type);
            Assert.Equal(function, kpi.Evaluation.Function);
            Assert.All(args, a => Assert.Equal(a.Value, kpi.Evaluation.Args[a.Key].GetString()));
        }

        foreach (var (id, (dataset, function)) in LegacyDefinitionExporter.MigratedFunctions)
        {
            var kpi = Assert.IsType<DerivedKpi>(registry.Kpis[id]);
            Assert.Equal(EvaluationType.Function, kpi.Evaluation.Type);
            Assert.Equal(dataset, kpi.SourceDatasetId);
            Assert.Equal(function, kpi.Evaluation.Function);
        }

        foreach (var (id, (sources, aggregate)) in LegacyDefinitionExporter.MigratedComposites)
        {
            var kpi = Assert.IsType<CompositeKpi>(registry.Kpis[id]);
            Assert.Equal(sources, kpi.Sources);
            Assert.Equal(aggregate, kpi.Aggregate);
        }
    }

    [Theory]
    [InlineData("small-domain")]
    [InlineData("exchange-permissions")]
    public async Task Migrated_kpis_are_evaluated_with_drilldowns(string dataSet)
    {
        // Values and rows are frozen by DefinitionGoldenTests (captured against the typed collectors
        // before they were removed); this test checks coverage only.
        _server.WithDataSet(dataSet);
        var summary = await BuildSummaryAsync();
        var expected = LegacyDefinitionExporter.Migrated.Keys.Concat(LegacyDefinitionExporter.MigratedSearches).Concat(LegacyDefinitionExporter.MigratedFunctions.Keys).Concat(LegacyDefinitionExporter.MigratedComposites.Keys).Concat(LegacyDefinitionExporter.MigratedSearchFilters.Keys).Concat(LegacyDefinitionExporter.MigratedSearchFunctions.Keys).ToList();
        Assert.All(expected, id => Assert.True(summary.KpiResults.ContainsKey(id), $"{id} was not evaluated"));

        var errors = LegacyDefinitionExporter.DrilldownColumns.Keys
            .Where(summary.DefinitionDetailTables.ContainsKey)
            .Select(id => (id, summary.GetKpiDetailTable(id)!.Error))
            .Where(e => e.Error is not null)
            .Select(e => $"{e.id}: {e.Error}")
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.NotEmpty(summary.DefinitionDetailTables);
    }

    [Fact]
    public async Task Exchange_permission_drilldowns_resolve_trustees()
    {
        _server.WithDataSet("exchange-permissions");
        var summary = await BuildSummaryAsync();

        string[] Rows(string id) => summary.GetKpiDetailTable(id)!.Rows.Select(r => string.Join("|", r)).ToArray();

        Assert.Equal(["Finance Mailbox|TEST|Frank Delegate|CN=Finance Mailbox,OU=Mail,DC=test,DC=local"], Rows("ExchangeSendAsKpi"));
        Assert.Equal(["Finance Mailbox|TEST|Grace Reader|CN=Finance Mailbox,OU=Mail,DC=test,DC=local"], Rows("ExchangeFullAccessDelegatesKpi"));
        Assert.Equal(["Exec Mailbox|TEST|Dana Assistant, Smith, Eve|CN=Exec Mailbox,OU=Mail,DC=test,DC=local"], Rows("ExchangeSendOnBehalfKpi"));
    }
}
