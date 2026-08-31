using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Reporting;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActiveRolesDashboard.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ExportController : ControllerBase
{
    private readonly ReportBuilder _reportBuilder;
    private readonly ReportExporterFactory _exporterFactory;
    private readonly UserSettingsService _userSettings;
    private readonly ActiveRolesService _activeRoles;
    private readonly PerUserSummaryCache _summaryCache;

    public ExportController(ReportBuilder reportBuilder, ReportExporterFactory exporterFactory, UserSettingsService userSettings, ActiveRolesService activeRoles, PerUserSummaryCache summaryCache)
    {
        _reportBuilder = reportBuilder;
        _exporterFactory = exporterFactory;
        _userSettings = userSettings;
        _activeRoles = activeRoles;
        _summaryCache = summaryCache;
    }

    [HttpPost]
    public async Task<IActionResult> Export([FromForm] ReportRequest request)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        // Reuse the already-computed dashboard summary cached in session when available
        // (no new queries). Category/KPI dashboards cache the full summary under
        // "DashboardSummary". The main (Overview) dashboard only caches overview totals
        // under "OverviewTotals", which is insufficient for exporting the governance/risk
        // and other category KPIs. In that case compute a full summary on demand, reusing
        // the cached overview totals so the overview reads are not repeated.
        var username = User.Identity?.Name ?? string.Empty;
        var userSettings = _userSettings.Load(username);
        var settings = userSettings.KpiSettings;

        DashboardSummary? summary = null;

        var cached = _summaryCache.GetSummary(username);
        if (!string.IsNullOrEmpty(cached))
        {
            summary = JsonSerializer.Deserialize<DashboardSummary>(cached);
        }
        else
        {
            OverviewTotalsCache? cachedTotals = null;
            var totalsJson = _summaryCache.GetOverview(username);
            if (!string.IsNullOrEmpty(totalsJson))
                cachedTotals = JsonSerializer.Deserialize<OverviewTotalsCache>(totalsJson);

            summary = await _activeRoles.GetDashboardSummaryAsync(
                token, settings, userSettings, skipOverviewTotals: false, cachedTotals: cachedTotals);

            // Cache the freshly-computed full summary (unfiltered) so subsequent exports
            // reuse it instead of re-querying. The active segment filter is applied below
            // per-request, so the cached copy must remain unfiltered.
            if (summary != null)
                _summaryCache.SetSummary(username, JsonSerializer.Serialize(summary.ToSessionCacheSafe()));
        }

        if (summary == null)
            return BadRequest("No dashboard data is available to export. Please load the dashboard first.");

        // Honour the active segment (domain/tenant) filter so the export reflects exactly
        // what the user is viewing. Applied at the shared choke point on DashboardSummary.
        var segmentFilter = SegmentFilterSession.Get(HttpContext.Session);
        summary.ApplySegmentFilter(segmentFilter);

        if (!_exporterFactory.TryGet(request.Format, out var exporter))
            return BadRequest($"Export format '{request.Format}' is not supported.");

        var model = _reportBuilder.Build(request, summary, settings, username);
        var bytes = exporter.Export(model);

        var fileName = BuildFileName(request, model) + exporter.FileExtension;

        // Signal download completion to the client. The browser gives no JS event when a
        // file download finishes, so we echo the client's token back in a short-lived,
        // non-HttpOnly cookie on the file response. The client polls for this cookie and
        // hides the "Exporting..." overlay as soon as it appears.
        var downloadToken = Request.Form["DownloadToken"].ToString();
        if (!string.IsNullOrEmpty(downloadToken))
        {
            Response.Cookies.Append("exportDownload", downloadToken, new CookieOptions
            {
                HttpOnly = false,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(1)
            });
        }

        return File(bytes, exporter.ContentType, fileName);
    }

    private static string BuildFileName(ReportRequest request, ReportModel model)
    {
        var scopePart = request.Scope switch
        {
            ReportScope.Kpi => model.Title,
            ReportScope.Category => $"{model.Title}-{model.Subtitle}",
            _ => model.Title
        };

        var safe = string.Join("_", (scopePart ?? "Export").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"{safe}_{DateTime.Now:yyyyMMdd_HHmm}";
    }
}
