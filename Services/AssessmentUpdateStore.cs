using System.Reflection;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.AssessmentUpdates;

namespace ActiveRolesDashboard.Services;

/// <summary>Status of an installed update pack after evaluation against the current rule set.</summary>
public enum AssessmentUpdatePackStatus
{
    /// <summary>Enabled, valid and applied.</summary>
    Active,

    /// <summary>Disabled by an administrator; not applied.</summary>
    Disabled,

    /// <summary>Enabled but failed integrity or validation; not applied.</summary>
    Invalid
}

/// <summary>Persisted per-pack state (state.json).</summary>
public sealed class AssessmentUpdatePackState
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Sequence { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime ImportedUtc { get; set; }
    public string? ImportedBy { get; set; }
    public DateTime? ChangedUtc { get; set; }
    public string? ChangedBy { get; set; }
}

/// <summary>An installed pack with its evaluated status, for administration and auditing.</summary>
public sealed class AssessmentUpdatePackInfo
{
    public required AssessmentUpdatePackState State { get; init; }
    public AssessmentUpdatePack? Pack { get; init; }
    public AssessmentUpdatePackStatus Status { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>The effective rule set produced by applying active packs over the built-in rules.</summary>
public sealed class AssessmentRuleSet
{
    public required IReadOnlyList<AssessmentRule> Rules { get; init; }
    public required IReadOnlyList<AssessmentUpdatePackInfo> Packs { get; init; }

    /// <summary>Per-rule localized text overrides: ruleId -&gt; culture -&gt; text.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, AssessmentRuleText>> LocalizedText { get; init; }

    /// <summary>SHA-256 over the effective rules; identical for identical rule content.</summary>
    public required string Fingerprint { get; init; }

    public IEnumerable<AssessmentUpdatePackInfo> ActivePacks => Packs.Where(p => p.Status == AssessmentUpdatePackStatus.Active);
}

/// <summary>Builds an effective rule set by validating and applying packs in order.</summary>
public static class AssessmentRuleSetBuilder
{
    public static Version AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);

    public static IReadOnlySet<string> KnownKpiKeys() =>
        KpiInfo.All.Select(k => k.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Applies the given packs (already ordered) to <paramref name="builtIn"/>. Each enabled pack is
    /// validated against the rules produced by the packs before it; an invalid pack is skipped whole.
    /// </summary>
    public static AssessmentRuleSet Build(
        IReadOnlyList<AssessmentRule> builtIn,
        IEnumerable<(AssessmentUpdatePackState State, string? Json)> packs,
        Version? appVersion = null)
    {
        var version = appVersion ?? AppVersion;
        var kpis = KnownKpiKeys();
        var rules = builtIn.ToList();
        var text = new Dictionary<string, Dictionary<string, AssessmentRuleText>>(StringComparer.OrdinalIgnoreCase);
        var infos = new List<AssessmentUpdatePackInfo>();

        foreach (var (state, json) in packs)
        {
            if (json == null)
            {
                infos.Add(new() { State = state, Status = AssessmentUpdatePackStatus.Invalid, Errors = new[] { "Pack file is missing." } });
                continue;
            }

            var result = AssessmentUpdateValidator.Validate(json, rules, kpis, version);
            if (result.IsValid && !string.Equals(result.ComputedSha256, state.Sha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("Pack file changed since import (SHA-256 differs from the recorded hash).");

            if (!state.Enabled)
            {
                infos.Add(new() { State = state, Pack = result.Pack, Status = AssessmentUpdatePackStatus.Disabled, Errors = result.Errors });
                continue;
            }
            if (!result.IsValid)
            {
                infos.Add(new() { State = state, Pack = result.Pack, Status = AssessmentUpdatePackStatus.Invalid, Errors = result.Errors });
                continue;
            }

            rules = Apply(rules, result.Pack!, text);
            infos.Add(new() { State = state, Pack = result.Pack, Status = AssessmentUpdatePackStatus.Active });
        }

        return new AssessmentRuleSet
        {
            Rules = rules,
            Packs = infos,
            LocalizedText = text.ToDictionary(k => k.Key,
                v => (IReadOnlyDictionary<string, AssessmentRuleText>)v.Value, StringComparer.OrdinalIgnoreCase),
            Fingerprint = Fingerprint(rules)
        };
    }

    private static List<AssessmentRule> Apply(List<AssessmentRule> rules, AssessmentUpdatePack pack,
        Dictionary<string, Dictionary<string, AssessmentRuleText>> text)
    {
        var result = new List<AssessmentRule>(rules);
        foreach (var op in pack.Operations)
        {
            var index = result.FindIndex(r => string.Equals(r.Id, op.RuleId, StringComparison.OrdinalIgnoreCase));
            switch (op.Op)
            {
                case AssessmentUpdateOperationKind.Modify:
                    result[index] = AssessmentRuleMerger.Apply(result[index], op.Rule!);
                    break;
                case AssessmentUpdateOperationKind.Add:
                    result.Add(AssessmentRuleMerger.Apply(new AssessmentRule { Id = op.RuleId }, op.Rule!));
                    break;
                case AssessmentUpdateOperationKind.Retire:
                    if (op.Types is not { Count: > 0 })
                    {
                        result.RemoveAt(index);
                        text.Remove(op.RuleId);
                    }
                    else
                    {
                        var remaining = result[index].Types.Except(op.Types).ToArray();
                        if (remaining.Length == 0)
                            result.RemoveAt(index);
                        else
                            result[index] = AssessmentRuleMerger.Apply(result[index], new AssessmentRulePatch { Types = remaining.ToList() });
                    }
                    break;
            }

            if (op.Rule?.Localized is { Count: > 0 } localized && op.Op != AssessmentUpdateOperationKind.Retire)
            {
                if (!text.TryGetValue(op.RuleId, out var byCulture))
                    text[op.RuleId] = byCulture = new(StringComparer.OrdinalIgnoreCase);
                foreach (var (culture, value) in localized)
                    byCulture[culture] = value;
            }
        }
        return result;
    }

    private static string Fingerprint(IEnumerable<AssessmentRule> rules) =>
        AssessmentUpdateHasher.HashOf(rules
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => string.Join("|", r.Id, r.Title, r.CategoryName, r.KpiKey, r.Severity, r.Comparison,
                r.WarnThreshold, r.FailThreshold, string.Join(",", r.Types.OrderBy(t => t)), r.Recommendation)));
}

/// <summary>
/// Stores assessment update packs under <c>App_Data/AssessmentUpdates</c>: one JSON file per pack
/// plus <c>state.json</c> recording enabled flags, order and the SHA-256 verified at import.
/// Packs are never edited in place; rollback is disable, remove or revert-all.
/// </summary>
public sealed class AssessmentUpdateStore
{
    private const string StateFile = "state.json";
    private static readonly JsonSerializerOptions StateJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _folder;
    private readonly object _lock = new();

    /// <summary>Raised after any change so cached rule sets can be rebuilt.</summary>
    public event Action? Changed;

    public AssessmentUpdateStore(IWebHostEnvironment env)
        : this(Path.Combine(env.ContentRootPath, "App_Data", "AssessmentUpdates")) { }

    public AssessmentUpdateStore(string folder) => _folder = folder;

    public string Folder => _folder;

    /// <summary>Builds the effective rule set from the built-in rules and all installed packs.</summary>
    public AssessmentRuleSet BuildRuleSet(IReadOnlyList<AssessmentRule>? builtIn = null)
    {
        lock (_lock)
            return AssessmentRuleSetBuilder.Build(builtIn ?? AssessmentRuleLibrary.All, LoadPacks());
    }

    /// <summary>
    /// Validates a pack against the current effective rules and, when <paramref name="commit"/> is
    /// true and it is valid, installs it (enabled) as the last pack. A pack with the same id is
    /// replaced only when the new version is higher.
    /// </summary>
    public AssessmentUpdateValidationResult Import(string json, string? user, bool commit)
    {
        AssessmentUpdateValidationResult result;
        lock (_lock)
        {
            var states = LoadState();
            var probe = AssessmentUpdateValidator.Validate(json, Array.Empty<AssessmentRule>(), new HashSet<string>(), AssessmentRuleSetBuilder.AppVersion);
            var existing = probe.Pack == null ? null
                : states.FirstOrDefault(s => string.Equals(s.Id, probe.Pack.Id, StringComparison.OrdinalIgnoreCase));

            // Validate against the rules as they would be without the pack being replaced.
            var others = states.Where(s => s != existing).OrderBy(s => s.Sequence)
                .Select(s => (s, ReadPack(s.FileName)));
            var current = AssessmentRuleSetBuilder.Build(AssessmentRuleLibrary.All, others).Rules;
            result = AssessmentUpdateValidator.Validate(json, current, AssessmentRuleSetBuilder.KnownKpiKeys(), AssessmentRuleSetBuilder.AppVersion);

            if (result.IsValid && existing != null && probe.Pack != null)
            {
                var installed = ReadPack(existing.FileName);
                var installedVersion = installed == null ? null
                    : JsonSerializer.Deserialize<AssessmentUpdatePack>(installed, AssessmentUpdatePack.JsonOptions)?.Version;
                if (Version.TryParse(installedVersion, out var iv) && Version.TryParse(result.Pack!.Version, out var nv) && nv <= iv)
                    result.Errors.Add($"Version {installedVersion} of this pack is already installed; import a higher version.");
            }

            if (!commit || !result.IsValid)
                return result;

            Directory.CreateDirectory(_folder);
            var pack = result.Pack!;
            var fileName = $"{pack.Id}_{pack.Version}.json";
            File.WriteAllText(Path.Combine(_folder, fileName), json);

            if (existing != null)
            {
                if (!string.Equals(existing.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    TryDelete(existing.FileName);
                states.Remove(existing);
            }
            states.Add(new AssessmentUpdatePackState
            {
                Id = pack.Id,
                FileName = fileName,
                Enabled = true,
                Sequence = existing?.Sequence ?? (states.Count == 0 ? 1 : states.Max(s => s.Sequence) + 1),
                Sha256 = result.ComputedSha256!,
                ImportedUtc = DateTime.UtcNow,
                ImportedBy = user
            });
            SaveState(states);
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Enables or disables
    public bool SetEnabled(string id, bool enabled, string? user)
    {
        lock (_lock)
        {
            var states = LoadState();
            var state = states.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (state == null)
                return false;
            state.Enabled = enabled;
            state.ChangedUtc = DateTime.UtcNow;
            state.ChangedBy = user;
            SaveState(states);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes an installed pack and its file. Returns false when not found.</summary>
    public bool Remove(string id)
    {
        lock (_lock)
        {
            var states = LoadState();
            var state = states.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (state == null)
                return false;
            states.Remove(state);
            TryDelete(state.FileName);
            SaveState(states);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Disables every pack, restoring the built-in rules without deleting anything.</summary>
    public void RevertAll(string? user)
    {
        lock (_lock)
        {
            var states = LoadState();
            foreach (var s in states.Where(s => s.Enabled))
            {
                s.Enabled = false;
                s.ChangedUtc = DateTime.UtcNow;
                s.ChangedBy = user;
            }
            SaveState(states);
        }
        Changed?.Invoke();
    }

    private IEnumerable<(AssessmentUpdatePackState, string?)> LoadPacks() =>
        LoadState().OrderBy(s => s.Sequence).ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => (s, ReadPack(s.FileName)))
            .ToList();

    private string? ReadPack(string fileName)
    {
        var path = SafePath(fileName);
        return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private string? SafePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            return null;
        return Path.Combine(_folder, fileName);
    }

    private void TryDelete(string fileName)
    {
        var path = SafePath(fileName);
        if (path != null && File.Exists(path))
            File.Delete(path);
    }

    private List<AssessmentUpdatePackState> LoadState()
    {
        var path = Path.Combine(_folder, StateFile);
        if (!File.Exists(path))
            return new();
        try
        {
            return JsonSerializer.Deserialize<List<AssessmentUpdatePackState>>(File.ReadAllText(path), StateJson) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private void SaveState(List<AssessmentUpdatePackState> states)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, StateFile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(states.OrderBy(s => s.Sequence), StateJson));
        File.Move(temp, path, overwrite: true);
    }
}
