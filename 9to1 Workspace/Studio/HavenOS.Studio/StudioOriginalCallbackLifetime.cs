using System.Runtime.ExceptionServices;

namespace HavenOS.AIStudio;

/// <summary>Custody of exact original callbacks only; this owner grants no resource or Home authority.</summary>
internal sealed class StudioOriginalCallbackLifetime
{
    private const int MaximumRetainedOriginals = 64;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Original> _originals = [];
    private readonly AsyncLocal<Original?> _executing = new();
    private bool _closing;
    private Task? _close;

    internal bool IsClosing { get { lock (_sync) return _closing; } }
    internal bool IsExecutingOriginal => _executing.Value is { Owner: var owner } && ReferenceEquals(owner, this);
    internal Task? OriginalCloseTask { get { lock (_sync) return _close; } }
    internal bool OriginalsCapturedAndSettled
    {
        get
        {
            lock (_sync) return _closing && _close is { IsCompleted: true } &&
                _originals.All(item => item.Task.IsCompleted && item.LinkedDisposed &&
                    (!item.BodyInvoked || item.Body is { IsCompleted: true }));
        }
    }

    internal Task Run(Func<CancellationToken, Task> body, CancellationToken caller = default,
        Action<Task>? originalPublished = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        caller.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _originals.RemoveAll(item => item.Task.IsCompletedSuccessfully && item.LinkedDisposed);
            if (_originals.Count >= MaximumRetainedOriginals)
            {
                _closing = true;
                throw new InvalidOperationException("Original callback custody is full; retained failures were preserved.");
            }
            var original = new Original(this, caller);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Task = ExecuteOriginalAsync(start.Task, original, body);
            _originals.Add(original);
            try { originalPublished?.Invoke(original.Task); }
            catch (Exception error) { Add(original.Errors, error); }
            finally { start.SetResult(); }
            return original.Task;
        }
    }

    private async Task ExecuteOriginalAsync(Task start, Original original, Func<CancellationToken, Task> body)
    {
        await start;
        var previous = _executing.Value;
        CancellationTokenSource? linked = null;
        try
        {
            _executing.Value = original;
            if (original.Errors.Count != 0) Throw(original.Errors);
            linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, original.Caller);
            original.Token = linked.Token;
            original.Token.ThrowIfCancellationRequested();
            original.BodyInvoked = true;
            original.Body = body(original.Token)
                ?? throw new InvalidOperationException("The original callback returned no task.");
            await original.Body;
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException cancelled && linked is not null &&
                cancelled.CancellationToken == original.Token && _lifetime.IsCancellationRequested &&
                !original.Caller.IsCancellationRequested)
                original.OwnRetirementCancellation = error;
            Add(original.Errors, error);
        }
        finally
        {
            if (original.Body is not null)
                try { await original.Body; } catch (Exception error) { Add(original.Errors, error); }
            try { linked?.Dispose(); original.LinkedDisposed = true; }
            catch (Exception error) { Add(original.Errors, error); }
            _executing.Value = previous;
        }
        Throw(original.Errors);
    }

    internal Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var captured = _originals.ToArray();
            _close = CloseOriginalAsync(start.Task, captured);
            start.SetResult();
            return _close;
        }
    }

    private async Task CloseOriginalAsync(Task start, Original[] captured)
    {
        await start;
        List<Exception> errors = [];
        try { _lifetime.Cancel(); } catch (Exception error) { Add(errors, error); }
        foreach (var original in captured)
        {
            try { await original.Task; }
            catch (Exception error)
            {
                // Only this exact original owner-token cause can be retirement cancellation.
                // A caller-first or foreign-token cause remains a fault even after owner stop.
                if (!ReferenceEquals(error, original.OwnRetirementCancellation) ||
                    original.Caller.IsCancellationRequested) Add(errors, error);
            }
            foreach (var error in original.Errors)
                if (!ReferenceEquals(error, original.OwnRetirementCancellation) ||
                    original.Caller.IsCancellationRequested) Add(errors, error);
            if (!original.LinkedDisposed || original.BodyInvoked && original.Body is not { IsCompleted: true })
                Add(errors, new InvalidOperationException("Original callback/resource settlement is not established."));
        }
        try { _lifetime.Dispose(); } catch (Exception error) { Add(errors, error); }
        Throw(errors);
    }

    internal static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error);
    }
    internal static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original callback and independent cleanup failures.", errors);
    }

    private sealed class Original(StudioOriginalCallbackLifetime owner, CancellationToken caller)
    {
        internal StudioOriginalCallbackLifetime Owner { get; } = owner;
        internal CancellationToken Caller { get; } = caller;
        internal CancellationToken Token;
        internal Task Task = null!;
        internal Task? Body;
        internal bool BodyInvoked, LinkedDisposed;
        internal Exception? OwnRetirementCancellation;
        internal List<Exception> Errors { get; } = [];
    }
}
