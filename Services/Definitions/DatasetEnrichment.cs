using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ActiveRolesDashboard.Models.Definitions;
using Evaluation = ActiveRolesDashboard.Models.Definitions.Evaluation;

namespace ActiveRolesDashboard.Services.Definitions;

/// <summary>Directory lookups that dataset enrichments may perform.</summary>
public interface IDirectoryLookup
{
    /// <summary>True when an object with the GUID resolves.</summary>
    Task<bool> GuidExistsAsync(string guid);

    /// <summary>Display name of a SID (base64 or string form); empty when unresolved.</summary>
    Task<string> ResolveSidAsync(string sid);

    /// <summary>True when the SID belongs to a user account (not a computer or group).</summary>
    Task<bool> IsUserSidAsync(string sid);

    /// <summary>True when the GUID is a domain root or the Active Directory root container.</summary>
    Task<bool> IsRootScopeAsync(string guid);

    /// <summary>Name of the object with the GUID; "Unknown" when it does not resolve.</summary>
    Task<string> ResolveGuidNameAsync(string guid);

    /// <summary>Number of objects beneath <paramref name="dn"/>, excluding the object itself.</summary>
    Task<int> CountDescendantsAsync(string dn);

    /// <summary>
    /// SIDs explicitly granted <paramref name="right"/> (SendAs or FullAccess) on the mailbox,
    /// excluding system principals and the owner; null when the descriptor cannot be read.
    /// </summary>
    Task<IReadOnlyList<string>?> MailboxGranteesAsync(string objectGuid, string? ownerSid, string right);
}

/// <summary>
/// Applies a dataset's declared <see cref="EnrichmentDefinition"/>s to its items. Each item is
/// copied with extra top-level attributes; lookups are cached per enrichment run.
/// </summary>
public static class DatasetEnricher
{
    private static readonly Regex GuidToken =
        new(@"[{]?[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}[}]?", RegexOptions.Compiled);

    public static async Task<List<JsonElement>> EnrichAsync(DatasetDefinition dataset, List<JsonElement> items, IDirectoryLookup lookup)
    {
        foreach (var enrichment in dataset.Enrichments)
        {
            var step = Create(enrichment, lookup);
            var result = new List<JsonElement>(items.Count);
            foreach (var item in items)
            {
                var node = JsonNode.Parse(item.GetRawText()) as JsonObject ?? [];
                await step(item, node);
                result.Add(JsonSerializer.SerializeToElement(node));
            }

            items = result;
        }

        return items;
    }

    private static Func<JsonElement, JsonObject, Task> Create(EnrichmentDefinition e, IDirectoryLookup lookup)
    {
        string Arg(string name, string fallback = "") =>
            e.Args.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        switch (e.Name.ToLowerInvariant())
        {
            case "unresolvedguids":
            {
                var attribute = Arg("attribute");
                var cache = new Cache<bool>();
                return async (item, node) =>
                {
                    var missing = new JsonArray();
                    foreach (var text in Evaluation.ExpressionEvaluator.ReadAttribute(item, attribute) is { } v ? Values(v) : [])
                    {
                        foreach (Match m in GuidToken.Matches(text))
                        {
                            if (!Guid.TryParse(m.Value, out var g)) continue;
                            var guid = g.ToString();
                            if (!await cache.GetAsync(guid, lookup.GuidExistsAsync)) missing.Add(guid);
                        }
                    }

                    node["unresolvedGuids"] = missing;
                };
            }
            case "trustee":
            {
                var attribute = Arg("attribute", "edsaTrusteeSID");
                var names = new Cache<string>();
                var users = new Cache<bool>();
                return async (item, node) =>
                {
                    var sid = First(item, attribute);
                    node["trustee"] = sid is null ? "" : await names.GetAsync(sid, lookup.ResolveSidAsync);
                    node["trusteeIsUser"] = sid is not null && await users.GetAsync(sid, lookup.IsUserSidAsync);
                };
            }
            case "rootscope":
            {
                var attribute = Arg("attribute", "edsaSecObjectGUID");
                var cache = new Cache<bool>();
                return async (item, node) =>
                {
                    var guid = First(item, attribute);
                    node["isRootScope"] = guid is not null && await cache.GetAsync(guid, lookup.IsRootScopeAsync);
                };
            }
            case "objectname":
            {
                var attribute = Arg("attribute");
                var target = Arg("as", attribute + "Name");
                var cache = new Cache<string>();
                return async (item, node) =>
                {
                    var guid = First(item, attribute);
                    node[target] = guid is null ? "" : await cache.GetAsync(guid, lookup.ResolveGuidNameAsync);
                };
            }
            case "descendantcount":
                return async (item, node) =>
                {
                    var dn = First(item, "distinguishedName");
                    if (dn is null) { node["objectCount"] = null; node["note"] = "Tenant distinguished name unavailable; object count skipped."; return; }
                    try { node["objectCount"] = await lookup.CountDescendantsAsync(dn); }
                    catch (Exception ex) { node["objectCount"] = null; node["note"] = $"Object count unavailable ({ex.GetType().Name})."; }
                };
            case "mailboxpermissions":
            {
                var right = Arg("right", "SendAs");
                var names = new Cache<string>();
                return async (item, node) =>
                {
                    var trustees = new JsonArray();
                    var guid = First(item, "objectGUID");
                    var sids = guid is null ? null : await lookup.MailboxGranteesAsync(guid, First(item, "objectSid"), right);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var sid in sids ?? [])
                    {
                        var name = await names.GetAsync(sid, lookup.ResolveSidAsync);
                        var display = string.IsNullOrEmpty(name) ? sid : name;
                        if (seen.Add(display)) trustees.Add(display);
                    }

                    node["trustees"] = trustees;
                    node["trustee"] = string.Join(", ", seen);
                };
            }
            default:
                throw new InvalidOperationException($"Unknown enrichment '{e.Name}'.");
        }
    }

    private static string? First(JsonElement item, string attribute) =>
        Evaluation.ExpressionEvaluator.ReadAttribute(item, attribute) is { } v ? Values(v).FirstOrDefault(s => s.Length > 0) : null;

    private static IEnumerable<string> Values(object value) => value switch
    {
        string s => [s],
        IEnumerable<string> l => l,
        _ => [Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""]
    };

    private sealed class Cache<T>
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<T>>> _items = new(StringComparer.OrdinalIgnoreCase);
        public Task<T> GetAsync(string key, Func<string, Task<T>> load) => _items.GetOrAdd(key, k => new Lazy<Task<T>>(() => load(k))).Value;
    }
}
