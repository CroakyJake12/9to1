namespace Haven.Application;

/// <summary>Observations of one existing, physically protected canonical catalogue.
/// None of these values, including a digest or operation ID, grants file access.</summary>
public interface ICanonicalMiniComputerCatalogIdentityIntent
{
    AuthenticatedResourceActor Actor { get; }
    Guid OperationId { get; }
    string CatalogueName { get; }
    string OriginalCatalogSha256 { get; }
    string OriginalFileEvidenceSha256 { get; }
    long OriginalByteLength { get; }
    int OriginalSchemaVersion { get; }
}
public interface ICanonicalMiniComputerCatalogIdentityAcknowledgment
{
    ICanonicalMiniComputerCatalogIdentityIntent OriginalIntent { get; }
    bool Applied { get; }
    ResourceStoreIdentity? CreatedIdentity { get; }
    string? PublishedCatalogSha256 { get; }
    string Reason { get; }
}
public sealed record CanonicalMiniComputerCatalogIdentityPreparation(
    ICanonicalMiniComputerCatalogIdentityIntent? Intent, string Reason);

public interface ICanonicalMiniComputerCatalogIdentitySource
{
    bool IsIssuedOriginalIdentityIntent(ICanonicalMiniComputerCatalogIdentityIntent sameIntent);
    string GetOriginalIdentityIntentDigest(ICanonicalMiniComputerCatalogIdentityIntent sameIntent);
    Task ValidateOriginalIdentityIntentWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    void DemandOriginalPinnedIdentityIntent(ICanonicalMiniComputerCatalogIdentityIntent sameIntent);
    Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> InvokeOriginalIdentityWriteWithinSourceAsync(
        ICanonicalMiniComputerCatalogIdentityIntent sameIntent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsOriginalIdentityWriteTask(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> sameAtomic);
    bool IsOwnedOriginalIdentityAcknowledgment(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        ICanonicalMiniComputerCatalogIdentityAcknowledgment sameAcknowledgment,
        Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> sameAtomic);
}
public interface ICanonicalMiniComputerCatalogIdentityHomeClaim : IAsyncDisposable
{
    ICanonicalMiniComputerCatalogIdentityIntent OriginalIntent { get; }
    string OriginalApprovalRequestId { get; }
    Task<ICanonicalMiniComputerCatalogIdentityHomeClaim> OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalMiniComputerCatalogIdentityHomeSource
{
    Task<ICanonicalMiniComputerCatalogIdentityHomeClaim> AcquireOriginalIdentityWriteWithinSourceAsync(
        ICanonicalMiniComputerCatalogIdentityIntent sameIntent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, Action<ICanonicalMiniComputerCatalogIdentityHomeClaim> retainOriginalClaim,
        CancellationToken token);
    bool IsIssuedOriginalIdentityWriteClaim(ICanonicalMiniComputerCatalogIdentityHomeClaim sameClaim,
        ICanonicalMiniComputerCatalogIdentityIntent sameIntent);
    Task AcquireOriginalIdentityWriteEntryWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityHomeClaim sameClaim,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    void DemandOriginalIdentityWrite(ICanonicalMiniComputerCatalogIdentityHomeClaim sameClaim,
        ICanonicalMiniComputerCatalogIdentityIntent sameIntent);
    void RetainOriginalIdentityWrite(ICanonicalMiniComputerCatalogIdentityHomeClaim sameClaim,
        Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> sameAtomic);
    Task CompleteOriginalIdentityWriteWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityHomeClaim sameClaim,
        Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> sameAtomic,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsAcknowledgedOriginalIdentityWriteRefusal(Task sameActualSource) => false;
    // The maintained generic field name OriginalAtomicSqlTask carries this SAME
    // JSON atomic write Task; no SQLite store or SQL permission is involved.
    bool IsIssuedOriginalSettlementReleasePhase(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerCatalogIdentityIntent,
            ICanonicalMiniComputerCatalogIdentityAcknowledgment> samePhase) => false;
}
