using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task End_pass_joins_terminal_ack_excludes_resume_and_preserves_the_actual_durable_queue()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var initial = rig.Service.CurrentCanonicalTask!;
        var required = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Tasks.PauseOriginalRunAsync(initial.TaskId, initial.ExecutionId, token);
        await rig.Owner.ApproveOriginalAsync(required.Id, token);
        var queued = await rig.Tasks.SubmitFollowUpAsync(initial.TaskId, "keep this queued instruction",
            TaskFollowUpMode.Queue, null, null, token);
        var controls = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(initial.TaskId, initial.ExecutionId, token);
        Assert.True(controls.CanResumeUnstartedOriginal);
        Assert.True(controls.CanEndPass);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.EndPausedOriginalRunAsync(
            controls.CanonicalTaskContext with { PersistenceRevision = controls.CanonicalTaskContext.PersistenceRevision - 1 }, token));
        var held = rig.TaskRows.HeldTerminalWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var ending = rig.Tasks.EndPausedOriginalRunAsync(controls.CanonicalTaskContext, token);
        try
        {
            await rig.TaskRows.TerminalWriteEntered.Task.WaitAsync(token);
            Assert.False(ending.IsCompleted);
            var beforeAck = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, beforeAck.State);
            Assert.Equal(queued.Snapshot.Queue, beforeAck.Queue);
            var pending = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(initial.TaskId, initial.ExecutionId, token);
            Assert.False(pending.CanEndPass);
            Assert.False(pending.CanResumeUnstartedOriginal);
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.ResumeOriginalRunAsync(initial.TaskId, initial.ExecutionId, token));
            Assert.Equal(0, rig.Client.Dispatches);
            held.SetResult();
            var ended = await ending.WaitAsync(token);
            Assert.Equal(TaskRunOriginalRunControlKind.EndPass, ended.Kind);
            Assert.Equal(TaskRunOriginalRunControlDisposition.EndedAfterOriginalPause, ended.Disposition);
            Assert.Equal(TaskExecutionLifecycle.Cancelled, ended.State);
            Assert.False(ended.RequiresInspection);
            var terminal = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            Assert.Equal(initial.TaskId, terminal.TaskId);
            Assert.Equal(initial.ExecutionId, terminal.ExecutionId);
            Assert.Equal(queued.Snapshot.Queue, terminal.Queue);
            Assert.Equal("ORIGINAL_PASS_ENDED_BY_USER", terminal.RecoveryObservation?.ReasonCode);
            Assert.Contains(terminal.RecoveryHistory, observation => observation.ObservationId == initial.RecoveryObservation!.ObservationId);
            Assert.Equal(QueuedFollowUpState.Queued, Assert.Single(terminal.Queue).State);
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.SubmitFollowUpAsync(initial.TaskId,
                "do not resurrect the ended pass", TaskFollowUpMode.Steer, null, null, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.PromoteQueuedToSteerAsync(initial.TaskId,
                terminal.Queue[0].TaskId, null, token));
            var afterRefusal = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Cancelled, afterRefusal.State);
            Assert.Equal(terminal.PersistenceRevision, afterRefusal.PersistenceRevision);
            Assert.Equal(terminal.Queue, afterRefusal.Queue);
            Assert.Same(ended, await rig.Tasks.EndPausedOriginalRunAsync(controls.CanonicalTaskContext, token));
            var unavailable = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(initial.TaskId, initial.ExecutionId, token);
            Assert.False(unavailable.CanEndPass); Assert.False(unavailable.CanResumeUnstartedOriginal);
            Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User);
        }
        finally
        {
            held.TrySetResult();
            _ = await Record.ExceptionAsync(() => ending);
        }
    }

    [Fact]
    public async Task End_pass_requires_the_actual_pause_ack_not_only_a_suspended_snapshot()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var initial = rig.Service.CurrentCanonicalTask!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, initial.State);
        var controls = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(initial.TaskId, initial.ExecutionId, token);
        Assert.False(controls.CanEndPass);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.EndPausedOriginalRunAsync(controls.CanonicalTaskContext, token));
        var unchanged = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
        Assert.Equal(initial.PersistenceRevision, unchanged.PersistenceRevision);
        Assert.Equal(TaskExecutionLifecycle.Suspended, unchanged.State);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task A_failed_actual_end_pass_write_retains_its_cause_and_cannot_advertise_terminal_or_resume_success()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var initial = rig.Service.CurrentCanonicalTask!;
        await rig.Tasks.PauseOriginalRunAsync(initial.TaskId, initial.ExecutionId, token);
        var controls = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(initial.TaskId, initial.ExecutionId, token);
        Assert.True(controls.CanEndPass);
        var held = rig.TaskRows.HeldTerminalWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var ending = rig.Tasks.EndPausedOriginalRunAsync(controls.CanonicalTaskContext, token);
        var exact = new IOException("actual terminal repository write failed");
        try
        {
            await rig.TaskRows.TerminalWriteEntered.Task.WaitAsync(token);
            held.SetException(exact);
            var failure = await Record.ExceptionAsync(() => ending.WaitAsync(token));
            Assert.NotNull(failure);
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, exact));
            Assert.Equal(TaskExecutionLifecycle.Suspended, (await rig.Tasks.GetAsync(initial.TaskId, token))!.State);
            var unavailable = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(initial.TaskId, initial.ExecutionId, token);
            Assert.False(unavailable.CanEndPass); Assert.False(unavailable.CanResumeUnstartedOriginal);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            held.TrySetException(exact);
            _ = await Record.ExceptionAsync(() => ending);
        }
    }
}
