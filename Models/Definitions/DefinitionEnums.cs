using System.Text.Json.Serialization;

namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Logical system a KPI or dataset describes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DataSource>))]
public enum DataSource
{
    ActiveDirectory,
    ActiveRoles,
    EntraId,
    Exchange,
    Licensing
}

/// <summary>Transport used to retrieve data. Only the Active Roles REST API is supported.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RetrievalMethod>))]
public enum RetrievalMethod
{
    Rest
}

/// <summary>Whether a retrieval runs a search or reads one or more specific objects.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RetrievalTarget>))]
public enum RetrievalTarget
{
    Search,
    Objects
}

/// <summary>Role of a category on its dashboard.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CategoryKind>))]
public enum CategoryKind
{
    Standard,
    Overview,
    Risk
}

/// <summary>Ordering applied to the KPIs of a category.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DefinitionSortOrder>))]
public enum DefinitionSortOrder
{
    Custom,
    AtoZ,
    ZtoA,
    CustomThenAtoZ
}

/// <summary>How a KPI value is calculated.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EvaluationType>))]
public enum EvaluationType
{
    /// <summary>Filter expression plus aggregate over the dataset items.</summary>
    Expression,

    /// <summary>Named, registered transform function.</summary>
    Function
}

/// <summary>Aggregate applied to the items selected by an expression.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AggregateFunction>))]
public enum AggregateFunction
{
    Count,
    DistinctCount,
    Sum,
    Min,
    Max
}

/// <summary>How a composite KPI combines its source KPIs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompositeAggregate>))]
public enum CompositeAggregate
{
    Sum,
    Max,
    Min,
    First
}

/// <summary>Chart rendering type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChartKind>))]
public enum ChartKind
{
    Doughnut,
    Pie,
    Bar,
    Line,
    Ring
}

/// <summary>Severity used to rank risk KPIs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KpiSeverity>))]
public enum KpiSeverity
{
    None,
    Low,
    Medium,
    High,
    Critical
}
