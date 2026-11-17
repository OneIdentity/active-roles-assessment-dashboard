using System.Security.Principal;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Drives <see cref="ArPermissionModelService"/> against the fake Active Roles REST API: AT Link
/// enumeration, trustee SID decoding, edsaATEList parsing, template caching, the licensing probe and
/// per-user SID resolution (tokenGroups and memberOf fallback).
/// </summary>
[Trait("Stage", "Security")]
public class ArPermissionModelServiceTests : IDisposable
{
    private const string LinkFilter = "edsaTrusteeSID=*";
    private const string LicensingFilter = "edsManagedObjectStatisticsData";
    private const string TemplateFilter = "objectClass=*";
    private const string TemplateDn = "CN=Help Desk,CN=Access Templates,CN=Configuration";
    private const string UserClassGuid = "bf967aba-0de6-11d0-a285-00aa003049e2";
    private const string GroupClassGuid = "bf967a9c-0de6-11d0-a285-00aa003049e2";
    private const string ObjectClassAttrGuid = "bf967a91-0de6-11d0-a285-00aa003049e2";

    private const string TrusteeSid = "S-1-5-21-100-200-300-1105";
    private const string OwnSid = "S-1-5-21-100-200-300-1001";
    private const string GroupSid = "S-1-5-21-100-200-300-2001";
    private const string UserGuid = "11111111-2222-3333-4444-555555555555";

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();

    private ArPermissionModelService Create() => new(
        new TestHttpClientFactory(TimeSpan.FromSeconds(5)),
        new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig { ApiBaseUrl = _server.ApiBaseUrl }),
        NullLogger<ArPermissionModelService>.Instance);

    private static string Base64Sid(string sid)
    {
        var s = new SecurityIdentifier(sid);
        var bytes = new byte[s.BinaryLength];
        s.GetBinaryForm(bytes, 0);
        return Convert.ToBase64String(bytes);
    }

    private static JsonObject LinkItem(string name, string objectGuid, string trusteeSid, string templateDn = TemplateDn) => new()
    {
        ["name"] = name,
        ["objectGUID"] = objectGuid,
        ["edsaTrusteeSID"] = trusteeSid,
        ["edsvaAccessTemplateDN"] = templateDn,
    };

    private void WithTemplate(params string[] aces) =>
        _server.WithSearch(TemplateFilter,
            [new JsonObject { ["name"] = "Help Desk", ["edsaATEList"] = new JsonArray(aces.Select(a => (JsonNode)a!).ToArray()) }],
            priority: 60);

    private int RequestsWithParam(string name, string contains) => _server.Requests.Count(r =>
        r.RequestMessage.Query != null
        && r.RequestMessage.Query.TryGetValue(name, out var v)
        && v.Any(x => x.Contains(contains, StringComparison.OrdinalIgnoreCase)));

    // ------------------------------------------------------------------ BuildAsync

    [Fact]
    public async Task Build_indexes_links_by_name_and_object_guid_with_decoded_sid()
    {
        _server.WithSearch(LinkFilter, [LinkItem("link-cn", "{AAAAAAAA-0000-0000-0000-000000000001}", Base64Sid(TrusteeSid))]);
        WithTemplate($"[A;;LO;;{UserClassGuid}]");

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        var byName = model.LinksByGuid["link-cn"];
        Assert.Same(byName, model.LinksByGuid["aaaaaaaa-0000-0000-0000-000000000001"]);
        Assert.Contains(TrusteeSid, byName.TrusteeSids);
        Assert.Equal(TemplateDn, byName.AccessTemplateName);
        Assert.NotNull(byName.Template);
        Assert.True(byName.Template!.GrantsReadOn("user"));
        Assert.False(byName.Template.GrantsReadOn("group"));
    }

    [Fact]
    public async Task Build_accepts_string_sids_and_skips_links_without_identity()
    {
        _server.WithSearch(LinkFilter,
        [
            LinkItem("L1", "", TrusteeSid),
            new JsonObject { ["edsaTrusteeSID"] = TrusteeSid },
            LinkItem("L2", "", "not-a-sid!"),
        ]);
        WithTemplate("[A;;LO;;]");

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.Equal(2, model.LinksByGuid.Count);
        Assert.Contains(TrusteeSid, model.LinksByGuid["L1"].TrusteeSids);
        Assert.Empty(model.LinksByGuid["L2"].TrusteeSids);
    }

    [Fact]
    public async Task Build_loads_each_template_once_when_shared_by_links()
    {
        _server.WithSearch(LinkFilter,
        [
            LinkItem("L1", "", TrusteeSid),
            LinkItem("L2", "", TrusteeSid),
            LinkItem("L3", "", TrusteeSid),
        ]);
        WithTemplate("[A;;LO;;]");

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.Same(model.LinksByGuid["L1"].Template, model.LinksByGuid["L3"].Template);
        Assert.Equal(1, RequestsWithParam("base", "CN=Help Desk"));
    }

    [Theory]
    [InlineData("[A;;CCDCLCSWRPWPDTLOCRCOSDRCWDWO;;]", "computer", true)]   // Full Control, all classes
    [InlineData("[A;;LO;;" + GroupClassGuid + "]", "group", true)]          // List Object on group
    [InlineData("[A;;RP;;" + UserClassGuid + "]", "user", true)]            // Read all properties on user
    [InlineData("[A;;RP;" + ObjectClassAttrGuid + ";" + UserClassGuid + "]", "user", true)] // Read objectClass
    [InlineData("[A;;RP;bf967953-0de6-11d0-a285-00aa003049e2;" + UserClassGuid + "]", "user", false)] // Read other attribute
    [InlineData("[A;;WP;;" + UserClassGuid + "]", "user", false)]           // Write only
    [InlineData("[A;;CCDC;;]", "user", false)]                              // Create/delete children
    [InlineData("[X;;LO;;]", "user", false)]                                // Unknown ACE type
    public async Task Build_parses_ate_list_visibility_grants(string ace, string objectClass, bool expected)
    {
        _server.WithSearch(LinkFilter, [LinkItem("L1", "", TrusteeSid)]);
        WithTemplate(ace);

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.Equal(expected, model.LinksByGuid["L1"].Template!.GrantsReadOn(objectClass));
    }

    [Fact]
    public async Task Build_records_deny_aces_and_multiple_tokens_per_value()
    {
        _server.WithSearch(LinkFilter, [LinkItem("L1", "", TrusteeSid)]);
        WithTemplate($"[A;;LO;;][D;;LO;;{UserClassGuid}]");

        var template = (await Create().BuildAsync(FakeActiveRolesServer.ValidToken)).LinksByGuid["L1"].Template!;

        Assert.True(template.DeniesReadOn("user"));
        Assert.False(template.GrantsReadOn("user"));
        Assert.True(template.GrantsReadOn("group"));
    }

    [Fact]
    public async Task Build_leaves_template_null_when_template_object_is_missing()
    {
        _server.WithSearch(LinkFilter, [LinkItem("L1", "", TrusteeSid)]);

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(model.LinksByGuid["L1"].Template);
    }

    [Fact]
    public async Task Build_returns_empty_model_when_link_search_fails()
    {
        _server.WithSearchError(LinkFilter, 500);

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.Empty(model.LinksByGuid);
    }

    [Fact]
    public async Task Build_captures_licensing_probe()
    {
        _server.WithSearch(LicensingFilter,
        [
            new JsonObject
            {
                ["name"] = "Stats",
                ["objectClass"] = new JsonArray("top", "edsManagedObjectStatisticsData"),
                [SegmentAttributes.EffectiveLinksAttribute] = new JsonArray("CN=L9,CN=AT Links,CN=Configuration"),
            }
        ], priority: 10);

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.NotNull(model.LicensingProbe);
        Assert.Equal(["L9"], model.LicensingProbe!.EffectiveLinkGuids);
        Assert.Equal("edsmanagedobjectstatisticsdata", model.LicensingProbe.ObjectClass);
    }

    [Fact]
    public async Task Build_has_no_licensing_probe_when_statistics_object_absent()
    {
        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(model.LicensingProbe);
    }

    [Fact]
    public async Task Build_fails_closed_on_malformed_licensing_response()
    {
        _server.WithSearchRawBody(LicensingFilter, "{not json", priority: 10);

        var model = await Create().BuildAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(model.LicensingProbe);
    }

    // ------------------------------------------------------------------ ResolveUserSidSetAsync

    private void WithUserSearch(string filterContains = "samAccountName=jdoe", string objectSid = OwnSid) =>
        _server.WithSearch(filterContains,
        [
            new JsonObject
            {
                ["objectGUID"] = UserGuid,
                ["objectSid"] = Base64Sid(objectSid),
                ["objectClass"] = new JsonArray("top", "person", "organizationalPerson", "user"),
                ["sAMAccountName"] = "jdoe",
            }
        ]);

    [Theory]
    [InlineData(@"TEST\jdoe")]
    [InlineData("jdoe@test.local")]
    [InlineData("jdoe")]
    public async Task Resolve_returns_own_sid_and_token_groups(string username)
    {
        WithUserSearch();
        _server.WithObject(UserGuid, new JsonObject
        {
            ["objectSid"] = Base64Sid(OwnSid),
            ["tokenGroups"] = new JsonArray(Base64Sid(GroupSid), "S-1-5-32-545"),
        });

        var set = await Create().ResolveUserSidSetAsync(FakeActiveRolesServer.ValidToken, username);

        Assert.Equal(username, set.Username);
        Assert.Equal(new[] { OwnSid, GroupSid, "S-1-5-32-545" }.Order(), set.Sids.Order());
        Assert.Contains(_server.Requests, r => r.RequestMessage.Path == $"/api/objects/{UserGuid}"
            && r.RequestMessage.Query!["includeAttributes"].Contains("all"));
        Assert.DoesNotContain(_server.Requests, r => r.RequestMessage.Query != null
            && r.RequestMessage.Query.TryGetValue("attributes", out var a) && a.Contains("tokenGroups"));
    }

    [Fact]
    public async Task Resolve_escapes_ldap_special_characters()
    {
        await Create().ResolveUserSidSetAsync(FakeActiveRolesServer.ValidToken, @"TEST\j*(doe)");

        Assert.True(RequestsWithParam("filter", @"samAccountName=j\2a\28doe\29") > 0);
    }

    [Fact]
    public async Task Resolve_falls_back_to_member_of_when_token_groups_unavailable()
    {
        const string groupDn = "CN=Helpdesk,OU=Groups,DC=test,DC=local";
        WithUserSearch();
        _server.WithObject(UserGuid, new JsonObject { ["memberOf"] = new JsonArray(groupDn) });
        _server.WithSearch("objectClass=group", [new JsonObject { ["objectSid"] = Base64Sid(GroupSid) }], priority: 45);

        var set = await Create().ResolveUserSidSetAsync(FakeActiveRolesServer.ValidToken, @"TEST\jdoe");

        Assert.Contains(OwnSid, set.Sids);
        Assert.Contains(GroupSid, set.Sids);
        Assert.Equal(1, RequestsWithParam("filter", "(objectClass=group)"));
    }

    [Fact]
    public async Task Resolve_ignores_non_user_matches()
    {
        _server.WithSearch("samAccountName=jdoe",
        [
            new JsonObject { ["objectGUID"] = UserGuid, ["objectSid"] = OwnSid, ["objectClass"] = new JsonArray("top", "group") }
        ]);

        var set = await Create().ResolveUserSidSetAsync(FakeActiveRolesServer.ValidToken, "jdoe");

        Assert.Empty(set.Sids);
    }

    [Fact]
    public async Task Resolve_returns_empty_set_for_unknown_user()
    {
        var set = await Create().ResolveUserSidSetAsync(FakeActiveRolesServer.ValidToken, @"TEST\ghost");

        Assert.Empty(set.Sids);
        // Tries both attribute casings and the class-agnostic fallback before giving up.
        Assert.Equal(3, _server.Requests.Count(r => r.RequestMessage.Path == "/api/objects"));
    }

    [Fact]
    public async Task Resolve_keeps_own_sid_when_object_get_fails()
    {
        WithUserSearch();
        _server.Server
            .Given(WireMock.RequestBuilders.Request.Create().WithPath($"/api/objects/{UserGuid}").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(500));

        var set = await Create().ResolveUserSidSetAsync(FakeActiveRolesServer.ValidToken, "jdoe");

        Assert.Equal([OwnSid], set.Sids);
    }

    public void Dispose() => _server.Dispose();
}
