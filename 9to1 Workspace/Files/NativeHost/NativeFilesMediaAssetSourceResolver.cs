using Haven.Application;
using Haven.Core.Media;

namespace HavenOS.Files.NativeHost;

/// <summary>Resolves each operation through the current verified native Files workspace.</summary>
public sealed class NativeFilesMediaAssetSourceResolver(NativeFilesWorkspaceAuthority workspaces,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService authorization) : IMediaRetainedAssetSourceResolver
{
    public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
        string? expectedRevision, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(fileID, assetID, expectedRevision, false, null, null, cancellationToken);

    public Task<MediaEngineResult<MediaAssetReadLease>> ResolveRetainedAsync(string fileID, MediaAssetId assetID,
        string expectedRevision, CancellationToken cancellationToken = default) =>
        ResolveCoreAsync(fileID, assetID, expectedRevision, true, null, null, cancellationToken);

    /// <summary>Reads a retained source for the immutable original host binding. The returned materialization is not
    /// a continuing access grant; owning render/display callbacks must revalidate their original binding.</summary>
    public Task<MediaEngineResult<MediaAssetReadLease>> ResolveRetainedAsync(Guid expectedStoreId,
        AuthenticatedResourceActor originalActor, string fileID, MediaAssetId assetID, string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Select the original Files store UUID.", nameof(expectedStoreId));
        return ResolveCoreAsync(fileID, assetID, expectedRevision, true, expectedStoreId, originalActor, cancellationToken);
    }

    private async Task<MediaEngineResult<MediaAssetReadLease>> ResolveCoreAsync(string fileID, MediaAssetId assetID,
        string? revision, bool retained, Guid? expectedStoreId, AuthenticatedResourceActor? originalActor,
        CancellationToken cancellationToken)
    {
        NativeFilesWorkspace? workspace;
        try
        {
            workspace = expectedStoreId is { } originalStore
                ? await workspaces.GetCurrentAsync(originalStore, cancellationToken).ConfigureAwait(false)
                : await workspaces.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (originalActor is not null && (workspace?.Actor != originalActor ||
                await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != originalActor))
                throw new UnauthorizedAccessException("The original media source session changed.");
        }
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
        var result = retained
            ? await resolver.ResolveRetainedAsync(fileID, assetID, revision!, cancellationToken).ConfigureAwait(false)
            : await resolver.ResolveAsync(fileID, assetID, revision, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || expectedStoreId is not { } capturedStore) return result;
        try
        {
            var current = await workspaces.GetCurrentAsync(capturedStore, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Actor != originalActor || !ReferenceEquals(current.Provider, workspace.Provider) ||
                await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != originalActor)
                throw new UnauthorizedAccessException("The original media source binding changed.");
            await workspace.Provider.GetStoreEvidenceAsync(capturedStore, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or OperationCanceledException)
        {
            await result.Value!.DisposeAsync().ConfigureAwait(false);
            if (error is OperationCanceledException) throw;
            return MediaEngineResult<MediaAssetReadLease>.Failure(new(MediaEngineErrorCode.PermissionDenied,
                "The original media source binding changed. Select it again from Files.", "media.asset.read", fileID, true, true));
        }
    }
}
