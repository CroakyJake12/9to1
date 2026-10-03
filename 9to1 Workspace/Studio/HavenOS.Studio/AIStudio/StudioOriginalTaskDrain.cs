using System.Runtime.ExceptionServices;

namespace HavenOS.AIStudio;

/// <summary>Closes UI admission, captures the original Tasks before cancellation, and attempts every owned retirement.
/// This component supplies no Den, Agent, execution or permission authority.</summary>
public sealed class StudioOriginalTaskDrain(
    Action closeAdmission, IReadOnlyList<Func<Task?>> captureOriginalTasks,
    Action requestOwnedCancellation, IReadOnlyList<Func<Task>> retireOriginalResources,
    Func<OperationCanceledException, bool>? exactOwnedCancellation = null)
{
    private readonly object _gate = new();
    private Task? _operation;
    public bool IsClosing { get { lock (_gate) return _operation is not null; } }
    public bool OriginalTasksCapturedAndSettled { get; private set; }

    public Task CloseAndDrainAsync()
    {
        lock (_gate)
        {
            if (_operation is not null) return _operation;
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _operation = DrainAsync(published.Task);
            published.SetResult();
            return _operation;
        }
    }

    private async Task DrainAsync(Task published)
    {
        await published;
        List<Exception> failures = [];
        List<Task> originals = [];
        bool allCaptured = true;
        try { closeAdmission(); } catch (Exception error) { Add(failures, error); }
        foreach (var capture in captureOriginalTasks)
        {
            try
            {
                var original = capture();
                if (original is not null && !originals.Any(previous => ReferenceEquals(previous, original)))
                    originals.Add(original);
            }
            catch (Exception error) { allCaptured = false; Add(failures, error); }
        }
        try { requestOwnedCancellation(); } catch (Exception error) { Add(failures, error); }
        foreach (var original in originals)
        {
            try { await original; }
            catch (OperationCanceledException error)
            {
                bool owned = false;
                if (exactOwnedCancellation is not null)
                    try { owned = exactOwnedCancellation(error); }
                    catch (Exception checkFailure) { Add(failures, error); Add(failures, checkFailure); }
                if (!owned) Add(failures, error);
            }
            catch (Exception error) { Add(failures, error); }
        }
        OriginalTasksCapturedAndSettled = allCaptured;
        foreach (var retire in retireOriginalResources)
            try { await retire(); } catch (Exception error) { Add(failures, error); }
        Throw(failures);
    }

    /// <summary>Start each original retirement independently before awaiting any of them.</summary>
    public static async Task RunIndependentAsync(IReadOnlyList<Func<Task>> starts)
    {
        List<Exception> failures = [];
        List<Task> originals = [];
        foreach (var start in starts)
            try { originals.Add(start()); } catch (Exception error) { Add(failures, error); }
        foreach (var original in originals)
            try { await original; } catch (Exception error) { Add(failures, error); }
        Throw(failures);
    }

    public static void RunSynchronous(IReadOnlyList<Action> actions)
    {
        List<Exception> failures = [];
        foreach (var action in actions)
            try { action(); } catch (Exception error) { Add(failures, error); }
        Throw(failures);
    }
    public static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error); }
    public static void Throw(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
}

/// <summary>Only the current Studio validation callbacks and their original Tasks.
/// Replacement cancels this context, while a new context receives a new instance.</summary>
public sealed class StudioOriginalValidationWork(CancellationToken parentLifetime)
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(parentLifetime);
    private sealed class Original { public Task Task = Task.CompletedTask; public CancellationToken Token; public bool ObservedOwnedCancellation; }
    private readonly List<Original> _originals = [];
    private Task? _close;
    public bool IsClosing { get { lock (_gate) return _close is not null; } }

    public Task RunAsync(Func<CancellationToken, Task> action, CancellationToken caller = default)
    {
        lock (_gate)
        {
            if (_close is not null) throw new ObjectDisposedException(nameof(StudioOriginalValidationWork));
            _originals.RemoveAll(original => original.Task.IsCompletedSuccessfully);
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = new Original();
            original.Task = RunOriginalAsync(published.Task, original, action, caller);
            _originals.Add(original);
            published.SetResult();
            return original.Task;
        }
    }
    private async Task RunOriginalAsync(Task published, Original original,
        Func<CancellationToken, Task> action, CancellationToken caller)
    {
        await published;
        List<Exception> failures = [];
        CancellationTokenSource? linked = null;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
            original.Token = linked.Token;
            await action(linked.Token);
        }
        catch (Exception error)
        {
            original.ObservedOwnedCancellation = error is OperationCanceledException cancelled &&
                !caller.IsCancellationRequested && _lifetime.IsCancellationRequested && original.Token.IsCancellationRequested &&
                cancelled.CancellationToken == original.Token;
            StudioOriginalTaskDrain.Add(failures, error);
        }
        finally
        {
            if (linked is not null)
                try { linked.Dispose(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        }
        StudioOriginalTaskDrain.Throw(failures);
    }
    public Task CloseAndDrainAsync()
    {
        lock (_gate)
        {
            if (_close is not null) return _close;
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = DrainOriginalAsync(published.Task, _originals.ToArray());
            published.SetResult();
            return _close;
        }
    }
    private async Task DrainOriginalAsync(Task published, Original[] originals)
    {
        await published;
        List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        foreach (var original in originals)
        {
            try { await original.Task; }
            catch (OperationCanceledException error) when (original.ObservedOwnedCancellation &&
                error.CancellationToken == original.Token) { }
            catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        }
        try { _lifetime.Dispose(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        StudioOriginalTaskDrain.Throw(failures);
    }
}
