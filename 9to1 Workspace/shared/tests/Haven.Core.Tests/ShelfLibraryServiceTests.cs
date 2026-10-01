using System.Text.Json;
using Haven.Application;
using Haven.Application.Go;
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

    [Fact]
    public async Task Caller_mutation_during_storage_wait_cannot_change_saved_target_or_tags()
    {
        var store = new MemorySettings { PauseExport = true };
        var tags = new List<string> { "reviewed" };
        var arguments = new List<string> { "reviewed-argument" };
        var item = new ShelfLaunchItem(Guid.NewGuid(), "App",
            new(ShelfTargetKind.InstalledApplication, "real.app", Arguments: arguments), Tags: tags);
        var pending = new ShelfLibraryService(store).AddItemAsync(0, item);
        await store.ExportEntered.Task;
        tags[0] = "changed-after-call";
        arguments[0] = "changed-after-call";
        store.ExportRelease.SetResult();
        Assert.True((await pending).Success);
        var saved = Assert.Single((await new ShelfLibraryService(store).ReadAsync()).Library.Items);
        Assert.Equal("reviewed", Assert.Single(saved.Tags!));
        Assert.Equal("reviewed-argument", Assert.Single(saved.Target.Arguments!));
    }

    [Theory]
    [InlineData("os.installed-applications")]
    [InlineData("android.installed-applications")]
    public async Task Installed_app_activation_preserves_owner_result_and_rechecks_current_owner(string providerID)
    {
        var library = new ShelfLibraryService(new MemorySettings());
        var provider = new InstalledProvider(providerID);
        var adapter = new ShelfInstalledApplicationActivation(library, new GoService([provider]), providerID);
        var discovered = new List<GoResult>();
        await foreach (var update in adapter.DiscoverAsync("Editor")) if (update.Result is { } item) discovered.Add(item);
        var original = Assert.Single(discovered);
        var itemToSave = adapter.CreateItem(original);
        Assert.True((await library.AddItemAsync(0, itemToSave)).Success);
        Assert.Equal("RevisionConflict", (await adapter.ActivateAsync(itemToSave.Id, 0, original)).Code);
        Assert.Equal(0, provider.Activations);
        var activated = await adapter.ActivateAsync(itemToSave.Id, 1, original);
        Assert.True(activated.Requested); Assert.Equal("ActivationRequested", activated.Code);
        Assert.Equal(original.Reference, provider.LastReference); Assert.Equal(1, provider.Activations);
        provider.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.ActivateAsync(itemToSave.Id, 1, original));
        provider.Allowed = true; provider.Revision = "2";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.ActivateAsync(itemToSave.Id, 1, original));
        Assert.Equal(1, provider.Activations);
    }

    [Theory]
    [InlineData("arguments", "ActivationCapabilityUnavailable")]
    [InlineData("working", "ActivationCapabilityUnavailable")]
    [InlineData("openwith", "ActivationCapabilityUnavailable")]
    [InlineData("archive", "ItemUnavailable")]
    [InlineData("different", "TargetMismatch")]
    public async Task Installed_activation_rejects_unavailable_Shelf_target_options(string change, string code)
    {
        var library = new ShelfLibraryService(new MemorySettings()); var provider = new InstalledProvider("os.installed-applications");
        var adapter = new ShelfInstalledApplicationActivation(library, new GoService([provider]), provider.ProviderId);
        var original = provider.Result(); var item = adapter.CreateItem(original);
        item = change switch
        {
            "arguments" => item with { Target = item.Target with { Arguments = ["unreviewed"] } },
            "working" => item with { Target = item.Target with { WorkingDirectory = "/tmp" } },
            "openwith" => item with { Behaviour = ShelfLaunchBehaviour.OpenWith },
            "different" => item with { Target = item.Target with { CanonicalId = Guid.NewGuid().ToString("D") } },
            _ => item
        };
        Assert.True((await library.AddItemAsync(0, item)).Success);
        long revision = 1;
        if (change == "archive") { Assert.True((await library.SetItemArchivedAsync(1, item.Id, true)).Success); revision = 2; }
        var result = await adapter.ActivateAsync(item.Id, revision, original);
        Assert.False(result.Requested); Assert.Equal(code, result.Code); Assert.Equal(0, provider.Activations);
        Assert.Throws<ArgumentException>(() => adapter.CreateItem(original with { ProviderId = "untrusted" }));
    }

    private sealed class InstalledProvider(string providerID) : IGoProvider
    {
        public string ProviderId => providerID;
        public Guid ID { get; } = Guid.NewGuid();
        public string Revision { get; set; } = "1";
        public bool Allowed { get; set; } = true;
        public int Activations { get; private set; }
        public GoCanonicalReference? LastReference { get; private set; }
        public GoResult Result() => new(ProviderId, new("Home", "os.installed-application", ID.ToString("D"), Revision), "Editor", "Apps", [new("Open", "Open")]);
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield(); token.ThrowIfCancellationRequested();
            Assert.Equal("Apps", query.Category); Assert.Contains(ProviderId, query.Scope!.ProviderIds!);
            if (Allowed) yield return Result();
        }
        public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Allowed || reference != Result().Reference || actionId != "Open") throw new UnauthorizedAccessException();
            LastReference = reference; Activations++; return Task.CompletedTask;
        }
    }

    private sealed class MemorySettings : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly Dictionary<string, string> _values = new();
        public bool PauseExport { get; init; }
        public TaskCompletionSource ExportEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExportRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        {
            if (PauseExport)
            {
                ExportEntered.TrySetResult();
                await ExportRelease.Task.WaitAsync(token);
            }
            return new SettingsExportManifest { Settings = new(_values) };
        }
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
}
