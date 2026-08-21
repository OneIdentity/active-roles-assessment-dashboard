using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Reporting;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

public class AssessmentsModel : DashboardPageModel
{
    private readonly AssessmentService _assessments;
    private readonly AssessmentReportBuilder _reportBuilder;
    private readonly ReportExporterFactory _exporterFactory;

    public AssessmentsModel(
        ActiveRolesService arService,
        UserSettingsService userSettingsService,
        IOptionsMonitor<ActiveRolesConfig> arConfig,
        AssessmentService assessments,
        AssessmentReportBuilder reportBuilder,
        ReportExporterFactory exporterFactory)
        : base(arService, userSettingsService, arConfig)
    {
        _assessments = assessments;
        _reportBuilder = reportBuilder;
        _exporterFactory = exporterFactory;
    }

    /// <summary>The result currently being displayed (freshly run or loaded from history).</summary>
    public AssessmentResult? Assessment { get; set; }

    /// <summary>Saved assessments for the selected type, newest first.</summary>
    public List<AssessmentHeader> History { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public AssessmentType Type { get; set; } = AssessmentType.ActiveDirectory;

    /// <summary>When set on GET, loads and displays a stored assessment instead of running a new one.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    /// <summary>Baseline run id for a comparison request.</summary>
    [BindProperty(SupportsGet = true)]
    public string? FromId { get; set; }

    /// <summary>Compare-to run id for a comparison request. The reserved value "current" compares against a live (unsaved) evaluation.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ToId { get; set; }

    /// <summary>Populated when a comparison was requested and both sides resolved.</summary>
    public AssessmentRunComparison? Comparison { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Load the dashboard summary so the page knows whether Entra group membership is still
        // loading. Without this, EntraMembershipDataPending is always false on GET and the
        // "Run & save" button is never disabled for membership-dependent assessments (the
        // server-side guard would then fire only after the form is submitted). Prefer the
        // cached summary to avoid re-querying Active Roles on every view.
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

        if (!string.IsNullOrWhiteSpace(Id))
        {
            Assessment = await _assessments.LoadAsync(Id);
            if (Assessment != null)
                Type = Assessment.Type;
        }

        await BuildComparisonIfRequestedAsync();

        await LoadHistoryAsync();
        return Page();
    }

    /// <summary>
    /// Builds a comparison when both <see cref="FromId"/> and <see cref="ToId"/> are supplied.
    /// The reserved <c>ToId == "current"</c> compares the baseline against a live (unsaved)
    /// evaluation of the current dashboard summary. Comparison is only meaningful within a
    /// single assessment type, so a mismatched compare-to run is rejected.
    /// </summary>
    private async Task BuildComparisonIfRequestedAsync()
    {
        if (string.IsNullOrWhiteSpace(FromId) || string.IsNullOrWhiteSpace(ToId))
            return;

        var from = await _assessments.LoadAsync(FromId);
        if (from == null)
        {
            StatusMessage = "The baseline assessment could not be found.";
            return;
        }

        Type = from.Type;

        AssessmentResult? to;
        bool toIsCurrent = string.Equals(ToId, "current", StringComparison.OrdinalIgnoreCase);
        if (toIsCurrent)
        {
            // Don't score a membership-dependent type against provisional data while Entra
            // group membership is still loading; mirror the guard used when running an assessment.
            if (AssessmentRuleLibrary.DependsOnEntraGroupMembership(from.Type) && Summary.EntraMembershipDataPending)
            {
                StatusMessage = $"The {AssessmentTypeInfo.DisplayName(from.Type)} assessment depends on Entra group " +
                    "membership, which is still loading. Wait until membership loading completes before comparing " +
                    "against current values.";
                return;
            }

            // Live, unsaved evaluation of the current summary.
            to = _assessments.Evaluate(Summary, from.Type, "Current (live)", User.Identity?.Name);
        }
        else
        {
            to = await _assessments.LoadAsync(ToId);
            if (to == null)
            {
                StatusMessage = "The comparison assessment could not be found.";
                return;
            }
            if (to.Type != from.Type)
            {
                StatusMessage = "Assessments can only be compared within the same assessment type.";
                return;
            }
        }

        Comparison = _assessments.Compare(from, to, toIsCurrent);
    }

    /// <summary>Runs a new assessment for the selected type, saves it, and displays it.</summary>
    public async Task<IActionResult> OnPostRunAsync(string? label)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        var token = GetAccessToken()!;
        var userSettings = UserSettingsService.Load(User.Identity?.Name ?? "");

        // Prefer the session-cached summary: once lazy Entra group membership finishes loading it
        // is persisted back into this cache (with MembershipLoaded = true). Building a fresh summary
        // here would always report membership as pending, so a membership-dependent assessment could
        // never run even after loading completed. Fall back to a fresh fetch only when no cache exists.
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

        // Block assessments that depend on accurate Entra group membership until lazy
        // membership loading has completed. Independent assessments (Active Roles, Active
        // Directory, etc.) are unaffected and continue to run.
        if (AssessmentRuleLibrary.DependsOnEntraGroupMembership(Type) && Summary.EntraMembershipDataPending)
        {
            StatusMessage = $"The {AssessmentTypeInfo.DisplayName(Type)} assessment depends on Entra group " +
                "membership, which is still loading. Wait until membership loading completes, then run it " +
                "again so group-membership checks are scored against complete data.";
            await LoadHistoryAsync();
            return RedirectToPage(new { Type });
        }

        Assessment = _assessments.Evaluate(Summary, Type, label, User.Identity?.Name);
        await _assessments.SaveAsync(Assessment);

        StatusMessage = $"{AssessmentTypeInfo.DisplayName(Type)} assessment completed and saved (grade {Assessment.Grade}).";
        return RedirectToPage(new { Type, Id = Assessment.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        StatusMessage = _assessments.Delete(id) ? "Assessment deleted." : "Assessment not found.";
        return RedirectToPage(new { Type });
    }

    /// <summary>Exports a saved assessment as a PDF or Word document.</summary>
    public async Task<IActionResult> OnPostExportAsync(string id, ReportFormat format)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        var assessment = await _assessments.LoadAsync(id);
        if (assessment == null)
        {
            StatusMessage = "Assessment not found.";
            return RedirectToPage(new { Type });
        }

        if (!_exporterFactory.TryGet(format, out var exporter))
        {
            StatusMessage = $"Export format '{format}' is not supported.";
            return RedirectToPage(new { Type, Id = id });
        }

        var model = _reportBuilder.Build(assessment, User.Identity?.Name ?? string.Empty);
        var bytes = exporter.Export(model);

        var typeName = AssessmentTypeInfo.DisplayName(assessment.Type);
        var safe = string.Join("_", $"{typeName}-Assessment".Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var fileName = $"{safe}_{assessment.GeneratedUtc:yyyyMMdd_HHmm}{exporter.FileExtension}";

        return File(bytes, exporter.ContentType, fileName);
    }

    private async Task LoadHistoryAsync()
    {
        History = await _assessments.ListAsync(Type);
    }
}
