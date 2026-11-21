using System.Text.Json.Serialization;

namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>
/// Base class for KPI definitions. The <c>kind</c> discriminator selects the child type,
/// which determines how the KPI's data is obtained.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SearchKpi), "Search")]
[JsonDerivedType(typeof(ObjectKpi), "Objects")]
[JsonDerivedType(typeof(DerivedKpi), "Derived")]
[JsonDerivedType(typeof(CompositeKpi), "Composite")]
public abstract class KpiDefinition : DefinitionBase
{
    protected override string LocalizationPrefix => "Kpi_";

    [JsonRequired, JsonPropertyOrder(-9)]
    public string CategoryId { get; init; } = string.Empty;

    [JsonRequired, JsonPropertyOrder(-8)]
    public DataSource DataSource { get; init; }

    public KpiDisplay Display { get; init; } = new();

    /// <summary>Also shown in the dashboard's Governance and Risk category.</summary>
    public bool IsRiskKpi { get; init; }

    public KpiSeverity Severity { get; init; } = KpiSeverity.None;

    /// <summary>Value can be broken down by AD domain / Entra tenant.</summary>
    public bool IsSegmentable { get; init; }

    /// <summary>Value is only accurate once Entra group membership has been lazily loaded.</summary>
    public bool RequiresEntraMembership { get; init; }

    /// <summary>Attributes this KPI needs from its dataset (merged into the dataset request).</summary>
    public IReadOnlyList<string> Attributes { get; init; } = [];

    public DrilldownDefinition Drilldown { get; init; } = new();

    public bool HasCharts => Charts.Count > 0;

    public IReadOnlyList<ChartDefinition> Charts { get; init; } = [];

    /// <summary>Key used for threshold overrides. Defaults to <see cref="DefinitionBase.Id"/>.</summary>
    public string? ThresholdKey { get; init; }

    /// <summary>Key used for exclusion overrides. Defaults to <see cref="DefinitionBase.Id"/>.</summary>
    public string? ExclusionKey { get; init; }

    /// <summary>True when the value is read from the source; false when derived from collected data.</summary>
    [JsonIgnore]
    public abstract bool RetrievesFromSource { get; }
}

/// <summary>Visual properties of a KPI tile.</summary>
public sealed class KpiDisplay
{
    public string Color { get; init; } = string.Empty;

    public string? SectionId { get; init; }

    /// <summary>Resource key for the short tile label. Defaults to <c>KpiTile_{Id}</c>.</summary>
    public string? TileLabelKey { get; init; }

    /// <summary>Tile label used when the resource key is not found.</summary>
    public string? TileFallbackText { get; init; }
}

/// <summary>KPI calculated from a search against the source, optionally shared through a dataset.</summary>
public sealed class SearchKpi : KpiDefinition
{
    /// <summary>Dataset holding the search results. Shared when several KPIs use the same id.</summary>
    public string DatasetId { get; init; } = string.Empty;

    /// <summary>Inline retrieval used when the dataset is private to this KPI.</summary>
    public RetrievalDefinition? Retrieval { get; init; }

    public EvaluationDefinition Evaluation { get; init; } = new();

    public override bool RetrievesFromSource => true;
}

/// <summary>KPI calculated from one or more specific objects (e.g. managed object statistics).</summary>
public sealed class ObjectKpi : KpiDefinition
{
    public string DatasetId { get; init; } = string.Empty;

    public RetrievalDefinition? Retrieval { get; init; }

    public EvaluationDefinition Evaluation { get; init; } = new();

    public override bool RetrievesFromSource => true;
}

/// <summary>KPI calculated from already-collected data or another KPI's summary numbers.</summary>
public sealed class DerivedKpi : KpiDefinition
{
    /// <summary>Dataset to evaluate. Exactly one of this or <see cref="SourceKpiId"/> is set.</summary>
    public string? SourceDatasetId { get; init; }

    /// <summary>KPI whose result is reused.</summary>
    public string? SourceKpiId { get; init; }

    public EvaluationDefinition Evaluation { get; init; } = new();

    public override bool RetrievesFromSource => false;
}

/// <summary>KPI combining the results of other KPIs (e.g. AD plus Entra totals).</summary>
public sealed class CompositeKpi : KpiDefinition
{
    public IReadOnlyList<string> Sources { get; init; } = [];

    public CompositeAggregate Aggregate { get; init; } = CompositeAggregate.Sum;

    public override bool RetrievesFromSource => false;
}
