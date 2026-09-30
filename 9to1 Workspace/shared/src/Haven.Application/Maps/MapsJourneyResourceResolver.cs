using System.Globalization;

namespace Haven.Application;

/// <summary>Bound by the host to the owning profile's Maps library; payloads cannot nominate its owner.</summary>
public sealed class MapsJourneyResourceResolver(MapsJourneyService journeys, string ownerProfileID,
    Guid? organisationID = null) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "maps.journey";

    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        bool AllowedAction() => (actionId, scope.Access) is
            ("maps.planner.attach", ResourceAccess.Write) or ("maps.planner.read", ResourceAccess.Read)
            or ("maps.planner.start", ResourceAccess.Execute);
        if (string.IsNullOrWhiteSpace(ownerProfileID) || actor.ProfileId != ownerProfileID
            || actor.OrganisationId != organisationID || scope.Kind != ResourceKind || !AllowedAction()
            || !Guid.TryParse(scope.Id, out var journeyID) || journeyID == Guid.Empty)
            return new(false, "Denied", actor.ActorId, scope.Revision, organisationID);
        var journey = (await journeys.ReadAsync(cancellationToken).ConfigureAwait(false)).Journeys
            .FirstOrDefault(item => item.JourneyId == journeyID);
        var revision = journey?.Revision.ToString(CultureInfo.InvariantCulture) ?? "missing";
        return new(journey is not null && revision == scope.Revision, journey is null ? "NotFound" : "RevisionChecked",
            actor.ActorId, revision, organisationID);
    }
}
