namespace Haven.Application;

/// <summary>Private Home-issued, individually accepted WRITE for SAME source intent/read.
/// Request IDs, interface implementations and READ claims never create this authority.</summary>
public interface ICanonicalProjectTaskContextHomeWriteClaim : IAsyncDisposable
{
    ICanonicalProjectTaskContextCreationIntent OriginalIntent { get; }
    IDeveloperOriginalProjectCommandRead OriginalProjectRead { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

/// <summary>Separate actual Home manual WRITE issuer, composed with the SAME canonical SQL
/// creator and Home project READ issuer. No global resource ownership is granted here.</summary>
public interface ICanonicalProjectTaskContextHomeWriteSource
{
    Task<ICanonicalProjectTaskContextHomeWriteClaim> AcquireOriginalWriteWithinSourceAsync(
        ICanonicalProjectTaskContextCreationIntent sameIntent,
        IDeveloperOriginalProjectCommandRead sameLiveStudioRead,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        Action<ICanonicalProjectTaskContextHomeWriteClaim>? captureOriginalClaim,
        CancellationToken token);
    // Exact source-owned terminal manual Decline/Blocked before any capability or SQL.
    // This pure occurrence proof cannot acknowledge aliases, mixed faults or pending outcomes.
    bool IsAcknowledgedOriginalWriteRefusal(Task sameOriginalAcquisition);
    bool IsIssuedOriginalWriteClaim(ICanonicalProjectTaskContextHomeWriteClaim sameClaim,
        ICanonicalProjectTaskContextCreationIntent sameIntent);
    // Only AFTER SQL owner validates/acquires its actual native/store pins. No SQL
    // transaction or mutation begins before this separate held entry is acquired.
    Task AcquireOriginalCommitEntryWithinSourceAsync(ICanonicalProjectTaskContextHomeWriteClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task ValidateOriginalCommitWithinSourceAsync(ICanonicalProjectTaskContextHomeWriteClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Pure checks plus SAME producer's pinned finite native checks. No Home/SQLite reentry.
    void DemandOriginalCommit(ICanonicalProjectTaskContextHomeWriteClaim sameClaim,
        ICanonicalProjectTaskContextCreationIntent sameIntent);
    // Register actual atomic SQL child before its start/callback release. It must be
    // producer-owned, not the encompassing driver awaiting this audit/cleanup.
    void RetainOriginalSqlCommit(ICanonicalProjectTaskContextHomeWriteClaim sameClaim,
        Task<ICanonicalProjectTaskContextCreation> sameOriginalAtomicSqlTask);
    // Joins SAME raw SQL, then releases held Home/completion leases BEFORE Home audit.
    // A fault/unknown ACK never becomes success and never authorizes another dispatch.
    // Pure private phase/cached raw-release proof from the SAME actual issuer.
    // Enables only original pin cleanup, never another mutation or SQL dispatch.
    bool IsIssuedOriginalSettlementReleasePhase(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalProjectTaskContextCreationIntent, ICanonicalProjectTaskContextCreation> samePhase) => false;
    Task CompleteOriginalWriteWithinSourceAsync(ICanonicalProjectTaskContextHomeWriteClaim sameClaim,
        Task<ICanonicalProjectTaskContextCreation> sameOriginalAtomicSqlTask,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
