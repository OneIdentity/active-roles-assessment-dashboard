using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Security;

public class GroupPathExplorerTests
{
    private static Dictionary<string, List<string>> Graph(params (string Parent, string Child)[] edges)
    {
        var g = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (p, c) in edges)
        {
            if (!g.TryGetValue(p, out var l)) g[p] = l = [];
            l.Add(c);
            g.TryAdd(c, []);
        }
        return g;
    }

    [Fact]
    public void Lists_descendant_and_ancestor_paths()
    {
        var g = Graph(("A", "B"), ("B", "C"), ("X", "B"));

        var r = GroupPathExplorer.Analyze(g, "B");

        Assert.Equal(["B", "C"], Assert.Single(r.Descendants.Paths));
        Assert.Equal(2, r.Ancestors.Paths.Count);
        Assert.Contains(r.Ancestors.Paths, p => p.SequenceEqual(["A", "B"]));
        Assert.Contains(r.Ancestors.Paths, p => p.SequenceEqual(["X", "B"]));
        Assert.Equal(1, r.Depth);
        Assert.False(r.IsCircular);
    }

    [Fact]
    public void Diamond_reports_multi_path_group()
    {
        var g = Graph(("A", "B"), ("A", "C"), ("B", "D"), ("C", "D"));

        var r = GroupPathExplorer.Analyze(g, "A");

        var (dn, count) = Assert.Single(r.MultiPathGroups);
        Assert.Equal("D", dn);
        Assert.Equal(2, count);
        Assert.Equal(2, r.Descendants.Paths.Count);
    }

    [Fact]
    public void Cycle_is_detected_and_enumeration_terminates()
    {
        var g = Graph(("A", "B"), ("B", "C"), ("C", "A"));

        var r = GroupPathExplorer.Analyze(g, "A");

        Assert.True(r.IsCircular);
        Assert.Equal(["B", "C"], r.CyclePeers);
        Assert.Equal(["A", "B", "C", "A"], Assert.Single(r.Descendants.Paths));
        Assert.Empty(r.MultiPathGroups);
    }

    [Fact]
    public void Path_enumeration_is_capped()
    {
        // 2^10 distinct paths through a chain of diamonds.
        var edges = new List<(string, string)>();
        for (var i = 0; i < 10; i++)
        {
            edges.Add(($"N{i}", $"L{i}")); edges.Add(($"N{i}", $"R{i}"));
            edges.Add(($"L{i}", $"N{i + 1}")); edges.Add(($"R{i}", $"N{i + 1}"));
        }

        var r = GroupPathExplorer.Analyze(Graph([.. edges]), "N0", maxPaths: 50);

        Assert.True(r.Descendants.Truncated);
        Assert.Equal(50, r.Descendants.Paths.Count);
        Assert.Equal(1024, r.MultiPathGroups.First(m => m.GroupDn == "N10").PathCount);
    }

    [Fact]
    public void PathsTo_lists_every_route_to_users_direct_groups()
    {
        var g = Graph(("A", "B"), ("A", "C"), ("B", "D"), ("C", "D"), ("A", "E"));
        var direct = new HashSet<string>(["D", "E"], StringComparer.OrdinalIgnoreCase);

        var r = GroupPathExplorer.PathsTo(g, "A", direct);

        Assert.Equal(3, r.Paths.Count);
        Assert.Contains(r.Paths, p => p.SequenceEqual(["A", "B", "D"]));
        Assert.Contains(r.Paths, p => p.SequenceEqual(["A", "C", "D"]));
        Assert.Contains(r.Paths, p => p.SequenceEqual(["A", "E"]));
    }

    [Fact]
    public void PathsTo_single_route_and_direct_membership()
    {
        var g = Graph(("A", "B"));

        Assert.Single(GroupPathExplorer.PathsTo(g, "A", new HashSet<string>(["B"])).Paths);
        Assert.Equal(["A"], Assert.Single(GroupPathExplorer.PathsTo(g, "A", new HashSet<string>(["A"])).Paths));
        Assert.Empty(GroupPathExplorer.PathsTo(g, "A", new HashSet<string>(["Z"])).Paths);
    }

    [Fact]
    public void PathsTo_terminates_on_cycles()
    {
        var g = Graph(("A", "B"), ("B", "A"), ("B", "C"));

        var r = GroupPathExplorer.PathsTo(g, "A", new HashSet<string>(["C"]));

        Assert.Equal(["A", "B", "C"], Assert.Single(r.Paths));
    }

    [Fact]
    public void Restrict_drops_groups_outside_visibility()
    {
        var g = Graph(("A", "B"), ("A", "Hidden"), ("Hidden", "C"));

        var r = GroupPathExplorer.Restrict(g, new HashSet<string>(["A", "B", "C"], StringComparer.OrdinalIgnoreCase));

        Assert.False(r.ContainsKey("Hidden"));
        Assert.Equal(["B"], r["A"]);
    }
}
