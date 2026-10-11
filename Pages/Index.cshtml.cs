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

        // The React dashboard (/app) replaces this page. Legacy "back to dashboard" links land
        // here, so forward them and ask the SPA to restore the last dashboard tab viewed.
        return Redirect($"{Request.PathBase}/app/?restore=1");
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

        if (!CanRebuildCache)
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

        if (!CanRebuildCache)
            return new JsonResult(new { authorized = false });

        return new JsonResult(new
        {
            authorized = true,
            sequence = Cache.RefreshSequence,
            refreshing = Cache.State == CacheState.Refreshing,
            failed = Cache.LastRefreshFailed,
            error = Cache.LastError
        });
    }
}
