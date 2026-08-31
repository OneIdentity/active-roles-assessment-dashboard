using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

public class IndexModel : DashboardPageModel
{
    public IndexModel(ActiveRolesService arService, UserSettingsService userSettingsService, IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // The main dashboard hosts the global segment-filter dropdown.
        ShowSegmentFilter = true;

        var token = GetAccessToken()!;

        // Always prefer the warm per-user cache (built once from the shared superset). This is
        // keyed by username and survives logout, so a re-login within the cache lifetime reloads
        // instantly instead of re-running the (potentially slow) per-user projection. The cached=true
        // flag only matters as a hint for the lighter overview-totals fallback below.
        var cachedJson = GetCachedSummaryJson();
        if (!string.IsNullOrEmpty(cachedJson))
        {
            Summary = JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
            ApplyActiveSegmentFilter();
            return Page();
        }

        if (cached && RestoreOverviewTotalsFromCache())
            return Page();

        // Fresh load or refresh: pre-warm the full dashboard data for all in-scope
        // dashboards so subsequent exports and sub-dashboard views are served from cache.
        await LoadFullSummaryAsync(token);

        return Page();
    }

    /// <summary>
    /// Admin-only manual refresh of the shared service-account superset. Signals the background
    /// loader to rebuild the cache, clears this session's cached summary so the next render reflects
    /// fresh data, and redirects back to the main dashboard where the "Building cache…" state shows.
    /// Other dashboards keep their existing (unchanged) refresh behaviour.
    /// </summary>
    public async Task<IActionResult> OnPostRefreshAsync()
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        if (!IsActiveRolesAdmin)
            return Forbid();

        HttpContext.RequestServices
            .GetRequiredService<SupersetLoaderHostedService>()
            .TriggerManualRefresh();

        // Drop the per-session cached summary so the next load rebuilds from the refreshed cache.
        UserSummaryCache.Clear(UserCacheKey);

        // Capture the refresh sequence at trigger time. The client polls OnGetRefreshStatus until
        // the sequence advances, then shows a success or error toast based on the outcome. Passing
        // it on the redirect lets the reloaded page know a background refresh is in flight.
        var seq = Cache.RefreshSequence;
        return RedirectToPage("/Index", new { refreshFrom = seq });
    }

    /// <summary>
    /// Lightweight polling endpoint used by the main dashboard after an admin triggers a manual
    /// refresh. Returns the current refresh sequence and whether the last completed refresh failed,
    /// so the client can surface a success or error toast without reloading. Admin-only.
    /// </summary>
    public async Task<IActionResult> OnGetRefreshStatusAsync()
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return new JsonResult(new { authorized = false });

        if (!IsActiveRolesAdmin)
            return new JsonResult(new { authorized = false });

        return new JsonResult(new
        {
            authorized = true,
            sequence = Cache.RefreshSequence,
            failed = Cache.LastRefreshFailed,
            error = Cache.LastError
        });
    }
}
