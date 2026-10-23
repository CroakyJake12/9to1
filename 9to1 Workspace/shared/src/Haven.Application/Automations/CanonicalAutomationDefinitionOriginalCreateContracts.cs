using Haven.Core;

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
