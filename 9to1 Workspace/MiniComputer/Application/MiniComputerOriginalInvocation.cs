using System.Runtime.ExceptionServices;
using Haven.Application;

namespace HavenOS.Apps.MiniComputer;

/// <summary>Finite source custody for the actual canonical engine/provider operation.
/// This helper supplies no resource permission and never turns a raw failure into a
/// provider result. The operation owner keeps the helper until its cached close settles.</summary>
internal sealed class MiniComputerOriginalInvocation(object owner, Action<Action> caller,
    Action<Task> retain)
{
    private readonly object _gate = new();
    private readonly List<Task> _raw = [];
    private readonly List<Exception> _errors = [];
    private readonly HashSet<Task> _acknowledged = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> _resources = [];
    private readonly HashSet<IDisposable> _enteredClose = new(ReferenceEqualityComparer.Instance);
    private readonly List<IAsyncDisposable> _asyncResources = [];
    private readonly Dictionary<IAsyncDisposable, Task?> _asyncCloses = new(ReferenceEqualityComparer.Instance);
    private Task? _close;

    internal T Invoke<T>(Func<T> body)
    {
        var live = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
        T value = default!; var failures = new List<Exception>();
        void Remember(Exception cause)
        {
            this.Remember(cause);
            if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
        }
        try
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
            {
                try
                {
                    caller(() =>
                    {
                        if (Volatile.Read(ref live) == 0 || thread != Environment.CurrentManagedThreadId ||
                            Interlocked.CompareExchange(ref used, 1, 0) != 0)
                        {
                            var failure = new InvalidOperationException("The original Mini Computer callback expired, repeated or moved threads.");
                            Remember(failure); throw failure;
                        }
                        try { value = body(); }
                        catch (Exception cause) { Remember(cause); throw; }
                    });
                }
                catch (Exception cause) { Remember(cause); }
                if (Volatile.Read(ref used) == 0)
                    Remember(new InvalidOperationException("The original Mini Computer callback was not entered."));
                return true;
            });
        }
        finally { Volatile.Write(ref live, 0); }
        Throw(failures); return value;
    }
    internal void Run(Action body) => Invoke(() => { body(); return true; });
    internal void Retain(Task task)
    {
        Track(task); // Capture precedes the borrowed retainer and its postguard.
        try { retain(task); } catch (Exception cause) { Remember(cause); throw; }
    }
    internal T Own<T>(T resource) where T : class, IDisposable
    {
        lock (_gate) if (!_resources.Any(prior => ReferenceEquals(prior, resource))) _resources.Add(resource);
        return resource;
    }
    internal T OwnAsync<T>(T resource) where T : class, IAsyncDisposable
    {
        lock (_gate) if (!_asyncResources.Any(prior => ReferenceEquals(prior, resource))) _asyncResources.Add(resource);
        return resource;
    }
    internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
    {
        Task<T>? actual = null; T result = default!; var errors = new List<Exception>();
        try { Run(() => { actual = factory(); Retain(actual); }); }
        catch (Exception cause) { errors.Add(cause); }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); capture?.Invoke(result); }
            catch (Exception cause) { errors.Add(cause); }
        Throw(errors);
        return actual is null ? throw new InvalidOperationException("No original Mini Computer task was captured.") : result;
    }
    internal async Task Read(Func<Task> factory, Action? capture = null)
    {
        Task? actual = null; var errors = new List<Exception>();
        try { Run(() => { actual = factory(); Retain(actual); }); }
        catch (Exception cause) { errors.Add(cause); }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); capture?.Invoke(); }
            catch (Exception cause) { errors.Add(cause); }
        Throw(errors);
    }
    internal Task Capture(Func<Task> factory)
    {
        Task? result = null;
        Run(() => { result = factory(); Retain(result); });
        return result!;
    }
    internal Task<T> Capture<T>(Func<Task<T>> factory)
    {
        Task<T>? result = null;
        Run(() => { result = factory(); Retain(result); });
        return result!;
    }
    internal void Remember(Exception cause)
    { lock (_gate) if (!_errors.Any(prior => ReferenceEquals(prior, cause))) _errors.Add(cause); }
    private void Track(Task actual)
    { lock (_gate) if (!_raw.Any(prior => ReferenceEquals(prior, actual))) _raw.Add(actual); }
    internal bool AcknowledgeOriginalRefusal(Task actual, Func<Task, bool> issuerProof)
    {
        if (!actual.IsFaulted || !CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => issuerProof(actual))) return false;
        lock (_gate)
        { if (!_raw.Any(prior => ReferenceEquals(prior, actual))) return false; _acknowledged.Add(actual); return true; }
    }
    internal void AcknowledgeOriginalRefusalOccurrences(Func<Task, bool> issuerProof)
    { Task[] raw; lock (_gate) raw = _raw.ToArray(); foreach (var task in raw) AcknowledgeOriginalRefusal(task, issuerProof); }
    internal async Task JoinRawAsync()
    {
        var errors = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] raw; lock (_gate) raw = _raw.Where(joined.Add).ToArray();
            if (raw.Length == 0) break;
            foreach (var task in raw)
            {
                try { await task.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    lock (_gate) if (_acknowledged.Contains(task)) continue;
                    // Foreign aggregates remain opaque, and an alias on another raw
                    // Task is never removed by an acknowledgment of this occurrence.
                    var actual = task.Exception ?? cause;
                    if (!errors.Any(prior => ReferenceEquals(prior, actual))) errors.Add(actual);
                }
            }
        }
        lock (_gate) errors.AddRange(_errors);
        Throw(errors);
    }
    internal Task CloseAsync()
    {
        lock (_gate)
        {
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseBody(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task CloseBody(Task start)
    {
        await start.ConfigureAwait(false);
        // Cleanup is owned locally and deliberately does not re-enter a retired
        // presentation callback. Every actual reader/process must settle first.
        try { await JoinRawAsync().ConfigureAwait(false); } catch { /* raw/source evidence remains retained below */ }
        IDisposable[] resources; lock (_gate) resources = _resources.ToArray();
        foreach (var resource in resources.Reverse())
        {
            bool entered; lock (_gate) entered = _enteredClose.Add(resource);
            if (!entered) continue;
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { resource.Dispose(); return true; }); }
            catch (Exception cause) { Remember(cause); }
        }
        IAsyncDisposable[] asyncResources; lock (_gate) asyncResources = _asyncResources.ToArray();
        foreach (var resource in asyncResources.Reverse())
        {
            bool enter; lock (_gate) { enter = !_asyncCloses.ContainsKey(resource); if (enter) _asyncCloses.Add(resource, null); }
            if (!enter) continue;
            Task? actual = null;
            try
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                {
                    actual = resource.DisposeAsync().AsTask();
                    lock (_gate) _asyncCloses[resource] = actual;
                    Track(actual); return true;
                });
            }
            catch (Exception cause) { Remember(cause); }
            if (actual is not null) try { await actual.ConfigureAwait(false); }
                catch (Exception cause) { Remember(cause); }
        }
        await JoinRawAsync().ConfigureAwait(false);
    }
    internal static void Throw(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original Mini Computer sources or cleanup failed.", errors);
    }
}
