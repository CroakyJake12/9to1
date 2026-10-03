using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Desktop.Controls;
using HavenOS.Home.NativeUI;
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
                var access = new ProfileSpacesResourceAccess(actors, settings,
                    new HomeResourceStoreOwnershipAuthority(ownership, actors), new SpaceRegistry(settings),
                    () => NineToOne.Cui.AI.AppAiAccessMode.ReadOnly);
                Assert.False(await access.MayReadAsync(SpaceRegistry.StudySpaceId, token));
                Assert.Null(await new SpaceRegistry(settings).ReadExistingAsync(SpaceRegistry.StudySpaceId, token));
                var session = await OwnedSpacesSession.OpenAsync(settings, authority, actors, token);
                Assert.Equal("Explicitly owned", (await session.Registry.CreateAsync("Explicitly owned", cancellationToken: token)).Name);
                principal.Revoked = true;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.RequireCurrentAccessAsync(token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Registry.CreateAsync("Denied", cancellationToken: token));
                Assert.DoesNotContain(await new SpaceRegistry(settings).GetAllAsync(cancellationToken: token), space => space.Name == "Denied");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task Mounted_setup_inspection_preserves_empty_store_until_explicit_native_button()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-spaces-setup-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new VersionedAtomicSettingsStore(new Paths(Path.Combine(root, "settings")));
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var permissions = new HomePermissionTrustService(home, (_, _) => null);
            var evidence = new SpacesLocalStoreEvidenceProvider(settings, settings);
            var ownership = new HomeLocalStoreOwnership(home, actors, new HomeLocalStoreEvidenceRegistry([evidence]), permissions);
            var setup = new SpacesStorageSetupSession(settings, actors, ownership, evidence);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, actors)]);
            var reviews = 0;
            using var surface = new SpacesStorageSetupCuiSurface(setup, new HomeProfileCuiReadiness(runtime, actors),
                (_, _) => { reviews++; return Task.CompletedTask; });
            var window = new Window { Width = 1000, Height = 700, Content = surface };
            try
            {
                await surface.InitializeAsync(token);
                window.Show(); window.UpdateLayout();
                var displayed = await setup.InspectAsync(token);
                Assert.False(displayed.IsOwned);
                Assert.Null(await new SpaceRegistry(settings).ReadExistingAsync(SpaceRegistry.StudySpaceId, token));
                var button = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), item => Equals(item.Content, "Set up empty Spaces store"));
                Assert.True(button.IsVisible); Assert.True(button.IsEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var owned = false;
                for (var attempt = 0; attempt < 250; attempt++)
                {
                    owned = (await setup.InspectAsync(token)).IsOwned;
                    if (owned) break;
                    await Task.Delay(20, token);
                }
                Assert.True(owned);
                Assert.Equal(0, reviews); // Empty-store setup is explicit, but requires no import approval.
                Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                Assert.Null(await new SpaceRegistry(settings).ReadExistingAsync(SpaceRegistry.StudySpaceId, token));
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Session_keeps_acknowledged_ownership_and_retries_only_failed_audit(bool afterAuditCommit)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-spaces-setup-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new VersionedAtomicSettingsStore(new Paths(Path.Combine(root, "settings")));
            await new SpaceRegistry(settings).CreateAsync("Existing owned content", cancellationToken: token);
            var home = new FaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), afterAuditCommit);
            var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var permissions = new HomePermissionTrustService(home, (app, action) =>
                app == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
            var evidence = new SpacesLocalStoreEvidenceProvider(settings, settings);
            var ownership = new HomeLocalStoreOwnership(home, actors, new HomeLocalStoreEvidenceRegistry([evidence]), permissions);
            var setup = new SpacesStorageSetupSession(settings, actors, ownership, evidence);
            Assert.False((await setup.InspectAsync(token)).IsOwned);
            var request = await setup.RequestImportAsync(token);
            Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            home.FailAudit = true;
            await setup.CompleteImportAsync(token);
            var pending = await setup.InspectAsync(token);
            Assert.True(pending.IsOwned); Assert.Null(pending.PendingRequestId);
            Assert.Equal(request.RequestId, pending.PendingAuditRequestId);
            var before = (await home.ReadAsync(token)).State!.Records.Single(record => record.RecordType == "home.local-store-ownership");
            Assert.Equal(1, home.BindingWrites);
            await setup.RetryAuditAsync(token);
            Assert.Null((await setup.InspectAsync(token)).PendingAuditRequestId);
            var after = (await home.ReadAsync(token)).State!.Records.Single(record => record.RecordType == "home.local-store-ownership");
            Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Payload.GetRawText(), after.Payload.GetRawText());
            Assert.Equal(1, home.BindingWrites);
            Assert.Equal(HomePermissionRequestState.Succeeded, (await permissions.GetAuthorizationAsync(request.RequestId, token)).State);
            await Assert.ThrowsAsync<InvalidOperationException>(() => setup.CompleteImportAsync(token));
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
    private sealed class FaultStore(IHomeCoreStateStore inner, bool afterCommit) : IHomeCoreStateStore
    {
        public bool FailAudit; public int BindingWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (FailAudit && record.RecordType == "home.permissions-trust" && record.Payload.GetRawText().Contains("HOME_STORE_IMPORTED", StringComparison.Ordinal))
            {
                FailAudit = false;
                if (afterCommit) _ = await inner.WriteAsync(record, expected, ct);
                throw new IOException("Injected final audit storage failure.");
            }
            return await inner.WriteAsync(record, expected, ct);
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        {
            if (record.RecordType == "home.local-store-ownership") BindingWrites++;
            return inner.WriteGuardedAsync(record, expected, actor, guard, ct);
        }
    }
}
