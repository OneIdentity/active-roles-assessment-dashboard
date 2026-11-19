using System.Collections.Concurrent;
using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>A named transform used when a KPI cannot be expressed as filter + aggregate.</summary>
public interface IKpiFunction
{
    string Name { get; }

    /// <summary>Returns the KPI value, or null when the function defers to other logic.</summary>
    long? Evaluate(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context);
}

/// <summary>Registry of named transform functions available to definitions.</summary>
public sealed class KpiFunctionRegistry
{
    /// <summary>Placeholder used by generated definitions whose value is still computed by hand-written code.</summary>
    public const string Legacy = "Legacy";

    private readonly Dictionary<string, IKpiFunction> _functions = new(StringComparer.OrdinalIgnoreCase);

    public KpiFunctionRegistry(IEnumerable<IKpiFunction>? functions = null)
    {
        foreach (var f in BuiltIn.Concat(functions ?? []))
            _functions[f.Name] = f;
    }

    /// <summary>Transform functions shipped with the application and always available to definitions.</summary>
    private static readonly IKpiFunction[] BuiltIn = [new CircularGroupNestingFunction(), new ManagedObjectStatisticsFunction(), new GuestContainingGroupsFunction()];

    public bool IsRegistered(string name) => name == Legacy || _functions.ContainsKey(name);

    public IKpiFunction? Get(string name) => _functions.GetValueOrDefault(name);
}

/// <summary>
/// Computes KPI values from dataset items using a definition's evaluation. Parsed expressions are
/// cached by text. Returns null for KPIs whose value is still produced by legacy code.
/// </summary>
public sealed class KpiEvaluator(KpiFunctionRegistry functions)
{
    private readonly ConcurrentDictionary<string, Expr> _cache = new(StringComparer.Ordinal);

    public Expr Parse(string expression) => _cache.GetOrAdd(expression, ExpressionParser.Parse);

    public long? Evaluate(EvaluationDefinition evaluation, IReadOnlyList<JsonElement> items, EvaluationContext context)
    {
        if (evaluation.Type == EvaluationType.Function)
        {
            if (evaluation.Function is null or KpiFunctionRegistry.Legacy) return null;
            var fn = functions.Get(evaluation.Function)
                ?? throw new ExpressionException($"Unknown function '{evaluation.Function}'.");
            return fn.Evaluate(items, evaluation.Args, context);
        }

        var selected = string.IsNullOrWhiteSpace(evaluation.Filter)
            ? items
            : items.Where(i => ExpressionEvaluator.Matches(Parse(evaluation.Filter), i, context)).ToList();

        if (evaluation.Aggregate == AggregateFunction.Count) return selected.Count;

        var attr = evaluation.AggregateAttribute
            ?? throw new ExpressionException($"Aggregate {evaluation.Aggregate} requires aggregateAttribute.");
        var values = selected.Select(i => ExpressionEvaluator.ReadAttribute(i, attr));

        if (evaluation.Aggregate == AggregateFunction.DistinctCount)
            return values.SelectMany(ExprValue.List).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        var numbers = values.Select(ExprValue.Number).Where(n => n.HasValue).Select(n => n!.Value).ToList();
        if (numbers.Count == 0) return 0;
        return (long)(evaluation.Aggregate switch
        {
            AggregateFunction.Sum => numbers.Sum(),
            AggregateFunction.Min => numbers.Min(),
            _ => numbers.Max()
        });
    }

    public static long Combine(CompositeAggregate aggregate, IEnumerable<long> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return 0;
        return aggregate switch
        {
            CompositeAggregate.Sum => list.Sum(),
            CompositeAggregate.Min => list.Min(),
            CompositeAggregate.Max => list.Max(),
            CompositeAggregate.First => list[0],
            _ => list.Sum()
        };
    }
}
