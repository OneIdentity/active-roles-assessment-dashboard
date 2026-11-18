namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>
/// Projects the loaded <see cref="DefinitionRegistry"/> onto the runtime <see cref="KpiInfo"/>,
/// <see cref="CategoryInfo"/> and <see cref="ChartInfo"/> lists consumed by the dashboard. Data
/// retrieval still uses the compiled-in searches for known KPIs until the generic collector
/// replaces them.
/// </summary>
public static class DefinitionCatalog
{
    public static IReadOnlyList<KpiInfo> LegacyKpis { get; } = KpiInfo.All;
    public static IReadOnlyList<CategoryInfo> LegacyCategories { get; } = CategoryInfo.All;
    public static IReadOnlyList<ChartInfo> LegacyCharts { get; } = ChartInfo.All;

    public static void Apply(DefinitionRegistry registry)
    {
        var legacyKpis = LegacyKpis.ToDictionary(k => k.Key, StringComparer.OrdinalIgnoreCase);

        CategoryInfo.Replace(InLegacyOrder(
            registry.Set.Categories.Where(c => c.Enabled).Select(ToCategory), c => c.Key, LegacyCategories.Select(c => c.Key)));

        ChartInfo.All = InLegacyOrder(registry.Set.Categories
            .Where(c => c.Enabled)
            .SelectMany(c => c.Charts.Where(ch => ch.Enabled).Select(ch => ToChart(ch, c.Id))), c => c.Key, LegacyCharts.Select(c => c.Key));

        KpiInfo.All = InLegacyOrder(registry.Set.Kpis
            .Where(k => k.Enabled)
            .Select(k => ToKpi(k, legacyKpis.GetValueOrDefault(k.Id))), k => k.Key, LegacyKpis.Select(k => k.Key));
    }

    /// <summary>
    /// Embedded files merge in file-name order; keep items that existed in the compiled-in lists
    /// in their original order (consumers iterate these lists directly) and append new items.
    /// </summary>
    private static List<T> InLegacyOrder<T>(IEnumerable<T> items, Func<T, string> key, IEnumerable<string> legacyKeys)
    {
        var index = legacyKeys.Select((k, i) => (k, i))
            .DistinctBy(x => x.k, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.k, x => x.i, StringComparer.OrdinalIgnoreCase);
        return items.OrderBy(i => index.GetValueOrDefault(key(i), int.MaxValue)).ToList();
    }

    /// <summary>Restores the compiled-in lists (used by tests).</summary>
    public static void Reset()
    {
        CategoryInfo.Replace(LegacyCategories);
        ChartInfo.All = LegacyCharts;
        KpiInfo.All = LegacyKpis;
    }

    private static CategoryInfo ToCategory(CategoryDefinition c) => new()
    {
        Key = c.Id,
        DisplayName = c.FallbackText ?? c.Id,
        DashboardKey = c.DashboardId,
        SortOrder = c.SortOrder,
        KpiSortOrder = Enum.Parse<CategorySortOrder>(c.KpiSortOrder.ToString()),
        IsOverviewCategory = c.Kind == CategoryKind.Overview,
        IsRiskCategory = c.Kind == CategoryKind.Risk,
    };

    private static ChartInfo ToChart(ChartDefinition c, string categoryId) => new()
    {
        Key = c.Id,
        Title = c.FallbackText ?? c.Id,
        CategoryKey = categoryId,
        Type = Enum.Parse<ChartType>(c.Type.ToString()),
        SortOrder = c.SortOrder,
        SourceSplitKpiKey = c.SourceSplitKpiId ?? string.Empty,
        SliceOffset = c.SliceOffset,
        DisableTypeToggle = !c.AllowTypeToggle,
        Series = c.Series
            .Where(s => s.KpiId is not null)
            .Select(s => new ChartSeriesItem { KpiKey = s.KpiId!, Label = s.Label ?? string.Empty, CssColor = s.Color ?? string.Empty })
            .ToList(),
    };

    private static KpiInfo ToKpi(KpiDefinition k, KpiInfo? legacy) => new()
    {
        Key = k.Id,
        DisplayName = k.FallbackText ?? k.Id,
        TileLabel = k.Display.TileFallbackText ?? string.Empty,
        CategoryKey = k.CategoryId,
        DataSource = k.DataSource,
        CssColor = k.Display.Color,
        SectionId = k.Display.SectionId ?? string.Empty,
        HasDrilldown = k.Drilldown.Enabled,
        SortOrder = k.SortOrder,
        IsRiskKpi = k.IsRiskKpi,
        IsSegmentable = k.IsSegmentable,
        Searches = legacy?.Searches ?? [],
    };
}
