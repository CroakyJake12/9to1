using Haven.Core;

namespace Haven.Application.Automations;

public enum CanonicalAutomationLibraryReadState { Available, SetupRequired }

/// <summary>A privately issued current page of the maintained saved rows. Metadata,
/// stored enabled flags and recovery descriptors issue no write or execution authority.</summary>
public interface ICanonicalAutomationLibraryOriginalObservation
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity? OriginalStoreIdentity { get; }
    CanonicalAutomationLibraryReadState State { get; }
    string Detail { get; }
    IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> Definitions { get; }
    ICanonicalAutomationLibraryOriginalContinuation? NextContinuation { get; }
}

/// <summary>Opaque source-owned page boundary. Copying a target ID or query does
/// not recreate this observation or authorize another actor/store/page.</summary>
public interface ICanonicalAutomationLibraryOriginalContinuation { }

public interface ICanonicalAutomationLibraryOriginalReadSource
{
    bool IsIssuedOriginalObservation(ICanonicalAutomationLibraryOriginalObservation actual);
    bool IsIssuedOriginalContinuation(ICanonicalAutomationLibraryOriginalContinuation actual);
    Task<ICanonicalAutomationLibraryOriginalObservation> ReadOriginalLibraryWithinSourceAsync(
        AuthenticatedResourceActor actualActor, AutomationLibraryQuery query,
        Action<Action> scope, Action<Task> retain, CancellationToken token,
        ICanonicalAutomationLibraryOriginalContinuation? continuation = null);
    Task RevalidateOriginalObservationWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameActual,
        AuthenticatedResourceActor actualActor, Action<Action> scope,
        Action<Task> retain, CancellationToken token);
}

/// <summary>Permission-filtered lifecycle facts from the SAME persisted run ledger.
/// Payloads, credentials, lease tokens and arbitrary result/error JSON are not
/// exposed by this read. Null revision/continuation metadata means unavailable
/// in the observed schema, rather than an invented initial revision or false.</summary>
public sealed record CanonicalAutomationRunSummary(Guid RunId, Guid AutomationId,
    AutomationRunStatus Status, DateTimeOffset ScheduledFor, DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt, long? Revision, long? DefinitionRevision,
    AutomationGraphBinding? PinnedGraph, bool HasResult, bool HasError,
    bool? HasExecutionDetails, bool? HasContinuation);

public interface ICanonicalAutomationRunOriginalContinuation { }
public interface ICanonicalAutomationRunOriginalObservation
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    Guid AutomationId { get; }
    CanonicalAutomationLibraryReadState State { get; }
    string Detail { get; }
    IReadOnlyList<CanonicalAutomationRunSummary> Runs { get; }
    ICanonicalAutomationRunOriginalContinuation? NextContinuation { get; }
}

/// <summary>Optional capability of the SAME protected library producer. A copied
/// page, row, cursor or RunID grants no independent access or run-control authority.</summary>
public interface ICanonicalAutomationRunOriginalReadSource
{
    bool IsIssuedOriginalRunObservation(ICanonicalAutomationRunOriginalObservation actual);
    bool IsIssuedOriginalRunContinuation(ICanonicalAutomationRunOriginalContinuation actual);
    Task<ICanonicalAutomationRunOriginalObservation> ReadOriginalRunsWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation samePage,
        AutomationOwnerRead<AutomationDefinition> sameRow, AuthenticatedResourceActor actualActor,
        int limit, Action<Action> scope, Action<Task> retain, CancellationToken token,
        ICanonicalAutomationRunOriginalContinuation? continuation = null);
}
