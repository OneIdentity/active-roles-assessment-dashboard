using System;
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

    // Licensed entitlement thresholds surfaced for the totals-vs-thresholds chart.
    // A value of 0 means "not configured" (no threshold line / no breach styling).
    public int LicensedDomainObjects => Math.Max(0, ArConfig.CurrentValue.Licensing.DomainObjects);
    public int LicensedPartitionObjects => Math.Max(0, ArConfig.CurrentValue.Licensing.PartitionObjects);
    public int LicensedAzureObjects => Math.Max(0, ArConfig.CurrentValue.Licensing.AzureObjects);
    public int LicensedSaasObjects => Math.Max(0, ArConfig.CurrentValue.Licensing.SaasObjects);
    public int LicensedTotalObjects => Math.Max(0, ArConfig.CurrentValue.Licensing.TotalObjects);

    // True when at least one threshold is configured, so the view can decide whether to render the chart.
    public bool HasLicensingThresholds =>
        LicensedDomainObjects > 0 || LicensedPartitionObjects > 0 || LicensedAzureObjects > 0
        || LicensedSaasObjects > 0 || LicensedTotalObjects > 0;

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Gate the whole dashboard: only Active Roles admins and viewers granted read on
        // edsManagedObjectStatisticsData (List Object + Read objectClass, or Read all properties)
        // may see Licensing. Everyone else is sent back to the main dashboard.
        if (!await CanViewLicensingAsync(HttpContext.RequestAborted))
            return RedirectToPage("/Index");

        // Load Licensing-specific data using cached totals
        var cachedTotals = GetCachedOverviewTotals();
        var token = GetAccessToken()!;
        var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
        Summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, userSettings, skipOverviewTotals: true, cachedTotals: cachedTotals);
        CacheSummary();

        return Page();
    }
}
