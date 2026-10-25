using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesArtifactResourceResolver
{
    /// <summary>Issues a private original folder WRITE observation for the distinct Home
    /// reviewed files.board.create action. This is authorization evidence only, not approval
    /// or permission to create until the broker claim and commit fence are acquired.</summary>
    internal async Task<IOriginalCanonicalWriteContext> CaptureOriginalBoardDocumentCreateAsync(
        NativeFilesWorkspace original, HostedItemMetadata selectedFolder, Func<bool> lifetime,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(selectedFolder);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(source);
        if (_originalAuthority is null || selectedFolder.Kind != HostedItemKind.Folder ||
            selectedFolder.CurrentRevisionId is not { } revision)
            throw new UnauthorizedAccessException("Select an original current Files folder for board creation.");

        var current = await source.Observe(() => _originalAuthority.CaptureOriginalReadCheckAsync(
            original, lifetime, source, token).AsTask()).ConfigureAwait(false);
        if (!await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files board-create source retired.");
        var observed = await source.Observe(() => original.Provider.GetForOriginalStoreAsync(
            original.Configuration.StoreId, selectedFolder.Id, token, source.OriginalSynchronousScope,
            source.RetainOriginalTask)).ConfigureAwait(false);
        if (!observed.IsSuccess || observed.Value != selectedFolder ||
            !await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files board-create folder changed.");

        var folderObservation = new OriginalRead(this, original.Actor,
            new(ResourceKind, selectedFolder.Id.ToString(), revision.ToString(), ResourceAccess.Write),
            "files.board.create", original.Configuration.StoreId, null, original.Provider,
            original.Directories, current);
        return new OriginalWrite(this, folderObservation);
    }
}
