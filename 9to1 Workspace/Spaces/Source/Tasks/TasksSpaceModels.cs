using Haven.Core;

namespace HavenOS.Apps.Spaces.Tasks;

public enum TasksThreadState
{
    Draft = 0,
    Queued = 1,
    Running = 2,
    WaitingForApproval = 3,
    Blocked = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
}

public enum TasksAgentJobState
{
    Queued = 0,
    Running = 1,
    WaitingForApproval = 2,
    Blocked = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
}

public enum TasksApprovalState
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Expired = 3,
}

public enum TasksToolAccessState
{
    Available = 0,
    ApprovalRequired = 1,
    Approved = 2,
    Denied = 3,
    Unavailable = 4,
}

public enum TasksMessageAuthor
{
    User = 0,
    Haven = 1,
    System = 2,
}

public enum TasksOutputKind
{
    Summary = 0,
    File = 1,
    Link = 2,
    Artifact = 3,
}

public enum TasksApprovalDecision
{
    Approve = 0,
    Deny = 1,
}

public sealed record TasksProjectContext(
    string Name,
    string Summary,
    string? WorkspaceRoot,
    bool WorkspaceAccepted)
{
    public static TasksProjectContext None { get; } = new("No project", "No project context attached.", null, false);
}

public sealed record TasksToolAccess(
    string CapabilityKey,
    string Name,
    CapabilityRiskClass RiskClass,
    TasksToolAccessState State,
    string Explanation);

public sealed record TasksThreadMessage(
    Guid Id,
    TasksMessageAuthor Author,
    string Text,
    DateTimeOffset CreatedAt);

/// <summary>
/// An output is only presented as completed evidence after the runtime/provider has observed it.
/// A dispatch acknowledgement must be stored with <see cref="Observed"/> set to false.
/// </summary>
public sealed record TasksJobOutput(
    Guid Id,
    TasksOutputKind Kind,
    string Title,
    string Summary,
    string? ResourceReference,
    bool Observed,
    DateTimeOffset CreatedAt);

/// <summary>
/// A Tasks approval mirrors the result returned by Haven's central approval engine. This record is
/// not itself permission to execute a capability.
/// </summary>
public sealed record TasksApproval(
    Guid Id,
    Guid JobId,
    string CapabilityKey,
    string Summary,
    CapabilityRiskClass RiskClass,
    TasksApprovalState State,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionReason);

public sealed record TasksAgentJob(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string Instruction,
    TasksAgentJobState State,
    int ProgressPercent,
    string ProgressLabel,
    IReadOnlyList<TasksJobOutput> Outputs,
    IReadOnlyList<TasksApproval> Approvals,
    Guid? RuntimeRunId,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record TasksThread(
    Guid Id,
    Guid? GroupId,
    string Title,
    TasksThreadState State,
    TasksProjectContext ProjectContext,
    IReadOnlyList<TasksToolAccess> ToolAccess,
    IReadOnlyList<TasksThreadMessage> Messages,
    IReadOnlyList<TasksAgentJob> Jobs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TasksGroup(
    Guid Id,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TasksSpaceSnapshot(
    int Version,
    IReadOnlyList<TasksGroup> Groups,
    IReadOnlyList<TasksThread> Threads,
    Guid? SelectedThreadId,
    DateTimeOffset UpdatedAt)
{
    public const int CurrentVersion = 1;

    public static TasksSpaceSnapshot Empty(DateTimeOffset now) => new(CurrentVersion, [], [], null, now);
}

public sealed record TasksJobProgress(
    int Percent,
    string Label,
    IReadOnlyList<TasksJobOutput> NewOutputs);

public sealed record TasksApprovalOutcome(
    Guid ApprovalId,
    TasksApprovalDecision Decision,
    string Reason,
    DateTimeOffset ObservedAt);
