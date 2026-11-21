using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;

namespace ActiveRolesDashboard.Services.Definitions;

/// <summary>
/// Retrieves datasets described by the definitions through the Active Roles REST search.
/// Each dataset is fetched once and shared by every KPI that reads it.
/// </summary>
public sealed class DatasetCollector(DefinitionRegistry registry, ILogger<DatasetCollector> logger)
{
    /// <summary>
    /// Collects <paramref name="datasetIds"/> using <paramref name="search"/> to run each request.
    /// Datasets that cannot be collected are returned as failed results carrying the reason, so the
    /// KPIs reading them display an error rather than silently falling back.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DatasetResult>> CollectAsync(
        IEnumerable<string> datasetIds,
        ActiveRolesConfig config,
        Func<DatasetRequest, Task<List<JsonElement>>> search,
        IDirectoryLookup? lookup = null)
    {
        var requests = new List<DatasetRequest>();
        var skipped = new Dictionary<string, DatasetResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in datasetIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!registry.Datasets.TryGetValue(id, out var dataset))
            {
                logger.LogWarning("Dataset {Dataset} is not defined", id);
                skipped[id] = DatasetResult.Failed($"Dataset '{id}' is not defined");
                continue;
            }

            if (dataset.Enrichments.Count > 0 && lookup is null)
            {
                logger.LogWarning("Dataset {Dataset} needs directory lookups but none were supplied", id);
                skipped[id] = DatasetResult.Failed($"Dataset '{id}' needs directory lookups but none were supplied");
                continue;
            }

            if (DatasetRequestResolver.IsUnconfiguredRoleGroup(dataset, config))
            {
                skipped[id] = new DatasetResult([], null);
                continue;
            }

            var request = DatasetRequestResolver.Resolve(dataset, config, DatasetRequestResolver.ConsumersOf(registry, id));
            if (request is null)
            {
                logger.LogWarning("Dataset {Dataset} cannot be collected generically", id);
                skipped[id] = DatasetResult.Failed($"Dataset '{id}' cannot be collected: retrieval could not be resolved");
                continue;
            }

            requests.Add(request);
        }

        var results = await Task.WhenAll(requests.Select(async r =>
        {
            try
            {
                var items = await search(r);
                if (lookup is not null && registry.Datasets[r.DatasetId].Enrichments.Count > 0)
                    items = await DatasetEnricher.EnrichAsync(registry.Datasets[r.DatasetId], items, lookup);
                return (r.DatasetId, Result: new DatasetResult(items, null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dataset {Dataset} collection failed", r.DatasetId);
                return (r.DatasetId, Result: DatasetResult.Failed($"No data ({ex.GetType().Name}: {ex.Message})"));
            }
        }));

        var collected = results.ToDictionary(r => r.DatasetId, r => r.Result, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, result) in skipped) collected[id] = result;
        return collected;
    }
}
