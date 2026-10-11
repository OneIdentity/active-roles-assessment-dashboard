using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActiveRolesDashboard.Controllers;

/// <summary>
/// REST API that backs the Angular main dashboard (the former Razor <c>Pages/Index.cshtml</c>).
/// All loading, permission, caching and segment-filter behaviour is shared with the Razor
/// dashboards through <see cref="DashboardDataService"/>; this controller only shapes the
/// result into a typed payload (overview KPI tiles, overview charts, navigation tiles,
/// segment-filter state and the caller's capabilities).
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    /// <summary>Named chart colour tokens → hex (kept in sync with <c>wwwroot/js/dashboard.js</c>).</summary>
    private static readonly IReadOnlyDictionary<string, string> ChartPalette =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = "#2563eb",
            ["green"] = "#16a34a",
            ["purple"] = "#7c3aed",
            ["teal"] = "#0d9488",
            ["amber"] = "#d97706",
            ["pink"] = "#db2777",
            ["slate"] = "#475569",
            ["red"] = "#dc2626",
            ["orange"] = "#ea580c",
            ["indigo"] = "#4f46e5"
        };

    private const string FallbackChartColor = "#94a3b8";

    private readonly DashboardDataService _data;
    private readonly UserSettingsService _userSettingsService;
    private readonly DashboardCacheHolder _cache;

    public DashboardController(DashboardDataService data, UserSettingsService userSettingsService, DashboardCacheHolder cache)
    {
        _data = data;
        _userSettingsService = userSettingsService;
        _cache = cache;
    }

    // ---------------------------------------------------------------------
    // Contracts
    // ---------------------------------------------------------------------

    public sealed class DashboardCapabilities
    {
        public bool IsActiveRolesAdmin { get; set; }
        public bool CanAccessSettings { get; set; }
        public bool CanViewSnapshots { get; set; }
        public bool CanViewExposureReport { get; set; }
        public bool CanViewAssessments { get; set; }
        public bool CanExportDashboardData { get; set; }
        public bool CanRebuildCache { get; set; }
        public bool CacheRebuildInProgress { get; set; }
    }

    public sealed class KpiTile
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public string? Error { get; set; }
        /// <summary>Named colour token (e.g. "blue").</summary>
        public string ColorToken { get; set; } = string.Empty;
        /// <summary>Resolved hex colour for the token.</summary>
        public string Color { get; set; } = FallbackChartColor;
    }

    public sealed class ChartDataset
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        /// <summary>"doughnut" | "pie" | "bar".</summary>
        public string Type { get; set; } = "doughnut";
        public bool DisableTypeToggle { get; set; }
        public int SliceOffset { get; set; }
        public List<string> Labels { get; set; } = new();
        public List<int> Values { get; set; } = new();
        /// <summary>Hex colours, parallel to <see cref="Labels"/>.</summary>
        public List<string> Colors { get; set; } = new();
    }

    public sealed class NavigationTile
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        /// <summary>App-relative URL (no PathBase); the client prefixes its base href.</summary>
        public string Url { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
    }

    public sealed class SegmentFilterView
    {
        public bool Enabled { get; set; }
        public List<string> AvailableDomains { get; set; } = new();
        public List<string> SelectedDomains { get; set; } = new();
        public List<string> AvailableTenants { get; set; } = new();
        public List<string> SelectedTenants { get; set; } = new();
    }

    public sealed class OverviewSection
    {
        public bool Enabled { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<KpiTile> Kpis { get; set; } = new();
        public List<ChartDataset> Charts { get; set; } = new();
    }

    public sealed class DashboardPayload
    {
        public string UserName { get; set; } = string.Empty;
        public int AutoRefreshMinutes { get; set; }
        public DashboardCapabilities Capabilities { get; set; } = new();
        public OverviewSection Overview { get; set; } = new();
        public SegmentFilterView SegmentFilter { get; set; } = new();
        public List<NavigationTile> Tiles { get; set; } = new();
        public DateTime GeneratedAtUtc { get; set; }
    }

    public sealed class SegmentRequest
    {
        /// <summary>"Domain" or "Tenant".</summary>
        public string Dimension { get; set; } = string.Empty;
        public List<string>? Segments { get; set; }
    }

    // ---------------------------------------------------------------------
    // Endpoints
    // ---------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var token = DashboardDataService.GetAccessToken(HttpContext);
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var userSettings = _userSettingsService.Load(User.Identity?.Name ?? string.Empty);
        var kpiSettings = userSettings.KpiSettings;
        var access = await _data.ResolveAccessAsync(HttpContext, token);

        var summary = await _data.LoadIndexSummaryAsync(HttpContext, token, access, kpiSettings);
        var segments = _data.ApplyActiveSegmentFilter(HttpContext, summary);

        var payload = new DashboardPayload
        {
            UserName = User.Identity?.Name ?? string.Empty,
            AutoRefreshMinutes = userSettings.AutoRefreshMinutes,
            Capabilities = BuildCapabilities(access),
            Overview = BuildOverview(summary, kpiSettings),
            SegmentFilter = new SegmentFilterView
            {
                Enabled = segments.AvailableDomains.Count > 0 || segments.AvailableTenants.Count > 0,
                AvailableDomains = segments.AvailableDomains.ToList(),
                SelectedDomains = segments.SelectedDomains.ToList(),
                AvailableTenants = segments.AvailableTenants.ToList(),
                SelectedTenants = segments.SelectedTenants.ToList()
            },
            Tiles = BuildTiles(summary, access),
            GeneratedAtUtc = DateTime.UtcNow
        };

        return Ok(payload);
    }

    /// <summary>
    /// Persists a segment selection for one dimension to session (mirrors
    /// <c>DashboardPageModel.OnPostSetSegmentFilter</c>) and returns the stored raw selection.
    /// The client re-fetches <c>GET /api/dashboard</c> to render the filtered data.
    /// </summary>
    [HttpPost("segment")]
    public IActionResult SetSegment([FromBody] SegmentRequest request)
    {
        if (string.IsNullOrEmpty(DashboardDataService.GetAccessToken(HttpContext)))
            return Unauthorized();

        if (request.Dimension is not ("Domain" or "Tenant"))
            return BadRequest(new { error = "Dimension must be 'Domain' or 'Tenant'." });

        var state = _data.SetSegmentFilter(HttpContext, request.Dimension, request.Segments);
        return Ok(new { domains = state.Domains, tenants = state.Tenants });
    }

    // ---------------------------------------------------------------------
    // Builders
    // ---------------------------------------------------------------------

    private DashboardCapabilities BuildCapabilities(DashboardAccess access) => new()
    {
        IsActiveRolesAdmin = access.IsActiveRolesAdmin,
        CanAccessSettings = RolePermissionRegistry.CanAccessSettings(access.Permissions),
        CanViewSnapshots = access.HasPermission(DashboardPermission.ViewSnapshots),
        CanViewExposureReport = access.HasPermission(DashboardPermission.ViewExposureReport),
        CanViewAssessments = access.HasPermission(DashboardPermission.ViewAssessments),
        CanExportDashboardData = RolePermissionRegistry.CanExportDashboardData(access.Permissions, access.IsActiveRolesAdmin),
        CanRebuildCache = access.IsActiveRolesAdmin || access.HasPermission(DashboardPermission.RebuildCache),
        CacheRebuildInProgress = _cache.State == CacheState.Refreshing
    };

    private static OverviewSection BuildOverview(DashboardSummary summary, KpiSettings settings)
    {
        var category = CategoryInfo.Overview;
        var section = new OverviewSection
        {
            Enabled = settings.OverviewEnabled,
            Title = category.DisplayName
        };
        if (!section.Enabled)
            return section;

        foreach (var kpi in KpiInfo.ForCategory(category))
        {
            if (!settings.IsKpiEnabled(category.Key, kpi.Key)) continue;
            // The mailbox total is only meaningful/authorized when the Exchange dashboard is visible.
            if (kpi.Key == "MainTotalMailboxes" && !summary.ExchangeVisible) continue;

            var result = summary.GetKpiResult(kpi.Key);
            section.Kpis.Add(new KpiTile
            {
                Key = kpi.Key,
                Label = kpi.Label,
                Count = result.Count,
                Error = result.Error,
                ColorToken = kpi.CssColor,
                Color = ResolveColor(kpi.CssColor)
            });
        }

        section.Charts = BuildCharts(category, summary, settings);
        return section;
    }

    /// <summary>Server-side port of <c>Pages/Shared/_CategoryCharts.cshtml</c>.</summary>
    private static List<ChartDataset> BuildCharts(CategoryInfo category, DashboardSummary summary, KpiSettings settings)
    {
        var result = new List<ChartDataset>();

        foreach (var chart in ChartInfo.ForCategory(category))
        {
            var dataset = new ChartDataset
            {
                Key = chart.Key,
                Title = chart.Title,
                Type = chart.Type.ToString().ToLowerInvariant(),
                DisableTypeToggle = chart.DisableTypeToggle,
                SliceOffset = chart.SliceOffset
            };

            if (!string.IsNullOrEmpty(chart.SourceSplitKpiKey))
            {
                var kpi = KpiInfo.All.FirstOrDefault(k => k.Key == chart.SourceSplitKpiKey);
                if (kpi is null || !settings.IsKpiEnabled(kpi.CategoryKey, kpi.Key)) continue;

                var colorIndex = 0;
                foreach (var part in summary.GetSourceSplit(chart.SourceSplitKpiKey))
                {
                    if (part.Count <= 0) continue;

                    string token;
                    if (part.Source == "Active Directory")
                        token = ChartInfo.SourceSplitColors[0];
                    else if (part.Source == "Entra ID")
                        token = ChartInfo.SourceSplitColors[1 % ChartInfo.SourceSplitColors.Count];
                    else
                        token = ChartInfo.SourceSplitColors[colorIndex % ChartInfo.SourceSplitColors.Count];

                    dataset.Labels.Add(part.Source);
                    dataset.Values.Add(part.Count);
                    dataset.Colors.Add(ResolveColor(token));
                    colorIndex++;
                }
            }
            else
            {
                foreach (var item in chart.Series)
                {
                    var kpi = KpiInfo.All.FirstOrDefault(k => k.Key == item.KpiKey);
                    if (kpi is null || !settings.IsKpiEnabled(kpi.CategoryKey, kpi.Key)) continue;

                    var kpiResult = summary.GetKpiResult(kpi.Key);
                    if (kpiResult.Error is not null || kpiResult.Count <= 0) continue;

                    dataset.Labels.Add(string.IsNullOrEmpty(item.Label) ? kpi.Label : item.Label);
                    dataset.Values.Add(kpiResult.Count);
                    dataset.Colors.Add(ResolveColor(string.IsNullOrEmpty(item.CssColor) ? kpi.CssColor : item.CssColor));
                }
            }

            if (dataset.Values.Count > 0)
                result.Add(dataset);
        }

        return result;
    }

    /// <summary>Navigation tiles with the same gating as <c>Pages/Index.cshtml</c>.</summary>
    private static List<NavigationTile> BuildTiles(DashboardSummary summary, DashboardAccess access)
    {
        var canViewActiveRoles = access.IsActiveRolesAdmin || access.HasPermission(DashboardPermission.ViewActiveRolesDashboard);
        var canViewAd = access.HasPermission(DashboardPermission.ViewActiveDirectoryDashboard) || summary.AdVisible;
        var canViewEntra = access.HasPermission(DashboardPermission.ViewEntraIdDashboard) || summary.EntraVisible;
        var canViewLicensing = access.HasPermission(DashboardPermission.ViewLicensingDashboard) || summary.LicensingVisible;
        var canViewExchange = summary.ExchangeVisible;

        var tiles = new List<NavigationTile>();
        foreach (var dash in DashboardInfo.All)
        {
            if (dash.Key == "ActiveRoles")
            {
                if (!canViewActiveRoles) continue;
            }
            else if (dash.RequiresAdmin && !access.IsActiveRolesAdmin) continue;

            if (dash.Key == "ActiveDirectory" && !canViewAd) continue;
            if (dash.Key == "EntraId" && !canViewEntra) continue;
            if (dash.Key == "Licensing" && !canViewLicensing) continue;
            if (dash.Key == "Exchange" && !canViewExchange) continue;

            tiles.Add(new NavigationTile
            {
                Key = dash.Key,
                Title = dash.Title,
                Subtitle = dash.Subtitle,
                Url = dash.Url,
                Image = dash.Image
            });
        }

        return tiles;
    }

    private static string ResolveColor(string? token) =>
        !string.IsNullOrEmpty(token) && ChartPalette.TryGetValue(token, out var hex) ? hex : FallbackChartColor;
}
