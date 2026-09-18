using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public DiagnosticsController(
        DiagnosticsService diagnostics,
        DiagnosticsTargetProvider targetProvider,
        PerUserSummaryCache summaryCache,
        RoleService roleService)
    {
        _diagnostics = diagnostics;
        _targetProvider = targetProvider;
        _summaryCache = summaryCache;
        _roleService = roleService;
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
