using System.Globalization;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Background service that owns the lifecycle of the shared dashboard superset. It performs the
/// initial collection at application startup (using the service-account identity), then refreshes
/// daily at the configured local time, and on-demand when an Active Roles admin triggers a manual
/// refresh from the main dashboard. Collection and refresh never block HTTP request threads; the
/// previous snapshot keeps being served until a new one is published.
/// </summary>
public class SupersetLoaderHostedService : BackgroundService
{
    private readonly DashboardCacheHolder _cache;
    private readonly ServiceAccountTokenProvider _tokenProvider;
    private readonly ActiveRolesService _arService;
    private readonly ArPermissionModelService _permissionModelService;
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;
    private readonly ILogger<SupersetLoaderHostedService> _logger;

    // Signalled to wake the loop for an immediate (manual) refresh.
    private readonly SemaphoreSlim _manualRefresh = new(0);

    public SupersetLoaderHostedService(
        DashboardCacheHolder cache,
        ServiceAccountTokenProvider tokenProvider,
        ActiveRolesService arService,
        ArPermissionModelService permissionModelService,
        IOptionsMonitor<ActiveRolesConfig> config,
        ILogger<SupersetLoaderHostedService> logger)
    {
        _cache = cache;
        _tokenProvider = tokenProvider;
        _arService = arService;
        _permissionModelService = permissionModelService;
        _config = config;
        _logger = logger;
    }

    /// <summary>Requests an out-of-band refresh (invoked by an AR admin from the main dashboard).</summary>
    public void TriggerManualRefresh()
    {
        // Release only if not already signalled, to avoid unbounded count growth.
        if (_manualRefresh.CurrentCount == 0)
            _manualRefresh.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sa = _config.CurrentValue.ServiceAccount;

        if (sa.LoadOnStartup)
        {
            await RefreshAsync(stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextDailyRefresh();
            _logger.LogInformation("Next scheduled superset refresh in {Delay} (at {Time} local).",
                delay, _config.CurrentValue.ServiceAccount.DailyRefreshTime);

            // Wait until either the daily time elapses or a manual refresh is triggered.
            var manualTask = _manualRefresh.WaitAsync(stoppingToken);
            var delayTask = Task.Delay(delay, stoppingToken);
            var completed = await Task.WhenAny(manualTask, delayTask).ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested)
                break;

            if (completed == manualTask)
                _logger.LogInformation("Manual superset refresh triggered.");

            await RefreshAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        _cache.MarkLoading();
        try
        {
            _logger.LogInformation("Starting service-account superset collection.");
            var token = await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);

            // Collect the unfiltered superset with the service-account identity.
            var summary = await _arService.GetDashboardSummaryAsync(token).ConfigureAwait(false);

            // Capture the permission model (AT links + template read grants) from the same view.
            var permissionModel = await _permissionModelService.BuildAsync(token, ct).ConfigureAwait(false);

            // Publish the base superset FIRST - BEFORE loading Entra group membership - so the
            // "Building cache..." overlay clears and the dashboard renders as soon as the core data
            // is ready. Membership is the dominant collection cost; blocking the publish on it would
            // keep users waiting on the overlay for the entire membership load. The snapshot's
            // summary object is the same reference held by the cache, so enriching its EntraTotals
            // in place below progressively updates what admins/Entra-visible viewers see, and the
            // membership progress badge counts down live.
            var snapshot = new DashboardSupersetSnapshot(summary, DateTimeOffset.UtcNow);
            _cache.Publish(snapshot, permissionModel);
            _logger.LogInformation("Superset published at {Time:o} (Entra membership loading next).", snapshot.CollectedAtUtc);

            // Enrich Entra group membership once, into the already-published shared superset, so it
            // is computed a single time at startup/refresh rather than lazily per user session. This
            // eventually sets EntraTotals.MembershipLoaded = true so viewers see membership fully
            // available (badge shows nothing remaining) and the client-side batch loader is a no-op.
            //
            // Loading is done in slices so live progress can be reported to the cache holder. That
            // lets a user who logs in WHILE membership is still loading see a real server-side
            // countdown (groups remaining) instead of falling back to per-session client loading.
            var entra = summary.EntraTotals;
            var groupCount = entra.Items.Count(i => ActiveRolesService.IsEntraGroupType(i.ObjectType));
            if (groupCount > 0)
            {
                _logger.LogInformation("Loading Entra group membership into superset ({Count} groups).", groupCount);
                _cache.BeginMembershipLoading(groupCount);
                try
                {
                    var sliceSize = Math.Max(1, _config.CurrentValue.EntraMembershipBatchSize);
                    for (var skip = 0; skip < groupCount; skip += sliceSize)
                    {
                        ct.ThrowIfCancellationRequested();
                        await _arService
                            .LoadEntraGroupMembershipAsync(token, entra, skip, sliceSize)
                            .ConfigureAwait(false);
                        _cache.ReportMembershipProgress(entra.MembershipLoadedCount);
                    }
                }
                finally
                {
                    _cache.EndMembershipLoading();
                }
                _logger.LogInformation(
                    "Entra group membership loaded into superset ({Count} groups).",
                    entra.MembershipLoadedCount);
            }

            _logger.LogInformation("Superset collection fully complete at {Time:o}.", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Superset collection failed.");
            _cache.MarkFaulted(ex.Message);
            _tokenProvider.Invalidate();
        }
    }

    /// <summary>
    /// Computes the delay until the next occurrence of the configured daily refresh time (local).
    /// Falls back to 24h if the configured value cannot be parsed.
    /// </summary>
    private TimeSpan TimeUntilNextDailyRefresh()
    {
        var configured = _config.CurrentValue.ServiceAccount.DailyRefreshTime;
        if (!TimeSpan.TryParseExact(configured, new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var timeOfDay))
        {
            _logger.LogWarning("Invalid DailyRefreshTime '{Value}'; defaulting to 24h interval.", configured);
            return TimeSpan.FromHours(24);
        }

        var now = DateTime.Now;
        var next = now.Date.Add(timeOfDay);
        if (next <= now)
            next = next.AddDays(1);

        return next - now;
    }
}
