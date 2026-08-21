using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ActiveRolesDashboard.Models;

/// <summary>
/// The persisted segment filter selection for the current session. Holds the raw
/// (possibly stale) domain/tenant names the user selected; resolution against the
/// currently-available segments happens at the filter choke points via
/// <see cref="SegmentSelection"/>. Storing the raw selection (rather than a pre-resolved
/// set) keeps it valid across data refreshes: segments that reappear are honoured again,
/// and segments that vanish fall away safely.
/// <para>
/// A <c>null</c> list means the dimension has never been set and therefore resolves to
/// all available segments (the first-run default). A non-null but empty list means the
/// user explicitly cleared the dimension and it resolves to no segments (hiding the
/// dependent tile / dashboard / export section).
/// </para>
/// </summary>
public class SegmentFilterState
{
    /// <summary>Selected AD domain NetBIOS names. Null ⇒ all domains; empty ⇒ no domains.</summary>
    public List<string>? Domains { get; set; }

    /// <summary>Selected Entra tenant names. Null ⇒ all tenants; empty ⇒ no tenants.</summary>
    public List<string>? Tenants { get; set; }

    /// <summary>The AD domain selection as a <see cref="SegmentSelection"/> (empty ⇒ none).</summary>
    public SegmentSelection DomainSelection => SegmentSelection.ExplicitOf(Domains);

    /// <summary>The Entra tenant selection as a <see cref="SegmentSelection"/> (empty ⇒ none).</summary>
    public SegmentSelection TenantSelection => SegmentSelection.ExplicitOf(Tenants);
}

/// <summary>
/// Shared session accessor for <see cref="SegmentFilterState"/> so the dashboard pages
/// and the export controller read/write the selection through one key and one
/// serialization path. The selection is stored independently of the summary caches so it
/// applies uniformly regardless of which cache the export reconstructs its data from.
/// </summary>
public static class SegmentFilterSession
{
    public const string SessionKey = "SegmentFilter";

    public static SegmentFilterState Get(ISession session)
    {
        var json = session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json))
            return new SegmentFilterState();

        try
        {
            return JsonSerializer.Deserialize<SegmentFilterState>(json) ?? new SegmentFilterState();
        }
        catch (JsonException)
        {
            return new SegmentFilterState();
        }
    }

    public static void Set(ISession session, SegmentFilterState state) =>
        session.SetString(SessionKey, JsonSerializer.Serialize(state));
}
