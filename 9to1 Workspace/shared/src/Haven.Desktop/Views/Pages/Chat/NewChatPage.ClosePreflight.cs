using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private bool _isPreparingChatClose;
    private ChatCloseSnapshot? _temporaryDiscardConsent;
    private (Guid ConversationId, string Draft)? _acknowledgedCloseDraft;
    private sealed record ChatCloseSnapshot(Conversation Conversation, ChatMessage[] Messages,
        ChatMessage[] Redo, ConversationContextEntry[] Context, string Draft, ChatCloseContext Composer);
    private sealed record ChatCloseContext(TaskAttachmentSnapshot Attachments, string[] Images,
        KeyValuePair<string, string>[] Context, MessageAttachment[] PersistedAttachments, Guid[] PendingAttachments,
        AgentDefinition? Agent, PromptDefinition[] Instructions, string? RegisteredContext)
    {
        internal bool IsEmpty => Attachments.Files.Count == 0 && Attachments.Apps.Count == 0
            && Attachments.Capabilities.Count == 0 && Images.Length == 0 && Context.Length == 0
            && PersistedAttachments.Length == 0 && PendingAttachments.Length == 0
            && Agent is null && Instructions.Length == 0;
    }

    internal Task? OriginalClose => _originalWork.OriginalClose;
    internal bool IsOriginalChatClosePrepared => Dispatcher.UIThread.CheckAccess()
        && !_disposed && !_originalWork.IsRetiring && !_isPreparingChatClose && !_isChangingTemporary
        && !_isAwaitingTemporaryMemory && !_isAwaitingToolApproval && _pendingTemporarySave is null
        && (_conversation.IsTemporary
            ? !_isSending && _originalSend is not { IsCompleted: false }
                && _temporaryDiscardConsent is { } consent && MatchesCloseSnapshot(consent)
            : string.IsNullOrEmpty(_scene.Instruction.Text)
                || _acknowledgedCloseDraft == (_conversation.Id, _scene.Instruction.Text));

    private ChatCloseSnapshot CaptureCloseSnapshot() => new(_conversation, _messages.ToArray(),
        _redoMessages.ToArray(), _temporaryContextEntries.ToArray(), _scene.Instruction.Text, CaptureCloseContext());

    private ChatCloseContext CaptureCloseContext() => new(_taskAttachments.Snapshot(), _attachedImages.ToArray(),
        _attachedContext.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray(),
        _persistedAttachments.ToArray(), _pendingAttachmentIds.Order().ToArray(), _activeAgent,
        _activeInstructions.ToArray(), _registeredContextOverride);

    private bool MatchesCloseContext(ChatCloseContext snapshot)
    {
        var current = CaptureCloseContext();
        return snapshot.Attachments.Files.SequenceEqual(current.Attachments.Files)
            && snapshot.Attachments.Apps.SequenceEqual(current.Attachments.Apps)
            && snapshot.Attachments.Capabilities.SequenceEqual(current.Attachments.Capabilities)
            && snapshot.Attachments.ExplicitAppIds.SetEquals(current.Attachments.ExplicitAppIds)
            && snapshot.Images.SequenceEqual(current.Images) && snapshot.Context.SequenceEqual(current.Context)
            && snapshot.PersistedAttachments.SequenceEqual(current.PersistedAttachments)
            && snapshot.PendingAttachments.SequenceEqual(current.PendingAttachments)
            && snapshot.Agent == current.Agent && snapshot.Instructions.SequenceEqual(current.Instructions)
            && snapshot.RegisteredContext == current.RegisteredContext;
    }

    private bool MatchesCloseSnapshot(ChatCloseSnapshot snapshot) =>
        _conversation == snapshot.Conversation && _messages.SequenceEqual(snapshot.Messages)
        && _redoMessages.SequenceEqual(snapshot.Redo) && _temporaryContextEntries.SequenceEqual(snapshot.Context)
        && _scene.Instruction.Text == snapshot.Draft && MatchesCloseContext(snapshot.Composer);

    public Task<bool> PrepareToCloseAsync(ContentControl? choiceHost, CancellationToken caller) =>
        RunChatOriginalAsync(() => PrepareChatCloseOriginalAsync(choiceHost ?? _choiceOverlay, caller));

    private async Task<bool> PrepareChatCloseOriginalAsync(ContentControl choiceHost, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        if (IsOriginalChatClosePrepared) return true;
        if (_isPreparingChatClose || _isChangingTemporary || _isAwaitingTemporaryMemory || _isAwaitingToolApproval ||
            (_conversation.IsTemporary && (_isSending || _originalSend is { IsCompleted: false })))
        {
            SetProjectionStatus("Finish or stop the current chat operation before closing.");
            return false;
        }
        _isPreparingChatClose = true;
        try
        {
            if (!_conversation.IsTemporary)
                return await SaveOriginalCloseDraftAsync(caller);
            var snapshot = CaptureCloseSnapshot();
            if (_pendingTemporarySave is null && snapshot.Messages.Length == 0 && snapshot.Redo.Length == 0
                && snapshot.Context.Length == 0 && string.IsNullOrEmpty(snapshot.Draft)
                && snapshot.Composer.IsEmpty)
            {
                _temporaryDiscardConsent = snapshot;
                return true;
            }
            if (TopLevel.GetTopLevel(choiceHost) is null) return false;
            var original = RequireChatOriginal();
            var prepared = false;
            var saveUncertain = _pendingTemporarySave is not null;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
            Task Save() => RunChatNestedOriginalAsync(async () =>
            {
                original.DemandPublication();
                cancellation.Token.ThrowIfCancellationRequested();
                if (!MatchesCloseSnapshot(snapshot)) return;
                if (_attachedImages.Count != 0 || _attachedContext.Count != 0)
                {
                    SetProjectionStatus("Keep this chat open while its attached files are prepared for saving.");
                    return;
                }
                if (!string.IsNullOrEmpty(snapshot.Draft) && _conversationProduction is null)
                {
                    SetProjectionStatus("This store cannot save the unsent draft. The chat remains open.");
                    return;
                }
                await AwaitChatOriginalAsync(SetTemporaryAsync(false));
                if (_conversation.IsTemporary) return;
                prepared = await SaveOriginalCloseDraftAsync(cancellation.Token);
            });
            Task Discard()
            {
                original.DemandPublication();
                cancellation.Token.ThrowIfCancellationRequested();
                if (saveUncertain || _pendingTemporarySave is not null || !MatchesCloseSnapshot(snapshot))
                    return Task.CompletedTask;
                _temporaryDiscardConsent = snapshot;
                prepared = true;
                return Task.CompletedTask;
            }
            var prompt = new CuiChoicePrompt(new CuiChoicePromptOptions(
                "Close Temporary chat?",
                saveUncertain
                    ? "The previous save has not been acknowledged. Retry that save or keep the chat open."
                    : "Save this conversation locally, discard this session, or keep it open. Saving does not enable Mesh or Cloud.",
                saveUncertain ? "Retry save" : "Save", saveUncertain ? "Keep open" : "Discard", "Cancel",
                Save, Discard, () => Task.CompletedTask), "Spaces");
            try { await AwaitChatOriginalAsync(prompt.ShowAsync(choiceHost, cancellation.Token)); }
            finally { await original.AwaitAsync(prompt.CloseAsync()); }
            return prepared;
        }
        finally { _isPreparingChatClose = false; }
    }

    private async Task<bool> SaveOriginalCloseDraftAsync(CancellationToken caller)
    {
        var conversation = _conversation;
        var draft = _scene.Instruction.Text;
        if (string.IsNullOrEmpty(draft)) return true;
        if (_conversationProduction is null) return false;
        if (_isSending || _originalSend is { IsCompleted: false }) return false;
        // A Normal chat may still be an unpersisted empty draft. Establish its
        // existing identity before the branch/draft owner's foreign-key write.
        await AwaitChatOriginalAsync(_conversations.UpsertConversationAsync(conversation, caller));
        await AwaitChatOriginalAsync(SavePendingAttachmentDraftAsync(caller));
        if (_conversation.Id != conversation.Id || _conversation.IsTemporary || _scene.Instruction.Text != draft) return false;
        _acknowledgedCloseDraft = (conversation.Id, draft);
        return true;
    }
}
