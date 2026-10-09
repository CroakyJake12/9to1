using Haven.Core;

namespace Haven.Application;

/// <summary>Private maintained producer-issued setup intent. Its observations and saved
/// keys grant neither WRITE nor tool execution. Importing canonical.sqlite grants no setup WRITE.</summary>
public interface ICapabilityOriginalInitializationIntent
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    Guid OperationId { get; }
    IReadOnlyList<CapabilityDefinition> MissingDefinitions { get; }
    bool IsRecoveredOperation { get; }
}

public interface ICapabilityOriginalInitializationAcknowledgment
{
    ICapabilityOriginalInitializationIntent OriginalIntent { get; }
    Guid OperationId { get; }
    IReadOnlyList<Guid> InsertedDefinitionIds { get; }
    bool IsRecoveredOperation { get; }
}

/// <summary>Initializes missing SAME maintained builtins with INSERT-only SQL. Existing
/// disabled/custom rows survive unchanged. Only a separate current Home setup WRITE admits effects.</summary>
public interface ICapabilityOriginalInitializationSource
{
    Task<ICapabilityOriginalInitializationIntent> PrepareOriginalInitializationWithinSourceAsync(
        AuthenticatedResourceActor sameActor, Guid operationId, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalInitializationIntent(ICapabilityOriginalInitializationIntent sameIntent);
    string GetOriginalInitializationIntentDigest(ICapabilityOriginalInitializationIntent sameIntent);
    Task RevalidateOriginalInitializationIntentWithinSourceAsync(ICapabilityOriginalInitializationIntent sameIntent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Pure captured/native checks only, valid inside this producer's SAME admitted commit.
    void DemandOriginalInitializationCommit(ICapabilityOriginalInitializationIntent sameIntent);
    Task<ICapabilityOriginalInitializationAcknowledgment> CommitOriginalInitializationWithinSourceAsync(
        ICapabilityOriginalInitializationIntent sameIntent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsOriginalAtomicInitializationTask(ICapabilityOriginalInitializationIntent sameIntent,
        Task<ICapabilityOriginalInitializationAcknowledgment> sameAtomicSqlTask);
    bool IsOwnedOriginalInitializationAcknowledgment(ICapabilityOriginalInitializationIntent sameIntent,
        ICapabilityOriginalInitializationAcknowledgment sameAcknowledgment,
        Task<ICapabilityOriginalInitializationAcknowledgment> sameAtomicSqlTask);
    // Exact healthy no-effect source occurrences only; aliases/mixed faults never qualify.
    bool IsAcknowledgedOriginalInitializationSourceRefusal(Task sameOriginalSource) => false;
}

public interface ICapabilityOriginalInitializationHomeWriteClaim : IAsyncDisposable
{
    ICapabilityOriginalInitializationIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

/// <summary>One individually accepted central Home setup action. No caller implementation,
/// catalogue observation or read/import receipt can replace this actual claim.</summary>
public interface ICapabilityOriginalInitializationHomeWriteSource
{
    Task<ICapabilityOriginalInitializationHomeWriteClaim> AcquireOriginalWriteWithinSourceAsync(
        ICapabilityOriginalInitializationIntent sameIntent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, Action<ICapabilityOriginalInitializationHomeWriteClaim>? captureOriginalClaim,
        CancellationToken token);
    bool IsAcknowledgedOriginalWriteRefusal(Task sameOriginalAcquisition);
    bool IsIssuedOriginalWriteClaim(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
        ICapabilityOriginalInitializationIntent sameIntent);
    Task AcquireOriginalCommitEntryWithinSourceAsync(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task ValidateOriginalCommitWithinSourceAsync(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // SAME held Home entry and producer native pin; no ordinary Home/SQLite reentry.
    void DemandOriginalCommit(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
        ICapabilityOriginalInitializationIntent sameIntent);
    void RetainOriginalSqlCommit(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
        Task<ICapabilityOriginalInitializationAcknowledgment> sameAtomicSqlTask);
    Task CompleteOriginalWriteWithinSourceAsync(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
        Task<ICapabilityOriginalInitializationAcknowledgment> sameAtomicSqlTask,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
