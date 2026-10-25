using Haven.Core;
using Haven.Application.NodeGraph;

namespace Haven.Application.Automations;

public enum CanonicalAutomationGraphEditKind { AddNode, RemoveNode, SetValue, Connect, Disconnect, Undo, Redo }
public sealed record CanonicalAutomationGraphEdit(CanonicalAutomationGraphEditKind Kind, string? NodeTypeId = null,
    Guid? NodeId = null, string? Value = null, Guid? OutputPortId = null, Guid? InputPortId = null, Guid? ConnectionId = null);

/// <summary>Issuer-owned saved-graph READ only. All observations require the SAME private
/// producer pairing. IDs, interface implementations and deserialized values issue no grant.</summary>
public interface ICanonicalAutomationOriginalSavedGraphRead
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    AutomationDefinition OriginalDefinition { get; }
    string OriginalCreationOriginSha256 { get; }
    /// <summary>Hash of the exact protected definition/descriptor snapshot used by the original Home graph review.</summary>
    string OriginalSnapshotSha256 { get; }
}

/// <summary>Private local draft session over the actual saved graph. Local revision is an
/// editor CAS only; the Home graph revision changes solely through reviewed publication.
/// This source phase supplies no Save, Activate, Run or target-action permission.</summary>
public interface ICanonicalAutomationOriginalGraphEditSession
{
    Guid AutomationId { get; }
    AuthenticatedResourceActor Actor { get; }
    GraphDocument SavedDraft { get; }
    GraphDocument CurrentDraft { get; }
    long LocalRevision { get; }
    bool CanEdit { get; }
    bool HasUnsubmittedChanges { get; }
    IReadOnlyList<GraphNodeType> RegisteredNodeTypes { get; }
}
public interface ICanonicalAutomationOriginalGraphEditSource
{
    bool SupportsOriginalSavedGraphEditing { get; }
    bool IsIssuedOriginalSavedGraphRead(ICanonicalAutomationOriginalSavedGraphRead sameRead);
    Task RevalidateOriginalSavedGraphReadWithinSourceAsync(ICanonicalAutomationOriginalSavedGraphRead sameRead,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task<GraphDocument?> ReadOriginalSavedGraphWithinSourceAsync(ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameRow, Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task<ICanonicalAutomationOriginalGraphEditSession> OpenOriginalGraphEditWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, AutomationOwnerRead<AutomationDefinition> sameRow,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalGraphEditSession(ICanonicalAutomationOriginalGraphEditSession sameSession);
    Task<GraphEditResult> EditOriginalGraphWithinSourceAsync(ICanonicalAutomationOriginalGraphEditSession sameSession,
        long expectedLocalRevision, CanonicalAutomationGraphEdit edit, Action<Action> scope, Action<Task> retain, CancellationToken token);
    // Read-only reconciliation of actual Home records with a fresh protected SQL
    // owner/row. A true result issues no selection, approval or mutation authority.
    Task<bool> IsOriginalBoundSavedGraphWithinSourceAsync(AuthenticatedResourceActor actor,
        ResourceStoreIdentity store, GraphOwnerDescriptor owner, GraphDocument draft, long? activeRevision,
        string creationOriginSha256, Action<Action> scope, Action<Task> retain, CancellationToken token);
}

public enum CanonicalAutomationGraphPublicationCompletionKind
{ SavedDisabled, ActiveGraphDisabled, DeclinedBeforeEffect, GraphPublishedBindingPending, AuditPending, OutcomeUnconfirmed, ObservationRetired }
public interface ICanonicalAutomationOriginalGraphPublicationCompletion
{
    ICanonicalAutomationOriginalGraphPublicationObservation OriginalObservation { get; }
    CanonicalAutomationGraphPublicationCompletionKind Kind { get; }
    string Message { get; }
}
/// <summary>Finite presentation delivery, independent of its producer-owned
/// business driver. Retirement does not cancel or restart graph/SQL publication.</summary>
public interface ICanonicalAutomationOriginalGraphPublicationObservation
{
    Guid OperationId { get; }
    Guid GraphOperationId { get; }
    Guid AutomationId { get; }
    Guid GraphId { get; }
    GraphPublicationKind PublicationKind { get; }
    Task<ICanonicalAutomationOriginalGraphPublicationCompletion> WaitOriginalCompletionAsync(CancellationToken token);
    bool IsIssuedOriginalCompletion(ICanonicalAutomationOriginalGraphPublicationCompletion sameCompletion);
    void RequestOriginalRetirement();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalAutomationOriginalGraphPublicationSource : ICanonicalAutomationOriginalGraphEditSource
{
    Task<ICanonicalAutomationOriginalGraphPublicationObservation> StartOriginalGraphPublicationWithinSourceAsync(
        ICanonicalAutomationOriginalGraphEditSession sameSession, long expectedLocalRevision, GraphPublicationKind kind,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalGraphPublicationObservation(ICanonicalAutomationOriginalGraphPublicationObservation sameObservation);
}

/// <summary>Read-only metadata for a durable Home publication whose separate
/// Automation definition binding did not complete. The instance is issued by
/// the canonical write owner; its public fields do not supply receipt authority.</summary>
public interface ICanonicalAutomationOriginalGraphPublicationRecovery
{
    Guid AutomationId { get; }
    Guid GraphId { get; }
    GraphPublicationKind Kind { get; }
    long ExpectedGraphRevision { get; }
    long PublishedGraphRevision { get; }
    long? PublishedActiveRevision { get; }
    GraphDocument PublishedGraph { get; }
}

public interface ICanonicalAutomationOriginalGraphPublicationRecoverySource : ICanonicalAutomationOriginalGraphPublicationSource
{
    bool IsIssuedOriginalGraphPublicationRecovery(ICanonicalAutomationOriginalGraphPublicationRecovery sameRecovery);
    Task<ICanonicalAutomationOriginalGraphPublicationRecovery?> ReadOriginalGraphPublicationRecoveryWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, AutomationOwnerRead<AutomationDefinition> sameRow,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> RecoverOriginalGraphPublicationWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation, AutomationOwnerRead<AutomationDefinition> sameRow,
        ICanonicalAutomationOriginalGraphPublicationRecovery sameRecovery, Action<Action> scope, Action<Task> retain,
        CancellationToken token);
}
