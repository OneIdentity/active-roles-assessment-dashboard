using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>Inputs shared by every evaluation: the clock and named parameters (e.g. <c>$staleDays</c>).</summary>
public sealed class EvaluationContext
{
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Results of KPIs already evaluated in this pass (used by derived/composite KPIs).</summary>
    public IReadOnlyDictionary<string, long> KpiResults { get; init; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every dataset available in this pass (used by functions that compare datasets).</summary>
    public IReadOnlyDictionary<string, DatasetResult> Datasets { get; init; } = new Dictionary<string, DatasetResult>(StringComparer.OrdinalIgnoreCase);

    public EvaluationContext WithDatasets(IReadOnlyDictionary<string, DatasetResult> datasets) => new()
    {
        Now = Now,
        Parameters = Parameters,
        KpiResults = KpiResults,
        Datasets = datasets
    };
}

/// <summary>Evaluates parsed expressions against a single dataset item.</summary>
public static class ExpressionEvaluator
{
    public static bool Matches(Expr expr, JsonElement item, EvaluationContext context) =>
        ExprValue.Truthy(Evaluate(expr, item, context));

    public static object? Evaluate(Expr expr, JsonElement item, EvaluationContext context) => expr switch
    {
        LiteralExpr l => l.Value,
        AttributeExpr a => ReadAttribute(item, a.Name),
        ParameterExpr p => context.Parameters.TryGetValue(p.Name, out var v)
            ? v
            : throw new ExpressionException($"Unknown parameter '${p.Name}'."),
        UnaryExpr { Operator: "!" } u => !ExprValue.Truthy(Evaluate(u.Operand, item, context)),
        UnaryExpr u => -(ExprValue.Number(Evaluate(u.Operand, item, context)) ?? 0),
        BinaryExpr { Operator: "&&" } b => ExprValue.Truthy(Evaluate(b.Left, item, context)) && ExprValue.Truthy(Evaluate(b.Right, item, context)),
        BinaryExpr { Operator: "||" } b => ExprValue.Truthy(Evaluate(b.Left, item, context)) || ExprValue.Truthy(Evaluate(b.Right, item, context)),
        BinaryExpr b => Binary(b.Operator, Evaluate(b.Left, item, context), Evaluate(b.Right, item, context)),
        CallExpr c => BuiltInFunctions.Invoke(c.Name, c.Args.Select(a => Evaluate(a, item, context)).ToList(), context),
        _ => throw new ExpressionException($"Unsupported expression {expr.GetType().Name}.")
    };

    /// <summary>Reads an attribute from the item root or its <c>attributes</c> object (Active Roles REST shape).</summary>
    public static object? ReadAttribute(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        if (TryGet(item, name, out var v)) return ExprValue.FromJson(v);
        if (TryGet(item, "attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object && TryGet(attrs, name, out var av))
            return ExprValue.FromJson(av);
        return null;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }

    private static object? Binary(string op, object? left, object? right)
    {
        switch (op)
        {
            case "+" or "-" or "*" or "/":
                if (ExprValue.Number(left) is not { } l || ExprValue.Number(right) is not { } r) return null;
                return op switch { "+" => l + r, "-" => l - r, "*" => l * r, _ => r == 0 ? null : l / r };
            case "==": return AreEqual(left, right);
            case "!=": return !AreEqual(left, right);
        }

        int? cmp = ExprValue.Number(left) is { } ln && ExprValue.Number(right) is { } rn
            ? ln.CompareTo(rn)
            : left is null || right is null ? null : string.Compare(ExprValue.Text(left), ExprValue.Text(right), StringComparison.OrdinalIgnoreCase);
        if (cmp is null) return false;
        return op switch { "<" => cmp < 0, "<=" => cmp <= 0, ">" => cmp > 0, ">=" => cmp >= 0, _ => throw new ExpressionException($"Unknown operator '{op}'.") };
    }

    private static bool AreEqual(object? left, object? right)
    {
        if (left is null || right is null)
            return !ExprValue.Truthy(left) && !ExprValue.Truthy(right) && (left is null or string { Length: 0 } or IReadOnlyList<string> { Count: 0 }) && (right is null or string { Length: 0 } or IReadOnlyList<string> { Count: 0 });
        if (left is bool || right is bool) return ExprValue.Truthy(left) == ExprValue.Truthy(right);
        if (ExprValue.Number(left) is { } ln && ExprValue.Number(right) is { } rn) return ln == rn;
        return string.Equals(ExprValue.Text(left), ExprValue.Text(right), StringComparison.OrdinalIgnoreCase);
    }
}
