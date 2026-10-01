using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class NativeSpacesPageTests
{
    [AvaloniaFact]
    public async Task Delete_dispatches_once_and_denied_refresh_clears_loaded_space_projection()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-spaces-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var registry = new SpaceRegistry(new VersionedAtomicSettingsStore(new Paths(root)));
            var target = await registry.CreateAsync("Canonical deletion", cancellationToken: token);
            var allowed = true;
            var calls = 0;
            using var page = new NativeSpacesPage(registry, null, null,
                deleteSpace: async id => { calls++; await registry.DeleteAsync(id, token); },
                requireCurrentAccess: _ => allowed ? Task.CompletedTask : Task.FromException(new UnauthorizedAccessException("Host admission revoked.")));
            var window = new Window { Width = 1000, Height = 700, Content = page };
            try
            {
                window.Show(); await page.RefreshNowAsync(token); window.UpdateLayout();
                Assert.Contains(page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(), text => text.Content == target.Name);
                await page.DeleteSpaceAsync(target.Id);
                Assert.Equal(1, calls);
                var deleted = Assert.IsType<SpaceDefinition>(await registry.ReadExistingAsync(target.Id, token));
                Assert.True(deleted.IsArchived);
                Assert.Equal(target.Revision + 1, deleted.Revision);
                var retained = await registry.CreateAsync("Retained but no longer displayed", cancellationToken: token);
                await page.RefreshNowAsync(token);
                Assert.Contains(page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(), text => text.Content == retained.Name);
                allowed = false;
                await page.RefreshNowAsync(token);
                Assert.DoesNotContain(page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(), text => text.Content == retained.Name);
                Assert.Equal(string.Empty, page.Scene.Root.DescendantsAndSelf().OfType<Input>().Single(input => input.Name == "Name").Text);
                await page.DeleteSpaceAsync(retained.Id);
                Assert.Equal(1, calls);
                Assert.NotNull(await registry.ReadExistingAsync(retained.Id, token));
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
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
