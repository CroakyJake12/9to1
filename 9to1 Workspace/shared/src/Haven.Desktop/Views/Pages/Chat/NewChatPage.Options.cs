using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private sealed class NativeConversationOptions(NewChatPage page) : IChatComposerConversationActions
    {
        private sealed record CompactionObservation(long Epoch, OriginalConversationTarget Target,
            Conversation Conversation, ChatMessage[] History, int IncludedMessages);
        private CompactionObservation? _compactionObservation;
        private long _observationEpoch;

        private bool IsBusy => page.IsSending || page.HasAdmittedOriginalSend() ||
            Volatile.Read(ref page._temporarySaveInProgress) != 0 ||
            Volatile.Read(ref page._contextCompactionInProgress) != 0;

        private int? IncludedHistoryCount(bool temporary)
        {
            if (temporary) return page._messages.Count(message => !message.IsCompacted &&
                message.Role is MessageRole.User or MessageRole.Assistant);
            var rawCount = page._messages.Count(message => message.Role is MessageRole.User or MessageRole.Assistant);
            if (rawCount < 10) return rawCount;
            var observed = _compactionObservation;
            return observed is not null && observed.Epoch == Volatile.Read(ref _observationEpoch) &&
                observed.Target == page.CaptureOriginalConversationTarget() &&
                ReferenceEquals(observed.Conversation, page._conversation) &&
                observed.History.SequenceEqual(page._messages)
                ? observed.IncludedMessages : null;
        }

        private long InvalidateObservation()
        {
            var epoch = Interlocked.Increment(ref _observationEpoch);
            _compactionObservation = null;
            return epoch;
        }

        public ValueTask RefreshAsync()
        {
            var epoch = InvalidateObservation();
            return new(page.RunChatOriginalAsync(async () =>
            {
                var conversation = page._conversation;
                var target = page.CaptureOriginalConversationTarget();
                var history = page._messages.ToArray();
                if (conversation.IsTemporary || conversation.Mode != HavenMode.Chat ||
                    conversation.Kind != ConversationKind.Chat || conversation.IsArchived ||
                    page._safetyLocked || IsBusy ||
                    history.Count(message => message.Role is MessageRole.User or MessageRole.Assistant) < 10 ||
                    page._conversations is not IConversationCompactionRepository ||
                    page._conversations is not IConversationPromptContextRepository source)
                    return;

                // Global IsCompacted flags cannot identify a branch's valid replacements.
                // Observe the same canonical prompt context the actual compaction owner reads.
                var context = await page.AwaitChatOriginalAsync(source.GetPromptContextAsync(
                    conversation.Id, page.RequireChatOriginal().Token));
                if (epoch != Volatile.Read(ref _observationEpoch) || IsBusy ||
                    target != page.CaptureOriginalConversationTarget() ||
                    !ReferenceEquals(conversation, page._conversation) ||
                    !history.SequenceEqual(page._messages) ||
                    context.ConversationId != conversation.Id || !context.OriginalMessages.SequenceEqual(history))
                    return;

                _compactionObservation = new(epoch, target, conversation, history,
                    context.Messages.Count(message => !message.IsCompacted &&
                        message.Role is MessageRole.User or MessageRole.Assistant));
            }));
        }

        public ChatComposerConversationOptions? Current
        {
            get
            {
                if (page._conversation.Mode != HavenMode.Chat || page._conversation.Kind != ConversationKind.Chat)
                    return null;
                var available = !page._disposed && !page._originalWork.IsRetiring && !page._safetyLocked &&
                    !page._conversation.IsArchived;
                var busy = IsBusy;
                var temporary = page._conversation.IsTemporary;
                var fresh = page._unpublishedFreshChat.ContainsKey(page._conversation.Id) &&
                    !page._startedTemporaryLifecycle.ContainsKey(page._conversation.Id) && page._messages.Count == 0;
                var history = IncludedHistoryCount(temporary);
                var supportsCompaction = temporary || page._conversations is IConversationCompactionRepository and IConversationPromptContextRepository;
                return new(temporary, available && !busy && (temporary || fresh),
                    busy ? "Wait for the current response, save or compaction to finish." :
                    !temporary && !fresh ? "Start a new chat to use Temporary. This saved history stays available." :
                    temporary && !fresh ? "Turning Temporary off asks you to save this same chat locally." :
                    "Change this unsent chat without losing its draft or attachments.",
                    available && !busy && page.CanUseSelectedModel && supportsCompaction && history is >= 10,
                    busy ? "Wait for the current operation before compacting context." :
                    !page.CanUseSelectedModel ? "Choose an available model to compact context." :
                    !supportsCompaction ? "This conversation store cannot retain summaries with their original history." :
                    history is null ? "The current context has changed. Reopen Options to check this branch again." :
                    history < 10 ? "Compaction becomes available after more conversation history." :
                    "Summarise older turns while retaining the full message history.",
                    temporary ? "Messages and summaries stay in this session until you explicitly save locally. Saving does not grant tools or transfer the chat." :
                    fresh ? "This unsent draft has not been saved as conversation history. The first accepted message starts local history." :
                    "This conversation is saved locally. Compaction keeps the original messages available in history.");
            }
        }

        public ValueTask SetTemporary(bool temporary) => new(page.RunChatOriginalAsync(() =>
        {
            if (Current?.CanChangeTemporary != true)
                throw new InvalidOperationException("The original chat can no longer change its Temporary setting.");
            return page.SetTemporaryOriginalBodyAsync(temporary);
        }));

        public ValueTask CompactContext() => new(page.RunChatOriginalAsync(() =>
        {
            if (Current?.CanCompactContext != true)
                throw new InvalidOperationException("The original chat can no longer compact context.");
            // Invalidate before the first source await. A held earlier observation cannot
            // re-enable this action, even when existing global flags do not change.
            InvalidateObservation();
            return page.CompactContextOriginalBodyAsync();
        }));
    }
}
