using System.Security.AccessControl;
using ActiveRolesDashboard.Services.PermissionTracker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.PermissionTracker;

[Trait("Stage", "PermissionTracker")]
public class PermissionScanJobManagerTests
{
    private const string Sid = "S-1-5-21-1-2-3-1200";

    private sealed class Monitor(PermissionTrackerOptions value) : IOptionsMonitor<PermissionTrackerOptions>
    {
        public PermissionTrackerOptions CurrentValue => value;
        public PermissionTrackerOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PermissionTrackerOptions, string?> listener) => null;
    }

    private sealed class FakeSource(int objectCount) : IPermissionScanDataSource
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Blocking { get; init; }
        public string DefaultBaseDn => "DC=test,DC=local";

        public Task<IReadOnlyList<PermissionScanObject>> EnumerateAsync(string t, string b, bool all, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PermissionScanObject>>(Enumerable.Range(0, objectCount)
                .Select(i => new PermissionScanObject(Guid.NewGuid().ToString(), $"OU={i},{b}", $"OU{i}", "organizationalUnit")).ToList());

        public async Task<RawSecurityDescriptor?> GetSecurityDescriptorAsync(string t, string g, CancellationToken ct)
        {
            if (Blocking) await Gate.Task.WaitAsync(ct);
            return new RawSecurityDescriptor($"O:DAG:DAD:(A;;GA;;;{Sid})");
        }

        public Task<IReadOnlyList<ArTemplateLinkRecord>> LoadAccessTemplateLinksAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ArTemplateLinkRecord>>(
                [new("l", "OU=Sales,DC=test,DC=local", "CN=T", "T", [Sid], ["A;CI;RP;;;"])]);

        public Task<string> ResolveTrusteeNameAsync(string t, string sid, CancellationToken ct) => Task.FromResult("Help Desk");
        public Task<string?> GetDefaultSecurityDescriptorAsync(string t, string dn, string cls, CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private static PermissionScanJobManager Create(IPermissionScanDataSource src, PermissionTrackerOptions? o = null) =>
        new(src, new Monitor(o ?? new PermissionTrackerOptions()), NullLogger<PermissionScanJobManager>.Instance);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    [Fact]
    public async Task Scan_completes_with_ad_and_ar_results_and_resolved_names()
    {
        using var mgr = Create(new FakeSource(3));
        var job = mgr.Start("alice", "tok", new PermissionScanRequest())!;
        await WaitUntil(() => !job.IsActive);

        Assert.Equal(PermissionScanStatus.Completed, job.Status);
        Assert.Equal(3, job.Result.AdObjects.Count);
        Assert.Equal(3, job.ProcessedObjects);
        Assert.NotEmpty(job.Result.ArEntries);
        Assert.Equal("Help Desk", job.Result.TrusteeName(Sid));
    }

    [Fact]
    public async Task Only_one_active_scan_per_user_and_jobs_are_owner_scoped()
    {
        var src = new FakeSource(2) { Blocking = true };
        using var mgr = Create(src);
        var job = mgr.Start("alice", "tok", new PermissionScanRequest())!;

        Assert.Null(mgr.Start("ALICE", "tok", new PermissionScanRequest()));
        Assert.Null(mgr.Get(job.Id, "bob"));
        Assert.NotNull(mgr.Get(job.Id, "alice"));

        src.Gate.SetResult();
        await WaitUntil(() => !job.IsActive);
        Assert.NotNull(mgr.Start("alice", "tok", new PermissionScanRequest { IncludeActiveDirectory = false }));
    }

    [Fact]
    public async Task Cancel_stops_scan_and_reports_cancelled()
    {
        var src = new FakeSource(5) { Blocking = true };
        using var mgr = Create(src);
        var job = mgr.Start("alice", "tok", new PermissionScanRequest())!;
        await WaitUntil(() => job.Status == PermissionScanStatus.Running);

        Assert.True(mgr.Cancel(job.Id, "alice"));
        await WaitUntil(() => !job.IsActive);
        Assert.Equal(PermissionScanStatus.Cancelled, job.Status);
    }

    [Fact]
    public async Task Object_cap_truncates_and_threshold_requires_confirmation()
    {
        using var mgr = Create(new FakeSource(10), new PermissionTrackerOptions { MaxObjects = 6, ConfirmationThreshold = 4 });
        var job = mgr.Start("alice", "tok", new PermissionScanRequest { IncludeActiveRoles = false })!;
        await WaitUntil(() => job.Status == PermissionScanStatus.AwaitingConfirmation);

        Assert.True(mgr.Confirm(job.Id, "alice"));
        await WaitUntil(() => !job.IsActive);
        Assert.Equal(PermissionScanStatus.Completed, job.Status);
        Assert.True(job.Result.Truncated);
        Assert.Equal(6, job.Result.AdObjects.Count);
    }

    [Fact]
    public void Request_without_sources_is_rejected()
    {
        using var mgr = Create(new FakeSource(0));
        Assert.Throws<ArgumentException>(() =>
            mgr.Start("alice", "tok", new PermissionScanRequest { IncludeActiveDirectory = false, IncludeActiveRoles = false }));
    }

    [Fact]
    public async Task Report_builder_produces_ad_and_ar_sections_exportable_in_all_formats()
    {
        using var mgr = Create(new FakeSource(2));
        var job = mgr.Start("alice", "tok", new PermissionScanRequest())!;
        await WaitUntil(() => !job.IsActive);

        var model = PermissionTrackerReportBuilder.Build(job, new(false, false, false), "alice", k => k);

        Assert.Equal(2, model.Sections.Count);
        var adTable = model.Sections[0].Tables[0];
        Assert.Equal(2, adTable.Rows.Count);
        Assert.All(adTable.Rows, r => Assert.Equal("Help Desk", r[1]));
        Assert.NotEmpty(model.Sections[1].Tables[0].Rows);

        Assert.NotEmpty(new ActiveRolesDashboard.Services.Reporting.ExcelReportExporter().Export(model));
        Assert.NotEmpty(new ActiveRolesDashboard.Services.Reporting.WordReportExporter().Export(model));
    }

    [Fact]
    public async Task Report_builder_applies_filters()
    {
        using var mgr = Create(new FakeSource(2));
        var job = mgr.Start("alice", "tok", new PermissionScanRequest())!;
        await WaitUntil(() => !job.IsActive);

        var all = PermissionTrackerReportBuilder.Build(job, new(false, false, false), "alice", k => k);
        var risky = PermissionTrackerReportBuilder.Build(job, new(true, false, false), "alice", k => k);

        var allRows = all.Sections[0].Tables[0].Rows;
        Assert.All(risky.Sections[0].Tables[0].Rows, r => Assert.Equal("HighRisk", r[6]));
        Assert.Equal(allRows.Count(r => r[6] == "HighRisk"), risky.Sections[0].Tables[0].Rows.Count);
    }
}
