using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// The admin-only /diagnostics/validate-filter endpoint: anonymous callers are rejected, signed-in
/// non-admins are forbidden, admins must supply a username, and a harness that cannot run (no
/// superset loaded yet) surfaces as 503 rather than a generic failure.
/// </summary>
[Trait("Stage", "Security")]
public class ValidateFilterEndpointTests : IDisposable
{
    private const string UserDn = "CN=Admin,OU=Users,DC=test,DC=local";

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private DashboardAppFactory? _app;

    private DashboardAppFactory App()
    {
        _app = new DashboardAppFactory(activeRoles: _server)
        {
            ConfigureServices = services => services.AddSingleton<IPostConfigureOptions<ActiveRolesConfig>>(
                new PostConfigureOptions<ActiveRolesConfig>(Options.DefaultName,
                    c => c.RoleGroups.ActiveRolesAdmins = "AR Admins"))
        };
        return _app;
    }

    private void MakeUserActiveRolesAdmin()
    {
        _server.WithSearch("samAccountName=admin",
            [new JsonObject { ["sAMAccountName"] = "admin", ["distinguishedName"] = UserDn }], priority: 20);
        _server.WithSearch("name=AR Admins)",
            [new JsonObject { ["distinguishedName"] = "CN=AR Admins,OU=Groups,DC=test,DC=local", ["edsaMember"] = new JsonArray(UserDn) }],
            priority: 20);
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

    [Fact]
    public async Task Anonymous_caller_is_rejected()
    {
        using var client = App().CreateNonRedirectingClient();

        var response = await client.GetAsync("/diagnostics/validate-filter?username=someone");

        Assert.False(response.IsSuccessStatusCode);
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("/Login", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        else
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_non_admin_is_forbidden()
    {
        using var client = await SignedIn(App());

        var response = await client.GetAsync("/diagnostics/validate-filter?username=someone");

        Assert.False(response.IsSuccessStatusCode);
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, response.StatusCode.ToString());
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.DoesNotContain("/app", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/diagnostics/validate-filter?username=")]
    [InlineData("/diagnostics/validate-filter?username=%20%20")]
    public async Task Admin_with_blank_username_gets_bad_request(string url)
    {
        MakeUserActiveRolesAdmin();
        using var client = await SignedIn(App());

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("username is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_gets_service_unavailable_when_superset_is_not_loaded()
    {
        MakeUserActiveRolesAdmin();
        using var client = await SignedIn(App());

        var response = await client.GetAsync("/diagnostics/validate-filter?username=someone");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("Cache is not ready", json.RootElement.GetProperty("error").GetString());
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
