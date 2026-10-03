using System.Globalization;

namespace Haven.Application;

/// <summary>Bound by the host to the owning profile's Maps library; payloads cannot nominate its owner.</summary>
public sealed class MapsJourneyResourceResolver(MapsJourneyService journeys, IResourceStoreIdentitySource identities,
    IResourceStoreOwnershipAuthority ownership) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "maps.journey";

    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        bool AllowedAction() => (actionId, scope.Access) is
            ("maps.planner.attach", ResourceAccess.Write) or ("maps.planner.read", ResourceAccess.Read)
            or ("maps.planner.start", ResourceAccess.Execute);
        if (actor.OrganisationId is not null || scope.Kind != ResourceKind || !AllowedAction()
            || !Guid.TryParse(scope.Id, out var journeyID) || journeyID == Guid.Empty)
            return new(false, "Denied", actor.ActorId, scope.Revision, null);
        ResourceStoreIdentity identity;
        try { identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false); }
        catch (InvalidOperationException) { return new(false, "StoreIdentityUnavailable", actor.ActorId, scope.Revision, null); }
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty) return new(false, "StoreIdentityUnavailable", actor.ActorId, scope.Revision, null);
        var binding = await ownership.GetVerifiedAsync("maps", identity.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (binding is null || binding.ResourceKind != "maps" || binding.StoreId != identity.StoreId.ToString("D")
            || binding.ProfileId != actor.ProfileId || string.IsNullOrWhiteSpace(binding.ObservedStoreRevision))
            return new(false, "StoreOwnershipDenied", actor.ActorId, scope.Revision, null);
        var journey = (await journeys.ReadAsync(cancellationToken).ConfigureAwait(false)).Journeys
            .FirstOrDefault(item => item.JourneyId == journeyID);
        var currentIdentity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (currentIdentity.SchemaVersion != 1 || currentIdentity.StoreId != identity.StoreId
            || await ownership.GetVerifiedAsync("maps", identity.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false) != binding)
            return new(false, "StoreOwnershipChanged", actor.ActorId, scope.Revision, null);
        var revision = journey?.Revision.ToString(CultureInfo.InvariantCulture) ?? "missing";
        return new(journey is not null && revision == scope.Revision, journey is null ? "NotFound" : "RevisionChecked",
            actor.ActorId, revision, null);
    }
}
