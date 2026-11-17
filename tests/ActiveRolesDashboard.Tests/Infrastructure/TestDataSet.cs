using System.Text.Json.Nodes;

namespace ActiveRolesDashboard.Tests.Infrastructure;

/// <summary>
/// A prepared Active Roles data set loaded from TestData/&lt;name&gt;.json.
/// Each search entry maps a filter fragment to the objects the fake server returns for it.
/// </summary>
public sealed class TestDataSet
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<SearchEntry> Searches { get; init; } = [];

    public sealed class SearchEntry
    {
        public string FilterContains { get; init; } = string.Empty;
        public int PageSize { get; init; }
        public List<JsonNode> Items { get; init; } = [];
    }

    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "TestData");

    public static TestDataSet Load(string name)
    {
        var path = Path.Combine(DataDirectory, $"{name}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Test data set '{name}' not found.", path);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return new TestDataSet
        {
            Name = name,
            Description = root["description"]?.GetValue<string>() ?? string.Empty,
            Searches = (root["searches"]?.AsArray() ?? [])
                .Select(s => new SearchEntry
                {
                    FilterContains = s!["filterContains"]!.GetValue<string>(),
                    PageSize = s["pageSize"]?.GetValue<int>() ?? 0,
                    Items = (s["items"]?.AsArray() ?? []).Select(i => i!.DeepClone()).ToList()
                })
                .ToList()
        };
    }

    public static string ReadText(string relativePath)
        => File.ReadAllText(Path.Combine(DataDirectory, relativePath));
}
