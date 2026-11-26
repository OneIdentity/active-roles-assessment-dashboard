using System.Globalization;

namespace ActiveRolesDashboard.Services.PermissionTracker;

/// <summary>
/// Recognises ACEs that come from a class's schema <c>defaultSecurityDescriptor</c> (applied by AD when
/// the object was created) so the Permission Tracker can hide them and show only explicitly set
/// permissions. The SDDL is parsed here (rather than via the Windows API) so domain-relative aliases
/// such as DA/DU resolve without a domain context: domain SIDs are compared by RID only.
/// </summary>
public static class DefaultSecurityDescriptors
{
    private static readonly Dictionary<string, int> RightCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GA"] = 0x10000000, ["GR"] = unchecked((int)0x80000000), ["GW"] = 0x40000000, ["GX"] = 0x20000000,
        ["RC"] = 0x20000, ["SD"] = 0x10000, ["WD"] = 0x40000, ["WO"] = 0x80000,
        ["RP"] = 0x10, ["WP"] = 0x20, ["CC"] = 0x1, ["DC"] = 0x2, ["LC"] = 0x4, ["SW"] = 0x8,
        ["LO"] = 0x80, ["DT"] = 0x40, ["CR"] = 0x100
    };

    private static readonly Dictionary<string, int> DomainRids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DA"] = 512, ["DU"] = 513, ["DG"] = 514, ["DC"] = 515, ["DD"] = 516, ["CA"] = 517, ["SA"] = 518,
        ["EA"] = 519, ["PA"] = 520, ["RO"] = 498, ["CN"] = 522, ["AP"] = 525, ["KA"] = 526, ["EK"] = 527, ["RS"] = 553
    };

    private static readonly Dictionary<string, string> WellKnownSids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SY"] = "S-1-5-18", ["PS"] = "S-1-5-10", ["AU"] = "S-1-5-11", ["WD"] = "S-1-1-0", ["CO"] = "S-1-3-0",
        ["CG"] = "S-1-3-1", ["ED"] = "S-1-5-9", ["AN"] = "S-1-5-7", ["NS"] = "S-1-5-20", ["LS"] = "S-1-5-19",
        ["SU"] = "S-1-5-6", ["IU"] = "S-1-5-4", ["NU"] = "S-1-5-2", ["BA"] = "S-1-5-32-544", ["BU"] = "S-1-5-32-545",
        ["BG"] = "S-1-5-32-546", ["PU"] = "S-1-5-32-547", ["AO"] = "S-1-5-32-548", ["SO"] = "S-1-5-32-549",
        ["PO"] = "S-1-5-32-550", ["BO"] = "S-1-5-32-551", ["RE"] = "S-1-5-32-552", ["RU"] = "S-1-5-32-554",
        ["ER"] = "S-1-5-32-573"
    };

    /// <summary>
    /// ACEs written on the domain root by domain creation (dcpromo / adprep), which are not part of the
    /// domainDNS schema defaultSecurityDescriptor.
    /// </summary>
    public const string DomainRootInstallDefaults =
        "D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;EA)(A;CI;RPWPCRCCDCLCLORCWOWDSDDTSW;;;EA)(A;;RPWPCRCCDCLCLORCWOWDSW;;;DA)" +
        "(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;SY)(A;CI;RPWPCRLCLOCCRCWDWOSDSW;;;BA)(A;;RPLCLORC;;;ED)(A;;RPLCLORC;;;AU)" +
        "(A;CI;LC;;;RU)(A;;RP;;;WD)(A;CI;RPLCLORC;;;BA)" +
        "(OA;;CR;1131f6aa-9c07-11d1-f79f-00c04fc2dcd2;;ED)(OA;;CR;1131f6aa-9c07-11d1-f79f-00c04fc2dcd2;;BA)(OA;;CR;1131f6aa-9c07-11d1-f79f-00c04fc2dcd2;;DD)(OA;;CR;1131f6aa-9c07-11d1-f79f-00c04fc2dcd2;;RO)" +
        "(OA;;CR;1131f6ad-9c07-11d1-f79f-00c04fc2dcd2;;BA)(OA;;CR;1131f6ad-9c07-11d1-f79f-00c04fc2dcd2;;DD)" +
        "(OA;;CR;89e95b76-444d-4c62-991a-0facbeda640c;;BA)(OA;;CR;89e95b76-444d-4c62-991a-0facbeda640c;;DD)(OA;;CR;89e95b76-444d-4c62-991a-0facbeda640c;;ED)" +
        "(OA;;CR;1131f6ab-9c07-11d1-f79f-00c04fc2dcd2;;BA)(OA;;CR;1131f6ac-9c07-11d1-f79f-00c04fc2dcd2;;BA)" +
        "(OA;;CR;e2a36dc9-ae17-47c3-b58b-be34c55ba633;;S-1-5-32-557)(OA;;CR;e2a36dc9-ae17-47c3-b58b-be34c55ba633;;BA)" +
        "(OA;;CR;280f369c-67c7-438e-ae98-1d46f3c6f541;;AU)(OA;;CR;ccc2dc7d-a6ad-4a7a-8846-c04e3cc53501;;AU)(OA;;CR;05c74c5e-4deb-43b4-bd9f-86664c2a7fd5;;AU)" +
        "(OA;;CR;3e0f7e18-2c7a-4c10-ba82-4d926db99a3e;;CN)(OA;;CR;d31a8757-2447-4545-8081-3bb610cacbf2;;ED)" +
        "(OA;;RP;c7407360-20bf-11d0-a768-00aa006e0529;;RU)(OA;;RP;b8119fd0-04f6-4762-ab7a-4986c76b3f9a;;RU)" +
        "(OA;;RP;c7407360-20bf-11d0-a768-00aa006e0529;;AU)(OA;;RP;b8119fd0-04f6-4762-ab7a-4986c76b3f9a;;AU)" +
        "(OA;;RP;b8119fd0-04f6-4762-ab7a-4986c76b3f9a;;AN)" +
        "(OA;CIIO;RP;4c164200-20c0-11d0-a768-00aa006e0529;4828cc14-1437-45bc-9b07-ad6f015e5f28;RU)(OA;CIIO;RP;4c164200-20c0-11d0-a768-00aa006e0529;bf967aba-0de6-11d0-a285-00aa003049e2;RU)" +
        "(OA;CIIO;RP;5f202010-79a5-11d0-9020-00c04fc2d4cf;4828cc14-1437-45bc-9b07-ad6f015e5f28;RU)(OA;CIIO;RP;5f202010-79a5-11d0-9020-00c04fc2d4cf;bf967aba-0de6-11d0-a285-00aa003049e2;RU)" +
        "(OA;CIIO;RP;bc0ac240-79a9-11d0-9020-00c04fc2d4cf;4828cc14-1437-45bc-9b07-ad6f015e5f28;RU)(OA;CIIO;RP;bc0ac240-79a9-11d0-9020-00c04fc2d4cf;bf967aba-0de6-11d0-a285-00aa003049e2;RU)" +
        "(OA;CIIO;RP;59ba2f42-79a2-11d0-9020-00c04fc2d3cf;4828cc14-1437-45bc-9b07-ad6f015e5f28;RU)(OA;CIIO;RP;59ba2f42-79a2-11d0-9020-00c04fc2d3cf;bf967aba-0de6-11d0-a285-00aa003049e2;RU)" +
        "(OA;CIIO;RP;037088f8-0ae1-11d2-b422-00a0c968f939;4828cc14-1437-45bc-9b07-ad6f015e5f28;RU)(OA;CIIO;RP;037088f8-0ae1-11d2-b422-00a0c968f939;bf967aba-0de6-11d0-a285-00aa003049e2;RU)" +
        "(OA;CIIO;RP;b7c69e6d-2cc7-11d2-854e-00a0c983f608;bf967a86-0de6-11d0-a285-00aa003049e2;ED)(OA;CIIO;RP;b7c69e6d-2cc7-11d2-854e-00a0c983f608;bf967a9c-0de6-11d0-a285-00aa003049e2;ED)(OA;CIIO;RP;b7c69e6d-2cc7-11d2-854e-00a0c983f608;bf967aba-0de6-11d0-a285-00aa003049e2;ED)" +
        "(OA;CIIO;LCRPLORC;;4828cc14-1437-45bc-9b07-ad6f015e5f28;RU)(OA;CIIO;LCRPLORC;;bf967a9c-0de6-11d0-a285-00aa003049e2;RU)(OA;CIIO;LCRPLORC;;bf967aba-0de6-11d0-a285-00aa003049e2;RU)" +
        "(OA;CIIO;RP;46a9b11d-60ae-405a-b7e8-ff8a58d456d2;;S-1-5-32-560)(OA;CIIO;RPWP;5b47d60f-6090-40b2-9f37-2a4de88f3063;;CO)" +
        "(OA;CI;RPWP;5b47d60f-6090-40b2-9f37-2a4de88f3063;;KA)(OA;CI;RPWP;5b47d60f-6090-40b2-9f37-2a4de88f3063;;EK)" +
        "(OA;CIIO;SW;9b026da6-0d3c-465c-8bee-5199d7165cba;bf967a86-0de6-11d0-a285-00aa003049e2;CO)(OA;CIIO;SW;9b026da6-0d3c-465c-8bee-5199d7165cba;bf967a86-0de6-11d0-a285-00aa003049e2;PS)" +
        "(OA;CIIO;WP;ea1b7b93-5e48-46d5-bc6c-4df4fda78a35;bf967a86-0de6-11d0-a285-00aa003049e2;PS)" +
        "(OA;CIIO;RPWP;3f78c3e5-f79a-46bd-a0b8-9d18116ddc79;bf967a86-0de6-11d0-a285-00aa003049e2;PS)" +
        "(OA;CIIO;CR;ab721a53-1e2f-11d0-9819-00aa0040529b;bf967a86-0de6-11d0-a285-00aa003049e2;PS)" +
        "(OA;CIIO;CRRPWP;91e647de-d96f-4b70-9557-d63ff4f3ccd8;;PS)(OA;CIIO;CR;ab721a53-1e2f-11d0-9819-00aa0040529b;;PS)" +
        "(OA;CIIO;RPWPCR;91e647de-d96f-4b70-9557-d63ff4f3ccd8;;PS)" +
        "(OA;CIIO;WP;f3a64788-5306-11d1-a9c5-0000f80367c1;bf967a86-0de6-11d0-a285-00aa003049e2;PS)" +
        "(OA;CIIO;WP;72e39547-7b18-11d1-adef-00c04fd8d5cd;bf967a86-0de6-11d0-a285-00aa003049e2;PS)" +
        "(OA;CIID;RP;46a9b11d-60ae-405a-b7e8-ff8a58d456d2;;S-1-5-32-560)";

    /// <summary>Returns the extra install-time default ACE keys for <paramref name="objectClass"/>.</summary>
    public static HashSet<string> InstallDefaultKeys(string? objectClass) =>
        string.Equals(objectClass, "domainDNS", StringComparison.OrdinalIgnoreCase)
            ? ParseKeys(DomainRootInstallDefaults)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the set of ACE keys present in a default security descriptor (SDDL).</summary>
    public static HashSet<string> ParseKeys(string? sddl)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sddl)) return keys;

        var dacl = sddl.IndexOf("D:", StringComparison.Ordinal);
        if (dacl < 0) return keys;
        var end = sddl.IndexOf("S:", dacl, StringComparison.Ordinal);
        var body = end < 0 ? sddl[dacl..] : sddl[dacl..end];

        var pos = 0;
        while ((pos = body.IndexOf('(', pos)) >= 0)
        {
            var close = body.IndexOf(')', pos);
            if (close < 0) break;
            var parts = body[(pos + 1)..close].Split(';');
            pos = close + 1;
            if (parts.Length < 6) continue;

            var isAllow = parts[0] is "A" or "OA";
            if (!isAllow && parts[0] is not ("D" or "OD")) continue;
            var mask = ParseMask(parts[2]);
            if (mask == null) continue;
            keys.Add(Key(isAllow, mask.Value, Guid(parts[3]), Guid(parts[4]), TrusteeKey(parts[5])));
        }
        return keys;
    }

    /// <summary>Marks the non-inherited entries that match <paramref name="defaultKeys"/> as defaults.</summary>
    public static IReadOnlyList<AdPermissionEntry> Mark(IReadOnlyList<AdPermissionEntry> entries, HashSet<string>? defaultKeys)
    {
        if (defaultKeys is not { Count: > 0 }) return entries;
        return entries.Select(e => !e.IsInherited && defaultKeys.Contains(KeyOf(e)) ? e with { IsDefault = true } : e).ToList();
    }

    public static string KeyOf(AdPermissionEntry e) =>
        Key(e.IsAllow, e.AccessMask, e.ObjectType, e.InheritedObjectType, TrusteeKey(e.TrusteeSid));

    private static string Key(bool allow, int mask, Guid? objectType, Guid? inheritedType, string trustee) =>
        $"{(allow ? 'A' : 'D')}|{Normalize(mask):X}|{objectType}|{inheritedType}|{trustee}";

    /// <summary>Maps generic rights to their AD-specific equivalents, as AD does when storing ACEs.</summary>
    private static int Normalize(int mask)
    {
        var m = mask & 0x0FFFFFFF;
        if ((mask & 0x10000000) != 0) m |= 0xF01FF;
        if ((mask & unchecked((int)0x80000000)) != 0) m |= 0x20094;
        if ((mask & 0x40000000) != 0) m |= 0x20028;
        if ((mask & 0x20000000) != 0) m |= 0x20004;
        return m;
    }

    private static int? ParseMask(string rights)
    {
        if (rights.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(rights[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        if (rights.Length % 2 != 0) return null;
        var mask = 0;
        for (var i = 0; i < rights.Length; i += 2)
        {
            if (!RightCodes.TryGetValue(rights.Substring(i, 2), out var bit)) return null;
            mask |= bit;
        }
        return mask;
    }

    private static Guid? Guid(string value) => System.Guid.TryParse(value, out var g) ? g : null;

    /// <summary>Domain SIDs compare by RID only so defaults match in every domain of the forest.</summary>
    private static string TrusteeKey(string sid)
    {
        if (DomainRids.TryGetValue(sid, out var rid)) return "RID:" + rid;
        if (WellKnownSids.TryGetValue(sid, out var wk)) return wk;
        if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && sid.Count(c => c == '-') == 7)
            return "RID:" + sid[(sid.LastIndexOf('-') + 1)..];
        return sid.ToUpperInvariant();
    }
}
