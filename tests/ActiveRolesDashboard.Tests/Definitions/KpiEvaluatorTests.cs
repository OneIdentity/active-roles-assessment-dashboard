using System.Text.Json;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public sealed class KpiEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly EvaluationContext Context = new()
    {
        Now = Now,
        Parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["staleDays"] = 90d }
    };

    private static readonly IReadOnlyList<JsonElement> Users = Parse("""
        [
          { "name": "a", "attributes": { "userAccountControl": "514", "lastLogonTimestamp": "0", "memberOf": ["CN=Admins,DC=x"], "msDS-SiteName": "HQ" } },
          { "name": "b", "attributes": { "userAccountControl": "66048", "lastLogonTimestamp": "{{OLD}}", "memberOf": [], "msDS-SiteName": "hq" } },
          { "name": "c", "userAccountControl": 512, "attributes": { "lastLogonTimestamp": "{{RECENT}}", "manager": "CN=Boss" } }
        ]
        """.Replace("{{OLD}}", Now.AddDays(-200).ToFileTime().ToString())
           .Replace("{{RECENT}}", Now.AddDays(-5).ToFileTime().ToString()));

    private static IReadOnlyList<JsonElement> Parse(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray().ToList();

    private static long? Run(string? filter, AggregateFunction agg = AggregateFunction.Count, string? attr = null) =>
        new KpiEvaluator(new KpiFunctionRegistry()).Evaluate(
            new EvaluationDefinition { Filter = filter, Aggregate = agg, AggregateAttribute = attr }, Users, Context);

    [Theory]
    [InlineData(null, 3)]
    [InlineData("hasFlag(userAccountControl, 0x2)", 1)]
    [InlineData("!hasFlag(userAccountControl, 2) && hasFlag(userAccountControl, 0x10000)", 1)]
    [InlineData("isEmpty(manager)", 2)]
    [InlineData("exists(manager)", 1)]
    [InlineData("daysSince(lastLogonTimestamp) > $staleDays", 1)]
    [InlineData("!exists(daysSince(lastLogonTimestamp))", 1)]
    [InlineData("anyStartsWith(memberOf, 'CN=Admins')", 1)]
    [InlineData("count(memberOf) == 0", 2)]
    [InlineData("`msDS-SiteName` == 'HQ'", 2)]
    [InlineData("msDS-SiteName == \"hq\"", 2)]
    [InlineData("in(name, 'a', 'c')", 2)]
    [InlineData("userAccountControl >= 512 && userAccountControl < 1000", 2)]
    [InlineData("(name == 'a' || name == 'b') && !(name == 'b')", 1)]
    public void Filter_counts(string? filter, long expected) => Assert.Equal(expected, Run(filter));

    [Fact]
    public void Aggregates()
    {
        Assert.Equal(1, Run(null, AggregateFunction.DistinctCount, "msDS-SiteName")); // HQ and hq are the same value
        Assert.Equal(512 + 514 + 66048, Run(null, AggregateFunction.Sum, "userAccountControl"));
        Assert.Equal(512, Run(null, AggregateFunction.Min, "userAccountControl"));
        Assert.Equal(66048, Run(null, AggregateFunction.Max, "userAccountControl"));
    }

    [Theory]
    [InlineData("name ==")]
    [InlineData("name == 'x")]
    [InlineData("deleteAll(name)")]
    [InlineData("name ; drop")]
    [InlineData("(name == 'a'")]
    public void Invalid_expressions_are_rejected(string expression) =>
        Assert.Throws<ExpressionException>(() => ExpressionParser.Parse(expression));

    [Fact]
    public void Unknown_parameter_throws() =>
        Assert.Throws<ExpressionException>(() => Run("$missing > 1"));

    [Fact]
    public void Legacy_function_defers_and_named_function_runs()
    {
        var evaluator = new KpiEvaluator(new KpiFunctionRegistry([new FixedFunction()]));

        Assert.Null(evaluator.Evaluate(new EvaluationDefinition { Type = EvaluationType.Function, Function = KpiFunctionRegistry.Legacy }, Users, Context));
        Assert.Equal(42, evaluator.Evaluate(new EvaluationDefinition { Type = EvaluationType.Function, Function = "Fixed" }, Users, Context));
        Assert.Throws<ExpressionException>(() => evaluator.Evaluate(new EvaluationDefinition { Type = EvaluationType.Function, Function = "Nope" }, Users, Context));
    }

    [Fact]
    public void Composite_combines()
    {
        Assert.Equal(6, KpiEvaluator.Combine(CompositeAggregate.Sum, [1, 2, 3]));
        Assert.Equal(3, KpiEvaluator.Combine(CompositeAggregate.Max, [1, 3, 2]));
        Assert.Equal(1, KpiEvaluator.Combine(CompositeAggregate.Min, [2, 1, 3]));
        Assert.Equal(2, KpiEvaluator.Combine(CompositeAggregate.First, [2, 1]));
    }

    [Fact]
    public void Validator_reports_bad_filter_and_unregistered_function()
    {
        var set = new DefinitionSet
        {
            Categories = [new CategoryDefinition { Id = "C", DashboardId = "D" }],
            Kpis =
            [
                new DerivedKpi { Id = "K1", CategoryId = "C", Evaluation = new EvaluationDefinition { Filter = "name ==" } },
                new DerivedKpi { Id = "K2", CategoryId = "C", Evaluation = new EvaluationDefinition { Type = EvaluationType.Function, Function = "Nope" } },
                new DerivedKpi { Id = "K3", CategoryId = "C", Evaluation = new EvaluationDefinition { Aggregate = AggregateFunction.Sum } }
            ]
        };

        var errors = DefinitionValidator.Validate(set);

        Assert.Contains(errors, e => e.Contains("K1") && e.Contains("filter"));
        Assert.Contains(errors, e => e.Contains("K2") && e.Contains("Nope"));
        Assert.Contains(errors, e => e.Contains("K3") && e.Contains("aggregateAttribute"));
    }

    private sealed class FixedFunction : IKpiFunction
    {
        public string Name => "Fixed";

        public long? Evaluate(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context) => 42;
    }
}
