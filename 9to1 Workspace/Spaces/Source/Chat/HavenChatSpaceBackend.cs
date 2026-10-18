using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>
/// Thin adapter over Haven's existing Chat repositories and services. It owns no persistence and
/// delegates every stored mutation to the shared production contracts.
/// </summary>
public sealed class HavenChatSpaceBackend : IChatSpaceBackend
{
    private readonly IConversationRepository conversations;
    private readonly IConversationProductionRepository production;
    private readonly IConversationVersioningService versioning;
    private readonly IMessageAttachmentService attachmentService;
    private readonly IChatSpaceModelCatalog models;
    private readonly IChatSpaceTurnExecutor turns;
    private readonly IConversationSafetyService safety;
    private readonly SpaceRegistry? registry;
    private readonly SpaceScope scope = SpaceScope.Chat;

    // Keep this actual overload: extensions already compiled against seven parameters retain
    // their Chat-only boundary rather than being rebound to a newly optional parameter.
    public HavenChatSpaceBackend(IConversationRepository conversations,
        IConversationProductionRepository production, IConversationVersioningService versioning,
        IMessageAttachmentService attachmentService, IChatSpaceModelCatalog models,
        IChatSpaceTurnExecutor turns, IConversationSafetyService safety)
    {
        this.conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        this.production = production ?? throw new ArgumentNullException(nameof(production));
        this.versioning = versioning ?? throw new ArgumentNullException(nameof(versioning));
        this.attachmentService = attachmentService ?? throw new ArgumentNullException(nameof(attachmentService));
        this.models = models ?? throw new ArgumentNullException(nameof(models));
        this.turns = turns ?? throw new ArgumentNullException(nameof(turns));
        this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
    }

    /// <summary>One immutable selected-Space boundary per hosted view. The supplied context is
    /// identity, never a durable permission grant; every operation reads current membership.</summary>
    public HavenChatSpaceBackend(IConversationRepository conversations,
        IConversationProductionRepository production, IConversationVersioningService versioning,
        IMessageAttachmentService attachmentService, IChatSpaceModelCatalog models,
        IChatSpaceTurnExecutor turns, IConversationSafetyService safety,
        SpacesContext context, SpaceRegistry registry)
        : this(conversations, production, versioning, attachmentService, models, turns, safety)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        scope = context.Scope;
        if (scope.RegisteredSpaceId is not { } id || id == Guid.Empty ||
            context.RegisteredSpace is not { } registered || registered.Id != id ||
            registered.IsArchived || context.Definition.IsBuiltIn != registered.IsBuiltIn ||
            scope != ScopeFor(registered))
            throw new InvalidOperationException("The selected Space context does not match its registered identity.");
    }

    public Guid SpaceId => scope.RegisteredSpaceId!.Value;

    public async Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken cancellationToken)
    {
        await RequireActiveSpaceAsync(cancellationToken).ConfigureAwait(false);
        var maximum = Math.Clamp(limit, 1, 100);
        // Explicitly scoped queries filter by Space BEFORE the repository applies LIMIT.
        // Legacy unassigned general Chats remain readable only in built-in Chat. Neither
        // selecting Chat nor reading its history migrates or rewrites their saved IDs.
        var rows = registry is null
            ? await conversations.GetRecentInScopeAsync(ConversationScope.GeneralChat, maximum, cancellationToken).ConfigureAwait(false)
            : await conversations.GetBySpaceAsync(SpaceId, maximum, cancellationToken).ConfigureAwait(false);
        if (registry is not null && SpaceId == SpaceRegistry.ChatSpaceId)
        {
            var legacy = await conversations.GetUnassignedGeneralChatsAsync(maximum, cancellationToken).ConfigureAwait(false);
            rows = rows.Concat(legacy).DistinctBy(item => item.Id).ToArray();
        }
        var result = new List<Conversation>();
        foreach (var row in rows.Where(IsMember).Where(item => !item.IsArchived && !item.IsTemporary))
        {
            var current = await conversations.GetAsync(row.Id, cancellationToken).ConfigureAwait(false);
            if (current is not null && IsMember(current) && !current.IsArchived && !current.IsTemporary)
                result.Add(current);
        }
        await RequireActiveSpaceAsync(cancellationToken).ConfigureAwait(false);
        return result.OrderByDescending(item => item.UpdatedAt).Take(maximum).ToArray();
    }

    public async Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken cancellationToken)
    {
        await RequireActiveSpaceAsync(cancellationToken).ConfigureAwait(false);
        var result = await models.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        await RequireActiveSpaceAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<Conversation> CreateChatAsync(CancellationToken cancellationToken)
    {
        await RequireActiveSpaceAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var study = SpaceId == SpaceRegistry.StudySpaceId;
        var conversation = new Conversation(
            Guid.NewGuid(), study ? HavenMode.Study : HavenMode.Chat,
            study ? ConversationKind.QuickChat : ConversationKind.Chat, "New chat", null, null,
            false, false, now, now, SpaceId: SpaceId);
        cancellationToken.ThrowIfCancellationRequested();
        await conversations.UpsertConversationAsync(conversation, cancellationToken).ConfigureAwait(false);
        await RequireConversationAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
        await production.EnsureRootBranchAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
        return await RequireConversationAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);

        var current = await production.GetCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false)
            ?? await production.EnsureRootBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var messageTask = conversations.GetMessagesAsync(conversationId, cancellationToken);
        var attachmentTask = production.GetAttachmentsAsync(conversationId, null, cancellationToken);
        var branchTask = production.GetBranchesAsync(conversationId, cancellationToken);
        var draftTask = production.GetDraftAsync(conversationId, current.Id, cancellationToken);
        await Task.WhenAll(messageTask, attachmentTask, branchTask, draftTask).ConfigureAwait(false);
        var publishedBranch = await production.GetCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (publishedBranch?.Id != current.Id || current.ConversationId != conversationId ||
            publishedBranch.ConversationId != conversationId ||
            messageTask.Result.Any(item => item.ConversationId != conversationId) ||
            attachmentTask.Result.Any(item => item.ConversationId != conversationId) ||
            branchTask.Result.Any(item => item.ConversationId != conversationId) ||
            draftTask.Result is { } draft && (draft.ConversationId != conversationId || draft.BranchId != current.Id))
            throw new InvalidOperationException("The conversation branch changed while its content was loading.");
        conversation = await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
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
        RequireCurrentBranch(await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false), branchId);
        var imported = await attachmentService.ImportAsync(conversationId, null, branchId, path, null, cancellationToken).ConfigureAwait(false);
        await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return imported;
    }

    public async Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var actual = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        RequirePendingAttachment(actual, branchId, attachmentId);
        await EnsureMayActAsync(conversationId, "chat.remove-attachment", cancellationToken).ConfigureAwait(false);
        // Safety may suspend while a send claims this attachment. Fresh validation
        // gives an actionable failure; the repository guard closes the final race.
        actual = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        RequirePendingAttachment(actual, branchId, attachmentId);
        if (!await attachmentService.TryDeletePendingAsync(attachmentId, conversationId, branchId!.Value, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The pending attachment or its conversation branch changed before removal.");
    }

    private static void RequirePendingAttachment(ChatSpaceConversationData actual, Guid? branchId, Guid attachmentId)
    {
        RequireCurrentBranch(actual, branchId);
        if (!actual.Attachments.Any(item => item.Id == attachmentId && item.ConversationId == actual.Conversation.Id
                && item.MessageId is null && (item.BranchId is null || item.BranchId == branchId)))
            throw new InvalidOperationException("Only a pending attachment on this conversation branch can be removed.");
    }

    public async Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken)
    {
        var actual = await GetConversationAsync(request.Conversation.Id, cancellationToken).ConfigureAwait(false);
        RequireCurrentBranch(actual, request.BranchId);
        await EnsureMayActAsync(actual.Conversation.Id, "chat.send", cancellationToken).ConfigureAwait(false);
        var inventory = await models.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        var model = inventory.Models.FirstOrDefault(item => item.Name.Equals(request.Model.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected model is no longer available in the governed inventory.");
        // The inventory lookup may await a provider: do not dispatch the earlier membership,
        // branch, attachment or safety snapshot if another action changed it while waiting.
        actual = await GetConversationAsync(request.Conversation.Id, cancellationToken).ConfigureAwait(false);
        RequireCurrentBranch(actual, request.BranchId);
        await EnsureMayActAsync(actual.Conversation.Id, "chat.send", cancellationToken).ConfigureAwait(false);
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
        await EnsureMayActAsync(conversationId, "chat.branch", cancellationToken).ConfigureAwait(false);
        await production.SetCurrentBranchAsync(conversationId, branch.Id, cancellationToken).ConfigureAwait(false);
        await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
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
        var admitted = await GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        RequireCurrentBranch(admitted, branch.Id);
        await EnsureMayActAsync(conversationId, "chat.regenerate", cancellationToken).ConfigureAwait(false);
        await turns.ExecuteAsync(
            new ChatSpaceTurnRequest(admitted.Conversation, precedingUser.Content, model,
                actual.Attachments.Where(item => item.MessageId == precedingUser.Id).Select(item => item.Id).ToArray(), BranchId: branch.Id, ExistingUserInput: precedingUser),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var snapshot = await safety.GetSnapshotAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

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
        await EnsureMayActAsync(conversationId, "chat.delete-message", cancellationToken).ConfigureAwait(false);
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

    private async Task EnsureMayActAsync(Guid conversationId, string operation, CancellationToken cancellationToken)
    {
        await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await safety.EnsureMayActAsync(conversationId, operation, cancellationToken).ConfigureAwait(false);
        await RequireConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Conversation> RequireConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await RequireActiveSpaceAsync(cancellationToken).ConfigureAwait(false);
        var current = await conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Chat '{conversationId}' was not found.");
        cancellationToken.ThrowIfCancellationRequested();
        if (current.Id != conversationId || !IsMember(current) || current.IsArchived)
            throw new InvalidOperationException("The conversation is not available in the selected Space.");
        await RequireActiveSpaceAsync(cancellationToken).ConfigureAwait(false);
        return current;
    }

    private bool IsMember(Conversation conversation)
    {
        var belongs = conversation.SpaceId == SpaceId ||
            conversation.SpaceId is null && SpaceId == SpaceRegistry.ChatSpaceId && ConversationScope.GeneralChat.Matches(conversation);
        if (!belongs) return false;
        if (registry is null) return ConversationScope.GeneralChat.Matches(conversation);
        return conversation.Mode == HavenMode.Chat && conversation.Kind == ConversationKind.Chat ||
            conversation.Mode == HavenMode.Study && conversation.Kind is ConversationKind.QuickChat or ConversationKind.LessonChat;
    }

    private async Task RequireActiveSpaceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registry is null) return; // Original Chat-only constructor has no registry dependency.
        var current = await registry.ReadExistingAsync(SpaceId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (current is null || current.IsArchived || scope != ScopeFor(current))
            throw new InvalidOperationException("The selected Space is no longer available.");
        // This is an availability/membership check, not a grant of model, tool or source access.
        // Canonical safety and executor permissions remain mandatory at their owning boundaries.
    }

    private static SpaceScope ScopeFor(SpaceDefinition space)
    {
        if (!space.IsBuiltIn) return SpaceScope.ForCustom(space.Id);
        return new[] { SpaceScope.Chat, SpaceScope.Study, SpaceScope.Tasks, SpaceScope.Agent,
            SpaceScope.Shopping, SpaceScope.Research, SpaceScope.Translate, SpaceScope.Experiences }
            .Single(item => item.RegisteredSpaceId == space.Id);
    }

    private static void RequireCurrentBranch(ChatSpaceConversationData actual, Guid? expected)
    {
        if (expected is null || !actual.Branches.Any(branch => branch.Id == expected && branch.ConversationId == actual.Conversation.Id && branch.IsCurrent))
            throw new InvalidOperationException("The original conversation branch changed. Reopen it before continuing.");
    }
}
