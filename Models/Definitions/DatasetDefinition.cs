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
