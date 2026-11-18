using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

[Collection(DefinitionCatalogCollection.Name)]
public sealed class DefinitionCatalogTests
{
    [Fact]
    public void Embedded_definitions_project_to_the_compiled_in_lists()
    {
        var legacyKpis = KpiInfo.All.ToList();
        var legacyCategories = CategoryInfo.All.ToList();
        var legacyCharts = ChartInfo.All.ToList();
        var registry = new DefinitionStore(DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly), null).Registry;

        try
        {
            DefinitionCatalog.Apply(registry);

            Assert.Equal(Describe(legacyCategories), Describe(CategoryInfo.All));
            Assert.Equal(Describe(legacyCharts), Describe(ChartInfo.All));
            Assert.Equal(Describe(legacyKpis), Describe(KpiInfo.All));

            foreach (var dashboard in DashboardInfo.All)
            {
                var categories = CategoryInfo.ForDashboard(dashboard);
                Assert.NotEmpty(categories);
                foreach (var category in categories)
                    Assert.Equal(KpiInfo.ForCategory(category).Count, legacyKpis.Count(k => k.CategoryKey == category.Key)
                        + (category.IsRiskCategory ? KpiInfo.ForCategory(category).Count(k => k.CategoryKey != category.Key) : 0));
            }
        }
        finally
        {
            DefinitionCatalog.Reset();
        }
    }

    [Fact]
    public void Disabled_definitions_are_removed()
    {
        var embedded = DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly);
        var target = embedded.SelectMany(s => s.Kpis).First(k => !embedded.SelectMany(s => s.Categories).SelectMany(c => c.Charts).SelectMany(c => c.Series).Any(s => s.KpiId == k.Id));
        var disabled = new CompositeKpi { Id = target.Id, CategoryId = target.CategoryId, DataSource = target.DataSource, Enabled = false, Sources = [target.Id] };
        var set = DefinitionStore.Merge([.. embedded, new DefinitionSet { Kpis = [disabled] }]);

        try
        {
            DefinitionCatalog.Apply(new DefinitionRegistry(set));

            Assert.DoesNotContain(KpiInfo.All, k => k.Key == target.Id);
        }
        finally
        {
            DefinitionCatalog.Reset();
        }
    }

    private static List<string> Describe(IEnumerable<CategoryInfo> items) =>
        items.Select(c => $"{c.Key}|{c.DisplayName}|{c.DashboardKey}|{c.SortOrder}|{c.KpiSortOrder}|{c.IsOverviewCategory}|{c.IsRiskCategory}").ToList();

    private static List<string> Describe(IEnumerable<ChartInfo> items) =>
        items.OrderBy(c => c.Key).Select(c => $"{c.Key}|{c.Title}|{c.CategoryKey}|{c.Type}|{c.SortOrder}|{c.SourceSplitKpiKey}|{c.SliceOffset}|{c.DisableTypeToggle}|"
            + string.Join(",", c.Series.Select(s => $"{s.KpiKey}/{s.Label}/{s.CssColor}"))).ToList();

    private static List<string> Describe(IEnumerable<KpiInfo> items) =>
        items.Select(k => $"{k.Key}|{k.DisplayName}|{k.Label}|{k.CategoryKey}|{k.DataSource}|{k.CssColor}|{k.SectionId}|{k.HasDrilldown}|{k.SortOrder}|{k.IsRiskKpi}|{k.IsSegmentable}|{k.Searches.Count}").ToList();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DefinitionCatalogCollection
{
    public const string Name = "DefinitionCatalog";
}
