namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>A chart attached to a KPI or a category.</summary>
public sealed class ChartDefinition : DefinitionBase
{
    protected override string LocalizationPrefix => "Chart_";

    public ChartKind Type { get; init; } = ChartKind.Doughnut;

    /// <summary>Data points; each is either a KPI reference or a group-by over a dataset.</summary>
    public IReadOnlyList<ChartSeriesDefinition> Series { get; init; } = [];

    /// <summary>When set, the chart shows the AD/Entra (or per-tenant) split of this KPI instead of <see cref="Series"/>.</summary>
    public string? SourceSplitKpiId { get; init; }

    /// <summary>Pixels each slice is pushed out from the center.</summary>
    public int SliceOffset { get; init; }

    public bool AllowTypeToggle { get; init; } = true;

    public bool ShowLegend { get; init; } = true;

    /// <summary>Color names applied in order when series do not set their own.</summary>
    public IReadOnlyList<string> Colors { get; init; } = [];

    /// <summary>Format hint for values, e.g. <c>number</c> or <c>percent</c>.</summary>
    public string? ValueFormat { get; init; }

    /// <summary>Resource key shown when the chart has no data.</summary>
    public string? EmptyStateKey { get; init; }

    public int? MinHeight { get; init; }
}

/// <summary>
/// One data point source. Set <see cref="KpiId"/> to use a KPI value, or
/// <see cref="GroupBy"/> and <see cref="DatasetId"/> to group dataset items by an attribute.
/// </summary>
public sealed class ChartSeriesDefinition
{
    public string? KpiId { get; init; }

    public string? DatasetId { get; init; }

    public string? GroupBy { get; init; }

    /// <summary>Optional filter expression applied before grouping.</summary>
    public string? Filter { get; init; }

    /// <summary>Keep only the largest N groups; the rest are combined.</summary>
    public int? Top { get; init; }

    /// <summary>Resource key overriding the label.</summary>
    public string? LabelKey { get; init; }

    public string? Color { get; init; }
}
