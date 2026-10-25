using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesArtifactResourceResolver
{
    /// <summary>Issues a current board write observation for the exact privately selected item.
    /// This is only a canonical resource observation; Home review and commit fencing remain separate.</summary>
    internal async Task<IOriginalCanonicalWriteContext> CaptureOriginalBoardDocumentWriteAsync(
        NativeFilesWorkspace original, HostedItemMetadata selected, Func<bool> lifetime,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(source);
        if (_originalAuthority is null || selected.Kind != HostedItemKind.File ||
            selected.CurrentRevisionId is not { } revision || !FilesNativeBrowserService.IsBoardDocumentCandidate(selected))
            throw new UnauthorizedAccessException("Select an original current .9to1board Files item.");

        var current = await source.Observe(() => _originalAuthority.CaptureOriginalReadCheckAsync(
            original, lifetime, source, token).AsTask()).ConfigureAwait(false);
        if (!await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files board-document write source retired.");
        var observed = await source.Observe(() => original.Provider.GetForOriginalStoreAsync(
            original.Configuration.StoreId, selected.Id, token, source.OriginalSynchronousScope, source.RetainOriginalTask)).ConfigureAwait(false);
        if (!observed.IsSuccess || observed.Value != selected ||
            !await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files board-document revision changed.");

        var readObservation = new OriginalRead(this, original.Actor,
            new(ResourceKind, selected.Id.ToString(), revision.ToString(), ResourceAccess.Read),
            "files.board.read", original.Configuration.StoreId, null, original.Provider, original.Directories, current);
        return new OriginalWrite(this, readObservation);
    }
}
