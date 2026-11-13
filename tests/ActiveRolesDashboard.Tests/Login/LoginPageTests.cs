using System.Net;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Login;

[Trait("Stage", "Login")]
public class LoginPageTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private readonly DashboardAppFactory _app;

    public LoginPageTests() => _app = new DashboardAppFactory(activeRoles: _server);

    private static FormUrlEncodedContent Form(string user, string password) =>
        new(new Dictionary<string, string> { ["Username"] = user, ["Password"] = password });

    private static HttpRequestMessage JsonPost(string user, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Login") { Content = Form(user, password) };
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    [Fact]
    public async Task Login_page_renders_anonymously()
    {
        using var client = _app.CreateNonRedirectingClient();
        var response = await client.GetAsync("/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Password", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(@"TEST\admin", "")]
    [InlineData("", "P@ssw0rd!")]
    public async Task Missing_credentials_rerender_page_without_calling_rsts(string user, string password)
    {
        using var client = _app.CreateNonRedirectingClient();
        var before = _server.Requests.Count(r => r.RequestMessage?.Path?.Contains("RSTS") == true);

        var response = await client.PostAsync("/Login", Form(user, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, _server.Requests.Count(r => r.RequestMessage?.Path?.Contains("RSTS") == true));
        Assert.DoesNotContain(".AspNetCore.Cookies", string.Join(";", response.Headers.TryGetValues("Set-Cookie", out var c) ? c : []));
    }

    [Fact]
    public async Task Invalid_credentials_do_not_sign_in()
    {
        using var client = _app.CreateNonRedirectingClient();

        var response = await client.SendAsync(JsonPost(@"TEST\admin", "wrong"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var c) ? string.Join(";", c) : string.Empty;
        Assert.DoesNotContain(".AspNetCore.Cookies", cookies);
        Assert.Contains("Authentication failed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Valid_credentials_sign_in_and_return_app_redirect()
    {
        using var client = _app.CreateNonRedirectingClient();

        var response = await client.SendAsync(JsonPost(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"redirectUrl\":\"/app/\"", await response.Content.ReadAsStringAsync());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith(".AspNetCore.Cookies"));
    }

    [Fact]
    public async Task Rsts_outage_is_reported_without_sign_in()
    {
        _server.Reset();
        _server.WithTokenEndpointStatus(503).WithApiRoot().WithEmptySearchDefault();
        using var client = _app.CreateNonRedirectingClient();

        var response = await client.SendAsync(JsonPost(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var c) && c.Any(v => v.StartsWith(".AspNetCore.Cookies")));
    }

    [Theory]
    [InlineData("/Settings")]
    [InlineData("/Snapshots")]
    [InlineData("/Assessments")]
    public async Task Protected_pages_redirect_anonymous_users_to_login(string path)
    {
        using var client = _app.CreateNonRedirectingClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Logout_clears_authentication_and_redirects_to_login()
    {
        using var client = _app.CreateNonRedirectingClient();
        var login = await client.SendAsync(JsonPost(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword));
        var authCookie = login.Headers.GetValues("Set-Cookie").First(v => v.StartsWith(".AspNetCore.Cookies")).Split(';')[0];

        var request = new HttpRequestMessage(HttpMethod.Get, "/Logout");
        request.Headers.Add("Cookie", authCookie);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith(".AspNetCore.Cookies=;"));
    }

    public void Dispose()
    {
        _app.Dispose();
        _server.Dispose();
    }
}
