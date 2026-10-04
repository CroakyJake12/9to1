using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

public sealed record ChatSpaceCitation(
    int Number,
    string Title,
    string Source,
    string? Uri,
    string Excerpt);

public sealed record ChatSpaceMessage(
    Guid Id,
    MessageRole Role,
    string Content,
    string SpeakerLabel,
    string CreatedLabel,
    string? ModelName,
    IReadOnlyList<MessageAttachment> Attachments,
    IReadOnlyList<ChatSpaceCitation> Citations,
    IReadOnlyList<ToolActivity> ToolActivities,
    bool CanRegenerate);

public sealed record ChatSpaceBranch(
    Guid Id,
    string Name,
    ConversationBranchReason Reason,
    bool IsCurrent);

public sealed record ChatSpaceConversation(
    Guid Id,
    string Title,
    IReadOnlyList<ChatSpaceMessage> Messages,
    IReadOnlyList<ChatSpaceBranch> Branches,
    Guid? CurrentBranchId);

public sealed record ChatSpaceRecentChat(
    Guid Id,
    string Title,
    string UpdatedLabel,
    DateTimeOffset UpdatedAt,
    bool IsCurrent);

public sealed record ChatSpaceModelChoice(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlySet<ToolCapability> Capabilities);

public sealed record ChatSpaceModelInventory(
    IReadOnlyList<ModelDescriptor> Models,
    string? PreferredModelName = null);

public sealed record ChatSpaceComposer(
    string Draft,
    IReadOnlyList<MessageAttachment> Attachments,
    string? SelectedModelName,
    bool IsSending)
{
    public bool CanSend =>
        !IsSending &&
        !string.IsNullOrWhiteSpace(SelectedModelName) &&
        (!string.IsNullOrWhiteSpace(Draft) || Attachments.Count > 0);
}

public enum ChatSpaceStatusTone
{
    Neutral,
    Progress,
    Success,
    Error
}

public sealed record ChatSpaceStatus(string Message, ChatSpaceStatusTone Tone, bool Announce);

public sealed record ChatSpaceViewState(
    IReadOnlyList<ChatSpaceRecentChat> RecentChats,
    ChatSpaceConversation? Conversation,
    IReadOnlyList<ChatSpaceModelChoice> Models,
    ChatSpaceComposer Composer,
    ChatSpaceStatus Status,
    bool IsLoading,
    int Revision)
{
    public static ChatSpaceViewState Empty { get; } = new(
        [],
        null,
        [],
        new ChatSpaceComposer(string.Empty, [], null, false),
        new ChatSpaceStatus("Ready", ChatSpaceStatusTone.Neutral, false),
        false,
        0);
}

public sealed record ChatSpaceTurnRequest(
    Conversation Conversation,
    string Prompt,
    ModelDescriptor Model,
    IReadOnlyList<Guid> AttachmentIds);

/// <summary>
/// Host adapter for the existing mature Chat turn pipeline. Implementations must retain the
/// production capability registry, approval policy, model governance, streaming, and persistence.
/// </summary>
public interface IChatSpaceTurnExecutor
{
    Task ExecuteAsync(ChatSpaceTurnRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Adapter to the host's governed model inventory and preferred model selection.
/// </summary>
public interface IChatSpaceModelCatalog
{
    Task<ChatSpaceModelInventory> GetInventoryAsync(CancellationToken cancellationToken);
}

public interface IChatSpaceBackend
{
    Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken cancellationToken);
    Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken cancellationToken);
    Task<Conversation> CreateChatAsync(CancellationToken cancellationToken);
    Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken);
    Task SaveDraftAsync(Guid conversationId, Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken);
    Task<MessageAttachment> ImportAttachmentAsync(Guid conversationId, Guid? branchId, string path, CancellationToken cancellationToken);
    Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken);
    Task SendAsync(ChatSpaceTurnRequest request, CancellationToken cancellationToken);
    Task<ConversationBranch> CreateBranchAsync(Guid conversationId, Guid messageId, string? name, CancellationToken cancellationToken);
    Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken);
    Task RegenerateAsync(Guid conversationId, Guid assistantMessageId, ModelDescriptor model, CancellationToken cancellationToken);
}

public sealed record ChatSpaceConversationData(
    Conversation Conversation,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<MessageAttachment> Attachments,
    IReadOnlyList<ConversationBranch> Branches,
    ConversationDraft? Draft);
