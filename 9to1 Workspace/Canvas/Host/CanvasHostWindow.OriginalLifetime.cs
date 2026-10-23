using Avalonia.Threading;
using Haven.Application;

namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasHostSurface
{
    private sealed class NoEffectReadProof
    {
        internal Task? Original;
        internal Exception? Cause;
    }
    private sealed record NoEffectCloseSnapshot(Task[] Originals, Exception[] Causes);
    private readonly object _noEffectGate = new();
    private readonly List<NoEffectReadProof> _noEffectReads = [];
    private NoEffectCloseSnapshot? _noEffectCloseSnapshot;
    private Task? _latestOriginalCommand, _originalChildrenClose;
    private bool _nativeOwnerAcquired;
    internal Task? LatestOriginalCommand => _latestOriginalCommand;
    internal int RetainedReadProofCount { get { lock (_noEffectGate) return _noEffectReads.Count; } }
    internal IReadOnlyList<Exception>? ObserveOwnedFailureCauses(Exception wrapper)
        => _originalWork.TryGetOwnedFailureCauses(wrapper, out var causes) ? causes : null;
    internal Exception? RecoveredNoEffectCloseFailure { get; private set; }

    // UI/native-owner observation only. Never read these controls from the CUI validator,
    // which can run on a worker thread during its original retirement join.
    private bool EmptyReadOnlySessionForClose() => !_nativeOwnerAcquired && !_busy &&
        _view is null && _content.Content is null && _opened is null && _openedFile is null &&
        _pendingRequest is null && _pendingOwnership is null && _pendingAudit is null &&
        _pendingBeginAudit is null && !_requestUncertain && _pendingExecute is null &&
        _pendingDisposable is null && _retainedOriginalCommit is null;

    private NoEffectReadProof? CaptureReadAdmission(string action)
    {
        if (action is not ("canvas.host.search" or "canvas.host.refresh" or "canvas.host.page" or
            "canvas.host.open" or "canvas.host.openItem") || !EmptyReadOnlySessionForClose()) return null;
        return new();
    }
    private Exception RejectBeforeEffect(NoEffectReadProof? proof, Exception cause)
    {
        // This private method is called at the two explicit pre-effect validation sites,
        // never from a catch, a caller-supplied marker, a native failure or a save failure.
        if (proof is not null && !_nativeOwnerAcquired && _view is null && _opened is null &&
            _openedFile is null && _pendingRequest is null && _pendingOwnership is null &&
            _pendingAudit is null && _pendingBeginAudit is null && !_requestUncertain &&
            _pendingExecute is null && _pendingDisposable is null && _retainedOriginalCommit is null)
            lock (_noEffectGate) proof.Cause = cause;
        return cause;
    }
    private bool ContainsOnlyKnownCauses(Exception error, IReadOnlyList<Exception> allowed)
    {
        var pending = new Stack<Exception>(); pending.Push(error);
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            if (visited.Count > 4096) return false;
            if (allowed.Any(cause => ReferenceEquals(cause, current))) continue;
            if (!_originalWork.TryGetOwnedFailureCauses(current, out var direct) || direct.Count == 0) return false;
            foreach (var cause in direct) pending.Push(cause);
        }
        return true;
    }
    private NoEffectCloseSnapshot? CaptureNoEffectCloseSnapshot()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!EmptyReadOnlySessionForClose()) return null;
        var failed = _originalWork.RetainedOriginalTasks.Where(task => !task.IsCompletedSuccessfully).ToArray();
        if (failed.Length == 0 || failed.Any(task => !task.IsFaulted)) return null;
        NoEffectReadProof[] proofs; lock (_noEffectGate) proofs = _noEffectReads.ToArray();
        var causes = new List<Exception>();
        foreach (var task in failed)
        {
            var proof = proofs.SingleOrDefault(candidate => ReferenceEquals(candidate.Original, task));
            if (proof?.Cause is not { } cause || !_originalWork.OwnsOriginalTask(task)) return null;
            // Inspect the framework wrapper of this exact owner-issued task once. Foreign
            // inner aggregates remain opaque unless this owner actually captured them.
            var fault = task.Exception;
            if (fault is null || fault.InnerExceptions.Count == 0 ||
                !fault.InnerExceptions.All(error => ContainsOnlyKnownCauses(error, new[] { cause }))) return null;
            if (!causes.Any(previous => ReferenceEquals(previous, cause))) causes.Add(cause);
        }
        return new(failed, causes.ToArray());
    }
    private bool IsSettledNoEffectActionFailure(Exception error)
    {
        // Pure, nonthrowing, thread-safe callback for the existing CUI retirement hook.
        // This is not task-origin proof; final host close separately checks EVERY issued
        // original task and the real independent children-close success receipt.
        try
        {
            NoEffectCloseSnapshot? snapshot; lock (_noEffectGate) snapshot = _noEffectCloseSnapshot;
            return snapshot is not null && ContainsOnlyKnownCauses(error, snapshot.Causes);
        }
        catch { return false; }
    }
    internal bool CanCompleteKnownNoEffectClose(Task actual, Exception observed)
    {
        if (!Dispatcher.UIThread.CheckAccess() || !_closed || !ReferenceEquals(actual, OriginalCloseTask) ||
            !actual.IsFaulted || _originalChildrenClose?.IsCompletedSuccessfully != true) return false;
        var fault = actual.Exception;
        if (fault is null || !fault.InnerExceptions.Any(cause => ReferenceEquals(cause, observed))) return false;
        var fresh = CaptureNoEffectCloseSnapshot();
        NoEffectCloseSnapshot? retained; lock (_noEffectGate) retained = _noEffectCloseSnapshot;
        if (fresh is null || retained is null || fresh.Originals.Length != retained.Originals.Length ||
            fresh.Originals.Any(task => !retained.Originals.Any(previous => ReferenceEquals(task, previous)))) return false;
        return ContainsOnlyKnownCauses(observed, fresh.Causes);
    }

    /// <summary>A failed acquired surface must finish its original child drain before the host
    /// releases it. Dispose alone schedules that drain and cannot acknowledge completion.</summary>
    internal static async Task InitializeOwnedSurfaceAsync(CanvasNativeCuiSurface surface, CancellationToken token)
    {
        Task? initialization = null;
        try
        {
            initialization = surface.InitializeAsync(token);
            await initialization;
        }
        catch (Exception initializationError)
        {
            var errors = new List<Exception>();
            CaptureOriginalFailure(errors, initialization, initializationError);
            Task? close = null;
            try { close = surface.CloseAndDrainAsync(); await close; }
            catch (Exception closeError) { CaptureOriginalFailure(errors, close, closeError); }
            if (errors.Count == 1 && errors[0] is not OperationCanceledException)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Canvas initialization and acquired surface cleanup failed.", errors);
        }
    }
    private static void CaptureOriginalFailure(List<Exception> errors, Task? actual, Exception caught)
    {
        if (actual?.Exception is { } group)
            foreach (var direct in group.InnerExceptions) AddOriginalFailure(errors, direct);
        else AddOriginalFailure(errors, caught);
    }
    private static void AddOriginalFailure(List<Exception> errors, Exception error)
    {
        // Deeper aggregates belong to the source. Even an empty group is a fault.
        if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error);
    }
    private Task? _surfaceClose, _workOwnerClose;
    public Task? OriginalCloseTask => _surfaceClose;
    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(CloseAndDrainAsync);
        if (_surfaceClose is not null) return _surfaceClose;
        _closed = true;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _surfaceClose = CloseOriginalSurfaceAsync(start.Task); start.SetResult(); return _surfaceClose;
    }
    private async Task CloseOriginalSurfaceAsync(Task start)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        _workOwnerClose = Original(() => _originalWork.CloseAndDrainAsync(() => _originalChildrenClose = CloseOriginalChildrenAsync()));
        await _workOwnerClose;
    }
    private async Task CloseOriginalChildrenAsync()
    {
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        // WorkOwner invokes this only after all exact issued callbacks have settled.
        // Freeze UI eligibility before child retirement enters worker-thread joins.
        var snapshot = CaptureNoEffectCloseSnapshot();
        lock (_noEffectGate) _noEffectCloseSnapshot = snapshot;
        var errors = new List<Exception>();
        try { Original(() => { _lifetime.Cancel(); return true; }); } catch (Exception error) { errors.Add(error); }
        Task[] sources; lock (_sourceGate) sources = _sourceTasks.ToArray();
        foreach (var actual in sources)
            try { await actual; } catch (Exception error) { CaptureOriginalFailure(errors, actual, error); }
        Task? view = null;
        try
        {
            if (_view is CanvasNativeCuiSurface native) { view = Original(native.CloseAndDrainAsync); await view; }
            else _view?.Dispose();
        }
        catch (Exception error) { errors.Add((Exception?)view?.Exception ?? error); }
        // Local native/presentation owners retire independently; Home/Files remain borrowed.
        try { _pendingDisposable?.Dispose(); } catch (Exception error) { errors.Add(error); }
        Task? approvals = null; Task? shell = null;
        try { approvals = Original(_approvals.CloseAndDrainAsync); }
        catch (Exception error) { errors.Add(error); }
        try { shell = Original(_shell.CloseOriginalAsync); }
        catch (Exception error) { errors.Add(error); }
        foreach (var actual in new[] { approvals, shell }.OfType<Task>())
            try { await actual; } catch (Exception error) { errors.Add((Exception?)actual.Exception ?? error); }
        try { _lifetime.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Canvas window native children did not drain successfully.", errors);
    }
    private async Task ObserveStatusAsync(Task actual)
    {
        try { await actual; }
        catch (Exception error)
        {
            if (_closed) return;
            _ready = false; SetStatus(((Exception?)actual.Exception ?? error).Message); RefreshBindings();
        }
    }
}
