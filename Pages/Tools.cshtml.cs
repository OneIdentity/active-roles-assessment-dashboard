using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

/// <summary>
/// Tools landing page: lists the on-demand analysis tools the user may run. Each tool is gated by its own
/// permission (see <see cref="RolePermissionRegistry.ToolPermissions"/>); the page itself is reachable only
/// when at least one tool is available.
/// </summary>
public class ToolsModel : DashboardPageModel
{
    public ToolsModel(
        ActiveRolesService arService,
        UserSettingsService userSettingsService,
        IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Block direct navigation for users holding no tool permission.
        var first = ToolCatalog.Available(DashboardPermissions, IsActiveRolesAdmin).FirstOrDefault();
        if (!CanViewTools || first == null)
            return Redirect($"{Request.PathBase}/app/");

        // No overview: land directly on the first tool (tabs are ordered alphabetically).
        return Redirect($"{Request.PathBase}/{first.Path}");
    }
}
