using System.Threading.Tasks;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

public class ExchangeModel : DashboardPageModel
{
    public ExchangeModel(ActiveRolesService arService, UserSettingsService userSettingsService, IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Gate the whole dashboard: it is only visible when Exchange is deployed (at least one
        // msExchExchangeServer that is not a transport-only server) AND the viewer holds the View
        // Exchange dashboard permission, is an Active Roles admin, or is a member of an Exchange
        // administrative security group ("Organization Management" / "View-Only Organization
        // Management"). Everyone else is sent back to the main dashboard so the page cannot be
        // reached by navigating directly to the URL.
        if (!await CanViewExchangeAsync(HttpContext.RequestAborted))
            return RedirectToPage("/Index");

        // Serve from the shared, already-collected superset (admins: unfiltered; others: per-user
        // projection), matching the other child dashboards.
        var token = GetAccessToken()!;
        await LoadFullSummaryAsync(token);

        return Page();
    }
}
