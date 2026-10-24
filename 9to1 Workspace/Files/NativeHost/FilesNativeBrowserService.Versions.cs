using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    public bool CanReadNativeVersions(FilesNativeBrowserPage page, HostedItemMetadata item,
        AuthenticatedResourceActor actor) => !page.IsTrash && (item.Kind is HostedItemKind.File or HostedItemKind.Artifact)
        && item.CurrentRevisionId is not null
        && item.OwnerPrincipalId == actor.ActorId && _originalPages.TryGetValue(page, out var original)
        && original.Actor == actor && page.Items.Any(row => ReferenceEquals(row, item))
        && original.Workspace.Provider.Location.Capabilities.HasFlag(FilesProviderCapabilities.Versions);

    /// <summary>Reads immutable revision metadata for the exact current row. Historical bytes remain
    /// behind their separate permission-checked content read route.</summary>
    public async Task<FilesPage<FilesRevision>> ReadNativeVersionsAsync(FilesNativeBrowserPage page,
        HostedItemMetadata item, AuthenticatedResourceActor actor, Func<bool> lifetime, string? pageToken,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!lifetime() || !CanReadNativeVersions(page, item, actor)
            || !_originalPages.TryGetValue(page, out var retained) || retained.Actor != actor)
            throw new UnauthorizedAccessException("Select the same current original Files item to review version history.");
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        var ownerCurrent = await CaptureOriginalPageReadCheckAsync(page, actor, lifetime, token).ConfigureAwait(false);
        if (!lifetime() || !await ownerCurrent(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files history read owner retired.");
        var versions = await retained.Workspace.Provider.GetVersionsAsync(item.Id, pageToken, token).ConfigureAwait(false);
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        if (!lifetime() || !await ownerCurrent(token).ConfigureAwait(false)
            || !ReferenceEquals((await RequireWorkspaceAsync(actor, page.StoreID, token).ConfigureAwait(false)).Provider,
                retained.Workspace.Provider)
            || versions.Items.Any(version => version.ItemId != item.Id)
            || versions.Items.Select(version => version.Id).Distinct().Count() != versions.Items.Count
            || versions.Items.Count(version => version.IsCurrent) > 1)
            throw new UnauthorizedAccessException("The original version-history result changed item or revision identity.");
        return versions;
    }
}
