using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed record FilesNativeItemProperties(FilesLocation ProviderLocation,
    IReadOnlyList<HostedItemMetadata> Ancestors, HostedItemMetadata Item,
    FilesArtifactReference? AppAssociation, bool MaterializationRegistryPresent,
    FilesMaterializedFile? Materialization, string? AncestorPathIssue);

public sealed partial class FilesNativeBrowserService
{
    /// <summary>Resolves a human-readable canonical path from the same issued item page. Every ancestor
    /// is independently reauthorized; the entire result is discarded if the store snapshot changes.</summary>
    public async Task<FilesNativeItemProperties> ReadNativeItemPropertiesAsync(FilesNativeBrowserPage page,
        HostedItemMetadata item, AuthenticatedResourceActor actor, Func<bool> lifetime,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!lifetime() || !_originalPages.TryGetValue(page, out var retained) || retained.Actor != actor
            || !page.Items.Any(row => ReferenceEquals(row, item)))
            throw new UnauthorizedAccessException("Select the same current privately issued Files item for Properties.");
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        var ownerCurrent = await CaptureOriginalPageReadCheckAsync(page, actor, lifetime, token).ConfigureAwait(false);
        var provider = retained.Workspace.Provider;
        var before = await provider.GetStoreEvidenceAsync(page.StoreID, token).ConfigureAwait(false);
        if (before.Revision != page.StoreRevision || (page.ProviderLocation is { } issuedLocation
            && (issuedLocation.Id != provider.Location.Id || issuedLocation.ProviderId != provider.Location.ProviderId))
            || !ReferenceEquals((await RequireWorkspaceAsync(actor, page.StoreID, token).ConfigureAwait(false)).Provider, provider))
            throw new InvalidOperationException("Files changed before the canonical Properties path was read.");

        var ancestors = new List<HostedItemMetadata>();
        var seen = new HashSet<HostedItemId> { item.Id };
        var parentId = item.ParentId;
        string? pathIssue = null;
        while (parentId is { } parent)
        {
            if (!lifetime() || !await ownerCurrent(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The Files Properties owner retired while resolving ancestors.");
            if (ancestors.Count >= 256 || !seen.Add(parent))
            {
                pathIssue = ancestors.Count >= 256 ? "ancestor depth limit reached" : "cycle detected";
                break;
            }
            var result = await provider.GetAsync(parent, token).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is not { Kind: HostedItemKind.Folder } ancestor)
            {
                pathIssue = "an ancestor is missing or is not a folder";
                break;
            }
            try { await RequireMetadataAsync(ancestor, actor, page.StoreID, token).ConfigureAwait(false); }
            catch (UnauthorizedAccessException)
            {
                if (!lifetime() || !await ownerCurrent(token).ConfigureAwait(false)
                    || !ReferenceEquals((await RequireWorkspaceAsync(actor, page.StoreID, token).ConfigureAwait(false)).Provider, provider))
                    throw;
                pathIssue = "ancestor READ is unavailable";
                break;
            }
            if (!lifetime() || !await ownerCurrent(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The Files Properties owner retired while checking an ancestor.");
            ancestors.Add(ancestor);
            parentId = ancestor.ParentId;
        }

        FilesArtifactReference? association = null;
        if (item.Kind == HostedItemKind.Artifact)
        {
            var artifact = await provider.GetArtifactForOriginalStoreAsync(page.StoreID, item.Id,
                item.CurrentRevisionId, token).ConfigureAwait(false);
            if (artifact.IsSuccess && artifact.Value is { } actual)
            {
                if (actual.FileId != item.Id || actual.ParentFolderId != item.ParentId || actual.DisplayName != item.Name)
                    throw new UnauthorizedAccessException("The owning-app artifact association does not match the selected canonical item.");
                association = actual;
            }
            else if (artifact.Error?.Code != FilesErrorCode.ItemNotFound)
                throw new InvalidOperationException(artifact.Error?.Message ?? "The owning-app association is unavailable.");
        }

        var materializationPath = Path.Combine(retained.Workspace.Configuration.RootDirectory,
            ".9to1-files", "materializations.json");
        if (Directory.Exists(materializationPath))
            throw new InvalidDataException("The local materialization registry path is a directory.");
        var materializationRegistryPresent = File.Exists(materializationPath);
        if (materializationRegistryPresent && (File.GetAttributes(materializationPath) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The local materialization registry is redirected.");
        var materialization = materializationRegistryPresent
            ? await retained.Workspace.Materializations.GetExistingByItemIdAsync(item.Id, token).ConfigureAwait(false)
            : null;
        var materializationRegistryStillPresent = File.Exists(materializationPath);
        if (Directory.Exists(materializationPath))
            throw new InvalidDataException("The local materialization registry path changed to a directory.");
        if (materializationRegistryStillPresent != materializationRegistryPresent)
            throw new InvalidOperationException("The local materialization registry changed while Properties was read.");
        if (materializationRegistryStillPresent && (File.GetAttributes(materializationPath) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The local materialization registry changed to a redirected path.");
        var materializationAfter = materializationRegistryStillPresent
            ? await retained.Workspace.Materializations.GetExistingByItemIdAsync(item.Id, token).ConfigureAwait(false)
            : null;
        if (materializationAfter != materialization)
            throw new InvalidOperationException("The selected item's local materialization changed while Properties was read.");

        var after = await provider.GetStoreEvidenceAsync(page.StoreID, token).ConfigureAwait(false);
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        if (before != after || !lifetime() || !await ownerCurrent(token).ConfigureAwait(false)
            || !ReferenceEquals((await RequireWorkspaceAsync(actor, page.StoreID, token).ConfigureAwait(false)).Provider, provider))
            throw new InvalidOperationException("The Files item or ancestor path changed while Properties was read.");
        return new(provider.Location, Array.AsReadOnly(ancestors.AsEnumerable().Reverse().ToArray()), item,
            association, materializationRegistryPresent, materialization, pathIssue);
    }
}
