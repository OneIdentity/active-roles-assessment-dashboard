using System.Diagnostics;
using System.Text.Json;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Transitive group membership computed locally (replacement for Active Roles' edsaMemberIndirect),
/// including nesting depth, cycles, self-membership, foreign members and scale.
/// </summary>
[Trait("Stage", "Security")]
public class IndirectMembershipCalculatorTests
{
    private static JsonElement Group(string dn, params string[] members) =>
        JsonSerializer.SerializeToElement(new { dn, member = members });

    private static IndirectMembershipCalculator.Result Compute(params JsonElement[] groups) =>
        IndirectMembershipCalculator.Compute(
            groups,
            g => g.GetProperty("dn").GetString() ?? string.Empty,
            (g, attr) => g.TryGetProperty(attr, out var m) ? m.EnumerateArray().Select(x => x.GetString()!).ToList() : []);

    private static void AssertSet(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), actual.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Flat_group_has_no_indirect_members()
    {
        var r = Compute(Group("G", "U1", "U2"));

        AssertSet(["U1", "U2"], r.DirectByDn["G"]);
        Assert.Empty(r.IndirectByDn["G"]);
    }

    [Fact]
    public void Nested_chain_is_expanded_transitively()
    {
        var r = Compute(Group("A", "B"), Group("B", "C", "U1"), Group("C", "U2"));

        AssertSet(["C", "U1", "U2"], r.IndirectByDn["A"]);
        AssertSet(["U2"], r.IndirectByDn["B"]);
        Assert.Empty(r.IndirectByDn["C"]);
    }

    [Fact]
    public void Direct_members_are_never_reported_as_indirect()
    {
        // U1 is both direct of A and reachable through B.
        var r = Compute(Group("A", "B", "U1"), Group("B", "U1", "U2"));

        AssertSet(["U2"], r.IndirectByDn["A"]);
    }

    [Fact]
    public void Two_group_cycle_terminates_with_active_roles_semantics()
    {
        var r = Compute(Group("A", "B"), Group("B", "A", "U1"));

        // reachable(B) = {A, B, U1}; indirect(A) = reachable(B) - {B} = {A, U1}.
        AssertSet(["A", "U1"], r.IndirectByDn["A"]);
        AssertSet(["B"], r.IndirectByDn["B"]);
    }

    [Fact]
    public void Three_group_cycle_matches_documented_example()
    {
        var r = Compute(Group("A", "B"), Group("B", "C"), Group("C", "A"));

        AssertSet(["A", "C"], r.IndirectByDn["A"]);
        AssertSet(["A", "B"], r.IndirectByDn["B"]);
        AssertSet(["B", "C"], r.IndirectByDn["C"]);
    }

    [Fact]
    public void Self_membership_terminates()
    {
        var r = Compute(Group("A", "A", "U1"));

        AssertSet(["A", "U1"], r.DirectByDn["A"]);
        Assert.Empty(r.IndirectByDn["A"]);
    }

    [Fact]
    public void Foreign_and_unknown_members_are_kept_but_not_expanded()
    {
        var r = Compute(Group("A", "B", "CN=S-1-5-21-9,CN=ForeignSecurityPrincipals,DC=x"), Group("B", "CN=Missing Group,DC=x"));

        AssertSet(["CN=Missing Group,DC=x"], r.IndirectByDn["A"]);
        Assert.Empty(r.IndirectByDn["B"]);
    }

    [Fact]
    public void Dn_matching_is_case_insensitive()
    {
        var r = Compute(Group("CN=A,DC=x", "cn=b,dc=x"), Group("CN=B,DC=X", "U1"));

        AssertSet(["U1"], r.IndirectByDn["cn=a,dc=x"]);
    }

    [Fact]
    public void Empty_groups_duplicates_and_missing_dns_are_tolerated()
    {
        var r = Compute(Group("A"), Group("A", "U9"), Group("", "U1"), Group("B", "A"));

        Assert.Empty(r.DirectByDn["A"]);
        Assert.Empty(r.IndirectByDn["B"]);
        Assert.Equal(2, r.DirectByDn.Count);
    }

    [Fact]
    public void Empty_input_returns_empty_result()
    {
        var r = Compute();

        Assert.Empty(r.DirectByDn);
        Assert.Empty(r.IndirectByDn);
    }

    [Fact]
    public void Deep_nesting_does_not_overflow()
    {
        const int depth = 5000;
        var groups = Enumerable.Range(0, depth)
            .Select(i => i < depth - 1 ? Group($"G{i}", $"G{i + 1}") : Group($"G{i}", "U"))
            .ToArray();

        var r = Compute(groups);

        Assert.Equal(depth - 1, r.IndirectByDn["G0"].Count); // G2..G4999 + U
        Assert.Contains("U", r.IndirectByDn["G0"]);
    }

    [Fact]
    public void Wide_interconnected_graph_completes_quickly()
    {
        // 300 groups each nesting the next 5 (with wrap-around cycles) plus a unique user.
        const int n = 300;
        var groups = Enumerable.Range(0, n)
            .Select(i => Group($"G{i}", Enumerable.Range(1, 5).Select(k => $"G{(i + k) % n}").Append($"U{i}").ToArray()))
            .ToArray();

        var sw = Stopwatch.StartNew();
        var r = Compute(groups);
        sw.Stop();

        Assert.Equal(n, r.IndirectByDn.Count);
        Assert.Contains("U150", r.IndirectByDn["G0"]);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Took {sw.Elapsed}");
    }
}
