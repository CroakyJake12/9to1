namespace HavenOS.Home.Core;

/// <summary>
/// The single Home-owned lifecycle owner for the shared local Dulche runtime
/// (<c>dulche.runtime</c>). It runs through the real Home Core/native host startup and composes
/// platform package discovery, verified model evidence and a bounded process/transport adapter.
/// It owns or attaches to process resources only through supported borrowed-ownership contracts; it
/// never launches a competing model server, downloads a model or grants taskless use.
///
/// Lifecycle guarantees (pass 3):
/// <list type="bullet">
/// <item><description><b>Fail closed.</b> Every non-Ready observation refuses a successful start,
/// independent of whether the source produced diagnostics. A silent success would be promoted to
/// Ready by HomeCoreRuntime and fabricate readiness.</description></item>
/// <item><description><b>One shared start.</b> Concurrent borrowers share ONE underlying start
/// (one-start coalescing). A start completion from an older generation can never publish state after
/// a stop/restart (stale completion guard).</description></item>
/// <item><description><b>Original-work custody.</b> Stop drains the real held original start and any
/// retained child work before settling, and preserves their faults by identity instead of swallowing
/// them. Admission for new original work closes the moment Stop/Dispose begins; late work is refused
/// rather than surviving an acknowledged close.</description></item>
/// <item><description><b>One cached close.</b> A single cached stop/dispose original is published
/// before any callback can reenter; repeated Stop/Dispose calls share the SAME close task.
/// </description></item>
/// </list>
/// </summary>
public sealed class HomeDulcheRuntimeService : IHomeCoreService, IAsyncDisposable
{
    private readonly IHomeDulcheRuntimeObservationSource _source;
    private readonly object _sync = new();
    private readonly List<RetainedOriginal> _retained = [];
    private Task? _startTask;
    private Task? _closeTask;
    private HomeDulcheRuntimeObservation? _observation;
    private int _generation;
    private bool _stopped;
    private bool _disposed;
    private bool _admissionClosed;

    private sealed record RetainedOriginal(Task Original, string Identity);

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
    /// observed lifecycle/catalogue state; on a non-Ready observation it fails closed with a
    /// structured, recoverable degraded result without blocking unrelated services.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task start;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new InvalidOperationException("A stopped Dulche runtime cannot be restarted; create a new runtime instance.");
            if (_admissionClosed) throw new ObjectDisposedException(nameof(HomeDulcheRuntimeService),
                "A Dulche runtime seal has begun; create a new runtime instance.");
            if (_startTask is { } existing)
            {
                // Coalesce repeated starts: the service owns ONE shared start. A completed start
                // (Ready or a truthful refusal) is returned as-is so repeated Starts never spawn
                // new observations/owners.
                start = existing;
            }
            else
            {
                // Publish ONE shared start BEFORE any source callback can run so a synchronous source
                // callback or a reentrant Stop joins the SAME published start instead of forgetting a
                // held observation or early-acknowledging a close.
                var generation = ++_generation;
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                start = StartCoreAsync(begin.Task, generation);
                _startTask = start;
                begin.SetResult();
            }
        }
        // Await the shared start with the caller's own token so one borrower's cancelled wait never
        // cancels the shared start or another borrower's wait.
        await start.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StartCoreAsync(Task begin, int generation)
    {
        await begin.ConfigureAwait(false);
        var observation = await _source.ObserveAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_sync)
        {
            // Stale completion guard: completion from an older generation never publishes after a
            // stop/restart began. It still runs to completion but changes no state.
            if (generation != _generation) return;
            ObjectDisposedException.ThrowIf(_disposed, this);
            _observation = observation;
        }
        if (observation.Lifecycle != HomeServiceLifecycleState.Ready)
        {
            // Fail closed: EVERY non-Ready observation refuses a successful start independently of
            // whether the source produced diagnostics. HomeCoreRuntime promotes a successful start
            // to Ready, so a silent success here would fabricate readiness.
            var reason = observation.Deficiencies.Count != 0
                ? string.Join("; ", observation.Deficiencies)
                : $"The shared Dulche runtime is {observation.Lifecycle} with no recorded diagnostic detail.";
            throw new InvalidOperationException("HomeDulcheRuntimeUnavailable: " + reason);
        }
    }

    /// <summary>Retains an original work/run task so Stop drains it before the runtime settles.
    /// Admission closes the moment Stop/Dispose begins; an attempt to retain work after retirement is
    /// refused rather than silently surviving an acknowledged close. Faults in the retained original
    /// are preserved by identity and surface in the Stop result.</summary>
    public void AttachOriginalWork(Task original)
    {
        ArgumentNullException.ThrowIfNull(original);
        Task[] retained;
        lock (_sync)
        {
            if (_disposed || _stopped || _admissionClosed)
                throw new InvalidOperationException("Original Dulche work cannot be admitted after the runtime retired.");
            _retained.Add(new RetainedOriginal(original, "attached"));
            retained = _retained.Select(item => item.Original).ToArray();
        }
        // Never hold _sync while user code/async I/O runs; the reference is retained under the lock
        // and the caller is free to complete it asynchronously.
        _ = retained;
    }

    /// <summary>Stops the runtime and drains the exact held original start plus every retained child
    /// before settling. Publishes ONE cached close that all repeated Stop/Dispose calls share; the
    /// original start/child faults are preserved by identity in the close result.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_closeTask is not null) return _closeTask;
            // Bump generation so any in-flight older start completion is discarded, close admission,
            // and publish the SAME cached close BEFORE any callback can reenter.
            _generation++;
            _admissionClosed = true;
            var startTask = _startTask;
            var retained = _retained.Select(item => item.Original).ToArray();
            _retained.Clear();
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var close = CloseCoreAsync(begin.Task, startTask, retained);
            _closeTask = close;
            begin.SetResult();
            return close;
        }
    }

    private async Task CloseCoreAsync(Task begin, Task? startTask, Task[] retained)
    {
        await begin.ConfigureAwait(false);
        var failures = new List<Exception>();
        // Drain the actual held original (the shared observe), preserving its fault by identity.
        // A truthful expected readiness refusal (non-Ready/optional-missing-prerequisite observation)
        // is a startup diagnostic, NOT an acquired-resource cleanup failure: nothing was acquired,
        // so a normal close must succeed. Genuine owned-start/connection/child faults still fail
        // close and are retained.
        if (startTask is not null)
            try { await startTask.ConfigureAwait(false); }
            catch (Exception error) { if (!IsExpectedReadinessRefusal(error)) Record(failures, error); }
        foreach (var original in retained)
            try { await original.ConfigureAwait(false); }
            catch (Exception error) { Record(failures, error); }
        lock (_sync)
        {
            _observation = null;
            _stopped = true;
        }
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException("The original Dulche work did not stop cleanly.", failures);

        static void Record(List<Exception> sink, Exception error)
        {
            if (!sink.Any(existing => ReferenceEquals(existing, error))) sink.Add(error);
        }
    }

    /// <summary>True when a start fault is the classified expected readiness refusal raised for any
    /// non-Ready observation. Such a refusal is preserved as Home startup diagnostics (HomeCoreRuntime
    /// records it under <c>home.core</c> start failures) but must not poison a normal close that
    /// acquired no resource.</summary>
    private static bool IsExpectedReadinessRefusal(Exception error) =>
        error is InvalidOperationException ioe &&
        ioe.Message.StartsWith("HomeDulcheRuntimeUnavailable:", StringComparison.Ordinal);

    /// <summary>The last genuinely observed lifecycle/catalogue state, or null before start or after stop.</summary>
    public HomeDulcheRuntimeObservation? CurrentObservation
    {
        get { lock (_sync) return _observation; }
    }

    public async ValueTask DisposeAsync()
    {
        Task close;
        lock (_sync)
        {
            if (_disposed) return;
            if (_closeTask is not null) close = _closeTask;
            else
            {
                _generation++;
                _admissionClosed = true;
                var startTask = _startTask;
                var retained = _retained.Select(item => item.Original).ToArray();
                _retained.Clear();
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                close = CloseCoreAsync(begin.Task, startTask, retained);
                _closeTask = close;
                begin.SetResult();
            }
        }
        await close.ConfigureAwait(false);
        lock (_sync) _disposed = true;
    }
}
