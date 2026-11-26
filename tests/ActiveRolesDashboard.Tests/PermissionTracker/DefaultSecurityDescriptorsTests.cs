using System.Security.AccessControl;
using ActiveRolesDashboard.Services.PermissionTracker;
using Xunit;

namespace ActiveRolesDashboard.Tests.PermissionTracker;

public class DefaultSecurityDescriptorsTests
{
    private const string Domain = "S-1-5-21-1111111111-2222222222-3333333333";

    [Fact]
    public void Marks_only_aces_present_in_schema_default()
    {
        var defaults = DefaultSecurityDescriptors.ParseKeys("D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;DA)(A;;RPLCLORC;;;AU)");
        var sd = new RawSecurityDescriptor(
            $"O:DAG:DAD:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;{Domain}-512)(A;;RPLCLORC;;;AU)(A;;WP;;;{Domain}-1105)");

        var entries = DefaultSecurityDescriptors.Mark(AdAceParser.Parse(sd), defaults);

        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsDefault);
        Assert.True(entries[1].IsDefault);
        Assert.False(entries[2].IsDefault);
    }

    [Fact]
    public void Domain_root_install_defaults_are_marked()
    {
        var sd = new RawSecurityDescriptor(
            $"D:(A;;RPWPCRCCDCLCLORCWOWDSDDTSW;;;{Domain}-519)" +
            $"(OA;CIIO;RP;4c164200-20c0-11d0-a768-00aa006e0529;bf967aba-0de6-11d0-a285-00aa003049e2;RU)" +
            $"(OA;;CR;e2a36dc9-ae17-47c3-b58b-be34c55ba633;;S-1-5-32-557)" +
            $"(OA;;CR;1131f6ad-9c07-11d1-f79f-00c04fc2dcd2;;{Domain}-1105)");

        var entries = DefaultSecurityDescriptors.Mark(AdAceParser.Parse(sd), DefaultSecurityDescriptors.InstallDefaultKeys("domainDNS"));

        Assert.True(entries[0].IsDefault);
        Assert.True(entries[1].IsDefault);
        Assert.True(entries[2].IsDefault);
        Assert.False(entries[3].IsDefault);
        Assert.Empty(DefaultSecurityDescriptors.InstallDefaultKeys("user"));
    }

    [Fact]
    public void Empty_or_missing_sddl_marks_nothing()
    {
        var sd = new RawSecurityDescriptor("D:(A;;RPLCLORC;;;AU)");
        var entries = DefaultSecurityDescriptors.Mark(AdAceParser.Parse(sd), DefaultSecurityDescriptors.ParseKeys(null));
        Assert.All(entries, e => Assert.False(e.IsDefault));
    }
}
