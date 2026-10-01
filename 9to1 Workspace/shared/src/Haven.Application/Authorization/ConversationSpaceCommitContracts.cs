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
public enum ConversationSpaceReadStatus { Available, StoreMismatch }
/// <summary>One SQL read snapshot, including archived and temporary rows. This is not a grant or
/// proof of atomic completion with another store; callers must apply their current owning authority.</summary>
public sealed record ConversationSpaceMembershipPage(ConversationSpaceReadStatus Status,
    ResourceStoreIdentity StoreIdentity, Guid SpaceId, IReadOnlyList<Conversation> Rows, bool HasMore);
public interface IConversationSpaceCommitStore
{
    Task<ConversationSpaceMembershipPage> ReadSpaceMembershipAsync(Guid expectedStoreId, Guid spaceId,
        Guid? afterId = null, int limit = 1000, CancellationToken cancellationToken = default);

    Task<ConversationSpaceCommitResult> CompareExchangeSpaceAsync(Guid expectedStoreId,
        IReadOnlyList<ConversationSpaceChange> changes, IConversationSpaceCommitAdmission admission,
        CancellationToken cancellationToken = default);
}
