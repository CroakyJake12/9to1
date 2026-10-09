using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    private FilesOriginalReadSourceScope DeveloperIdentityReadSources(DeveloperIdentityOriginal owner) => new(
        actual => AcquireDeveloperIdentitySource(() => { actual(); return true; }),
        actual => { lock (_developerIdentityGate) if (!owner.Sources.Any(value => ReferenceEquals(value, actual))) owner.Sources.Add(actual); });
    private async Task<NativeFilesWorkspace> RequireOriginalDeveloperWorkspaceAsync(AuthenticatedResourceActor originalActor, Guid? expectedStoreId,
        FilesOriginalReadSourceScope original, CancellationToken token)
    {
        if (await original.Observe(() => actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The original Files session changed.");
        var workspace = await original.Observe(() => workspaces.GetOriginalCurrentAsync(expectedStoreId, original, token)).ConfigureAwait(false);
        if (workspace?.Actor != originalActor || await original.Observe(() => actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The original session has no verified Files binding. Set up Files in Home.");
        return workspace;
    }

    private async Task RequireOriginalDeveloperMetadataAsync(HostedItemMetadata item, AuthenticatedResourceActor originalActor,
        Guid expectedStoreId, FilesOriginalReadSourceScope original, CancellationToken token)
    {
        if (await original.Observe(() => resources.AuthorizeAsync("files.browser.read", [new("files.item", item.Id.ToString(),
            item.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read)], token).AsTask()).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The original session cannot read this canonical Files item.");
        var workspace = await original.Observe(() => RequireOriginalDeveloperWorkspaceAsync(originalActor, expectedStoreId, original, token)).ConfigureAwait(false);
        var current = await original.Observe(() => workspace.Provider.GetAsync(item.Id, token)).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value != item)
            throw new InvalidOperationException("The displayed canonical Files item changed.");
    }
    private async Task RevalidateOriginalDeveloperAsync(FilesNativeBrowserPage page, AuthenticatedResourceActor originalActor,
        FilesOriginalReadSourceScope original, CancellationToken token)
    {
        var workspace = await original.Observe(() => RequireOriginalDeveloperWorkspaceAsync(originalActor, page.StoreID, original, token)).ConfigureAwait(false);
        var evidence = await original.Observe(() => workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token)).ConfigureAwait(false);
        if (page.StoreID != evidence.StoreId || page.StoreRevision != evidence.Revision)
            throw new InvalidOperationException("The displayed Files folder changed.");
        foreach (var item in page.Items)
            await original.ObserveVoid(() => RequireOriginalDeveloperMetadataAsync(item, originalActor, page.StoreID, original, token)).ConfigureAwait(false);
        if (await original.Observe(() => workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token)).ConfigureAwait(false) != evidence)
            throw new InvalidOperationException("Files changed during display revalidation.");
        await original.Observe(() => RequireOriginalDeveloperWorkspaceAsync(originalActor, page.StoreID, original, token)).ConfigureAwait(false);
    }


    private async Task<FilesNativeBrowserPage> ListOriginalDeveloperAsync(AuthenticatedResourceActor originalActor, FilesOriginalReadSourceScope original,
        Guid? parentID = null, string search = "", FilesNativeBrowserCursor? cursor = null,
        CancellationToken token = default, Guid? expectedStoreId = null)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        expectedStoreId ??= cursor?.StoreID;
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Retain the original Files store UUID.");
        if (search.Length > 256 || parentID == Guid.Empty)
            throw new ArgumentException("Select a canonical folder and a bounded search.");
        var workspace = await original.Observe(() => RequireOriginalDeveloperWorkspaceAsync(originalActor, expectedStoreId, original, token)).ConfigureAwait(false);
        var before = await original.Observe(() => workspace.Provider.GetStoreEvidenceAsync(workspace.Configuration.StoreId, token)).ConfigureAwait(false);
        if (cursor is not null && (cursor.OriginalActor != originalActor || cursor.StoreID != before.StoreId ||
            cursor.StoreRevision != before.Revision || cursor.ParentID != parentID || cursor.Search != search))
            throw new InvalidOperationException("This Files page changed. Refresh before continuing.");
        if (parentID is { } parent)
        {
            var result = await original.Observe(() => workspace.Provider.GetAsync(new(parent), token)).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value!.Kind != HostedItemKind.Folder)
                throw new InvalidOperationException("The canonical folder is unavailable.");
            await original.ObserveVoid(() => RequireOriginalDeveloperMetadataAsync(result.Value, originalActor, workspace.Configuration.StoreId, original, token)).ConfigureAwait(false);
        }
        var page = await original.Observe(() => workspace.Provider.ListAsync(parentID is { } id ? new HostedItemId(id) : null,
            new FilesSearchQuery(search, Limit: 100), cursor?.Offset, token)).ConfigureAwait(false);
        foreach (var item in page.Items)
            await original.ObserveVoid(() => RequireOriginalDeveloperMetadataAsync(item, originalActor, workspace.Configuration.StoreId, original, token)).ConfigureAwait(false);
        var after = await original.Observe(() => workspace.Provider.GetStoreEvidenceAsync(workspace.Configuration.StoreId, token)).ConfigureAwait(false);
        if (before.StoreId != workspace.Configuration.StoreId || after != before ||
            !ReferenceEquals((await original.Observe(() => RequireOriginalDeveloperWorkspaceAsync(originalActor, expectedStoreId, original, token)).ConfigureAwait(false)).Provider, workspace.Provider))
            throw new InvalidOperationException("Files changed during navigation. Refresh this folder.");
        var issued = new FilesNativeBrowserPage(before.StoreId, before.Revision, parentID, Array.AsReadOnly(page.Items.ToArray()),
            page.NextPageToken is { } next ? new(before.StoreId, before.Revision, parentID, search, next, originalActor) : null);
        original.Invoke(() => { _originalPages.Add(issued, new(workspace, originalActor)); return true; });
        return issued;
    }


    private async ValueTask<Func<CancellationToken, ValueTask<bool>>> CaptureOriginalDeveloperPageReadCheckAsync(
        FilesNativeBrowserPage originalPage, AuthenticatedResourceActor originalActor,
        Func<bool> originalLifetime, FilesOriginalReadSourceScope original, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalPage);
        ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(originalLifetime);
        if (!_originalPages.TryGetValue(originalPage, out var retained) ||
            retained.Actor != originalActor ||
            retained.Workspace.Configuration.StoreId != originalPage.StoreID)
            throw new UnauthorizedAccessException("Retain the SAME privately issued original Files page.");
        bool Paired()
        {
            try
            {
                return original.Invoke(originalLifetime) && _originalPages.TryGetValue(originalPage, out var current) &&
                    ReferenceEquals(current, retained) && current.Actor == originalActor &&
                    current.Workspace.Configuration.StoreId == originalPage.StoreID;
            }
            catch { return false; }
        }
        if (!Paired()) throw new UnauthorizedAccessException("The original Files page retired.");
        var check = await original.Observe(() => workspaces.CaptureOriginalReadCheckAsync(retained.Workspace, Paired, original, token).AsTask()).ConfigureAwait(false);
        if (!Paired() || !await original.Observe(() => check(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files Home binding retired.");
        return async currentToken => Paired() && await original.Observe(() => check(currentToken).AsTask()).ConfigureAwait(false) && Paired();
    }

}
