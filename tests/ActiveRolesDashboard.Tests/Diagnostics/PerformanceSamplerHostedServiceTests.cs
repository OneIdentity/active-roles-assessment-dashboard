using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ActiveRolesDashboard.Tests.Diagnostics;

/// <summary>
/// Background performance sampler: the startup sample written to the trend store, the conditions
/// that suppress sampling (disabled, unconfigured, no superset, no service-account token), and
/// clean shutdown. Only the Active Roles RSTS target (pointed at the fake server) is probed.
/// </summary>
[Trait("Stage", "Diagnostics")]
public class PerformanceSamplerHostedServiceTests : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(1);
    private static readonly TimeSpan NoSampleWait = TimeSpan.FromMilliseconds(500);

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private readonly ServiceAccountSecretProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly DashboardCacheHolder _cache = new();
    private readonly PerformanceTrendStore _store = new();

    private TestOptionsMonitor<ActiveRolesConfig>? _config;

    public void Dispose() => _server.Dispose();

    private PerformanceSamplerHostedService Create(bool enabled = true, bool configured = true, bool withServiceAccount = true,
        TimeProvider? clock = null, int intervalMinutes = 15)
    {
        var config = _config = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig
        {
            ApiBaseUrl = configured ? _server.ApiBaseUrl : "",
            RstsUrl = _server.RstsUrl,
            PerformanceTrending = new PerformanceTrendingConfig { Enabled = enabled, RetentionHours = 24, SnapshotIntervalMinutes = intervalMinutes },
            ServiceAccount = withServiceAccount
                ? new ServiceAccountConfig
                {
                    Username = FakeActiveRolesServer.ValidUser,
                    ProtectedPassword = _protector.Protect(FakeActiveRolesServer.ValidPassword)
                }
                : new ServiceAccountConfig()
        });
        var factory = new TestHttpClientFactory(TimeSpan.FromSeconds(10));
        var rsts = new RstsAuthService(config, factory);
        var tokens = new ServiceAccountTokenProvider(rsts, _protector, config, NullLogger<ServiceAccountTokenProvider>.Instance);
        var diagnostics = new DiagnosticsService(factory, rsts, tokens, _protector, config, NullLogger<DiagnosticsService>.Instance);

        return new PerformanceSamplerHostedService(_cache, tokens, new DiagnosticsTargetProvider(config), diagnostics,
            _store, config, NullLogger<PerformanceSamplerHostedService>.Instance, clock);
    }

    private int AuthSampleCount() =>
        _store.GetSeries(DiagnosticsDashboard.ActiveRoles, Window)
            .SingleOrDefault(s => s.TestType == DiagnosticsTestType.RstsAuth)?.Samples.Count ?? 0;

    private async Task<bool> WaitForCount(int expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (AuthSampleCount() >= expected)
                return true;
            await Task.Delay(25);
        }
        return AuthSampleCount() >= expected;
    }

    /// <summary>
    /// Advances the fake clock by one interval until the next sample lands. The loop registers its
    /// delay only after the previous sample completes, so an advance can race ahead of it; retrying
    /// covers that without advancing further once the sample is recorded.
    /// </summary>
    private async Task AdvanceUntilSample(FakeTimeProvider clock, TimeSpan interval, int expected)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            clock.Advance(interval);
            if (await WaitForCount(expected, TimeSpan.FromSeconds(2)))
                return;
        }
        Assert.Fail($"Expected {expected} samples, found {AuthSampleCount()}.");
    }

    private async Task<FakeTimeProvider> StartedWithStartupSample(PerformanceSamplerHostedService sampler, FakeTimeProvider clock)
    {
        await sampler.StartAsync(CancellationToken.None);
        Assert.True(await WaitForCount(1, TimeSpan.FromSeconds(15)));
        await Task.Delay(200); // let the loop register its interval delay
        return clock;
    }

    private void PublishSuperset() =>
        _cache.Publish(new DashboardSupersetSnapshot(new DashboardSummary(), DateTimeOffset.UtcNow));

    private async Task<IReadOnlyList<PerformanceTrendSeries>> WaitForSeries(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var series = _store.GetSeries(DiagnosticsDashboard.ActiveRoles, Window);
            if (series.Count > 0)
                return series;
            await Task.Delay(50);
        }
        return _store.GetSeries(DiagnosticsDashboard.ActiveRoles, Window);
    }

    private static async Task RunFor(PerformanceSamplerHostedService sampler, Func<Task> body)
    {
        await sampler.StartAsync(CancellationToken.None);
        try
        {
            await body();
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    private void AssertNoSamples()
    {
        foreach (var dashboard in Enum.GetValues<DiagnosticsDashboard>())
            Assert.Empty(_store.GetSeries(dashboard, Window));
    }

    [Fact]
    public async Task Startup_sample_is_recorded_when_superset_is_available()
    {
        PublishSuperset();
        var sampler = Create();

        IReadOnlyList<PerformanceTrendSeries> series = [];
        await RunFor(sampler, async () => series = await WaitForSeries(TimeSpan.FromSeconds(15)));

        Assert.All(series, s =>
        {
            Assert.Equal("rsts", s.TargetId);
            Assert.Equal(DiagnosticsServerType.Rsts, s.ServerType);
            Assert.Single(s.Samples);
        });
        var auth = Assert.Single(series, s => s.TestType == DiagnosticsTestType.RstsAuth);
        Assert.Equal(DiagnosticsStatus.Ok, auth.Samples[0].Status);
    }

    [Fact]
    public async Task Dashboards_without_targets_record_nothing()
    {
        PublishSuperset();
        var sampler = Create();

        await RunFor(sampler, async () => await WaitForSeries(TimeSpan.FromSeconds(15)));

        Assert.Empty(_store.GetSeries(DiagnosticsDashboard.ActiveDirectory, Window));
        Assert.Empty(_store.GetSeries(DiagnosticsDashboard.EntraId, Window));
        Assert.Empty(_store.GetSeries(DiagnosticsDashboard.Exchange, Window));
    }

    [Fact]
    public async Task Disabled_sampling_records_nothing()
    {
        PublishSuperset();

        await RunFor(Create(enabled: false), () => Task.Delay(NoSampleWait));

        AssertNoSamples();
    }

    [Fact]
    public async Task Unconfigured_app_records_nothing()
    {
        PublishSuperset();

        await RunFor(Create(configured: false), () => Task.Delay(NoSampleWait));

        AssertNoSamples();
    }

    [Fact]
    public async Task Missing_superset_records_nothing()
    {
        await RunFor(Create(), () => Task.Delay(NoSampleWait));

        AssertNoSamples();
    }

    [Fact]
    public async Task Missing_service_account_token_records_nothing()
    {
        PublishSuperset();

        await RunFor(Create(withServiceAccount: false), () => Task.Delay(NoSampleWait));

        AssertNoSamples();
    }

    [Fact]
    public async Task Stop_completes_promptly_while_waiting_for_superset()
    {
        var sampler = Create();
        await sampler.StartAsync(CancellationToken.None);

        var stop = sampler.StopAsync(CancellationToken.None);

        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
        await stop;
    }

    [Fact]
    public async Task Stop_completes_promptly_after_startup_sample()
    {
        PublishSuperset();
        var sampler = Create();
        await sampler.StartAsync(CancellationToken.None);
        await WaitForSeries(TimeSpan.FromSeconds(15));

        var stop = sampler.StopAsync(CancellationToken.None);

        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
        await stop;
    }

    // ---------------------------------------------------------------- repeated samples

    [Fact]
    public async Task Each_interval_appends_a_sample_to_the_same_series()
    {
        PublishSuperset();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sampler = Create(clock: clock, intervalMinutes: 10);
        var interval = TimeSpan.FromMinutes(10);

        try
        {
            await StartedWithStartupSample(sampler, clock);
            await AdvanceUntilSample(clock, interval, 2);
            await AdvanceUntilSample(clock, interval, 3);
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }

        var series = _store.GetSeries(DiagnosticsDashboard.ActiveRoles, Window);
        Assert.All(series, s => Assert.Equal(3, s.Samples.Count));
        var auth = Assert.Single(series, s => s.TestType == DiagnosticsTestType.RstsAuth);
        Assert.All(auth.Samples, s => Assert.Equal(DiagnosticsStatus.Ok, s.Status));
        Assert.True(auth.Samples.Zip(auth.Samples.Skip(1)).All(p => p.First.TimestampUtc <= p.Second.TimestampUtc));
    }

    [Fact]
    public async Task Interval_below_minimum_is_clamped()
    {
        PublishSuperset();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sampler = Create(clock: clock, intervalMinutes: 1);

        try
        {
            await StartedWithStartupSample(sampler, clock);

            clock.Advance(TimeSpan.FromMinutes(PerformanceTrendingConfig.MinimumIntervalMinutes - 1));
            await Task.Delay(NoSampleWait);
            Assert.Equal(1, AuthSampleCount());

            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(await WaitForCount(2, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Disabling_at_runtime_pauses_sampling_and_reenabling_resumes_it()
    {
        PublishSuperset();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sampler = Create(clock: clock, intervalMinutes: 10);
        var interval = TimeSpan.FromMinutes(10);

        try
        {
            await StartedWithStartupSample(sampler, clock);

            _config!.CurrentValue.PerformanceTrending.Enabled = false;
            clock.Advance(interval);
            await Task.Delay(NoSampleWait);
            clock.Advance(interval);
            await Task.Delay(NoSampleWait);
            Assert.Equal(1, AuthSampleCount());

            _config.CurrentValue.PerformanceTrending.Enabled = true;
            await AdvanceUntilSample(clock, interval, 2);
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Superset_published_later_is_picked_up_by_the_interval_loop()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sampler = Create(clock: clock, intervalMinutes: 10);
        var interval = TimeSpan.FromMinutes(10);

        try
        {
            await sampler.StartAsync(CancellationToken.None);
            // Exhaust the startup polling window without a superset.
            clock.Advance(interval);
            await Task.Delay(NoSampleWait);
            Assert.Equal(0, AuthSampleCount());

            PublishSuperset();
            await AdvanceUntilSample(clock, interval, 1);
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Rsts_outage_between_intervals_records_a_failed_sample()
    {
        PublishSuperset();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sampler = Create(clock: clock, intervalMinutes: 10);
        var interval = TimeSpan.FromMinutes(10);

        try
        {
            await StartedWithStartupSample(sampler, clock);

            _server.WithTokenEndpointStatus(503);
            await AdvanceUntilSample(clock, interval, 2);
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }

        var auth = Assert.Single(_store.GetSeries(DiagnosticsDashboard.ActiveRoles, Window), s => s.TestType == DiagnosticsTestType.RstsAuth);
        Assert.Equal(DiagnosticsStatus.Ok, auth.Samples[0].Status);
        Assert.NotEqual(DiagnosticsStatus.Ok, auth.Samples[1].Status);
    }
}
