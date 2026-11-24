using System.Diagnostics;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Source-agnostic nesting analysis: depth, cycles, multi-path memberships, Entra eligibility hooks and scale.
/// </summary>
[Trait("Stage", "Security")]
public class GroupNestingAnalyzerTests
{
    private static Dictionary<string, List<string>> Graph(params (string Group, string[] Members)[] groups) =>
        groups.ToDictionary(g => g.Group, g => g.Members.ToList(), StringComparer.OrdinalIgnoreCase);

    private static (string, string[]) G(string id, params string[] members) => (id, members);

    [Fact]
    public void Flat_group_has_depth_zero_and_no_findings()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "U1", "U2")));

        Assert.Equal(0, r.DepthByGroup["A"]);
        Assert.Empty(r.CircularGroups);
        Assert.Empty(r.MultiPath);
    }

    [Fact]
    public void Chain_depth_counts_nested_levels()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "B"), G("B", "C"), G("C", "D"), G("D", "U")));

        Assert.Equal(3, r.DepthByGroup["A"]);
        Assert.Equal(2, r.DepthByGroup["B"]);
        Assert.Equal(0, r.DepthByGroup["D"]);
        Assert.Equal(["A"], r.GroupsDeeperThan(2));
    }

    [Fact]
    public void Depth_uses_longest_branch()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "B", "X"), G("B", "C"), G("C"), G("X")));

        Assert.Equal(2, r.DepthByGroup["A"]);
    }

    [Fact]
    public void Cycle_groups_are_detected_including_ones_reached_through_finished_nodes()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "B", "C"), G("B", "A"), G("C", "B"), G("P", "A")));

        Assert.Equal(["A", "B", "C"], r.CircularGroups.Order());
        Assert.Contains("P", r.ReachesCycle);
        Assert.Equal(2, r.DepthByGroup["P"]);
        Assert.DoesNotContain("A", r.GroupsDeeperThan(0));
    }

    [Fact]
    public void Self_membership_is_circular()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "A", "U")));

        Assert.Contains("A", r.CircularGroups);
        Assert.Empty(r.MultiPath);
    }

    [Fact]
    public void Diamond_produces_multi_path_membership()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "B", "C"), G("B", "U"), G("C", "U"), G("D", "U2")));

        var mp = Assert.Single(r.MultiPath);
        Assert.Equal(("A", "U", 2), (mp.GroupId, mp.MemberId, mp.PathCount));
    }

    [Fact]
    public void Direct_plus_indirect_is_multi_path()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "U", "B"), G("B", "U")));

        var mp = Assert.Single(r.MultiPath);
        Assert.Equal("A", mp.GroupId);
        Assert.Equal(2, mp.PathCount);
    }

    [Fact]
    public void Ids_are_case_insensitive_and_duplicates_ignored()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "b", "B", "u"), G("B", "U")));

        Assert.Equal(1, r.DepthByGroup["a"]);
        Assert.Equal(2, Assert.Single(r.MultiPath).PathCount);
    }

    [Fact]
    public void Ineligible_parent_edges_are_ignored_but_group_still_traversed_as_child()
    {
        var graph = Graph(G("Cloud", "M365"), G("M365", "Inner"), G("Inner", "U"));
        var r = GroupNestingAnalyzer.Analyze(graph, new GroupNestingAnalyzer.Options { CanNest = id => id != "M365" });

        Assert.Equal(1, r.DepthByGroup["Cloud"]);
        Assert.Equal(0, r.DepthByGroup["M365"]);
    }

    [Fact]
    public void Unreported_groups_are_traversed_but_not_reported()
    {
        var graph = Graph(G("Cloud", "Synced"), G("Synced", "Synced2"), G("Synced2", "U"));
        var r = GroupNestingAnalyzer.Analyze(graph, new GroupNestingAnalyzer.Options { IsReported = id => id == "Cloud" });

        Assert.Equal(["Cloud"], r.DepthByGroup.Keys);
        Assert.Equal(2, r.DepthByGroup["Cloud"]);
    }

    [Fact]
    public void Multi_path_is_skipped_above_edge_limit()
    {
        var r = GroupNestingAnalyzer.Analyze(Graph(G("A", "B", "C"), G("B", "U"), G("C", "U")),
            new GroupNestingAnalyzer.Options { MultiPathEdgeLimit = 2 });

        Assert.True(r.MultiPathSkipped);
        Assert.Empty(r.MultiPath);
        Assert.Equal(1, r.DepthByGroup["A"]);
    }

    [Fact]
    public void Large_deep_graph_completes_quickly()
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 20_000; i++)
            graph[$"G{i}"] = i + 1 < 20_000 ? [$"G{i + 1}", $"U{i}"] : [$"U{i}"];

        var sw = Stopwatch.StartNew();
        var r = GroupNestingAnalyzer.Analyze(graph, new GroupNestingAnalyzer.Options { ComputeMultiPath = false });
        sw.Stop();

        Assert.Equal(19_999, r.DepthByGroup["G0"]);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Took {sw.Elapsed}");
    }
}
