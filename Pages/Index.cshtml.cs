using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ActiveRolesDashboard.Pages;

/// <summary>
/// The React dashboard (/app) replaces the legacy Razor dashboards. The site root and any
/// legacy "back to dashboard" links land here and are forwarded to the SPA, which restores
/// the last dashboard tab viewed.
/// </summary>
public class IndexModel : PageModel
{
    public IActionResult OnGet() => Redirect($"{Request.PathBase}/app/?restore=1");
}
