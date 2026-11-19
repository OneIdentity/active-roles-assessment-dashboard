using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;
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
    }

    [Theory]
    [InlineData("small-domain")]
    public async Task Migrated_kpis_match_legacy_values(string dataSet)
    {
        _server.WithDataSet(dataSet);
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
        var checker = new DefinitionParityChecker(NullLogger<DefinitionParityChecker>.Instance);
        var registry = Registry();
        var calculator = new ActiveRolesDashboard.Services.Definitions.DefinitionKpiCalculator(registry, new KpiEvaluator(new KpiFunctionRegistry()));
        var collector = new ActiveRolesDashboard.Services.Definitions.DatasetCollector(registry, NullLogger<ActiveRolesDashboard.Services.Definitions.DatasetCollector>.Instance);
        var service = new ActiveRolesService(factory, config, NullLogger<ActiveRolesService>.Instance, tokens, checker, kpiCalculator: calculator, datasetCollector: collector);

        var summary = await service.GetDashboardSummaryAsync(FakeActiveRolesServer.ValidToken);

        var expected = LegacyDefinitionExporter.Migrated.Keys.Concat(LegacyDefinitionExporter.MigratedSearches).ToList();
        Assert.All(expected, id => Assert.True(summary.KpiResults.ContainsKey(id), $"{id} was not evaluated"));

        var results = checker.LastResults;
        var checkedIds = results.Select(r => r.KpiId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(expected, id => Assert.True(checkedIds.Contains(id), $"{id} was not parity-checked"));
        Assert.All(results, r => Assert.True(r.Matches, $"{r.KpiId}: legacy {r.Legacy}, definition {r.Definition} {r.Error}"));
    }
}
