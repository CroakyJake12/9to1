using System.Text.Json;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private readonly HashSet<Guid> _pendingAttachmentIds = [];

    private IReadOnlyList<ChatAttachmentChip> BuildAttachmentChips()
    {
        var chips = new List<ChatAttachmentChip>();
        if (_activeAgent is { } agent)
            chips.Add(new ChatAttachmentChip($"agent:{agent.Id:D}", agent.Name, string.IsNullOrWhiteSpace(agent.IconKey) ? "agents" : agent.IconKey));
        chips.AddRange(_activeInstructions.Select(item =>
            new ChatAttachmentChip($"instruction:{item.Id:D}", item.Name, string.IsNullOrWhiteSpace(item.IconKey) ? "prompt" : item.IconKey)));
        if (_messageAttachments is not null)
        {
            chips.AddRange(_persistedAttachments.Select(item =>
                new ChatAttachmentChip($"attachment:{item.Id:D}", item.OriginalName, AttachmentIconForKind(item.Kind))));
        }
        else
        {
            chips.AddRange(_taskAttachments.Files.Select(path =>
                new ChatAttachmentChip("file:" + path, Path.GetFileName(path), AttachmentIconForFile(path))));
        }
        chips.AddRange(_taskAttachments.Capabilities.Select(item =>
            new ChatAttachmentChip($"capability:{item.Id:D}", item.Name, "plugin")));
        chips.AddRange(_taskAttachments.Apps.Select(item =>
            new ChatAttachmentChip($"app:{item.Id:D}", item.Name, string.IsNullOrWhiteSpace(item.IconKey) ? "all-modes" : item.IconKey)));
        return chips;
    }

    private void OnAttachmentRemoveRequested(object? sender, string id)
    {
        if (_originalWork.IsRetiring) return;
        _ = RunChatOriginalAsync(() => OnAttachmentRemoveRequestedOriginalBodyAsync(sender, id));
    }

    private async Task OnAttachmentRemoveRequestedOriginalBodyAsync(object? sender, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var changed = false;
        if (TryReadGuid(id, "attachment:", out var attachmentId) && _messageAttachments is not null)
        {
            try
            {
                await AwaitChatOriginalAsync(_messageAttachments.DeleteAsync(attachmentId, RequireChatOriginal().Token));
                changed = _persistedAttachments.RemoveAll(item => item.Id == attachmentId) > 0;
                _pendingAttachmentIds.Remove(attachmentId);
                if (_attachmentSourcePaths.Remove(attachmentId, out var sourcePath))
                    _taskAttachments.RemoveFile(sourcePath);
                await AwaitChatOriginalAsync(RefreshPersistedAttachmentPromptContextAsync());
                await AwaitChatOriginalAsync(SavePendingAttachmentDraftAsync());
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                RetainHandledOriginalChatCause(exception);
                _scene.SetStatus("The attachment could not be removed: " + exception.Message);
                return;
            }
        }
        else if (id.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = id[5..];
            changed = _taskAttachments.RemoveFile(path);
            if (changed)
            {
                _attachedImages.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
                _attachedContext.Remove(path);
            }
        }
        else if (TryReadGuid(id, "capability:", out var capabilityId))
            changed = _taskAttachments.RemoveCapability(capabilityId);
        else if (TryReadGuid(id, "app:", out var appId))
            changed = _taskAttachments.RemoveApp(appId);
        else if (TryReadGuid(id, "instruction:", out var instructionId))
            changed = _activeInstructions.RemoveAll(item => item.Id == instructionId) > 0;
        else if (TryReadGuid(id, "agent:", out var agentId) && _activeAgent?.Id == agentId)
        {
            _activeAgent = null;
            changed = true;
        }

        if (!changed) return;
        RefreshAttachmentStatus();
        RefreshResponseControls();
        _scene.SetStatus("Attachment removed from this chat.");
        FocusComposer();
    }

    private Task LoadPendingPersistedAttachmentsAsync(Conversation conversation) => RunChatNestedOriginalAsync(() => LoadPendingPersistedAttachmentsOriginalBodyAsync(conversation));

    private async Task LoadPendingPersistedAttachmentsOriginalBodyAsync(Conversation conversation)
    {
        _pendingAttachmentIds.Clear();
        _persistedAttachments.Clear();
        _attachmentSourcePaths.Clear();
        if (_conversationProduction is null || conversation.IsTemporary) return;

        try
        {
            var branch = await AwaitChatOriginalAsync(_conversationProduction.GetCurrentBranchAsync(conversation.Id, RequireChatOriginal().Token))
                         ?? await AwaitChatOriginalAsync(_conversationProduction.EnsureRootBranchAsync(conversation.Id, RequireChatOriginal().Token));
            var draft = await AwaitChatOriginalAsync(_conversationProduction.GetDraftAsync(conversation.Id, branch.Id, RequireChatOriginal().Token));
            if (draft is null || string.IsNullOrWhiteSpace(draft.AttachmentIdsJson)) return;

            Guid[] ids;
            try
            {
                ids = JsonSerializer.Deserialize<Guid[]>(draft.AttachmentIdsJson) ?? [];
            }
            catch (JsonException originalCaughtCause)
            {
                RetainHandledOriginalChatCause(originalCaughtCause);
                ids = [];
            }
            foreach (var id in ids.Where(id => id != Guid.Empty)) _pendingAttachmentIds.Add(id);
            if (_pendingAttachmentIds.Count == 0) return;

            var attachments = await AwaitChatOriginalAsync(_conversationProduction.GetAttachmentsAsync(conversation.Id, messageId: null, RequireChatOriginal().Token));
            _persistedAttachments.AddRange(attachments.Where(item => _pendingAttachmentIds.Contains(item.Id)));
            _pendingAttachmentIds.IntersectWith(_persistedAttachments.Select(item => item.Id));
            await AwaitChatOriginalAsync(RefreshPersistedAttachmentPromptContextAsync());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            RetainHandledOriginalChatCause(exception);
            _scene.SetStatus("Pending attachments could not be restored: " + exception.Message);
        }
    }

    private Task<Guid?> EnsureAttachmentBranchAsync(CancellationToken cancellationToken) => RunChatNestedOriginalAsync(() => EnsureAttachmentBranchOriginalBodyAsync(cancellationToken));

    private async Task<Guid?> EnsureAttachmentBranchOriginalBodyAsync(CancellationToken cancellationToken)
    {
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RequireChatOriginal().Token);
        cancellationToken = originalCancellation.Token;
        if (_conversationProduction is null || _conversation.IsTemporary) return null;
        var branch = await AwaitChatOriginalAsync(_conversationProduction.GetCurrentBranchAsync(_conversation.Id, cancellationToken))
                     ?? await AwaitChatOriginalAsync(_conversationProduction.EnsureRootBranchAsync(_conversation.Id, cancellationToken));
        return branch.Id;
    }

    private Task SavePendingAttachmentDraftAsync(CancellationToken cancellationToken = default) => RunChatNestedOriginalAsync(() => SavePendingAttachmentDraftOriginalBodyAsync(cancellationToken));

    private async Task SavePendingAttachmentDraftOriginalBodyAsync(CancellationToken cancellationToken = default)
    {
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RequireChatOriginal().Token);
        cancellationToken = originalCancellation.Token;
        if (_conversationProduction is null || _conversation.IsTemporary) return;
        var branchId = await AwaitChatOriginalAsync(EnsureAttachmentBranchAsync(cancellationToken));
        if (branchId is null) return;
        if (_pendingAttachmentIds.Count == 0 && string.IsNullOrWhiteSpace(_scene.Instruction.Text))
        {
            await AwaitChatOriginalAsync(_conversationProduction.DeleteDraftAsync(_conversation.Id, branchId, cancellationToken));
            return;
        }
        await AwaitChatOriginalAsync(_conversationProduction.SaveDraftAsync(
            new ConversationDraft(
                _conversation.Id,
                branchId.Value,
                _scene.Instruction.Text,
                JsonSerializer.Serialize(_pendingAttachmentIds.OrderBy(id => id).ToArray()),
                DateTimeOffset.UtcNow),
            cancellationToken));
    }

    private Task AssociatePendingAttachmentsWithUserMessageAsync(Guid userMessageId, CancellationToken cancellationToken) => RunChatNestedOriginalAsync(() => AssociatePendingAttachmentsWithUserMessageOriginalBodyAsync(userMessageId, cancellationToken));

    private async Task AssociatePendingAttachmentsWithUserMessageOriginalBodyAsync(Guid userMessageId, CancellationToken cancellationToken)
    {
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RequireChatOriginal().Token);
        cancellationToken = originalCancellation.Token;
        if (_conversationProduction is null || _pendingAttachmentIds.Count == 0 || _conversation.IsTemporary || userMessageId == Guid.Empty) return;

        var attachments = await AwaitChatOriginalAsync(_conversationProduction.GetAttachmentsAsync(_conversation.Id, messageId: null, cancellationToken));
        var now = DateTimeOffset.UtcNow;
        foreach (var attachment in attachments.Where(item => _pendingAttachmentIds.Contains(item.Id)))
        {
            await AwaitChatOriginalAsync(_conversationProduction.UpsertAttachmentAsync(
                attachment with { MessageId = userMessageId, UpdatedAt = now },
                cancellationToken));
        }

        var branch = await AwaitChatOriginalAsync(_conversationProduction.GetCurrentBranchAsync(_conversation.Id, cancellationToken));
        await AwaitChatOriginalAsync(_conversationProduction.DeleteDraftAsync(_conversation.Id, branch?.Id, cancellationToken));
        ClearPendingPersistedAttachmentsFromComposer();
    }

    private void ClearPendingPersistedAttachmentsFromComposer()
    {
        foreach (var path in _taskAttachments.Files.ToArray()) _taskAttachments.RemoveFile(path);
        _pendingAttachmentIds.Clear();
        _persistedAttachments.Clear();
        _attachmentSourcePaths.Clear();
        _attachedImages.Clear();
        _attachedContext.Remove("persisted-attachments");
        RefreshAttachmentStatus();
    }

    private static bool TryReadGuid(string value, string prefix, out Guid id)
    {
        id = Guid.Empty;
        return value.StartsWith(prefix, StringComparison.Ordinal)
               && Guid.TryParse(value[prefix.Length..], out id);
    }

    private static string AttachmentIconForKind(MessageAttachmentKind kind) => kind switch
    {
        MessageAttachmentKind.Image => "image",
        MessageAttachmentKind.Video => "play",
        MessageAttachmentKind.Audio => "mic",
        _ => "file"
    };

    private static string AttachmentIconForFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => "image",
            ".mp4" or ".mov" or ".m4v" or ".webm" or ".avi" => "play",
            ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg" => "mic",
            _ => "file"
        };
    }
}
