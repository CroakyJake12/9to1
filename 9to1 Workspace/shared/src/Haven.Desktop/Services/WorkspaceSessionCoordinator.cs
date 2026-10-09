using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Services;

/// <summary>Debounces one authoritative snapshot across every Haven window.</summary>
public sealed partial class WorkspaceSessionCoordinator(IWorkspaceSessionRepository repository)
{
    private readonly object _gate = new();
    private readonly Dictionary<MainView, WorkspaceWindowKind> _shells = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Guid, PopUpRegistration> _popUps = [];
    private readonly List<QueuedSave> _queuedSaves = [];
    private QueuedSave? _pending;
    private Task? _finalSave;

    private sealed record PopUpRegistration(
        Func<TabSessionSnapshot> CreateTabSnapshot,
        Func<WorkspaceWindowSnapshot> CreateWindowSnapshot);

    private sealed class FinalSnapshotCapture
    {
        internal WorkspaceSessionSnapshot? Snapshot;
        internal Exception? Failure;
    }

    // Explicit saves also use this record. Their caller token remains caller-owned;
    // its separate CTS owns only coordinator cancellation reservations/cleanup.
    private sealed class QueuedSave
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal Task? OriginalTask;
        internal Task? RepositoryTask;
        internal Task? DispatcherTask;
        internal readonly List<Exception> CallbackFailures = [];
        internal bool CancellationDisposed;
        internal int CancellationUsers;
        internal TaskCompletionSource? CancellationSettlement;
    }

    public void Register(MainView shell, WorkspaceWindowKind kind, bool queueSave = true)
    {
        lock (_gate) _shells[shell] = kind;
        if (queueSave) QueueSave();
    }

    public void Unregister(MainView shell)
    {
        lock (_gate) _shells.Remove(shell);
        QueueSave();
    }

    public void RegisterPopUp(
        Guid windowId,
        Func<TabSessionSnapshot> createTabSnapshot,
        Func<WorkspaceWindowSnapshot> createWindowSnapshot)
    {
        ArgumentNullException.ThrowIfNull(createTabSnapshot);
        ArgumentNullException.ThrowIfNull(createWindowSnapshot);
        lock (_gate) _popUps[windowId] = new PopUpRegistration(createTabSnapshot, createWindowSnapshot);
        QueueSave();
    }

    public void UnregisterPopUp(Guid windowId)
    {
        lock (_gate) _popUps.Remove(windowId);
        QueueSave();
    }

    public void QueueSave()
    {
        QueuedSave? previous;
        QueuedSave original;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            // Final close seals only save admission. Ordinary reversible registration
            // and unregister callbacks still update the same in-memory ownership maps.
            if (_finalSave is not null) return;
            RetireSuccessfulQueuedSavesUnderLock();
            DemandOriginalCapacityUnderLock();
            previous = _pending;
            if (previous is not null) ReserveCancellationUnderLock(previous);
            original = new QueuedSave();
            original.OriginalTask = SaveAfterDelayAsync(start.Task, original);
            _queuedSaves.Add(original);
            _pending = original;
        }
        // The actual new task is already retained before cancellation callbacks can reenter.
        try { if (previous is not null) CancelQueuedSave(previous); }
        finally { start.SetResult(); }
    }

    public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken cancellationToken) => repository.LoadAsync(cancellationToken);

    public Task SaveNowAndCancelPendingAsync(CancellationToken cancellationToken) =>
        AdmitExplicitSave(cancellationToken, cancelPending: true);

    public Task SaveNowAsync(CancellationToken cancellationToken) =>
        AdmitExplicitSave(cancellationToken, cancelPending: false);

    private Task AdmitExplicitSave(CancellationToken cancellationToken, bool cancelPending)
    {
        QueuedSave? pending;
        QueuedSave original;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_finalSave is not null) return _finalSave;
            RetireSuccessfulQueuedSavesUnderLock();
            DemandOriginalCapacityUnderLock();
            pending = cancelPending ? _pending : null;
            if (pending is not null && IsLiveOriginal(pending))
                throw new InvalidOperationException("An original workspace callback cannot join its own predecessor save.");
            if (cancelPending) _pending = null;
            if (pending is not null) ReserveCancellationUnderLock(pending);
            original = new QueuedSave();
            original.OriginalTask = SaveExplicitCoreAsync(start.Task, original, pending, cancellationToken);
            _queuedSaves.Add(original);
        }
        // Publish/retain the SAME actual save before pending-token or snapshot callbacks.
        start.SetResult();
        return original.OriginalTask!;
    }

    private async Task SaveExplicitCoreAsync(Task start, QueuedSave original,
        QueuedSave? pending, CancellationToken cancellationToken)
    {
        await start.ConfigureAwait(false);
        using var scope = EnterOriginalScope(original);
        var failures = new List<Exception>();
        if (pending is not null)
        {
            try { CancelQueuedSave(pending); }
            catch (Exception error) { AddFailure(failures, error); }
            Task? settlement;
            lock (_gate) settlement = pending.CancellationSettlement?.Task;
            if (settlement is not null) await JoinOriginalTaskAsync(settlement, failures).ConfigureAwait(false);
            if (pending.OriginalTask is { } prior) await JoinOriginalTaskAsync(prior, failures).ConfigureAwait(false);
            else AddFailure(failures, new InvalidOperationException("An admitted predecessor lost its original save task."));
            if (pending.DispatcherTask is { } dispatch) await JoinOriginalTaskAsync(dispatch, failures).ConfigureAwait(false);
            if (pending.RepositoryTask is { } repositoryTask) await JoinOriginalTaskAsync(repositoryTask, failures).ConfigureAwait(false);
            lock (_gate) foreach (var error in pending.CallbackFailures) AddFailure(failures, error);
        }
        ThrowFailures(failures);
        cancellationToken.ThrowIfCancellationRequested();
        await SaveNowCoreAsync(cancellationToken,
            task => original.RepositoryTask = task,
            task => original.DispatcherTask = task).ConfigureAwait(false);
    }

    private async Task SaveNowCoreAsync(CancellationToken cancellationToken,
        Action<Task>? retainRepositoryTask, Action<Task>? retainDispatcherTask)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceSessionSnapshot snapshot;
        if (Dispatcher.UIThread.CheckAccess()) snapshot = CaptureSnapshotOnUiThread();
        else
        {
            var dispatch = Dispatcher.UIThread.InvokeAsync(CaptureSnapshotOnUiThread).GetTask();
            retainDispatcherTask?.Invoke(dispatch);
            snapshot = await AwaitActualOriginalAsync(dispatch).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await SaveSnapshotCoreAsync(snapshot, cancellationToken, retainRepositoryTask).ConfigureAwait(false);
    }

    private WorkspaceSessionSnapshot CaptureSnapshotOnUiThread()
    {
        Dispatcher.UIThread.VerifyAccess();
        MainView[] shells;
        Dictionary<MainView, WorkspaceWindowKind> kinds;
        PopUpRegistration[] popUps;
        lock (_gate)
        {
            shells = _shells.Keys.Where(shell => !shell.IsDisposed).ToArray();
            kinds = new Dictionary<MainView, WorkspaceWindowKind>(_shells, ReferenceEqualityComparer.Instance);
            popUps = _popUps.Values.ToArray();
        }
        WorkspaceSessionSnapshot CreateSnapshot()
        {
            var tabs = shells.SelectMany(shell => shell.CreateTabSnapshots())
                .Concat(popUps.Select(popUp => popUp.CreateTabSnapshot()))
                .DistinctBy(tab => tab.Id)
                .ToArray();
            var groups = shells.SelectMany(shell => shell.CreateGroupSnapshots()).DistinctBy(group => group.Id).ToArray();
            var windows = shells.Select(shell => shell.CreateWindowSnapshot(kinds[shell]))
                .Concat(popUps.Select(popUp => popUp.CreateWindowSnapshot()))
                .ToArray();
            return new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion, tabs, groups, windows, DateTimeOffset.UtcNow);
        }

        using var callbackScope = EnterOriginalScope(null);
        try { return CreateSnapshot(); }
        catch (OperationCanceledException error)
        {
            throw new AggregateException("A synchronous snapshot callback has no proven canceled original Task.", error);
        }
    }

    private static WorkspaceSessionSnapshot DetachSnapshot(WorkspaceSessionSnapshot snapshot) =>
        snapshot with
        {
            Tabs = Array.AsReadOnly(snapshot.Tabs.ToArray()),
            Groups = Array.AsReadOnly(snapshot.Groups.Select(group => group with
            {
                OrderedTabIds = Array.AsReadOnly(group.OrderedTabIds.ToArray())
            }).ToArray()),
            Windows = Array.AsReadOnly(snapshot.Windows.Select(window => window with
            {
                OrderedTabIds = Array.AsReadOnly(window.OrderedTabIds.ToArray()),
                Layout = window.Layout with
                {
                    Panes = Array.AsReadOnly(window.Layout.Panes.ToArray())
                }
            }).ToArray())
        };

    private async Task SaveSnapshotCoreAsync(WorkspaceSessionSnapshot snapshot,
        CancellationToken cancellationToken, Action<Task>? retainRepositoryTask)
    {
        var actualRepositoryTask = AcquireActualOriginalTask(() => repository.SaveAsync(snapshot, cancellationToken));
        retainRepositoryTask?.Invoke(actualRepositoryTask);
        await AwaitActualOriginalAsync(actualRepositoryTask).ConfigureAwait(false);
    }

    // Actual final application close only. The call publishes and seals before any
    // cancellation/snapshot/repository callback; its original task is always coalesced.
    internal Task SaveFinalSnapshotAndSealAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        QueuedSave[] originals;
        Task final;
        var capture = new FinalSnapshotCapture();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_finalSave is not null) return _finalSave;
            originals = _queuedSaves.ToArray();
            foreach (var original in originals) ReserveCancellationUnderLock(original);
            _pending = null;
            _finalSave = final = SaveFinalSnapshotCoreAsync(start.Task, originals, capture, cancellationToken);
        }
        // The coalesced actual task/seal exist before any snapshot delegate reentry.
        // Freeze complete DTO values now, before cancellation, older-save waits or Close.
        // Only persistence of this captured DTO waits for all preceding actual writes.
        try { capture.Snapshot = DetachSnapshot(CaptureSnapshotOnUiThread()); }
        catch (Exception error) { capture.Failure = error; }
        finally { start.SetResult(); }
        return final;
    }

    private async Task SaveFinalSnapshotCoreAsync(Task start, QueuedSave[] originals,
        FinalSnapshotCapture capture, CancellationToken cancellationToken)
    {
        await start.ConfigureAwait(true);
        using var scope = EnterOriginalScope(null);
        var failures = new List<Exception>();
        lock (_gate) if (_originalAdmissionFailure is { } refusal) AddFailure(failures, refusal);
        if (capture.Failure is { } captureFailure) AddFailure(failures, captureFailure);
        foreach (var original in originals)
        {
            try { CancelQueuedSave(original); }
            catch (Exception error) { AddFailure(failures, error); }
        }
        // Every reservation predates the terminal seal. Its settlement covers the
        // original synchronous cancellation callback, including concurrent normal callers.
        // This completion gate does not replace any actual repository/save Task.
        foreach (var original in originals)
        {
            Task? cancellationSettlement;
            lock (_gate) cancellationSettlement = original.CancellationSettlement?.Task;
            if (cancellationSettlement is not null)
                await JoinOriginalTaskAsync(cancellationSettlement, failures).ConfigureAwait(true);
        }
        // An actual write that ignores cancellation remains held here until it really settles.
        // No late older write can overtake the final pre-close snapshot.
        foreach (var original in originals)
        {
            if (original.OriginalTask is { } task)
                await JoinOriginalTaskAsync(task, failures).ConfigureAwait(true);
            else AddFailure(failures, new InvalidOperationException("An admitted workspace save lost its original task."));
            // Preserve every direct cause of the original repository task, including a
            // compound whose first error was selected by an intermediate async await.
            if (original.DispatcherTask is { } dispatcherTask)
                await JoinOriginalTaskAsync(dispatcherTask, failures).ConfigureAwait(true);
            if (original.RepositoryTask is { } repositoryTask)
                await JoinOriginalTaskAsync(repositoryTask, failures).ConfigureAwait(true);
            lock (_gate)
                foreach (var error in original.CallbackFailures) AddFailure(failures, error);
            try { DisposeQueuedCancellation(original); }
            catch (Exception error) { AddFailure(failures, error); }
        }

        Task? finalSnapshot = null;
        Task? actualFinalRepositoryTask = null;
        if (capture.Snapshot is { } snapshot)
        {
            try
            {
                finalSnapshot = SaveSnapshotCoreAsync(snapshot, cancellationToken,
                    task => actualFinalRepositoryTask = task);
                await JoinOriginalTaskAsync(finalSnapshot, failures).ConfigureAwait(true);
            }
            catch (Exception error) { AddFailure(failures, error); }
            if (actualFinalRepositoryTask is not null)
                await JoinOriginalTaskAsync(actualFinalRepositoryTask, failures).ConfigureAwait(true);
        }
        else if (capture.Failure is null)
            AddFailure(failures, new InvalidOperationException("The original pre-close snapshot was not captured."));
        // The admission seal is retained on success, failure, cancellation or unknown effect.
        // A failed actual original cannot authorize clean shutdown merely because final save succeeded.
        ThrowFailures(failures);
    }

    private async Task SaveAfterDelayAsync(Task start, QueuedSave original)
    {
        await start.ConfigureAwait(false);
        using var scope = EnterOriginalScope(original);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), original.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (original.Cancellation.IsCancellationRequested)
        {
            // This cancellation happened before any repository entry: the ordinary
            // superseded debounce has no persistence effect and needs no final write.
            return;
        }
        // Errors/cancellation from an actual entered write are kept by the original task;
        // they are not swallowed. QueueSave is request-only and retains that task until drain.
        await SaveNowCoreAsync(original.Cancellation.Token,
            task => original.RepositoryTask = task,
            task => original.DispatcherTask = task).ConfigureAwait(false);
    }

    private void ReserveCancellationUnderLock(QueuedSave original)
    {
        if (original.CancellationUsers == 0)
            original.CancellationSettlement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        original.CancellationUsers++;
    }

    private void CancelQueuedSave(QueuedSave original)
    {
        try { RunOriginalCancellationCallback(original.Cancellation.Cancel); }
        catch (Exception error)
        {
            lock (_gate) AddFailure(original.CallbackFailures, error);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                original.CancellationUsers--;
                if (original.CancellationUsers == 0) original.CancellationSettlement?.TrySetResult();
            }
        }
    }

    private void DisposeQueuedCancellation(QueuedSave original)
    {
        lock (_gate)
        {
            if (original.CancellationDisposed) return;
            if (original.CancellationUsers != 0)
                throw new InvalidOperationException("An original workspace cancellation callback is still held.");
            original.Cancellation.Dispose();
            original.CancellationDisposed = true;
        }
    }

    private void RetireSuccessfulQueuedSavesUnderLock()
    {
        for (var i = _queuedSaves.Count - 1; i >= 0; i--)
        {
            var original = _queuedSaves[i];
            if (original.OriginalTask?.IsCompletedSuccessfully != true || original.CallbackFailures.Count != 0
                || original.CancellationUsers != 0) continue;
            try { DisposeQueuedCancellation(original); }
            catch (Exception error)
            {
                AddFailure(original.CallbackFailures, error);
                continue;
            }
            _queuedSaves.RemoveAt(i);
            if (ReferenceEquals(_pending, original)) _pending = null;
        }
    }

    private static async Task JoinOriginalTaskAsync(Task original, List<Exception> failures)
    {
        try { await original.ConfigureAwait(true); }
        catch (Exception error)
        {
            if (original.Exception is { } fault)
                foreach (var cause in fault.InnerExceptions) AddFailure(failures, cause);
            else AddFailure(failures, error);
        }
    }

    private static void AddFailure(List<Exception> failures, Exception error)
    {
        if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error);
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0)
            throw new AggregateException("Original workspace saves or final snapshot failed.", failures);
    }
}
