using Haven.Core;

namespace Haven.Application;

/// <summary>Preparation comes from the actual tool/domain owner; the request and recorded policy alone grant nothing.</summary>
public interface ITaskRunToolActionPreparation
{
    TaskRunAttemptAdmission OriginalAttempt { get; }
    Guid ActionId { get; }
    TaskActionInterruptionPolicy InterruptionPolicy { get; }
    IReadOnlyList<string> RequiredPermissionScopes { get; }
    TaskOriginalToolIntent OriginalToolIntent { get; }
}

/// <summary>Invokes the existing tool body once, retaining its original result and separate owner acceptance.</summary>
public interface ITaskRunToolActionOwner
{
    /// <summary>Deny-only availability metadata. Actual preparation, current authority and final owner fences remain required.</summary>
    bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string toolName);
    Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(
        TaskRunAttemptAdmission originalAttempt, TaskExecutionSnapshot currentSnapshot,
        Guid actionId, OllamaToolCall originalCall, ToolRuntimeKind runtime,
        PermissionMode permissionIntent, string? originalWorkspaceRoot, CancellationToken cancellationToken);

    Task<TaskRunToolActionResult> ExecuteOriginalAsync(
        ITaskRunToolActionPreparation originalPreparation,
        Func<CancellationToken, Task<WorkspaceToolResult>> originalBody,
        CancellationToken cancellationToken);

    ValueTask ValidateOriginalPreparationAsync(
        ITaskRunToolActionPreparation originalPreparation,
        TaskExecutionSnapshot currentSnapshot,
        CancellationToken cancellationToken);

    ValueTask ValidateOriginalResultAsync(
        ITaskRunToolActionPreparation originalPreparation,
        TaskRunToolActionResult originalResult,
        CancellationToken cancellationToken);

    ValueTask RetireAcknowledgedOriginalAsync(
        ITaskRunToolActionPreparation originalPreparation,
        TaskExecutionSnapshot acknowledgedSnapshot,
        CancellationToken cancellationToken);
}

/// <summary>Only a current owning-service receipt may accept a mutation. Runtime success remains an observation.</summary>
public sealed record TaskRunToolActionResult(
    WorkspaceToolResult OriginalResult,
    string? OwnerReceiptReference,
    bool ReadOnlyObservationComplete,
    bool KnownNoEffect);
