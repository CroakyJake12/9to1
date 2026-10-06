using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority : ITaskRunOwnerReauthenticationAuthority
{
    private readonly List<RenewalWork> _renewalWork = [];
    private readonly HashSet<Renewal> _renewals = [];
    private readonly HashSet<Owner> _renewalReservations = [];
    private readonly AsyncLocal<RenewalPhase?> _renewalExecuting = new();
    [ThreadStatic] private static List<TaskRunPermissionAuthority>? _renewalPhysical;
    private bool _renewalClosing;
    private Exception? _renewalCapacityFailure;
    private Task? _renewalClose;

    // Configuration observation only. Actual private custody/context validation remains mandatory.
    public bool HasOwnerReauthenticationSources => _verifiedReauthentication is TaskRunVerifiedActorReauthenticationSource verified &&
        ReferenceEquals(verified.OriginalVerifiedTaskActors, _actors) && _contextReauthentication is not null &&
        _reauthenticationCustody is not null && _originalTasks is not null;

    public ValueTask<ITaskRunOwnerReauthenticationAdmission> PrepareOriginalAsync(
        TaskExecutionSnapshot currentSuspended, ITaskRunReauthenticationQuiescence sameOriginalQuiescence,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(currentSuspended); ArgumentNullException.ThrowIfNull(sameOriginalQuiescence);
        // Missing real producers refuses before sealing an otherwise healthy ordinary/local owner.
        DemandRenewalConfiguration();
        return new(StartRenewalWork(null, token, work => PrepareRenewalBodyAsync(work, currentSuspended, sameOriginalQuiescence, token)));
    }

    private async Task<ITaskRunOwnerReauthenticationAdmission> PrepareRenewalBodyAsync(RenewalWork work,
        TaskExecutionSnapshot proposed, ITaskRunReauthenticationQuiescence originalQuiescence, CancellationToken token)
    {
        var owner = RequireOwner(proposed); OwnerActivation activation;
        lock (_sync)
        {
            if (!_renewalReservations.Add(owner)) throw new UnauthorizedAccessException("An original owner renewal is already retained.");
            activation = owner.Activation;
        }
        ITaskRunVerifiedReauthenticationLease? identity = null;
        ITaskRunContextReauthenticationLease? context = null;
        var issued = false; var validatedQuiescence = false;
        try
        {
            var current = await ReadRenewalSnapshotAsync(work, proposed.TaskId, token).ConfigureAwait(false);
            DemandSuspendedPrevious(current, activation.Binding, proposed.PersistenceRevision);
            await work.AwaitAsync(InvokeRenewalPhysical(() => _reauthenticationCustody!.ValidateOriginalAsync(
                originalQuiescence, current, token).AsTask())).ConfigureAwait(false);
            validatedQuiescence = true;
            // This seal follows genuine whole-invocation no-overlap validation, never a DTO flag.
            lock (_sync) { DemandSameActivation(owner, activation); activation.Seal(); }
            identity = await work.AwaitAsync(InvokeRenewalPhysical(() => _verifiedReauthentication!.AcquireOriginalAsync(
                activation.Binding, token).AsTask())).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("No original verified identity was issued.");
            DemandStableRenewalIdentity(activation.Binding, identity.CurrentActor);
            context = await work.AwaitAsync(InvokeRenewalPhysical(() => _contextReauthentication!.AcquireOriginalAsync(
                current, identity, token).AsTask())).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("No actual context reauthorization scope was issued.");
            var next = activation.Binding with
            {
                AuthenticationRevision = identity.CurrentActor.AuthenticationRevision,
                AuthorizationReceiptReference = "task-owner-activation:" + Guid.NewGuid().ToString("N")
            };
            var renewal = new Renewal(this, owner, activation, next, current.PersistenceRevision,
                identity, context, originalQuiescence);
            await ValidateRenewalResourcesAsync(work, renewal, current, token).ConfigureAwait(false);
            lock (_sync)
            {
                DemandRenewalOpen(); DemandRetainedRenewalActivation(renewal);
                work.Renewal = renewal; renewal.Work.Add(work); _renewals.Add(renewal); issued = true;
            }
            return renewal;
        }
        finally
        {
            if (!issued)
            {
                // A failed acquisition without a returned resource remains unknown and retained.
                // Close every actually acquired resource independently; never unseal the old owner.
                if (context is not null) await work.CloseResourceAsync(context, InvokeRenewalPhysical).ConfigureAwait(false);
                if (identity is not null) await work.CloseResourceAsync(identity, InvokeRenewalPhysical).ConfigureAwait(false);
                if (validatedQuiescence) await work.CloseResourceAsync(originalQuiescence, InvokeRenewalPhysical).ConfigureAwait(false);
            }
        }
    }

    private async Task ValidateRenewalResourcesAsync(RenewalWork work, Renewal renewal,
        TaskExecutionSnapshot expected, CancellationToken token)
    {
        var current = await ReadRenewalSnapshotAsync(work, expected.TaskId, token).ConfigureAwait(false);
        DemandSuspendedPrevious(current, renewal.PreviousOwner, renewal.ExpectedPersistenceRevision);
        await work.AwaitAsync(InvokeRenewalPhysical(() => _contextReauthentication!.ValidateOriginalAsync(
            renewal.Context, current, renewal.Identity, token).AsTask())).ConfigureAwait(false);
        await work.AwaitAsync(InvokeRenewalPhysical(() => _reauthenticationCustody!.ValidateOriginalAsync(
            renewal.Quiescence, current, token).AsTask())).ConfigureAwait(false);
        // No later repository/context await can leave a stale authenticated actor at return.
        await work.AwaitAsync(InvokeRenewalPhysical(() => _verifiedReauthentication!.ValidateOriginalAsync(
            renewal.Identity, renewal.PreviousOwner, token).AsTask())).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_sync) DemandRetainedRenewalActivation(renewal);
    }

    public ValueTask ActivateAcknowledgedOriginalAsync(ITaskRunOwnerReauthenticationAdmission sameOriginalAdmission,
        ITaskRunReauthenticationAcknowledgment sameOriginalAcknowledgment, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameOriginalAcknowledgment); DemandExternalRenewalJoin(); DemandRenewalConfiguration();
        Renewal renewal;
        lock (_sync)
        {
            DemandRenewalOpen(); renewal = RequireRenewal(sameOriginalAdmission);
            if (renewal.Activation is not null)
            {
                if (!ReferenceEquals(renewal.Acknowledgment, sameOriginalAcknowledgment))
                    throw new UnauthorizedAccessException("The SAME original binding CAS acknowledgment is required.");
                return new(renewal.Activation); // SAME public original, including failure.
            }
            if (renewal.Close is not { IsCompletedSuccessfully: true })
                throw new UnauthorizedAccessException("Actual permission/context/quiescence scope and every pin must close successfully before activation.");
        }
        // Reserve and publish the SAME whole activation before ACK/repository/actor callbacks.
        TaskCompletionSource start; RenewalWork work;
        lock (_sync)
        {
            if (renewal.Activation is not null)
            {
                if (!ReferenceEquals(renewal.Acknowledgment, sameOriginalAcknowledgment))
                    throw new UnauthorizedAccessException("The SAME original binding CAS acknowledgment is required.");
                return new(renewal.Activation);
            }
            renewal.Acknowledgment = sameOriginalAcknowledgment;
            work = ReserveRenewalWork(renewal, addToScopeWork: false); start = NewRenewalGate();
            renewal.Activation = work.Driver = RunRenewalWorkAsync(work, start.Task,
                () => ActivateRenewalBodyAsync(work, renewal, sameOriginalAcknowledgment, token));
        }
        start.SetResult(); return new(renewal.Activation);
    }

    private async Task<object?> ActivateRenewalBodyAsync(RenewalWork work, Renewal renewal,
        ITaskRunReauthenticationAcknowledgment originalAck, CancellationToken token)
    {
        await work.AwaitAsync(InvokeRenewalPhysical(() => _reauthenticationCustody!.ValidateAcknowledgmentAsync(
            renewal, originalAck, token).AsTask())).ConfigureAwait(false);
        var current = await ReadRenewalSnapshotAsync(work, renewal.PreviousOwner.TaskId, token).ConfigureAwait(false);
        if (current.TaskId != renewal.NextOwner.TaskId || current.ContextId != renewal.NextOwner.ContextId ||
            current.ExecutionId != renewal.NextOwner.ExecutionId || current.OwnerBinding != renewal.NextOwner ||
            current.PersistenceRevision != checked(renewal.ExpectedPersistenceRevision + 1) || current.State != TaskExecutionLifecycle.Suspended)
            throw new UnauthorizedAccessException("The genuine acknowledged same-run binding CAS is no longer current.");
        // Sources are now closed, so reread the SAME trusted signed account/profile actor itself.
        // This is identity/currentness only; all future routes/tools/context effects read policy again.
        var actual = await work.AwaitAsync(InvokeRenewalPhysical(() => _actors.GetCurrentAsync(token).AsTask())).ConfigureAwait(false);
        if (actual != new AuthenticatedResourceActor(renewal.NextOwner.ActorId, renewal.NextOwner.ProfileId,
                renewal.NextOwner.AccountId, renewal.NextOwner.OrganisationId, renewal.NextOwner.AuthenticationRevision))
            throw new UnauthorizedAccessException("Verified session activation changed after the actual binding CAS/cleanup.");
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            DemandRenewalOpen(); DemandRetainedRenewalActivation(renewal);
            if (renewal.Close is not { IsCompletedSuccessfully: true } || renewal.Errors.Count != 0)
                throw new UnauthorizedAccessException("An original renewal or cleanup fault remains retained.");
            renewal.StableOwner.PublishActivation(renewal.PreviousActivation, new(renewal.NextOwner));
            renewal.Activated = true;
        }
        // Data must await THIS actual task, validate SAME private ACK/current binding and only then
        // reopen its retained producer seal. No provider invocation, automatic resume or new run here.
        return null;
    }

    private async Task<TaskExecutionSnapshot> ReadRenewalSnapshotAsync(RenewalWork work, Guid taskId, CancellationToken token)
    {
        var original = InvokeRenewalPhysical(() => _originalTasks!().GetAsync(taskId, token));
        return await work.AwaitAsync(original).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual canonical Task is unavailable.");
    }
    private static void DemandSuspendedPrevious(TaskExecutionSnapshot current, TaskExecutionOwnerBinding previous, long revision)
    {
        if (revision <= 0 || current.PersistenceRevision != revision || current.State != TaskExecutionLifecycle.Suspended ||
            current.OwnerBinding != previous || current.TaskId != previous.TaskId || current.ContextId != previous.ContextId ||
            current.ExecutionId != previous.ExecutionId)
            throw new UnauthorizedAccessException("Renewal requires the exact current suspended same Task/context/run and expected revision.");
    }
    private static void DemandStableRenewalIdentity(TaskExecutionOwnerBinding previous, AuthenticatedResourceActor next)
    {
        if (!ValidActor(next) || next.ActorId != previous.ActorId || next.ProfileId != previous.ProfileId ||
            next.AccountId != previous.AccountId || next.OrganisationId != previous.OrganisationId ||
            next.AuthenticationRevision == previous.AuthenticationRevision)
            throw new UnauthorizedAccessException("A genuine fresh same-account/profile activation is required.");
    }
    private void DemandRenewalConfiguration()
    { if (!HasOwnerReauthenticationSources) throw new InvalidOperationException("Verified identity, actual context reauthorization and canonical original custody/CAS producers are required; renewal is unavailable."); }
    private void DemandRenewalOpen()
    { DemandOriginalAdmissionOpen(); if (_renewalClosing) throw new ObjectDisposedException("Task owner reauthentication"); }
    private Renewal RequireRenewal(ITaskRunOwnerReauthenticationAdmission original)
    {
        if (original is not Renewal renewal || !ReferenceEquals(renewal.Issuer, this) || !_renewals.Contains(renewal))
            throw new UnauthorizedAccessException("The SAME privately issued owner renewal is required.");
        return renewal;
    }
    private void DemandRetainedRenewalActivation(Renewal renewal)
    {
        if (!renewal.PreviousActivation.IsSealed || !ReferenceEquals(renewal.StableOwner.Activation, renewal.PreviousActivation) ||
            !ReferenceEquals(_owners.GetValueOrDefault(renewal.PreviousOwner.TaskId), renewal.StableOwner))
            throw new UnauthorizedAccessException("The original sealed owner activation is no longer retained.");
    }

    private sealed class Renewal(TaskRunPermissionAuthority issuer, Owner stableOwner, OwnerActivation previous,
        TaskExecutionOwnerBinding next, long expectedRevision, ITaskRunVerifiedReauthenticationLease identity,
        ITaskRunContextReauthenticationLease context, ITaskRunReauthenticationQuiescence quiescence) : ITaskRunOwnerReauthenticationAdmission
    {
        public TaskRunPermissionAuthority Issuer { get; } = issuer;
        public Owner StableOwner { get; } = stableOwner;
        public OwnerActivation PreviousActivation { get; } = previous;
        public TaskExecutionOwnerBinding PreviousOwner => PreviousActivation.Binding;
        public TaskExecutionOwnerBinding NextOwner { get; } = next;
        public long ExpectedPersistenceRevision { get; } = expectedRevision;
        public ITaskRunVerifiedReauthenticationLease Identity { get; } = identity;
        public ITaskRunContextReauthenticationLease Context { get; } = context;
        public ITaskRunReauthenticationQuiescence Quiescence { get; } = quiescence;
        public readonly SemaphoreSlim Commit = new(1, 1);
        public readonly List<RenewalWork> Work = [];
        public readonly List<RenewalPin> Pins = [];
        public readonly List<Exception> Errors = [];
        public readonly List<Task> CleanupOriginals = [];
        public bool Closing, Activated;
        public Task? Close, Activation, OriginalCloseCommitWait;
        public ITaskRunReauthenticationAcknowledgment? Acknowledgment;
        public ValueTask RevalidateOriginalAsync(TaskExecutionSnapshot current, CancellationToken token)
        {
            Issuer.DemandExternalRenewalJoin();
            return new(Issuer.StartRenewalWork(this, token, async work =>
            { await Issuer.ValidateRenewalResourcesAsync(work, this, current, token).ConfigureAwait(false); return (object?)null; }));
        }
        public ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken token)
        {
            Issuer.DemandExternalRenewalJoin();
            return new(Issuer.StartRenewalWork(this, token, work => Issuer.AcquireRenewalPinBodyAsync(work, this, token)));
        }
        public ValueTask DisposeAsync() { Issuer.DemandExternalRenewalJoin(); return new(Issuer.StartRenewalScopeClose(this)); }
    }

    private async Task<IAsyncDisposable> AcquireRenewalPinBodyAsync(RenewalWork work, Renewal renewal, CancellationToken token)
    {
        await work.AwaitAsync(renewal.Commit.WaitAsync(token)).ConfigureAwait(false);
        IAsyncDisposable? raw = null; var transferred = false;
        try
        {
            lock (_sync) { if (renewal.Closing) throw new UnauthorizedAccessException("Original renewal scope retired."); DemandRetainedRenewalActivation(renewal); }
            // Pure lifetime pin only. ALL actor/context/policy/repository reads precede this call.
            raw = await work.AwaitAsync(InvokeRenewalPhysical(() => renewal.Quiescence.AcquireOriginalCommitPinAsync(token).AsTask())).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("No original canonical lifetime pin was issued.");
            lock (_sync)
            {
                if (renewal.Closing) throw new UnauthorizedAccessException("Original renewal scope retired while acquiring its pin.");
                DemandRetainedRenewalActivation(renewal);
                var pin = new RenewalPin(this, renewal, raw); renewal.Pins.Add(pin); transferred = true; return pin;
            }
        }
        finally
        {
            if (!transferred)
            {
                if (raw is not null) await work.CloseResourceAsync(raw, InvokeRenewalPhysical).ConfigureAwait(false);
                renewal.Commit.Release();
            }
        }
    }
    private sealed class RenewalPin(TaskRunPermissionAuthority issuer, Renewal renewal, IAsyncDisposable original) : IAsyncDisposable
    {
        public TaskRunPermissionAuthority Issuer { get; } = issuer;
        public Renewal Renewal { get; } = renewal;
        public IAsyncDisposable Original { get; } = original;
        public Task? Close, OriginalClose;
        public ValueTask DisposeAsync()
        {
            Issuer.DemandExternalRenewalJoin(); TaskCompletionSource? start = null; Task actual;
            lock (Issuer._sync)
            {
                if (Close is null) { start = NewRenewalGate(); Close = Issuer.CloseRenewalPinAsync(this, start.Task); }
                actual = Close;
            }
            start?.SetResult(); return new(actual);
        }
    }
    private async Task CloseRenewalPinAsync(RenewalPin pin, Task start)
    {
        await start.ConfigureAwait(false); var old = _renewalExecuting.Value; var phase = new RenewalPhase(this, old); _renewalExecuting.Value = phase;
        var failures = new List<Exception>(); Task? rawClose = null;
        try
        {
            try
            {
                rawClose = InvokeRenewalPhysical(() => pin.Original.DisposeAsync().AsTask());
                lock (_sync) pin.OriginalClose = rawClose; // SAME raw original retained before await.
            }
            catch (Exception error) { AddRenewalCause(failures, error); }
            if (rawClose is not null) await JoinRenewalTaskAsync(rawClose, failures).ConfigureAwait(false);
        }
        finally
        {
            try { pin.Renewal.Commit.Release(); } catch (Exception error) { AddRenewalCause(failures, error); }
            lock (_sync) foreach (var cause in failures) AddRenewalCause(pin.Renewal.Errors, cause);
            phase.Retire(); _renewalExecuting.Value = old;
        }
        ThrowRenewalCauses(failures);
    }

    private Task StartRenewalScopeClose(Renewal renewal)
    {
        TaskCompletionSource? start = null; Task actual;
        lock (_sync)
        {
            if (renewal.Close is not null) return renewal.Close; // Retired originals keep their SAME close identity.
            _ = RequireRenewal(renewal);
            if (renewal.Close is null) { renewal.Closing = true; start = NewRenewalGate(); renewal.Close = CloseRenewalScopeAsync(renewal, start.Task); }
            actual = renewal.Close;
        }
        start?.SetResult(); return actual;
    }
    private async Task CloseRenewalScopeAsync(Renewal renewal, Task start)
    {
        await start.ConfigureAwait(false); var old = _renewalExecuting.Value; var phase = new RenewalPhase(this, old); _renewalExecuting.Value = phase;
        var failures = new List<Exception>(); RenewalWork[] work;
        try
        {
            lock (_sync) work = renewal.Work.ToArray();
            foreach (var operation in work) await JoinRenewalWorkAsync(operation, failures).ConfigureAwait(false);
            var originalCommitWait = renewal.Commit.WaitAsync();
            lock (_sync) renewal.OriginalCloseCommitWait = originalCommitWait;
            await originalCommitWait.ConfigureAwait(false); // An actually held pin is not completed-task proof.
            try
            {
                RenewalPin[] pins; lock (_sync) pins = renewal.Pins.ToArray();
                foreach (var pin in pins)
                    if (pin.Close is { } actualClose) await JoinRenewalTaskAsync(actualClose, failures).ConfigureAwait(false);
                foreach (var resource in new IAsyncDisposable[] { renewal.Context, renewal.Identity, renewal.Quiescence })
                {
                    Task? close = null;
                    try { close = InvokeRenewalPhysical(() => resource.DisposeAsync().AsTask()); } catch (Exception error) { AddRenewalCause(failures, error); }
                    if (close is not null)
                    {
                        lock (_sync) renewal.CleanupOriginals.Add(close); // Retain exact child identity/status before await.
                        await JoinRenewalTaskAsync(close, failures).ConfigureAwait(false);
                    }
                }
            }
            finally { renewal.Commit.Release(); }
            foreach (var cause in renewal.Errors) AddRenewalCause(failures, cause);
            lock (_sync) foreach (var cause in failures) AddRenewalCause(renewal.Errors, cause);
        }
        finally { phase.Retire(); _renewalExecuting.Value = old; }
        ThrowRenewalCauses(failures); // Known CAS remains observable; cleanup failure never activates/reopens.
    }

    public Task CloseAndDrainOwnerReauthenticationAsync()
    {
        DemandExternalRenewalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_sync)
        {
            if (_renewalClose is null) { _renewalClosing = true; start = NewRenewalGate(); _renewalClose = CloseRenewalOriginalsAsync(start.Task); }
            actual = _renewalClose;
        }
        start?.SetResult(); return actual;
    }
    private async Task CloseRenewalOriginalsAsync(Task start)
    {
        await start.ConfigureAwait(false); var old = _renewalExecuting.Value; var phase = new RenewalPhase(this, old); _renewalExecuting.Value = phase;
        var failures = new List<Exception>(); RenewalWork[] work; Renewal[] scopes;
        try
        {
            lock (_sync) { work = _renewalWork.ToArray(); scopes = _renewals.ToArray(); }
            foreach (var operation in work) await JoinRenewalWorkAsync(operation, failures).ConfigureAwait(false);
            foreach (var renewal in scopes)
            {
                await JoinRenewalTaskAsync(StartRenewalScopeClose(renewal), failures).ConfigureAwait(false);
                Task[] cleanup; RenewalPin[] pins;
                lock (_sync) { cleanup = renewal.CleanupOriginals.ToArray(); pins = renewal.Pins.ToArray(); }
                if (renewal.OriginalCloseCommitWait is { } originalWait)
                    await JoinRenewalTaskAsync(originalWait, failures).ConfigureAwait(false);
                foreach (var original in cleanup) await JoinRenewalTaskAsync(original, failures).ConfigureAwait(false);
                foreach (var pin in pins)
                    if (pin.OriginalClose is { } originalClose) await JoinRenewalTaskAsync(originalClose, failures).ConfigureAwait(false);
            }
            if (_renewalCapacityFailure is { } failure) AddRenewalCause(failures, failure);
        }
        finally { phase.Retire(); _renewalExecuting.Value = old; }
        ThrowRenewalCauses(failures);
    }

    private Task<T> StartRenewalWork<T>(Renewal? renewal, CancellationToken token, Func<RenewalWork, Task<T>> body)
    {
        token.ThrowIfCancellationRequested(); TaskCompletionSource start; RenewalWork work; Task<T> actual;
        lock (_sync)
        {
            work = ReserveRenewalWork(renewal, addToScopeWork: true); start = NewRenewalGate();
            actual = RunRenewalWorkAsync(work, start.Task, () => body(work)); work.Driver = actual;
        }
        start.SetResult(); return actual;
    }
    private RenewalWork ReserveRenewalWork(Renewal? renewal, bool addToScopeWork)
    {
        DemandRenewalOpen();
        if (renewal is not null && (RequireRenewal(renewal).Closing && addToScopeWork))
            throw new UnauthorizedAccessException("Original renewal scope retired.");
        foreach (var retired in _renewals.Where(item => item.Activated && item.Errors.Count == 0 &&
            item.Close?.IsCompletedSuccessfully == true && item.Activation?.IsCompletedSuccessfully == true).ToArray())
        {
            _renewals.Remove(retired); _renewalReservations.Remove(retired.StableOwner);
        }
        _renewalWork.RemoveAll(operation => operation.Driver.IsCompletedSuccessfully && operation.Renewal is
            { Activated: true, Close.IsCompletedSuccessfully: true, Activation.IsCompletedSuccessfully: true });
        if (_renewalCapacityFailure is not null || _renewalWork.Count >= 128)
            throw _renewalCapacityFailure ??= new InvalidOperationException("Original owner-renewal custody is full.");
        var work = new RenewalWork { Renewal = renewal }; _renewalWork.Add(work);
        if (addToScopeWork) renewal?.Work.Add(work); return work;
    }
    private async Task<T> RunRenewalWorkAsync<T>(RenewalWork work, Task start, Func<Task<T>> body)
    {
        await start.ConfigureAwait(false); var old = _renewalExecuting.Value; var phase = new RenewalPhase(this, old); _renewalExecuting.Value = phase;
        T result = default!; Task<T>? original = null;
        try { original = InvokeRenewalPhysical(body); result = await work.AwaitAsync(original).ConfigureAwait(false); }
        catch (Exception error) { AddRenewalCause(work.Errors, error); }
        finally
        {
            if (work.Renewal is { } renewal) lock (_sync) foreach (var cause in work.Errors) AddRenewalCause(renewal.Errors, cause);
            phase.Retire(); _renewalExecuting.Value = old;
        }
        if (original?.IsCanceled == true && !work.CleanupIncomplete && !work.Raw.Any(task => task.IsFaulted) &&
            work.Errors.Count != 0 && work.Errors.All(error => error is OperationCanceledException))
            ExceptionDispatchInfo.Capture(work.Errors[0]).Throw(); // Genuine returned cancellation, originals remain in the ledger.
        ThrowRenewalCauses(work.Errors); return result;
    }
    private sealed class RenewalWork
    {
        public Task Driver = null!;
        public Renewal? Renewal;
        public readonly List<Task> Raw = [];
        public readonly List<Exception> Errors = [];
        public bool CleanupIncomplete;
        public async Task<T> AwaitAsync<T>(Task<T> actual)
        {
            ArgumentNullException.ThrowIfNull(actual); Raw.Add(actual);
            try { return await actual.ConfigureAwait(false); }
            catch (Exception error) { Capture(actual, error); if (actual.IsFaulted) ExceptionDispatchInfo.Capture(actual.Exception!).Throw(); throw; }
        }
        public async Task AwaitAsync(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual); Raw.Add(actual);
            try { await actual.ConfigureAwait(false); }
            catch (Exception error) { Capture(actual, error); if (actual.IsFaulted) ExceptionDispatchInfo.Capture(actual.Exception!).Throw(); throw; }
        }
        private void Capture(Task actual, Exception observed)
        {
            AddRenewalCause(Errors, observed);
            if (actual.Exception is { } group) { AddRenewalCause(Errors, group); foreach (var cause in group.InnerExceptions) AddRenewalCause(Errors, cause); }
        }
        public async Task CloseResourceAsync(IAsyncDisposable actual, Func<Func<Task>, Task> invoke)
        {
            Task? close = null;
            try { close = invoke(() => actual.DisposeAsync().AsTask()); } catch (Exception error) { CleanupIncomplete = true; AddRenewalCause(Errors, error); }
            if (close is not null)
            {
                Raw.Add(close); await JoinRenewalTaskAsync(close, Errors).ConfigureAwait(false);
                if (!close.IsCompletedSuccessfully) CleanupIncomplete = true;
            }
        }
    }
    private sealed class RenewalPhase(TaskRunPermissionAuthority owner, RenewalPhase? parent)
    {
        public TaskRunPermissionAuthority Owner { get; } = owner;
        public RenewalPhase? Parent { get; } = parent;
        private int _live = 1;
        public bool Live => Volatile.Read(ref _live) != 0;
        public void Retire() => Interlocked.Exchange(ref _live, 0);
    }
    private void DemandExternalRenewalJoin()
    {
        for (var phase = _renewalExecuting.Value; phase is not null; phase = phase.Parent)
            if (phase.Live && ReferenceEquals(phase.Owner, this)) throw new InvalidOperationException("An owner-renewal original cannot join its containing scope/source.");
        if (_renewalPhysical?.Any(actual => ReferenceEquals(actual, this)) == true)
            throw new InvalidOperationException("An owner-renewal callback cannot join its containing scope/source.");
    }
    private T InvokeRenewalPhysical<T>(Func<T> callback)
    {
        var calls = _renewalPhysical ??= []; calls.Add(this);
        try { return callback(); }
        catch (OperationCanceledException error) { throw new AggregateException("Synchronous owner-renewal callback fault.", error); }
        finally { calls.RemoveAt(calls.Count - 1); }
    }
    private static TaskCompletionSource NewRenewalGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void AddRenewalCause(List<Exception> errors, Exception actual)
    { if (!errors.Any(cause => ReferenceEquals(cause, actual))) errors.Add(actual); }
    private static async Task JoinRenewalTaskAsync(Task actual, List<Exception> errors)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error)
        { AddRenewalCause(errors, error); if (actual.Exception is { } group) { AddRenewalCause(errors, group); foreach (var cause in group.InnerExceptions) AddRenewalCause(errors, cause); } }
    }
    private static async Task JoinRenewalWorkAsync(RenewalWork work, List<Exception> errors)
    {
        await JoinRenewalTaskAsync(work.Driver, errors).ConfigureAwait(false);
        foreach (var raw in work.Raw) await JoinRenewalTaskAsync(raw, errors).ConfigureAwait(false);
        foreach (var cause in work.Errors) AddRenewalCause(errors, cause);
    }
    private static void ThrowRenewalCauses(List<Exception> errors, bool actualCancellation = false)
    {
        if (errors.Count == 1)
        {
            if (errors[0] is OperationCanceledException && !actualCancellation)
                throw new AggregateException("Original owner-renewal faulted cancellation cause.", errors[0]);
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }
        if (errors.Count > 1) throw new AggregateException("Original owner-renewal/custody/cleanup faults.", errors);
    }
}
