using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private sealed record OriginalResponseRegeneration(Guid BranchId, ChatMessage UserInput, ModelDescriptor Model,
        IReadOnlyList<string>? Images, IReadOnlyList<string> AttachmentContext);

    private async Task RegenerateResponseOriginalBodyAsync(Conversation originalConversation, long originalGeneration,
        ChatMessage message, ResponseRegenerationMode requestedMode)
    {
        RequireChatOriginal().AllowsSourceContextTransition = false;
        RequireChatOriginal().BindPublicationGuard(() => _conversation.Id == originalConversation.Id && _originalPresentationGeneration == originalGeneration);
        DemandOriginalChatPublication();
        if (_isSending || Volatile.Read(ref _temporarySaveInProgress) != 0) return;
        if (!MayOrganizeSavedChat("regenerate a saved response")) return;
        if (originalConversation.Mode == HavenMode.Tasks)
        {
            SetProjectionStatus("Use the original task's recovery controls to continue its work.");
            return;
        }
        if (_dual is { IsActive: true })
        {
            SetProjectionStatus("Turn off comparison before regenerating a saved response.");
            return;
        }
        if (!await AwaitChatOriginalAsync(EnsureConversationMayActAsync("chat.regenerate"))) return;
        if (!CanUseSelectedModel)
        {
            SetModelGateStatus("Choose an available model before regenerating this response.");
            return; // Never queue the old input as a new composer submission.
        }
        var admittedModel = _selectedModel!;
        try
        {
            var index = _messages.FindIndex(item => item.Id == message.Id && item.Role == MessageRole.Assistant);
            if (index < 0) return;
            var precedingUser = _messages.Take(index).LastOrDefault(item => item.Role == MessageRole.User)
                ?? throw new InvalidOperationException("This response has no preceding user message.");
            var isLatest = _messages.LastOrDefault(item => item.Role == MessageRole.Assistant)?.Id == message.Id;
            var mode = isLatest ? requestedMode : ResponseRegenerationMode.NewBranch;
            if (mode == ResponseRegenerationMode.NewBranch && _scene.HasQueuedComposerMessages)
            {
                SetProjectionStatus("Send the queued messages on this branch before regenerating into a new branch. Your draft and queued messages have been retained.");
                return;
            }
            var previousBranch = await AwaitChatOriginalAsync(EnsureAttachmentBranchAsync(RequireChatOriginal().Token));
            if (mode == ResponseRegenerationMode.NewBranch && _conversationProduction is null)
                throw new InvalidOperationException("Branch draft storage is unavailable; the current draft has been retained.");

            // These reads belong to the original input, not to attachments awaiting the NEXT send.
            var (images, attachmentContext) = await ReadRegenerationAttachmentsAsync(precedingUser);
            OriginalDraftTransition? outgoing = null;
            await AwaitChatOriginalAsync(InvokeOriginalChatUiAsync(() => outgoing = CaptureOriginalDraftTransition()));
            var captured = outgoing ?? throw new InvalidOperationException("The outgoing branch draft could not be captured.");
            if (mode == ResponseRegenerationMode.NewBranch)
                await AwaitChatOriginalAsync(FlushOriginalConversationDraftAsync(captured));
            else
                await AwaitChatOriginalAsync(SavePendingAttachmentDraftAsync());
            var branch = await AwaitChatOriginalAsync(_versioning.PrepareRegenerationAsync(
                originalConversation.Id, message.Id, isLatest, mode, RequireChatOriginal().Token));
            if (branch.Id == Guid.Empty || branch.ConversationId != originalConversation.Id)
                throw new InvalidOperationException("The regeneration owner returned a different conversation branch.");

            var changedBranch = previousBranch is { } previous && previous != branch.Id;
            if (changedBranch)
                await AdoptRegenerationBranchDraftAsync(originalConversation, previousBranch!.Value, branch.Id, captured);
            var persisted = await AwaitChatOriginalAsync(_conversations.GetMessagesAsync(originalConversation.Id, RequireChatOriginal().Token));
            await AwaitChatOriginalAsync(InvokeOriginalChatUiAsync(() =>
            {
                _messages.Clear();
                _messages.AddRange(persisted);
                RefreshMessages();
            }));
            await AwaitChatOriginalAsync(SubmitCurrentInstructionOriginalBodyAsync(originalConversation, originalGeneration,
                precedingUser.Content, new(branch.Id, precedingUser, admittedModel, images, attachmentContext)));
            if (changedBranch && !_originalWork.IsRetiring)
                await AwaitChatOriginalAsync(InvokeOriginalChatUiAsync(() =>
                    SetProjectionStatus((string.IsNullOrWhiteSpace(_projectionStatus) ? string.Empty : _projectionStatus + " ") +
                        "Your previous draft and pending attachments remain on their original branch.")));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            RetainHandledOriginalChatCause(exception);
            SetProjectionStatus(exception.Message);
        }
    }

    private async Task<(IReadOnlyList<string>? Images, IReadOnlyList<string> Context)> ReadRegenerationAttachmentsAsync(ChatMessage input)
    {
        if (_conversationProduction is null) return (null, []);
        var attachments = await AwaitChatOriginalAsync(_conversationProduction.GetAttachmentsAsync(
            input.ConversationId, input.Id, RequireChatOriginal().Token));
        if (attachments.Any(item => item.ConversationId != input.ConversationId || item.MessageId != input.Id))
            throw new InvalidOperationException("The original input's attachment identities could not be confirmed.");
        if (attachments.Count == 0) return (null, []);
        if (_messageAttachments is null)
            throw new InvalidOperationException("The original attachment service is unavailable for regeneration.");
        var context = await AwaitChatOriginalAsync(_messageAttachments.BuildPromptContextAsync(
            input.ConversationId, attachments.Select(item => item.Id).ToArray(), null, RequireChatOriginal().Token));
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.ExtractedText)) sections.Add(context.ExtractedText);
        if (context.Notices.Count > 0) sections.Add("Attachment processing:\n" + string.Join("\n", context.Notices));
        return (context.ImageBase64.Count == 0 ? null : context.ImageBase64.ToArray(), sections);
    }

    private async Task AdoptRegenerationBranchDraftAsync(Conversation conversation, Guid previousBranchId, Guid branchId,
        OriginalDraftTransition captured)
    {
        // The existing branch policy saves the outgoing draft and restores the target branch's
        // own draft. It never moves pending attachments or retires them as regenerated input.
        var adopted = false;
        await AwaitChatOriginalAsync(InvokeOriginalChatUiAsync(() =>
        {
            if (Volatile.Read(ref _originalComposerRevision) != captured.Revision ||
                !string.Equals(_composerInput.Text, captured.ComposerText, StringComparison.Ordinal) ||
                !_pendingAttachmentIds.SetEquals(captured.Outgoing.AttachmentIds)) return;
            ClearOriginalComposerDraft();
            ClearPendingPersistedAttachmentsFromComposer();
            RememberOriginalDraftBranch(conversation.Id, branchId);
            _originalDraftRestoreConversationId = conversation.Id;
            _originalDraftRestoreRevision = Volatile.Read(ref _originalComposerRevision);
            adopted = true;
        }));
        if (!adopted)
        {
            // A newer edit still belongs to the outgoing branch. Preserve the prepared branch,
            // restore the original selection and keep the live draft instead of moving its ownership.
            await AwaitChatOriginalAsync(_conversationProduction!.SetCurrentBranchAsync(
                conversation.Id, previousBranchId, RequireChatOriginal().Token));
            throw new InvalidOperationException("The draft or attachments changed while preparing the new branch. Your original input and the prepared branch were retained; regenerate again when ready.");
        }
        await AwaitChatOriginalAsync(LoadPendingPersistedAttachmentsAsync(conversation));
        RefreshAttachmentStatus();
    }
}
