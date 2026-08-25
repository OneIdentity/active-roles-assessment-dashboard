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
        && !DeniesReadOn(target)
        && (AllowReadTargets.Contains(target) || AllowReadTargets.Contains("Any") || AllowReadTargets.Contains("All"));

    public bool DeniesReadOn(string target) =>
        !string.IsNullOrEmpty(target)
        && (DenyReadTargets.Contains(target) || DenyReadTargets.Contains("Any") || DenyReadTargets.Contains("All"));
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

    /// <summary>
    /// A single real <c>edsManagedObjectStatisticsData</c> object captured with the service account,
    /// reduced to the two things the visibility engine needs: the Access Template Link GUIDs
    /// effective on it and its structural class. Used to decide whether a viewer may see the
    /// Licensing dashboard (they must have List Object + Read objectClass, or Read all properties,
    /// on that class). Null when no statistics object exists / could be read.
    /// </summary>
    public LicensingVisibilityProbe? LicensingProbe { get; init; }

    public static ArPermissionModel Empty { get; } = new()
    {
        LinksByGuid = new Dictionary<string, AccessTemplateLinkModel>(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>True when the raw AR item is visible to <paramref name="user"/> under this model.</summary>
    public bool IsVisible(JsonElement item, UserSidSet user) => PermissionScope.IsVisibleTo(item, user, this);

    /// <summary>True when the enriched typed item is visible to <paramref name="user"/> under this model.</summary>
    public bool IsVisible(IPermissionScoped item, UserSidSet user) => PermissionScope.IsVisibleTo(item, user, this);

    /// <summary>
    /// True when <paramref name="user"/> is permitted to see the Licensing dashboard: they must be
    /// able to read a <c>edsManagedObjectStatisticsData</c> object (List Object + Read objectClass,
    /// or Read all properties). Evaluated with the same Allow/Deny link engine used for AD objects
    /// against the captured <see cref="LicensingProbe"/>. Returns false when no probe was captured
    /// (least-privilege default).
    /// </summary>
    public bool GrantsLicensingVisibility(UserSidSet user)
        => LicensingProbe is { } probe
           && PermissionScope.IsVisibleTo(probe.EffectiveLinkGuids, probe.ObjectClass, user, this);
}

/// <summary>
/// Minimal snapshot of a real <c>edsManagedObjectStatisticsData</c> object needed to evaluate
/// Licensing-dashboard visibility: the effective AT Link GUIDs on it plus its structural class.
/// </summary>
public sealed class LicensingVisibilityProbe
{
    public required IReadOnlyList<string> EffectiveLinkGuids { get; init; }
    public required string ObjectClass { get; init; }
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
        // AT Link (edsACE) objects live under CN=AT Links. The AR REST API does NOT accept
        // 'CN=AT Links,CN=Configuration' as a base (400), and it also rejects an
        // '(objectClass=edsACE)' filter at the configuration root (400). Enumerating by the presence
        // of the trustee-SID attribute — '(edsaTrusteeSID=*)' under CN=Configuration, subtree — is
        // accepted and returns every link. The AT Link's key attributes are:
        //   edsaTrusteeSID        - the trustee SID (BINARY, base64-encoded in REST JSON)
        //   edsvaAccessTemplateDN - the DN of the linked Access Template
        //   edsaAccessTemplateGUID- the GUID of the linked Access Template (alternative to the DN)
        //   edsvaSecObjectDN      - the directory object the template is linked to
        var linkItems = await SearchAsync(
            serviceToken, ConfigDn, "(edsaTrusteeSID=*)", "sub",
            new[] { "name", "objectGUID", "edsaTrusteeSID", "edsvaAccessTemplateDN", "edsaAccessTemplateGUID", "edsvaSecObjectDN" }, ct);

        // 2. Cache Access Template read models on demand (many links share a template).
        var templateCache = new Dictionary<string, AccessTemplateReadModel?>(StringComparer.OrdinalIgnoreCase);
        var linksByGuid = new Dictionary<string, AccessTemplateLinkModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in linkItems)
        {
            ct.ThrowIfCancellationRequested();

            // Objects reference their effective links by the link's CN (the value that appears in
            // edsvaATLinksEffective, e.g. CN=<name>,CN=AT Links,...). For AR-generated custom links
            // the CN is a distinct GUID that is NOT the object's objectGUID, so the join key MUST be
            // the CN (name). We index by name AND by normalized objectGUID so lookups succeed no
            // matter which form an object's effective-link DN carries.
            var name = GetAttr(item, "name");
            var objectGuid = NormalizeGuid(GetAttr(item, "objectGUID"));

            var linkGuid = !string.IsNullOrEmpty(name) ? name : objectGuid;
            if (string.IsNullOrEmpty(linkGuid))
                continue;

            var templateDn = GetAttr(item, "edsvaAccessTemplateDN");

            var link = new AccessTemplateLinkModel
            {
                LinkGuid = linkGuid,
                AccessTemplateName = templateDn
            };

            // The trustee SID is a binary value (base64 in JSON). Decode it to canonical S-1-5-...
            // string form so it can be compared against the user's objectSid / tokenGroups.
            foreach (var rawSid in GetMulti(item, "edsaTrusteeSID"))
            {
                var sidString = SidToString(rawSid);
                if (!string.IsNullOrEmpty(sidString))
                    link.TrusteeSids.Add(sidString);
            }

            if (!string.IsNullOrEmpty(templateDn))
            {
                if (!templateCache.TryGetValue(templateDn, out var tmpl))
                {
                    tmpl = await LoadTemplateReadModelAsync(serviceToken, templateDn, ct);
                    templateCache[templateDn] = tmpl;
                }
                link.Template = tmpl;
            }

            // Register under both keys so an object referencing the link by either CN or objectGUID
            // resolves to the same link model.
            if (!string.IsNullOrEmpty(name))
                linksByGuid[name] = link;
            if (!string.IsNullOrEmpty(objectGuid))
                linksByGuid[objectGuid] = link;
        }

        _logger.LogInformation("Built AR permission model: {LinkCount} AT links, {TemplateCount} templates.",
            linksByGuid.Count, templateCache.Count);

        var licensingProbe = await BuildLicensingProbeAsync(serviceToken, ct);

        return new ArPermissionModel { LinksByGuid = linksByGuid, LicensingProbe = licensingProbe };
    }

    // Base DN under which Active Roles keeps the per-run managed-object-statistics data. The class
    // 'edsManagedObjectStatisticsData' is what a user must be able to read to see the Licensing
    // dashboard, so we probe a real instance and evaluate its effective AT Links per viewer.
    private const string ManagedObjectStatisticsBaseDn =
        "CN=Managed Object Statistics,CN=Server Configuration,CN=Configuration";

    /// <summary>
    /// Captures one real <c>edsManagedObjectStatisticsData</c> object (with its effective AT Links
    /// and structural class) so the per-user filter can decide Licensing-dashboard visibility using
    /// the same Allow/Deny engine that governs AD objects. Returns null when no statistics object
    /// exists or the search fails, in which case non-admins are denied the dashboard.
    /// </summary>
    private async Task<LicensingVisibilityProbe?> BuildLicensingProbeAsync(string serviceToken, CancellationToken ct)
    {
        try
        {
            var items = await SearchAsync(
                serviceToken, ManagedObjectStatisticsBaseDn,
                "(objectClass=edsManagedObjectStatisticsData)", "sub",
                new[] { "name", "objectClass", SegmentAttributes.EffectiveLinksAttribute }, ct);

            if (items.Count == 0)
            {
                _logger.LogInformation("Licensing visibility probe: no edsManagedObjectStatisticsData object found.");
                return null;
            }

            var probeItem = items[0];
            var probe = new LicensingVisibilityProbe
            {
                EffectiveLinkGuids = SegmentAttributes.EffectiveLinksOf(probeItem).ToList(),
                ObjectClass = SegmentAttributes.ClassOf(probeItem)
            };

            _logger.LogInformation(
                "Licensing visibility probe: captured 1 statistics object with {LinkCount} effective AT links (class '{Class}').",
                probe.EffectiveLinkGuids.Count, probe.ObjectClass);
            return probe;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Licensing visibility probe failed; Licensing dashboard will be hidden from non-admins.");
            return null;
        }
    }

    /// <summary>
    /// Resolves a user's SID set: their own objectSid plus the SIDs of all groups they are a
    /// transitive member of (via tokenGroups where available, falling back to memberOf walking).
    /// </summary>
    public async Task<UserSidSet> ResolveUserSidSetAsync(string serviceToken, string username, CancellationToken ct = default)
    {
        var result = new UserSidSet { Username = username };

        var sam = username.Contains('\\') ? username[(username.IndexOf('\\') + 1)..] : username;
        // Drop any UPN suffix (user@domain) so we match on the bare sAMAccountName.
        if (sam.Contains('@')) sam = sam[..sam.IndexOf('@')];
        var escaped = EscapeLdapValue(sam);

        // NOTE: requesting 'tokenGroups' as a SEARCH attribute triggers an AR REST API bug where the
        // search silently returns an empty result set. We therefore resolve in two steps:
        //   1. SEARCH (without tokenGroups) to locate the account and get its objectGUID.
        //   2. GET the single object by GUID with includeAttributes=all to obtain tokenGroups
        //      (includeAttributes=all only works on a single-object GET, not on a search).
        var attrs = new[] { "objectGUID", "objectSid", "objectClass", "sAMAccountName" };

        // The AR REST virtual 'CN=Active Directory' provider is CASE-SENSITIVE on the attribute name
        // in the filter and exposes the account attribute as 'samAccountName' (lowercase s/a), not the
        // standard AD 'sAMAccountName'. Using the wrong casing silently returns zero rows. Try the
        // AR casing first, then the standard casing, then a class-agnostic fallback, filtering to the
        // user class client-side so a resolvable account is never silently missed.
        var users = await SearchAsync(
            serviceToken, DirectoryDn,
            $"(&(objectClass=user)(samAccountName={escaped}))", "sub", attrs, ct);

        if (users.Count == 0)
        {
            users = await SearchAsync(
                serviceToken, DirectoryDn,
                $"(&(objectClass=user)(sAMAccountName={escaped}))", "sub", attrs, ct);
        }

        if (users.Count == 0)
        {
            users = await SearchAsync(
                serviceToken, DirectoryDn,
                $"(samAccountName={escaped})", "sub", attrs, ct);
        }

        // Keep only genuine user objects (the simpler filter may also return group/contact matches).
        users = users
            .Where(u => string.Equals(GetAttr(u, "objectClass"), "user", StringComparison.OrdinalIgnoreCase)
                        || GetMulti(u, "objectClass").Any(c => string.Equals(c, "user", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (users.Count == 0)
        {
            _logger.LogWarning("Could not resolve SID set for user '{User}' (sam='{Sam}', base='{Base}').",
                username, sam, DirectoryDn);
            return result;
        }

        var searchHit = users[0];

        // Add the account's own SID from the search hit (this attribute is safe in a search).
        var ownSid = SidToString(GetAttr(searchHit, "objectSid"));
        if (!string.IsNullOrEmpty(ownSid))
            result.Sids.Add(ownSid);

        // Step 2: fetch the full object by GUID with includeAttributes=all to obtain tokenGroups,
        // which cannot be requested via search (AR REST bug). This is the authoritative, transitive
        // set of group SIDs computed by AD, so it supersedes any memberOf walking.
        var objectGuid = NormalizeGuid(GetAttr(searchHit, "objectGUID"));
        var full = await GetObjectAllAttributesAsync(serviceToken, objectGuid, ct);

        if (full is { } fullObj)
        {
            if (result.Sids.Count == 0)
            {
                var fullOwnSid = SidToString(GetAttr(fullObj, "objectSid"));
                if (!string.IsNullOrEmpty(fullOwnSid))
                    result.Sids.Add(fullOwnSid);
            }

            foreach (var sid in GetMulti(fullObj, "tokenGroups"))
            {
                var s = SidToString(sid);
                if (!string.IsNullOrEmpty(s))
                    result.Sids.Add(s);
            }
        }

        // Fallback: if tokenGroups was unavailable, walk memberOf and resolve group SIDs.
        if (result.Sids.Count <= 1 && full is { } memberSource)
        {
            var memberOf = GetMulti(memberSource, "memberOf");
            _logger.LogDebug("User '{User}' has {Count} memberOf entries (tokenGroups unavailable, walking memberOf).", username, memberOf.Count);
            foreach (var groupDn in memberOf)
            {
                ct.ThrowIfCancellationRequested();
                var groups = await SearchAsync(
                    serviceToken, groupDn, "(objectClass=group)", "base",
                    new[] { "objectSid" }, ct);
                if (groups.Count > 0)
                {
                    var gsid = SidToString(GetAttr(groups[0], "objectSid"));
                    if (!string.IsNullOrEmpty(gsid))
                        result.Sids.Add(gsid);
                }
                else
                {
                    _logger.LogDebug("memberOf group '{GroupDn}' returned no objectSid.", groupDn);
                }
            }
        }

        _logger.LogInformation("Resolved {Count} SIDs for user '{User}'.", result.Sids.Count, username);
        return result;
    }

    /// <summary>
    /// Fetches a single directory object by its objectGUID with includeAttributes=all. This is the
    /// only reliable way to obtain 'tokenGroups' from the AR REST API — requesting tokenGroups as a
    /// search attribute triggers an API bug that returns an empty result set, and includeAttributes=all
    /// is only honoured on a single-object GET (not on a search). Returns null if not found/failed.
    /// </summary>
    private async Task<JsonElement?> GetObjectAllAttributesAsync(string serviceToken, string? objectGuid, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(objectGuid))
            return null;

        var client = CreateClient(serviceToken);
        var url = $"{BaseUrl}/objects/{Uri.EscapeDataString(objectGuid)}?includeAttributes=all";

        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var response = await client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);

                // The endpoint may return the object directly or wrapped in an 'items' array.
                if (doc.RootElement.TryGetProperty("items", out var items)
                    && items.ValueKind == JsonValueKind.Array)
                {
                    var first = items.EnumerateArray().FirstOrDefault();
                    return first.ValueKind == JsonValueKind.Object ? first.Clone() : (JsonElement?)null;
                }

                return doc.RootElement.ValueKind == JsonValueKind.Object
                    ? doc.RootElement.Clone()
                    : (JsonElement?)null;
            }
            catch (Exception ex)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogWarning(ex, "Failed to GET object '{Guid}' with includeAttributes=all after {Attempts} attempts.", objectGuid, attempt);
                    return null;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
        }
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

        // edsaATEList encodes ACEs in an SDDL-like form, one per bracketed token, e.g.
        //   [A;;LO;;;bf967a9c-...][A;;RP;;;bf967aba-...]
        // Each token splits on ';' into the standard ACE fields:
        //   [0]=AceType (A=Allow, D=Deny)   [1]=Flags   [2]=Rights (access mask mnemonics)
        //   [3]=ObjectType (a specific attribute / property-set / extended-right GUID; empty = all)
        //   [4]=InheritedObjectType (the CLASS the ACE applies to; empty = all classes)
        //
        // Interpretation follows the ActiveRolesMcpServer AccessTemplateParser (GetCategory /
        // GetDescriptionAndScope): the access mask determines a permission CATEGORY, and the meaning
        // of ObjectType/InheritedObjectType depends on that category. For the dashboard we only care
        // whether the trustee can SEE an object and know its type, so we grant visibility when an
        // Allow ACE is any of:
        //   - Full Control          (category FullControl)
        //   - List Object           (ObjectAccess mask contains LO)  <-- alone this is sufficient
        //   - Read all properties   (ObjectPropertyAccess RP with EMPTY ObjectType)
        //   - Read objectClass      (ObjectPropertyAccess RP whose ObjectType is objectClass) [nice-to-have]
        // Write/create/delete and per-attribute reads on OTHER attributes are ignored.
        foreach (var ace in GetMulti(items[0], "edsaATEList"))
        {
            if (string.IsNullOrEmpty(ace)) continue;

            foreach (var token in SplitAceTokens(ace))
            {
                var fields = token.Split(';');
                if (fields.Length < 3) continue;

                var aceType = fields[0].Trim();
                var isAllow = aceType.Equals("A", StringComparison.OrdinalIgnoreCase)
                              || aceType.Equals("OA", StringComparison.OrdinalIgnoreCase);
                var isDeny = aceType.Equals("D", StringComparison.OrdinalIgnoreCase)
                             || aceType.Equals("OD", StringComparison.OrdinalIgnoreCase);
                if (!isAllow && !isDeny) continue;

                var mask = fields[2].Trim();
                var objectType = fields.Length > 3 ? fields[3].Trim() : string.Empty;   // specific attribute/right
                var inheritedType = fields.Length > 4 ? fields[4].Trim().TrimEnd(']').Trim() : string.Empty; // target class

                var category = GetCategory(mask);

                bool grantsVisibility = category switch
                {
                    // Full Control implies List Object + Read Property, so it always grants visibility.
                    AteCategory.FullControl => true,
                    // Object access: List Object (enumerate) is sufficient on its own.
                    AteCategory.ObjectAccess => mask.Contains("LO", StringComparison.OrdinalIgnoreCase),
                    // Property access: Read all properties (no ObjectType) or Read objectClass.
                    AteCategory.ObjectPropertyAccess =>
                        mask.Contains("RP", StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrEmpty(objectType) || IsObjectClassAttribute(objectType)),
                    _ => false
                };
                if (!grantsVisibility) continue;

                // The class this ACE applies to comes from InheritedObjectType; empty => all classes.
                var target = ClassNameFromGuid(inheritedType);
                if (string.IsNullOrEmpty(target)) target = "Any";

                if (isDeny) model.DenyReadTargets.Add(target);
                else model.AllowReadTargets.Add(target);
            }
        }

        return model;
    }

    private enum AteCategory { Unknown, FullControl, ObjectAccess, ObjectPropertyAccess, CreationDeletionOfChildObjects }

    // Mirrors ActiveRolesMcpServer AccessTemplateParser.GetCategory: classify an access-mask string.
    private static AteCategory GetCategory(string accessMask)
    {
        // Full Control: all 14 standard rights tokens present.
        var fullControlTokens = new[] { "CC", "DC", "LC", "SW", "RP", "WP", "DT", "LO", "CR", "CO", "SD", "RC", "WD", "WO" };
        if (fullControlTokens.All(t => accessMask.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return AteCategory.FullControl;

        if (accessMask.Contains("RP", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("WP", StringComparison.OrdinalIgnoreCase))
            return AteCategory.ObjectPropertyAccess;

        if (accessMask.Contains("CC", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("DC", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("MT", StringComparison.OrdinalIgnoreCase))
            return AteCategory.CreationDeletionOfChildObjects;

        if (accessMask.Contains("SD", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("DT", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("RC", StringComparison.OrdinalIgnoreCase) ||
            accessMask.Contains("WD", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("LC", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("LO", StringComparison.OrdinalIgnoreCase) ||
            accessMask.Contains("CO", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("MF", StringComparison.OrdinalIgnoreCase) || accessMask.Contains("CR", StringComparison.OrdinalIgnoreCase) ||
            accessMask.Contains("SW", StringComparison.OrdinalIgnoreCase))
            return AteCategory.ObjectAccess;

        return AteCategory.Unknown;
    }

    // objectClass attribute schemaIDGUID (bf967a91-...) — an RP scoped to it means "Read objectClass".
    private static bool IsObjectClassAttribute(string objectType) =>
        !string.IsNullOrEmpty(objectType)
        && objectType.Trim().StartsWith("bf967a91-0de6-11d0-a285-00aa003049e2", StringComparison.OrdinalIgnoreCase);

    // Splits an edsaATEList value into individual '[...]' ACE tokens (contents without the brackets).
    private static IEnumerable<string> SplitAceTokens(string ace)
    {
        int start = -1;
        for (var i = 0; i < ace.Length; i++)
        {
            if (ace[i] == '[') start = i + 1;
            else if (ace[i] == ']' && start >= 0)
            {
                yield return ace[start..i];
                start = -1;
            }
        }

        // Fall back to the whole string if it wasn't bracketed.
        if (start == -1 && !ace.Contains('['))
            yield return ace;
    }

    // Well-known AD schema class GUIDs used in edsaATEList inheritedObjectType fields.
    private static string ClassNameFromGuid(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return string.Empty;
        return guid.Trim().ToLowerInvariant() switch
        {
            "bf967aba-0de6-11d0-a285-00aa003049e2" => "user",
            "bf967a9c-0de6-11d0-a285-00aa003049e2" => "group",
            "bf967a86-0de6-11d0-a285-00aa003049e2" => "computer",
            "4828cc14-1437-45bc-9b07-ad6f015e5f28" => "inetorgperson",
            "bf967aa8-0de6-11d0-a285-00aa003049e2" => "printqueue",
            "bf967a0a-0de6-11d0-a285-00aa003049e2" => "contact",
            _ => string.Empty
        };
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
        // Mirror the proven-working ActiveRolesService URL shape: escape only '&' in values and let
        // HttpClient percent-encode spaces. DNs keep their literal '=' and ',' (the AR REST API
        // expects a literal DN in the base parameter). Attributes MUST be repeated
        // (attributes=a&attributes=b), never comma-separated.
        var attrQuery = string.Join("&", attributes.Select(a => $"attributes={EscapeAmp(a)}"));
        var url = $"{BaseUrl}/objects?base={EscapeAmp(baseDn)}&filter={EscapeAmp(filter)}&scope={scope}&{attrQuery}";
        var all = new List<JsonElement>();

        while (url != null)
        {
            ct.ThrowIfCancellationRequested();
            HttpResponseMessage response;

            // The AR REST endpoint is intermittently flaky and can transiently fail an otherwise
            // valid request. A permanently-empty SID set silently disables a user's entire view, so
            // retry transient failures a few times before giving up.
            const int maxAttempts = 4;
            var attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    response = await client.GetAsync(url, ct);
                    response.EnsureSuccessStatusCode();
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt >= maxAttempts)
                    {
                        _logger.LogWarning(ex, "AR search failed for base '{Base}' filter '{Filter}' url '{Url}' after {Attempts} attempts.", baseDn, filter, url, attempt);
                        return all;
                    }

                    _logger.LogDebug(ex, "AR search transient failure (attempt {Attempt}/{Max}) for base '{Base}' filter '{Filter}'; retrying.", attempt, maxAttempts, baseDn, filter);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
                }
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

    /// <summary>
    /// Converts an AR REST SID value to canonical S-1-5-... string form. AR returns SIDs either as
    /// a base64-encoded binary blob (e.g. edsaTrusteeSID, and often tokenGroups/objectSid) or, in
    /// some responses, already as an S-1-... string. Handles both; returns empty on failure.
    /// </summary>
    private static string SidToString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (LooksLikeSid(raw)) return raw;

        try
        {
            var bytes = Convert.FromBase64String(raw);
            if (bytes.Length == 0) return string.Empty;
            return new System.Security.Principal.SecurityIdentifier(bytes, 0).Value;
        }
        catch
        {
            return string.Empty;
        }
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
