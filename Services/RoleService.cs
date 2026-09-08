using System.Text.Json;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// The resolved role assignment for a user: the assigned <see cref="DashboardRole"/> and the
/// effective set of permissions it carries.
/// </summary>
public sealed record UserRoleAssignment(DashboardRole Role, IReadOnlySet<DashboardPermission> Permissions)
{
    public bool Has(DashboardPermission permission) => Permissions.Contains(permission);
}

/// <summary>
/// Owns the fixed role model: loads/saves the editable role -> permission matrix (encrypted via
/// <see cref="RolePermissionProtector"/>), evaluates a user's <see cref="DashboardRole"/> against
/// the configured role groups, and resolves the effective permission set. The roles and
/// permissions themselves are fixed in code (<see cref="RolePermissionRegistry"/>); this service
/// only manages the per-role assignments and evaluation. The Dashboard Administrator row is fixed
/// at full permissions and is never editable or overridden by a persisted matrix.
/// </summary>
public class RoleService
{
    private readonly ActiveRolesService _arService;
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;
    private readonly RolePermissionProtector _protector;
    private readonly ILogger<RoleService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public RoleService(
        ActiveRolesService arService,
        IOptionsMonitor<ActiveRolesConfig> config,
        RolePermissionProtector protector,
        ILogger<RoleService> logger)
    {
        _arService = arService;
        _config = config;
        _protector = protector;
        _logger = logger;
    }

    /// <summary>
    /// Returns the effective role -> permission matrix: the persisted (decrypted) matrix merged
    /// over the code defaults, with the Dashboard Administrator row forced to full permissions and
    /// any unknown roles/permissions ignored. Falls back to the default matrix when nothing is
    /// persisted or decryption fails.
    /// </summary>
    public IReadOnlyDictionary<DashboardRole, IReadOnlySet<DashboardPermission>> GetMatrix()
    {
        var matrix = RolePermissionRegistry.AllRoles.ToDictionary(
            role => role,
            role => (IReadOnlySet<DashboardPermission>)new HashSet<DashboardPermission>(
                RolePermissionRegistry.GetDefaultPermissions(role)));

        var protectedMatrix = _config.CurrentValue.Roles?.ProtectedMatrix ?? string.Empty;
        if (!string.IsNullOrEmpty(protectedMatrix))
        {
            try
            {
                var json = _protector.Unprotect(protectedMatrix);
                var stored = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions);
                if (stored != null)
                {
                    foreach (var (roleKey, permKeys) in stored)
                    {
                        if (!Enum.TryParse<DashboardRole>(roleKey, ignoreCase: true, out var role))
                            continue; // unknown/removed role - ignore

                        // Dashboard Administrator is fixed at full permissions; never override.
                        if (role == DashboardRole.DashboardAdministrator)
                            continue;

                        var perms = new HashSet<DashboardPermission>();
                        foreach (var permKey in permKeys)
                        {
                            if (Enum.TryParse<DashboardPermission>(permKey, ignoreCase: true, out var perm))
                                perms.Add(perm); // unknown/removed permission - ignore
                        }
                        matrix[role] = perms;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetMatrix: Failed to decrypt/parse persisted role matrix; falling back to defaults.");
            }
        }

        // Dashboard Administrator is always its fixed permission set, regardless of persisted state.
        matrix[DashboardRole.DashboardAdministrator] =
            new HashSet<DashboardPermission>(RolePermissionRegistry.DashboardAdministratorPermissions);

        return matrix;
    }

    /// <summary>
    /// Serializes and encrypts the supplied matrix into a protected payload suitable for storing
    /// in <c>ActiveRoles:Roles:ProtectedMatrix</c>. The Dashboard Administrator row is dropped
    /// (it is fixed in code), and unknown roles/permissions are not representable (enum-typed).
    /// </summary>
    public string ProtectMatrix(IReadOnlyDictionary<DashboardRole, IReadOnlySet<DashboardPermission>> matrix)
    {
        var toStore = new Dictionary<string, List<string>>();
        foreach (var (role, perms) in matrix)
        {
            if (role == DashboardRole.DashboardAdministrator)
                continue; // fixed in code

            toStore[role.ToString()] = perms
                .OrderBy(p => (int)p)
                .Select(p => p.ToString())
                .ToList();
        }

        var json = JsonSerializer.Serialize(toStore, JsonOptions);
        return _protector.Protect(json);
    }

    /// <summary>
    /// Evaluates a user's dashboard role. Order: Active Roles admins are always Dashboard
    /// Administrators; otherwise the first matching role group (Dashboard Admins, then Auditors,
    /// then Power Users) wins; otherwise the base <see cref="DashboardRole.User"/> role. The
    /// <paramref name="isActiveRolesAdmin"/> flag is supplied by the caller (already computed and
    /// cached elsewhere) so this method does not re-run the admin check.
    /// </summary>
    public async Task<DashboardRole> EvaluateRoleAsync(string token, string username, bool isActiveRolesAdmin)
    {
        if (isActiveRolesAdmin)
            return DashboardRole.DashboardAdministrator;

        var config = _config.CurrentValue;
        var baseDn = config.DefaultActiveDirectoryDN;
        var filters = config.DefaultFilters;

        if (await _arService.IsUserMemberOfGroupFilterAsync(token, username, baseDn, filters.DashboardAdmins, "EvaluateRole:DashboardAdmins"))
            return DashboardRole.DashboardAdministrator;

        if (await _arService.IsUserMemberOfGroupFilterAsync(token, username, baseDn, filters.Auditors, "EvaluateRole:Auditors"))
            return DashboardRole.Auditor;

        if (await _arService.IsUserMemberOfGroupFilterAsync(token, username, baseDn, filters.PowerUsers, "EvaluateRole:PowerUsers"))
            return DashboardRole.PowerUser;

        return DashboardRole.User;
    }

    /// <summary>
    /// Efficiently resolves BOTH the Active Roles admin flag and the dashboard role in a single
    /// pass for login-time evaluation. The user's DN is resolved ONCE and reused for every group
    /// check, and each membership check queries only the specific role group (reading Active Roles'
    /// server-computed <c>edsaMember</c>/<c>edsaMemberIndirect</c> attributes) rather than
    /// enumerating the entire directory group graph. This replaces the previous path, which
    /// triggered a full ~thousands-of-groups enumeration per check on every login.
    /// </summary>
    public async Task<(bool IsActiveRolesAdmin, DashboardRole Role)> ResolveLoginFactsAsync(string token, string username)
    {
        var config = _config.CurrentValue;
        var baseDn = config.DefaultActiveDirectoryDN;
        var filters = config.DefaultFilters;

        // Resolve the viewer's DN once; every group-membership check below reuses it.
        var userDn = await _arService.ResolveUserDnAsync(token, username);
        if (string.IsNullOrEmpty(userDn))
        {
            _logger.LogWarning("ResolveLoginFactsAsync: could not resolve DN for '{Username}'; defaulting to User role.", username);
            return (false, DashboardRole.User);
        }

        // Active Roles administrators get full permissions regardless of role-group membership.
        var isAdmin = await _arService.IsDnMemberOfGroupFilterAsync(
            token, userDn, baseDn, filters.ActiveRolesAdmins, "LoginFacts:ActiveRolesAdmins");
        if (isAdmin)
            return (true, DashboardRole.DashboardAdministrator);

        if (await _arService.IsDnMemberOfGroupFilterAsync(token, userDn, baseDn, filters.DashboardAdmins, "LoginFacts:DashboardAdmins"))
            return (false, DashboardRole.DashboardAdministrator);

        if (await _arService.IsDnMemberOfGroupFilterAsync(token, userDn, baseDn, filters.Auditors, "LoginFacts:Auditors"))
            return (false, DashboardRole.Auditor);

        if (await _arService.IsDnMemberOfGroupFilterAsync(token, userDn, baseDn, filters.PowerUsers, "LoginFacts:PowerUsers"))
            return (false, DashboardRole.PowerUser);

        return (false, DashboardRole.User);
    }

    /// <summary>
    /// Resolves the effective permission set for a role from the current (persisted or default)
    /// matrix.
    /// </summary>
    public IReadOnlySet<DashboardPermission> GetPermissions(DashboardRole role)
    {
        var matrix = GetMatrix();
        return matrix.TryGetValue(role, out var perms)
            ? perms
            : RolePermissionRegistry.GetDefaultPermissions(role);
    }

    /// <summary>
    /// Convenience: evaluates the user's role and returns the assignment (role + effective
    /// permissions) in one call.
    /// </summary>
    public async Task<UserRoleAssignment> EvaluateAssignmentAsync(string token, string username, bool isActiveRolesAdmin)
    {
        var role = await EvaluateRoleAsync(token, username, isActiveRolesAdmin);
        return new UserRoleAssignment(role, GetPermissions(role));
    }
}
