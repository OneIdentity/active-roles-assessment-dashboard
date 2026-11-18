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
/// Per-user data filtering through the real sign-in flow: a user signs in at <c>/Login</c>, the app
/// resolves their SID set through the service account (search + tokenGroups GET), projects the
/// shared superset with <see cref="PerUserDashboardFilter"/>, and serves the result from
/// <c>/api/dashboard</c>. Filter rule semantics are covered by <see cref="PermissionFilteringTests"/>.
/// </summary>
[Trait("Stage", "Security")]
public class DashboardDataFilteringTests : IDisposable
{
    private const string UserGuid = "0a1b2c3d-0000-0000-0000-000000000001";
    private const string OwnSid = "S-1-5-21-7-1001";
    private const string HelpDeskSid = "S-1-5-21-7-2001";
    private const string OtherSid = "S-1-5-21-7-9999";

    private const string OwnLink = "11111111-1111-1111-1111-111111111111";
    private const string GroupLink = "22222222-2222-2222-2222-222222222222";
    private const string OtherLink = "33333333-3333-3333-3333-333333333333";

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private DashboardAppFactory? _app;

    /// <summary>
    /// App whose User role holds <paramref name="userPermissions"/> and whose service account can
    /// authenticate against the fake server, with the superset and permission model published.
    /// </summary>
    private DashboardAppFactory App(params DashboardPermission[] userPermissions)
    {
        var matrix = JsonSerializer.Serialize(new Dictionary<string, List<string>>
        {
            [nameof(DashboardRole.User)] = userPermissions.Select(p => p.ToString()).ToList()
        });

        _app = new DashboardAppFactory(activeRoles: _server)
        {
            ConfigureServices = services => services.AddSingleton<IPostConfigureOptions<ActiveRolesConfig>>(sp =>
                new PostConfigureOptions<ActiveRolesConfig>(Options.DefaultName, c =>
                {
                    c.Roles.ProtectedMatrix = sp.GetRequiredService<RolePermissionProtector>().Protect(matrix);
                    c.ServiceAccount.Username = FakeActiveRolesServer.ValidUser;
                    c.ServiceAccount.ProtectedPassword = sp.GetRequiredService<ServiceAccountSecretProtector>().Protect(FakeActiveRolesServer.ValidPassword);
                }))
        };

        _app.Services.GetRequiredService<DashboardCacheHolder>()
            .Publish(new DashboardSupersetSnapshot(Superset(), DateTimeOffset.UtcNow), Model());
        return _app;
    }

    private DashboardAppFactory DelegatedApp() => App(
        DashboardPermission.UseDelegatedPermissionsForVisibility,
        DashboardPermission.ViewActiveDirectoryDashboard);

    /// <summary>The directory resolves the signed-in user to <paramref name="ownSid"/> plus <paramref name="groupSids"/>.</summary>
    private void Directory(string ownSid, params string[] groupSids)
    {
        _server.WithSearch("samAccountName=admin",
            [new JsonObject
            {
                ["objectGUID"] = UserGuid,
                ["objectSid"] = ownSid,
                ["objectClass"] = new JsonArray("top", "user"),
                ["sAMAccountName"] = "admin"
            }], priority: 20);
        _server.WithObject(UserGuid, new JsonObject
        {
            ["objectSid"] = ownSid,
            ["tokenGroups"] = new JsonArray(groupSids.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray())
        });
    }

    private static ArPermissionModel Model()
    {
        static AccessTemplateLinkModel Link(string guid, string trusteeSid)
        {
            var template = new AccessTemplateReadModel { Name = $"AT-{guid[..4]}" };
            template.AllowReadTargets.Add("user");
            var link = new AccessTemplateLinkModel { LinkGuid = guid, AccessTemplateName = template.Name, Template = template };
            link.TrusteeSids.Add(trusteeSid);
            return link;
        }

        return new ArPermissionModel
        {
            LinksByGuid = new Dictionary<string, AccessTemplateLinkModel>(StringComparer.OrdinalIgnoreCase)
            {
                [OwnLink] = Link(OwnLink, OwnSid),
                [GroupLink] = Link(GroupLink, HelpDeskSid),
                [OtherLink] = Link(OtherLink, OtherSid)
            }
        };
    }

    private static JsonElement RawUser(string name, string link) => JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["name"] = name,
        ["distinguishedName"] = $"CN={name},OU=Users,DC=test,DC=local",
        ["objectClass"] = new[] { "top", "user" },
        ["edsvaATLinksEffective"] = new[] { $"CN={{{link}}},CN=AT Links,CN=Configuration" },
        [SegmentAttributes.DomainAttribute] = "TEST"
    });

    private static ADUserAccountDetailInfo DetailUser(string name, string link) => new()
    {
        Name = name,
        Dn = $"CN={name},OU=Users,DC=test,DC=local",
        ObjectClass = "user",
        EffectiveLinkGuids = [link]
    };

    private static DashboardSummary Superset()
    {
        List<JsonElement> users = [RawUser("own-user", OwnLink), RawUser("group-user", GroupLink), RawUser("other-user", OtherLink)];
        return new DashboardSummary
        {
            ADUserAccounts = new ADUserAccountsSummary { Items = users, TotalCount = users.Count },
            EnabledUsers = new ADUserAccountDetailSummary
            {
                Items = [DetailUser("own-enabled", OwnLink), DetailUser("group-enabled", GroupLink), DetailUser("other-enabled", OtherLink)],
                TotalCount = 3
            }
        };
    }

    private async Task<HttpClient> SignedIn()
    {
        var client = _app!.CreateNonRedirectingClient();
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

    private static async Task<JsonNode> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }

    private static async Task<int> TotalUsers(HttpClient client)
    {
        var payload = await Json(await client.GetAsync("/api/dashboard"));
        var tile = payload["overview"]!["kpis"]!.AsArray().Single(k => (string?)k!["key"] == "ADUserAccounts")!;
        return (int)tile["count"]!;
    }

    private int SidLookups() =>
        _server.Requests.Count(r => r.RequestMessage.Path == $"/api/objects/{UserGuid}");

    // ---------------------------------------------------------------- overview totals

    [Fact]
    public async Task Delegated_viewer_sees_only_objects_linked_to_their_own_sid()
    {
        DelegatedApp();
        Directory(OwnSid);
        using var client = await SignedIn();

        Assert.Equal(1, await TotalUsers(client));
    }

    [Fact]
    public async Task Delegated_viewer_gains_visibility_through_nested_group_sids()
    {
        DelegatedApp();
        Directory(OwnSid, HelpDeskSid);
        using var client = await SignedIn();

        Assert.Equal(2, await TotalUsers(client));
    }

    [Fact]
    public async Task Delegated_viewer_without_any_delegation_sees_zero()
    {
        DelegatedApp();
        Directory("S-1-5-21-7-5555");
        using var client = await SignedIn();

        Assert.Equal(0, await TotalUsers(client));
    }

    [Fact]
    public async Task Role_without_delegated_visibility_sees_the_full_superset()
    {
        App(DashboardPermission.ViewActiveDirectoryDashboard);
        Directory(OwnSid);
        using var client = await SignedIn();

        Assert.Equal(3, await TotalUsers(client));
        Assert.Equal(0, SidLookups());
    }

    // ---------------------------------------------------------------- drilldowns

    [Fact]
    public async Task Kpi_detail_rows_contain_only_visible_objects()
    {
        DelegatedApp();
        Directory(OwnSid, HelpDeskSid);
        using var client = await SignedIn();

        var detail = await Json(await client.GetAsync("/api/dashboard/kpi/EnabledUsers"));
        var body = detail.ToJsonString();

        Assert.Equal(2, (int)detail["count"]!);
        Assert.Equal(2, detail["rows"]!.AsArray().Count);
        Assert.Contains("own-enabled", body);
        Assert.Contains("group-enabled", body);
        Assert.DoesNotContain("other-enabled", body);
    }

    [Fact]
    public async Task Kpi_detail_is_empty_for_a_viewer_without_delegation()
    {
        DelegatedApp();
        Directory("S-1-5-21-7-5555");
        using var client = await SignedIn();

        var detail = await Json(await client.GetAsync("/api/dashboard/kpi/EnabledUsers"));

        Assert.Equal(0, (int)detail["count"]!);
        Assert.Empty(detail["rows"]!.AsArray());
        Assert.DoesNotContain("-enabled", detail.ToJsonString());
    }

    // ---------------------------------------------------------------- caching and isolation

    [Fact]
    public async Task Viewer_sids_are_resolved_once_per_session()
    {
        DelegatedApp();
        Directory(OwnSid);
        using var client = await SignedIn();

        await TotalUsers(client);
        _app!.Services.GetRequiredService<PerUserSummaryCache>().Clear(FakeActiveRolesServer.ValidUser);
        await TotalUsers(client);

        Assert.Equal(1, SidLookups());
    }

    [Fact]
    public async Task Per_user_cache_holds_the_filtered_projection_and_superset_is_untouched()
    {
        var app = DelegatedApp();
        Directory(OwnSid);
        using var client = await SignedIn();

        await TotalUsers(client);

        var cached = app.Services.GetRequiredService<PerUserSummaryCache>().GetSummaryModel(FakeActiveRolesServer.ValidUser);
        Assert.NotNull(cached);
        Assert.Equal(1, cached!.ADUserAccounts.TotalCount);
        Assert.Equal(1, cached.EnabledUsers.TotalCount);

        var superset = app.Services.GetRequiredService<DashboardCacheHolder>().Current!.Summary;
        Assert.Equal(3, superset.ADUserAccounts.Items.Count);
        Assert.Equal(3, superset.EnabledUsers.Items.Count);
    }

    [Fact]
    public async Task Unresolvable_viewer_is_not_shown_the_superset()
    {
        DelegatedApp();
        // No SID search stubbed: the account cannot be resolved, so the SID set is empty.
        using var client = await SignedIn();

        Assert.Equal(0, await TotalUsers(client));
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
