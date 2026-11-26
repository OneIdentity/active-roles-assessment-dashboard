using ActiveRolesDashboard.Pages.Tools;

namespace ActiveRolesDashboard.Tests.PermissionTracker;

[Trait("Stage", "PermissionTracker")]
public class PermissionTrackerPageTests
{
    [Theory]
    [InlineData("OU=Sales,DC=test,DC=local", "DC=test,DC=local")]
    [InlineData(@"CN=Smith\, John,OU=Sales,DC=test", "OU=Sales,DC=test")]
    [InlineData("DC=local", null)]
    public void ParentDn_honours_escaped_commas(string dn, string? expected) =>
        Assert.Equal(expected, PermissionTrackerModel.ParentDn(dn));
}
