using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

/// <summary>Read a preexisting registered child using privately issued original provider observations.
/// Neither caller paths nor store/folder IDs can mint a physical mapping or project grant.</summary>
public sealed class FilesOriginalChildFolderReadSource(FilesArtifactResourceResolver issuer, ResourceAuthorizationService resources)
{
    /// <summary>Use only the root retained by the actual original Home Files configuration.
    /// Missing app setup is a refusal; this path never registers or substitutes another app root.</summary>
    public async Task<FilesNativeFolderReadLease> ReadConfiguredChildAsync(NativeFilesWorkspace original,
        string owningApp, Guid selectedChild, string expectedChildRevision, Func<bool> originalLifetime,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(originalLifetime);
        if (owningApp is not ("stacks" or "sites") || !original.Configuration.AppFolders.TryGetValue(owningApp, out var root) ||
            root.Value == Guid.Empty || selectedChild == Guid.Empty || selectedChild == root.Value)
            throw new UnauthorizedAccessException("The original app root is not configured.");
        var rootContext = await issuer.CaptureOriginalFolderReadAsync(original, root, originalLifetime, token).ConfigureAwait(false);
        if (!resources.IsIssuedOriginalReadOwnerBinding(original.Actor, rootContext, Action, rootContext.OriginalScope,
                original.Provider, original.Directories, original.Configuration.StoreId) ||
            await resources.AuthorizeOriginalReadForActorAsync(original.Actor, rootContext, Action, [rootContext.OriginalScope], token).ConfigureAwait(false) != original.Actor)
            throw new UnauthorizedAccessException("The original configured root is unavailable.");
        return await ReadAsync(original, owningApp, root.Value, rootContext.OriginalScope.Revision,
            selectedChild, expectedChildRevision, originalLifetime, token).ConfigureAwait(false);
    }

    private const string Action = "files.folder.native-root.read";
    public async Task<FilesNativeFolderReadLease> ReadAsync(NativeFilesWorkspace original,
        string owningApp, Guid originalRoot, string expectedRootRevision, Guid selectedChild,
        string expectedChildRevision, Func<bool> originalLifetime, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(originalLifetime);
        if (owningApp is not ("stacks" or "sites") || originalRoot == Guid.Empty || selectedChild == Guid.Empty || originalRoot == selectedChild ||
            !Guid.TryParse(expectedRootRevision, out var rootRevision) || rootRevision == Guid.Empty ||
            !Guid.TryParse(expectedChildRevision, out var childRevision) || childRevision == Guid.Empty ||
            !Guid.TryParse(original.Actor.ProfileId, out var profile))
            throw new ArgumentException("Select an original canonical registered root and child.");
        var retained = new List<(HostedItemMetadata Metadata, IOriginalCanonicalReadContext Context)>();
        async Task<HostedItemMetadata> Capture(Guid id, Guid? requiredRevision)
        {
            var context = await issuer.CaptureOriginalFolderReadAsync(original, new(id), originalLifetime, token).ConfigureAwait(false);
            if (requiredRevision is { } revision && (!Guid.TryParse(context.OriginalScope.Revision, out var observed) || observed != revision))
                throw new InvalidOperationException("The original displayed folder revision changed.");
            await Require(context, token).ConfigureAwait(false);
            var result = await original.Provider.GetForOriginalStoreAsync(original.Configuration.StoreId, new(id), token).ConfigureAwait(false);
            await Require(context, token).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is not { Kind: HostedItemKind.Folder } metadata ||
                metadata.CurrentRevisionId?.ToString() != context.OriginalScope.Revision)
                throw new InvalidOperationException("The original folder changed during capture.");
            retained.Add((metadata, context)); return metadata;
        }
        async Task Require(IOriginalCanonicalReadContext context, CancellationToken ct)
        {
            if (!resources.IsIssuedOriginalReadOwnerBinding(original.Actor, context, Action, context.OriginalScope,
                    original.Provider, original.Directories, original.Configuration.StoreId) ||
                await resources.AuthorizeOriginalReadForActorAsync(original.Actor, context, Action, [context.OriginalScope], ct).ConfigureAwait(false) != original.Actor)
                throw new UnauthorizedAccessException("The original canonical folder read is unavailable.");
        }
        async Task RequireAll(CancellationToken ct)
        { foreach (var entry in retained) await Require(entry.Context, ct).ConfigureAwait(false); }

        var root = await Capture(originalRoot, rootRevision).ConfigureAwait(false);
        await RequireAll(token).ConfigureAwait(false);
        var rootResult = await original.Directories.ResolveOriginalRegisteredFolderAsync(profile, owningApp, root.Id, original.Provider,
            original.Configuration.StoreId, false, ct => new ValueTask(RequireAll(ct)), token).ConfigureAwait(false);
        await RequireAll(token).ConfigureAwait(false);
        if (!rootResult.IsSuccess || rootResult.Value!.FolderId != root.Id || rootResult.Value.OwningAppId != owningApp || rootResult.Value.LocationId != root.LocationId)
            throw new UnauthorizedAccessException("The original app root has no trusted registered mapping.");
        var rootBinding = rootResult.Value!;
        var seen = new HashSet<Guid>(); var next = selectedChild; HostedItemMetadata? child = null;
        for (var depth = 0; depth < 128; depth++)
        {
            if (!seen.Add(next)) throw new InvalidDataException("Canonical folder ancestry contains a cycle.");
            var metadata = next == originalRoot ? root : await Capture(next, depth == 0 ? childRevision : null).ConfigureAwait(false);
            child ??= metadata;
            await RequireAll(token).ConfigureAwait(false);
            if (metadata.Id.Value == originalRoot) break;
            next = metadata.ParentId?.Value ?? throw new UnauthorizedAccessException("The selected child is outside the original registered root.");
        }
        if (next != originalRoot || child is null) throw new UnauthorizedAccessException("The bounded canonical ancestry does not reach the original root.");
        var originalChild = child;
        await RequireAll(token).ConfigureAwait(false);
        var mapped = await original.Directories.ResolveOriginalRegisteredFolderAsync(profile, owningApp + ".project." + selectedChild.ToString("N"),
            originalChild.Id, original.Provider, original.Configuration.StoreId, true, ct => new ValueTask(RequireAll(ct)), token).ConfigureAwait(false);
        await RequireAll(token).ConfigureAwait(false);
        if (!mapped.IsSuccess || mapped.Value!.FolderId != originalChild.Id || mapped.Value.LocationId != originalChild.LocationId ||
            mapped.Value.OwningAppId != owningApp + ".project." + selectedChild.ToString("N"))
            throw new UnauthorizedAccessException("The child has no unique trusted original project mapping.");
        var binding = mapped.Value!;
        await Revalidate(token).ConfigureAwait(false);
        return new(new(selectedChild, expectedChildRevision, original.Configuration.StoreId, owningApp, originalChild.Name, original.Actor)
            { RootFolderID = originalRoot, RootFolderRevision = expectedRootRevision }, binding.DirectoryPath, Revalidate);

        async Task Revalidate(CancellationToken ct)
        {
            foreach (var entry in retained)
            {
                await Require(entry.Context, ct).ConfigureAwait(false);
                var current = await original.Provider.GetForOriginalStoreAsync(original.Configuration.StoreId, entry.Metadata.Id, ct).ConfigureAwait(false);
                await Require(entry.Context, ct).ConfigureAwait(false);
                if (!current.IsSuccess || current.Value != entry.Metadata) throw new InvalidOperationException("The retained original canonical ancestry changed.");
            }
            await RequireAll(ct).ConfigureAwait(false);
            var currentRoot = await original.Directories.ResolveOriginalRegisteredFolderAsync(profile, owningApp, root.Id, original.Provider,
                original.Configuration.StoreId, false, token => new ValueTask(RequireAll(token)), ct).ConfigureAwait(false);
            await RequireAll(ct).ConfigureAwait(false);
            if (!currentRoot.IsSuccess || currentRoot.Value != rootBinding) throw new UnauthorizedAccessException("The original root mapping changed.");
            var currentChild = await original.Directories.ResolveOriginalRegisteredFolderAsync(profile, owningApp + ".project." + selectedChild.ToString("N"),
                originalChild.Id, original.Provider, original.Configuration.StoreId, true, token => new ValueTask(RequireAll(token)), ct).ConfigureAwait(false);
            await RequireAll(ct).ConfigureAwait(false);
            if (!currentChild.IsSuccess || currentChild.Value != binding) throw new UnauthorizedAccessException("The original child mapping changed.");
        }
    }
}
