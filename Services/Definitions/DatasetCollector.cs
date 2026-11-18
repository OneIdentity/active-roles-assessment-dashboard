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
    /// Datasets that cannot be collected generically are omitted from the result.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DatasetResult>> CollectAsync(
        IEnumerable<string> datasetIds,
        ActiveRolesConfig config,
        Func<DatasetRequest, Task<List<JsonElement>>> search)
    {
        var requests = new List<DatasetRequest>();
        foreach (var id in datasetIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!registry.Datasets.TryGetValue(id, out var dataset))
            {
                logger.LogWarning("Dataset {Dataset} is not defined", id);
                continue;
            }

            var request = DatasetRequestResolver.Resolve(dataset, config, DatasetRequestResolver.ConsumersOf(registry, id));
            if (request is null)
            {
                logger.LogDebug("Dataset {Dataset} cannot be collected generically yet", id);
                continue;
            }

            requests.Add(request);
        }

        var results = await Task.WhenAll(requests.Select(async r =>
        {
            try
            {
                return (r.DatasetId, Result: new DatasetResult(await search(r), null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dataset {Dataset} collection failed", r.DatasetId);
                return (r.DatasetId, Result: DatasetResult.Failed($"No data ({ex.GetType().Name}: {ex.Message})"));
            }
        }));

        return results.ToDictionary(r => r.DatasetId, r => r.Result, StringComparer.OrdinalIgnoreCase);
    }
}
