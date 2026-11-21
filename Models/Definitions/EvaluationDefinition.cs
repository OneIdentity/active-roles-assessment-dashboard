using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>
/// How a KPI value is calculated from its dataset. Uses an expression where possible and a
/// named transform function otherwise.
/// </summary>
public sealed class EvaluationDefinition
{
    public EvaluationType Type { get; init; } = EvaluationType.Expression;

    /// <summary>Filter expression selecting items (Expression type). Empty selects all items.</summary>
    public string? Filter { get; init; }

    public AggregateFunction Aggregate { get; init; } = AggregateFunction.Count;

    /// <summary>Attribute used by DistinctCount/Sum/Min/Max.</summary>
    public string? AggregateAttribute { get; init; }

    /// <summary>Expression computing the per-item value aggregated by Sum/Min/Max (alternative to AggregateAttribute).</summary>
    public string? AggregateExpression { get; init; }

    /// <summary>Registered transform function name (Function type).</summary>
    public string? Function { get; init; }

    /// <summary>Arguments passed to the transform function.</summary>
    public IReadOnlyDictionary<string, JsonElement> Args { get; init; } = new Dictionary<string, JsonElement>();
}
