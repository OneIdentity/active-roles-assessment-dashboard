using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;

namespace ActiveRolesDashboard.Services.Definitions;

/// <summary>Comparison of a generically collected dataset with the legacy collection.</summary>
public sealed record DatasetParityResult(string DatasetId, int Legacy, int Generic, int MissingInGeneric, int ExtraInGeneric, string? Error)
{
    public bool Matches => Error is null && Legacy == Generic && MissingInGeneric == 0 && ExtraInGeneric == 0;
}

/// <summary>
/// Compares datasets collected from the definitions with the items gathered by the hand-written
/// collection code, matching items by distinguished name.
/// </summary>
public static class DatasetParity
{
    /// <summary>Legacy items for the datasets currently collected by hand-written code.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> LegacyDatasets(DashboardSummary summary)
    {
        var map = new Dictionary<string, IReadOnlyList<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        if (summary.ADUserAccounts.Error is null && summary.ADUserAccounts.Items.Count > 0)
            map["ADUserAccounts"] = summary.ADUserAccounts.Items;
        if (summary.ADGroups.Error is null && summary.ADGroups.Items.Count > 0)
            map["ADGroups"] = summary.ADGroups.Items;
        if (summary.Computers.Error is null && summary.Computers.Items.Count > 0)
            map["Computers"] = summary.Computers.Items;
        return map;
    }

    public static IReadOnlyList<DatasetParityResult> Compare(
        IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> legacy,
        IReadOnlyDictionary<string, DatasetResult> generic)
    {
        var results = new List<DatasetParityResult>();
        foreach (var (id, legacyItems) in legacy)
        {
            if (!generic.TryGetValue(id, out var collected)) continue;
            if (collected.Error is not null)
            {
                results.Add(new DatasetParityResult(id, legacyItems.Count, 0, 0, 0, collected.Error));
                continue;
            }

            var legacyKeys = Keys(legacyItems);
            var genericKeys = Keys(collected.Items);
            results.Add(new DatasetParityResult(id, legacyItems.Count, collected.Items.Count,
                legacyKeys.Count(k => !genericKeys.Contains(k)),
                genericKeys.Count(k => !legacyKeys.Contains(k)),
                null));
        }

        return results;
    }

    private static HashSet<string> Keys(IEnumerable<JsonElement> items)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("distinguishedName", out var dn)
                && dn.ValueKind == JsonValueKind.String
                && dn.GetString() is { Length: > 0 } value)
            {
                keys.Add(value);
            }
        }

        return keys;
    }
}
