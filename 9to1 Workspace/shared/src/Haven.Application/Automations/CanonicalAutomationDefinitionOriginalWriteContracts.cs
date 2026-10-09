using Haven.Core;

namespace Haven.Application.Automations;

public enum CanonicalAutomationOriginalChangeKind { RecoverLegacy, Disable }

/// <summary>Privately issued by the SAME protected row owner. This detached observation
/// grants no WRITE, scheduling, Task admission or execution permission.</summary>
public interface ICanonicalAutomationDefinitionOriginalChangeIntent
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    AutomationDefinition OriginalDefinition { get; }
    CanonicalAutomationOriginalChangeKind ChangeKind { get; }
    string ActionId { get; }
    Guid OperationId { get; }
    long ExpectedRevision { get; }
    string OriginalRowSha256 { get; }
    bool IsRecoveredOperation { get; }
}
public interface ICanonicalAutomationDefinitionOriginalChangeAcknowledgment
{
    ICanonicalAutomationDefinitionOriginalChangeIntent OriginalIntent { get; }
    Guid OperationId { get; }
    AutomationDefinition Definition { get; }
    AutomationOwnerCommitReceipt OriginalReceipt { get; }
    bool IsRecoveredOperation { get; }
}
public interface ICanonicalAutomationDefinitionOriginalWriteSource
{
    Task<ICanonicalAutomationDefinitionOriginalChangeIntent> PrepareOriginalChangeWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameSelectedRow, CanonicalAutomationOriginalChangeKind kind,
        Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalChangeIntent(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    string GetOriginalChangeIntentDigest(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    Task RevalidateOriginalChangeIntentWithinSourceAsync(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalChangeCommit(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> CommitOriginalChangeWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent, Action<Action> scope,
        Action<Task> retain, CancellationToken token);
    bool IsOriginalAtomicChangeTask(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic);
    bool IsOwnedOriginalChangeAcknowledgment(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        ICanonicalAutomationDefinitionOriginalChangeAcknowledgment sameAcknowledgment,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic);
    // Pure private dispatch + independently joined SAME native pin close proof.
    // This neither erases a business failure nor infers no SQL from a null field.
    bool IsOwnedOriginalChangeNativeRelease(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? sameAtomic);
    bool IsAcknowledgedOriginalChangeSourceRefusal(Task sameActualSource) => false;
}
public interface ICanonicalAutomationDefinitionOriginalHomeWriteClaim : IAsyncDisposable
{
    ICanonicalAutomationDefinitionOriginalChangeIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalAutomationDefinitionOriginalHomeWriteSource
{
    Task<ICanonicalAutomationDefinitionOriginalHomeWriteClaim> AcquireOriginalWriteWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent, Action<Action> scope, Action<Task> retain,
        Action<ICanonicalAutomationDefinitionOriginalHomeWriteClaim>? capture, CancellationToken token);
    bool IsAcknowledgedOriginalWriteRefusal(Task sameAcquisition);
    bool IsIssuedOriginalWriteClaim(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    Task AcquireOriginalCommitEntryWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ValidateOriginalCommitWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalCommit(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    void RetainOriginalSqlCommit(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic);
    Task CompleteOriginalWriteWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
public interface ICanonicalAutomationDefinitionOriginalHomeReviewWithdrawalSource
{
    Task WithdrawOriginalPendingWriteWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
