using System.Runtime.CompilerServices;
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
public sealed partial class FilesNativeBrowserService(NativeFilesWorkspaceAuthority workspaces,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
    ICompatibilityPackageContentSource packages)
{
    private sealed record OriginalPage(NativeFilesWorkspace Workspace, AuthenticatedResourceActor Actor);
    private readonly ConditionalWeakTable<FilesNativeBrowserPage, OriginalPage> _originalPages = new();
    private readonly FilesOriginalChildFolderReadSource? _originalFolders;
    private ResourceAuthorizationService OriginalMailResources => resources;

    /// <summary>Identity observation only; canonical read methods still authorize every operation.</summary>
    public bool IsBoundToOriginalComposition(NativeFilesWorkspaceAuthority expectedWorkspaces,
        IAuthenticatedResourceActorSource expectedActors, ResourceAuthorizationService expectedResources,
        ICompatibilityPackageContentSource expectedPackages)
        => ReferenceEquals(workspaces, expectedWorkspaces) && ReferenceEquals(actors, expectedActors)
            && ReferenceEquals(resources, expectedResources) && ReferenceEquals(packages, expectedPackages);


    public FilesNativeBrowserService(NativeFilesWorkspaceAuthority workspaces, IAuthenticatedResourceActorSource actors,
        ResourceAuthorizationService resources, ICompatibilityPackageContentSource packages,
        FilesOriginalChildFolderReadSource originalFolders) : this(workspaces, actors, resources, packages)
    { _originalFolders = originalFolders ?? throw new ArgumentNullException(nameof(originalFolders)); }

    public bool HasOriginalStacksSelection(FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
        AuthenticatedResourceActor originalActor)
        => _originalFolders is not null && _originalPages.TryGetValue(originalPage, out var retained) &&
            retained.Actor == originalActor && retained.Workspace.Configuration.StoreId == originalPage.StoreID &&
            retained.Workspace.Configuration.AppFolders.TryGetValue("stacks", out var root) && root.Value != Guid.Empty &&
            originalRow.Kind == HostedItemKind.Folder && originalRow.Id != root && originalRow.CurrentRevisionId is not null &&
            originalPage.Items.Any(row => ReferenceEquals(row, originalRow));

    public async Task<FilesNativeFolderReadLease> ReadOriginalStacksSelectionAsync(FilesNativeBrowserPage originalPage,
        HostedItemMetadata originalRow, AuthenticatedResourceActor originalActor, Func<bool> originalLifetime,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalLifetime);
        if (!HasOriginalStacksSelection(originalPage, originalRow, originalActor) || !originalLifetime() ||
            !_originalPages.TryGetValue(originalPage, out var retained))
            throw new UnauthorizedAccessException("Select an original privately issued registered Stacks child.");
        bool Current() => originalLifetime() && HasOriginalStacksSelection(originalPage, originalRow, originalActor);
        var lease = await _originalFolders!.ReadConfiguredChildAsync(retained.Workspace, "stacks", originalRow.Id.Value,
            originalRow.CurrentRevisionId!.Value.ToString(), Current, token).ConfigureAwait(false);
        try
        {
            await lease.RevalidateAsync(token).ConfigureAwait(false);
            var evidence = await retained.Workspace.Provider.GetStoreEvidenceAsync(originalPage.StoreID, token).ConfigureAwait(false);
            await lease.RevalidateAsync(token).ConfigureAwait(false);
            if (!Current() || evidence.StoreId != originalPage.StoreID || evidence.Revision != originalPage.StoreRevision)
                throw new UnauthorizedAccessException("The original selected Stacks page retired or changed.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

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
        var issued = new FilesNativeBrowserPage(before.StoreId, before.Revision, parentID, Array.AsReadOnly(page.Items.ToArray()),
            page.NextPageToken is { } next ? new(before.StoreId, before.Revision, parentID, search, next, originalActor) : null);
        _originalPages.Add(issued, new(workspace, originalActor));
        return issued;
    }

    /// <summary>Retain the SAME privately issued page's Home/configuration observation.
    /// This callback grants no access and never resolves a replacement Files workspace.</summary>
    public async ValueTask<Func<CancellationToken, ValueTask<bool>>> CaptureOriginalPageReadCheckAsync(
        FilesNativeBrowserPage originalPage, AuthenticatedResourceActor originalActor,
        Func<bool> originalLifetime, CancellationToken token)
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
                return originalLifetime() && _originalPages.TryGetValue(originalPage, out var current) &&
                    ReferenceEquals(current, retained) && current.Actor == originalActor &&
                    current.Workspace.Configuration.StoreId == originalPage.StoreID;
            }
            catch { return false; }
        }
        if (!Paired()) throw new UnauthorizedAccessException("The original Files page retired.");
        var check = await workspaces.CaptureOriginalReadCheckAsync(retained.Workspace, Paired, token)
            .ConfigureAwait(false);
        if (!Paired() || !await check(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files Home binding retired.");
        return async currentToken => Paired() && await check(currentToken).ConfigureAwait(false) && Paired();
    }

    /// <summary>Resolve the canonical parent of the original current folder; names never determine identity.</summary>
    public async Task<(Guid? ID, string Title)> GetParentAsync(FilesNativeBrowserPage originalPage,
        AuthenticatedResourceActor originalActor, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalPage);
        if (originalPage.ParentID is not { } currentFolderID)
            throw new InvalidOperationException("The Files root has no parent folder.");
        await RevalidateAsync(originalPage, originalActor, token).ConfigureAwait(false);
        var workspace = await RequireWorkspaceAsync(originalActor, originalPage.StoreID, token).ConfigureAwait(false);
        var current = await workspace.Provider.GetAsync(new(currentFolderID), token).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value!.Kind != HostedItemKind.Folder)
            throw new InvalidOperationException("The original canonical folder is unavailable.");
        await RequireMetadataAsync(current.Value, originalActor, originalPage.StoreID, token).ConfigureAwait(false);
        (Guid? ID, string Title) destination = (null, "Files");
        if (current.Value.ParentId is { } parentID)
        {
            var parent = await workspace.Provider.GetAsync(parentID, token).ConfigureAwait(false);
            if (!parent.IsSuccess || parent.Value!.Kind != HostedItemKind.Folder)
                throw new InvalidOperationException("The canonical parent folder is unavailable.");
            await RequireMetadataAsync(parent.Value, originalActor, originalPage.StoreID, token).ConfigureAwait(false);
            destination = (parent.Value.Id.Value, parent.Value.Name);
        }
        await RevalidateAsync(originalPage, originalActor, token).ConfigureAwait(false);
        if (!ReferenceEquals((await RequireWorkspaceAsync(originalActor, originalPage.StoreID, token).ConfigureAwait(false)).Provider, workspace.Provider))
            throw new UnauthorizedAccessException("The Files provider changed during parent navigation.");
        return destination;
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
