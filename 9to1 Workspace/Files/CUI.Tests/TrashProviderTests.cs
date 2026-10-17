namespace HavenOS.Files.CUI.Tests;

internal static class TrashProviderTests
{
    public static async Task RunAllAsync()
    {
        await RoundTripPreservesSubtreeAcrossReopen();
        await NativeCommitRequiresCurrentAuthorityAndRevisions();
        await RestoreConflictPreservesBothItems();
        Console.WriteLine("Files Trash provider checks: 3 passed (real durable storage; native Home/UI not exercised).");
    }

    private static async Task RoundTripPreservesSubtreeAcrossReopen() => await WithStore(async (provider, path, location) =>
    {
        var folder = await Create(provider, null, "Recoverable folder");
        var child = await Create(provider, folder.Id, "Retained child");
        var deleted = await provider.MutateAsync(Operation("Delete", folder), null, default);
        Check.True(deleted.IsSuccess);
        Check.False((await provider.GetAsync(folder.Id, default)).IsSuccess);
        Check.False((await provider.GetAsync(child.Id, default)).IsSuccess);
        var reopened = new DurableDriveProvider(path, location, "trash-owner");
        var evidence = await reopened.GetStoreEvidenceAsync();
        var trash = await reopened.ListTrashForOriginalStoreAsync(evidence.StoreId, "recoverable", null, 100);
        Check.Equal(1, trash.Items.Count);
        var tombstone = trash.Items[0];
        Check.Equal(folder.Id, tombstone.Metadata.Id);
        Check.Equal(folder.ParentId, tombstone.Metadata.ParentId);
        Check.Equal(deleted.Value!.ResultRevisionId, tombstone.Metadata.CurrentRevisionId);
        Check.True(tombstone.DeletedAt >= folder.ModifiedAt);
        var restored = await reopened.MutateAsync(Operation("Restore", tombstone.Metadata), null, default);
        Check.True(restored.IsSuccess);
        var final = new DurableDriveProvider(path, location, "trash-owner");
        Check.Equal(folder.Id, (await final.GetAsync(folder.Id, default)).Value!.Id);
        Check.Equal(child.CurrentRevisionId, (await final.GetAsync(child.Id, default)).Value!.CurrentRevisionId);
        Check.Equal(0, (await final.ListTrashForOriginalStoreAsync(evidence.StoreId, "", null, 100)).Items.Count);
        Check.False((await final.GetTrashedAsync(folder.Id)).IsSuccess);
        Check.Equal(4, (await final.GetChangesAsync(null, 100, default)).Items.Count);
        var live = (await final.GetAsync(folder.Id, default)).Value!;
        var secondRestore = await final.MutateAsync(Operation("Restore", live), null, default);
        Check.Equal(FilesErrorCode.InvalidState, secondRestore.Error!.Code);
    });

    private static async Task NativeCommitRequiresCurrentAuthorityAndRevisions() => await WithStore(async (provider, _, _) =>
    {
        var folder = await Create(provider, null, "Guarded item");
        var before = await provider.GetStoreEvidenceAsync();
        var deletion = Operation("Delete", folder);
        var denied = await provider.CommitNativeBrowserStructureAsync(deletion, null, before.StoreId, before.Revision,
            [], new("trash-owner", _ => ValueTask.FromResult(false)), default);
        Check.Equal(FilesErrorCode.PermissionDenied, denied.Error!.Code);
        Check.Equal(before, await provider.GetStoreEvidenceAsync(before.StoreId));
        var stale = await provider.CommitNativeBrowserStructureAsync(deletion, null, before.StoreId, "stale-revision",
            [], new("trash-owner", _ => ValueTask.FromResult(true)), default);
        Check.Equal(FilesErrorCode.RevisionConflict, stale.Error!.Code);
        var deleted = await provider.CommitNativeBrowserStructureAsync(deletion, null, before.StoreId, before.Revision,
            [], new("trash-owner", _ => ValueTask.FromResult(true)), default);
        Check.True(deleted.IsSuccess);
        await Check.ThrowsAsync<FilesOriginalStoreReadChangedException>(() => provider.GetTrashedForOriginalStoreAsync(Guid.NewGuid(), folder.Id));
        await Check.ThrowsAsync<FilesOriginalStoreReadChangedException>(() => provider.ListTrashForOriginalStoreAsync(Guid.NewGuid(), "", null, 100));
        var current = (await provider.GetTrashedForOriginalStoreAsync(before.StoreId, folder.Id)).Value!;
        var wrongRevision = await provider.MutateAsync(Operation("Restore", folder), null, default);
        Check.Equal(FilesErrorCode.RevisionConflict, wrongRevision.Error!.Code);
        var evidence = await provider.GetStoreEvidenceAsync(before.StoreId);
        var restored = await provider.CommitNativeBrowserStructureAsync(Operation("Restore", current), null, evidence.StoreId,
            evidence.Revision, [], new("trash-owner", _ => ValueTask.FromResult(true)), default);
        Check.True(restored.IsSuccess);
    });

    private static async Task RestoreConflictPreservesBothItems() => await WithStore(async (provider, _, _) =>
    {
        var original = await Create(provider, null, "Occupied name");
        Check.True((await provider.MutateAsync(Operation("Delete", original), null, default)).IsSuccess);
        var replacement = await Create(provider, null, "Occupied name");
        var trash = (await provider.GetTrashedAsync(original.Id)).Value!;
        var restored = await provider.MutateAsync(Operation("Restore", trash), null, default);
        Check.Equal(FilesErrorCode.NameConflict, restored.Error!.Code);
        Check.True((await provider.GetTrashedAsync(original.Id)).IsSuccess);
        Check.Equal(replacement.Id, (await provider.ListAsync(null, null, null, default)).Items.Single().Id);
        Check.Equal(3, (await provider.GetChangesAsync(null, 100, default)).Items.Count);
    });

    private static async Task WithStore(Func<DurableDriveProvider, string, FilesLocationId, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "files-trash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var location = new FilesLocationId(Guid.NewGuid()); var path = Path.Combine(directory, "files.json");
            await action(new(path, location, "trash-owner"), path, location);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task<HostedItemMetadata> Create(DurableDriveProvider provider, HostedItemId? parent, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new FilesOperation(new(Guid.NewGuid()), "trash-owner", HostedItemId.New(), null, parent,
            "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
        Check.True((await provider.MutateAsync(operation, name, default)).IsSuccess);
        return (await provider.GetAsync(operation.ItemId, default)).Value!;
    }
    private static FilesOperation Operation(string action, HostedItemMetadata item)
    {
        var now = DateTimeOffset.UtcNow;
        return new(new(Guid.NewGuid()), "trash-owner", item.Id, item.ParentId, null, action,
            item.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
    }
}
