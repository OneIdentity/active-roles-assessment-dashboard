using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Login;

/// <summary>
/// Directory facts evaluated through the real login flow: a role change between logins discards the
/// user's role-scoped cached data and takes effect immediately, an unchanged role keeps it, and a
/// directory outage at login keeps the user's last-known role instead of downgrading them. The
/// Auditor role is granted a diagnostics permission the User role lacks so the effective role can be
/// observed through an authorized endpoint.
/// </summary>
[Trait("Stage", "Login")]
public class LoginDirectoryFactsTests : IDisposable
{
    private const string UserDn = "CN=Admin,OU=Users,DC=test,DC=local";
    private const string Username = FakeActiveRolesServer.ValidUser;
    private const string AuditorOnlyUrl = "/api/Diagnostics/targets?dashboard=ActiveRoles";

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private readonly DashboardAppFactory _app;

    public LoginDirectoryFactsTests()
    {
        var matrix = JsonSerializer.Serialize(new Dictionary<string, List<string>>
        {
            [nameof(DashboardRole.User)] = [],
            [nameof(DashboardRole.Auditor)] = [nameof(DashboardPermission.RunActiveRolesPerformanceTests)]
        });

        _app = new DashboardAppFactory(activeRoles: _server)
        {
            ConfigureServices = services => services.AddSingleton<IPostConfigureOptions<ActiveRolesConfig>>(sp =>
                new PostConfigureOptions<ActiveRolesConfig>(Options.DefaultName, c =>
                {
                    c.RoleGroups.ActiveRolesAdmins = "AR Admins";
                    c.RoleGroups.DashboardAdmins = "Dash Admins";
                    c.RoleGroups.Auditors = "Auditors";
                    c.RoleGroups.PowerUsers = "Power Users";
                    c.Roles.ProtectedMatrix = sp.GetRequiredService<RolePermissionProtector>().Protect(matrix);
                }))
        };

        _server.WithSearch("samAccountName=admin", [new JsonObject { ["sAMAccountName"] = "admin", ["distinguishedName"] = UserDn }], priority: 20);
    }

    private PerUserSummaryCache UserCache => _app.Services.GetRequiredService<PerUserSummaryCache>();

    private void AuditorMembership(bool member, int priority) =>
        _server.WithSearch("name=Auditors)",
            [new JsonObject { ["distinguishedName"] = "CN=Auditors,OU=Groups,DC=test,DC=local", ["edsaMember"] = member ? new JsonArray(UserDn) : new JsonArray() }],
            priority: priority);

    private void DirectoryOutage() =>
        _server.WithSearchError("(objectClass=user)(samAccountName=admin)", 503, priority: 1);

    private void SeedUserData()
    {
        UserCache.SetSummary(Username, JsonSerializer.Serialize(new DashboardSummary()));
        UserCache.SetOverview(Username, "{}");
    }

    private async Task<(HttpClient Client, HttpResponseMessage Response)> Login()
    {
        var client = _app.CreateNonRedirectingClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = Username,
                ["Password"] = FakeActiveRolesServer.ValidPassword
            })
        };
        request.Headers.Accept.ParseAdd("application/json");
        return (client, await client.SendAsync(request));
    }

    private async Task<HttpClient> SignedIn()
    {
        var (client, response) = await Login();
        Assert.Contains("/app/", await response.Content.ReadAsStringAsync());
        return client;
    }

    private static bool SetsAuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith(".AspNetCore.Cookies="));

    private static void AssertDenied(HttpStatusCode status) =>
        Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, status.ToString());

    private async Task<HttpStatusCode> AuditorEndpointStatus(HttpClient client)
    {
        // The endpoint needs a cached summary for the signed-in user; seed one only if absent so
        // tests can still assert on whether login discarded the previous one.
        if (UserCache.GetSummary(Username) is null)
            UserCache.SetSummary(Username, JsonSerializer.Serialize(new DashboardSummary()));
        return (await client.GetAsync(AuditorOnlyUrl)).StatusCode;
    }

    // ---------------------------------------------------------------- role changes

    [Fact]
    public async Task First_login_resolves_role_from_directory()
    {
        AuditorMembership(member: true, priority: 20);

        using var client = await SignedIn();

        Assert.Equal(nameof(DashboardRole.Auditor), UserCache.GetRole(Username));
        Assert.Equal(HttpStatusCode.OK, await AuditorEndpointStatus(client));
    }

    [Fact]
    public async Task Promotion_between_logins_takes_effect_and_discards_cached_data()
    {
        AuditorMembership(member: false, priority: 20);
        using (var before = await SignedIn())
            AssertDenied(await AuditorEndpointStatus(before));
        SeedUserData();

        AuditorMembership(member: true, priority: 5);
        using var after = await SignedIn();

        Assert.Null(UserCache.GetSummary(Username));
        Assert.Null(UserCache.GetOverview(Username));
        Assert.Equal(nameof(DashboardRole.Auditor), UserCache.GetRole(Username));
        Assert.Equal(HttpStatusCode.OK, await AuditorEndpointStatus(after));
    }

    [Fact]
    public async Task Demotion_between_logins_takes_effect_and_discards_cached_data()
    {
        AuditorMembership(member: true, priority: 20);
        using (var before = await SignedIn())
            Assert.Equal(HttpStatusCode.OK, await AuditorEndpointStatus(before));
        SeedUserData();

        AuditorMembership(member: false, priority: 5);
        using var after = await SignedIn();

        Assert.Null(UserCache.GetSummary(Username));
        Assert.Equal(nameof(DashboardRole.User), UserCache.GetRole(Username));
        AssertDenied(await AuditorEndpointStatus(after));
    }

    [Fact]
    public async Task Unchanged_role_keeps_cached_data()
    {
        AuditorMembership(member: true, priority: 20);
        using (await SignedIn()) { }
        SeedUserData();

        using var client = await SignedIn();

        Assert.NotNull(UserCache.GetSummary(Username));
        Assert.Equal("{}", UserCache.GetOverview(Username));
        Assert.Equal(HttpStatusCode.OK, await AuditorEndpointStatus(client));
    }

    [Fact]
    public async Task Role_change_does_not_touch_other_users_data()
    {
        const string other = @"TEST\other";
        UserCache.SetSummary(other, "{}");
        AuditorMembership(member: false, priority: 20);
        using (await SignedIn()) { }

        AuditorMembership(member: true, priority: 5);
        using (await SignedIn()) { }

        Assert.Equal("{}", UserCache.GetSummary(other));
    }

    // ---------------------------------------------------------------- directory outage

    [Fact]
    public async Task Outage_at_login_keeps_last_known_role_and_cached_data()
    {
        AuditorMembership(member: true, priority: 20);
        using (await SignedIn()) { }
        SeedUserData();

        DirectoryOutage();
        using var client = await SignedIn();

        Assert.NotNull(UserCache.GetSummary(Username));
        Assert.Equal(nameof(DashboardRole.Auditor), UserCache.GetRole(Username));
        Assert.Equal(HttpStatusCode.OK, await AuditorEndpointStatus(client));
    }

    [Fact]
    public async Task Outage_at_first_ever_login_does_not_sign_in()
    {
        DirectoryOutage();

        var (client, response) = await Login();
        using (client)
        {
            Assert.False(response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("/app/"));
            Assert.False(SetsAuthCookie(response));
        }

        Assert.Null(UserCache.GetRole(Username));
    }

    [Fact]
    public async Task Outage_after_cache_rebuild_does_not_reuse_stale_role()
    {
        AuditorMembership(member: true, priority: 20);
        using (await SignedIn()) { }
        UserCache.InvalidateDirectoryFacts();

        DirectoryOutage();
        var (client, response) = await Login();
        using (client)
            Assert.False(SetsAuthCookie(response));
    }

    public void Dispose()
    {
        _app.Dispose();
        _server.Dispose();
    }
}
