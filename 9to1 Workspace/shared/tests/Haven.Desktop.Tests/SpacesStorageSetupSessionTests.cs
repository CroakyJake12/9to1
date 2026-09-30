using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class SpacesStorageSetupSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inspection_does_not_bind_and_explicit_setup_requires_same_displayed_local_actor(bool revoke)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-spaces-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new VersionedAtomicSettingsStore(new Paths(Path.Combine(root, "settings")));
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var principal = new Principal();
            var actors = new HomeLocalProfileIdentity(home, principal);
            var evidence = new SpacesLocalStoreEvidenceProvider(settings, settings);
            var ownership = new HomeLocalStoreOwnership(home, actors, new HomeLocalStoreEvidenceRegistry([evidence]),
                new HomePermissionTrustService(home, (_, _) => null));
            var setup = new SpacesStorageSetupSession(settings, actors, ownership, evidence);
            var displayed = await setup.InspectAsync(token);
            Assert.False(displayed.IsOwned); Assert.True(displayed.CanBindEmpty); Assert.Null(displayed.PendingRequestId);
            Assert.Null(await ownership.GetVerifiedAsync("spaces", displayed.StoreId.ToString("D"), token));
            Assert.Null(await new SpaceRegistry(settings).ReadExistingAsync(SpaceRegistry.StudySpaceId, token));
            var before = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token);
            if (revoke)
            {
                principal.Revoked = true;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup.BindEmptyAsync(token));
                Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
            }
            else
            {
                await setup.BindEmptyAsync(token);
                Assert.True((await setup.InspectAsync(token)).IsOwned);
                var authority = new SpaceLocalStoreAuthority(settings, actors,
                    new HomeResourceStoreOwnershipAuthority(ownership, actors), () => true);
                var registry = new SpaceRegistry(settings, authority.CaptureWriteAdmissionAsync);
                Assert.Equal("Explicitly owned", (await registry.CreateAsync("Explicitly owned", cancellationToken: token)).Name);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public bool Revoked;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => Revoked
            ? ValueTask.FromResult<string?>(null) : new OperatingSystemPrincipalSource().GetPrincipalAsync(token);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
