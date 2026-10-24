namespace HavenOS.Files.CUI.Tests;

internal static class FilesSearchProviderTests
{
    public static async Task RunAllAsync()
    {
        await NameSearchIsRecursiveScopedAndRejectsStalePages();
    }

    private static async Task NameSearchIsRecursiveScopedAndRejectsStalePages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "files-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(Path.Combine(directory, "files.json"), location, "search-owner");
            var root = await Create(provider, null, "Reports");
            var first = await Create(provider, root.Id, "Quarter report one");
            var second = await Create(provider, root.Id, "Quarter report two");
            var unrelated = await Create(provider, null, "Quarter report elsewhere");

            var query = new FilesSearchQuery("report", Limit: 2);
            var page = await provider.SearchAsync(query, null);
            Check.Equal(2, page.Items.Count);
            Check.True(page.NextPageToken is not null);
            Check.True(page.Items.All(item => item.LocationId == location));
            var scoped = await provider.SearchAsync(query with { ParentId = root.Id }, null);
            Check.True(scoped.Items.Select(item => item.Id).ToHashSet().SetEquals(new[] { first.Id, second.Id }));
            Check.False(scoped.Items.Any(item => item.Id == unrelated.Id));

            var staleToken = page.NextPageToken!;
            await Create(provider, null, "New report after first page");
            await Check.ThrowsAsync<ArgumentException>(() => provider.SearchAsync(query, staleToken));
            await Check.ThrowsAsync<ArgumentException>(() => provider.SearchAsync(query with { LocationId = new(Guid.NewGuid()) }, null));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<HostedItemMetadata> Create(DurableDriveProvider provider, HostedItemId? parent, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new FilesOperation(new(Guid.NewGuid()), "search-owner", HostedItemId.New(), null, parent,
            "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
        Check.True((await provider.MutateAsync(operation, name, default)).IsSuccess);
        return (await provider.GetAsync(operation.ItemId, default)).Value!;
    }
}
