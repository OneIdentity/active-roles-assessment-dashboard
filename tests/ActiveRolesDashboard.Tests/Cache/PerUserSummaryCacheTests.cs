using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace ActiveRolesDashboard.Tests.Cache;

/// <summary>
/// Per-user server-side cache: sliding expiry, per-user isolation (case-insensitive usernames),
/// the user-data and directory-facts epochs, targeted clearing, and corrupt summary recovery.
/// </summary>
[Trait("Stage", "Cache")]
public class PerUserSummaryCacheTests
{
    private const string Alice = @"TEST\alice";
    private const string Bob = @"TEST\bob";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private sealed class ManualClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    }

    private readonly ManualClock _clock = new();
    private readonly PerUserSummaryCache _cache;

    public PerUserSummaryCacheTests()
    {
#pragma warning disable CS0618 // ISystemClock is the supported way to drive MemoryCache time in tests.
        _cache = new PerUserSummaryCache(new MemoryCache(new MemoryCacheOptions { Clock = _clock }));
#pragma warning restore CS0618
    }

    private void SeedAll(string user)
    {
        _cache.SetSummary(user, "{}");
        _cache.SetOverview(user, "{\"o\":1}");
        _cache.SetAdmin(user, true);
        _cache.SetRole(user, nameof(DashboardRole.Auditor));
    }

    // ------------------------------------------------------------------ empty / round trip

    [Fact]
    public void Unknown_user_has_nothing_cached()
    {
        Assert.Null(_cache.GetSummary(Alice));
        Assert.Null(_cache.GetSummaryModel(Alice));
        Assert.Null(_cache.GetOverview(Alice));
        Assert.Null(_cache.GetAdmin(Alice));
        Assert.Null(_cache.GetRole(Alice));
        Assert.Null(_cache.GetExchangeDeployed());
    }

    [Fact]
    public void Values_round_trip()
    {
        SeedAll(Alice);
        _cache.SetExchangeDeployed(false);

        Assert.Equal("{}", _cache.GetSummary(Alice));
        Assert.Equal("{\"o\":1}", _cache.GetOverview(Alice));
        Assert.True(_cache.GetAdmin(Alice));
        Assert.Equal(nameof(DashboardRole.Auditor), _cache.GetRole(Alice));
        Assert.False(_cache.GetExchangeDeployed());
    }

    [Fact]
    public void Summary_model_is_a_fresh_copy_each_time()
    {
        _cache.SetSummary(Alice, "{}");

        var first = _cache.GetSummaryModel(Alice);
        var second = _cache.GetSummaryModel(Alice);

        Assert.NotNull(first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Json_null_summary_yields_empty_model()
    {
        _cache.SetSummary(Alice, "null");

        Assert.NotNull(_cache.GetSummaryModel(Alice));
    }

    [Fact]
    public void Corrupt_summary_is_dropped_with_overview_but_facts_survive()
    {
        SeedAll(Alice);
        _cache.SetSummary(Alice, "{not json");

        Assert.Null(_cache.GetSummaryModel(Alice));

        Assert.Null(_cache.GetSummary(Alice));
        Assert.Null(_cache.GetOverview(Alice));
        Assert.True(_cache.GetAdmin(Alice));
        Assert.Equal(nameof(DashboardRole.Auditor), _cache.GetRole(Alice));
    }

    // ------------------------------------------------------------------ isolation

    [Fact]
    public void Users_are_isolated()
    {
        SeedAll(Alice);

        Assert.Null(_cache.GetSummary(Bob));
        Assert.Null(_cache.GetOverview(Bob));
        Assert.Null(_cache.GetAdmin(Bob));
        Assert.Null(_cache.GetRole(Bob));
    }

    [Fact]
    public void Usernames_are_case_insensitive()
    {
        SeedAll(Alice);

        Assert.Equal("{}", _cache.GetSummary(Alice.ToUpperInvariant()));
        Assert.Equal(nameof(DashboardRole.Auditor), _cache.GetRole(@"test\ALICE"));
    }

    [Fact]
    public void Clear_drops_everything_for_one_user_only()
    {
        SeedAll(Alice);
        SeedAll(Bob);
        _cache.SetExchangeDeployed(true);

        _cache.Clear(Alice);

        Assert.Null(_cache.GetSummary(Alice));
        Assert.Null(_cache.GetOverview(Alice));
        Assert.Null(_cache.GetAdmin(Alice));
        Assert.Null(_cache.GetRole(Alice));
        Assert.Equal("{}", _cache.GetSummary(Bob));
        Assert.Equal(nameof(DashboardRole.Auditor), _cache.GetRole(Bob));
        Assert.True(_cache.GetExchangeDeployed());
    }

    [Fact]
    public void ClearUserData_keeps_directory_facts()
    {
        SeedAll(Alice);

        _cache.ClearUserData(Alice);

        Assert.Null(_cache.GetSummary(Alice));
        Assert.Null(_cache.GetOverview(Alice));
        Assert.True(_cache.GetAdmin(Alice));
        Assert.Equal(nameof(DashboardRole.Auditor), _cache.GetRole(Alice));
    }

    // ------------------------------------------------------------------ epochs

    [Fact]
    public void InvalidateAllUserData_hides_data_for_every_user_but_keeps_facts()
    {
        SeedAll(Alice);
        SeedAll(Bob);

        _cache.InvalidateAllUserData();

        foreach (var user in new[] { Alice, Bob })
        {
            Assert.Null(_cache.GetSummary(user));
            Assert.Null(_cache.GetOverview(user));
            Assert.True(_cache.GetAdmin(user));
            Assert.Equal(nameof(DashboardRole.Auditor), _cache.GetRole(user));
        }
    }

    [Fact]
    public void Data_written_after_user_data_invalidation_is_visible()
    {
        _cache.SetSummary(Alice, "{\"old\":1}");
        _cache.InvalidateAllUserData();

        _cache.SetSummary(Alice, "{}");

        Assert.Equal("{}", _cache.GetSummary(Alice));
    }

    [Fact]
    public void InvalidateDirectoryFacts_hides_facts_for_every_user_but_keeps_data()
    {
        SeedAll(Alice);
        SeedAll(Bob);
        var epoch = _cache.DirectoryFactsEpoch;

        _cache.InvalidateDirectoryFacts();

        Assert.Equal(epoch + 1, _cache.DirectoryFactsEpoch);
        foreach (var user in new[] { Alice, Bob })
        {
            Assert.Null(_cache.GetAdmin(user));
            Assert.Null(_cache.GetRole(user));
            Assert.Equal("{}", _cache.GetSummary(user));
        }
    }

    [Fact]
    public void Facts_written_after_directory_invalidation_are_visible()
    {
        SeedAll(Alice);
        _cache.InvalidateDirectoryFacts();

        _cache.SetAdmin(Alice, false);
        _cache.SetRole(Alice, nameof(DashboardRole.PowerUser));

        Assert.False(_cache.GetAdmin(Alice));
        Assert.Equal(nameof(DashboardRole.PowerUser), _cache.GetRole(Alice));
    }

    [Fact]
    public async Task Concurrent_invalidations_are_not_lost()
    {
        var start = _cache.DirectoryFactsEpoch;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                _cache.InvalidateDirectoryFacts();
        })));

        Assert.Equal(start + 4000, _cache.DirectoryFactsEpoch);
    }

    // ------------------------------------------------------------------ sliding expiry

    [Fact]
    public void Entries_expire_after_idle_lifetime()
    {
        SeedAll(Alice);
        _cache.SetExchangeDeployed(true);

        _clock.UtcNow += Lifetime + TimeSpan.FromSeconds(1);

        Assert.Null(_cache.GetSummary(Alice));
        Assert.Null(_cache.GetOverview(Alice));
        Assert.Null(_cache.GetAdmin(Alice));
        Assert.Null(_cache.GetRole(Alice));
        Assert.Null(_cache.GetExchangeDeployed());
    }

    [Fact]
    public void Entries_survive_just_inside_idle_lifetime()
    {
        SeedAll(Alice);

        _clock.UtcNow += Lifetime - TimeSpan.FromSeconds(1);

        Assert.Equal("{}", _cache.GetSummary(Alice));
        Assert.True(_cache.GetAdmin(Alice));
    }

    [Fact]
    public void Access_extends_the_sliding_lifetime()
    {
        _cache.SetSummary(Alice, "{}");

        for (var i = 0; i < 3; i++)
        {
            _clock.UtcNow += Lifetime - TimeSpan.FromMinutes(1);
            Assert.Equal("{}", _cache.GetSummary(Alice));
        }
    }

    [Fact]
    public void Expiry_of_one_user_does_not_affect_another()
    {
        _cache.SetSummary(Alice, "{}");
        _clock.UtcNow += TimeSpan.FromHours(4);
        _cache.SetSummary(Bob, "{}");

        _clock.UtcNow += TimeSpan.FromHours(4) + TimeSpan.FromSeconds(1);

        Assert.Null(_cache.GetSummary(Alice));
        Assert.Equal("{}", _cache.GetSummary(Bob));
    }
}
