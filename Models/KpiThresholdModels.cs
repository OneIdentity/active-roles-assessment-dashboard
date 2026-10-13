namespace ActiveRolesDashboard.Models;

/// <summary>How the configurable thresholds of a KPI are expressed.</summary>
public enum KpiThresholdMode
{
    /// <summary>Medium/High are absolute object counts.</summary>
    Count,
    /// <summary>Medium/High are percentages (0-100) of the KPI's total population.</summary>
    Percent
}

/// <summary>Severity band derived from a KPI count and its thresholds.</summary>
public enum KpiThresholdLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// Configurable thresholds for a single KPI (bound from ActiveRoles:KpiThresholds:&lt;KpiKey&gt;).
/// None is fixed at 0 and Low is calculated (above 0 and below Medium); only Medium and High
/// are configurable, and both share the same <see cref="Mode"/>.
/// </summary>
public class KpiThresholdConfig
{
    public KpiThresholdMode Mode { get; set; } = KpiThresholdMode.Count;
    public double Medium { get; set; }
    public double High { get; set; }

    /// <summary>
    /// Optional KPI key whose count is used as the total for <see cref="KpiThresholdMode.Percent"/>.
    /// When empty, <see cref="KpiThresholdDefaults.TotalKpiKeyFor"/> is used.
    /// </summary>
    public string? TotalKpiKey { get; set; }

    /// <summary>Returns a validation error, or null when the configuration is valid.</summary>
    public string? Validate()
    {
        if (Medium <= 0 || High <= 0)
            return "Medium and High thresholds must be greater than 0.";
        if (High < Medium)
            return "High threshold must be greater than or equal to Medium.";
        if (Mode == KpiThresholdMode.Percent && High > 100)
            return "Percentage thresholds must not exceed 100.";
        return null;
    }
}

/// <summary>Medium/High thresholds resolved to absolute counts.</summary>
public readonly record struct ResolvedKpiThresholds(int Medium, int High);

/// <summary>Built-in defaults used by the threshold resolver.</summary>
public static class KpiThresholdDefaults
{
    /// <summary>
    /// Default total-population KPI used for percentage thresholds, by KPI key prefix/family.
    /// Returns null when no sensible total is known (percentage mode then falls back).
    /// </summary>
    public static string? TotalKpiKeyFor(string kpiKey)
    {
        if (string.IsNullOrEmpty(kpiKey)) return null;
        if (kpiKey.StartsWith("Exchange", StringComparison.OrdinalIgnoreCase)) return null;
        if (kpiKey.StartsWith("Entra", StringComparison.OrdinalIgnoreCase))
            return kpiKey.Contains("Group", StringComparison.OrdinalIgnoreCase) ? "EntraGroups" : null;
        if (kpiKey.Contains("Computer", StringComparison.OrdinalIgnoreCase)
            || kpiKey.Contains("ServerOs", StringComparison.OrdinalIgnoreCase)
            || kpiKey.Contains("ClientOs", StringComparison.OrdinalIgnoreCase))
            return "Computers";
        if (kpiKey.Contains("Group", StringComparison.OrdinalIgnoreCase))
            return "ADGroups";
        return "ADUserAccounts";
    }
}
