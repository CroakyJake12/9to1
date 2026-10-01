using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed class OwnedSpacesWorkspaceTests
{
    [AvaloniaFact]
    public async Task Actual_two_native_setup_actions_open_one_actor_bound_workspace_without_auto_binding()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-owned-spaces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new Paths(root); var settings = new VersionedAtomicSettingsStore(paths);
            var sql = new SqliteDatabase(paths); await new ConversationProductionDatabase(sql).InitializeAsync(token);
            var repository = new ConversationRepository(sql);
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var permissions = new HomePermissionTrustService(home, (_, _) => null);
            var spacesEvidence = new SpacesLocalStoreEvidenceProvider(settings, settings);
            var chatEvidence = new ConversationLocalStoreEvidenceProvider(sql);
            var ownership = new HomeLocalStoreOwnership(home, actors, new HomeLocalStoreEvidenceRegistry([spacesEvidence, chatEvidence]), permissions);
            var receipts = new HomeResourceStoreOwnershipAuthority(ownership, actors);
            var chatAuthority = new ConversationLocalStoreAuthority(actors, receipts);
            var available = true;
            Task<OwnedSpacesWorkspace> Open() => OwnedSpacesWorkspace.OpenAsync(settings, actors, receipts, sql,
                chatAuthority, repository, () => available, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Open);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, actors)]);
            async Task BindThroughNativeAsync(string kind, string label, IResourceStoreIdentitySource identities, IHomeLocalStoreEvidenceProvider evidence)
            {
                var setup = new HomeLocalStoreSetupSession(kind, identities, actors, ownership, evidence);
                using var surface = new HomeLocalStoreSetupCuiSurface(setup, label, "Home", new HomeProfileCuiReadiness(runtime, actors),
                    (_, _) => throw new InvalidOperationException("Empty setup must not fabricate an approval request."));
                var window = new Window { Content = surface, Width = 900, Height = 600 };
                try
                {
                    await surface.InitializeAsync(token); window.Show(); window.UpdateLayout();
                    Assert.False((await setup.InspectAsync(token)).IsOwned);
                    Assert.Null(await ownership.GetVerifiedAsync(kind, (await identities.GetStoreIdentityAsync(token)).StoreId.ToString("D"), token));
                    var button = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), item => Equals(item.Content, "Set up empty " + label + " store"));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (var attempt = 0; attempt < 250 && !(await setup.InspectAsync(token)).IsOwned; attempt++) await Task.Delay(20, token);
                    Assert.True((await setup.InspectAsync(token)).IsOwned);
                }
                finally { window.Close(); }
            }
            await BindThroughNativeAsync("spaces", "Spaces", settings, spacesEvidence);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Open);
            await BindThroughNativeAsync("conversation", "Conversations", sql, chatEvidence);
            var workspace = await Open();
            Assert.NotEqual((await settings.GetStoreIdentityAsync(token)).StoreId, (await sql.GetStoreIdentityAsync(token)).StoreId);
            var space = await workspace.Registry.CreateAsync("Verified workspace", cancellationToken: token);
            var now = DateTimeOffset.UtcNow;
            var proposed = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Canonical chat", null, null,
                false, false, now, now, SpaceId: space.Id);
            Assert.Same(proposed, await workspace.Writer.CreateAsync(proposed, space, token));
            Assert.Equal(proposed, await repository.GetAsync(proposed.Id, token));
            // The same production lifecycle used by native new-chat, branch and sidebar callbacks.
            var lifecycle = new OwnedSpaceChatLifecycle(_ => Open(), repository);
            var other = await workspace.Registry.CreateAsync("Other workspace", cancellationToken: token);
            var fresh = proposed with { Id = Guid.NewGuid(), Title = "Selected Space chat", SpaceId = null };
            var created = await lifecycle.CreateAsync(fresh, other.Id, token);
            Assert.Equal(other.Id, created.SpaceId);
            Assert.Equal(created, await repository.GetAsync(created.Id, token));
            var branch = created with { Id = Guid.NewGuid(), ParentConversationId = created.Id, Title = "Branch" };
            var createdBranch = await lifecycle.CreateAsync(branch, space.Id, token);
            Assert.Equal(other.Id, createdBranch.SpaceId); // branch retains source instead of selected sidebar Space
            Assert.Equal(created, await repository.GetAsync(created.Id, token));
            var moved = await lifecycle.AssignAsync(createdBranch, space.Id, token);
            Assert.Equal(space.Id, moved.SpaceId);
            Assert.Equal(moved, await repository.GetAsync(moved.Id, token));
            await Assert.ThrowsAsync<SpaceConversationWriteException>(() => lifecycle.AssignAsync(createdBranch, null, token));
            var detached = await lifecycle.AssignAsync(moved, null, token);
            Assert.Null(detached.SpaceId);
            Assert.Equal(detached, await repository.GetAsync(detached.Id, token));
            available = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lifecycle.CreateAsync(fresh with { Id = Guid.NewGuid() }, space.Id, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lifecycle.AssignAsync(created, space.Id, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.RequireCurrentAccessAsync(token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.Registry.CreateAsync("Denied", cancellationToken: token));
            Assert.Equal(proposed, await repository.GetAsync(proposed.Id, token));
            Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "conversation.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
