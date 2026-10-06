using System.Text.Json;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// The caller's resolved dashboard identity: Active Roles admin flag, dashboard role and the
/// effective permission set carried by that role.
/// </summary>
public sealed class DashboardAccess
{
    public bool IsActiveRolesAdmin { get; init; }
    public DashboardRole Role { get; init; } = DashboardRole.User;
    public IReadOnlySet<DashboardPermission> Permissions { get; init; } = new HashSet<DashboardPermission>();

    public bool HasPermission(DashboardPermission permission) => Permissions.Contains(permission);

    /// <summary>See <c>DashboardPageModel.UsesFullVisibility</c>.</summary>
    public bool UsesFullVisibility =>
        IsActiveRolesAdmin || !HasPermission(DashboardPermission.UseDelegatedPermissionsForVisibility);
}

/// <summary>The available and effective (resolved) segment selections for both dimensions.</summary>
public sealed class SegmentSelectionView
{
    public IReadOnlyList<string> AvailableDomains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SelectedDomains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AvailableTenants { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SelectedTenants { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Shared dashboard data/permission/segment logic used by both the Razor
/// <c>DashboardPageModel</c> and the <c>DashboardController</c> REST API, so the two hosts share a
/// single implementation of directory-facts resolution, per-user summary loading/caching,
/// Exchange/Licensing gating and segment filtering.
/// </summary>
public sealed class DashboardDataService
{
    private readonly ActiveRolesService _arService;
    private readonly UserSettingsService _userSettingsService;
    private readonly IOptionsMonitor<ActiveRolesConfig> _arConfig;
    private readonly DashboardCacheHolder _cache;
    private readonly PerUserDashboardFilter _perUserFilter;
    private readonly ServiceAccountTokenProvider _serviceAccountTokens;
    private readonly ArPermissionModelService _permissionModelService;
    private readonly PerUserSummaryCache _userSummaryCache;
    private readonly RoleService _roleService;
    private readonly DirectoryFactsResolver _directoryFacts;

    public DashboardDataService(
        ActiveRolesService arService,
        UserSettingsService userSettingsService,
        IOptionsMonitor<ActiveRolesConfig> arConfig,
        DashboardCacheHolder cache,
        PerUserDashboardFilter perUserFilter,
        ServiceAccountTokenProvider serviceAccountTokens,
        ArPermissionModelService permissionModelService,
        PerUserSummaryCache userSummaryCache,
        RoleService roleService,
        DirectoryFactsResolver directoryFacts)
    {
        _arService = arService;
        _userSettingsService = userSettingsService;
        _arConfig = arConfig;
        _cache = cache;
        _perUserFilter = perUserFilter;
        _serviceAccountTokens = serviceAccountTokens;
        _permissionModelService = permissionModelService;
        _userSummaryCache = userSummaryCache;
        _roleService = roleService;
        _directoryFacts = directoryFacts;
    }

    private static string UserKey(HttpContext http) => http.User.Identity?.Name ?? string.Empty;

    public static string? GetAccessToken(HttpContext http) => http.Session.GetString("AccessToken");

    public string? GetCachedSummaryJson(HttpContext http) => _userSummaryCache.GetSummary(UserKey(http));

    /// <summary>
    /// Resolves the admin flag and dashboard role. Directory facts are evaluated once at login and
    /// again after a superset rebuild: the session values are preferred while they match the
    /// current directory-facts epoch, otherwise they are re-evaluated once and re-cached.
    /// </summary>
    public async Task<DashboardAccess> ResolveAccessAsync(HttpContext http, string token)
    {
        var username = UserKey(http);
        var currentEpoch = _directoryFacts.CurrentEpoch;
        var sessionEpoch = http.Session.GetString("DirectoryFactsEpoch");
        var sessionAdmin = http.Session.GetString("IsActiveRolesAdmin");
        var sessionRole = http.Session.GetString("DashboardRole");

        bool isAdmin;
        DashboardRole role;
        if (sessionEpoch == currentEpoch.ToString()
            && sessionAdmin != null
            && sessionRole != null
            && Enum.TryParse(sessionRole, out DashboardRole parsedRole))
        {
            isAdmin = bool.TryParse(sessionAdmin, out var val) && val;
            role = parsedRole;
        }
        else
        {
            var facts = await _directoryFacts.ResolveAsync(token, username);
            isAdmin = facts.IsActiveRolesAdmin;
            role = facts.Role;

            http.Session.SetString("IsActiveRolesAdmin", facts.IsActiveRolesAdmin.ToString());
            http.Session.SetString("DashboardRole", facts.Role.ToString());
            http.Session.SetString("DirectoryFactsEpoch", facts.Epoch.ToString());
        }

        return new DashboardAccess
        {
            IsActiveRolesAdmin = isAdmin,
            Role = role,
            Permissions = _roleService.GetPermissions(role)
        };
    }

    /// <summary>
    /// Resolves the current viewer's SID set via the service-account token, cached in session.
    /// Returns null for AR admins and when the permission model/service account is unavailable.
    /// </summary>
    public async Task<UserSidSet?> GetViewerSidSetAsync(HttpContext http, DashboardAccess access, CancellationToken ct = default)
    {
        if (access.IsActiveRolesAdmin)
            return null;

        var username = UserKey(http);
        if (string.IsNullOrEmpty(username))
            return null;

        var cached = http.Session.GetString("ViewerSids");
        if (cached != null)
        {
            var sids = JsonSerializer.Deserialize<string[]>(cached) ?? Array.Empty<string>();
            var set = new UserSidSet { Username = username };
            foreach (var sid in sids) set.Sids.Add(sid);
            return set;
        }

        var serviceToken = await _serviceAccountTokens.GetTokenAsync(ct);
        if (string.IsNullOrEmpty(serviceToken))
            return null;

        var resolved = await _permissionModelService.ResolveUserSidSetAsync(serviceToken, username, ct);
        http.Session.SetString("ViewerSids", JsonSerializer.Serialize(resolved.Sids.ToArray()));
        return resolved;
    }

    /// <summary>See <c>DashboardPageModel.CanViewLicensingAsync</c>.</summary>
    public async Task<bool> CanViewLicensingAsync(HttpContext http, DashboardAccess access, CancellationToken ct = default)
    {
        if (access.HasPermission(DashboardPermission.ViewLicensingDashboard))
            return true;

        if (access.UsesFullVisibility)
            return true;

        var model = _cache.PermissionModel;
        if (model is null)
            return true;

        var viewer = await GetViewerSidSetAsync(http, access, ct);
        return viewer is null || model.GrantsLicensingVisibility(viewer);
    }

    /// <summary>See <c>DashboardPageModel.CanViewExchangeAsync</c>.</summary>
    public async Task<bool> CanViewExchangeAsync(HttpContext http, DashboardAccess access, CancellationToken ct = default)
    {
        var token = GetAccessToken(http);
        if (string.IsNullOrEmpty(token))
            return false;

        bool deployed;
        var cachedDeployed = _userSummaryCache.GetExchangeDeployed();
        if (cachedDeployed is bool knownDeployed)
        {
            deployed = knownDeployed;
        }
        else
        {
            deployed = await _arService.IsExchangeDeployedAsync(token);
            _userSummaryCache.SetExchangeDeployed(deployed);
        }

        if (!deployed)
            return false;

        if (access.HasPermission(DashboardPermission.ViewExchangeDashboard))
            return true;

        if (access.UsesFullVisibility)
            return true;

        var cachedMember = http.Session.GetString("IsExchangeAdmin");
        if (cachedMember != null)
            return bool.TryParse(cachedMember, out var v) && v;

        var username = UserKey(http);
        var isMember = !string.IsNullOrEmpty(username)
            && await _arService.IsUserExchangeAdminAsync(token, username);
        http.Session.SetString("IsExchangeAdmin", isMember.ToString());
        return isMember;
    }

    /// <summary>
    /// Loads the FULL (permission-scoped, UNFILTERED) dashboard summary from the shared superset
    /// (or a direct per-user query when the cache is cold), and caches it plus the derived overview
    /// totals per user. The caller applies the active segment filter afterwards.
    /// </summary>
    public async Task<DashboardSummary> LoadFullSummaryAsync(HttpContext http, string token, DashboardAccess access, KpiSettings kpiSettings)
    {
        DashboardSummary summary;
        var superset = _cache.Current?.Summary;
        if (superset is null)
        {
            var fallbackSettings = _userSettingsService.Load(UserKey(http));
            summary = await _arService.GetDashboardSummaryAsync(token, kpiSettings, fallbackSettings);
            summary.ExchangeVisible = await CanViewExchangeAsync(http, access, http.RequestAborted);
        }
        else
        {
            var model = _cache.PermissionModel;
            var viewer = access.UsesFullVisibility ? null : await GetViewerSidSetAsync(http, access, http.RequestAborted);

            summary = (viewer is not null && model is not null)
                ? _perUserFilter.Filter(superset, viewer, model)
                : superset;

            summary.LicensingVisible = access.HasPermission(DashboardPermission.ViewLicensingDashboard)
                || viewer is null || model is null || model.GrantsLicensingVisibility(viewer);

            summary.ExchangeVisible = await CanViewExchangeAsync(http, access, http.RequestAborted);
        }

        var key = UserKey(http);
        _userSummaryCache.SetSummary(key, JsonSerializer.Serialize(summary.ToSessionCacheSafe()));
        _userSummaryCache.SetOverview(key, JsonSerializer.Serialize(new OverviewTotalsCache
        {
            ADUserAccounts = summary.ADUserAccounts,
            ADGroups = summary.ADGroups.WithoutMemberPayload(),
            Computers = summary.Computers,
            EntraTotals = summary.EntraTotals
        }));

        return summary;
    }

    /// <summary>
    /// True when a cached summary still reports Entra membership as pending but the shared
    /// collector has since finished, so the summary should be rebuilt from the superset.
    /// </summary>
    public bool ShouldReconcileMembership(DashboardSummary? summary)
    {
        var totals = summary?.EntraTotals;
        if (totals is null || !totals.MembershipDataPending)
            return false;

        var supersetTotals = _cache.Current?.Summary?.EntraTotals;
        return !_cache.MembershipLoading && supersetTotals is not null && supersetTotals.MembershipLoaded;
    }

    public void ClearCachedSummary(HttpContext http) => _userSummaryCache.Clear(UserKey(http));

    /// <summary>
    /// Main-dashboard load flow (mirrors <c>IndexModel.OnGetAsync</c>): prefer the warm per-user
    /// cache (reconciling pending Entra membership against a completed superset), otherwise load
    /// and cache the full summary. Returns the UNFILTERED summary.
    /// </summary>
    public async Task<DashboardSummary> LoadIndexSummaryAsync(HttpContext http, string token, DashboardAccess access, KpiSettings kpiSettings)
    {
        var cachedJson = GetCachedSummaryJson(http);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            var cachedSummary = JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
            if (!ShouldReconcileMembership(cachedSummary))
                return cachedSummary;

            ClearCachedSummary(http);
        }

        return await LoadFullSummaryAsync(http, token, access, kpiSettings);
    }

    /// <summary>
    /// Applies the session's active segment filter to <paramref name="summary"/> (in place),
    /// capturing available segments from the UNFILTERED summary first.
    /// </summary>
    public SegmentSelectionView ApplyActiveSegmentFilter(HttpContext http, DashboardSummary summary)
    {
        var filter = SegmentFilterSession.Get(http.Session);

        var availableDomains = summary.GetAdDomains();
        var availableTenants = summary.EntraTotals.Tenants
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var view = new SegmentSelectionView
        {
            AvailableDomains = availableDomains,
            AvailableTenants = availableTenants,
            SelectedDomains = filter.DomainSelection.Resolve(availableDomains),
            SelectedTenants = filter.TenantSelection.Resolve(availableTenants)
        };

        summary.ApplySegmentFilter(filter);
        return view;
    }

    /// <summary>
    /// Persists a segment selection for one dimension ("Domain" or "Tenant") to session, preserving
    /// the other. Names are stored raw; an empty selection is stored as an explicit "none".
    /// </summary>
    public SegmentFilterState SetSegmentFilter(HttpContext http, string dimension, IEnumerable<string>? segments)
    {
        var state = SegmentFilterSession.Get(http.Session);
        var selected = (segments ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        switch (dimension)
        {
            case "Domain":
                state.Domains = selected;
                break;
            case "Tenant":
                state.Tenants = selected;
                break;
        }

        SegmentFilterSession.Set(http.Session, state);
        return state;
    }
}
