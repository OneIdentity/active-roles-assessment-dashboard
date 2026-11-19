using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;

namespace ActiveRolesDashboard.Services.Definitions;

/// <summary>
/// Fills <see cref="DashboardSummary.KpiResults"/> by evaluating every enabled KPI whose
/// definition is an expression against the summary's <see cref="DashboardSummary.Datasets"/>.
/// KPIs still computed by hand-written code (legacy functions) are left out.
/// </summary>
public sealed class DefinitionKpiCalculator(DefinitionRegistry registry, KpiEvaluator evaluator)
{
    public void Populate(DashboardSummary summary, KpiSettings settings, EvaluationContext context) =>
        Populate(summary, kpi => settings.IsKpiEnabled(kpi.CategoryId, kpi.Id), context);

    /// <summary>Evaluates the expression KPIs accepted by <paramref name="include"/>.</summary>
    public void Populate(DashboardSummary summary, Func<KpiDefinition, bool> include, EvaluationContext context)
    {
        var datasets = summary.Datasets;
        context = context.WithDatasets(datasets);
        var results = new Dictionary<string, KpiResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var kpi in registry.Set.Kpis)
        {
            var (datasetId, evaluation) = kpi switch
            {
                DerivedKpi d => (d.SourceDatasetId, d.Evaluation),
                SearchKpi s => (s.DatasetId, s.Evaluation),
                ObjectKpi o => (o.DatasetId, o.Evaluation),
                _ => (null, null)
            };
            if (datasetId is null || evaluation is null) continue;
            if (IsLegacy(evaluation)) continue;
            if (!kpi.Enabled || !include(kpi)) continue;
            if (!datasets.TryGetValue(datasetId, out var dataset))
            {
                results[kpi.Id] = KpiResult.Failed($"Dataset '{datasetId}' was not collected");
                continue;
            }

            if (dataset.Error is not null)
            {
                results[kpi.Id] = KpiResult.Failed(dataset.Error);
                continue;
            }

            try
            {
                results[kpi.Id] = new KpiResult(evaluator.Evaluate(evaluation, dataset.Items, context) ?? 0, null);
            }
            catch (Exception ex)
            {
                results[kpi.Id] = KpiResult.Failed($"Definition evaluation failed: {ex.Message}");
            }
        }

        PopulateComposites(summary, results, include);
        summary.KpiResults = results;
    }

    /// <summary>
    /// Combines the results of other KPIs. A composite is only produced once every source has a
    /// definition result; failed sources are skipped unless all of them failed. Sources still
    /// computed by legacy code contribute the legacy value until they are migrated.
    /// </summary>
    private void PopulateComposites(DashboardSummary summary, Dictionary<string, KpiResult> results, Func<KpiDefinition, bool> include)
    {
        var pending = registry.Set.Kpis.OfType<CompositeKpi>()
            .Where(k => k.Enabled && include(k) && k.Sources.Count > 0)
            .ToList();

        var available = new Dictionary<string, KpiResult>(results, StringComparer.OrdinalIgnoreCase);
        foreach (var source in pending.SelectMany(k => k.Sources).Distinct(StringComparer.OrdinalIgnoreCase).Where(IsLegacyKpi))
        {
            var (count, error) = summary.GetLegacyKpiResult(source);
            available.TryAdd(source, error is null ? new KpiResult(count, null) : KpiResult.Failed(error));
        }

        bool progressed;
        do
        {
            progressed = false;
            foreach (var kpi in pending.ToList())
            {
                if (!kpi.Sources.All(available.ContainsKey)) continue;

                var sources = kpi.Sources.Select(s => available[s]).ToList();
                var succeeded = sources.Where(r => r.Error is null).ToList();
                results[kpi.Id] = available[kpi.Id] = succeeded.Count == 0
                    ? KpiResult.Failed(sources[0].Error!)
                    : new KpiResult(KpiEvaluator.Combine(kpi.Aggregate, succeeded.Select(r => r.Value)), null);
                pending.Remove(kpi);
                progressed = true;
            }
        } while (progressed && pending.Count > 0);

        foreach (var kpi in pending)
        {
            var missing = kpi.Sources.Where(s => !available.ContainsKey(s));
            results[kpi.Id] = KpiResult.Failed($"Source KPIs not evaluated: {string.Join(", ", missing)}");
        }
    }

    /// <summary>Re-evaluates the KPIs already present in <see cref="DashboardSummary.KpiResults"/> (e.g. after a segment filter).</summary>
    public void Reevaluate(DashboardSummary summary, EvaluationContext context)
    {
        if (summary.KpiResults.Count == 0) return;
        var ids = summary.KpiResults.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Populate(summary, kpi => ids.Contains(kpi.Id), context);
    }

    /// <summary>
    /// Ids of datasets read by enabled expression KPIs that are not already provided by the typed
    /// summaries, i.e. the datasets the generic collector must fetch.
    /// </summary>
    public IReadOnlyList<string> RequiredDatasetIds(KpiSettings settings)
    {
        var legacy = new DashboardSummary().Datasets.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evaluations = registry.Set.Kpis
            .Where(k => k.Enabled && settings.IsKpiEnabled(k.CategoryId, k.Id))
            .Select(k => k switch
            {
                DerivedKpi d when !IsLegacy(d.Evaluation) => (Dataset: d.SourceDatasetId, d.Evaluation),
                SearchKpi s when !IsLegacy(s.Evaluation) => (Dataset: s.DatasetId, s.Evaluation),
                ObjectKpi o when !IsLegacy(o.Evaluation) => (Dataset: o.DatasetId, o.Evaluation),
                _ => (Dataset: (string?)null, Evaluation: (EvaluationDefinition?)null)
            })
            .Where(e => e.Dataset is not null)
            .ToList();

        return evaluations.Select(e => e.Dataset!)
            .Concat(evaluations.Select(e => ReferencedDataset(e.Evaluation!)).OfType<string>())
            .Where(id => !legacy.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReferencedDataset(EvaluationDefinition evaluation) =>
        evaluation.Type == EvaluationType.Function
        && evaluation.Args.TryGetValue(UnreferencedItemsFunction.ReferenceDatasetArg, out var v)
        && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()
            : null;

    private bool IsLegacyKpi(string id) =>
        registry.Kpis.TryGetValue(id, out var kpi) && IsLegacy(kpi switch
        {
            DerivedKpi d => d.Evaluation,
            SearchKpi s => s.Evaluation,
            ObjectKpi o => o.Evaluation,
            _ => new EvaluationDefinition { Type = EvaluationType.Expression }
        });

    private static bool IsLegacy(EvaluationDefinition? evaluation) =>
        evaluation is null
        || (evaluation.Type == EvaluationType.Function
            && (string.IsNullOrEmpty(evaluation.Function) || string.Equals(evaluation.Function, KpiFunctionRegistry.Legacy, StringComparison.OrdinalIgnoreCase)));
}
