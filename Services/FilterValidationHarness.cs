using System.Text.Json;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Result of validating the per-user filter for a single principal against the shared superset.
/// The filter (<see cref="PerUserDashboardFilter"/>) is the production path; this harness re-derives
/// the expected visible set directly from the permission model and asserts two invariants per KPI
/// family (soundness: nothing the viewer cannot see leaks through; completeness: nothing the viewer
/// can see is wrongly dropped). Any non-zero <see cref="FilterFamilyResult.Leaked"/> or
/// <see cref="FilterFamilyResult.Dropped"/> indicates a filtering defect.
/// </summary>
public sealed class FilterValidationResult
{
    public required string Username { get; init; }
    public required int ViewerSidCount { get; init; }
    public List<FilterFamilyResult> Families { get; } = new();
    public bool Passed => Families.All(f => f.Passed);
}

public sealed class FilterFamilyResult
{
    public required string Family { get; init; }
    public required int SupersetCount { get; init; }
    public required int ExpectedVisible { get; init; }
    public required int FilteredCount { get; init; }

    /// <summary>Items present in the filtered output that the viewer should NOT see.</summary>
    public required int Leaked { get; init; }

    /// <summary>Items the viewer SHOULD see that are missing from the filtered output.</summary>
    public required int Dropped { get; init; }

    public bool Passed => Leaked == 0 && Dropped == 0 && FilteredCount == ExpectedVisible;
}

/// <summary>
/// Admin-only diagnostics harness that cross-checks <see cref="PerUserDashboardFilter"/> output
/// against an independently derived ground truth (per-item <c>IsVisible</c> evaluation) using the
/// live shared superset and permission model. Requires no end-user credentials: the viewer's SID
/// set is resolved with the service-account token.
/// </summary>
public sealed class FilterValidationHarness
{
    private readonly DashboardCacheHolder _cache;
    private readonly PerUserDashboardFilter _filter;
    private readonly ArPermissionModelService _permissionModel;
    private readonly ServiceAccountTokenProvider _tokens;

    public FilterValidationHarness(
        DashboardCacheHolder cache,
        PerUserDashboardFilter filter,
        ArPermissionModelService permissionModel,
        ServiceAccountTokenProvider tokens)
    {
        _cache = cache;
        _filter = filter;
        _permissionModel = permissionModel;
        _tokens = tokens;
    }

    public async Task<FilterValidationResult> ValidateAsync(string username, CancellationToken ct = default)
    {
        var snapshot = _cache.Current
            ?? throw new InvalidOperationException("Cache is not ready; run the harness after the superset has loaded.");
        var model = _cache.PermissionModel;

        var serviceToken = await _tokens.GetTokenAsync(ct).ConfigureAwait(false);
        var viewer = await _permissionModel.ResolveUserSidSetAsync(serviceToken, username, ct).ConfigureAwait(false);

        var filtered = _filter.Filter(snapshot.Summary, viewer, model);

        var result = new FilterValidationResult { Username = username, ViewerSidCount = viewer.Sids.Count };
        result.Families.Add(CompareFamily("ADUserAccounts",
            snapshot.Summary.ADUserAccounts.Items, filtered.ADUserAccounts.Items, viewer, model));
        result.Families.Add(CompareFamily("ADGroups",
            snapshot.Summary.ADGroups.Items, filtered.ADGroups.Items, viewer, model));
        result.Families.Add(CompareFamily("Computers",
            snapshot.Summary.Computers.Items, filtered.Computers.Items, viewer, model));
        return result;
    }

    private static FilterFamilyResult CompareFamily(
        string family,
        IReadOnlyList<JsonElement> superset,
        IReadOnlyList<JsonElement> filtered,
        UserSidSet viewer,
        ArPermissionModel model)
    {
        var expected = superset.Where(i => model.IsVisible(i, viewer)).ToList();
        var expectedKeys = new HashSet<string>(expected.Select(KeyOf), StringComparer.OrdinalIgnoreCase);

        var leaked = filtered.Count(i => !model.IsVisible(i, viewer));
        var filteredKeys = new HashSet<string>(filtered.Select(KeyOf), StringComparer.OrdinalIgnoreCase);
        var dropped = expectedKeys.Count(k => !filteredKeys.Contains(k));

        return new FilterFamilyResult
        {
            Family = family,
            SupersetCount = superset.Count,
            ExpectedVisible = expected.Count,
            FilteredCount = filtered.Count,
            Leaked = leaked,
            Dropped = dropped
        };
    }

    /// <summary>Stable identity key for an AR object: objectGUID, else distinguishedName, else raw text.</summary>
    private static string KeyOf(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            if (item.TryGetProperty("objectGUID", out var guid) && guid.ValueKind == JsonValueKind.String)
                return guid.GetString() ?? item.GetRawText();
            if (item.TryGetProperty("distinguishedName", out var dn) && dn.ValueKind == JsonValueKind.String)
                return dn.GetString() ?? item.GetRawText();
        }
        return item.GetRawText();
    }
}
