using System.Net.Http.Headers;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Read/List grant summary for a single Access Template, reduced to what the visibility
/// filter needs: which object classes (Targets) the template grants read on, and whether
/// any Deny ACE is present for a class. Empirically validated against the "Custom: Help Desk -
/// View Users and Groups" (Target = User, Group) and "Custom: View PROD domain" (Target =
/// Domain-DNS) templates.
/// </summary>
public sealed class AccessTemplateReadModel
{
    public required string Name { get; init; }

    /// <summary>Object classes (AR Target names, lowercased) the template grants Read/List on.</summary>
    public HashSet<string> AllowReadTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Object classes (AR Target names, lowercased) explicitly Denied read by this template.</summary>
    public HashSet<string> DenyReadTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool GrantsReadOn(string target) =>
        !string.IsNullOrEmpty(target)
        && !DenyReadTargets.Contains(target)
        && (AllowReadTargets.Contains(target) || AllowReadTargets.Contains("Any") || AllowReadTargets.Contains("All"));

    public bool DeniesReadOn(string target) =>
        !string.IsNullOrEmpty(target) && DenyReadTargets.Contains(target);
}

/// <summary>
/// A single Access Template Link keyed by its GUID (as it appears in an object's
/// <c>edsvaATLinksEffective</c> DN: <c>CN=&lt;LinkGuid&gt;,CN=AT Links,CN=Configuration</c>),
/// carrying the trustee SIDs and the linked template's read model.
/// </summary>
public sealed class AccessTemplateLinkModel
{
    public required string LinkGuid { get; init; }
    public required string AccessTemplateName { get; init; }
    public HashSet<string> TrusteeSids { get; } = new(StringComparer.OrdinalIgnoreCase);
    public AccessTemplateReadModel? Template { get; set; }
}

/// <summary>
/// The full permission model captured with the service-account identity: a map of AT Link
/// GUID -> link (trustee SIDs + template read model). Immutable once built; rebuilt per refresh.
/// </summary>
public sealed class ArPermissionModel
{
    public required IReadOnlyDictionary<string, AccessTemplateLinkModel> LinksByGuid { get; init; }
    public DateTimeOffset BuiltAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static ArPermissionModel Empty { get; } = new()
    {
        LinksByGuid = new Dictionary<string, AccessTemplateLinkModel>(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>True when the raw AR item is visible to <paramref name="user"/> under this model.</summary>
    public bool IsVisible(JsonElement item, UserSidSet user) => PermissionScope.IsVisibleTo(item, user, this);

    /// <summary>True when the enriched typed item is visible to <paramref name="user"/> under this model.</summary>
    public bool IsVisible(IPermissionScoped item, UserSidSet user) => PermissionScope.IsVisibleTo(item, user, this);
}

/// <summary>
/// The resolved SID set for a principal: their own objectSid plus the SIDs of every group
/// they are a (transitive) member of. Delegation in Active Roles is SID-based and frequently
/// Indirect (via nested groups), so visibility must be evaluated against the whole set.
/// </summary>
public sealed class UserSidSet
{
    public required string Username { get; init; }
    public HashSet<string> Sids { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Builds the Active Roles permission model (Access Template Links + template read grants) and
/// resolves per-user SID sets, all using the SERVICE-ACCOUNT token. End-user tokens cannot read
/// this configuration, so this service is the sole source of the data the per-user filter needs.
/// </summary>
public class ArPermissionModelService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;
    private readonly ILogger<ArPermissionModelService> _logger;

    public ArPermissionModelService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ActiveRolesConfig> config,
        ILogger<ArPermissionModelService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    private string BaseUrl => _config.CurrentValue.ApiBaseUrl.TrimEnd('/');
    private string ConfigDn => string.IsNullOrWhiteSpace(_config.CurrentValue.DefaultARConfigurationDN)
        ? "CN=Configuration"
        : _config.CurrentValue.DefaultARConfigurationDN;
    private string DirectoryDn => string.IsNullOrWhiteSpace(_config.CurrentValue.DefaultActiveDirectoryDN)
        ? "CN=Active Directory"
        : _config.CurrentValue.DefaultActiveDirectoryDN;

    /// <summary>
    /// Builds the permission model: enumerates Access Template Links under CN=AT Links,
    /// resolves each link's trustee SIDs and its Access Template's read grants (Target class +
    /// Deny), keyed by link GUID for O(1) lookup during per-object filtering.
    /// </summary>
    public async Task<ArPermissionModel> BuildAsync(string serviceToken, CancellationToken ct = default)
    {
        var atLinksBase = $"CN=AT Links,{ConfigDn}";

        // 1. Enumerate all AT Link objects (edsACE) under CN=AT Links.
        var linkItems = await SearchAsync(
            serviceToken, atLinksBase, "(objectClass=edsACE)", "sub",
            new[] { "name", "objectGUID", "edsaACETrustee", "edsaACEAccessTemplate" }, ct);

        // 2. Cache Access Template read models on demand (many links share a template).
        var templateCache = new Dictionary<string, AccessTemplateReadModel?>(StringComparer.OrdinalIgnoreCase);
        var linksByGuid = new Dictionary<string, AccessTemplateLinkModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in linkItems)
        {
            ct.ThrowIfCancellationRequested();

            var linkGuid = NormalizeGuid(GetAttr(item, "objectGUID"))
                           ?? GetAttr(item, "name");
            if (string.IsNullOrEmpty(linkGuid))
                continue;

            var link = new AccessTemplateLinkModel
            {
                LinkGuid = linkGuid,
                AccessTemplateName = GetAttr(item, "edsaACEAccessTemplate")
            };

            foreach (var sid in await ResolveTrusteeSidsAsync(serviceToken, GetMulti(item, "edsaACETrustee"), ct))
                link.TrusteeSids.Add(sid);

            var templateDn = GetAttr(item, "edsaACEAccessTemplate");
            if (!string.IsNullOrEmpty(templateDn))
            {
                if (!templateCache.TryGetValue(templateDn, out var tmpl))
                {
                    tmpl = await LoadTemplateReadModelAsync(serviceToken, templateDn, ct);
                    templateCache[templateDn] = tmpl;
                }
                link.Template = tmpl;
            }

            linksByGuid[linkGuid] = link;
        }

        _logger.LogInformation("Built AR permission model: {LinkCount} AT links, {TemplateCount} templates.",
            linksByGuid.Count, templateCache.Count);

        return new ArPermissionModel { LinksByGuid = linksByGuid };
    }

    /// <summary>
    /// Resolves a user's SID set: their own objectSid plus the SIDs of all groups they are a
    /// transitive member of (via tokenGroups where available, falling back to memberOf walking).
    /// </summary>
    public async Task<UserSidSet> ResolveUserSidSetAsync(string serviceToken, string username, CancellationToken ct = default)
    {
        var result = new UserSidSet { Username = username };

        var sam = username.Contains('\\') ? username[(username.IndexOf('\\') + 1)..] : username;
        var escaped = EscapeLdapValue(sam);

        var users = await SearchAsync(
            serviceToken, DirectoryDn,
            $"(&(objectClass=user)(sAMAccountName={escaped}))", "sub",
            new[] { "objectSid", "tokenGroups", "memberOf", "distinguishedName" }, ct);

        if (users.Count == 0)
        {
            _logger.LogWarning("Could not resolve SID set for user '{User}'.", username);
            return result;
        }

        var user = users[0];

        var ownSid = GetAttr(user, "objectSid");
        if (!string.IsNullOrEmpty(ownSid))
            result.Sids.Add(ownSid);

        // Preferred: tokenGroups already contains all transitive group SIDs computed by AD.
        foreach (var sid in GetMulti(user, "tokenGroups"))
            if (!string.IsNullOrEmpty(sid))
                result.Sids.Add(sid);

        // Fallback: if tokenGroups is unavailable, walk memberOf and resolve group SIDs.
        if (result.Sids.Count <= 1)
        {
            foreach (var groupDn in GetMulti(user, "memberOf"))
            {
                ct.ThrowIfCancellationRequested();
                var groups = await SearchAsync(
                    serviceToken, groupDn, "(objectClass=group)", "base",
                    new[] { "objectSid" }, ct);
                if (groups.Count > 0)
                {
                    var gsid = GetAttr(groups[0], "objectSid");
                    if (!string.IsNullOrEmpty(gsid))
                        result.Sids.Add(gsid);
                }
            }
        }

        _logger.LogInformation("Resolved {Count} SIDs for user '{User}'.", result.Sids.Count, username);
        return result;
    }

    private async Task<AccessTemplateReadModel?> LoadTemplateReadModelAsync(string serviceToken, string templateDn, CancellationToken ct)
    {
        // Read the template's parsed permission list (edsaATEList) via base-scope search.
        var items = await SearchAsync(
            serviceToken, templateDn, "(objectClass=*)", "base",
            new[] { "name", "edsaATEList" }, ct);
        if (items.Count == 0)
            return null;

        var model = new AccessTemplateReadModel { Name = GetAttr(items[0], "name") };

        // edsaATEList encodes ACEs; we look for Read Property / List Object grants and any Deny,
        // recording the Target object class. The raw values are opaque per deployment, so we scan
        // the multi-valued entries for the read/list markers and target class tokens.
        foreach (var ace in GetMulti(items[0], "edsaATEList"))
        {
            if (string.IsNullOrEmpty(ace)) continue;

            var isRead = ace.Contains("Read Property", StringComparison.OrdinalIgnoreCase)
                         || ace.Contains("List Object", StringComparison.OrdinalIgnoreCase)
                         || ace.Contains("Read Permissions", StringComparison.OrdinalIgnoreCase);
            if (!isRead) continue;

            var isDeny = ace.Contains("Deny", StringComparison.OrdinalIgnoreCase);
            var target = ExtractTarget(ace);
            if (string.IsNullOrEmpty(target)) target = "Any";

            if (isDeny) model.DenyReadTargets.Add(target);
            else model.AllowReadTargets.Add(target);
        }

        return model;
    }

    // Trustees on edsACE are typically SIDs already, but may be DNs; resolve DNs to objectSid.
    private async Task<IEnumerable<string>> ResolveTrusteeSidsAsync(string serviceToken, IReadOnlyList<string> trustees, CancellationToken ct)
    {
        var sids = new List<string>();
        foreach (var trustee in trustees)
        {
            if (string.IsNullOrEmpty(trustee)) continue;

            if (LooksLikeSid(trustee))
            {
                sids.Add(trustee);
                continue;
            }

            var items = await SearchAsync(serviceToken, trustee, "(objectClass=*)", "base",
                new[] { "objectSid" }, ct);
            if (items.Count > 0)
            {
                var sid = GetAttr(items[0], "objectSid");
                if (!string.IsNullOrEmpty(sid)) sids.Add(sid);
            }
        }
        return sids;
    }

    // ---- HTTP + JSON helpers (mirror ActiveRolesService conventions) ----

    private HttpClient CreateClient(string token)
    {
        var client = _httpClientFactory.CreateClient("ActiveRolesApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<List<JsonElement>> SearchAsync(
        string token, string baseDn, string filter, string scope, IEnumerable<string> attributes, CancellationToken ct)
    {
        var client = CreateClient(token);
        // Attributes MUST be repeated (attributes=a&attributes=b), never comma-separated.
        var attrQuery = string.Join("&", attributes.Select(a => $"attributes={EscapeAmp(a)}"));
        var url = $"{BaseUrl}/objects?base={EscapeAmp(baseDn)}&filter={EscapeAmp(filter)}&scope={scope}&{attrQuery}";
        var all = new List<JsonElement>();

        while (url != null)
        {
            ct.ThrowIfCancellationRequested();
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AR search failed for base '{Base}' filter '{Filter}'.", baseDn, filter);
                break;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                    all.Add(item.Clone());

            // Pagination continuation sends ONLY the nextPage parameter.
            if (doc.RootElement.TryGetProperty("nextPage", out var next) && next.ValueKind == JsonValueKind.String)
                url = $"{BaseUrl}/objects?nextPage={next.GetString()}";
            else
                url = null;
        }

        return all;
    }

    private static string GetAttr(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var val)) return string.Empty;
        return val.ValueKind switch
        {
            JsonValueKind.String => val.GetString() ?? string.Empty,
            JsonValueKind.Array => val.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.String
                ? val.EnumerateArray().First().GetString() ?? string.Empty
                : string.Empty,
            JsonValueKind.Number => val.ToString(),
            _ => string.Empty
        };
    }

    private static List<string> GetMulti(JsonElement item, string name)
    {
        var list = new List<string>();
        if (!item.TryGetProperty(name, out var val)) return list;
        if (val.ValueKind == JsonValueKind.String)
            list.Add(val.GetString() ?? string.Empty);
        else if (val.ValueKind == JsonValueKind.Array)
            foreach (var e in val.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    list.Add(e.GetString() ?? string.Empty);
        return list;
    }

    private static string? NormalizeGuid(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return Guid.TryParse(raw, out var g) ? g.ToString() : raw;
    }

    private static bool LooksLikeSid(string value) =>
        value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractTarget(string ace)
    {
        // Look for a "Target=<class>" or "Target: <class>" token in the parsed ACE string.
        foreach (var marker in new[] { "Target=", "Target:" })
        {
            var idx = ace.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var rest = ace[(idx + marker.Length)..].TrimStart();
            var end = rest.IndexOfAny(new[] { ';', ',', '|', ')', ' ' });
            return (end > 0 ? rest[..end] : rest).Trim();
        }
        return null;
    }

    private static string EscapeAmp(string value) => value.Replace("&", "%26");

    private static string EscapeLdapValue(string value) =>
        value.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}

/// <summary>
/// The single per-user visibility gate. An object is visible to a user when at least one Access
/// Template Link effective on it (a) is trusteed to a SID the user holds (own SID or a nested
/// group SID) and (b) links a template that grants read on the object's class, with no matching
/// Deny. Every filtered item list — for both totals and drilldowns — flows through this predicate,
/// so Overview totals and all derived KPI counts stay consistent with what the user can actually
/// see. AR admins bypass this entirely (handled by the caller).
/// </summary>
public static class PermissionScope
{
    /// <summary>
    /// Evaluates visibility for a raw AR item that still carries <c>edsvaATLinksEffective</c> and
    /// <c>objectClass</c> (the shape returned by the service-account collection).
    /// </summary>
    public static bool IsVisibleTo(JsonElement element, UserSidSet user, ArPermissionModel model)
        => IsVisibleTo(SegmentAttributes.EffectiveLinksOf(element), SegmentAttributes.ClassOf(element), user, model);

    /// <summary>Evaluates visibility for a typed item that has been enriched during collection.</summary>
    public static bool IsVisibleTo(IPermissionScoped item, UserSidSet user, ArPermissionModel model)
        => item != null && IsVisibleTo(item.EffectiveLinkGuids, item.ObjectClass, user, model);

    /// <summary>Core evaluation over an object's effective link GUIDs and structural class.</summary>
    public static bool IsVisibleTo(
        IEnumerable<string> effectiveLinkGuids,
        string objectClass,
        UserSidSet user,
        ArPermissionModel model)
    {
        if (user == null || model == null || effectiveLinkGuids == null)
            return false;

        var sawDeny = false;
        var sawAllow = false;

        foreach (var guid in effectiveLinkGuids)
        {
            if (!model.LinksByGuid.TryGetValue(guid, out var link) || link.Template == null)
                continue;

            // The link must be trusteed to a SID the user holds (direct or via nested groups).
            if (!link.TrusteeSids.Overlaps(user.Sids))
                continue;

            if (link.Template.DeniesReadOn(objectClass))
                sawDeny = true;
            else if (link.Template.GrantsReadOn(objectClass))
                sawAllow = true;
        }

        // Deny (from a link the user is trusteed on) wins over any Allow.
        return sawAllow && !sawDeny;
    }

    /// <summary>
    /// Filters a sequence of raw AR items to those visible to <paramref name="user"/>. Returns the
    /// same reference semantics as the input (a materialized list) so callers can both count and
    /// enumerate the drilldown rows from a single projection.
    /// </summary>
    public static List<JsonElement> VisibleItems(
        IEnumerable<JsonElement> items, UserSidSet user, ArPermissionModel model)
        => items == null
            ? new List<JsonElement>()
            : items.Where(i => IsVisibleTo(i, user, model)).ToList();

    /// <summary>Filters a sequence of enriched typed items to those visible to <paramref name="user"/>.</summary>
    public static List<T> VisibleItems<T>(
        IEnumerable<T> items, UserSidSet user, ArPermissionModel model) where T : IPermissionScoped
        => items == null
            ? new List<T>()
            : items.Where(i => IsVisibleTo(i, user, model)).ToList();
}
