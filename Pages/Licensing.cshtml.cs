using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

public class LicensingModel : DashboardPageModel
{
    public LicensingModel(ActiveRolesService arService, UserSettingsService userSettingsService, IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Load Licensing-specific data using cached totals
        var cachedTotals = GetCachedOverviewTotals();
        var token = GetAccessToken()!;
        var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
        Summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, userSettings, skipOverviewTotals: true, cachedTotals: cachedTotals);
        CacheSummary();

        return Page();
    }
}
