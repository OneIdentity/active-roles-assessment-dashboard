using System.Text.Json;
using System.Xml.Linq;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>
/// Sums the managed object counts of the most recent Active Roles statistics run. Each item holds
/// one run as XML (default attribute <c>edsaStatisticsCountXML</c>, override with the
/// <c>attribute</c> arg); domain, partition and Entra (cloud-only + hybrid + guest) counts are added.
/// </summary>
public sealed class ManagedObjectStatisticsFunction : IKpiFunction
{
    public string Name => "managedObjectStatistics";

    public long? Evaluate(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context)
    {
        var attribute = args.TryGetValue("attribute", out var a) && a.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(a.GetString())
            ? a.GetString()!
            : "edsaStatisticsCountXML";

        (DateTime RunTime, long Total)? latest = null;
        foreach (var item in items)
        {
            var run = Parse(ExprValue.Text(ExpressionEvaluator.ReadAttribute(item, attribute)));
            if (run is not null && (latest is null || run.Value.RunTime >= latest.Value.RunTime))
                latest = run;
        }

        return latest?.Total ?? 0;
    }

    private static (DateTime RunTime, long Total)? Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return null;
            var ns = root.GetDefaultNamespace();

            var runTime = long.TryParse(root.Attribute("runTime")?.Value, out var ft) ? DateTime.FromFileTimeUtc(ft) : default;

            long total = 0;
            foreach (var e in root.Descendants(ns + "Domain").Concat(root.Descendants(ns + "Partition")))
                total += Int(e, ns + "Count");
            foreach (var e in root.Descendants(ns + "AzureCount"))
                total += Int(e, ns + "CloudOnlyCount") + Int(e, ns + "HybridCount") + Int(e, ns + "GuestCount");

            return (runTime, total);
        }
        catch
        {
            return null;
        }
    }

    private static int Int(XElement parent, XName name) => int.TryParse(parent.Element(name)?.Value, out var v) ? v : 0;
}
