using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Apps.Spaces.Builder;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class OriginalSpaceRequestPolicySource
{
    private const int MaximumFileChoiceRows = 4_096;
    private sealed record FilesChoiceCatalogue(OriginalSpaceRequestPolicySource Issuer, Owners Owners,
        Authority Authority, FilesNativeBrowserService Browser, Guid StoreId, Guid SpaceId, long SpaceRevision);
    private sealed record OriginalFilesChoice(FilesChoiceCatalogue Catalogue, FilesNativeBrowserPage Page,
        HostedItemMetadata Row) : ISpaceBuilderOriginalFilesSourceChoice;

    internal Task<IReadOnlyList<SpaceBuilderFilesSourceOption>> GetOriginalFilesContextChoicesAsync(
        SpaceRegistry registry, Guid spaceId, long expectedRevision, Func<bool> lifetime,
        Action<Action> acquireOriginal, CancellationToken token) => Run(acquireOriginal, async originals =>
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var owners = originals.Invoke(CurrentOwners);
        if (!ReferenceEquals(owners.Spaces, registry))
            throw Block("SPACE_FILES_CHOOSER_REGISTRY_CHANGED", "The Files chooser belongs to another Space registry.");
        var authority = await ReadAuthorityAsync(owners, originals, token).ConfigureAwait(false);
        await DemandChooserSpaceAsync(owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
        if (!originals.Invoke(lifetime)) throw Block("SPACE_FILES_CHOOSER_RETIRED", "The Space Builder was closed before Files browsing finished.");
        var browser = GetOriginalFilesBrowser(owners);
        var available = await originals.AwaitAsync(originals.Invoke(() => browser.ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(
            authority.Actor, lifetime, Own, Retain, token))).ConfigureAwait(false);
        if (!browser.IsIssuedOriginalHomeLibraryAvailability(available, authority.Actor) ||
            !available.IsAvailable || available.StoreId is not { } storeId || storeId == Guid.Empty)
            throw Block("SPACE_FILES_CHOOSER_UNAVAILABLE", available.Message);

        var catalogue = new FilesChoiceCatalogue(this, owners, authority, browser, storeId, spaceId, expectedRevision);
        var pendingFolders = new Queue<(Guid? Id, string Path)>();
        pendingFolders.Enqueue((null, "Files"));
        var seenFolders = new HashSet<Guid>();
        var seenRows = new HashSet<Guid>();
        var pages = new List<FilesNativeBrowserPage>();
        var choices = new List<SpaceBuilderFilesSourceOption>();
        string? storeRevision = null;
        var observedRows = 0;
        while (pendingFolders.TryDequeue(out var folder))
        {
            FilesNativeBrowserCursor? cursor = null;
            var offsets = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (!originals.Invoke(lifetime))
                    throw Block("SPACE_FILES_CHOOSER_RETIRED", "The Space Builder was closed before Files browsing finished.");
                var page = await originals.AwaitAsync(originals.Invoke(() => browser.ListOriginalHomeLibraryFolderPageWithinSourceAsync(
                    authority.Actor, storeId, folder.Id, string.Empty, cursor, lifetime, Own, Retain, token))).ConfigureAwait(false);
                if (page.StoreID != storeId || page.ParentID != folder.Id || page.IsTrash ||
                    storeRevision is not null && storeRevision != page.StoreRevision)
                    throw Block("SPACE_FILES_CHOOSER_PAGE_CHANGED", "The original Files folder listing changed stores or revisions. Refresh it.");
                storeRevision ??= page.StoreRevision;
                pages.Add(page);
                observedRows = checked(observedRows + page.Items.Count);
                if (observedRows > MaximumFileChoiceRows)
                    throw Block("SPACE_FILES_CHOOSER_LIMIT", "This Files tree exceeds the current 4,096-item metadata limit. Browse Files to narrow it before attaching a reference.");
                foreach (var row in page.Items)
                {
                    if (row.Id.Value == Guid.Empty || string.IsNullOrWhiteSpace(row.Name) || row.ParentId?.Value != folder.Id)
                        throw Block("SPACE_FILES_CHOOSER_ROW_INVALID", "Files returned invalid canonical item metadata.");
                    if (!seenRows.Add(row.Id.Value))
                        throw Block("SPACE_FILES_CHOOSER_DUPLICATE", "Files returned a repeated item identity. Refresh before attaching a reference.");
                    if (row.Kind == HostedItemKind.Folder)
                    {
                        if (!seenFolders.Add(row.Id.Value))
                            throw Block("SPACE_FILES_CHOOSER_FOLDER_CYCLE", "Files returned a repeated folder identity.");
                        pendingFolders.Enqueue((row.Id.Value, folder.Path + " / " + row.Name));
                        continue;
                    }
                    if (row.Kind != HostedItemKind.File || row.CurrentRevisionId is not { } fileRevision) continue;
                    var canonicalId = storeId.ToString("D") + "/" + row.Id.Value.ToString("D");
                    choices.Add(new(canonicalId, BoundLabel(row.Name), folder.Path, fileRevision.Value.ToString("D"),
                        new OriginalFilesChoice(catalogue, page, row)));
                }
                cursor = page.Next;
                if (cursor is not null && (cursor.StoreID != storeId || cursor.StoreRevision != storeRevision ||
                    cursor.ParentID != folder.Id || cursor.OriginalActor != authority.Actor || cursor.IsTrash ||
                    cursor.Search != string.Empty || !offsets.Add(cursor.Offset)))
                    throw Block("SPACE_FILES_CHOOSER_CURSOR_CHANGED", "The original Files cursor changed or repeated. Refresh before attaching a reference.");
            } while (cursor is not null);
        }

        foreach (var page in pages)
            await originals.AwaitAsync(originals.Invoke(() => browser.RevalidateOriginalHomeLibraryPageWithinSourceAsync(
                page, authority.Actor, lifetime, Own, Retain, token))).ConfigureAwait(false);
        await DemandChooserSpaceAsync(owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
        await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
        return (IReadOnlyList<SpaceBuilderFilesSourceOption>)Array.AsReadOnly(choices
            .OrderBy(choice => choice.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Id, StringComparer.Ordinal).ToArray());

        void Own(Action body) => originals.Invoke(() => { body(); return true; });
        void Retain(Task task) => originals.Track(task);
    });

    internal Task<SpaceContextReference> ResolveOriginalFilesContextChoiceAsync(
        ISpaceBuilderOriginalFilesSourceChoice choice, SpaceRegistry registry, Guid spaceId, long expectedRevision,
        Func<bool> lifetime, Action<Action> acquireOriginal, CancellationToken token) => Run(acquireOriginal, async originals =>
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (choice is not OriginalFilesChoice actual || !ReferenceEquals(actual.Catalogue.Issuer, this) ||
            !ReferenceEquals(actual.Catalogue.Owners.Spaces, registry) || actual.Catalogue.SpaceId != spaceId ||
            actual.Catalogue.SpaceRevision != expectedRevision)
            throw Block("SPACE_FILES_CHOOSER_INPUT_NOT_ORIGINAL", "Choose a current Files row from this Space Builder's own Files listing.");
        var catalogue = actual.Catalogue;
        await DemandAuthorityAsync(catalogue.Owners, catalogue.Authority, originals, token).ConfigureAwait(false);
        await DemandChooserSpaceAsync(catalogue.Owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
        if (!originals.Invoke(lifetime))
            throw Block("SPACE_FILES_CHOOSER_RETIRED", "The Space Builder was closed before the Files reference could be attached.");
        var browser = GetOriginalFilesBrowser(catalogue.Owners);
        if (!ReferenceEquals(browser, catalogue.Browser) || actual.Page.StoreID != catalogue.StoreId ||
            actual.Row.Kind != HostedItemKind.File || actual.Row.CurrentRevisionId is null ||
            !actual.Page.Items.Any(row => ReferenceEquals(row, actual.Row)))
            throw Block("SPACE_FILES_CHOOSER_SELECTION_CHANGED", "The selected Files row is no longer the exact current file. Refresh the list.");

        void Own(Action body) => originals.Invoke(() => { body(); return true; });
        void Retain(Task task) => originals.Track(task);
        var selected = await originals.AwaitAsync(originals.Invoke(() => browser.SelectOriginalContextMetadataWithinSourceAsync(
            actual.Page, actual.Row, catalogue.Authority.Actor, Own, Retain, token))).ConfigureAwait(false);
        if (!browser.IsIssuedOriginalContextMetadataSelection(selected) || selected.OriginalActor != catalogue.Authority.Actor ||
            selected.StoreId != catalogue.StoreId || selected.FileId != actual.Row.Id.Value ||
            selected.RevisionId != actual.Row.CurrentRevisionId!.Value.Value ||
            selected.CanonicalEntityId != storeIdAndFileId(selected.StoreId, selected.FileId))
            throw Block("SPACE_FILES_CHOOSER_SELECTION_NOT_ORIGINAL", "The Files owner did not issue the current selected row.");
        await originals.AwaitAsync(originals.Invoke(() => browser.RevalidateOriginalContextMetadataWithinSourceAsync(
            selected, Own, Retain, token))).ConfigureAwait(false);
        await DemandChooserSpaceAsync(catalogue.Owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
        await DemandAuthorityAsync(catalogue.Owners, catalogue.Authority, originals, token).ConfigureAwait(false);

        var reference = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.File, "files",
            selected.CanonicalEntityId, selected.RevisionId.ToString("D"), SpaceContextPermission.Read,
            SpaceContextIndexState.NotRequired, true, DateTimeOffset.UtcNow, selected.FileId)
        { ContentSha256 = selected.ContentSha256 };
        return reference;
    });

    private FilesNativeBrowserService GetOriginalFilesBrowser(Owners owners)
    {
        var browser = _services.GetRequiredService<FilesNativeBrowserService>();
        var workspaces = _services.GetRequiredService<NativeFilesWorkspaceAuthority>();
        var packages = _services.GetRequiredService<ICompatibilityPackageContentSource>();
        if (!browser.IsBoundToOriginalComposition(workspaces, owners.Home.Profiles, owners.Home.Resources, packages))
            throw Block("SPACE_FILES_CHOOSER_OWNER_CHANGED", "The Files browser is not bound to this original Home actor, resource owner and package source.");
        return browser;
    }

    private static string storeIdAndFileId(Guid storeId, Guid fileId) => storeId.ToString("D") + "/" + fileId.ToString("D");
}
