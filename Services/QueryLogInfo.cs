using System.Text;
using System.Text.RegularExpressions;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Human-readable description of an Active Roles REST API object search, used to make query
/// logging self-explanatory: which system is being queried (Active Roles configuration,
/// Active Directory or Entra), which domain/tenant, which object types and which LDAP filter.
/// Derivation is best-effort and never throws; unknown values fall back to placeholders.
/// </summary>
public sealed record QueryLogInfo(
    string QueryId,
    string Label,
    string Target,
    string Domain,
    string ObjectTypes,
    string Scope,
    string Filter)
{
    public const int DefaultMaxFilterLength = 200;

    public const string TargetActiveRoles = "ActiveRoles";
    public const string TargetActiveDirectory = "ActiveDirectory";
    public const string TargetEntra = "Entra";
    public const string TargetUnknown = "Unknown";

    private static readonly Regex ObjectTypeRegex = new(
        @"\((?:objectClass|objectCategory)=([^)*(]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RecipientTypeRegex = new(
        @"\(msExchRecipientTypeDetails=(\d+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static QueryLogInfo From(string? label, string? baseDn, string? filter, string? scope, int maxFilterLength = DefaultMaxFilterLength)
    {
        baseDn ??= string.Empty;
        filter ??= string.Empty;
        var target = ClassifyTarget(baseDn, filter);
        return new QueryLogInfo(
            NewQueryId(),
            NormalizeLabel(label),
            target,
            DescribeDomain(baseDn, target),
            ExtractObjectTypes(filter),
            string.IsNullOrWhiteSpace(scope) ? "-" : scope,
            Abbreviate(filter, maxFilterLength));
    }

    public static string NewQueryId() => Guid.NewGuid().ToString("N")[..8];

    public static string NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "(unspecified)";
        var trimmed = label.Trim();
        return trimmed.Length > 5 && trimmed.EndsWith("Async", StringComparison.Ordinal) ? trimmed[..^5] : trimmed;
    }

    public static string ClassifyTarget(string baseDn, string filter)
    {
        var dn = baseDn.Trim();
        if (dn.Contains("CN=Azure,CN=Configuration", StringComparison.OrdinalIgnoreCase)
            || filter.Contains("edsAzure", StringComparison.OrdinalIgnoreCase)
            || filter.Contains("edsvaAzure", StringComparison.OrdinalIgnoreCase))
            return TargetEntra;

        if (dn.EndsWith("CN=Configuration", StringComparison.OrdinalIgnoreCase))
            return TargetActiveRoles;

        if (dn.Equals("CN=Active Directory", StringComparison.OrdinalIgnoreCase)
            || dn.Contains("DC=", StringComparison.OrdinalIgnoreCase))
            return TargetActiveDirectory;

        return TargetUnknown;
    }

    public static string DescribeDomain(string baseDn, string target)
    {
        var dn = baseDn.Trim();
        if (dn.Length == 0) return "-";

        if (target == TargetActiveDirectory)
        {
            var dcParts = SplitDn(dn)
                .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                .Select(p => p[3..])
                .ToList();
            if (dcParts.Count > 0) return string.Join('.', dcParts);
            if (dn.Equals("CN=Active Directory", StringComparison.OrdinalIgnoreCase))
                return "(all managed domains)";
        }

        if (target == TargetEntra)
        {
            // Tenant containers live directly under CN=Azure,CN=Configuration.
            var parts = SplitDn(dn);
            var azureIndex = parts.FindIndex(p => p.Equals("CN=Azure", StringComparison.OrdinalIgnoreCase));
            if (azureIndex > 0)
            {
                var tenant = parts[azureIndex - 1];
                var eq = tenant.IndexOf('=');
                return eq >= 0 ? tenant[(eq + 1)..] : tenant;
            }
            return "(all tenants)";
        }

        return dn;
    }

    public static string ExtractObjectTypes(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return "-";

        var types = new List<string>();
        foreach (Match m in ObjectTypeRegex.Matches(filter))
        {
            var value = m.Groups[1].Value.Trim();
            if (value.Length > 0 && !types.Contains(value, StringComparer.OrdinalIgnoreCase))
                types.Add(value);
        }
        foreach (Match m in RecipientTypeRegex.Matches(filter))
        {
            var value = "recipientType:" + m.Groups[1].Value;
            if (!types.Contains(value, StringComparer.OrdinalIgnoreCase))
                types.Add(value);
        }
        return types.Count == 0 ? "-" : string.Join(',', types);
    }

    public static string Abbreviate(string filter, int maxLength = DefaultMaxFilterLength)
    {
        if (string.IsNullOrEmpty(filter)) return "-";
        var collapsed = WhitespaceRegex.Replace(filter, " ").Trim();
        if (maxLength <= 0 || collapsed.Length <= maxLength) return collapsed;

        var sb = new StringBuilder(maxLength + 24);
        sb.Append(collapsed, 0, maxLength);
        sb.Append("… [len=").Append(collapsed.Length).Append(']');
        return sb.ToString();
    }

    /// <summary>Key/value pairs for <c>ILogger.BeginScope</c> so every line of a query carries its context.</summary>
    public Dictionary<string, object> ToScope() => new()
    {
        ["QueryId"] = QueryId,
        ["QueryLabel"] = Label,
        ["QueryTarget"] = Target,
        ["QueryDomain"] = Domain,
    };

    private static List<string> SplitDn(string dn)
    {
        // Split on unescaped commas only (DN values can contain "\,").
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                sb.Append(c).Append(dn[++i]);
                continue;
            }
            if (c == ',')
            {
                parts.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString().Trim());
        return parts;
    }
}
