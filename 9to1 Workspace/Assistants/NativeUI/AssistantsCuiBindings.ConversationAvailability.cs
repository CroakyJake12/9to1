using Haven.Core;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private bool CanRefreshConversationModels => _snapshot.ConversationBinding is not null && !Busy && !_streaming;

    private void RefreshConversationAvailability(bool usable)
    {
        var hasConversation = _snapshot.ConversationBinding is not null;
        var isTask = _snapshot.Conversation?.Conversation.Kind == ConversationKind.Task;
        Set("CanRefreshConversationModels", usable && CanRefreshConversationModels);
        Set("ShowChatComposer", !isTask);
        Set("ShowTaskComposer", isTask);
        Set("ShowModelAvailabilityHelp", hasConversation && _selectedModel is null);
        Set("ModelAvailabilityHelp", !hasConversation
            ? "Create or open a conversation to check available models."
            : _snapshot.Models.Count == 0
                ? "No model is available for this Assistant. Review its model and cloud preferences, then refresh models. You can write and save a draft while model access is unavailable."
                : "Choose an available model below before sending. Your saved model preference is unchanged.");
        Set("ComposerAvailability", !hasConversation ? "Open a conversation to write a message."
            : _snapshot.SelectedAssistant?.Configuration.Enabled != true ? "This Assistant is disabled. Enable it in Configure before sending."
            : _selectedModel is null ? "Send is unavailable until you choose an authorized model."
            : _streaming ? "Your Assistant is responding."
            : Busy ? "Wait for the current operation to finish."
            : string.IsNullOrWhiteSpace(_prompt) ? "Write a message to begin." : "");
    }
}
