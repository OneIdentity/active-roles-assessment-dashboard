using System.Text.RegularExpressions;

namespace ActiveRolesDashboard.Models;

/// <summary>
/// Exclusion patterns (ActiveRoles:KpiExclusions) for known false positives. Patterns use
/// <c>*</c>/<c>?</c> wildcards and match case-insensitively against an object's Name or
/// Distinguished Name. Excluded objects remain visible in drill-downs but are not counted.
/// </summary>
public class KpiExclusionConfig
{
    /// <summary>Patterns applied to every KPI.</summary>
    public List<string> Global { get; set; } = new();

    /// <summary>Additional patterns per KPI key. Must not duplicate a Global pattern.</summary>
    public Dictionary<string, List<string>> PerKpi { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Trims, removes blanks and case-insensitive duplicates, preserving order.</summary>
    public static List<string> Normalize(IEnumerable<string>? patterns) =>
        (patterns ?? Enumerable.Empty<string>())
            .Select(p => p?.Trim() ?? string.Empty)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Case-insensitive <c>*</c>/<c>?</c> wildcard matcher with a compiled-pattern cache.</summary>
public static class WildcardPattern
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool IsMatch(string pattern, string? value)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = Cache.GetOrAdd(pattern.Trim(), p => new Regex(
            "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(250)));
        return regex.IsMatch(value);
    }
}
