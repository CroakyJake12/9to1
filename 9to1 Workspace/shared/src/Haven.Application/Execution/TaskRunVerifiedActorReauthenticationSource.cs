using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Private renewal identity custody over the SAME trusted Task actor producer. Browser DI must
/// supply the concrete BrowserTaskActorSource backed by the maintained signed broker/account/profile
/// path. No stored actor or caller-provided revision is read here. This supplies identity only;
/// context access, canonical no-overlap custody and the real binding CAS remain separate owners.
/// </summary>
public sealed class TaskRunVerifiedActorReauthenticationSource : ITaskRunVerifiedReauthenticationSource, IAsyncDisposable
{
    private const int Capacity = 128;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Operation> _work = [];
    private readonly HashSet<IdentityLease> _leases = [];
    private readonly List<Exception> _closeErrors = [];
    private readonly AsyncLocal<Phase?> _executing = new();
    [ThreadStatic] private static List<TaskRunVerifiedActorReauthenticationSource>? _physical;
    private bool _closing;
    private Exception? _capacityRefusal;
    private Task? _close;

    public TaskRunVerifiedActorReauthenticationSource(IAuthenticatedResourceActorSource sameTrustedTaskActors)
        => _actors = sameTrustedTaskActors ?? throw new ArgumentNullException(nameof(sameTrustedTaskActors));

    internal IAuthenticatedResourceActorSource OriginalVerifiedTaskActors => _actors;

    public ValueTask<ITaskRunVerifiedReauthenticationLease> AcquireOriginalAsync(
        TaskExecutionOwnerBinding previousOwner, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(previousOwner);
        if (previousOwner.TaskId == Guid.Empty || previousOwner.ContextId == Guid.Empty || previousOwner.ExecutionId == Guid.Empty ||
            previousOwner.AccountId is not { } account || account == Guid.Empty || previousOwner.OrganisationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(previousOwner.ActorId) || string.IsNullOrWhiteSpace(previousOwner.ProfileId) ||
            string.IsNullOrWhiteSpace(previousOwner.AuthenticationRevision))
            throw new UnauthorizedAccessException("Renewal requires a genuinely account-bound previous Task owner.");
        return new(StartOriginalAsync(null, previousOwner, acquire: true, token));
    }

    public async ValueTask ValidateOriginalAsync(ITaskRunVerifiedReauthenticationLease sameOriginal,
        TaskExecutionOwnerBinding previousOwner, CancellationToken token)
    {
        IdentityLease original;
        lock (_sync)
        {
            RequireOpen();
            original = RequireLease(sameOriginal);
            if (original.Previous != previousOwner || original.Closing)
                throw new UnauthorizedAccessException("The original renewal identity/previous binding is unavailable.");
        }
        var task = StartOriginalAsync(original, previousOwner, acquire: false, token);
        await task.ConfigureAwait(false); // Driver itself preserves actual raw fault groups/status.
    }

    private Task<ITaskRunVerifiedReauthenticationLease> StartOriginalAsync(IdentityLease? lease,
        TaskExecutionOwnerBinding previous, bool acquire, CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        TaskCompletionSource start;
        Operation operation;
        lock (_sync)
        {
            RequireOpen();
            if (lease is not null && (lease.Closing || !_leases.Contains(lease)))
                throw new UnauthorizedAccessException("Original renewal identity retired.");
            _work.RemoveAll(op => op.Driver.IsCompletedSuccessfully && op.Lease is { HealthyClosed: true, Close.IsCompletedSuccessfully: true });
            _leases.RemoveWhere(item => item.HealthyClosed && item.Close?.IsCompletedSuccessfully == true);
            if (_capacityRefusal is not null || _work.Count >= Capacity || _leases.Count >= Capacity)
            {
                _capacityRefusal ??= new InvalidOperationException("Original renewal identity custody is full.");
                throw _capacityRefusal;
            }
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operation = new(lease, previous, callerToken);
            operation.Driver = RunOriginalAsync(operation, start.Task, acquire);
            _work.Add(operation); lease?.Work.Add(operation); // SAME whole driver owned before callbacks.
        }
        start.SetResult(); return operation.Driver;
    }

    private async Task<ITaskRunVerifiedReauthenticationLease> RunOriginalAsync(Operation operation, Task start, bool acquire)
    {
        await start.ConfigureAwait(false);
        var old = _executing.Value; var phase = new(this, old); _executing.Value = phase;
        CancellationTokenSource? linked = null;
        ITaskRunVerifiedReauthenticationLease? result = null;
        var failures = new List<Exception>();
        var cleanupFaulted = false;
        try
        {
            lock (_sync)
            {
                RequireOpen();
                if (operation.Lease is { Closing: true }) throw new UnauthorizedAccessException("Original renewal identity retired before read.");
            }
            operation.PreReadCancellation = operation.CallerToken.IsCancellationRequested;
            operation.CallerToken.ThrowIfCancellationRequested();
            linked = operation.Lease is { } lease
                ? CancellationTokenSource.CreateLinkedTokenSource(operation.CallerToken, _stop.Token, lease.Stop.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(operation.CallerToken, _stop.Token);
            operation.PreReadCancellation = linked.IsCancellationRequested;
            linked.Token.ThrowIfCancellationRequested();
            try { operation.ActorRead = InvokePhysical(() => _actors.GetCurrentAsync(linked.Token).AsTask()); }
            catch (OperationCanceledException error)
            { throw new AggregateException("The verified identity read faulted synchronously.", error); }
            AuthenticatedResourceActor? actor;
            try { actor = await operation.ActorRead.ConfigureAwait(false); }
            catch (Exception error) { Capture(operation, operation.ActorRead, error); throw; }
            if (actor is null || !SameStableOwner(operation.Previous, actor) ||
                string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
                throw new UnauthorizedAccessException("Current verified account/profile does not own the original Task.");
            operation.PreReadCancellation = linked.IsCancellationRequested;
            linked.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                RequireOpen();
                if (acquire)
                {
                    if (actor.AuthenticationRevision == operation.Previous.AuthenticationRevision)
                        throw new UnauthorizedAccessException("A genuinely new verified session activation is required for renewal.");
                    var issued = new IdentityLease(this, operation.Previous, actor);
                    operation.Lease = issued; issued.Work.Add(operation); _leases.Add(issued);
                    result = issued;
                }
                else
                {
                    var issued = operation.Lease!;
                    if (issued.Closing || !_leases.Contains(issued) || actor != issued.CurrentActor)
                        throw new UnauthorizedAccessException("Verified renewal identity/session generation changed during revalidation.");
                    result = issued;
                }
            }
        }
        catch (Exception error)
        {
            Add(failures, error);
            foreach (var cause in operation.Errors) Add(failures, cause);
            if (operation.ActorRead?.IsFaulted == true && operation.ActorRead.Exception is { } group)
            {
                // Preserve the SAME raw fault snapshot, including a sole faulted OCE.
                Add(failures, group); foreach (var cause in group.InnerExceptions) Add(failures, cause);
            }
        }
        finally
        {
            try { linked?.Dispose(); } catch (Exception error) { cleanupFaulted = true; Add(failures, error); }
            lock (_sync)
            {
                foreach (var error in failures) Add(operation.Errors, error);
                if (failures.Count != 0 && acquire && operation.Lease is { } partial) partial.Closing = true;
            }
            phase.Retire(); _executing.Value = old;
        }
        Throw(failures, actualCancellation: !cleanupFaulted && (operation.ActorRead?.IsCanceled == true || operation.PreReadCancellation));
        return result ?? throw new InvalidOperationException("No original verified identity was issued.");
    }

    private IdentityLease RequireLease(ITaskRunVerifiedReauthenticationLease original)
    {
        if (original is not IdentityLease actual || !ReferenceEquals(actual.Source, this) || !_leases.Contains(actual))
            throw new UnauthorizedAccessException("The same privately issued renewal identity is required.");
        return actual;
    }

    private static bool SameStableOwner(TaskExecutionOwnerBinding previous, AuthenticatedResourceActor current)
        => current.ActorId == previous.ActorId && current.ProfileId == previous.ProfileId &&
           current.AccountId == previous.AccountId && current.OrganisationId == previous.OrganisationId;

    private sealed class IdentityLease(TaskRunVerifiedActorReauthenticationSource source,
        TaskExecutionOwnerBinding previous, AuthenticatedResourceActor current) : ITaskRunVerifiedReauthenticationLease
    {
        public TaskRunVerifiedActorReauthenticationSource Source { get; } = source;
        public TaskExecutionOwnerBinding Previous { get; } = previous;
        public AuthenticatedResourceActor CurrentActor { get; } = current;
        public readonly List<Operation> Work = [];
        public readonly CancellationTokenSource Stop = new();
        public bool Closing, HealthyClosed;
        public Task? Close;
        public ValueTask DisposeAsync() { Source.DemandExternalClose(); return new(Source.StartLeaseClose(this)); }
    }

    private Task StartLeaseClose(IdentityLease lease)
    {
        TaskCompletionSource? start = null; Task actual;
        lock (_sync)
        {
            if (lease.Close is null)
            {
                lease.Closing = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lease.Close = CloseLeaseOriginalAsync(lease, start.Task);
            }
            actual = lease.Close;
        }
        start?.SetResult(); return actual;
    }

    private async Task CloseLeaseOriginalAsync(IdentityLease lease, Task start)
    {
        await start.ConfigureAwait(false);
        var old = _executing.Value; var phase = new(this, old); _executing.Value = phase;
        var failures = new List<Exception>();
        try
        {
            try { InvokePhysical(() => { lease.Stop.Cancel(); return true; }); }
            catch (Exception error) { Add(failures, error); }
            Operation[] work; lock (_sync) work = lease.Work.ToArray();
            foreach (var operation in work) await JoinOriginalOperationAsync(operation, failures).ConfigureAwait(false);
            try { lease.Stop.Dispose(); } catch (Exception error) { Add(failures, error); }
            lock (_sync) lease.HealthyClosed = failures.Count == 0;
        }
        finally { phase.Retire(); _executing.Value = old; }
        Throw(failures);
    }

    public ValueTask DisposeAsync()
    {
        DemandExternalClose();
        TaskCompletionSource? start = null; Task actual;
        lock (_sync)
        {
            if (_close is null)
            {
                _closing = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseSourceOriginalAsync(start.Task);
            }
            actual = _close;
        }
        start?.SetResult(); return new(actual);
    }

    private async Task CloseSourceOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var old = _executing.Value; var phase = new(this, old); _executing.Value = phase;
        var failures = new List<Exception>();
        try
        {
            try { InvokePhysical(() => { _stop.Cancel(); return true; }); } catch (Exception error) { Add(failures, error); }
            IdentityLease[] leases; Operation[] work;
            lock (_sync) { leases = _leases.ToArray(); work = _work.ToArray(); }
            foreach (var lease in leases)
            {
                Task? close = null;
                try { close = StartLeaseClose(lease); } catch (Exception error) { Add(failures, error); }
                if (close is not null) await JoinOriginalTaskAsync(close, failures).ConfigureAwait(false);
            }
            foreach (var operation in work) await JoinOriginalOperationAsync(operation, failures).ConfigureAwait(false);
            if (_capacityRefusal is not null) Add(failures, _capacityRefusal);
            try { _stop.Dispose(); } catch (Exception error) { Add(failures, error); }
            lock (_sync) foreach (var cause in failures) Add(_closeErrors, cause);
        }
        finally { phase.Retire(); _executing.Value = old; }
        Throw(failures);
    }

    private void DemandExternalClose()
    {
        for (var phase = _executing.Value; phase is not null; phase = phase.Parent)
            if (phase.Live && ReferenceEquals(phase.Source, this)) throw new InvalidOperationException("An identity original cannot join its containing renewal source.");
        if (_physical?.Any(item => ReferenceEquals(item, this)) == true)
            throw new InvalidOperationException("An identity callback cannot join its containing renewal source.");
    }

    private T InvokePhysical<T>(Func<T> callback)
    {
        var calls = _physical ??= []; calls.Add(this);
        try { return callback(); } finally { calls.RemoveAt(calls.Count - 1); }
    }
    private void RequireOpen() { if (_closing) throw new ObjectDisposedException(nameof(TaskRunVerifiedActorReauthenticationSource)); }

    private sealed class Operation(IdentityLease? lease, TaskExecutionOwnerBinding previous, CancellationToken caller)
    {
        public IdentityLease? Lease = lease;
        public TaskExecutionOwnerBinding Previous { get; } = previous;
        public CancellationToken CallerToken { get; } = caller;
        public Task<ITaskRunVerifiedReauthenticationLease> Driver = null!;
        public Task<AuthenticatedResourceActor?>? ActorRead;
        public bool PreReadCancellation;
        public readonly List<Exception> Errors = [];
    }
    private sealed class Phase(TaskRunVerifiedActorReauthenticationSource source, Phase? parent)
    {
        public TaskRunVerifiedActorReauthenticationSource Source { get; } = source;
        public Phase? Parent { get; } = parent;
        private int _live = 1;
        public bool Live => Volatile.Read(ref _live) != 0;
        public void Retire() => Interlocked.Exchange(ref _live, 0);
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error); }
    private static void Capture(Operation operation, Task original, Exception observed)
    {
        Add(operation.Errors, observed);
        if (original.Exception is { } group) foreach (var cause in group.InnerExceptions) Add(operation.Errors, cause);
    }
    private static async Task JoinOriginalOperationAsync(Operation operation, List<Exception> errors)
    {
        await JoinOriginalTaskAsync(operation.Driver, errors).ConfigureAwait(false);
        if (operation.ActorRead is { } raw) await JoinOriginalTaskAsync(raw, errors).ConfigureAwait(false);
        foreach (var cause in operation.Errors) Add(errors, cause);
    }
    private static async Task JoinOriginalTaskAsync(Task original, List<Exception> errors)
    {
        try { await original.ConfigureAwait(false); }
        catch (Exception observed)
        {
            Add(errors, observed);
            if (original.Exception is { } group) foreach (var cause in group.InnerExceptions) Add(errors, cause);
        }
    }
    private static void Throw(List<Exception> errors, bool actualCancellation = false)
    {
        if (errors.Count == 1)
        {
            if (errors[0] is OperationCanceledException && !actualCancellation)
                throw new AggregateException("Original synchronous/faulted cancellation cause.", errors[0]);
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }
        if (errors.Count > 1) throw new AggregateException("Original verified identity acquisition/retirement faults.", errors);
    }
}
