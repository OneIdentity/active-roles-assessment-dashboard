using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Tests.Settings;

[Trait("Stage", "Settings")]
public class UpdatesVisibilityTests
{
    private static readonly IReadOnlySet<DashboardPermission> None = new HashSet<DashboardPermission>();
    private static readonly IReadOnlySet<DashboardPermission> ManageUpdates = new HashSet<DashboardPermission> { DashboardPermission.ManageAssessmentUpdates };

    [Fact]
    public void ActiveRolesAdmin_SeesAndManagesUpdates()
    {
        Assert.True(RolePermissionRegistry.CanViewUpdates(None, isActiveRolesAdmin: true, DashboardRole.User));
        Assert.True(RolePermissionRegistry.CanManageAssessmentUpdates(None, isActiveRolesAdmin: true));
    }

    [Fact]
    public void DashboardAdministrator_SeesUpdates()
    {
        Assert.True(RolePermissionRegistry.CanViewUpdates(None, false, DashboardRole.DashboardAdministrator));
    }

    [Fact]
    public void UpdatePermission_GrantsVisibilityAndSettingsAccess()
    {
        Assert.True(RolePermissionRegistry.CanViewUpdates(ManageUpdates, false, DashboardRole.User));
        Assert.True(RolePermissionRegistry.CanManageAssessmentUpdates(ManageUpdates, false));
        Assert.True(RolePermissionRegistry.CanAccessSettings(ManageUpdates));
    }

    [Fact]
    public void OrdinaryUser_DoesNotSeeUpdates()
    {
        Assert.False(RolePermissionRegistry.CanViewUpdates(None, false, DashboardRole.User));
        Assert.False(RolePermissionRegistry.CanManageAssessmentUpdates(None, false));
    }

    [Fact]
    public void EveryUpdatePermission_MakesUpdatesVisible()
    {
        foreach (var p in RolePermissionRegistry.UpdatePermissions)
            Assert.True(RolePermissionRegistry.CanViewUpdates(new HashSet<DashboardPermission> { p }, false, DashboardRole.User));
    }

    [Fact]
    public void UpdatesCategory_IsRegistered()
    {
        Assert.Contains(SettingsCategoryInfo.Updates, SettingsCategoryInfo.All);
        Assert.Equal("Updates", SettingsCategoryInfo.Updates.Key);
    }
}
