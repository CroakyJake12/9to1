namespace Haven.Application;

/// <summary>Actual operation child tasks and direct causes; no Flatten, error-text reconstruction or OCE waiver.</summary>
public sealed class CloudflareOriginalTaskLedger
{
    private readonly object _sync = new();
    private object? _originalOwner;
    private Action<Action>? _originalCallerCallback;
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<Action>? OriginalCallerCallback { get { lock (_sync) return _originalCallerCallback; } }
    public void BindOriginalCallerCallback(Action<Action>? callback)
    {
        if (callback is null) return;
        lock (_sync)
        {
            if (_originalCallerCallback is not null && !_originalCallerCallback.Equals(callback))
                throw new InvalidOperationException("Original caller lifetime callback changed.");
            _originalCallerCallback ??= callback;
        }
    }
    public void BindOriginalOwner(object owner)
    { lock (_sync) { if (_originalOwner is not null && !ReferenceEquals(_originalOwner, owner)) throw new InvalidOperationException("Original ledger owner changed."); _originalOwner = owner; } }
    private readonly List<Task> _tasks = [];
    private readonly List<Exception> _errors = [];
    public IReadOnlyList<Task> OriginalTasks { get { lock (_sync) return Array.AsReadOnly(_tasks.ToArray()); } }
    public IReadOnlyList<Exception> OriginalErrors { get { lock (_sync) return Array.AsReadOnly(_errors.ToArray()); } }
    public Task<T> Track<T>(Task<T> original) { lock (_sync) { if (!_tasks.Any(x => ReferenceEquals(x, original))) _tasks.Add(original); } return original; }
    public Task<T> Track<T>(ValueTask<T> original) => Track(original.AsTask());
    public Task Track(Task original) { lock (_sync) { if (!_tasks.Any(x => ReferenceEquals(x, original))) _tasks.Add(original); } return original; }
    public Task Track(ValueTask original) => Track(original.AsTask());
    public void Retain(Exception error) { lock (_sync) { if (!_errors.Any(x => ReferenceEquals(x, error))) _errors.Add(error); } }
    public void Capture(Task? original, Exception observed)
    {
        if (original?.Exception is { } group) foreach (var error in group.InnerExceptions) Retain(error);
        else Retain(observed);
    }
    public T Invoke<T>(Func<T> finite)
    {
        try
        {
            T result = default!; bool invoked = false;
            void Run()
            {
                if (invoked) throw new InvalidOperationException("The original finite caller scope invoked its callback twice.");
                invoked = true; result = _originalOwner is null ? finite() : CloudflareOriginalExecutionGuard.InvokeOriginal(_originalOwner, finite);
                if (result is Task actual) Track(actual); // Custody precedes any post-callback scope failure.
            }
            var originalCaller = OriginalCallerCallback;
            if (originalCaller is null) Run(); else originalCaller(Run);
            if (!invoked) throw new InvalidOperationException("The original caller scope did not invoke its finite callback.");
            return result;
        }
        catch (OperationCanceledException error) { Retain(error); throw new AggregateException("Synchronous original callback fault.", error); }
        catch (Exception error) { Retain(error); throw; }
    }
    public ValueTask Invoke(Func<ValueTask> finite) => new(Invoke(() => finite().AsTask()));
    public ValueTask<T> Invoke<T>(Func<ValueTask<T>> finite) => new(Invoke(() => finite().AsTask()));
    public async Task<T> AwaitAsync<T>(Task<T> original)
    {
        Track(original);
        try { return await original.ConfigureAwait(false); }
        catch (Exception error) { Capture(original, error); if (original.IsFaulted) throw new AggregateException(original.Exception!.InnerExceptions); throw; }
    }
    public Task<T> AwaitAsync<T>(ValueTask<T> original) => AwaitAsync(original.AsTask());
    public async Task AwaitAsync(Task original)
    {
        Track(original);
        try { await original.ConfigureAwait(false); }
        catch (Exception error) { Capture(original, error); if (original.IsFaulted) throw new AggregateException(original.Exception!.InnerExceptions); throw; }
    }
    public Task AwaitAsync(ValueTask original) => AwaitAsync(original.AsTask());
    // Encompass failed finite starts too: a Task returned before a scope fault
    // must reach its actual terminal state before this source operation can settle.
    public async Task<T> RunToOriginalSettlementAsync<T>(Func<Task<T>> body)
    {
        T value = default!; Exception? primary = null;
        try { value = await AwaitAsync(Invoke(body)).ConfigureAwait(false); }
        catch (Exception error) { primary = error; Retain(error); }
        await ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (primary is OperationCanceledException && OriginalTasks.All(task => !task.IsFaulted) && OriginalErrors.All(error => error is OperationCanceledException))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        if (OriginalErrors.Count != 0) throw new AggregateException("Original caller-scoped source or retained child failed.", OriginalErrors);
        return value;
    }
    public Task RunToOriginalSettlementAsync(Func<Task> body) => RunToOriginalSettlementAsync(async () =>
    { await AwaitAsync(Invoke(body)).ConfigureAwait(false); return true; });
    public async Task ObserveAllOriginalTasksAsync()
    {
        foreach (var actual in OriginalTasks)
            try { await AwaitAsync(actual).ConfigureAwait(false); } catch (Exception error) { Capture(actual, error); }
    }
    public async Task<T> CaptureOriginalAcquisitionAsync<T>(Func<Task<T>> finite, Action<T> retainActualResult)
    {
        Task<T>? actual = null; Exception? failure = null; T result = default!;
        try { Invoke(() => { actual = finite(); Track(actual); return actual; }); }
        catch (Exception error) { failure = error; Retain(error); }
        if (actual is not null)
        {
            try { result = await AwaitAsync(actual).ConfigureAwait(false); retainActualResult(result); }
            catch (Exception error) { Capture(actual, error); failure ??= error; }
        }
        if (failure is not null)
        {
            if (failure is OperationCanceledException && actual?.IsCanceled == true && OriginalErrors.All(error => error is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw new AggregateException("Original acquisition or finite caller scope failed; exact late result remains in owning cleanup custody.", OriginalErrors);
        }
        return result;
    }
    public Task<T> CaptureOriginalAcquisitionAsync<T>(Func<ValueTask<T>> finite, Action<T> retainActualResult)
        => CaptureOriginalAcquisitionAsync(() => finite().AsTask(), retainActualResult);
    public async Task ObserveOriginalCloseAsync(Func<ValueTask> finite)
    {
        Task? actual = null; var errors = new List<Exception>(); bool knownFault = false;
        try { Invoke(() => { actual = finite().AsTask(); Track(actual); return actual; }); }
        catch (Exception error) { knownFault = true; Retain(error); errors.Add(error); }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                Capture(actual, error); knownFault |= actual.IsFaulted;
                if (actual.Exception is { } group) errors.AddRange(group.InnerExceptions); else errors.Add(error);
            }
        if (errors.Count == 1 && !knownFault && actual?.IsCanceled == true)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual close and finite caller scope failed.", errors);
    }
}
