using Haven.Core;
using HavenOS.Apps.Spaces.Tasks;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class TasksSpaceApprovalTransitionTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-25T12:00:00Z");
    private static readonly DateTimeOffset Requested = Created.AddMinutes(1);
    private static readonly DateTimeOffset Decided = Requested.AddMinutes(1);

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Unknown_decision_does_not_settle_pending_approval(int rawDecision)
    {
        var waiting = WaitingJob();
        var approval = Assert.Single(waiting.Approvals);

        Assert.Throws<ArgumentOutOfRangeException>(() => TasksSpaceStateMachine.ApplyApprovalOutcome(
            waiting, new(approval.Id, (TasksApprovalDecision)rawDecision, "unknown", Decided), Decided));

        Assert.Equal(TasksAgentJobState.WaitingForApproval, waiting.State);
        Assert.Equal(Requested, waiting.UpdatedAt);
        Assert.Same(approval, Assert.Single(waiting.Approvals));
        Assert.Equal(TasksApprovalState.Pending, approval.State);
        Assert.Null(approval.DecidedAt);
        Assert.Null(approval.DecisionReason);
    }

    [Theory]
    [InlineData(TasksApprovalDecision.Approve, TasksAgentJobState.Running, TasksApprovalState.Approved)]
    [InlineData(TasksApprovalDecision.Deny, TasksAgentJobState.Blocked, TasksApprovalState.Denied)]
    public void Explicit_decision_settles_exact_approval_once(
        TasksApprovalDecision decision, TasksAgentJobState expectedJob, TasksApprovalState expectedApproval)
    {
        var waiting = WaitingJob();
        var approval = Assert.Single(waiting.Approvals);
        var outcome = new TasksApprovalOutcome(approval.Id, decision, "observed decision", Decided);

        var settled = TasksSpaceStateMachine.ApplyApprovalOutcome(waiting, outcome, Decided);

        Assert.Equal(waiting.Id, settled.Id);
        Assert.Equal(waiting.RuntimeRunId, settled.RuntimeRunId);
        Assert.Equal(expectedJob, settled.State);
        var actual = Assert.Single(settled.Approvals);
        Assert.Equal(approval.Id, actual.Id);
        Assert.Equal(approval.JobId, actual.JobId);
        Assert.Equal(expectedApproval, actual.State);
        Assert.Equal(Decided, actual.DecidedAt);
        Assert.Equal("observed decision", actual.DecisionReason);
        Assert.Throws<InvalidOperationException>(() =>
            TasksSpaceStateMachine.ApplyApprovalOutcome(settled, outcome, Decided));
    }

    private static TasksAgentJob WaitingJob()
    {
        var running = new TasksAgentJob(Guid.NewGuid(), Guid.NewGuid(), "Agent", "Work",
            TasksAgentJobState.Running, 10, "Running", [], [], Guid.NewGuid(), null,
            Created, Created, null);
        var approval = new TasksApproval(Guid.NewGuid(), running.Id, "files.write", "Write a file",
            CapabilityRiskClass.Consequential, TasksApprovalState.Pending, Requested, null, null);
        return TasksSpaceStateMachine.RequestApproval(running, approval, Requested);
    }
}
