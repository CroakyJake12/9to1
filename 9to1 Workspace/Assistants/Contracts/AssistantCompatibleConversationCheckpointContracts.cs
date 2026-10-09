using HavenOS.Apps.Dev;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Assistants.Contracts;

public enum AssistantCompatibleConversationCreationState { Ready, DeclinedPending }

/// <summary>An acknowledged durable Den checkpoint. The planned conversation does
/// not become a canonical binding until the actual create-only SQL pair and Ready
/// publication acknowledge. IDs, metadata and this object grant no resource access.</summary>
public sealed class AssistantCompatibleConversationCheckpoint : ICanonicalProjectTaskContextResumeCheckpoint
{
    internal AssistantCompatibleConversationCheckpoint(object actualIssuer, AssistantIdentity identity,
        long definitionRevision, string denSessionId, long membershipRevision,
        Guid plannedConversationId, Guid originalCreationOperationId, Guid lastCommandOperationId,
        string title, DeveloperProjectReference project, Guid originalStudioConversationId,
        Guid originalStudioContainerId, ResourceStoreIdentity originalStoreIdentity,
        Conversation originalTaskConversation, ContainerDefinition originalTaskContainer, string reason)
    {
        Issuer = actualIssuer; Identity = identity; DefinitionRevision = definitionRevision;
        DenSessionId = denSessionId; MembershipRevision = membershipRevision;
        PlannedConversationId = plannedConversationId; OriginalCreationOperationId = originalCreationOperationId;
        LastCommandOperationId = lastCommandOperationId; Title = title; Project = project;
        OriginalStudioConversationId = originalStudioConversationId;
        OriginalStudioContainerId = originalStudioContainerId; OriginalStoreIdentity = originalStoreIdentity;
        OriginalTaskConversation = originalTaskConversation; OriginalTaskContainer = originalTaskContainer; Reason = reason;
    }
    internal object Issuer { get; }
    public AssistantIdentity Identity { get; }
    public long DefinitionRevision { get; }
    public string DenSessionId { get; }
    public long MembershipRevision { get; }
    public Guid PlannedConversationId { get; }
    public Guid OriginalCreationOperationId { get; }
    public Guid LastCommandOperationId { get; }
    public string Title { get; }
    public DeveloperProjectReference Project { get; }
    public Guid OriginalStudioConversationId { get; }
    public Guid OriginalStudioContainerId { get; }
    public ResourceStoreIdentity OriginalStoreIdentity { get; }
    public Conversation OriginalTaskConversation { get; }
    public ContainerDefinition OriginalTaskContainer { get; }
    public string Reason { get; }
}

/// <summary>A source-issued settled command result. DeclinedPending is a pause with
/// a durably acknowledged checkpoint; it provides no binding and certifies neither
/// an enclosing pre-effect refusal nor an unknown source failure as success.</summary>
public sealed class AssistantCompatibleConversationCreationOutcome
{
    internal AssistantCompatibleConversationCreationOutcome(object actualIssuer,
        AssistantConversationBinding? actualBinding, AssistantCompatibleConversationCheckpoint? actualCheckpoint)
    {
        if ((actualBinding is null) == (actualCheckpoint is null))
            throw new ArgumentException("A settled compatible-conversation result requires exactly one actual binding or declined checkpoint.");
        Issuer = actualIssuer; Binding = actualBinding; Checkpoint = actualCheckpoint;
        State = actualBinding is null ? AssistantCompatibleConversationCreationState.DeclinedPending
            : AssistantCompatibleConversationCreationState.Ready;
    }
    internal object Issuer { get; }
    public AssistantCompatibleConversationCreationState State { get; }
    public AssistantConversationBinding? Binding { get; }
    public AssistantCompatibleConversationCheckpoint? Checkpoint { get; }
}

/// <summary>Optional configured canonical owner. Current definition/session/actor,
/// protected metadata source and fresh manual Home READ/WRITE are revalidated for
/// every explicit attempt. A previous Decline is not a permission or replay token.</summary>
public interface IAssistantOriginalCompatibleConversationCheckpointOwner
{
    bool IsIssuedOriginalCheckpoint(AssistantCompatibleConversationCheckpoint sameCheckpoint);
    bool IsIssuedOriginalCreationOutcome(AssistantCompatibleConversationCreationOutcome sameOutcome);
    Task<IReadOnlyList<AssistantCompatibleConversationCheckpoint>> ReadOriginalDeclinedPendingAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, int maximum,
        CancellationToken cancellationToken);
    Task<AssistantCompatibleConversationCreationOutcome> CreateOriginalCompatibleConversationAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, Guid plannedConversationId,
        string title, Guid originalCreationOperationId, Guid commandOperationId,
        AssistantOriginalProjectChoice actualProjectChoice, CancellationToken cancellationToken);
    Task<AssistantCompatibleConversationCreationOutcome> ResumeOriginalCompatibleConversationAsync(
        AssistantCompatibleConversationCheckpoint sameCheckpoint, long expectedDefinitionRevision,
        Guid newCommandOperationId, AssistantOriginalProjectChoice freshActualProjectChoice,
        CancellationToken cancellationToken);
}
