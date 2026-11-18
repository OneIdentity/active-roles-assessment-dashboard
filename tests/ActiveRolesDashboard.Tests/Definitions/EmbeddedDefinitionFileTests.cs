using System.Reflection;
using ActiveRolesDashboard.Models.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public class EmbeddedDefinitionFileTests
{
    private const string ResourcePrefix = "ActiveRolesDashboard.Definitions.";
    private const string ResourceSuffix = ".definitions.json";

    private static readonly Assembly AppAssembly = typeof(DefinitionSet).Assembly;

    /// <summary>
    /// Regenerates <c>Definitions/*.definitions.json</c> from the compiled-in instances.
    /// Runs only when <c>AR_REGENERATE_DEFINITIONS=1</c>; otherwise it is a no-op.
    /// </summary>
    [Fact]
    public void Regenerate_definition_files_when_requested()
    {
        if (Environment.GetEnvironmentVariable("AR_REGENERATE_DEFINITIONS") != "1")
        {
            return;
        }

        var folder = Path.Combine(FindRepoRoot(), "Definitions");
        Directory.CreateDirectory(folder);
        foreach (var (dashboard, set) in LegacyDefinitionExporter.ExportByDashboard())
        {
            File.WriteAllText(Path.Combine(folder, dashboard + ResourceSuffix), DefinitionJson.Serialize(set) + Environment.NewLine);
        }
    }

    [Fact]
    public void Embedded_files_match_compiled_in_definitions()
    {
        var expected = LegacyDefinitionExporter.ExportByDashboard();
        var embedded = LoadEmbedded();

        Assert.Equal(expected.Keys.Order(StringComparer.OrdinalIgnoreCase), embedded.Keys.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var (dashboard, set) in expected)
        {
            Assert.True(
                DefinitionJson.Serialize(set) == DefinitionJson.Serialize(embedded[dashboard]),
                $"Definitions/{dashboard}{ResourceSuffix} is out of date. Regenerate with AR_REGENERATE_DEFINITIONS=1.");
        }
    }

    [Fact]
    public void Embedded_files_contain_every_kpi_category_and_chart()
    {
        var sets = LoadEmbedded().Values.ToList();
        var kpis = sets.SelectMany(s => s.Kpis).ToList();
        var categories = sets.SelectMany(s => s.Categories).ToList();

        Assert.Equal(Models.KpiInfo.All.Count, kpis.Count);
        Assert.Equal(Models.CategoryInfo.All.Count, categories.Count);
        Assert.Equal(Models.ChartInfo.All.Count, categories.SelectMany(c => c.Charts).Count());
        Assert.Equal(kpis.Count, kpis.Select(k => k.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var datasetIds = sets.SelectMany(s => s.Datasets).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(kpis.OfType<SearchKpi>(), k => Assert.Contains(k.DatasetId, datasetIds));
        Assert.All(kpis.OfType<ObjectKpi>(), k => Assert.Contains(k.DatasetId, datasetIds));
    }

    [Fact]
    public void Every_static_kpi_is_listed_and_records_its_data_source()
    {
        var declared = typeof(Models.KpiInfo)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Models.KpiInfo))
            .Select(f => ((Models.KpiInfo)f.GetValue(null)!).Key)
            .ToList();

        Assert.Equal(declared.Order(), Models.KpiInfo.All.Select(k => k.Key).Order());

        // DataSource is recorded on every initializer: the file text must name it once per KPI.
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Models", "DashboardModels.cs"));
        var missing = declared.Where(key => !System.Text.RegularExpressions.Regex.IsMatch(
            source, $@"Key = ""{key}"",[^\r\n]*DataSource = Definitions\.DataSource\.\w+")).ToList();
        Assert.Empty(missing);
    }

    private static Dictionary<string, DefinitionSet> LoadEmbedded()
    {
        var result = new Dictionary<string, DefinitionSet>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in AppAssembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(ResourceSuffix, StringComparison.Ordinal)))
        {
            using var stream = AppAssembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            result[name[ResourcePrefix.Length..^ResourceSuffix.Length]] = DefinitionJson.Deserialize(reader.ReadToEnd());
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ActiveRolesDashboard.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
