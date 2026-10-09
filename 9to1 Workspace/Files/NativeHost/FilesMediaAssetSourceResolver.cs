using System.Security.Cryptography;
using Haven.Application;
using Haven.Core.Media;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed record FilesMediaSourceBinding(IFilesProvider Provider, FilesMaterializationRegistry Materializations);

/// <summary>Operation leases are Files-owned immutable copies of a verified canonical revision,
/// never an app-private import. Owning export hosts must separately broker their output action.</summary>
public sealed class FilesMediaAssetSourceResolver(IAuthenticatedResourceActorSource actors,
    Func<AuthenticatedResourceActor, FilesMediaSourceBinding?> bindings,
    FilesWorkspaceDirectoryResolver directories, ResourceAuthorizationService authorization) : IMediaRetainedAssetSourceResolver
{
    public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
        string? expectedRevision, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(fileID, assetID, expectedRevision, false, cancellationToken);

    /// <summary>Leases a retained revision only when its immutable reference is still verified under the actual source folder binding.</summary>
    public Task<MediaEngineResult<MediaAssetReadLease>> ResolveRetainedAsync(string fileID, MediaAssetId assetID,
        string expectedRevision, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(fileID, assetID, expectedRevision, true, cancellationToken);

    private async Task<MediaEngineResult<MediaAssetReadLease>> ResolveCoreAsync(string fileID, MediaAssetId assetID,
        string? expectedRevision, bool retained, CancellationToken cancellationToken)
    {
        MediaEngineResult<MediaAssetReadLease> Fail(MediaEngineErrorCode code, string message) =>
            MediaEngineResult<MediaAssetReadLease>.Failure(new(code, message, "media.asset.read", fileID, true, true));
        if (!Guid.TryParse(fileID, out var id) || id == Guid.Empty || assetID.IsEmpty)
            return Fail(MediaEngineErrorCode.UnsupportedSource, "A canonical Files identity and media asset identity are required.");
        string? leaseDirectory = null;
        try
        {
            var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            var binding = actor is null ? null : bindings(actor);
            if (actor is null || binding is null) return Fail(MediaEngineErrorCode.PermissionDenied, "No authorised Files provider is active.");
            var fileId = new HostedItemId(id);
            var metadata = await binding.Provider.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
            if (!metadata.IsSuccess || metadata.Value!.CurrentRevisionId is not { } revision)
                return Fail(MediaEngineErrorCode.SourceUnavailable, "The canonical Files revision is unavailable.");
            var contentRevision = revision;
            FilesArtifactContentRevision? retainedContent = null;
            if (binding.Provider is DurableDriveProvider durable)
            {
                if (retained && (!Guid.TryParse(expectedRevision, out var requestedRevision) || requestedRevision == Guid.Empty))
                    return Fail(MediaEngineErrorCode.UnsupportedSource, "A retained canonical content revision is required.");
                var committedContent = retained
                    ? await durable.GetArtifactRevisionContentAsync(fileId, new(Guid.Parse(expectedRevision!)), cancellationToken).ConfigureAwait(false)
                    : await durable.GetCurrentArtifactContentAsync(fileId, cancellationToken).ConfigureAwait(false);
                if (!committedContent.IsSuccess) return Fail(MediaEngineErrorCode.SourceUnavailable, "Files has no committed media content revision.");
                contentRevision = committedContent.Value!.Revision.Id;
                if (retained) retainedContent = committedContent.Value;
            }
            var revisionText = contentRevision.ToString();
            if (expectedRevision is not null &&
                (!Guid.TryParse(expectedRevision, out var expectedContentRevision) || expectedContentRevision != contentRevision.Value))
                return Fail(MediaEngineErrorCode.RevisionConflict, "The media project references another Files revision.");
            // Preserve the caller's opaque token only after validating this Files GUID revision.
            if (retained) revisionText = expectedRevision!;
            var scope = new ResourceScope("files.item", fileId.ToString(), revision.ToString(), ResourceAccess.Read);
            if (await authorization.AuthorizeAsync("media.asset.read", [scope], cancellationToken).ConfigureAwait(false) != actor)
                return Fail(MediaEngineErrorCode.PermissionDenied, "Current Files access does not permit this media source.");
            var root = retained && retainedContent?.UploadAnchorFolderId is { } anchor
                ? await directories.ResolveFolderAsync(actor.AccountId ?? Guid.Empty,
                    actor.AccountId is null && Guid.TryParse(actor.ProfileId, out var anchorProfile) ? anchorProfile : null, anchor, cancellationToken).ConfigureAwait(false)
                : actor.AccountId is { } account
                ? await directories.ResolveAsync(account, "media", cancellationToken).ConfigureAwait(false)
                : Guid.TryParse(actor.ProfileId, out var profile)
                    ? await directories.ResolveProfileAsync(profile, "media", cancellationToken).ConfigureAwait(false)
                    : null;
            if (root is null || !root.IsSuccess) return Fail(MediaEngineErrorCode.PermissionDenied, "Files has no authorised operation-lease folder binding.");
            var rootDirectory = Path.TrimEndingDirectorySeparator(root.Value!.DirectoryPath);
            string sourcePath;
            long sourceSize;
            string? sourceHash;
            if (retained)
            {
                if (retainedContent is null || retainedContent.UploadAnchorFolderId != root.Value!.FolderId ||
                    retainedContent.Revision.SizeBytes is not { } retainedSize || retainedSize < 0 ||
                    NormalizeHash(retainedContent.Revision.ContentHash) is not { Length: 64 } ||
                    retainedContent.ProviderContentReference is not { } relative || Path.IsPathFullyQualified(relative) ||
                    relative.Split(['/', '\\']).Any(part => part is "" or "." or ".."))
                    return Fail(MediaEngineErrorCode.SourceUnavailable, "The retained source has no verified registered folder materialisation.");
                sourcePath = Path.GetFullPath(Path.Combine(rootDirectory, relative));
                var prefix = rootDirectory + Path.DirectorySeparatorChar;
                if (!sourcePath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return Fail(MediaEngineErrorCode.PermissionDenied, "The retained source escapes its canonical folder.");
                for (var current = sourcePath; ; current = Path.GetDirectoryName(current)!)
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new UnauthorizedAccessException("The retained source redirects outside its canonical materialisation.");
                    if (current == rootDirectory) break;
                }
                sourceSize = retainedSize;
                sourceHash = retainedContent.Revision.ContentHash;
            }
            else
            {
                var source = await binding.Materializations.GetByItemIdAsync(fileId, cancellationToken).ConfigureAwait(false);
                if (source is null || source.CurrentRemoteRevisionId != contentRevision || source.LocalRevisionId is not null ||
                    source.State is not (SyncAvailability.AvailableOffline or SyncAvailability.AlwaysAvailable) ||
                    source.SizeBytes != metadata.Value.SizeBytes || NormalizeHash(source.ContentHash) != NormalizeHash(metadata.Value.ContentHash))
                    return Fail(MediaEngineErrorCode.RevisionConflict, "The local materialisation is not the canonical committed Files revision.");
                sourcePath = source.LocalPath;
                sourceSize = source.SizeBytes;
                sourceHash = source.ContentHash;
            }
            leaseDirectory = Path.Combine(rootDirectory, ".9to1-media-leases", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(leaseDirectory);
            for (var current = leaseDirectory; ; current = Path.GetDirectoryName(current)!)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Files lease materialisation redirects to another directory.");
                if (current == rootDirectory) break;
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(leaseDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var extension = Path.GetExtension(sourcePath);
            if (extension.Length > 20 || extension.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.')) extension = ".media";
            var leasedPath = Path.Combine(leaseDirectory, "source" + extension);
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            await using (var output = new FileStream(leasedPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(output, cancellationToken).ConfigureAwait(false));
                if (output.Length != sourceSize || hash != NormalizeHash(sourceHash))
                    throw new InvalidDataException("Media materialisation bytes differ from the canonical Files proof.");
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(leasedPath, UnixFileMode.UserRead);
            if (await authorization.AuthorizeAsync("media.asset.read", [scope], cancellationToken).ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("Files authority changed while materialising the revision.");
            var ownedLeaseDirectory = leaseDirectory;
            var result = MediaEngineResult<MediaAssetReadLease>.Success(new(new(assetID, id, new Uri(leasedPath), revisionText), () =>
            {
                Directory.Delete(ownedLeaseDirectory, true);
                return ValueTask.CompletedTask;
            }));
            leaseDirectory = null;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException exception) { return Fail(MediaEngineErrorCode.PermissionDenied, exception.Message); }
        catch (InvalidDataException exception) { return Fail(MediaEngineErrorCode.SourceUnavailable, exception.Message); }
        catch (IOException exception) { return Fail(MediaEngineErrorCode.SourceUnavailable, exception.Message); }
        finally { if (leaseDirectory is not null && Directory.Exists(leaseDirectory)) Directory.Delete(leaseDirectory, true); }
    }

    private static string? NormalizeHash(string? hash) => string.IsNullOrWhiteSpace(hash) ? null :
        (hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? hash[7..] : hash).ToUpperInvariant();
}
