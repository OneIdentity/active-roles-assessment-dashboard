using System.Diagnostics;
using ActiveRolesDashboard.Services;
using Xunit.Abstractions;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Scale measurements for the nesting analyzer and the Group Analyzer path explorer on synthetic graphs.
/// Prints time and allocations and asserts that the configured guards keep cost bounded.
/// </summary>
[Trait("Stage", "Security")]
public class GroupGraphScaleTests(ITestOutputHelper output)
{
    private static string Gid(int i) => "G" + i;

    /// <summary>Realistic mix: random DAG fan-out, users in several groups, some cycles, one chain deeper than 50.</summary>
    private static Dictionary<string, List<string>> Mixed(int groups, int seed = 1)
    {
        var rnd = new Random(seed);
        var g = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups; i++)
        {
            var m = new List<string>();
            for (var u = 0; u < 8; u++) m.Add("U" + rnd.Next(groups * 4));
            if (i > 0) for (var k = 0; k < 2; k++) m.Add(Gid(rnd.Next(Math.Max(0, i - 500), i)));
            g[Gid(i)] = m;
        }
        for (var c = 0; c < groups / 1000; c++) g[Gid(rnd.Next(groups / 2))].Add(Gid(groups - 1 - rnd.Next(groups / 2)));
        return g;
    }

    /// <summary>Single chain; every group has its own users. Reachable-leaf sets grow quadratically.</summary>
    private static Dictionary<string, List<string>> Chain(int groups, int usersPerGroup)
    {
        var g = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups; i++)
        {
            var m = new List<string>();
            for (var u = 0; u < usersPerGroup; u++) m.Add($"U{i}_{u}");
            if (i + 1 < groups) m.Add(Gid(i + 1));
            g[Gid(i)] = m;
        }
        return g;
    }

    /// <summary>Layers of width w fully connected to the next layer: w^layers paths.</summary>
    private static Dictionary<string, List<string>> Lattice(int layers, int width)
    {
        var g = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        g["ROOT"] = Enumerable.Range(0, width).Select(j => $"L0_{j}").ToList();
        for (var l = 0; l < layers; l++)
            for (var j = 0; j < width; j++)
                g[$"L{l}_{j}"] = l + 1 < layers
                    ? Enumerable.Range(0, width).Select(k => $"L{l + 1}_{k}").Append("U" + j).ToList()
                    : ["U" + j];
        return g;
    }

    private static Dictionary<string, List<string>> Ring(int groups)
    {
        var g = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups; i++) g[Gid(i)] = [Gid((i + 1) % groups), "U" + i];
        return g;
    }

    private static Dictionary<string, List<string>> GroupEdgesOnly(Dictionary<string, List<string>> g) =>
        g.ToDictionary(kv => kv.Key, kv => kv.Value.Where(g.ContainsKey).ToList(), StringComparer.OrdinalIgnoreCase);

    private (T Value, TimeSpan Time, long Bytes) Measure<T>(string name, Func<T> f)
    {
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var v = f();
        sw.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{name,-48} {sw.Elapsed.TotalMilliseconds,10:N0} ms {bytes / 1024d / 1024d,10:N1} MB");
        return (v, sw.Elapsed, bytes);
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    [InlineData(200_000)]
    public void Nesting_analyzer_scales_on_mixed_directory(int groups)
    {
        var g = Mixed(groups);
        var (r, time, _) = Measure($"nesting mixed {groups}", () => GroupNestingAnalyzer.Analyze(g));
        output.WriteLine($"  edges={r.EdgeCount} multiPath={r.MultiPath.Count} skipped={r.MultiPathSkipped} circular={r.CircularGroups.Count}");
        Assert.True(time < TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(10_000)]
    [InlineData(50_000)]
    public void Nesting_analyzer_multi_path_is_bounded_on_deep_chain(int groups)
    {
        var g = Chain(groups, 5);
        var (r, time, bytes) = Measure($"nesting chain {groups}", () => GroupNestingAnalyzer.Analyze(g));
        output.WriteLine($"  edges={r.EdgeCount} depth={r.DepthByGroup[Gid(0)]} skipped={r.MultiPathSkipped}");
        Assert.Equal(groups - 1, r.DepthByGroup[Gid(0)]);
        Assert.True(time < TimeSpan.FromSeconds(30));
        Assert.True(bytes < 4L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void Nesting_analyzer_handles_exponential_lattice()
    {
        var g = Lattice(60, 4);
        var (r, time, _) = Measure("nesting lattice 60x4", () => GroupNestingAnalyzer.Analyze(g));
        Assert.Equal(60, r.DepthByGroup["ROOT"]);
        Assert.True(time < TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    [InlineData(200_000)]
    public void Path_explorer_is_bounded_on_mixed_directory(int groups)
    {
        var edges = GroupEdgesOnly(Mixed(groups));
        var (r, time, _) = Measure($"explorer mixed {groups}", () => GroupPathExplorer.Analyze(edges, Gid(groups - 1)));
        output.WriteLine($"  down={r.Descendants.Paths.Count} up={r.Ancestors.Paths.Count} peers={r.CyclePeers.Count} multi={r.MultiPathGroups.Count}");
        Assert.True(r.Descendants.Paths.Count <= 200 && r.Ancestors.Paths.Count <= 200);
        Assert.True(time < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Path_explorer_is_bounded_on_lattice_and_deep_chain()
    {
        var lattice = GroupEdgesOnly(Lattice(60, 4));
        var (l, _, _) = Measure("explorer lattice 60x4", () => GroupPathExplorer.Analyze(lattice, "ROOT"));
        Assert.True(l.Descendants.Truncated);
        Assert.Equal(200, l.Descendants.Paths.Count);

        var chain = GroupEdgesOnly(Chain(10_000, 0));
        var (c, _, _) = Measure("explorer chain 10000", () => GroupPathExplorer.Analyze(chain, Gid(0)));
        Assert.True(c.Descendants.Truncated);
        Assert.All(c.Descendants.Paths, p => Assert.True(p.Count <= 51));
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(10_000)]
    public void Path_explorer_cycle_peers_scale_linearly(int groups)
    {
        var ring = GroupEdgesOnly(Ring(groups));
        var (r, time, _) = Measure($"explorer ring {groups}", () => GroupPathExplorer.Analyze(ring, Gid(0)));
        Assert.Equal(groups - 1, r.CyclePeers.Count);
        Assert.True(time < TimeSpan.FromSeconds(10));
    }
}
