using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>
/// Loads embedded definitions, layers writable overrides from a directory on top (items replace
/// embedded items with the same id) and validates the result. Overrides apply at startup only;
/// <see cref="HasPendingChanges"/> reports when the override files changed after loading.
/// </summary>
public sealed class DefinitionStore
{
    private const string ResourcePrefix = "ActiveRolesDashboard.Definitions.";
    private const string FileSuffix = ".definitions.json";

    private readonly string? _overrideDirectory;

    public DefinitionStore(string? overrideDirectory, ILogger<DefinitionStore>? logger = null)
        : this(LoadEmbedded(typeof(DefinitionStore).Assembly), overrideDirectory, logger)
    {
    }

    public DefinitionStore(IReadOnlyList<DefinitionSet> embedded, string? overrideDirectory, ILogger<DefinitionStore>? logger = null)
    {
        _overrideDirectory = overrideDirectory;

        var baseSet = new DefinitionSet
        {
            SchemaVersion = embedded.Select(s => s.SchemaVersion).DefaultIfEmpty(1).Max(),
            Categories = [.. embedded.SelectMany(s => s.Categories)],
            Datasets = [.. embedded.SelectMany(s => s.Datasets)],
            Kpis = [.. embedded.SelectMany(s => s.Kpis)],
        };
        var baseErrors = DefinitionValidator.Validate(baseSet);
        if (baseErrors.Count > 0)
            throw new InvalidOperationException("Embedded definitions are invalid: " + string.Join(" ", baseErrors));

        LoadedFingerprint = ComputeFingerprint(overrideDirectory);

        var errors = new List<string>();
        var overrides = LoadOverrides(overrideDirectory, errors);
        var effective = overrides.Count == 0 ? baseSet : Merge([baseSet, .. overrides]);

        if (overrides.Count > 0)
        {
            var overrideErrors = DefinitionValidator.Validate(effective);
            if (overrideErrors.Count > 0)
            {
                errors.AddRange(overrideErrors);
                effective = baseSet;
            }
            else
            {
                OverridesApplied = true;
            }
        }

        foreach (var e in errors)
            logger?.LogWarning("Definition override ignored: {Error}", e);

        OverrideErrors = errors;
        Registry = new DefinitionRegistry(effective);
    }

    public DefinitionRegistry Registry { get; }

    /// <summary>True when valid override files were merged into the effective definitions.</summary>
    public bool OverridesApplied { get; }

    /// <summary>Problems found in override files. When non-empty, the embedded definitions are in use.</summary>
    public IReadOnlyList<string> OverrideErrors { get; }

    public string LoadedFingerprint { get; }

    /// <summary>True when override files were added, changed or removed since startup.</summary>
    public bool HasPendingChanges => ComputeFingerprint(_overrideDirectory) != LoadedFingerprint;

    public static IReadOnlyList<DefinitionSet> LoadEmbedded(Assembly assembly)
    {
        var sets = new List<DefinitionSet>();
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(FileSuffix, StringComparison.Ordinal))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            sets.Add(DefinitionJson.Deserialize(reader.ReadToEnd()));
        }
        return sets;
    }

    /// <summary>Merges sets in order; later items replace earlier items with the same id.</summary>
    public static DefinitionSet Merge(IEnumerable<DefinitionSet> sets)
    {
        var categories = new OrderedById<CategoryDefinition>(c => c.Id);
        var datasets = new OrderedById<DatasetDefinition>(d => d.Id);
        var kpis = new OrderedById<KpiDefinition>(k => k.Id);
        var schemaVersion = 1;

        foreach (var set in sets)
        {
            schemaVersion = Math.Max(schemaVersion, set.SchemaVersion);
            categories.AddRange(set.Categories);
            datasets.AddRange(set.Datasets);
            kpis.AddRange(set.Kpis);
        }

        return new DefinitionSet
        {
            SchemaVersion = schemaVersion,
            Categories = categories.ToList(),
            Datasets = datasets.ToList(),
            Kpis = kpis.ToList(),
        };
    }

    private static List<DefinitionSet> LoadOverrides(string? directory, List<string> errors)
    {
        var sets = new List<DefinitionSet>();
        foreach (var file in OverrideFiles(directory))
        {
            try
            {
                sets.Add(DefinitionJson.Deserialize(File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return sets;
    }

    private static IEnumerable<string> OverrideFiles(string? directory) =>
        directory is not null && Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*" + FileSuffix).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            : [];

    private static string ComputeFingerprint(string? directory)
    {
        var sb = new StringBuilder();
        foreach (var file in OverrideFiles(directory))
        {
            var info = new FileInfo(file);
            sb.Append(info.Name).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed class OrderedById<T>(Func<T, string> id)
    {
        private readonly List<T> _items = [];
        private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);

        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                if (_index.TryGetValue(id(item), out var i))
                    _items[i] = item;
                else
                {
                    _index[id(item)] = _items.Count;
                    _items.Add(item);
                }
            }
        }

        public List<T> ToList() => [.. _items];
    }
}
