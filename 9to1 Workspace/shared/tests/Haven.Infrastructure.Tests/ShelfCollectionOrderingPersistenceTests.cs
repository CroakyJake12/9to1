using System.Text.Json;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;

namespace Haven.Infrastructure.Tests;

public sealed class ShelfCollectionOrderingPersistenceTests
{
    [Fact]
    public async Task Lying_order_count_stops_at_first_excess_id_without_touching_actual_persisted_collection()
    {
        using var paths = new Paths(); var seed = Seed(); var store = new VersionedAtomicSettingsStore(paths);
        var service = new ShelfLibraryService(store);
        Assert.True((await service.ImportAsync(0, new(seed.Library, [], []))).Success);
        var file = Path.Combine(paths.DataDirectory, "settings.json"); var before = await File.ReadAllBytesAsync(file);
        var supplied = new LyingOrder(seed.Items[0].Id);
        Assert.Throws<ArgumentException>(() => { _ = service.ReorderCollectionAsync(1, seed.First.Id, supplied); });
        Assert.Equal(2, supplied.Consumed);
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
        Assert.Equal(1, (await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync()).Library.Revision);
    }
    private sealed class LyingOrder(Guid id) : IReadOnlyList<Guid>
    {
        public int Consumed { get; private set; }
        public int Count => 1;
        public Guid this[int index] => throw new NotSupportedException("Enumeration must be bounded without indexing caller storage.");
        public IEnumerator<Guid> GetEnumerator()
        { for (var index = 0; index < 1_000_000; index++) { Consumed++; yield return id; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task Actual_persisted_collection_order_survives_restart_and_filtered_search_without_reordering_other_collection()
    {
        using var paths = new Paths(); var fixture = Seed();
        var library = new ShelfLibraryService(new VersionedAtomicSettingsStore(paths));
        Assert.True((await library.ImportAsync(0, new(fixture.Library, [], []))).Success);
        var changed = await library.ReorderCollectionAsync(1, fixture.First.Id, [fixture.Items[2].Id, fixture.Items[0].Id, fixture.Items[1].Id]);
        Assert.True(changed.Success);
        var restarted = new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)); var state = await restarted.ReadAsync();
        Assert.Equal(2, state.Library.Revision);
        Assert.Equal([fixture.Items[2].Id, fixture.Items[0].Id, fixture.Items[1].Id], ShelfLibraryService.Search(state, "", fixture.First.Id).Select(item => item.Id));
        Assert.Equal([fixture.Items[2].Id, fixture.Items[0].Id], ShelfLibraryService.Search(state, "project", fixture.First.Id).Select(item => item.Id));
        Assert.Equal(fixture.Items.Select(item => item.Id), ShelfLibraryService.Search(state, "", fixture.Second.Id).Select(item => item.Id));
        Assert.Equal(JsonSerializer.Serialize(fixture.Items), JsonSerializer.Serialize(state.Library.Items));
        Assert.Equal(fixture.Library.Memberships.Where(item => item.CollectionId == fixture.Second.Id), state.Library.Memberships.Where(item => item.CollectionId == fixture.Second.Id));
        Assert.Single(state.Library.Items, item => item.Target.CanonicalId == "org.uninstalled.retained");
    }

    [Fact]
    public async Task Archived_members_keep_their_slot_and_restoration_does_not_duplicate_or_drop_targets()
    {
        using var paths = new Paths(); var fixture = Seed(); var service = new ShelfLibraryService(new VersionedAtomicSettingsStore(paths));
        Assert.True((await service.ImportAsync(0, new(fixture.Library, [fixture.Items[1].Id], []))).Success);
        Assert.True((await service.ReorderCollectionAsync(1, fixture.First.Id, [fixture.Items[2].Id, fixture.Items[0].Id])).Success);
        var state = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
        Assert.Equal([fixture.Items[2].Id, fixture.Items[0].Id], ShelfLibraryService.Search(state, "", fixture.First.Id).Select(item => item.Id));
        Assert.Equal([fixture.Items[2].Id, fixture.Items[1].Id, fixture.Items[0].Id], ShelfLibraryPolicy.ResolveCollection(state.Library, fixture.First.Id).Select(item => item.Id));
        Assert.Equal(6, state.Library.Memberships.Count); Assert.Equal(3, state.Library.Items.Count);
        Assert.True((await service.SetItemArchivedAsync(2, fixture.Items[1].Id, false)).Success);
        state = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
        Assert.Equal([fixture.Items[2].Id, fixture.Items[1].Id, fixture.Items[0].Id], ShelfLibraryService.Search(state, "", fixture.First.Id).Select(item => item.Id));
    }

    [Fact]
    public async Task Incomplete_duplicate_foreign_archived_and_stale_orders_preserve_actual_store()
    {
        using var paths = new Paths(); var fixture = Seed(); var service = new ShelfLibraryService(new VersionedAtomicSettingsStore(paths));
        Assert.True((await service.ImportAsync(0, new(fixture.Library, [fixture.Items[1].Id], []))).Success);
        var before = JsonSerializer.Serialize(await service.ReadAsync());
        foreach (var order in new Guid[][] { [fixture.Items[0].Id], [fixture.Items[0].Id, fixture.Items[0].Id],
                     [fixture.Items[0].Id, Guid.NewGuid()], [fixture.Items[0].Id, fixture.Items[1].Id, fixture.Items[2].Id] })
        {
            var denied = await service.ReorderCollectionAsync(1, fixture.First.Id, order);
            Assert.False(denied.Success); Assert.Equal("InvalidArgument", denied.ErrorCode);
            Assert.Equal(before, JsonSerializer.Serialize(await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync()));
        }
        var stale = await service.ReorderCollectionAsync(0, fixture.First.Id, [fixture.Items[2].Id, fixture.Items[0].Id]);
        Assert.False(stale.Success); Assert.Equal("RevisionConflict", stale.ErrorCode); Assert.Equal(before, JsonSerializer.Serialize(await service.ReadAsync()));
        Assert.True((await service.SetCollectionArchivedAsync(1, fixture.First.Id, true)).Success);
        before = JsonSerializer.Serialize(await service.ReadAsync());
        var archived = await service.ReorderCollectionAsync(2, fixture.First.Id, [fixture.Items[2].Id, fixture.Items[0].Id]);
        Assert.False(archived.Success); Assert.Equal("InvalidOperation", archived.ErrorCode); Assert.Equal(before, JsonSerializer.Serialize(await service.ReadAsync()));
    }

    [Fact]
    public async Task Caller_order_is_captured_before_actual_storage_wait()
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var fixture = Seed(); Assert.True((await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ImportAsync(0, new(fixture.Library, [], []), token)).Success);
        var barrier = new ExportBarrier(1); var service = new ShelfLibraryService(new HeldStore(new VersionedAtomicSettingsStore(paths), barrier));
        var order = new List<Guid> { fixture.Items[2].Id, fixture.Items[0].Id, fixture.Items[1].Id };
        var pending = service.ReorderCollectionAsync(1, fixture.First.Id, order, token);
        await barrier.Entered.Task.WaitAsync(token); order.Reverse(); barrier.Release.TrySetResult();
        Assert.True((await pending).Success);
        var actual = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync(token);
        Assert.Equal([fixture.Items[2].Id, fixture.Items[0].Id, fixture.Items[1].Id], ShelfLibraryService.Search(actual, "", fixture.First.Id).Select(item => item.Id));
    }

    [Fact]
    public async Task Independent_actual_stores_preparing_from_one_revision_commit_only_one_order()
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var fixture = Seed(); Assert.True((await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ImportAsync(0, new(fixture.Library, [], []), token)).Success);
        var barrier = new ExportBarrier(2);
        var first = new ShelfLibraryService(new HeldStore(new VersionedAtomicSettingsStore(paths), barrier));
        var second = new ShelfLibraryService(new HeldStore(new VersionedAtomicSettingsStore(paths), barrier));
        var one = first.ReorderCollectionAsync(1, fixture.First.Id, [fixture.Items[2].Id, fixture.Items[0].Id, fixture.Items[1].Id], token);
        var two = second.ReorderCollectionAsync(1, fixture.First.Id, [fixture.Items[1].Id, fixture.Items[2].Id, fixture.Items[0].Id], token);
        await barrier.Entered.Task.WaitAsync(token); barrier.Release.TrySetResult(); var results = await Task.WhenAll(one, two);
        var winner = Assert.Single(results, result => result.Success); Assert.Single(results, result => result.ErrorCode == "RevisionConflict");
        var actual = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync(token);
        Assert.Equal(2, actual.Library.Revision); Assert.Equal(JsonSerializer.Serialize(winner.Snapshot), JsonSerializer.Serialize(actual));
        Assert.Equal(fixture.Library.Memberships.Where(item => item.CollectionId == fixture.Second.Id), actual.Library.Memberships.Where(item => item.CollectionId == fixture.Second.Id));
    }

    [Fact]
    public async Task Membership_removed_while_reorder_waits_is_not_resurrected()
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var fixture = Seed(); var peer = new ShelfLibraryService(new VersionedAtomicSettingsStore(paths));
        Assert.True((await peer.ImportAsync(0, new(fixture.Library, [], []), token)).Success);
        var barrier = new ExportBarrier(1); var waiting = new ShelfLibraryService(new HeldStore(new VersionedAtomicSettingsStore(paths), barrier));
        var pending = waiting.ReorderCollectionAsync(1, fixture.First.Id, [fixture.Items[2].Id, fixture.Items[0].Id, fixture.Items[1].Id], token);
        await barrier.Entered.Task.WaitAsync(token);
        Assert.True((await peer.RemoveMembershipAsync(1, fixture.First.Id, fixture.Items[1].Id, token)).Success);
        var saved = JsonSerializer.Serialize(await peer.ReadAsync(token)); barrier.Release.TrySetResult();
        var denied = await pending; Assert.False(denied.Success); Assert.Equal("RevisionConflict", denied.ErrorCode);
        var actual = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync(token);
        Assert.Equal(saved, JsonSerializer.Serialize(actual));
        Assert.DoesNotContain(actual.Library.Memberships, member => member.CollectionId == fixture.First.Id && member.LaunchItemId == fixture.Items[1].Id);
        Assert.Contains(actual.Library.Memberships, member => member.CollectionId == fixture.Second.Id && member.LaunchItemId == fixture.Items[1].Id);
    }

    [Fact]
    public async Task Cancelling_a_prepared_reorder_preserves_actual_collection_order()
    {
        using var paths = new Paths(); var fixture = Seed(); var peer = new ShelfLibraryService(new VersionedAtomicSettingsStore(paths));
        Assert.True((await peer.ImportAsync(0, new(fixture.Library, [], []))).Success); var saved = JsonSerializer.Serialize(await peer.ReadAsync());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var barrier = new ExportBarrier(1); var waiting = new ShelfLibraryService(new HeldStore(new VersionedAtomicSettingsStore(paths), barrier));
        var pending = waiting.ReorderCollectionAsync(1, fixture.First.Id, [fixture.Items[2].Id, fixture.Items[0].Id, fixture.Items[1].Id], cancellation.Token);
        await barrier.Entered.Task.WaitAsync(cancellation.Token); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(saved, JsonSerializer.Serialize(await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync()));
    }

    private static (ShelfLibrary Library, ShelfCollection First, ShelfCollection Second, ShelfLaunchItem[] Items) Seed()
    {
        var items = new[] { new ShelfLaunchItem(Guid.NewGuid(), "First project", new(ShelfTargetKind.Project, Guid.NewGuid().ToString("D")), Tags: ["course"], IsFavourite: true, Order: 0),
            new ShelfLaunchItem(Guid.NewGuid(), "Unavailable app", new(ShelfTargetKind.InstalledApplication, "org.uninstalled.retained"), Order: 10),
            new ShelfLaunchItem(Guid.NewGuid(), "Second project", new(ShelfTargetKind.Project, Guid.NewGuid().ToString("D")), Order: 20) };
        var first = new ShelfCollection(Guid.NewGuid(), "Work", ShelfCollectionKind.Manual); var second = new ShelfCollection(Guid.NewGuid(), "Study", ShelfCollectionKind.Manual);
        return (ShelfLibrary.Empty with { Items = items, Collections = [first, second], Memberships = items.SelectMany((item, index) => new[] {
            new ShelfCollectionMembership(first.Id, item.Id, (index + 1) * 10), new ShelfCollectionMembership(second.Id, item.Id, (index + 1) * 5) }).ToArray() }, first, second, items);
    }
    private sealed class ExportBarrier(int count)
    {
        private int _entered;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken token)
        { if (Interlocked.Increment(ref _entered) == count) Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
    }
    private sealed class HeldStore(VersionedAtomicSettingsStore inner, ExportBarrier barrier) : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token) => inner.CompareExchangeAsync(key, expectedJson, replacementJson, token);
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        { var snapshot = await inner.ExportAsync(token); await barrier.WaitAsync(token); return snapshot; }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("astra-shelf-order-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
