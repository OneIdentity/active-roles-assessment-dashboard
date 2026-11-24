namespace ActiveRolesDashboard.Services;

/// <summary>
/// Source-agnostic analysis of a group membership graph (Active Directory or Entra ID).
/// Input is each group's id with its direct member ids; members that are not themselves keys of
/// the graph are leaves (users, computers, contacts, service principals...).
///
/// Produces, in a single pass over the graph:
///   - groups lying on a membership cycle (exact, Tarjan strongly connected components);
///   - nesting depth of every group: the longest chain of group-in-group edges below it, where a
///     cycle counts as a single level so depth is always finite;
///   - optionally, multi-path memberships: leaf members reachable in a group through two or more
///     of its direct members (including direct + indirect).
///
/// Cost: SCC and depth are linear in groups + edges. Multi-path is proportional to the sum of
/// reachable-leaf set sizes and is skipped when the graph exceeds <see cref="Options.MultiPathEdgeLimit"/>.
/// </summary>
public static class GroupNestingAnalyzer
{
    public sealed class Options
    {
        /// <summary>
        /// Whether a group may contain groups. Outgoing group edges of a group for which this
        /// returns false are ignored (e.g. Entra Microsoft 365, dynamic or role-assignable groups).
        /// Leaf members are kept. Default: every group can nest.
        /// </summary>
        public Func<string, bool>? CanNest { get; init; }

        /// <summary>
        /// Whether a group is reported in the results (e.g. cloud-only Entra groups). Groups that
        /// are not reported are still traversed as children. Default: every group is reported.
        /// </summary>
        public Func<string, bool>? IsReported { get; init; }

        /// <summary>Compute multi-path memberships. Default true.</summary>
        public bool ComputeMultiPath { get; init; } = true;

        /// <summary>
        /// Maximum number of direct membership edges for which multi-path is computed; above it
        /// <see cref="Result.MultiPathSkipped"/> is set instead. Default 2,000,000.
        /// </summary>
        public long MultiPathEdgeLimit { get; init; } = 2_000_000;

        /// <summary>
        /// Maximum total of reachable-leaf entries (summed over components and per-group counts) built
        /// while computing multi-path. Bounds memory independently of edge count (deep chains and dense
        /// DAGs grow quadratically); above it <see cref="Result.MultiPathSkipped"/> is set. Default 10,000,000.
        /// </summary>
        public long MultiPathWorkLimit { get; init; } = 10_000_000;
    }

    /// <summary>A leaf member reachable in a group through more than one of the group's direct members.</summary>
    public sealed record MultiPathMembership(string GroupId, string MemberId, int PathCount);

    public sealed class Result
    {
        /// <summary>Reported groups that lie on at least one membership cycle.</summary>
        public HashSet<string> CircularGroups { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Nesting depth of each reported group (0 = no nested groups).</summary>
        public Dictionary<string, int> DepthByGroup { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Reported groups whose nesting reaches a cycle (depth counts the cycle as one level).</summary>
        public HashSet<string> ReachesCycle { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Multi-path memberships of reported groups; empty when skipped or disabled.</summary>
        public List<MultiPathMembership> MultiPath { get; } = [];

        /// <summary>True when multi-path was not computed because the graph exceeded the edge limit.</summary>
        public bool MultiPathSkipped { get; set; }

        public long EdgeCount { get; set; }

        /// <summary>Reported groups with depth greater than <paramref name="maxDepth"/>, excluding circular groups.</summary>
        public IEnumerable<string> GroupsDeeperThan(int maxDepth) =>
            DepthByGroup.Where(kv => kv.Value > maxDepth && !CircularGroups.Contains(kv.Key)).Select(kv => kv.Key);
    }

    public static Result Analyze(IReadOnlyDictionary<string, List<string>> directMembersByGroup, Options? options = null)
    {
        options ??= new Options();
        var result = new Result();

        // Index groups; split each group's direct members into nested-group edges and leaves.
        var ids = new List<string>(directMembersByGroup.Count);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in directMembersByGroup.Keys)
        {
            if (!string.IsNullOrEmpty(id) && index.TryAdd(id, ids.Count)) ids.Add(id);
        }

        var n = ids.Count;
        var groupEdges = new List<int>[n];
        var leaves = new List<string>[n];
        long edgeCount = 0;
        for (var i = 0; i < n; i++)
        {
            var canNest = options.CanNest?.Invoke(ids[i]) ?? true;
            var edges = new List<int>();
            var leafList = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in directMembersByGroup[ids[i]])
            {
                if (string.IsNullOrEmpty(m) || !seen.Add(m)) continue;
                edgeCount++;
                if (index.TryGetValue(m, out var child))
                {
                    if (canNest) edges.Add(child);
                }
                else
                {
                    leafList.Add(m);
                }
            }
            groupEdges[i] = edges;
            leaves[i] = leafList;
        }
        result.EdgeCount = edgeCount;

        var (comp, compCount, compSize, selfLoop) = Tarjan(n, groupEdges);

        // Tarjan numbers components in reverse topological order: every edge u->v has comp[u] >= comp[v].
        // Build condensation successor lists and process components from 0 upwards (sinks first).
        var compMembers = new List<int>[compCount];
        for (var c = 0; c < compCount; c++) compMembers[c] = [];
        for (var i = 0; i < n; i++) compMembers[comp[i]].Add(i);

        var compSucc = new HashSet<int>[compCount];
        for (var c = 0; c < compCount; c++)
        {
            compSucc[c] = [];
            foreach (var u in compMembers[c])
                foreach (var v in groupEdges[u])
                    if (comp[v] != c) compSucc[c].Add(comp[v]);
        }

        var cyclic = new bool[compCount];
        for (var c = 0; c < compCount; c++)
            cyclic[c] = compSize[c] > 1 || selfLoop[compMembers[c][0]];

        var compDepth = new int[compCount];
        var compReachesCycle = new bool[compCount];
        for (var c = 0; c < compCount; c++)
        {
            var depth = 0;
            var reaches = false;
            foreach (var s in compSucc[c])
            {
                depth = Math.Max(depth, compDepth[s] + 1);
                reaches |= cyclic[s] || compReachesCycle[s];
            }
            // A cycle contains nested groups, so it is at least one level deep.
            if (cyclic[c]) depth = Math.Max(depth, 1);
            compDepth[c] = depth;
            compReachesCycle[c] = reaches;
        }

        for (var i = 0; i < n; i++)
        {
            if (!(options.IsReported?.Invoke(ids[i]) ?? true)) continue;
            var c = comp[i];
            result.DepthByGroup[ids[i]] = compDepth[c];
            if (cyclic[c]) result.CircularGroups.Add(ids[i]);
            else if (compReachesCycle[c]) result.ReachesCycle.Add(ids[i]);
        }

        if (!options.ComputeMultiPath) return result;
        if (edgeCount > options.MultiPathEdgeLimit)
        {
            result.MultiPathSkipped = true;
            return result;
        }

        // Reachable leaves per component (memoized; shared by every group of the component).
        var compLeaves = new HashSet<string>[compCount];
        long work = 0;
        for (var c = 0; c < compCount; c++)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in compMembers[c]) set.UnionWith(leaves[u]);
            foreach (var s in compSucc[c]) set.UnionWith(compLeaves[s]);
            compLeaves[c] = set;
            work += set.Count;
            if (work > options.MultiPathWorkLimit)
            {
                result.MultiPathSkipped = true;
                return result;
            }
        }

        for (var i = 0; i < n; i++)
        {
            if (!(options.IsReported?.Invoke(ids[i]) ?? true) || groupEdges[i].Count == 0) continue;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var leaf in leaves[i]) counts[leaf] = 1;
            foreach (var child in groupEdges[i])
            {
                if (child == i) continue;
                work += compLeaves[comp[child]].Count;
                if (work > options.MultiPathWorkLimit)
                {
                    result.MultiPath.Clear();
                    result.MultiPathSkipped = true;
                    return result;
                }
                foreach (var leaf in compLeaves[comp[child]])
                    counts[leaf] = counts.GetValueOrDefault(leaf) + 1;
            }
            foreach (var (leaf, count) in counts)
                if (count > 1) result.MultiPath.Add(new MultiPathMembership(ids[i], leaf, count));
        }

        return result;
    }

    /// <summary>Iterative Tarjan SCC. Returns component per node, component count, sizes and self-loop flags.</summary>
    private static (int[] Comp, int Count, int[] Size, bool[] SelfLoop) Tarjan(int n, List<int>[] edges)
    {
        var idx = new int[n];
        var low = new int[n];
        var onStack = new bool[n];
        var comp = new int[n];
        var selfLoop = new bool[n];
        Array.Fill(idx, -1);
        var sizes = new List<int>();
        var stack = new Stack<int>();
        var call = new Stack<(int Node, int Edge)>();
        var counter = 0;

        for (var root = 0; root < n; root++)
        {
            if (idx[root] != -1) continue;
            call.Push((root, 0));
            idx[root] = low[root] = counter++;
            stack.Push(root);
            onStack[root] = true;

            while (call.Count > 0)
            {
                var (v, e) = call.Pop();
                if (e < edges[v].Count)
                {
                    call.Push((v, e + 1));
                    var w = edges[v][e];
                    if (w == v) selfLoop[v] = true;
                    if (idx[w] == -1)
                    {
                        idx[w] = low[w] = counter++;
                        stack.Push(w);
                        onStack[w] = true;
                        call.Push((w, 0));
                    }
                    else if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], idx[w]);
                    }
                    continue;
                }

                if (low[v] == idx[v])
                {
                    var size = 0;
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack[w] = false;
                        comp[w] = sizes.Count;
                        size++;
                    } while (w != v);
                    sizes.Add(size);
                }

                if (call.Count > 0)
                {
                    var parent = call.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[v]);
                }
            }
        }

        return (comp, sizes.Count, sizes.ToArray(), selfLoop);
    }
}
