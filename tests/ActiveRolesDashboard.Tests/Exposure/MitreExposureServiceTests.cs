using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Exposure;

[Trait("Stage", "Exposure")]
public class MitreExposureServiceTests
{
    private readonly ActiveRolesConfig _config = new();
    private readonly MitreExposureService _service;

    public MitreExposureServiceTests()
    {
        var monitor = new TestOptionsMonitor<ActiveRolesConfig>(_config);
        _service = new MitreExposureService(new KpiThresholdService(monitor), new KpiExclusionService(monitor));
    }

    private static Func<string, (int Count, string? Error)> Lookup(Dictionary<string, int>? counts = null, params string[] errored) =>
        key => errored.Contains(key, StringComparer.OrdinalIgnoreCase)
            ? (0, "query failed")
            : (counts != null && counts.TryGetValue(key, out var c) ? c : 0, null);

    private static TechniqueExposure Technique(AttackExposureView view, string id) =>
        view.Tactics.SelectMany(t => t.Techniques).Single(t => t.Technique.Id == id);

    private static Snapshot SnapshotOf(DateTime created, params (string Key, int Count)[] kpis) => new()
    {
        Header = new SnapshotHeader { CreatedUtc = created },
        Dashboards = [new SnapshotDashboard { Key = "D", Categories = [new SnapshotCategory { Key = "C", Kpis = kpis.Select(k => new SnapshotKpi { Key = k.Key, Count = k.Count }).ToList() }] }]
    };

    // --- Library integrity ------------------------------------------------------

    [Fact]
    public void Library_technique_ids_are_unique()
    {
        var ids = MitreTechniqueLibrary.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Library_mappings_have_valid_thresholds_and_rationale()
    {
        foreach (var technique in MitreTechniqueLibrary.All)
        {
            Assert.NotEmpty(technique.Mappings);
            Assert.False(string.IsNullOrWhiteSpace(technique.Name));
            foreach (var m in technique.Mappings)
            {
                Assert.False(string.IsNullOrWhiteSpace(m.KpiKey));
                Assert.False(string.IsNullOrWhiteSpace(m.Rationale));
                Assert.True(m.MediumThreshold >= 1, $"{technique.Id}/{m.KpiKey} medium");
                Assert.True(m.HighThreshold >= m.MediumThreshold, $"{technique.Id}/{m.KpiKey} high<medium");
            }
        }
    }

    [Fact]
    public void Every_library_tactic_is_in_display_order()
    {
        Assert.All(MitreTechniqueLibrary.All, t => Assert.Contains(t.Tactic, MitreTacticInfo.Ordered));
    }

    // --- Build ------------------------------------------------------------------

    [Fact]
    public void All_zero_counts_produce_no_exposure()
    {
        var view = _service.Build(Lookup());

        Assert.Equal(MitreTechniqueLibrary.All.Count, view.TotalTechniques);
        Assert.Equal(0, view.CoveredTechniques);
        Assert.Equal((0, 0, 0), (view.HighCount, view.MediumCount, view.LowCount));
        Assert.All(view.Tactics.SelectMany(t => t.Techniques), t => Assert.True(t.HasData));
    }

    [Fact]
    public void Tactics_follow_canonical_order_and_skip_empty_tactics()
    {
        var view = _service.Build(Lookup());
        var expected = MitreTacticInfo.Ordered.Where(t => MitreTechniqueLibrary.ForTactic(t).Any()).ToList();
        Assert.Equal(expected, view.Tactics.Select(t => t.Tactic).ToList());
    }

    // T1558.004 AS-REP Roasting: NoKerberosPreauth Medium=1, High=5 (Low band is empty when Medium=1).
    [Theory]
    [InlineData(0, ExposureLevel.None)]
    [InlineData(1, ExposureLevel.Medium)]
    [InlineData(4, ExposureLevel.Medium)]
    [InlineData(5, ExposureLevel.High)]
    [InlineData(10_000, ExposureLevel.High)]
    public void Single_mapping_level_boundaries(int count, ExposureLevel expected)
    {
        var view = _service.Build(Lookup(new() { ["NoKerberosPreauth"] = count }));
        Assert.Equal(expected, Technique(view, "T1558.004").Level);
    }

    // T1069: EmptyGroups Medium=10, High=50 exercises the Low band.
    [Theory]
    [InlineData(1, ExposureLevel.Low)]
    [InlineData(9, ExposureLevel.Low)]
    [InlineData(10, ExposureLevel.Medium)]
    [InlineData(49, ExposureLevel.Medium)]
    [InlineData(50, ExposureLevel.High)]
    public void Low_band_boundaries(int count, ExposureLevel expected)
    {
        var view = _service.Build(Lookup(new() { ["EmptyGroups"] = count }));
        Assert.Equal(expected, Technique(view, "T1069").Level);
    }

    [Fact]
    public void Technique_level_is_highest_contribution()
    {
        var view = _service.Build(Lookup(new() { ["EmptyGroups"] = 1, ["NoGroupOwner"] = 30 }));
        var t = Technique(view, "T1069");

        Assert.Equal(ExposureLevel.High, t.Level);
        Assert.Contains(t.Contributions, c => c.KpiKey == "EmptyGroups" && c.Level == ExposureLevel.Low);
    }

    [Fact]
    public void Errored_kpis_contribute_none_and_flag_no_data()
    {
        var view = _service.Build(Lookup(new() { ["NoKerberosPreauth"] = 100 }, "NoKerberosPreauth"));
        var t = Technique(view, "T1558.004");

        Assert.Equal(ExposureLevel.None, t.Level);
        Assert.True(t.Contributions[0].HasError);
        Assert.False(t.HasData);
    }

    [Fact]
    public void Techniques_within_tactic_are_ordered_by_level_then_id()
    {
        var view = _service.Build(Lookup(new() { ["NoKerberosPreauth"] = 5, ["SpnUserAccounts"] = 1 }));
        var credential = view.Tactics.Single(t => t.Tactic == MitreTactic.CredentialAccess).Techniques;

        Assert.Equal("T1558.004", credential[0].Technique.Id);
        Assert.Equal("T1558.003", credential[1].Technique.Id);
        Assert.Equal(ExposureLevel.High, view.Tactics.Single(t => t.Tactic == MitreTactic.CredentialAccess).MaxLevel);
        var rest = credential.Skip(2).Select(t => t.Technique.Id).ToList();
        Assert.Equal(rest.OrderBy(i => i, StringComparer.Ordinal), rest);
    }

    [Fact]
    public void Configured_thresholds_override_library_defaults()
    {
        _config.KpiThresholds["NoKerberosPreauth"] = new KpiThresholdConfig { Medium = 50, High = 100 };

        var view = _service.Build(Lookup(new() { ["NoKerberosPreauth"] = 10 }));

        Assert.Equal(ExposureLevel.Low, Technique(view, "T1558.004").Level);
    }

    [Fact]
    public void Exclusions_reduce_exposure_from_summary()
    {
        _config.KpiExclusions.Global = ["svc_*"];
        var summary = new DashboardSummary
        {
            PasswordNotRequired = new ADUserAccountDetailSummary
            {
                TotalCount = 1,
                Items = [new() { Name = "svc_legacy", Dn = "CN=svc_legacy" }]
            }
        };

        Assert.Equal(ExposureLevel.None, Technique(_service.Build(summary), "T1110").Level);

        _config.KpiExclusions.Global = [];
        Assert.Equal(ExposureLevel.High, Technique(_service.Build(summary), "T1110").Level);
    }

    [Fact]
    public void Contribution_display_name_falls_back_to_key()
    {
        var view = _service.Build(Lookup());
        Assert.All(view.Tactics.SelectMany(t => t.Techniques).SelectMany(t => t.Contributions),
            c => Assert.False(string.IsNullOrWhiteSpace(c.DisplayName)));
    }

    // --- Snapshot-derived -------------------------------------------------------

    [Fact]
    public void BuildFromSnapshot_uses_snapshot_counts_and_timestamp()
    {
        var created = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var view = _service.BuildFromSnapshot(SnapshotOf(created, ("nokerberospreauth", 5)));

        Assert.Equal(created, view.GeneratedAt);
        Assert.Equal(ExposureLevel.High, Technique(view, "T1558.004").Level);
    }

    [Fact]
    public void BuildFromSnapshot_treats_missing_kpis_as_errors()
    {
        var view = _service.BuildFromSnapshot(SnapshotOf(DateTime.UtcNow));

        Assert.Equal(0, view.CoveredTechniques);
        Assert.All(view.Tactics.SelectMany(t => t.Techniques), t => Assert.False(t.HasData));
    }

    [Fact]
    public void BuildFromSnapshot_tolerates_duplicate_kpi_keys()
    {
        var view = _service.BuildFromSnapshot(SnapshotOf(DateTime.UtcNow, ("NoKerberosPreauth", 5), ("NOKERBEROSPREAUTH", 0)));
        Assert.Equal(ExposureLevel.High, Technique(view, "T1558.004").Level);
    }

    // --- Comparison -------------------------------------------------------------

    [Fact]
    public void Compare_classifies_increase_decrease_unchanged()
    {
        var from = _service.Build(Lookup(new() { ["NoKerberosPreauth"] = 1, ["SpnUserAccounts"] = 10 }));
        var to = _service.Build(Lookup(new() { ["NoKerberosPreauth"] = 5, ["SpnUserAccounts"] = 1 }));

        var result = _service.Compare(from, to, "Jan", "Now", toIsCurrent: true);
        var rows = result.Tactics.SelectMany(t => t.Rows).ToDictionary(r => r.TechniqueId);

        Assert.Equal(("Jan", "Now", true), (result.FromLabel, result.ToLabel, result.ToIsCurrent));
        Assert.Equal((ExposureDeltaDirection.Increased, DeltaSentiment.Bad), (rows["T1558.004"].Direction, rows["T1558.004"].Sentiment));
        Assert.Equal((ExposureDeltaDirection.Decreased, DeltaSentiment.Good), (rows["T1558.003"].Direction, rows["T1558.003"].Sentiment));
        Assert.Equal((ExposureDeltaDirection.Unchanged, DeltaSentiment.Neutral), (rows["T1110"].Direction, rows["T1110"].Sentiment));
        Assert.Equal(MitreTechniqueLibrary.All.Count, result.IncreasedCount + result.DecreasedCount + result.UnchangedCount);
        Assert.Equal((0, 0), (result.AddedCount, result.RemovedCount));
    }

    [Fact]
    public void Compare_reports_added_and_removed_techniques()
    {
        var full = _service.Build(Lookup());
        var empty = new AttackExposureView();

        var added = _service.Compare(empty, full, "a", "b");
        var removed = _service.Compare(full, empty, "a", "b");

        Assert.Equal(MitreTechniqueLibrary.All.Count, added.AddedCount);
        Assert.All(added.Tactics.SelectMany(t => t.Rows), r => Assert.Null(r.FromLevel));
        Assert.Equal(MitreTechniqueLibrary.All.Count, removed.RemovedCount);
        Assert.All(removed.Tactics.SelectMany(t => t.Rows), r => Assert.Equal(DeltaSentiment.Neutral, r.Sentiment));
    }

    [Fact]
    public void Compare_two_empty_views_is_empty()
    {
        var result = _service.Compare(new AttackExposureView(), new AttackExposureView(), "", "");
        Assert.Empty(result.Tactics);
    }

    // --- Trend ------------------------------------------------------------------

    [Fact]
    public void Trend_is_empty_for_no_snapshots()
    {
        Assert.Empty(_service.BuildTrend([]).Labels);
        Assert.Empty(_service.BuildTrend(null!).Techniques);
    }

    [Fact]
    public void Trend_tracks_levels_and_aggregates_per_snapshot()
    {
        var snapshots = new[]
        {
            SnapshotOf(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), ("NoKerberosPreauth", 0), ("EmptyGroups", 1)),
            SnapshotOf(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), ("NoKerberosPreauth", 5), ("EmptyGroups", 10))
        };

        var trend = _service.BuildTrend(snapshots);

        Assert.Equal(["2024-01-01 00:00", "2024-02-01 00:00"], trend.Labels);
        Assert.Equal(MitreTechniqueLibrary.All.Count, trend.Techniques.Count);
        Assert.Equal([0, 3], trend.Techniques.Single(t => t.TechniqueId == "T1558.004").Values);
        Assert.Equal([1, 2], trend.Techniques.Single(t => t.TechniqueId == "T1069").Values);
        Assert.Equal([0, 1], trend.HighCounts);
        Assert.Equal([0, 1], trend.MediumCounts);
        Assert.Equal([1, 0], trend.LowCounts);
    }
}
