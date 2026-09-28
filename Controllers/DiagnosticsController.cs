using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Controllers;

/// <summary>
/// On-demand performance / connectivity diagnostics endpoint. Results are live and are NOT
/// cached or persisted; each POST triggers a fresh probe sweep against the requested targets.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DiagnosticsController : ControllerBase
{
    private readonly DiagnosticsService _diagnostics;
    private readonly DiagnosticsTargetProvider _targetProvider;
    private readonly PerUserSummaryCache _summaryCache;
    private readonly RoleService _roleService;
    private readonly PerformanceTrendStore _trendStore;
    private readonly IOptionsMonitor<ActiveRolesConfig> _arConfig;

    public DiagnosticsController(
        DiagnosticsService diagnostics,
        DiagnosticsTargetProvider targetProvider,
        PerUserSummaryCache summaryCache,
        RoleService roleService,
        PerformanceTrendStore trendStore,
        IOptionsMonitor<ActiveRolesConfig> arConfig)
    {
        _diagnostics = diagnostics;
        _targetProvider = targetProvider;
        _summaryCache = summaryCache;
        _roleService = roleService;
        _trendStore = trendStore;
        _arConfig = arConfig;
    }

    /// <summary>
    /// True when the current caller may run performance diagnostics, enforced server-side so a
    /// crafted request cannot bypass the hidden UI. Mirrors <see cref="DashboardPageModel.CanRunPerformanceTests(DiagnosticsDashboard)"/>.
    /// </summary>
    private bool CallerCanRunPerformanceTests(DiagnosticsDashboard dashboard)
    {
        var isActiveRolesAdmin = string.Equals(HttpContext.Session.GetString("IsActiveRolesAdmin"), "True", StringComparison.OrdinalIgnoreCase);
        var role = Enum.TryParse<DashboardRole>(HttpContext.Session.GetString("DashboardRole"), out var parsedRole)
            ? parsedRole
            : DashboardRole.User;
        var permissions = _roleService.GetPermissions(role);
        return RolePermissionRegistry.CanRunPerformanceTests(permissions, isActiveRolesAdmin, dashboard);
    }

    /// <summary>Returns the discoverable probe targets for a dashboard (for building the UI filters).</summary>
    [HttpGet("targets")]
    public IActionResult GetTargets([FromQuery] DiagnosticsDashboard dashboard)
    {
        if (!CallerCanRunPerformanceTests(dashboard))
            return Forbid();

        var summary = LoadSummary();
        if (summary == null)
            return Ok(new { error = "No collected dashboard data is available yet." });

        var targets = _targetProvider.BuildTargets(dashboard, summary)
            .Select(t => new
            {
                t.Id,
                t.Name,
                serverType = t.ServerType.ToString(),
                applicableTests = t.ApplicableTests.Select(x => x.ToString())
            });
        return Ok(targets);
    }

    /// <summary>
    /// Returns the retained performance-trend series for a dashboard, for rendering the trend
    /// chart. Gated by the same per-dashboard performance permission as the live tests. The
    /// optional <paramref name="periodHours"/> narrows the view window (clamped to the configured
    /// retention); when omitted the full retention window is returned.
    /// </summary>
    [HttpGet("trends")]
    public IActionResult GetTrends([FromQuery] DiagnosticsDashboard dashboard, [FromQuery] int? periodHours)
    {
        if (!CallerCanRunPerformanceTests(dashboard))
            return Forbid();

        var options = _arConfig.CurrentValue.PerformanceTrending;
        var retention = options.RetentionWindow;

        // The requested window may not exceed what is retained.
        var window = retention;
        if (periodHours is > 0)
        {
            var requested = TimeSpan.FromHours(periodHours.Value);
            if (requested < retention)
                window = requested;
        }

        var series = _trendStore.GetSeries(dashboard, window);

        var response = new PerformanceTrendResponse
        {
            Dashboard = dashboard,
            IntervalMinutes = options.EffectiveIntervalMinutes,
            RetentionHours = options.RetentionHours,
            Series = series.ToList()
        };

        var allSamples = series.SelectMany(s => s.Samples).ToList();
        if (allSamples.Count > 0)
        {
            response.OldestSampleUtc = allSamples.Min(s => s.TimestampUtc);
            response.NewestSampleUtc = allSamples.Max(s => s.TimestampUtc);
        }

        return Ok(response);
    }

    /// <summary>Runs diagnostics for a dashboard, honouring optional server-type / test-type / target filters.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] DiagnosticsRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });

        if (!CallerCanRunPerformanceTests(request.Dashboard))
            return Forbid();

        var summary = LoadSummary();
        if (summary == null)
            return Ok(new { error = "No collected dashboard data is available yet." });

        var targets = _targetProvider.BuildTargets(request.Dashboard, summary);
        var result = await _diagnostics.RunAsync(request, targets, cancellationToken);

        return Ok(new
        {
            dashboard = result.Dashboard.ToString(),
            startedUtc = result.StartedUtc,
            completedUtc = result.CompletedUtc,
            okCount = result.OkCount,
            warnCount = result.WarnCount,
            failCount = result.FailCount,
            probes = result.Probes.Select(p => new
            {
                p.TargetId,
                p.TargetName,
                serverType = p.ServerType.ToString(),
                testType = p.TestType.ToString(),
                status = p.Status.ToString(),
                p.LatencyMs,
                p.Message
            })
        });
    }

    private DashboardSummary? LoadSummary()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return null;

        var json = _summaryCache.GetSummary(username);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DashboardSummary>(json);
        }
        catch
        {
            return null;
        }
    }
}
