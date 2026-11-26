using System.Globalization;
using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// The on-demand tools shown as tabs on the Tools pages. Each tool is gated by its own permission;
/// tabs are ordered alphabetically by localized title and /Tools lands on the first available tool.
/// Titles resolve from <c>Resources/ToolResources.resx</c> against <see cref="CultureInfo.CurrentUICulture"/>.
/// </summary>
public static class ToolCatalog
{
    public sealed record Tool(
        string Key,
        string Path,
        string TitleKey,
        string FallbackTitle,
        Func<IReadOnlySet<DashboardPermission>, bool, bool> CanUse);

    public static readonly IReadOnlyList<Tool> All =
    [
        new("groupanalyzer", "Tools/GroupAnalyzer", "Tool_GroupAnalyzer", "Group Analyzer", RolePermissionRegistry.CanUseGroupAnalyzer),
        new("permissiontracker", "Tools/PermissionTracker", "Tool_PermissionTracker", "Permission Tracker", RolePermissionRegistry.CanUsePermissionTracker)
    ];

    private static readonly System.Resources.ResourceManager Resources = new(
        "ActiveRolesDashboard.Resources.ToolResources", typeof(ToolCatalog).Assembly);

    public static string Localize(string key, string fallback)
    {
        try
        {
            var value = Resources.GetString(key, CultureInfo.CurrentUICulture);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch (System.Resources.MissingManifestResourceException)
        {
            return fallback;
        }
    }

    public static string TitleOf(Tool tool) => Localize(tool.TitleKey, tool.FallbackTitle);

    /// <summary>Tools the user may run, ordered alphabetically by localized title.</summary>
    public static IReadOnlyList<Tool> Available(IReadOnlySet<DashboardPermission> permissions, bool isActiveRolesAdmin) =>
        All.Where(t => t.CanUse(permissions, isActiveRolesAdmin))
            .OrderBy(TitleOf, StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true))
            .ToList();
}
