using System.Text.Json;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;

namespace Haven.Core.Tests;

public sealed class ShelfLibraryServiceTests
{
    [Fact]
    public async Task Restart_preserves_memberships_and_archive_restore()
    {
        var store = new MemorySettings();
        var service = new ShelfLibraryService(store);
        var item = new ShelfLaunchItem(Guid.NewGuid(), "Project", new(ShelfTargetKind.Project, Guid.NewGuid().ToString()));
        var first = new ShelfCollection(Guid.NewGuid(), "Work", ShelfCollectionKind.Manual);
        var second = new ShelfCollection(Guid.NewGuid(), "Study", ShelfCollectionKind.Manual);
        Assert.True((await service.AddItemAsync(0, item)).Success);
        Assert.True((await service.CreateCollectionAsync(1, first)).Success);
        Assert.True((await service.CreateCollectionAsync(2, second)).Success);
        Assert.True((await service.AddMembershipAsync(3, first.Id, item.Id)).Success);
        Assert.True((await service.AddMembershipAsync(4, second.Id, item.Id)).Success);
        Assert.True((await service.SetItemArchivedAsync(5, item.Id, true)).Success);

        service = new ShelfLibraryService(store);
        var state = await service.ReadAsync();
        Assert.Empty(ShelfLibraryService.Search(state, "Project"));
        Assert.Equal(2, state.Library.Memberships.Count);
        Assert.True((await service.SetItemArchivedAsync(6, item.Id, false)).Success);
        state = await service.ReadAsync();
        Assert.Single(ShelfLibraryService.Search(state, "Project", first.Id));
        Assert.Single(ShelfLibraryService.Search(state, "Project", second.Id));
    }

    [Fact]
    public async Task Simultaneous_surface_changes_conflict_instead_of_overwriting()
    {
        var service = new ShelfLibraryService(new MemorySettings());
        var first = new ShelfCollection(Guid.NewGuid(), "Work", ShelfCollectionKind.Manual);
        var second = new ShelfCollection(Guid.NewGuid(), "Study", ShelfCollectionKind.Manual);
        var results = await Task.WhenAll(service.CreateCollectionAsync(0, first), service.CreateCollectionAsync(0, second));
        Assert.Single(results, result => result.Success);
        Assert.Equal("RevisionConflict", results.Single(result => !result.Success).ErrorCode);
        Assert.Single((await service.ReadAsync()).Library.Collections);
    }

    [Fact]
    public async Task Import_rejects_unknown_schema_and_preserves_state()
    {
        var service = new ShelfLibraryService(new MemorySettings());
        var result = await service.ImportAsync(0, ShelfSnapshot.Empty with { Library = ShelfLibrary.Empty with { SchemaVersion = 999 } });
        Assert.False(result.Success);
        Assert.Equal("InvalidData", result.ErrorCode);
        Assert.Equal(0, (await service.ReadAsync()).Library.Revision);
    }

    [Fact]
    public void Duplicate_membership_with_different_order_is_invalid()
    {
        var item = new ShelfLaunchItem(Guid.NewGuid(), "App", new(ShelfTargetKind.InstalledApplication, "real.app"));
        var collection = new ShelfCollection(Guid.NewGuid(), "Apps", ShelfCollectionKind.Manual);
        var library = ShelfLibrary.Empty with { Items = [item], Collections = [collection],
            Memberships = [new(collection.Id, item.Id, 1), new(collection.Id, item.Id, 2)] };
        Assert.Contains("Shelf contains duplicate collection memberships.", library.Validate());
    }

    private sealed class MemorySettings : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly Dictionary<string, string> _values = new();
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { _values[key] = JsonSerializer.Serialize(value); return Task.CompletedTask; }
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token)
        {
            lock (_values)
            {
                _values.TryGetValue(key, out var current);
                if (current != expectedJson) return Task.FromResult(new SettingsCompareExchangeResult(false, current, 0));
                if (replacementJson is null) _values.Remove(key); else _values[key] = replacementJson;
                return Task.FromResult(new SettingsCompareExchangeResult(true, replacementJson, 0));
            }
        }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => Task.FromResult(new SettingsExportManifest { Settings = new(_values) });
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
}
