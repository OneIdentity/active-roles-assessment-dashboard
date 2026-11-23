using System.Text.Json;
using System.Text.RegularExpressions;
using ActiveRolesDashboard.Models.Definitions;

namespace ActiveRolesDashboard.Services.Definitions;

public enum DefinitionKind
{
    Category,
    Dataset,
    Kpi
}

public enum DefinitionOrigin
{
    /// <summary>Built in (embedded); read-only.</summary>
    System,

    /// <summary>Added by an administrator; no built-in item has the same id.</summary>
    Custom,

    /// <summary>Custom item replacing a built-in item with the same id.</summary>
    Override
}

/// <summary>One row of the definition admin list.</summary>
public sealed record DefinitionEntry(
    DefinitionKind Kind,
    string Id,
    string Name,
    string Type,
    string? CategoryId,
    bool Enabled,
    DefinitionOrigin Origin,
    string? File,
    bool Editable);

/// <summary>
/// Reads and writes administrator-managed definitions in
/// <c>App_Data/Definitions/custom.definitions.json</c>. Every change is validated against the
/// built-in definitions plus all other override files before it is written; changes take effect
/// after a restart (see <see cref="DefinitionStore.HasPendingChanges"/>).
/// </summary>
public sealed partial class DefinitionAdminService(DefinitionStore store)
{
    public const string CustomFileName = "custom.definitions.json";

    private static readonly Lock WriteLock = new();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex IdPattern();

    public bool IsAvailable => store.OverrideDirectory is not null;

    private string CustomPath => Path.Combine(store.OverrideDirectory!, CustomFileName);

    /// <summary>Custom items from every override file followed by all built-in items.</summary>
    public IReadOnlyList<DefinitionEntry> List()
    {
        var entries = new List<DefinitionEntry>();
        var embedded = store.Embedded;
        foreach (var (file, set) in ReadOverrideFiles())
        {
            var editable = string.Equals(file, CustomFileName, StringComparison.OrdinalIgnoreCase);
            foreach (var c in set.Categories)
                entries.Add(ToEntry(c, Origin(embedded.Categories, c.Id, x => x.Id), file, editable));
            foreach (var d in set.Datasets)
                entries.Add(ToEntry(d, Origin(embedded.Datasets, d.Id, x => x.Id), file, editable));
            foreach (var k in set.Kpis)
                entries.Add(ToEntry(k, Origin(embedded.Kpis, k.Id, x => x.Id), file, editable));
        }

        entries.AddRange(embedded.Categories.Select(c => ToEntry(c, DefinitionOrigin.System, null, false)));
        entries.AddRange(embedded.Datasets.Select(d => ToEntry(d, DefinitionOrigin.System, null, false)));
        entries.AddRange(embedded.Kpis.Select(k => ToEntry(k, DefinitionOrigin.System, null, false)));
        return entries;
    }

    /// <summary>Ids of categories available to KPIs (built-in plus custom).</summary>
    public IReadOnlyList<string> CategoryIds() => AllIds(DefinitionKind.Category);

    /// <summary>Ids of datasets available to KPIs (built-in plus custom).</summary>
    public IReadOnlyList<string> DatasetIds() => AllIds(DefinitionKind.Dataset);

    /// <summary>Ids of KPIs available as sources (built-in plus custom).</summary>
    public IReadOnlyList<string> KpiIds() => AllIds(DefinitionKind.Kpi);

    /// <summary>
    /// Items in the effective definitions (built-in plus all override files) that reference the given
    /// custom item, as "Kind 'Id'" labels. Empty when the item can be removed. Removing a custom item
    /// that replaces a built-in one restores the built-in item, so it never has blocking dependents.
    /// </summary>
    public IReadOnlyList<string> Dependents(DefinitionKind kind, string id)
    {
        var embedded = store.Embedded;
        var restoresBuiltIn = kind switch
        {
            DefinitionKind.Category => embedded.Categories.Any(c => Same(c.Id, id)),
            DefinitionKind.Dataset => embedded.Datasets.Any(d => Same(d.Id, id)),
            _ => embedded.Kpis.Any(k => Same(k.Id, id)),
        };
        if (restoresBuiltIn) return [];

        var merged = DefinitionStore.Merge([embedded, .. ReadOverrideFiles().Select(f => f.Set)]);
        var result = new List<string>();

        void Charts(IEnumerable<ChartDefinition> charts, string owner)
        {
            foreach (var chart in charts)
            {
                var uses = kind switch
                {
                    DefinitionKind.Kpi => Same(chart.SourceSplitKpiId, id) || chart.Series.Any(s => Same(s.KpiId, id)),
                    DefinitionKind.Dataset => chart.Series.Any(s => s.KpiId is null && Same(s.DatasetId, id)),
                    _ => false
                };
                if (uses) result.Add($"Chart '{chart.Id}' on {owner}");
            }
        }

        foreach (var c in merged.Categories)
            Charts(c.Charts, $"category '{c.Id}'");

        foreach (var k in merged.Kpis)
        {
            if (kind == DefinitionKind.Kpi && Same(k.Id, id)) continue;
            var uses = kind switch
            {
                DefinitionKind.Category => Same(k.CategoryId, id),
                DefinitionKind.Dataset => k switch
                {
                    SearchKpi s => s.Retrieval is null && Same(s.DatasetId, id),
                    ObjectKpi o => o.Retrieval is null && Same(o.DatasetId, id),
                    DerivedKpi d => Same(d.SourceDatasetId, id),
                    _ => false
                },
                _ => k switch
                {
                    DerivedKpi d => Same(d.SourceKpiId, id),
                    CompositeKpi c => c.Sources.Any(s => Same(s, id)),
                    _ => false
                }
            };
            if (uses) result.Add($"KPI '{k.Id}'");
            Charts(k.Charts, $"KPI '{k.Id}'");
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<string> AllIds(DefinitionKind kind) =>
        List().Where(e => e.Kind == kind).Select(e => e.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Returns the item as indented JSON: the custom copy when <paramref name="system"/> is false,
    /// otherwise the built-in item (used to clone). Null when not found.
    /// </summary>
    public string? GetJson(DefinitionKind kind, string id, bool system)
    {
        var set = system ? store.Embedded : ReadCustom();
        object? item = kind switch
        {
            DefinitionKind.Category => set.Categories.FirstOrDefault(c => Same(c.Id, id)),
            DefinitionKind.Dataset => set.Datasets.FirstOrDefault(d => Same(d.Id, id)),
            _ => set.Kpis.FirstOrDefault(k => Same(k.Id, id)),
        };
        return item switch
        {
            null => null,
            KpiDefinition k => JsonSerializer.Serialize(k, DefinitionJson.Options),
            CategoryDefinition c => JsonSerializer.Serialize(c, DefinitionJson.Options),
            DatasetDefinition d => JsonSerializer.Serialize(d, DefinitionJson.Options),
            _ => null
        };
    }

    /// <summary>
    /// Adds or replaces a custom item. <paramref name="originalId"/> is the id being edited (null when
    /// adding); renaming removes the item stored under the old id. Returns validation errors; nothing
    /// is written when any are returned.
    /// </summary>
    public IReadOnlyList<string> Save(DefinitionKind kind, string json, string? originalId)
    {
        if (!IsAvailable) return ["Definition overrides are not configured."];

        object item;
        try
        {
            item = kind switch
            {
                DefinitionKind.Category => JsonSerializer.Deserialize<CategoryDefinition>(json, DefinitionJson.Options)!,
                DefinitionKind.Dataset => JsonSerializer.Deserialize<DatasetDefinition>(json, DefinitionJson.Options)!,
                _ => JsonSerializer.Deserialize<KpiDefinition>(json, DefinitionJson.Options)!,
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return [$"Invalid JSON: {ex.Message}"];
        }
        if (item is null) return ["Definition is empty."];

        var id = IdOf(item);
        if (!IdPattern().IsMatch(id))
            return [$"Id '{id}' must start with a letter and contain only letters, digits and underscores."];

        lock (WriteLock)
        {
            var current = ReadCustom();
            var isRename = originalId is not null && !Same(originalId, id);
            var replacing = originalId ?? id;
            if ((originalId is null || isRename) && Contains(current, kind, id))
                return [$"A custom {kind.ToString().ToLowerInvariant()} with id '{id}' already exists."];

            var updated = Replace(current, kind, replacing, item);
            return ValidateAndWrite(updated);
        }
    }

    /// <summary>Removes a custom item. Returns errors (e.g. another item still references it).</summary>
    public IReadOnlyList<string> Remove(DefinitionKind kind, string id)
    {
        if (!IsAvailable) return ["Definition overrides are not configured."];

        lock (WriteLock)
        {
            var current = ReadCustom();
            if (!Contains(current, kind, id))
                return [$"Custom {kind.ToString().ToLowerInvariant()} '{id}' was not found."];
            var dependents = Dependents(kind, id);
            if (dependents.Count > 0)
                return [$"'{id}' cannot be removed because it is used by: {string.Join(", ", dependents)}."];
            return ValidateAndWrite(Replace(current, kind, id, null));
        }
    }

    private IReadOnlyList<string> ValidateAndWrite(DefinitionSet custom)
    {
        var overrides = ReadOverrideFiles()
            .Where(f => !string.Equals(f.File, CustomFileName, StringComparison.OrdinalIgnoreCase))
            .Append((File: CustomFileName, Set: custom))
            .OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Set)
            .ToList();

        var errors = store.ValidateWith(overrides);
        if (errors.Count > 0) return errors;

        Directory.CreateDirectory(store.OverrideDirectory!);
        var temp = CustomPath + ".tmp";
        File.WriteAllText(temp, DefinitionJson.Serialize(custom));
        File.Move(temp, CustomPath, overwrite: true);
        return [];
    }

    private DefinitionSet ReadCustom()
    {
        if (!IsAvailable || !File.Exists(CustomPath)) return new DefinitionSet();
        return DefinitionStore.ReadFile(CustomPath);
    }

    /// <summary>Readable override files in load order; unreadable files are skipped (Settings reports them).</summary>
    private List<(string File, DefinitionSet Set)> ReadOverrideFiles()
    {
        var result = new List<(string, DefinitionSet)>();
        if (!IsAvailable || !Directory.Exists(store.OverrideDirectory)) return result;
        foreach (var path in Directory.GetFiles(store.OverrideDirectory!, "*.definitions.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try { result.Add((Path.GetFileName(path), DefinitionStore.ReadFile(path))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        }
        return result;
    }

    private static DefinitionSet Replace(DefinitionSet set, DefinitionKind kind, string id, object? item)
    {
        static List<T> Swap<T>(IReadOnlyList<T> list, Func<T, string> key, string id, object? item) where T : class
        {
            var result = list.ToList();
            var index = result.FindIndex(x => Same(key(x), id));
            if (index >= 0) result.RemoveAt(index);
            if (item is T t)
            {
                if (index >= 0) result.Insert(index, t);
                else result.Add(t);
            }
            return result;
        }

        return new DefinitionSet
        {
            SchemaVersion = DefinitionStore.SupportedSchemaVersion,
            Categories = kind == DefinitionKind.Category ? Swap(set.Categories, c => c.Id, id, item) : set.Categories,
            Datasets = kind == DefinitionKind.Dataset ? Swap(set.Datasets, d => d.Id, id, item) : set.Datasets,
            Kpis = kind == DefinitionKind.Kpi ? Swap(set.Kpis, k => k.Id, id, item) : set.Kpis,
        };
    }

    private static bool Contains(DefinitionSet set, DefinitionKind kind, string id) => kind switch
    {
        DefinitionKind.Category => set.Categories.Any(c => Same(c.Id, id)),
        DefinitionKind.Dataset => set.Datasets.Any(d => Same(d.Id, id)),
        _ => set.Kpis.Any(k => Same(k.Id, id)),
    };

    private static string IdOf(object item) => item switch
    {
        DefinitionBase b => b.Id,
        DatasetDefinition d => d.Id,
        _ => string.Empty
    };

    private static DefinitionOrigin Origin<T>(IEnumerable<T> embedded, string id, Func<T, string> key) =>
        embedded.Any(e => Same(key(e), id)) ? DefinitionOrigin.Override : DefinitionOrigin.Custom;

    private static DefinitionEntry ToEntry(CategoryDefinition c, DefinitionOrigin origin, string? file, bool editable) =>
        new(DefinitionKind.Category, c.Id, c.FallbackText ?? c.Id, c.Kind.ToString(), null, c.Enabled, origin, file, editable);

    private static DefinitionEntry ToEntry(DatasetDefinition d, DefinitionOrigin origin, string? file, bool editable) =>
        new(DefinitionKind.Dataset, d.Id, d.Id, d.DataSource.ToString(), null, true, origin, file, editable);

    private static DefinitionEntry ToEntry(KpiDefinition k, DefinitionOrigin origin, string? file, bool editable) =>
        new(DefinitionKind.Kpi, k.Id, k.FallbackText ?? k.Id, KindName(k), k.CategoryId, k.Enabled, origin, file, editable);

    public static string KindName(KpiDefinition k) => k switch
    {
        SearchKpi => "Search",
        ObjectKpi => "Objects",
        DerivedKpi => "Derived",
        CompositeKpi => "Composite",
        _ => k.GetType().Name
    };

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
