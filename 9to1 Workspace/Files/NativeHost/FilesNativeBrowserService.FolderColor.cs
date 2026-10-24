using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    public async Task<FilesFolderPresentation?> ReadNativeFolderPresentationAsync(FilesNativeBrowserPage page,
        HostedItemMetadata folder, AuthenticatedResourceActor actor, Func<bool> lifetime,
        CancellationToken token = default)
    {
        var retained = RequireOriginalFolderSelection(page, folder, actor, lifetime);
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        var ownerCurrent = await CaptureOriginalPageReadCheckAsync(page, actor, lifetime, token).ConfigureAwait(false);
        var scope = new ResourceScope("files.item", folder.Id.ToString(), folder.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
        if (await resources.AuthorizeForActorAsync(actor, "files.browser.read", [scope], token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Home did not authorize reading this folder's Files presentation.");
        var value = await retained.FolderPresentations.GetAsync(folder.Id, token).ConfigureAwait(false);
        if (!lifetime() || !await ownerCurrent(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original folder presentation owner retired during readback.");
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        return value;
    }

    private NativeFilesWorkspace RequireOriginalFolderSelection(FilesNativeBrowserPage page,
        HostedItemMetadata folder, AuthenticatedResourceActor actor, Func<bool> lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (page is null || folder is null || actor is null || !lifetime() || page.IsTrash
            || !_originalPages.TryGetValue(page, out var retained) || retained.Actor != actor
            || retained.Workspace.Configuration.StoreId != page.StoreID || folder.Kind != HostedItemKind.Folder
            || folder.CurrentRevisionId is null || !page.Items.Any(item => ReferenceEquals(item, folder)))
            throw new UnauthorizedAccessException("Select the same current folder from the original Files page.");
        return retained.Workspace;
    }

}
