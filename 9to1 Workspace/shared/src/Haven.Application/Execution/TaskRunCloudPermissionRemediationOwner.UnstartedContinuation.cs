using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskRunCloudPermissionRemediationOwner
{
    private readonly List<ContinuationOperation> _continuationOperations = new();
    private readonly List<UnstartedPermissionLease> _continuationLeases = new();
    private readonly SemaphoreSlim _continuationCommitGate = new(1, 1);
    private Exception? _continuationCapacityRefusal;

    private sealed class ContinuationOperation
    {
        public Task Published = null!;
        public Task? ActualBody;
        public Task Runner = null!;
        public Binding Binding = null!;
    }

    // Deny-only observations of the SAME privately admitted response, never current permission.
    public bool HasOriginalAllowedUnstartedResponse(TaskRunCloudPermissionRequiredException sameAsk,
        Task<RemediationRequest> samePublication)
    {
        ArgumentNullException.ThrowIfNull(sameAsk); ArgumentNullException.ThrowIfNull(samePublication);
        lock (_sync)
            return !_closing && sameAsk.OriginalRequest is { } original &&
                _requests.TryGetValue(original, out var record) &&
                ReferenceEquals(record.OriginalAsk, sameAsk) && ReferenceEquals(record.Publication, samePublication) &&
                HasHealthyOriginalUnstartedResponse(record);
    }

    private static bool HasHealthyOriginalUnstartedResponse(Binding record) =>
        record.Captured && record.ExpectedAttempt is null && record.Publication.IsCompletedSuccessfully &&
        record.Approved == true && record.ApprovalValidated && record.Approval is { IsCompletedSuccessfully: true } &&
        record.Callback is { IsCompletedSuccessfully: true } &&
        record.Decision is { IsCompletedSuccessfully: true } decision && decision.Result.Kind == PermissionDecisionKind.Allowed &&
        record.OriginalSourceDecision is { IsCompletedSuccessfully: true } source && source.Result.Kind == PermissionDecisionKind.Allowed &&
        record.RequestEvent is { IsCompletedSuccessfully: true } && record.DecisionEvent is { IsCompletedSuccessfully: true };

    public ValueTask<ITaskRunUnstartedContinuationPermissionLease> AcquireOriginalUnstartedContinuationAsync(
        TaskRunCloudPermissionRequiredException sameAsk, Task<RemediationRequest> samePublication,
        TaskExecutionSnapshot currentSuspended, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sameAsk); ArgumentNullException.ThrowIfNull(samePublication);
        ArgumentNullException.ThrowIfNull(currentSuspended); cancellationToken.ThrowIfCancellationRequested();
        Binding record; Task<ITaskRunUnstartedContinuationPermissionLease> original;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (sameAsk.OriginalRequest is not { } request || !_requests.TryGetValue(request, out record!) ||
                !ReferenceEquals(record.OriginalAsk, sameAsk) || !ReferenceEquals(record.Publication, samePublication) ||
                !HasHealthyOriginalUnstartedResponse(record))
                throw new UnauthorizedAccessException("The SAME successful privately admitted original Allow response is required.");
            ValidateUnstartedSnapshot(record, currentSuspended);
            if (record.Continuation is not null)
            {
                if (record.ContinuationRevision != currentSuspended.PersistenceRevision)
                    throw new UnauthorizedAccessException("A different continuation observation cannot replace the original acquisition.");
                return new(record.Continuation);
            }
            record.ContinuationRevision = currentSuspended.PersistenceRevision;
            original = StartContinuationOperationLocked(record, () => AcquireUnstartedCoreAsync(record, currentSuspended));
            record.Continuation = original;
        }
        return new(original);
    }

    // The source records only permission-owner lifetime. Data's private recovery issuer separately
    // proves no attempt admission/effect was invoked and retains terminal originals before Task CAS.
    private static void ValidateUnstartedSnapshot(Binding record, TaskExecutionSnapshot current)
    {
        var owner = record.Original.OriginalOwner;
        if (current.TaskId != owner.TaskId || current.ContextId != owner.ContextId ||
            current.ExecutionId != owner.ExecutionId || current.OwnerBinding != owner ||
            current.State != TaskExecutionLifecycle.Suspended || current.Attempts.Count != 0)
            throw new UnauthorizedAccessException("The exact original suspended Task/run with no admitted attempt is required.");
    }

    private async Task DemandUnstartedCurrentAsync(Binding record, TaskExecutionSnapshot observation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); _ownerToken.ThrowIfCancellationRequested();
        var current = await DemandCurrentAsync(record, capture: false).ConfigureAwait(false);
        ValidateUnstartedSnapshot(record, current);
        if (current.PersistenceRevision != observation.PersistenceRevision || current.OwnerBinding != observation.OwnerBinding)
            throw new UnauthorizedAccessException("The actual suspended Task changed during continuation preparation.");
        // DemandCurrent's final repository read is awaited after its earlier actor/model check.
        // Refresh genuine current actor/selected-model authority after that held read; no later
        // repository callback intervenes before the pure final central-policy observation.
        var finalAuthority = CallOriginal(() => _authority.ValidateOriginalCloudPermissionAsync(current,
            record.Original.OriginalOwner, record.Original.OriginalCandidate, token));
        await ObserveAsync(finalAuthority).ConfigureAwait(false);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (!_requests.TryGetValue(record.Original, out var issued) || !ReferenceEquals(issued, record) ||
                !HasHealthyOriginalUnstartedResponse(record))
                throw new UnauthorizedAccessException("The original permission response is no longer available.");
        }
        // This is a pure final central-policy observation after all awaited owner reads, not a
        // monetary reservation, egress grant or policy/network exclusion interval.
        _source.DemandOriginalAllowedUnstartedRequest(record.Original);
        token.ThrowIfCancellationRequested(); _ownerToken.ThrowIfCancellationRequested();
    }

    private async Task<ITaskRunUnstartedContinuationPermissionLease> AcquireUnstartedCoreAsync(
        Binding record, TaskExecutionSnapshot observation)
    {
        ITaskRunCloudUsePermissionLease? acquired = null; Task<ITaskRunCloudUsePermissionLease>? actual = null;
        try
        {
            await DemandUnstartedCurrentAsync(record, observation, _ownerToken).ConfigureAwait(false);
            actual = CallOriginal(() => _source.AcquireOriginalAsync(record.Original.OriginalOwner,
                record.Original.OriginalCandidate, _ownerToken).AsTask());
            lock (_sync) record.ActualContinuationPermissionAcquisition = actual;
            acquired = await ObserveAsync(actual).ConfigureAwait(false);
            lock (_sync) record.AcquiredContinuationPermission = acquired;
            await DemandUnstartedCurrentAsync(record, observation, _ownerToken).ConfigureAwait(false);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                var lease = new UnstartedPermissionLease(this, record, acquired);
                record.ContinuationLease = lease; _continuationLeases.Add(lease); acquired = null;
                return lease;
            }
        }
        catch (Exception error)
        {
            var causes = new List<Exception>();
            AddContinuationCauses(causes, error, actual);
            if (acquired is not null)
            {
                Task? close = null;
                try
                {
                    close = CallOriginal(() => acquired.DisposeAsync().AsTask());
                    lock (_sync) record.ActualUnpublishedContinuationClose = close;
                    await ObserveAsync(close).ConfigureAwait(false);
                }
                catch (Exception cleanup) { AddContinuationCauses(causes, cleanup, close); }
            }
            ThrowContinuationCauses(causes); throw;
        }
    }

    // Called with _sync held; gate guarantees actual publication precedes any repository/actor callback.
    private Task<T> StartContinuationOperationLocked<T>(Binding binding, Func<Task<T>> body)
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        _continuationOperations.RemoveAll(value => value.Published.IsCompletedSuccessfully &&
            value.ActualBody is { IsCompletedSuccessfully: true } && value.Runner.IsCompletedSuccessfully);
        if (_continuationCapacityRefusal is not null) ExceptionDispatchInfo.Capture(_continuationCapacityRefusal).Throw();
        if (_continuationOperations.Count >= 128)
            throw _continuationCapacityRefusal = new InvalidOperationException("Original continuation lifetime custody is full.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new ContinuationOperation { Published = completion.Task, Binding = binding };
        _continuationOperations.Add(operation);
        operation.Runner = RunContinuationOperationAsync(start.Task, operation, body, completion);
        start.SetResult(); return completion.Task;
    }
    private async Task RunContinuationOperationAsync<T>(Task start, ContinuationOperation operation,
        Func<Task<T>> body, TaskCompletionSource<T> completion)
    {
        await start.ConfigureAwait(false);
        var phase = EnterOriginal(completion.Task);
        try
        {
            operation.ActualBody = CallOriginal(body) ?? throw new InvalidOperationException("No actual original continuation task was returned.");
            var result = await ObserveAsync((Task<T>)operation.ActualBody).ConfigureAwait(false);
            completion.TrySetResult(result);
        }
        catch (Exception error) { completion.TrySetException((Exception?)operation.ActualBody?.Exception ?? error); }
        finally { ExitOriginal(phase); }
    }

    private sealed class UnstartedPermissionLease : ITaskRunUnstartedContinuationPermissionLease
    {
        private readonly TaskRunCloudPermissionRemediationOwner _owner;
        private readonly Binding _binding;
        private readonly ITaskRunCloudUsePermissionLease _permission;
        private bool _sealed, _pinIssued;
        private Task? _close;
        private readonly List<Task> _actualChildren = new();
        public Task? ActualPermissionClose;
        public bool SuccessfullyClosed => _close is { IsCompletedSuccessfully: true };
        public UnstartedPermissionLease(TaskRunCloudPermissionRemediationOwner owner, Binding binding,
            ITaskRunCloudUsePermissionLease permission) { _owner = owner; _binding = binding; _permission = permission; }

        public ValueTask RevalidateOriginalAsync(TaskExecutionSnapshot current, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(current); token.ThrowIfCancellationRequested(); Task<bool> original;
            lock (_owner._sync)
            {
                DemandOpenLocked();
                original = _owner.StartContinuationOperationLocked(_binding, async () =>
                {
                    var actual = _owner.CallOriginal(() => _permission.RevalidateAsync(token).AsTask());
                    RetainActualChild(actual);
                    await ObserveAsync(actual).ConfigureAwait(false);
                    await _owner.DemandUnstartedCurrentAsync(_binding, current, token).ConfigureAwait(false);
                    lock (_owner._sync) DemandOpenLocked();
                    return true;
                });
            }
            return new(original);
        }
        public ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Task<IAsyncDisposable> original;
            lock (_owner._sync)
            {
                DemandOpenLocked();
                if (_pinIssued) throw new InvalidOperationException("The original continuation creation pin is one-use.");
                _pinIssued = true;
                original = _owner.StartContinuationOperationLocked(_binding, () => AcquirePinAsync(token));
            }
            return new(original);
        }
        private async Task<IAsyncDisposable> AcquirePinAsync(CancellationToken token)
        {
            var actual = _owner._continuationCommitGate.WaitAsync(token);
            RetainActualChild(actual);
            await ObserveAsync(actual).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                lock (_owner._sync) DemandOpenLocked(); // Pure owned lifetime only; no Home/actor/repository read beneath pin.
                return new CommitPin(_owner._continuationCommitGate);
            }
            catch { _owner._continuationCommitGate.Release(); throw; }
        }
        private void RetainActualChild(Task actual)
        {
            lock (_owner._sync)
            {
                _actualChildren.RemoveAll(value => value.IsCompletedSuccessfully);
                _actualChildren.Add(actual);
            }
        }
        private void DemandOpenLocked()
        {
            ObjectDisposedException.ThrowIf(_owner._closing || _sealed, this);
            if (!_owner._requests.TryGetValue(_binding.Original, out var same) || !ReferenceEquals(same, _binding))
                throw new UnauthorizedAccessException("This is not a live original permission lifetime.");
        }
        public ValueTask DisposeAsync() => new(CloseOriginalAsync());
        public Task CloseOriginalAsync()
        {
            _owner.RefuseOriginalSelfJoin();
            lock (_owner._sync)
            {
                if (_close is not null) return _close;
                _sealed = true;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var admitted = _owner._continuationOperations
                    .Where(value => ReferenceEquals(value.Binding, _binding)).ToArray();
                _close = CloseScopeAsync(start.Task, admitted); start.SetResult(); return _close;
            }
        }
        private async Task CloseScopeAsync(Task start, ContinuationOperation[] admitted)
        {
            await start.ConfigureAwait(false);
            var phase = _owner.EnterOriginal(_close!);
            var causes = new List<Exception>(); Task? wait = null;
            try
            {
                // Seal precedes this snapshot. Join every admitted actual revalidation/pin operation
                // before disposing its underlying permission scope, even when an original failed.
                foreach (var operation in admitted)
                {
                    try { await ObserveAsync(operation.Published).ConfigureAwait(false); }
                    catch (Exception error) { AddContinuationCauses(causes, error, operation.Published); }
                    try { await ObserveAsync(operation.Runner).ConfigureAwait(false); }
                    catch (Exception error) { AddContinuationCauses(causes, error, operation.Runner); }
                    if (operation.ActualBody is { } body)
                        try { await ObserveAsync(body).ConfigureAwait(false); }
                        catch (Exception error) { AddContinuationCauses(causes, error, body); }
                }
                Task[] children; lock (_owner._sync) children = _actualChildren.ToArray();
                foreach (var actual in children)
                    try { await ObserveAsync(actual).ConfigureAwait(false); }
                    catch (Exception error) { AddContinuationCauses(causes, error, actual); }
                var pinReleased = false;
                try
                {
                    // Wait for the actual finite Task CAS pin to release before source retirement.
                    wait = _owner._continuationCommitGate.WaitAsync();
                    await ObserveAsync(wait).ConfigureAwait(false);
                    _owner._continuationCommitGate.Release(); pinReleased = true;
                }
                catch (Exception error) { AddContinuationCauses(causes, error, wait); }
                if (pinReleased)
                {
                    try
                    {
                        ActualPermissionClose = _owner.CallOriginal(() => _permission.DisposeAsync().AsTask());
                        await ObserveAsync(ActualPermissionClose).ConfigureAwait(false);
                    }
                    catch (Exception error) { AddContinuationCauses(causes, error, ActualPermissionClose); }
                }
                // An unknown pin release cannot be converted into permission retirement proof.
                ThrowContinuationCauses(causes);
            }
            finally { _owner.ExitOriginal(phase); }
        }
    }
    private sealed class CommitPin(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;
        public ValueTask DisposeAsync()
        { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); return ValueTask.CompletedTask; }
    }

    private async Task CloseOriginalContinuationLifetimesAsync(List<Exception> errors)
    {
        ContinuationOperation[] operations; UnstartedPermissionLease[] leases;
        lock (_sync)
        {
            operations = _continuationOperations.ToArray(); leases = _continuationLeases.ToArray();
            if (_continuationCapacityRefusal is not null) Add(errors, _continuationCapacityRefusal);
        }
        // Existing outer originals are already joined. Retain every actual body/scope close independently.
        foreach (var operation in operations)
            if (operation.ActualBody is { } actual)
                try { await ObserveAsync(actual).ConfigureAwait(false); } catch (Exception error) { AddContinuationCauses(errors, error, actual); }
        foreach (var lease in leases)
        {
            Task? actual = null;
            try { actual = lease.CloseOriginalAsync(); await ObserveAsync(actual).ConfigureAwait(false); }
            catch (Exception error) { AddContinuationCauses(errors, error, actual); }
        }
        Binding[] records; lock (_sync) records = _requests.Values.ToArray();
        foreach (var record in records)
        {
            foreach (var actual in new Task?[] { record.ActualContinuationPermissionAcquisition, record.ActualUnpublishedContinuationClose })
                if (actual is not null)
                    try { await ObserveAsync(actual).ConfigureAwait(false); } catch (Exception error) { AddContinuationCauses(errors, error, actual); }
        }
        if (leases.All(value => value.SuccessfullyClosed) && records.All(value =>
                value.AcquiredContinuationPermission is null || value.ContinuationLease is not null ||
                value.ActualUnpublishedContinuationClose is { IsCompletedSuccessfully: true }))
            try { _continuationCommitGate.Dispose(); } catch (Exception error) { Add(errors, error); }
        // Failed/unknown scope retirement keeps its original resource/gate strongly owned.
    }
    private static void AddContinuationCauses(List<Exception> errors, Exception error, Task? actual)
    {
        var payload = actual?.Exception;
        if (payload is null) Add(errors, error); else foreach (var cause in payload.InnerExceptions) Add(errors, cause);
    }
    private static void ThrowContinuationCauses(List<Exception> causes)
    {
        if (causes.Count == 1 && causes[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        if (causes.Count != 0) throw new AggregateException("Original continuation permission and cleanup failed.", causes);
    }
}
