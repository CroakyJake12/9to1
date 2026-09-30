using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed record NativeFilesArtifactContent(FilesArtifactReference Reference, FilesRevision Revision, byte[] Bytes, HostedItemMetadata Metadata);

/// <summary>Operation-scoped canonical bytes; no app-owned storage or caller-selected directory.</summary>
public sealed class NativeFilesArtifactContentReader(NativeFilesWorkspaceAuthority files,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources)
{
    private const long MaximumBytes = 16 * 1024 * 1024;
    public async Task<NativeFilesArtifactContent> ReadAsync(string ownerAppId, HostedItemId fileId,
        FilesRevisionId? expectedContentRevision = null, CancellationToken cancellationToken = default)
    {
        var type = ownerAppId switch
        {
            "write" => nameof(FilesArtifactType.WriteDocument),
            "canvas" => nameof(FilesArtifactType.Canvas),
            "picture" => nameof(FilesArtifactType.Picture),
            "games" => nameof(FilesArtifactType.GameProject),
            _ => throw new NotSupportedException("No canonical native artifact reader is registered for this owner.")
        };
        var workspace = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home has not verified this Files store.");
        var actor = workspace.Actor;
        var metadata = await workspace.Provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        var reference = await workspace.Provider.GetArtifactAsync(fileId, cancellationToken).ConfigureAwait(false);
        var content = await workspace.Provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || !reference.IsSuccess || !content.IsSuccess ||
            metadata.Value!.Kind != HostedItemKind.Artifact || reference.Value!.OwnerAppId != ownerAppId || reference.Value.ArtifactType != type ||
            content.Value!.Revision.ItemId != fileId || content.Value.Revision.OwningAppId != ownerAppId ||
            expectedContentRevision is { } expected && expected != content.Value.Revision.Id)
            throw new InvalidDataException("The canonical artifact identity or revision is unavailable.");
        if (!workspace.Configuration.AppFolders.ContainsKey(ownerAppId))
            throw new UnauthorizedAccessException("The artifact has no registered owning app materialisation anchor.");
        var scope = new ResourceScope("files.item", fileId.ToString(), metadata.Value.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
        var action = ownerAppId + ".file.open";
        if (await resources.AuthorizeAsync(action, [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Current artifact read access is unavailable.");
        var root = await files.ResolveAppDirectoryAsync(ownerAppId, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The canonical app directory is unavailable.");
        var path = SafePath(root, content.Value.ProviderContentReference);
        var info = new FileInfo(path);
        if (content.Value.Revision.SizeBytes is not { } size || size < 0 || size > MaximumBytes || info.Length != size)
            throw new InvalidDataException("The canonical artifact size does not match its durable revision.");
        var bytes = new byte[checked((int)size)];
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        {
            if (stream.Length != size) throw new InvalidDataException("The artifact changed before its bounded read.");
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.ReadByte() != -1) throw new InvalidDataException("The artifact grew beyond its durable revision.");
        }
        var hash = content.Value.Revision.ContentHash;
        if (bytes.LongLength != size || hash is null ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? hash[7..] : hash,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The canonical artifact hash does not match its durable revision.");
        var current = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current?.Actor != actor || current.Configuration.StoreId != workspace.Configuration.StoreId ||
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor ||
            await resources.AuthorizeAsync(action, [scope], cancellationToken).ConfigureAwait(false) != actor ||
            (await current.Provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false)).Value != metadata.Value ||
            (await current.Provider.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false)).Value != content.Value ||
            await files.ResolveAppDirectoryAsync(ownerAppId, cancellationToken).ConfigureAwait(false) != root)
            throw new UnauthorizedAccessException("Artifact access changed while its bytes were being read.");
        SafePath(root, content.Value.ProviderContentReference);
        return new(reference.Value, content.Value.Revision, bytes, metadata.Value);
    }

    private static string SafePath(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\0') || Path.IsPathFullyQualified(relative) ||
            relative.Split(['/', '\\']).Any(part => part is "." or "..")) throw new InvalidDataException("Invalid Files-relative artifact reference.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Artifact reference escapes its Files directory.");
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Artifact content redirects outside its Files authority.");
        return full;
    }
}
