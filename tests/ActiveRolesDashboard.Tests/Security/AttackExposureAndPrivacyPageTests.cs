using System.Net;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Signed-in page-load coverage for the AttackExposure and Privacy Razor Pages.
/// </summary>
[Trait("Stage", "Security")]
public class AttackExposureAndPrivacyPageTests : IDisposable
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

    // ---------------------------------------------------------------- AttackExposure

    [Fact]
    public async Task AttackExposure_loads_for_user_with_view_permission()
    {
        using var client = await SignedIn(App(DashboardPermission.ViewExposureReport));

        var response = await client.GetAsync("/AttackExposure");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttackExposure_redirects_to_app_without_view_permission()
    {
        using var client = await SignedIn(App(DashboardPermission.ViewAssessments));

        var response = await client.GetAsync("/AttackExposure");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/app/", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("?FromId=missing&ToId=current")]
    [InlineData("?FromId=../appsettings&ToId=current")]
    public async Task AttackExposure_with_unknown_comparison_ids_still_loads(string query)
    {
        using var client = await SignedIn(App(DashboardPermission.ViewExposureReport, DashboardPermission.CompareExposureReports));

        var response = await client.GetAsync($"/AttackExposure{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------- Privacy

    [Fact]
    public async Task Privacy_loads_when_signed_in()
    {
        using var client = await SignedIn(App());

        var response = await client.GetAsync("/Privacy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
