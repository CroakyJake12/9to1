using Haven.Core;

namespace Haven.Application;

/// <summary>Private canonical producer-issued intent. Every property is observation only;
/// caller implementations, record IDs and scope text confer no WRITE authority.</summary>
public interface ICanonicalAssistantMemoryWriteIntent
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    VerifiedResourceStoreOwnership OriginalDenOwnership { get; }
    string DenId { get; }
    string NamespaceId { get; }
    string DefinitionId { get; }
    long DefinitionRevision { get; }
    string OriginalStorageScope { get; }
    Guid OperationId { get; }
    KnowledgeRecord Candidate { get; }
    IChatOriginalPersistentMemoryInput OriginalProductInput { get; }
    IChatOriginalPersistentMemorySource OriginalProductSource { get; }
    Conversation OriginalConversation { get; }
    ProviderExecutionContext? OriginalCanonicalContext { get; }
}

public interface ICanonicalAssistantMemoryWriteAcknowledgment
{
    ICanonicalAssistantMemoryWriteIntent OriginalIntent { get; }
    KnowledgeRecord Record { get; }
    RetrievalDocument OriginalRetrievalDocument { get; }
    Guid OperationId { get; }
}

/// <summary>SAME maintained Knowledge Library producer. Approval pins the exact source
/// intent; only its actual atomic SQL child may issue a saved-record acknowledgment.</summary>
public interface ICanonicalAssistantMemoryWriteSource
{
    bool IsIssuedOriginalWriteIntent(ICanonicalAssistantMemoryWriteIntent intent);
    string GetOriginalWriteIntentDigest(ICanonicalAssistantMemoryWriteIntent intent);
    Task ValidateOriginalWriteIntentWithinSourceAsync(ICanonicalAssistantMemoryWriteIntent intent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    void DemandOriginalWriteCommit(ICanonicalAssistantMemoryWriteIntent intent);
    Task<ICanonicalAssistantMemoryWriteAcknowledgment> CommitOriginalWriteWithinSourceAsync(
        ICanonicalAssistantMemoryWriteIntent intent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsOriginalAtomicWriteTask(ICanonicalAssistantMemoryWriteIntent intent,
        Task<ICanonicalAssistantMemoryWriteAcknowledgment> actualAtomicTask);
    bool IsOwnedOriginalWriteAcknowledgment(ICanonicalAssistantMemoryWriteIntent intent,
        ICanonicalAssistantMemoryWriteAcknowledgment acknowledgment,
        Task<ICanonicalAssistantMemoryWriteAcknowledgment> actualAtomicTask);
}

/// <summary>Separate individually accepted Home WRITE. The assistant.memory store import
/// and the source's READ input cannot mint or substitute for this live claim.</summary>
public interface ICanonicalAssistantMemoryHomeWriteClaim : IAsyncDisposable
{
    ICanonicalAssistantMemoryWriteIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

public interface ICanonicalAssistantMemoryHomeWriteSource
{
    bool IsAcknowledgedOriginalWriteRefusal(Task sameOriginalAcquisition) => false;
    Task<ICanonicalAssistantMemoryHomeWriteClaim> AcquireOriginalWriteWithinSourceAsync(
        ICanonicalAssistantMemoryWriteIntent sameIntent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, Action<ICanonicalAssistantMemoryHomeWriteClaim>? captureOriginalClaim,
        CancellationToken token);
    bool IsIssuedOriginalWriteClaim(ICanonicalAssistantMemoryHomeWriteClaim sameClaim,
        ICanonicalAssistantMemoryWriteIntent sameIntent);
    Task AcquireOriginalCommitEntryWithinSourceAsync(ICanonicalAssistantMemoryHomeWriteClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task ValidateOriginalCommitWithinSourceAsync(ICanonicalAssistantMemoryHomeWriteClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Pure captured checks plus the SAME producer's native pin; no Home/SQLite reentry.
    void DemandOriginalCommit(ICanonicalAssistantMemoryHomeWriteClaim sameClaim,
        ICanonicalAssistantMemoryWriteIntent sameIntent);
    void RetainOriginalSqlCommit(ICanonicalAssistantMemoryHomeWriteClaim sameClaim,
        Task<ICanonicalAssistantMemoryWriteAcknowledgment> sameOriginalAtomicSqlTask);
    // Joins actual atomic Task, then releases held Home/completion BEFORE outcome audit.
    // Pure private phase/cached raw-release proof from the SAME actual issuer.
    // Enables only original pin cleanup, never another mutation or SQL dispatch.
    bool IsIssuedOriginalSettlementReleasePhase(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAssistantMemoryWriteIntent, ICanonicalAssistantMemoryWriteAcknowledgment> samePhase) => false;
    Task CompleteOriginalWriteWithinSourceAsync(ICanonicalAssistantMemoryHomeWriteClaim sameClaim,
        Task<ICanonicalAssistantMemoryWriteAcknowledgment> sameOriginalAtomicSqlTask,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
