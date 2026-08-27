using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Lifecycle state of the shared dashboard superset cache. Surfaced to the login/wait
/// screen so it can render a "Building cache…" message until the first load completes.
/// </summary>
public enum CacheState
{
    /// <summary>No load has been attempted yet.</summary>
    NotStarted = 0,

    /// <summary>The initial superset load is in progress; no snapshot is available yet.</summary>
    Loading = 1,

    /// <summary>A snapshot is available and current.</summary>
    Ready = 2,

    /// <summary>A refresh is in progress but the previous snapshot is still being served.</summary>
    Refreshing = 3,

    /// <summary>The most recent load attempt failed. A previous snapshot may or may not exist.</summary>
    Faulted = 4
}

/// <summary>
/// Immutable point-in-time snapshot of the service-account-collected dashboard superset.
/// Published atomically by <see cref="DashboardCacheHolder"/>. Treat all contents as read-only;
/// build a new instance and swap the reference rather than mutating in place.
/// </summary>
public sealed class DashboardSupersetSnapshot
{
    public DashboardSupersetSnapshot(DashboardSummary summary, DateTimeOffset collectedAtUtc)
    {
        Summary = summary;
        CollectedAtUtc = collectedAtUtc;
    }

    /// <summary>The unfiltered superset summary collected with the service-account identity.</summary>
    public DashboardSummary Summary { get; }

    /// <summary>UTC timestamp at which this superset was collected.</summary>
    public DateTimeOffset CollectedAtUtc { get; }
}

/// <summary>
/// Thread-safe singleton holder for the shared dashboard superset. Readers get the current
/// immutable snapshot via <see cref="Current"/>; the background loader publishes a new snapshot
/// with <see cref="Publish"/> (atomic reference swap) and updates <see cref="State"/> as the
/// load progresses. Never blocks readers during a refresh — the previous snapshot remains served
/// until the new one is published.
/// </summary>
public sealed class DashboardCacheHolder
{
    private volatile DashboardSupersetSnapshot? _current;
    private volatile CacheState _state = CacheState.NotStarted;
    private volatile string? _lastError;
    private volatile ArPermissionModel _permissionModel = ArPermissionModel.Empty;

    // Monotonic counter incremented every time a refresh attempt COMPLETES (successfully or not).
    // The client polls this after an admin-triggered refresh to detect that the attempt finished,
    // then inspects LastRefreshFailed to decide whether to show a success or an error toast.
    private int _refreshSequence;
    private volatile bool _lastRefreshFailed;

    // Live Entra membership-collection progress, observable while the superset is still being
    // built (before the atomic snapshot publish). Lets the login/badge path show a real
    // server-side countdown instead of falling back to per-session client loading. Written only
    // by the background collector; read by request threads. Ints are updated via Volatile to keep
    // reads consistent without locking.
    private volatile bool _membershipLoading;
    private int _membershipLoadedCount;
    private int _membershipTotalCount;

    /// <summary>The most recently published snapshot, or null if none has been published yet.</summary>
    public DashboardSupersetSnapshot? Current => _current;

    /// <summary>The AR permission model captured alongside the current snapshot (service-account view).</summary>
    public ArPermissionModel PermissionModel => _permissionModel;

    /// <summary>Current lifecycle state of the cache.</summary>
    public CacheState State => _state;

    /// <summary>True once at least one snapshot has been published and is being served.</summary>
    public bool IsReady => _current is not null && (_state == CacheState.Ready || _state == CacheState.Refreshing);

    /// <summary>Message from the last failed load attempt, if any.</summary>
    public string? LastError => _lastError;

    /// <summary>UTC time the current snapshot was collected, if a snapshot exists.</summary>
    public DateTimeOffset? CollectedAtUtc => _current?.CollectedAtUtc;

    /// <summary>
    /// Monotonically increasing counter incremented each time a refresh attempt completes
    /// (whether it succeeded or faulted). Clients capture this value before triggering a manual
    /// refresh and poll until it changes to know the attempt has finished.
    /// </summary>
    public int RefreshSequence => Volatile.Read(ref _refreshSequence);

    /// <summary>True if the most recently completed refresh attempt failed.</summary>
    public bool LastRefreshFailed => _lastRefreshFailed;

    /// <summary>True while the background collector is actively loading Entra group membership.</summary>
    public bool MembershipLoading => _membershipLoading;

    /// <summary>Number of Entra groups whose membership has been loaded so far in the current collection.</summary>
    public int MembershipLoadedCount => Volatile.Read(ref _membershipLoadedCount);

    /// <summary>Total number of Entra groups whose membership will be loaded in the current collection.</summary>
    public int MembershipTotalCount => Volatile.Read(ref _membershipTotalCount);

    /// <summary>
    /// Marks the start of an Entra membership-loading pass with the given total group count. Resets
    /// the loaded counter to zero. Called by the background collector before it begins loading.
    /// </summary>
    public void BeginMembershipLoading(int totalGroups)
    {
        Volatile.Write(ref _membershipTotalCount, Math.Max(0, totalGroups));
        Volatile.Write(ref _membershipLoadedCount, 0);
        _membershipLoading = true;
    }

    /// <summary>Reports incremental membership-loading progress (monotonic high-water mark).</summary>
    public void ReportMembershipProgress(int loadedCount)
    {
        var current = Volatile.Read(ref _membershipLoadedCount);
        if (loadedCount > current)
            Volatile.Write(ref _membershipLoadedCount, loadedCount);
    }

    /// <summary>Marks the membership-loading pass complete (collector finished or failed).</summary>
    public void EndMembershipLoading()
    {
        _membershipLoading = false;
    }

    /// <summary>Transitions to <see cref="CacheState.Loading"/> or <see cref="CacheState.Refreshing"/>.</summary>
    public void MarkLoading()
    {
        _lastError = null;
        _state = _current is null ? CacheState.Loading : CacheState.Refreshing;
    }

    /// <summary>Atomically publishes a new snapshot and transitions to <see cref="CacheState.Ready"/>.</summary>
    public void Publish(DashboardSupersetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = snapshot;
        _lastError = null;
        _state = CacheState.Ready;
        _lastRefreshFailed = false;
        Interlocked.Increment(ref _refreshSequence);
    }

    /// <summary>Atomically publishes a new snapshot together with its AR permission model.</summary>
    public void Publish(DashboardSupersetSnapshot snapshot, ArPermissionModel permissionModel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(permissionModel);
        _permissionModel = permissionModel;
        Publish(snapshot);
    }

    /// <summary>
    /// Records a failed load. If a previous snapshot exists it remains served (state returns to
    /// Ready so users are not blocked); otherwise the cache is marked Faulted.
    /// </summary>
    public void MarkFaulted(string error)
    {
        _lastError = error;
        _state = _current is null ? CacheState.Faulted : CacheState.Ready;
        _lastRefreshFailed = true;
        Interlocked.Increment(ref _refreshSequence);
    }
}
