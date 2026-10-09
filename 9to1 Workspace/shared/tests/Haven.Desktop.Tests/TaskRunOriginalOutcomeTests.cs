using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual original action/receipt/result/CAS conservation with explicitly controlled
/// owner and process observations. This fixture does not execute a process or certify test PASS.</summary>
public sealed partial class TaskRunOriginalActionAdmissionTests
{
    private static async Task<TaskRunToolActionResult> CompleteControlledOriginalProcessAsync(
        Harness actual, Preparation preparation, int exit, bool timedOut, CancellationToken token)
    {
        var claim = actual.Coordinator.RequireOriginalActionAdmission(preparation, actual.Attempt);
        await actual.Coordinator.ValidateOriginalActionAdmissionAsync(claim, preparation, actual.Attempt, token);
        actual.Coordinator.DemandOriginalActionAdmission(claim, preparation, actual.Attempt);
        if (Interlocked.CompareExchange(ref preparation.Started, 1, 0) != 0)
            throw new InvalidOperationException("The controlled original cannot replay its requested operation.");
        Interlocked.Increment(ref preparation.Owner.Effects);
        var raw = new WorkspaceToolResult(new(Guid.NewGuid(), "controlled once-completed process", "controlled observation",
            true, TimeSpan.Zero, DateTimeOffset.UtcNow), "controlled exact process output")
        { OriginalEffectBodyCompleted = true, OriginalProcessResult = new(exit, "controlled stdout", "controlled stderr", TimeSpan.Zero, timedOut) };
        var receipt = "controlled private original result " + Guid.NewGuid();
        actual.Authority.AcceptedReceipt = receipt;
        return preparation.Result = new(raw, receipt, false, false);
    }

    [Fact]
    public async Task Accepted_exit_seven_preserves_same_action_receipt_checkpoint_and_failed_business_outcome_without_replay()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "failed command observation", token);
        var result = await CompleteControlledOriginalProcessAsync(h, preparation, 7, false, token);
        var acknowledged = await h.Coordinator.RecordOriginalToolActionOutcomeAsync(preparation, result, token);
        var node = Assert.Single(acknowledged.Plan);
        Assert.Equal(TaskPlanNodeState.Completed, node.State); // Request completion conserves the accepted replay fence.
        Assert.Equal(result.OwnerReceiptReference, node.Acceptance!.OwnerReceiptReference);
        Assert.Equal(h.Attempt.AttemptId, node.Acceptance.AttemptId);
        Assert.Equal(preparation.ActionId, acknowledged.LastCheckpointActionId);
        var observed = Assert.IsType<TaskActionOperationOutcome>(node.OriginalOperationOutcome);
        Assert.True(observed.RequestedOperationCompleted);
        Assert.False(observed.BusinessSucceeded);
        Assert.Equal(7, observed.ProcessExitCode);
        Assert.False(observed.ProcessTimedOut);
        Assert.True(result.OriginalResult.Activity.Succeeded); // Generic activity truth cannot turn exit7 into business success.
        var saved = (await h.Rows.GetAsync(h.Task.TaskId, token))!;
        Assert.Equal(observed, Assert.Single(saved.Plan).OriginalOperationOutcome);
        Assert.Equal(h.Task.TaskId, saved.TaskId);
        Assert.Equal(h.Task.ContextId, saved.ContextId);
        Assert.Equal(h.Task.ExecutionId, saved.ExecutionId);
        Assert.Equal(acknowledged.PersistenceRevision, saved.PersistenceRevision);
        await h.Coordinator.RetireAcknowledgedToolOriginalAsync(preparation, acknowledged);
        var replay = h.Second.Prepare(h.Attempt, preparation.ActionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RegisterOriginalToolActionAsync(replay, null, "failed command observation", token));
        Assert.Equal(1, h.First.Effects);
        Assert.Equal(0, h.Second.Effects);
        Assert.Equal(1, h.First.Retirements);
        Assert.Equal(observed, Assert.Single((await h.Rows.GetAsync(h.Task.TaskId, token))!.Plan).OriginalOperationOutcome);
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(-1, true, false)]
    public async Task Original_process_observation_requires_zero_exit_and_no_timeout_for_business_success(int exit, bool timedOut, bool expected)
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "process result observation", token);
        var result = await CompleteControlledOriginalProcessAsync(h, preparation, exit, timedOut, token);
        var saved = await h.Coordinator.RecordOriginalToolActionOutcomeAsync(preparation, result, token);
        var node = Assert.Single(saved.Plan);
        Assert.Equal(TaskPlanNodeState.Completed, node.State);
        Assert.True(node.OriginalOperationOutcome!.RequestedOperationCompleted);
        Assert.Equal(expected, node.OriginalOperationOutcome.BusinessSucceeded);
        Assert.Equal(exit, node.OriginalOperationOutcome.ProcessExitCode);
        Assert.Equal(timedOut, node.OriginalOperationOutcome.ProcessTimedOut);
        Assert.Equal(result.OwnerReceiptReference, node.Acceptance!.OwnerReceiptReference);
        Assert.Equal(1, h.First.Effects);
    }

    [Fact]
    public async Task Copied_public_result_cannot_supply_a_successful_outcome_or_receipt_for_the_original_failed_operation()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid());
        var registered = await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "copy denied", token);
        var original = await CompleteControlledOriginalProcessAsync(h, preparation, 7, false, token);
        var copied = original with { OriginalResult = original.OriginalResult with { OriginalProcessResult = new(0, "copied success", "", TimeSpan.Zero, false) } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RecordOriginalToolActionOutcomeAsync(preparation, copied, token));
        var unchanged = (await h.Rows.GetAsync(h.Task.TaskId, token))!;
        Assert.Equal(registered.PersistenceRevision, unchanged.PersistenceRevision);
        Assert.Null(Assert.Single(unchanged.Plan).Acceptance);
        Assert.Null(Assert.Single(unchanged.Plan).OriginalOperationOutcome);
        Assert.Equal(TaskPlanNodeState.Running, Assert.Single(unchanged.Plan).State);
        var acknowledged = await h.Coordinator.RecordOriginalToolActionOutcomeAsync(preparation, original, token);
        Assert.False(Assert.Single(acknowledged.Plan).OriginalOperationOutcome!.BusinessSucceeded);
        Assert.Equal(7, Assert.Single(acknowledged.Plan).OriginalOperationOutcome!.ProcessExitCode);
        Assert.Equal(1, h.First.Effects);
    }

    [Fact]
    public async Task Receipt_only_legacy_acceptance_preserves_unknown_business_outcome_and_original_numeric_state()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "legacy receipt", token);
        var result = await CompleteControlledOriginalProcessAsync(h, preparation, 7, false, token);
        var saved = await h.Coordinator.AcceptActionAsync(h.Task.TaskId, h.Task.ExecutionId, h.Attempt.AttemptId,
            preparation.ActionId, result.OwnerReceiptReference!, token);
        var node = Assert.Single(saved.Plan);
        Assert.Equal(TaskPlanNodeState.Completed, node.State);
        Assert.Equal(result.OwnerReceiptReference, node.Acceptance!.OwnerReceiptReference);
        Assert.Null(node.OriginalOperationOutcome);
        Assert.Equal(4, (int)node.State);
        var roundtrip = JsonSerializer.Deserialize<TaskExecutionSnapshot>(JsonSerializer.Serialize(saved))!;
        Assert.Null(Assert.Single(roundtrip.Plan).OriginalOperationOutcome);
        Assert.Equal(saved.PersistenceRevision, roundtrip.PersistenceRevision);
        Assert.Equal(h.Task.ExecutionId, roundtrip.ExecutionId);
    }

    [Fact]
    public async Task Faulted_acknowledgment_retains_raw_siblings_and_known_failed_outcome_without_acceptance_retry_or_second_effect()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "unknown outcome ACK", token);
        var result = await CompleteControlledOriginalProcessAsync(h, preparation, 7, false, token);
        var oce = new OperationCanceledException("actual faulted outcome write");
        var io = new IOException("actual outcome write sibling");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([oce, io]);
        h.Rows.NextWriteOutcome = raw.Task;
        var driver = h.Coordinator.RecordOriginalToolActionOutcomeAsync(preparation, result, token);
        var failure = await Record.ExceptionAsync(() => driver);
        Assert.NotNull(failure);
        Assert.True(driver.IsFaulted);
        Assert.False(driver.IsCanceled);
        Assert.Contains(Causes(failure!), error => ReferenceEquals(error, oce));
        Assert.Contains(Causes(failure!), error => ReferenceEquals(error, io));
        var stages = (System.Collections.IEnumerable)typeof(TaskExecutionCoordinator).GetField("_originalProcessStages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(h.Coordinator)!;
        Assert.Contains(stages.Cast<object>().SelectMany(stage => (IReadOnlyList<Task>)stage.GetType().GetProperty("ActualSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stage)!), source => ReferenceEquals(source, raw.Task));
        var saved = (await h.Rows.GetAsync(h.Task.TaskId, token))!; // Controlled repository committed before returning its faulted actual Task.
        var node = Assert.Single(saved.Plan);
        Assert.Equal(result.OwnerReceiptReference, node.Acceptance!.OwnerReceiptReference);
        Assert.False(node.OriginalOperationOutcome!.BusinessSucceeded);
        Assert.Equal(7, node.OriginalOperationOutcome.ProcessExitCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RecordOriginalToolActionOutcomeAsync(preparation, result, token));
        var replay = h.Second.Prepare(h.Attempt, preparation.ActionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RegisterOriginalToolActionAsync(replay, null, "unknown outcome ACK", token));
        Assert.Equal(1, h.First.Effects);
        Assert.Equal(0, h.Second.Effects);
        Assert.Equal(0, h.First.Retirements);
        Assert.Equal(h.Task.TaskId, saved.TaskId);
        Assert.Equal(h.Task.ExecutionId, saved.ExecutionId);
    }
}
