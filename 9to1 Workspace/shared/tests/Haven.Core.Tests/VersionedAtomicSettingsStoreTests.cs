using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class VersionedAtomicSettingsStoreTests
{
    private sealed record Value(string Name);

    [Fact]
    public async Task Exact_key_cas_reloads_real_disk_and_conflict_never_changes_settings()
    {
        using var paths = new TestPaths();
        var first = new VersionedAtomicSettingsStore(paths);
        var second = new VersionedAtomicSettingsStore(paths);
        await first.SetAsync("form", new Value("draft"), default);
        Assert.Equal("draft", (await second.GetAsync<Value>("form", default))!.Name);
        var expected = JsonSerializer.Serialize(new Value("draft"));
        var replacement = JsonSerializer.Serialize(new Value("published"));
        Assert.True((await first.CompareExchangeAsync("form", expected, replacement, default)).Exchanged);
        var path = Path.Combine(paths.DataDirectory, "settings.json");
        var bytes = await File.ReadAllBytesAsync(path);
        var stale = await second.CompareExchangeAsync("form", expected, JsonSerializer.Serialize(new Value("lost update")), default);
        Assert.False(stale.Exchanged); Assert.Equal(replacement, stale.CurrentJson);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await second.SetAsync("different-key", new Value("other"), default);
        Assert.Equal("published", (await first.GetAsync<Value>("form", default))!.Name);
        Assert.Equal("other", (await first.GetAsync<Value>("different-key", default))!.Name);
        Assert.True((await second.CompareExchangeAsync("form", replacement, null, default)).Exchanged);
        Assert.Null(await first.GetAsync<Value>("form", default));
    }

    [Fact]
    public async Task Two_real_store_instances_concurrently_create_one_key_without_overwrite()
    {
        using var paths = new TestPaths();
        var first = new VersionedAtomicSettingsStore(paths);
        var second = new VersionedAtomicSettingsStore(paths);
        var results = await Task.WhenAll(first.CompareExchangeAsync("form", null, JsonSerializer.Serialize(new Value("one")), default),
            second.CompareExchangeAsync("form", null, JsonSerializer.Serialize(new Value("two")), default));
        Assert.Single(results, result => result.Exchanged);
        var manifest = await first.ExportAsync(default);
        Assert.Single(manifest.Settings);
        Assert.Equal(1, manifest.Version);
    }

    [Fact]
    public async Task Actual_store_uuid_reopens_and_import_cannot_replace_its_owner_identity()
    {
        using var paths = new TestPaths();
        var store = new VersionedAtomicSettingsStore(paths);
        var first = await store.GetStoreIdentityAsync(default);
        Assert.True(first.NewlyCreated);
        var reopened = new VersionedAtomicSettingsStore(paths);
        var identity = await reopened.GetStoreIdentityAsync(default);
        Assert.Equal(first.StoreId, identity.StoreId);
        Assert.False(identity.NewlyCreated);
        Assert.True((await reopened.ImportAsync(new SettingsExportManifest
        {
            StoreIdentity = new(1, Guid.NewGuid(), DateTimeOffset.UtcNow),
            Settings = new() { ["imported"] = JsonSerializer.Serialize(new Value("data")) }
        }, default)).Succeeded);
        Assert.Equal(first.StoreId, (await new VersionedAtomicSettingsStore(paths).GetStoreIdentityAsync(default)).StoreId);
        Assert.Equal("data", (await reopened.GetAsync<Value>("imported", default))!.Name);
        var snapshot = await reopened.ExportAsync(default);
        Assert.Equal(first.StoreId, snapshot.StoreIdentity!.StoreId);
        Assert.Equal("data", JsonSerializer.Deserialize<Value>(snapshot.Settings["imported"])!.Name);
    }

    [Fact]
    public async Task Legacy_development_data_receives_identity_without_an_ownership_grant_and_future_identity_is_preserved()
    {
        using var paths = new TestPaths();
        var path = Path.Combine(paths.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new SettingsExportManifest
        { Version = 3, Settings = new() { ["legacy"] = JsonSerializer.Serialize(new Value("preserved")) } }));
        var store = new VersionedAtomicSettingsStore(paths);
        Assert.False((await store.GetStoreIdentityAsync(default)).NewlyCreated);
        Assert.Equal("preserved", (await store.GetAsync<Value>("legacy", default))!.Name);
        var future = JsonSerializer.Serialize(new SettingsExportManifest
        { StoreIdentity = new(999, Guid.NewGuid(), DateTimeOffset.UtcNow), Settings = new() });
        await File.WriteAllTextAsync(path, future);
        await Assert.ThrowsAsync<NotSupportedException>(() => new VersionedAtomicSettingsStore(paths).GetStoreIdentityAsync(default).AsTask());
        Assert.Equal(future, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Unknown_schema_is_preserved_and_legacy_shape_migrates_on_next_save()
    {
        using var paths = new TestPaths();
        var path = Path.Combine(paths.DataDirectory, "settings.json");
        const string future = "{\"SchemaVersion\":999,\"Version\":2,\"Settings\":{}}";
        await File.WriteAllTextAsync(path, future);
        await Assert.ThrowsAsync<NotSupportedException>(() => new VersionedAtomicSettingsStore(paths).GetAsync<Value>("value", default));
        Assert.Equal(future, await File.ReadAllTextAsync(path));
        await File.WriteAllTextAsync(path, "{\"Version\":3,\"Settings\":{}}");
        await new VersionedAtomicSettingsStore(paths).SetAsync("value", new Value("saved"), default);
        var manifest = JsonSerializer.Deserialize<SettingsExportManifest>(await File.ReadAllTextAsync(path))!;
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(4, manifest.Version);
    }

    [Fact]
    public async Task Corrupt_primary_recovers_backup_and_preserves_corrupt_evidence()
    {
        using var paths = new TestPaths();
        var store = new VersionedAtomicSettingsStore(paths);
        await store.SetAsync("value", new Value("first"), default);
        await store.SetAsync("value", new Value("second"), default);
        var path = Path.Combine(paths.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(path, "broken JSON");
        store = new VersionedAtomicSettingsStore(paths);
        Assert.Equal("first", (await store.GetAsync<Value>("value", default))!.Name);
        await store.SetAsync("value", new Value("recovered"), default);
        Assert.Single(Directory.GetFiles(paths.DataDirectory, "settings.json.corrupt.*"));
        var backup = JsonSerializer.Deserialize<SettingsExportManifest>(await File.ReadAllTextAsync(path + ".bak"))!;
        Assert.Contains("first", backup.Settings["value"]);
        Assert.Equal("recovered", (await new VersionedAtomicSettingsStore(paths).GetAsync<Value>("value", default))!.Name);
    }

    [Fact]
    public async Task Unrecoverable_corruption_is_not_empty_state_and_cannot_be_overwritten()
    {
        using var paths = new TestPaths();
        var path = Path.Combine(paths.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(path, "broken JSON");
        var store = new VersionedAtomicSettingsStore(paths);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync<Value>("value", default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SetAsync("value", new Value("replacement"), default));
        Assert.Equal("broken JSON", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Failed_disk_commit_does_not_publish_candidate_to_readers()
    {
        using var paths = new TestPaths();
        var store = new VersionedAtomicSettingsStore(paths);
        await store.SetAsync("value", new Value("saved"), default);
        var path = Path.Combine(paths.DataDirectory, "settings.json");
        var savedBytes = await File.ReadAllBytesAsync(path);
        // A directory at the file replacement target deterministically rejects access.
        File.Delete(path);
        Directory.CreateDirectory(path);
        await Assert.ThrowsAsync<IOException>(() => store.SetAsync("value", new Value("unsaved"), default));
        await Assert.ThrowsAsync<IOException>(() => store.GetAsync<Value>("value", default));
        Directory.Delete(path);
        await File.WriteAllBytesAsync(path, savedBytes);
        Assert.Equal("saved", (await store.GetAsync<Value>("value", default))!.Name);
        Assert.Empty(Directory.GetFiles(paths.DataDirectory, "*.tmp"));
    }

    [Fact]
    public async Task Import_loads_existing_entries_and_validates_whole_operation_before_commit()
    {
        using var paths = new TestPaths();
        await new VersionedAtomicSettingsStore(paths).SetAsync("existing", new Value("saved"), default);
        var store = new VersionedAtomicSettingsStore(paths);
        var import = new SettingsExportManifest { Settings = new() { ["new"] = JsonSerializer.Serialize(new Value("new")) } };
        Assert.True((await store.ImportAsync(import, default)).Succeeded);
        Assert.Equal("saved", (await store.GetAsync<Value>("existing", default))!.Name);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(new SettingsExportManifest
        { Settings = new() { ["existing"] = "bad" } }, default));
        Assert.Equal("saved", (await store.GetAsync<Value>("existing", default))!.Name);
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-settings-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public TestPaths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
