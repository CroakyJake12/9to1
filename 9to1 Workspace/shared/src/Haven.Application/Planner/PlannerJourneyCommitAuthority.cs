using Haven.Core;

namespace Haven.Application;

public sealed record PlannerJourneySnapshot(ResourceStoreIdentity StoreIdentity, PlannerEvent Event, PlannerCalendar Calendar);

/// <summary>Owning SQL read snapshot, never a permission grant. Implementations must not read Maps settings.</summary>
public interface IPlannerJourneySnapshotSource
{
    Task<PlannerJourneySnapshot?> ReadJourneySnapshotAsync(Guid eventId, CancellationToken cancellationToken);
}

/// <summary>Captures local Planner ownership outside another store's commit lease. Organisation ACLs
/// require their actual owner implementation and are deliberately unavailable through this local port.</summary>
public sealed class PlannerJourneyCommitAuthority(IPlannerJourneySnapshotSource snapshots,
    IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipReceiptAuthority receipts)
{
    public async Task<PlannerJourneyCommitReceipt> CaptureAsync(AuthenticatedResourceActor expectedActor,
        PlannerEvent expectedEvent, bool forAssociationMutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(expectedEvent);
        if (expectedActor.AccountId is not null || expectedActor.OrganisationId is not null ||
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor)
            throw new UnauthorizedAccessException("The displayed local Planner actor changed.");
        var snapshot = await snapshots.ReadJourneySnapshotAsync(expectedEvent.Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Event != expectedEvent || !Permits(snapshot, forAssociationMutation))
            throw new UnauthorizedAccessException("The canonical appointment or Calendar permission changed.");
        var storeId = snapshot.StoreIdentity.StoreId.ToString("D");
        var binding = await receipts.GetVerifiedAsync("planner", storeId, cancellationToken).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "planner" || binding.StoreId != storeId || binding.ProfileId != expectedActor.ProfileId)
            throw new UnauthorizedAccessException("Explicit Planner storage ownership is required.");
        var receipt = new PlannerJourneyCommitReceipt(snapshot, expectedActor, binding, forAssociationMutation, snapshots, actors, receipts);
        if (!await receipt.CheckCurrentAsync(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Planner authority changed while preparing this Journey.");
        return receipt;
    }

    internal static bool Permits(PlannerJourneySnapshot snapshot, bool writing) =>
        snapshot.StoreIdentity.SchemaVersion == 1 && snapshot.StoreIdentity.StoreId != Guid.Empty &&
        snapshot.Event.Id != Guid.Empty && snapshot.Event.DeletedAt is null && snapshot.Event.CalendarId == snapshot.Calendar.Id &&
        Enum.IsDefined(snapshot.Calendar.Permission) &&
        (!writing || (!snapshot.Event.IsReadOnly && snapshot.Calendar.Permission is CalendarPermission.Owner or CalendarPermission.Writer));
}

/// <summary>Safe under the separate Maps settings lease: only direct actor, raw Home receipt and one
/// Planner SQL read snapshot are checked. Do not call while holding the Planner SQL writer transaction.
/// These are current observations, not a transaction spanning Home, SQL and settings.</summary>
public sealed class PlannerJourneyCommitReceipt
{
    private readonly PlannerJourneySnapshot _snapshot;
    private readonly AuthenticatedResourceActor _actor;
    private readonly VerifiedResourceStoreOwnership _binding;
    private readonly bool _writing;
    private readonly IPlannerJourneySnapshotSource _snapshots;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IResourceStoreOwnershipReceiptAuthority _receipts;
    internal PlannerJourneyCommitReceipt(PlannerJourneySnapshot snapshot, AuthenticatedResourceActor actor,
        VerifiedResourceStoreOwnership binding, bool writing, IPlannerJourneySnapshotSource snapshots,
        IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipReceiptAuthority receipts)
    { _snapshot = snapshot; _actor = actor; _binding = binding; _writing = writing; _snapshots = snapshots; _actors = actors; _receipts = receipts; }
    public Guid EventId => _snapshot.Event.Id;
    public Guid CalendarId => _snapshot.Calendar.Id;
    public DateTimeOffset EventRevision => _snapshot.Event.UpdatedAt;
    public DateTimeOffset CalendarRevision => _snapshot.Calendar.UpdatedAt;

    public async ValueTask<bool> CheckCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != _actor ||
            !await _receipts.IsCurrentAsync(_binding, _actor, cancellationToken).ConfigureAwait(false)) return false;
        var current = await _snapshots.ReadJourneySnapshotAsync(_snapshot.Event.Id, cancellationToken).ConfigureAwait(false);
        if (current is null || current.StoreIdentity.StoreId != _snapshot.StoreIdentity.StoreId ||
            current.Event != _snapshot.Event || current.Calendar != _snapshot.Calendar || !PlannerJourneyCommitAuthority.Permits(current, _writing)) return false;
        return await _actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == _actor &&
            await _receipts.IsCurrentAsync(_binding, _actor, cancellationToken).ConfigureAwait(false);
    }
}
