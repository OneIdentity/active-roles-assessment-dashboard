using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// The directory-derived facts about an authenticated user that gate dashboard access: whether the
/// user is an Active Roles administrator and which fixed <see cref="DashboardRole"/> they resolve
/// to. These are expensive to compute (they require directory group-membership enumeration), so
/// they are evaluated once at login and cached at app scope, then re-evaluated only when the shared
/// superset is (re)built.
/// </summary>
/// <param name="IsActiveRolesAdmin">True when the user is an Active Roles administrator.</param>
/// <param name="Role">The resolved fixed dashboard role.</param>
/// <param name="Epoch">The directory-facts epoch these values were resolved under.</param>
public readonly record struct DirectoryFacts(bool IsActiveRolesAdmin, DashboardRole Role, long Epoch);

/// <summary>
/// The outcome of a forced fresh directory-facts evaluation at login. Carries the newly resolved
/// facts plus the previously cached role/admin flag (when one existed) and whether either changed,
/// so the login flow can decide to discard the user's role-scoped cached data blobs.
/// </summary>
/// <param name="Facts">The freshly resolved (or, on directory failure, last-known) facts.</param>
/// <param name="PreviousRole">The role cached before this evaluation, or null if none was cached.</param>
/// <param name="Changed">True when the resolved role or admin flag differs from the cached value.</param>
public readonly record struct FreshDirectoryFacts(DirectoryFacts Facts, DashboardRole? PreviousRole, bool Changed);

/// <summary>
/// Resolves and caches the per-user directory facts (Active Roles admin flag + dashboard role).
/// Consolidates what was previously duplicated across the login flow and page initialization so the
/// membership evaluation runs at most once per user per directory-facts epoch. The epoch advances
/// whenever the superset is rebuilt (see <see cref="PerUserSummaryCache.InvalidateDirectoryFacts"/>),
/// which forces a re-evaluation on the user's next request.
/// </summary>
public sealed class DirectoryFactsResolver
{
    private readonly RoleService _roleService;
    private readonly PerUserSummaryCache _cache;
    private readonly ILogger<DirectoryFactsResolver> _logger;

    public DirectoryFactsResolver(
        RoleService roleService,
        PerUserSummaryCache cache,
        ILogger<DirectoryFactsResolver> logger)
    {
        _roleService = roleService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>The current directory-facts epoch.</summary>
    public long CurrentEpoch => _cache.DirectoryFactsEpoch;

    /// <summary>
    /// Returns the user's directory facts, using the app-level per-user cache when it is still valid
    /// for the current epoch, otherwise evaluating them freshly (a single group-graph enumeration is
    /// shared across the admin and role checks) and caching the result.
    /// </summary>
    public async Task<DirectoryFacts> ResolveAsync(string token, string username)
    {
        var epoch = _cache.DirectoryFactsEpoch;

        var cachedAdmin = _cache.GetAdmin(username);
        var cachedRole = _cache.GetRole(username);
        if (cachedAdmin is bool admin && cachedRole is string roleName && Enum.TryParse(roleName, out DashboardRole cRole))
        {
            return new DirectoryFacts(admin, cRole, epoch);
        }

        // Resolve admin flag + role in a single efficient pass: the user's DN is looked up once and
        // each role-group check queries only that specific group (via edsaMember/edsaMemberIndirect),
        // instead of enumerating the entire directory group graph per check.
        var (isAdmin, role) = await _roleService.ResolveLoginFactsAsync(token, username);

        _cache.SetAdmin(username, isAdmin);
        _cache.SetRole(username, role.ToString());

        _logger.LogInformation(
            "Resolved directory facts for {User}: admin={Admin}, role={Role} (epoch {Epoch}).",
            username, isAdmin, role, epoch);

        return new DirectoryFacts(isAdmin, role, epoch);
    }

    /// <summary>
    /// Forces a fresh directory-facts evaluation, bypassing the app-scope cache read, and reports
    /// whether the resolved role or admin flag changed relative to the previously cached value.
    /// Intended for the login flow so a role-group membership change made between logins is picked
    /// up immediately rather than waiting for the next superset rebuild or cache expiry. The fresh
    /// values are written back to the cache under the current epoch.
    ///
    /// Fails safe: if the directory lookup throws (e.g. Active Roles/DC transiently unavailable) and
    /// a previously cached value exists, that value is retained and reported unchanged, so a
    /// privileged user is not silently downgraded during an outage.
    /// </summary>
    public async Task<FreshDirectoryFacts> ResolveFreshAsync(string token, string username)
    {
        var epoch = _cache.DirectoryFactsEpoch;

        var cachedAdmin = _cache.GetAdmin(username);
        DashboardRole? previousRole =
            _cache.GetRole(username) is string cachedName && Enum.TryParse(cachedName, out DashboardRole cRole)
                ? cRole
                : null;

        bool isAdmin;
        DashboardRole role;
        try
        {
            (isAdmin, role) = await _roleService.ResolveLoginFactsAsync(token, username);
        }
        catch (Exception ex) when (cachedAdmin is bool || previousRole is not null)
        {
            _logger.LogWarning(ex,
                "Fresh directory-facts evaluation failed for {User}; retaining last-known facts (admin={Admin}, role={Role}).",
                username, cachedAdmin, previousRole);

            var retained = new DirectoryFacts(cachedAdmin ?? false, previousRole ?? default, epoch);
            return new FreshDirectoryFacts(retained, previousRole, Changed: false);
        }

        _cache.SetAdmin(username, isAdmin);
        _cache.SetRole(username, role.ToString());

        var changed = previousRole != role || (cachedAdmin is bool wasAdmin && wasAdmin != isAdmin);

        _logger.LogInformation(
            "Fresh directory facts for {User}: admin={Admin}, role={Role} (was role={PrevRole}, changed={Changed}, epoch {Epoch}).",
            username, isAdmin, role, previousRole, changed, epoch);

        return new FreshDirectoryFacts(new DirectoryFacts(isAdmin, role, epoch), previousRole, changed);
    }
}
