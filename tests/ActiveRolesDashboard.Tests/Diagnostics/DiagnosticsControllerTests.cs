using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Diagnostics;

/// <summary>
/// End-to-end behaviour of <c>/api/Diagnostics</c> for an authorised caller: target listing,
/// trend window clamping and metadata, run request validation and result shape, and the
/// "no collected data" responses. Authorization denial is covered by AccessControlTests.
/// </summary>
[Trait("Stage", "Diagnostics")]
public class DiagnosticsControllerTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private DashboardAppFactory? _app;

    private DashboardAppFactory App(Action<ActiveRolesConfig>? configure = null)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, List<string>>
        {
            [nameof(DashboardRole.User)] =
            [
                nameof(DashboardPermission.RunActiveRolesPerformanceTests),
                nameof(DashboardPermission.RunActiveDirectoryPerformanceTests)
            ]
        });

        _app = new DashboardAppFactory(activeRoles: _server)
        {
            ConfigureServices = services => services.AddSingleton<IPostConfigureOptions<ActiveRolesConfig>>(sp =>
                new PostConfigureOptions<ActiveRolesConfig>(Options.DefaultName, c =>
                {
                    c.Roles.ProtectedMatrix = sp.GetRequiredService<RolePermissionProtector>().Protect(json);
                    configure?.Invoke(c);
                }))
        };
        return _app;
    }

    private static async Task<HttpClient> SignedIn(DashboardAppFactory app, DashboardSummary? summary)
    {
        var client = app.CreateNonRedirectingClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = FakeActiveRolesServer.ValidUser,
                ["Password"] = FakeActiveRolesServer.ValidPassword
            })
        };
        login.Headers.Accept.ParseAdd("application/json");
        var response = await client.SendAsync(login);
        Assert.Contains("/app/", await response.Content.ReadAsStringAsync());

        var cache = app.Services.GetRequiredService<PerUserSummaryCache>();
        if (summary != null)
            cache.SetSummary(FakeActiveRolesServer.ValidUser, JsonSerializer.Serialize(summary));
        return client;
    }

    private static DashboardSummary AdSummary()
    {
        var summary = new DashboardSummary();
        summary.DomainControllers.Items.Add(new DomainControllerInfo { Name = "dc01", IsGlobalCatalog = true });
        summary.DomainControllers.Items.Add(new DomainControllerInfo { Name = "dc02" });
        return summary;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    // ------------------------------------------------------------------ targets

    [Fact]
    public async Task Targets_lists_id_name_type_and_tests_without_internal_details()
    {
        using var client = await SignedIn(App(), AdSummary());

        var body = (await Json(await client.GetAsync("/api/Diagnostics/targets?dashboard=ActiveDirectory"))).AsArray();

        Assert.Equal(["dc:dc01", "gc:dc01", "dc:dc02"], body.Select(t => t!["id"]!.GetValue<string>()));
        var gc = body[1]!.AsObject();
        Assert.Equal("dc01", gc["name"]!.GetValue<string>());
        Assert.Equal("GlobalCatalog", gc["serverType"]!.GetValue<string>());
        Assert.Equal(["Ping", "LdapBind"], gc["applicableTests"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(["applicableTests", "id", "name", "serverType"], gc.Select(p => p.Key).Order());
    }

    [Fact]
    public async Task Targets_reports_error_when_no_summary_is_cached()
    {
        using var client = await SignedIn(App(), summary: null);

        var body = await Json(await client.GetAsync("/api/Diagnostics/targets?dashboard=ActiveDirectory"));

        Assert.Equal("No collected dashboard data is available yet.", body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Targets_reports_error_when_cached_summary_is_corrupt()
    {
        using var client = await SignedIn(App(), summary: null);
        _app!.Services.GetRequiredService<PerUserSummaryCache>().SetSummary(FakeActiveRolesServer.ValidUser, "{not json");

        var body = await Json(await client.GetAsync("/api/Diagnostics/targets?dashboard=ActiveDirectory"));

        Assert.NotNull(body["error"]);
    }

    // ------------------------------------------------------------------ trends

    [Fact]
    public async Task Trends_returns_configuration_metadata_when_empty()
    {
        using var client = await SignedIn(App(c =>
        {
            c.PerformanceTrending.SnapshotIntervalMinutes = 1;
            c.PerformanceTrending.RetentionHours = 12;
        }), AdSummary());

        var body = await Json(await client.GetAsync("/api/Diagnostics/trends?dashboard=ActiveDirectory"));

        Assert.Equal("ActiveDirectory", body["dashboard"]!.GetValue<string>());
        Assert.Equal(PerformanceTrendingConfig.MinimumIntervalMinutes, body["intervalMinutes"]!.GetValue<int>());
        Assert.Equal(12, body["retentionHours"]!.GetValue<int>());
        Assert.Empty(body["series"]!.AsArray());
        Assert.Null(body["oldestSampleUtc"]);
        Assert.Null(body["newestSampleUtc"]);
    }

    [Theory]
    [InlineData(null, 3)]   // full retention window
    [InlineData(0, 3)]      // non-positive ignored
    [InlineData(-5, 3)]
    [InlineData(48, 3)]     // larger than retention is clamped
    [InlineData(2, 2)]      // narrower window
    [InlineData(1, 1)]
    public async Task Trends_window_is_clamped_to_retention(int? periodHours, int expectedSamples)
    {
        using var client = await SignedIn(App(c => c.PerformanceTrending.RetentionHours = 24), AdSummary());
        var store = _app!.Services.GetRequiredService<PerformanceTrendStore>();
        var now = DateTimeOffset.UtcNow;
        foreach (var hoursAgo in new[] { 20.0, 1.5, 0.25 })
        {
            store.Record(DiagnosticsDashboard.ActiveDirectory, new DiagnosticsResult
            {
                CompletedUtc = now.AddHours(-hoursAgo),
                Probes = [new DiagnosticsProbeResult { TargetId = "dc:dc01", TargetName = "dc01", TestType = DiagnosticsTestType.Ping, LatencyMs = 5 }]
            }, TimeSpan.FromHours(24));
        }

        var url = "/api/Diagnostics/trends?dashboard=ActiveDirectory" + (periodHours is null ? "" : $"&periodHours={periodHours}");
        var body = await Json(await client.GetAsync(url));

        var series = Assert.Single(body["series"]!.AsArray())!;
        Assert.Equal(expectedSamples, series["samples"]!.AsArray().Count);
        Assert.Equal("Ping", series["testType"]!.GetValue<string>());
        var oldest = body["oldestSampleUtc"]!.GetValue<DateTimeOffset>();
        var newest = body["newestSampleUtc"]!.GetValue<DateTimeOffset>();
        Assert.True(oldest <= newest);
        Assert.Equal(now.AddHours(-0.25), newest, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Trends_are_isolated_per_dashboard()
    {
        using var client = await SignedIn(App(), AdSummary());
        _app!.Services.GetRequiredService<PerformanceTrendStore>().Record(DiagnosticsDashboard.ActiveRoles, new DiagnosticsResult
        {
            CompletedUtc = DateTimeOffset.UtcNow,
            Probes = [new DiagnosticsProbeResult { TargetId = "rsts", TestType = DiagnosticsTestType.RstsAuth }]
        }, TimeSpan.FromHours(24));

        var ad = await Json(await client.GetAsync("/api/Diagnostics/trends?dashboard=ActiveDirectory"));
        var ar = await Json(await client.GetAsync("/api/Diagnostics/trends?dashboard=ActiveRoles"));

        Assert.Empty(ad["series"]!.AsArray());
        Assert.Single(ar["series"]!.AsArray());
    }

    // ------------------------------------------------------------------ run

    [Fact]
    public async Task Run_without_body_is_rejected()
    {
        using var client = await SignedIn(App(), AdSummary());

        var response = await client.PostAsync("/api/Diagnostics/run",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Run_reports_error_when_no_summary_is_cached()
    {
        using var client = await SignedIn(App(), summary: null);

        var body = await Json(await client.PostAsJsonAsync("/api/Diagnostics/run", new { dashboard = "ActiveRoles" }));

        Assert.Equal("No collected dashboard data is available yet.", body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Run_executes_filtered_probes_and_returns_result_shape()
    {
        var summary = new DashboardSummary();
        summary.ConfigDatabases.Items.Add(new DatabaseInfo { SqlAlias = "sql01", DatabaseName = "" });
        summary.ConfigDatabases.Items.Add(new DatabaseInfo { SqlAlias = "sql02", DatabaseName = "" });
        using var client = await SignedIn(App(), summary);

        // SqlQuery with no database name is skipped, so no real SQL/ICMP traffic is generated.
        var body = await Json(await client.PostAsJsonAsync("/api/Diagnostics/run", new
        {
            dashboard = "ActiveRoles",
            testType = "SqlQuery",
            targetIds = new[] { "sql:sql02:" }
        }));

        Assert.Equal("ActiveRoles", body["dashboard"]!.GetValue<string>());
        Assert.Equal(0, body["okCount"]!.GetValue<int>());
        Assert.Equal(0, body["warnCount"]!.GetValue<int>());
        Assert.Equal(0, body["failCount"]!.GetValue<int>());
        Assert.True(body["completedUtc"]!.GetValue<DateTimeOffset>() >= body["startedUtc"]!.GetValue<DateTimeOffset>());

        var probe = Assert.Single(body["probes"]!.AsArray())!;
        Assert.Equal("sql:sql02:", probe["targetId"]!.GetValue<string>());
        Assert.Equal("sql02", probe["targetName"]!.GetValue<string>());
        Assert.Equal("SqlServer", probe["serverType"]!.GetValue<string>());
        Assert.Equal("SqlQuery", probe["testType"]!.GetValue<string>());
        Assert.Equal("Skipped", probe["status"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(probe["message"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Run_does_not_record_on_demand_results_in_trend_store()
    {
        var summary = new DashboardSummary();
        summary.ConfigDatabases.Items.Add(new DatabaseInfo { SqlAlias = "sql01", DatabaseName = "" });
        using var client = await SignedIn(App(), summary);

        await Json(await client.PostAsJsonAsync("/api/Diagnostics/run", new { dashboard = "ActiveRoles", testType = "SqlQuery" }));

        Assert.Empty(_app!.Services.GetRequiredService<PerformanceTrendStore>()
            .GetSeries(DiagnosticsDashboard.ActiveRoles, TimeSpan.FromHours(24)));
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
