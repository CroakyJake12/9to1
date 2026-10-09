using Haven.Core;

namespace Haven.Application;

public enum CanonicalAttachmentDraftMutation { Import, Detach }

public interface ICanonicalAttachmentConversationRead
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    Conversation OriginalConversation { get; }
    Guid? BranchId { get; }
}

/// <summary>Source-issued exact destination and immutable source provenance.
/// Copied IDs, draft text and file metadata never constitute an import claim.</summary>
public interface ICanonicalAttachmentImportIntent
{
    AuthenticatedResourceActor Actor { get; }
    Guid OperationId { get; }
    CanonicalAttachmentDraftMutation Mutation { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    VerifiedResourceStoreOwnership OriginalDenOwnership { get; }
    string DenId { get; }
    string NamespaceId { get; }
    string DefinitionId { get; }
    long DefinitionRevision { get; }
    string SessionId { get; }
    long SessionRevision { get; }
    Conversation OriginalConversation { get; }
    ConversationDraft? OriginalDraft { get; }
    ConversationDraft CandidateDraft { get; }
    MessageAttachment Attachment { get; }
    ICanonicalAttachmentOriginalSelection? OriginalSelection { get; }
    OriginalMessageAttachmentProcessingResult? OriginalProcessing { get; }
}

public interface ICanonicalAttachmentImportAcknowledgment
{
    ICanonicalAttachmentImportIntent OriginalIntent { get; }
    bool Applied { get; }
    string Reason { get; }
    MessageAttachment Attachment { get; }
    ConversationDraft Draft { get; }
}

public interface ICanonicalAttachmentImportSource
{
    bool IsIssuedOriginalConversationRead(ICanonicalAttachmentConversationRead read);
    bool IsIssuedOriginalImportIntent(ICanonicalAttachmentImportIntent intent);
    string GetOriginalImportIntentDigest(ICanonicalAttachmentImportIntent intent);
    Task ValidateOriginalImportIntentWithinSourceAsync(ICanonicalAttachmentImportIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalImportCommit(ICanonicalAttachmentImportIntent intent);
    Task<ICanonicalAttachmentImportAcknowledgment> CommitOriginalImportWithinSourceAsync(
        ICanonicalAttachmentImportIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsOriginalAtomicImportTask(ICanonicalAttachmentImportIntent intent, Task<ICanonicalAttachmentImportAcknowledgment> actual);
    bool IsOwnedOriginalImportAcknowledgment(ICanonicalAttachmentImportIntent intent,
        ICanonicalAttachmentImportAcknowledgment acknowledgment, Task<ICanonicalAttachmentImportAcknowledgment> actual);
}

public interface ICanonicalAttachmentHomeImportClaim : IAsyncDisposable
{
    ICanonicalAttachmentImportIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

public interface ICanonicalAttachmentHomeImportSource
{
    bool IsAcknowledgedOriginalImportRefusal(Task sameAcquisition) => false;
    Task<ICanonicalAttachmentHomeImportClaim> AcquireOriginalImportWithinSourceAsync(ICanonicalAttachmentImportIntent intent,
        Action<Action> scope, Action<Task> retain, Action<ICanonicalAttachmentHomeImportClaim>? capture, CancellationToken token);
    bool IsIssuedOriginalImportClaim(ICanonicalAttachmentHomeImportClaim claim, ICanonicalAttachmentImportIntent intent);
    Task AcquireOriginalCommitEntryWithinSourceAsync(ICanonicalAttachmentHomeImportClaim claim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ValidateOriginalCommitWithinSourceAsync(ICanonicalAttachmentHomeImportClaim claim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalCommit(ICanonicalAttachmentHomeImportClaim claim, ICanonicalAttachmentImportIntent intent);
    void RetainOriginalSqlCommit(ICanonicalAttachmentHomeImportClaim claim, Task<ICanonicalAttachmentImportAcknowledgment> actual);
    bool IsIssuedOriginalSettlementReleasePhase(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAttachmentImportIntent, ICanonicalAttachmentImportAcknowledgment> phase) => false;
    Task CompleteOriginalImportWithinSourceAsync(ICanonicalAttachmentHomeImportClaim claim,
        Task<ICanonicalAttachmentImportAcknowledgment> actual, Action<Action> scope, Action<Task> retain, CancellationToken token);
}
