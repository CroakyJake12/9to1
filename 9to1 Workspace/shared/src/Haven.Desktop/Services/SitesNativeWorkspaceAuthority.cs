using HavenOS.Apps.Sites.Application;

namespace Haven.Desktop.Services;

/// <summary>Projects only the current Home-owned Files binding; the projection itself is not an action grant.</summary>
public sealed class SitesNativeWorkspaceAuthority(NativeFilesWorkspaceAuthority files) : ISiteNativeWorkspaceAuthority
{
    public async Task<SiteNativeWorkspaceBinding?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (workspace is null || !workspace.Configuration.AppFolders.TryGetValue("sites", out var folderId)) return null;
        var metadata = await workspace.Provider.GetAsync(folderId, cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || metadata.Value is null || metadata.Value.Kind != HavenOS.Files.HostedItemKind.Folder || metadata.Value.CurrentRevisionId is null) return null;
        var directory = await files.ResolveAppDirectoryAsync("sites", cancellationToken).ConfigureAwait(false);
        if (directory is null) return null;
        var current = await files.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || current.Actor != workspace.Actor || current.Configuration.StoreId != workspace.Configuration.StoreId ||
            !current.Configuration.AppFolders.TryGetValue("sites", out var currentFolder) || currentFolder != folderId) return null;
        var refreshed = await current.Provider.GetAsync(folderId, cancellationToken).ConfigureAwait(false);
        if (!refreshed.IsSuccess || refreshed.Value != metadata.Value) return null;
        return new(workspace.Actor.ProfileId, workspace.Actor.ActorId, workspace.Actor.AuthenticationRevision,
            folderId.Value, metadata.Value.CurrentRevisionId.Value.ToString(), directory);
    }
}
