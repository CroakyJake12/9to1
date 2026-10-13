using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class SpaceLocalStoreEvidenceProviderTests
{
    [Fact]
    public async Task Absent_identity_cannot_be_created_by_claiming_a_store_id()
    {
        using var fixture = new Fixture();
        var settings = new VersionedAtomicSettingsStore(fixture);
        var evidence = new SpaceLocalStoreEvidenceProvider(settings);
        Assert.Null(await evidence.ReadAsync(Guid.NewGuid().ToString("D"), default));
        Assert.Null((await settings.ExportAsync(default)).StoreIdentity);
        Assert.False(File.Exists(Path.Combine(fixture.DataDirectory, "settings.json")));
    }

    [Fact]
    public async Task Actual_store_identity_and_contents_are_observed_without_implicit_ownership()
    {
        using var fixture = new Fixture();
        var settings = new VersionedAtomicSettingsStore(fixture);
        var identity = await settings.GetStoreIdentityAsync(default);
        var evidence = new SpaceLocalStoreEvidenceProvider(settings);
        var observed = await evidence.ReadAsync(identity.StoreId.ToString("D"), default);
        Assert.NotNull(observed);
        Assert.Equal("spaces", observed.ResourceKind);
        Assert.True(observed.NewlyCreated);
        Assert.True(observed.IsEmpty);
        Assert.True(observed.AccessibleToCurrentOsPrincipal);
        Assert.True(evidence.IsBoundToOriginalSettings(settings));
        Assert.Null(await evidence.ReadAsync(Guid.NewGuid().ToString("D"), default));

        await settings.SetAsync("unrelated-existing-settings", new { value = "keep" }, default);
        var populated = await evidence.ReadAsync(identity.StoreId.ToString("D"), default);
        Assert.NotNull(populated);
        Assert.False(populated.IsEmpty); // No spaces.registry does not mean the owning store is empty.
        Assert.NotEqual(observed.Revision, populated.Revision);
        var reopened = new VersionedAtomicSettingsStore(fixture);
        Assert.False(evidence.IsBoundToOriginalSettings(reopened));
        var afterRestart = await new SpaceLocalStoreEvidenceProvider(reopened).ReadAsync(identity.StoreId.ToString("D"), default);
        Assert.NotNull(afterRestart);
        Assert.False(afterRestart.NewlyCreated);
        Assert.False(afterRestart.IsEmpty);
        Assert.Single((await reopened.ExportAsync(default)).Settings);
    }

    [Fact]
    public async Task Corrupt_unrecoverable_store_is_not_reported_accessible_or_reset()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(path, "invalid settings bytes");
        var provider = new SpaceLocalStoreEvidenceProvider(new VersionedAtomicSettingsStore(fixture));
        Assert.Null(await provider.ReadAsync(Guid.NewGuid().ToString("D"), default));
        Assert.Equal("invalid settings bytes", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Real_home_receipt_guards_original_registry_and_restart_requires_fresh_actor()
    {
        using var fixture = new Fixture();
        var settings = new VersionedAtomicSettingsStore(fixture);
        var identity = await settings.GetStoreIdentityAsync(default);
        var home = new FileHomeCoreStateStore(Path.Combine(fixture.DataDirectory, "Home", "state.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var evidence = new HomeLocalStoreEvidenceRegistry([new SpaceLocalStoreEvidenceProvider(settings)]);
        var permissions = new HomePermissionTrustService(home, (_, _) => null);
        var localOwnership = new HomeLocalStoreOwnership(home, profiles, evidence, permissions);
        var receipts = new HomeResourceStoreOwnershipAuthority(localOwnership, profiles);
        var allowed = true;
        var authority = new SpaceLocalStoreAuthority(settings, profiles, receipts, () => allowed);
        var registry = new SpaceRegistry(settings, authority.CaptureWriteAdmissionAsync);
        Assert.True(authority.IsBoundToOriginalComposition(settings, profiles, receipts));
        Assert.False(authority.IsBoundToOriginalComposition(new VersionedAtomicSettingsStore(fixture), profiles, receipts));
        Assert.False(authority.IsBoundToOriginalComposition(settings,
            new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource()), receipts));
        Assert.False(authority.IsBoundToOriginalComposition(settings, profiles,
            new HomeResourceStoreOwnershipAuthority(localOwnership, profiles)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.CreateAsync("Unbound"));
        Assert.Empty((await settings.ExportAsync(default)).Settings);
        var actor = await profiles.GetCurrentAsync(default);
        Assert.NotNull(actor);
        // Explicit existing Home setup action; provider registration granted nothing.
        await localOwnership.BindNewEmptyAsync(actor, "spaces", identity.StoreId.ToString("D"));
        var created = await registry.CreateAsync("Bound Space");
        allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.RenameAsync(created.Id, "Denied edit"));
        Assert.Equal("Bound Space", (await registry.ReadExistingAsync(created.Id))!.Name);

        var reopened = new VersionedAtomicSettingsStore(fixture);
        var restartedHome = new FileHomeCoreStateStore(Path.Combine(fixture.DataDirectory, "Home", "state.json"));
        var restartedProfiles = new HomeLocalProfileIdentity(restartedHome, new OperatingSystemPrincipalSource());
        var restartedOwnership = new HomeLocalStoreOwnership(restartedHome, restartedProfiles,
            new HomeLocalStoreEvidenceRegistry([new SpaceLocalStoreEvidenceProvider(reopened)]),
            new HomePermissionTrustService(restartedHome, (_, _) => null));
        var restartedAuthority = new SpaceLocalStoreAuthority(reopened, restartedProfiles,
            new HomeResourceStoreOwnershipAuthority(restartedOwnership, restartedProfiles), () => true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            restartedAuthority.CaptureWriteAdmissionForActorAsync(actor).AsTask());
        var restartedRegistry = new SpaceRegistry(reopened, restartedAuthority.CaptureWriteAdmissionAsync);
        var revised = await restartedRegistry.RenameAsync(created.Id, "Fresh actor edit");
        Assert.Equal(created.Id, revised.Id);
        Assert.Equal(created.Revision + 1, revised.Revision);
        Assert.Equal("Fresh actor edit", (await registry.ReadExistingAsync(created.Id))!.Name);
    }

    private sealed class Fixture : IDisposable, IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "spaces-evidence-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
