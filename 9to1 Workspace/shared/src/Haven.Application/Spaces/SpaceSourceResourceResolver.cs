using System.Globalization;

namespace Haven.Application;

/// <summary>Space source permissions intersect store ownership and the native host's current access mode.</summary>
public sealed class SpaceSourceResourceResolver(IAuthenticatedResourceActorSource actors,
    IResourceStoreIdentitySource identities, IResourceStoreOwnershipAuthority ownership, SpaceRegistry spaces,
    Func<bool> hostAllowsWrites) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "spaces.source";

    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        var ids = scope.Id.Split(':');
        if (scope.Kind != ResourceKind || ids.Length != 2 || !Guid.TryParse(ids[0], out var spaceId) || spaceId == Guid.Empty ||
            !Guid.TryParse(ids[1], out var sourceId) || sourceId == Guid.Empty ||
            scope.Access is not (ResourceAccess.Read or ResourceAccess.Write)) return Deny("SpaceSourceScopeInvalid");
        if (actor.AccountId is not null || actor.OrganisationId is not null ||
            actor != await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)) return Deny("SpaceProfileUnavailable");
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        var storeId = identity.StoreId.ToString("D");
        var binding = await ownership.GetVerifiedAsync("spaces", storeId, cancellationToken).ConfigureAwait(false);
        if (binding is null || binding.ResourceKind != "spaces" || binding.StoreId != storeId || binding.ProfileId != actor.ProfileId)
            return Deny("SpaceStoreOwnershipUnavailable");
        var space = await spaces.GetAsync(spaceId, cancellationToken).ConfigureAwait(false);
        if (space is null || space.IsArchived) return Deny("SpaceUnavailable");
        if (space.Revision.ToString(CultureInfo.InvariantCulture) != scope.Revision) return Deny("SpaceRevisionConflict");
        var references = space.ContextReferences?.Where(reference => reference.ContextId == sourceId).ToArray() ?? [];
        if (references.Length != 1 || references[0].HostedFileId is not { } hostedId || hostedId == Guid.Empty)
            return Deny("SpaceSourceUnavailable");
        var reference = references[0];
        var owner = reference.Kind switch
        {
            SpaceContextReferenceKind.WriteArtifact => "write",
            SpaceContextReferenceKind.CanvasArtifact => "canvas",
            SpaceContextReferenceKind.PictureArtifact => "picture",
            _ => null
        };
        if (owner is null || reference.OwnerAppId != owner ||
            actionId != owner + (scope.Access == ResourceAccess.Read ? ".file.open" : ".file.save"))
            return Deny("SpaceSourceActionUnavailable");
        if (reference.Permission is not (SpaceContextPermission.Read or SpaceContextPermission.ReadWrite))
            return Deny("SpaceSourcePermissionDenied");
        if (scope.Access == ResourceAccess.Write &&
            (reference.Permission != SpaceContextPermission.ReadWrite || !hostAllowsWrites())) return Deny("SpaceSourceReadOnly");
        var currentIdentity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        var currentBinding = await ownership.GetVerifiedAsync("spaces", currentIdentity.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var currentSpace = await spaces.GetAsync(spaceId, cancellationToken).ConfigureAwait(false);
        if (currentIdentity.StoreId != identity.StoreId || currentBinding != binding ||
            currentSpace?.Revision != space.Revision || actor != await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ||
            (scope.Access == ResourceAccess.Write && !hostAllowsWrites())) return Deny("SpaceSourceAccessChanged");
        return new(true, "SpaceSourceAllowed", actor.ActorId, scope.Revision, null);
    }
}
