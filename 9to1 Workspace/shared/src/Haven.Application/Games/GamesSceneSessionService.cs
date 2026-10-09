using System.Globalization;
using Haven.Core.Games;

namespace Haven.Application.Games;

public interface ICanonicalGamesSceneSource
{
    /// <summary>Reads the actual project artifact; no private copied project or path-based identity.</summary>
    Task<GamesSceneSnapshot?> GetAsync(Guid projectID, Guid sceneID, CancellationToken cancellationToken);
}

/// <summary>Structured native scene observation. Missing canonical owner/ACL adapters deny through
/// the shared authority; this service never executes project/user scripts or publishes scene edits.</summary>
public sealed class GamesSceneSessionService(ResourceAuthorizationService authority,
    ICanonicalGamesSceneSource scenes, IGamesSceneRuntime runtime)
{
    public async Task<GamesNativeSceneObservation> ObserveAsync(Guid projectID, Guid sceneID, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (projectID == Guid.Empty || sceneID == Guid.Empty || expectedRevision < 1)
            throw new ArgumentException("Games scene identity is invalid.");
        ResourceScope[] scopes = [new("games.scene", projectID.ToString("D") + "/" + sceneID.ToString("D"),
            expectedRevision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read)];
        var actor = await authority.AuthorizeAsync("games.scene.observe", scopes, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Games scene read permission is unavailable.");
        var scene = (await scenes.GetAsync(projectID, sceneID, cancellationToken).ConfigureAwait(false))?.Capture()
            ?? throw new KeyNotFoundException("GamesSceneNotFound");
        if (scene.ProjectID != projectID || scene.SceneID != sceneID || scene.Revision != expectedRevision)
            throw new InvalidOperationException("GamesSceneRevisionConflict");
        if (await authority.AuthorizeAsync("games.scene.observe", scopes, cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Games scene permission changed before native observation.");
        var observed = await runtime.ObserveAsync(scene, cancellationToken).ConfigureAwait(false);
        if (observed.ProjectID != projectID || observed.SceneID != sceneID || observed.Revision != expectedRevision)
            throw new InvalidDataException("Games native observation identity does not match the canonical project.");
        if (await authority.AuthorizeAsync("games.scene.observe", scopes, cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Games scene permission changed during native observation.");
        return observed;
    }
}
