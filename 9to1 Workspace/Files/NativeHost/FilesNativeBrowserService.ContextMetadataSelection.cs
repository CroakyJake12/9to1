using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

/// <summary>
/// Issuer-bound metadata identity for one current original Files row. This observation exposes
/// neither content bytes nor a READ, WRITE, registration, or native-path capability.
/// </summary>
public sealed class FilesOriginalContextMetadataSelection
{
    internal FilesOriginalContextMetadataSelection(AuthenticatedResourceActor actor, Guid storeId,
        Guid parentFolderId, Guid fileId, Guid revisionId, string name, string? contentSha256, long? sizeBytes)
    {
        OriginalActor = actor;
        StoreId = storeId;
        ParentFolderId = parentFolderId;
        FileId = fileId;
        RevisionId = revisionId;
        Name = name;
        ContentSha256 = contentSha256;
        SizeBytes = sizeBytes;
    }

    public AuthenticatedResourceActor OriginalActor { get; }
    public Guid StoreId { get; }
    public Guid ParentFolderId { get; }
    public Guid FileId { get; }
    public Guid RevisionId { get; }
    public string Name { get; }
    public string? ContentSha256 { get; }
    public long? SizeBytes { get; }
    public string CanonicalEntityId => $"{StoreId:D}/{FileId:D}";
}

public sealed partial class FilesNativeBrowserService
{
    private sealed record OriginalContextMetadataBinding(FilesNativeBrowserService Issuer,
        NativeFilesWorkspace Workspace, FilesNativeBrowserPage Page, HostedItemMetadata Row,
        AuthenticatedResourceActor Actor);

    private readonly object _originalContextMetadataGate = new();
    private readonly ConditionalWeakTable<FilesOriginalContextMetadataSelection, OriginalContextMetadataBinding>
        _originalContextMetadataSelections = new();

    /// <summary>Returns only an opaque identity backed by the SAME current privately issued page
    /// and reference-identical file row. Metadata identity does not authorize reading its content.</summary>
    public Task<FilesOriginalContextMetadataSelection> SelectOriginalContextMetadataWithinSourceAsync(
        FilesNativeBrowserPage samePage, HostedItemMetadata sameRow, AuthenticatedResourceActor actualActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(samePage);
        ArgumentNullException.ThrowIfNull(sameRow);
        ArgumentNullException.ThrowIfNull(actualActor);
        var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
        return source.Observe(async () =>
        {
            token.ThrowIfCancellationRequested();
            var retained = source.Invoke(() =>
            {
                if (!_originalPages.TryGetValue(samePage, out var original) || original.Actor != actualActor ||
                    original.Workspace.Configuration.StoreId != samePage.StoreID || samePage.IsTrash ||
                    samePage.ParentID is not { } parent || !samePage.Items.Any(item => ReferenceEquals(item, sameRow)) ||
                    sameRow.Kind != HostedItemKind.File || sameRow.ParentId?.Value != parent ||
                    sameRow.CurrentRevisionId is not { } revision || revision.Value == Guid.Empty ||
                    sameRow.Id.Value == Guid.Empty || string.IsNullOrWhiteSpace(sameRow.Name))
                    throw new UnauthorizedAccessException("Select the SAME current file row from an original Files folder.");
                return (Original: original, Parent: parent, Revision: revision);
            });

            var workspace = await source.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(
                samePage.StoreID, source, token)).ConfigureAwait(false);
            if (workspace?.Actor != actualActor || workspace.Configuration != retained.Original.Workspace.Configuration ||
                !ReferenceEquals(workspace.Provider, retained.Original.Workspace.Provider) ||
                !ReferenceEquals(workspace.Directories, retained.Original.Workspace.Directories) ||
                !ReferenceEquals(workspace.Materializations, retained.Original.Workspace.Materializations))
                throw new UnauthorizedAccessException("The original Home Files workspace or actor changed.");

            await source.ObserveVoid(() => RevalidateOriginalDeveloperAsync(
                samePage, actualActor, source, token)).ConfigureAwait(false);
            var current = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                samePage.StoreID, sameRow.Id, token)).ConfigureAwait(false);
            if (!current.IsSuccess || current.Value is not { } currentRow || currentRow != sameRow ||
                currentRow.CurrentRevisionId != retained.Revision || currentRow.ParentId?.Value != retained.Parent ||
                currentRow.Kind != HostedItemKind.File)
                throw new InvalidOperationException("The selected original Files file or revision changed.");

            var issued = new FilesOriginalContextMetadataSelection(actualActor, samePage.StoreID,
                retained.Parent, sameRow.Id.Value, retained.Revision.Value, sameRow.Name,
                sameRow.ContentHash, sameRow.SizeBytes);
            source.Invoke(() =>
            {
                lock (_originalContextMetadataGate)
                    _originalContextMetadataSelections.Add(issued,
                        new OriginalContextMetadataBinding(this, retained.Original.Workspace, samePage, sameRow, actualActor));
                return true;
            });

            await DemandOriginalContextMetadataSelectionAsync(issued, source, token).ConfigureAwait(false);
            return issued;
        });
    }

    /// <summary>Checks object identity and revalidates the same page, row, actor, store and revision.
    /// This check grants no content access and accepts no caller-reconstructed metadata.</summary>
    public Task RevalidateOriginalContextMetadataWithinSourceAsync(
        FilesOriginalContextMetadataSelection sameSelection, Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameSelection);
        var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
        return source.Observe(async () =>
        {
            token.ThrowIfCancellationRequested();
            await DemandOriginalContextMetadataSelectionAsync(sameSelection, source, token).ConfigureAwait(false);
            return true;
        });
    }

    /// <summary>Identity test only. Use the asynchronous revalidation before publishing or using a reference.</summary>
    public bool IsIssuedOriginalContextMetadataSelection(FilesOriginalContextMetadataSelection sameSelection)
    {
        if (sameSelection is null) return false;
        lock (_originalContextMetadataGate)
            return _originalContextMetadataSelections.TryGetValue(sameSelection, out var binding) &&
                ReferenceEquals(binding.Issuer, this) && binding.Actor == sameSelection.OriginalActor &&
                binding.Page.StoreID == sameSelection.StoreId && binding.Row.Id.Value == sameSelection.FileId &&
                binding.Row.CurrentRevisionId?.Value == sameSelection.RevisionId &&
                binding.Row.ParentId?.Value == sameSelection.ParentFolderId &&
                binding.Row.Name == sameSelection.Name && binding.Row.ContentHash == sameSelection.ContentSha256 &&
                binding.Row.SizeBytes == sameSelection.SizeBytes &&
                binding.Page.Items.Any(row => ReferenceEquals(row, binding.Row));
    }

    private async Task DemandOriginalContextMetadataSelectionAsync(
        FilesOriginalContextMetadataSelection sameSelection, FilesOriginalReadSourceScope source,
        CancellationToken token)
    {
        var binding = source.Invoke(() =>
        {
            lock (_originalContextMetadataGate)
                if (_originalContextMetadataSelections.TryGetValue(sameSelection, out var actual) &&
                    ReferenceEquals(actual.Issuer, this) && IsIssuedOriginalContextMetadataSelection(sameSelection))
                    return actual;
            throw new UnauthorizedAccessException("Retain the same original Files metadata selection.");
        });
        if (!source.Invoke(() => ReferenceEquals(binding.Issuer, this) && binding.Actor == sameSelection.OriginalActor &&
            binding.Page.StoreID == sameSelection.StoreId && binding.Row.Id.Value == sameSelection.FileId &&
            binding.Row.CurrentRevisionId?.Value == sameSelection.RevisionId &&
            binding.Row.ParentId?.Value == binding.Page.ParentID && binding.Page.Items.Any(row => ReferenceEquals(row, binding.Row))))
            throw new UnauthorizedAccessException("The original Files metadata selection changed.");

        var workspace = await source.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(
            sameSelection.StoreId, source, token)).ConfigureAwait(false);
        if (workspace?.Actor != binding.Actor || workspace.Configuration != binding.Workspace.Configuration ||
            !ReferenceEquals(workspace.Provider, binding.Workspace.Provider) ||
            !ReferenceEquals(workspace.Directories, binding.Workspace.Directories) ||
            !ReferenceEquals(workspace.Materializations, binding.Workspace.Materializations))
            throw new UnauthorizedAccessException("The original Files workspace changed.");

        await source.ObserveVoid(() => RevalidateOriginalDeveloperAsync(
            binding.Page, binding.Actor, source, token)).ConfigureAwait(false);
        var current = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
            sameSelection.StoreId, new(sameSelection.FileId), token)).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value is not { } currentRow || currentRow != binding.Row ||
            currentRow.Id.Value != sameSelection.FileId || currentRow.CurrentRevisionId?.Value != sameSelection.RevisionId ||
            currentRow.ParentId?.Value != sameSelection.ParentFolderId || currentRow.Name != sameSelection.Name ||
            currentRow.ContentHash != sameSelection.ContentSha256 || currentRow.SizeBytes != sameSelection.SizeBytes ||
            currentRow.Kind != HostedItemKind.File)
            throw new InvalidOperationException("The selected original Files file or revision changed.");
    }
}
