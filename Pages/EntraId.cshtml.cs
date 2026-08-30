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

        // Always prefer the per-user cached summary (built once from the shared superset when the
        // user first hits any dashboard). This applies to normal tile clicks too - not just
        // cached=true filter re-renders - so navigating here never re-runs the slow query.
        var cachedJson = GetCachedSummaryJson();
        if (!string.IsNullOrEmpty(cachedJson))
        {
            Summary = JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
            ApplyActiveSegmentFilter();
            return Page();
        }

        // Cache miss (e.g. first navigation lands here directly, or the per-user cache expired):
        // build the full per-user summary from the shared superset projection and cache it.
        var token = GetAccessToken()!;
        await LoadFullSummaryAsync(token);

        return Page();
    }
}
