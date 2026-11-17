using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ActiveRolesDashboard.Tests.DataLoading;

/// <summary>
/// Exercises <see cref="DashboardDataService"/> from the real DI container: cold (direct) versus
/// warm (shared superset) loading, per-user summary caching, Entra membership reconciliation and
/// segment filtering.
/// </summary>
[Trait("Stage", "DataLoad")]
public class DashboardDataServiceTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private readonly DashboardAppFactory _factory;

    public DashboardDataServiceTests()
    {
        _factory = new DashboardAppFactory(activeRoles: _server);
    }

    private DashboardDataService Data => _factory.Services.GetRequiredService<DashboardDataService>();
    private DashboardCacheHolder Cache => _factory.Services.GetRequiredService<DashboardCacheHolder>();
    private PerUserSummaryCache UserCache => _factory.Services.GetRequiredService<PerUserSummaryCache>();

    private static readonly DashboardAccess Admin = new() { IsActiveRolesAdmin = true, Role = DashboardRole.DashboardAdministrator };

    private static DefaultHttpContext Http(string user = "TEST\\alice")
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], "Test")),
        };
        http.Session = new InMemorySession();
        http.Session.SetString("AccessToken", FakeActiveRolesServer.ValidToken);
        return http;
    }

    private static JsonElement AdItem(string name, string domain) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["name"] = name,
            ["distinguishedName"] = $"CN={name},DC={domain.ToLowerInvariant()},DC=local",
            [SegmentAttributes.DomainAttribute] = domain,
        });

    private static DashboardSummary Superset(bool membershipLoaded = true) => new()
    {
        ADUserAccounts = new ADUserAccountsSummary
        {
            TotalCount = 3,
            Items = [AdItem("u1", "CONTOSO"), AdItem("u2", "CONTOSO"), AdItem("u3", "FABRIKAM")],
        },
        Computers = new ComputersSummary { TotalCount = 1, Items = [AdItem("c1", "FABRIKAM")] },
        EntraTotals = new EntraTotalsSummary
        {
            TotalCount = 2,
            Tenants = ["tenant-a", "tenant-b"],
            MembershipLoaded = membershipLoaded,
            Items =
            [
                new EntraObjectInfo { Name = "g1", Tenant = "tenant-a", ObjectType = EntraObjectType.SecurityGroup, Raw = JsonSerializer.SerializeToElement(new { name = "g1" }) },
                new EntraObjectInfo { Name = "u1", Tenant = "tenant-b", ObjectType = EntraObjectType.User, Raw = JsonSerializer.SerializeToElement(new { name = "u1" }) },
            ],
        },
    };

    private void PublishSuperset(DashboardSummary summary) =>
        Cache.Publish(new DashboardSupersetSnapshot(summary, DateTimeOffset.UtcNow));

    private int SearchRequestCount() => _server.Requests.Count(r => r.RequestMessage.Path == "/api/objects");

    // ------------------------------------------------------------------ cold vs warm

    [Fact]
    public async Task Cold_cache_loads_directly_from_active_roles()
    {
        _server.WithDataSet("small-domain");
        var http = Http();

        var summary = await Data.LoadFullSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        Assert.Null(Cache.Current);
        Assert.True(SearchRequestCount() > 0);
        Assert.True(summary.ADUserAccounts.TotalCount > 0);
        Assert.NotNull(Data.GetCachedSummaryJson(http));
    }

    [Fact]
    public async Task Warm_cache_is_served_from_superset_without_directory_queries()
    {
        PublishSuperset(Superset());

        var summary = await Data.LoadFullSummaryAsync(Http(), FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        Assert.Equal(3, summary.ADUserAccounts.TotalCount);
        Assert.Equal(1, summary.Computers.TotalCount);
        Assert.True(summary.LicensingVisible);
        // Only the organization-wide Exchange deployment probe may touch the directory.
        Assert.True(SearchRequestCount() <= 1);
    }

    [Fact]
    public async Task Exchange_is_hidden_when_not_deployed_and_probe_is_cached()
    {
        PublishSuperset(Superset());

        var first = await Data.LoadFullSummaryAsync(Http("TEST\\a"), FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());
        var afterFirst = SearchRequestCount();
        var second = await Data.LoadFullSummaryAsync(Http("TEST\\b"), FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        Assert.False(first.ExchangeVisible);
        Assert.False(second.ExchangeVisible);
        Assert.False(UserCache.GetExchangeDeployed());
        Assert.Equal(afterFirst, SearchRequestCount());
    }

    // ------------------------------------------------------------------ per-user cache

    [Fact]
    public async Task Load_stores_summary_and_overview_for_the_user()
    {
        PublishSuperset(Superset());
        var http = Http();

        await Data.LoadFullSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        var overview = JsonSerializer.Deserialize<OverviewTotalsCache>(UserCache.GetOverview("TEST\\alice")!)!;
        Assert.Equal(3, overview.ADUserAccounts.TotalCount);
        Assert.Equal(1, overview.Computers.TotalCount);
        Assert.NotNull(UserCache.GetSummary("test\\ALICE"));
        Assert.Null(UserCache.GetSummary("TEST\\bob"));
    }

    [Fact]
    public async Task Index_load_reuses_warm_per_user_cache()
    {
        PublishSuperset(Superset());
        var http = Http();
        await Data.LoadIndexSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        // A newer superset is not visible until the user's cached projection is cleared.
        var newer = Superset();
        newer.ADUserAccounts.TotalCount = 99;
        PublishSuperset(newer);
        var cached = await Data.LoadIndexSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());
        Data.ClearCachedSummary(http);
        var rebuilt = await Data.LoadIndexSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        Assert.Equal(3, cached.ADUserAccounts.TotalCount);
        Assert.Equal(99, rebuilt.ADUserAccounts.TotalCount);
    }

    [Fact]
    public async Task Invalidating_all_user_data_forces_rebuild()
    {
        PublishSuperset(Superset());
        var http = Http();
        await Data.LoadIndexSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        UserCache.InvalidateAllUserData();

        Assert.Null(Data.GetCachedSummaryJson(http));
        Assert.Null(UserCache.GetOverview("TEST\\alice"));
    }

    [Fact]
    public async Task Corrupt_cached_summary_is_not_silently_used()
    {
        PublishSuperset(Superset());
        var http = Http();
        UserCache.SetSummary("TEST\\alice", "{not json");

        await Assert.ThrowsAsync<JsonException>(() =>
            Data.LoadIndexSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings()));
    }

    // ------------------------------------------------------------------ membership reconciliation

    [Fact]
    public async Task Pending_membership_is_reconciled_once_superset_completes()
    {
        PublishSuperset(Superset(membershipLoaded: false));
        var http = Http();
        var pending = await Data.LoadIndexSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());
        Assert.True(pending.EntraMembershipDataPending);

        PublishSuperset(Superset(membershipLoaded: true));
        var reconciled = await Data.LoadIndexSummaryAsync(http, FakeActiveRolesServer.ValidToken, Admin, new KpiSettings());

        Assert.False(reconciled.EntraMembershipDataPending);
    }

    [Fact]
    public void Reconciliation_waits_while_membership_is_still_loading()
    {
        PublishSuperset(Superset(membershipLoaded: true));
        Cache.BeginMembershipLoading(5);

        Assert.False(Data.ShouldReconcileMembership(Superset(membershipLoaded: false)));

        Cache.EndMembershipLoading();
        Assert.True(Data.ShouldReconcileMembership(Superset(membershipLoaded: false)));
    }

    [Fact]
    public void Reconciliation_is_not_needed_for_complete_or_missing_summaries()
    {
        PublishSuperset(Superset(membershipLoaded: true));

        Assert.False(Data.ShouldReconcileMembership(null));
        Assert.False(Data.ShouldReconcileMembership(Superset(membershipLoaded: true)));
        Assert.False(Data.ShouldReconcileMembership(new DashboardSummary()));
    }

    // ------------------------------------------------------------------ segments

    [Fact]
    public void Default_segment_selection_is_all_available()
    {
        var summary = Superset();

        var view = Data.ApplyActiveSegmentFilter(Http(), summary);

        Assert.Equal(["CONTOSO", "FABRIKAM"], view.AvailableDomains);
        Assert.Equal(view.AvailableDomains, view.SelectedDomains);
        Assert.Equal(["tenant-a", "tenant-b"], view.SelectedTenants);
        Assert.Equal(3, summary.ADUserAccounts.Items.Count);
        Assert.True(summary.AdVisible);
        Assert.True(summary.EntraVisible);
    }

    [Fact]
    public void Selected_domain_filters_items_and_keeps_available_list()
    {
        var http = Http();
        Data.SetSegmentFilter(http, "Domain", [" contoso ", "CONTOSO", ""]);
        var summary = Superset();

        var view = Data.ApplyActiveSegmentFilter(http, summary);

        Assert.Equal(["CONTOSO", "FABRIKAM"], view.AvailableDomains);
        Assert.Single(view.SelectedDomains);
        Assert.Equal(2, summary.ADUserAccounts.Items.Count);
        Assert.Empty(summary.Computers.Items);
        Assert.Equal(["tenant-a", "tenant-b"], view.SelectedTenants);
    }

    [Fact]
    public void Empty_selection_hides_the_dimension()
    {
        var http = Http();
        Data.SetSegmentFilter(http, "Domain", []);
        Data.SetSegmentFilter(http, "Tenant", null);
        var summary = Superset();

        var view = Data.ApplyActiveSegmentFilter(http, summary);

        Assert.Empty(view.SelectedDomains);
        Assert.Empty(view.SelectedTenants);
        Assert.False(summary.AdVisible);
        Assert.False(summary.EntraVisible);
    }

    [Fact]
    public void Stale_or_unknown_selections_resolve_safely()
    {
        var http = Http();
        Data.SetSegmentFilter(http, "Domain", ["GONE"]);
        Data.SetSegmentFilter(http, "Bogus", ["x"]);
        var summary = Superset();

        var view = Data.ApplyActiveSegmentFilter(http, summary);

        Assert.Empty(view.SelectedDomains);
        Assert.Equal(["tenant-a", "tenant-b"], view.SelectedTenants);
    }

    [Fact]
    public void Corrupt_segment_session_falls_back_to_all()
    {
        var http = Http();
        http.Session.SetString(SegmentFilterSession.SessionKey, "{broken");

        var view = Data.ApplyActiveSegmentFilter(http, Superset());

        Assert.Equal(view.AvailableDomains, view.SelectedDomains);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _server.Dispose();
    }

    private sealed class InMemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _store.TryGetValue(key, out value);
    }
}
