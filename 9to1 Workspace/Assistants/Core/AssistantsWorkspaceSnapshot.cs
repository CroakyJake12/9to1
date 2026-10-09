using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

/// <summary>Presentation projection of saved canonical state, not authorization or recovery custody.</summary>
public sealed record AssistantsWorkspaceSnapshot(
    long Revision,
    IReadOnlyList<AssistantDefinitionSnapshot> Assistants,
    IReadOnlyList<AssistantCapabilityObservation> HostCapabilities,
    AssistantDefinitionSnapshot? SelectedAssistant,
    IReadOnlyList<AssistantConversationSummary> Conversations,
    AssistantConversationBinding? ConversationBinding,
    AssistantConversationData? Conversation,
    AssistantWorkObservation? Work,
    IReadOnlyList<AssistantModelChoice> Models,
    bool IsLoading,
    bool IsSaving,
    string? Error,
    bool IsRetiring)
{
    /// <summary>Actual acknowledged durable pending checkpoints for the selected identity.
    /// Display values do not grant store/project/effect access or substitute for a fresh issuer.</summary>
    public IReadOnlyList<AssistantCompatibleConversationCheckpoint> DeclinedPendingConversations { get; init; } = [];
    public AssistantCompatibleConversationCreationOutcome? LastCompatibleConversationOutcome { get; init; }
    public static AssistantsWorkspaceSnapshot Empty { get; } = new(
        0, [], [], null, [], null, null, null, [], false, false, null, false);
}
