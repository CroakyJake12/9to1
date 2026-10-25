using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesArtifactResourceResolver
{
    // A board document READ is distinct from a text preview so Home and Files can describe
    // the selected rich document truthfully. It grants no write capability.
    internal async Task<IOriginalCanonicalReadContext> CaptureOriginalBoardDocumentReadAsync(
        NativeFilesWorkspace original, HostedItemMetadata row, Func<bool> lifetime,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        if (_originalAuthority is null || row.Kind != HostedItemKind.File || row.CurrentRevisionId is not { } revision ||
            !FilesNativeBrowserService.IsBoardDocumentCandidate(row))
            throw new UnauthorizedAccessException("Select an original current .9to1board Files item.");
        var current = await source.Observe(() => _originalAuthority.CaptureOriginalReadCheckAsync(
            original, lifetime, source, token).AsTask()).ConfigureAwait(false);
        if (!await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files board-document read source retired.");
        var observed = await source.Observe(() => original.Provider.GetForOriginalStoreAsync(
            original.Configuration.StoreId, row.Id, token, source.OriginalSynchronousScope, source.RetainOriginalTask)).ConfigureAwait(false);
        if (!observed.IsSuccess || observed.Value != row ||
            !await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files board-document revision changed.");
        return new OriginalRead(this, original.Actor,
            new(ResourceKind, row.Id.ToString(), revision.ToString(), ResourceAccess.Read),
            "files.board.read", original.Configuration.StoreId, null, original.Provider, original.Directories, current);
    }
}
