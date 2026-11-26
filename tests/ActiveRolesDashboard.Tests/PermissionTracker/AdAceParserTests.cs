using System.Security.AccessControl;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.PermissionTracker;

namespace ActiveRolesDashboard.Tests.PermissionTracker;

[Trait("Stage", "PermissionTracker")]
public class AdAceParserTests
{
    private const string Delegate = "S-1-5-21-1-2-3-1105";

    private static IReadOnlyList<AdPermissionEntry> Parse(string sddl) =>
        AdAceParser.Parse(new RawSecurityDescriptor(sddl));

    [Fact]
    public void Base64_binary_descriptor_is_parsed()
    {
        var sd = new RawSecurityDescriptor($"O:DAD:(A;;GA;;;{Delegate})");
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);

        var parsed = ActiveRolesService.ParseSecurityDescriptor(Convert.ToBase64String(bytes));

        var entry = Assert.Single(AdAceParser.Parse(parsed));
        Assert.Equal(Delegate, entry.TrusteeSid);
        Assert.Equal("Full control", entry.Rights);
    }

    [Fact]
    public void Reset_password_extended_right_is_high_risk_with_readable_names()
    {
        var entry = Assert.Single(Parse($"D:(OA;CIIO;CR;00299570-246d-11d0-a768-00aa006e0529;bf967aba-0de6-11d0-a285-00aa003049e2;{Delegate})"));

        Assert.True(entry.IsAllow);
        Assert.Equal("Reset Password", entry.ObjectTypeName);
        Assert.Equal("user", entry.InheritedObjectTypeName);
        Assert.Equal("Descendant user objects", entry.AppliesTo);
        Assert.True(entry.IsHighRisk);
    }

    [Fact]
    public void Write_member_is_high_risk_but_read_property_is_not()
    {
        var entries = Parse($"D:(OA;;WP;bf9679c0-0de6-11d0-a285-00aa003049e2;;{Delegate})(A;;RP;;;{Delegate})");

        Assert.True(entries[0].IsHighRisk);
        Assert.Equal("member", entries[0].ObjectTypeName);
        Assert.False(entries[1].IsHighRisk);
    }

    [Fact]
    public void Built_in_admin_trustees_are_well_known_and_not_flagged()
    {
        var entries = Parse("D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;S-1-5-21-1-2-3-512)");

        Assert.All(entries, e => Assert.True(e.IsWellKnownTrustee));
        Assert.All(entries, e => Assert.False(e.IsHighRisk));
    }

    [Fact]
    public void Everyone_with_write_dacl_is_flagged()
    {
        var entry = Assert.Single(Parse("D:(A;;WD;;;WD)"));
        Assert.False(entry.IsWellKnownTrustee);
        Assert.True(entry.IsHighRisk);
        Assert.Equal("Modify permissions", entry.Rights);
    }

    [Fact]
    public void Deny_and_inherited_flags_are_reported()
    {
        var entry = Assert.Single(Parse($"D:(D;ID;DT;;;{Delegate})"));
        Assert.False(entry.IsAllow);
        Assert.True(entry.IsInherited);
        Assert.False(entry.IsHighRisk);
    }

    [Fact]
    public void Missing_descriptor_yields_no_entries()
    {
        Assert.Empty(AdAceParser.Parse(null));
        Assert.Null(ActiveRolesService.ParseSecurityDescriptor(""));
    }
}
