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
    IChatSpaceTurnExecutor turns) : IChatSpaceBackend
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

    public Task<MessageAttachment> ImportAttachmentAsync(
        Guid conversationId,
        Guid? branchId,
        string path,
        CancellationToken cancellationToken) =>
        attachmentService.ImportAsync(conversationId, null, branchId, path, null, cancellationToken);

    public Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        attachmentService.DeleteAsync(attachmentId, cancellationToken);

    public Task SendAsync(ChatSpaceTurnRequest request, CancellationToken cancellationToken) =>
        turns.ExecuteAsync(request, cancellationToken);

    public async Task<ConversationBranch> CreateBranchAsync(
        Guid conversationId,
        Guid messageId,
        string? name,
        CancellationToken cancellationToken)
    {
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

    public Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken) =>
        production.SetCurrentBranchAsync(conversationId, branchId, cancellationToken);

    public async Task RegenerateAsync(
        Guid conversationId,
        Guid assistantMessageId,
        ModelDescriptor model,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Chat '{conversationId}' was not found.");
        var messages = await conversations.GetMessagesAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var index = Array.FindIndex(messages.ToArray(), item => item.Id == assistantMessageId);
        if (index < 0) throw new InvalidOperationException("The response no longer exists in this chat.");
        var assistant = messages[index];
        if (assistant.Role != MessageRole.Assistant)
            throw new InvalidOperationException("Only assistant responses can be regenerated.");
        var precedingUser = messages.Take(index).LastOrDefault(item => item.Role == MessageRole.User)
            ?? throw new InvalidOperationException("The response has no preceding user message.");
        var latestAssistant = messages.LastOrDefault(item => item.Role == MessageRole.Assistant);
        var isLatest = latestAssistant?.Id == assistantMessageId;
        await versioning.PrepareRegenerationAsync(
            conversationId,
            assistantMessageId,
            isLatest,
            isLatest ? ResponseRegenerationMode.Here : ResponseRegenerationMode.NewBranch,
            cancellationToken).ConfigureAwait(false);
        await turns.ExecuteAsync(
            new ChatSpaceTurnRequest(conversation, precedingUser.Content, model, []),
            cancellationToken).ConfigureAwait(false);
    }
}
