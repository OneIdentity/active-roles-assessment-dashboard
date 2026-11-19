namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>
/// A named collection of objects retrieved once per superset build and shared by every KPI
/// that references it. The attributes requested are the union of <see cref="Attributes"/>
/// and the attributes declared by the referencing KPIs.
/// </summary>
public sealed class DatasetDefinition
{
    [System.Text.Json.Serialization.JsonRequired]
    public string Id { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonRequired]
    public DataSource DataSource { get; init; }

    public RetrievalDefinition Retrieval { get; init; } = new();

    public IReadOnlyList<string> Attributes { get; init; } = [];

    /// <summary>Attribute holding the segment (AD domain / Entra tenant) of each item.</summary>
    public string? SegmentAttribute { get; init; }

    /// <summary>Attribute uniquely identifying each item (defaults to objectGUID).</summary>
    public string KeyAttribute { get; init; } = "objectGUID";

    /// <summary>
    /// Directory lookups applied to each item after retrieval, in order. Each adds attributes that
    /// expressions can read. Names must be one of <see cref="EnrichmentNames.All"/>.
    /// </summary>
    public IReadOnlyList<EnrichmentDefinition> Enrichments { get; init; } = [];
}

/// <summary>A named, compiled-in lookup applied to dataset items, with string arguments.</summary>
public sealed class EnrichmentDefinition
{
    [System.Text.Json.Serialization.JsonRequired]
    public string Name { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Args { get; init; } = new Dictionary<string, string>();
}

/// <summary>The fixed set of enrichments; definitions cannot run arbitrary lookups.</summary>
public static class EnrichmentNames
{
    /// <summary>Adds <c>unresolvedGuids</c>: GUIDs in an attribute that no longer resolve. Args: <c>attribute</c>.</summary>
    public const string UnresolvedGuids = "unresolvedGuids";

    /// <summary>Adds <c>trustee</c> and <c>trusteeIsUser</c> from a SID attribute. Args: <c>attribute</c>.</summary>
    public const string Trustee = "trustee";

    /// <summary>Adds <c>isRootScope</c> for the object a GUID attribute points at. Args: <c>attribute</c>.</summary>
    public const string RootScope = "rootScope";

    /// <summary>Adds <c>objectCount</c>: objects beneath the item, excluding itself.</summary>
    public const string DescendantCount = "descendantCount";

    /// <summary>Adds <c>trustees</c>: explicit non-system grantees of a mailbox right. Args: <c>right</c> (SendAs or FullAccess).</summary>
    public const string MailboxPermissions = "mailboxPermissions";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        UnresolvedGuids, Trustee, RootScope, DescendantCount, MailboxPermissions
    };
}

/// <summary>How data is fetched from the source.</summary>
public sealed class RetrievalDefinition
{
    public RetrievalMethod Method { get; init; } = RetrievalMethod.Rest;

    public RetrievalTarget Target { get; init; } = RetrievalTarget.Search;

    /// <summary>REST endpoint relative to the API base URL.</summary>
    public string Endpoint { get; init; } = "objects";

    /// <summary>Search base. Supports tokens such as <c>{DefaultADDN}</c> and <c>{DefaultARConfigDN}</c>.</summary>
    public string? Base { get; init; }

    /// <summary>LDAP filter. Supports <c>{ConfigFilter:Name}</c> tokens.</summary>
    public string? Filter { get; init; }

    public string Scope { get; init; } = "sub";

    public int? PageSize { get; init; }

    /// <summary>Specific objects (DN, GUID or token) when <see cref="Target"/> is <see cref="RetrievalTarget.Objects"/>.</summary>
    public IReadOnlyList<string> Objects { get; init; } = [];
}
