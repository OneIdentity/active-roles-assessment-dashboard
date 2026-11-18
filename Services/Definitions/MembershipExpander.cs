using System.Text.Json;
using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services.Definitions;

/// <summary>
/// Turns the group objects of a fixed-object dataset into one item per member, matching the
/// legacy privileged group collection: direct members from <c>member</c>, then indirect members
/// from the locally computed transitive closure, skipping members already listed.
/// </summary>
public static class MembershipExpander
{
    /// <param name="groups">Group objects returned by the dataset search.</param>
    /// <param name="indirectMembers">Indirect member DNs of a group DN, or null when unknown.</param>
    /// <param name="lookup">Raw directory object for a member DN, used to copy permission-scope attributes.</param>
    public static List<JsonElement> Expand(
        IEnumerable<JsonElement> groups,
        Func<string, IReadOnlyCollection<string>?>? indirectMembers = null,
        Func<string, JsonElement?>? lookup = null)
    {
        var items = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var groupDn = Values(group, "distinguishedName").FirstOrDefault() ?? string.Empty;
            var domain = Values(group, "edsaDomainNetbiosName").FirstOrDefault() ?? string.Empty;

            foreach (var dn in Values(group, "member"))
            {
                seen.Add(dn);
                items.Add(Member(dn, domain, groupDn, "Direct", lookup));
            }

            if (groupDn.Length == 0 || indirectMembers?.Invoke(groupDn) is not { } indirect) continue;
            foreach (var dn in indirect)
            {
                if (seen.Add(dn))
                    items.Add(Member(dn, domain, groupDn, "Indirect", lookup));
            }
        }

        return items;
    }

    private static JsonElement Member(string dn, string domain, string groupDn, string membershipType, Func<string, JsonElement?>? lookup)
    {
        var item = new Dictionary<string, object?>
        {
            ["name"] = CommonName(dn),
            ["distinguishedName"] = dn,
            ["domain"] = domain,
            ["membershipType"] = membershipType,
            ["groupDN"] = groupDn,
        };

        if (lookup?.Invoke(dn) is { } source)
        {
            foreach (var attribute in new[] { SegmentAttributes.EffectiveLinksAttribute, SegmentAttributes.ClassAttribute })
            {
                if (TryGet(source, attribute, out var value))
                    item[attribute] = value.Clone();
            }
        }

        return JsonSerializer.SerializeToElement(item);
    }

    /// <summary>First RDN value of a DN, honoring escaped commas.</summary>
    public static string CommonName(string dn)
    {
        var start = dn.IndexOf('=') + 1;
        if (start <= 0) return dn;
        for (var i = start; i < dn.Length; i++)
        {
            if (dn[i] == '\\') { i++; continue; }
            if (dn[i] == ',') return dn[start..i].Replace("\\", string.Empty);
        }

        return dn[start..].Replace("\\", string.Empty);
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && (element.TryGetProperty(name, out value)
                || (element.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty(name, out value)));
    }

    private static IEnumerable<string> Values(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value)) yield break;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in value.EnumerateArray())
                if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s) yield return s;
        }
        else if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s)
        {
            yield return s;
        }
    }
}
