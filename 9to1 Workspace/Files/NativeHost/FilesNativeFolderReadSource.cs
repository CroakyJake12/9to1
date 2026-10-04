using System.Text.Json.Serialization;
using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed record FilesNativeFolderReference(Guid FolderID, string FolderRevision, Guid StoreID,
    string OwningAppID, string Name, AuthenticatedResourceActor ObservedActor)
{
    public Guid? RootFolderID { get; init; }
    public string? RootFolderRevision { get; init; }
}

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
    public Task<FilesNativeFolderReadLease> ReadAsync(string owningAppID, Guid folderID,
        string expectedFolderRevision, AuthenticatedResourceActor expectedActor, CancellationToken token) =>
        ReadRootCoreAsync(owningAppID, null, folderID, expectedFolderRevision, expectedActor, token);

    public Task<FilesNativeFolderReadLease> ReadAsync(string owningAppID, Guid expectedStoreId, Guid folderID,
        string expectedFolderRevision, AuthenticatedResourceActor expectedActor, CancellationToken token)
    {
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Retain the original Files store UUID.", nameof(expectedStoreId));
        return ReadRootCoreAsync(owningAppID, expectedStoreId, folderID, expectedFolderRevision, expectedActor, token);
    }

    private async Task<FilesNativeFolderReadLease> ReadRootCoreAsync(string owningAppID, Guid? expectedStoreId,
        Guid folderID, string expectedFolderRevision, AuthenticatedResourceActor expectedActor, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (owningAppID is not ("stacks" or "sites") || folderID == Guid.Empty ||
            !Guid.TryParse(expectedFolderRevision, out var revision) || revision == Guid.Empty)
            throw new ArgumentException("Select an existing registered canonical application folder.");
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != expectedActor)
            throw new UnauthorizedAccessException("The original Files profile changed.");
        var workspace = expectedStoreId is { } originalStore
            ? await workspaces.GetCurrentAsync(originalStore, token).ConfigureAwait(false)
            : await workspaces.GetCurrentAsync(token).ConfigureAwait(false);
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
            var currentWorkspace = await workspaces.GetCurrentAsync(workspace.Configuration.StoreId, cancellationToken).ConfigureAwait(false);
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
    /// <summary>Reads a preexisting project folder through canonical ancestry and an explicit trusted Files mapping.
    /// It never constructs a physical child path, registers a mapping, creates a project, or authorizes writes.</summary>
    public async Task<FilesNativeFolderReadLease> ReadChildAsync(string owningAppID, Guid expectedStoreId,
        Guid rootFolderID, string expectedRootRevision, Guid folderID, string expectedFolderRevision,
        AuthenticatedResourceActor expectedActor, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (expectedStoreId == Guid.Empty || folderID == Guid.Empty || folderID == rootFolderID ||
            !Guid.TryParse(expectedFolderRevision, out var revision) || revision == Guid.Empty)
            throw new ArgumentException("Retain the original store and a canonical preexisting child folder.");
        using var root = await ReadAsync(owningAppID, expectedStoreId, rootFolderID, expectedRootRevision, expectedActor, token).ConfigureAwait(false);
        var workspace = await workspaces.GetCurrentAsync(expectedStoreId, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Files store is unavailable.");
        if (workspace.Actor != expectedActor || !Guid.TryParse(expectedActor.ProfileId, out var profile))
            throw new UnauthorizedAccessException("The original project profile changed.");
        var rootMapping = await workspace.Directories.ResolveProfileAsync(profile, owningAppID, token).ConfigureAwait(false);
        if (!rootMapping.IsSuccess || rootMapping.Value!.FolderId.Value != rootFolderID || rootMapping.Value.DirectoryPath != root.DirectoryPath)
            throw new UnauthorizedAccessException("The original registered root mapping changed.");
        var originalRootMapping = rootMapping.Value;
        await root.RevalidateAsync(token).ConfigureAwait(false);
        var chain = new List<HostedItemMetadata>(); var seen = new HashSet<Guid>(); var next = folderID;
        for (var depth = 0; depth < 128; depth++)
        {
            if (!seen.Add(next)) throw new InvalidDataException("The canonical folder ancestry contains a cycle.");
            var found = await workspace.Provider.GetAsync(new(next), token).ConfigureAwait(false);
            if (!found.IsSuccess || found.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: { } } node)
                throw new InvalidOperationException("A canonical project ancestor is unavailable.");
            chain.Add(node);
            if (depth == 0 && node.CurrentRevisionId!.Value.Value != revision)
                throw new InvalidOperationException("The original project folder changed.");
            if (next == rootFolderID) break;
            next = node.ParentId?.Value ?? throw new UnauthorizedAccessException("The selected project is outside the registered app root.");
        }
        if (chain[^1].Id.Value != rootFolderID)
            throw new UnauthorizedAccessException("The canonical project ancestry exceeds the bounded registered root.");
        var scopes = chain.Select(node => new ResourceScope("files.item", node.Id.Value.ToString("D"),
            node.CurrentRevisionId!.Value.ToString(), ResourceAccess.Read)).ToArray();
        if (await resources.AuthorizeAsync(Action, scopes, token).ConfigureAwait(false) != expectedActor)
            throw new UnauthorizedAccessException("The original actor cannot read this project ancestry.");
        var mapped = await workspace.Directories.ResolveFolderAsync(Guid.Empty, profile, new(folderID), token).ConfigureAwait(false);
        var mappingKey = owningAppID + ".project." + folderID.ToString("N");
        if (!mapped.IsSuccess || mapped.Value!.OwningAppId != mappingKey || mapped.Value.FolderId.Value != folderID ||
            mapped.Value.LocationId != chain[0].LocationId)
            throw new UnauthorizedAccessException("The project has no unique trusted Files folder mapping.");
        var binding = mapped.Value;
        await RequireCurrentAsync(token).ConfigureAwait(false);
        var reference = new FilesNativeFolderReference(folderID, expectedFolderRevision, expectedStoreId,
            owningAppID, chain[0].Name, expectedActor) { RootFolderID = rootFolderID, RootFolderRevision = expectedRootRevision };
        return new(reference, binding.DirectoryPath, RequireCurrentAsync);

        async Task RequireCurrentAsync(CancellationToken cancellationToken)
        {
            // Root is disposed after acquisition; retain its raw descriptor and re-observe the owning mapping, never a disposed handle.
            using var currentRoot = await ReadAsync(owningAppID, expectedStoreId, rootFolderID,
                expectedRootRevision, expectedActor, cancellationToken).ConfigureAwait(false);
            var current = await workspaces.GetCurrentAsync(expectedStoreId, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Actor != expectedActor || !ReferenceEquals(current.Provider, workspace.Provider) ||
                await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor)
                throw new UnauthorizedAccessException("The original project binding changed.");
            var currentRootMapping = await current.Directories.ResolveProfileAsync(profile, owningAppID, cancellationToken).ConfigureAwait(false);
            if (!currentRootMapping.IsSuccess || currentRootMapping.Value != originalRootMapping ||
                currentRoot.DirectoryPath != originalRootMapping.DirectoryPath)
                throw new UnauthorizedAccessException("The original registered app root mapping changed.");
            foreach (var node in chain)
            {
                var found = await current.Provider.GetAsync(node.Id, cancellationToken).ConfigureAwait(false);
                if (!found.IsSuccess || found.Value != node)
                    throw new InvalidOperationException("The original project ancestry changed.");
            }
            var currentBinding = await current.Directories.ResolveFolderAsync(Guid.Empty, profile, new(folderID), cancellationToken).ConfigureAwait(false);
            if (!currentBinding.IsSuccess || currentBinding.Value != binding ||
                await resources.AuthorizeAsync(Action, scopes, cancellationToken).ConfigureAwait(false) != expectedActor ||
                await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor)
                throw new UnauthorizedAccessException("The original project mapping or read access changed.");
            await current.Provider.GetStoreEvidenceAsync(expectedStoreId, cancellationToken).ConfigureAwait(false);
        }
    }

}
