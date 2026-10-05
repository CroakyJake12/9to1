using System.Globalization;

namespace Haven.Application.Games;

/// <summary>The source is the host's actual owning artifact reader, including its current Files ACL.
/// Lazy source resolution avoids a constructor cycle with the Files authorization broker; it never authorizes games.scene recursively.</summary>
public sealed class GamesSceneResourceResolver(Func<ICanonicalGamesSceneSource> source,
    IAuthenticatedResourceActorSource actors) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "games.scene";

    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Denied(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        var ids = scope.Id?.Split('/') ?? [];
        if (scope.Kind != ResourceKind || actionId != "games.scene.observe" || scope.Access != ResourceAccess.Read
            || ids.Length != 2 || !Guid.TryParseExact(ids[0], "D", out var projectID) || projectID == Guid.Empty
            || !Guid.TryParseExact(ids[1], "D", out var sceneID) || sceneID == Guid.Empty
            || !long.TryParse(scope.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected < 1
            || expected.ToString(CultureInfo.InvariantCulture) != scope.Revision)
            return Denied("InvalidScope");
        if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Denied("ActorChanged");
        try
        {
            var scene = await source().GetAsync(projectID, sceneID, cancellationToken).ConfigureAwait(false);
            if (scene is null || scene.ProjectID != projectID || scene.SceneID != sceneID || scene.Revision != expected)
                return Denied("RevisionOrIdentityChanged");
            if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Denied("ActorChanged");
            return new(true, "CanonicalArtifactChecked", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (UnauthorizedAccessException) { return Denied("ArtifactAccessDenied"); }
        catch (InvalidDataException) { return Denied("ArtifactInvalid"); }
        catch (IOException) { return Denied("ArtifactUnavailable"); }
    }
}
