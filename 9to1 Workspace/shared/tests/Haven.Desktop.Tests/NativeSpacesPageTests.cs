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
                deleteSpace: async displayed => { calls++; await registry.DeleteAsync(displayed.Id, token); },
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
    [AvaloniaFact]
    public async Task Missing_owning_delete_callback_disables_native_action_and_preserves_canonical_settings()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-spaces-delete-denial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new VersionedAtomicSettingsStore(new Paths(root));
            var registry = new SpaceRegistry(settings);
            var space = await registry.CreateAsync("Retain without deletion authority", cancellationToken: token);
            var before = (await settings.ExportAsync(token)).Settings["spaces.registry"];
            using var page = new NativeSpacesPage(registry);
            var window = new Window { Content = page, Width = 1000, Height = 700 };
            try
            {
                window.Show(); await page.RefreshNowAsync(token); window.UpdateLayout();
                Assert.False(page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>()
                    .Single(button => button.Name == "Delete").GetValue(HavenProperties.Enabled));
                await page.DeleteSpaceAsync(space.Id);
                Assert.Equal(space.Revision, (await registry.ReadExistingAsync(space.Id, token))!.Revision);
                Assert.Equal(before, (await settings.ExportAsync(token)).Settings["spaces.registry"]);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task Delete_dispatch_captures_displayed_revision_instead_of_rereading_a_newer_space()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-spaces-displayed-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new VersionedAtomicSettingsStore(new Paths(root));
            var registry = new SpaceRegistry(settings);
            var displayed = await registry.CreateAsync("Displayed", cancellationToken: token);
            SpaceDefinition? dispatched = null;
            using var page = new NativeSpacesPage(registry, null, null,
                deleteSpace: expected => { dispatched = expected; return Task.CompletedTask; });
            var window = new Window { Content = page, Width = 1000, Height = 700 };
            try
            {
                window.Show(); await page.RefreshNowAsync(token); window.UpdateLayout();
                var updated = await registry.UpdateAsync(displayed with { Name = "Changed elsewhere" }, displayed.Revision, token);
                var unchangedSettings = (await settings.ExportAsync(token)).Settings["spaces.registry"];
                await page.DeleteSpaceAsync(displayed.Id);
                Assert.NotNull(dispatched);
                Assert.Equal(displayed.Id, dispatched.Id); Assert.Equal(displayed.Revision, dispatched.Revision);
                Assert.Equal(updated.Revision, (await registry.ReadExistingAsync(displayed.Id, token))!.Revision);
                Assert.Equal(unchangedSettings, (await settings.ExportAsync(token)).Settings["spaces.registry"]);
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
