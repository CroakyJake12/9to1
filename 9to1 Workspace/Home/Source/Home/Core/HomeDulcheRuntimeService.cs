namespace HavenOS.Home.Core;

/// <summary>
/// The single Home-owned lifecycle owner for the shared local Dulche runtime
/// (<c>dulche.runtime</c>). It runs through the real Home Core/native host startup and composes
/// platform package discovery, verified model evidence and a bounded process/transport adapter.
/// It owns or attaches to process resources only through supported borrowed-ownership contracts; it
/// never launches a competing model server, downloads a model or grants taskless use.
///
/// Concurrent borrowers share ONE underlying start (one-start coalescing). A start completion from an
/// older generation can never publish Ready after a stop/restart (stale completion guard). Stop
/// drains retained original work before settling and releases only what this service holds.
/// </summary>
public sealed class HomeDulcheRuntimeService : IHomeCoreService, IAsyncDisposable
{
    private readonly IHomeDulcheRuntimeObservationSource _source;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _sync = new();
    private readonly List<Task> _retainedWork = [];
    private Task? _startTask;
    private HomeDulcheRuntimeObservation? _observation;
    private int _generation;
    private bool _stopped;
    private bool _disposed;

    public HomeDulcheRuntimeService(IHomeDulcheRuntimeObservationSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public HomeServiceDescriptor Descriptor { get; } = new(
        "dulche.runtime",
        HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped,
        false,
        "The shared Dulche local runtime has not started.");

    public IReadOnlyList<string> Dependencies { get; } = [];

    /// <summary>Starts the shared runtime once for all concurrent borrowers. Publishes the genuinely
    /// observed lifecycle/catalogue state; on a non-Ready observation it reports a structured,
    /// recoverable degraded result without blocking unrelated services.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task start;
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new InvalidOperationException("A stopped Dulche runtime cannot be restarted; create a new runtime instance.");
            if (_startTask is not null && !_startTask.IsCompleted)
            {
                start = _startTask;
            }
            else
            {
                var generation = ++_generation;
                start = StartCoreAsync(generation, cancellationToken);
                _startTask = start;
            }
        }
        finally { _startGate.Release(); }
        await start.ConfigureAwait(false);
    }

    private async Task StartCoreAsync(int generation, CancellationToken cancellationToken)
    {
        var observation = await _source.ObserveAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            // Stale completion guard: a completion from an older generation must not publish
            // state after a stop/restart began or completed.
            if (generation != _generation) return;
            ObjectDisposedException.ThrowIf(_disposed, this);
            _observation = observation;
        }
        if (observation.Lifecycle == HomeServiceLifecycleState.Ready) return;
        if (observation.Deficiencies.Count != 0)
            throw new InvalidOperationException("HomeDulcheRuntimeUnavailable: " + string.Join("; ", observation.Deficiencies));
    }

    /// <summary>Retains an original work/run task so Stop joins and drains it before the runtime
    /// settles. This never terminates unrelated processes; it only awaits the owned originals.</summary>
    public void AttachOriginalWork(Task original)
    {
        if (original is null) return;
        lock (_sync) _retainedWork.Add(original);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            // Bump the generation so any in-flight older start completion is discarded.
            _generation++;
            _startTask = null;
            Task[] retained;
            lock (_sync) { retained = _retainedWork.ToArray(); _retainedWork.Clear(); }
            foreach (var original in retained)
            {
                try { await original.ConfigureAwait(false); }
                catch { /* Draining joins originals; a fault in retained work is not a stop failure. */ }
            }
            lock (_sync) { _observation = null; _stopped = true; }
        }
        finally { _startGate.Release(); }
    }

    /// <summary>The last genuinely observed lifecycle/catalogue state, or null before start or after stop.</summary>
    public HomeDulcheRuntimeObservation? CurrentObservation
    {
        get { lock (_sync) return _observation; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_sync) _disposed = true;
        _startGate.Dispose();
    }
}
