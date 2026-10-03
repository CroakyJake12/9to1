using System.Globalization;
using Haven.Application.Games;
using Haven.Core.Games;
using HavenOS.Files;

namespace Haven.Desktop.Services;

/// <summary>Projects remain canonical Files artifacts. This source cannot select a private store or bypass Home ownership.</summary>
public sealed class GamesCanonicalProjectSource(NativeFilesWorkspaceAuthority files,
    NativeFilesArtifactContentReader content) : ICanonicalGamesSceneSource
{
    public async Task<GamesSceneSnapshot?> GetAsync(Guid projectID, Guid sceneID, CancellationToken cancellationToken)
    {
        if (projectID == Guid.Empty || sceneID == Guid.Empty) throw new ArgumentException("Games project and scene IDs are required.");
        var workspace = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home has not verified the canonical Files store.");
        var identity = projectID.ToString("D");
        var found = await workspace.Provider.GetArtifactByOwnerIdentityAsync("games", identity, cancellationToken).ConfigureAwait(false);
        if (!found.IsSuccess)
        {
            if (found.Error?.Code == FilesErrorCode.ItemNotFound) return null;
            throw new InvalidDataException("The canonical Games artifact identity is unavailable or ambiguous.");
        }
        var reference = found.Value!;
        var read = await content.ReadAsync("games", reference.FileId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (read.Reference.FileId != reference.FileId || read.Reference.OwnerAppId != "games"
            || read.Reference.ArtifactId != identity || read.Reference.ArtifactType != nameof(FilesArtifactType.GameProject))
            throw new InvalidDataException("The Games artifact identity changed during its read.");
        var project = GamesProjectCodec.Decode(read.Bytes);
        if (project.ProjectID != projectID || read.Revision.OwningAppRevisionId != project.Revision.ToString(CultureInfo.InvariantCulture))
            throw new InvalidDataException("The Games project does not match its canonical Files identity or owning revision.");
        var current = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current?.Actor != workspace.Actor || current.Configuration.StoreId != workspace.Configuration.StoreId)
            throw new UnauthorizedAccessException("The Games store authority changed during its read.");
        var final = await current.Provider.GetArtifactByOwnerIdentityAsync("games", identity, cancellationToken).ConfigureAwait(false);
        if (!final.IsSuccess || final.Value!.FileId != reference.FileId)
            throw new InvalidDataException("The canonical Games artifact binding changed during its read.");
        var finalContent = await current.Provider.GetCurrentArtifactContentAsync(reference.FileId, cancellationToken).ConfigureAwait(false);
        if (!finalContent.IsSuccess || finalContent.Value!.Revision.Id != read.Revision.Id)
            throw new InvalidDataException("The canonical Games content revision changed during its read.");
        return project.Scenes.SingleOrDefault(scene => scene.SceneID == sceneID)?.Capture();
    }
}
