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
            if (evaluation.Type != EvaluationType.Expression) continue;
            if (!kpi.Enabled || !include(kpi)) continue;
            if (!datasets.TryGetValue(datasetId, out var dataset)) continue;

            if (dataset.Error is not null)
            {
                results[kpi.Id] = KpiResult.Failed(dataset.Error);
                continue;
            }

            try
            {
                results[kpi.Id] = new KpiResult(evaluator.Evaluate(evaluation, dataset.Items, context) ?? 0, null);
            }
            catch (ExpressionException ex)
            {
                results[kpi.Id] = KpiResult.Failed(ex.Message);
            }
        }

        summary.KpiResults = results;
    }

    /// <summary>
    /// Ids of datasets read by enabled expression KPIs that are not already provided by the typed
    /// summaries, i.e. the datasets the generic collector must fetch.
    /// </summary>
    public IReadOnlyList<string> RequiredDatasetIds(KpiSettings settings)
    {
        var legacy = new DashboardSummary().Datasets.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return registry.Set.Kpis
            .Where(k => k.Enabled && settings.IsKpiEnabled(k.CategoryId, k.Id))
            .Select(k => k switch
            {
                DerivedKpi d when d.Evaluation?.Type == EvaluationType.Expression => d.SourceDatasetId,
                SearchKpi s when s.Evaluation?.Type == EvaluationType.Expression => s.DatasetId,
                ObjectKpi o when o.Evaluation?.Type == EvaluationType.Expression => o.DatasetId,
                _ => null
            })
            .OfType<string>()
            .Where(id => !legacy.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
