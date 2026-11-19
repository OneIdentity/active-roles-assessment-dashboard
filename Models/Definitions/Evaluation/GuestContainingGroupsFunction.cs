using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>
/// Counts the group items with at least one member that is a guest item of the same dataset.
/// Optional args: <c>typeAttribute</c> (default <c>entraObjectType</c>), <c>guestType</c>
/// (default <c>GuestUser</c>), <c>groupTypes</c> (array; default the four Entra group types),
/// <c>keyAttribute</c> (default <c>distinguishedName</c>) and <c>memberAttribute</c> (default <c>member</c>).
/// </summary>
public sealed class GuestContainingGroupsFunction : IKpiFunction
{
    private static readonly string[] DefaultGroupTypes = ["DistributionGroup", "DynamicDistributionGroup", "Microsoft365Group", "SecurityGroup"];

    public string Name => "guestContainingGroups";

    public long? Evaluate(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context)
    {
        var typeAttribute = Arg(args, "typeAttribute", "entraObjectType");
        var guestType = Arg(args, "guestType", "GuestUser");
        var keyAttribute = Arg(args, "keyAttribute", "distinguishedName");
        var memberAttribute = Arg(args, "memberAttribute", "member");
        var groupTypes = (args.TryGetValue("groupTypes", out var g) && g.ValueKind == JsonValueKind.Array
                ? g.EnumerateArray().Select(e => e.GetString() ?? string.Empty)
                : DefaultGroupTypes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string TypeOf(JsonElement i) => ExprValue.Text(ExpressionEvaluator.ReadAttribute(i, typeAttribute));

        var guests = items
            .Where(i => string.Equals(TypeOf(i), guestType, StringComparison.OrdinalIgnoreCase))
            .Select(i => ExprValue.Text(ExpressionEvaluator.ReadAttribute(i, keyAttribute)))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (guests.Count == 0) return 0;

        return items.Count(i => groupTypes.Contains(TypeOf(i))
            && ExprValue.List(ExpressionEvaluator.ReadAttribute(i, memberAttribute)).Any(guests.Contains));
    }

    private static string Arg(IReadOnlyDictionary<string, JsonElement> args, string name, string fallback) =>
        args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : fallback;
}
