using System.Net;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Exports;

/// <summary>
/// Signed-in export tests. Users whose DN cannot be resolved by the fake directory fall back to
/// the User role; permissions are granted by overriding that role's row of the protected role
/// matrix, exercising the same RoleService path as production.
/// </summary>
[Trait("Stage", "Exports")]
public class ExportControllerTests : IDisposable
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

    private static async Task<HttpClient> SignedInClient(DashboardAppFactory app, bool seedSummary = true)
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

        if (seedSummary)
            app.Services.GetRequiredService<PerUserSummaryCache>()
                .SetSummary(FakeActiveRolesServer.ValidUser, JsonSerializer.Serialize(new DashboardSummary()));
        return client;
    }

    private static FormUrlEncodedContent Form(string format = "Pdf", string scope = "Dashboard", string dashboard = "Main", string? token = null, string? kpi = null, string? category = null)
    {
        var fields = new Dictionary<string, string> { ["Format"] = format, ["Scope"] = scope, ["DashboardKey"] = dashboard, ["IncludeDetails"] = "true" };
        if (token != null) fields["DownloadToken"] = token;
        if (kpi != null) fields["KpiKey"] = kpi;
        if (category != null) fields["CategoryKey"] = category;
        return new FormUrlEncodedContent(fields);
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, response.StatusCode.ToString());
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.DoesNotContain("/app", response.Headers.Location!.OriginalString);
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task User_without_export_permission_is_forbidden()
    {
        using var client = await SignedInClient(App(DashboardPermission.ManageUserSettings));
        AssertDenied(await client.PostAsync("/Export", Form()));
    }

    [Theory]
    [InlineData("Pdf", "application/pdf", ".pdf")]
    [InlineData("Word", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx")]
    [InlineData("Excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    public async Task Permitted_user_downloads_each_format(string format, string contentType, string extension)
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData));

        var response = await client.PostAsync("/Export", Form(format));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        Assert.EndsWith(extension, fileName!.Trim('"'));
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task Download_token_is_echoed_in_cookie()
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData));

        var response = await client.PostAsync("/Export", Form(token: "abc123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("exportDownload=abc123"));
    }

    [Fact]
    public async Task No_download_cookie_without_token()
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData));
        var response = await client.PostAsync("/Export", Form());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var c) && c.Any(v => v.StartsWith("exportDownload")));
    }

    [Fact]
    public async Task Dashboard_without_view_permission_is_forbidden()
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData));
        AssertDenied(await client.PostAsync("/Export", Form(dashboard: "ActiveRoles")));
    }

    [Fact]
    public async Task Dashboard_with_view_permission_is_allowed()
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData, DashboardPermission.ViewActiveRolesDashboard));
        var response = await client.PostAsync("/Export", Form(dashboard: "ActiveRoles"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hidden_exchange_dashboard_is_forbidden_even_with_permissions()
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData, DashboardPermission.ViewExchangeDashboard));
        AssertDenied(await client.PostAsync("/Export", Form(dashboard: "Exchange")));
    }

    [Fact]
    public async Task Kpi_scope_of_disallowed_dashboard_is_forbidden()
    {
        var kpi = KpiInfo.All.First(k => CategoryInfo.All.Any(c => c.DashboardKey == "ActiveRoles" && KpiInfo.ForCategory(c).Any(x => x.Key == k.Key)));
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData));

        AssertDenied(await client.PostAsync("/Export", Form(scope: "Kpi", kpi: kpi.Key)));
    }

    [Fact]
    public async Task Category_scope_of_allowed_dashboard_is_exported()
    {
        var category = CategoryInfo.All.First(c => c.DashboardKey == "ActiveDirectory");
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData, DashboardPermission.ViewActiveDirectoryDashboard));

        var response = await client.PostAsync("/Export", Form(scope: "Category", dashboard: "ActiveDirectory", category: category.Key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Csv")]
    [InlineData("99")]
    public async Task Unknown_format_is_rejected(string format)
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData));
        var response = await client.PostAsync("/Export", Form(format));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_not_allowed()
    {
        using var client = await SignedInClient(App(DashboardPermission.ExportDashboardData));
        var response = await client.GetAsync("/Export");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
