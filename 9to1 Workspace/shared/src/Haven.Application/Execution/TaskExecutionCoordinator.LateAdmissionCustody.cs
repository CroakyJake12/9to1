using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    // Weak SAME-admission indexing locates its owning stage. Successful capacity retirement does
    // not create a second strong archive; the real completed invocation/receipt keeps its originals.
    private readonly ConditionalWeakTable<TaskRunAttemptAdmission, OriginalAttemptProcessResource> _originalAttemptProcessResources = new();

    private void RetainOriginalBeginProcessResult(TaskRunProcessStageCustody stage,
        TaskExecutionSnapshot acknowledged, TaskRunInvocationCustody? custody)
    {
        stage.AcknowledgedBegin = acknowledged;
        // The already registered actual Chat producer retains and owns this same Begin driver.
        stage.BeginOwnershipTransferred = custody?.OriginalProcessProducer is not null
            && ReferenceEquals(custody.OriginalBinding, acknowledged);
        stage.OriginalResultClosed = () => stage.BeginOwnershipTransferred;
        stage.OriginalResultJoin = async () =>
        {
            if (stage.BeginOwnershipTransferred) return;
            await SuspendOriginalLateAdmissionAsync(stage, acknowledged, null).ConfigureAwait(false);
            stage.BeginOwnershipTransferred = true; // Actual suspended/completed CAS observation, no replay witness.
        };
    }

    private void TransferOriginalBeginToIssuedStage(TaskRunAttemptAdmission admission)
    {
        lock (_processProducerGate)
            foreach (var stage in _originalProcessStages)
                if (stage.AcknowledgedBegin is { } begin && begin.TaskId == admission.Snapshot.TaskId
                    && begin.ContextId == admission.Snapshot.ContextId && begin.ExecutionId == admission.Snapshot.ExecutionId
                    && begin.OwnerBinding == admission.Snapshot.OwnerBinding)
                    stage.BeginOwnershipTransferred = true; // The actual newly issued stage now owns the lease/registration.
    }

    private void ObserveOriginalRetiredProcessResource(TaskRunAttemptAdmission admission, Task actualRetirement)
    {
        if (_originalAttemptProcessResources.TryGetValue(admission, out var original))
            original.ObserveActualRetiredRegistration(actualRetirement);
    }

    private async Task SuspendOriginalLateAdmissionAsync(TaskRunProcessStageCustody stage,
        TaskExecutionSnapshot acknowledged, Guid? actualAttemptId)
    {
        var current = await stage.Await(() => repository.GetAsync(acknowledged.TaskId, CancellationToken.None),
            owningCleanup: true).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The acknowledged original task disappeared during process retirement.");
        if (current.TaskId != acknowledged.TaskId || current.ContextId != acknowledged.ContextId
            || current.ExecutionId != acknowledged.ExecutionId || current.OwnerBinding != acknowledged.OwnerBinding
            || actualAttemptId is { } id && current.Attempts.LastOrDefault()?.Id != id)
            throw new InvalidOperationException("Another task/run/owner/attempt cannot be overwritten by original admission cleanup.");
        // Genuine completed/cancelled ACKs win over a later shutdown request. No row text becomes
        // settlement proof: the separate resource still must join its actual registered originals.
        if (current.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled
            || current.State == TaskExecutionLifecycle.Suspended && (actualAttemptId is null
                || current.Attempts.LastOrDefault()?.State == TaskRunAttemptState.Suspended)) return;
        var now = _time.GetUtcNow();
        await stage.Await(() => PersistAsync(current with
        {
            State = TaskExecutionLifecycle.Suspended,
            Attempts = current.Attempts.Select(attempt => attempt.Id == actualAttemptId
                && attempt.State is TaskRunAttemptState.Admitted or TaskRunAttemptState.Running
                ? attempt with { State = TaskRunAttemptState.Suspended, UpdatedAt = now } : attempt).ToArray(),
            UpdatedAt = now
        }, CancellationToken.None, owningProcessCleanup: true), owningCleanup: true).ConfigureAwait(false);
    }

    private sealed class OriginalAttemptProcessResource(TaskExecutionCoordinator owner,
        TaskRunProcessStageCustody stage, ITaskRunAdmissionLease actualLease)
    {
        private readonly object _gate = new();
        private readonly ITaskRunOriginalAttemptRegistrationSource? _source = owner._runtimeSettlement as ITaskRunOriginalAttemptRegistrationSource;
        private TaskRunAttemptAdmission? _issued;
        private TaskRunOriginalAttemptRegistrationDisposition? _disposition;
        private Task? _actualJoin;
        private Task? _actualLocalLeaseClose;
        private Task? _actualRegistration;
        private Task? _actualSettlement;
        private Task? _actualLeaseClose;
        private TaskRunOriginalRetirementAcknowledgment? _actualRetirement;
        private Task? _actualRetirementDriver;
        private readonly List<Exception> _observationCauses = [];
        internal TaskRunAttemptAdmission? IssuedOriginal { get { lock (_gate) return _issued; } }

        internal void BindIssuedOriginal(TaskRunAttemptAdmission admission)
        {
            if (!owner._issuedAdmissions.TryGetValue(admission.AttemptId, out var genuine)
                || !ReferenceEquals(genuine, admission) || !ReferenceEquals(admission.Lease, actualLease))
                throw new InvalidOperationException("Only this actual privately issued admission can transfer lease cleanup ownership.");
            lock (_gate)
            {
                if (_issued is not null) throw new InvalidOperationException("The actual lease is already bound to an original admission.");
                _issued = admission;
            }
            owner._originalAttemptProcessResources.Add(admission, this);
            owner.TransferOriginalBeginToIssuedStage(admission);
        }

        internal async Task RegisterOriginalOwnershipAsync(CancellationToken token)
        {
            var issued = IssuedOriginal ?? throw new InvalidOperationException("The actual admission has not been privately issued.");
            // Legacy unconfigured callers retain their public behavior. They cannot produce a
            // process-clean receipt; production composition requires this SAME runtime source.
            if (_source is null) return;
            var receipt = stage.Invoke(() => _source.RegisterOriginalAttemptDisposition(issued, token), owningCleanup: true);
            CaptureReceipt(receipt, issued);
            if (receipt.Kind == TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership)
            {
                await CloseActualLocalLeaseAsync().ConfigureAwait(false);
                var cause = receipt.OriginalRefusal ?? throw new InvalidOperationException("The original refusal has no cause.");
                ExceptionDispatchInfo.Capture(cause).Throw();
            }
            var registration = receipt.OriginalRegistration
                ?? throw new InvalidOperationException("The actual registered owner returned no original registration Task.");
            stage.RetainSource(registration);
            await stage.Await(() => registration, owningCleanup: true).ConfigureAwait(false);
        }

        private void CaptureReceipt(TaskRunOriginalAttemptRegistrationDisposition receipt, TaskRunAttemptAdmission issued)
        {
            if (_source is null || !_source.IsIssuedOriginalAttemptRegistrationDisposition(receipt, issued)
                || !ReferenceEquals(receipt.OriginalAdmission, issued) || !ReferenceEquals(issued.Lease, actualLease))
                throw new InvalidOperationException("The registration disposition is not issued for this SAME genuine admission.");
            lock (_gate)
            {
                _disposition = receipt;
                _actualRegistration = receipt.OriginalRegistration;
                _actualSettlement = receipt.OriginalSettlement;
                _actualLeaseClose = receipt.OriginalLeaseClose;
                _actualRetirement = receipt.OriginalRetirementAcknowledgment;
            }
            foreach (var actual in new[] { receipt.OriginalRegistration, receipt.OriginalSettlement, receipt.OriginalLeaseClose })
                if (actual is not null) stage.RetainSource(actual);
        }

        internal void ObserveActualRetiredRegistration(Task actualRetirement)
        {
            stage.RetainSource(actualRetirement);
            lock (_gate) _actualRetirementDriver = actualRetirement;
            var issued = IssuedOriginal;
            if (_source is null || issued is null) return; // No absence/Completed inference.
            var receipt = TaskRunProcessProducerContext.Invoke(owner,
                () => _source.RegisterOriginalAttemptDisposition(issued, CancellationToken.None));
            CaptureReceipt(receipt, issued);
            if (receipt.Kind != TaskRunOriginalAttemptRegistrationKind.Registered
                || receipt.OriginalRetirementAcknowledgment is null
                || !HasSuccessfulOriginalTerminal())
                throw new InvalidOperationException("The actual retired owner has no successful whole-original terminal witness.");
        }

        internal bool HasSuccessfulOriginalTerminal()
        {
            lock (_gate)
            {
                if (_observationCauses.Count != 0) return false;
                if (_issued is null) return _actualLocalLeaseClose is { IsCompletedSuccessfully: true };
                if (_disposition?.Kind == TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership)
                    return _actualLocalLeaseClose is { IsCompletedSuccessfully: true };
                return _disposition?.Kind == TaskRunOriginalAttemptRegistrationKind.Registered
                    && _actualRegistration is { IsCompletedSuccessfully: true }
                    && _actualSettlement is { IsCompletedSuccessfully: true }
                    && _actualLeaseClose is { IsCompletedSuccessfully: true }
                    && (_actualRetirement is null || _actualRetirement.IsOriginalAcknowledgment
                        && _actualRetirementDriver is { IsCompletedSuccessfully: true }
                        && ReferenceEquals(_actualRetirement.OriginalAdmission, _issued));
            }
        }

        internal void RetainObservationFailure(Exception cause)
        {
            lock (_gate) _observationCauses.Add(cause);
            stage.RetainOriginalFailure(cause);
        }

        internal Task CloseAfterOriginalAdmissionFailureAsync() => JoinOriginalTerminalAsync();
        internal Task JoinOriginalTerminalAsync()
        {
            TaskCompletionSource? start = null;
            Task actual;
            lock (_gate)
            {
                if (_actualJoin is null)
                {
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _actualJoin = JoinOriginalTerminalBodyAsync(start.Task);
                }
                actual = _actualJoin;
            }
            stage.RetainSource(actual);
            start?.SetResult();
            return actual;
        }

        private async Task JoinOriginalTerminalBodyAsync(Task start)
        {
            await start.ConfigureAwait(false);
            var issued = IssuedOriginal;
            if (issued is null)
            {
                // No admission object was ever exposed to a frame owner. This is the actual
                // sole acquisition path, not a canceled-token or absent-registry inference.
                await CloseActualLocalLeaseAsync().ConfigureAwait(false);
                return;
            }
            if (_source is null)
                throw new InvalidOperationException("The configured runtime exposes no original registration disposition; lease ownership is unknown.");
            // A fresh receipt preserves actual healthy retired originals even after registry pruning.
            var receipt = stage.Invoke(() => _source.RegisterOriginalAttemptDisposition(issued, CancellationToken.None), owningCleanup: true);
            CaptureReceipt(receipt, issued);
            if (receipt.Kind == TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership)
            {
                await CloseActualLocalLeaseAsync().ConfigureAwait(false);
                return;
            }
            // Registered lookup/registration failure remains owned/unknown. Never independently
            // dispose the issuer lease after this owner has admitted its exact object.
            var errors = new List<Exception>();
            if (owner.HasSealedOriginalProcessProducerAdmission)
                try { await owner.SuspendOriginalLateAdmissionAsync(stage, issued.Snapshot, issued.AttemptId).ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(cause); }
            var registration = receipt.OriginalRegistration!;
            try { await registration.ConfigureAwait(false); }
            catch (Exception cause) { Retain(errors, cause, registration); }
            Task? settlement = receipt.OriginalSettlement;
            try
            {
                settlement ??= stage.Invoke(() => owner._runtimeSettlement!.AwaitSettlementAsync(
                    issued.Snapshot.TaskId, issued.Snapshot.ExecutionId, issued.AttemptId, CancellationToken.None), owningCleanup: true);
                stage.RetainSource(settlement);
                await settlement.ConfigureAwait(false);
            }
            catch (Exception cause) { Retain(errors, cause, settlement); }
            // Read terminal fields from the same privately retained actual record, not durable state.
            try
            {
                var terminal = stage.Invoke(() => _source.RegisterOriginalAttemptDisposition(issued, CancellationToken.None), owningCleanup: true);
                CaptureReceipt(terminal, issued);
                if (terminal.OriginalLeaseClose is { } leaseClose)
                    try { await leaseClose.ConfigureAwait(false); }
                    catch (Exception cause) { Retain(errors, cause, leaseClose); }
                if (!HasSuccessfulOriginalTerminal())
                    errors.Add(new InvalidOperationException("Actual registered originals lack a successful whole settlement and lease close."));
            }
            catch (Exception cause) { errors.Add(cause); }
            lock (_gate) errors.AddRange(_observationCauses);
            if (errors.Count != 0) throw new AggregateException("Actual attempt cleanup ownership did not settle successfully.", errors);
        }

        private Task CloseActualLocalLeaseAsync()
        {
            TaskCompletionSource? start = null;
            Task actual;
            lock (_gate)
            {
                if (_actualLocalLeaseClose is null)
                {
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _actualLocalLeaseClose = CloseActualLocalLeaseBodyAsync(start.Task);
                }
                actual = _actualLocalLeaseClose;
            }
            stage.RetainSource(actual);
            start?.SetResult();
            return actual;
        }

        private async Task CloseActualLocalLeaseBodyAsync(Task start)
        {
            await start.ConfigureAwait(false);
            await stage.Await(() => actualLease.DisposeAsync().AsTask(), owningCleanup: true).ConfigureAwait(false);
        }

        private static void Retain(List<Exception> errors, Exception cause, Task? actual)
        {
            IEnumerable<Exception> originals = actual?.Exception is { } group ? group.InnerExceptions : new[] { cause };
            foreach (var original in originals)
                if (!errors.Any(prior => ReferenceEquals(prior, original))) errors.Add(original);
        }
    }
}
