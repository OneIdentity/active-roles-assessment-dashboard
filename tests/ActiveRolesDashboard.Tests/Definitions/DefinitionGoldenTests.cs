using System.Runtime.CompilerServices;
using System.Text.Json;
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
/// Freezes the KPI values and drilldown rows produced from the fake Active Roles server fixtures.
/// The snapshots were captured while the typed collectors were still the parity oracle, so they keep
/// guarding the definition pipeline after the typed collectors are removed.
/// Set the environment variable UPDATE_GOLDEN=1 to regenerate the snapshot files.
/// </summary>
public sealed class DefinitionGoldenTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();

    public void Dispose() => _server.Dispose();

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
        var registry = new DefinitionStore(DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly), null).Registry;
        var calculator = new ActiveRolesDashboard.Services.Definitions.DefinitionKpiCalculator(registry, new KpiEvaluator(new KpiFunctionRegistry()));
        var collector = new ActiveRolesDashboard.Services.Definitions.DatasetCollector(registry, NullLogger<ActiveRolesDashboard.Services.Definitions.DatasetCollector>.Instance);
        var service = new ActiveRolesService(factory, config, NullLogger<ActiveRolesService>.Instance, tokens, kpiCalculator: calculator, datasetCollector: collector);
        return await service.GetDashboardSummaryAsync(FakeActiveRolesServer.ValidToken);
    }

    private static string GoldenPath(string dataSet, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", $"{dataSet}.json");

    private static SortedDictionary<string, object> Snapshot(DashboardSummary summary)
    {
        var snapshot = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (id, result) in summary.KpiResults)
            snapshot[$"kpi:{id}"] = result.Error is null ? result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"error:{result.Error}";
        foreach (var id in summary.DefinitionDetailTables.Keys)
        {
            var table = summary.GetKpiDetailTable(id)!;
            snapshot[$"rows:{id}"] = table.Error is not null
                ? [$"error:{table.Error}"]
                : table.Rows.Select(r => string.Join("|", r)).Order(StringComparer.Ordinal).ToList();
        }
        return snapshot;
    }

    [Theory]
    [InlineData("small-domain")]
    [InlineData("exchange-permissions")]
    public async Task Kpi_values_and_drilldowns_match_golden_snapshot(string dataSet)
    {
        _server.WithDataSet(dataSet);
        var summary = await BuildSummaryAsync();
        var actual = JsonSerializer.Serialize(Snapshot(summary), new JsonSerializerOptions { WriteIndented = true });
        var path = GoldenPath(dataSet);

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual + Environment.NewLine);
            return;
        }

        Assert.True(File.Exists(path), $"Golden snapshot missing: {path}. Run with UPDATE_GOLDEN=1.");
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, JsonElement>>(File.ReadAllText(path))!;
        var current = JsonSerializer.Deserialize<SortedDictionary<string, JsonElement>>(actual)!;
        var diffs = expected.Keys.Union(current.Keys)
            .Where(k => !expected.TryGetValue(k, out var e) || !current.TryGetValue(k, out var c) || e.GetRawText() != c.GetRawText())
            .Select(k => $"{k}: expected {(expected.TryGetValue(k, out var e) ? e.GetRawText() : "<none>")} actual {(current.TryGetValue(k, out var c) ? c.GetRawText() : "<none>")}")
            .ToList();
        Assert.True(diffs.Count == 0, string.Join(Environment.NewLine, diffs));
    }

    [Theory]
    [InlineData("small-domain")]
    [InlineData("exchange-permissions")]
    public async Task Group_tree_targets_match_typed_targets(string dataSet)
    {
        _server.WithDataSet(dataSet);
        var summary = await BuildSummaryAsync();
        var keys = new[]
        {
            "AccountOperators", "Administrators", "BackupOperators", "DomainAdmins", "ServerOperators",
            "EnterpriseAdmins", "SchemaAdmins", "ActiveRolesAdmins", "DistributionGroups", "DomainLocalGroups",
            "GlobalGroups", "MailEnabledSecurityGroups", "SecurityGroups", "UniversalGroups", "NoGroupOwner",
        };

        var diffs = new List<string>();
        foreach (var key in keys)
        {
            var (typedDn, typedRows) = summary.GetKpiGroupTreeTargets(key);
            var table = summary.GetKpiDetailTable(key);
            var genericDn = table?.Error is null ? table?.GroupTreeDn : null;
            if (!string.Equals(typedDn, genericDn, StringComparison.OrdinalIgnoreCase))
                diffs.Add($"{key}: GroupTreeDn typed '{typedDn}' generic '{genericDn}'");

            static string Targets(IEnumerable<string?>? dns) =>
                string.Join(";", (dns ?? []).OfType<string>().Order(StringComparer.OrdinalIgnoreCase));
            var typed = Targets(typedRows);
            var generic = table?.Error is null ? Targets(table?.RowGroupTreeDns) : string.Empty;
            if (!string.Equals(typed, generic, StringComparison.OrdinalIgnoreCase))
                diffs.Add($"{key}: RowGroupTreeDns typed '{typed}' generic '{generic}'");
        }

        Assert.True(diffs.Count == 0, string.Join(Environment.NewLine, diffs));
    }
}
