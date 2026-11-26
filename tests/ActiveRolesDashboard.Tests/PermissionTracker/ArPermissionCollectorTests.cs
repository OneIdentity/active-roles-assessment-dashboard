using ActiveRolesDashboard.Services.PermissionTracker;

namespace ActiveRolesDashboard.Tests.PermissionTracker;

[Trait("Stage", "PermissionTracker")]
public class ArPermissionCollectorTests
{
    private const string HelpDesk = "S-1-5-21-1-2-3-1200";
    private const string Ou = "OU=Sales,DC=test,DC=local";

    private static ArTemplateLinkRecord Link(params string[] aces) =>
        new("link1", Ou, "CN=Helpdesk,CN=Access Templates,CN=Configuration", "Helpdesk", [HelpDesk], aces);

    [Fact]
    public void Reset_password_on_users_is_expanded_and_flagged()
    {
        var entry = Assert.Single(ArPermissionCollector.Expand([
            Link("A;CI;CR;00299570-246d-11d0-a768-00aa006e0529;bf967aba-0de6-11d0-a285-00aa003049e2")]));

        Assert.Equal(Ou, entry.SecuredObjectDn);
        Assert.Equal(HelpDesk, entry.TrusteeSid);
        Assert.Equal("Helpdesk", entry.AccessTemplateName);
        Assert.Equal("Reset Password", entry.ObjectTypeName);
        Assert.Equal("user", entry.InheritedObjectTypeName);
        Assert.True(entry.IsAllow);
        Assert.True(entry.IsHighRisk);
    }

    [Fact]
    public void Read_only_template_is_not_high_risk()
    {
        var entry = Assert.Single(ArPermissionCollector.Expand([Link("A;CI;LCRPLO;;")]));
        Assert.False(entry.IsHighRisk);
        Assert.Contains("Read property", entry.Rights);
    }

    [Fact]
    public void Deny_entries_and_multiple_trustees_are_expanded()
    {
        var link = Link("D;;WP;;") with { TrusteeSids = [HelpDesk, "S-1-5-21-1-2-3-1201"] };
        var entries = ArPermissionCollector.Expand([link]);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.False(e.IsAllow));
        Assert.All(entries, e => Assert.False(e.IsHighRisk));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AU;SA;WP;;")]
    [InlineData("A;;ZZ;;")]
    public void Unsupported_tokens_are_ignored(string token) =>
        Assert.Null(ArPermissionCollector.ParseToken(token));
}
