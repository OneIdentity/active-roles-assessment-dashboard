using System.Globalization;
using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>Values flowing through an expression: null, bool, double, string or a list of strings (multi-valued attributes).</summary>
internal static class ExprValue
{
    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0,
        IReadOnlyList<string> l => l.Count > 0,
        _ => true
    };

    public static double? Number(object? v) => v switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        IReadOnlyList<string> { Count: 1 } l => Number(l[0]),
        _ => null
    };

    public static string Text(object? v) => v switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        string s => s,
        IReadOnlyList<string> l => string.Join(";", l),
        _ => v.ToString() ?? string.Empty
    };

    public static IReadOnlyList<string> List(object? v) => v switch
    {
        null => [],
        IReadOnlyList<string> l => l,
        _ => [Text(v)]
    };

    public static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? string.Empty : x.GetRawText()).ToList(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText()
    };
}

/// <summary>Built-in expression functions. The set is fixed so definitions cannot run arbitrary code.</summary>
internal static class BuiltInFunctions
{
    private delegate object? Fn(IReadOnlyList<object?> args, EvaluationContext context);

    private static readonly Dictionary<string, (int Min, int Max, Fn Body)> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["exists"] = (1, 1, (a, _) => a[0] is not null),
        ["isEmpty"] = (1, 1, (a, _) => !ExprValue.Truthy(a[0])),
        ["count"] = (1, 1, (a, _) => (double)ExprValue.List(a[0]).Count),
        ["lower"] = (1, 1, (a, _) => ExprValue.Text(a[0]).ToLowerInvariant()),
        ["contains"] = (2, 2, (a, _) => a[0] is IReadOnlyList<string> l
            ? l.Contains(ExprValue.Text(a[1]), StringComparer.OrdinalIgnoreCase)
            : ExprValue.Text(a[0]).Contains(ExprValue.Text(a[1]), StringComparison.OrdinalIgnoreCase)),
        ["startsWith"] = (2, 2, (a, _) => ExprValue.Text(a[0]).StartsWith(ExprValue.Text(a[1]), StringComparison.OrdinalIgnoreCase)),
        ["endsWith"] = (2, 2, (a, _) => ExprValue.Text(a[0]).EndsWith(ExprValue.Text(a[1]), StringComparison.OrdinalIgnoreCase)),
        ["anyStartsWith"] = (2, 2, (a, _) => ExprValue.List(a[0]).Any(v => v.StartsWith(ExprValue.Text(a[1]), StringComparison.OrdinalIgnoreCase))),
        ["containsAny"] = (2, 2, (a, _) =>
        {
            var candidates = ExprValue.List(a[1]).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return ExprValue.List(a[0]).Any(v => !string.IsNullOrWhiteSpace(v) && candidates.Contains(v.Trim()));
        }),
        ["in"] = (2, 32, (a, _) => a.Skip(1).Any(x => string.Equals(ExprValue.Text(a[0]), ExprValue.Text(x), StringComparison.OrdinalIgnoreCase))),
        ["hasFlag"] = (2, 2, (a, _) => ExprValue.Number(a[0]) is { } v && ExprValue.Number(a[1]) is { } f && ((long)v & (long)f) == (long)f && (long)f != 0),
        ["daysSince"] = (1, 1, (a, c) => ParseDate(a[0]) is { } d ? (c.Now - d).TotalDays : null),
        ["daysUntil"] = (1, 1, (a, c) => ParseDate(a[0]) is { } d ? (d - c.Now).TotalDays : null),
        ["coalesce"] = (1, 16, (a, _) => a.FirstOrDefault(ExprValue.Truthy)),
        ["isBlank"] = (1, 1, (a, _) => ExprValue.List(a[0]).All(string.IsNullOrWhiteSpace)),
        ["aceHasDeny"] = (1, 1, (a, _) => ExprValue.List(a[0]).Any(AceListHasDeny)),
        ["xmlCount"] = (2, 2, (a, _) => (double)XmlElementCount(ExprValue.Text(a[0]), ExprValue.Text(a[1]))),
        ["friendlyOsName"] = (2, 2, (a, _) => Services.ActiveRolesService.GetFriendlyOSName(ExprValue.Text(a[0]), ExprValue.Text(a[1]))),
        ["homeTenant"] = (1, 1, (a, _) => EntraTotalsSummary.HomeTenantOfUpn(ExprValue.Text(a[0]))),
        ["iif"] = (3, 3, (a, _) => ExprValue.Truthy(a[0]) ? a[1] : a[2]),
        ["abs"] = (1, 1, (a, _) => ExprValue.Number(a[0]) is { } n ? Math.Abs(n) : null),
        ["round"] = (1, 1, (a, _) => ExprValue.Number(a[0]) is { } n ? Math.Round(n) : null),
        // cnList(dns): distinct common names of DN values, comma-separated.
        ["cnList"] = (1, 1, (a, _) => string.Join(", ", ExprValue.List(a[0]).Select(Services.ActiveRolesService.GetCnFromDn)
            .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase))),
        ["arBool"] = (1, 1, (a, _) => Services.ActiveRolesService.ParseArBool(ExprValue.Text(a[0]))),
        ["arTime"] = (1, 1, (a, _) => Services.ActiveRolesService.ParseArTime(ExprValue.Text(a[0]))?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty),
        ["replicationRole"] = (1, 1, (a, _) => Services.ActiveRolesService.ParseReplicationRole(ExprValue.Text(a[0])).ToString()),
        ["scriptType"] = (1, 1, (a, _) => Services.ActiveRolesService.MapScriptType(ExprValue.Text(a[0]))),
        ["apeRuleCount"] = (1, 1, (a, _) => (double)Services.ActiveRolesService.CountApeRules(ExprValue.Text(a[0]))),
        ["membershipRuleCount"] = (1, 1, (a, _) => (double)Services.ActiveRolesService.CountMembershipRules(ExprValue.Text(a[0]))),
        // missingNames('attrA', valueA, 'attrB', valueB, ...): comma-separated names whose value is blank.
        ["missingNames"] = (2, 12, (a, _) => string.Join(", ", Enumerable.Range(0, a.Count / 2)
            .Where(i => string.IsNullOrWhiteSpace(ExprValue.Text(a[i * 2 + 1])))
            .Select(i => ExprValue.Text(a[i * 2])))),
        ["formatDate"] = (2, 2, (a, _) => ParseDate(a[0]) is { } d ? d.UtcDateTime.ToString(ExprValue.Text(a[1]), CultureInfo.InvariantCulture) : null),
        ["daysUntilDate"] = (1, 1, (a, c) => ParseDate(a[0]) is { } d ? (double)(d.UtcDateTime.Date - c.Now.UtcDateTime.Date).Days : null),
    };

    /// <summary>True when an Active Roles ACE list (<c>[type;...]</c> entries) contains a Deny (D/OD) entry.</summary>
    private static bool AceListHasDeny(string aceList)
    {
        var start = -1;
        for (var i = 0; i < aceList.Length; i++)
        {
            if (aceList[i] == '[') start = i + 1;
            else if (aceList[i] == ']' && start >= 0)
            {
                var type = aceList[start..i].Split(';')[0].Trim();
                if (type.Equals("D", StringComparison.OrdinalIgnoreCase) || type.Equals("OD", StringComparison.OrdinalIgnoreCase))
                    return true;
                start = -1;
            }
        }
        return false;
    }

    /// <summary>Number of elements with the given name in an XML attribute value; 0 when empty or malformed.</summary>
    private static int XmlElementCount(string xml, string element)
    {
        if (string.IsNullOrWhiteSpace(xml)) return 0;
        try { return System.Xml.Linq.XDocument.Parse(xml).Descendants(element).Count(); }
        catch (System.Xml.XmlException) { return 0; }
    }

    public static bool IsKnown(string name, int argCount) =>
        Functions.TryGetValue(name, out var f) && argCount >= f.Min && argCount <= f.Max;

    public static object? Invoke(string name, IReadOnlyList<object?> args, EvaluationContext context) =>
        Functions[name].Body(args, context);

    /// <summary>Accepts AD FILETIME integers, ISO 8601 and LDAP generalized time; FILETIME 0 / max mean "never".</summary>
    internal static DateTimeOffset? ParseDate(object? value)
    {
        // FILETIME attributes arrive as JSON numbers from the REST API; their text form uses
        // exponent notation, so convert the number directly instead of re-parsing it.
        if (value is IReadOnlyList<string> { Count: 1 } single) value = single[0];
        if (value is double number)
        {
            if (number <= 0 || number >= long.MaxValue) return null;
            try { return DateTimeOffset.FromFileTime((long)number).ToUniversalTime(); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        var text = ExprValue.Text(value);
        if (text.Length == 0) return null;

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ft))
        {
            if (ft <= 0 || ft == long.MaxValue) return null;
            try { return DateTimeOffset.FromFileTime(ft).ToUniversalTime(); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        if (DateTimeOffset.TryParseExact(text, ["yyyyMMddHHmmss.0Z", "yyyyMMddHHmmss.fZ", "yyyyMMddHHmmssZ"], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var gt))
            return gt;

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var iso) ? iso : null;
    }
}
