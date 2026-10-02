using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Home.Tests;

/// <summary>Real local composition and persistence; principal/evidence are controlled protocol inputs.</summary>
public sealed class HomeLocalReadCompositionTests
{
    [Fact]
    public async Task Exact_read_composition_is_pure_and_foreign_components_do_not_create_or_change_Home()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-read-composition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "home.json");
            var home = new FileHomeCoreStateStore(path);
            var profiles = new HomeLocalProfileIdentity(home, new Principal());
            var permissions = new HomePermissionTrustService(home, (_, _) => null);
            var ownership = new HomeResourceStoreOwnershipAuthority(new HomeLocalStoreOwnership(home, profiles, new Evidence(), permissions), profiles);
            Assert.True(HomeLocalReadComposition.IsBound(home, profiles, ownership));
            Assert.False(File.Exists(path));
            Assert.NotNull(await profiles.GetCurrentAsync(default));
            var original = await File.ReadAllBytesAsync(path);
            var foreignPath = Path.Combine(root, "foreign", "home.json");
            var foreignHome = new FileHomeCoreStateStore(foreignPath);
            var foreignProfiles = new HomeLocalProfileIdentity(foreignHome, new Principal());
            var sameHomeForeignProfiles = new HomeLocalProfileIdentity(home, new Principal());
            var foreignPermissions = new HomePermissionTrustService(foreignHome, (_, _) => null);
            var foreignOwnership = new HomeResourceStoreOwnershipAuthority(new HomeLocalStoreOwnership(foreignHome, foreignProfiles, new Evidence(), foreignPermissions), foreignProfiles);
            Assert.False(HomeLocalReadComposition.IsBound(foreignHome, profiles, ownership));
            Assert.False(HomeLocalReadComposition.IsBound(home, foreignProfiles, ownership));
            Assert.False(HomeLocalReadComposition.IsBound(home, sameHomeForeignProfiles, ownership));
            Assert.False(HomeLocalReadComposition.IsBound(home, profiles, foreignOwnership));
            Assert.False(HomeLocalReadComposition.IsBound(null, profiles, ownership));
            Assert.False(HomeLocalReadComposition.IsBound(home, null, ownership));
            Assert.False(HomeLocalReadComposition.IsBound(home, profiles, null));
            Assert.True(HomeLocalReadComposition.IsBound(home, profiles, ownership));
            Assert.False(File.Exists(foreignPath));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>("controlled-read-principal");
    }
    private sealed class Evidence : IHomeLocalStoreEvidenceSource
    {
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string kind, string id, CancellationToken ct) => ValueTask.FromResult<HomeLocalStoreEvidence?>(null);
    }
}
