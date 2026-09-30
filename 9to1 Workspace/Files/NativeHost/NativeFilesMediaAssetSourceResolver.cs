using Haven.Application;
using Haven.Core.Media;

namespace HavenOS.Files.NativeHost;

/// <summary>Resolves each operation through the current verified native Files workspace.</summary>
public sealed class NativeFilesMediaAssetSourceResolver(NativeFilesWorkspaceAuthority workspaces,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService authorization) : IMediaAssetSourceResolver
{
    public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
        string? expectedRevision, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(fileID, assetID, expectedRevision, false, cancellationToken);

    public Task<MediaEngineResult<MediaAssetReadLease>> ResolveRetainedAsync(string fileID, MediaAssetId assetID,
        string expectedRevision, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(fileID, assetID, expectedRevision, true, cancellationToken);

    private async Task<MediaEngineResult<MediaAssetReadLease>> ResolveCoreAsync(string fileID, MediaAssetId assetID,
        string? revision, bool retained, CancellationToken cancellationToken)
    {
        NativeFilesWorkspace? workspace;
        try { workspace = await workspaces.GetCurrentAsync(cancellationToken).ConfigureAwait(false); }
        catch (UnauthorizedAccessException)
        {
            return MediaEngineResult<MediaAssetReadLease>.Failure(new(MediaEngineErrorCode.PermissionDenied,
                "Home does not currently authorise this Files workspace.", "media.asset.read", fileID, true, true));
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return MediaEngineResult<MediaAssetReadLease>.Failure(new(MediaEngineErrorCode.SourceUnavailable,
                "The Files workspace requires recovery in Home. Existing data was preserved.", "media.asset.read", fileID, true, true));
        }
        if (workspace is null)
            return MediaEngineResult<MediaAssetReadLease>.Failure(new(MediaEngineErrorCode.PermissionDenied,
                "Configure Files and verify its ownership in Home before opening media.", "media.asset.read", fileID, true, true));
        var resolver = new FilesMediaAssetSourceResolver(actors,
            actor => actor == workspace.Actor ? new(workspace.Provider, workspace.Materializations) : null,
            workspace.Directories, authorization);
        return retained
            ? await resolver.ResolveRetainedAsync(fileID, assetID, revision!, cancellationToken).ConfigureAwait(false)
            : await resolver.ResolveAsync(fileID, assetID, revision, cancellationToken).ConfigureAwait(false);
    }
}
