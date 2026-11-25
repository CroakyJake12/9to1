using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantsWorkspaceController
{
    public bool HasSecondaryConversationArchiveOwner => _bridge is IAssistantSecondaryConversationArchiveOwner;

    public Task<AssistantsWorkspaceSnapshot> SetSecondaryConversationArchivedAsync(AssistantIdentity identity,
        Guid conversationId, bool observedArchived, CancellationToken token = default)
    {
        var generation = CurrentNavigation();
        var initial = Snapshot;
        return CommandAsync(async () =>
        {
            var state = Snapshot;
            if (!IsCurrentNavigation(generation) || state.SelectedAssistant?.Identity != identity ||
                state.SelectedAssistant.Revision != initial.SelectedAssistant?.Revision ||
                state.ConversationBinding?.Conversation.Id == conversationId ||
                !state.Conversations.Any(row => row.ConversationId == conversationId &&
                    row.IsOrdinarySecondary && !row.IsMain && row.CanonicalTask is null && row.IsArchived == observedArchived))
                throw IssueLocalRefusal("Select an unchanged secondary conversation from the current Assistant Workspace without leaving your current draft.");
            if (_bridge is not IAssistantSecondaryConversationArchiveOwner owner)
                throw IssueLocalRefusal("The actual canonical secondary conversation lifecycle owner is unavailable.");
            var binding = await ObserveSourceAsync(() => _bridge.OpenConversationAsync(identity, conversationId, token)).ConfigureAwait(false);
            if (binding.Definition.Revision != state.SelectedAssistant.Revision || binding.Conversation.IsArchived != observedArchived ||
                !IsCurrentNavigation(generation) || Snapshot.ConversationBinding?.Conversation.Id == conversationId)
                throw IssueLocalRefusal("The selected secondary conversation changed. Refresh and select it again.");
            await ObserveSourceAsync(() => owner.SetSecondaryConversationArchivedAsync(binding, !observedArchived, token)).ConfigureAwait(false);
            var conversations = await ObserveSourceAsync(() => _bridge.ReadConversationsAsync(identity, token: token)).ConfigureAwait(false);
            return Publish(current => IsCurrentNavigation(generation) && current.SelectedAssistant?.Identity == identity
                ? current with { Conversations = Freeze(conversations), Error = null } : current);
        }, true, token);
    }
}
