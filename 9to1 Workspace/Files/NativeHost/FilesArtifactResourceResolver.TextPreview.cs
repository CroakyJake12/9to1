using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesArtifactResourceResolver
{
    // Separate original READ observation. Folder, package, Mail and Canvas observations
    // cannot be replayed as text preview, and this context cannot derive a write.
    internal async Task<IOriginalCanonicalReadContext> CaptureOriginalTextPreviewReadAsync(
        NativeFilesWorkspace original, HostedItemMetadata row, Func<bool> lifetime,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        if (_originalAuthority is null || row.Kind != HostedItemKind.File || row.CurrentRevisionId is not { } revision)
            throw new UnauthorizedAccessException("The original Files preview owner is unavailable.");
        var current = await source.Observe(() => _originalAuthority.CaptureOriginalReadCheckAsync(
            original, lifetime, source, token).AsTask()).ConfigureAwait(false);
        if (!await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files preview source retired.");
        var observed = await source.Observe(() => original.Provider.GetForOriginalStoreAsync(
            original.Configuration.StoreId, row.Id, token, source.OriginalSynchronousScope, source.RetainOriginalTask)).ConfigureAwait(false);
        if (!observed.IsSuccess || observed.Value != row ||
            !await source.Observe(() => current(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files preview row changed.");
        return new OriginalRead(this, original.Actor, new(ResourceKind, row.Id.ToString(), revision.ToString(), ResourceAccess.Read),
            "files.preview.read", original.Configuration.StoreId, null, original.Provider, original.Directories, current);
    }
}
