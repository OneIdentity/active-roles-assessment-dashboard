using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions.Evaluation;

/// <summary>
/// Counts the groups that lie on at least one membership cycle. Edges run from each group to the
/// members that are themselves groups in the dataset. Optional args <c>keyAttribute</c>
/// (default <c>distinguishedName</c>) and <c>memberAttribute</c> (default <c>member</c>).
/// </summary>
public sealed class CircularGroupNestingFunction : IKpiItemSelector
{
    public string Name => "circularNesting";

    public long? Evaluate(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context) =>
        Select(items, args, context).Count;

    public IReadOnlyList<JsonElement> Select(IReadOnlyList<JsonElement> items, IReadOnlyDictionary<string, JsonElement> args, EvaluationContext context)
    {
        var keyAttribute = Arg(args, "keyAttribute", "distinguishedName");
        var memberAttribute = Arg(args, "memberAttribute", "member");

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var members = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var first = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var key = ExprValue.Text(ExpressionEvaluator.ReadAttribute(item, keyAttribute));
            if (key.Length > 0 && !members.ContainsKey(key))
            {
                members[key] = ExprValue.List(ExpressionEvaluator.ReadAttribute(item, memberAttribute));
                first[key] = item;
            }
        }
        foreach (var (key, list) in members)
            adjacency[key] = list.Where(m => m.Length > 0 && members.ContainsKey(m)).ToList();

        var onCycle = FindNodesOnCycles(adjacency);
        return first.Where(p => onCycle.Contains(p.Key)).Select(p => p.Value).ToList();
    }

    /// <summary>Iterative DFS; every node between a back edge's target and the current node is on a cycle.</summary>
    internal static HashSet<string> FindNodesOnCycles(IReadOnlyDictionary<string, List<string>> adjacency)
    {
        const int White = 0, Gray = 1, Black = 2;
        var color = adjacency.Keys.ToDictionary(k => k, _ => White, StringComparer.OrdinalIgnoreCase);
        var onCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in adjacency.Keys)
        {
            if (color[start] != White) continue;

            var stack = new Stack<string>();
            var path = new List<string>();
            var pathIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            stack.Push(start);

            while (stack.Count > 0)
            {
                var node = stack.Peek();
                if (color[node] == White)
                {
                    color[node] = Gray;
                    pathIndex[node] = path.Count;
                    path.Add(node);
                }

                var advanced = false;
                foreach (var next in adjacency[node])
                {
                    if (color[next] == White)
                    {
                        stack.Push(next);
                        advanced = true;
                        break;
                    }
                    if (color[next] == Gray && pathIndex.TryGetValue(next, out var startIdx))
                    {
                        for (var k = startIdx; k < path.Count; k++)
                            onCycle.Add(path[k]);
                    }
                }

                if (!advanced)
                {
                    color[node] = Black;
                    if (path.Count > 0 && string.Equals(path[^1], node, StringComparison.OrdinalIgnoreCase))
                    {
                        pathIndex.Remove(node);
                        path.RemoveAt(path.Count - 1);
                    }
                    stack.Pop();
                }
            }
        }

        return onCycle;
    }

    private static string Arg(IReadOnlyDictionary<string, JsonElement> args, string name, string fallback) =>
        args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : fallback;
}
