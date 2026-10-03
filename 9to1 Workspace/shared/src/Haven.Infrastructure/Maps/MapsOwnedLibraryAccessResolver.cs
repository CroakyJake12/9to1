using System.Globalization;
using Haven.Application;
using Haven.Application.Shelf;
namespace Haven.Infrastructure;
/// <summary>Canonical actual-library access for the single owning action; resource IDs are requests,
/// not grants. This resolver has no Home broker dependency and cannot form a DI review cycle.</summary>
public sealed class MapsOwnedLibraryAccessResolver(MapsJourneyService library, IAuthenticatedResourceActorSource actors,
    IResourceStoreOwnershipAuthority ownership) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "maps.library";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionID,
        ResourceScope scope, CancellationToken token)
    {
        ResourceAccessDecision Deny() => new(false, "PermissionDenied", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != ResourceKind || scope.Access != ResourceAccess.Write || actionID != HomeMapsLibraryOwner.ActionID
            || actor.AccountId is not null || actor.OrganisationId is not null || ownership is not IResourceStoreOwnershipReceiptAuthority receipts
            || !Guid.TryParse(scope.Id, out var storeID) || !long.TryParse(scope.Revision, NumberStyles.None,
                CultureInfo.InvariantCulture, out var revision) || revision < 0) return Deny();
        try
        {
            if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor) return Deny();
            var identity = await library.GetStoreIdentityAsync(token).ConfigureAwait(false);
            if (identity.SchemaVersion != 1 || identity.StoreId != storeID) return Deny();
            var binding = await ownership.GetVerifiedAsync("maps", storeID.ToString("D"), token).ConfigureAwait(false);
            if (binding?.Receipt is null || binding.ResourceKind != "maps" || binding.StoreId != storeID.ToString("D")
                || binding.ProfileId != actor.ProfileId || !await receipts.IsCurrentAsync(binding, actor, token).ConfigureAwait(false)) return Deny();
            var snapshot = await library.ReadAsync(token).ConfigureAwait(false);
            if (snapshot.Revision != revision || (await library.GetStoreIdentityAsync(token).ConfigureAwait(false)).StoreId != storeID
                || await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor
                || !await receipts.IsCurrentAsync(binding, actor, token).ConfigureAwait(false)) return Deny();
            return new(true, "Allowed", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return Deny(); }
    }
}
