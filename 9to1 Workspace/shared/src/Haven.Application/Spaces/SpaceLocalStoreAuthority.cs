namespace Haven.Application;

/// <summary>Captures explicit Home ownership of the real local Spaces settings store.
/// This never provisions an ownership binding or treats an account/organisation claim as local authority.</summary>
public sealed class SpaceLocalStoreAuthority(IResourceStoreIdentitySource identities,
    IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipAuthority ownership, Func<bool> hostAllowsWrites)
{
    /// <summary>Observes the original configured dependencies. This supplies no write decision
    /// or receipt; CaptureWriteAdmissionAsync remains required for every owning mutation.</summary>
    public bool MatchesOriginalInputs(IResourceStoreIdentitySource originalIdentities,
        IAuthenticatedResourceActorSource originalActors, IResourceStoreOwnershipAuthority originalOwnership) =>
        ReferenceEquals(identities, originalIdentities) && ReferenceEquals(actors, originalActors) &&
        ReferenceEquals(ownership, originalOwnership);

    public ValueTask<ISettingsCommitAdmission> CaptureWriteAdmissionAsync(CancellationToken cancellationToken = default) =>
        CaptureAsync(null, cancellationToken);

    public ValueTask<ISettingsCommitAdmission> CaptureWriteAdmissionForActorAsync(AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return CaptureAsync(expectedActor, cancellationToken);
    }

    private async ValueTask<ISettingsCommitAdmission> CaptureAsync(AuthenticatedResourceActor? expectedActor, CancellationToken token)
    {
        if (!hostAllowsWrites() || ownership is not IResourceStoreOwnershipReceiptAuthority receipts)
            throw new UnauthorizedAccessException("Current Home ownership does not permit Space writes.");
        var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null ||
            expectedActor is not null && actor != expectedActor)
            throw new UnauthorizedAccessException("The current local Space actor is unavailable.");
        var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty)
            throw new UnauthorizedAccessException("The canonical Space store identity is unavailable.");
        var storeId = identity.StoreId.ToString("D");
        var verified = await receipts.GetVerifiedAsync("spaces", storeId, token).ConfigureAwait(false);
        if (verified?.Receipt is null || verified.ResourceKind != "spaces" || verified.StoreId != storeId ||
            verified.ProfileId != actor.ProfileId || !await receipts.IsCurrentAsync(verified, actor, token).ConfigureAwait(false) ||
            !hostAllowsWrites())
            throw new UnauthorizedAccessException("Home has not verified current ownership of this Space store.");
        return new Admission(identity.StoreId, receipts, verified, actor, hostAllowsWrites);
    }

    // Home is observed under the settings lease at admission and immediately before publication.
    // These separate stores do not provide one globally atomic revocation transaction.
    private sealed class Admission(Guid storeId, IResourceStoreOwnershipReceiptAuthority receipts,
        VerifiedResourceStoreOwnership captured, AuthenticatedResourceActor actor, Func<bool> allowsWrites) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token) =>
            allowsWrites() && context.StoreIdentity.SchemaVersion == 1 && context.StoreIdentity.StoreId == storeId
                ? receipts.IsCurrentAsync(captured, actor, token) : ValueTask.FromResult(false);
    }
}
