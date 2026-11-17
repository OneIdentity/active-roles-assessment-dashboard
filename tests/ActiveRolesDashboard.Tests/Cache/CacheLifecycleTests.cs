using System.Net;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ActiveRolesDashboard.Tests.Cache;

/// <summary>
/// Drives the real <see cref="SupersetLoaderHostedService"/> against the fake Active Roles server.
/// Covers the install/upgrade defects found in RC1 testing: setup values must apply without a
/// restart, and an undecryptable service-account password must fault the cache cleanly.
/// </summary>
[Trait("Stage", "Cache")]
public class CacheLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // ------------------------------------------------------------------ setup wizard

    [Fact]
    public async Task Setup_wizard_values_apply_without_restart()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = new DashboardAppFactory(AppSettingsBuilder.Default(null).Build(), server);
        using var client = app.CreateNonRedirectingClient();
        await using var loader = await LoaderRun.StartAsync(app);

        var response = await client.PostAsync("/Setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ApiBaseUrl"] = server.ApiBaseUrl,
            ["RstsUrl"] = server.RstsUrl,
            ["ServiceAccountUsername"] = FakeActiveRolesServer.ValidUser,
            ["ServiceAccountPassword"] = FakeActiveRolesServer.ValidPassword,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Login", response.Headers.Location!.OriginalString);

        await loader.WaitForRefreshAsync();

        Assert.Equal(CacheState.Ready, loader.Cache.State);
        Assert.Null(loader.Cache.LastError);
        Assert.Contains(server.Requests, r => r.RequestMessage.Path == "/RSTS/oauth2/token");
    }

    // ------------------------------------------------------------------ lost key ring

    [Fact]
    public async Task Password_from_lost_key_ring_faults_cache_without_contacting_rsts()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        var foreignPayload = new ServiceAccountSecretProtector(new EphemeralDataProtectionProvider())
            .Protect(FakeActiveRolesServer.ValidPassword);
        var settings = ConfiguredSettings(server, foreignPayload).Set("ActiveRoles:DataRefresh:LoadOnStartup", true).Build();
        using var app = new DashboardAppFactory(settings, server);

        await using var loader = await LoaderRun.StartAsync(app, expectStartupRefresh: true);

        Assert.Equal(CacheState.Faulted, loader.Cache.State);
        Assert.False(string.IsNullOrWhiteSpace(loader.Cache.LastError));
        Assert.True(loader.Cache.LastRefreshFailed);
        Assert.DoesNotContain(server.Requests, r => r.RequestMessage.Path == "/RSTS/oauth2/token");
    }

    [Fact]
    public async Task Faulted_cache_shows_banner_and_disables_sign_in()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        var foreignPayload = new ServiceAccountSecretProtector(new EphemeralDataProtectionProvider())
            .Protect(FakeActiveRolesServer.ValidPassword);
        var settings = ConfiguredSettings(server, foreignPayload).Set("ActiveRoles:DataRefresh:LoadOnStartup", true).Build();
        using var app = new DashboardAppFactory(settings, server);
        using var client = app.CreateNonRedirectingClient();
        await using var loader = await LoaderRun.StartAsync(app, expectStartupRefresh: true);

        var html = await client.GetStringAsync("/Login");

        Assert.Contains("id=\"cacheErrorBanner\" role=\"alert\">", html);
        Assert.Contains("id=\"btnSignIn\" disabled", html);
    }

    // ------------------------------------------------------------------ loader behaviour

    [Fact]
    public async Task Unconfigured_app_skips_collection()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = new DashboardAppFactory(AppSettingsBuilder.Default(null).Build(), server);
        await using var loader = await LoaderRun.StartAsync(app);

        loader.Loader.TriggerManualRefresh();
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(CacheState.NotStarted, loader.Cache.State);
        Assert.Equal(0, loader.Cache.RefreshSequence);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Unreachable_api_faults_cache()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server, UnreachableApiBaseUrl);
        await using var loader = await LoaderRun.StartAsync(app);

        await loader.RefreshAsync();

        Assert.Contains("unreachable", loader.Cache.LastError, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(CacheState.Faulted, loader.Cache.State);
        Assert.Null(loader.Cache.Current);
        Assert.False(string.IsNullOrWhiteSpace(loader.Cache.LastError));
    }

    [Fact]
    public async Task Manual_refresh_publishes_snapshot()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server);
        await using var loader = await LoaderRun.StartAsync(app);

        await loader.RefreshAsync();

        Assert.Equal(CacheState.Ready, loader.Cache.State);
        Assert.NotNull(loader.Cache.Current);
        Assert.False(loader.Cache.LastRefreshFailed);
    }

    [Fact]
    public async Task Failed_refresh_keeps_serving_previous_snapshot()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server);
        await using var loader = await LoaderRun.StartAsync(app);
        await loader.RefreshAsync();
        var previous = loader.Cache.Current;

        Reconfigure(app, server, app.ContentRoot.ReadAppSettings()["ActiveRoles"]!["ServiceAccount"]!["ProtectedPassword"]!.GetValue<string>(), UnreachableApiBaseUrl);
        await loader.RefreshAsync();

        Assert.Equal(CacheState.Ready, loader.Cache.State);
        Assert.True(loader.Cache.LastRefreshFailed);
        Assert.Same(previous, loader.Cache.Current);
    }

    [Fact]
    public async Task Rebuild_invalidates_per_user_data_and_directory_facts()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server);
        var userCache = app.Services.GetRequiredService<PerUserSummaryCache>();
        await using var loader = await LoaderRun.StartAsync(app);
        await loader.RefreshAsync();

        userCache.SetSummary("alice", "{}");
        userCache.SetRole("alice", "Auditor");
        var factsEpoch = userCache.DirectoryFactsEpoch;

        await loader.RefreshAsync();

        Assert.Null(userCache.GetSummary("alice"));
        Assert.Null(userCache.GetRole("alice"));
        Assert.True(userCache.DirectoryFactsEpoch > factsEpoch);
    }

    // ------------------------------------------------------------------ daily schedule
    // The factory clock starts at 2026-01-15 09:00 and FakeTimeProvider's local zone is UTC, so
    // the configured daily time is evaluated against 09:00.

    /// <summary>Lets the loop register its next scheduled delay before the clock is advanced.</summary>
    private static Task Settle() => Task.Delay(TimeSpan.FromMilliseconds(300));

    private static async Task AssertNoRefresh(LoaderRun loader)
    {
        var before = loader.Cache.RefreshSequence;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Equal(before, loader.Cache.RefreshSequence);
    }

    [Fact]
    public async Task Scheduled_refresh_runs_at_configured_daily_time()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server, dailyRefreshTime: "10:00");
        await using var loader = await LoaderRun.StartAsync(app);
        await Settle();

        app.Clock.Advance(TimeSpan.FromMinutes(59));
        await AssertNoRefresh(loader);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        await loader.WaitForRefreshAsync();

        Assert.Equal(CacheState.Ready, loader.Cache.State);
        Assert.NotNull(loader.Cache.Current);
    }

    [Fact]
    public async Task Next_scheduled_refresh_is_one_day_later()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server, dailyRefreshTime: "10:00");
        await using var loader = await LoaderRun.StartAsync(app);
        await Settle();
        app.Clock.Advance(TimeSpan.FromHours(1));
        await loader.WaitForRefreshAsync();
        await Settle();

        app.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await AssertNoRefresh(loader);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        await loader.WaitForRefreshAsync();
    }

    [Theory]
    [InlineData("08:00")]
    [InlineData("09:00")]
    public async Task Daily_time_already_reached_today_runs_tomorrow(string dailyTime)
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server, dailyRefreshTime: dailyTime);
        await using var loader = await LoaderRun.StartAsync(app);
        await Settle();
        var untilTomorrow = TimeSpan.Parse(dailyTime) + TimeSpan.FromHours(24) - TimeSpan.FromHours(9);

        app.Clock.Advance(untilTomorrow - TimeSpan.FromMinutes(1));
        await AssertNoRefresh(loader);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        await loader.WaitForRefreshAsync();
    }

    [Theory]
    [InlineData("not-a-time")]
    [InlineData("25:00")]
    [InlineData("")]
    public async Task Invalid_daily_time_falls_back_to_24_hours(string dailyTime)
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server, dailyRefreshTime: dailyTime);
        await using var loader = await LoaderRun.StartAsync(app);
        await Settle();

        app.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await AssertNoRefresh(loader);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        await loader.WaitForRefreshAsync();
    }

    [Fact]
    public async Task Manual_refresh_does_not_shift_the_daily_schedule()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = ConfiguredApp(server, dailyRefreshTime: "10:00");
        await using var loader = await LoaderRun.StartAsync(app);
        await Settle();

        app.Clock.Advance(TimeSpan.FromMinutes(30));
        await loader.RefreshAsync();
        await Settle();

        app.Clock.Advance(TimeSpan.FromMinutes(29));
        await AssertNoRefresh(loader);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        await loader.WaitForRefreshAsync();
    }

    [Fact]
    public async Task Scheduled_refresh_while_unconfigured_does_not_collect()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = new DashboardAppFactory(AppSettingsBuilder.Default(null).Set("ActiveRoles:DataRefresh:DailyRefreshTime", "10:00").Build(), server);
        await using var loader = await LoaderRun.StartAsync(app);
        await Settle();

        app.Clock.Advance(TimeSpan.FromHours(1));
        await AssertNoRefresh(loader);

        Assert.Equal(CacheState.NotStarted, loader.Cache.State);
        Assert.Empty(server.Requests);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Nothing listens on port 1, so connecting fails at the transport layer.</summary>
    private const string UnreachableApiBaseUrl = "http://127.0.0.1:1/api";

    private static AppSettingsBuilder ConfiguredSettings(FakeActiveRolesServer server, string protectedPassword, string? apiBaseUrl = null, string? dailyRefreshTime = null)
    {
        var b = AppSettingsBuilder.Default(server)
            .Set("ActiveRoles:ServiceAccount:Username", FakeActiveRolesServer.ValidUser)
            .Set("ActiveRoles:ServiceAccount:ProtectedPassword", protectedPassword);
        if (apiBaseUrl != null)
            b.Set("ActiveRoles:ApiBaseUrl", apiBaseUrl);
        if (dailyRefreshTime != null)
            b.Set("ActiveRoles:DataRefresh:DailyRefreshTime", dailyRefreshTime);
        return b;
    }

    private static void Reconfigure(DashboardAppFactory app, FakeActiveRolesServer server, string protectedPassword, string? apiBaseUrl = null, string? dailyRefreshTime = null)
    {
        app.ContentRoot.WriteAppSettings(ConfiguredSettings(server, protectedPassword, apiBaseUrl, dailyRefreshTime).Build());
        ((IConfigurationRoot)app.Services.GetRequiredService<IConfiguration>()).Reload();
    }

    /// <summary>
    /// Configured app whose service-account password is protected with the app's OWN key ring
    /// (written after the host starts, then reloaded, exactly as the setup wizard does).
    /// </summary>
    private static DashboardAppFactory ConfiguredApp(FakeActiveRolesServer server, string? apiBaseUrl = null, string? dailyRefreshTime = null)
    {
        var app = new DashboardAppFactory(activeRoles: server);
        var payload = app.Services.GetRequiredService<ServiceAccountSecretProtector>().Protect(FakeActiveRolesServer.ValidPassword);
        Reconfigure(app, server, payload, apiBaseUrl, dailyRefreshTime);
        return app;
    }

    /// <summary>Starts the loader's background loop and observes completed refresh attempts.</summary>
    private sealed class LoaderRun : IAsyncDisposable
    {
        private int _observedSequence;

        private LoaderRun(SupersetLoaderHostedService loader, DashboardCacheHolder cache)
        {
            Loader = loader;
            Cache = cache;
        }

        public SupersetLoaderHostedService Loader { get; }
        public DashboardCacheHolder Cache { get; }

        public static async Task<LoaderRun> StartAsync(DashboardAppFactory app, bool expectStartupRefresh = false)
        {
            var run = new LoaderRun(
                app.Services.GetRequiredService<SupersetLoaderHostedService>(),
                app.Services.GetRequiredService<DashboardCacheHolder>());
            await run.Loader.StartAsync(CancellationToken.None);
            if (expectStartupRefresh)
                await run.WaitForRefreshAsync();
            return run;
        }

        /// <summary>Triggers a manual refresh and waits until that attempt completes.</summary>
        public Task RefreshAsync()
        {
            Loader.TriggerManualRefresh();
            return WaitForRefreshAsync();
        }

        /// <summary>Waits until the next refresh attempt (success or failure) completes.</summary>
        public async Task WaitForRefreshAsync()
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (Cache.RefreshSequence <= _observedSequence)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Cache refresh did not complete within {Timeout}. State={Cache.State}, LastError={Cache.LastError}");
                await Task.Delay(25);
            }
            _observedSequence = Cache.RefreshSequence;
        }

        public async ValueTask DisposeAsync() => await Loader.StopAsync(CancellationToken.None);
    }
}
