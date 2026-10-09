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
