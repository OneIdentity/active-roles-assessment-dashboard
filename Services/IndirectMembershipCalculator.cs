using System.Text.Json;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Calculates transitive (indirect) group membership — Active Roles' <c>edsaMemberIndirect</c>
/// virtual attribute — locally from native directory data, so the dashboard no longer has to
/// request the expensive (and, at scale, failure-prone) <c>edsaMember</c> / <c>edsaMemberIndirect</c>
/// virtual attributes from the Active Roles REST API.
///
/// Given the full set of AD group records (each carrying its direct <c>member</c> DNs), it computes
/// for every group the set of member DNs reachable through nested groups (its indirect members),
/// reproducing Active Roles' semantics exactly:
///
///   indirect(G) = (union of reachable(m) for each direct group-member m of G) - directMembers(G)
///
/// Subtracting the group's DIRECT members (rather than only the group itself) reproduces AR for
/// BOTH acyclic and cyclic graphs, INCLUDING the case where AR counts a group as its own indirect
/// member when a cycle loops back to it:
///   - Non-cyclic A -> B -> C(user): reachable(B) = {C}; indirect(A) = {C} - {B} = {C}.
///   - Cyclic A -> B -> C -> A: reachable(B) = {A, B, C}; indirect(A) = {A, B, C} - {B} = {A, C}.
///
/// This algorithm and its cycle semantics were validated offline (unit self-test) and against a
/// live Active Roles directory (sampled edsaMemberIndirect ground truth), where the native
/// <c>member</c>-sourced calculation matched AR for all normal nesting and cycles.
///
/// The reachable-member set of a group is independent of how it was entered, so it is memoized and
/// reused across every group — essential for deep, wide customer nesting.
/// </summary>
public static class IndirectMembershipCalculator
{
    /// <summary>Native direct-membership source attribute.</summary>
    public const string DefaultMemberAttr = "member";

    /// <summary>
    /// Direct and indirect membership computed for each group, keyed by group DN
    /// (case-insensitive). Direct = the group's own <c>member</c> DNs; Indirect = DNs reachable
    /// only through nested groups (with AR cycle semantics).
    /// </summary>
    public sealed class Result
    {
        public Dictionary<string, List<string>> DirectByDn { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> IndirectByDn { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes direct and indirect membership for every group in <paramref name="groups"/>.
    /// <paramref name="dnOf"/> extracts a group's DN; <paramref name="membersOf"/> extracts its
    /// direct member DNs (from the <paramref name="memberAttr"/> attribute).
    /// </summary>
    public static Result Compute(
        IReadOnlyList<JsonElement> groups,
        Func<JsonElement, string> dnOf,
        Func<JsonElement, string, List<string>> membersOf,
        string memberAttr = DefaultMemberAttr)
    {
        var result = new Result();

        // Index each group's direct members by DN for graph traversal.
        var byDn = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var dn = dnOf(g);
            if (string.IsNullOrEmpty(dn) || byDn.ContainsKey(dn)) continue;
            var members = membersOf(g, memberAttr);
            byDn[dn] = members;
            result.DirectByDn[dn] = members;
        }

        // Entry-point-independent reachable-member cache (safe to reuse across all groups).
        var reachableCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in byDn)
        {
            var groupDn = kvp.Key;
            var directMembers = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);

            var indirectSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var memberDn in kvp.Value)
            {
                if (!byDn.ContainsKey(memberDn)) continue; // only nested groups expand
                indirectSet.UnionWith(ReachableMembers(memberDn, byDn, reachableCache, memberAttr));
            }

            // Exclude the group's own DIRECT members; anything left is reached via >= 2 hops
            // (a group re-appears here only when a cycle loops back to it — matching AR).
            indirectSet.ExceptWith(directMembers);

            result.IndirectByDn[groupDn] = indirectSet.ToList();
        }

        return result;
    }

    /// <summary>
    /// Returns the full set of member DNs reachable by expanding <paramref name="startGroupDn"/>'s
    /// members transitively through nested groups. Includes any group DN reachable through a cycle
    /// back to the start. Entry-point independent, so it is memoized in <paramref name="cache"/>.
    /// Iterative (worklist + global visited set) so cycles terminate cleanly.
    /// </summary>
    private static HashSet<string> ReachableMembers(
        string startGroupDn,
        Dictionary<string, List<string>> byDn,
        Dictionary<string, HashSet<string>> cache,
        string memberAttr)
    {
        if (cache.TryGetValue(startGroupDn, out var cached))
            return cached;

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expandedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(startGroupDn);

        while (queue.Count > 0)
        {
            var currentGroupDn = queue.Dequeue();
            if (!expandedGroups.Add(currentGroupDn))
                continue; // already expanded (cycle / shared-parent guard)

            // Fold in a complete cached reachable set for a nested group and skip re-expansion.
            if (!ReferenceEquals(currentGroupDn, startGroupDn) &&
                cache.TryGetValue(currentGroupDn, out var cachedNested))
            {
                reachable.UnionWith(cachedNested);
                foreach (var dn in cachedNested)
                    if (byDn.ContainsKey(dn))
                        expandedGroups.Add(dn);
                continue;
            }

            if (!byDn.TryGetValue(currentGroupDn, out var members))
                continue;

            foreach (var memberDn in members)
            {
                reachable.Add(memberDn);
                if (byDn.ContainsKey(memberDn) && !expandedGroups.Contains(memberDn))
                    queue.Enqueue(memberDn);
            }
        }

        cache[startGroupDn] = reachable;
        return reachable;
    }
}
