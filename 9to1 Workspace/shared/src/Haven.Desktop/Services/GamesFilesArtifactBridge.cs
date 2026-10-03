using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Games;
using Haven.Core.Games;
using HavenOS.Files;

namespace Haven.Desktop.Services;

/// <summary>Owning Games persistence through actual Home/Files authority. Payload candidates are immutable;
/// Files CAS publishes their revision. A failed commit retains the prior canonical project and recoverable candidate.</summary>
public sealed class GamesFilesArtifactBridge(NativeFilesWorkspaceAuthority files, NativeFilesArtifactContentReader reader,
    ResourceAuthorizationService resources, IAuthenticatedResourceActorSource? actors = null) : IActorBoundGamesProjectStore
{
    public async Task<GamesStoredProject> OpenAsync(Guid fileID, CancellationToken cancellationToken = default)
    {
        if (fileID == Guid.Empty) throw new ArgumentException("File identity is required.");
        var read = await reader.ReadAsync("games", new(fileID), cancellationToken: cancellationToken).ConfigureAwait(false);
        var project = GamesProjectCodec.Decode(read.Bytes);
        if (read.Reference.ArtifactId != project.ProjectID.ToString("D")
            || read.Revision.OwningAppRevisionId != project.Revision.ToString(CultureInfo.InvariantCulture)
            || read.Metadata.CurrentRevisionId is null)
            throw new InvalidDataException("Games project and canonical Files identities do not match.");
        return new(fileID, read.Metadata.CurrentRevisionId.Value.Value, read.Revision.Id.Value, project);
    }

    public Task<GamesStoredProject> SaveAsync(Guid fileID, Guid expectedStructuralRevisionID, long expectedProjectRevision,
        GamesProjectDocument project, CancellationToken cancellationToken = default)
        => SaveCoreAsync(null, fileID, expectedStructuralRevisionID, expectedProjectRevision, project, cancellationToken);

    public Task<GamesStoredProject> SaveForActorAsync(AuthenticatedResourceActor expectedActor, Guid fileID,
        Guid expectedStructuralRevisionID, long expectedProjectRevision, GamesProjectDocument project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return SaveCoreAsync(expectedActor, fileID, expectedStructuralRevisionID, expectedProjectRevision, project, cancellationToken);
    }

    private async Task<GamesStoredProject> SaveCoreAsync(AuthenticatedResourceActor? expectedActor, Guid fileID,
        Guid expectedStructuralRevisionID, long expectedProjectRevision, GamesProjectDocument project, CancellationToken cancellationToken)
    {
        if (actors is null) throw new UnauthorizedAccessException("A trusted current actor source is required at the Games commit boundary.");
        var captured = project.Capture();
        if (expectedActor is not null && (await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false))?.Actor != expectedActor)
            throw new UnauthorizedAccessException("The actor that claimed this Games operation is no longer current.");
        var bytes = GamesProjectCodec.Encode(captured);
        var opened = await OpenAsync(fileID, cancellationToken).ConfigureAwait(false);
        if (opened.StructuralRevisionID != expectedStructuralRevisionID || opened.Project.Revision != expectedProjectRevision
            || captured.Revision != checked(expectedProjectRevision + 1)) throw new InvalidOperationException("GamesProjectRevisionConflict");
        if (captured.ProjectID != opened.Project.ProjectID) throw new InvalidDataException("Games cannot replace a canonical project identity.");
        foreach (var scene in captured.Scenes)
        {
            var prior = opened.Project.Scenes.SingleOrDefault(item => item.SceneID == scene.SceneID);
            if (prior is not null && JsonSerializer.Serialize(prior) != JsonSerializer.Serialize(scene)
                && scene.Revision != checked(prior.Revision + 1)) throw new InvalidOperationException("GamesSceneRevisionConflict");
        }
        var workspace = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home has not verified this Files store.");
        var scope = new ResourceScope("files.item", fileID.ToString("N"), expectedStructuralRevisionID.ToString("N"), ResourceAccess.Write);
        var actor = await resources.AuthorizeAsync("games.file.save", [scope], cancellationToken).ConfigureAwait(false);
        if (actor != workspace.Actor || expectedActor is not null && actor != expectedActor)
            throw new UnauthorizedAccessException("Current Games project write permission is unavailable for the claimed actor.");
        var root = await files.ResolveAppDirectoryAsync("games", cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The registered Games payload directory is unavailable.");
        var relative = Path.Combine(".9to1-artifacts", fileID.ToString("N"), Guid.NewGuid().ToString("N") + ".9to1g");
        var path = SafePath(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        path = SafePath(root, relative);
        await RecheckAsync().ConfigureAwait(false);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        await RecheckAsync().ConfigureAwait(false);
        SafePath(root, relative);
        var result = await workspace.Provider.CommitDurableRevisionAsync(new(new(fileID), "games",
            captured.Revision.ToString(CultureInfo.InvariantCulture), actor!.ActorId, DateTimeOffset.UtcNow, bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)), relative, new(expectedStructuralRevisionID)),
            new FilesCommitAuthorityGuard(actor!.ActorId, async token => await actors.GetCurrentAsync(token).ConfigureAwait(false) == actor),
            cancellationToken).ConfigureAwait(false);
        if (result.Error?.Code == FilesErrorCode.PermissionDenied)
            throw new UnauthorizedAccessException("Games commit authority changed; the prior canonical project remains available.");
        if (!result.IsSuccess) throw new InvalidOperationException("GamesProjectRevisionConflict: candidate retained; " + result.Error?.Message);
        return new(fileID, result.Value!.Id.Value, result.Value.Id.Value, captured);

        async Task RecheckAsync()
        {
            var current = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (current?.Actor != actor || current.Configuration.StoreId != workspace.Configuration.StoreId
                || await files.ResolveAppDirectoryAsync("games", cancellationToken).ConfigureAwait(false) != root
                || await resources.AuthorizeAsync("games.file.save", [scope], cancellationToken).ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("Games write authority changed; the prior canonical project remains available.");
        }
    }

    private static string SafePath(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Games candidate escapes the Files payload anchor.");
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Games payload anchor redirects outside current Files authority.");
        return full;
    }
}
