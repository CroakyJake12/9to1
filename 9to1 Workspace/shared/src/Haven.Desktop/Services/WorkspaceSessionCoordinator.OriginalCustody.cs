namespace Haven.Desktop.Services;

/// <summary>Custody for the existing debounced/explicit save owner, not a durable Task store.</summary>
public sealed partial class WorkspaceSessionCoordinator
{
    private const int MaximumRetainedOriginalSaves = 128;
    private Exception? _originalAdmissionFailure;
    private readonly AsyncLocal<LiveOriginal?> _liveOriginal = new();
    [ThreadStatic] private static List<WorkspaceSessionCoordinator>? _cancellationOwners;

    private sealed class LiveOriginal(QueuedSave? save, LiveOriginal? parent)
    {
        internal QueuedSave? Save { get; } = save;
        internal LiveOriginal? Parent { get; } = parent;
        internal bool Active = true;
    }

    private sealed class OriginalScope(WorkspaceSessionCoordinator owner, LiveOriginal actual,
        LiveOriginal? previous) : IDisposable
    {
        public void Dispose()
        {
            Volatile.Write(ref actual.Active, false);
            owner._liveOriginal.Value = previous;
        }
    }

    private IDisposable EnterOriginalScope(QueuedSave? save)
    {
        var previous = _liveOriginal.Value;
        var actual = new LiveOriginal(save, previous);
        _liveOriginal.Value = actual;
        return new OriginalScope(this, actual, previous);
    }

    private bool IsLiveOriginal(QueuedSave? save = null)
    {
        for (var original = _liveOriginal.Value; original is not null; original = original.Parent)
            if (Volatile.Read(ref original.Active) && (save is null || ReferenceEquals(original.Save, save)))
                return true;
        return false;
    }

    private void RunOriginalCancellationCallback(Action callback)
    {
        // CTS restores callback ExecutionContext. The actual synchronous stack preserves exact ownership.
        var owners = _cancellationOwners ??= [];
        owners.Add(this);
        try { callback(); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    private void DemandOriginalCapacityUnderLock()
    {
        if (_queuedSaves.Count < MaximumRetainedOriginalSaves) return;
        throw _originalAdmissionFailure ??= new InvalidOperationException(
            "Retained original workspace saves require external drain before more work can be admitted.");
    }

    /// <summary>
    /// External join only, after UI-thread SaveFinalSnapshotAndSealAsync has published the actual pre-close save.
    /// Live save/snapshot/cancellation callbacks request seal and return before their external owner joins.
    /// </summary>
    internal Task JoinOriginalFinalSaveAsync()
    {
        if (IsLiveOriginal() || _cancellationOwners?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("A live workspace original must return before its external owner joins final save.");
        lock (_gate) return _finalSave ?? throw new InvalidOperationException("The original pre-close snapshot was not published.");
    }

    private static Task AcquireActualOriginalTask(Func<Task> acquisition)
    {
        try { return acquisition() ?? throw new InvalidOperationException("The repository returned no original save task."); }
        catch (OperationCanceledException original)
        {
            throw new AggregateException("Task acquisition has no proven canceled original.", original);
        }
    }

    private static async Task AwaitActualOriginalAsync(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) when (actual.IsFaulted)
        {
            var failures = new List<Exception>();
            if (actual.Exception is { } group)
                foreach (var original in group.InnerExceptions) AddFailure(failures, original);
            else AddFailure(failures, error);
            ThrowFailures(failures);
            throw;
        }
    }

    private static async Task<T> AwaitActualOriginalAsync<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch (Exception error) when (actual.IsFaulted)
        {
            var failures = new List<Exception>();
            if (actual.Exception is { } group)
                foreach (var original in group.InnerExceptions) AddFailure(failures, original);
            else AddFailure(failures, error);
            ThrowFailures(failures);
            throw;
        }
    }
}
