using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ActiveRolesDashboard.Tests.Snapshots;

[Trait("Stage", "Snapshots")]
public class SnapshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ard-tests", Guid.NewGuid().ToString("N"));
    private readonly ActiveRolesConfig _config = new();
    private readonly SnapshotService _service;

    public SnapshotServiceTests()
    {
        Directory.CreateDirectory(_root);
        var monitor = new TestOptionsMonitor<ActiveRolesConfig>(_config);
        _service = new SnapshotService(new TestEnvironment(_root), monitor, new KpiExclusionService(monitor));
    }

    private string Folder => Path.Combine(_root, "App_Data", "Snapshots");

    private static Snapshot Make(DateTime created, params (string Key, int Count, bool Risk)[] kpis) => new()
    {
        Header = new SnapshotHeader { CreatedUtc = created, KpiCount = kpis.Length },
        Dashboards =
        [
            new SnapshotDashboard
            {
                Key = "D", DisplayName = "Dash",
                Categories = [new SnapshotCategory { Key = "C", DisplayName = "Cat", Kpis = kpis.Select(k => new SnapshotKpi { Key = k.Key, DisplayName = k.Key, Count = k.Count, IsRiskKpi = k.Risk }).ToList() }]
            }
        ]
    };

    [Fact]
    public void Capture_trims_label_and_records_header()
    {
        var snapshot = _service.Capture(new DashboardSummary(), "  Q1 baseline  ", @"TEST\admin", "prod");

        Assert.Equal("Q1 baseline", snapshot.Header.Label);
        Assert.Equal(@"TEST\admin", snapshot.Header.CreatedBy);
        Assert.Equal("prod", snapshot.Header.Environment);
        Assert.Equal(snapshot.Dashboards.Sum(d => d.Categories.Sum(c => c.Kpis.Count)), snapshot.Header.KpiCount);
        Assert.True(snapshot.Header.KpiCount > 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Capture_blank_label_becomes_null(string? label)
    {
        Assert.Null(_service.Capture(new DashboardSummary(), label, null, null).Header.Label);
    }

    [Fact]
    public void Capture_applies_kpi_exclusions()
    {
        _config.KpiExclusions.Global = ["svc_*"];
        var summary = new DashboardSummary
        {
            PasswordNotRequired = new ADUserAccountDetailSummary
            {
                TotalCount = 2,
                Items = [new() { Name = "svc_a", Dn = "CN=svc_a" }, new() { Name = "bob", Dn = "CN=bob" }]
            }
        };

        var kpi = _service.Capture(summary, null, null, null).Dashboards
            .SelectMany(d => d.Categories).SelectMany(c => c.Kpis).FirstOrDefault(k => k.Key == "PasswordNotRequired");

        Assert.NotNull(kpi);
        Assert.Equal(1, kpi!.Count);
    }

    [Fact]
    public async Task Save_load_list_delete_round_trip()
    {
        var snapshot = Make(DateTime.UtcNow, ("A", 3, true));
        await _service.SaveAsync(snapshot);

        var loaded = await _service.LoadAsync(snapshot.Header.Id);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Dashboards[0].Categories[0].Kpis[0].Count);
        Assert.Single(await _service.ListAsync());

        Assert.True(_service.Delete(snapshot.Header.Id));
        Assert.False(_service.Delete(snapshot.Header.Id));
        Assert.Null(await _service.LoadAsync(snapshot.Header.Id));
        Assert.Empty(await _service.ListAsync());
    }

    [Fact]
    public async Task List_is_empty_when_folder_missing()
    {
        Assert.Empty(await _service.ListAsync());
        Assert.Empty((await _service.BuildTrendAsync()).Labels);
    }

    [Fact]
    public async Task List_orders_newest_first_and_skips_corrupt_files()
    {
        await _service.SaveAsync(Make(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _service.SaveAsync(Make(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await File.WriteAllTextAsync(Path.Combine(Folder, "corrupt.json"), "{ not json");
        await File.WriteAllTextAsync(Path.Combine(Folder, "empty.json"), "");

        var list = await _service.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.True(list[0].CreatedUtc > list[1].CreatedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../appsettings")]
    [InlineData(@"..\appsettings")]
    [InlineData("sub/file")]
    [InlineData("a..b")]
    [InlineData("bad|name")]
    public async Task Path_traversal_and_invalid_ids_are_rejected(string id)
    {
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{}");

        Assert.Null(await _service.LoadAsync(id));
        Assert.False(_service.Delete(id));
        Assert.True(File.Exists(Path.Combine(_root, "appsettings.json")));
    }

    [Fact]
    public async Task Absolute_snapshot_directory_is_honoured()
    {
        var custom = Path.Combine(_root, "custom");
        _config.SnapshotDirectory = custom;

        await _service.SaveAsync(Make(DateTime.UtcNow));

        Assert.Single(Directory.GetFiles(custom, "*.json"));
    }

    [Fact]
    public void Compare_classifies_direction_and_sentiment()
    {
        var from = Make(DateTime.UtcNow, ("Up", 1, true), ("Down", 5, true), ("Same", 2, true), ("NeutralUp", 1, false), ("Gone", 4, true));
        var to = Make(DateTime.UtcNow, ("Up", 3, true), ("Down", 2, true), ("Same", 2, true), ("NeutralUp", 9, false), ("New", 7, true));

        var result = _service.Compare(from, to, toIsCurrent: true);
        var rows = result.Dashboards.SelectMany(d => d.Categories).SelectMany(c => c.Rows).ToDictionary(r => r.Key);

        Assert.True(result.ToIsCurrent);
        Assert.Equal((DeltaDirection.Increase, DeltaSentiment.Bad, 2), (rows["Up"].Direction, rows["Up"].Sentiment, rows["Up"].Change));
        Assert.Equal((DeltaDirection.Decrease, DeltaSentiment.Good, -3), (rows["Down"].Direction, rows["Down"].Sentiment, rows["Down"].Change));
        Assert.Equal((DeltaDirection.NoChange, DeltaSentiment.Neutral, 0), (rows["Same"].Direction, rows["Same"].Sentiment, rows["Same"].Change));
        Assert.Equal(DeltaSentiment.Neutral, rows["NeutralUp"].Sentiment);
        Assert.Equal((DeltaDirection.Removed, (int?)null, -4), (rows["Gone"].Direction, rows["Gone"].ToCount, rows["Gone"].Change));
        Assert.Equal((DeltaDirection.Added, (int?)null, 7), (rows["New"].Direction, rows["New"].FromCount, rows["New"].Change));
        Assert.Equal((2, 1, 1, 1, 1), (result.IncreaseCount, result.DecreaseCount, result.NoChangeCount, result.AddedCount, result.RemovedCount));
    }

    [Fact]
    public void Compare_matches_keys_case_insensitively()
    {
        var result = _service.Compare(Make(DateTime.UtcNow, ("abc", 1, false)), Make(DateTime.UtcNow, ("ABC", 1, false)));
        Assert.Equal(1, result.NoChangeCount);
        Assert.Equal(0, result.AddedCount);
    }

    [Fact]
    public void Compare_empty_snapshots_produces_empty_result()
    {
        var result = _service.Compare(new Snapshot(), new Snapshot());
        Assert.Empty(result.Dashboards);
    }

    [Fact]
    public async Task Trend_aligns_series_and_uses_null_for_missing_values()
    {
        await _service.SaveAsync(Make(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), ("A", 1, false)));
        await _service.SaveAsync(Make(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), ("A", 2, false), ("B", 5, false)));

        var trend = await _service.BuildTrendAsync();
        var kpis = trend.Dashboards.SelectMany(d => d.Categories).SelectMany(c => c.Kpis).ToDictionary(k => k.Key);

        Assert.Equal(["2024-01-01 00:00", "2024-02-01 00:00"], trend.Labels);
        Assert.Equal([1, 2], kpis["A"].Values);
        Assert.Equal([null, 5], kpis["B"].Values);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ActiveRolesDashboard";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
