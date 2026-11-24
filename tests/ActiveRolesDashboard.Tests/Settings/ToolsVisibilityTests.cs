using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Tests.Settings;

[Trait("Stage", "Settings")]
public class ToolsVisibilityTests
{
    private static readonly IReadOnlySet<DashboardPermission> None = new HashSet<DashboardPermission>();
    private static readonly IReadOnlySet<DashboardPermission> GroupAnalyzer = new HashSet<DashboardPermission> { DashboardPermission.UseGroupAnalyzer };

    [Fact]
    public void ActiveRolesAdmin_SeesToolsAndUsesGroupAnalyzer()
    {
        Assert.True(RolePermissionRegistry.CanViewTools(None, isActiveRolesAdmin: true));
        Assert.True(RolePermissionRegistry.CanUseGroupAnalyzer(None, isActiveRolesAdmin: true));
    }

    [Fact]
    public void GroupAnalyzerPermission_GrantsToolsAndAnalyzer()
    {
        Assert.True(RolePermissionRegistry.CanViewTools(GroupAnalyzer, false));
        Assert.True(RolePermissionRegistry.CanUseGroupAnalyzer(GroupAnalyzer, false));
    }

    [Fact]
    public void OrdinaryUser_DoesNotSeeTools()
    {
        Assert.False(RolePermissionRegistry.CanViewTools(None, false));
        Assert.False(RolePermissionRegistry.CanUseGroupAnalyzer(None, false));
    }

    [Fact]
    public void EveryToolPermission_MakesToolsVisible()
    {
        foreach (var permission in RolePermissionRegistry.ToolPermissions)
            Assert.True(RolePermissionRegistry.CanViewTools(new HashSet<DashboardPermission> { permission }, false));
    }

    [Theory]
    [InlineData(DashboardRole.DashboardAdministrator, true)]
    [InlineData(DashboardRole.Auditor, true)]
    [InlineData(DashboardRole.PowerUser, false)]
    [InlineData(DashboardRole.User, false)]
    public void UseGroupAnalyzer_default_grants(DashboardRole role, bool expected)
    {
        Assert.Equal(expected, RolePermissionRegistry.GetDefaultPermissions(role).Contains(DashboardPermission.UseGroupAnalyzer));
    }

    [Fact]
    public void UseGroupAnalyzer_has_display_metadata()
    {
        Assert.Equal("Perm_UseGroupAnalyzer", RolePermissionRegistry.PermissionDisplay[DashboardPermission.UseGroupAnalyzer].ResourceKey);
    }
}
