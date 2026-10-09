using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Actual_pause_waits_for_held_original_provider_disposal_and_retains_faulted_cleanup_as_same_run_inspection()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var required = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(required.Id, token);
        rig.Client.RunApprovedOwnedFrame = true;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        rig.Client.NextDisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = await rig.Tasks.ResumeOriginalRunAsync(original.TaskId, original.ExecutionId, token);
        async Task ConsumeActualAsync()
        {
            await foreach (var value in actual.WithCancellation(token)) rig.Stream.Add(value);
        }
        var consuming = ConsumeActualAsync();
        Task<TaskRunOriginalRunControlResult>? pausing = null;
        var exact = new IOException("actual held resumed provider close failed");
        try
        {
            await rig.Client.NextDisposeEntered.Task.WaitAsync(token);
            Assert.True(Assert.Single(rig.Capture.ApprovedRunningOriginals).IsCompletedSuccessfully);
            var running = rig.Service.CurrentCanonicalTask!;
            Assert.Equal(TaskExecutionLifecycle.Running, running.State);
            Assert.Equal(TaskRunAttemptState.Running, Assert.Single(running.Attempts).State);
            pausing = rig.Tasks.PauseOriginalRunAsync(original.TaskId, original.ExecutionId, token);
            Assert.False(pausing.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            Assert.Equal(1, rig.Client.Dispatches);
            held.SetException(exact);
            var bodyFailure = await Record.ExceptionAsync(() => consuming.WaitAsync(token));
            var pauseFailure = await Record.ExceptionAsync(() => pausing.WaitAsync(token));
            Assert.NotNull(bodyFailure);
            Assert.NotNull(pauseFailure);
            Assert.True(pausing.IsFaulted);
            Assert.Contains(Leaves(pauseFailure!), error => ReferenceEquals(error, exact));
            var suspended = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(original.TaskId, suspended.TaskId);
            Assert.Equal(original.ExecutionId, suspended.ExecutionId);
            Assert.Equal(original.ContextId, suspended.ContextId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, suspended.State);
            Assert.NotNull(suspended.RecoveryObservation);
            Assert.Single(suspended.RecoveryHistory);
            Assert.DoesNotContain(rig.Stream, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.Equal(2, rig.Tasks.LiveOriginalInvocationCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.ResumeOriginalRunAsync(original.TaskId, original.ExecutionId, token));
            Assert.Single((await rig.Tasks.GetAsync(original.TaskId, token))!.Attempts);
        }
        finally
        {
            held.TrySetException(exact);
            _ = await Record.ExceptionAsync(() => consuming);
            if (pausing is not null) _ = await Record.ExceptionAsync(() => pausing);
        }
    }

    [Fact]
    public async Task Actual_pause_and_approved_resume_use_the_same_live_input_run_and_producer_without_new_user_message()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var saved = rig.Service.CurrentCanonicalTask!;
        var acceptedUser = Assert.Single(rig.Conversations.Messages);
        var waiting = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        var initial = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(saved.TaskId, saved.ExecutionId, token);
        Assert.True(initial.CanPause);
        Assert.True(initial.CanStop);
        Assert.False(initial.CanResumeUnstartedOriginal);
        var pause = await rig.Tasks.PauseOriginalRunAsync(saved.TaskId, saved.ExecutionId, token);
        Assert.Equal(TaskRunOriginalRunControlKind.Pause, pause.Kind);
        Assert.Equal(TaskRunOriginalRunControlDisposition.Suspended, pause.Disposition);
        Assert.Equal(saved.TaskId, pause.CanonicalTaskContext.TaskId);
        Assert.Equal(saved.ContextId, pause.CanonicalTaskContext.ContextId);
        Assert.Equal(saved.ExecutionId, pause.CanonicalTaskContext.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, pause.State);
        Assert.Equal(0, rig.Client.Dispatches);
        var repeated = await rig.Tasks.PauseOriginalRunAsync(saved.TaskId, saved.ExecutionId, token);
        Assert.Same(pause, repeated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.ResumeOriginalRunAsync(saved.TaskId, saved.ExecutionId, token));
        Assert.Equal(saved.PersistenceRevision, (await rig.Tasks.GetAsync(saved.TaskId, token))!.PersistenceRevision);
        await rig.Owner.ApproveOriginalAsync(waiting.Id, token);
        Assert.Equal(0, rig.Client.Dispatches);
        var permitted = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(saved.TaskId, saved.ExecutionId, token);
        Assert.True(permitted.CanResumeUnstartedOriginal);
        rig.Client.RunApprovedOwnedFrame = true;
        var actual = await rig.Tasks.ResumeOriginalRunAsync(saved.TaskId, saved.ExecutionId, token);
        var events = new List<ChatStreamEvent>();
        await foreach (var value in actual.WithCancellation(token)) events.Add(value);
        var completed = rig.Service.CurrentCanonicalTask!;
        Assert.Equal(saved.TaskId, completed.TaskId);
        Assert.Equal(saved.ExecutionId, completed.ExecutionId);
        Assert.Equal(saved.ContextId, completed.ContextId);
        Assert.Equal(TaskExecutionLifecycle.Completed, completed.State);
        Assert.Single(completed.Attempts);
        Assert.Equal(acceptedUser, Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User));
        Assert.DoesNotContain(events, value => value.Kind == ChatStreamEventKind.UserMessage);
        Assert.Single(events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Single(completed.RecoveryHistory);
        Assert.Equal(1, rig.Client.Dispatches);
        Assert.Equal(0, rig.Workspace.Effects);
        Assert.Equal(0, rig.Tasks.LiveOriginalInvocationCount);
    }

    [Fact]
    public async Task Explicit_stop_acknowledges_cancelled_only_after_the_same_closed_no_attempt_source_and_retains_history()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var saved = rig.Service.CurrentCanonicalTask!;
        var originalObservation = saved.RecoveryObservation;
        var result = await rig.Tasks.StopOriginalRunAsync(saved.TaskId, saved.ExecutionId, token);
        Assert.Equal(TaskRunOriginalRunControlKind.Stop, result.Kind);
        Assert.Equal(TaskRunOriginalRunControlDisposition.CancelledAfterOriginalClose, result.Disposition);
        Assert.Equal(TaskExecutionLifecycle.Cancelled, result.State);
        Assert.False(result.CanResumeUnstartedOriginal);
        var current = (await rig.Tasks.GetAsync(saved.TaskId, token))!;
        Assert.Equal(saved.TaskId, current.TaskId);
        Assert.Equal(saved.ContextId, current.ContextId);
        Assert.Equal(saved.ExecutionId, current.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Cancelled, current.State);
        Assert.Equal(originalObservation!.ObservationId, current.RecoveryObservation!.ObservationId);
        Assert.Empty(current.Attempts);
        Assert.Empty(current.Plan);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Workspace.Effects);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.ResumeOriginalRunAsync(saved.TaskId, saved.ExecutionId, token));
        Assert.Equal(TaskExecutionLifecycle.Cancelled, (await rig.Tasks.GetAsync(saved.TaskId, token))!.State);
    }

    [Fact]
    public async Task Current_actor_revocation_and_reconstructed_owner_refuse_task_controls_before_original_stop_or_dispatch()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var saved = rig.Service.CurrentCanonicalTask!;
        var disposals = rig.Client.StreamDisposals;
        var actualActor = rig.Actors.Current;
        rig.Actors.Current = null;
        Assert.NotNull(await Record.ExceptionAsync(() => rig.Tasks.PauseOriginalRunAsync(saved.TaskId, saved.ExecutionId, token)));
        Assert.NotNull(await Record.ExceptionAsync(() => rig.Tasks.StopOriginalRunAsync(saved.TaskId, saved.ExecutionId, token)));
        Assert.Equal(disposals, rig.Client.StreamDisposals);
        Assert.Equal(saved.PersistenceRevision, (await rig.Tasks.GetAsync(saved.TaskId, token))!.PersistenceRevision);
        rig.Actors.Current = actualActor;
        var reconstructed = new TaskExecutionCoordinator(rig.TaskRows, rig.Events, admissionAuthority: rig.Authority, runtimeSettlement: rig.Settlement);
        var observed = await reconstructed.GetOriginalRunControlAvailabilityAsync(saved.TaskId, saved.ExecutionId, token);
        Assert.False(observed.CanPause);
        Assert.False(observed.CanStop);
        Assert.False(observed.CanResumeUnstartedOriginal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconstructed.PauseOriginalRunAsync(saved.TaskId, saved.ExecutionId, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconstructed.ResumeOriginalRunAsync(saved.TaskId, saved.ExecutionId, token));
        Assert.Equal(disposals, rig.Client.StreamDisposals);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Workspace.Effects);
        Assert.Equal(saved.TaskId, (await rig.Tasks.GetByContextAsync(saved.ContextId, token))!.TaskId);
    }
}

public sealed partial class ChatCanonicalContextProducerTests
{
    [Fact]
    public async Task A_restored_context_snapshot_callback_cannot_admit_a_self_joining_original_run_control()
    {
        var token = TestContext.Current.CancellationToken;
        var restored = ExecutionContext.Capture() ?? throw new InvalidOperationException("Missing controlled pre-owner context.");
        var h = Harness.Create(temporary: true);
        Exception? actualRefusal = null;
        Task<TaskRunOriginalRunControlResult>? admitted = null;
        h.Coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Attempts.Count != 0 || actualRefusal is not null) return;
            ExecutionContext.Run(restored, _ =>
            {
                actualRefusal = Record.Exception(() => { admitted = h.Coordinator.PauseOriginalRunAsync(snapshot.TaskId, snapshot.ExecutionId, token); });
            }, null);
        };
        await h.RunAsync("finish actual body after pure callback refusal").WaitAsync(token);
        Assert.IsType<InvalidOperationException>(actualRefusal);
        Assert.Null(admitted);
        Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask!.State);
        Assert.Equal(1, h.Provider.Frames);
        Assert.Equal(1, h.Authority.Lease!.Disposes);
        Assert.Equal(0, h.Workspace.Effects);
        Assert.Equal(0, h.Coordinator.LiveOriginalInvocationCount);
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
    }
}
