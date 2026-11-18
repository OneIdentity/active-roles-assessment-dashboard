namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>A group of KPIs on a dashboard.</summary>
public sealed class CategoryDefinition : DefinitionBase
{
    protected override string LocalizationPrefix => "Cat_";

    /// <summary>Key of the owning dashboard (<see cref="DashboardInfo"/>; dashboards stay in code).</summary>
    [System.Text.Json.Serialization.JsonRequired, System.Text.Json.Serialization.JsonPropertyOrder(-9)]
    public string DashboardId { get; init; } = string.Empty;

    public CategoryKind Kind { get; init; } = CategoryKind.Standard;

    public DefinitionSortOrder KpiSortOrder { get; init; } = DefinitionSortOrder.Custom;

    /// <summary>Optional expression deciding whether the category is shown (e.g. a configuration flag).</summary>
    public string? EnabledWhen { get; init; }

    /// <summary>Charts rendered beneath the category's KPIs.</summary>
    public IReadOnlyList<ChartDefinition> Charts { get; init; } = [];
}
