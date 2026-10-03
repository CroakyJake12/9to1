using Haven.Core;

namespace Haven.Application;

/// <summary>Registered owning route for a genuine opaque Home-issued admission. Definitions are observations, never grants.</summary>
public interface IWorkspaceOriginalToolDispatcher
{
    ValueTask<IReadOnlyList<OllamaToolDefinition>> GetOriginalDefinitionsAsync(object originalExecutionAuthority,
        Guid conversationId, string modelIdentity, IReadOnlyCollection<ActiveCapability> currentCapabilities,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<OllamaToolDefinition>>([]);

    ValueTask<WorkspaceToolResult> ExecuteOriginalAsync(object originalExecutionAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall originalDispatchCall, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<WorkspaceToolResult>(new UnauthorizedAccessException("The original owning route is unavailable."));
}

/// <summary>Known owning commit whose final observation failed. IDs are observations, not a receipt or authority; the registered owner verifies its exact retained original and durable journal.</summary>
public sealed class WorkspaceOriginalCommittedObservationException(string ownerKind, string operationId, Exception originalFailure)
    : Exception("The original owning operation committed; final observation failed. Do not automatically replay.", originalFailure)
{
    public string OwnerKind { get; } = ownerKind;
    public string OperationId { get; } = operationId;
}
