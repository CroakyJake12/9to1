namespace Haven.Application;

/// <summary>Captures current owning Maps admission outside its settings lease. The resulting check
/// must use only refreshed commit context and the raw Home binding receipt, never recursive Maps reads.</summary>
public interface IMapsJourneyCommitAuthority
{
    ValueTask<ISettingsCommitAdmission?> CaptureAsync(Guid journeyID, long expectedJourneyRevision,
        string actionID, AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}

/// <summary>Personal Maps admission over the actual library root and explicit Home ownership binding.
/// This does not establish Calendar authority or a cross-store atomic revocation transaction.</summary>
public sealed class MapsJourneyCommitAuthority(MapsJourneyService journeys, IAuthenticatedResourceActorSource actors,
    IResourceStoreOwnershipAuthority ownership) : IMapsJourneyCommitAuthority
{
    public async ValueTask<ISettingsCommitAdmission?> CaptureAsync(Guid journeyID, long expectedJourneyRevision,
        string actionID, AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken)
    {
        if (expectedActor is null || journeyID == Guid.Empty || expectedJourneyRevision < 1
            || actionID is not ("maps.planner.attach" or "maps.planner.start")
            || expectedActor.AccountId is not null || expectedActor.OrganisationId is not null
            || string.IsNullOrWhiteSpace(expectedActor.ActorId) || string.IsNullOrWhiteSpace(expectedActor.ProfileId)
            || string.IsNullOrWhiteSpace(expectedActor.AuthenticationRevision)
            || ownership is not IResourceStoreOwnershipReceiptAuthority receipts
            || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return null;
        var identity = await journeys.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty) return null;
        var library = await journeys.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (library.Journeys.SingleOrDefault(journey => journey.JourneyId == journeyID)?.Revision != expectedJourneyRevision) return null;
        var binding = await ownership.GetVerifiedAsync("maps", identity.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "maps" || binding.StoreId != identity.StoreId.ToString("D")
            || binding.ProfileId != expectedActor.ProfileId || !await receipts.IsCurrentAsync(binding, expectedActor, cancellationToken).ConfigureAwait(false)) return null;
        var current = await journeys.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (current.SchemaVersion != 1 || current.StoreId != identity.StoreId
            || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return null;
        return new Admission(identity.StoreId, expectedActor, actors, receipts, binding);
    }

    private sealed class Admission(Guid storeID, AuthenticatedResourceActor actor, IAuthenticatedResourceActorSource actors,
        IResourceStoreOwnershipReceiptAuthority receipts, VerifiedResourceStoreOwnership binding) : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) =>
            context.StoreIdentity.SchemaVersion == 1 && context.StoreIdentity.StoreId == storeID
            && await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == actor
            && await receipts.IsCurrentAsync(binding, actor, cancellationToken).ConfigureAwait(false)
            && await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == actor;
    }
}
