using Haven.Application;
using HavenOS.Mail.Providers;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    // Only a page/row privately issued by this exact browser retains its original workspace.
    // Mail routing IDs are not Files authority and never infer a message AttachmentID or Send grant.
    public async Task<MailAttachmentContent> ReadOriginalMailAttachmentSelectionAsync(
        FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
        AuthenticatedResourceActor originalActor, FilesArtifactResourceResolver registeredOwner,
        Guid originalAccountId, Guid originalDraftId, Func<bool> originalLifetime,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalLifetime);
        if (!_originalPages.TryGetValue(originalPage, out var retained) || retained.Actor != originalActor ||
            retained.Workspace.Configuration.StoreId != originalPage.StoreID ||
            originalRow.Kind != HostedItemKind.File || originalRow.CurrentRevisionId is null ||
            !originalPage.Items.Any(row => ReferenceEquals(row, originalRow)) || !originalLifetime())
            throw new UnauthorizedAccessException("Select an original canonical Files attachment.");
        bool Current() => originalLifetime() && _originalPages.TryGetValue(originalPage, out var same) &&
            ReferenceEquals(same, retained) && originalPage.Items.Any(row => ReferenceEquals(row, originalRow));
        using var source = await FilesMailAttachmentContentSource.CaptureDisplayedSelectionAsync(retained.Workspace,
            registeredOwner, OriginalMailResources, originalAccountId, originalDraftId, originalRow.Id, originalRow.CurrentRevisionId.Value, Current, token).ConfigureAwait(false);
        if (!Current()) throw new UnauthorizedAccessException("The original attachment selection retired.");
        var result = await source.ReadAsync(originalAccountId, originalDraftId, originalRow.Id.Value, token).ConfigureAwait(false);
        if (!Current()) throw new UnauthorizedAccessException("The original attachment selection retired.");
        return result;
    }
}
