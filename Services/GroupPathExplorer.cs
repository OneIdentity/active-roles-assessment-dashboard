namespace ActiveRolesDashboard.Services;

/// <summary>
/// On-demand path enumeration for the Group Analyzer tool. Works on a group-to-group edge map
/// (parent DN -> nested group DNs) and lists the concrete nesting paths below and above a group.
/// Enumeration is capped (paths and depth) so very large or cyclic graphs stay bounded.
/// </summary>
public static class GroupPathExplorer
{
    public sealed record PathSet(List<List<string>> Paths, bool Truncated);

    public sealed class Report
    {
        public required string GroupDn { get; init; }
        public int Depth { get; init; }
        public bool IsCircular { get; init; }

        /// <summary>Nesting paths from the group down to each leaf group (root first).</summary>
        public required PathSet Descendants { get; init; }

        /// <summary>Paths from each top-level parent down to the group (top-level parent first).</summary>
        public required PathSet Ancestors { get; init; }

        /// <summary>Groups on the same membership cycle as the selected group (excluding itself).</summary>
        public List<string> CyclePeers { get; init; } = [];

        /// <summary>Nested groups reached from the selected group through more than one path, with path counts.</summary>
        public List<(string GroupDn, long PathCount)> MultiPathGroups { get; init; } = [];
    }

    /// <summary>
    /// Restricts <paramref name="edges"/> to groups in <paramref name="visible"/> (both ends), so the
    /// tool never reveals groups outside the caller's visibility scope.
    /// </summary>
    public static Dictionary<string, List<string>> Restrict(
        IReadOnlyDictionary<string, List<string>> edges, ISet<string> visible)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (parent, children) in edges)
        {
            if (!visible.Contains(parent)) continue;
            result[parent] = children.Where(visible.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        return result;
    }

    public static Report Analyze(
        IReadOnlyDictionary<string, List<string>> edges, string groupDn, int maxPaths = 200, int maxDepth = 50)
    {
        var reverse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (parent, children) in edges)
            foreach (var child in children)
            {
                if (!reverse.TryGetValue(child, out var list)) reverse[child] = list = [];
                list.Add(parent);
            }

        var nesting = GroupNestingAnalyzer.Analyze(
            edges.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase),
            new GroupNestingAnalyzer.Options { ComputeMultiPath = false });

        var down = Enumerate(edges, groupDn, maxPaths, maxDepth);
        var up = Enumerate(reverse, groupDn, maxPaths, maxDepth);
        foreach (var p in up.Paths) p.Reverse();

        var isCircular = nesting.CircularGroups.Contains(groupDn);
        var peers = isCircular
            ? Reachable(edges, groupDn).Intersect(Reachable(reverse, groupDn), StringComparer.OrdinalIgnoreCase)
                .Where(g => !g.Equals(groupDn, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

        return new Report
        {
            GroupDn = groupDn,
            Depth = nesting.DepthByGroup.GetValueOrDefault(groupDn),
            IsCircular = isCircular,
            Descendants = down,
            Ancestors = up,
            CyclePeers = peers,
            MultiPathGroups = isCircular ? [] : CountPaths(edges, groupDn)
        };
    }

    /// <summary>DFS path enumeration; a path ends at a leaf, at a node already on the path (cycle) or at the depth cap.</summary>
    private static PathSet Enumerate(IReadOnlyDictionary<string, List<string>> edges, string start, int maxPaths, int maxDepth)
    {
        var paths = new List<List<string>>();
        var truncated = false;
        var path = new List<string> { start };
        var onPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
        var stack = new Stack<IEnumerator<string>>();
        stack.Push(Children(edges, start).GetEnumerator());
        var extended = new Stack<bool>();
        extended.Push(false);

        while (stack.Count > 0)
        {
            var it = stack.Peek();
            if (!it.MoveNext())
            {
                stack.Pop();
                var hadChild = extended.Pop();
                if (!hadChild && path.Count > 1) paths.Add([.. path]);
                if (path.Count > 0) { onPath.Remove(path[^1]); path.RemoveAt(path.Count - 1); }
                if (paths.Count >= maxPaths) { truncated = true; break; }
                continue;
            }

            extended.Pop(); extended.Push(true);
            var next = it.Current;
            if (onPath.Contains(next) || path.Count >= maxDepth)
            {
                // Cycle back-edge or depth cap: record the path including the repeated node.
                paths.Add([.. path, next]);
                if (path.Count >= maxDepth) truncated = true;
                if (paths.Count >= maxPaths) { truncated = true; break; }
                continue;
            }

            path.Add(next);
            onPath.Add(next);
            stack.Push(Children(edges, next).GetEnumerator());
            extended.Push(false);
        }

        return new PathSet(paths, truncated);
    }

    private static IEnumerable<string> Children(IReadOnlyDictionary<string, List<string>> edges, string node) =>
        edges.TryGetValue(node, out var c) ? c : Enumerable.Empty<string>();

    private static HashSet<string> Reachable(IReadOnlyDictionary<string, List<string>> edges, string start)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(Children(edges, start));
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (!seen.Add(n)) continue;
            foreach (var c in Children(edges, n)) queue.Enqueue(c);
        }
        return seen;
    }

    /// <summary>Counts distinct paths from <paramref name="root"/> to each nested group in an acyclic subgraph.</summary>
    private static List<(string, long)> CountPaths(IReadOnlyDictionary<string, List<string>> edges, string root)
    {
        var reach = Reachable(edges, root);
        reach.Add(root);
        var indegree = reach.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var n in reach)
            foreach (var c in Children(edges, n))
                if (reach.Contains(c)) indegree[c]++;

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { [root] = 1 };
        var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            var nc = counts.GetValueOrDefault(n);
            foreach (var c in Children(edges, n))
            {
                if (!reach.Contains(c)) continue;
                counts[c] = Math.Min(long.MaxValue / 2, counts.GetValueOrDefault(c) + nc);
                if (--indegree[c] == 0) queue.Enqueue(c);
            }
        }
        return counts.Where(kv => kv.Value > 1 && !kv.Key.Equals(root, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
