using Haven.Core;

namespace Haven.Application;

/// <summary>Immutable owning row CAS. Existing rows may change only SpaceId and UpdatedAt.</summary>
public sealed record ConversationSpaceChange(Conversation? Expected, Conversation Proposed);
public enum ConversationSpaceCommitPhase { Admission, Publication }
public enum ConversationSpaceCommitStatus { Committed, RevisionConflict, AdmissionRejected, StoreMismatch }
public sealed record ConversationSpaceCommitContext(ResourceStoreIdentity StoreIdentity,
    IReadOnlyList<ConversationSpaceChange> Changes, ConversationSpaceCommitPhase Phase);
public sealed record ConversationSpaceCommitResult(ConversationSpaceCommitStatus Status);

/// <summary>Trusted host admission supplied from current actor and explicit conversation-store Home
/// binding receipts, plus the owning Space revision if required. Must not reenter this SQL store.</summary>
public interface IConversationSpaceCommitAdmission
{
    ValueTask<bool> CheckAsync(ConversationSpaceCommitContext context, CancellationToken cancellationToken);
}
public interface IConversationSpaceCommitStore
{
    Task<ConversationSpaceCommitResult> CompareExchangeSpaceAsync(Guid expectedStoreId,
        IReadOnlyList<ConversationSpaceChange> changes, IConversationSpaceCommitAdmission admission,
        CancellationToken cancellationToken = default);
}
