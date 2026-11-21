namespace ActiveRolesDashboard.Models;

/// <summary>Shared chart colour palettes.</summary>
public static class ChartColors
{
    // CSS color names used for source-split slices, in order. The first two keep the
    // established AD (blue) / Entra (teal) mapping; the remainder give distinct colours
    // when a chart is split across multiple tenants.
    public static readonly IReadOnlyList<string> SourceSplit = ["blue", "teal", "purple", "amber", "pink", "slate", "indigo", "orange"];
}
