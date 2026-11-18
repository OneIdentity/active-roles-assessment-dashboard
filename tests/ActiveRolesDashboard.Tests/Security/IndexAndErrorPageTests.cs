using System.Net;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Simple page coverage: the configured root sends callers to the SPA shell with session
/// restore, and the Error page renders uncached without leaking exception details.
/// </summary>
[Trait("Stage", "Security")]
public class IndexAndErrorPageTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private readonly DashboardAppFactory _app;

    public IndexAndErrorPageTests() => _app = new DashboardAppFactory(activeRoles: _server);

    [Theory]
    [InlineData("/")]
    [InlineData("/Index")]
    public async Task Configured_root_redirects_to_app_with_restore(string url)
    {
        using var client = _app.CreateNonRedirectingClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/app/?restore=1", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Error_page_renders_request_id_and_is_not_cached()
    {
        using var client = _app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/Error");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "Error page must not be cached");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<html", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body);
    }

    public void Dispose()
    {
        _app.Dispose();
        _server.Dispose();
    }
}
