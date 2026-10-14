using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Controllers;

/// <summary>
/// REST API that backs the React dashboard
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
    private readonly IOptionsMonitor<ActiveRolesConfig> _arConfig;
    private readonly KpiThresholdService _thresholds;
    private readonly KpiExclusionService _exclusions;

    public DashboardController(DashboardDataService data, UserSettingsService userSettingsService, DashboardCacheHolder cache, IOptionsMonitor<ActiveRolesConfig> arConfig, KpiThresholdService thresholds, KpiExclusionService exclusions)
    {
        _data = data;
        _userSettingsService = userSettingsService;
        _cache = cache;
        _arConfig = arConfig;
        _thresholds = thresholds;
        _exclusions = exclusions;
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
        /// <summary>Exclusions are configured for this KPI but could not be applied to its count.</summary>
        public bool ExclusionsNotApplied { get; set; }
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
        /// <summary>Named colour tokens, parallel to <see cref="Labels"/> (mapped to IRIS CSS variables by the client).</summary>
        public List<string> ColorTokens { get; set; } = new();
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
        /// <summary>Tabs visible to the caller: "Overview" followed by each permitted dashboard.</summary>
        public List<DashboardTab> Tabs { get; set; } = new();
        public DateTime GeneratedAtUtc { get; set; }
    }

    public sealed class SourceSplitItem
    {
        public string Source { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public sealed class ViewKpi
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public string? Error { get; set; }
        public string ColorToken { get; set; } = string.Empty;
        public string Color { get; set; } = FallbackChartColor;
        public string SectionId { get; set; } = string.Empty;
        public bool IsRisk { get; set; }
        /// <summary>Threshold severity (none/low/medium/high), or null when the KPI has no thresholds.</summary>
        public string? Severity { get; set; }
        /// <summary>Exclusions are configured for this KPI but could not be applied to its count.</summary>
        public bool ExclusionsNotApplied { get; set; }
    }

    public sealed class ViewCategory
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsOverview { get; set; }
        public bool IsRisk { get; set; }
        public List<ViewKpi> Kpis { get; set; } = new();
        public List<ChartDataset> Charts { get; set; } = new();
    }

    public sealed class DashboardTab
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public sealed class DashboardView
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<ViewCategory> Categories { get; set; } = new();
        public List<SourceSplitItem> SourceSplit { get; set; } = new();
        public ViewKpi? ManagedObjects { get; set; }
        public LicensingView? Licensing { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>A labelled count; <see cref="Kind"/> lets the client localise suffixes (e.g. "tenant-hybrid").</summary>
    public sealed class LicensingSegment
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public sealed class LicensingThreshold
    {
        /// <summary>"Domain" | "Partition" | "Azure" | "Total".</summary>
        public string Key { get; set; } = string.Empty;
        public int Total { get; set; }
        /// <summary>0 = not configured.</summary>
        public int Threshold { get; set; }
    }

    public sealed class LicensingTrendSeries
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>"Domain" | "Partition" | "Azure".</summary>
        public string Category { get; set; } = string.Empty;
        public List<int> Values { get; set; } = new();
    }

    /// <summary>Specialised licensing charts (formerly the legacy Razor Licensing page).</summary>
    public sealed class LicensingView
    {
        public string? Error { get; set; }
        public List<string> TrendLabels { get; set; } = new();
        public List<LicensingTrendSeries> Trend { get; set; } = new();
        public List<LicensingThreshold> Thresholds { get; set; } = new();
        public bool OverThreshold { get; set; }
        public List<LicensingSegment> Composition { get; set; } = new();
        public List<LicensingSegment> Excluded { get; set; } = new();
    }

    public sealed class KpiDetail
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public string? Error { get; set; }
        public List<string> Columns { get; set; } = new();
        public List<IReadOnlyList<string>> Rows { get; set; } = new();
        /// <summary>DN of the group itself when its nested membership can be viewed (privileged groups).</summary>
        public string? GroupTreeDn { get; set; }
        /// <summary>Per-row group DN for the nested-membership tree (aligned with Rows; null entries have none).</summary>
        public List<string?>? RowGroupTreeDns { get; set; }
        /// <summary>Exclusions are configured for this KPI but could not be applied to its count.</summary>
        public bool ExclusionsNotApplied { get; set; }
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
        payload.Tabs.Add(new DashboardTab { Key = OverviewKey, Title = CategoryInfo.Overview.DisplayName });
        payload.Tabs.AddRange(payload.Tiles.Select(t => new DashboardTab { Key = t.Key, Title = t.Title }));

        return Ok(payload);
    }

    /// <summary>
    /// Returns the content of one dashboard tab ("Overview" or a <see cref="DashboardInfo"/> key):
    /// its enabled categories with KPIs and charts, plus the source split shown in the
    /// Source Distribution section. Uses the same permission gating as the Razor dashboards.
    /// </summary>
    [HttpGet("view/{key}")]
    public async Task<IActionResult> GetView(string key)
    {
        var token = DashboardDataService.GetAccessToken(HttpContext);
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var kpiSettings = _userSettingsService.Load(User.Identity?.Name ?? string.Empty).KpiSettings;
        var access = await _data.ResolveAccessAsync(HttpContext, token);
        var summary = await _data.LoadIndexSummaryAsync(HttpContext, token, access, kpiSettings);
        _data.ApplyActiveSegmentFilter(HttpContext, summary);

        var isOverview = string.Equals(key, OverviewKey, StringComparison.OrdinalIgnoreCase);
        IEnumerable<CategoryInfo> categories;
        string title;
        string sourceSplitKey;
        if (isOverview)
        {
            categories = [CategoryInfo.Overview, CategoryInfo.ADGovernance];
            title = CategoryInfo.Overview.DisplayName;
            sourceSplitKey = "ADUserAccounts";
        }
        else
        {
            var tile = BuildTiles(summary, access).FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
            var dashboard = DashboardInfo.All.FirstOrDefault(d => d.Key == tile?.Key);
            if (dashboard is null)
                return NotFound();
            categories = CategoryInfo.ForDashboard(dashboard);
            title = dashboard.Title;
            sourceSplitKey = dashboard.Key == "ActiveDirectory" ? "AdOverviewUsers" : string.Empty;
        }

        var view = new DashboardView
        {
            Key = isOverview ? OverviewKey : key,
            Title = title,
            GeneratedAtUtc = DateTime.UtcNow
        };

        foreach (var category in categories)
        {
            if (!kpiSettings.IsCategoryEnabled(category.Key)) continue;
            var viewCategory = new ViewCategory
            {
                Key = category.Key,
                Title = category.DisplayName,
                IsOverview = category.IsOverviewCategory,
                IsRisk = category.IsRiskCategory,
                Charts = BuildCharts(category, summary, kpiSettings)
            };
            foreach (var kpi in KpiInfo.ForCategory(category))
            {
                if (!kpiSettings.IsKpiEnabled(kpi.CategoryKey, kpi.Key)) continue;
                if (kpi.Key == "MainTotalMailboxes" && !summary.ExchangeVisible) continue;
                viewCategory.Kpis.Add(ToViewKpi(kpi, summary));
            }
            if (viewCategory.Kpis.Count > 0 || viewCategory.Charts.Count > 0)
                view.Categories.Add(viewCategory);
        }

        if (!string.IsNullOrEmpty(sourceSplitKey))
        {
            view.SourceSplit = summary.GetSourceSplit(sourceSplitKey)
                .Where(s => s.Count > 0)
                .Select(s => new SourceSplitItem { Source = s.Source, Count = s.Count })
                .ToList();
        }

        var canViewLicensing = access.HasPermission(DashboardPermission.ViewLicensingDashboard) || summary.LicensingVisible;
        var managedObjects = KpiInfo.All.FirstOrDefault(k => k.Key == "ManagedObjects");
        if (isOverview && canViewLicensing && managedObjects is not null
            && kpiSettings.IsKpiEnabled(managedObjects.CategoryKey, managedObjects.Key))
        {
            view.ManagedObjects = ToViewKpi(managedObjects, summary);
        }

        if (!isOverview && view.Key.Equals(DashboardInfo.Licensing.Key, StringComparison.OrdinalIgnoreCase)
            && kpiSettings.IsCategoryEnabled(CategoryInfo.Licensing.Key)
            && kpiSettings.IsKpiEnabled(CategoryInfo.Licensing.Key, "ManagedObjects"))
        {
            view.Licensing = BuildLicensing(summary);
        }

        return Ok(view);
    }

    /// <summary>
    /// Detail rows for one KPI (same table as the export's detail section). Rejects KPIs whose
    /// owning dashboard is not visible to the caller, and honours the active segment filter.
    /// </summary>
    [HttpGet("kpi/{key}")]
    public async Task<IActionResult> GetKpiDetail(string key)
    {
        var token = DashboardDataService.GetAccessToken(HttpContext);
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var kpi = KpiInfo.All.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
        if (kpi is null)
            return NotFound();

        var kpiSettings = _userSettingsService.Load(User.Identity?.Name ?? string.Empty).KpiSettings;
        if (!kpiSettings.IsKpiEnabled(kpi.CategoryKey, kpi.Key))
            return NotFound();

        var access = await _data.ResolveAccessAsync(HttpContext, token);
        var summary = await _data.LoadIndexSummaryAsync(HttpContext, token, access, kpiSettings);
        _data.ApplyActiveSegmentFilter(HttpContext, summary);

        var category = CategoryInfo.All.FirstOrDefault(c => KpiInfo.ForCategory(c).Any(k => k.Key == kpi.Key));
        var dashboardKey = category?.DashboardKey ?? string.Empty;
        var visible = dashboardKey == "Main"
            || BuildTiles(summary, access).Any(t => t.Key == dashboardKey);
        if (!visible)
            return NotFound();

        var result = _exclusions.Evaluate(summary, kpi.Key);
        var table = summary.GetKpiDetailTable(kpi.Key);
        _exclusions.ApplyToTable(kpi.Key, table);
        var (groupTreeDn, rowGroupTreeDns) = summary.GetKpiGroupTreeTargets(kpi.Key);
        return Ok(new KpiDetail
        {
            Key = kpi.Key,
            Label = kpi.Label,
            Count = result.Count,
            Error = table?.Error ?? result.Error,
            Columns = table?.Columns ?? new(),
            Rows = table?.Rows ?? new(),
            GroupTreeDn = groupTreeDn,
            RowGroupTreeDns = rowGroupTreeDns is not null && rowGroupTreeDns.Count == (table?.Rows.Count ?? 0) ? rowGroupTreeDns : null,
            ExclusionsNotApplied = result.NotApplied
        });
    }

    /// <summary>Rebuild polling endpoint (mirrors <c>IndexModel.OnGetRefreshStatusAsync</c>).</summary>
    [HttpGet("refresh-status")]
    public async Task<IActionResult> GetRefreshStatus()
    {
        var token = DashboardDataService.GetAccessToken(HttpContext);
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var access = await _data.ResolveAccessAsync(HttpContext, token);
        if (!(access.IsActiveRolesAdmin || access.HasPermission(DashboardPermission.RebuildCache)))
            return Forbid();

        return Ok(new
        {
            sequence = _cache.RefreshSequence,
            refreshing = _cache.State == CacheState.Refreshing,
            failed = _cache.LastRefreshFailed,
            error = _cache.LastError
        });
    }

    /// <summary>Server-side licensing calculations (formerly the legacy Razor Licensing page).</summary>
    private LicensingView BuildLicensing(DashboardSummary summary)
    {
        var view = new LicensingView();
        var managed = summary.ManagedObjects;
        if (managed.Error != null)
        {
            view.Error = managed.Error;
            return view;
        }

        // Historical trend: one series per source (domain total / partition total / tenant cloud-only).
        var points = managed.DataPoints.OrderBy(p => p.RunTime).ToList();
        view.TrendLabels = points.Select(p => p.RunTime.ToString("yyyy-MM-dd HH:mm")).ToList();
        var sources = points.SelectMany(p => p.Items)
            .Where(i => !string.Equals(i.Category, "SAAS", StringComparison.OrdinalIgnoreCase))
            .Select(i => (i.Category, i.DisplayName))
            .Distinct()
            .ToList();
        foreach (var (category, name) in sources)
        {
            var isAzure = string.Equals(category, "Azure", StringComparison.OrdinalIgnoreCase);
            view.Trend.Add(new LicensingTrendSeries
            {
                Name = name,
                Category = category,
                Values = points.Select(p =>
                {
                    var item = p.Items.FirstOrDefault(i => i.Category == category && i.DisplayName == name);
                    return item is null ? 0 : (isAzure ? item.CloudOnlyCount : item.Count);
                }).ToList()
            });
        }

        var cfg = _arConfig.CurrentValue.Licensing;
        var latest = points.LastOrDefault(p => p.Items.Count > 0) ?? points.LastOrDefault();
        if (latest != null)
        {
            int Total(string cat) => latest.Items
                .Where(i => string.Equals(i.Category, cat, StringComparison.OrdinalIgnoreCase)).Sum(i => i.Count);
            var domain = Total("Domain");
            var partition = Total("Partition");
            var azure = latest.Items
                .Where(i => string.Equals(i.Category, "Azure", StringComparison.OrdinalIgnoreCase)).Sum(i => i.CloudOnlyCount);
            var grand = domain + partition + azure;
            view.Thresholds =
            [
                new() { Key = "Domain", Total = domain, Threshold = Math.Max(0, cfg.DomainObjects) },
                new() { Key = "Partition", Total = partition, Threshold = Math.Max(0, cfg.PartitionObjects) },
                new() { Key = "Azure", Total = azure, Threshold = Math.Max(0, cfg.AzureObjects) },
                new() { Key = "Total", Total = grand, Threshold = Math.Max(0, cfg.TotalObjects) }
            ];
            view.OverThreshold = view.Thresholds.Any(t => t.Threshold > 0 && t.Total > t.Threshold);
        }

        // Composition (licensed) and excluded segments.
        var comp = view.Composition;
        var excl = view.Excluded;
        var addedDomainFromKpi = false;
        if (summary.EnabledUsers?.Items is { Count: > 0 } enabled)
        {
            foreach (var g in enabled.Where(i => i.IsManaged)
                .GroupBy(i => string.IsNullOrWhiteSpace(i.Domain) ? "(unknown)" : i.Domain)
                .OrderByDescending(g => g.Count()))
            {
                comp.Add(new() { Name = g.Key, Kind = "domain", Value = g.Count() });
                addedDomainFromKpi = true;
            }
            var adExcluded = enabled.Count(i => !i.IsManaged);
            if (adExcluded > 0)
                excl.Add(new() { Name = "Active Directory", Kind = "excluded-mgmt-ad", Value = adExcluded });
        }

        if (summary.DisabledUsers?.Items is { Count: > 0 } disabled)
        {
            foreach (var g in disabled
                .GroupBy(i => string.IsNullOrWhiteSpace(i.Domain) ? "(unknown)" : i.Domain)
                .OrderByDescending(g => g.Count()))
                excl.Add(new() { Name = g.Key, Kind = "domain-disabled", Value = g.Count() });
        }

        if (latest != null)
        {
            if (!addedDomainFromKpi)
            {
                foreach (var d in latest.Items
                    .Where(i => string.Equals(i.Category, "Domain", StringComparison.OrdinalIgnoreCase) && i.Count > 0)
                    .OrderByDescending(i => i.Count))
                    comp.Add(new() { Name = d.DisplayName, Kind = "domain", Value = d.Count });
            }

            var azureItems = latest.Items
                .Where(i => string.Equals(i.Category, "Azure", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var t in azureItems.Where(i => i.CloudOnlyCount > 0).OrderByDescending(i => i.CloudOnlyCount))
                comp.Add(new() { Name = t.DisplayName, Kind = "tenant", Value = t.CloudOnlyCount });
            foreach (var t in azureItems.Where(i => i.HybridCount > 0).OrderByDescending(i => i.HybridCount))
                excl.Add(new() { Name = t.DisplayName, Kind = "tenant-hybrid", Value = t.HybridCount });
            foreach (var t in azureItems.Where(i => i.GuestCount > 0).OrderByDescending(i => i.GuestCount))
                excl.Add(new() { Name = t.DisplayName, Kind = "tenant-guest", Value = t.GuestCount });
        }

        if (summary.EntraTotals?.Error == null && summary.EntraTotals != null)
        {
            var entraExcluded = summary.EntraTotals.EntraUsers(onPremSynced: false).Items.Count(i => !i.IsManaged);
            if (entraExcluded > 0)
            {
                var remaining = entraExcluded;
                foreach (var seg in comp.Where(s => s.Kind == "tenant"))
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, seg.Value);
                    seg.Value -= take;
                    remaining -= take;
                }
                comp.RemoveAll(s => s.Kind == "tenant" && s.Value <= 0);
                excl.Add(new() { Name = "Entra ID", Kind = "excluded-mgmt-cloud", Value = entraExcluded });
            }
        }

        return view;
    }

    /// <summary>
    /// Triggers a rebuild of the shared superset cache (mirrors <c>IndexModel.OnPostRefreshAsync</c>)
    /// and clears the caller's cached summary. Returns the refresh sequence captured at trigger time.
    /// </summary>
    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild([FromServices] SupersetLoaderHostedService loader)
    {
        var token = DashboardDataService.GetAccessToken(HttpContext);
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var access = await _data.ResolveAccessAsync(HttpContext, token);
        if (!(access.IsActiveRolesAdmin || access.HasPermission(DashboardPermission.RebuildCache)))
            return Forbid();

        loader.TriggerManualRefresh();
        _data.ClearCachedSummary(HttpContext);
        return Ok(new { refreshFrom = _cache.RefreshSequence });
    }

    private const string OverviewKey = "Overview";

    private ViewKpi ToViewKpi(KpiInfo kpi, DashboardSummary summary)
    {
        var result = _exclusions.Evaluate(summary, kpi.Key);
        return new ViewKpi
        {
            Key = kpi.Key,
            Label = kpi.Label,
            Count = result.Count,
            Error = result.Error,
            ColorToken = kpi.CssColor,
            Color = ResolveColor(kpi.CssColor),
            SectionId = kpi.SectionId,
            IsRisk = kpi.IsRiskKpi,
            Severity = result.Error == null ? ResolveSeverity(kpi.Key, result.Count, summary) : null,
            ExclusionsNotApplied = result.NotApplied
        };
    }

    private string? ResolveSeverity(string kpiKey, int count, DashboardSummary summary)
    {
        ResolvedKpiThresholds? thresholds = null;
        var lookup = _exclusions.LookupFor(summary);
        if (KpiThresholdDefaults.BuiltIn.TryGetValue(kpiKey, out var builtIn))
            thresholds = _thresholds.Resolve(kpiKey, builtIn, lookup);
        else if (_thresholds.GetConfigured(kpiKey) is { } configured)
            thresholds = KpiThresholdService.Resolve(configured, kpiKey, new ResolvedKpiThresholds(1, 1), lookup);
        if (thresholds is null) return null;
        return KpiThresholdService.LevelFor(count, thresholds.Value).ToString().ToLowerInvariant();
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

    private OverviewSection BuildOverview(DashboardSummary summary, KpiSettings settings)
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

            var result = _exclusions.Evaluate(summary, kpi.Key);
            section.Kpis.Add(new KpiTile
            {
                Key = kpi.Key,
                Label = kpi.Label,
                Count = result.Count,
                Error = result.Error,
                ColorToken = kpi.CssColor,
                Color = ResolveColor(kpi.CssColor),
                ExclusionsNotApplied = result.NotApplied
            });
        }

        section.Charts = BuildCharts(category, summary, settings);
        return section;
    }

    /// <summary>Server-side category chart data (formerly rendered by the legacy Razor category charts).</summary>
    private List<ChartDataset> BuildCharts(CategoryInfo category, DashboardSummary summary, KpiSettings settings)
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
                    dataset.ColorTokens.Add(token);
                    colorIndex++;
                }
            }
            else
            {
                foreach (var item in chart.Series)
                {
                    var kpi = KpiInfo.All.FirstOrDefault(k => k.Key == item.KpiKey);
                    if (kpi is null || !settings.IsKpiEnabled(kpi.CategoryKey, kpi.Key)) continue;

                    var kpiResult = _exclusions.GetAdjustedResult(summary, kpi.Key);
                    if (kpiResult.Error is not null || kpiResult.Count <= 0) continue;

                    dataset.Labels.Add(string.IsNullOrEmpty(item.Label) ? kpi.Label : item.Label);
                    dataset.Values.Add(kpiResult.Count);
                    var colorToken = string.IsNullOrEmpty(item.CssColor) ? kpi.CssColor : item.CssColor;
                    dataset.Colors.Add(ResolveColor(colorToken));
                    dataset.ColorTokens.Add(colorToken);
                }
            }

            if (dataset.Values.Count > 0)
                result.Add(dataset);
        }

        return result;
    }

    /// <summary>Navigation tiles with the same gating as the former legacy Razor overview.</summary>
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
