using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesArtifactResourceResolver
{
    /// <summary>Mail attachment bytes require this separate privately issued original raw-file observation.
    /// The Canvas display and write observations do not authorize this action.</summary>
    public ValueTask<IOriginalCanonicalReadContext> CaptureOriginalMailAttachmentReadAsync(
        NativeFilesWorkspace original, HostedItemId file, Func<bool> originalLifetime,
        CancellationToken cancellationToken = default)
        => CaptureOriginalMailAttachmentReadCoreAsync(original, file, null, originalLifetime, cancellationToken);

    public ValueTask<IOriginalCanonicalReadContext> CaptureOriginalMailAttachmentReadForRevisionAsync(
        NativeFilesWorkspace original, HostedItemId file, FilesRevisionId originalRevision, Func<bool> originalLifetime,
        CancellationToken cancellationToken = default)
        => CaptureOriginalMailAttachmentReadCoreAsync(original, file, originalRevision, originalLifetime, cancellationToken);

    private async ValueTask<IOriginalCanonicalReadContext> CaptureOriginalMailAttachmentReadCoreAsync(
        NativeFilesWorkspace original, HostedItemId file, FilesRevisionId? originalRevision,
        Func<bool> originalLifetime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(originalLifetime);
        if (_originalAuthority is null || file.Value == Guid.Empty)
            throw new UnauthorizedAccessException("The registered original Files attachment port is unavailable.");
        var current = await _originalAuthority.CaptureOriginalReadCheckAsync(original, originalLifetime, cancellationToken).ConfigureAwait(false);
        if (!await current(cancellationToken).ConfigureAwait(false)) throw new UnauthorizedAccessException("The original Files selection retired.");
        var item = originalRevision is { } retained
            ? await original.Provider.GetForOriginalStoreAtRevisionAsync(original.Configuration.StoreId, file, retained, cancellationToken).ConfigureAwait(false)
            : await original.Provider.GetForOriginalStoreAsync(original.Configuration.StoreId, file, cancellationToken).ConfigureAwait(false);
        if (!await current(cancellationToken).ConfigureAwait(false) || !item.IsSuccess ||
            item.Value is not { Kind: HostedItemKind.File, CurrentRevisionId: { } revision })
            throw new UnauthorizedAccessException("Select an available committed original Files attachment.");
        return new OriginalRead(this, original.Actor,
            new ResourceScope(ResourceKind, file.ToString(), revision.ToString(), ResourceAccess.Read),
            "mail.attachment.read", original.Configuration.StoreId, null, original.Provider, original.Directories, current);
    }
}
