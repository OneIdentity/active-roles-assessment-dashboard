using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Pure-logic coverage of <see cref="AccessTemplateReadModel"/>, <see cref="ArPermissionModel"/> and
/// <see cref="PermissionScope"/>: class grants, Any/All wildcards, deny precedence, trustee overlap,
/// missing links/templates, licensing visibility and delegation detection.
/// </summary>
[Trait("Stage", "Security")]
public class ArPermissionModelTests
{
    private const string UserSid = "S-1-5-21-1-2-3-1001";
    private const string GroupSid = "S-1-5-21-1-2-3-2001";
    private const string OtherSid = "S-1-5-21-1-2-3-9999";

    private static AccessTemplateReadModel Template(IEnumerable<string>? allow = null, IEnumerable<string>? deny = null)
    {
        var t = new AccessTemplateReadModel { Name = "T" };
        foreach (var a in allow ?? []) t.AllowReadTargets.Add(a);
        foreach (var d in deny ?? []) t.DenyReadTargets.Add(d);
        return t;
    }

    private static AccessTemplateLinkModel Link(string guid, AccessTemplateReadModel? template, params string[] trustees)
    {
        var link = new AccessTemplateLinkModel { LinkGuid = guid, AccessTemplateName = "CN=T", Template = template };
        foreach (var s in trustees) link.TrusteeSids.Add(s);
        return link;
    }

    private static ArPermissionModel Model(LicensingVisibilityProbe? probe = null, params AccessTemplateLinkModel[] links)
        => new()
        {
            LinksByGuid = links.ToDictionary(l => l.LinkGuid, StringComparer.OrdinalIgnoreCase),
            LicensingProbe = probe
        };

    private static UserSidSet User(params string[] sids)
    {
        var u = new UserSidSet { Username = @"TEST\jdoe" };
        foreach (var s in sids) u.Sids.Add(s);
        return u;
    }

    private static JsonElement Item(string objectClass, params string[] linkGuids)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["objectClass"] = new[] { "top", objectClass },
            [SegmentAttributes.EffectiveLinksAttribute] = linkGuids.Select(g => $"CN={g},CN=AT Links,CN=Configuration").ToArray()
        });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed class Scoped : IPermissionScoped
    {
        public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = Array.Empty<string>();
        public string ObjectClass { get; set; } = string.Empty;
    }

    // ------------------------------------------------------------------ AccessTemplateReadModel

    [Theory]
    [InlineData("user", true)]
    [InlineData("USER", true)]
    [InlineData("group", false)]
    [InlineData("", false)]
    public void Template_grants_only_listed_class(string target, bool expected)
        => Assert.Equal(expected, Template(allow: ["user"]).GrantsReadOn(target));

    [Theory]
    [InlineData("Any")]
    [InlineData("All")]
    public void Template_wildcard_grants_every_class(string wildcard)
    {
        var t = Template(allow: [wildcard]);
        Assert.True(t.GrantsReadOn("user"));
        Assert.True(t.GrantsReadOn("computer"));
    }

    [Fact]
    public void Template_deny_overrides_allow_for_same_class()
    {
        var t = Template(allow: ["Any"], deny: ["user"]);
        Assert.True(t.DeniesReadOn("user"));
        Assert.False(t.GrantsReadOn("user"));
        Assert.True(t.GrantsReadOn("group"));
    }

    [Fact]
    public void Template_wildcard_deny_blocks_every_class()
    {
        var t = Template(allow: ["user"], deny: ["Any"]);
        Assert.False(t.GrantsReadOn("user"));
        Assert.True(t.DeniesReadOn("group"));
        Assert.False(t.DeniesReadOn(""));
    }

    // ------------------------------------------------------------------ PermissionScope

    [Fact]
    public void Visible_when_link_trustee_and_class_match()
    {
        var model = Model(null, Link("L1", Template(allow: ["user"]), UserSid));
        Assert.True(PermissionScope.IsVisibleTo(["L1"], "user", User(UserSid), model));
    }

    [Fact]
    public void Visible_via_nested_group_sid()
    {
        var model = Model(null, Link("L1", Template(allow: ["user"]), GroupSid));
        Assert.True(PermissionScope.IsVisibleTo(["L1"], "user", User(UserSid, GroupSid), model));
    }

    [Fact]
    public void Hidden_when_user_is_not_a_trustee()
    {
        var model = Model(null, Link("L1", Template(allow: ["user"]), OtherSid));
        Assert.False(PermissionScope.IsVisibleTo(["L1"], "user", User(UserSid), model));
    }

    [Fact]
    public void Hidden_when_template_does_not_cover_class()
    {
        var model = Model(null, Link("L1", Template(allow: ["group"]), UserSid));
        Assert.False(PermissionScope.IsVisibleTo(["L1"], "user", User(UserSid), model));
    }

    [Fact]
    public void Hidden_when_link_unknown_or_template_missing()
    {
        var model = Model(null, Link("L1", null, UserSid));
        Assert.False(PermissionScope.IsVisibleTo(["L1"], "user", User(UserSid), model));
        Assert.False(PermissionScope.IsVisibleTo(["missing"], "user", User(UserSid), model));
        Assert.False(PermissionScope.IsVisibleTo([], "user", User(UserSid), model));
    }

    [Fact]
    public void Deny_on_any_trusteed_link_wins_over_allow_on_another()
    {
        var model = Model(null,
            Link("A", Template(allow: ["user"]), UserSid),
            Link("D", Template(deny: ["user"]), GroupSid));
        Assert.False(PermissionScope.IsVisibleTo(["A", "D"], "user", User(UserSid, GroupSid), model));
    }

    [Fact]
    public void Deny_on_link_user_is_not_trusteed_on_is_ignored()
    {
        var model = Model(null,
            Link("A", Template(allow: ["user"]), UserSid),
            Link("D", Template(deny: ["user"]), OtherSid));
        Assert.True(PermissionScope.IsVisibleTo(["A", "D"], "user", User(UserSid), model));
    }

    [Fact]
    public void Null_arguments_fail_closed()
    {
        var model = Model(null, Link("L1", Template(allow: ["Any"]), UserSid));
        Assert.False(PermissionScope.IsVisibleTo(["L1"], "user", null!, model));
        Assert.False(PermissionScope.IsVisibleTo(["L1"], "user", User(UserSid), null!));
        Assert.False(PermissionScope.IsVisibleTo((IEnumerable<string>)null!, "user", User(UserSid), model));
        Assert.False(PermissionScope.IsVisibleTo((IPermissionScoped)null!, User(UserSid), model));
    }

    [Fact]
    public void Raw_items_resolve_links_from_dn_and_last_object_class()
    {
        var model = Model(null, Link("L1", Template(allow: ["group"]), UserSid));
        var user = User(UserSid);

        Assert.True(model.IsVisible(Item("group", "L1"), user));
        Assert.False(model.IsVisible(Item("user", "L1"), user));
    }

    [Fact]
    public void VisibleItems_filters_raw_and_typed_sequences()
    {
        var model = Model(null, Link("L1", Template(allow: ["user"]), UserSid));
        var user = User(UserSid);

        var raw = PermissionScope.VisibleItems([Item("user", "L1"), Item("user", "L2"), Item("group", "L1")], user, model);
        Assert.Single(raw);

        var typed = PermissionScope.VisibleItems(
            new[]
            {
                new Scoped { EffectiveLinkGuids = ["L1"], ObjectClass = "user" },
                new Scoped { EffectiveLinkGuids = ["L1"], ObjectClass = "computer" },
            }, user, model);
        Assert.Single(typed);
        Assert.True(model.IsVisible(typed[0], user));

        Assert.Empty(PermissionScope.VisibleItems((IEnumerable<JsonElement>)null!, user, model));
        Assert.Empty(PermissionScope.VisibleItems((IEnumerable<Scoped>)null!, user, model));
    }

    // ------------------------------------------------------------------ ArPermissionModel

    [Fact]
    public void Licensing_visibility_requires_probe_and_grant()
    {
        var link = Link("L1", Template(allow: ["edsmanagedobjectstatisticsdata"]), UserSid);
        var probe = new LicensingVisibilityProbe { EffectiveLinkGuids = ["L1"], ObjectClass = "edsmanagedobjectstatisticsdata" };

        Assert.True(Model(probe, link).GrantsLicensingVisibility(User(UserSid)));
        Assert.False(Model(probe, link).GrantsLicensingVisibility(User(OtherSid)));
        Assert.False(Model(null, link).GrantsLicensingVisibility(User(UserSid)));
    }

    [Fact]
    public void HasAnyDelegation_requires_trusteed_link_with_template()
    {
        Assert.True(Model(null, Link("L1", Template(), GroupSid)).HasAnyDelegation(User(UserSid, GroupSid)));
        Assert.False(Model(null, Link("L1", null, UserSid)).HasAnyDelegation(User(UserSid)));
        Assert.False(Model(null, Link("L1", Template(), OtherSid)).HasAnyDelegation(User(UserSid)));
        Assert.False(Model(null, Link("L1", Template(), UserSid)).HasAnyDelegation(User()));
        Assert.False(Model(null, Link("L1", Template(), UserSid)).HasAnyDelegation(null!));
    }

    [Fact]
    public void Empty_model_grants_nothing()
    {
        var empty = ArPermissionModel.Empty;
        Assert.Empty(empty.LinksByGuid);
        Assert.False(empty.HasAnyDelegation(User(UserSid)));
        Assert.False(empty.GrantsLicensingVisibility(User(UserSid)));
        Assert.False(empty.IsVisible(Item("user", "L1"), User(UserSid)));
    }
}
