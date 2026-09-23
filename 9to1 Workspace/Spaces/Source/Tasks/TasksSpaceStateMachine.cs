namespace HavenOS.Apps.Spaces.Tasks;

/// <summary>
/// The single transition table for Tasks job state. Runtime adapters report observed outcomes into
/// this type; presentation code cannot mutate jobs or approvals directly.
/// </summary>
public static class TasksSpaceStateMachine
{
    public static TasksAgentJob Start(TasksAgentJob job, Guid runtimeRunId, DateTimeOffset now)
    {
        RequireState(job, TasksAgentJobState.Queued, "start");
        if (runtimeRunId == Guid.Empty) throw new ArgumentException("A runtime run ID is required.", nameof(runtimeRunId));
        return job with
        {
            State = TasksAgentJobState.Running,
            RuntimeRunId = runtimeRunId,
            ProgressLabel = "Starting",
            UpdatedAt = now,
        };
    }

    public static TasksAgentJob ReportProgress(TasksAgentJob job, TasksJobProgress progress, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(progress);
        RequireState(job, TasksAgentJobState.Running, "report progress for");
        if (progress.Percent is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(progress), "Active job progress must be between 0 and 99.");
        if (progress.Percent < job.ProgressPercent)
            throw new InvalidOperationException("Job progress cannot move backwards.");
        var label = RequiredText(progress.Label, nameof(progress.Label), 160);
        var additions = progress.NewOutputs ?? throw new ArgumentException("Progress outputs are required.", nameof(progress));
        EnsureUniqueOutputs(job.Outputs, additions);
        return job with
        {
            ProgressPercent = progress.Percent,
            ProgressLabel = label,
            Outputs = [.. job.Outputs, .. additions],
            UpdatedAt = now,
        };
    }

    public static TasksAgentJob RequestApproval(TasksAgentJob job, TasksApproval approval, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);
        RequireState(job, TasksAgentJobState.Running, "request approval for");
        if (approval.JobId != job.Id) throw new ArgumentException("Approval does not belong to this job.", nameof(approval));
        if (approval.State != TasksApprovalState.Pending)
            throw new ArgumentException("A new approval must be pending.", nameof(approval));
        if (job.Approvals.Any(item => item.Id == approval.Id))
            throw new InvalidOperationException("The approval has already been recorded.");
        return job with
        {
            State = TasksAgentJobState.WaitingForApproval,
            ProgressLabel = "Waiting for your approval",
            Approvals = [.. job.Approvals, approval],
            UpdatedAt = now,
        };
    }

    public static TasksAgentJob ApplyApprovalOutcome(
        TasksAgentJob job,
        TasksApprovalOutcome outcome,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        RequireState(job, TasksAgentJobState.WaitingForApproval, "resolve approval for");
        var approval = job.Approvals.FirstOrDefault(item => item.Id == outcome.ApprovalId)
            ?? throw new KeyNotFoundException("Approval was not found on this job.");
        if (approval.State != TasksApprovalState.Pending)
            throw new InvalidOperationException("Only a pending approval can be resolved.");
        var nextApprovalState = outcome.Decision == TasksApprovalDecision.Approve
            ? TasksApprovalState.Approved
            : TasksApprovalState.Denied;
        var approvals = job.Approvals.Select(item => item.Id == approval.Id
            ? item with
            {
                State = nextApprovalState,
                DecidedAt = outcome.ObservedAt,
                DecisionReason = RequiredText(outcome.Reason, nameof(outcome.Reason), 500),
            }
            : item).ToArray();
        return job with
        {
            State = outcome.Decision == TasksApprovalDecision.Approve
                ? TasksAgentJobState.Running
                : TasksAgentJobState.Blocked,
            ProgressLabel = outcome.Decision == TasksApprovalDecision.Approve
                ? "Continuing"
                : "Blocked because access was not approved",
            Approvals = approvals,
            UpdatedAt = now,
        };
    }

    public static TasksAgentJob Complete(
        TasksAgentJob job,
        IReadOnlyList<TasksJobOutput> finalOutputs,
        DateTimeOffset now)
    {
        RequireState(job, TasksAgentJobState.Running, "complete");
        ArgumentNullException.ThrowIfNull(finalOutputs);
        EnsureUniqueOutputs(job.Outputs, finalOutputs);
        var allOutputs = job.Outputs.Concat(finalOutputs).ToArray();
        if (allOutputs.Length == 0 || allOutputs.Any(output => !output.Observed))
            throw new InvalidOperationException("A completed job requires at least one observed output and cannot include unobserved output claims.");
        return job with
        {
            State = TasksAgentJobState.Completed,
            ProgressPercent = 100,
            ProgressLabel = "Completed",
            Outputs = allOutputs,
            Error = null,
            UpdatedAt = now,
            CompletedAt = now,
        };
    }

    public static TasksAgentJob Fail(TasksAgentJob job, string error, DateTimeOffset now)
    {
        RequireNonTerminal(job, "fail");
        return job with
        {
            State = TasksAgentJobState.Failed,
            ProgressLabel = "Needs attention",
            Error = RequiredText(error, nameof(error), 1000),
            UpdatedAt = now,
            CompletedAt = now,
        };
    }

    public static TasksAgentJob Cancel(TasksAgentJob job, DateTimeOffset now)
    {
        RequireNonTerminal(job, "cancel");
        return job with
        {
            State = TasksAgentJobState.Cancelled,
            ProgressLabel = "Cancelled",
            UpdatedAt = now,
            CompletedAt = now,
        };
    }

    public static TasksThread RecalculateThread(TasksThread thread, DateTimeOffset now)
    {
        var state = thread.Jobs.Count == 0
            ? TasksThreadState.Draft
            : thread.Jobs.Any(job => job.State == TasksAgentJobState.WaitingForApproval)
                ? TasksThreadState.WaitingForApproval
                : thread.Jobs.Any(job => job.State == TasksAgentJobState.Running)
                    ? TasksThreadState.Running
                    : thread.Jobs.Any(job => job.State == TasksAgentJobState.Queued)
                        ? TasksThreadState.Queued
                        : thread.Jobs.Any(job => job.State == TasksAgentJobState.Blocked)
                            ? TasksThreadState.Blocked
                            : thread.Jobs.Any(job => job.State == TasksAgentJobState.Failed)
                                ? TasksThreadState.Failed
                                : thread.Jobs.All(job => job.State == TasksAgentJobState.Cancelled)
                                    ? TasksThreadState.Cancelled
                                    : TasksThreadState.Completed;
        return thread with { State = state, UpdatedAt = now };
    }

    internal static string RequiredText(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength) throw new ArgumentException($"Value must be at most {maximumLength} characters.", parameterName);
        return trimmed;
    }

    private static void EnsureUniqueOutputs(
        IReadOnlyList<TasksJobOutput> existing,
        IReadOnlyList<TasksJobOutput> additions)
    {
        if (additions.Any(output => output is null)) throw new ArgumentException("Outputs cannot contain null entries.", nameof(additions));
        var ids = existing.Select(output => output.Id).ToHashSet();
        foreach (var output in additions)
        {
            if (output.Id == Guid.Empty) throw new ArgumentException("Output IDs cannot be empty.", nameof(additions));
            if (!ids.Add(output.Id)) throw new InvalidOperationException("Output IDs must be unique within a job.");
        }
    }

    private static void RequireState(TasksAgentJob job, TasksAgentJobState expected, string operation)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.State != expected)
            throw new InvalidOperationException($"Cannot {operation} a job in state {job.State}; expected {expected}.");
    }

    private static void RequireNonTerminal(TasksAgentJob job, string operation)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.State is TasksAgentJobState.Completed or TasksAgentJobState.Failed or TasksAgentJobState.Cancelled)
            throw new InvalidOperationException($"Cannot {operation} a terminal job in state {job.State}.");
    }
}
