using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

// Actual FileHome/profile object composition; controlled principal/evidence only. This
// pure predicate has no native/canonical owner effect, policy or actor-value authority.
public sealed class HomeLocalCommitCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "original-composition-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Exact_original_composition_and_foreign_components_are_checked_before_any_foreign_Home_read()
    {
        var ct = CancellationToken.None;
        var path = Path.Combine(_root, "home.json"); var store = new FileHomeCoreStateStore(path);
        var profiles = new HomeLocalProfileIdentity(store, new Principal());
        var permissions = new HomePermissionTrustService(store, (_, _) => null);
        var ownership = new HomeResourceStoreOwnershipAuthority(new HomeLocalStoreOwnership(store, profiles, new Evidence(), permissions), profiles);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(profiles, []), permissions);
        Assert.True(HomeLocalCommitComposition.IsBound(broker, store, profiles, ownership));
        Assert.False(File.Exists(path));
        Assert.NotNull(await profiles.GetCurrentAsync(ct));
        var before = await File.ReadAllBytesAsync(path, ct);
        var foreignPath = Path.Combine(_root, "foreign", "home.json"); var foreignStore = new FileHomeCoreStateStore(foreignPath);
        var foreignProfiles = new HomeLocalProfileIdentity(foreignStore, new Principal());
        var foreignPermissions = new HomePermissionTrustService(foreignStore, (_, _) => null);
        var foreignOwnership = new HomeResourceStoreOwnershipAuthority(
            new HomeLocalStoreOwnership(foreignStore, foreignProfiles, new Evidence(), foreignPermissions), foreignProfiles);
        Assert.False(HomeLocalCommitComposition.IsBound(broker, foreignStore, profiles, ownership));
        Assert.False(HomeLocalCommitComposition.IsBound(broker, store, foreignProfiles, ownership));
        Assert.False(HomeLocalCommitComposition.IsBound(broker, store, profiles, foreignOwnership));
        Assert.False(HomeLocalCommitComposition.IsBound(new(new ResourceAuthorizationService(profiles, []), foreignPermissions), store, profiles, ownership));
        Assert.False(HomeLocalCommitComposition.IsBound(null, store, profiles, ownership));
        Assert.False(File.Exists(foreignPath)); Assert.Equal(before, await File.ReadAllBytesAsync(path, ct));
    }
    [Fact]
    public async Task Equal_value_forwarding_actor_cannot_replace_original_source_or_trigger_an_observation()
    {
        var ct = CancellationToken.None; var path = Path.Combine(_root, "home.json");
        var store = new FileHomeCoreStateStore(path); var profiles = new HomeLocalProfileIdentity(store, new Principal());
        var permissions = new HomePermissionTrustService(store, (_, _) => null);
        var ownership = new HomeResourceStoreOwnershipAuthority(new HomeLocalStoreOwnership(store, profiles, new Evidence(), permissions), profiles);
        Assert.NotNull(await profiles.GetCurrentAsync(ct)); var before = await File.ReadAllBytesAsync(path, ct);
        var forwarding = new Forwarder(profiles);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(forwarding, []), permissions);
        Assert.False(HomeLocalCommitComposition.IsBound(broker, store, profiles, ownership));
        Assert.Equal(0, forwarding.Calls); Assert.Equal(before, await File.ReadAllBytesAsync(path, ct));
    }
    private sealed class Forwarder(HomeLocalProfileIdentity profiles) : IAuthenticatedResourceActorSource
    {
        public int Calls { get; private set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { Calls++; return profiles.GetCurrentAsync(ct); }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>("controlled-original-composition-principal"); } }
    private sealed class Evidence : IHomeLocalStoreEvidenceSource
    { public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string kind, string id, CancellationToken ct) => ValueTask.FromResult<HomeLocalStoreEvidence?>(null); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
