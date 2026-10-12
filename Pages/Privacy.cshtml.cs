using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ActiveRolesDashboard.Pages;

public class PrivacyModel : PageModel
{
    private readonly RoleService _roleService;

    public PrivacyModel(RoleService roleService)
    {
        _roleService = roleService;
    }

    /// <summary>
    /// Permission set used by the shared IRIS shell to render the sidebar. Taken from the role
    /// already resolved for this session; unresolved sessions get no permission-gated entries.
    /// </summary>
    public IReadOnlySet<DashboardPermission> Permissions { get; private set; } = new HashSet<DashboardPermission>();

    public void OnGet()
    {
        if (User.Identity?.IsAuthenticated == true
            && Enum.TryParse<DashboardRole>(HttpContext.Session.GetString("DashboardRole"), out var role))
        {
            Permissions = _roleService.GetPermissions(role);
        }
    }
}

