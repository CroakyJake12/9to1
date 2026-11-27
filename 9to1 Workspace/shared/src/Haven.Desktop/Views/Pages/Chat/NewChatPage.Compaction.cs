using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private const int CompactionSourceCharacterLimit = 180_000;
    private int _contextCompactionInProgress;
    private readonly Dictionary<Guid, List<ConversationCompaction>> _temporaryCompactions = [];

    private async Task CompactContextOriginalBodyAsync()
    {
        if (Interlocked.CompareExchange(ref _contextCompactionInProgress, 1, 0) != 0) return;
        try
        {
            if (!await AwaitChatOriginalAsync(EnsureConversationMayActAsync("chat.compact-context"))) return;
            if (!CanUseSelectedModel)
            {
                SetModelGateStatus("Choose an available model before compacting context.");
                return;
            }
            var original = _conversation;
            var originalModel = _selectedModel!;
            var originalOptions = _preferences.GenerationOptions with { Temperature = 0.2 };
            var store = _conversations as IConversationCompactionRepository;
            var contextSource = _conversations as IConversationPromptContextRepository;
            if (!original.IsTemporary && (store is null || contextSource is null))
            {
                SetProjectionStatus("This conversation store cannot retain a summary and its source history together. No context was changed.");
                return;
            }
            var originalContext = original.IsTemporary ? null
                : await AwaitChatOriginalAsync(contextSource!.GetPromptContextAsync(original.Id, RequireChatOriginal().Token));
            var includedHistory = originalContext?.Messages ?? _messages;
            var candidates = includedHistory.Where(message => !message.IsCompacted &&
                message.Role is MessageRole.User or MessageRole.Assistant).SkipLast(6).ToArray();
            if (candidates.Length < 4)
            {
                SetProjectionStatus("There is not enough older context to compact yet.");
                return;
            }

            // Retain complete source messages. The previous suffix truncation excluded messages
            // from the model input and still marked them compacted afterwards.
            var transcript = new StringBuilder();
            var selected = new List<ChatMessage>();
            foreach (var message in candidates)
            {
                var text = $"{message.Role}: {message.Content}";
                var separator = selected.Count == 0 ? 0 : 2;
                if (text.Length > CompactionSourceCharacterLimit - transcript.Length - separator) break;
                if (separator != 0) transcript.Append("\n\n");
                transcript.Append(text); selected.Add(message);
            }
            if (selected.Count < 4)
            {
                SetProjectionStatus("The older messages exceed this compaction window. No messages were excluded from context.");
                return;
            }

            SetProjectionStatus("Compacting older context…");
            var summary = await AwaitChatOriginalAsync(_ollama.CompleteAsync(
                new OllamaChatRequest(originalModel.Name,
                    [new OllamaMessage("user", "Summarise this conversation context for a future assistant. Preserve requirements, decisions, named files, unresolved questions, errors, and verified evidence. Do not invent facts.\n\n" + transcript)],
                    EffortLevel.Medium, Options: originalOptions),
                RequireChatOriginal().Token));
            if (string.IsNullOrWhiteSpace(summary))
                throw new InvalidOperationException("The model returned no summary. The full source context was retained.");
            if (!await AwaitChatOriginalAsync(EnsureConversationMayActAsync("chat.compact-context"))) return;
            if (_conversation.Id != original.Id || _conversation.IsTemporary != original.IsTemporary ||
                selected.Any(message => _messages.SingleOrDefault(current => current.Id == message.Id) is not { } current ||
                    (current with { IsCompacted = false }) != message))
                throw new InvalidOperationException("The original compaction context changed. No messages were excluded.");

            var now = DateTimeOffset.UtcNow;
            var replacement = new ConversationCompaction(
                new(Guid.NewGuid(), original.Id, ContextEntryKind.CompactSummary, "Manual compact summary", summary,
                    $"Retains {selected.Count} original messages; created {now:O}.", now), selected).Snapshot(original.Id);
            if (originalContext is not null) replacement = originalContext.BindCompaction(replacement.Summary, selected);
            if (original.IsTemporary)
            {
                // This is the session-only replacement owner. No durable write happens until
                // the existing explicit local Save admits the same summary and history.
                if (!_temporaryCompactions.TryGetValue(original.Id, out var retained))
                    _temporaryCompactions.Add(original.Id, retained = []);
                retained.Add(replacement);
            }
            else
            {
                var receipt = await AwaitChatOriginalAsync(store!.CommitCompactionAsync(original.Id,
                    replacement, RequireChatOriginal().Token));
                if (receipt.ConversationId != original.Id || receipt.Summary != replacement.Summary ||
                    !receipt.SourceMessageIds.SequenceEqual(selected.Select(message => message.Id)))
                    throw new InvalidOperationException("The compaction receipt does not match the retained source and summary.");
                now = receipt.CompactedAt;
            }

            // Only the actual replacement owner's acknowledgement permits changing inclusion.
            var ids = selected.Select(message => message.Id).ToHashSet();
            for (var index = 0; index < _messages.Count; index++)
                if (ids.Contains(_messages[index].Id)) _messages[index] = _messages[index] with { IsCompacted = true };
            _conversation = _conversation with { CompactedAt = now, UpdatedAt = now };
            _redoMessages.Clear();
            RefreshMessages();
            SetProjectionStatus(original.IsTemporary
                ? $"Compacted {selected.Count} older messages into a session-only summary. Full history is retained."
                : $"Compacted {selected.Count} older messages into a saved summary. Full history is retained.");
            await AwaitChatOriginalAsync(RefreshContextEntriesAsync());
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or System.Data.Common.DbException)
        {
            RetainHandledOriginalChatCause(exception);
            // The actual source failure stays with the admitted original even if
            // close or a branch replacement has already withdrawn its UI target.
            _originalWork.Executing?.RunAcceptedPublicationCallback(() =>
                SetProjectionStatus("Context compaction failed: " + exception.Message));
        }
        finally { Volatile.Write(ref _contextCompactionInProgress, 0); }
    }

    private IReadOnlyList<ConversationCompaction> CurrentTemporaryCompactions() =>
        _temporaryCompactions.TryGetValue(_conversation.Id, out var retained) ? retained : [];

    private GenerationOptions CaptureCompactionGenerationOptions(Conversation original) =>
        _preferences.GenerationOptions with
        {
            SpecialistId = _activeAgent?.Id,
            OriginalTemporaryContext = original.IsTemporary
                ? new TemporaryConversationContext(original, _messages, CurrentTemporaryCompactions()) : null
        };
}
