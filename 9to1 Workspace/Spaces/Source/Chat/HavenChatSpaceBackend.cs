using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>
/// Thin adapter over Haven's existing Chat repositories and services. It owns no persistence and
/// delegates every stored mutation to the shared production contracts.
/// </summary>
public sealed class HavenChatSpaceBackend(
    IConversationRepository conversations,
    IConversationProductionRepository production,
    IConversationVersioningService versioning,
    IMessageAttachmentService attachmentService,
    IChatSpaceModelCatalog models,
    IChatSpaceTurnExecutor turns,
    IConversationSafetyService safety) : IChatSpaceBackend
{
    public async Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken cancellationToken) =>
        (await conversations.GetRecentInScopeAsync(ConversationScope.GeneralChat, Math.Clamp(limit, 1, 100), cancellationToken)
            .ConfigureAwait(false))
        .Where(item => !item.IsArchived && !item.IsTemporary &&
            (item.SpaceId is null || item.SpaceId == SpaceRegistry.ChatSpaceId))
        .OrderByDescending(item => item.UpdatedAt)
        .ToArray();

    public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken cancellationToken) =>
        models.GetInventoryAsync(cancellationToken);

    public async Task<Conversation> CreateChatAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "New chat", null, null,
            false, false, now, now, SpaceId: SpaceRegistry.ChatSpaceId);
        await conversations.UpsertConversationAsync(conversation, cancellationToken).ConfigureAwait(false);
        await production.EnsureRootBranchAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
        return conversation;
    }

    public async Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Chat '{conversationId}' was not found.");
        if (!ConversationScope.GeneralChat.Matches(conversation) ||
            conversation.SpaceId is { } spaceId && spaceId != SpaceRegistry.ChatSpaceId)
            throw new InvalidOperationException("Chat Space can open only general Chat conversations assigned to Chat.");

        var current = await production.GetCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false)
            ?? await production.EnsureRootBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var messageTask = conversations.GetMessagesAsync(conversationId, cancellationToken);
        var attachmentTask = production.GetAttachmentsAsync(conversationId, null, cancellationToken);
        var branchTask = production.GetBranchesAsync(conversationId, cancellationToken);
        var draftTask = production.GetDraftAsync(conversationId, current.Id, cancellationToken);
        await Task.WhenAll(messageTask, attachmentTask, branchTask, draftTask).ConfigureAwait(false);
        return new(conversation, messageTask.Result, attachmentTask.Result, branchTask.Result, draftTask.Result);
    }

    public async Task SaveDraftAsync(
        Guid conversationId,
        Guid? branchId,
        string content,
        IReadOnlyList<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        var actual = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (branchId is not null && !actual.Branches.Any(branch => branch.Id == branchId))
            throw new InvalidOperationException("The draft branch does not belong to this conversation.");
        if (attachmentIds.Any(id => !actual.Attachments.Any(item => item.Id == id && item.MessageId is null &&
                (item.BranchId is null || item.BranchId == branchId))))
            throw new InvalidOperationException("A draft attachment is not owned by this conversation branch.");
        await EnsureMayActAsync(conversationId, "chat.save-draft", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content) && attachmentIds.Count == 0)
        {
            await production.DeleteDraftAsync(conversationId, branchId, cancellationToken).ConfigureAwait(false);
            return;
        }
        await production.SaveDraftAsync(
            new ConversationDraft(
                conversationId,
                branchId,
                content,
                JsonSerializer.Serialize(attachmentIds),
                DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MessageAttachment> ImportAttachmentAsync(
        Guid conversationId,
        Guid? branchId,
        string path,
        CancellationToken cancellationToken)
    {
        var actual = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        RequireCurrentBranch(actual, branchId);
        await EnsureMayActAsync(conversationId, "chat.attach", cancellationToken).ConfigureAwait(false);
        return await attachmentService.ImportAsync(conversationId, null, branchId, path, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var actual = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        RequireCurrentBranch(actual, branchId);
        if (!actual.Attachments.Any(item => item.Id == attachmentId && item.MessageId is null &&
                (item.BranchId is null || item.BranchId == branchId)))
            throw new InvalidOperationException("Only a pending attachment on this conversation branch can be removed.");
        await EnsureMayActAsync(conversationId, "chat.remove-attachment", cancellationToken).ConfigureAwait(false);
        await attachmentService.DeleteAsync(attachmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken)
    {
        var actual = await GetConversationAsync(request.Conversation.Id, cancellationToken).ConfigureAwait(false);
        RequireCurrentBranch(actual, request.BranchId);
        await EnsureMayActAsync(actual.Conversation.Id, "chat.send", cancellationToken).ConfigureAwait(false);
        var inventory = await models.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        var model = inventory.Models.FirstOrDefault(item => item.Name.Equals(request.Model.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected model is no longer available in the governed inventory.");
        if (request.AttachmentIds.Any(id => !actual.Attachments.Any(item => item.Id == id && item.ConversationId == actual.Conversation.Id &&
                item.MessageId is null && (item.BranchId is null || item.BranchId == request.BranchId))))
            throw new InvalidOperationException("A submitted attachment is no longer owned by this conversation.");
        if (request.ExistingUserInput is not null)
            throw new InvalidOperationException("Existing input is admitted only by the canonical regeneration action.");
        await turns.ExecuteAsync(request with { Conversation = actual.Conversation, Model = model }, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationBranch> CreateBranchAsync(
        Guid conversationId,
        Guid messageId,
        string? name,
        CancellationToken cancellationToken)
    {
        await RequireMessageAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
        await EnsureMayActAsync(conversationId, "chat.branch", cancellationToken).ConfigureAwait(false);
        var current = await versioning.EnsureCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var branch = await production.CreateBranchAsync(
            conversationId,
            current.Id,
            messageId,
            string.IsNullOrWhiteSpace(name) ? $"Branch {DateTimeOffset.Now:g}" : name.Trim(),
            ConversationBranchReason.Manual,
            cancellationToken).ConfigureAwait(false);
        await production.SetCurrentBranchAsync(conversationId, branch.Id, cancellationToken).ConfigureAwait(false);
        return branch;
    }

    public async Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken)
    {
        var data = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (!data.Branches.Any(branch => branch.Id == branchId)) throw new InvalidOperationException("The branch does not belong to this conversation.");
        await EnsureMayActAsync(conversationId, "chat.switch-branch", cancellationToken).ConfigureAwait(false);
        await production.SetCurrentBranchAsync(conversationId, branchId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RegenerateAsync(
        Guid conversationId,
        Guid assistantMessageId,
        ModelDescriptor model,
        CancellationToken cancellationToken)
    {
        var actual = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var inventory = await models.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        model = inventory.Models.FirstOrDefault(item => item.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected model is no longer available in the governed inventory.");
        await EnsureMayActAsync(conversationId, "chat.regenerate", cancellationToken).ConfigureAwait(false);
        var conversation = actual.Conversation;
        var messages = actual.Messages;
        var index = Array.FindIndex(messages.ToArray(), item => item.Id == assistantMessageId);
        if (index < 0) throw new InvalidOperationException("The response no longer exists in this chat.");
        var assistant = messages[index];
        if (assistant.Role != MessageRole.Assistant)
            throw new InvalidOperationException("Only assistant responses can be regenerated.");
        var precedingUser = messages.Take(index).LastOrDefault(item => item.Role == MessageRole.User)
            ?? throw new InvalidOperationException("The response has no preceding user message.");
        var latestAssistant = messages.LastOrDefault(item => item.Role == MessageRole.Assistant);
        var isLatest = latestAssistant?.Id == assistantMessageId;
        var branch = await versioning.PrepareRegenerationAsync(
            conversationId,
            assistantMessageId,
            isLatest,
            isLatest ? ResponseRegenerationMode.Here : ResponseRegenerationMode.NewBranch,
            cancellationToken).ConfigureAwait(false);
        await turns.ExecuteAsync(
            new ChatSpaceTurnRequest(conversation, precedingUser.Content, model,
                actual.Attachments.Where(item => item.MessageId == precedingUser.Id).Select(item => item.Id).ToArray(), BranchId: branch.Id, ExistingUserInput: precedingUser),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken) =>
        safety.GetSnapshotAsync(conversationId, cancellationToken);

    private async Task<ChatMessage> RequireMessageAsync(Guid conversationId, Guid messageId, CancellationToken token)
    {
        var data = await GetConversationAsync(conversationId, token).ConfigureAwait(false);
        return data.Messages.FirstOrDefault(item => item.Id == messageId && item.ConversationId == conversationId)
            ?? throw new KeyNotFoundException("The message is not available in this conversation.");
    }
    public async Task<string> GetMessageContentAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) =>
        (await RequireMessageAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false)).Content;
    public async Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        await RequireMessageAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
        await conversations.DeleteMessageAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
    }
    public async Task ForgetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        await RequireMessageAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
        await EnsureMayActAsync(conversationId, "chat.forget", cancellationToken).ConfigureAwait(false);
        await conversations.MarkMessagesCompactedAsync(conversationId, [messageId], cancellationToken).ConfigureAwait(false);
    }

    public async Task EditUserMessageAsync(
        Guid conversationId,
        Guid messageId,
        string content,
        MessageEditMode mode,
        CancellationToken cancellationToken)
    {
        var message = await RequireMessageAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
        if (message.Role != MessageRole.User) throw new InvalidOperationException("Only user messages can be edited through this action.");
        await EnsureMayActAsync(conversationId, "chat.edit", cancellationToken).ConfigureAwait(false);
        await versioning.EditUserMessageAsync(conversationId, messageId, content, mode, cancellationToken).ConfigureAwait(false);
    }

    public Task<DualModelRun> RunDualAsync(string prompt, ModelDescriptor first, string secondModelName,
        DuoMode mode, CancellationToken cancellationToken) =>
        Task.FromException<DualModelRun>(new NotSupportedException("Dual-model Chat requires canonical persistence and is not available on this surface yet."));

    private async Task EnsureMayActAsync(Guid conversationId, string operation, CancellationToken cancellationToken) =>
        await safety.EnsureMayActAsync(conversationId, operation, cancellationToken).ConfigureAwait(false);

    private static void RequireCurrentBranch(ChatSpaceConversationData actual, Guid? expected)
    {
        if (expected is null || !actual.Branches.Any(branch => branch.Id == expected && branch.IsCurrent))
            throw new InvalidOperationException("The original conversation branch changed. Reopen it before continuing.");
    }
}
