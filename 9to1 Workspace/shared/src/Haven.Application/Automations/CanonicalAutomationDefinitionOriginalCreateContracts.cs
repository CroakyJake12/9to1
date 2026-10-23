using Haven.Core;
using Haven.Application.NodeGraph;

namespace Haven.Application.Automations;

/// <summary>Privately issued creation origin over the SAME actual protected library/store.
/// Absence describes the SQL definition/operation targets only; Home graph absence
/// and the complete immutable graph proposal have not been observed or admitted.
/// IDs, digests and ownership metadata are descriptive. This is neither Home approval,
/// graph publication, a durable creation receipt nor permission to insert or execute.</summary>
public interface ICanonicalAutomationDefinitionOriginalCreationReservation
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    Guid AutomationId { get; }
    Guid GraphId { get; }
    Guid DefinitionOperationId { get; }
    Guid GraphOperationId { get; }
    string Name { get; }
    string OriginalSchemaSha256 { get; }
    string OriginalAbsenceSha256 { get; }
    DateTimeOffset CapturedAt { get; }
}

/// <summary>Read-only preparatory capability of the maintained protected producer.
/// A working create chain must separately obtain exact Home graph and definition
/// admissions, native absence/CAS, durable receipts and cross-store recovery.</summary>
public interface ICanonicalAutomationDefinitionOriginalCreateSource
{
    Task<ICanonicalAutomationDefinitionOriginalCreationReservation> CaptureOriginalCreationWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, string name,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalCreationReservation(ICanonicalAutomationDefinitionOriginalCreationReservation sameReservation);
    string GetOriginalCreationReservationDigest(ICanonicalAutomationDefinitionOriginalCreationReservation sameReservation);
    Task RevalidateOriginalCreationWithinSourceAsync(ICanonicalAutomationDefinitionOriginalCreationReservation sameReservation,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}

/// <summary>A privately issued selection from an actual protected Home graph read.
/// Its durable IDs are evidence only; recovery obtains fresh current-actor approval.</summary>
public interface ICanonicalAutomationDefinitionOriginalCreationRecovery
{
    Guid AutomationId { get; }
    Guid GraphId { get; }
    string Name { get; }
}
public enum CanonicalAutomationCreationCompletionKind
{
    CreatedDisabled, GraphDraftOnly, OutcomeUnconfirmed, AuditPending, DeclinedBeforeEffect, ObservationRetired
}
public interface ICanonicalAutomationDefinitionOriginalCreationCompletion
{
    ICanonicalAutomationDefinitionOriginalCreationObservation OriginalObservation { get; }
    CanonicalAutomationCreationCompletionKind Kind { get; }
    string Message { get; }
    Guid AutomationId { get; }
    Guid GraphId { get; }
}
public interface ICanonicalAutomationDefinitionOriginalCreationObservation : IAsyncDisposable
{
    Guid AutomationId { get; }
    Guid GraphId { get; }
    Task<ICanonicalAutomationDefinitionOriginalCreationCompletion> WaitOriginalCompletionAsync(CancellationToken token);
    bool IsIssuedOriginalCompletion(ICanonicalAutomationDefinitionOriginalCreationCompletion sameCompletion);
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalAutomationDefinitionOriginalCreationProcessSource : ICanonicalAutomationDefinitionOriginalCreateSource
{
    bool SupportsOriginalDefinitionCreation { get; }
    Task<ICanonicalAutomationDefinitionOriginalCreationObservation> StartOriginalCreationWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalCreationReservation sameReservation,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task<IReadOnlyList<ICanonicalAutomationDefinitionOriginalCreationRecovery>> ReadOriginalCreationRecoveryWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalCreationRecovery(ICanonicalAutomationDefinitionOriginalCreationRecovery sameRecovery);
    Task<ICanonicalAutomationDefinitionOriginalCreationObservation> RecoverOriginalCreationWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, ICanonicalAutomationDefinitionOriginalCreationRecovery sameRecovery,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalCreationObservation(ICanonicalAutomationDefinitionOriginalCreationObservation sameObservation);
    Task<GraphDocument?> ReadOriginalCanonicalGraphWithinSourceAsync(ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameRow, Action<Action> scope, Action<Task> retain, CancellationToken token);
}
/// <summary>Detached durable creation evidence. Only the same actual SQL owner may
/// issue it for its exact atomic acknowledgment; metadata cannot revive approval.</summary>
public sealed record CanonicalAutomationOriginalCreationAuditEvidence(string OriginalRequestId, string OriginalArgumentsSha256,
    string CreationOriginSha256, AutomationOwnerCommitReceipt OriginalReceipt);
public interface ICanonicalAutomationDefinitionOriginalCreationAuditSource
{
    CanonicalAutomationOriginalCreationAuditEvidence? ObserveOriginalCreationAudit(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        ICanonicalAutomationDefinitionOriginalChangeAcknowledgment sameAcknowledgment,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic);
}
