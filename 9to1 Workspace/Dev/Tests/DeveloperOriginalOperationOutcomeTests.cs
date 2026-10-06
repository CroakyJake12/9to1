using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Maintained Dev/coordinator/runtime; synthetic issuer and process provider. These observations
// do not certify real process execution, durable SQLite, Home approval or kernel effect custody.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [Fact]
    public async Task Known_failed_process_is_conserved_as_completed_request_with_failed_business_outcome()
    {
        var f = await Fixture.CreateAsync();
        f.Tools.Process = new(7, "actual reported test failure", "", TimeSpan.FromMilliseconds(3), false);
        var context = f.Context();
        var result = await f.Dev.RunTestsAsync(f.Reference, context, "dotnet test");
        Assert.True(result.Succeeded);
        Assert.Same(f.Tools.Process, result.Value!.OriginalProcessResult);
        Assert.False(result.Value.ProcessSucceeded);
        var actual = f.Current.Plan.Single(value => value.ActionId == context.ActionId);
        Assert.Equal(TaskPlanNodeState.Completed, actual.State);
        Assert.NotNull(actual.Acceptance);
        var outcome = Assert.IsType<TaskActionOperationOutcome>(actual.OriginalOperationOutcome);
        Assert.Equal(true, outcome.RequestedOperationCompleted);
        Assert.Equal(false, outcome.BusinessSucceeded);
        Assert.Equal(7, outcome.ProcessExitCode);
        Assert.Equal(false, outcome.ProcessTimedOut);
        // Reopen the already accepted SAME ActionId via the maintained service. The request
        // completion does not permit test replay and does not turn the saved exit into PASS.
        var reopened = await f.Dev.RunTestsAsync(f.Reference, context, "dotnet test");
        Assert.True(reopened.Value!.AlreadyAcknowledged);
        Assert.Null(reopened.Value.OriginalProcessResult);
        Assert.Same(outcome, reopened.Value.Action!.OriginalOperationOutcome);
        Assert.Equal(1, f.Owner.ExecuteCalls);
        Assert.Equal(1, f.Tools.ProcessCalls);
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Completed_source_read_records_observation_without_a_mutation_acceptance_or_process_result()
    {
        var f = await Fixture.CreateAsync();
        var context = f.Context();
        var result = await f.Dev.ReadFileAsync(f.Reference, context, f.Document);
        Assert.True(result.Succeeded);
        var actual = f.Current.Plan.Single(value => value.ActionId == context.ActionId);
        Assert.Equal(TaskPlanNodeState.Completed, actual.State);
        Assert.Null(actual.Acceptance);
        Assert.Null(result.Value!.OriginalProcessResult);
        var outcome = Assert.IsType<TaskActionOperationOutcome>(actual.OriginalOperationOutcome);
        Assert.Equal(true, outcome.RequestedOperationCompleted);
        Assert.Equal(true, outcome.BusinessSucceeded);
        Assert.Null(outcome.ProcessExitCode);
        Assert.Null(outcome.ProcessTimedOut);
        Assert.Equal(context.TaskId, result.Value.AcknowledgedTask.TaskId);
        Assert.Equal(context.ExecutionId, result.Value.AcknowledgedTask.ExecutionId);
        Assert.Equal(context.ContextId, result.Value.AcknowledgedTask.ContextId);
        Assert.Equal(1, f.Tools.ReadCalls);
        Assert.Equal(0, f.Tools.ProcessCalls);
        await f.Dev.CloseAndDrainAsync();
    }
}
