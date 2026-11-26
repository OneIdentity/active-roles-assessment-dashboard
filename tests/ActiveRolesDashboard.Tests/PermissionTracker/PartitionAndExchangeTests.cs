using ActiveRolesDashboard.Pages.Tools;
using ActiveRolesDashboard.Services.PermissionTracker;
using Xunit;

namespace ActiveRolesDashboard.Tests.PermissionTracker;

public class PartitionAndExchangeTests
{
    [Fact]
    public void Schema_candidates_for_domain_object_walk_up_parent_domains()
    {
        var dns = ActiveRolesPermissionScanDataSource.SchemaDnCandidates("OU=Sales,DC=prod,DC=oneidentity,DC=corp").ToList();
        Assert.Equal(
        [
            "CN=Schema,CN=Configuration,DC=prod,DC=oneidentity,DC=corp",
            "CN=Schema,CN=Configuration,DC=oneidentity,DC=corp"
        ], dns);
    }

    [Fact]
    public void Schema_candidates_for_configuration_object_use_its_own_partition_first()
    {
        var dns = ActiveRolesPermissionScanDataSource.SchemaDnCandidates("CN=Sites,CN=Configuration,DC=oneidentity,DC=corp").ToList();
        Assert.Equal("CN=Schema,CN=Configuration,DC=oneidentity,DC=corp", dns[0]);
        Assert.Equal(dns.Count, dns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("CN=Sites,CN=Configuration,DC=oneidentity,DC=corp", true)]
    [InlineData("CN=Configuration,DC=oneidentity,DC=corp", true)]
    [InlineData("OU=Configuration,DC=prod,DC=oneidentity,DC=corp", false)]
    [InlineData("DC=prod,DC=oneidentity,DC=corp", false)]
    public void Detects_configuration_partition(string dn, bool expected) =>
        Assert.Equal(expected, PermissionTrackerModel.IsConfigurationDn(dn));

    [Theory]
    [InlineData("PROD\\Exchange Trusted Subsystem", true)]
    [InlineData("Exchange Windows Permissions", true)]
    [InlineData("CN=Organization Management,OU=Microsoft Exchange Security Groups,DC=prod,DC=corp", true)]
    [InlineData("PROD\\Domain Admins", false)]
    [InlineData(null, false)]
    public void Recognises_exchange_trustees(string? name, bool expected) =>
        Assert.Equal(expected, ExchangeDefaults.IsExchangeTrustee(name));
}
