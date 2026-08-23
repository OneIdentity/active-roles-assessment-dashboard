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
    }
}
