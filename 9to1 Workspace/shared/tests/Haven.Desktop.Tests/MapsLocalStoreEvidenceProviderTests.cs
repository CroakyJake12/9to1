using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class MapsLocalStoreEvidenceProviderTests
{
    [Fact]
    public async Task Real_root_identity_and_original_empty_domain_are_observed_before_default_creation()
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var identity = await settings.GetStoreIdentityAsync(TestContext.Current.CancellationToken);
        var provider = new MapsLocalStoreEvidenceProvider(settings, settings);
        var initial = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
        Assert.NotNull(initial);
        Assert.True(initial.NewlyCreated);
        Assert.True(initial.IsEmpty);
        Assert.True(initial.AccessibleToCurrentOsPrincipal);
        Assert.Equal("maps", initial.ResourceKind);
        Assert.Null(await provider.ReadAsync(Guid.NewGuid().ToString("D"), TestContext.Current.CancellationToken));
        await settings.SetAsync("spaces.unrelated.v1", new { Revision = 1 }, TestContext.Current.CancellationToken);
        var unrelated = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
        Assert.True(unrelated!.IsEmpty);
        Assert.Equal(initial.Revision, unrelated.Revision);
    }

    [Fact]
    public async Task Existing_maps_domain_after_restart_is_not_new_empty_even_if_only_defaults_or_unknown_keys_remain()
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var identity = await settings.GetStoreIdentityAsync(TestContext.Current.CancellationToken);
        var provider = new MapsLocalStoreEvidenceProvider(settings, settings);
        var before = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
        await settings.SetAsync("maps.journeys.v1", MapsJourneyLibrary.Empty, TestContext.Current.CancellationToken);
        var existing = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
        Assert.False(existing!.IsEmpty);
        Assert.NotEqual(before!.Revision, existing.Revision);
        settings = new VersionedAtomicSettingsStore(paths);
        provider = new(settings, settings);
        var restored = await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken);
        Assert.False(restored!.NewlyCreated);
        Assert.False(restored.IsEmpty);
        Assert.Equal(existing.StoreId, restored.StoreId);
        Assert.Equal(existing.Revision, restored.Revision);
        await settings.RemoveAsync("maps.journeys.v1", TestContext.Current.CancellationToken);
        await settings.SetAsync("maps.unknown.future", new { SchemaVersion = 999 }, TestContext.Current.CancellationToken);
        Assert.False((await provider.ReadAsync(identity.StoreId.ToString("D"), TestContext.Current.CancellationToken))!.IsEmpty);
    }

    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("maps-owner-evidence-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
