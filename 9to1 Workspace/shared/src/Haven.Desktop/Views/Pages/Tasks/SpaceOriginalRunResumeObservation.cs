using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Tasks;

/// <summary>One exact coordinator-issued resume observation, not its business producer. The scene
/// original owns the acquisition/wait/source-drain Tasks; the same desktop owner
/// publishes and coalesces this child's retirement before any source callbacks.</summary>
internal sealed class SpaceOriginalRunResumeObservation
{
    private readonly object _gate = new();
    private readonly TaskExecutionCoordinator _source;
    private readonly DesktopOriginalWorkLifetime.Original _sceneOriginal;
    private readonly Func<Task<TaskRunOriginalResumeObservationLease>> _acquire;
    private readonly Guid _taskId, _runId, _contextId;
    private readonly Func<bool> _sceneIsRetiring;
    private readonly DesktopOriginalWorkLifetime _retirement;
    private readonly TaskCompletionSource<bool> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task<TaskRunOriginalResumeObservationLease?> _actualAcquisition;
    private TaskRunOriginalResumeObservationLease? _lease;
    private bool _actualSourceFactoryEntered;
    private Task? _actualRequest;
    private Task<TaskRunOriginalResumeObservationLease>? _actualSourceAcquisition;
    private Task<TaskRunOriginalResumeObservationResult>? _actualWait;
    private Task? _actualSourceDetach;
    [ThreadStatic] private static List<SpaceOriginalRunResumeObservation>? _synchronousAcquisitions;

    internal SpaceOriginalRunResumeObservation(TaskExecutionCoordinator source,
        DesktopOriginalWorkLifetime.Original sceneOriginal, Guid taskId, Guid runId, Guid contextId,
        Func<Task<TaskRunOriginalResumeObservationLease>> acquire, Func<bool> sceneIsRetiring)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sceneOriginal = sceneOriginal ?? throw new ArgumentNullException(nameof(sceneOriginal));
        if (taskId == Guid.Empty || runId == Guid.Empty || contextId == Guid.Empty) throw new ArgumentException("Retain the original Task/Run/context identities.");
        (_taskId, _runId, _contextId) = (taskId, runId, contextId);
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        _sceneIsRetiring = sceneIsRetiring ?? throw new ArgumentNullException(nameof(sceneIsRetiring));
        _retirement = new DesktopOriginalWorkLifetime(StopOriginalAsync, () => Task.CompletedTask);
        // Both actual drivers exist and the acquisition phase is enrolled before
        // the scene publishes this capture or permits a source callback to run.
        _actualAcquisition = AcquireOriginalAsync();
        ActualObservation = ObserveOriginalAsync();
    }

    internal Task<TaskRunOriginalResumeObservationResult?> ActualObservation { get; }
    internal Task? OriginalClose => _retirement.OriginalClose;
    internal bool CanPruneHealthy
    {
        get
        {
            lock (_gate)
                return OriginalClose is { IsCompletedSuccessfully: true } &&
                    ActualObservation.IsCompletedSuccessfully && _actualAcquisition.IsCompletedSuccessfully &&
                    (_actualSourceAcquisition is null || _actualSourceAcquisition.IsCompletedSuccessfully) &&
                    (_actualWait is null || _actualWait.IsCompletedSuccessfully) &&
                    (_actualSourceDetach is null || _actualSourceDetach.IsCompletedSuccessfully);
        }
    }

    internal void BeginOriginalAcquisition()
    {
        if (_sceneIsRetiring() || _retirement.IsRetiring)
        {
            RequestRetirement();
            return;
        }
        _start.TrySetResult(true);
    }

    private async Task<TaskRunOriginalResumeObservationLease?> AcquireOriginalAsync()
    {
        // False is the outcome of this actual pre-callback gate: the source
        // factory was not invoked. It is not an inferred producer-completion ACK.
        if (!await _start.Task.ConfigureAwait(false)) return null;
        _sceneOriginal.DemandPublication();
        var actualAcquisition = AcquireOriginalFactory();
        lock (_gate) _actualSourceAcquisition = actualAcquisition;
        var actualLease = await _sceneOriginal.AwaitAsync(actualAcquisition).ConfigureAwait(false);
        lock (_gate) _lease = actualLease; // Capture even when the factory retired the scene before returning.
        DemandIssued(actualLease);
        if (_sceneIsRetiring() || _retirement.IsRetiring) RequestRetirement();
        return actualLease;
    }

    private async Task<TaskRunOriginalResumeObservationResult?> ObserveOriginalAsync()
    {
        try
        {
            var actualLease = await _sceneOriginal.AwaitAsync(_actualAcquisition).ConfigureAwait(false);
            if (actualLease is null) return null;
            if (_sceneIsRetiring() || _retirement.IsRetiring)
            {
                RequestRetirement();
                return null; // The caller still independently joins actual source detachment.
            }
            DemandIssued(actualLease);
            Task<TaskRunOriginalResumeObservationResult>? actualWait = null;
            AcquirePhysicalSource(() => actualWait = actualLease.WaitAsync());
            lock (_gate) _actualWait = actualWait;
            var result = await _sceneOriginal.AwaitAsync(actualWait!).ConfigureAwait(false);
            if (result.Disposition == TaskRunOriginalResumeObservationDisposition.ProducerTerminal && result.CanonicalTaskContext is not null && result.State is not null)
            { DemandContext(result.CanonicalTaskContext); RequestRetirement(); return result; }
            if (result.Disposition == TaskRunOriginalResumeObservationDisposition.ObservationDetached && result.CanonicalTaskContext is null && result.State is null)
            { RequestRetirement(); return result; }
            throw new InvalidOperationException("The actual Task resume observation returned inconsistent terminal metadata.");
        }
        catch (Exception error)
        {
            _sceneOriginal.Retain(error);
            _sceneOriginal.ThrowRetained(); // In particular, a faulted/synchronous OCE remains a fault payload.
            throw;
        }
    }

    private Task<TaskRunOriginalResumeObservationLease> AcquireOriginalFactory()
    {
        // A source callback can restore an ExecutionContext captured before this
        // scene original. Preserve every actual synchronous acquisition owner,
        // including an outer acquisition while another source factory is nested.
        var owners = _synchronousAcquisitions ??= [];
        owners.Add(this);
        try
        {
            lock (_gate) _actualSourceFactoryEntered = true;
            return _acquire();
        }
        catch (OperationCanceledException original)
        { throw new AggregateException("A synchronous resume source supplied no cancelled original Task.", original); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    private void AcquirePhysicalSource(Action callback)
    {
        var owners = _synchronousAcquisitions ??= [];
        owners.Add(this);
        try { callback(); }
        catch (OperationCanceledException cause)
        { throw new AggregateException("A synchronous observation callback supplied no cancelled original Task.", cause); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    private void DemandIssued(TaskRunOriginalResumeObservationLease actualLease)
    {
        ArgumentNullException.ThrowIfNull(actualLease);
        if (!_source.IsIssuedOriginalRunResumeObservation(actualLease))
            throw new InvalidOperationException("The SAME Task coordinator did not issue this original observation.");
        DemandContext(actualLease.CanonicalTaskContext);
    }

    private void DemandContext(ProviderExecutionContext? actual)
    {
        if (actual is null || actual.TaskId != _taskId || actual.ExecutionId != _runId || actual.ContextId != _contextId || actual.PersistenceRevision < 0)
            throw new InvalidOperationException("The source-issued observation belongs to a different actual Task/Run/context.");
    }

    private async Task StopOriginalAsync()
    {
        var failed = false;
        // A retirement before gate release settles the actual acquisition driver
        // without invoking a business factory. A factory already in progress is
        // joined below and its returned resource cannot escape after sealing.
        _start.TrySetResult(false);
        TaskRunOriginalResumeObservationLease? captured;
        lock (_gate) captured = _lease;
        Task? actualRequest = captured is null ? null : AcquireOriginalRequest(captured);
        try { await _sceneOriginal.AwaitAsync(_actualAcquisition).ConfigureAwait(false); }
        catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
        lock (_gate) captured = _lease;
        if (captured is not null)
        {
            // This checks permanent private issuance, not active-slot membership.
            // A genuine already-detached lease remains identifiable here.
            actualRequest ??= AcquireOriginalRequest(captured);
            Task? actualDetach = null;
            try
            {
                _retirement.RunCloseCallback(() =>
                {
                    DemandIssued(captured);
                    actualDetach = captured.DetachAndDrainAsync();
                    lock (_gate) _actualSourceDetach = actualDetach;
                });
            }
            catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
            if (actualDetach is not null)
            {
                try { await _sceneOriginal.AwaitAsync(actualDetach).ConfigureAwait(false); }
                catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
            }
            else
            {
                _sceneOriginal.Retain(new InvalidOperationException("No actual Task resume observation detach Task was acquired."));
                failed = true;
            }
        }
        if (actualRequest is not null)
        {
            try { await _sceneOriginal.AwaitAsync(actualRequest).ConfigureAwait(false); }
            catch (Exception error) { _sceneOriginal.Retain(error); failed = true; }
        }
        if (failed) _sceneOriginal.ThrowRetained();
    }

    private Task AcquireOriginalRequest(TaskRunOriginalResumeObservationLease captured)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task actual;
        lock (_gate)
        {
            if (_actualRequest is not null) return _actualRequest;
            actual = RequestOriginalAsync(start.Task, captured);
            _actualRequest = actual; // Actual driver published before the void source callback.
        }
        start.SetResult();
        return actual;
    }

    private async Task RequestOriginalAsync(Task start, TaskRunOriginalResumeObservationLease captured)
    {
        await start.ConfigureAwait(false);
        try
        {
            _retirement.RunCloseCallback(() =>
            {
                DemandIssued(captured);
                captured.RequestOriginalObservationRetirement();
            });
        }
        catch (Exception error)
        {
            _sceneOriginal.Retain(error);
            _sceneOriginal.ThrowRetained();
            throw;
        }
    }

    internal void RequestRetirement() => _retirement.RequestRetirement();
    internal void DemandExternalClose()
    {
        if (_synchronousAcquisitions?.Any(actual => ReferenceEquals(actual, this)) == true)
            throw new InvalidOperationException("An actual Task resume observation factory must return before joining its encompassing retirement.");
        _retirement.DemandExternalClose();
        // Before terminal capture, this close joins the SAME actual source
        // acquisition and must reject its live business/source ancestry. Once
        // that driver is terminal, this child joins only observer wait/request/
        // detach originals; unrelated inherited business ancestry is no join.
        bool sourceAcquisitionPending;
        lock (_gate) sourceAcquisitionPending = _actualSourceFactoryEntered && !_actualAcquisition.IsCompleted;
        if (sourceAcquisitionPending) _source.DemandExternalOriginalProcessJoin();
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalClose(); // Also before returning an already published close.
        return _retirement.CloseAndDrainAsync();
    }
}
