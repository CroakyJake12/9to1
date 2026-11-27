using Haven.Core;

namespace Haven.Application;

/// <summary>
/// An optional capability of the original conversation repository. Binding does not create a
/// branch or grant permission: the first persisted user message must atomically admit the
/// requested current branch. Later messages and context stay on that immutable original branch.
/// </summary>
public interface IConversationBranchBoundRepository
{
    IConversationBranchInvocation BindInvocation(Guid conversationId, Guid branchId);
}

/// <summary>
/// An ordinary invocation whose first create-only user write atomically selects the current
/// branch. Acquiring the handle performs no I/O; its branch is known only after input commits.
/// </summary>
public interface IConversationCurrentBranchBoundRepository : IConversationBranchBoundRepository
{
    IConversationBranchInvocation BindCurrentInvocation(Guid conversationId);
}

/// <summary>
/// One explicit invocation on the same canonical repository, never ambient thread state.
/// Closing prevents new operations and joins operations already admitted by this handle.
/// </summary>
public interface IConversationBranchInvocation : IAsyncDisposable
{
    Guid ConversationId { get; }
    /// <summary>The admitted branch; a current-branch handle refuses access before its first input commits.</summary>
    Guid BranchId { get; }
    Task AdmitExistingUserInputAsync(ChatMessage message, CancellationToken cancellationToken);
    Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken);
    /// <summary>Atomically persists the original user input and claims its still-pending attachments.</summary>
    Task AddUserMessageWithAttachmentsAsync(ChatMessage message, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(CancellationToken cancellationToken);
}
