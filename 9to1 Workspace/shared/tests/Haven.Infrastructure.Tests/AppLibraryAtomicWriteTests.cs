using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;

namespace Haven.Infrastructure.Tests;

public sealed class AppLibraryAtomicWriteTests
{
    [Fact]
    public async Task Independent_maps_instances_conflict_when_both_prepare_from_same_disk_revision()
    {
        using var paths = new Paths();
        var barrier = new Barrier();
        var first = new MapsJourneyService(new PreparedStore(new VersionedAtomicSettingsStore(paths), barrier));
        var second = new MapsJourneyService(new PreparedStore(new VersionedAtomicSettingsStore(paths), barrier));
        var results = await Task.WhenAll(
            first.CreateLandmarkAsync(0, Guid.NewGuid(), "First", new(54, -1)),
            second.CreateLandmarkAsync(0, Guid.NewGuid(), "Second", new(55, -2)));
        Assert.Single(results, item => item.Success);
        Assert.Single(results, item => item.ErrorCode == "RevisionConflict");
        var restored = await new MapsJourneyService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
        Assert.Equal(1, restored.Revision);
        Assert.Single(restored.Places);
    }

    [Fact]
    public async Task Independent_shelf_instances_conflict_and_preserve_the_winning_collection_on_restart()
    {
        using var paths = new Paths();
        var barrier = new Barrier();
        var first = new ShelfLibraryService(new PreparedStore(new VersionedAtomicSettingsStore(paths), barrier));
        var second = new ShelfLibraryService(new PreparedStore(new VersionedAtomicSettingsStore(paths), barrier));
        var results = await Task.WhenAll(
            first.CreateCollectionAsync(0, new(Guid.NewGuid(), "First", ShelfCollectionKind.Manual)),
            second.CreateCollectionAsync(0, new(Guid.NewGuid(), "Second", ShelfCollectionKind.Manual)));
        Assert.Single(results, item => item.Success);
        Assert.Single(results, item => item.ErrorCode == "RevisionConflict");
        var restored = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
        Assert.Equal(1, restored.Library.Revision);
        Assert.Equal(results.Single(item => item.Success).Snapshot!.Library.Collections.Single().Id,
            Assert.Single(restored.Library.Collections).Id);
    }

    private sealed class Barrier
    {
        private int _prepared;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _prepared) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        }
    }
    private sealed class PreparedStore(VersionedAtomicSettingsStore inner, Barrier barrier)
        : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        {
            var snapshot = await inner.ExportAsync(token);
            await barrier.WaitAsync(token);
            return snapshot;
        }
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token)
            => inner.CompareExchangeAsync(key, expectedJson, replacementJson, token);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("app-library-cas-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
