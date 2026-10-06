using System.Reflection;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class TaskRunOriginalFrameOwnerTests
{
    [Fact]
    public async Task Failed_frame_without_actual_failure_CAS_never_issues_settlement_evidence()
    {
        var h = await CreateFailureSettlementHarnessAsync();
        var cause = new IOException("exact original provider failure");
        var raw = Task.FromException<string>(cause);
        var frame = h.Owner.StartOriginalFrameAsync(h.Admission, _ => raw, CancellationToken.None);
        var failures = new List<Exception>();
        try
        {
            Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => frame));
            var observation = Assert.IsType<TaskRunOriginalFailureObservation>(h.Owner.TryObserveProviderFailure(h.Admission, frame));
            Assert.Null(h.Owner.TryGetOriginalFailedAttemptSettlement(observation, h.Admission));
            var actualSettlement = h.Owner.AwaitSettlementAsync(h.Admission.Snapshot.TaskId,
                h.Admission.Snapshot.ExecutionId, h.Admission.AttemptId, CancellationToken.None);
            var terminal = await Record.ExceptionAsync(() => actualSettlement);
            Assert.NotNull(terminal);
            Assert.Contains(Causes(terminal!), original => ReferenceEquals(original, cause));
            Assert.Null(h.Owner.TryGetOriginalFailedAttemptSettlement(observation, h.Admission));
            Assert.True(frame.IsFaulted);
            Assert.True(raw.IsFaulted);
            Assert.Same(cause, Assert.Single(raw.Exception!.InnerExceptions));
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectNewOriginalAsync(h.Owner.CloseAndDrainAsync(), failures, [cause]); }
        ThrowNewControlErrors(failures);
    }

    [Fact]
    public async Task Held_actual_lease_close_denies_then_issues_same_failed_settled_originals_without_effect_grant()
    {
        var leaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = await CreateFailureSettlementHarnessAsync(leaseClose.Task, entered);
        var cause = new IOException("actual failed finite frame");
        var raw = Task.FromException<string>(cause);
        var frame = h.Owner.StartOriginalFrameAsync(h.Admission, _ => raw, CancellationToken.None);
        Task? settlement = null;
        var failures = new List<Exception>();
        try
        {
            Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => frame));
            var observation = Assert.IsType<TaskRunOriginalFailureObservation>(h.Owner.TryObserveProviderFailure(h.Admission, frame));
            var failed = await h.Coordinator.RecordAttemptFailureAsync(h.Admission.Snapshot.TaskId,
                h.Admission.Snapshot.ExecutionId, h.Admission.AttemptId,
                new ExecutionFailure("PROVIDER_CALL_FAILED", "failed", "actual original"), CancellationToken.None, observation);
            Assert.Equal(TaskExecutionLifecycle.Suspended, failed.State);
            Assert.Null(h.Owner.TryGetOriginalFailedAttemptSettlement(observation, h.Admission));
            settlement = h.Owner.AwaitSettlementAsync(h.Admission.Snapshot.TaskId,
                h.Admission.Snapshot.ExecutionId, h.Admission.AttemptId, CancellationToken.None);
            await entered.Task;
            Assert.False(settlement.IsCompleted);
            Assert.Null(h.Owner.TryGetOriginalFailedAttemptSettlement(observation, h.Admission));
            leaseClose.SetResult();
            await settlement;
            var receipt = Assert.IsType<TaskRunOriginalFailedAttemptSettlement>(
                h.Owner.TryGetOriginalFailedAttemptSettlement(observation, h.Admission));
            Assert.True(h.Owner.IsIssuedOriginalFailedAttemptSettlement(receipt, observation, h.Admission));
            Assert.Same(h.Admission, receipt.OriginalAdmission);
            Assert.Same(observation, receipt.OriginalObservation);
            Assert.Same(frame, receipt.OriginalFrame);
            Assert.Same(cause, receipt.OriginalCause);
            Assert.Same(h.Registration, receipt.OriginalRegistration);
            Assert.Same(settlement, receipt.OriginalSettlement);
            Assert.Same(leaseClose.Task, receipt.OriginalLeaseClose);
            Assert.Same(failed, receipt.OriginalFailureAcknowledgment.AcknowledgedSnapshot);
            Assert.Same(observation, receipt.OriginalFailureAcknowledgment.OriginalObservation);
            Assert.Same(h.Admission, receipt.OriginalFailureAcknowledgment.OriginalAdmission);
            Assert.Null(receipt.OriginalRetirementAcknowledgment);
            Assert.Equal(TaskRunOriginalFailureEffectKnowledge.Unknown, receipt.ProviderNativeEffects);
            Assert.True(frame.IsFaulted);
            Assert.Same(cause, Assert.Single(raw.Exception!.InnerExceptions));
            Assert.Equal(1, h.Authority.Lease!.Disposes);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            leaseClose.TrySetResult();
            if (settlement is not null) await CollectNewOriginalAsync(settlement, failures, []);
            await CollectNewOriginalAsync(h.Owner.CloseAndDrainAsync(), failures, [cause]);
        }
        ThrowNewControlErrors(failures);
    }

    [Fact]
    public async Task Copied_cross_owner_admission_and_observation_cannot_validate_failed_settlement_receipt()
    {
        var h = await CreateFailureSettlementHarnessAsync();
        var other = await CreateFailureSettlementHarnessAsync();
        var failures = new List<Exception>();
        var cause = new IOException("retained actual provider fault");
        try
        {
            var frame = h.Owner.StartOriginalFrameAsync(h.Admission, _ => Task.FromException<string>(cause), CancellationToken.None);
            Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => frame));
            var observation = Assert.IsType<TaskRunOriginalFailureObservation>(h.Owner.TryObserveProviderFailure(h.Admission, frame));
            await h.Coordinator.RecordAttemptFailureAsync(h.Admission.Snapshot.TaskId, h.Admission.Snapshot.ExecutionId,
                h.Admission.AttemptId, new ExecutionFailure("PROVIDER_CALL_FAILED", "failed", "actual"), CancellationToken.None, observation);
            await h.Owner.AwaitSettlementAsync(h.Admission.Snapshot.TaskId, h.Admission.Snapshot.ExecutionId,
                h.Admission.AttemptId, CancellationToken.None);
            var receipt = Assert.IsType<TaskRunOriginalFailedAttemptSettlement>(h.Owner.TryGetOriginalFailedAttemptSettlement(observation, h.Admission));
            var copy = (TaskRunOriginalFailedAttemptSettlement)typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(receipt, null)!;
            var observationCopy = (TaskRunOriginalFailureObservation)typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(observation, null)!;
            Assert.False(h.Owner.IsIssuedOriginalFailedAttemptSettlement(copy, observation, h.Admission));
            Assert.False(h.Owner.IsIssuedOriginalFailedAttemptSettlement(receipt, observationCopy, h.Admission));
            Assert.False(h.Owner.IsIssuedOriginalFailedAttemptSettlement(receipt, observation, h.Admission with { }));
            Assert.False(other.Owner.IsIssuedOriginalFailedAttemptSettlement(receipt, observation, h.Admission));
            Assert.Null(h.Owner.TryGetOriginalFailedAttemptSettlement(observationCopy, h.Admission));
            Assert.Null(h.Owner.TryGetOriginalFailedAttemptSettlement(observation, other.Admission));
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            await CollectNewOriginalAsync(h.Owner.CloseAndDrainAsync(), failures, [cause]);
            await CollectNewOriginalAsync(other.Owner.CloseAndDrainAsync(), failures, []);
        }
        ThrowNewControlErrors(failures);
    }

    [Fact]
    public async Task Actual_faulted_lease_close_keeps_failed_settlement_unknown_and_exact_cleanup_fault()
    {
        var cleanup = new OperationCanceledException("faulted lease-close OCE, not actual cancellation");
        var actualLeaseClose = Task.FromException(cleanup);
        var h = await CreateFailureSettlementHarnessAsync(actualLeaseClose);
        var provider = new IOException("actual provider failure");
        var failures = new List<Exception>();
        try
        {
            var frame = h.Owner.StartOriginalFrameAsync(h.Admission, _ => Task.FromException<string>(provider), CancellationToken.None);
            Assert.Same(provider, await Assert.ThrowsAsync<IOException>(() => frame));
            var observation = Assert.IsType<TaskRunOriginalFailureObservation>(h.Owner.TryObserveProviderFailure(h.Admission, frame));
            await h.Coordinator.RecordAttemptFailureAsync(h.Admission.Snapshot.TaskId, h.Admission.Snapshot.ExecutionId,
                h.Admission.AttemptId, new ExecutionFailure("PROVIDER_CALL_FAILED", "failed", "actual"), CancellationToken.None, observation);
            var settlement = h.Owner.AwaitSettlementAsync(h.Admission.Snapshot.TaskId, h.Admission.Snapshot.ExecutionId,
                h.Admission.AttemptId, CancellationToken.None);
            Assert.NotNull(await Record.ExceptionAsync(() => settlement));
            Assert.False(settlement.IsCompletedSuccessfully);
            Assert.True(actualLeaseClose.IsFaulted);
            Assert.Same(cleanup, Assert.Single(actualLeaseClose.Exception!.InnerExceptions));
            Assert.Null(h.Owner.TryGetOriginalFailedAttemptSettlement(observation, h.Admission));
            var close = await Record.ExceptionAsync(() => h.Owner.CloseAndDrainAsync());
            Assert.NotNull(close);
            Assert.Contains(Causes(close!), original => ReferenceEquals(original, cleanup));
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectNewOriginalAsync(h.Owner.CloseAndDrainAsync(), failures, [cleanup]); }
        ThrowNewControlErrors(failures);
    }

    private static async Task<FailureSettlementHarness> CreateFailureSettlementHarnessAsync(
        Task? leaseClose = null, TaskCompletionSource? leaseCloseEntered = null)
    {
        var repository = new RetirementRepository();
        var authority = new FailureSettlementAuthority(leaseClose ?? Task.CompletedTask, leaseCloseEntered);
        TaskExecutionCoordinator? coordinator = null;
        var owner = new TaskRunOriginalFrameOwner((actualTask, actualRun, actualAttempt, token) => coordinator!.GetIssuedAttemptAsync(actualTask, actualRun, actualAttempt, token));
        coordinator = new(repository, new RetirementSink(), admissionAuthority: authority, runtimeSettlement: owner);
        var task = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "actual failed settlement",
            TaskExecutionDurability.PersistedPlan, [], CancellationToken.None);
        var admission = await coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId,
            new("synthetic-failure-route", 1, "synthetic-provider", "synthetic-model", null, false, []), CancellationToken.None);
        var registration = owner.RegisterOriginalAttemptAsync(admission, CancellationToken.None);
        await registration;
        return new(coordinator, owner, authority, admission, registration);
    }
    private sealed record FailureSettlementHarness(TaskExecutionCoordinator Coordinator, TaskRunOriginalFrameOwner Owner,
        FailureSettlementAuthority Authority, TaskRunAttemptAdmission Admission, Task Registration);
    private sealed class FailureSettlementAuthority(Task close, TaskCompletionSource? entered) : ITaskRunCommandAuthority
    {
        public FailureSettlementLease? Lease { get; private set; }
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot snapshot, CancellationToken token) => Task.FromResult(
            new TaskExecutionOwnerBinding(snapshot.TaskId, snapshot.ContextId, snapshot.ExecutionId,
                "synthetic-actor", "synthetic-profile", null, null, "synthetic-auth", "synthetic-owner"));
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid id,
            TaskRunRouteCandidate candidate, Guid? previous, CancellationToken token) => Task.FromResult<ITaskRunAdmissionLease>(
                Lease = new(snapshot.OwnerBinding!, id, candidate, close, entered));
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attempt, Guid action, string receipt,
            CancellationToken token) => Task.CompletedTask;
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class FailureSettlementLease(TaskExecutionOwnerBinding owner, Guid id, TaskRunRouteCandidate candidate,
        Task close, TaskCompletionSource? entered) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => id;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic-failure-settlement-lease";
        public int Disposes { get; private set; }
        public ValueTask RevalidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposes++; entered?.TrySetResult(); return new(close); }
    }
}
