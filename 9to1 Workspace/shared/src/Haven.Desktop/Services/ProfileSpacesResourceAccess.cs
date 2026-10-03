using Haven.Application;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Services;

/// <summary>Personal Spaces require an explicit verified Home store binding. A logged-in profile
/// alone never claims existing Spaces; organisation policy requires its separate owner adapter.</summary>
public sealed class ProfileSpacesResourceAccess(IAuthenticatedResourceActorSource actors,
    IResourceStoreIdentitySource identities, IResourceStoreOwnershipAuthority ownership, SpaceRegistry spaces,
    Func<AppAiAccessMode> currentHostAccessMode) : IPluginSidebarSpaceAccess
{
    public Task<bool> MayReadAsync(Guid spaceId, CancellationToken cancellationToken) => AllowsAsync(spaceId, false, cancellationToken);
    public Task<bool> MayManageAsync(Guid spaceId, CancellationToken cancellationToken) => AllowsAsync(spaceId, true, cancellationToken);

    private async Task<bool> AllowsAsync(Guid spaceId, bool writing, CancellationToken cancellationToken)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.OrganisationId is not null || (writing && currentHostAccessMode() != AppAiAccessMode.Write)) return false;
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        var binding = await ownership.GetVerifiedAsync("spaces", identity.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (binding?.ProfileId != actor.ProfileId || binding.ResourceKind != "spaces" || binding.StoreId != identity.StoreId.ToString("D")) return false;
        var space = await spaces.ReadExistingAsync(spaceId, cancellationToken).ConfigureAwait(false);
        return space is { IsArchived: false } &&
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == actor &&
            (!writing || currentHostAccessMode() == AppAiAccessMode.Write);
    }
}
