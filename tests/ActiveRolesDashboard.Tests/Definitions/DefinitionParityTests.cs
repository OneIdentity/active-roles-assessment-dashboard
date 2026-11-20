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

        var expected = LegacyDefinitionExporter.Migrated.Keys.Concat(LegacyDefinitionExporter.MigratedSearches).Concat(LegacyDefinitionExporter.MigratedFunctions.Keys).Concat(LegacyDefinitionExporter.MigratedComposites.Keys).Concat(LegacyDefinitionExporter.MigratedSearchFilters.Keys).Concat(LegacyDefinitionExporter.MigratedSearchFunctions.Keys).ToList();
        Assert.All(expected, id => Assert.True(summary.KpiResults.ContainsKey(id), $"{id} was not evaluated"));

        var results = checker.LastResults;
        var checkedIds = results.Select(r => r.KpiId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(expected, id => Assert.True(checkedIds.Contains(id), $"{id} was not parity-checked"));
        Assert.All(results, r => Assert.True(r.Matches, $"{r.KpiId}: legacy {r.Legacy}, definition {r.Definition} {r.Error}"));

        // Drilldowns: the definition table must match the typed table (same columns, same rows).
        // Known improvements over the typed tables are listed with the column they fill in.
        var mismatches = new List<string>();
        foreach (var id in LegacyDefinitionExporter.DrilldownColumns.Keys.Where(summary.DefinitionDetailTables.ContainsKey))
        {
            var generic = summary.GetKpiDetailTable(id)!;
            var legacy = summary.GetLegacyKpiDetailTable(id)!;
            if (generic.Error is not null) { mismatches.Add($"{id}: {generic.Error}"); continue; }
            if (!legacy.Columns.SequenceEqual(generic.Columns)) { mismatches.Add($"{id}: columns [{string.Join(",", legacy.Columns)}] vs [{string.Join(",", generic.Columns)}]"); continue; }
            var filled = DrilldownImprovements.TryGetValue(id, out var column) ? legacy.Columns.IndexOf(column) : -1;
            IEnumerable<string> Keys(ReportTable t, bool blankFilled) => t.Rows
                .Select(r => string.Join("|", r.Select((v, i) => blankFilled && i == filled ? string.Empty : v)))
                .Order(StringComparer.Ordinal);
            if (!Keys(legacy, false).SequenceEqual(Keys(generic, filled >= 0)))
                mismatches.Add($"{id}: legacy [{string.Join("; ", Keys(legacy, false))}] definition [{string.Join("; ", Keys(generic, false))}]");
        }
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        Assert.NotEmpty(summary.DefinitionDetailTables);
    }

    /// <summary>Drilldowns where the definition table fills a column the typed table leaves blank.</summary>
    private static readonly Dictionary<string, string> DrilldownImprovements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NoManagerUser"] = "Domain",
    };
}
