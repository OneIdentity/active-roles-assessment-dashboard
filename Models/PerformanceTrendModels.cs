using System.Text.Json.Serialization;

namespace ActiveRolesDashboard.Models;

/// <summary>
/// A single captured performance sample: one diagnostics probe (one test against one target)
/// at a point in time. These are appended to the in-memory <c>PerformanceTrendStore</c> by the
/// background sampler and projected into per-target series for the trend charts.
/// </summary>
public class PerformanceSample
{
    /// <summary>UTC timestamp at which the probe completed.</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>Round-trip latency in milliseconds, when measurable. Null for failed/skipped probes.</summary>
    public long? LatencyMs { get; set; }

    /// <summary>Outcome classification of the probe.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DiagnosticsStatus Status { get; set; }
}

/// <summary>
/// A time-ordered series of samples for a single (dashboard, target, test type) combination,
/// returned to the client for charting. One line on the chart corresponds to one series.
/// </summary>
public class PerformanceTrendSeries
{
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DiagnosticsServerType ServerType { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DiagnosticsTestType TestType { get; set; }

    /// <summary>Samples ordered oldest-to-newest, already trimmed to the requested window.</summary>
    public List<PerformanceSample> Samples { get; set; } = new();
}

/// <summary>
/// The trend payload for one dashboard: all series retained (and within the requested window),
/// plus metadata describing the retained range so the client can render a period dropdown.
/// </summary>
public class PerformanceTrendResponse
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DiagnosticsDashboard Dashboard { get; set; }

    /// <summary>Configured snapshot interval (effective, minimum-clamped) in minutes.</summary>
    public int IntervalMinutes { get; set; }

    /// <summary>Configured retention window in hours (the widest selectable period).</summary>
    public int RetentionHours { get; set; }

    /// <summary>UTC timestamp of the oldest retained sample across all series, if any.</summary>
    public DateTimeOffset? OldestSampleUtc { get; set; }

    /// <summary>UTC timestamp of the newest retained sample across all series, if any.</summary>
    public DateTimeOffset? NewestSampleUtc { get; set; }

    public List<PerformanceTrendSeries> Series { get; set; } = new();
}
