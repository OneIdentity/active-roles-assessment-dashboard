using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Delegated-visibility projection of the shared superset for non-admin viewers: Access Template
/// Link trustee matching (direct and via group SIDs), Allow/Deny semantics, count recomputation,
/// scoping of domain-level signals, and Entra/Licensing exposure.
/// </summary>
[Trait("Stage", "Security")]
public class PermissionFilteringTests
{
    private const string ViewerSid = "S-1-5-21-1-1001";
    private const string HelpDeskSid = "S-1-5-21-1-2001";
    private const string OtherSid = "S-1-5-21-1-9999";

    private const string UsersLink = "11111111-1111-1111-1111-111111111111";
    private const string GroupsLink = "22222222-2222-2222-2222-222222222222";
    private const string DenyUsersLink = "33333333-3333-3333-3333-333333333333";
    private const string OtherTrusteeLink = "44444444-4444-4444-4444-444444444444";
    private const string DomainLink = "55555555-5555-5555-5555-555555555555";
    private const string AnyLink = "66666666-6666-6666-6666-666666666666";

    private static readonly PerUserDashboardFilter Filter = new();

    private static AccessTemplateLinkModel Link(string guid, string trusteeSid, string[]? allow = null, string[]? deny = null)
    {
        var template = new AccessTemplateReadModel { Name = $"AT-{guid[..4]}" };
        foreach (var a in allow ?? []) template.AllowReadTargets.Add(a);
        foreach (var d in deny ?? []) template.DenyReadTargets.Add(d);
        var link = new AccessTemplateLinkModel { LinkGuid = guid, AccessTemplateName = template.Name, Template = template };
        link.TrusteeSids.Add(trusteeSid);
        return link;
    }

    private static ArPermissionModel Model(params AccessTemplateLinkModel[] links) => new()
    {
        LinksByGuid = links.ToDictionary(l => l.LinkGuid, StringComparer.OrdinalIgnoreCase)
    };

    private static ArPermissionModel StandardModel() => Model(
        Link(UsersLink, ViewerSid, allow: ["user"]),
        Link(GroupsLink, HelpDeskSid, allow: ["group"]),
        Link(DenyUsersLink, ViewerSid, deny: ["user"]),
        Link(OtherTrusteeLink, OtherSid, allow: ["Any"]),
        Link(DomainLink, ViewerSid, allow: ["domainDNS"]));

    private static UserSidSet Viewer(params string[] sids)
    {
        var u = new UserSidSet { Username = @"TEST\viewer" };
        foreach (var s in sids) u.Sids.Add(s);
        return u;
    }

    private static JsonElement Raw(string name, string cls, params string[] linkGuids) => JsonSerializer.SerializeToElement(new
    {
        name,
        objectClass = new[] { "top", cls },
        edsvaATLinksEffective = linkGuids.Select(g => $"CN={{{g}}},CN=AT Links,CN=Configuration").ToArray()
    });

    private static ADUserAccountDetailInfo Detail(string name, params string[] links) =>
        new() { Name = name, ObjectClass = "user", EffectiveLinkGuids = links };

    private static DashboardSummary Superset()
    {
        var users = new List<JsonElement>
        {
            Raw("visible-user", "user", UsersLink),
            Raw("denied-user", "user", UsersLink, DenyUsersLink),
            Raw("other-trustee-user", "user", OtherTrusteeLink),
            Raw("no-links-user", "user"),
            Raw("unknown-link-user", "user", "99999999-9999-9999-9999-999999999999")
        };
        var groups = new List<JsonElement>
        {
            Raw("helpdesk-group", "group", GroupsLink),
            Raw("user-link-on-group", "group", UsersLink)
        };

        return new DashboardSummary
        {
            ADUserAccounts = new ADUserAccountsSummary { Items = users, TotalCount = users.Count },
            ADGroups = new ADGroupsSummary { Items = groups, TotalCount = groups.Count },
            EnabledUsers = new ADUserAccountDetailSummary
            {
                Items = [Detail("a", UsersLink), Detail("b", OtherTrusteeLink), Detail("c", UsersLink, DenyUsersLink)],
                TotalCount = 3
            },
            DisabledUsers = new ADUserAccountDetailSummary { Error = "collection failed" },
            Domains = new DomainSummary
            {
                Items =
                [
                    new DomainInfo { Name = "PROD", ObjectClass = "domainDNS", EffectiveLinkGuids = [DomainLink] },
                    new DomainInfo { Name = "DEV", ObjectClass = "domainDNS", EffectiveLinkGuids = [OtherTrusteeLink] }
                ],
                TotalCount = 2
            },
            ExchangeKpis = { ["mailboxes"] = new GovernanceKpiSummary { Items = [], TotalCount = 0 } }
        };
    }

    private static List<string> Names(IEnumerable<JsonElement> items) =>
        items.Select(i => i.GetProperty("name").GetString()!).ToList();

    [Fact]
    public void Direct_trustee_sees_only_allowed_objects_of_the_granted_class()
    {
        var s = Filter.Filter(Superset(), Viewer(ViewerSid), StandardModel());

        Assert.Equal(["visible-user"], Names(s.ADUserAccounts.Items));
        Assert.Equal(1, s.ADUserAccounts.TotalCount);
        // The user-class template must not leak group objects carrying the same link.
        Assert.Empty(s.ADGroups.Items);
        Assert.Equal(0, s.ADGroups.TotalCount);
    }

    [Fact]
    public void Deny_overrides_allow_on_the_same_object()
    {
        var s = Filter.Filter(Superset(), Viewer(ViewerSid), StandardModel());

        Assert.DoesNotContain("denied-user", Names(s.ADUserAccounts.Items));
        Assert.Equal(["a"], s.EnabledUsers.Items.Select(i => i.Name));
        Assert.Equal(1, s.EnabledUsers.TotalCount);
    }

    [Fact]
    public void Indirect_trustee_through_group_sid_gains_group_visibility()
    {
        var s = Filter.Filter(Superset(), Viewer(ViewerSid, HelpDeskSid), StandardModel());

        Assert.Equal(["helpdesk-group"], Names(s.ADGroups.Items));
    }

    [Fact]
    public void Links_trusteed_to_other_principals_grant_nothing()
    {
        var s = Filter.Filter(Superset(), Viewer(ViewerSid), StandardModel());

        Assert.DoesNotContain("other-trustee-user", Names(s.ADUserAccounts.Items));
        Assert.DoesNotContain("no-links-user", Names(s.ADUserAccounts.Items));
        Assert.DoesNotContain("unknown-link-user", Names(s.ADUserAccounts.Items));
    }

    [Fact]
    public void Viewer_with_no_delegation_gets_a_zeroed_summary()
    {
        var s = Filter.Filter(Superset(), Viewer("S-1-5-21-1-5555"), StandardModel());

        Assert.Empty(s.ADUserAccounts.Items);
        Assert.Equal(0, s.ADUserAccounts.TotalCount);
        Assert.Empty(s.Domains.Items);
        Assert.False(s.LicensingVisible);
        Assert.False(s.ExchangeVisible);
    }

    [Fact]
    public void Empty_sid_set_and_empty_model_expose_nothing()
    {
        Assert.Empty(Filter.Filter(Superset(), Viewer(), StandardModel()).ADUserAccounts.Items);
        Assert.Empty(Filter.Filter(Superset(), Viewer(ViewerSid), ArPermissionModel.Empty).ADUserAccounts.Items);
    }

    [Fact]
    public void Link_without_template_is_ignored()
    {
        var link = Link(UsersLink, ViewerSid, allow: ["user"]);
        link.Template = null;

        var s = Filter.Filter(Superset(), Viewer(ViewerSid), Model(link));

        Assert.Empty(s.ADUserAccounts.Items);
    }

    [Fact]
    public void Any_target_grants_all_classes_but_deny_still_wins()
    {
        var model = Model(Link(AnyLink, ViewerSid, allow: ["Any"]), Link(DenyUsersLink, ViewerSid, deny: ["user"]));
        var superset = new DashboardSummary
        {
            ADUserAccounts = new ADUserAccountsSummary { Items = [Raw("u1", "user", AnyLink), Raw("u2", "user", AnyLink, DenyUsersLink)] },
            ADGroups = new ADGroupsSummary { Items = [Raw("g1", "group", AnyLink)] }
        };

        var s = Filter.Filter(superset, Viewer(ViewerSid), model);

        Assert.Equal(["u1"], Names(s.ADUserAccounts.Items));
        Assert.Equal(["g1"], Names(s.ADGroups.Items));
    }

    [Fact]
    public void Sid_and_link_guid_matching_is_case_insensitive()
    {
        var model = Model(Link(UsersLink.ToUpperInvariant(), ViewerSid.ToLowerInvariant(), allow: ["USER"]));
        var superset = new DashboardSummary
        {
            ADUserAccounts = new ADUserAccountsSummary { Items = [Raw("u1", "user", UsersLink)] }
        };

        var s = Filter.Filter(superset, Viewer(ViewerSid.ToUpperInvariant()), model);

        Assert.Single(s.ADUserAccounts.Items);
    }

    [Fact]
    public void Error_summaries_are_preserved_rather_than_reported_as_zero()
    {
        var s = Filter.Filter(Superset(), Viewer(ViewerSid), StandardModel());

        Assert.Equal("collection failed", s.DisabledUsers.Error);
    }

    [Fact]
    public void Entra_data_is_never_exposed_to_delegated_viewers()
    {
        var superset = Superset();
        var s = Filter.Filter(superset, Viewer(ViewerSid), StandardModel());

        Assert.NotSame(superset.EntraTotals, s.EntraTotals);
        Assert.Equal(JsonSerializer.Serialize(new EntraTotalsSummary()), JsonSerializer.Serialize(s.EntraTotals));
    }

    [Fact]
    public void Exchange_kpi_keys_are_preserved()
    {
        var s = Filter.Filter(Superset(), Viewer(ViewerSid), StandardModel());

        Assert.True(s.ExchangeKpis.ContainsKey("mailboxes"));
    }

    [Fact]
    public void Superset_is_not_mutated()
    {
        var superset = Superset();
        var before = JsonSerializer.Serialize(superset);

        Filter.Filter(superset, Viewer(ViewerSid), StandardModel());

        Assert.Equal(before, JsonSerializer.Serialize(superset));
        Assert.Equal(5, superset.ADUserAccounts.Items.Count);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Filter.Filter(null!, Viewer(ViewerSid), StandardModel()));
        Assert.Throws<ArgumentNullException>(() => Filter.Filter(Superset(), null!, StandardModel()));
        Assert.Throws<ArgumentNullException>(() => Filter.Filter(Superset(), Viewer(ViewerSid), null!));
    }

    [Fact]
    public void HasAnyDelegation_requires_trustee_overlap_with_a_templated_link()
    {
        var model = StandardModel();

        Assert.True(model.HasAnyDelegation(Viewer(ViewerSid)));
        Assert.True(model.HasAnyDelegation(Viewer(HelpDeskSid)));
        Assert.False(model.HasAnyDelegation(Viewer("S-1-5-21-1-5555")));
        Assert.False(model.HasAnyDelegation(Viewer()));
        Assert.False(model.HasAnyDelegation(null!));
    }

    [Theory]
    [InlineData(ViewerSid, "edsManagedObjectStatisticsData", true)]
    [InlineData(OtherSid, "edsManagedObjectStatisticsData", false)]
    [InlineData(ViewerSid, "user", false)]
    public void Licensing_visibility_requires_read_on_the_statistics_probe(string sid, string probeClass, bool expected)
    {
        var model = new ArPermissionModel
        {
            LinksByGuid = new Dictionary<string, AccessTemplateLinkModel>(StringComparer.OrdinalIgnoreCase)
            {
                [UsersLink] = Link(UsersLink, ViewerSid, allow: ["edsManagedObjectStatisticsData"])
            },
            LicensingProbe = new LicensingVisibilityProbe { EffectiveLinkGuids = [UsersLink], ObjectClass = probeClass }
        };

        Assert.Equal(expected, model.GrantsLicensingVisibility(Viewer(sid)));
    }

    [Fact]
    public void Licensing_is_hidden_when_no_probe_was_captured()
    {
        Assert.False(StandardModel().GrantsLicensingVisibility(Viewer(ViewerSid)));
    }

    [Fact]
    public void Definition_datasets_are_filtered_and_kpi_results_recomputed_per_viewer()
    {
        var registry = new ActiveRolesDashboard.Models.Definitions.DefinitionRegistry(new ActiveRolesDashboard.Models.Definitions.DefinitionSet
        {
            Categories = [new ActiveRolesDashboard.Models.Definitions.CategoryDefinition { Id = "Cat", DashboardId = "Main" }],
            Kpis =
            [
                new ActiveRolesDashboard.Models.Definitions.DerivedKpi
                {
                    Id = "AllExtra", CategoryId = "Cat", SourceDatasetId = "Extra",
                    Evaluation = new ActiveRolesDashboard.Models.Definitions.EvaluationDefinition()
                },
                new ActiveRolesDashboard.Models.Definitions.DerivedKpi
                {
                    Id = "AllUsers", CategoryId = "Cat", SourceDatasetId = "ADUserAccounts",
                    Evaluation = new ActiveRolesDashboard.Models.Definitions.EvaluationDefinition()
                }
            ]
        });
        var calculator = new ActiveRolesDashboard.Services.Definitions.DefinitionKpiCalculator(registry,
            new ActiveRolesDashboard.Models.Definitions.Evaluation.KpiEvaluator(new ActiveRolesDashboard.Models.Definitions.Evaluation.KpiFunctionRegistry()));
        var superset = Superset();
        superset.CollectedDatasets["Extra"] = new([Raw("visible-user", "user", UsersLink), Raw("hidden", "user", OtherTrusteeLink)], null);
        calculator.Populate(superset, new KpiSettings(), new() { Now = DateTimeOffset.UtcNow });
        Assert.Equal(2, superset.KpiResults["AllExtra"].Value);

        var s = new PerUserDashboardFilter(calculator).Filter(superset, Viewer(ViewerSid), StandardModel());

        Assert.Single(s.CollectedDatasets["Extra"].Items);
        Assert.Equal(1, s.KpiResults["AllExtra"].Value);
        Assert.Equal(s.ADUserAccounts.Items.Count, s.KpiResults["AllUsers"].Value);

        var zeroed = new PerUserDashboardFilter(calculator).Filter(superset, Viewer("S-1-5-21-1-5555"), StandardModel());
        Assert.Equal(0, zeroed.KpiResults["AllUsers"].Value);
    }
}
