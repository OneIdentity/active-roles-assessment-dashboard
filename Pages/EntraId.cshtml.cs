using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

public class EntraIdModel : DashboardPageModel
{
    public EntraIdModel(ActiveRolesService arService, UserSettingsService userSettingsService, IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // The Entra dashboard honours the global segment selection (by tenant) but does not
        // host the filter dropdown; that lives on the main dashboard Overview.

        // When re-rendering after a filter change, reuse the cached (unfiltered) summary
        // instead of re-querying Active Roles.
        if (cached)
        {
            var cachedJson = HttpContext.Session.GetString("DashboardSummary");
            if (!string.IsNullOrEmpty(cachedJson))
            {
                Summary = JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
                ApplyActiveSegmentFilter();
                return Page();
            }
        }

        // Load Entra ID-specific data using cached totals
        var cachedTotals = GetCachedOverviewTotals();
        var token = GetAccessToken()!;
        var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
        Summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, userSettings, skipOverviewTotals: true, cachedTotals: cachedTotals);
        CacheSummary();
        ApplyActiveSegmentFilter();

        return Page();
    }
}
