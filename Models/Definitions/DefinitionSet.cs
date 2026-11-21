namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Root of a definition file. A store may load several files and merge them.</summary>
public sealed class DefinitionSet
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<CategoryDefinition> Categories { get; init; } = [];

    public IReadOnlyList<DatasetDefinition> Datasets { get; init; } = [];

    public IReadOnlyList<KpiDefinition> Kpis { get; init; } = [];
}
