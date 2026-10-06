using System.Collections;
using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCanonicalContextProducerTests
{
    [Fact]
    public async Task Copied_completed_row_cannot_replace_the_private_actual_completion_acknowledgment()
    {
        var token = TestContext.Current.CancellationToken;
        var h = Harness.Create(temporary: true);
        var exact = new IOException("actual context refused before any attempt");
        Task? actualCopiedWrite = null;
        h.Capture.BeforeCapture = () =>
        {
            var original = h.Service.CurrentCanonicalTask!;
            actualCopiedWrite = h.TaskRepository.UpsertAsync(original with
            {
                State = TaskExecutionLifecycle.Completed,
                PersistenceRevision = original.PersistenceRevision + 1
            }, token);
            // This controlled repository writes synchronously; observe its actual returned Task.
            Assert.True(actualCopiedWrite.IsCompletedSuccessfully);
        };
        h.Capture.Refusal = exact;
        var actualRun = h.RunAsync("copied status is not original completion");
        Assert.Same(exact, await Record.ExceptionAsync(() => actualRun.WaitAsync(token)));
        Assert.True(actualCopiedWrite!.IsCompletedSuccessfully);
        var current = h.Service.CurrentCanonicalTask!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
        Assert.Empty(current.Attempts);
        Assert.NotNull(current.RecoveryObservation);
        Assert.Equal(TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked, current.RecoveryObservation.SettlementOutcome);
        Assert.DoesNotContain(h.Events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(1, h.Coordinator.LiveOriginalInvocationCount);
        Assert.Equal(0, h.Authority.AttemptChecks);
        Assert.Equal(0, h.Provider.Frames);
        Assert.Equal(0, h.Workspace.Effects);
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
    }

    [Fact]
    public async Task Genuine_completed_CAS_is_conserved_when_later_actual_stage_cleanup_faults_and_never_retires_as_healthy()
    {
        var token = TestContext.Current.CancellationToken;
        var releaseRetirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HeldCompletedRetirement? runtimeOwner = null;
        var h = Harness.Create(temporary: false, runtimeOwner: runtime => runtimeOwner = new HeldCompletedRetirement(runtime, releaseRetirement.Task));
        var actualOce = new OperationCanceledException("actual post-completion stage cancellation callback failed");
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration callback = default;
        object? completedStage = null;
        TaskExecutionSnapshot? acknowledged = null;
        h.Coordinator.SnapshotChanged += (_, saved) =>
        {
            if (saved.State != TaskExecutionLifecycle.Completed || acknowledged is not null) return;
            acknowledged = saved;
            completedStage = Assert.Single(ReadActualCompletionStages(h.Coordinator),
                stage => (string)ReadActualCompletionMember(stage, "Name") == "complete-original-attempt");
            var lifetime = (CancellationTokenSource)ReadActualCompletionMember(completedStage, "_lifetime");
            callback = lifetime.Token.Register(() => { callbackEntered.TrySetResult(); throw actualOce; });
        };
        var actualRun = h.RunAsync("retain actual completion input");
        try
        {
            var actualOwner = Assert.IsType<HeldCompletedRetirement>(runtimeOwner);
            await actualOwner.Entered.Task.WaitAsync(token);
            var actualAcknowledged = Assert.IsType<TaskExecutionSnapshot>(acknowledged);
            Assert.Equal(TaskExecutionLifecycle.Completed, actualAcknowledged.State);
            Assert.Equal(TaskRunAttemptState.Completed, Assert.Single(actualAcknowledged.Attempts).State);
            Assert.False(actualRun.IsCompleted);
            Assert.Equal(1, h.Authority.Lease!.Disposes);
            Assert.Equal(1, h.Provider.Frames);
            h.Coordinator.RequestOriginalProcessRetirement();
            await callbackEntered.Task.WaitAsync(token);
            var actualStop = (Task)ReadActualCompletionMember(completedStage!, "_stop");
            var stopFailure = await Record.ExceptionAsync(() => actualStop.WaitAsync(token));
            Assert.NotNull(stopFailure);
            Assert.True(actualStop.IsFaulted);
            Assert.Contains(OriginalCauses(stopFailure!), cause => ReferenceEquals(cause, actualOce));
            releaseRetirement.SetResult();
            var actualFailure = await Record.ExceptionAsync(() => actualRun.WaitAsync(token));
            Assert.NotNull(actualFailure);
            Assert.True(actualRun.IsFaulted);
            Assert.Contains(OriginalCauses(actualFailure!), cause => ReferenceEquals(cause, actualOce));
            var current = (await h.TaskRepository.GetAsync(actualAcknowledged.TaskId, token))!;
            Assert.Equal(actualAcknowledged.TaskId, current.TaskId);
            Assert.Equal(actualAcknowledged.ContextId, current.ContextId);
            Assert.Equal(actualAcknowledged.ExecutionId, current.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Completed, current.State);
            Assert.Equal(TaskRunAttemptState.Completed, Assert.Single(current.Attempts).State);
            Assert.NotNull(current.RecoveryObservation);
            Assert.Equal(TaskRunOriginalSettlementOutcome.JoinedByOriginalCompletion, current.RecoveryObservation.SettlementOutcome);
            Assert.Contains(current.RecoveryObservation.Causes, cause => cause.Message.Contains("post-completion stage cancellation callback", StringComparison.Ordinal));
            Assert.DoesNotContain(h.Events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            Assert.Single(h.Conversations.Messages, value => value.Role == MessageRole.User);
            Assert.Equal(1, h.Coordinator.LiveOriginalInvocationCount);
            Assert.Equal(1, h.Authority.AttemptChecks);
            Assert.Equal(0, h.Workspace.Effects);
            Assert.True(actualOwner.ActualRetirement!.IsCompletedSuccessfully);
            var actualCompletion = (Task)ReadActualCompletionMember(completedStage!, "ActualDriver");
            Assert.True(actualCompletion.IsFaulted);
            var closeFailure = await Record.ExceptionAsync(() => h.Coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token));
            Assert.NotNull(closeFailure);
            Assert.Contains(OriginalCauses(closeFailure!), cause => ReferenceEquals(cause, actualOce));
            await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
            Assert.Equal(TaskExecutionLifecycle.Completed, (await h.TaskRepository.GetAsync(current.TaskId, token))!.State);
        }
        finally
        {
            releaseRetirement.TrySetResult();
            _ = await Record.ExceptionAsync(() => actualRun);
            callback.Dispose();
        }
    }

    private static object ReadActualCompletionMember(object actual, string name) =>
        actual.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(actual)
        ?? throw new InvalidOperationException($"The genuine owning member {name} was not found.");
    private static IEnumerable<object> ReadActualCompletionStages(TaskExecutionCoordinator owner) =>
        ((IEnumerable)ReadActualCompletionMember(owner, "_originalProcessStages")).Cast<object>();

    private sealed class HeldCompletedRetirement(TaskRunOriginalFrameOwner original, Task release) :
        ITaskRunRuntimeSettlement, ITaskRunOriginalAttemptRetirement, ITaskRunOriginalAttemptRegistrationSource
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? ActualRetirement;
        public Task AwaitSettlementAsync(Guid taskId, Guid executionId, Guid attemptId, CancellationToken token) =>
            original.AwaitSettlementAsync(taskId, executionId, attemptId, token);
        public ValueTask RetireAcknowledgedOriginalAttemptAsync(TaskRunOriginalRetirementAcknowledgment acknowledgment, CancellationToken token)
        {
            ActualRetirement = RetireActualAsync(acknowledgment, token);
            return new ValueTask(ActualRetirement);
        }
        private async Task RetireActualAsync(TaskRunOriginalRetirementAcknowledgment acknowledgment, CancellationToken token)
        {
            var actual = original.RetireAcknowledgedOriginalAttemptAsync(acknowledgment, token).AsTask();
            await actual.ConfigureAwait(false);
            Entered.SetResult();
            await release.ConfigureAwait(false);
        }
        public TaskRunOriginalAttemptRegistrationDisposition RegisterOriginalAttemptDisposition(TaskRunAttemptAdmission sameOriginal, CancellationToken token) =>
            ((ITaskRunOriginalAttemptRegistrationSource)original).RegisterOriginalAttemptDisposition(sameOriginal, token);
        public bool IsIssuedOriginalAttemptRegistrationDisposition(TaskRunOriginalAttemptRegistrationDisposition receipt, TaskRunAttemptAdmission sameOriginal) =>
            ((ITaskRunOriginalAttemptRegistrationSource)original).IsIssuedOriginalAttemptRegistrationDisposition(receipt, sameOriginal);
    }
}

public sealed partial class TaskRunLateAdmissionProcessTests
{
    [Theory]
    [InlineData("running", false)]
    [InlineData("running", true)]
    [InlineData("complete", false)]
    [InlineData("complete", true)]
    [InlineData("failure", false)]
    [InlineData("failure", true)]
    public async Task Terminal_command_retains_actual_raw_snapshot_fault_siblings_and_distinguishes_genuine_cancellation(string command, bool genuineCancellation)
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        var begun = await h.BeginAsync(token);
        var admission = await h.Coordinator.StartAttemptAsync(begun.TaskId, begun.ExecutionId, Route(), token);
        var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exact = new OperationCanceledException("actual terminal snapshot raw fault");
        var sibling = new IOException("actual terminal snapshot raw sibling");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        if (genuineCancellation) raw.SetCanceled(canceled.Token); else raw.SetException([exact, sibling]);
        h.Rows.OverrideRead = raw.Task;
        var original = command switch
        {
            "running" => h.Coordinator.MarkAttemptRunningAsync(begun.TaskId, begun.ExecutionId, admission.AttemptId, token),
            "complete" => h.Coordinator.CompleteAttemptAsync(begun.TaskId, begun.ExecutionId, admission.AttemptId, token),
            "failure" => h.Coordinator.RecordAttemptFailureAsync(begun.TaskId, begun.ExecutionId, admission.AttemptId,
                new ExecutionFailure("CONTROLLED", "controlled", "actual controlled failure"), token),
            _ => throw new InvalidOperationException()
        };
        var error = await Record.ExceptionAsync(() => original.WaitAsync(token));
        Assert.NotNull(error);
        Assert.Equal(genuineCancellation, original.IsCanceled);
        Assert.Equal(!genuineCancellation, original.IsFaulted);
        if (!genuineCancellation)
        {
            Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, exact));
            Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, sibling));
        }
        Assert.Contains(AllSources(h.Coordinator), actual => ReferenceEquals(actual, raw.Task));
        h.Rows.OverrideRead = null;
        var saved = (await h.Rows.GetAsync(begun.TaskId, token))!;
        Assert.Equal(TaskExecutionLifecycle.Running, saved.State);
        Assert.Equal(TaskRunAttemptState.Admitted, Assert.Single(saved.Attempts).State);
        Assert.Equal(begun.ExecutionId, saved.ExecutionId);
        Assert.Equal(0, ((Lease)admission.Lease).Disposes);
        _ = await Record.ExceptionAsync(() => h.Coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token));
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
        Assert.Equal(1, ((Lease)admission.Lease).Disposes);
    }
}
