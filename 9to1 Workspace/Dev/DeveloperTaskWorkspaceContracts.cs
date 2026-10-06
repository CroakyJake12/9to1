using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Dev;

/// <summary>A reference to the existing saved project. Opening it never creates or clones a workspace.</summary>
public sealed record DeveloperProjectReference(
    Guid WorkspaceId, long WorkspaceRevision, Guid ProjectId, long ProjectRevision,
    Guid RootId, string? RepositoryBindingId = null);

/// <summary>Current saved descriptors; metadata and paths do not issue access or execution authority.</summary>
public sealed record DeveloperResolvedProject(
    DeveloperProjectReference Reference, DeveloperWorkspace Workspace, DeveloperProject Project,
    DeveloperWorkspaceRoot Root, DeveloperSourceControlBinding? Repository);

/// <summary>Observation of the SAME canonical Task/Run, with a distinct stable action identity.</summary>
public sealed record DeveloperCanonicalActionContext(
    Guid TaskId, Guid ExecutionId, Guid ContextId, Guid AttemptId, long PersistenceRevision,
    Guid ActionId, Guid? ParentActionId = null);

/// <summary>Exact reviewed edit; the shared change-set owner checks the current text hash before mutation.</summary>
public sealed record DeveloperReviewedTextEdit(DeveloperCodeDocument Document, string ExpectedSha256, string NewText);

public enum DeveloperGitOperation { Status, Diff, Branches, CreateBranch, SwitchBranch }

/// <summary>
/// The real tool observation and acknowledged canonical state are separate. A process being accepted
/// by its owner does not mean a build or test passed; its actual exit/result must be inspected.
/// </summary>
public sealed record DeveloperActionObservation(
    DeveloperProjectReference Project, DeveloperCanonicalActionContext OriginalContext,
    TaskExecutionSnapshot AcknowledgedTask, string ToolName, WorkspaceToolResult? OriginalToolResult,
    bool AlreadyAcknowledged)
{
    public TaskPlanNode? Action => AcknowledgedTask.Plan.FirstOrDefault(value => value.ActionId == OriginalContext.ActionId);
    public string? OwnerReceiptReference => Action?.Acceptance?.OwnerReceiptReference;
    public ProcessResult? OriginalProcessResult => OriginalToolResult?.OriginalProcessResult;
    public bool? ProcessSucceeded => OriginalProcessResult is { } actual ? !actual.TimedOut && actual.ExitCode == 0 : null;
    public bool KnownNoEffect { get; init; }
    public bool RequiresOutcomeInspection => !KnownNoEffect && Action?.State != TaskPlanNodeState.Completed;
}
