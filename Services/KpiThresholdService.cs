using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Central resolver for configurable KPI thresholds (ActiveRoles:KpiThresholds). When a KPI has
/// no valid configured thresholds the caller-supplied fallback (rule/mapping specific) is used,
/// so behaviour is unchanged until thresholds are configured.
/// </summary>
public class KpiThresholdService
{
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;

    public KpiThresholdService(IOptionsMonitor<ActiveRolesConfig> config)
    {
        _config = config;
    }

    /// <summary>Returns the configured thresholds for a KPI, or null when not configured/invalid.</summary>
    public KpiThresholdConfig? GetConfigured(string kpiKey)
    {
        var map = _config.CurrentValue.KpiThresholds;
        if (map == null || string.IsNullOrEmpty(kpiKey)) return null;
        var match = map.FirstOrDefault(kv => string.Equals(kv.Key, kpiKey, StringComparison.OrdinalIgnoreCase)).Value;
        return match != null && match.Validate() == null ? match : null;
    }

    /// <summary>
    /// Resolves Medium/High as absolute counts. Percent thresholds are converted using the total
    /// KPI from <paramref name="kpiLookup"/>; if the total is unavailable the fallback is used.
    /// </summary>
    public ResolvedKpiThresholds Resolve(
        string kpiKey,
        ResolvedKpiThresholds fallback,
        Func<string, (int Count, string? Error)>? kpiLookup)
    {
        var configured = GetConfigured(kpiKey);
        return configured == null ? fallback : Resolve(configured, kpiKey, fallback, kpiLookup);
    }

    public static ResolvedKpiThresholds Resolve(
        KpiThresholdConfig configured,
        string kpiKey,
        ResolvedKpiThresholds fallback,
        Func<string, (int Count, string? Error)>? kpiLookup)
    {
        if (configured.Mode == KpiThresholdMode.Count)
            return Normalize((int)Math.Ceiling(configured.Medium), (int)Math.Ceiling(configured.High));

        var totalKey = string.IsNullOrWhiteSpace(configured.TotalKpiKey)
            ? KpiThresholdDefaults.TotalKpiKeyFor(kpiKey)
            : configured.TotalKpiKey;
        if (totalKey == null || kpiLookup == null) return fallback;

        var (total, error) = kpiLookup(totalKey);
        if (error != null || total <= 0) return fallback;

        return Normalize(
            (int)Math.Ceiling(total * configured.Medium / 100d),
            (int)Math.Ceiling(total * configured.High / 100d));
    }

    /// <summary>None = 0; Low = above 0 and below Medium; Medium = below High; High otherwise.</summary>
    public static KpiThresholdLevel LevelFor(int count, ResolvedKpiThresholds thresholds)
    {
        if (count <= 0) return KpiThresholdLevel.None;
        if (count >= thresholds.High) return KpiThresholdLevel.High;
        if (count >= thresholds.Medium) return KpiThresholdLevel.Medium;
        return KpiThresholdLevel.Low;
    }

    private static ResolvedKpiThresholds Normalize(int medium, int high)
    {
        medium = Math.Max(1, medium);
        high = Math.Max(medium, high);
        return new ResolvedKpiThresholds(medium, high);
    }
}
