using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>
/// Counts the items whose key is not referenced by any item of another dataset, e.g. access
/// templates that no access template link points at. Args: <c>referenceDataset</c> and
/// <c>referenceAttribute</c> (required), <c>keyAttribute</c> (default <c>objectGUID</c>) and
/// <c>match</c> (<c>guid</c> to ignore braces and case, otherwise case-insensitive text).
/// </summary>
public sealed class UnreferencedItemsFunction : IKpiItemSelector
{
    public const string FunctionName = "unreferencedItems";
    public const string ReferenceDatasetArg = "referenceDataset";

    public string Name => FunctionName;

    public long? Evaluate(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context) =>
        Select(items, args, context).Count;

    public IReadOnlyList<JsonElement> Select(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context)
    {
        var referenceDataset = Arg(args, ReferenceDatasetArg, null)
            ?? throw new ExpressionException($"{FunctionName} requires '{ReferenceDatasetArg}'.");
        var referenceAttribute = Arg(args, "referenceAttribute", null)
            ?? throw new ExpressionException($"{FunctionName} requires 'referenceAttribute'.");
        var keyAttribute = Arg(args, "keyAttribute", "objectGUID")!;
        var asGuid = string.Equals(Arg(args, "match", null), "guid", StringComparison.OrdinalIgnoreCase);

        if (!context.Datasets.TryGetValue(referenceDataset, out var reference))
            throw new ExpressionException($"Dataset '{referenceDataset}' is not available.");
        if (reference.Error is not null)
            throw new ExpressionException(reference.Error);

        string Normalize(string value) => asGuid ? value.Trim().Trim('{', '}').Trim().ToLowerInvariant() : value.Trim();

        var referenced = reference.Items
            .SelectMany(i => ExprValue.List(ExpressionEvaluator.ReadAttribute(i, referenceAttribute)))
            .Select(Normalize)
            .Where(v => v.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return items.Where(i => !referenced.Contains(Normalize(ExprValue.Text(ExpressionEvaluator.ReadAttribute(i, keyAttribute))))).ToList();
    }

    private static string? Arg(IReadOnlyDictionary<string, JsonElement> args, string name, string? fallback) =>
        args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : fallback;
}
