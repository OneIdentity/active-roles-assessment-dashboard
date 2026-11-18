using System.Reflection;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;

namespace ActiveRolesDashboard.Tests.Definitions;

/// <summary>
/// Builds definition sets from the compiled-in KPI, category and chart instances. Used to
/// generate the embedded <c>Definitions/*.json</c> files and to detect drift between them and
/// the code while both exist. Evaluation is recorded as the <c>Legacy</c> transform function
/// until the generic evaluator replaces the hand-written logic.
/// </summary>
internal static class LegacyDefinitionExporter
{
    public const string LegacyFunction = "Legacy";

    public static IReadOnlyDictionary<string, DefinitionSet> ExportByDashboard()
    {
        var categoryDashboard = CategoryInfo.All.ToDictionary(c => c.Key, c => c.DashboardKey, StringComparer.OrdinalIgnoreCase);

        var datasets = new Dictionary<string, (string Dashboard, DatasetDefinition Dataset)>(StringComparer.OrdinalIgnoreCase);
        var kpisByDashboard = new Dictionary<string, List<KpiDefinition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kpi in KpiInfo.All)
        {
            var dashboard = categoryDashboard.GetValueOrDefault(kpi.CategoryKey, "Main");
            var definition = ToKpi(kpi, dashboard, out var kpiDatasets);
            foreach (var ds in kpiDatasets)
            {
                datasets.TryAdd(ds.Id, (dashboard, ds));
            }

            Bucket(kpisByDashboard, dashboard).Add(definition);
        }

        var result = new Dictionary<string, DefinitionSet>(StringComparer.OrdinalIgnoreCase);
        var dashboards = CategoryInfo.All.Select(c => c.DashboardKey)
            .Concat(kpisByDashboard.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dashboard in dashboards)
        {
            result[dashboard] = new DefinitionSet
            {
                Categories = CategoryInfo.All
                    .Where(c => string.Equals(c.DashboardKey, dashboard, StringComparison.OrdinalIgnoreCase))
                    .Select(ToCategory)
                    .ToList(),
                Datasets = datasets.Values
                    .Where(d => string.Equals(d.Dashboard, dashboard, StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.Dataset)
                    .ToList(),
                Kpis = kpisByDashboard.GetValueOrDefault(dashboard) ?? []
            };
        }

        return result;
    }

    private static List<T> Bucket<T>(Dictionary<string, List<T>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        return list;
    }

    private static CategoryDefinition ToCategory(CategoryInfo c) => new()
    {
        Id = c.Key,
        FallbackText = Literal(c, "_displayName"),
        DashboardId = c.DashboardKey,
        SortOrder = c.SortOrder,
        Kind = c.IsOverviewCategory ? CategoryKind.Overview : c.IsRiskCategory ? CategoryKind.Risk : CategoryKind.Standard,
        KpiSortOrder = Enum.Parse<DefinitionSortOrder>(c.KpiSortOrder.ToString()),
        Charts = ChartInfo.ForCategory(c.Key).Select(ToChart).ToList()
    };

    private static ChartDefinition ToChart(ChartInfo c) => new()
    {
        Id = c.Key,
        FallbackText = Literal(c, "_title"),
        SortOrder = c.SortOrder,
        Type = Enum.Parse<ChartKind>(c.Type.ToString()),
        SourceSplitKpiId = string.IsNullOrEmpty(c.SourceSplitKpiKey) ? null : c.SourceSplitKpiKey,
        SliceOffset = c.SliceOffset,
        AllowTypeToggle = !c.DisableTypeToggle,
        Series = c.Series.Select(s => new ChartSeriesDefinition
        {
            KpiId = s.KpiKey,
            Label = string.IsNullOrEmpty(s.Label) ? null : s.Label,
            Color = string.IsNullOrEmpty(s.CssColor) ? null : s.CssColor
        }).ToList()
    };

    private static KpiDefinition ToKpi(KpiInfo k, string dashboard, out List<DatasetDefinition> datasets)
    {
        datasets = [];
        var source = k.DataSource;
        var display = new KpiDisplay
        {
            Color = k.CssColor,
            SectionId = string.IsNullOrEmpty(k.SectionId) ? null : k.SectionId,
            TileFallbackText = NullIfEmpty(Literal(k, "_tileLabel"))
        };
        var legacy = new EvaluationDefinition { Type = EvaluationType.Function, Function = LegacyFunction };

        if (k.Searches.Count == 0)
        {
            return new DerivedKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = new DrilldownDefinition { Enabled = k.HasDrilldown }, Evaluation = legacy
            };
        }

        // Every compiled-in KPI has at most one search; extra searches would need composite KPIs.
        var search = k.Searches.Single();
        var datasetId = search.SharedSearchKey ?? k.Key;
        var isObject = !string.IsNullOrEmpty(search.GroupName);
        datasets.Add(new DatasetDefinition
        {
            Id = datasetId,
            DataSource = source,
            Retrieval = new RetrievalDefinition
            {
                Target = isObject ? RetrievalTarget.Objects : RetrievalTarget.Search,
                Base = search.BaseDn,
                Filter = search.Filter,
                Scope = search.Scope,
                Objects = isObject ? [search.GroupName!] : []
            },
            Attributes = search.Attributes.StartsWith('{')
                ? [search.Attributes]
                : search.Attributes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        });

        return isObject
            ? new ObjectKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = new DrilldownDefinition { Enabled = k.HasDrilldown }, DatasetId = datasetId, Evaluation = legacy
            }
            : new SearchKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = new DrilldownDefinition { Enabled = k.HasDrilldown }, DatasetId = datasetId, Evaluation = legacy
            };
    }

    private static string Literal(object instance, string field) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as string ?? string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
