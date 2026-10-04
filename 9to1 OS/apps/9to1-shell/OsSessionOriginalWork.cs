namespace NineToOne.Os.Shell;

/// <summary>Owns actual OS host work and shutdown tasks. This issues no actor or service authority.</summary>
internal sealed class OsSessionOriginalWork(CancellationTokenSource originalLifetime, Func<Task> originalCleanup) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Work> _active = [];
    private readonly OsOriginalFailures _completedFailures = new();
    private Task? _closeTask;
    private Task? _originalCleanupTask;
    private bool _closing;

    private sealed class Work(CancellationTokenSource linked, CancellationToken caller)
    {
        internal readonly CancellationTokenSource Linked = linked;
        internal readonly CancellationToken Token = linked.Token;
        internal readonly CancellationToken Caller = caller;
        internal Task Task = System.Threading.Tasks.Task.CompletedTask;
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> originalOperation, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(originalOperation);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Work work;
        Task<T> task;
        lock (_gate)
        {
            if (_closing) throw new ObjectDisposedException(nameof(OsSessionOriginalWork));
            work = new(CancellationTokenSource.CreateLinkedTokenSource(originalLifetime.Token, caller), caller);
            task = RunOriginalAsync(work, originalOperation, start.Task);
            work.Task = task;
            _active.Add(work); // Retain before the original asynchronous operation can start.
            _ = task.ContinueWith(_ => RetireSettled(work), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        start.SetResult();
        return task;
    }

    private static async Task<T> RunOriginalAsync<T>(Work work, Func<CancellationToken, Task<T>> originalOperation, Task start)
    {
        await start; // Preserve the original caller's dispatcher context, where applicable.
        Exception? primary = null;
        try
        {
            work.Token.ThrowIfCancellationRequested();
            var result = await originalOperation(work.Token);
            work.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            try { work.Linked.Dispose(); }
            catch (Exception cleanup)
            {
                if (primary is not null && !ReferenceEquals(primary, cleanup))
                    throw new AggregateException(primary, cleanup);
                throw;
            }
        }
    }

    private void RetireSettled(Work work)
    {
        lock (_gate)
        {
            // This continuation runs only after the original task and its finally have settled.
            try { work.Task.GetAwaiter().GetResult(); } // Already settled; observe its exact original cause.
            catch (OperationCanceledException failure) when (failure.CancellationToken == work.Token &&
                work.Token.IsCancellationRequested && originalLifetime.IsCancellationRequested && !work.Caller.IsCancellationRequested) { }
            catch (Exception failure) { _completedFailures.Retain(failure); }
            _active.Remove(work);
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? start = null;
        Task close;
        lock (_gate)
        {
            if (_closeTask is null)
            {
                _closing = true;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _closeTask = CloseOriginalAsync(start.Task);
            }
            close = _closeTask; // Publish before original cancellation callbacks can reenter.
        }
        start?.SetResult();
        return new(close);
    }

    private async Task CloseOriginalAsync(Task start)
    {
        await start;
        var failures = new OsOriginalFailures();
        try { originalLifetime.Cancel(); } catch (Exception failure) { failures.Retain(failure); }
        Work[] retained;
        lock (_gate) retained = _active.ToArray();
        foreach (var work in retained)
        {
            try { await work.Task; }
            catch (OperationCanceledException failure) when (failure.CancellationToken == work.Token &&
                work.Token.IsCancellationRequested && originalLifetime.IsCancellationRequested && !work.Caller.IsCancellationRequested) { }
            catch (Exception failure) { failures.Retain(failure); }
        }
        foreach (var failure in _completedFailures.Snapshot()) failures.Retain(failure);
        try
        {
            _originalCleanupTask = originalCleanup();
            await _originalCleanupTask;
        }
        catch (Exception failure) { failures.Retain(failure); }
        failures.ThrowIfAny("Original OS host work and shutdown failures are retained.");
    }
}

internal sealed class OsOriginalFailures
{
    private readonly object _gate = new();
    private readonly List<Exception> _errors = [];
    public void Retain(Exception failure)
    {
        lock (_gate)
            if (!_errors.Any(original => ReferenceEquals(original, failure))) _errors.Add(failure);
    }
    public Exception[] Snapshot() { lock (_gate) return _errors.ToArray(); }
    public void ThrowIfAny(string message)
    {
        var errors = Snapshot();
        if (errors.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Length > 1) throw new AggregateException(message, errors);
    }
}
