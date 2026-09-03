using Microsoft.Extensions.Caching.Memory;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Per-user, server-side (in-process) cache for the two large dashboard blobs that used to be
/// stored in ASP.NET Core Session: the full per-user <c>DashboardSummary</c> and the lighter
/// <c>OverviewTotals</c>. Keeping these in Session made the session entry too large to round-trip
/// reliably, which caused the whole session (including the auth token) to be dropped on
/// navigation and signed users out. Session now holds only small keys; these bulky, already
/// permission-scoped projections live here instead.
///
/// Values are stored as JSON strings so the copy-semantics match the previous Session behaviour
/// exactly (deserialize yields a fresh object, never an alias of the shared superset snapshot).
/// Entries are keyed by the authenticated username and use a sliding expiration aligned with the
/// session idle timeout, so an idle user's entry simply rebuilds from the shared superset on the
/// next request rather than causing a sign-out.
///
/// NOTE: like <see cref="DashboardCacheHolder"/>, this is an in-process store and assumes a
/// single application instance (no web farm).
/// </summary>
public sealed class PerUserSummaryCache
{
    private readonly IMemoryCache _cache;

    // Aligned with the session IdleTimeout (see AddSession in Program.cs).
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public PerUserSummaryCache(IMemoryCache cache) => _cache = cache;

    /// <summary>Gets the cached full dashboard summary JSON for the user, or null if absent.</summary>
    public string? GetSummary(string user) => Read(SummaryKey(user));

    /// <summary>Stores the full dashboard summary JSON for the user.</summary>
    public void SetSummary(string user, string json) => Write(SummaryKey(user), json);

    /// <summary>Gets the cached overview totals JSON for the user, or null if absent.</summary>
    public string? GetOverview(string user) => Read(OverviewKey(user));

    /// <summary>Stores the overview totals JSON for the user.</summary>
    public void SetOverview(string user, string json) => Write(OverviewKey(user), json);

    /// <summary>
    /// Gets the cached "is Active Roles administrator" flag for the user, or null if not yet
    /// resolved. Cached at app scope (not Session) so it survives logout/login and the directory
    /// membership check runs at most once per cache lifetime rather than on every login.
    /// </summary>
    public bool? GetAdmin(string user) => _cache.TryGetValue(AdminKey(user), out bool value) ? value : null;

    /// <summary>Stores the resolved "is Active Roles administrator" flag for the user.</summary>
    public void SetAdmin(string user, bool isAdmin) =>
        _cache.Set(AdminKey(user), isAdmin, new MemoryCacheEntryOptions { SlidingExpiration = Lifetime });

    /// <summary>
    /// Gets the cached organization-wide "is Exchange deployed" flag, or null if not yet resolved.
    /// Cached at app scope (not per-user) since Exchange deployment is the same for every viewer.
    /// </summary>
    public bool? GetExchangeDeployed() => _cache.TryGetValue(ExchangeDeployedKey, out bool value) ? value : null;

    /// <summary>Stores the resolved organization-wide "is Exchange deployed" flag.</summary>
    public void SetExchangeDeployed(bool deployed) =>
        _cache.Set(ExchangeDeployedKey, deployed, new MemoryCacheEntryOptions { SlidingExpiration = Lifetime });

    /// <summary>Drops both cached blobs for the user (e.g. on an admin-triggered refresh).</summary>
    public void Clear(string user)
    {
        _cache.Remove(SummaryKey(user));
        _cache.Remove(OverviewKey(user));
        _cache.Remove(AdminKey(user));
    }

    private const string ExchangeDeployedKey = "org-exchange-deployed";

    private string? Read(string key) => _cache.TryGetValue(key, out string? value) ? value : null;

    private void Write(string key, string json) =>
        _cache.Set(key, json, new MemoryCacheEntryOptions { SlidingExpiration = Lifetime });

    private static string SummaryKey(string user) => $"pu-summary::{Normalize(user)}";

    private static string OverviewKey(string user) => $"pu-overview::{Normalize(user)}";

    private static string AdminKey(string user) => $"pu-admin::{Normalize(user)}";

    private static string Normalize(string user) => (user ?? string.Empty).ToLowerInvariant();
}
