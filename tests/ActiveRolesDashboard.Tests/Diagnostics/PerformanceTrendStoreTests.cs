using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Diagnostics;

/// <summary>
/// In-memory performance trend history: grouping, retention/window pruning, dashboard isolation,
/// ordering, failed probes, and concurrent writers.
/// </summary>
[Trait("Stage", "Diagnostics")]
public class PerformanceTrendStoreTests
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private static DiagnosticsProbeResult Probe(string id, string name = "", DiagnosticsTestType type = DiagnosticsTestType.Ping,
        long? ms = 10, DiagnosticsStatus status = DiagnosticsStatus.Ok) =>
        new() { TargetId = id, TargetName = name == "" ? id : name, TestType = type, LatencyMs = ms, Status = status, ServerType = DiagnosticsServerType.DomainController };

    private static DiagnosticsResult Run(DateTimeOffset completed, params DiagnosticsProbeResult[] probes) =>
        new() { CompletedUtc = completed, Probes = probes.ToList() };

    [Fact]
    public void Empty_store_returns_no_series()
    {
        Assert.Empty(new PerformanceTrendStore().GetSeries(DiagnosticsDashboard.ActiveDirectory, Day));
    }

    [Fact]
    public void Null_or_empty_results_are_ignored()
    {
        var store = new PerformanceTrendStore();
        store.Record(DiagnosticsDashboard.ActiveDirectory, null!, Day);
        store.Record(DiagnosticsDashboard.ActiveDirectory, new DiagnosticsResult { Probes = null! }, Day);
        store.Record(DiagnosticsDashboard.ActiveDirectory, new DiagnosticsResult(), Day);

        Assert.Empty(store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day));
    }

    [Fact]
    public void Samples_are_grouped_by_target_and_test_type()
    {
        var store = new PerformanceTrendStore();
        var now = DateTimeOffset.UtcNow;
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(now.AddMinutes(-2), Probe("dc1"), Probe("dc1", type: DiagnosticsTestType.LdapBind)), Day);
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(now.AddMinutes(-1), Probe("DC1", ms: 20)), Day);

        var series = store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day);

        Assert.Equal(2, series.Count);
        var ping = series.Single(s => s.TestType == DiagnosticsTestType.Ping);
        Assert.Equal([10L, 20L], ping.Samples.Select(s => s.LatencyMs));
        Assert.Equal("DC1", ping.TargetName); // latest name wins
    }

    [Fact]
    public void Dashboards_are_isolated()
    {
        var store = new PerformanceTrendStore();
        store.Record(DiagnosticsDashboard.ActiveRoles, Run(DateTimeOffset.UtcNow, Probe("ars1")), Day);

        Assert.Empty(store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day));
        Assert.Single(store.GetSeries(DiagnosticsDashboard.ActiveRoles, Day));
    }

    [Fact]
    public void Retention_prunes_old_samples_on_write()
    {
        var store = new PerformanceTrendStore();
        var now = DateTimeOffset.UtcNow;
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(now.AddHours(-3), Probe("dc1", ms: 1)), TimeSpan.FromHours(10));
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(now, Probe("dc1", ms: 2)), TimeSpan.FromHours(1));

        var samples = store.GetSeries(DiagnosticsDashboard.ActiveDirectory, TimeSpan.FromDays(7)).Single().Samples;

        Assert.Equal([2L], samples.Select(s => s.LatencyMs));
    }

    [Fact]
    public void Window_limits_returned_samples_and_drops_empty_series()
    {
        var store = new PerformanceTrendStore();
        var now = DateTimeOffset.UtcNow;
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(now.AddHours(-5), Probe("old")), Day);
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(now.AddMinutes(-5), Probe("new")), Day);

        var series = store.GetSeries(DiagnosticsDashboard.ActiveDirectory, TimeSpan.FromHours(1));

        Assert.Equal(["new"], series.Select(s => s.TargetId));
    }

    [Fact]
    public void Missing_completion_time_uses_now()
    {
        var store = new PerformanceTrendStore();
        var before = DateTimeOffset.UtcNow;
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(default, Probe("dc1")), Day);

        var ts = store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day).Single().Samples.Single().TimestampUtc;

        Assert.InRange(ts, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Failed_probes_without_latency_are_kept()
    {
        var store = new PerformanceTrendStore();
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(DateTimeOffset.UtcNow, Probe("dc1", ms: null, status: DiagnosticsStatus.Fail)), Day);

        var sample = store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day).Single().Samples.Single();

        Assert.Null(sample.LatencyMs);
        Assert.Equal(DiagnosticsStatus.Fail, sample.Status);
    }

    [Fact]
    public void Series_are_ordered_by_target_name_then_test_type()
    {
        var store = new PerformanceTrendStore();
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(DateTimeOffset.UtcNow,
            Probe("2", "beta", DiagnosticsTestType.Ping),
            Probe("1", "Alpha", DiagnosticsTestType.Ping),
            Probe("1", "Alpha", DiagnosticsTestType.LdapBind)), Day);

        var series = store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day);

        Assert.Equal(["Alpha/LdapBind", "Alpha/Ping", "beta/Ping"], series.Select(s => $"{s.TargetName}/{s.TestType}"));
    }

    [Fact]
    public void Returned_samples_are_copies()
    {
        var store = new PerformanceTrendStore();
        store.Record(DiagnosticsDashboard.ActiveDirectory, Run(DateTimeOffset.UtcNow, Probe("dc1", ms: 5)), Day);

        store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day).Single().Samples[0].LatencyMs = 999;

        Assert.Equal(5, store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day).Single().Samples[0].LatencyMs);
    }

    [Fact]
    public async Task Concurrent_writers_and_readers_are_safe()
    {
        var store = new PerformanceTrendStore();
        var now = DateTimeOffset.UtcNow;

        var writers = Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
                store.Record(DiagnosticsDashboard.ActiveDirectory, Run(now, Probe($"dc{i % 4}", ms: i)), Day);
        }));
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
                store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day);
        }));
        await Task.WhenAll(writers.Concat(readers));

        var series = store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Day);
        Assert.Equal(4, series.Count);
        Assert.Equal(2000, series.Sum(s => s.Samples.Count));
    }
}
