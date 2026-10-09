using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Detached durable observations. These values issue no old receipt, Task, lease,
/// permission or replay grant. Only the protected journal's private entry/claim validates provenance.</summary>
public enum TaskRunColdClosedActionKind { OriginalOwnedTool = 1, SuccessfulModelResponse = 2 }
public sealed record TaskRunColdClosedAction(TaskRunColdClosedActionKind Kind, TaskPlanNode OriginalNode);
public sealed record TaskRunColdFailedProviderInvocation(Guid OriginalAttemptId, string ProviderId,
    string InspectedMethod, Guid OriginalObservedAttemptId, IReadOnlyList<string> OriginalCauseTypes);

/// <summary>The exact unfinished model turn after accepted tools. Accepted calls and outputs
/// remain in the original transcript; recovery executes the next response, never those tools.</summary>
public sealed record TaskRunColdToolCheckpoint(
    OllamaToolRequest OriginalNextRequest, TaskRunContextInventory OriginalInventory,
    Guid AssistantId, string AssistantText, IReadOnlyList<ToolActivity> Activities,
    int CallsUsed, int ToolLimit, OllamaToolCall? LastCall, WorkspaceToolResult? LastResult,
    IReadOnlyDictionary<string, ToolRuntimeKind> RuntimeByName, string OriginalControlFingerprint,
    Guid OriginalUnfinishedResponseActionId, TaskRunRecoveryObservation OriginalFailure,
    IReadOnlyList<TaskRunColdClosedAction> OriginalClosedActions,
    IReadOnlyList<TaskRunColdFailedProviderInvocation> OriginalFailedInvocations);

/// <summary>Mandatory additional validation for an invoked boundary. Interface presence is
/// not authority; the SAME configured protected producer must validate its private claim.</summary>
public interface ITaskRunColdAcceptedBoundarySource
{
    Task ValidateOriginalAcceptedBoundaryWithinSourceAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot actualExpected, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>Fresh local selection observation after authentic cold activation. No old failed
/// attempt or selection receipt is rebuilt. Normal fresh Start/admission still owns permissions.</summary>
public interface ITaskRunColdToolCheckpointSelectionSource
{
    Task<ITaskRunColdToolCheckpointSelection?> SelectLocalForColdOriginalToolCheckpointAsync(
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, TaskExecutionSnapshot actualCurrent,
        TaskRunColdOriginalSourceScope currentSources, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalColdToolCheckpointSelection(ITaskRunColdToolCheckpointSelection sameSelection,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, TaskExecutionSnapshot sameCurrent);
}
public interface ITaskRunColdToolCheckpointSelection
{
    ITaskRunColdJournalAcknowledgment OriginalAcknowledgment { get; }
    TaskExecutionSnapshot ActualCurrentSnapshot { get; }
    ProviderModelDescriptor ActualSelectedModel { get; }
    TaskRunRouteCandidate ActualSelectedCandidate { get; }
    Task OriginalSelectionTask { get; }
    IReadOnlyList<Task> OriginalSelectionSources { get; }
}

public static partial class TaskRunColdRecoveryBoundary
{
    /// <summary>Material consistency only, after private journal provenance validation.
    /// A public capsule/status/copy can never call this method to acquire authority.</summary>
    public static void DemandRestorableBoundary(TaskRunColdCapsule capsule, TaskExecutionSnapshot actual)
    {
        if (capsule.Boundary == TaskRunColdBoundaryKind.NeverStartedAcceptedInput)
        { DemandNeverStarted(capsule, actual); return; }
        var tool = capsule.OriginalToolCheckpoint;
        if (capsule.SchemaVersion != 2 || capsule.CapsuleId == Guid.Empty
            || capsule.Boundary != TaskRunColdBoundaryKind.SettledUnfinishedToolResponse || tool is null
            || JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(capsule.AcknowledgedTask)
            || actual.State != TaskExecutionLifecycle.Suspended || actual.OwnerBinding is null
            || actual.ParentDelegation is not null || actual.Delegations.Count != 0
            || capsule.AcceptedConversation.IsTemporary || capsule.AcceptedConversation.Mode != HavenMode.Tasks
            || capsule.AcceptedConversation.Id != actual.ContextId
            || capsule.AcceptedUserMessage.ConversationId != actual.ContextId
            || capsule.AcceptedUserMessage.Role != MessageRole.User
            || capsule.AcceptedUserMessage.Content != capsule.OriginalInput.Prompt
            || capsule.OriginalInput.Conversation.Id != actual.ContextId
            || tool.AssistantId == Guid.Empty || tool.CallsUsed < 0 || tool.CallsUsed >= tool.ToolLimit
            || tool.ToolLimit is < 1 or > 100 || tool.OriginalFailedInvocations.Count is < 1 or > 128
            || tool.OriginalClosedActions.Count > 256 || tool.OriginalNextRequest.Messages.Count > 1024
            || tool.OriginalNextRequest.Tools.Count > 256
            || tool.OriginalNextRequest.ExecutionContext is not { } context
            || context.TaskId != actual.TaskId || context.ContextId != actual.ContextId
            || context.ExecutionId != actual.ExecutionId || context.ActionId != tool.OriginalUnfinishedResponseActionId
            || tool.OriginalUnfinishedResponseActionId == Guid.Empty
            || JsonSerializer.Serialize(actual.RecoveryObservation) != JsonSerializer.Serialize(tool.OriginalFailure)
            || tool.OriginalFailure.SettlementOutcome != TaskRunOriginalSettlementOutcome.Joined
            || tool.OriginalControlFingerprint != ChatSessionService.OriginalToolCheckpointControlFingerprint(actual))
            throw new InvalidOperationException("The authenticated unfinished response material is not an exact settled same-run boundary.");
        var unfinished = actual.Plan.SingleOrDefault(node => node.ActionId == tool.OriginalUnfinishedResponseActionId);
        if (unfinished is not { State: TaskPlanNodeState.Running, InterruptionPolicy: TaskActionInterruptionPolicy.ReadOnlyCancellable,
                Acceptance: null, OriginalToolIntent: null, OriginalOperationOutcome: null }
            || tool.OriginalClosedActions.Select(value => value.OriginalNode.ActionId).Distinct().Count() != tool.OriginalClosedActions.Count)
            throw new InvalidOperationException("Only the exact unresolved response may continue; accepted effects remain closed.");
        foreach (var node in actual.Plan)
        {
            if (node.ActionId == unfinished.ActionId) continue;
            if (node.State == TaskPlanNodeState.Superseded) continue;
            var closed = tool.OriginalClosedActions.SingleOrDefault(value => value.OriginalNode.ActionId == node.ActionId);
            if (node.State != TaskPlanNodeState.Completed || closed is null
                || JsonSerializer.Serialize(node) != JsonSerializer.Serialize(closed.OriginalNode)
                || closed.Kind == TaskRunColdClosedActionKind.OriginalOwnedTool
                    && node.OriginalOperationOutcome?.RequestedOperationCompleted != true
                || closed.Kind == TaskRunColdClosedActionKind.SuccessfulModelResponse
                    && (node.InterruptionPolicy != TaskActionInterruptionPolicy.ReadOnlyCancellable
                        || node.Acceptance is not null || node.OriginalToolIntent is not null)
                || closed.Kind is not (TaskRunColdClosedActionKind.OriginalOwnedTool or TaskRunColdClosedActionKind.SuccessfulModelResponse))
                throw new InvalidOperationException("An action has no authenticated owning closed-outcome provenance; automatic replay is refused.");
        }
        if (tool.OriginalFailedInvocations.Any(value => !actual.Attempts.Any(attempt => attempt.Id == value.OriginalAttemptId)
            || value.OriginalObservedAttemptId != value.OriginalAttemptId || string.IsNullOrWhiteSpace(value.ProviderId)
            || string.IsNullOrWhiteSpace(value.InspectedMethod) || value.OriginalCauseTypes.Count == 0))
            throw new InvalidOperationException("An original failed invocation is absent from the conserved attempt history.");
        // These resource kinds require their own fresh domain/current-root authority.
        // A captured path, container identifier or previous permission is not that proof.
        if (capsule.OriginalInput.WorkspaceRoot is not null || capsule.OriginalInput.ProjectContext is not null
            || capsule.OriginalInput.ProjectInstructions is not null || capsule.OriginalInput.RegisteredContext is not null || capsule.OriginalInput.ComputerUseRequest is not null
            || capsule.OriginalInput.Images is { Count: > 0 } || capsule.AcceptedConversation.ContainerId is not null
            || capsule.AcceptedConversation.LessonId is not null)
        {
            if (capsule.OriginalProjectIdentity is null)
                throw new InvalidOperationException("This cold boundary has no fresh owner for its additional context resources.");
            TaskRunColdProjectBoundary.DemandOriginalProjectMaterial(capsule);
        }
        else if (capsule.OriginalProjectIdentity is not null)
            throw new InvalidOperationException("A project descriptor cannot replace the original resource-free input.");
    }
}
