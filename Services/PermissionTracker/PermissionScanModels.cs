using System.Security.AccessControl;
using System.Text.Json;

namespace ActiveRolesDashboard.Services.PermissionTracker;

public enum PermissionScanStatus { Queued, Enumerating, AwaitingConfirmation, Running, Completed, Cancelled, Failed }

/// <summary>What a Permission Tracker scan covers.</summary>
public sealed record PermissionScanRequest
{
    public bool IncludeActiveDirectory { get; init; } = true;
    public bool IncludeActiveRoles { get; init; } = true;

    /// <summary>False (default) scans containers and key security objects; true scans every object.</summary>
    public bool IncludeAllObjects { get; init; }

    /// <summary>Optional base DN; null scans the whole managed directory.</summary>
    public string? BaseDn { get; init; }
}

/// <summary>An object enumerated for a scan.</summary>
public sealed record PermissionScanObject(string ObjectGuid, string Dn, string Name, string ObjectClass);

/// <summary>An AD object and its parsed DACL.</summary>
public sealed record AdObjectPermissions(PermissionScanObject Object, IReadOnlyList<AdPermissionEntry> Entries, string? Error);

/// <summary>The (possibly partial) outcome of a scan.</summary>
public sealed class PermissionScanResult
{
    public List<AdObjectPermissions> AdObjects { get; } = [];
    public List<ArPermissionEntry> ArEntries { get; } = [];
    public Dictionary<string, string> TrusteeNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Truncated { get; set; }
    public string? ArError { get; set; }

    public string TrusteeName(string sid) => TrusteeNames.TryGetValue(sid, out var n) ? n : sid;
}

/// <summary>Tunable limits; bound from the "PermissionTracker" configuration section.</summary>
public sealed class PermissionTrackerOptions
{
    public int MaxConcurrentScans { get; set; } = 2;
    public int ParallelReadsPerScan { get; set; } = 4;
    public int ConfirmationThreshold { get; set; } = 5000;
    public int MaxObjects { get; set; } = 50000;
    public int ResultRetentionMinutes { get; set; } = 60;
}

/// <summary>Directory access used by the scan engine (abstracted for testing).</summary>
public interface IPermissionScanDataSource
{
    string DefaultBaseDn { get; }
    Task<IReadOnlyList<PermissionScanObject>> EnumerateAsync(string userToken, string baseDn, bool includeAllObjects, CancellationToken ct);
    Task<RawSecurityDescriptor?> GetSecurityDescriptorAsync(string userToken, string objectGuid, CancellationToken ct);
    Task<IReadOnlyList<ArTemplateLinkRecord>> LoadAccessTemplateLinksAsync(CancellationToken ct);
    Task<string> ResolveTrusteeNameAsync(string userToken, string sid, CancellationToken ct);

    /// <summary>The schema <c>defaultSecurityDescriptor</c> (SDDL) of <paramref name="objectClass"/>, or null when unavailable.</summary>
    Task<string?> GetDefaultSecurityDescriptorAsync(string userToken, string objectDn, string objectClass, CancellationToken ct);
}

/// <summary>Active Roles REST implementation of <see cref="IPermissionScanDataSource"/>.</summary>
public sealed class ActiveRolesPermissionScanDataSource(
    ActiveRolesService activeRoles,
    ArPermissionModelService permissionModel,
    ServiceAccountTokenProvider serviceTokens,
    Microsoft.Extensions.Options.IOptionsMonitor<ActiveRolesDashboard.Models.ActiveRolesConfig> config,
    Microsoft.Extensions.Logging.ILogger<ActiveRolesPermissionScanDataSource> logger) : IPermissionScanDataSource
{
    /// <summary>
    /// Default scope: containers (where delegation is normally set) plus key security objects -
    /// protected accounts/groups (adminCount=1, AdminSDHolder-protected), domain controllers and GPOs.
    /// </summary>
    public const string KeyObjectsFilter =
        "(|(objectClass=domainDNS)(objectClass=organizationalUnit)(objectClass=container)(objectClass=builtinDomain)" +
        "(objectClass=groupPolicyContainer)(adminCount=1)(userAccountControl:1.2.840.113556.1.4.803:=8192))";

    public string DefaultBaseDn => config.CurrentValue.DefaultActiveDirectoryDN;

    public async Task<IReadOnlyList<PermissionScanObject>> EnumerateAsync(string userToken, string baseDn, bool includeAllObjects, CancellationToken ct)
    {
        var items = await activeRoles.SearchPermissionScopeAsync(userToken, baseDn,
            includeAllObjects ? "(objectClass=*)" : KeyObjectsFilter, "sub", "name,distinguishedName,objectClass,objectGUID");
        ct.ThrowIfCancellationRequested();
        return items.Select(ToObject).Where(o => o.ObjectGuid.Length > 0).ToList();
    }

    public Task<RawSecurityDescriptor?> GetSecurityDescriptorAsync(string userToken, string objectGuid, CancellationToken ct) =>
        activeRoles.GetSecurityDescriptorAsync(userToken, objectGuid);

    public async Task<IReadOnlyList<ArTemplateLinkRecord>> LoadAccessTemplateLinksAsync(CancellationToken ct)
    {
        var token = await serviceTokens.GetTokenAsync(ct);
        return await permissionModel.LoadAccessTemplateLinksAsync(token, ct);
    }

    public Task<string> ResolveTrusteeNameAsync(string userToken, string sid, CancellationToken ct) =>
        activeRoles.ResolveTrusteeNameAsync(userToken, sid);

    /// <summary>
    /// Reads the classSchema object's defaultSecurityDescriptor from the forest Schema partition.
    /// Candidate schema DNs come from <see cref="SchemaDnCandidates"/>: the object's own Configuration
    /// partition when it lives there, otherwise the object's domain naming context and its parents.
    /// </summary>
    public async Task<string?> GetDefaultSecurityDescriptorAsync(string userToken, string objectDn, string objectClass, CancellationToken ct)
    {
        var filter = $"(&(objectClass=classSchema)(lDAPDisplayName={objectClass}))";
        foreach (var schemaDn in SchemaDnCandidates(objectDn))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var items = await activeRoles.SearchPermissionScopeAsync(userToken, schemaDn, filter, "one", "defaultSecurityDescriptor,lDAPDisplayName");
                if (items.Count == 0) continue;
                var sddl = ReadString(items[0], "defaultSecurityDescriptor");
                if (string.IsNullOrEmpty(sddl))
                    logger.LogWarning("Permission Tracker: classSchema {Class} under {SchemaDn} returned no defaultSecurityDescriptor. Item: {Item}",
                        objectClass, schemaDn, items[0].GetRawText());
                return sddl;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Permission Tracker: schema lookup for {Class} under {SchemaDn} failed", objectClass, schemaDn);
            }
        }
        logger.LogWarning("Permission Tracker: no defaultSecurityDescriptor found for class {Class} (object {Dn}); default permissions cannot be filtered for it",
            objectClass, objectDn);
        return null;
    }

    /// <summary>Schema container DNs to try for an object, most specific first.</summary>
    public static IEnumerable<string> SchemaDnCandidates(string objectDn)
    {
        var parts = objectDn.Split(',').Select(p => p.Trim()).ToList();
        var cfg = parts.FindIndex(p => p.Equals("CN=Configuration", StringComparison.OrdinalIgnoreCase));
        var dcs = parts.Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cfg >= 0 && cfg + 1 < parts.Count && parts[cfg + 1].StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
        {
            var dn = "CN=Schema," + string.Join(",", parts.Skip(cfg));
            if (seen.Add(dn)) yield return dn;
        }
        for (var i = 0; i < dcs.Count - 1; i++)
        {
            var dn = "CN=Schema,CN=Configuration," + string.Join(",", dcs.Skip(i));
            if (seen.Add(dn)) yield return dn;
        }
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v) && !(item.TryGetProperty("attributes", out var a) && a.TryGetProperty(name, out v)))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array => v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText()).FirstOrDefault(),
            JsonValueKind.Object when v.TryGetProperty("value", out var inner) && inner.ValueKind == JsonValueKind.String => inner.GetString(),
            _ => null
        };
    }

    private static PermissionScanObject ToObject(JsonElement item)
    {
        static string Attr(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v) && !(e.TryGetProperty("attributes", out var a) && a.TryGetProperty(name, out v)))
                return string.Empty;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? string.Empty,
                JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).LastOrDefault() ?? string.Empty,
                _ => string.Empty
            };
        }

        // objectClass is multi-valued with the most specific (structural) class last.
        return new PermissionScanObject(Attr(item, "objectGUID"), Attr(item, "distinguishedName"), Attr(item, "name"), Attr(item, "objectClass"));
    }
}
