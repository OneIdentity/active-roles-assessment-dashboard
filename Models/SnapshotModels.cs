namespace ActiveRolesDashboard.Models;

/// <summary>
/// A point-in-time capture of KPI summary counts across all dashboards,
/// organized by dashboard -> category -> KPI. Serialized to a JSON file.
/// </summary>
public class Snapshot
{
    public SnapshotHeader Header { get; set; } = new();
    public List<SnapshotDashboard> Dashboards { get; set; } = new();
}

/// <summary>
/// Descriptive metadata stored at the top of every snapshot file.
/// The header is also used on its own for listing snapshots without
/// deserializing the full data body.
/// </summary>
public class SnapshotHeader
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Label { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? Environment { get; set; }

    /// <summary>Total number of KPI data points captured (informational).</summary>
    public int KpiCount { get; set; }
}

public class SnapshotDashboard
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SnapshotCategory> Categories { get; set; } = new();
}

public class SnapshotCategory
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SnapshotKpi> Kpis { get; set; } = new();
}

public class SnapshotKpi
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Count { get; set; }
    public string? Error { get; set; }

    /// <summary>True when an increase in this KPI represents a worse security posture.</summary>
    public bool IsRiskKpi { get; set; }
}

// ---------------------------------------------------------------------------
// Comparison result types
// ---------------------------------------------------------------------------

public enum DeltaDirection
{
    NoChange,
    Increase,
    Decrease,
    Added,      // KPI present in the "to" side only
    Removed     // KPI present in the "from" side only
}

/// <summary>How a change should be interpreted for colouring: good, bad, or neutral.</summary>
public enum DeltaSentiment
{
    Neutral,
    Good,
    Bad
}

/// <summary>The full result of comparing a baseline snapshot against another snapshot or the current live values.</summary>
public class SnapshotComparison
{
    public SnapshotHeader From { get; set; } = new();
    public SnapshotHeader To { get; set; } = new();

    /// <summary>True when the "To" side represents live/current values rather than a saved snapshot.</summary>
    public bool ToIsCurrent { get; set; }

    public List<SnapshotComparisonDashboard> Dashboards { get; set; } = new();

    public int IncreaseCount { get; set; }
    public int DecreaseCount { get; set; }
    public int NoChangeCount { get; set; }
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }
}

public class SnapshotComparisonDashboard
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SnapshotComparisonCategory> Categories { get; set; } = new();
}

public class SnapshotComparisonCategory
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SnapshotComparisonRow> Rows { get; set; } = new();
}

public class SnapshotComparisonRow
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? FromCount { get; set; }
    public int? ToCount { get; set; }
    public int Change { get; set; }
    public DeltaDirection Direction { get; set; }
    public DeltaSentiment Sentiment { get; set; }
    public bool IsRiskKpi { get; set; }
}

// ---------------------------------------------------------------------------
// Trend types
// ---------------------------------------------------------------------------

/// <summary>
/// A time-series view over saved snapshots, organized by dashboard -> category
/// -> KPI, where each KPI carries one count per captured snapshot timestamp.
/// The <see cref="Labels"/> list is shared across every KPI series (same order).
/// </summary>
public class SnapshotTrend
{
    /// <summary>Snapshot timestamps (oldest first) shared by all KPI series.</summary>
    public List<string> Labels { get; set; } = new();

    public List<SnapshotTrendDashboard> Dashboards { get; set; } = new();
}

public class SnapshotTrendDashboard
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SnapshotTrendCategory> Categories { get; set; } = new();
}

public class SnapshotTrendCategory
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SnapshotTrendKpi> Kpis { get; set; } = new();
}

public class SnapshotTrendKpi
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsRiskKpi { get; set; }

    /// <summary>One value per snapshot label; null where the KPI was absent in that snapshot.</summary>
    public List<int?> Values { get; set; } = new();
}
