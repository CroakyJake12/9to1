using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    // The key is the SAME privately issued invocation, never a persisted status or public DTO.
    private readonly ConditionalWeakTable<TaskRunInvocationCustody, OriginalCompletionCasAcknowledgment> _originalCompletionCasAcknowledgments = new();

    private sealed class OriginalCompletionCasAcknowledgment(
        TaskExecutionCoordinator issuer, TaskRunInvocationCustody invocation, TaskRunProcessStageCustody stage,
        TaskRunAttemptAdmission admission, Task settlement, Task<TaskExecutionSnapshot> write, TaskExecutionSnapshot snapshot)
    {
        internal readonly TaskExecutionCoordinator Issuer = issuer;
        internal readonly TaskRunInvocationCustody Invocation = invocation;
        internal readonly TaskRunProcessStageCustody Stage = stage;
        internal readonly TaskRunAttemptAdmission Admission = admission;
        internal readonly Task Settlement = settlement;
        internal readonly Task<TaskExecutionSnapshot> Write = write;
        internal readonly TaskExecutionSnapshot Snapshot = snapshot;
    }

    private void BindOriginalCompletionCasAcknowledgment(TaskRunInvocationCustody invocation,
        TaskRunProcessStageCustody stage, TaskRunAttemptAdmission admission, Task settlement,
        Task<TaskExecutionSnapshot> actualWrite, TaskExecutionSnapshot acknowledged)
    {
        RequireOriginalInvocation(invocation);
        if (!ReferenceEquals(stage.Owner, this) || !ReferenceEquals(TaskRunProcessStageCustody.CurrentFor(this), stage)
            || !ReferenceEquals(invocation.OriginalSettlement, settlement) || !settlement.IsCompletedSuccessfully
            || !actualWrite.IsCompletedSuccessfully || !ReferenceEquals(actualWrite.Result, acknowledged)
            || !_issuedAdmissions.TryGetValue(admission.AttemptId, out var issued) || !ReferenceEquals(issued, admission)
            || acknowledged.State != TaskExecutionLifecycle.Completed
            || acknowledged.TaskId != admission.Snapshot.TaskId || acknowledged.ContextId != admission.Snapshot.ContextId
            || acknowledged.ExecutionId != admission.Snapshot.ExecutionId || acknowledged.OwnerBinding != admission.Snapshot.OwnerBinding
            || acknowledged.Attempts.LastOrDefault() is not { State: TaskRunAttemptState.Completed } completed
            || completed.Id != admission.AttemptId || !invocation.CompletionCallAdmitted
            || !invocation.ReachedEnd || !invocation.OwnedCleanupTerminal || !invocation.ResourcesDisposed
            || invocation.Causes.Count != 0 || invocation.OriginalMoves.Any(actual => !actual.IsCompletedSuccessfully)
            || !invocation.DisposeInvoked || invocation.OriginalDispose is not { IsCompletedSuccessfully: true }
            || invocation.OriginalTracker is not null && invocation.OriginalTrackerDispose is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("Only the genuine same-invocation completion CAS after actual original cleanup may acknowledge completion.");
        RequireCompletionBasis(invocation.CompletionBasis
            ?? throw new InvalidOperationException("No actual completion basis exists."), acknowledged);
        invocation.RetainAdditionalOriginal("completion.actual-cas", actualWrite);
        _originalCompletionCasAcknowledgments.Add(invocation,
            new OriginalCompletionCasAcknowledgment(this, invocation, stage, admission, settlement, actualWrite, acknowledged));
    }

    private OriginalCompletionCasAcknowledgment? RequireMatchingOriginalCompletionCasAcknowledgment(
        TaskRunInvocationCustody invocation, TaskExecutionSnapshot current)
    {
        if (!_originalCompletionCasAcknowledgments.TryGetValue(invocation, out var original)) return null;
        RequireOriginalInvocation(invocation);
        if (!ReferenceEquals(original.Issuer, this) || !ReferenceEquals(original.Invocation, invocation)
            || !ReferenceEquals(original.Stage.Owner, this) || !original.Write.IsCompletedSuccessfully
            || !ReferenceEquals(original.Write.Result, original.Snapshot) || !original.Settlement.IsCompletedSuccessfully
            || !ReferenceEquals(invocation.OriginalSettlement, original.Settlement)
            || current.State != TaskExecutionLifecycle.Completed || current.PersistenceRevision < original.Snapshot.PersistenceRevision
            || current.Attempts.LastOrDefault() is not { State: TaskRunAttemptState.Completed } completed
            || completed.Id != original.Admission.AttemptId)
            throw new InvalidOperationException("The current task no longer matches its genuine original completed CAS acknowledgment.");
        RequireCompletionBasis(original.Snapshot, current);
        return original;
    }

    private static async Task AwaitOriginalProcessSettlementAsync(TaskRunProcessStageCustody stage,
        Task actualSettlement, CancellationToken callerToken)
    {
        stage.RetainSource(actualSettlement);
        try
        {
            // A canceled caller withdraws this wait only; the actual owning settlement is retained.
            if (callerToken.CanBeCanceled) await actualSettlement.WaitAsync(callerToken).ConfigureAwait(false);
            else await actualSettlement.ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            stage.RetainOriginalFailure(cause, actualSettlement);
            if (actualSettlement.IsFaulted && actualSettlement.Exception is { } originalEnvelope)
                throw originalEnvelope;
            throw;
        }
    }

    private T InvokeOriginalInvocationObservationSource<T>(TaskRunInvocationCustody custody, Func<T> source)
    {
        RequireOriginalInvocation(custody);
        try { return TaskRunProcessProducerContext.Invoke(this, source); }
        catch (Exception cause) { custody.Retain(cause); custody.ThrowRetained(); throw; }
    }

    private async Task<T> AwaitOriginalInvocationObservationSourceAsync<T>(TaskRunInvocationCustody custody,
        string stage, Func<Task<T>> source)
    {
        Task<T>? actual = null;
        try
        {
            actual = InvokeOriginalInvocationObservationSource(custody, source)
                ?? throw new InvalidOperationException("No actual original invocation observation Task was returned.");
            custody.RetainAdditionalOriginal(stage, actual);
            return await actual.ConfigureAwait(false);
        }
        catch (Exception cause) { custody.Retain(cause, actual); custody.ThrowRetained(); throw; }
    }

    private async Task AwaitOriginalInvocationObservationSourceAsync(TaskRunInvocationCustody custody,
        string stage, Func<Task> source)
    {
        Task? actual = null;
        try
        {
            actual = InvokeOriginalInvocationObservationSource(custody, source)
                ?? throw new InvalidOperationException("No actual original invocation observation Task was returned.");
            custody.RetainAdditionalOriginal(stage, actual);
            await actual.ConfigureAwait(false);
        }
        catch (Exception cause) { custody.Retain(cause, actual); custody.ThrowRetained(); throw; }
    }

    private async Task<TaskExecutionSnapshot> RequireOriginalInvocationSnapshotAsync(TaskRunInvocationCustody custody,
        Guid taskId, string stage) => await AwaitOriginalInvocationObservationSourceAsync(custody, stage,
            () => repository.GetAsync(taskId, CancellationToken.None)).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");
}
