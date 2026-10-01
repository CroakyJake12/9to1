using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed record FilesNativeBrowserCursor(Guid StoreID, string StoreRevision, Guid? ParentID,
    string Search, string Offset, AuthenticatedResourceActor OriginalActor);

public sealed record FilesNativeBrowserPage(Guid StoreID, string StoreRevision, Guid? ParentID,
    IReadOnlyList<HostedItemMetadata> Items, FilesNativeBrowserCursor? Next);

/// <summary>Canonical metadata navigation bound to the actor captured at the original host click.
/// Cursor revisions pin the real Files state, rather than inferring stability from names or timestamps.</summary>
public sealed class FilesNativeBrowserService(NativeFilesWorkspaceAuthority workspaces,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
    ICompatibilityPackageContentSource packages)
{
    public async Task<FilesNativeBrowserPage> ListAsync(AuthenticatedResourceActor originalActor,
        Guid? parentID = null, string search = "", FilesNativeBrowserCursor? cursor = null,
        CancellationToken token = default, Guid? expectedStoreId = null)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        expectedStoreId ??= cursor?.StoreID;
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Retain the original Files store UUID.");
        if (search.Length > 256 || parentID == Guid.Empty)
            throw new ArgumentException("Select a canonical folder and a bounded search.");
        var workspace = await RequireWorkspaceAsync(originalActor, expectedStoreId, token).ConfigureAwait(false);
        var before = await workspace.Provider.GetStoreEvidenceAsync(workspace.Configuration.StoreId, token).ConfigureAwait(false);
        if (cursor is not null && (cursor.OriginalActor != originalActor || cursor.StoreID != before.StoreId ||
            cursor.StoreRevision != before.Revision || cursor.ParentID != parentID || cursor.Search != search))
            throw new InvalidOperationException("This Files page changed. Refresh before continuing.");
        if (parentID is { } parent)
        {
            var result = await workspace.Provider.GetAsync(new(parent), token).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value!.Kind != HostedItemKind.Folder)
                throw new InvalidOperationException("The canonical folder is unavailable.");
            await RequireMetadataAsync(result.Value, originalActor, workspace.Configuration.StoreId, token).ConfigureAwait(false);
        }
        var page = await workspace.Provider.ListAsync(parentID is { } id ? new HostedItemId(id) : null,
            new FilesSearchQuery(search, Limit: 100), cursor?.Offset, token).ConfigureAwait(false);
        foreach (var item in page.Items)
            await RequireMetadataAsync(item, originalActor, workspace.Configuration.StoreId, token).ConfigureAwait(false);
        var after = await workspace.Provider.GetStoreEvidenceAsync(workspace.Configuration.StoreId, token).ConfigureAwait(false);
        if (before.StoreId != workspace.Configuration.StoreId || after != before ||
            !ReferenceEquals((await RequireWorkspaceAsync(originalActor, expectedStoreId, token).ConfigureAwait(false)).Provider, workspace.Provider))
            throw new InvalidOperationException("Files changed during navigation. Refresh this folder.");
        return new(before.StoreId, before.Revision, parentID, Array.AsReadOnly(page.Items.ToArray()),
            page.NextPageToken is { } next ? new(before.StoreId, before.Revision, parentID, search, next, originalActor) : null);
    }

    public async Task RevalidateAsync(FilesNativeBrowserPage page, AuthenticatedResourceActor originalActor,
        CancellationToken token)
    {
        var workspace = await RequireWorkspaceAsync(originalActor, page.StoreID, token).ConfigureAwait(false);
        var evidence = await workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token).ConfigureAwait(false);
        if (page.StoreID != evidence.StoreId || page.StoreRevision != evidence.Revision)
            throw new InvalidOperationException("The displayed Files folder changed.");
        foreach (var item in page.Items)
            await RequireMetadataAsync(item, originalActor, page.StoreID, token).ConfigureAwait(false);
        if (await workspace.Provider.GetStoreEvidenceAsync(page.StoreID, token).ConfigureAwait(false) != evidence)
            throw new InvalidOperationException("Files changed during display revalidation.");
        await RequireWorkspaceAsync(originalActor, page.StoreID, token).ConfigureAwait(false);
    }

    public async Task<CompatibilityPackageSource> ReadPackageSelectionAsync(FilesNativeBrowserPage originalPage, HostedItemMetadata displayed,
        AuthenticatedResourceActor originalActor, long maximumBytes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalPage);
        if (!originalPage.Items.Contains(displayed))
            throw new InvalidOperationException("Select an item from the original displayed Files page.");
        await RevalidateAsync(originalPage, originalActor, token).ConfigureAwait(false);
        var workspace = await RequireWorkspaceAsync(originalActor, originalPage.StoreID, token).ConfigureAwait(false);
        if (workspace.Configuration.StoreId != originalPage.StoreID)
            throw new UnauthorizedAccessException("The original Files store changed.");
        await RequireMetadataAsync(displayed, originalActor, originalPage.StoreID, token).ConfigureAwait(false);
        if (displayed.Kind != HostedItemKind.File)
            throw new InvalidOperationException("Select a canonical package file.");
        var content = await workspace.Provider.GetCurrentArtifactContentAsync(displayed.Id, token).ConfigureAwait(false);
        if (!content.IsSuccess) throw new InvalidOperationException("The selected file has no immutable content.");
        await using var lease = await packages.ReadAsync(originalPage.StoreID, originalActor, displayed.Id.Value,
            content.Value!.Revision.Id.ToString(), maximumBytes, token).ConfigureAwait(false);
        if (lease.Source.StoreId != originalPage.StoreID || lease.Source.ObservedActor != originalActor || lease.Source.Name != displayed.Name ||
            lease.Source.MetadataRevision != displayed.CurrentRevisionId?.ToString())
            throw new InvalidOperationException("The original selected file changed.");
        await lease.RevalidateAsync(token).ConfigureAwait(false);
        await RequireWorkspaceAsync(originalActor, originalPage.StoreID, token).ConfigureAwait(false);
        await RevalidateAsync(originalPage, originalActor, token).ConfigureAwait(false);
        if (!ReferenceEquals((await RequireWorkspaceAsync(originalActor, originalPage.StoreID, token).ConfigureAwait(false)).Provider, workspace.Provider))
            throw new UnauthorizedAccessException("The original Files provider changed.");
        return lease.Source;
    }

    private async Task<NativeFilesWorkspace> RequireWorkspaceAsync(AuthenticatedResourceActor originalActor, Guid? expectedStoreId,
        CancellationToken token)
    {
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The original Files session changed.");
        var workspace = expectedStoreId is { } originalStore
            ? await workspaces.GetCurrentAsync(originalStore, token).ConfigureAwait(false)
            : await workspaces.GetCurrentAsync(token).ConfigureAwait(false);
        if (workspace?.Actor != originalActor || await actors.GetCurrentAsync(token).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The original session has no verified Files binding. Set up Files in Home.");
        return workspace;
    }

    private async Task RequireMetadataAsync(HostedItemMetadata item, AuthenticatedResourceActor originalActor,
        Guid expectedStoreId, CancellationToken token)
    {
        if (await resources.AuthorizeAsync("files.browser.read", [new("files.item", item.Id.ToString(),
            item.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read)], token).ConfigureAwait(false) != originalActor)
            throw new UnauthorizedAccessException("The original session cannot read this canonical Files item.");
        var workspace = await RequireWorkspaceAsync(originalActor, expectedStoreId, token).ConfigureAwait(false);
        var current = await workspace.Provider.GetAsync(item.Id, token).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value != item)
            throw new InvalidOperationException("The displayed canonical Files item changed.");
    }
}
