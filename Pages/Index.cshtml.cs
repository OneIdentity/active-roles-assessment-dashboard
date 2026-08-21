using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
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

        // If navigating back (cached=true), reuse cached data without re-fetching. Prefer the
        // full cached summary, falling back to the lighter overview totals.
        if (cached)
        {
            var cachedJson = HttpContext.Session.GetString("DashboardSummary");
            if (!string.IsNullOrEmpty(cachedJson))
            {
                Summary = JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
                ApplyActiveSegmentFilter();
                return Page();
            }

            if (RestoreOverviewTotalsFromCache())
                return Page();
        }

        // Fresh load or refresh: pre-warm the full dashboard data for all in-scope
        // dashboards so subsequent exports and sub-dashboard views are served from cache.
        await LoadFullSummaryAsync(token);

        return Page();
    }
}
