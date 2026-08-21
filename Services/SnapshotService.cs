using System.Text.Json;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Captures, persists, lists, deletes and compares KPI snapshots. Snapshots are
/// stored as indented JSON files in the configured snapshot directory (resolved
/// under the content root when the configured path is relative).
/// </summary>
public class SnapshotService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly IWebHostEnvironment _env;
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;

    public SnapshotService(IWebHostEnvironment env, IOptionsMonitor<ActiveRolesConfig> config)
    {
        _env = env;
        _config = config;
    }

    private string SnapshotFolder
    {
        get
        {
            var configured = _config.CurrentValue.SnapshotDirectory;
            if (string.IsNullOrWhiteSpace(configured))
                configured = "App_Data/Snapshots";
            return Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(_env.ContentRootPath, configured);
        }
    }

    // -----------------------------------------------------------------------
    // Capture
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a snapshot from a fully-populated <see cref="DashboardSummary"/> by
    /// traversing the dashboard/category/KPI metadata hierarchy.
    /// </summary>
    public Snapshot Capture(DashboardSummary summary, string? label, string? createdBy, string? environment)
    {
        var snapshot = new Snapshot
        {
            Header = new SnapshotHeader
            {
                Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                CreatedUtc = DateTime.UtcNow,
                CreatedBy = createdBy,
                Environment = environment
            }
        };

        var kpiCount = 0;

        foreach (var dashboard in DashboardInfo.All)
        {
            var snapDashboard = new SnapshotDashboard
            {
                Key = dashboard.Key,
                DisplayName = dashboard.Title
            };

            foreach (var category in CategoryInfo.ForDashboard(dashboard))
            {
                if (category.IsRiskCategory)
                    continue; // risk categories are aggregations of KPIs owned elsewhere

                var snapCategory = new SnapshotCategory
                {
                    Key = category.Key,
                    DisplayName = category.DisplayName
                };

                foreach (var kpi in KpiInfo.ForCategory(category))
                {
                    var (count, error) = summary.GetKpiResult(kpi.Key);
                    snapCategory.Kpis.Add(new SnapshotKpi
                    {
                        Key = kpi.Key,
                        DisplayName = kpi.DisplayName,
                        Count = count,
                        Error = error,
                        IsRiskKpi = kpi.IsRiskKpi
                    });
                    kpiCount++;
                }

                if (snapCategory.Kpis.Count > 0)
                    snapDashboard.Categories.Add(snapCategory);
            }

            if (snapDashboard.Categories.Count > 0)
                snapshot.Dashboards.Add(snapDashboard);
        }

        snapshot.Header.KpiCount = kpiCount;
        return snapshot;
    }

    // -----------------------------------------------------------------------
    // Persistence
    // -----------------------------------------------------------------------

    public async Task SaveAsync(Snapshot snapshot)
    {
        Directory.CreateDirectory(SnapshotFolder);
        var path = Path.Combine(SnapshotFolder, snapshot.Header.Id + ".json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, _jsonOptions);
    }

    /// <summary>Returns snapshot headers only, newest first, without loading full bodies.</summary>
    public async Task<List<SnapshotHeader>> ListAsync()
    {
        var headers = new List<SnapshotHeader>();
        if (!Directory.Exists(SnapshotFolder))
            return headers;

        foreach (var file in Directory.EnumerateFiles(SnapshotFolder, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, _jsonOptions);
                if (snapshot?.Header != null)
                    headers.Add(snapshot.Header);
            }
            catch
            {
                // Skip unreadable/corrupt files rather than failing the whole list.
            }
        }

        return headers.OrderByDescending(h => h.CreatedUtc).ToList();
    }

    public async Task<Snapshot?> LoadAsync(string id)
    {
        var path = ResolvePath(id);
        if (path == null || !File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Snapshot>(stream, _jsonOptions);
    }

    public bool Delete(string id)
    {
        var path = ResolvePath(id);
        if (path == null || !File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    // -----------------------------------------------------------------------
    // Trend
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads every saved snapshot (oldest first). Unreadable/corrupt files are skipped.
    /// Shared by trend building (snapshot KPI trend and derived exposure trend).
    /// </summary>
    public async Task<List<Snapshot>> LoadAllOrderedAsync()
    {
        var snapshots = new List<Snapshot>();
        if (!Directory.Exists(SnapshotFolder))
            return snapshots;

        foreach (var file in Directory.EnumerateFiles(SnapshotFolder, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, _jsonOptions);
                if (snapshot?.Header != null)
                    snapshots.Add(snapshot);
            }
            catch
            {
                // Skip unreadable/corrupt files.
            }
        }

        return snapshots.OrderBy(s => s.Header.CreatedUtc).ToList();
    }

    /// <summary>
    /// Loads every saved snapshot (oldest first) and builds a dashboard/category/KPI
    /// time-series where each KPI carries one value per snapshot timestamp. A value is
    /// null where the KPI was not present in that particular snapshot.
    /// </summary>
    public async Task<SnapshotTrend> BuildTrendAsync()
    {
        var trend = new SnapshotTrend();

        var snapshots = await LoadAllOrderedAsync();
        if (snapshots.Count == 0)
            return trend;

        trend.Labels = snapshots.Select(s => s.Header.CreatedUtc.ToString("yyyy-MM-dd HH:mm")).ToList();

        // Index each snapshot's KPI counts by key for aligned lookups.
        var perSnapshotCounts = snapshots
            .Select(s => s.Dashboards
                .SelectMany(d => d.Categories)
                .SelectMany(c => c.Kpis)
                .GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Count, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Build the hierarchy from the latest snapshot so display names are current,
        // then fill each KPI series across all snapshots.
        var latest = snapshots[^1];
        foreach (var dashboard in latest.Dashboards)
        {
            var trendDashboard = new SnapshotTrendDashboard
            {
                Key = dashboard.Key,
                DisplayName = dashboard.DisplayName
            };

            foreach (var category in dashboard.Categories)
            {
                var trendCategory = new SnapshotTrendCategory
                {
                    Key = category.Key,
                    DisplayName = category.DisplayName
                };

                foreach (var kpi in category.Kpis)
                {
                    var series = new SnapshotTrendKpi
                    {
                        Key = kpi.Key,
                        DisplayName = kpi.DisplayName,
                        IsRiskKpi = kpi.IsRiskKpi
                    };

                    foreach (var counts in perSnapshotCounts)
                        series.Values.Add(counts.TryGetValue(kpi.Key, out var v) ? v : (int?)null);

                    trendCategory.Kpis.Add(series);
                }

                if (trendCategory.Kpis.Count > 0)
                    trendDashboard.Categories.Add(trendCategory);
            }

            if (trendDashboard.Categories.Count > 0)
                trend.Dashboards.Add(trendDashboard);
        }

        return trend;
    }

    /// <summary>Resolves a snapshot file path from an id, guarding against path traversal.</summary>
    private string? ResolvePath(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var fileName = id + ".json";
        // Reject any id that would escape the snapshot folder.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..") ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return Path.Combine(SnapshotFolder, fileName);
    }

    // -----------------------------------------------------------------------
    // Comparison
    // -----------------------------------------------------------------------

    /// <summary>Compares a baseline snapshot against another snapshot (or live values captured as a snapshot).</summary>
    public SnapshotComparison Compare(Snapshot from, Snapshot to, bool toIsCurrent = false)
    {
        var result = new SnapshotComparison
        {
            From = from.Header,
            To = to.Header,
            ToIsCurrent = toIsCurrent
        };

        // Index the "to" side KPIs by key for fast lookup.
        var toKpis = to.Dashboards
            .SelectMany(d => d.Categories)
            .SelectMany(c => c.Kpis)
            .GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seenToKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fromDashboard in from.Dashboards)
        {
            var cmpDashboard = new SnapshotComparisonDashboard
            {
                Key = fromDashboard.Key,
                DisplayName = fromDashboard.DisplayName
            };

            foreach (var fromCategory in fromDashboard.Categories)
            {
                var cmpCategory = new SnapshotComparisonCategory
                {
                    Key = fromCategory.Key,
                    DisplayName = fromCategory.DisplayName
                };

                foreach (var fromKpi in fromCategory.Kpis)
                {
                    var hasTo = toKpis.TryGetValue(fromKpi.Key, out var toKpi);
                    if (hasTo)
                        seenToKeys.Add(fromKpi.Key);

                    var row = BuildRow(fromKpi, hasTo ? toKpi!.Count : (int?)null, fromKpi.IsRiskKpi);
                    cmpCategory.Rows.Add(row);
                    Tally(result, row.Direction);
                }

                if (cmpCategory.Rows.Count > 0)
                    cmpDashboard.Categories.Add(cmpCategory);
            }

            if (cmpDashboard.Categories.Count > 0)
                result.Dashboards.Add(cmpDashboard);
        }

        // Surface KPIs that exist only on the "to" side (newly added).
        var addedByDashboard = to.Dashboards
            .SelectMany(d => d.Categories.SelectMany(c => c.Kpis.Select(k => (d, c, k))))
            .Where(x => !seenToKeys.Contains(x.k.Key));

        foreach (var group in addedByDashboard.GroupBy(x => (x.d.Key, x.d.DisplayName)))
        {
            var cmpDashboard = result.Dashboards.FirstOrDefault(d => d.Key == group.Key.Key)
                ?? AddDashboard(result, group.Key.Key, group.Key.DisplayName);

            foreach (var catGroup in group.GroupBy(x => (x.c.Key, x.c.DisplayName)))
            {
                var cmpCategory = cmpDashboard.Categories.FirstOrDefault(c => c.Key == catGroup.Key.Key)
                    ?? AddCategory(cmpDashboard, catGroup.Key.Key, catGroup.Key.DisplayName);

                foreach (var (_, _, kpi) in catGroup)
                {
                    var row = new SnapshotComparisonRow
                    {
                        Key = kpi.Key,
                        DisplayName = kpi.DisplayName,
                        FromCount = null,
                        ToCount = kpi.Count,
                        Change = kpi.Count,
                        Direction = DeltaDirection.Added,
                        Sentiment = DeltaSentiment.Neutral,
                        IsRiskKpi = kpi.IsRiskKpi
                    };
                    cmpCategory.Rows.Add(row);
                    Tally(result, row.Direction);
                }
            }
        }

        return result;
    }

    private static SnapshotComparisonDashboard AddDashboard(SnapshotComparison result, string key, string name)
    {
        var d = new SnapshotComparisonDashboard { Key = key, DisplayName = name };
        result.Dashboards.Add(d);
        return d;
    }

    private static SnapshotComparisonCategory AddCategory(SnapshotComparisonDashboard dashboard, string key, string name)
    {
        var c = new SnapshotComparisonCategory { Key = key, DisplayName = name };
        dashboard.Categories.Add(c);
        return c;
    }

    private static SnapshotComparisonRow BuildRow(SnapshotKpi fromKpi, int? toCount, bool isRisk)
    {
        var row = new SnapshotComparisonRow
        {
            Key = fromKpi.Key,
            DisplayName = fromKpi.DisplayName,
            FromCount = fromKpi.Count,
            ToCount = toCount,
            IsRiskKpi = isRisk
        };

        if (toCount == null)
        {
            row.Direction = DeltaDirection.Removed;
            row.Change = -fromKpi.Count;
            row.Sentiment = DeltaSentiment.Neutral;
            return row;
        }

        row.Change = toCount.Value - fromKpi.Count;
        if (row.Change > 0)
            row.Direction = DeltaDirection.Increase;
        else if (row.Change < 0)
            row.Direction = DeltaDirection.Decrease;
        else
            row.Direction = DeltaDirection.NoChange;

        row.Sentiment = DetermineSentiment(row.Direction, isRisk);
        return row;
    }

    /// <summary>For risk KPIs, an increase is bad and a decrease is good; other KPIs are neutral.</summary>
    private static DeltaSentiment DetermineSentiment(DeltaDirection direction, bool isRisk)
    {
        if (!isRisk)
            return DeltaSentiment.Neutral;
        return direction switch
        {
            DeltaDirection.Increase => DeltaSentiment.Bad,
            DeltaDirection.Decrease => DeltaSentiment.Good,
            _ => DeltaSentiment.Neutral
        };
    }

    private static void Tally(SnapshotComparison result, DeltaDirection direction)
    {
        switch (direction)
        {
            case DeltaDirection.Increase: result.IncreaseCount++; break;
            case DeltaDirection.Decrease: result.DecreaseCount++; break;
            case DeltaDirection.NoChange: result.NoChangeCount++; break;
            case DeltaDirection.Added: result.AddedCount++; break;
            case DeltaDirection.Removed: result.RemovedCount++; break;
        }
    }
}
