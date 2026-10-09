using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    // These selectors observe current canonical metadata; they grant no Files
    // read/write, registration, content transport or native navigation authority.
    // The returned page/row come from the SAME maintained authorized page issuer.
    internal async Task<(FilesNativeBrowserPage Page, HostedItemMetadata Row)> ReadOriginalRegisteredDownloadSelectionWithinSourceAsync(
        AuthenticatedResourceActor originalActor, Guid originalStoreId, HostedItemId originalParent,
        HostedItemId originalFile, FilesRevisionId originalRevision, Func<bool> originalLifetime,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalActor); ArgumentNullException.ThrowIfNull(originalLifetime);
        var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
        if (originalStoreId == Guid.Empty || originalParent.Value == Guid.Empty || originalFile.Value == Guid.Empty || originalRevision.Value == Guid.Empty)
            throw new ArgumentException("Retain the observed canonical Files store, parent, file and revision.");
        FilesNativeBrowserCursor? cursor = null;
        var observedOffsets = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            source.Invoke(() =>
            {
                if (!originalLifetime()) throw new UnauthorizedAccessException("The original registered Files observation retired.");
                return true;
            });
            var page = await source.Observe(() => ListOriginalDeveloperAsync(originalActor, source, originalParent.Value,
                cursor: cursor, token: token, expectedStoreId: originalStoreId)).ConfigureAwait(false);
            var row = source.Invoke(() => page.Items.SingleOrDefault(item => item.Id == originalFile));
            if (row is not null)
            {
                if (row.Kind != HostedItemKind.File || row.ParentId != originalParent || row.CurrentRevisionId != originalRevision)
                    throw new InvalidOperationException("The original registered Files item now has another parent, kind or revision.");
                await source.ObserveVoid(() => RevalidateOriginalDeveloperAsync(page, originalActor, source, token)).ConfigureAwait(false);
                source.Invoke(() =>
                {
                    if (!originalLifetime()) throw new UnauthorizedAccessException("The original registered Files observation retired.");
                    return true;
                });
                return (page, row);
            }
            cursor = page.Next;
            if (cursor is null) throw new InvalidOperationException("The exact registered file is absent from its current canonical parent.");
            // Reject a malformed repeated producer cursor; do not reconstruct or
            // increment its offset and do not substitute another page/store window.
            if (!observedOffsets.Add(cursor.Offset)) throw new InvalidDataException("The actual Files page cursor did not advance.");
        }
    }
}
