using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

/// <summary>
/// Renders a MITRE ATT&CK exposure view derived from live dashboard KPI counts. Unlike
/// the Assessments page this is a visibility model (which techniques the current
/// environment is exposed to), not a scored compliance grade.
/// </summary>
public class AttackExposureModel : DashboardPageModel
{
    private readonly MitreExposureService _mitre;
    private readonly SnapshotService _snapshots;

    public AttackExposureModel(
        ActiveRolesService arService,
        UserSettingsService userSettingsService,
        IOptionsMonitor<ActiveRolesConfig> arConfig,
        MitreExposureService mitre,
        SnapshotService snapshots)
        : base(arService, userSettingsService, arConfig)
    {
        _mitre = mitre;
        _snapshots = snapshots;
    }

    /// <summary>The computed exposure view shown on the page.</summary>
    public AttackExposureView Exposure { get; set; } = new();

    /// <summary>Saved snapshots available as comparison baselines (newest first).</summary>
    public List<SnapshotHeader> Snapshots { get; set; } = new();

    /// <summary>Exposure comparison result, when requested via query.</summary>
    public ExposureComparison? Comparison { get; set; }

    /// <summary>Exposure trend derived from saved snapshots.</summary>
    public ExposureTrend Trend { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FromId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ToId { get; set; }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Prefer the session-cached dashboard summary so the exposure view reflects the same
        // Entra membership state as the dashboard. Re-fetching here would always report
        // membership as pending (the eager path never expands membership), which would both
        // show the "loading" banner incorrectly and cause back-navigation to the dashboard to
        // restart membership loading. Only fetch fresh when no cache exists.
        var token = GetAccessToken()!;
        var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
        var cachedJson = HttpContext.Session.GetString("DashboardSummary");
        if (!string.IsNullOrEmpty(cachedJson))
        {
            Summary = System.Text.Json.JsonSerializer.Deserialize<DashboardSummary>(cachedJson) ?? new DashboardSummary();
        }
        else
        {
            Summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, userSettings);
            CacheSummary();
        }

        Exposure = _mitre.Build(Summary);

        // Snapshots power the (derived) compare and trend features. Exposure itself is
        // never persisted; historical exposure is recomputed from snapshot KPI counts.
        Snapshots = await _snapshots.ListAsync();
        await BuildComparisonIfRequestedAsync();
        Trend = _mitre.BuildTrend(await _snapshots.LoadAllOrderedAsync());

        return Page();
    }

    private async Task BuildComparisonIfRequestedAsync()
    {
        if (string.IsNullOrWhiteSpace(FromId))
            return;

        var fromSnapshot = await _snapshots.LoadAsync(FromId);
        if (fromSnapshot == null)
            return;

        var fromView = _mitre.BuildFromSnapshot(fromSnapshot);
        var fromLabel = DescribeSnapshot(fromSnapshot.Header);

        // "current" is a reserved To value meaning compare against the live exposure view.
        if (string.Equals(ToId, "current", StringComparison.OrdinalIgnoreCase))
        {
            Comparison = _mitre.Compare(fromView, Exposure, fromLabel, "Current", toIsCurrent: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(ToId))
            return;

        var toSnapshot = await _snapshots.LoadAsync(ToId);
        if (toSnapshot == null)
            return;

        var toView = _mitre.BuildFromSnapshot(toSnapshot);
        Comparison = _mitre.Compare(fromView, toView, fromLabel, DescribeSnapshot(toSnapshot.Header));
    }

    private static string DescribeSnapshot(SnapshotHeader header) =>
        string.IsNullOrWhiteSpace(header.Label)
            ? header.CreatedUtc.ToString("yyyy-MM-dd HH:mm")
            : $"{header.Label} ({header.CreatedUtc:yyyy-MM-dd HH:mm})";
}
