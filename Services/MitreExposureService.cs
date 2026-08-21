using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Computes a MITRE ATT&CK exposure view from a <see cref="DashboardSummary"/> by
/// evaluating the curated <see cref="MitreTechniqueLibrary"/> mappings against live KPI
/// counts. This is a visibility/exposure model, not a scored compliance assessment: a
/// technique's exposure is the highest exposure of any KPI mapped to it.
/// </summary>
public class MitreExposureService
{
    public AttackExposureView Build(DashboardSummary summary)
        => Build(summary.GetKpiResult);

    /// <summary>
    /// Recomputes the exposure view from a saved snapshot's KPI counts. Because exposure
    /// is a deterministic function of the KPI counts (and the current technique thresholds),
    /// historical exposure can be derived from snapshots without persisting exposure itself.
    /// </summary>
    public AttackExposureView BuildFromSnapshot(Snapshot snapshot)
    {
        var counts = snapshot.Dashboards
            .SelectMany(d => d.Categories)
            .SelectMany(c => c.Kpis)
            .GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var view = Build(key => counts.TryGetValue(key, out var kpi)
            ? (kpi.Count, kpi.Error)
            : (0, "KPI not present in snapshot"));
        view.GeneratedAt = snapshot.Header.CreatedUtc;
        return view;
    }

    /// <summary>
    /// Core exposure computation over any KPI-count source. <paramref name="kpiLookup"/>
    /// returns the count and optional error for a KPI key (matching DashboardSummary.GetKpiResult).
    /// </summary>
    public AttackExposureView Build(Func<string, (int Count, string? Error)> kpiLookup)
    {
        var view = new AttackExposureView { GeneratedAt = DateTime.UtcNow };

        foreach (var tactic in MitreTacticInfo.Ordered)
        {
            var techniques = MitreTechniqueLibrary.ForTactic(tactic).ToList();
            if (techniques.Count == 0) continue;

            var tacticExposure = new TacticExposure { Tactic = tactic };

            foreach (var technique in techniques)
            {
                var exposure = new TechniqueExposure { Technique = technique };

                foreach (var mapping in technique.Mappings)
                {
                    var (count, error) = kpiLookup(mapping.KpiKey);
                    var contribution = new TechniqueKpiContribution
                    {
                        KpiKey = mapping.KpiKey,
                        DisplayName = ResolveKpiName(mapping.KpiKey),
                        Rationale = mapping.Rationale,
                        Count = count,
                        HasError = error != null,
                        Level = error != null ? ExposureLevel.None : LevelFor(count, mapping)
                    };
                    exposure.Contributions.Add(contribution);
                }

                exposure.Level = exposure.Contributions.Count == 0
                    ? ExposureLevel.None
                    : exposure.Contributions.Max(c => c.Level);

                tacticExposure.Techniques.Add(exposure);
            }

            // Order techniques by exposure (highest first) then by id for stable display.
            tacticExposure.Techniques = tacticExposure.Techniques
                .OrderByDescending(t => t.Level)
                .ThenBy(t => t.Technique.Id, StringComparer.Ordinal)
                .ToList();

            view.Tactics.Add(tacticExposure);
        }

        return view;
    }

    private static ExposureLevel LevelFor(int count, TechniqueKpiMapping mapping)
    {
        if (count <= 0) return ExposureLevel.None;
        if (count >= mapping.HighThreshold) return ExposureLevel.High;
        if (count >= mapping.MediumThreshold) return ExposureLevel.Medium;
        return ExposureLevel.Low;
    }

    private static string ResolveKpiName(string kpiKey) =>
        KpiInfo.All.FirstOrDefault(k => string.Equals(k.Key, kpiKey, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? kpiKey;

    // -----------------------------------------------------------------------
    // Comparison
    // -----------------------------------------------------------------------

    /// <summary>
    /// Compares a baseline exposure view against another (or the current live view),
    /// keyed by technique id. Exposure is risk-directional, so a rising level is "bad"
    /// and a falling level is "good".
    /// </summary>
    public ExposureComparison Compare(
        AttackExposureView from,
        AttackExposureView to,
        string fromLabel,
        string toLabel,
        bool toIsCurrent = false)
    {
        var result = new ExposureComparison
        {
            FromGeneratedAt = from.GeneratedAt,
            ToGeneratedAt = to.GeneratedAt,
            FromLabel = fromLabel,
            ToLabel = toLabel,
            ToIsCurrent = toIsCurrent
        };

        var toByTechnique = to.Tactics
            .SelectMany(t => t.Techniques)
            .GroupBy(t => t.Technique.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seenTo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Walk the baseline tactics in their canonical order.
        foreach (var tactic in MitreTacticInfo.Ordered)
        {
            var fromTactic = from.Tactics.FirstOrDefault(t => t.Tactic == tactic);
            var toTactic = to.Tactics.FirstOrDefault(t => t.Tactic == tactic);
            if (fromTactic == null && toTactic == null) continue;

            var cmpTactic = new ExposureComparisonTactic { Tactic = tactic };

            foreach (var fromTech in fromTactic?.Techniques ?? Enumerable.Empty<TechniqueExposure>())
            {
                var hasTo = toByTechnique.TryGetValue(fromTech.Technique.Id, out var toTech);
                if (hasTo) seenTo.Add(fromTech.Technique.Id);

                var row = BuildComparisonRow(
                    fromTech.Technique,
                    fromTech.Level,
                    hasTo ? toTech!.Level : (ExposureLevel?)null);
                cmpTactic.Rows.Add(row);
                TallyComparison(result, row.Direction);
            }

            // Techniques present only on the "to" side within this tactic.
            foreach (var toTech in (toTactic?.Techniques ?? Enumerable.Empty<TechniqueExposure>())
                         .Where(t => !seenTo.Contains(t.Technique.Id)))
            {
                seenTo.Add(toTech.Technique.Id);
                var row = new ExposureComparisonRow
                {
                    TechniqueId = toTech.Technique.Id,
                    TechniqueName = toTech.Technique.Name,
                    FromLevel = null,
                    ToLevel = toTech.Level,
                    Direction = ExposureDeltaDirection.Added,
                    Sentiment = DeltaSentiment.Neutral
                };
                cmpTactic.Rows.Add(row);
                TallyComparison(result, row.Direction);
            }

            if (cmpTactic.Rows.Count > 0)
                result.Tactics.Add(cmpTactic);
        }

        return result;
    }

    private static ExposureComparisonRow BuildComparisonRow(MitreTechnique technique, ExposureLevel fromLevel, ExposureLevel? toLevel)
    {
        var row = new ExposureComparisonRow
        {
            TechniqueId = technique.Id,
            TechniqueName = technique.Name,
            FromLevel = fromLevel,
            ToLevel = toLevel
        };

        if (toLevel == null)
        {
            row.Direction = ExposureDeltaDirection.Removed;
            row.Sentiment = DeltaSentiment.Neutral;
            return row;
        }

        if (toLevel.Value > fromLevel)
        {
            row.Direction = ExposureDeltaDirection.Increased;
            row.Sentiment = DeltaSentiment.Bad;
        }
        else if (toLevel.Value < fromLevel)
        {
            row.Direction = ExposureDeltaDirection.Decreased;
            row.Sentiment = DeltaSentiment.Good;
        }
        else
        {
            row.Direction = ExposureDeltaDirection.Unchanged;
            row.Sentiment = DeltaSentiment.Neutral;
        }

        return row;
    }

    private static void TallyComparison(ExposureComparison result, ExposureDeltaDirection direction)
    {
        switch (direction)
        {
            case ExposureDeltaDirection.Increased: result.IncreasedCount++; break;
            case ExposureDeltaDirection.Decreased: result.DecreasedCount++; break;
            case ExposureDeltaDirection.Unchanged: result.UnchangedCount++; break;
            case ExposureDeltaDirection.Added: result.AddedCount++; break;
            case ExposureDeltaDirection.Removed: result.RemovedCount++; break;
        }
    }

    // -----------------------------------------------------------------------
    // Trend (derived from saved snapshots)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds an exposure time-series by recomputing exposure for each saved snapshot
    /// (expected oldest-first). Each technique carries one numeric level (0=None..3=High)
    /// per snapshot timestamp, and aggregate High/Medium/Low counts are provided for a
    /// summary chart. The technique hierarchy is taken from the current library so display
    /// names and coverage always reflect the latest mappings.
    /// </summary>
    public ExposureTrend BuildTrend(IReadOnlyList<Snapshot> snapshotsOldestFirst)
    {
        var trend = new ExposureTrend();
        if (snapshotsOldestFirst == null || snapshotsOldestFirst.Count == 0)
            return trend;

        trend.Labels = snapshotsOldestFirst
            .Select(s => s.Header.CreatedUtc.ToString("yyyy-MM-dd HH:mm"))
            .ToList();

        // Recompute exposure per snapshot, indexed by technique id for aligned lookups.
        var perSnapshot = snapshotsOldestFirst
            .Select(s => BuildFromSnapshot(s).Tactics
                .SelectMany(t => t.Techniques)
                .GroupBy(t => t.Technique.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Level, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var technique in MitreTechniqueLibrary.All)
        {
            var series = new ExposureTrendTechnique
            {
                TechniqueId = technique.Id,
                TechniqueName = technique.Name,
                Tactic = technique.Tactic
            };

            foreach (var levels in perSnapshot)
                series.Values.Add(levels.TryGetValue(technique.Id, out var lvl) ? (int)lvl : (int?)null);

            trend.Techniques.Add(series);
        }

        // Aggregate High/Medium/Low counts per timestamp.
        foreach (var levels in perSnapshot)
        {
            trend.HighCounts.Add(levels.Values.Count(l => l == ExposureLevel.High));
            trend.MediumCounts.Add(levels.Values.Count(l => l == ExposureLevel.Medium));
            trend.LowCounts.Add(levels.Values.Count(l => l == ExposureLevel.Low));
        }

        return trend;
    }
}
