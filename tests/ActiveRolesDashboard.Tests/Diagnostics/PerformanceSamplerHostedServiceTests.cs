using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

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

    public void Dispose() => _server.Dispose();

    private PerformanceSamplerHostedService Create(bool enabled = true, bool configured = true, bool withServiceAccount = true)
    {
        var config = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig
        {
            ApiBaseUrl = configured ? _server.ApiBaseUrl : "",
            RstsUrl = _server.RstsUrl,
            PerformanceTrending = new PerformanceTrendingConfig { Enabled = enabled, RetentionHours = 24 },
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
            _store, config, NullLogger<PerformanceSamplerHostedService>.Instance);
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
}
