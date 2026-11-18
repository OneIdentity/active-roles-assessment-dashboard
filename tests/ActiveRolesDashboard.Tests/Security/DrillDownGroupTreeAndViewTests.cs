using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Signed-in integration coverage for the DrillDown API, the GroupTree Razor Page and the
/// dashboard view endpoint, driven through the real /Login flow against the fake Active Roles API.
/// </summary>
[Trait("Stage", "Security")]
public class DrillDownGroupTreeAndViewTests : IDisposable
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

    private static async Task<JsonElement> GetJson(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static JsonObject Obj(params (string Key, JsonNode? Value)[] props)
    {
        var o = new JsonObject();
        foreach (var (k, v) in props) o[k] = v;
        return o;
    }

    // ---------------------------------------------------------------- DrillDown

    [Fact]
    public async Task DrillDown_access_templates_maps_backend_items()
    {
        _server.WithSearch("objectClass=edsAccessTemplate", [
            Obj(("name", "AT One"), ("distinguishedName", "CN=AT One,CN=Access Templates,CN=Configuration"), ("objectGuid", "11111111-0000-0000-0000-000000000001")),
            Obj(("name", "AT Two"), ("distinguishedName", "CN=AT Two,CN=Access Templates,CN=Configuration"), ("objectGuid", "11111111-0000-0000-0000-000000000002"))
        ]);
        using var client = await SignedIn(App());

        var json = await GetJson(client, "/api/DrillDown/accesstemplates");

        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        var names = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Equal(["AT One", "AT Two"], names);
    }

    [Fact]
    public async Task DrillDown_empty_access_templates_returns_only_templates_without_aces()
    {
        _server.WithSearch("objectClass=edsAccessTemplate", [
            Obj(("name", "Empty AT"), ("distinguishedName", "CN=Empty AT,CN=Access Templates,CN=Configuration")),
            Obj(("name", "Full AT"), ("distinguishedName", "CN=Full AT,CN=Access Templates,CN=Configuration"), ("edsaATEList", new JsonArray("A;RP;;")))
        ]);
        using var client = await SignedIn(App());

        var json = await GetJson(client, "/api/DrillDown/emptyaccesstemplates");

        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Equal("Empty AT", json.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task DrillDown_backend_failure_is_reported_as_error_not_exception()
    {
        _server.WithSearchError("objectClass=edsAccessTemplate", 500);
        using var client = await SignedIn(App());

        var json = await GetJson(client, "/api/DrillDown/accesstemplates");

        Assert.StartsWith("No data", json.GetProperty("error").GetString());
        Assert.Equal(0, json.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task DrillDown_details_passes_through_object_and_reports_missing_object()
    {
        const string guid = "22222222-0000-0000-0000-000000000001";
        _server.WithObject(guid, Obj(("name", "Detail Object"), ("objectGUID", guid)));
        using var client = await SignedIn(App());

        var found = await GetJson(client, $"/api/DrillDown/details/{guid}");
        Assert.Equal("Detail Object", found.GetProperty("name").GetString());

        var missing = await GetJson(client, "/api/DrillDown/details/22222222-0000-0000-0000-000000000099");
        Assert.Equal("No data", missing.GetProperty("error").GetString());
    }

    // ---------------------------------------------------------------- GroupTree

    [Fact]
    public async Task GroupTree_anonymous_is_redirected_to_login()
    {
        using var client = App().CreateNonRedirectingClient();

        var response = await client.GetAsync("/GroupTree?group=Admins");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GroupTree_resolves_group_and_renders_root()
    {
        _server.WithSearch("objectClass=group)(|(name=", [
            Obj(("name", "Admins"), ("distinguishedName", "CN=Admins,OU=Groups,DC=test,DC=local"))
        ]);
        using var client = await SignedIn(App());

        var response = await client.GetAsync("/GroupTree?group=Admins");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"tree-root\"", html);
        Assert.Contains("data-dn=\"CN=Admins,OU=Groups,DC=test,DC=local\"", html);
        Assert.Contains(_server.Requests, r => r.RequestMessage.Query?.TryGetValue("filter", out var f) == true
            && f.Any(v => v.Contains("name=Admins")));
    }

    [Fact]
    public async Task GroupTree_unknown_group_renders_without_tree()
    {
        using var client = await SignedIn(App());

        var response = await client.GetAsync("/GroupTree?group=DoesNotExist");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("id=\"tree-root\"", html);
    }

    private const string GroupA = "CN=GroupA,OU=Groups,DC=test,DC=local";
    private const string GroupB = "CN=GroupB,OU=Groups,DC=test,DC=local";
    private const string UserU = "CN=UserU,OU=Users,DC=test,DC=local";

    private void WithGroupAMembers()
    {
        // Base-scope read of GroupA's members; GroupA contains itself to form a cycle.
        _server.WithSearch("(objectClass=*)", [Obj(("member", new JsonArray(UserU, GroupB, GroupA)))], priority: 60);
        _server.WithSearch("(|(distinguishedName=", [
            Obj(("name", "UserU"), ("distinguishedName", UserU), ("objectClass", new JsonArray("top", "person", "user"))),
            Obj(("name", "GroupB"), ("distinguishedName", GroupB), ("objectClass", new JsonArray("top", "group"))),
            Obj(("name", "GroupA"), ("distinguishedName", GroupA), ("objectClass", new JsonArray("top", "group")))
        ], priority: 30);
    }

    [Fact]
    public async Task GroupTree_expand_renders_members_and_marks_cycle()
    {
        WithGroupAMembers();
        using var client = await SignedIn(App());

        var response = await client.GetAsync($"/GroupTree?handler=Expand&dn={Uri.EscapeDataString(GroupA)}&depth=0");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("UserU", html);
        Assert.Contains($"data-dn=\"{GroupB}\"", html);
        Assert.DoesNotContain($"data-dn=\"{GroupA}\"", html);
        Assert.Contains("tree-note", html);
        Assert.True(html.IndexOf("GroupB", StringComparison.Ordinal) < html.IndexOf("UserU", StringComparison.Ordinal), "groups are listed first");
    }

    [Fact]
    public async Task GroupTree_expand_at_depth_cap_does_not_offer_further_expansion()
    {
        WithGroupAMembers();
        using var client = await SignedIn(App());

        var response = await client.GetAsync($"/GroupTree?handler=Expand&dn={Uri.EscapeDataString(GroupA)}&depth=1000");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GroupB", html);
        Assert.DoesNotContain("class=\"tree-toggle\"", html);
    }

    [Fact]
    public async Task GroupTree_expand_group_without_members_renders_empty_message()
    {
        using var client = await SignedIn(App());

        var response = await client.GetAsync($"/GroupTree?handler=Expand&dn={Uri.EscapeDataString(GroupA)}&depth=0");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gt-message", html);
        Assert.DoesNotContain("group-tree-item", html);
    }

    // ---------------------------------------------------------------- dashboard view

    [Fact]
    public async Task Dashboard_view_overview_is_returned()
    {
        using var client = await SignedIn(App());

        var json = await GetJson(client, "/api/dashboard/view/Overview");

        Assert.Equal("Overview", json.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("categories").ValueKind);
    }

    [Fact]
    public async Task Dashboard_view_unknown_key_is_not_found()
    {
        using var client = await SignedIn(App());

        var response = await client.GetAsync("/api/dashboard/view/NoSuchDashboard");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_view_permitted_dashboard_returns_its_categories()
    {
        using var client = await SignedIn(App(DashboardPermission.ViewActiveDirectoryDashboard));

        var json = await GetJson(client, "/api/dashboard/view/ActiveDirectory");

        Assert.Equal("ActiveDirectory", json.GetProperty("key").GetString());
        Assert.Equal(DashboardInfo.All.Single(d => d.Key == "ActiveDirectory").Title, json.GetProperty("title").GetString());
        Assert.NotEqual(0, json.GetProperty("categories").GetArrayLength());
    }

    [Theory]
    [InlineData("ActiveRoles", DashboardPermission.ViewActiveDirectoryDashboard)]
    [InlineData("ActiveDirectory", DashboardPermission.ViewActiveRolesDashboard)]
    public async Task Dashboard_view_without_permission_is_not_found(string key, DashboardPermission otherPermission)
    {
        using var client = await SignedIn(App(otherPermission));

        var response = await client.GetAsync($"/api/dashboard/view/{key}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
