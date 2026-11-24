using Haven.Core;

namespace Haven.Application;

/// <summary>Atomic create-only operation of the actual canonical conversation store.
/// True acknowledges this original insertion; false observes an existing ID and never adopts or overwrites it.</summary>
public interface IConversationCreateOnlyRepository
{
    Task<bool> TryCreateConversationAsync(Conversation original, CancellationToken cancellationToken);
}

/// <summary>Explicit conversion of one started Temporary Chat to local history. This is a
/// storage primitive, not consent to convert, run a model, or enable memory or learning.</summary>
public interface ITemporaryChatSaveRepository
{
    Task<TemporaryChatSaveReceipt> SaveTemporaryChatAsync(TemporaryChatSaveRequest request, CancellationToken cancellationToken);
}

/// <summary>The exact session and composer snapshot admitted after confirmation. An existing
/// local placeholder, branch, or draft must match the caller's previously acknowledged ownership
/// snapshot; a coincident empty row must never be silently adopted. Attachment IDs must be the
/// complete already-issued local pending set. A caller with unsupported transient or historical
/// attachments must not call this history-only transfer primitive.</summary>
public sealed record TemporaryChatSaveRequest(Conversation Original, IReadOnlyList<ChatMessage> History,
    string DraftText, IReadOnlyList<Guid> PendingAttachmentIds, Conversation? ExpectedPlaceholder = null,
    ConversationDraft? ExpectedDraft = null, Guid? ExpectedBranchId = null);

/// <summary>Acknowledges the single committed local transaction.</summary>
public sealed record TemporaryChatSaveReceipt(Conversation Conversation, Guid BranchId);
