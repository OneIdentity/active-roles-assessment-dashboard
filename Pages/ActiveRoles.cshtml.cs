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

        // Serve from the shared, already-collected superset (admins: unfiltered; others: per-user
        // projection) instead of re-querying Active Roles for every KPI on each visit. This also
        // keeps membership-dependent KPIs (e.g. Circular Group Nesting) accurate, since the
        // superset retains the `member` payload that the cached overview totals strip.
        var token = GetAccessToken()!;
        await LoadFullSummaryAsync(token);

        return Page();
    }
}
