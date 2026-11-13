using System.Net;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.InfrastructureTests;

/// <summary>Verifies the in-memory dashboard host starts and is isolated from the repository.</summary>
[Trait("Category", "Infrastructure")]
public class DashboardAppFactoryTests
{
    [Fact]
    public async Task Unconfigured_app_redirects_to_setup()
    {
        using var app = new DashboardAppFactory(AppSettingsBuilder.Default(null).Build());
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Setup", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Configured_app_serves_anonymous_cache_status()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = new DashboardAppFactory(activeRoles: server);
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/cache/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"ready\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task App_writes_state_only_into_temp_content_root()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = new DashboardAppFactory(activeRoles: server);
        using var client = app.CreateNonRedirectingClient();

        await client.GetAsync("/cache/status");

        Assert.StartsWith(Path.GetTempPath(), app.ContentRoot.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(app.ContentRoot.DataProtectionKeysPath));
        Assert.Equal(server.ApiBaseUrl, app.ContentRoot.ReadAppSettings()["ActiveRoles"]!["ApiBaseUrl"]!.GetValue<string>());
    }

    [Fact]
    public void Disposing_factory_removes_temp_content_root()
    {
        string path;
        using (var app = new DashboardAppFactory(AppSettingsBuilder.Default(null).Build()))
        {
            _ = app.Services;
            path = app.ContentRoot.Path;
            Assert.True(Directory.Exists(path));
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Clock_is_replaced_with_fake_time()
    {
        using var app = new DashboardAppFactory(AppSettingsBuilder.Default(null).Build());

        var clock = app.Services.GetService(typeof(TimeProvider));

        Assert.Same(app.Clock, clock);
    }
}
