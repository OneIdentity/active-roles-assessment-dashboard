using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services.PermissionTracker;

/// <summary>A background Permission Tracker scan owned by one user.</summary>
public sealed class PermissionScanJob
{
    private readonly object _gate = new();
    internal readonly CancellationTokenSource Cts;
    internal readonly TaskCompletionSource<bool> Confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PermissionScanJob(string owner, PermissionScanRequest request, CancellationToken shutdown)
    {
        Owner = owner;
        Request = request;
        Cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Owner { get; }
    public PermissionScanRequest Request { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; private set; }
    public PermissionScanStatus Status { get; private set; } = PermissionScanStatus.Queued;
    public string Phase { get; private set; } = "Queued";
    public int TotalObjects { get; private set; }
    public int ProcessedObjects => Volatile.Read(ref _processed);
    public string? Error { get; private set; }
    public PermissionScanResult Result { get; } = new();

    private int _processed;

    public bool IsActive => Status is PermissionScanStatus.Queued or PermissionScanStatus.Enumerating
        or PermissionScanStatus.AwaitingConfirmation or PermissionScanStatus.Running;

    internal void SetPhase(PermissionScanStatus status, string phase) { lock (_gate) { if (IsActive) { Status = status; Phase = phase; } } }
    internal void SetTotal(int total) => TotalObjects = total;
    internal void IncrementProcessed() => Interlocked.Increment(ref _processed);

    internal void Finish(PermissionScanStatus status, string? error = null)
    {
        lock (_gate)
        {
            if (!IsActive) return;
            Status = status;
            Phase = status.ToString();
            Error = error;
            FinishedAt = DateTimeOffset.UtcNow;
        }
    }
}

/// <summary>
/// Runs Permission Tracker scans in the background: one active scan per user, a server-wide
/// concurrency limit, bounded parallel reads, an object cap with partial results, a confirmation
/// gate above a threshold, cancellation, and time-limited in-memory result retention.
/// </summary>
public sealed class PermissionScanJobManager : IHostedService, IDisposable
{
    private readonly IPermissionScanDataSource _source;
    private readonly IOptionsMonitor<PermissionTrackerOptions> _options;
    private readonly ILogger<PermissionScanJobManager> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, PermissionScanJob> _jobs = new();
    private readonly SemaphoreSlim _serverSlots;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startGate = new();

    public PermissionScanJobManager(IPermissionScanDataSource source, IOptionsMonitor<PermissionTrackerOptions> options,
        ILogger<PermissionScanJobManager> logger, TimeProvider? time = null)
    {
        _source = source;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _serverSlots = new SemaphoreSlim(Math.Max(1, options.CurrentValue.MaxConcurrentScans));
    }

    /// <summary>Starts a scan, or returns null when the user already has an active scan.</summary>
    public PermissionScanJob? Start(string owner, string userToken, PermissionScanRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        if (!request.IncludeActiveDirectory && !request.IncludeActiveRoles)
            throw new ArgumentException("At least one source must be selected.", nameof(request));

        PruneExpired();
        PermissionScanJob job;
        lock (_startGate)
        {
            if (_jobs.Values.Any(j => j.IsActive && Same(j.Owner, owner))) return null;
            job = new PermissionScanJob(owner, request, _shutdown.Token);
            _jobs[job.Id] = job;
        }
        _ = Task.Run(() => RunAsync(job, userToken));
        return job;
    }

    /// <summary>Returns a job only to its owner.</summary>
    public PermissionScanJob? Get(string jobId, string owner)
    {
        PruneExpired();
        return _jobs.TryGetValue(jobId, out var j) && Same(j.Owner, owner) ? j : null;
    }

    /// <summary>Most recent job for the user (active or retained).</summary>
    public PermissionScanJob? GetLatest(string owner)
    {
        PruneExpired();
        return _jobs.Values.Where(j => Same(j.Owner, owner)).OrderByDescending(j => j.StartedAt).FirstOrDefault();
    }

    public bool Cancel(string jobId, string owner)
    {
        var job = Get(jobId, owner);
        if (job is null || !job.IsActive) return false;
        job.Cts.Cancel();
        job.Confirmation.TrySetResult(false);
        return true;
    }

    /// <summary>Continues a scan paused above the confirmation threshold.</summary>
    public bool Confirm(string jobId, string owner)
    {
        var job = Get(jobId, owner);
        return job is { Status: PermissionScanStatus.AwaitingConfirmation } && job.Confirmation.TrySetResult(true);
    }

    internal void PruneExpired()
    {
        var cutoff = _time.GetUtcNow() - TimeSpan.FromMinutes(_options.CurrentValue.ResultRetentionMinutes);
        foreach (var (id, job) in _jobs)
            if (!job.IsActive && job.FinishedAt < cutoff && _jobs.TryRemove(id, out var removed))
                removed.Cts.Dispose();
    }

    private async Task RunAsync(PermissionScanJob job, string userToken)
    {
        var ct = job.Cts.Token;
        var acquired = false;
        try
        {
            await _serverSlots.WaitAsync(ct);
            acquired = true;
            var opts = _options.CurrentValue;

            if (job.Request.IncludeActiveDirectory)
                await ScanActiveDirectoryAsync(job, userToken, opts, ct);

            if (job.Request.IncludeActiveRoles)
            {
                job.SetPhase(PermissionScanStatus.Running, "ActiveRoles");
                try
                {
                    var links = await _source.LoadAccessTemplateLinksAsync(ct);
                    job.Result.ArEntries.AddRange(ArPermissionCollector.Expand(links));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Permission Tracker: Active Roles delegation collection failed");
                    job.Result.ArError = ex.Message;
                }
            }

            job.SetPhase(PermissionScanStatus.Running, "ResolvingTrustees");
            await ResolveTrusteesAsync(job, userToken, opts, ct);
            job.Finish(PermissionScanStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            job.Finish(PermissionScanStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Permission Tracker scan {JobId} failed", job.Id);
            job.Finish(PermissionScanStatus.Failed, ex.Message);
        }
        finally
        {
            if (acquired) _serverSlots.Release();
        }
    }

    private async Task ScanActiveDirectoryAsync(PermissionScanJob job, string userToken, PermissionTrackerOptions opts, CancellationToken ct)
    {
        job.SetPhase(PermissionScanStatus.Enumerating, "Enumerating");
        var baseDn = string.IsNullOrWhiteSpace(job.Request.BaseDn) ? _source.DefaultBaseDn : job.Request.BaseDn!;
        var objects = await _source.EnumerateAsync(userToken, baseDn, job.Request.IncludeAllObjects, ct);

        if (objects.Count > opts.MaxObjects)
        {
            objects = objects.Take(opts.MaxObjects).ToList();
            job.Result.Truncated = true;
        }
        job.SetTotal(objects.Count);

        if (objects.Count > opts.ConfirmationThreshold)
        {
            job.SetPhase(PermissionScanStatus.AwaitingConfirmation, "AwaitingConfirmation");
            if (!await job.Confirmation.Task.WaitAsync(ct)) throw new OperationCanceledException(ct);
        }

        job.SetPhase(PermissionScanStatus.Running, "ReadingPermissions");
        var defaults = new ConcurrentDictionary<string, Task<HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        Task<HashSet<string>> DefaultsFor(PermissionScanObject obj, CancellationToken token) =>
            defaults.GetOrAdd(obj.ObjectClass, async cls =>
            {
                var keys = DefaultSecurityDescriptors.InstallDefaultKeys(cls);
                try { keys.UnionWith(DefaultSecurityDescriptors.ParseKeys(await _source.GetDefaultSecurityDescriptorAsync(userToken, obj.Dn, cls, token))); }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
                return keys;
            });
        var results = new AdObjectPermissions?[objects.Count];
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, objects.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, opts.ParallelReadsPerScan), CancellationToken = ct },
                async (i, token) =>
                {
                    var obj = objects[i];
                    try
                    {
                        var sd = await _source.GetSecurityDescriptorAsync(userToken, obj.ObjectGuid, token);
                        var entries = AdAceParser.Parse(sd);
                        if (entries.Count > 0 && obj.ObjectClass.Length > 0)
                            entries = DefaultSecurityDescriptors.Mark(entries, await DefaultsFor(obj, token));
                        results[i] = new AdObjectPermissions(obj, entries, null);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        results[i] = new AdObjectPermissions(obj, [], ex.Message);
                    }
                    job.IncrementProcessed();
                });
        }
        finally
        {
            // Keep whatever was read so a cancelled scan still exposes partial results.
            job.Result.AdObjects.AddRange(results.Where(r => r is not null)!);
        }
    }

    private async Task ResolveTrusteesAsync(PermissionScanJob job, string userToken, PermissionTrackerOptions opts, CancellationToken ct)
    {
        var sids = job.Result.AdObjects.SelectMany(o => o.Entries).Select(e => e.TrusteeSid)
            .Concat(job.Result.ArEntries.Select(e => e.TrusteeSid))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var names = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(sids,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, opts.ParallelReadsPerScan), CancellationToken = ct },
            async (sid, token) =>
            {
                try { names[sid] = await _source.ResolveTrusteeNameAsync(userToken, sid, token); }
                catch (Exception ex) when (ex is not OperationCanceledException) { names[sid] = sid; }
            });
        foreach (var (k, v) in names) job.Result.TrusteeNames[k] = v;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        foreach (var job in _jobs.Values) job.Confirmation.TrySetResult(false);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _serverSlots.Dispose();
    }
}
