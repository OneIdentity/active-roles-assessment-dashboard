namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Immutable, validated view of the effective definitions loaded at startup.</summary>
public sealed class DefinitionRegistry
{
    public DefinitionRegistry(DefinitionSet set)
    {
        Set = set;
        Categories = set.Categories.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        Datasets = set.Datasets.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        Kpis = set.Kpis.ToDictionary(k => k.Id, StringComparer.OrdinalIgnoreCase);
    }

    public DefinitionSet Set { get; }

    public IReadOnlyDictionary<string, CategoryDefinition> Categories { get; }

    public IReadOnlyDictionary<string, DatasetDefinition> Datasets { get; }

    public IReadOnlyDictionary<string, KpiDefinition> Kpis { get; }

    public IEnumerable<CategoryDefinition> CategoriesFor(string dashboardId) =>
        Set.Categories.Where(c => c.Enabled && string.Equals(c.DashboardId, dashboardId, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<KpiDefinition> KpisFor(string categoryId) =>
        Set.Kpis.Where(k => k.Enabled && string.Equals(k.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase));
}
