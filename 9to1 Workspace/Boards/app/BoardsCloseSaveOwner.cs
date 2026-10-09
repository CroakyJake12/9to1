namespace CakeOS.Apps.Boards.App;

/// <summary>Owns the real session save used to decide whether the native window may close.</summary>
public sealed class BoardsCloseSaveOwner(Func<IRichBoardSession> currentSession)
{
    private readonly object _gate = new();
    private Task? _attempt;
    private Task? _cleanup;
    private IRichBoardSession? _savedSession;

    public Task? OriginalSaveTask { get; private set; }
    public Task? OriginalActionDrainTask { get; private set; }
    public Task? OriginalDisposeTask { get; private set; }
    public bool SaveAccepted { get; private set; }

    /// <summary>Repeated close requests share the pending attempt. A failed save can be retried.</summary>
    public Task SaveBeforeCloseAsync()
    {
        TaskCompletionSource start;
        Task result;
        lock (_gate)
        {
            if (_attempt is { IsCompleted: false } or { IsCompletedSuccessfully: true })
                return _attempt;
            start = new TaskCompletionSource();
            result = _attempt = SaveAsync(start.Task);
        }
        start.SetResult();
        return result;
    }

    private async Task SaveAsync(Task start)
    {
        await start;
        SaveAccepted = false;
        var session = currentSession();
        var original = OriginalSaveTask = session.SaveAsync().AsTask();
        try { await original; }
        catch (Exception error) { ThrowOriginal(original, error); throw; }
        _savedSession = session;
        SaveAccepted = true;
    }

    /// <summary>Only an acknowledged save permits retiring callbacks and the session.</summary>
    public Task DrainAfterSaveAsync(Action stopCallbacks, Func<Task> drainCallbacks)
    {
        ArgumentNullException.ThrowIfNull(stopCallbacks);
        ArgumentNullException.ThrowIfNull(drainCallbacks);
        TaskCompletionSource start;
        Task result;
        lock (_gate)
        {
            if (!SaveAccepted)
                throw new InvalidOperationException("The current board must be saved before close cleanup.");
            if (_cleanup is not null) return _cleanup;
            start = new TaskCompletionSource();
            result = _cleanup = DrainAsync(start.Task, stopCallbacks, drainCallbacks);
        }
        start.SetResult();
        return result;
    }

    private async Task DrainAsync(Task start, Action stopCallbacks, Func<Task> drainCallbacks)
    {
        await start;
        var failures = new List<Exception>();
        try { stopCallbacks(); }
        catch (Exception error) { failures.Add(error); }
        try
        {
            OriginalActionDrainTask = drainCallbacks();
            await OriginalActionDrainTask;
        }
        catch (Exception error) { AddOriginal(failures, OriginalActionDrainTask, error); }
        // A callback failure must not skip the independent real session disposal.
        try
        {
            OriginalDisposeTask = (_savedSession
                ?? throw new InvalidOperationException("The saved Boards session is unavailable."))
                .DisposeAsync().AsTask();
            await OriginalDisposeTask;
        }
        catch (Exception error) { AddOriginal(failures, OriginalDisposeTask, error); }
        if (failures.Count > 0)
            throw new AggregateException("Boards close cleanup failed after the physical save.", failures);
    }

    private static void AddOriginal(List<Exception> failures, Task? original, Exception caught)
    {
        IEnumerable<Exception> causes = original?.Exception is { InnerExceptions.Count: > 0 } group
            ? group.InnerExceptions : new[] { caught };
        foreach (var cause in causes)
            if (!failures.Any(previous => ReferenceEquals(previous, cause))) failures.Add(cause);
    }

    private static void ThrowOriginal(Task original, Exception caught)
    {
        if (original.Exception is { InnerExceptions.Count: > 0 } group)
        {
            // A faulted OCE must remain faulted, and await must not drop sibling failures.
            if (group.InnerExceptions.Count > 1 || caught is OperationCanceledException)
                throw new AggregateException("The original Boards save failed.", group.InnerExceptions);
        }
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }
}
