using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class TaskRunOriginalActionAdmissionTests
{
    private static object ActualToolOutcomeCustody(TaskExecutionCoordinator coordinator,
        ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, TaskExecutionSnapshot acknowledgment) =>
        typeof(TaskExecutionCoordinator).GetMethod("CaptureOriginalToolOutcomeCustody", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [preparation, result, acknowledgment])!;

    private static bool ActualToolOutcomeIsClosed(TaskExecutionCoordinator coordinator, object sameOriginal) =>
        (bool)typeof(TaskExecutionCoordinator).GetMethod("HasSuccessfulOriginalToolCheckpointOutcome", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [sameOriginal])!;

    [Fact]
    public async Task Same_validated_result_acknowledgment_and_actual_owner_close_are_both_required_for_private_tool_checkpoint_custody()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var prep = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(prep, null, "controlled original checkpoint", token);
        var result = await CompleteControlledOriginalProcessAsync(h, prep, 7, false, token);
        var actualWrite = h.Coordinator.RecordOriginalToolActionOutcomeAsync(prep, result, token);
        var acknowledged = await actualWrite;
        var original = ActualToolOutcomeCustody(h.Coordinator, prep, result, acknowledged);
        Assert.False(ActualToolOutcomeIsClosed(h.Coordinator, original));
        Assert.Same(result, original.GetType().GetField("Result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original));
        Assert.Same(acknowledged, original.GetType().GetField("Acknowledgment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original));
        var stage = original.GetType().GetField("Stage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)!;
        Assert.Same(actualWrite, stage.GetType().GetField("ActualDriver", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stage));
        var copiedResult = result with { };
        var copiedAck = acknowledged with { };
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => { _ = ActualToolOutcomeCustody(h.Coordinator, prep, copiedResult, acknowledged); }).InnerException);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => { _ = ActualToolOutcomeCustody(h.Coordinator, prep, result, copiedAck); }).InnerException);
        await h.Coordinator.RetireAcknowledgedToolOriginalAsync(prep, acknowledged);
        Assert.True(ActualToolOutcomeIsClosed(h.Coordinator, original));
        Assert.Same(original, ActualToolOutcomeCustody(h.Coordinator, prep, result, acknowledged));
        var copy = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(original, null)!;
        Assert.False(ActualToolOutcomeIsClosed(h.Coordinator, copy));
        Assert.False(Assert.Single(acknowledged.Plan).OriginalOperationOutcome!.BusinessSucceeded);
        Assert.Equal(7, Assert.Single(acknowledged.Plan).OriginalOperationOutcome!.ProcessExitCode);
        Assert.Equal(1, h.First.Effects);
        Assert.Equal(1, h.First.Retirements);
        Assert.Equal(0, h.Second.Effects);
        Assert.Equal(h.Task.TaskId, acknowledged.TaskId);
        Assert.Equal(h.Task.ExecutionId, acknowledged.ExecutionId);
    }

    [Fact]
    public async Task Held_then_faulted_actual_owner_close_conserves_accepted_failed_business_result_but_cannot_issue_checkpoint_replay_proof()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var prep = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(prep, null, "controlled held owner close", token);
        var result = await CompleteControlledOriginalProcessAsync(h, prep, 7, false, token);
        var acknowledged = await h.Coordinator.RecordOriginalToolActionOutcomeAsync(prep, result, token);
        var original = ActualToolOutcomeCustody(h.Coordinator, prep, result, acknowledged);
        var rawClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Owners.OriginalRetirementClose = rawClose.Task;
        var outward = h.Coordinator.RetireAcknowledgedToolOriginalAsync(prep, acknowledged).AsTask();
        try
        {
            Assert.False(ActualToolOutcomeIsClosed(h.Coordinator, original));
            Assert.False(rawClose.Task.IsCompleted);
            var oce = new OperationCanceledException("actual faulted original tool owner close");
            var io = new IOException("actual original close sibling");
            rawClose.SetException([oce, io]);
            await outward.WaitAsync(token); // Existing durable-ACK observer contract keeps this known write.
            Assert.False(ActualToolOutcomeIsClosed(h.Coordinator, original));
            var actionOriginal = original.GetType().GetField("ActionOriginal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)!;
            var actualClose = Assert.IsAssignableFrom<Task>(actionOriginal.GetType().GetField("Retirement", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actionOriginal));
            Assert.True(actualClose.IsFaulted);
            Assert.False(actualClose.IsCanceled);
            Assert.Contains(Causes(actualClose.Exception!), cause => ReferenceEquals(cause, oce));
            Assert.Contains(Causes(actualClose.Exception!), cause => ReferenceEquals(cause, io));
            var current = (await h.Rows.GetAsync(h.Task.TaskId, token))!;
            Assert.Equal(h.Task.TaskId, current.TaskId);
            Assert.Equal(h.Task.ExecutionId, current.ExecutionId);
            Assert.Equal(result.OwnerReceiptReference, Assert.Single(current.Plan).Acceptance!.OwnerReceiptReference);
            Assert.False(Assert.Single(current.Plan).OriginalOperationOutcome!.BusinessSucceeded);
            var other = h.Second.Prepare(h.Attempt, prep.ActionId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RegisterOriginalToolActionAsync(other, null, "no replay after failed close", token));
            Assert.Equal(1, h.First.Effects);
            Assert.Equal(0, h.Second.Effects);
        }
        finally { rawClose.TrySetException(new IOException("controlled final close")); _ = await Record.ExceptionAsync(() => outward); }
    }
}
