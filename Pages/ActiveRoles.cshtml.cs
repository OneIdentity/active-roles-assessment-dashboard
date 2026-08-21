using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

public class ActiveRolesModel : DashboardPageModel
{
    public ActiveRolesModel(ActiveRolesService arService, UserSettingsService userSettingsService, IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Restore cached totals for derivation (e.g. GroupFamilies from ADGroups)
        var cachedTotals = GetCachedOverviewTotals();

        // Load AR Configuration-specific data using cached totals
        var token = GetAccessToken()!;
        var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
        Summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, userSettings, skipOverviewTotals: true, cachedTotals: cachedTotals);
        CacheSummary();

        return Page();
    }
}
