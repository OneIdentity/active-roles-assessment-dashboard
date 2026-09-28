using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Background service that captures performance-probe latency at a fixed interval so the trend
/// charts can show performance over the application's running time. It mirrors
/// <see cref="SupersetLoaderHostedService"/>: it uses the same service-account identity
/// (<see cref="ServiceAccountTokenProvider"/>) and defers all work until the app is configured.
///
/// Each cycle it builds the probe targets for every diagnostics dashboard from the current
/// superset summary, runs the same diagnostics probes the on-demand UI runs, and appends the
/// results to the in-memory <see cref="PerformanceTrendStore"/> (pruned to the retention window).
/// Samples are never persisted; display-time permission gating is enforced separately by the
/// read endpoint.
/// </summary>
public class PerformanceSamplerHostedService : BackgroundService
{
    private readonly DashboardCacheHolder _cache;
    private readonly ServiceAccountTokenProvider _tokenProvider;
    private readonly DiagnosticsTargetProvider _targetProvider;
    private readonly DiagnosticsService _diagnostics;
    private readonly PerformanceTrendStore _store;
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;
    private readonly ILogger<PerformanceSamplerHostedService> _logger;

    public PerformanceSamplerHostedService(
        DashboardCacheHolder cache,
        ServiceAccountTokenProvider tokenProvider,
        DiagnosticsTargetProvider targetProvider,
        DiagnosticsService diagnostics,
        PerformanceTrendStore store,
        IOptionsMonitor<ActiveRolesConfig> config,
        ILogger<PerformanceSamplerHostedService> logger)
    {
        _cache = cache;
        _tokenProvider = tokenProvider;
        _targetProvider = targetProvider;
        _diagnostics = diagnostics;
        _store = store;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run an initial sample as soon as the superset is available (probe targets are derived
        // from the collected summary), rather than waiting a full interval. This makes the trend
        // charts populate right after the startup superset load. If sampling is disabled or the
        // app is not configured, fall through to the normal loop which no-ops until enabled.
        await SampleOnStartupAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _config.CurrentValue.PerformanceTrending;
            var interval = TimeSpan.FromMinutes(options.EffectiveIntervalMinutes);

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            options = _config.CurrentValue.PerformanceTrending;
            if (options.Enabled)
                await SampleAsync(options, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits (with short polling) until the shared superset snapshot exists, then captures the
    /// first performance sample. Bounded so it never blocks shutdown or spins indefinitely when
    /// the app is unconfigured; the regular interval loop takes over afterwards regardless.
    /// </summary>
    private async Task SampleOnStartupAsync(CancellationToken ct)
    {
        var options = _config.CurrentValue.PerformanceTrending;
        if (!options.Enabled)
            return;

        // Poll for the superset for up to one effective interval, at a short cadence, so the first
        // sample lands just after the startup superset load instead of a full interval later.
        var pollDelay = TimeSpan.FromSeconds(5);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(options.EffectiveIntervalMinutes);

        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            if (!options.Enabled)
                return;

            if (!string.IsNullOrWhiteSpace(_config.CurrentValue.ApiBaseUrl) && _cache.Current?.Summary is not null)
            {
                await SampleAsync(options, ct).ConfigureAwait(false);
                return;
            }

            try
            {
                await Task.Delay(pollDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            options = _config.CurrentValue.PerformanceTrending;
        }
    }

    private async Task SampleAsync(PerformanceTrendingConfig options, CancellationToken ct)
    {
        // Nothing to probe until the app is configured and a superset snapshot exists (targets
        // are derived from the collected summary).
        if (string.IsNullOrWhiteSpace(_config.CurrentValue.ApiBaseUrl))
            return;

        var summary = _cache.Current?.Summary;
        if (summary is null)
        {
            _logger.LogDebug("Performance sampling skipped: no superset snapshot available yet.");
            return;
        }

        string token;
        try
        {
            token = await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Performance sampling skipped: could not acquire service-account token.");
            return;
        }
        _ = token; // Token acquisition validates the service-account session; probes authenticate internally.

        var retention = options.RetentionWindow;

        foreach (DiagnosticsDashboard dashboard in Enum.GetValues<DiagnosticsDashboard>())
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var targets = _targetProvider.BuildTargets(dashboard, summary);
                if (targets.Count == 0)
                    continue;

                var request = new DiagnosticsRequest { Dashboard = dashboard };
                var result = await _diagnostics.RunAsync(request, targets, ct).ConfigureAwait(false);
                _store.Record(dashboard, result, retention);
            }
            catch (Exception ex)
            {
                // Isolate per-dashboard failures so one bad scope does not stop the others.
                _logger.LogWarning(ex, "Performance sampling failed for dashboard {Dashboard}.", dashboard);
            }
        }
    }
}
