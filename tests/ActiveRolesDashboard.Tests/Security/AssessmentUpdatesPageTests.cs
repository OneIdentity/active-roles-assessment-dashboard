using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.AssessmentUpdates;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Signed-in integration coverage for Settings &gt; Updates &gt; Assessments: category visibility,
/// SHA-256 verified upload (validate vs install), enable/disable/remove/revert and authorization.
/// </summary>
[Trait("Stage", "Security")]
public class AssessmentUpdatesPageTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private DashboardAppFactory? _app;

    private static readonly AssessmentRule Rule = AssessmentRuleLibrary.All.First();

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
        return client;
    }

    private static string Pack(string id = "p1", string version = "1.0") => AssessmentUpdateHasher.Seal($$"""
        { "schemaVersion": 1, "id": "{{id}}", "version": "{{version}}", "title": "Test pack",
          "operations": [ { "op": "modify", "ruleId": "{{Rule.Id}}", "rule": { "title": "Updated title" } } ] }
        """);

    private static Task<HttpResponseMessage> Upload(HttpClient client, string json, bool install)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(file, "packFile", "pack.json");
        content.Add(new StringContent(install ? "true" : "false"), "install");
        return client.PostAsync("/Settings?handler=AssessmentUpdateUpload", content);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string handler, Dictionary<string, string> form) =>
        client.PostAsync($"/Settings?handler={handler}", new FormUrlEncodedContent(form));

    private AssessmentUpdateStore Store => _app!.Services.GetRequiredService<AssessmentUpdateStore>();

    private IReadOnlyList<AssessmentUpdatePackInfo> Packs => Store.BuildRuleSet().Packs;

    [Fact]
    public async Task Updates_category_is_visible_with_update_permission()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageAssessmentUpdates));

        var html = await (await client.GetAsync("/Settings")).Content.ReadAsStringAsync();

        Assert.Contains("id=\"updatesCategory\"", html);
        Assert.Contains("AssessmentUpdateUpload", html);
    }

    [Fact]
    public async Task Updates_category_is_hidden_without_update_permission()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageUserSettings, DashboardPermission.ManageSystemSettings));

        var html = await (await client.GetAsync("/Settings")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("id=\"updatesCategory\"", html);
    }

    [Fact]
    public async Task Validate_reports_hash_without_installing()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageAssessmentUpdates));
        var json = Pack();

        var response = await Upload(client, json, install: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(AssessmentUpdateHasher.Compute(json), await response.Content.ReadAsStringAsync());
        Assert.Empty(Packs);
    }

    [Fact]
    public async Task Install_adds_active_pack()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageAssessmentUpdates));

        var response = await Upload(client, Pack(), install: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pack = Assert.Single(Packs);
        Assert.Equal(AssessmentUpdatePackStatus.Active, pack.Status);
        Assert.Equal(FakeActiveRolesServer.ValidUser, pack.State.ImportedBy);
    }

    [Fact]
    public async Task Tampered_pack_is_rejected()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageAssessmentUpdates));
        var tampered = Pack().Replace("Updated title", "Tampered title");

        var response = await Upload(client, tampered, install: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Packs);
    }

    [Fact]
    public async Task Disable_enable_remove_and_revert_change_pack_state()
    {
        using var client = await SignedIn(App(DashboardPermission.ManageAssessmentUpdates));
        await Upload(client, Pack("p1"), install: true);

        await Post(client, "AssessmentUpdateToggle", new() { ["id"] = "p1", ["enabled"] = "false" });
        Assert.Equal(AssessmentUpdatePackStatus.Disabled, Assert.Single(Packs).Status);

        await Post(client, "AssessmentUpdateToggle", new() { ["id"] = "p1", ["enabled"] = "true" });
        Assert.Equal(AssessmentUpdatePackStatus.Active, Assert.Single(Packs).Status);

        await Post(client, "AssessmentUpdateRevertAll", new());
        Assert.Equal(AssessmentUpdatePackStatus.Disabled, Assert.Single(Packs).Status);

        await Post(client, "AssessmentUpdateRemove", new() { ["id"] = "p1" });
        Assert.Empty(Packs);
    }

    [Fact]
    public async Task Handlers_without_permission_change_nothing()
    {
        var app = App(DashboardPermission.ManageSystemSettings);
        using var client = await SignedIn(app);
        Store.Import(Pack("p1"), "seed", commit: true);

        var upload = await Upload(client, Pack("p2"), install: true);
        var toggle = await Post(client, "AssessmentUpdateToggle", new() { ["id"] = "p1", ["enabled"] = "false" });
        var revert = await Post(client, "AssessmentUpdateRevertAll", new());
        var remove = await Post(client, "AssessmentUpdateRemove", new() { ["id"] = "p1" });

        Assert.All(new[] { upload, toggle, revert, remove }, r => Assert.NotEqual(HttpStatusCode.OK, r.StatusCode));
        var pack = Assert.Single(Packs);
        Assert.Equal("p1", pack.State.Id);
        Assert.Equal(AssessmentUpdatePackStatus.Active, pack.Status);
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
