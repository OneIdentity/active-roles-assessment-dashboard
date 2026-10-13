using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Reporting;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Applies configured exclusion patterns (ActiveRoles:KpiExclusions) to KPI counts and detail
/// tables. Counts are only adjusted when the KPI's detail table is complete (row count equals
/// the raw count) and exposes a Name and/or Distinguished Name column; otherwise the raw count
/// is returned unchanged. With no patterns configured behaviour is identical to the raw summary.
/// </summary>
public class KpiExclusionService
{
    public const string ExcludedColumn = "Excluded";
    private const string NameColumn = "Name";
    private const string DnColumn = "Distinguished Name";

    private readonly IOptionsMonitor<ActiveRolesConfig> _config;

    public KpiExclusionService(IOptionsMonitor<ActiveRolesConfig> config)
    {
        _config = config;
    }

    /// <summary>Effective patterns for a KPI (Global plus per-KPI, de-duplicated).</summary>
    public IReadOnlyList<string> GetPatterns(string kpiKey)
    {
        var cfg = _config.CurrentValue.KpiExclusions;
        if (cfg == null) return [];
        IEnumerable<string> patterns = cfg.Global ?? [];
        if (!string.IsNullOrEmpty(kpiKey) && cfg.PerKpi != null)
        {
            var perKpi = cfg.PerKpi.FirstOrDefault(kv => string.Equals(kv.Key, kpiKey, StringComparison.OrdinalIgnoreCase)).Value;
            if (perKpi != null) patterns = patterns.Concat(perKpi);
        }
        return KpiExclusionConfig.Normalize(patterns);
    }

    public bool IsExcluded(string kpiKey, string? name, string? dn) =>
        IsExcluded(GetPatterns(kpiKey), name, dn);

    private static bool IsExcluded(IReadOnlyList<string> patterns, string? name, string? dn) =>
        patterns.Any(p => WildcardPattern.IsMatch(p, name) || WildcardPattern.IsMatch(p, dn));

    /// <summary>
    /// Appends an "Excluded" column (True/False) to the table when any row matches, and returns
    /// the number of excluded rows. Tables without Name/DN columns are left untouched.
    /// </summary>
    public int ApplyToTable(string kpiKey, ReportTable? table)
    {
        if (table == null || table.Error != null || table.Rows.Count == 0) return 0;
        var patterns = GetPatterns(kpiKey);
        if (patterns.Count == 0) return 0;

        var nameIdx = table.Columns.IndexOf(NameColumn);
        var dnIdx = table.Columns.IndexOf(DnColumn);
        if (nameIdx < 0 && dnIdx < 0) return 0;

        var flags = table.Rows.Select(r => IsExcluded(patterns,
            nameIdx >= 0 && nameIdx < r.Count ? r[nameIdx] : null,
            dnIdx >= 0 && dnIdx < r.Count ? r[dnIdx] : null)).ToList();
        var excluded = flags.Count(f => f);
        if (excluded == 0) return 0;

        table.Columns = [.. table.Columns, ExcludedColumn];
        table.Rows = table.Rows.Select((r, i) => (IReadOnlyList<string>)[.. r, flags[i].ToString()]).ToList();
        return excluded;
    }

    /// <summary>KPI count with excluded objects removed (raw result when not applicable).</summary>
    public (int Count, string? Error) GetAdjustedResult(DashboardSummary summary, string kpiKey)
    {
        var raw = summary.GetKpiResult(kpiKey);
        if (raw.Error != null || raw.Count <= 0 || GetPatterns(kpiKey).Count == 0) return raw;

        var table = summary.GetKpiDetailTable(kpiKey);
        if (table == null || table.Error != null || table.Rows.Count != raw.Count) return raw;

        var excluded = ApplyToTable(kpiKey, table);
        return (Math.Max(0, raw.Count - excluded), raw.Error);
    }

    /// <summary>Convenience lookup delegate for consumers that take a KPI lookup function.</summary>
    public Func<string, (int Count, string? Error)> LookupFor(DashboardSummary summary) =>
        key => GetAdjustedResult(summary, key);
}
