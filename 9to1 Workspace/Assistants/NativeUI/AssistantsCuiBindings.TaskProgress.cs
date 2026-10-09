using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private AssistantConversationBinding? _taskProgressBinding;
    private TaskExecutionSnapshot? _taskProgressSnapshot;
    private IReadOnlyList<TaskProgressRow> _taskPlanRows = [], _taskSteerRows = [], _taskQueueRows = [];
    private FollowUpDecision? _taskFollowUpDecision;
    private bool _taskFollowUpBusy;

    internal void SetTaskFollowUpBusy(bool value) { _taskFollowUpBusy = value; Refresh(); }

    internal void PublishOriginalFollowUpDecision(AssistantConversationBinding binding, FollowUpDecision actual)
    {
        if (!Current || !ReferenceEquals(_snapshot.ConversationBinding, binding) ||
            _snapshot.Work?.CanonicalTask is not { } current || !SameTask(current, actual.Snapshot) ||
            current.PersistenceRevision < actual.Snapshot.PersistenceRevision) return;
        _taskFollowUpDecision = actual; Refresh();
    }

    private static bool SameTask(TaskExecutionSnapshot left, TaskExecutionSnapshot right) =>
        left.TaskId == right.TaskId && left.ContextId == right.ContextId && left.ExecutionId == right.ExecutionId;

    private void RefreshTaskProgress()
    {
        RefreshColdTaskRecovery();
        var binding = _snapshot.ConversationBinding;
        var actual = Current && binding is not null && _snapshot.Work?.CanonicalTask is { } task &&
            task.ContextId == binding.Conversation.Id ? task : null;
        if (!ReferenceEquals(_taskProgressBinding, binding))
        {
            _taskProgressBinding = binding; _taskFollowUpDecision = null; _taskFollowUpBusy = false;
        }
        if (!ReferenceEquals(_taskProgressSnapshot, actual))
        {
            _taskProgressSnapshot = actual;
            _taskPlanRows = actual is null ? [] : Array.AsReadOnly(actual.Plan.Select(node => new TaskProgressRow(
                node.ActionId, node.Summary, PlanState(node.State))).ToArray());
            _taskSteerRows = actual is null ? [] : Array.AsReadOnly(actual.Steers.OrderBy(item => item.Sequence)
                .Select(item => new TaskProgressRow(item.Id, item.Summary, SteerState(item.State))).ToArray());
            _taskQueueRows = actual is null ? [] : Array.AsReadOnly(actual.Queue.OrderBy(item => item.Position)
                .Select(item => new TaskProgressRow(item.TaskId, item.Summary, QueueState(item.State))).ToArray());
        }
        if (actual is null || _taskFollowUpDecision is { } decision && !SameTask(actual, decision.Snapshot))
            _taskFollowUpDecision = null;
        Set("HasTaskProgress", actual is not null);
        Set("TaskObjective", actual?.PromptSummary ?? "");
        Set("TaskExecutionStatus", actual is null ? "" : ExecutionState(actual.State));
        Set("TaskCurrentActivity", actual?.Plan.LastOrDefault(node => node.State is TaskPlanNodeState.Running or TaskPlanNodeState.WaitingSafeBoundary)?.Summary
            ?? "No action is currently running.");
        Set("TaskCheckpointStatus", actual?.CheckpointId is not null ? "A recovery checkpoint is saved."
            : "No recovery checkpoint has been saved.");
        Set("TaskAcceptanceStatus", actual is null ? "" : $"{TaskExecutionProjection.From(actual).AcceptedActions.Count} steps accepted. Overall verification is separate.");
        Set("TaskRecoveryStatus", actual is null ? "" : _snapshot.Work?.RequiresOwnerRenewal == true
            ? "Review recovery to continue this Task in your current session."
            : actual.RecoveryObservation is not null
            ? "The previous attempt needs review before work can resume."
            : _snapshot.Work?.Controls is { } controls && (controls.CanResumeUnstartedOriginal || controls.CanResumeOriginalToolCheckpoint)
                ? "This saved Task can resume using Resume work."
                : actual.Attempts.Count == 0 ? "No model attempt has started." : "No recovery review is currently recorded.");
        Set("TaskPlanRows", _taskPlanRows); Set("TaskSteerRows", _taskSteerRows); Set("TaskQueueRows", _taskQueueRows);
        Set("HasTaskPlan", _taskPlanRows.Count != 0); Set("HasTaskSteers", _taskSteerRows.Count != 0);
        Set("HasTaskQueue", _taskQueueRows.Count != 0);
        Set("TaskFollowUpResult", _taskFollowUpDecision is { } result ? DescribeFollowUp(result) : "");
        Set("HasTaskFollowUpResult", _taskFollowUpDecision is not null);
    }

    private bool TryGetTaskProgressItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not TaskProgressRow row || !ContainsOriginalRow(row, "TaskPlanRows", "TaskSteerRows", "TaskQueueRows")) return false;
        value = path switch { "Id" => row.Id, "Summary" => row.Summary, "Status" => row.Status, _ => null };
        return path is "Id" or "Summary" or "Status";
    }

    private static string DescribeFollowUp(FollowUpDecision actual) => actual.Mode == TaskFollowUpMode.Queue
        ? "Follow-up queued: " + actual.QueuedTask?.Summary
        : actual.RequiresApproval ? "Steering saved; new permission is required."
        : actual.WaitingForSafeBoundary ? "Steering saved; waiting for the current action to reach a safe stopping point."
        : "Steering applied: " + actual.Steer?.Summary;

    private static string ExecutionState(TaskExecutionLifecycle state) => state switch
    {
        TaskExecutionLifecycle.Running => "Working", TaskExecutionLifecycle.WaitingSafeBoundary => "Waiting for a safe stopping point",
        TaskExecutionLifecycle.Blocked => "Blocked", TaskExecutionLifecycle.Suspended => "Paused",
        TaskExecutionLifecycle.Completed => "Completed", TaskExecutionLifecycle.Failed => "Failed",
        TaskExecutionLifecycle.Cancelled => "Stopped", _ => "Unknown state"
    };
    private static string PlanState(TaskPlanNodeState state) => state switch
    {
        TaskPlanNodeState.Pending => "Pending", TaskPlanNodeState.Running => "Working",
        TaskPlanNodeState.WaitingSafeBoundary => "Waiting for a safe stopping point", TaskPlanNodeState.Blocked => "Blocked",
        TaskPlanNodeState.Completed => "Completed", TaskPlanNodeState.Failed => "Failed", TaskPlanNodeState.Cancelled => "Stopped",
        TaskPlanNodeState.Superseded => "Replaced", TaskPlanNodeState.RequiresReexecution => "Needs review before retry", _ => "Unknown state"
    };
    private static string SteerState(SteerInstructionState state) => state switch
    {
        SteerInstructionState.Pending => "Pending", SteerInstructionState.WaitingSafeBoundary => "Waiting for a safe stopping point",
        SteerInstructionState.Applied => "Applied", SteerInstructionState.Superseded => "Replaced",
        SteerInstructionState.Blocked => "Blocked", _ => "Unknown state"
    };
    private static string QueueState(QueuedFollowUpState state) => state switch
    {
        QueuedFollowUpState.Queued => "Queued", QueuedFollowUpState.Ready => "Ready", QueuedFollowUpState.Running => "Working",
        QueuedFollowUpState.Blocked => "Blocked", QueuedFollowUpState.Completed => "Completed", QueuedFollowUpState.Failed => "Failed",
        QueuedFollowUpState.Cancelled => "Stopped", QueuedFollowUpState.PromotedToSteer => "Moved to current Task", _ => "Unknown state"
    };
    internal sealed class TaskProgressRow(Guid id, string summary, string status)
    { public Guid Id { get; } = id; public string Summary { get; } = summary; public string Status { get; } = status; }
}
