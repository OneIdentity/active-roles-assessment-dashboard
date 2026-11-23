using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// End-to-end access control on the real app: anonymous callers are rejected everywhere, and
/// signed-in callers are allowed or forbidden by the permissions of their resolved role. The fake
/// directory cannot resolve the user's DN, so they resolve to the User role; its permissions are
/// set through the protected role matrix (the same path production uses).
/// </summary>
[Trait("Stage", "Security")]
public class AccessControlTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private DashboardAppFactory? _app;

    private DashboardAppFactory App(params DashboardPermission[] userPermissions)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, List<string>>
        {
            [nameof(DashboardRole.User)] = userPermissions.Select(p => p.ToString()).ToList()
        });

        _app = new DashboardAppFactory(activeRoles: _server)
        {
            ConfigureServices = services => services.AddSingleton<IPostConfigureOptions<ActiveRolesConfig>>(sp =>
                new PostConfigureOptions<ActiveRolesConfig>(Options.DefaultName,
                    c => c.Roles.ProtectedMatrix = sp.GetRequiredService<RolePermissionProtector>().Protect(json)))
        };
        return _app;
    }

    private static async Task<HttpClient> SignedIn(DashboardAppFactory app)
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

        app.Services.GetRequiredService<PerUserSummaryCache>()
            .SetSummary(FakeActiveRolesServer.ValidUser, JsonSerializer.Serialize(new DashboardSummary()));
        return client;
    }

    private static void AssertRejected(HttpResponseMessage response)
    {
        Assert.False(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri} returned {response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("/Login", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        else
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest,
                response.StatusCode.ToString());
    }

    private static void AssertForbidden(HttpResponseMessage response)
    {
        Assert.False(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri} returned {response.StatusCode}");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, response.StatusCode.ToString());
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.DoesNotContain("/app", response.Headers.Location!.OriginalString);
    }

    // ---------------------------------------------------------------- anonymous

    [Theory]
    [InlineData("/api/dashboard")]
    [InlineData("/api/dashboard/view/ActiveDirectory")]
    [InlineData("/api/dashboard/kpi/StaleUsers")]
    [InlineData("/api/dashboard/refresh-status")]
    [InlineData("/api/Diagnostics/targets?dashboard=ActiveRoles")]
    [InlineData("/api/DrillDown/domains")]
    [InlineData("/api/DrillDown/accesstemplates")]
    [InlineData("/api/DrillDown/details/00000000-0000-0000-0000-000000000001")]
    [InlineData("/Settings")]
    public async Task Anonymous_get_is_rejected(string url)
    {
        using var client = App().CreateNonRedirectingClient();

        AssertRejected(await client.GetAsync(url));
    }

    [Theory]
    [InlineData("/api/dashboard/rebuild")]
    [InlineData("/api/dashboard/segment")]
    [InlineData("/Export")]
    public async Task Anonymous_post_is_rejected(string url)
    {
        using var client = App().CreateNonRedirectingClient();

        AssertRejected(await client.PostAsync(url, new FormUrlEncodedContent([])));
    }

    [Fact]
    public async Task Anonymous_diagnostics_run_is_rejected()
    {
        using var client = App().CreateNonRedirectingClient();

        AssertRejected(await client.PostAsJsonAsync("/api/Diagnostics/run", new { dashboard = "ActiveRoles" }));
    }

    [Fact]
    public async Task Forged_auth_cookie_is_rejected()
    {
        using var client = App().CreateNonRedirectingClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.Add("Cookie", ".AspNetCore.Cookies=CfDJ8forgedvalue; .AspNetCore.Session=CfDJ8forged");

        AssertRejected(await client.SendAsync(request));
    }

    // ---------------------------------------------------------------- signed in, role gated

    [Fact]
    public async Task Signed_in_user_can_read_dashboard_and_capabilities_reflect_role()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageUserSettings));

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"User\"", body);
        Assert.DoesNotContain("\"ActiveRolesAdmin\"", body);
        Assert.Contains("\"isActiveRolesAdmin\":false", body);
    }

    [Fact]
    public async Task Kpi_search_index_only_lists_kpis_on_permitted_tabs()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageUserSettings));

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/dashboard"));
        var tabs = doc.RootElement.GetProperty("tabs").EnumerateArray()
            .Select(t => t.GetProperty("key").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = doc.RootElement.GetProperty("kpiIndex").EnumerateArray().ToList();

        Assert.NotEmpty(index);
        Assert.All(index, item =>
        {
            Assert.Contains(item.GetProperty("dashboardKey").GetString(), tabs);
            Assert.False(string.IsNullOrEmpty(item.GetProperty("kpi").GetProperty("label").GetString()));
        });
    }

    [Fact]
    public async Task User_without_rebuild_permission_cannot_view_refresh_status()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageUserSettings));

        AssertForbidden(await client.GetAsync("/api/dashboard/refresh-status"));
    }

    [Fact]
    public async Task User_with_rebuild_permission_can_view_refresh_status()
    {
        using var client = await SignedIn(App(DashboardPermission.RebuildCache));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/refresh-status")).StatusCode);
    }

    [Fact]
    public async Task User_without_rebuild_permission_cannot_trigger_rebuild()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageUserSettings));

        AssertForbidden(await client.PostAsync("/api/dashboard/rebuild", new FormUrlEncodedContent([])));
    }

    [Theory]
    [InlineData("ActiveRoles", DashboardPermission.RunActiveDirectoryPerformanceTests)]
    [InlineData("ActiveDirectory", DashboardPermission.RunActiveRolesPerformanceTests)]
    [InlineData("EntraId", DashboardPermission.ExportDashboardData)]
    [InlineData("Exchange", DashboardPermission.RebuildCache)]
    public async Task Diagnostics_require_the_matching_dashboard_permission(string dashboard, DashboardPermission unrelated)
    {
        using var client = await SignedIn(App(unrelated));

        AssertForbidden(await client.GetAsync($"/api/Diagnostics/targets?dashboard={dashboard}"));
        AssertForbidden(await client.GetAsync($"/api/Diagnostics/trends?dashboard={dashboard}"));
    }

    [Fact]
    public async Task Diagnostics_allowed_with_matching_permission()
    {
        using var client = await SignedIn(App(DashboardPermission.RunActiveDirectoryPerformanceTests));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Diagnostics/targets?dashboard=ActiveDirectory")).StatusCode);
    }

    [Fact]
    public async Task Diagnostics_run_body_cannot_escalate_to_another_dashboard()
    {
        using var client = await SignedIn(App(DashboardPermission.RunActiveDirectoryPerformanceTests));

        AssertForbidden(await client.PostAsJsonAsync("/api/Diagnostics/run", new { dashboard = "ActiveRoles" }));
    }

    [Fact]
    public async Task Settings_page_hides_system_settings_without_permission()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageUserSettings));

        var response = await client.GetAsync("/Settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_server.RstsUrl, html);
    }

    [Fact]
    public async Task Settings_page_shows_system_settings_with_view_permission()
    {
        using var client = await SignedIn(App(DashboardPermission.ViewSystemSettings));

        var html = await (await client.GetAsync("/Settings")).Content.ReadAsStringAsync();

        Assert.Contains(_server.RstsUrl, html);
    }

    [Fact]
    public async Task Role_matrix_change_takes_effect_without_relogin()
    {
        var app = App(DashboardPermission.ManageUserSettings);
        using var client = await SignedIn(app);
        AssertForbidden(await client.GetAsync("/api/dashboard/refresh-status"));

        var monitor = app.Services.GetRequiredService<IOptionsMonitor<ActiveRolesConfig>>();
        monitor.CurrentValue.Roles.ProtectedMatrix = app.Services.GetRequiredService<RolePermissionProtector>()
            .Protect(JsonSerializer.Serialize(new Dictionary<string, List<string>> { ["User"] = ["RebuildCache"] }));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/refresh-status")).StatusCode);
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
