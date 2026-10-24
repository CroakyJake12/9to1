namespace HavenOS.Files.CUI.Tests;

internal static class FilesVersionProviderTests
{
    public static async Task RunAllAsync()
    {
        await DurableHistoryReturnsCurrentAndPriorCanonicalRevisions();
    }

    private static async Task DurableHistoryReturnsCurrentAndPriorCanonicalRevisions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "files-versions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string owner = "versions-owner";
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(Path.Combine(directory, "files.json"), location, owner);
            var now = DateTimeOffset.UtcNow;
            var folderOperation = new FilesOperation(new(Guid.NewGuid()), owner, HostedItemId.New(), null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Check.True((await provider.MutateAsync(folderOperation, "Documents", default)).IsSuccess);
            var folder = (await provider.GetAsync(folderOperation.ItemId, default)).Value!;
            var store = await provider.GetStoreEvidenceAsync();
            var fileId = HostedItemId.New();
            var artifact = new FilesArtifactReference("write", "versioned-document", fileId, folder.Id,
                "document", "Versioned document");
            var registered = await provider.RegisterArtifactAsync(artifact, owner);
            Check.True(registered.IsSuccess);
            var authority = new FilesCommitAuthorityGuard(owner, _ => ValueTask.FromResult(true));
            var first = new FilesOwningAppRevisionCommit(fileId, "write", "revision-one", owner, now,
                10, new string('a', 64), "versions/revision-one", null);
            var created = await provider.CommitCreatedArtifactAsync(registered.Value!, first,
                [new FilesItemRevisionPrecondition(folder.Id, folder.CurrentRevisionId)], store.StoreId,
                authority, default);
            Check.True(created.IsSuccess);

            var secondTime = now.AddMinutes(1);
            var second = new FilesOwningAppRevisionCommit(fileId, "write", "revision-two", owner, secondTime,
                20, new string('b', 64), "versions/revision-two", created.Value!.Id);
            var updated = await provider.CommitDurableRevisionAsync(second, Array.Empty<FilesItemRevisionPrecondition>(),
                store.StoreId, authority, default);
            Check.True(updated.IsSuccess);

            Check.True(provider.Location.Capabilities.Supports(FilesProviderCapabilities.Versions));
            var history = await provider.GetVersionsAsync(fileId, null);
            Check.Equal(2, history.Items.Count);
            Check.Equal(updated.Value!.Id, history.Items[0].Id);
            Check.True(history.Items[0].IsCurrent);
            Check.Equal(created.Value.Id, history.Items[1].Id);
            Check.False(history.Items[1].IsCurrent);
            Check.Equal(fileId, history.Items[0].ItemId);
            Check.Equal(second.OwningAppRevisionId, history.Items[0].OwningAppRevisionId);
            await Check.ThrowsAsync<ArgumentException>(() => provider.GetVersionsAsync(fileId, "files-versions-v1:100:stale"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
