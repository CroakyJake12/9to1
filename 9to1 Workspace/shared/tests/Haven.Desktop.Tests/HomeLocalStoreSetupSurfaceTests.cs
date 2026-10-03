using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed class HomeLocalStoreSetupSurfaceTests
{
    [AvaloniaFact]
    public async Task Actual_conversation_setup_uses_its_own_UUID_and_only_explicit_native_button_binds()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-home-conversation-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new SqliteDatabase(new Paths(root));
            await new ConversationProductionDatabase(database).InitializeAsync(token);
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var permissions = new HomePermissionTrustService(home, (_, _) => null);
            var evidence = new ConversationLocalStoreEvidenceProvider(database);
            var ownership = new HomeLocalStoreOwnership(home, actors, new HomeLocalStoreEvidenceRegistry([evidence]), permissions);
            var setup = new HomeLocalStoreSetupSession("conversation", database, actors, ownership, evidence);
            Assert.Throws<ArgumentException>(() => new HomeLocalStoreSetupSession("spaces", database, actors, ownership, evidence));
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, actors)]);
            var reviews = 0;
            using var surface = new HomeLocalStoreSetupCuiSurface(setup, "Conversations", "Home", new HomeProfileCuiReadiness(runtime, actors),
                (_, _) => { reviews++; return Task.CompletedTask; });
            var window = new Window { Content = surface, Width = 1000, Height = 700 };
            try
            {
                await surface.InitializeAsync(token);
                window.Show(); window.UpdateLayout();
                var displayed = await setup.InspectAsync(token);
                Assert.False(displayed.IsOwned); Assert.True(displayed.CanBindEmpty);
                Assert.Null(await ownership.GetVerifiedAsync("conversation", displayed.StoreId.ToString("D"), token));
                var button = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), item => Equals(item.Content, "Set up empty Conversations store"));
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
                Assert.Equal(0, reviews);
                Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                var binding = Assert.Single((await home.ReadAsync(token)).State!.Records, item => item.RecordType == "home.local-store-ownership");
                var payload = binding.Payload.Deserialize<HomeLocalStoreBinding>();
                Assert.NotNull(payload);
                Assert.Equal("conversation", payload.ResourceKind);
                Assert.Equal(displayed.StoreId.ToString("D"), payload.StoreId);
                Assert.Empty(await new ConversationRepository(database).GetRecentAsync(null, 10, token));
            }
            finally { window.Close(); }
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
