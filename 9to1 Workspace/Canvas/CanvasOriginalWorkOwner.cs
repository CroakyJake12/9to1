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
    private Task? _close;
    private bool _sealed;
    public CancellationToken Token => _lifetime.Token;
    public Task? OriginalCloseTask { get { lock (_gate) return _close; } }

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
        catch (OperationCanceledException error) { throw new AggregateException("Synchronous Canvas source supplied no canceled original Task.", error); }
        return await ObserveOriginalAsync(actual).ConfigureAwait(true);
    }
    public async Task ObserveOriginalAsync(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        try { await actual.ConfigureAwait(true); }
        catch (Exception error)
        {
            var whole = (Exception?)actual.Exception ?? error; lock (_gate) Add(_sourceErrors, whole);
            ExceptionDispatchInfo.Capture(whole).Throw(); throw;
        }
    }
    public async Task<T> ObserveOriginalAsync<T>(Task<T> actual)
    { await ObserveOriginalAsync((Task)actual).ConfigureAwait(true); return actual.Result; }

    public Task CloseAndDrainAsync(Func<Task>? independentCleanup = null)
    {
        TaskCompletionSource start; Task original;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original = _close = ClosePublishedAsync(start.Task, _originals.ToArray(), independentCleanup);
        }
        start.SetResult(); return original;
    }
    private async Task ClosePublishedAsync(Task start, Task[] originals, Func<Task>? cleanup)
    {
        await start.ConfigureAwait(true); var errors = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { Add(errors, error); }
        foreach (var actual in originals)
        {
            try { await actual.ConfigureAwait(true); }
            catch (Exception error) { Add(errors, (Exception?)actual.Exception ?? error); }
        }
        // Cleanup must run even after failed/canceled sources and is retained independently.
        Task? close = null;
        try { if (cleanup is not null) { close = cleanup(); await close.ConfigureAwait(true); } }
        catch (Exception error) { Add(errors, (Exception?)close?.Exception ?? error); }
        lock (_gate) foreach (var error in _sourceErrors) Add(errors, error);
        try { _lifetime.Dispose(); } catch (Exception error) { Add(errors, error); }
        Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException group) { foreach (var child in group.InnerExceptions) Add(errors, child); return; }
        if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1 && errors[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 0) throw new AggregateException("Canvas original callbacks and independent cleanup did not drain successfully.", errors);
    }
}
