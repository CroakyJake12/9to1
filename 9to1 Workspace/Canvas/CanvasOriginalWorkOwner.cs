using System.Runtime.ExceptionServices;

namespace HavenOS.Apps.Canvas;

/// <summary>Finite local callback custody. No actor, business admission, global retirement or
/// permission is issued here. Original source Tasks and independent cleanup remain joined.</summary>
public sealed class CanvasOriginalWorkOwner : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _originals = [];
    private readonly List<Exception> _sourceErrors = [];
    private readonly Dictionary<Exception, Exception[]> _ownedGroups = new(ReferenceEqualityComparer.Instance);
    private Task? _close;
    private bool _sealed;
    public CancellationToken Token => _lifetime.Token;
    public Task? OriginalCloseTask { get { lock (_gate) return _close; } }

    /// <summary>Immutable observation of this owner's exact issued callback tasks. A task's
    /// presence or settlement never grants permission or changes its original failure receipt.</summary>
    public IReadOnlyList<Task> RetainedOriginalTasks
    { get { lock (_gate) return Array.AsReadOnly(_originals.ToArray()); } }

    public bool OwnsOriginalTask(Task original)
    {
        ArgumentNullException.ThrowIfNull(original);
        lock (_gate) return _originals.Any(candidate => ReferenceEquals(candidate, original));
    }

    /// <summary>Only unwrap the exact framework wrapper captured by this owner observing an
    /// actual task, or the exact compound wrapper this owner produced. Observation is not task
    /// issuance; callers must separately validate original-task custody and each source cause.</summary>
    public bool TryGetOwnedFailureCauses(Exception wrapper, out IReadOnlyList<Exception> causes)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        lock (_gate)
        {
            if (_ownedGroups.TryGetValue(wrapper, out var direct))
            { causes = Array.AsReadOnly(direct.ToArray()); return true; }
        }
        causes = Array.Empty<Exception>(); return false;
    }

    public Task RunOriginalAsync(Func<CancellationToken, Task> callback, CancellationToken caller = default)
        => RunOriginalAsync(async token => { await ObserveOriginalAsync(callback(token)).ConfigureAwait(true); return true; }, caller);

    public Task<T> RunOriginalAsync<T>(Func<CancellationToken, Task<T>> callback, CancellationToken caller = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        TaskCompletionSource start; Task<T> original;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_sealed, this);
            _originals.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw new InvalidOperationException("Canvas original callback custody is full; drain this window.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original = RunPublishedAsync(start.Task, callback, caller); _originals.Add(original);
        }
        start.SetResult(); return original;
    }
    private async Task<T> RunPublishedAsync<T>(Task start, Func<CancellationToken, Task<T>> callback, CancellationToken caller)
    {
        await start.ConfigureAwait(true);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        Task<T> actual;
        try { actual = callback(linked.Token) ?? throw new InvalidOperationException("The Canvas callback supplied no original Task."); }
        catch (OperationCanceledException error) { throw CreateOwnedGroup("Synchronous Canvas source supplied no canceled original Task.", [error]); }
        return await ObserveOriginalAsync(actual).ConfigureAwait(true);
    }
    public async Task ObserveOriginalAsync(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        try { await actual.ConfigureAwait(true); }
        catch (Exception error)
        {
            // Only this Task.Exception is a known framework wrapper. Its direct causes
            // may themselves be foreign aggregates, whose identity must remain intact.
            var group = actual.Exception;
            var whole = (Exception?)group ?? error;
            lock (_gate)
            {
                if (group is not null) _ownedGroups.TryAdd(group, group.InnerExceptions.ToArray());
                Add(_sourceErrors, whole);
            }
            ExceptionDispatchInfo.Capture(whole).Throw(); throw;
        }
    }
    public async Task<T> ObserveOriginalAsync<T>(Task<T> actual)
    { await ObserveOriginalAsync((Task)actual).ConfigureAwait(true); return actual.Result; }

    /// <summary>The optional host source wraps the actual private-token cancellation,
    /// including its synchronous dependency callbacks. It must invoke once on this
    /// physical thread; its finite scope never spans an await.</summary>
    public Task CloseAndDrainAsync(Func<Task>? independentCleanup = null,
        Action<Action>? originalCancellationSource = null)
    {
        TaskCompletionSource start; Task original;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original = _close = ClosePublishedAsync(start.Task, _originals.ToArray(), independentCleanup, originalCancellationSource);
        }
        start.SetResult(); return original;
    }
    private async Task ClosePublishedAsync(Task start, Task[] originals, Func<Task>? cleanup,
        Action<Action>? originalCancellationSource)
    {
        await start.ConfigureAwait(true); var errors = new List<Exception>();
        try { CancelOriginalLifetime(originalCancellationSource); } catch (Exception error) { Add(errors, error); }
        foreach (var actual in originals)
        {
            try { await actual.ConfigureAwait(true); }
            catch (Exception error) { Capture(errors, actual, error); }
        }
        // Cleanup must run even after failed/canceled sources and is retained independently.
        Task? close = null;
        try { if (cleanup is not null) { close = cleanup(); await close.ConfigureAwait(true); } }
        catch (Exception error) { Capture(errors, close, error); }
        lock (_gate) foreach (var error in _sourceErrors) Add(errors, error);
        try { _lifetime.Dispose(); } catch (Exception error) { Add(errors, error); }
        Throw(errors);
    }
    private void CancelOriginalLifetime(Action<Action>? source)
    {
        if (source is null) { _lifetime.Cancel(); return; }
        var thread = Environment.CurrentManagedThreadId;
        var active = true; var invoked = false;
        try
        {
            source(() =>
            {
                if (!active || invoked || Environment.CurrentManagedThreadId != thread)
                    throw new InvalidOperationException("Invoke actual Canvas cancellation once inside its finite original host source.");
                invoked = true; _lifetime.Cancel();
            });
            if (!invoked) throw new InvalidOperationException("The original Canvas host did not invoke actual cancellation.");
        }
        finally { active = false; }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private void Capture(List<Exception> errors, Task? actual, Exception caught)
    {
        if (actual?.Exception is { } group)
            foreach (var direct in group.InnerExceptions) Add(errors, direct);
        else Add(errors, caught);
    }
    private void Add(List<Exception> errors, Exception error)
    {
        Exception[]? owned;
        lock (_gate) _ownedGroups.TryGetValue(error, out owned);
        if (owned is not null) { foreach (var direct in owned) Add(errors, direct); return; }
        if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
    private AggregateException CreateOwnedGroup(string message, IEnumerable<Exception> causes)
    {
        var direct = causes.ToArray();
        var group = new AggregateException(message, direct);
        lock (_gate) _ownedGroups.Add(group, direct);
        return group;
    }
    private void Throw(List<Exception> errors)
    {
        if (errors.Count == 1 && errors[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 0) throw CreateOwnedGroup("Canvas original callbacks and independent cleanup did not drain successfully.", errors);
    }
}
