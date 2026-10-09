using Haven.Application;

namespace HavenOS.Home.Core;

// Original raw-source custody only. This helper issues no installed identity,
// connection, Home readiness, permission or service compatibility observation.
internal sealed class HomeNativeOriginalStartupScope
{
    private readonly object _owner;
    private readonly Func<object?> _product;
    private readonly Action<Task> _retain;
    private readonly Action<Exception>? _retainOriginalCause;
    private readonly HomeOwnershipOriginalSourceCallbacks _protocol;
    private readonly object _sync = new();
    private readonly List<Task> _raw = [];
    private readonly List<Exception> _errors = [];
    private readonly List<NativeResource> _native = [];
    private readonly List<HomeNativeOriginalStartupScope> _children = [];
    internal Task? Driver { get; private set; }
    private sealed class NativeResource(IDisposable actual)
    { internal readonly IDisposable Actual = actual; internal Task? Close; }
    internal HomeNativeOriginalStartupScope(object owner, Func<object?> product,
        Action<Action> scope, Action<Task> retain, Action<Exception>? retainOriginalCause = null)
    {
        _owner = owner; _product = product; _retain = retain; _retainOriginalCause = retainOriginalCause;
        _protocol = new(body => Physical(() => { scope(() => ForwardOriginalCallback(body)); return true; }),
            raw => Physical(() => { retain(raw); return true; }));
    }
    private void ForwardOriginalCallback(Action body)
    {
        // Late/repeated invocations happen after Run has returned. Record them
        // at this actual callback boundary so the product sink stays informed.
        try { Physical(() => { body(); return true; }); }
        catch (Exception cause) { Keep(cause); throw; }
    }
    private T Physical<T>(Func<T> body) => CloudflareOriginalExecutionGuard.InvokeOriginal(_owner,
        () => _product() is { } actual ? CloudflareOriginalExecutionGuard.InvokeOriginal(actual, body) : body());
    internal void DemandExternalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(_owner);
        if (_product() is { } product) CloudflareOriginalExecutionGuard.DemandExternalJoin(product);
    }
    internal void Keep(Exception cause)
    {
        bool added;
        lock (_sync)
        {
            added = !_errors.Any(known => ReferenceEquals(known, cause));
            if (added) _errors.Add(cause);
        }
        // The sink is an actual private product owner, never a borrowed callback.
        // Its tiny occurrence roots survive pruning of a fully joined source.
        if (added) _retainOriginalCause?.Invoke(cause);
    }
    internal Exception[] Errors
    {
        get { lock (_sync) return _errors.Concat(_protocol.Errors).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray(); }
    }
    internal void Throw()
    { if (Errors is { Length: > 0 } causes) throw new AggregateException("Original native startup/raw/caller source did not settle.", causes); }
    internal void Run(Action body) { try { _protocol.Run(body); } catch (Exception cause) { Keep(cause); throw; } }
    internal T Invoke<T>(Func<T> body) { T result = default!; Run(() => { result = body(); }); return result; }
    // The invocation already strongly owns its Driver; do not add that enclosing
    // task to its own raw cohort when forwarding publication to the caller.
    internal void Publish(Task actual) { try { Run(() => _retain(actual)); } catch (Exception cause) { Keep(cause); } }
    internal void Retain(Task raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        lock (_sync) if (!_raw.Any(known => ReferenceEquals(known, raw))) _raw.Add(raw);
        try { _protocol.Retain(raw); } catch (Exception cause) { Keep(cause); throw; }
    }
    internal async Task<T> Read<T>(Func<Task<T>> acquire, Action<T>? capture = null)
    {
        Task<T>? raw = null; T value = default!;
        try { Run(() => { raw = acquire() ?? throw new InvalidOperationException("No original native startup Task returned."); Retain(raw); }); } catch (Exception cause) { Keep(cause); }
        if (raw is not null)
            try { value = await raw.ConfigureAwait(false); capture?.Invoke(value); }
            catch (Exception cause) { Keep(raw.Exception ?? cause); }
        else Keep(new InvalidOperationException("The actual native startup source returned no original Task."));
        Throw(); return value;
    }
    internal async Task Read(Func<Task> acquire)
    {
        Task? raw = null;
        try { Run(() => { raw = acquire() ?? throw new InvalidOperationException("No original native startup Task returned."); Retain(raw); }); } catch (Exception cause) { Keep(cause); }
        if (raw is not null) try { await raw.ConfigureAwait(false); } catch (Exception cause) { Keep(raw.Exception ?? cause); }
        else Keep(new InvalidOperationException("The actual native startup source returned no original Task."));
        Throw();
    }
    internal async Task JoinAll()
    {
        Task[] all; lock (_sync) all = _raw.ToArray();
        foreach (var raw in all) try { await raw.ConfigureAwait(false); } catch (Exception cause) { Keep(raw.Exception ?? cause); }
    }
    internal Task<T> BeginChild<T>(Func<HomeNativeOriginalStartupScope, Task<T>> body)
    {
        var owner = new object();
        var child = new HomeNativeOriginalStartupScope(owner, () => null, Run, Retain);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = Drive(); child.Driver = actual;
        lock (_sync) _children.Add(child); // Before any borrowed callback.
        child.Publish(actual); start.SetResult(); return actual;
        async Task<T> Drive()
        {
            await start.Task.ConfigureAwait(false);
            using var own = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
            T value = default!;
            try { child.Throw(); value = await body(child).ConfigureAwait(false); }
            catch (Exception cause) { child.Keep(cause); }
            await child.CloseNativeResources().ConfigureAwait(false);
            await child.JoinAll().ConfigureAwait(false); child.Throw(); return value;
        }
    }
    internal T CaptureNative<T>(T actual) where T : IDisposable
    {
        lock (_sync) if (!_native.Any(known => ReferenceEquals(known.Actual, actual))) _native.Add(new(actual));
        return actual;
    }
    internal async Task CloseNativeResources()
    {
        NativeResource[] all; lock (_sync) all = _native.ToArray();
        foreach (var resource in all.Reverse())
        {
            Task same; lock (resource) same = Close(resource.Actual, ref resource.Close);
            try { await same.ConfigureAwait(false); } catch (Exception cause) { Keep(same.Exception ?? cause); }
        }
    }
    // Cleanup is source-owned and must acquire every sibling even if the former
    // productive caller scope has sealed. Raw custody still precedes forwarding.
    internal async Task Cleanup(Func<Task> acquire, Action<Task>? capture = null)
    {
        Task? raw = null;
        try { raw = Physical(acquire); capture?.Invoke(raw); }
        catch (Exception cause) { Keep(cause); }
        if (raw is not null)
        {
            try { Retain(raw); } catch (Exception cause) { Keep(cause); }
            try { await raw.ConfigureAwait(false); } catch (Exception cause) { Keep(raw.Exception ?? cause); }
        }
    }
    // Native synchronous close is still a real once-only source receipt. Caller
    // failure after an actual successful dispose cannot cause physical replay.
    internal Task Close(IDisposable actual, ref Task? cached)
    {
        if (cached is not null) return cached;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cached = done.Task;
        try { Retain(done.Task); } catch (Exception cause) { Keep(cause); }
        try { Physical(() => { actual.Dispose(); return true; }); done.SetResult(); }
        catch (Exception cause) { Keep(cause); done.SetException(cause); }
        return done.Task;
    }
}
