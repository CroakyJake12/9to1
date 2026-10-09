using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;

namespace Haven.Infrastructure;

// Finite source custody for the configured SQLite owner only; no access decision.
internal sealed class CanonicalSqliteOriginalSourceScope(
    object owner, Action<Action> caller, Action<Task> retainer,
    Action<Exception>? unexpectedCallback = null, Action? demandProductive = null)
{
    [ThreadStatic] private static Dictionary<object, int>? _physical;
    private readonly object _gate = new();
    private readonly List<Task> _raw = [];
    private readonly List<Exception> _errors = [];
    private readonly Dictionary<Task, Exception[]> _rawFaults = new(ReferenceEqualityComparer.Instance);
    private Exception? _originalPreEffectRefusal;
    internal Exception? OriginalPreEffectRefusal { get { lock (_gate) return _originalPreEffectRefusal; } }
    internal void RegisterOriginalPreEffectRefusal(Exception actualCause)
    {
        lock (_gate)
        {
            if (_originalPreEffectRefusal is not null && !ReferenceEquals(_originalPreEffectRefusal, actualCause))
                throw new InvalidOperationException("The original source already issued a different refusal.");
            _originalPreEffectRefusal = actualCause;
        }
    }
    internal bool AcknowledgeOriginalExternalPreEffectRefusal(Task actual, Func<Task, bool> actualIssuerProof)
    {
        ArgumentNullException.ThrowIfNull(actual); ArgumentNullException.ThrowIfNull(actualIssuerProof);
        if (!actual.IsFaulted || !InvokeOwningCleanup(() => actualIssuerProof(actual))) return false;
        lock (_gate)
        {
            if (!_raw.Any(value => ReferenceEquals(value, actual)) || !_rawFaults.ContainsKey(actual)) return false;
            // Exact occurrence only. A foreign Task carrying the SAME exception object,
            // callback failure or unknown sibling remains in its own custody.
            _raw.RemoveAll(value => ReferenceEquals(value, actual)); _rawFaults.Remove(actual); return true;
        }
    }
    private readonly List<IAsyncDisposable> _resources = [];
    private readonly ConditionalWeakTable<IAsyncDisposable, CloseRecord> _closes = new();
    private sealed class CloseRecord
    {
        internal Task Driver = null!;
        internal Task? Raw;
    }
    internal static bool IsPhysicalSource(object owner) => _physical?.ContainsKey(owner) == true;
    internal bool IsHealthySettled { get { lock (_gate) return _raw.Count == 0 && _resources.Count == 0 && _errors.Count == 0 && _rawFaults.Count == 0; } }
    internal void Run(Action body) => Invoke(() => { body(); return 0; });
    internal T InvokeProductive<T>(Func<T> body)
    {
        demandProductive?.Invoke();
        var result = Invoke(() => { demandProductive?.Invoke(); return body(); });
        // The external caller may itself have invoked an escaped old callback after
        // the body completed. This guard precedes result publication to its caller.
        demandProductive?.Invoke(); return result;
    }
    internal void RunProductive(Action body) => InvokeProductive(() => { body(); return 0; });
    internal T Invoke<T>(Func<T> body)
    {
        var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId; T result = default!;
        Exception? bodyFailure = null;
        void Actual()
        {
            if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread ||
                Interlocked.Exchange(ref used, 1) != 0)
                throw RememberUnexpectedCallback(new InvalidOperationException("The original SQLite callback is inactive, foreign-thread or already consumed."));
            try { result = body(); CaptureResource(result); }
            catch (Exception cause) { bodyFailure = cause; Remember(cause); throw; }
        }
        var physical = _physical ??= new(ReferenceEqualityComparer.Instance);
        physical.TryGetValue(owner, out var depth); physical[owner] = depth + 1;
        physical.TryGetValue(this, out var sourceDepth); physical[this] = sourceDepth + 1;
        try
        {
            try { caller(Actual); }
            catch (Exception cause)
            {
                if (IsObservedBodyFailure(cause, bodyFailure)) Remember(cause);
                else RememberUnexpectedCallback(cause);
                throw;
            }
            if (Volatile.Read(ref used) == 0)
                throw RememberUnexpectedCallback(new InvalidOperationException("The actual SQLite source callback was not invoked."));
            return result;
        }
        finally
        {
            Volatile.Write(ref active, 0);
            if (sourceDepth == 0) physical.Remove(this); else physical[this] = sourceDepth;
            if (depth == 0) physical.Remove(owner); else physical[owner] = depth;
        }
    }
    internal void Retain(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate) if (!_raw.Any(prior => ReferenceEquals(prior, actual))) _raw.Add(actual);
        // Private custody precedes the external callback, even if it declines publication.
        try
        {
            Run(() =>
            {
                try { retainer(actual); }
                catch (Exception cause) { RememberUnexpectedCallback(cause); throw; }
            });
        }
        catch (Exception cause) { RememberUnexpectedCallback(cause); throw; }
    }
    internal Task<T> Read<T>(Func<Task<T>> factory) => ReadCapture(factory, static _ => { });
    internal async Task<T> ReadCapture<T>(Func<Task<T>> factory, Action<T> captureActual)
    {
        await JoinCompletedSuccessfulOriginalsAsync().ConfigureAwait(false);
        DemandCapacity(); demandProductive?.Invoke();
        Task<T>? actual = null; T result = default!; Exception? acquisition = null, terminal = null;
        try { Run(() => { demandProductive?.Invoke(); actual = factory() ?? throw new InvalidOperationException("No actual SQLite source Task was returned."); Retain(actual); }); }
        catch (Exception cause) { acquisition = cause; }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); CaptureResource(result); captureActual(result); MarkJoined(actual); demandProductive?.Invoke(); }
            catch (Exception cause) { terminal = Capture(actual, cause); }
        if (acquisition is not null && terminal is not null)
            throw new AggregateException("Actual SQLite source acquisition and retained Task both failed.", acquisition, terminal);
        if (acquisition is not null) ExceptionDispatchInfo.Capture(acquisition).Throw();
        if (terminal is not null) ExceptionDispatchInfo.Capture(terminal).Throw();
        return actual is null ? throw new InvalidOperationException("No actual SQLite source Task was captured.") : result;
    }
    internal async Task Read(Func<Task> factory)
    {
        await JoinCompletedSuccessfulOriginalsAsync().ConfigureAwait(false);
        DemandCapacity(); demandProductive?.Invoke();
        Task? actual = null; Exception? acquisition = null, terminal = null;
        try { Run(() => { demandProductive?.Invoke(); actual = factory() ?? throw new InvalidOperationException("No actual SQLite source Task was returned."); Retain(actual); }); }
        catch (Exception cause) { acquisition = cause; }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); MarkJoined(actual); demandProductive?.Invoke(); }
            catch (Exception cause) { terminal = Capture(actual, cause); }
        if (acquisition is not null && terminal is not null)
            throw new AggregateException("Actual SQLite source acquisition and retained Task both failed.", acquisition, terminal);
        if (acquisition is not null) ExceptionDispatchInfo.Capture(acquisition).Throw();
        if (terminal is not null) ExceptionDispatchInfo.Capture(terminal).Throw();
        if (actual is null) throw new InvalidOperationException("No actual SQLite source Task was captured.");
    }
    internal Task CloseAsync(IAsyncDisposable actual)
    {
        CloseRecord record; TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_closes.TryGetValue(actual, out record!)) return record.Driver;
            if (!_resources.Any(prior => ReferenceEquals(prior, actual)))
                throw new UnauthorizedAccessException("This source did not privately capture the actual SQLite cleanup object.");
            record = new(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            record.Driver = Drive(start.Task, record); _closes.Add(actual, record);
        }
        // Reservation/custody was published before any external Dispose factory enters.
        start.SetResult(); return record.Driver;
        async Task Drive(Task begin, CloseRecord captured)
        {
            await begin.ConfigureAwait(false); Exception? acquisition = null, terminal = null; var factoryEntered = false;
            try { Run(() => { factoryEntered = true; captured.Raw = actual.DisposeAsync().AsTask(); Retain(captured.Raw); }); }
            catch (Exception cause) { acquisition = cause; }
            if (captured.Raw is null)
            {
                // A synchronous disposal fault may have changed the object. Its exact
                // factory was entered: preserve custody/failure rather than replay it.
                if (factoryEntered)
                    ExceptionDispatchInfo.Capture(acquisition ?? new InvalidOperationException("The actual entered close returned no Task.")).Throw();
                try
                {
                    captured.Raw = InvokeOwningCleanup(() => { factoryEntered = true; return actual.DisposeAsync().AsTask(); });
                    lock (_gate) _raw.Add(captured.Raw);
                }
                catch (Exception cause) { Remember(cause); throw; }
            }
            try { await captured.Raw.ConfigureAwait(false); MarkJoined(captured.Raw); }
            catch (Exception cause) { terminal = Capture(captured.Raw, cause); }
            if (acquisition is not null && terminal is not null)
                throw new AggregateException("Original SQLite close acquisition and actual close both failed.", acquisition, terminal);
            if (acquisition is not null) ExceptionDispatchInfo.Capture(acquisition).Throw();
            if (terminal is not null) ExceptionDispatchInfo.Capture(terminal).Throw();
            lock (_gate) _resources.RemoveAll(prior => ReferenceEquals(prior, actual));
        }
    }
    internal async Task CloseResourcesAsync()
    {
        IAsyncDisposable[] resources; lock (_gate) resources = _resources.AsEnumerable().Reverse().ToArray();
        var errors = new List<Exception>();
        foreach (var actual in resources)
            try { await CloseAsync(actual).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Every actual SQLite object close was independently joined.", errors);
    }
    internal void CaptureOriginalResource(IAsyncDisposable actual) => CaptureResource(actual);
    private void CaptureResource<T>(T actual)
    {
        if (actual is not IAsyncDisposable resource) return;
        lock (_gate) if (!_resources.Any(prior => ReferenceEquals(prior, resource))) _resources.Add(resource);
    }
    internal T InvokeOwningCleanup<T>(Func<T> body)
    {
        var physical = _physical ??= new(ReferenceEqualityComparer.Instance);
        physical.TryGetValue(owner, out var depth); physical[owner] = depth + 1;
        physical.TryGetValue(this, out var sourceDepth); physical[this] = sourceDepth + 1;
        try { return body(); }
        finally
        {
            if (sourceDepth == 0) physical.Remove(this); else physical[this] = sourceDepth;
            if (depth == 0) physical.Remove(owner); else physical[owner] = depth;
        }
    }
    internal async Task JoinAllAsync()
    {
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] pending; lock (_gate) pending = _raw.Where(joined.Add).ToArray();
            if (pending.Length == 0) break;
            foreach (var raw in pending)
                try { await raw.ConfigureAwait(false); MarkJoined(raw); }
                catch (Exception cause) { Capture(raw, cause); }
        }
        Exception[] errors; lock (_gate) errors = _errors.Concat(_rawFaults.Values.SelectMany(value => value)).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (errors.Length != 0) throw new AggregateException("Actual SQLite source/cleanup did not settle.", errors);
    }
    private async Task JoinCompletedSuccessfulOriginalsAsync()
    {
        Task[] completed;
        lock (_gate) completed = _raw.Where(actual => actual.IsCompletedSuccessfully).ToArray();
        // Deep original source callbacks may forward healthy children into this parent.
        // Independently join only terminal successes; never await a pending parent/current
        // driver, drop faults or infer resource close from a Task status.
        foreach (var actual in completed)
        {
            await actual.ConfigureAwait(false);
            MarkJoined(actual);
        }
    }
    internal void DemandCapacity()
    {
        lock (_gate)
            if (_raw.Count >= 1024 || _resources.Count >= 256 || _errors.Count >= 256 || _rawFaults.Count >= 256)
                throw new InvalidOperationException("Settle retained SQLite sources/resources before another productive acquisition.");
    }
    private void MarkJoined(Task actual)
    {
        if (!actual.IsCompletedSuccessfully) return;
        lock (_gate) _raw.RemoveAll(prior => ReferenceEquals(prior, actual));
    }
    private Exception Capture(Task actual, Exception observed)
    {
        if (actual.IsFaulted && actual.Exception is { } clrContainer)
        {
            // Only this exact immediate Task.Exception container is opened. Foreign nested
            // and empty aggregates are kept as the actual opaque causes they are.
            lock (_gate) _rawFaults[actual] = clrContainer.InnerExceptions.ToArray();
            return clrContainer;
        }
        if (actual.IsCanceled)
        { lock (_gate) _rawFaults[actual] = [observed]; return observed; }
        return Remember(observed);
    }
    private static bool IsObservedBodyFailure(Exception actual, Exception? observed)
    {
        if (observed is null) return false;
        // Exact supported CLR envelopes only. Bound both distinct references and
        // total edges; a shared wrapper DAG is proved once, never expanded into
        // exponentially repeated paths. Opaque, empty, cyclic or over-bound
        // envelopes remain unexpected original occurrences in owner custody.
        const int maximumNodes = 4096, maximumEdges = 4096, maximumDepth = 64;
        var pending = new Stack<(Exception Value, int Depth, bool Finish)>();
        var active = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var proved = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var nodes = 0; var edges = 0;
        pending.Push((actual, 0, false));
        while (pending.Count != 0)
        {
            var (value, depth, finish) = pending.Pop();
            if (finish) { active.Remove(value); proved.Add(value); continue; }
            if (depth > maximumDepth) return false;
            if (ReferenceEquals(value, observed)) continue;
            if (active.Contains(value)) return false;
            if (proved.Contains(value)) continue;
            if (++nodes > maximumNodes || value.GetType() != typeof(AggregateException) ||
                value is not AggregateException { InnerExceptions.Count: > 0 } combined) return false;
            if (combined.InnerExceptions.Count > maximumEdges - edges) return false;
            edges += combined.InnerExceptions.Count; active.Add(value);
            pending.Push((value, depth, true));
            for (var index = combined.InnerExceptions.Count - 1; index >= 0; index--)
                pending.Push((combined.InnerExceptions[index], depth + 1, false));
        }
        return true;
    }
    private Exception RememberUnexpectedCallback(Exception cause)
    {
        Remember(cause); unexpectedCallback?.Invoke(cause); return cause;
    }
    private Exception Remember(Exception cause)
    {
        lock (_gate) if (!_errors.Any(prior => ReferenceEquals(prior, cause))) _errors.Add(cause);
        return cause;
    }
}
