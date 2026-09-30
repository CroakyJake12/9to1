using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Read-only Application port over canonical Home ownership; cannot create or approve a binding.</summary>
public sealed class HomeResourceStoreOwnershipAuthority(HomeLocalStoreOwnership ownership,
    IAuthenticatedResourceActorSource actors) : IResourceStoreOwnershipAuthority
{
    public async ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedAsync(string resourceKind, string storeId,
        CancellationToken cancellationToken)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.OrganisationId is not null) return null;
        var binding = await ownership.GetVerifiedAsync(resourceKind, storeId, cancellationToken).ConfigureAwait(false);
        var current = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (binding is null || actor != current || binding.ProfileId != actor.ProfileId) return null;
        return new(binding.ResourceKind, binding.StoreId, binding.ProfileId, binding.ObservedStoreRevision);
    }
}
