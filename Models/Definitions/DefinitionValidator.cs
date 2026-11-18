namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Checks ids, required fields and cross references of a merged definition set.</summary>
public static class DefinitionValidator
{
    public static IReadOnlyList<string> Validate(DefinitionSet set)
    {
        var errors = new List<string>();

        Duplicates(set.Categories.Select(c => c.Id), "category", errors);
        Duplicates(set.Datasets.Select(d => d.Id), "dataset", errors);
        Duplicates(set.Kpis.Select(k => k.Id), "KPI", errors);

        var categories = new HashSet<string>(set.Categories.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var datasets = new HashSet<string>(set.Datasets.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
        var kpis = new HashSet<string>(set.Kpis.Select(k => k.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var c in set.Categories)
        {
            if (string.IsNullOrWhiteSpace(c.Id)) errors.Add("A category has no id.");
            if (string.IsNullOrWhiteSpace(c.DashboardId)) errors.Add($"Category '{c.Id}' has no dashboardId.");
            ValidateCharts(c.Charts, $"category '{c.Id}'", kpis, datasets, errors);
        }

        foreach (var d in set.Datasets)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) errors.Add("A dataset has no id.");
            ValidateRetrieval(d.Retrieval, $"dataset '{d.Id}'", errors);
        }

        foreach (var k in set.Kpis)
        {
            var name = $"KPI '{k.Id}'";
            if (string.IsNullOrWhiteSpace(k.Id)) errors.Add("A KPI has no id.");
            if (!categories.Contains(k.CategoryId)) errors.Add($"{name} references unknown category '{k.CategoryId}'.");
            ValidateCharts(k.Charts, name, kpis, datasets, errors);

            switch (k)
            {
                case SearchKpi s:
                    ValidateDataset(s.DatasetId, s.Retrieval, name, datasets, errors);
                    break;
                case ObjectKpi o:
                    ValidateDataset(o.DatasetId, o.Retrieval, name, datasets, errors);
                    break;
                case DerivedKpi dk:
                    var hasDataset = !string.IsNullOrWhiteSpace(dk.SourceDatasetId);
                    var hasKpi = !string.IsNullOrWhiteSpace(dk.SourceKpiId);
                    if (hasDataset && hasKpi) errors.Add($"{name} sets both sourceDatasetId and sourceKpiId.");
                    if (hasDataset && !datasets.Contains(dk.SourceDatasetId!)) errors.Add($"{name} references unknown dataset '{dk.SourceDatasetId}'.");
                    if (hasKpi && !kpis.Contains(dk.SourceKpiId!)) errors.Add($"{name} references unknown KPI '{dk.SourceKpiId}'.");
                    break;
                case CompositeKpi ck:
                    if (ck.Sources.Count == 0) errors.Add($"{name} has no sources.");
                    foreach (var src in ck.Sources.Where(src => !kpis.Contains(src)))
                        errors.Add($"{name} references unknown KPI '{src}'.");
                    break;
            }
        }

        return errors;
    }

    private static void ValidateDataset(string datasetId, RetrievalDefinition? retrieval, string name, HashSet<string> datasets, List<string> errors)
    {
        if (retrieval is not null)
            ValidateRetrieval(retrieval, name, errors);
        else if (string.IsNullOrWhiteSpace(datasetId))
            errors.Add($"{name} has neither datasetId nor retrieval.");
        else if (!datasets.Contains(datasetId))
            errors.Add($"{name} references unknown dataset '{datasetId}'.");
    }

    private static void ValidateRetrieval(RetrievalDefinition r, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(r.Endpoint)) errors.Add($"{name} retrieval has no endpoint.");
        if (r.Target == RetrievalTarget.Objects && r.Objects.Count == 0) errors.Add($"{name} retrieval targets objects but lists none.");
        if (r.Scope is not ("base" or "one" or "sub")) errors.Add($"{name} retrieval scope '{r.Scope}' is invalid.");
    }

    private static void ValidateCharts(IEnumerable<ChartDefinition> charts, string owner, HashSet<string> kpis, HashSet<string> datasets, List<string> errors)
    {
        foreach (var chart in charts)
        {
            if (chart.SourceSplitKpiId is { } split && !kpis.Contains(split))
                errors.Add($"Chart '{chart.Id}' on {owner} references unknown KPI '{split}'.");
            foreach (var s in chart.Series)
            {
                if (s.KpiId is not null)
                {
                    if (!kpis.Contains(s.KpiId)) errors.Add($"Chart '{chart.Id}' on {owner} references unknown KPI '{s.KpiId}'.");
                }
                else if (s.DatasetId is null || s.GroupBy is null)
                    errors.Add($"Chart '{chart.Id}' on {owner} has a series without kpiId or datasetId+groupBy.");
                else if (!datasets.Contains(s.DatasetId))
                    errors.Add($"Chart '{chart.Id}' on {owner} references unknown dataset '{s.DatasetId}'.");
            }
        }
    }

    private static void Duplicates(IEnumerable<string> ids, string kind, List<string> errors)
    {
        foreach (var g in ids.GroupBy(i => i, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Duplicate {kind} id '{g.Key}'.");
    }
}
