using System.Net;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ActiveRolesDashboard.Tests.Setup;

/// <summary>
/// Drives the first-run Setup wizard and the Logout page through the real pipeline: first-run
/// gating, validation re-renders, persistence of the submitted settings and post-setup redirects.
/// </summary>
[Trait("Stage", "Setup")]
public class SetupPageTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();

    private DashboardAppFactory Unconfigured() => new(AppSettingsBuilder.Default(null).Build(), _server);

    private Dictionary<string, string> ValidForm() => new()
    {
        ["ApiBaseUrl"] = _server.ApiBaseUrl,
        ["RstsUrl"] = _server.RstsUrl,
        ["ServiceAccountUsername"] = FakeActiveRolesServer.ValidUser,
        ["ServiceAccountPassword"] = FakeActiveRolesServer.ValidPassword,
    };

    private static JsonObject ActiveRoles(DashboardAppFactory app) =>
        app.ContentRoot.ReadAppSettings()["ActiveRoles"]!.AsObject();

    // ------------------------------------------------------------------ first-run gating

    [Theory]
    [InlineData("/")]
    [InlineData("/Login")]
    [InlineData("/Logout")]
    public async Task Unconfigured_app_redirects_requests_to_setup(string path)
    {
        using var app = Unconfigured();
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Setup", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Setup_renders_form_when_unconfigured()
    {
        using var app = Unconfigured();
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/Setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"setupForm\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Setup_redirects_to_login_when_already_configured()
    {
        using var app = new DashboardAppFactory(activeRoles: _server);
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/Setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString);
    }

    // ------------------------------------------------------------------ validation

    [Theory]
    [InlineData("ApiBaseUrl", "REST API URL is required.")]
    [InlineData("RstsUrl", "RSTS Token URL is required.")]
    [InlineData("ServiceAccountUsername", "Service account username is required.")]
    [InlineData("ServiceAccountPassword", "Service account password is required.")]
    public async Task Missing_mandatory_field_rerenders_with_error_and_does_not_persist(string field, string expectedError)
    {
        using var app = Unconfigured();
        using var client = app.CreateNonRedirectingClient();
        var before = app.ContentRoot.ReadAppSettings().ToJsonString();
        var form = ValidForm();
        form[field] = "   ";

        var response = await client.PostAsync("/Setup", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedError, await response.Content.ReadAsStringAsync());
        Assert.Equal(before, app.ContentRoot.ReadAppSettings().ToJsonString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("2001")]
    public async Task Out_of_range_page_size_rerenders_with_error_and_does_not_persist(string pageSize)
    {
        using var app = Unconfigured();
        using var client = app.CreateNonRedirectingClient();
        var before = app.ContentRoot.ReadAppSettings().ToJsonString();
        var form = ValidForm();
        form["PageSize"] = pageSize;

        var response = await client.PostAsync("/Setup", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"error-msg\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, app.ContentRoot.ReadAppSettings().ToJsonString());
    }

    // ------------------------------------------------------------------ persistence

    [Fact]
    public async Task Valid_post_persists_trimmed_settings_and_encrypts_password()
    {
        using var app = Unconfigured();
        using var client = app.CreateNonRedirectingClient();
        var form = ValidForm();
        form["ApiBaseUrl"] = $"  {_server.ApiBaseUrl}  ";
        form["RstsUrl"] = $"  {_server.RstsUrl}  ";
        form["RstsScope"] = "  ar.api  ";
        form["ServiceAccountUsername"] = $"  {FakeActiveRolesServer.ValidUser}  ";
        form["PageSize"] = "250";
        form["WebInterfaceUrl"] = "  https://ar.example/ui  ";
        form["CustomNoManagerUserFilter"] = "  (department=IT)  ";

        var response = await client.PostAsync("/Setup", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString);

        var ar = ActiveRoles(app);
        Assert.Equal(_server.ApiBaseUrl, (string?)ar["ApiBaseUrl"]);
        Assert.Equal(_server.RstsUrl, (string?)ar["RstsUrl"]);
        Assert.Equal("ar.api", (string?)ar["RstsScope"]);
        Assert.Equal(250, (int?)ar["PageSize"]);
        Assert.Equal("https://ar.example/ui", (string?)ar["WebInterfaceUrl"]);
        Assert.Equal("(department=IT)", (string?)ar["CustomFilters"]!["NoManagerUser"]);

        var serviceAccount = ar["ServiceAccount"]!.AsObject();
        Assert.Equal(FakeActiveRolesServer.ValidUser, (string?)serviceAccount["Username"]);
        var protectedPassword = (string?)serviceAccount["ProtectedPassword"];
        Assert.False(string.IsNullOrEmpty(protectedPassword));
        Assert.DoesNotContain(FakeActiveRolesServer.ValidPassword, app.ContentRoot.ReadAppSettings().ToJsonString());
        var protector = app.Services.GetRequiredService<ServiceAccountSecretProtector>();
        Assert.Equal(FakeActiveRolesServer.ValidPassword, protector.Unprotect(protectedPassword!));
    }

    [Fact]
    public async Task Valid_post_normalizes_language_licensing_role_groups_and_analytics()
    {
        using var app = Unconfigured();
        using var client = app.CreateNonRedirectingClient();
        var form = ValidForm();
        form["Language"] = "xx-NOPE";
        form["LicensedDomainObjects"] = "-10";
        form["LicensedTotalObjects"] = "500";
        form["DashboardAdminsGroup"] = "  Custom Admins  ";
        form["AuditorsGroup"] = "   ";
        form["AnalyticsEnabled"] = "true";
        form["AnalyticsMeasurementId"] = "  G-TEST123  ";

        var response = await client.PostAsync("/Setup", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var ar = ActiveRoles(app);

        Assert.Equal("en", (string?)ar["DefaultLanguage"]);

        var licensing = ar["Licensing"]!.AsObject();
        Assert.Equal(0, (int?)licensing["DomainObjects"]);
        Assert.Equal(500, (int?)licensing["TotalObjects"]);

        var roleGroups = ar["RoleGroups"]!.AsObject();
        var defaults = new ActiveRolesDashboard.Models.RoleGroupsConfig();
        Assert.Equal("Custom Admins", (string?)roleGroups["DashboardAdmins"]);
        Assert.Equal(defaults.Auditors, (string?)roleGroups["Auditors"]);
        Assert.Equal(defaults.ActiveRolesAdmins, (string?)roleGroups["ActiveRolesAdmins"]);
        Assert.Equal(defaults.PowerUsers, (string?)roleGroups["PowerUsers"]);

        var analytics = ar["Analytics"]!.AsObject();
        Assert.True((bool?)analytics["Enabled"]);
        Assert.True((bool?)analytics["ConsentGiven"]);
        Assert.Equal("G-TEST123", (string?)analytics["MeasurementId"]);
    }

    [Fact]
    public async Task Completed_setup_takes_effect_without_restart()
    {
        using var app = Unconfigured();
        using var client = app.CreateNonRedirectingClient();

        var post = await client.PostAsync("/Setup", new FormUrlEncodedContent(ValidForm()));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var setup = await client.GetAsync("/Setup");
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        Assert.Contains("/Login", setup.Headers.Location!.OriginalString);

        var login = await client.GetAsync("/Login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ------------------------------------------------------------------ logout

    [Fact]
    public async Task Anonymous_logout_redirects_to_login()
    {
        using var app = new DashboardAppFactory(activeRoles: _server);
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/Logout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString);
    }

    public void Dispose() => _server.Dispose();
}
