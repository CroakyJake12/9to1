using System.Globalization;
using Haven.Core.Games;

namespace Haven.Application.Games;

/// <summary>Runs only the pinned first-party C# scene driver after both actual Files and scene authority checks.
/// This read operation does not authorise arbitrary project/user code, package installation or game export.</summary>
public sealed class GamesManagedProjectSessionService(ResourceAuthorizationService authority,
    ICanonicalGamesProjectStore projects, IGamesManagedProjectRuntime runtime)
{
    public async Task<GamesManagedProjectObservation> ObserveAsync(Guid fileID, Guid expectedStructuralRevisionID,
        Guid projectID, long expectedProjectRevision, Guid sceneID, long expectedSceneRevision, CancellationToken token = default)
    {
        if (fileID == Guid.Empty || expectedStructuralRevisionID == Guid.Empty || projectID == Guid.Empty || sceneID == Guid.Empty
            || expectedProjectRevision < 1 || expectedSceneRevision < 1) throw new ArgumentException("Canonical Games runtime identities are required.");
        ResourceScope[] scopes = [new("files.item", fileID.ToString("N"), expectedStructuralRevisionID.ToString("N"), ResourceAccess.Read),
            new("games.scene", projectID.ToString("D") + "/" + sceneID.ToString("D"), expectedSceneRevision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read)];
        var actor = await authority.AuthorizeAsync("games.scene.observe", scopes, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current Files and Games scene read authority is required.");
        var opened = await projects.OpenAsync(fileID, token).ConfigureAwait(false);
        var captured = opened.Project.Capture();
        if (opened.FileID != fileID || opened.StructuralRevisionID != expectedStructuralRevisionID || captured.ProjectID != projectID
            || captured.Revision != expectedProjectRevision || !captured.Scenes.Any(scene => scene.SceneID == sceneID && scene.Revision == expectedSceneRevision))
            throw new InvalidOperationException("GamesProjectRevisionConflict");
        if (await authority.AuthorizeAsync("games.scene.observe", scopes, token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Games authority changed before the managed observation.");
        var observed = await runtime.ObserveAsync(captured, sceneID, token).ConfigureAwait(false);
        if (observed.ProjectID != projectID || observed.ProjectRevision != expectedProjectRevision || observed.SceneID != sceneID || observed.SceneRevision != expectedSceneRevision)
            throw new InvalidDataException("Managed observation does not match the authorised project revision.");
        if (await authority.AuthorizeAsync("games.scene.observe", scopes, token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Games authority changed during the managed observation.");
        return observed;
    }
}
