using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class SpaceNavigationPreferencesTests
{
    [Fact]
    public async Task Unbound_or_retired_home_cannot_read_or_write_navigation_preferences()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Preferences.ReadAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Preferences.SetLayoutAsync(SpaceSwitcherLayout.Dropdown, 0));
        Assert.Empty((await fixture.Settings.ExportAsync(default)).Settings);
        await fixture.BindAsync();
        var initial = await fixture.Preferences.ReadAsync();
        Assert.Equal(SpaceSwitcherLayout.Rail, initial.Layout); Assert.Empty(initial.PinnedSpaceIds);
        fixture.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Preferences.SetLayoutAsync(SpaceSwitcherLayout.Dropdown, initial.Revision));
        Assert.Empty((await fixture.Settings.ExportAsync(default)).Settings);
    }

    [Fact]
    public async Task Real_home_owned_pins_order_layout_and_per_space_collapse_survive_restart_without_switching_space()
    {
        using var fixture = new Fixture(); await fixture.BindAsync();
        var registry = new SpaceRegistry(fixture.Settings, fixture.Authority.CaptureWriteAdmissionAsync);
        var first = await registry.CreateAsync("First"); var second = await registry.CreateAsync("Second");
        await registry.SetCurrentSpaceIdAsync(first.Id);
        var value = await fixture.Preferences.ReadAsync();
        value = await fixture.Preferences.SetPinnedAsync(first.Id, true, value.Revision);
        value = await fixture.Preferences.SetPinnedAsync(second.Id, true, value.Revision);
        value = await fixture.Preferences.MovePinAsync(second.Id, 0, value.Revision);
        value = await fixture.Preferences.SetLayoutAsync(SpaceSwitcherLayout.Dropdown, value.Revision);
        value = await fixture.Preferences.SetCollapsedAsync(first.Id, true, value.Revision);
        Assert.Equal(first.Id, await registry.GetCurrentSpaceIdAsync());
        Assert.False(value.IsCollapsed(second.Id)); Assert.True(value.IsCollapsed(first.Id));
        var reopened = fixture.Reopen();
        var restored = await reopened.ReadAsync();
        Assert.Equal(value.Revision, restored.Revision); Assert.Equal(SpaceSwitcherLayout.Dropdown, restored.Layout);
        Assert.Equal(new[] { second.Id, first.Id }, restored.PinnedSpaceIds); Assert.True(restored.IsCollapsed(first.Id));
        var nameChanged = await registry.RenameAsync(first.Id, "Renamed");
        Assert.Equal(first.Id, nameChanged.Id);
        Assert.Equal(restored.PinnedSpaceIds, (await fixture.Preferences.ReadAsync()).PinnedSpaceIds);
    }

    [Fact]
    public async Task Stale_editor_cannot_overwrite_another_instances_saved_preferences()
    {
        using var fixture = new Fixture(); await fixture.BindAsync();
        var value = await fixture.Preferences.ReadAsync(); var second = fixture.Reopen();
        await second.SetLayoutAsync(SpaceSwitcherLayout.Dropdown, value.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Preferences.SetCollapsedAsync(null, true, value.Revision));
        var saved = await fixture.Preferences.ReadAsync();
        Assert.Equal(SpaceSwitcherLayout.Dropdown, saved.Layout); Assert.False(saved.IsCollapsed(null));
    }

    [Fact]
    public async Task Archive_preserves_pin_identity_but_does_not_allow_a_new_pin_and_explicit_unpin_is_available()
    {
        using var fixture = new Fixture(); await fixture.BindAsync();
        var registry = new SpaceRegistry(fixture.Settings, fixture.Authority.CaptureWriteAdmissionAsync);
        var space = await registry.CreateAsync("Archive fixture");
        var value = await fixture.Preferences.SetPinnedAsync(space.Id, true, 0);
        await registry.SetArchivedAsync(space.Id, true);
        Assert.Equal(new[] { space.Id }, (await fixture.Preferences.ReadAsync()).PinnedSpaceIds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Preferences.SetPinnedAsync(space.Id, true, value.Revision));
        value = await fixture.Preferences.SetPinnedAsync(space.Id, false, value.Revision);
        Assert.Empty(value.PinnedSpaceIds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Preferences.SetPinnedAsync(Guid.NewGuid(), true, value.Revision));
    }

    private sealed class Fixture : IDisposable, IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "spaces-nav-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public VersionedAtomicSettingsStore Settings { get; }
        public SpaceLocalStoreAuthority Authority { get; }
        public SpaceNavigationPreferences Preferences { get; }
        private readonly HomeLocalProfileIdentity _profiles;
        private readonly HomeLocalStoreOwnership _ownership;
        public bool Allowed = true;
        public Fixture()
        {
            Directory.CreateDirectory(DataDirectory);
            Settings = new(this);
            var home = new FileHomeCoreStateStore(Path.Combine(DataDirectory, "Home", "state.json"));
            _profiles = new(home, new OperatingSystemPrincipalSource());
            _ownership = new(home, _profiles, new HomeLocalStoreEvidenceRegistry([new SpaceLocalStoreEvidenceProvider(Settings)]),
                new HomePermissionTrustService(home, (_, _) => null));
            Authority = new(Settings, _profiles, new HomeResourceStoreOwnershipAuthority(_ownership, _profiles), () => Allowed);
            Preferences = new(Settings, Authority);
        }
        public async Task BindAsync()
        {
            var identity = await Settings.GetStoreIdentityAsync(default);
            var actor = await _profiles.GetCurrentAsync(default);
            await _ownership.BindNewEmptyAsync(actor!, "spaces", identity.StoreId.ToString("D"));
        }
        public SpaceNavigationPreferences Reopen()
        {
            var settings = new VersionedAtomicSettingsStore(this);
            var home = new FileHomeCoreStateStore(Path.Combine(DataDirectory, "Home", "state.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var ownership = new HomeLocalStoreOwnership(home, profiles,
                new HomeLocalStoreEvidenceRegistry([new SpaceLocalStoreEvidenceProvider(settings)]),
                new HomePermissionTrustService(home, (_, _) => null));
            return new(settings, new(settings, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles), () => Allowed));
        }
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
