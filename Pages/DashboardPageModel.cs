using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

[Authorize]
public abstract class DashboardPageModel : PageModel
{
    protected readonly ActiveRolesService ArService;
    protected readonly UserSettingsService UserSettingsService;
    protected readonly IOptionsMonitor<ActiveRolesConfig> ArConfig;

    protected DashboardPageModel(ActiveRolesService arService, UserSettingsService userSettingsService, IOptionsMonitor<ActiveRolesConfig> arConfig)
    {
        ArService = arService;
        UserSettingsService = userSettingsService;
        ArConfig = arConfig;
    }

    // Cross-cutting cache/permission infrastructure is resolved from the request container so the
    // nine derived page-model constructors don't each have to thread these dependencies through.
    protected DashboardCacheHolder Cache => HttpContext.RequestServices.GetRequiredService<DashboardCacheHolder>();
    protected PerUserDashboardFilter PerUserFilter => HttpContext.RequestServices.GetRequiredService<PerUserDashboardFilter>();
    protected ServiceAccountTokenProvider ServiceAccountTokens => HttpContext.RequestServices.GetRequiredService<ServiceAccountTokenProvider>();
    protected ArPermissionModelService PermissionModelService => HttpContext.RequestServices.GetRequiredService<ArPermissionModelService>();

    // Per-user server-side cache for the large summary/overview blobs (previously held in Session).
    protected PerUserSummaryCache UserSummaryCache => HttpContext.RequestServices.GetRequiredService<PerUserSummaryCache>();

    /// <summary>Cache key for the current authenticated user's per-user summary/overview blobs.</summary>
    protected string UserCacheKey => User.Identity?.Name ?? string.Empty;

    /// <summary>Gets the cached full dashboard summary JSON for the current user, or null if absent.</summary>
    protected string? GetCachedSummaryJson() => UserSummaryCache.GetSummary(UserCacheKey);

    public DashboardSummary Summary { get; set; } = new();
    public KpiSettings KpiSettings { get; set; } = new();
    public int AutoRefreshMinutes { get; set; }
    public string WebInterfaceUrl => ArConfig.CurrentValue.WebInterfaceUrl;
    public int StaleAccountThresholdDays => ArConfig.CurrentValue.StaleAccountThresholdDays > 0 ? ArConfig.CurrentValue.StaleAccountThresholdDays : 90;
    public bool IsActiveRolesAdmin { get; set; }

    /// <summary>Number of groups the client requests per lazy-membership batch (min 1).</summary>
    public int MembershipBatchSize => Math.Max(1, ArConfig.CurrentValue.Entra.MembershipBatchSize);

    /// <summary>Delay in ms before the membership start toast is shown (min 0).</summary>
    public int MembershipToastDelayMs => Math.Max(0, ArConfig.CurrentValue.Entra.MembershipToastDelayMs);

    public int LargeGroupMemberThreshold => Math.Max(1, ArConfig.CurrentValue.Entra.LargeGroupMemberThreshold);

    /// <summary>
    /// True when the shared superset collector is actively loading Entra group membership. When
    /// this is set, the header badge should reflect the server-side collection progress and the
    /// client-side batch loader must stay idle (the server is doing the loading). Consumed by
    /// _EntraMembershipConfig.cshtml / dashboard.js.
    /// </summary>
    public bool ServerMembershipLoading => Cache.MembershipLoading;

    /// <summary>Total Entra groups in the in-progress server-side membership collection.</summary>
    public int ServerMembershipTotal => Cache.MembershipTotalCount;

    /// <summary>Groups whose membership the server-side collection has loaded so far.</summary>
    public int ServerMembershipLoaded => Math.Min(Cache.MembershipTotalCount, Cache.MembershipLoadedCount);

    /// <summary>
    /// Shared guard surfaced to views: true when <see cref="Summary"/>'s Entra group membership
    /// is still loading, so membership-dependent Entra Groups KPIs may be inaccurate. Used by the
    /// Snapshots, Assessments, and MITRE Exposure pages to render a staleness warning.
    /// </summary>
    public bool EntraMembershipDataPending => Summary?.EntraMembershipDataPending ?? false;

    /// <summary>Warning text paired with <see cref="EntraMembershipDataPending"/>.</summary>
    public string EntraMembershipPendingWarning => DashboardSummary.EntraMembershipPendingWarning;

    /// <summary>
    /// Whether this page hosts the global segment-filter dropdown. Only the main dashboard
    /// sets this true; the AD/Entra dashboards honour the selection but do not render the control.
    /// </summary>
    public bool ShowSegmentFilter { get; set; }

    /// <summary>All AD domains available to filter on (derived from the UNFILTERED summary).</summary>
    public IReadOnlyList<string> AvailableDomains { get; set; } = Array.Empty<string>();

    /// <summary>The currently effective (resolved) AD domain selection, echoed back to the UI.</summary>
    public IReadOnlyList<string> SelectedDomains { get; set; } = Array.Empty<string>();

    /// <summary>All Entra tenants available to filter on (derived from the UNFILTERED summary).</summary>
    public IReadOnlyList<string> AvailableTenants { get; set; } = Array.Empty<string>();

    /// <summary>The currently effective (resolved) Entra tenant selection, echoed back to the UI.</summary>
    public IReadOnlyList<string> SelectedTenants { get; set; } = Array.Empty<string>();

    /// <summary>True when the global segment filter should be shown (this page hosts it and there is at least one segment to choose from).</summary>
    public bool SegmentFilterEnabled => ShowSegmentFilter && (AvailableDomains.Count > 0 || AvailableTenants.Count > 0);

    /// <summary>
    /// Initializes common page state (settings, token validation, admin flag).
    /// Returns null if successful, or a redirect result if the session is invalid.
    /// </summary>
    protected async Task<IActionResult?> InitializePageAsync()
    {
        var username = User.Identity?.Name ?? "";
        var userSettings = UserSettingsService.Load(username);

        AutoRefreshMinutes = userSettings.AutoRefreshMinutes;
        KpiSettings = userSettings.KpiSettings;

        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }

        var adminFlag = HttpContext.Session.GetString("IsActiveRolesAdmin");
        if (adminFlag != null)
        {
            IsActiveRolesAdmin = bool.TryParse(adminFlag, out var val) && val;
        }
        else
        {
            // Prefer the app-level per-user cache (survives logout/login) so the directory
            // membership check runs at most once per cache lifetime, not on every login.
            var cachedAdmin = UserSummaryCache.GetAdmin(username);
            if (cachedAdmin is bool known)
            {
                IsActiveRolesAdmin = known;
            }
            else
            {
                IsActiveRolesAdmin = await ArService.IsUserActiveRolesAdminAsync(token, username);
                UserSummaryCache.SetAdmin(username, IsActiveRolesAdmin);
            }

            HttpContext.Session.SetString("IsActiveRolesAdmin", IsActiveRolesAdmin.ToString());
        }

        return null;
    }

    protected string? GetAccessToken() => HttpContext.Session.GetString("AccessToken");

    /// <summary>
    /// Resolves the current viewer's SID set (own SID + nested group SIDs) used to filter the shared
    /// superset per user. Resolved once via the SERVICE-ACCOUNT token (end-user tokens cannot read
    /// this) and cached in session as a JSON SID array. Returns null for AR admins (who bypass
    /// filtering) and when the permission model or service account is unavailable.
    /// </summary>
    protected async Task<UserSidSet?> GetViewerSidSetAsync(CancellationToken ct = default)
    {
        if (IsActiveRolesAdmin)
            return null;

        var username = User.Identity?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(username))
            return null;

        var cached = HttpContext.Session.GetString("ViewerSids");
        if (cached != null)
        {
            var sids = JsonSerializer.Deserialize<string[]>(cached) ?? Array.Empty<string>();
            var set = new UserSidSet { Username = username };
            foreach (var sid in sids) set.Sids.Add(sid);
            return set;
        }

        var serviceToken = await ServiceAccountTokens.GetTokenAsync(ct);
        if (string.IsNullOrEmpty(serviceToken))
            return null;

        var resolved = await PermissionModelService.ResolveUserSidSetAsync(serviceToken, username, ct);
        HttpContext.Session.SetString("ViewerSids", JsonSerializer.Serialize(resolved.Sids.ToArray()));
        return resolved;
    }

    /// <summary>
    /// Determines whether the current viewer may see the Licensing dashboard: Active Roles admins
    /// always may; any other viewer must be granted read (List Object + Read objectClass, or Read
    /// all properties) on <c>edsManagedObjectStatisticsData</c> in the Active Roles permission
    /// model. Falls back to <c>true</c> when the shared permission model / SID set is unavailable
    /// (cache-cold direct queries are already scoped by the caller's own AR permissions).
    /// </summary>
    protected async Task<bool> CanViewLicensingAsync(CancellationToken ct = default)
    {
        if (IsActiveRolesAdmin)
            return true;

        var model = Cache.PermissionModel;
        if (model is null)
            return true;

        var viewer = await GetViewerSidSetAsync(ct);
        return viewer is null || model.GrantsLicensingVisibility(viewer);
    }

    /// <summary>
    /// Determines whether the current viewer may see the Exchange dashboard. Two conditions must
    /// both hold: (1) the organization has Exchange deployed (at least one
    /// <c>msExchExchangeServer</c> that is not a transport-only server), and (2) the viewer is an
    /// Active Roles admin OR a member of an Exchange administrative security group ("Organization
    /// Management" / "View-Only Organization Management"). All other users are denied.
    ///
    /// The org-wide deployment signal and the per-user membership result are cached (deployment in
    /// the app-level cache, membership in session) so the directory lookups run at most once per
    /// cache lifetime rather than on every page load.
    /// </summary>
    protected async Task<bool> CanViewExchangeAsync(CancellationToken ct = default)
    {
        var token = GetAccessToken();
        if (string.IsNullOrEmpty(token))
            return false;

        // (1) Org-wide precondition: Exchange must be deployed. Cache the result app-wide.
        bool deployed;
        var cachedDeployed = UserSummaryCache.GetExchangeDeployed();
        if (cachedDeployed is bool knownDeployed)
        {
            deployed = knownDeployed;
        }
        else
        {
            deployed = await ArService.IsExchangeDeployedAsync(token);
            UserSummaryCache.SetExchangeDeployed(deployed);
        }

        if (!deployed)
            return false;

        // (2) Active Roles admins may always see it once deployed.
        if (IsActiveRolesAdmin)
            return true;

        // Otherwise the viewer must be a member of an Exchange administrative group. Cache the
        // per-user result in session so the membership lookup runs at most once per session.
        var cachedMember = HttpContext.Session.GetString("IsExchangeAdmin");
        if (cachedMember != null)
            return bool.TryParse(cachedMember, out var v) && v;

        var username = User.Identity?.Name ?? string.Empty;
        var isMember = !string.IsNullOrEmpty(username)
            && await ArService.IsUserExchangeAdminAsync(token, username);
        HttpContext.Session.SetString("IsExchangeAdmin", isMember.ToString());
        return isMember;
    }

    /// <summary>
    /// Applies the session's active segment (domain/tenant) filter to <see cref="Summary"/>.
    /// Call this AFTER caching the unfiltered summary so the cache can be re-filtered when
    /// the selection changes without re-querying Active Roles. Rendering, export, and any
    /// other consumer therefore share one filtering rule.
    ///
    /// Captures the full set of available segments (from the UNFILTERED summary) and the
    /// resolved effective selection so the filter UI can render options and check state.
    /// Which dimension is captured is driven by <see cref="SegmentDimension"/>.
    /// </summary>
    protected void ApplyActiveSegmentFilter()
    {
        var filter = SegmentFilterSession.Get(HttpContext.Session);

        // Capture available segments (both dimensions) from the unfiltered summary BEFORE
        // reducing it, otherwise the lists would collapse to the current selection and could
        // never widen. The filter is global, so both dimensions are always captured.
        AvailableDomains = Summary.GetAdDomains();
        AvailableTenants = Summary.EntraTotals.Tenants
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SelectedDomains = filter.DomainSelection.Resolve(AvailableDomains);
        SelectedTenants = filter.TenantSelection.Resolve(AvailableTenants);

        Summary.ApplySegmentFilter(filter);
    }

    /// <summary>
    /// Caches the current <see cref="Summary"/> in session so features such as
    /// export can reuse it without re-querying Active Roles.
    /// </summary>
    protected void CacheSummary() =>
        UserSummaryCache.SetSummary(UserCacheKey, JsonSerializer.Serialize(Summary.ToSessionCacheSafe()));

    /// <summary>
    /// Lazily loads Entra group membership (the <c>member</c> attribute) and owner
    /// (<c>edsaAzureGroupManagedBy</c>) for the cached, unfiltered Entra totals, in parallel,
    /// then recomputes the three membership-dependent Entra Groups hygiene KPIs (Empty Groups,
    /// No Group Owner, Guest-Containing Groups) and returns them as JSON. The updated totals
    /// (with membership merged in) are written back to the session caches so back-navigation
    /// and export see the enriched data. Shared by every dashboard page that renders the Entra
    /// Groups panels; invoked by the dashboard's lazy loader on page load and refresh.
    /// </summary>
    public async Task<IActionResult> OnGetEntraMembershipAsync()
    {
        var token = GetAccessToken();
        if (string.IsNullOrEmpty(token))
            return new JsonResult(new { error = "Not authenticated." }) { StatusCode = 401 };

        var totals = LoadUnfilteredEntraTotals();
        if (totals == null)
            return new JsonResult(new { error = "No cached dashboard data. Reload the dashboard." }) { StatusCode = 409 };

        try
        {
            await ArService.LoadEntraGroupMembershipAsync(token, totals);
        }
        catch (Exception ex)
        {
            return new JsonResult(new { error = $"Membership load failed ({ex.GetType().Name}: {ex.Message})." })
            { StatusCode = 500 };
        }

        PersistEntraTotals(totals);

        return new JsonResult(new
        {
            emptyGroups = ToKpiPayload(totals.EntraEmptyGroups()),
            noGroupOwner = ToKpiPayload(totals.EntraNoGroupOwnerGroups()),
            guestContaining = ToKpiPayload(totals.EntraGuestContainingGroups()),
            singleOwner = ToKpiPayload(totals.EntraSingleOwnerGroups()),
            largeGroups = ToKpiPayload(totals.EntraLargeGroups(ArConfig.CurrentValue.Entra.LargeGroupMemberThreshold))
        });
    }

    /// <summary>
    /// Reports live server-side Entra membership-collection progress
    /// collector, so the client can render a real countdown badge when a user logs in WHILE the
    /// superset is still loading membership (before the atomic snapshot publish). Only meaningful
    /// for viewers who can see Entra; non-admins never render the badge. Returns whether the
    /// collector is actively loading, the total group count, how many are loaded, and how many
    /// remain.
    /// </summary>
    public IActionResult OnGetEntraMembershipProgress()
    {
        var loading = Cache.MembershipLoading;
        var total = Cache.MembershipTotalCount;
        var loaded = Math.Min(total, Cache.MembershipLoadedCount);
        var remaining = Math.Max(0, total - loaded);

        return new JsonResult(new
        {
            serverLoading = loading,
            totalGroups = total,
            loadedCount = loaded,
            remaining,
            done = !loading && remaining == 0
        });
    }

    /// <summary>
    /// Batched variant of <see cref="OnGetEntraMembershipAsync"/>. Loads Entra group
    /// membership for a single window of groups (<paramref name="skip"/>..<paramref name="skip"/>+
    /// <paramref name="take"/>), persists the progressively-enriched totals to session, and
    /// returns the cumulative KPI payloads (recomputed from every group loaded so far) plus
    /// the total group count and how many groups still remain. The client's lazy loader calls
    /// this repeatedly to drive the header progress badge and progressively fill the panels.
    /// </summary>
    public async Task<IActionResult> OnGetEntraMembershipBatchAsync(int skip = 0, int take = 0)
    {
        var token = GetAccessToken();
        if (string.IsNullOrEmpty(token))
            return new JsonResult(new { error = "Not authenticated." }) { StatusCode = 401 };

        var totals = LoadUnfilteredEntraTotals();
        if (totals == null)
            return new JsonResult(new { error = "No cached dashboard data. Reload the dashboard." }) { StatusCode = 409 };

        if (take <= 0)
            take = Math.Max(1, ArConfig.CurrentValue.Entra.MembershipBatchSize);

        int totalGroups;
        try
        {
            totalGroups = await ArService.LoadEntraGroupMembershipAsync(token, totals, skip, take);
        }
        catch (Exception ex)
        {
            return new JsonResult(new { error = $"Membership load failed ({ex.GetType().Name}: {ex.Message})." })
            { StatusCode = 500 };
        }

        PersistEntraTotals(totals);

        var loaded = Math.Min(totalGroups, totals.MembershipLoadedCount);
        var remaining = Math.Max(0, totalGroups - loaded);

        return new JsonResult(new
        {
            totalGroups,
            loadedCount = loaded,
            remaining,
            done = remaining == 0,
            emptyGroups = ToKpiPayload(totals.EntraEmptyGroups()),
            noGroupOwner = ToKpiPayload(totals.EntraNoGroupOwnerGroups()),
            guestContaining = ToKpiPayload(totals.EntraGuestContainingGroups()),
            singleOwner = ToKpiPayload(totals.EntraSingleOwnerGroups()),
            largeGroups = ToKpiPayload(totals.EntraLargeGroups(ArConfig.CurrentValue.Entra.LargeGroupMemberThreshold))
        });
    }

    private static object ToKpiPayload(EntraGroupDetailSummary summary) => new
    {
        error = summary.Error,
        totalCount = summary.TotalCount,
        items = summary.Items.Select(i => new { name = i.Name, tenant = i.Tenant, dn = i.Dn })
    };

    /// <summary>
    /// Reads the UNFILTERED Entra totals from session cache, preferring the full dashboard
    /// summary and falling back to the lighter overview totals. Returns null when neither is cached.
    /// </summary>
    private EntraTotalsSummary? LoadUnfilteredEntraTotals()
    {
        var summaryJson = GetCachedSummaryJson();
        if (!string.IsNullOrEmpty(summaryJson))
        {
            var full = JsonSerializer.Deserialize<DashboardSummary>(summaryJson);
            if (full?.EntraTotals != null)
                return full.EntraTotals;
        }

        return GetCachedOverviewTotals()?.EntraTotals;
    }

    /// <summary>
    /// Writes the membership-enriched Entra totals back into both session caches so that
    /// back-navigation (cached=true) and export reuse the enriched data.
    /// </summary>
    private void PersistEntraTotals(EntraTotalsSummary totals)
    {
        var summaryJson = GetCachedSummaryJson();
        if (!string.IsNullOrEmpty(summaryJson))
        {
            var full = JsonSerializer.Deserialize<DashboardSummary>(summaryJson);
            if (full != null)
            {
                full.EntraTotals = totals;
                UserSummaryCache.SetSummary(UserCacheKey, JsonSerializer.Serialize(full));
            }
        }

        var overviewJson = UserSummaryCache.GetOverview(UserCacheKey);
        if (!string.IsNullOrEmpty(overviewJson))
        {
            var overview = JsonSerializer.Deserialize<OverviewTotalsCache>(overviewJson);
            if (overview != null)
            {
                overview.EntraTotals = totals;
                UserSummaryCache.SetOverview(UserCacheKey, JsonSerializer.Serialize(overview));
            }
        }
    }

    /// <summary>
    /// Loads overview totals (ADUserAccounts, ADGroups, Computers) and caches them in session.
    /// </summary>
    protected async Task LoadOverviewTotalsAsync(string token)
    {
        Summary.ADUserAccounts = await ArService.GetADUserAccountsCountAsync(token);
        Summary.ADGroups = await ArService.GetADGroupsAsync(token);
        Summary.Computers = await ArService.GetComputersAsync(token);
        Summary.EntraTotals = await ArService.GetEntraTotalsAsync(token);

        UserSummaryCache.SetOverview(UserCacheKey, JsonSerializer.Serialize(new OverviewTotalsCache
        {
            ADUserAccounts = Summary.ADUserAccounts,
            ADGroups = Summary.ADGroups.WithoutMemberPayload(),
            Computers = Summary.Computers,
            EntraTotals = Summary.EntraTotals
        }));

        // Cache stores unfiltered totals; apply the active selection for rendering.
        ApplyActiveSegmentFilter();
    }

    /// <summary>
    /// Loads the FULL dashboard summary (all dashboards/KPIs) once and caches it in session
    /// under both "DashboardSummary" (for export and sub-dashboard reuse) and "OverviewTotals"
    /// (for the main dashboard's fast back-navigation). Used to pre-warm all in-scope data at
    /// login/refresh so subsequent exports and dashboard views are served from cache.
    /// </summary>
    protected async Task LoadFullSummaryAsync(string token)
    {
        var superset = Cache.Current?.Summary;
        if (superset is null)
        {
            // Shared cache not yet ready: fall back to a direct per-user query so the page still
            // renders (already correctly scoped by the caller's own Active Roles permissions).
            var fallbackSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
            Summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, fallbackSettings);

            // Even on the cache-cold path, resolve Exchange visibility so the tile renders correctly.
            Summary.ExchangeVisible = await CanViewExchangeAsync(HttpContext.RequestAborted);
        }
        else
        {
            // Serve from the shared service-account superset. Admins see the unfiltered data;
            // everyone else sees a per-user projection scoped to their AR delegation.
            var model = Cache.PermissionModel;
            var viewer = IsActiveRolesAdmin ? null : await GetViewerSidSetAsync(HttpContext.RequestAborted);

            Summary = (viewer is not null && model is not null)
                ? PerUserFilter.Filter(superset, viewer, model)
                : superset;

            // The Licensing dashboard is gated on read access to edsManagedObjectStatisticsData.
            // Admins (and the cache-cold fallback above) keep the default true; a non-admin viewer
            // must be granted List Object + Read objectClass (or Read all properties) on that class.
            Summary.LicensingVisible = viewer is null || model is null || model.GrantsLicensingVisibility(viewer);

            // The Exchange dashboard is gated on Exchange being deployed AND the viewer being an
            // Active Roles admin or a member of an Exchange administrative group. CanViewExchangeAsync
            // encapsulates both conditions (and caches the directory lookups).
            Summary.ExchangeVisible = await CanViewExchangeAsync(HttpContext.RequestAborted);
        }

        // Cache the (already permission-scoped) summary for export and sub-dashboard reuse.
        CacheSummary();

        // Also cache the overview totals derived from the full summary so the main
        // dashboard's cached back-navigation path keeps working.
        UserSummaryCache.SetOverview(UserCacheKey, JsonSerializer.Serialize(new OverviewTotalsCache
        {
            ADUserAccounts = Summary.ADUserAccounts,
            ADGroups = Summary.ADGroups.WithoutMemberPayload(),
            Computers = Summary.Computers,
            EntraTotals = Summary.EntraTotals
        }));

        // Cache stores unfiltered data; apply the active selection for rendering.
        ApplyActiveSegmentFilter();
    }

    /// <summary>
    /// Restores overview totals from session cache into Summary.
    /// Returns true if cached totals were found.
    /// </summary>
    protected bool RestoreOverviewTotalsFromCache()
    {
        var totals = GetCachedOverviewTotals();
        if (totals == null) return false;

        Summary.ADUserAccounts = totals.ADUserAccounts;
        Summary.ADGroups = totals.ADGroups;
        Summary.Computers = totals.Computers;
        Summary.EntraTotals = totals.EntraTotals;

        // Cached totals are unfiltered; apply the active selection for rendering.
        ApplyActiveSegmentFilter();
        return true;
    }

    /// <summary>
    /// Gets the cached overview totals object, or null if not cached.
    /// </summary>
    protected OverviewTotalsCache? GetCachedOverviewTotals()
    {
        var cached = UserSummaryCache.GetOverview(UserCacheKey);
        if (string.IsNullOrEmpty(cached)) return null;
        return JsonSerializer.Deserialize<OverviewTotalsCache>(cached);
    }

    /// <summary>
    /// Persists a segment (domain/tenant) filter selection to session and redirects back
    /// to the current dashboard, re-rendering from cache (no re-query). The posted names
    /// are stored raw; resolution ("unset ⇒ all", "explicit empty ⇒ none") happens at the
    /// choke points. Only the dimension being changed is updated, preserving the other.
    /// An empty selection for the posted dimension is stored as an explicit "none".
    /// </summary>
    public IActionResult OnPostSetSegmentFilter(string dimension, string returnPage, List<string>? segments)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
            return RedirectToPage("/Login");

        var state = SegmentFilterSession.Get(HttpContext.Session);
        var selected = (segments ?? new List<string>())
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

        SegmentFilterSession.Set(HttpContext.Session, state);

        var page = string.IsNullOrWhiteSpace(returnPage) ? "/Index" : returnPage;
        return RedirectToPage(page, new { cached = true });
    }

    public virtual async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        var token = GetAccessToken()!;

        if (cached)
        {
            var cachedJson = GetCachedSummaryJson();
            if (!string.IsNullOrEmpty(cachedJson))
            {
                Summary = JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
                ApplyActiveSegmentFilter();
                return Page();
            }
        }

        // Serve from the shared permission-scoped cache (falls back to a direct per-user query
        // when the cache is not yet warm). LoadFullSummaryAsync also caches and segment-filters.
        await LoadFullSummaryAsync(token);

        return Page();
    }
}
