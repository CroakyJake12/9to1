using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;
public sealed class ShelfLibraryWorkspaceTests
{
    [Fact]
    public async Task Real_manual_target_review_requires_Home_accept_then_reload_keeps_canonical_identity_and_Finish_never_replays()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await ShelfLibraryWorkspace.OpenAsync(fixture.Owner, fixture.Actor);
        var proposed = Item(); var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
        var id = await workspace.ReviewSaveAsync(proposed);
        Assert.Equal("NoAttemptedOutcome", (await workspace.FinishAsync(id)).Code);
        Assert.Equal("ApprovalRequired", (await workspace.ApplyOrRecoverAsync(id)).Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.True((await fixture.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await workspace.ApplyOrRecoverAsync(id)).Committed);
        var committed = await File.ReadAllBytesAsync(fixture.SettingsFile);
        await workspace.ReloadAsync();
        var displayed = Assert.Single(workspace.Snapshot.Library.Items);
        Assert.Equal(proposed.Id, displayed.Id); Assert.Equal(proposed.Target, displayed.Target);
        Assert.Equal(id, Assert.Single(workspace.Reviews).RequestID);
        workspace.Dispose(); Assert.True((await workspace.FinishAsync(id)).Committed);
        Assert.Equal(committed, await File.ReadAllBytesAsync(fixture.SettingsFile));
    }
    [Fact]
    public async Task Close_retains_pending_request_for_real_Home_decline_and_audit_only_Finish()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await ShelfLibraryWorkspace.OpenAsync(fixture.Owner, fixture.Actor);
        var id = await workspace.ReviewSaveAsync(Item()); var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
        workspace.Dispose();
        Assert.True((await fixture.Permissions.DecideAsync(id, HomeApprovalChoice.Decline)).Succeeded);
        Assert.Equal("NoAttemptedOutcome", (await workspace.FinishAsync(id)).Code);
        Assert.Equal(id, Assert.Single(workspace.Reviews).RequestID);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Empty((await fixture.Library.ReadAsync()).Library.Items);
    }
    [Fact]
    public async Task Original_display_refuses_changed_physical_UUID_on_review_and_reload_without_foreign_write()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var workspace = await ShelfLibraryWorkspace.OpenAsync(fixture.Owner, fixture.Actor);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.SettingsFile))!.AsObject();
        envelope[nameof(SettingsExportManifest.StoreIdentity)]![nameof(SettingsStoreIdentity.StoreId)] = Guid.NewGuid();
        var foreign = JsonSerializer.SerializeToUtf8Bytes(envelope); await File.WriteAllBytesAsync(fixture.SettingsFile, foreign);
        var home = await File.ReadAllBytesAsync(fixture.HomeFile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.ReviewSaveAsync(Item()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.ReviewSaveAsync(Item()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.ReloadAsync());
        Assert.Empty(workspace.Reviews); Assert.Empty(workspace.Snapshot.Library.Items);
        Assert.Equal(foreign, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
    }
    [Fact]
    public async Task Retired_original_host_after_reload_denies_original_approved_review_without_new_Home_or_settings_effect()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var current = true;
        using var workspace = await ShelfLibraryWorkspace.OpenAsync(fixture.Owner, fixture.Actor,
            originalLifetime: () => current);
        await workspace.ReloadAsync();
        var id = await workspace.ReviewSaveAsync(Item());
        Assert.True((await fixture.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        var settings = await File.ReadAllBytesAsync(fixture.SettingsFile);
        var home = await File.ReadAllBytesAsync(fixture.HomeFile);
        current = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.ReloadAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.ApplyOrRecoverAsync(id));
        Assert.Equal("NoAttemptedOutcome", (await workspace.FinishAsync(id)).Code);
        Assert.Equal(id, Assert.Single(workspace.Reviews).RequestID);
        Assert.Equal(settings, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
    }
    [Fact]
    public async Task Queued_original_factory_keeps_prequeue_UUID_and_rejects_replacement_without_adopting_it()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var original = await fixture.Owner.LoadForDisplayAsync(fixture.Actor);
        using var retained = original.Selection;
        using (var positive = await ShelfLibraryWorkspace.OpenForOriginalDisplayAsync(fixture.Owner, retained))
            Assert.Empty(positive.Snapshot.Library.Items);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.SettingsFile))!.AsObject();
        envelope[nameof(SettingsExportManifest.StoreIdentity)]![nameof(SettingsStoreIdentity.StoreId)] = Guid.NewGuid();
        var foreign = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await File.WriteAllBytesAsync(fixture.SettingsFile, foreign);
        var home = await File.ReadAllBytesAsync(fixture.HomeFile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ShelfLibraryWorkspace.OpenForOriginalDisplayAsync(fixture.Owner, retained));
        Assert.Equal(foreign, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
    }
    private static ShelfLaunchItem Item() => new(Guid.NewGuid(), "Reference site",
        new(ShelfTargetKind.WebAddress, "https://example.test/reference", Uri: "https://example.test/reference"));
    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public string HomeFile => Path.Combine(Paths.DataDirectory, "home.json");
        public HomePermissionTrustService Permissions { get; }
        public HomeShelfLibraryOwner Owner { get; private set; } = null!;
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public ShelfLibraryService Library { get; }
        private readonly VersionedAtomicSettingsStore _settings;
        private readonly FileHomeCoreStateStore _home;
        private readonly HomeLocalProfileIdentity _profiles;
        public Fixture()
        {
            _settings = new(Paths); Library = new(_settings);
            _home = new(Path.Combine(Paths.DataDirectory, "home.json"));
            _profiles = new(_home, new OperatingSystemPrincipalSource());
            var policy = new ShelfOwnedLibraryActionPolicies();
            Permissions = new(_home, policy.TryGet);
        }
        public async Task InitializeAsync()
        {
            Actor = await _profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual local Home actor required.");
            var identity = await _settings.GetStoreIdentityAsync(default);
            var ownership = new HomeLocalStoreOwnership(_home, _profiles,
                new HomeLocalStoreEvidenceRegistry([new ShelfOwnedLibraryEvidence(_settings)]), Permissions);
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, _profiles);
            var resolver = new ShelfOwnedLibraryAccessResolver(Library, _profiles, authority);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, [resolver]), Permissions);
            Owner = new(Library, _profiles, authority, broker, Permissions,
                new HomeOwnedLibraryCommitFenceSource(_home, _profiles, authority, broker));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Owner.LoadForDisplayAsync(Actor));
            await ownership.BindNewEmptyAsync("shelf", identity.StoreId.ToString("D"));
        }
        public void Dispose() { try { Directory.Delete(Paths.RootDirectory, true); } catch (IOException) { } }
    }
    private sealed class Paths : IAppPaths
    {
        public string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-shelf-workspace-" + Guid.NewGuid().ToString("N"));
        public string DataDirectory => RootDirectory;
        public string DatabasePath => Path.Combine(RootDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(RootDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(RootDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(RootDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(RootDirectory, "logs");
        public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
        public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
    }
}
