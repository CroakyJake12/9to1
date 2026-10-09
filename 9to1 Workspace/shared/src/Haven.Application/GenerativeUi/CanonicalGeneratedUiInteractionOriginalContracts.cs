using Haven.Core;

namespace Haven.Application;

/// <summary>Only the configured canonical message/runtime authority issues a selection.</summary>
public interface ICanonicalGeneratedUiOriginalSelection { }
// Values describe a SAME privately issued Den membership occurrence. A copied
// implementation is never source evidence; the actual bridge checks its CWT.
public interface ICanonicalGeneratedUiOriginalBindingEvidence
{
    AuthenticatedResourceActor HomeActor { get; }
    Guid ConversationId { get; }
    VerifiedResourceStoreOwnership OriginalDenOwnership { get; }
}
public interface ICanonicalGeneratedUiOriginalSnapshot
{
    AuthenticatedResourceActor HomeActor { get; }
    Guid ConversationId { get; }
    Guid MessageId { get; }
    int TemplateOrdinal { get; }
    string MessageContentSha256 { get; }
    string OriginalDeclarationSha256 { get; }
    VerifiedResourceStoreOwnership OriginalDenOwnership { get; }
    GenUiAppDefinition Definition { get; }
    GenUiOriginalInstanceObservation OriginalRegistration { get; }
    GenUiOriginalMutationReceipt? OriginalMutation { get; }
}
public interface ICanonicalGeneratedUiOriginalMessageAuthority
{
    GenUiInstanceStore OriginalInstances { get; }
    bool IsIssuedOriginalSelection(ICanonicalGeneratedUiOriginalSelection sameSelection);
    Task<ICanonicalGeneratedUiOriginalSnapshot> ObserveOriginalSnapshotWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSelection sameSelection, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalSnapshot(ICanonicalGeneratedUiOriginalSnapshot sameSnapshot);
    Task RevalidateOriginalSnapshotWithinSourceAsync(ICanonicalGeneratedUiOriginalSnapshot sameSnapshot,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalSnapshotCurrent(ICanonicalGeneratedUiOriginalSnapshot sameSnapshot);
    // Actual SAME Den/membership revisions must remain pinned through SQL settlement.
    // This pin grants no Home or SQLite permission and cannot run Home observation IO.
    Task<ICanonicalGeneratedUiOriginalSourcePin> AcquireOriginalCommitPinWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSnapshot sameSnapshot, Action<Action> scope, Action<Task> retain, CancellationToken token);
}
public interface ICanonicalGeneratedUiOriginalSourcePin : IAsyncDisposable
{
    ICanonicalGeneratedUiOriginalSnapshot OriginalSnapshot { get; }
    Task? OriginalClose { get; }
    void DemandOriginalCurrent();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
    bool IsOwnedOriginalHealthyClose(Task sameClose);
}

public enum CanonicalGeneratedUiOriginalReadState { NoSavedInteraction, Restorable, Unprovenance, AuditUnavailable }
public interface ICanonicalGeneratedUiOriginalObservation
{
    ICanonicalGeneratedUiOriginalSnapshot OriginalSnapshot { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    CanonicalGeneratedUiOriginalReadState State { get; }
    string Detail { get; }
    // Never populated for a legacy/unknown row or an unacknowledged Home audit.
    GenUiAppDefinition? SavedDefinition { get; }
    CanonicalGeneratedUiOriginalCommitReceipt? OriginalReceipt { get; }
}
public sealed record CanonicalGeneratedUiOriginalCommitReceipt(int SchemaVersion, Guid StoreId,
    Guid OperationId, Guid InstanceId, Guid ConversationId, Guid MessageId, int TemplateOrdinal,
    string MessageContentSha256, string DeclarationSha256, string BeforeRowSha256, string AfterRowSha256,
    string PayloadSha256, AuthenticatedResourceActor ObservedActor, string HomeApprovalRequestId, string HomeArgumentsSha256, DateTimeOffset SavedAtUtc);
public interface ICanonicalGeneratedUiInteractionOriginalReadSource
{
    Task<ICanonicalGeneratedUiOriginalObservation> ReadOriginalInteractionWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSelection sameSelection, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalObservation(ICanonicalGeneratedUiOriginalObservation sameObservation);
    Task RevalidateOriginalObservationWithinSourceAsync(ICanonicalGeneratedUiOriginalObservation sameObservation,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
public interface ICanonicalGeneratedUiOriginalSaveIntent
{
    ICanonicalGeneratedUiOriginalObservation OriginalObservation { get; }
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    Guid OperationId { get; }
    string OriginalPayloadSha256 { get; }
    string OriginalRowSha256 { get; }
}
public interface ICanonicalGeneratedUiOriginalSaveAcknowledgment
{
    ICanonicalGeneratedUiOriginalSaveIntent OriginalIntent { get; }
    CanonicalGeneratedUiOriginalCommitReceipt OriginalReceipt { get; }
}
public interface ICanonicalGeneratedUiInteractionOriginalWriteSource
{
    Task<ICanonicalGeneratedUiOriginalSaveIntent> PrepareOriginalSaveWithinSourceAsync(
        ICanonicalGeneratedUiOriginalObservation sameObservation, Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalSaveIntent(ICanonicalGeneratedUiOriginalSaveIntent sameIntent);
    bool IsOriginalSaveInvocation(ICanonicalGeneratedUiOriginalSaveIntent sameIntent);
    string GetOriginalSaveDigest(ICanonicalGeneratedUiOriginalSaveIntent sameIntent);
    Task RevalidateOriginalSaveWithinSourceAsync(ICanonicalGeneratedUiOriginalSaveIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalSaveCommit(ICanonicalGeneratedUiOriginalSaveIntent sameIntent);
    Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> CommitOriginalSaveWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSaveIntent sameIntent, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsOriginalAtomicSaveTask(ICanonicalGeneratedUiOriginalSaveIntent sameIntent,
        Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> sameAtomic);
    Task WaitOriginalSaveDispatchSettledWithinSourceAsync(ICanonicalGeneratedUiOriginalSaveIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ReleaseOriginalSaveResourcesWithinSourceAsync(ICanonicalGeneratedUiOriginalSaveIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsOwnedOriginalSaveResourcesRelease(ICanonicalGeneratedUiOriginalSaveIntent sameIntent,
        Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment>? sameAtomic);
    bool IsAcknowledgedOriginalSaveSourceRefusal(Task sameRaw);
    bool IsOwnedOriginalSaveAcknowledgment(ICanonicalGeneratedUiOriginalSaveIntent sameIntent,
        ICanonicalGeneratedUiOriginalSaveAcknowledgment sameAcknowledgment, Task sameAtomic);
}
public interface ICanonicalGeneratedUiOriginalHomeWriteClaim : IAsyncDisposable
{
    ICanonicalGeneratedUiOriginalSaveIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    string OriginalReviewArgumentsDigest { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalGeneratedUiOriginalHomeWriteSource
{
    Task<ICanonicalGeneratedUiOriginalHomeWriteClaim> AcquireOriginalWriteWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSaveIntent sameIntent, Action<Action> scope, Action<Task> retain,
        Action<ICanonicalGeneratedUiOriginalHomeWriteClaim> capture, CancellationToken token);
    bool IsIssuedOriginalWriteClaim(ICanonicalGeneratedUiOriginalHomeWriteClaim sameClaim,
        ICanonicalGeneratedUiOriginalSaveIntent sameIntent);
    bool IsAcknowledgedOriginalWriteRefusal(Task sameAcquisition);
    Task AcquireOriginalCommitEntryWithinSourceAsync(ICanonicalGeneratedUiOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ValidateOriginalCommitWithinSourceAsync(ICanonicalGeneratedUiOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalCommit(ICanonicalGeneratedUiOriginalHomeWriteClaim sameClaim, ICanonicalGeneratedUiOriginalSaveIntent sameIntent);
    void RetainOriginalSaveCommit(ICanonicalGeneratedUiOriginalHomeWriteClaim sameClaim, Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> sameAtomic);
    Task CompleteOriginalWriteWithinSourceAsync(ICanonicalGeneratedUiOriginalHomeWriteClaim sameClaim,
        Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> sameAtomic, Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task<bool> VerifyOriginalStoredAuditWithinSourceAsync(CanonicalGeneratedUiOriginalCommitReceipt sameReceipt,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task WithdrawOriginalPendingWriteWithinSourceAsync(ICanonicalGeneratedUiOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
