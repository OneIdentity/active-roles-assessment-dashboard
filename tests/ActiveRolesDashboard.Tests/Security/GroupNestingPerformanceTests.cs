using System.Diagnostics;
using ActiveRolesDashboard.Services;
using Xunit.Abstractions;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Scale tests for the nesting analyzer and path explorer on generated directories.
/// Each test asserts a generous upper bound so regressions surface in CI, and logs
/// the measured time so the limits can be tuned.
/// </summary>
[Trait("Category", "Performance")]
public class GroupNestingPerformanceTests(ITestOutputHelper output)
{
    /// <summary>
    /// Builds a directory with realistic structure: a forest of groups with fan-out,
    /// some deep chains, diamond lattices, cycles, and users spread across groups.
    /// </summary>
    private static Dictionary<string, List<string>> Generate(int groups, int usersPerGroup, int seed = 42)
    {
        var rnd = new Random(seed);
        var g = new Dictionary<string, List<string>>(groups, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups; i++) g[$"G{i}"] = [];

        // Tree-ish nesting: each group nests into a random earlier group (fan-out).
        for (var i = 1; i < groups; i++)
            if (rnd.NextDouble() < 0.6) g[$"G{rnd.Next(i)}"].Add($"G{i}");

        // Extra cross links create multiple paths (diamonds).
        for (var i = 0; i < groups / 10; i++)
        {
            var a = rnd.Next(groups - 1);
            var b = a + 1 + rnd.Next(groups - a - 1);
            g[$"G{a}"].Add($"G{b}");
        }

        // Deep chains (depth 60, past the explorer's depth cap).
        for (var c = 0; c < 5; c++)
        {
            var start = rnd.Next(groups - 60);
            for (var d = 0; d < 59; d++) g[$"G{start + d}"].Add($"G{start + d + 1}");
        }

        // Cycles.
        for (var c = 0; c < groups / 1000 + 1; c++)
        {
            var a = rnd.Next(groups); var b = rnd.Next(groups);
            g[$"G{a}"].Add($"G{b}"); g[$"G{b}"].Add($"G{a}");
        }

        // Users.
        var userCount = groups * usersPerGroup;
        for (var u = 0; u < userCount; u++)
        {
            var memberships = 1 + rnd.Next(3);
            for (var m = 0; m < memberships; m++) g[$"G{rnd.Next(groups)}"].Add($"U{u}");
        }
        return g;
    }

    private static Dictionary<string, List<string>> Lattice(int layers)
    {
        var g = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < layers; i++)
        {
            g[$"N{i}"] = [$"L{i}", $"R{i}"];
            g[$"L{i}"] = [$"N{i + 1}"];
            g[$"R{i}"] = [$"N{i + 1}"];
        }
        g[$"N{layers}"] = ["U0"];
        return g;
    }

    private (T Result, double Ms, long AllocKb) Measure<T>(string label, Func<T> run)
    {
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var r = run();
        sw.Stop();
        var kb = (GC.GetAllocatedBytesForCurrentThread() - before) / 1024;
        output.WriteLine($"{label}: {sw.Elapsed.TotalMilliseconds:F0} ms, {kb:N0} KB allocated");
        return (r, sw.Elapsed.TotalMilliseconds, kb);
    }

    private static Func<string, bool> IsGroup => id => id.StartsWith('G') || id.StartsWith('N') || id.StartsWith('L') || id.StartsWith('R');

    [Theory]
    [InlineData(10_000, 5, 5_000)]
    [InlineData(50_000, 5, 20_000)]
    [InlineData(200_000, 5, 60_000)]
    public void Nesting_analyzer_scales(int groups, int usersPerGroup, int budgetMs)
    {
        var g = Generate(groups, usersPerGroup);

        var (r, ms, _) = Measure($"Analyze {groups:N0} groups",
            () => GroupNestingAnalyzer.Analyze(g, new() { CanNest = IsGroup }));

        output.WriteLine($"  edges={r.EdgeCount:N0} circular={r.CircularGroups.Count} maxDepth={r.DepthByGroup.Values.DefaultIfEmpty().Max()} multiPath={r.MultiPath.Count:N0} skipped={r.MultiPathSkipped}");
        Assert.True(ms < budgetMs, $"Analyze took {ms:F0} ms (budget {budgetMs} ms)");
        Assert.NotEmpty(r.CircularGroups);
    }

    [Fact]
    public void Nesting_analyzer_bounds_exponential_lattice()
    {
        var (r, ms, _) = Measure("Analyze lattice 60 layers",
            () => GroupNestingAnalyzer.Analyze(Lattice(60), new() { CanNest = IsGroup }));

        output.WriteLine($"  multiPath={r.MultiPath.Count:N0} skipped={r.MultiPathSkipped}");
        Assert.True(ms < 2_000);
    }

    [Theory]
    [InlineData(50_000)]
    [InlineData(200_000)]
    public void Path_explorer_worst_case_root(int groups)
    {
        var g = Generate(groups, 2);
        var groupOnly = g.ToDictionary(kv => kv.Key, kv => kv.Value.Where(m => m[0] == 'G').ToList(), StringComparer.OrdinalIgnoreCase);
        var root = groupOnly.MaxBy(kv => kv.Value.Count).Key;

        var (rep, ms, _) = Measure($"Explore root {root} of {groups:N0}",
            () => GroupPathExplorer.Analyze(groupOnly, root));

        output.WriteLine($"  down={rep.Descendants.Paths.Count} (trunc={rep.Descendants.Truncated}) up={rep.Ancestors.Paths.Count} multi={rep.MultiPathGroups.Count}");
        Assert.True(ms < 10_000, $"Explore took {ms:F0} ms");
        Assert.True(rep.Descendants.Paths.Count <= 200);
    }

    [Fact]
    public void Path_explorer_exponential_lattice_is_capped()
    {
        var (rep, ms, _) = Measure("Explore lattice 60 layers",
            () => GroupPathExplorer.Analyze(Lattice(60), "N0"));

        output.WriteLine($"  down={rep.Descendants.Paths.Count} trunc={rep.Descendants.Truncated}");
        Assert.True(rep.Descendants.Truncated);
        Assert.True(ms < 2_000, $"Explore took {ms:F0} ms");
    }
}
