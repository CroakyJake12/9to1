namespace HavenOS.Files.CUI.Tests;

internal static class MoveProviderTests
{
    public static async Task RunAllAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "files-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "files.json"); var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(path, location, "move-owner");
            var source = await Create(provider, null, "Source");
            var destination = await Create(provider, null, "Destination");
            var folder = await Create(provider, source.Id, "Moving folder");
            var child = await Create(provider, folder.Id, "Same child");
            var move = Operation(folder, destination.Id);
            var evidence = await provider.GetStoreEvidenceAsync();
            var parents = new[] { new FilesItemRevisionPrecondition(source.Id, source.CurrentRevisionId),
                new FilesItemRevisionPrecondition(destination.Id, destination.CurrentRevisionId) };
            var invalid = await provider.CommitNativeBrowserStructureAsync(move, null, evidence.StoreId, evidence.Revision,
                [null!], new("move-owner", _ => ValueTask.FromResult(true)), default);
            Check.Equal(FilesErrorCode.PermissionDenied, invalid.Error!.Code);
            var missingParent = await provider.CommitNativeBrowserStructureAsync(move, null, evidence.StoreId, evidence.Revision,
                [parents[0]], new("move-owner", _ => ValueTask.FromResult(true)), default);
            Check.Equal(FilesErrorCode.PermissionDenied, missingParent.Error!.Code);
            var wrongSource = await provider.MutateAsync(move with { SourceParentId = destination.Id }, null, default);
            Check.Equal(FilesErrorCode.RevisionConflict, wrongSource.Error!.Code);
            var self = await provider.MutateAsync(Operation(folder, folder.Id), null, default);
            Check.Equal(FilesErrorCode.InvalidState, self.Error!.Code);
            var descendant = await provider.MutateAsync(Operation(folder, child.Id), null, default);
            Check.Equal(FilesErrorCode.InvalidState, descendant.Error!.Code);
            Check.Equal(evidence, await provider.GetStoreEvidenceAsync());
            var denied = await provider.CommitNativeBrowserStructureAsync(move, null, evidence.StoreId, evidence.Revision,
                parents, new("move-owner", _ => ValueTask.FromResult(false)), default);
            Check.Equal(FilesErrorCode.PermissionDenied, denied.Error!.Code);
            Check.Equal(evidence, await provider.GetStoreEvidenceAsync());
            var result = await provider.CommitNativeBrowserStructureAsync(move, null, evidence.StoreId, evidence.Revision,
                parents, new("move-owner", _ => ValueTask.FromResult(true)), default);
            Check.True(result.IsSuccess);
            var reopened = new DurableDriveProvider(path, location, "move-owner");
            var moved = (await reopened.GetAsync(folder.Id, default)).Value!;
            Check.Equal(folder.Id, moved.Id); Check.Equal(destination.Id, moved.ParentId);
            Check.Equal(child, (await reopened.GetAsync(child.Id, default)).Value!);
            Check.Equal(0, (await reopened.ListAsync(source.Id, null, null, default)).Items.Count);
            var duplicate = await reopened.MutateAsync(move, null, default);
            Check.True(duplicate.IsSuccess); Check.Equal(result.Value, duplicate.Value);
            var changes = (await reopened.GetChangesAsync(null, 100, default)).Items;
            Check.Equal(5, changes.Count); Check.Equal(1, changes.Count(change => change.Kind == "Move"));
            var sameParent = await reopened.MutateAsync(Operation(moved, destination.Id), null, default);
            Check.Equal(FilesErrorCode.InvalidState, sameParent.Error!.Code);
            var occupied = await Create(reopened, source.Id, moved.Name);
            var conflict = await reopened.MutateAsync(Operation(moved, source.Id), null, default);
            Check.Equal(FilesErrorCode.NameConflict, conflict.Error!.Code);
            Check.Equal(destination.Id, (await reopened.GetAsync(folder.Id, default)).Value!.ParentId);
            Check.Equal(occupied.Id, (await reopened.ListAsync(source.Id, null, null, default)).Items.Single().Id);
            Console.WriteLine("Files Move provider checks passed: guarded publication, invalid parents, self/descendant refusal, exact conflicts, restart and idempotent identity preservation.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static FilesOperation Operation(HostedItemMetadata item, HostedItemId destination)
    {
        var now = DateTimeOffset.UtcNow;
        return new(new(Guid.NewGuid()), "move-owner", item.Id, item.ParentId, destination, "Move",
            item.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
    }

    private static async Task<HostedItemMetadata> Create(DurableDriveProvider provider, HostedItemId? parent, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new FilesOperation(new(Guid.NewGuid()), "move-owner", HostedItemId.New(), null, parent,
            "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
        Check.True((await provider.MutateAsync(operation, name, default)).IsSuccess);
        return (await provider.GetAsync(operation.ItemId, default)).Value!;
    }
}
