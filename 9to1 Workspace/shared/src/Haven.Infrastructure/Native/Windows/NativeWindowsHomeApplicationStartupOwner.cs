using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Owns the actual installed Root observation, Home connection and its
/// client for one native app. Locators and required-service declarations confer
/// no caller identity, compatibility, READ consent or product action authority.</summary>
public sealed class NativeWindowsHomeApplicationStartupOwner : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationToken _originalAppLifetime;
    private readonly string _machine;
    private CancellationTokenSource? _lifetime;
    private readonly TaskCompletionSource<CancellationTokenSource?> _originalLifetimeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _originalLifetimeCancellation, _rawLifetimeCancel;
    private Operation? _originalCancellationSources;
    private NativeWindowsHomeRootHostVerifier? _verifier;
    private readonly HomeCompatibilityRequest _requirements;
    private readonly List<Operation> _operations = [];
    private readonly List<Exception> _originalCallbackErrors = [];
    private HomeNativeWindowsAppConnection? _connection;
    private Task<HomeNativeStartupObservation>? _connect;
    private Task? _close, _connectionClose, _verifierClose, _lifetimeClose;
    private bool _retiring;
    private Operation? _cleanup;
    private sealed class Operation
    {
        internal readonly CloudflareOriginalTaskLedger Sources = new();
        internal readonly NativeWindowsHomeApplicationStartupOwner Owner;
        internal readonly Action<Action> Scope;
        internal readonly Action<Task> Retainer;
        internal Task Driver = null!;
        internal Operation(NativeWindowsHomeApplicationStartupOwner owner, Action<Action> scope, Action<Task> retain)
        { Owner = owner; Scope = scope; Retainer = retain; Sources.BindOriginalOwner(owner); }
        internal void Run(Action body)
        {
            var thread = Environment.CurrentManagedThreadId; var active = 1; var invoked = 0;
            var causes = new List<Exception>(); var sync = new object();
            void Keep(Exception cause) { Sources.Retain(cause); Owner.RetainOriginalCallbackError(cause); lock (sync) causes.Add(cause); }
            void Callback()
            {
                if (Volatile.Read(ref active) == 0 || Environment.CurrentManagedThreadId != thread || Interlocked.Increment(ref invoked) != 1)
                { var cause = new InvalidOperationException("The original startup callback is late, repeated or foreign-thread."); Keep(cause); throw cause; }
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(Owner, () => { body(); return true; }); }
                catch (Exception cause) { Keep(cause); throw; }
            }
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(Owner, () => { Scope(Callback); return true; }); }
            catch (Exception cause) { Keep(cause); }
            finally { Interlocked.Exchange(ref active, 0); }
            if (Volatile.Read(ref invoked) != 1) Keep(new InvalidOperationException("The actual startup finite callback was not invoked exactly once."));
            Exception[] all; lock (sync) all = causes.ToArray();
            if (all.Length != 0) throw new AggregateException("Original startup caller/body/protocol failed.", all);
        }
        internal void Retain(Task same)
        {
            ArgumentNullException.ThrowIfNull(same);
            _ = Sources.Track(same);
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(Owner, () => { Retainer(same); return true; }); }
            catch (Exception cause) { Sources.Retain(cause); Owner.RetainOriginalCallbackError(cause); throw; }
        }
        internal void Publish(Task same)
        { try { Run(() => Retainer(same)); } catch (Exception cause) { Sources.Retain(cause); Owner.RetainOriginalCallbackError(cause); } }
        internal async Task<T> Read<T>(Func<Task<T>> acquire, Action<T>? capture = null)
        {
            Throw();
            Task<T>? raw = null; T result = default!;
            try { Run(() => { raw = acquire() ?? throw new InvalidOperationException("No original startup Task returned."); Retain(raw); }); } catch (Exception cause) { Sources.Retain(cause); }
            if (raw is not null)
                try { result = await Sources.AwaitAsync(raw).ConfigureAwait(false); capture?.Invoke(result); }
                catch (Exception cause) { Sources.Capture(raw, cause); }
            else Sources.Retain(new InvalidOperationException("The actual startup source returned no original Task."));
            Throw(); return result;
        }
        internal void Throw()
        {
            Exception[] callbacks; lock (Owner._gate) callbacks = Owner._originalCallbackErrors.ToArray();
            var causes = Sources.OriginalErrors.Concat(callbacks).ToArray();
            if (causes.Length != 0) throw new AggregateException("Actual native startup originals remain unconfirmed.", causes);
        }
        internal bool Healthy
        {
            get { var same = Driver; if (!same.IsCompletedSuccessfully) return false; same.GetAwaiter().GetResult(); return Sources.OriginalErrors.Count == 0; }
        }
    }
    private void RetainOriginalCallbackError(Exception sameCause)
    { lock (_gate) if (!_originalCallbackErrors.Any(actual => ReferenceEquals(actual, sameCause))) _originalCallbackErrors.Add(sameCause); }
    public NativeWindowsHomeApplicationStartupOwner(string actualMachineStateLocator,
        HomeCompatibilityRequest trustedApplicationRequirements, CancellationToken originalAppLifetime)
    {
        if (!originalAppLifetime.CanBeCanceled) throw new ArgumentException("The actual app owning lifetime is required.", nameof(originalAppLifetime));
        ArgumentNullException.ThrowIfNull(trustedApplicationRequirements);
        var rows = trustedApplicationRequirements.RequiredServices?.Take(65).ToArray();
        if (rows is null || rows.Length is 0 or > 64 || rows.Any(row => row is null))
            throw new ArgumentException("Bounded trusted application service declarations are required.", nameof(trustedApplicationRequirements));
        _requirements = trustedApplicationRequirements with { RequiredServices = Array.AsReadOnly(rows.Select(row => row with { }).ToArray()) };
        _originalAppLifetime = originalAppLifetime; _machine = Path.GetFullPath(actualMachineStateLocator);
    }
    public IHomeNativeStartupSession? OriginalStartup { get { lock (_gate) return _connection?.Startup; } }
    public Task<HomeNativeStartupObservation> ConnectWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        lock (_gate) if (_connect is not null) return _connect;
        return Begin(async work =>
        {
            work.Run(() =>
            {
                var sameLifetime = _lifetime = CancellationTokenSource.CreateLinkedTokenSource(_originalAppLifetime);
                // Publication precedes the verifier constructor and caller postguard.
                _originalLifetimeReady.TrySetResult(sameLifetime);
                _verifier = new(_machine, sameLifetime.Token);
            });
            var endpoint = await work.Read(() => _verifier!.ObserveOriginalEndpointWithinSourceAsync(work.Run, work.Retain, token)).ConfigureAwait(false);
            if (endpoint is null) return new(HomeNativeStartupState.Unready, "HomeInstalledBootstrapRequired",
                "The authenticated installed Root could not confirm its current Home host.");
            if (!_verifier!.IsIssuedOriginalEndpoint(endpoint)) throw new UnauthorizedAccessException("The actual Root host endpoint is not source-issued and healthy.");
            await work.Read(() => HomeNativeWindowsAppConnection.ConnectWithinOriginalSourceAsync(endpoint.Endpoint, _verifier!,
                endpoint.HostRequirement, _requirements, _lifetime!.Token, work.Run, work.Retain, token),
                actual => { lock (_gate) _connection = actual; }).ConfigureAwait(false);
            return await work.Read(() => _connection!.CheckStartupWithinOriginalSourceAsync(work.Run, work.Retain, token)).ConfigureAwait(false);
        }, scope, retain, actual => _connect = actual, () => _connect,
            () => _originalLifetimeReady.TrySetResult(null));
    }
    public Task<HomeNativeStartupObservation> RefreshWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token = default) =>
        Begin(async work =>
        {
            HomeNativeWindowsAppConnection actual; lock (_gate) actual = _connection ?? throw new InvalidOperationException("No original Home connection is retained.");
            return await work.Read(() => actual.CheckStartupWithinOriginalSourceAsync(work.Run, work.Retain, token)).ConfigureAwait(false);
        }, scope, retain);
    public Task InitializeWithinOriginalSourceAsync(Func<CancellationToken, Task> initializer,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default) =>
        Begin(async work =>
        {
            HomeNativeWindowsAppConnection actual; lock (_gate) actual = _connection ?? throw new InvalidOperationException("No original Home connection is retained.");
            Task? raw = null;
            try { work.Run(() => { raw = actual.InitializeWithinOriginalSourceAsync(initializer, work.Run, work.Retain, token); work.Retain(raw); }); }
            catch (Exception cause) { work.Sources.Retain(cause); }
            if (raw is not null) try { await work.Sources.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { work.Sources.Capture(raw, cause); }
            work.Throw(); return true;
        }, scope, retain);
    private Task<T> Begin<T>(Func<Operation, Task<T>> body, Action<Action> scope, Action<Task> retain, Action<Task<T>>? capture = null, Func<Task<T>?>? sameExisting = null, Action? settled = null)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var work = new Operation(this, scope, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> actual;
        lock (_gate)
        {
            if (sameExisting?.Invoke() is { } cached) return cached;
            ObjectDisposedException.ThrowIf(_retiring, this); _originalAppLifetime.ThrowIfCancellationRequested();
            if (_originalCallbackErrors.Count != 0)
                throw new AggregateException("An actual startup callback occurrence remains unconfirmed.", _originalCallbackErrors);
            _operations.RemoveAll(old => old.Healthy);
            if (_operations.Count >= 128) throw new InvalidOperationException("Unconfirmed native startup originals remain retained.");
            actual = Drive(); work.Driver = actual; _operations.Add(work); capture?.Invoke(actual);
        }
        work.Publish(actual); start.SetResult(); return actual;
        async Task<T> Drive()
        {
            await start.Task.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            T result = default!;
            try { work.Throw(); result = await body(work).ConfigureAwait(false); }
            catch (Exception cause) { work.Sources.Retain(cause); }
            finally
            {
                try { settled?.Invoke(); } catch (Exception cause) { work.Sources.Retain(cause); }
            }
            await work.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false); work.Throw(); return result;
        }
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    internal Task? OriginalLifetimeCancellation { get { lock (_gate) return _originalLifetimeCancellation; } }
    internal Task? OriginalLifetimeCancel { get { lock (_gate) return _rawLifetimeCancel; } }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null)
            {
                if (_connect is null) _originalLifetimeReady.TrySetResult(null);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var cancellation = _originalCancellationSources = new Operation(this, body => body(), _ => { });
                _originalLifetimeCancellation = CancelPublishedLifetime(start.Task, cancellation);
                cancellation.Driver = _originalLifetimeCancellation;
                _close = Close(start.Task);
            }
            actual = _close;
        }
        start?.SetResult(); return actual;
        async Task Close(Task begin)
        {
            await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var cleanup = _cleanup = new Operation(this, body => body(), _ => { });
            cleanup.Driver = _close!;
            // An admitted Connect can publish its actual CTS after retirement begins.
            // This child waits only that publication, never the encompassing Connect.
            try { await cleanup.Sources.AwaitAsync(_originalLifetimeCancellation!).ConfigureAwait(false); }
            catch (Exception cause) { cleanup.Sources.Capture(_originalLifetimeCancellation, cause); }
            Operation[] all; lock (_gate) all = _operations.ToArray();
            foreach (var operation in all)
            {
                try { await operation.Driver.ConfigureAwait(false); } catch (Exception cause) { cleanup.Sources.Capture(operation.Driver, cause); }
                await operation.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in operation.Sources.OriginalErrors) cleanup.Sources.Retain(cause);
            }
            HomeNativeWindowsAppConnection? connection; lock (_gate) connection = _connection;
            if (connection is not null) await JoinClose(connection.CloseAndDrainAsync, raw => _connectionClose = raw).ConfigureAwait(false);
            if (_verifier is { } sameVerifier) await JoinClose(sameVerifier.CloseAndDrainOriginalAsync, raw => _verifierClose = raw).ConfigureAwait(false);
            if (_lifetime is { } sameLifetime)
            {
                var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _lifetimeClose = disposed.Task; cleanup.Retain(disposed.Task);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { sameLifetime.Dispose(); return true; }); disposed.SetResult(); }
                catch (Exception cause) { disposed.SetException(cause); }
            }
            await cleanup.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            // A saved callback can fault after its formerly healthy operation was pruned.
            Exception[] callbackErrors; lock (_gate) callbackErrors = _originalCallbackErrors.ToArray();
            foreach (var cause in callbackErrors) cleanup.Sources.Retain(cause);
            cleanup.Throw();
            async Task JoinClose(Func<Task> acquire, Action<Task> capture)
            {
                Task? same = null;
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { same = acquire(); capture(same); cleanup.Retain(same); return true; }); }
                catch (Exception cause) { cleanup.Sources.Retain(cause); }
                if (same is not null) try { await cleanup.Sources.AwaitAsync(same).ConfigureAwait(false); } catch (Exception cause) { cleanup.Sources.Capture(same, cause); }
            }
        }
    }
    private async Task CancelPublishedLifetime(Task begin, Operation cancellation)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        CancellationTokenSource? sameLifetime = null;
        try { sameLifetime = await cancellation.Sources.AwaitAsync(_originalLifetimeReady.Task).ConfigureAwait(false); }
        catch (Exception cause) { cancellation.Sources.Capture(_originalLifetimeReady.Task, cause); }
        if (sameLifetime is not null)
        {
            // Once-only actual Cancel has its own retained receipt before any callback.
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _rawLifetimeCancel = canceled.Task;
            cancellation.Retain(canceled.Task);
            try { cancellation.Run(sameLifetime.Cancel); canceled.SetResult(); }
            catch (Exception cause) { canceled.SetException(cause); }
            try { await cancellation.Sources.AwaitAsync(canceled.Task).ConfigureAwait(false); }
            catch (Exception cause) { cancellation.Sources.Capture(canceled.Task, cause); }
        }
        await cancellation.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false); cancellation.Throw();
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
