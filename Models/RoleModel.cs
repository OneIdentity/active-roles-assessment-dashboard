namespace ActiveRolesDashboard.Models;

/// <summary>
/// The fixed set of dashboard roles. New roles cannot be added and existing roles cannot be
/// removed - the set is closed by the enum. Roles are evaluated in <see cref="DashboardRole"/>
/// precedence order (highest privilege first) against the configured role groups; see
/// <c>RolePermissionRegistry.EvaluationOrder</c>.
/// </summary>
public enum DashboardRole
{
    /// <summary>Full access to everything in the dashboard, including System settings.</summary>
    DashboardAdministrator = 0,

    /// <summary>See and do everything except viewing or changing System settings.</summary>
    Auditor = 1,

    /// <summary>See what their permissions allow and perform some functions.</summary>
    PowerUser = 2,

    /// <summary>See what their permissions allow and perform limited functions. The base role
    /// assigned when a user matches no role group.</summary>
    User = 3
}

/// <summary>
/// The fixed set of dashboard permissions. New permissions cannot be added and existing
/// permissions cannot be removed - the set is closed by the enum. Roles are composed of one
/// or more of these permissions (see <c>RolePermissionRegistry.DefaultMatrix</c>).
/// </summary>
public enum DashboardPermission
{
    ManageUserSettings = 0,
    ViewSystemSettings = 1,
    ManageSystemSettings = 2,
    ViewActiveRolesDashboard = 3,
    ViewActiveDirectoryDashboard = 4,
    ViewEntraIdDashboard = 5,
    ViewExchangeDashboard = 6,
    ViewLicensingDashboard = 7,
    UseDelegatedPermissionsForVisibility = 8,
    RefreshCurrentDashboard = 9,
    ViewSnapshots = 10,
    CompareSnapshots = 11,
    RunAndSaveSnapshots = 12,
    DeleteSnapshots = 13,
    ViewExposureReport = 14,
    CompareExposureReports = 15,
    ViewAssessments = 16,
    RunAndSaveAssessments = 17,
    CompareAssessments = 18,
    DeleteAssessments = 19,
    RebuildCache = 20,

    // NOTE: The value of this permission is questionable - a user who can see a group can
    // already view the group tree. Retained for completeness; flagged for later removal.
    ViewGroupTree = 21
}

/// <summary>
/// Static display/localization metadata for a <see cref="DashboardRole"/> or
/// <see cref="DashboardPermission"/>. The <see cref="ResourceKey"/> is the resx key used to
/// resolve a localized label; <see cref="DefaultName"/> is the English fallback.
/// </summary>
public sealed record RoleModelDisplay(string ResourceKey, string DefaultName);

/// <summary>
/// The authoritative, fixed registry of dashboard roles and permissions: the default
/// role -> permission matrix, the role evaluation (precedence) order, and display metadata.
/// Because the roles and permissions are closed enum sets, this registry guarantees that the
/// set can never be added to or removed from - only the per-role permission assignments are
/// editable (persisted encrypted, see <c>RoleService</c>). The Dashboard Administrator row is
/// fixed at full permissions and is never editable.
/// </summary>
public static class RolePermissionRegistry
{
    /// <summary>All roles, highest privilege first (canonical display order).</summary>
    public static readonly IReadOnlyList<DashboardRole> AllRoles = new[]
    {
        DashboardRole.DashboardAdministrator,
        DashboardRole.Auditor,
        DashboardRole.PowerUser,
        DashboardRole.User
    };

    /// <summary>All permissions in canonical display order (matches the enum order).</summary>
    public static readonly IReadOnlyList<DashboardPermission> AllPermissions =
        Enum.GetValues<DashboardPermission>();

    /// <summary>
    /// The fixed permission set for the Dashboard Administrator role: every permission EXCEPT
    /// <see cref="DashboardPermission.UseDelegatedPermissionsForVisibility"/>. Administrators see
    /// the full, unfiltered dashboard, so scoping visibility to their delegated Active Roles
    /// permissions does not apply to them.
    /// </summary>
    public static readonly IReadOnlySet<DashboardPermission> DashboardAdministratorPermissions =
        new HashSet<DashboardPermission>(AllPermissions
            .Where(p => p != DashboardPermission.UseDelegatedPermissionsForVisibility));

    /// <summary>
    /// Order in which a user's role is evaluated. The first role whose role group the user is a
    /// member of wins; if none match, the base <see cref="DashboardRole.User"/> role is assigned.
    /// Dashboard Administrator is resolved separately (Active Roles admins are always Dashboard
    /// Administrators), so only the group-backed roles appear here.
    /// </summary>
    public static readonly IReadOnlyList<DashboardRole> EvaluationOrder = new[]
    {
        DashboardRole.DashboardAdministrator,
        DashboardRole.Auditor,
        DashboardRole.PowerUser
    };

    /// <summary>
    /// The default role -> permission matrix, mirroring the initial mapping table. This is the
    /// fallback used when no persisted matrix exists and the source of truth for the fixed
    /// Dashboard Administrator row (which is always full permissions).
    /// </summary>
    public static readonly IReadOnlyDictionary<DashboardRole, IReadOnlySet<DashboardPermission>> DefaultMatrix =
        new Dictionary<DashboardRole, IReadOnlySet<DashboardPermission>>
        {
            [DashboardRole.DashboardAdministrator] = new HashSet<DashboardPermission>(DashboardAdministratorPermissions),
            [DashboardRole.Auditor] = new HashSet<DashboardPermission>
            {
                DashboardPermission.ManageUserSettings,
                DashboardPermission.ViewSystemSettings,
                DashboardPermission.ViewActiveRolesDashboard,
                DashboardPermission.ViewActiveDirectoryDashboard,
                DashboardPermission.ViewEntraIdDashboard,
                DashboardPermission.ViewExchangeDashboard,
                DashboardPermission.ViewLicensingDashboard,
                DashboardPermission.RefreshCurrentDashboard,
                DashboardPermission.ViewSnapshots,
                DashboardPermission.CompareSnapshots,
                DashboardPermission.RunAndSaveSnapshots,
                DashboardPermission.ViewExposureReport,
                DashboardPermission.CompareExposureReports,
                DashboardPermission.ViewAssessments,
                DashboardPermission.RunAndSaveAssessments,
                DashboardPermission.CompareAssessments,
                DashboardPermission.RebuildCache,
                DashboardPermission.ViewGroupTree
            },
            [DashboardRole.PowerUser] = new HashSet<DashboardPermission>
            {
                DashboardPermission.ManageUserSettings,
                DashboardPermission.ViewSystemSettings,
                DashboardPermission.UseDelegatedPermissionsForVisibility,
                DashboardPermission.RefreshCurrentDashboard,
                DashboardPermission.ViewAssessments,
                DashboardPermission.RunAndSaveAssessments,
                DashboardPermission.CompareAssessments,
                DashboardPermission.ViewGroupTree
            },
            [DashboardRole.User] = new HashSet<DashboardPermission>
            {
                DashboardPermission.ManageUserSettings,
                DashboardPermission.UseDelegatedPermissionsForVisibility,
                DashboardPermission.RefreshCurrentDashboard,
                DashboardPermission.ViewAssessments,
                DashboardPermission.ViewGroupTree
            }
        };

    /// <summary>Display/localization metadata per role.</summary>
    public static readonly IReadOnlyDictionary<DashboardRole, RoleModelDisplay> RoleDisplay =
        new Dictionary<DashboardRole, RoleModelDisplay>
        {
            [DashboardRole.DashboardAdministrator] = new("Role_DashboardAdministrator", "Dashboard Administrator"),
            [DashboardRole.Auditor] = new("Role_Auditor", "Auditor"),
            [DashboardRole.PowerUser] = new("Role_PowerUser", "Power User"),
            [DashboardRole.User] = new("Role_User", "User")
        };

    /// <summary>Display/localization metadata per permission.</summary>
    public static readonly IReadOnlyDictionary<DashboardPermission, RoleModelDisplay> PermissionDisplay =
        new Dictionary<DashboardPermission, RoleModelDisplay>
        {
            [DashboardPermission.ManageUserSettings] = new("Perm_ManageUserSettings", "Manage User settings"),
            [DashboardPermission.ViewSystemSettings] = new("Perm_ViewSystemSettings", "View System settings"),
            [DashboardPermission.ManageSystemSettings] = new("Perm_ManageSystemSettings", "Manage System settings"),
            [DashboardPermission.ViewActiveRolesDashboard] = new("Perm_ViewActiveRolesDashboard", "View Active Roles dashboard"),
            [DashboardPermission.ViewActiveDirectoryDashboard] = new("Perm_ViewActiveDirectoryDashboard", "View Active Directory dashboard"),
            [DashboardPermission.ViewEntraIdDashboard] = new("Perm_ViewEntraIdDashboard", "View Entra Id dashboard"),
            [DashboardPermission.ViewExchangeDashboard] = new("Perm_ViewExchangeDashboard", "View Exchange dashboard"),
            [DashboardPermission.ViewLicensingDashboard] = new("Perm_ViewLicensingDashboard", "View Licensing dashboard"),
            [DashboardPermission.UseDelegatedPermissionsForVisibility] = new("Perm_UseDelegatedPermissionsForVisibility", "Use delegated permissions to determine dashboard visibility"),
            [DashboardPermission.RefreshCurrentDashboard] = new("Perm_RefreshCurrentDashboard", "Refresh current dashboard"),
            [DashboardPermission.ViewSnapshots] = new("Perm_ViewSnapshots", "View snapshots"),
            [DashboardPermission.CompareSnapshots] = new("Perm_CompareSnapshots", "Compare snapshots"),
            [DashboardPermission.RunAndSaveSnapshots] = new("Perm_RunAndSaveSnapshots", "Run & Save snapshots"),
            [DashboardPermission.DeleteSnapshots] = new("Perm_DeleteSnapshots", "Delete snapshots"),
            [DashboardPermission.ViewExposureReport] = new("Perm_ViewExposureReport", "View Exposure report"),
            [DashboardPermission.CompareExposureReports] = new("Perm_CompareExposureReports", "Compare Exposure reports"),
            [DashboardPermission.ViewAssessments] = new("Perm_ViewAssessments", "View Assessments"),
            [DashboardPermission.RunAndSaveAssessments] = new("Perm_RunAndSaveAssessments", "Run & Save assessments"),
            [DashboardPermission.CompareAssessments] = new("Perm_CompareAssessments", "Compare assessments"),
            [DashboardPermission.DeleteAssessments] = new("Perm_DeleteAssessments", "Delete assessments"),
            [DashboardPermission.RebuildCache] = new("Perm_RebuildCache", "Rebuild cache"),
            [DashboardPermission.ViewGroupTree] = new("Perm_ViewGroupTree", "View Group tree")
        };

    /// <summary>
    /// Returns the default permission set for a role (a defensive copy). Falls back to an empty
    /// set for any unmapped role (should not occur for the fixed enum set).
    /// </summary>
    public static IReadOnlySet<DashboardPermission> GetDefaultPermissions(DashboardRole role) =>
        DefaultMatrix.TryGetValue(role, out var perms)
            ? new HashSet<DashboardPermission>(perms)
            : new HashSet<DashboardPermission>();
}
