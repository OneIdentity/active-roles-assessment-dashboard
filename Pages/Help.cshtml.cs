using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Pages;

/// <summary>
/// Basic help page rendered in the IRIS shell. Reuses <see cref="PrivacyModel"/> so the shared
/// shell resolves the sidebar permission set the same way.
/// </summary>
public class HelpModel : PrivacyModel
{
    public HelpModel(RoleService roleService) : base(roleService)
    {
    }
}
