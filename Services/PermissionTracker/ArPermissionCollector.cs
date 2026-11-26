using System.Security.AccessControl;

namespace ActiveRolesDashboard.Services.PermissionTracker;

/// <summary>A raw Access Template Link: the secured object, its trustees and the template's edsaATEList tokens.</summary>
public sealed record ArTemplateLinkRecord(
    string LinkName,
    string SecuredObjectDn,
    string AccessTemplateDn,
    string AccessTemplateName,
    IReadOnlyList<string> TrusteeSids,
    IReadOnlyList<string> AceTokens);

/// <summary>One Active Roles delegated permission: a trustee granted (or denied) rights on an object via an Access Template.</summary>
public sealed record ArPermissionEntry(
    string SecuredObjectDn,
    string TrusteeSid,
    string AccessTemplateName,
    string AccessTemplateDn,
    bool IsAllow,
    string Rights,
    string? ObjectTypeName,
    string? InheritedObjectTypeName,
    string AppliesTo,
    bool IsHighRisk);

/// <summary>
/// Expands Access Template Links into per-trustee, per-ACE <see cref="ArPermissionEntry"/> rows.
/// edsaATEList tokens use SDDL ACE syntax (<c>A;CI;RPWP;&lt;objectType&gt;;&lt;inheritedType&gt;</c>), which
/// is parsed by building a one-ACE SDDL string for the trustee and reusing <see cref="AdAceParser"/>.
/// </summary>
public static class ArPermissionCollector
{
    public static IReadOnlyList<ArPermissionEntry> Expand(IEnumerable<ArTemplateLinkRecord> links)
    {
        var result = new List<ArPermissionEntry>();
        foreach (var link in links)
        {
            foreach (var token in link.AceTokens)
            {
                var ace = ParseToken(token);
                if (ace == null) continue;
                foreach (var sid in link.TrusteeSids)
                {
                    result.Add(new ArPermissionEntry(
                        link.SecuredObjectDn,
                        sid,
                        link.AccessTemplateName,
                        link.AccessTemplateDn,
                        ace.Value.IsAllow,
                        AdAceParser.DescribeRights(ace.Value.Mask),
                        AdAceParser.NameOf(ace.Value.ObjectType),
                        AdAceParser.NameOf(ace.Value.InheritedType),
                        AdAceParser.DescribeAppliesTo(ace.Value.Flags, ace.Value.InheritedType),
                        ace.Value.IsAllow && AdAceParser.IsDangerous(ace.Value.Mask, ace.Value.ObjectType)));
                }
            }
        }
        return result;
    }

    /// <summary>Parses a single edsaATEList token (without brackets). Returns null when it is not an allow/deny ACE.</summary>
    public static (bool IsAllow, int Mask, AceFlags Flags, Guid? ObjectType, Guid? InheritedType)? ParseToken(string token)
    {
        var fields = token.Trim().Trim('[', ']').Split(';');
        if (fields.Length < 3) return null;

        var type = fields[0].Trim().ToUpperInvariant();
        if (type is not ("A" or "D" or "OA" or "OD")) return null;

        Guid? objectType = fields.Length > 3 && Guid.TryParse(fields[3].Trim(), out var o) ? o : null;
        Guid? inheritedType = fields.Length > 4 && Guid.TryParse(fields[4].Trim(), out var i) ? i : null;
        var aceType = objectType != null || inheritedType != null
            ? (type.EndsWith('A') ? "OA" : "OD")
            : (type.EndsWith('A') ? "A" : "D");

        try
        {
            // WD (Everyone) is a placeholder trustee; only the mask/flags/types are used.
            var sddl = $"D:({aceType};{fields[1].Trim()};{fields[2].Trim()};{objectType};{inheritedType};WD)";
            var sd = new RawSecurityDescriptor(sddl);
            if (sd.DiscretionaryAcl is not { Count: 1 } acl || acl[0] is not KnownAce known) return null;
            return (type.EndsWith('A'), known.AccessMask, known.AceFlags, objectType, inheritedType);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
