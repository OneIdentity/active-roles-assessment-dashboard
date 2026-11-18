using System.Collections;
using System.Reflection;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Exhaustive coverage of <see cref="PerUserDashboardFilter"/> across every item-bearing family of
/// <see cref="DashboardSummary"/>. The filter maps each family by hand, so these tests discover the
/// families by reflection and verify each one independently: a family that is forgotten, mapped
/// from the wrong source, left unfiltered or given a stale count fails here, including families
/// added to the model later. Visibility rule semantics are covered by <see cref="PermissionFilteringTests"/>.
/// </summary>
[Trait("Stage", "Security")]
public class PerUserDashboardFilterCoverageTests
{
    private const string ViewerSid = "S-1-5-21-1-1001";
    private const string OtherSid = "S-1-5-21-1-9999";
    private const string VisibleLink = "11111111-1111-1111-1111-111111111111";
    private const string HiddenLink = "22222222-2222-2222-2222-222222222222";

    private static readonly PerUserDashboardFilter Filter = new();

    /// <summary>One item-bearing family: the summary property and its item type.</summary>
    public sealed record Family(PropertyInfo Property, Type ItemType)
    {
        public override string ToString() => Property.Name;
    }

    private static readonly IReadOnlyList<Family> Families = typeof(DashboardSummary)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite)
        .Select(p => (p, items: p.PropertyType.GetProperty("Items")))
        .Where(x => x.items?.PropertyType is { IsGenericType: true } t
                    && t.GetGenericTypeDefinition() == typeof(List<>)
                    && typeof(IPermissionScoped).IsAssignableFrom(t.GetGenericArguments()[0]))
        .Select(x => new Family(x.p, x.items!.PropertyType.GetGenericArguments()[0]))
        .ToList();

    public static TheoryData<string> FamilyNames()
    {
        var data = new TheoryData<string>();
        foreach (var f in Families) data.Add(f.Property.Name);
        return data;
    }

    private static Family Get(string name) => Families.Single(f => f.Property.Name == name);

    private static ArPermissionModel Model()
    {
        AccessTemplateLinkModel Link(string guid, string sid)
        {
            var template = new AccessTemplateReadModel { Name = $"AT-{guid[..4]}" };
            template.AllowReadTargets.Add("Any");
            var link = new AccessTemplateLinkModel { LinkGuid = guid, AccessTemplateName = template.Name, Template = template };
            link.TrusteeSids.Add(sid);
            return link;
        }

        return new ArPermissionModel
        {
            LinksByGuid = new Dictionary<string, AccessTemplateLinkModel>(StringComparer.OrdinalIgnoreCase)
            {
                [VisibleLink] = Link(VisibleLink, ViewerSid),
                [HiddenLink] = Link(HiddenLink, OtherSid)
            }
        };
    }

    private static UserSidSet Viewer(string sid = ViewerSid)
    {
        var u = new UserSidSet { Username = @"TEST\viewer" };
        u.Sids.Add(sid);
        return u;
    }

    private static IPermissionScoped NewItem(Type itemType, string link)
    {
        var item = Activator.CreateInstance(itemType)!;
        itemType.GetProperty(nameof(IPermissionScoped.EffectiveLinkGuids))!.SetValue(item, new[] { link });
        itemType.GetProperty(nameof(IPermissionScoped.ObjectClass))!.SetValue(item, "user");
        return (IPermissionScoped)item;
    }

    private static IList Items(object summary) =>
        (IList)summary.GetType().GetProperty("Items")!.GetValue(summary)!;

    private static int? TotalCount(object summary) =>
        summary.GetType().GetProperty("TotalCount")?.GetValue(summary) as int?;

    /// <summary>
    /// Fills every family with one visible and one hidden item, each a distinct instance, and
    /// returns the visible instance per family so tests can detect cross-family mix-ups.
    /// </summary>
    private static (DashboardSummary Superset, Dictionary<string, IPermissionScoped> Visible) Populated()
    {
        var superset = new DashboardSummary();
        var visible = new Dictionary<string, IPermissionScoped>();

        foreach (var f in Families)
        {
            var summary = Activator.CreateInstance(f.Property.PropertyType)!;
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(f.ItemType))!;
            var seen = NewItem(f.ItemType, VisibleLink);
            list.Add(seen);
            list.Add(NewItem(f.ItemType, HiddenLink));
            f.Property.PropertyType.GetProperty("Items")!.SetValue(summary, list);
            f.Property.PropertyType.GetProperty("TotalCount")?.SetValue(summary, list.Count);
            f.Property.SetValue(superset, summary);
            visible[f.Property.Name] = seen;
        }

        return (superset, visible);
    }

    // ---------------------------------------------------------------- discovery guard

    [Fact]
    public void Reflection_discovers_all_filtered_families()
    {
        // Guards against the theories passing vacuously if the model shape changes.
        Assert.True(Families.Count >= 70, $"Only {Families.Count} families discovered.");
        Assert.Contains(Families, f => f.Property.Name == nameof(DashboardSummary.EnabledUsers));
        Assert.Contains(Families, f => f.Property.Name == nameof(DashboardSummary.DomainAdmins));
        Assert.Contains(Families, f => f.Property.Name == nameof(DashboardSummary.StaleComputers));
        Assert.Contains(Families, f => f.Property.Name == nameof(DashboardSummary.Domains));
    }

    // ---------------------------------------------------------------- per-family filtering

    [Theory]
    [MemberData(nameof(FamilyNames))]
    public void Family_keeps_only_its_own_visible_item_and_recounts(string name)
    {
        var family = Get(name);
        var (superset, visible) = Populated();

        var filtered = family.Property.GetValue(Filter.Filter(superset, Viewer(), Model()))!;

        // Same instance proves the family was filtered from its OWN source, not another family's.
        var item = Assert.Single(Items(filtered).Cast<object>());
        Assert.Same(visible[name], item);
        if (TotalCount(filtered) is { } count)
            Assert.Equal(1, count);
    }

    [Theory]
    [MemberData(nameof(FamilyNames))]
    public void Family_is_empty_for_a_viewer_without_delegation(string name)
    {
        var family = Get(name);
        var (superset, _) = Populated();

        var filtered = family.Property.GetValue(Filter.Filter(superset, Viewer("S-1-5-21-1-5555"), Model()))!;

        Assert.Empty(Items(filtered));
        if (TotalCount(filtered) is { } count)
            Assert.Equal(0, count);
    }

    [Theory]
    [MemberData(nameof(FamilyNames))]
    public void Family_returns_a_new_instance_and_leaves_the_superset_intact(string name)
    {
        var family = Get(name);
        var (superset, _) = Populated();
        var source = family.Property.GetValue(superset)!;

        var filtered = family.Property.GetValue(Filter.Filter(superset, Viewer(), Model()))!;

        Assert.NotSame(source, filtered);
        Assert.Equal(2, Items(source).Count);
        if (TotalCount(source) is { } count)
            Assert.Equal(2, count);
    }

    [Theory]
    [MemberData(nameof(FamilyNames))]
    public void Family_error_is_preserved(string name)
    {
        var family = Get(name);
        var errorProperty = family.Property.PropertyType.GetProperty("Error");
        if (errorProperty is not { CanWrite: true })
            return;

        var (superset, _) = Populated();
        var source = family.Property.GetValue(superset)!;
        errorProperty.SetValue(source, $"{name} failed");

        var filtered = family.Property.GetValue(Filter.Filter(superset, Viewer(), Model()))!;

        Assert.Equal($"{name} failed", errorProperty.GetValue(filtered));
    }

    [Theory]
    [MemberData(nameof(FamilyNames))]
    public void Missing_family_yields_an_empty_summary(string name)
    {
        var family = Get(name);
        var (superset, _) = Populated();
        family.Property.SetValue(superset, null);

        var filtered = family.Property.GetValue(Filter.Filter(superset, Viewer(), Model()));

        Assert.NotNull(filtered);
        Assert.Empty(Items(filtered!));
    }

    // ---------------------------------------------------------------- non-generic families

    [Fact]
    public void Privileged_group_identity_is_preserved()
    {
        var (superset, _) = Populated();
        superset.DomainAdmins.GroupDn = "CN=Domain Admins,CN=Users,DC=test,DC=local";
        superset.DomainAdmins.GroupName = "Domain Admins";

        var s = Filter.Filter(superset, Viewer(), Model());

        Assert.Equal("CN=Domain Admins,CN=Users,DC=test,DC=local", s.DomainAdmins.GroupDn);
        Assert.Equal("Domain Admins", s.DomainAdmins.GroupName);
    }

    [Fact]
    public void Every_exchange_kpi_is_filtered_independently()
    {
        GovernanceKpiSummary Kpi(out GovernanceKpiInfo seen)
        {
            seen = (GovernanceKpiInfo)NewItem(typeof(GovernanceKpiInfo), VisibleLink);
            return new GovernanceKpiSummary { Items = [seen, (GovernanceKpiInfo)NewItem(typeof(GovernanceKpiInfo), HiddenLink)], TotalCount = 2 };
        }

        var superset = new DashboardSummary();
        superset.ExchangeKpis["mailboxes"] = Kpi(out var mailbox);
        superset.ExchangeKpis["sendAs"] = Kpi(out var sendAs);
        superset.ExchangeKpis["failed"] = new GovernanceKpiSummary { Error = "boom" };

        var s = Filter.Filter(superset, Viewer(), Model());

        Assert.Same(mailbox, Assert.Single(s.ExchangeKpis["mailboxes"].Items));
        Assert.Equal(1, s.ExchangeKpis["mailboxes"].TotalCount);
        Assert.Same(sendAs, Assert.Single(s.ExchangeKpis["sendAs"].Items));
        Assert.Equal("boom", s.ExchangeKpis["failed"].Error);
        Assert.Equal(2, superset.ExchangeKpis["mailboxes"].Items.Count);
    }

    [Fact]
    public void Raw_overview_sources_are_filtered_and_recounted()
    {
        JsonElement Raw(string name, string link) => JsonSerializer.SerializeToElement(new
        {
            name,
            objectClass = new[] { "top", "user" },
            edsvaATLinksEffective = new[] { $"CN={{{link}}},CN=AT Links,CN=Configuration" }
        });
        List<JsonElement> Pair(string prefix) => [Raw($"{prefix}-visible", VisibleLink), Raw($"{prefix}-hidden", HiddenLink)];

        var superset = new DashboardSummary
        {
            ADUserAccounts = new ADUserAccountsSummary { Items = Pair("user"), TotalCount = 2 },
            ADGroups = new ADGroupsSummary { Items = Pair("group"), TotalCount = 2 },
            Computers = new ComputersSummary { Items = Pair("computer"), TotalCount = 2 }
        };

        var s = Filter.Filter(superset, Viewer(), Model());

        static string Name(JsonElement e) => e.GetProperty("name").GetString()!;
        Assert.Equal("user-visible", Name(Assert.Single(s.ADUserAccounts.Items)));
        Assert.Equal(1, s.ADUserAccounts.TotalCount);
        Assert.Equal("group-visible", Name(Assert.Single(s.ADGroups.Items)));
        Assert.Equal(1, s.ADGroups.TotalCount);
        Assert.Equal("computer-visible", Name(Assert.Single(s.Computers.Items)));
        Assert.Equal(1, s.Computers.TotalCount);
    }

    [Fact]
    public void Raw_overview_error_is_preserved()
    {
        var superset = new DashboardSummary { Computers = new ComputersSummary { Error = "computers failed" } };

        var s = Filter.Filter(superset, Viewer(), Model());

        Assert.Equal("computers failed", s.Computers.Error);
    }
}
