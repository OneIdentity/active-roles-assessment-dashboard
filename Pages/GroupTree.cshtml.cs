using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

// Isolated trial page for nested group membership. Intentionally NOT linked from
// the main navigation; reachable only by URL (/GroupTree).
public class GroupTreeModel : DashboardPageModel
{
    public GroupTreeModel(ActiveRolesService arService, UserSettingsService userSettingsService, IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    [BindProperty(SupportsGet = true)]
    public string? Group { get; set; }

    // Optional dashboard panel id to return to (e.g. "panel-globalgroups").
    [BindProperty(SupportsGet = true)]
    public string? Return { get; set; }

    // Optional dashboard page to return to (e.g. "ActiveDirectory" or "ActiveRoles").
    [BindProperty(SupportsGet = true)]
    public string? ReturnPage { get; set; }

    public string? RootDn { get; set; }
    public string? RootName { get; set; }
    public string? LookupError { get; set; }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Populate Summary so the shared group-membership loading badge/toast (driven by the
        // _EntraMembershipConfig partial + dashboard.js) can render and resume background
        // loading here too. Prefer the session-cached (already per-user-scoped) dashboard summary
        // to avoid re-querying Active Roles; otherwise build it via the shared superset projection
        // so the same per-user visibility model as the main dashboard applies here.
        var cachedJson = HttpContext.Session.GetString("DashboardSummary");
        if (!string.IsNullOrEmpty(cachedJson))
        {
            Summary = System.Text.Json.JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
        }
        else
        {
            var summaryToken = GetAccessToken()!;
            await LoadFullSummaryAsync(summaryToken);
        }

        if (!string.IsNullOrWhiteSpace(Group))
        {
            var token = GetAccessToken()!;
            try
            {
                var group = await ArService.ResolveGroupAsync(token, Group.Trim());
                if (group == null)
                {
                    LookupError = $"No group found matching '{Group}'.";
                }
                else
                {
                    RootName = group.Value.Name;
                    RootDn = group.Value.Dn;
                }
            }
            catch (Exception ex)
            {
                LookupError = $"Lookup failed ({ex.GetType().Name}: {ex.Message}).";
            }
        }

        return Page();
    }

    // Named handler: /GroupTree?handler=Expand&dn=...&depth=...
    public async Task<IActionResult> OnGetExpandAsync(string dn, int depth)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        var token = GetAccessToken()!;
        var maxDepth = ArConfig.CurrentValue.MaxGroupTreeDepth;

        // The clicked node itself is an ancestor for cycle detection within this branch.
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { dn };
        var children = await ArService.ExpandGroupChildrenAsync(token, dn, depth, maxDepth, ancestors);

        return Partial("_GroupTreeNodes", children);
    }
}
