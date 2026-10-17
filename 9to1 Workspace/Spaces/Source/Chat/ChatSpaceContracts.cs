using Haven.Application;
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
    bool CanRegenerate,
    string? Thinking = null,
    bool IsStreaming = false,
    string? ProgressLabel = null,
    bool CanEdit = false,
    bool AvatarVisible = false,
    string? AvatarSource = null)
{
    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);
}

public sealed record ChatSpaceBranch(
    Guid Id,
    string Name,
    ConversationBranchReason Reason,
    bool IsCurrent)
{
    /// <summary>
    /// Returns the branch name so native selector controls that lack an explicit display-member
    /// binding render the intended label instead of the record's CLR format.
    /// </summary>
    public override string ToString() => Name;
}

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
    IReadOnlySet<ToolCapability> Capabilities)
{
    /// <summary>
    /// Returns the human-readable model label so native selector controls that lack an explicit
    /// display-member binding render the intended name instead of the record's CLR format.
    /// </summary>
    public override string ToString() => DisplayName;
}

public sealed record ChatSpaceModelInventory(
    IReadOnlyList<ModelDescriptor> Models,
    string? PreferredModelName = null);

public sealed record ChatSpaceThreadSettings(
    ChatActionMode ActionMode,
    GenerativeUiResponseMode ResponseMode);

public sealed record ChatSpaceDualModelState(
    bool IsActive,
    string? SecondModelName,
    bool IsRunning,
    DuoMode Mode,
    int SecondModelIndex = -1);

public sealed record ChatSpaceLayout(
    string? Columns = null,
    string? Rows = null,
    int ConversationColumn = 0,
    int ConversationRow = 0,
    double? RecentHeight = null);

public sealed record ChatSpaceComposer(
    string Draft,
    IReadOnlyList<MessageAttachment> Attachments,
    string? SelectedModelName,
    bool IsSending,
    int SelectedModelIndex = -1,
    bool IsEditing = false,
    Guid? EditingMessageId = null)
{
    public bool CanSend =>
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
    int Revision,
    ChatSpaceLayout? Layout = null,
    bool IsSafetyLocked = false,
    ChatSpaceThreadSettings? ThreadSettings = null,
    ChatSpaceDualModelState? DualModel = null,
    int SelectedBranchIndex = -1)
{
    public static ChatSpaceViewState Empty { get; } = new(
        [],
        null,
        [],
        new ChatSpaceComposer(string.Empty, [], null, false),
        new ChatSpaceStatus("Ready", ChatSpaceStatusTone.Neutral, false),
        false,
        0,
        new ChatSpaceLayout("280px 1fr", "1fr", 1, 0, null));
}

public sealed record ChatSpaceTurnRequest(
    Conversation Conversation,
    string Prompt,
    ModelDescriptor Model,
    IReadOnlyList<Guid> AttachmentIds,
    ChatActionMode ActionMode = ChatActionMode.AllowBasicActions,
    GenerativeUiResponseMode ResponseMode = GenerativeUiResponseMode.Auto,
    DuoMode DuoMode = DuoMode.Solo,
    string? SecondModelName = null,
    Guid? BranchId = null,
    ChatMessage? ExistingUserInput = null,
    Func<ChatMessage, CancellationToken, Task>? InputAccepted = null,
    Action<ChatStreamEvent>? OnStreamEvent = null);

/// <summary>
/// Host adapter for the existing mature Chat turn pipeline. Implementations must retain the
/// production capability registry, approval policy, model governance, streaming, and persistence.
/// </summary>
public interface IChatSpaceTurnExecutor
{
    Task ExecuteAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Backward-compatible dispatch for callers that do not need live streaming deltas.
    /// </summary>
    Task ExecuteAsync(ChatSpaceTurnRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request, null, cancellationToken);
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
    Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken);
    Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken);
    Task<ConversationBranch> CreateBranchAsync(Guid conversationId, Guid messageId, string? name, CancellationToken cancellationToken);
    Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken);
    Task RegenerateAsync(Guid conversationId, Guid assistantMessageId, ModelDescriptor model, CancellationToken cancellationToken);

    // Governed safety state
    Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken);

    // Per-message actions
    Task<string> GetMessageContentAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken);
    Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken);
    Task ForgetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken);
    Task EditUserMessageAsync(Guid conversationId, Guid messageId, string content, MessageEditMode mode, CancellationToken cancellationToken);

    // Dual-model forwarding
    Task<DualModelRun> RunDualAsync(string prompt, ModelDescriptor first, string secondModelName, DuoMode mode, CancellationToken cancellationToken);

    /// <summary>
    /// Backward-compatible send without streaming.
    /// </summary>
    Task SendAsync(ChatSpaceTurnRequest request, CancellationToken cancellationToken) =>
        SendAsync(request, null, cancellationToken);
}

public sealed record ChatSpaceConversationData(
    Conversation Conversation,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<MessageAttachment> Attachments,
    IReadOnlyList<ConversationBranch> Branches,
    ConversationDraft? Draft);
