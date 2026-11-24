using System.Net;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Signed-in integration coverage for the Assessments and Snapshots Razor Page handlers:
/// run/capture, delete and export, each verified against its role permission.
/// </summary>
[Trait("Stage", "Security")]
public class AssessmentsAndSnapshotsPageTests : IDisposable
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

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, Dictionary<string, string> form) =>
        client.PostAsync(url, new FormUrlEncodedContent(form));

    private static void AssertRedirectedToApp(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/app/", response.Headers.Location!.OriginalString);
    }

    private async Task<AssessmentResult> SeedAssessment(string label = "Seeded")
    {
        var service = _app!.Services.GetRequiredService<AssessmentService>();
        var result = service.Evaluate(new DashboardSummary(), AssessmentType.ActiveDirectory, label, "seed");
        await service.SaveAsync(result);
        return result;
    }

    private async Task<Snapshot> SeedSnapshot(string label = "Seeded")
    {
        var service = _app!.Services.GetRequiredService<SnapshotService>();
        var snapshot = service.Capture(new DashboardSummary(), label, "seed", null);
        await service.SaveAsync(snapshot);
        return snapshot;
    }

    // ---------------------------------------------------------------- Assessments

    [Fact]
    public async Task Assessments_run_saves_labelled_result_and_redirects_to_it()
    {
        var app = App(DashboardPermission.ViewAssessments, DashboardPermission.RunAndSaveAssessments);
        using var client = await SignedIn(app);

        var response = await Post(client, "/Assessments?handler=Run",
            new() { ["Type"] = nameof(AssessmentType.ActiveDirectory), ["label"] = "Quarterly review" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = Assert.Single(await app.Services.GetRequiredService<AssessmentService>().ListAsync(AssessmentType.ActiveDirectory));
        Assert.Equal("Quarterly review", saved.Label);
        Assert.Contains($"Id={saved.Id}", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assessments_run_without_permission_is_rejected_and_saves_nothing()
    {
        var app = App(DashboardPermission.ViewAssessments);
        using var client = await SignedIn(app);

        var response = await Post(client, "/Assessments?handler=Run",
            new() { ["Type"] = nameof(AssessmentType.ActiveDirectory), ["label"] = "Denied" });

        AssertRedirectedToApp(response);
        Assert.Empty(await app.Services.GetRequiredService<AssessmentService>().ListAsync());
    }

    [Fact]
    public async Task Assessments_delete_removes_saved_result()
    {
        var app = App(DashboardPermission.ViewAssessments, DashboardPermission.DeleteAssessments);
        using var client = await SignedIn(app);
        var seeded = await SeedAssessment();

        var response = await Post(client, "/Assessments?handler=Delete", new() { ["id"] = seeded.Id });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await app.Services.GetRequiredService<AssessmentService>().LoadAsync(seeded.Id));
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("..\\..\\appsettings")]
    public async Task Assessments_delete_unknown_or_traversal_id_leaves_data_intact(string id)
    {
        var app = App(DashboardPermission.ViewAssessments, DashboardPermission.DeleteAssessments);
        using var client = await SignedIn(app);
        var seeded = await SeedAssessment();

        var response = await Post(client, "/Assessments?handler=Delete", new() { ["id"] = id });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(await app.Services.GetRequiredService<AssessmentService>().LoadAsync(seeded.Id));
    }

    [Fact]
    public async Task Assessments_delete_without_permission_is_rejected_and_keeps_result()
    {
        var app = App(DashboardPermission.ViewAssessments);
        using var client = await SignedIn(app);
        var seeded = await SeedAssessment();

        var response = await Post(client, "/Assessments?handler=Delete", new() { ["id"] = seeded.Id });

        AssertRedirectedToApp(response);
        Assert.NotNull(await app.Services.GetRequiredService<AssessmentService>().LoadAsync(seeded.Id));
    }

    [Theory]
    [InlineData("Pdf", "application/pdf", ".pdf")]
    [InlineData("Word", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx")]
    public async Task Assessments_export_returns_document_in_requested_format(string format, string contentType, string extension)
    {
        var app = App(DashboardPermission.ViewAssessments, DashboardPermission.ExportAssessments);
        using var client = await SignedIn(app);
        var seeded = await SeedAssessment();

        var response = await Post(client, "/Assessments?handler=Export", new() { ["id"] = seeded.Id, ["format"] = format });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(extension, response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Assessments_export_unknown_id_redirects_back_without_file()
    {
        var app = App(DashboardPermission.ViewAssessments, DashboardPermission.ExportAssessments);
        using var client = await SignedIn(app);

        var response = await Post(client, "/Assessments?handler=Export", new() { ["id"] = "does-not-exist", ["format"] = "Pdf" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Assessments", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Assessments_export_without_permission_is_rejected()
    {
        var app = App(DashboardPermission.ViewAssessments);
        using var client = await SignedIn(app);
        var seeded = await SeedAssessment();

        var response = await Post(client, "/Assessments?handler=Export", new() { ["id"] = seeded.Id, ["format"] = "Pdf" });

        AssertRedirectedToApp(response);
    }

    [Fact]
    public async Task Assessments_page_without_view_permission_redirects_to_app()
    {
        using var client = await SignedIn(App());

        AssertRedirectedToApp(await client.GetAsync("/Assessments"));
    }

    // ---------------------------------------------------------------- Snapshots

    [Fact]
    public async Task Snapshots_capture_saves_labelled_snapshot()
    {
        var app = App(DashboardPermission.ViewSnapshots, DashboardPermission.RunAndSaveSnapshots);
        using var client = await SignedIn(app);

        var response = await Post(client, "/Snapshots?handler=Capture", new() { ["label"] = "Before cleanup" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = Assert.Single(await app.Services.GetRequiredService<SnapshotService>().ListAsync());
        Assert.Equal("Before cleanup", saved.Label);
        Assert.Equal(FakeActiveRolesServer.ValidUser, saved.CreatedBy);
    }

    [Fact]
    public async Task Snapshots_capture_without_permission_is_rejected_and_saves_nothing()
    {
        var app = App(DashboardPermission.ViewSnapshots);
        using var client = await SignedIn(app);

        var response = await Post(client, "/Snapshots?handler=Capture", new() { ["label"] = "Denied" });

        AssertRedirectedToApp(response);
        Assert.Empty(await app.Services.GetRequiredService<SnapshotService>().ListAsync());
    }

    [Fact]
    public async Task Snapshots_delete_removes_saved_snapshot()
    {
        var app = App(DashboardPermission.ViewSnapshots, DashboardPermission.DeleteSnapshots);
        using var client = await SignedIn(app);
        var seeded = await SeedSnapshot();

        var response = await Post(client, "/Snapshots?handler=Delete", new() { ["id"] = seeded.Header.Id });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await app.Services.GetRequiredService<SnapshotService>().LoadAsync(seeded.Header.Id));
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("..\\..\\appsettings")]
    public async Task Snapshots_delete_unknown_or_traversal_id_leaves_data_intact(string id)
    {
        var app = App(DashboardPermission.ViewSnapshots, DashboardPermission.DeleteSnapshots);
        using var client = await SignedIn(app);
        var seeded = await SeedSnapshot();

        var response = await Post(client, "/Snapshots?handler=Delete", new() { ["id"] = id });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(await app.Services.GetRequiredService<SnapshotService>().LoadAsync(seeded.Header.Id));
    }

    [Fact]
    public async Task Snapshots_delete_without_permission_is_rejected_and_keeps_snapshot()
    {
        var app = App(DashboardPermission.ViewSnapshots);
        using var client = await SignedIn(app);
        var seeded = await SeedSnapshot();

        var response = await Post(client, "/Snapshots?handler=Delete", new() { ["id"] = seeded.Header.Id });

        AssertRedirectedToApp(response);
        Assert.NotNull(await app.Services.GetRequiredService<SnapshotService>().LoadAsync(seeded.Header.Id));
    }

    [Fact]
    public async Task Snapshots_page_without_view_permission_redirects_to_app()
    {
        using var client = await SignedIn(App());

        AssertRedirectedToApp(await client.GetAsync("/Snapshots"));
    }

    [Fact]
    public async Task Tools_page_without_tool_permission_redirects_to_app()
    {
        using var client = await SignedIn(App());

        AssertRedirectedToApp(await client.GetAsync("/Tools"));
    }

    [Fact]
    public async Task Tools_page_with_group_analyzer_permission_lands_on_first_tool()
    {
        using var client = await SignedIn(App(DashboardPermission.UseGroupAnalyzer));

        var response = await client.GetAsync("/Tools");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Tools/GroupAnalyzer", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Group_analyzer_without_permission_redirects_to_app()
    {
        using var client = await SignedIn(App());

        AssertRedirectedToApp(await client.GetAsync("/Tools/GroupAnalyzer"));
    }

    [Fact]
    public async Task Group_analyzer_with_permission_renders()
    {
        using var client = await SignedIn(App(DashboardPermission.UseGroupAnalyzer));

        var response = await client.GetAsync("/Tools/GroupAnalyzer");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("class=\"tools-tabs\"", html);
        Assert.Contains("aria-current=\"page\"", html);
    }

    [Fact]
    public async Task Anonymous_posts_are_not_processed()
    {
        var app = App(DashboardPermission.ViewSnapshots, DashboardPermission.RunAndSaveSnapshots,
            DashboardPermission.ViewAssessments, DashboardPermission.RunAndSaveAssessments);
        using var client = app.CreateNonRedirectingClient();

        var snap = await Post(client, "/Snapshots?handler=Capture", new() { ["label"] = "anon" });
        var asmt = await Post(client, "/Assessments?handler=Run", new() { ["Type"] = nameof(AssessmentType.ActiveDirectory) });

        Assert.NotEqual(HttpStatusCode.OK, snap.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, asmt.StatusCode);
        Assert.Empty(await app.Services.GetRequiredService<SnapshotService>().ListAsync());
        Assert.Empty(await app.Services.GetRequiredService<AssessmentService>().ListAsync());
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
