using System.Net;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Exposure;

[Trait("Stage", "Exposure")]
public class AttackExposurePageTests
{
    [Theory]
    [InlineData("/AttackExposure")]
    [InlineData("/AttackExposure?FromId=../appsettings&ToId=current")]
    public async Task Anonymous_requests_are_redirected_to_login(string path)
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = new DashboardAppFactory(activeRoles: server);
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString);
    }
}
