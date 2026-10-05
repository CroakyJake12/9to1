using Haven.Core;

namespace Haven.Application;

/// <summary>Bound to the authenticated host principal; request JSON never selects calendar authority.</summary>
public interface IPlannerEventJourneyPolicy
{
    ValueTask<bool> MayReadAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken);
    ValueTask<bool> MayAttachJourneyAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken);
}

/// <summary>Reads the canonical appointment and checks current owning Calendar permission at dispatch.</summary>
public sealed class PlannerEventJourneyAccessService(IPlannerRepository repository, IPlannerEventJourneyPolicy policy)
{
    public async Task<PlannerEvent> GetAuthorisedAsync(Guid eventId, bool forAssociationMutation,
        DateTimeOffset? expectedEventRevision = null, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("A canonical Planner Event identity is required.", nameof(eventId));
        var plannerEvent = await repository.GetEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (plannerEvent is null || plannerEvent.DeletedAt is not null)
            throw new PlannerEventUnavailableException(eventId);
        if (forAssociationMutation && plannerEvent.IsReadOnly)
            throw new UnauthorizedAccessException("This Calendar appointment is read-only.");
        if (!await policy.MayReadAsync(plannerEvent, cancellationToken).ConfigureAwait(false) ||
            (forAssociationMutation && !await policy.MayAttachJourneyAsync(plannerEvent, cancellationToken).ConfigureAwait(false)))
            throw new UnauthorizedAccessException("Current Calendar permission does not permit this Journey association.");
        if (expectedEventRevision is { } revision && plannerEvent.UpdatedAt != revision)
            throw new PlannerEventRevisionConflictException(eventId, revision, plannerEvent.UpdatedAt);
        return plannerEvent;
    }
}

public sealed class PlannerEventUnavailableException(Guid eventId) : InvalidOperationException($"Planner appointment '{eventId}' is unavailable.");
public sealed class PlannerEventRevisionConflictException(Guid eventId, DateTimeOffset expected, DateTimeOffset actual)
    : InvalidOperationException($"Planner appointment '{eventId}' changed; refresh before attaching a Journey.")
{
    public DateTimeOffset ExpectedRevision { get; } = expected;
    public DateTimeOffset ActualRevision { get; } = actual;
}

/// <summary>Host adapter around current Calendar grants; delegates must capture the trusted principal.</summary>
public sealed class PlannerEventJourneyPolicy(
    Func<PlannerEvent, CancellationToken, ValueTask<bool>> mayRead,
    Func<PlannerEvent, CancellationToken, ValueTask<bool>> mayAttach) : IPlannerEventJourneyPolicy
{
    public ValueTask<bool> MayReadAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) => mayRead(plannerEvent, cancellationToken);
    public ValueTask<bool> MayAttachJourneyAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) =>
        plannerEvent.IsReadOnly ? ValueTask.FromResult(false) : mayAttach(plannerEvent, cancellationToken);
}
