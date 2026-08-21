using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

public class SnapshotsModel : DashboardPageModel
{
    private readonly SnapshotService _snapshots;

    public SnapshotsModel(
        ActiveRolesService arService,
        UserSettingsService userSettingsService,
        IOptionsMonitor<ActiveRolesConfig> arConfig,
        SnapshotService snapshots)
        : base(arService, userSettingsService, arConfig)
    {
        _snapshots = snapshots;
    }

    public List<SnapshotHeader> Snapshots { get; set; } = new();
    public SnapshotComparison? Comparison { get; set; }
    public SnapshotTrend Trend { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FromId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ToId { get; set; }

    /// <summary>Status message shown after an action (capture/delete).</summary>
    [TempData]
    public string? StatusMessage { get; set; }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        await LoadSnapshotsAsync();
        await BuildComparisonIfRequestedAsync();
        Trend = await _snapshots.BuildTrendAsync();

        return Page();
    }

    /// <summary>Captures a snapshot from the current dashboard summary (fresh query).</summary>
    public async Task<IActionResult> OnPostCaptureAsync(string? label)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        var token = GetAccessToken()!;
        var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
        var summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, userSettings);

        var snapshot = _snapshots.Capture(
            summary,
            label,
            User.Identity?.Name,
            ArConfig.CurrentValue.WebInterfaceUrl);
        await _snapshots.SaveAsync(snapshot);

        var pendingNote = summary.EntraMembershipDataPending
            ? " " + DashboardSummary.EntraMembershipPendingWarning
            : string.Empty;

        StatusMessage = (string.IsNullOrWhiteSpace(label)
            ? "Snapshot captured."
            : $"Snapshot \u201C{label.Trim()}\u201D captured.") + pendingNote;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        StatusMessage = _snapshots.Delete(id) ? "Snapshot deleted." : "Snapshot not found.";
        return RedirectToPage();
    }

    private async Task LoadSnapshotsAsync()
    {
        Snapshots = await _snapshots.ListAsync();
    }

    private async Task BuildComparisonIfRequestedAsync()
    {
        if (string.IsNullOrWhiteSpace(FromId))
            return;

        var from = await _snapshots.LoadAsync(FromId);
        if (from == null)
            return;

        // "current" is a reserved To value meaning compare against live data.
        if (string.Equals(ToId, "current", StringComparison.OrdinalIgnoreCase))
        {
            var token = GetAccessToken()!;
            var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");
            var summary = await ArService.GetDashboardSummaryAsync(token, KpiSettings, userSettings);
            var live = _snapshots.Capture(summary, "Current", User.Identity?.Name, ArConfig.CurrentValue.WebInterfaceUrl);
            live.Header.CreatedUtc = DateTime.UtcNow;
            Comparison = _snapshots.Compare(from, live, toIsCurrent: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(ToId))
            return;

        var to = await _snapshots.LoadAsync(ToId);
        if (to == null)
            return;

        Comparison = _snapshots.Compare(from, to);
    }
}
