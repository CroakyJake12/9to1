using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Read-only Application port over canonical Home ownership; cannot create or approve a binding.</summary>
public sealed class HomeResourceStoreOwnershipAuthority(HomeLocalStoreOwnership ownership,
    IAuthenticatedResourceActorSource actors) : IResourceStoreOwnershipReceiptAuthority
{
    internal bool IsBoundTo(IHomeCoreStateStore store, HomeLocalProfileIdentity profiles) =>
        ownership.IsBoundTo(store, profiles) && ReferenceEquals(actors, profiles);
    public async ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedAsync(string resourceKind, string storeId,
        CancellationToken cancellationToken)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.OrganisationId is not null) return null;
        var binding = await ownership.GetVerifiedAsync(resourceKind, storeId, cancellationToken).ConfigureAwait(false);
        var current = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (binding is null || actor != current || binding.ProfileId != actor.ProfileId) return null;
        var receipt = await ownership.CaptureReceiptAsync(binding, cancellationToken).ConfigureAwait(false);
        if (receipt is null || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return null;
        return new(binding.ResourceKind, binding.StoreId, binding.ProfileId, binding.ObservedStoreRevision) { Receipt = receipt };
    }

    public async ValueTask<bool> IsCurrentAsync(VerifiedResourceStoreOwnership captured,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken)
    {
        if (captured.Receipt is null || expectedActor.AccountId is not null || expectedActor.OrganisationId is not null ||
            expectedActor.ProfileId != captured.ProfileId ||
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return false;
        return await ownership.IsReceiptCurrentAsync(captured, cancellationToken).ConfigureAwait(false) &&
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == expectedActor;
    }
}
