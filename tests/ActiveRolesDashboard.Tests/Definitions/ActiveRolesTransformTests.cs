using System.Text.Json;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public sealed class ActiveRolesTransformTests
{
    private static IReadOnlyList<JsonElement> Parse(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray().ToList();

    private static readonly KpiEvaluator Evaluator = new(new KpiFunctionRegistry());

    private static long? Filter(string filter, string json, EvaluationContext? context = null) =>
        Evaluator.Evaluate(new EvaluationDefinition { Filter = filter }, Parse(json), context ?? new EvaluationContext());

    [Theory]
    [InlineData("[]", false)]
    [InlineData("[\"[A;0x1;;;]\"]", false)]
    [InlineData("[\"[A;0x1;;;][D;0x2;;;]\"]", true)]
    [InlineData("[\"[ OD ;0x2;;;]\"]", true)]
    [InlineData("[\"[ODX;0x2;;;]\"]", false)]
    public void AceHasDeny_detects_deny_entries(string aceList, bool expected) =>
        Assert.Equal(expected ? 1 : 0, Filter("aceHasDeny(edsaATEList)", $"[{{ \"edsaATEList\": {aceList} }}]"));

    [Theory]
    [InlineData("", 0)]
    [InlineData("not xml", 0)]
    [InlineData("<APEList/>", 0)]
    [InlineData("<APEList><APE/><APE/></APEList>", 2)]
    public void XmlCount_counts_elements_and_tolerates_bad_xml(string xml, int count) =>
        Assert.Equal(1, Filter($"xmlCount(x, 'APE') == {count}", JsonSerializer.Serialize(new[] { new { x = xml } })));

    [Fact]
    public void IsBlank_treats_missing_empty_and_whitespace_as_blank() =>
        Assert.Equal(3, Filter("isBlank(a)", """[{}, { "a": "" }, { "a": "  " }, { "a": "x" }]"""));

    private static EvaluationDefinition Unreferenced(string dataset = "Links") => new()
    {
        Type = EvaluationType.Function,
        Function = UnreferencedItemsFunction.FunctionName,
        Args = new Dictionary<string, JsonElement>
        {
            ["referenceDataset"] = JsonSerializer.SerializeToElement(dataset),
            ["referenceAttribute"] = JsonSerializer.SerializeToElement("target"),
            ["match"] = JsonSerializer.SerializeToElement("guid")
        }
    };

    private static EvaluationContext WithLinks(DatasetResult links) => new()
    {
        Datasets = new Dictionary<string, DatasetResult>(StringComparer.OrdinalIgnoreCase) { ["Links"] = links }
    };

    [Fact]
    public void UnreferencedItems_counts_items_not_targeted_by_reference_dataset()
    {
        var items = Parse("""[{ "objectGUID": "AAAA" }, { "objectGUID": "{bbbb}" }, { "objectGUID": "cccc" }]""");
        var links = new DatasetResult(Parse("""[{ "target": "{aaaa}" }, { "target": " BBBB " }, { "target": "" }]"""), null);

        Assert.Equal(1, Evaluator.Evaluate(Unreferenced(), items, WithLinks(links)));
    }

    [Fact]
    public void UnreferencedItems_fails_when_reference_dataset_is_missing_or_errored()
    {
        var items = Parse("""[{ "objectGUID": "a" }]""");
        Assert.Throws<ExpressionException>(() => Evaluator.Evaluate(Unreferenced("Other"), items, WithLinks(new([], null))));
        Assert.Throws<ExpressionException>(() => Evaluator.Evaluate(Unreferenced(), items, WithLinks(new([], "boom"))));
    }
}
