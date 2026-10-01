using System.Text.Json.Serialization;
using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed record FilesNativeFolderReference(Guid FolderID, string FolderRevision, Guid StoreID,
    string OwningAppID, string Name, AuthenticatedResourceActor ObservedActor);

/// <summary>A native-process-only read/revalidation lease. Its path grants no mutation or execution authority.</summary>
public sealed class FilesNativeFolderReadLease : IDisposable
{
    private readonly Func<CancellationToken, Task> _revalidate;
    private readonly string _directory;
    private volatile bool _disposed;
    internal FilesNativeFolderReadLease(FilesNativeFolderReference source, string directory,
        Func<CancellationToken, Task> revalidate)
    { Source = source; _directory = directory; _revalidate = revalidate; }
    public FilesNativeFolderReference Source { get; }
    [JsonIgnore] public string DirectoryPath
    { get { ObjectDisposedException.ThrowIf(_disposed, this); return _directory; } }
    public async Task RevalidateAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _revalidate(token).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose() => _disposed = true;
}

/// <summary>Reads an already registered canonical app folder under the original authenticated actor.
/// It never accepts a path, registers a folder, creates a private root, or approves an app action.</summary>
public sealed class FilesNativeFolderReadSource(NativeFilesWorkspaceAuthority workspaces,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources)
{
    private const string Action = "files.folder.native-root.read";
    public async Task<FilesNativeFolderReadLease> ReadAsync(string owningAppID, Guid folderID,
        string expectedFolderRevision, AuthenticatedResourceActor expectedActor, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (owningAppID is not ("stacks" or "sites") || folderID == Guid.Empty ||
            !Guid.TryParse(expectedFolderRevision, out var revision) || revision == Guid.Empty)
            throw new ArgumentException("Select an existing registered canonical application folder.");
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != expectedActor)
            throw new UnauthorizedAccessException("The original Files profile changed.");
        var workspace = await workspaces.GetCurrentAsync(token).ConfigureAwait(false);
        if (workspace?.Actor != expectedActor || !Guid.TryParse(expectedActor.ProfileId, out var profile))
            throw new UnauthorizedAccessException("The original profile has no verified native Files workspace.");
        var folder = (await workspace.Provider.GetAsync(new(folderID), token).ConfigureAwait(false)).Value;
        if (folder is not { Kind: HostedItemKind.Folder, CurrentRevisionId: { } current } || current.Value != revision)
            throw new InvalidOperationException("The displayed canonical folder changed or is unavailable.");
        var scope = new ResourceScope("files.item", folderID.ToString("D"), expectedFolderRevision, ResourceAccess.Read);
        if (await resources.AuthorizeAsync(Action, [scope], token).ConfigureAwait(false) != expectedActor)
            throw new UnauthorizedAccessException("The original actor cannot read this canonical folder.");
        var registered = await workspace.Directories.ResolveProfileAsync(profile, owningAppID, token).ConfigureAwait(false);
        if (!registered.IsSuccess || registered.Value!.FolderId.Value != folderID ||
            registered.Value.OwningAppId != owningAppID || registered.Value.LocationId != folder.LocationId)
            throw new UnauthorizedAccessException("This app has no trusted registered materialisation for the selected folder.");
        var binding = registered.Value;
        await RequireCurrentAsync(token).ConfigureAwait(false);
        return new(new(folderID, expectedFolderRevision, workspace.Configuration.StoreId, owningAppID,
            folder.Name, expectedActor), binding.DirectoryPath, RequireCurrentAsync);

        async Task RequireCurrentAsync(CancellationToken cancellationToken)
        {
            var currentWorkspace = await workspaces.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (currentWorkspace?.Actor != expectedActor || !ReferenceEquals(currentWorkspace.Provider, workspace.Provider) ||
                currentWorkspace.Configuration.StoreId != workspace.Configuration.StoreId ||
                await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor ||
                await resources.AuthorizeAsync(Action, [scope], cancellationToken).ConfigureAwait(false) != expectedActor)
                throw new UnauthorizedAccessException("Home no longer permits this original native folder source.");
            var currentFolder = await currentWorkspace.Provider.GetAsync(new(folderID), cancellationToken).ConfigureAwait(false);
            var currentBinding = await currentWorkspace.Directories.ResolveProfileAsync(profile, owningAppID, cancellationToken).ConfigureAwait(false);
            if (!currentFolder.IsSuccess || currentFolder.Value != folder || !currentBinding.IsSuccess || currentBinding.Value != binding)
                throw new InvalidOperationException("The canonical folder or registered materialisation changed.");
            if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor ||
                await resources.AuthorizeAsync(Action, [scope], cancellationToken).ConfigureAwait(false) != expectedActor)
                throw new UnauthorizedAccessException("Folder source access changed during revalidation.");
        }
    }
}
