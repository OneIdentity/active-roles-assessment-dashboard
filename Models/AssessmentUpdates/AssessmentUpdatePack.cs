using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ActiveRolesDashboard.Models.AssessmentUpdates;

/// <summary>Kind of change an update operation makes to the assessment rule set.</summary>
public enum AssessmentUpdateOperationKind
{
    /// <summary>Changes selected fields of an existing rule; unspecified fields keep their current value.</summary>
    Modify,

    /// <summary>Removes a rule from all frameworks, or only from the listed <c>types</c>.</summary>
    Retire,

    /// <summary>Adds a new rule against an existing KPI.</summary>
    Add
}

/// <summary>
/// Field-level changes to a rule. Every property is optional; only non-null values are applied,
/// so future built-in changes to unspecified fields remain visible.
/// </summary>
public sealed class AssessmentRulePatch
{
    public string? Title { get; set; }
    public string? CategoryName { get; set; }
    public string? KpiKey { get; set; }
    public AssessmentSeverity? Severity { get; set; }
    public List<AssessmentType>? Types { get; set; }
    public AssessmentComparison? Comparison { get; set; }
    public int? WarnThreshold { get; set; }
    public int? FailThreshold { get; set; }
    public string? Recommendation { get; set; }

    /// <summary>Optional per-culture text overrides keyed by culture name (e.g. "de"), taking precedence over resources.</summary>
    public Dictionary<string, AssessmentRuleText>? Localized { get; set; }
}

/// <summary>Localized title/recommendation override for one culture.</summary>
public sealed class AssessmentRuleText
{
    public string? Title { get; set; }
    public string? Recommendation { get; set; }
}

/// <summary>A single change in an update pack.</summary>
public sealed class AssessmentUpdateOperation
{
    public AssessmentUpdateOperationKind Op { get; set; }
    public string RuleId { get; set; } = string.Empty;

    /// <summary>For <see cref="AssessmentUpdateOperationKind.Retire"/>: frameworks to retire from (null/empty = all).</summary>
    public List<AssessmentType>? Types { get; set; }

    /// <summary>For Modify/Add: the fields to set. Add requires every mandatory field.</summary>
    public AssessmentRulePatch? Rule { get; set; }

    /// <summary>Optional human-readable rationale shown to administrators.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// A versioned, SHA-256-protected set of changes to the built-in assessment rules. The
/// <see cref="Sha256"/> value is the hash of the canonical JSON of the pack with the
/// <c>sha256</c> property removed (see <see cref="AssessmentUpdateHasher"/>).
/// </summary>
public sealed class AssessmentUpdatePack
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public DateTime? PublishedUtc { get; set; }
    public string? MinAppVersion { get; set; }
    public string? MaxAppVersion { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public List<AssessmentUpdateOperation> Operations { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
}

/// <summary>
/// Computes and verifies the SHA-256 integrity hash of an update pack. The hash covers a
/// canonical form of the JSON (object keys sorted ordinally, no insignificant whitespace,
/// top-level <c>sha256</c> property removed) so that formatting changes do not alter it but
/// any change to content does.
/// </summary>
public static class AssessmentUpdateHasher
{
    public const string HashProperty = "sha256";

    /// <summary>Computes the lowercase hex SHA-256 of the canonical pack JSON (excluding the hash property).</summary>
    public static string Compute(string packJson)
    {
        var node = JsonNode.Parse(packJson, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow })
            as JsonObject ?? throw new JsonException("Update pack must be a JSON object.");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(node))));
    }

    /// <summary>True when <paramref name="declaredSha256"/> matches the computed hash (constant-time comparison).</summary>
    public static bool Verify(string packJson, string? declaredSha256)
    {
        if (string.IsNullOrWhiteSpace(declaredSha256) || declaredSha256.Length != 64)
            return false;
        var actual = Encoding.ASCII.GetBytes(Compute(packJson));
        var expected = Encoding.ASCII.GetBytes(declaredSha256.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Returns the pack JSON with its <c>sha256</c> property set to the computed hash (authoring helper).</summary>
    public static string Seal(string packJson)
    {
        var node = JsonNode.Parse(packJson)!.AsObject();
        RemoveHash(node);
        node[HashProperty] = Compute(packJson);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Hash of an ordered sequence of strings (used for rule-set fingerprints).</summary>
    public static string HashOf(IEnumerable<string> parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))));

    private static string Canonicalize(JsonObject root)
    {
        var clone = JsonNode.Parse(root.ToJsonString())!.AsObject();
        RemoveHash(clone);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            Write(writer, clone);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void RemoveHash(JsonObject obj)
    {
        foreach (var key in obj.Select(p => p.Key).Where(k => string.Equals(k, HashProperty, StringComparison.OrdinalIgnoreCase)).ToList())
            obj.Remove(key);
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(writer, value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray arr:
                writer.WriteStartArray();
                foreach (var item in arr)
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}

/// <summary>Outcome of parsing and validating an update pack.</summary>
public sealed class AssessmentUpdateValidationResult
{
    public AssessmentUpdatePack? Pack { get; init; }
    public string? ComputedSha256 { get; init; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Pack != null && Errors.Count == 0;
}

/// <summary>
/// Parses an update pack, verifies its SHA-256 hash and validates every operation against the
/// rule set it will be applied to. A pack with any error is rejected as a whole (never partially applied).
/// </summary>
public static class AssessmentUpdateValidator
{
    public const int MaxPackBytes = 1024 * 1024;
    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.Compiled);

    /// <param name="packJson">Raw pack file content.</param>
    /// <param name="currentRules">Effective rules before this pack (built-in plus earlier packs).</param>
    /// <param name="knownKpiKeys">KPI keys available to rules.</param>
    /// <param name="appVersion">Running application version for compatibility bounds.</param>
    public static AssessmentUpdateValidationResult Validate(
        string packJson,
        IReadOnlyCollection<AssessmentRule> currentRules,
        IReadOnlySet<string> knownKpiKeys,
        Version appVersion)
    {
        if (string.IsNullOrWhiteSpace(packJson))
            return Fail("Update pack is empty.");
        if (Encoding.UTF8.GetByteCount(packJson) > MaxPackBytes)
            return Fail($"Update pack exceeds the {MaxPackBytes / 1024} KB limit.");

        AssessmentUpdatePack? pack;
        string computed;
        try
        {
            computed = AssessmentUpdateHasher.Compute(packJson);
            pack = JsonSerializer.Deserialize<AssessmentUpdatePack>(packJson, AssessmentUpdatePack.JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return Fail($"Update pack is not valid JSON: {ex.Message}");
        }
        if (pack == null)
            return Fail("Update pack is empty.");

        var result = new AssessmentUpdateValidationResult { Pack = pack, ComputedSha256 = computed };
        var errors = result.Errors;

        if (string.IsNullOrWhiteSpace(pack.Sha256))
            errors.Add("Missing sha256 integrity hash.");
        else if (!AssessmentUpdateHasher.Verify(packJson, pack.Sha256))
            errors.Add("SHA-256 integrity check failed: the pack content does not match its sha256 hash.");

        if (pack.SchemaVersion != 1)
            errors.Add($"Unsupported schemaVersion {pack.SchemaVersion}.");
        if (!IdPattern.IsMatch(pack.Id ?? string.Empty))
            errors.Add("Pack id is required and may contain only letters, digits, '.', '_' and '-'.");
        if (!Version.TryParse(pack.Version, out _))
            errors.Add("Pack version is required and must be a numeric version (e.g. 1.0.0).");
        if (string.IsNullOrWhiteSpace(pack.Title))
            errors.Add("Pack title is required.");

        CheckBound(pack.MinAppVersion, "minAppVersion", v => appVersion < v, $"requires application version {pack.MinAppVersion} or later", errors);
        CheckBound(pack.MaxAppVersion, "maxAppVersion", v => appVersion > v, $"supports application versions up to {pack.MaxAppVersion}", errors);

        if (pack.Operations.Count == 0)
            errors.Add("Pack contains no operations.");

        var rules = currentRules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < pack.Operations.Count; i++)
        {
            var op = pack.Operations[i];
            var where = $"Operation {i + 1} ({op.Op} {op.RuleId})";

            if (!IdPattern.IsMatch(op.RuleId ?? string.Empty))
            {
                errors.Add($"{where}: ruleId is required and must be a valid identifier.");
                continue;
            }
            if (!touched.Add(op.RuleId))
                errors.Add($"{where}: the rule is changed more than once in this pack.");

            switch (op.Op)
            {
                case AssessmentUpdateOperationKind.Modify:
                    if (!rules.TryGetValue(op.RuleId, out var existing))
                        errors.Add($"{where}: rule does not exist.");
                    else if (op.Rule == null)
                        errors.Add($"{where}: 'rule' with at least one field is required.");
                    else
                        ValidateRule(where, AssessmentRuleMerger.Apply(existing, op.Rule), op.Rule, knownKpiKeys, errors);
                    break;

                case AssessmentUpdateOperationKind.Retire:
                    if (!rules.ContainsKey(op.RuleId))
                        errors.Add($"{where}: rule does not exist.");
                    if (op.Rule != null)
                        errors.Add($"{where}: retire operations must not include 'rule'.");
                    if (op.Types?.Any(t => !Enum.IsDefined(t)) == true)
                        errors.Add($"{where}: unknown assessment type.");
                    break;

                case AssessmentUpdateOperationKind.Add:
                    if (rules.ContainsKey(op.RuleId))
                        errors.Add($"{where}: a rule with this id already exists; use modify.");
                    else if (op.Rule == null)
                        errors.Add($"{where}: 'rule' is required.");
                    else
                    {
                        if (string.IsNullOrWhiteSpace(op.Rule.Title) || string.IsNullOrWhiteSpace(op.Rule.KpiKey)
                            || op.Rule.Types is not { Count: > 0 } || op.Rule.Recommendation == null)
                            errors.Add($"{where}: added rules require title, kpiKey, types and recommendation.");
                        else
                            ValidateRule(where, AssessmentRuleMerger.Apply(new AssessmentRule { Id = op.RuleId }, op.Rule), op.Rule, knownKpiKeys, errors);
                    }
                    break;

                default:
                    errors.Add($"{where}: unknown operation.");
                    break;
            }
        }

        return result;
    }

    private static void ValidateRule(string where, AssessmentRule rule, AssessmentRulePatch patch, IReadOnlySet<string> knownKpiKeys, List<string> errors)
    {
        if (patch.KpiKey != null && !knownKpiKeys.Contains(rule.KpiKey))
            errors.Add($"{where}: unknown KPI '{rule.KpiKey}'.");
        if (patch.Types != null && (patch.Types.Count == 0 || patch.Types.Any(t => !Enum.IsDefined(t))))
            errors.Add($"{where}: types must list at least one known assessment type.");
        if (patch.Severity is { } s && !Enum.IsDefined(s))
            errors.Add($"{where}: unknown severity.");
        if (patch.Comparison is { } c && !Enum.IsDefined(c))
            errors.Add($"{where}: unknown comparison.");
        if (rule.WarnThreshold < 0 || rule.FailThreshold < 0)
            errors.Add($"{where}: thresholds must be zero or greater.");
        else if (rule.Comparison == AssessmentComparison.AtLeast ? rule.WarnThreshold > rule.FailThreshold : rule.WarnThreshold < rule.FailThreshold)
            errors.Add($"{where}: warnThreshold ({rule.WarnThreshold}) must not be past failThreshold ({rule.FailThreshold}) for comparison {rule.Comparison}.");
        if (patch.Title != null && string.IsNullOrWhiteSpace(patch.Title))
            errors.Add($"{where}: title must not be blank.");
        if (patch.Localized != null)
        {
            foreach (var culture in patch.Localized.Keys)
            {
                try { _ = System.Globalization.CultureInfo.GetCultureInfo(culture, predefinedOnly: true); }
                catch (System.Globalization.CultureNotFoundException) { errors.Add($"{where}: unknown culture '{culture}'."); }
            }
        }
    }

    private static void CheckBound(string? value, string name, Func<Version, bool> violated, string message, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (!Version.TryParse(value, out var v))
            errors.Add($"{name} '{value}' is not a valid version.");
        else if (violated(v))
            errors.Add($"Pack {message}.");
    }

    private static AssessmentUpdateValidationResult Fail(string error)
    {
        var r = new AssessmentUpdateValidationResult();
        r.Errors.Add(error);
        return r;
    }
}

/// <summary>Applies a field-level patch to a rule, producing a new rule instance.</summary>
public static class AssessmentRuleMerger
{
    public static AssessmentRule Apply(AssessmentRule rule, AssessmentRulePatch patch) => new()
    {
        Id = rule.Id,
        Title = patch.Title ?? rule.Title,
        CategoryName = patch.CategoryName ?? rule.CategoryName,
        KpiKey = patch.KpiKey ?? rule.KpiKey,
        Severity = patch.Severity ?? rule.Severity,
        Types = patch.Types?.Distinct().ToArray() ?? rule.Types,
        Comparison = patch.Comparison ?? rule.Comparison,
        WarnThreshold = patch.WarnThreshold ?? rule.WarnThreshold,
        FailThreshold = patch.FailThreshold ?? rule.FailThreshold,
        Recommendation = patch.Recommendation ?? rule.Recommendation
    };
}
