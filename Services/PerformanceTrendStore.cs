using System.Collections.Concurrent;
using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// In-process, capped store of performance-probe samples used to render the per-dashboard trend
/// charts. Samples are appended by <see cref="PerformanceSamplerHostedService"/> and grouped by
/// (dashboard, target, test type). Each series is bounded by the configured retention window;
/// samples older than the window are pruned on write and on read.
///
/// This is an in-memory, single-instance store (like <see cref="PerUserSummaryCache"/> and
/// <see cref="DashboardCacheHolder"/>): trend history is lost on restart and is not shared across
/// a web farm. A future iteration could add optional persistence behind the same API without
/// changing callers.
/// </summary>
public sealed class PerformanceTrendStore
{
    private sealed class SeriesKey : IEquatable<SeriesKey>
    {
        public DiagnosticsDashboard Dashboard { get; init; }
        public string TargetId { get; init; } = string.Empty;
        public DiagnosticsTestType TestType { get; init; }

        public bool Equals(SeriesKey? other) =>
            other is not null &&
            Dashboard == other.Dashboard &&
            TestType == other.TestType &&
            string.Equals(TargetId, other.TargetId, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => Equals(obj as SeriesKey);

        public override int GetHashCode() =>
            HashCode.Combine(Dashboard, TestType, TargetId.ToLowerInvariant());
    }

    private sealed class SeriesBucket
    {
        public string TargetName { get; set; } = string.Empty;
        public DiagnosticsServerType ServerType { get; set; }
        // Oldest-to-newest. Guarded by its own lock for cheap per-series concurrency.
        public readonly LinkedList<PerformanceSample> Samples = new();
    }

    private readonly ConcurrentDictionary<SeriesKey, SeriesBucket> _series = new();

    /// <summary>
    /// Appends the probes from a completed diagnostics run to the store, pruning each affected
    /// series to <paramref name="retention"/>. Probes without a measurable latency are still
    /// recorded (with their status) so gaps/failures are visible on the chart.
    /// </summary>
    public void Record(DiagnosticsDashboard dashboard, DiagnosticsResult result, TimeSpan retention)
    {
        if (result?.Probes is null || result.Probes.Count == 0)
            return;

        var cutoff = DateTimeOffset.UtcNow - retention;

        foreach (var probe in result.Probes)
        {
            var key = new SeriesKey
            {
                Dashboard = dashboard,
                TargetId = probe.TargetId,
                TestType = probe.TestType
            };

            var bucket = _series.GetOrAdd(key, _ => new SeriesBucket());
            lock (bucket.Samples)
            {
                bucket.TargetName = probe.TargetName;
                bucket.ServerType = probe.ServerType;
                bucket.Samples.AddLast(new PerformanceSample
                {
                    TimestampUtc = result.CompletedUtc == default ? DateTimeOffset.UtcNow : result.CompletedUtc,
                    LatencyMs = probe.LatencyMs,
                    Status = probe.Status
                });
                Prune(bucket.Samples, cutoff);
            }
        }
    }

    /// <summary>
    /// Returns all series for a dashboard whose samples fall within <paramref name="window"/>
    /// (clamped to what is retained). Empty series are omitted.
    /// </summary>
    public IReadOnlyList<PerformanceTrendSeries> GetSeries(DiagnosticsDashboard dashboard, TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        var results = new List<PerformanceTrendSeries>();

        foreach (var kvp in _series)
        {
            if (kvp.Key.Dashboard != dashboard)
                continue;

            var bucket = kvp.Value;
            List<PerformanceSample> samples;
            lock (bucket.Samples)
            {
                Prune(bucket.Samples, DateTimeOffset.UtcNow - window);
                samples = bucket.Samples
                    .Where(s => s.TimestampUtc >= cutoff)
                    .Select(s => new PerformanceSample
                    {
                        TimestampUtc = s.TimestampUtc,
                        LatencyMs = s.LatencyMs,
                        Status = s.Status
                    })
                    .ToList();
            }

            if (samples.Count == 0)
                continue;

            results.Add(new PerformanceTrendSeries
            {
                TargetId = kvp.Key.TargetId,
                TargetName = bucket.TargetName,
                ServerType = bucket.ServerType,
                TestType = kvp.Key.TestType,
                Samples = samples
            });
        }

        return results
            .OrderBy(s => s.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.TestType.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Prune(LinkedList<PerformanceSample> samples, DateTimeOffset cutoff)
    {
        while (samples.First is { } head && head.Value.TimestampUtc < cutoff)
            samples.RemoveFirst();
    }
}
