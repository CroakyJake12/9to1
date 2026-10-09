namespace Haven.Application;

/// <summary>VM lifecycle actions only. Host input, disk deletion, provider installation,
/// networking and device access cannot be represented by this bounded contract.</summary>
public enum CanonicalMiniComputerAction { Inspect, Start, Pause, Resume, SaveState, Shutdown }

/// <summary>Immutable observations captured from the current canonical VM catalogue.
/// Neither these IDs nor a saved Assistant preference grants resource access.</summary>
public sealed record CanonicalMiniComputerTarget(Guid VirtualMachineId, Guid ProviderId,
    string ProviderMachineId, string Name, int ConfigurationVersion, long Revision,
    string OriginalCatalogSha256);

public interface ICanonicalMiniComputerOperationIntent
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    VerifiedResourceStoreOwnership OriginalDenOwnership { get; }
    CanonicalMiniComputerTarget Target { get; }
    CanonicalMiniComputerAction Action { get; }
    Guid OperationId { get; }
    string DenId { get; }
    string NamespaceId { get; }
    string DefinitionId { get; }
    long DefinitionRevision { get; }
    Guid ConversationId { get; }
    Guid? TaskId { get; }
}

/// <summary>The actual provider may accept a transition without yet observing its
/// destination state. Pending remains pending; viewer lifetime never changes VM state.</summary>
public interface ICanonicalMiniComputerOperationAcknowledgment
{
    ICanonicalMiniComputerOperationIntent OriginalIntent { get; }
    string ObservedState { get; }
    DateTimeOffset ObservedAt { get; }
    bool IsPending { get; }
    bool WasDispatched { get; }
    string Reason { get; }
}

/// <summary>Configured original product source. All proof is private issuer identity;
/// implementing an interface or reconstructing metadata cannot create a valid intent.</summary>
public interface ICanonicalMiniComputerOperationSource
{
    bool IsIssuedOriginalOperationIntent(ICanonicalMiniComputerOperationIntent sameIntent);
    string GetOriginalOperationIntentDigest(ICanonicalMiniComputerOperationIntent sameIntent);
    Task ValidateOriginalOperationIntentWithinSourceAsync(ICanonicalMiniComputerOperationIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    // Pinned checks only: no Home/Den acquisition while the Home entry is held.
    void DemandOriginalOperation(ICanonicalMiniComputerOperationIntent sameIntent);
    Task<ICanonicalMiniComputerOperationAcknowledgment> InvokeOriginalOperationWithinSourceAsync(
        ICanonicalMiniComputerOperationIntent sameIntent, Action<Action> scope,
        Action<Task> retain, CancellationToken token);
    bool IsOriginalOperationTask(ICanonicalMiniComputerOperationIntent sameIntent,
        Task<ICanonicalMiniComputerOperationAcknowledgment> sameActualTask);
    bool IsOwnedOriginalOperationAcknowledgment(ICanonicalMiniComputerOperationIntent sameIntent,
        ICanonicalMiniComputerOperationAcknowledgment sameAcknowledgment,
        Task<ICanonicalMiniComputerOperationAcknowledgment> sameActualTask);
}

public interface ICanonicalMiniComputerHomeOperationClaim : IAsyncDisposable
{
    ICanonicalMiniComputerOperationIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}

/// <summary>Separate manual Home admission for the exact source-issued VM and action.
/// The catalogue import and Assistant membership cannot substitute for this claim.</summary>
public interface ICanonicalMiniComputerHomeOperationSource
{
    bool IsAcknowledgedOriginalOperationRefusal(Task sameAcquisition) => false;
    Task<ICanonicalMiniComputerHomeOperationClaim> AcquireOriginalOperationWithinSourceAsync(
        ICanonicalMiniComputerOperationIntent sameIntent, Action<Action> scope, Action<Task> retain,
        Action<ICanonicalMiniComputerHomeOperationClaim>? capture, CancellationToken token);
    bool IsIssuedOriginalOperationClaim(ICanonicalMiniComputerHomeOperationClaim sameClaim,
        ICanonicalMiniComputerOperationIntent sameIntent);
    Task AcquireOriginalOperationEntryWithinSourceAsync(ICanonicalMiniComputerHomeOperationClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalOperation(ICanonicalMiniComputerHomeOperationClaim sameClaim,
        ICanonicalMiniComputerOperationIntent sameIntent);
    void RetainOriginalOperation(ICanonicalMiniComputerHomeOperationClaim sameClaim,
        Task<ICanonicalMiniComputerOperationAcknowledgment> sameRawOperation);
    bool IsIssuedOriginalSettlementReleasePhase(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerOperationIntent,
            ICanonicalMiniComputerOperationAcknowledgment> samePhase) => false;
    Task CompleteOriginalOperationWithinSourceAsync(ICanonicalMiniComputerHomeOperationClaim sameClaim,
        Task<ICanonicalMiniComputerOperationAcknowledgment> sameRawOperation,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
