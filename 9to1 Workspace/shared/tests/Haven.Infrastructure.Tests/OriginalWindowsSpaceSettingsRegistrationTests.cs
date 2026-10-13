using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class OriginalWindowsSpaceSettingsRegistrationTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires the original Windows Home composition; no emulated native ownership.";
        }
    }

    [WindowsFact]
    public async Task Original_registration_reuses_one_settings_instance_without_importing_ownership()
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var evidence = new SpaceLocalStoreEvidenceProvider(settings);
        SpaceLocalStoreAuthority? authority = null;
        await using var home = new HomeNativeWindowsComposition(
            new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "Home", "state.json")),
            new OperatingSystemPrincipalSource(), paths, new("9to1.home.spaces.registration-test"),
            configureOriginalStores: _ => new([evidence], new Dictionary<Type, object>
            { [typeof(SpaceLocalStoreEvidenceProvider)] = evidence }),
            configureOriginalResolvers: owner =>
            {
                authority = new(settings, owner.Identity.Profiles, owner.Ownership, () => true);
                return new([], new Dictionary<Type, object> { [typeof(SpaceLocalStoreAuthority)] = authority });
            });
        var services = BaselineServices();
        services.AddOriginalWindowsSpaceSettings(home, settings, evidence, authority!);
        using var provider = services.BuildServiceProvider();
        Assert.Same(settings, provider.GetRequiredService<IVersionedSettingsStore>());
        Assert.Same(settings, provider.GetRequiredService<VersionedAtomicSettingsStore>());
        Assert.Same(evidence, provider.GetRequiredService<SpaceLocalStoreEvidenceProvider>());
        Assert.Same(authority, provider.GetRequiredService<SpaceLocalStoreAuthority>());
        var registry = provider.GetRequiredService<SpaceRegistry>();
        var created = await registry.CreateAsync("Ordinary configuration preserved");
        Assert.Equal(created.Id, (await new SpaceRegistry(settings).GetAsync(created.Id))!.Id);
        var identity = await settings.GetStoreIdentityAsync(default);
        Assert.Null(await home.Ownership.GetVerifiedAsync("spaces", identity.StoreId.ToString("D"), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority!.CaptureWriteAdmissionAsync().AsTask());

        var substituted = BaselineServices();
        var descriptorCount = substituted.Count;
        Assert.Throws<UnauthorizedAccessException>(() => substituted.AddOriginalWindowsSpaceSettings(home,
            new VersionedAtomicSettingsStore(paths), evidence, authority!));
        Assert.Equal(descriptorCount, substituted.Count);
        Assert.Throws<UnauthorizedAccessException>(() => substituted.AddOriginalWindowsSpaceSettings(home,
            settings, new SpaceLocalStoreEvidenceProvider(settings), authority!));
        Assert.Equal(descriptorCount, substituted.Count);
        Assert.Throws<UnauthorizedAccessException>(() => substituted.AddOriginalWindowsSpaceSettings(home,
            settings, evidence, new SpaceLocalStoreAuthority(settings, home.Profiles, home.Ownership, () => true)));
        Assert.Equal(descriptorCount, substituted.Count);

        var customised = new ServiceCollection();
        customised.AddSingleton<IVersionedSettingsStore>(_ => settings);
        Assert.Throws<InvalidOperationException>(() => customised.AddOriginalWindowsSpaceSettings(home, settings, evidence, authority!));
        Assert.Single(customised); // Never silently replace a legitimate alternate producer.
    }

    private static ServiceCollection BaselineServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVersionedSettingsStore, VersionedAtomicSettingsStore>();
        services.AddSingleton<SpaceRegistry>(provider => new(provider.GetRequiredService<IVersionedSettingsStore>()));
        return services;
    }

    private sealed class Paths : IDisposable, IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "space-native-registration-" + Guid.NewGuid().ToString("N"));
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
