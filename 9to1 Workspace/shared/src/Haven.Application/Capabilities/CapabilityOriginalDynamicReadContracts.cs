using Haven.Core;

namespace Haven.Application;

// Capability metadata only; these values carry no service/credential permission.
public sealed record ExternalConnectionCapabilityMetadata(Guid Id, string Name,
    ExternalConnectionKind Kind, bool IsEnabled, ExternalConnectionState State, DateTimeOffset UpdatedAt);
public sealed record CalendarConnectionCapabilityMetadata(Guid Id, CalendarProviderKind Provider,
    CalendarSyncStatus Status, DateTimeOffset? LastSyncedAt, DateTimeOffset UpdatedAt);

/// <summary>Actual configured protected repository source for ONE SAME maintained
/// dynamic provider. Existing public GetCapabilities lifecycle remains independent.</summary>
public interface ICapabilityOriginalDynamicReadSource
{
    bool HasOriginalDynamicProvider(IDynamicCapabilityProvider sameActualProvider);
    Task<ICapabilityOriginalRepositoryObservation> ReadOriginalDynamicCapabilitiesWithinSourceAsync(
        AuthenticatedResourceActor actualActor, CapabilityPlatform platform,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalDynamicObservation(ICapabilityOriginalRepositoryObservation sameActual);
    Task RevalidateOriginalDynamicObservationWithinSourceAsync(ICapabilityOriginalRepositoryObservation sameActual,
        AuthenticatedResourceActor actualActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
}
