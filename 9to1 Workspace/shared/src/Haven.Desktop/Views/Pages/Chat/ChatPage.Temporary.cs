using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class ChatPage
{
    private readonly List<ConversationContextEntry> _temporaryContextEntries = [];
    private (Conversation Conversation, ChatMessage[] Messages, ConversationContextEntry[] Context)? _pendingLegacySave;
    private bool _changingLegacyType;
    private int _legacyAttachmentOperations;
    private long _presentationGeneration;

    public Func<bool> CapturePresentationGuard()
    {
        var generation = _presentationGeneration;
        var conversation = _conversation;
        return () => generation == _presentationGeneration && _conversation.Id == conversation.Id &&
            _conversation.Title == conversation.Title && _conversation.IsTemporary == conversation.IsTemporary &&
            !_changingLegacyType && _pendingLegacySave is null;
    }

    private void DemandLegacyIdle()
    {
        if (IsSending || _changingLegacyType || _legacyAttachmentOperations != 0 || _pendingLegacySave is not null)
            throw new InvalidOperationException("Wait for the current response or acknowledge the pending local save before changing this chat.");
    }

    private void DemandTemporaryReplacementConsent()
    {
        if (IsTemporary && !IsOriginalChatClosePrepared)
            throw new InvalidOperationException("Save or explicitly discard this Temporary chat before replacing it.");
    }

    private async Task<bool> NewChatWithConsentAsync()
    {
        DemandLegacyIdle();
        if (IsTemporary && !await PrepareToCloseAsync(_legacyChoiceOverlay, CancellationToken.None)) return false;
        NewChat();
        return true;
    }

    private void ResetLegacyHistory()
    {
        _presentationGeneration++;
        _temporaryContextEntries.Clear();
        _contextSummary = string.Empty;
    }

    public async Task SetTemporaryAsync(bool isTemporary)
    {
        if (IsTemporary == isTemporary) return;
        if (IsSending || _changingLegacyType || _legacyAttachmentOperations != 0)
            throw new InvalidOperationException("Wait for the current response or local save before changing chat type.");
        _changingLegacyType = true;
        var original = _conversation;
        try
        {
            if (isTemporary)
            {
                if (original.Kind is ConversationKind.Task or ConversationKind.AutomationRun or ConversationKind.Training or ConversationKind.Call)
                    throw new InvalidOperationException("This system-managed conversation cannot become temporary.");
                if (Messages.Count != 0)
                    throw new InvalidOperationException("A saved conversation cannot become temporary. Start a new chat first.");
                if (Attachments.Count != 0)
                    throw new InvalidOperationException("Remove attached files before starting a temporary draft. Existing files have been preserved.");
                var saved = await _conversations.GetAsync(original.Id, CancellationToken.None);
                if (saved is not null && (await _conversations.GetMessagesAsync(original.Id, CancellationToken.None)).Count != 0)
                    throw new InvalidOperationException("A saved conversation cannot become temporary. Start a new chat first.");
                if (_conversation != original) throw new InvalidOperationException("The chat changed while its type was being checked.");
                if (saved is not null) _conversation = original with { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
                IsTemporary = true;
                Status = "Temporary chat. History remains in this session until you explicitly save it.";
            }
            else
            {
                if (Attachments.Count != 0)
                    throw new InvalidOperationException("Send or remove unsent attached files before saving temporary history. The draft and its files have been preserved.");
                if (_conversations is not ITemporaryConversationSaveRepository owner)
                    throw new NotSupportedException("This store cannot atomically save temporary history. The chat remains temporary.");
                _pendingLegacySave ??= (original, Messages.Select(message => message.ToMessage(original.Id)).ToArray(), _temporaryContextEntries.ToArray());
                var pending = _pendingLegacySave.Value;
                var saved = await owner.SaveTemporaryLocallyAsync(pending.Conversation, pending.Messages, pending.Context, CancellationToken.None);
                if (_conversation != pending.Conversation ||
                    !Messages.Select(message => message.ToMessage(original.Id)).SequenceEqual(pending.Messages) ||
                    !_temporaryContextEntries.SequenceEqual(pending.Context))
                    throw new InvalidOperationException("The chat changed during the local save. Its original snapshot has been retained.");
                _conversation = saved;
                IsTemporary = false;
                _pendingLegacySave = null;
                _temporaryContextEntries.Clear();
                _contextSummary = string.Empty; // Saved context is now read from its real repository owner.
                Status = "Conversation and history saved locally. Cloud synchronisation has not been enabled.";
            }
            _presentationGeneration++;
            RaisePropertyChanged(nameof(ConversationId));
            RaisePropertyChanged(nameof(ConversationTitle));
            RaisePropertyChanged(nameof(ShowTemporaryHeaderAction));
            ConversationChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            Status = "Chat type was not changed: " + error.Message;
            throw;
        }
        finally { _changingLegacyType = false; }
    }
}
