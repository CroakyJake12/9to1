using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Browser;
using Haven.Infrastructure;
using Haven.UI;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class BrowserOwnedToolsMountTests
{
    [AvaloniaFact]
    public async Task Actual_native_module_same_Home_graph_mounts_tools_in_distinct_region_and_closed_page_rejects_retained_clicks()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = lifetime.Token;
        var directory = Directory.CreateTempSubdirectory("astra-browser-tools-mount-").FullName;
        var paths = new Paths(directory);
        var services = new ServiceCollection().AddHavenInfrastructure();
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(directory, "home.json")));
        services.AddSingleton<BrowserSessionService>();
        App.AddOwnedBrowserTools(services); // Exact production registration entry, not a parallel issuer graph.
        await using var provider = services.BuildServiceProvider();
        using var bus = new HavenEventBus();
        using var browser = provider.GetRequiredService<BrowserSessionService>();
        var actors = provider.GetRequiredService<IAuthenticatedResourceActorSource>();
        var actor = await actors.GetCurrentAsync(token) ?? throw new InvalidOperationException("Actual Home actor required.");
        var registry = provider.GetRequiredService<BrowseOwnedDocumentRegistry>();
        Assert.Same(registry, Assert.Single(provider.GetServices<ICanonicalResourceAccessResolver>(),
            value => value.ResourceKind == "webmcp.document"));
        Assert.Same(provider.GetRequiredService<HomeWebMcpOriginalActorApproval>(),
            provider.GetRequiredService<IWebMcpOriginalActorApproval>());
        var scene = new BrowseOwnedToolsScene(registry, provider.GetRequiredService<BrowseOwnedWebMcpBinding>(), actor,
            async update => await Dispatcher.UIThread.InvokeAsync(update), _ => throw new InvalidOperationException("No Home review was issued by this mount-only test."));
        using var page = new BrowserPage(bus, browser, new BrowserDataService(paths),
            provider.GetRequiredService<IOllamaClient>(), new UserPreferencesService(paths));
        page.MountOwnedTools(scene);
        var control = Assert.IsType<HavenSceneControl>(page.Content);
        var window = new Window { Width = 1100, Height = 900, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(scene.Root, control.Root!.DescendantsAndSelf().Single(value => value.Name == "Browse.Tools.Root"));
            Assert.Equal(5, scene.Root.GetValue(HavenProperties.Row));
            Assert.Equal(HavenOverflow.Scroll, scene.Root.GetValue(HavenProperties.Overflow));
            var tabs = control.Root.DescendantsAndSelf().Single(value => value.Name == "Browser.Tabs.Runtime");
            var web = control.Root.DescendantsAndSelf().Single(value => value.Name == "Browser.Web");
            Assert.Equal(3, web.GetValue(HavenProperties.Row));
            Assert.True(scene.Root.Bounds.Height > 0);
            Assert.True(scene.Root.Bounds.Y >= web.Bounds.Y + web.Bounds.Height);
            Assert.True(scene.Root.Bounds.Y >= tabs.Bounds.Y + tabs.Bounds.Height);
            Assert.True(scene.DiscoverButton.Bounds.Width > 0);
            Assert.True(scene.DiscoverButton.Bounds.Height > 0);
            var homeBefore = await File.ReadAllBytesAsync(Path.Combine(directory, "home.json"), token);
            page.Dispose();
            var button = scene.DiscoverButton;
            button.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None));
            button.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None));
            await page.WhenOwnedToolActionsIdleAsync().WaitAsync(token);
            Assert.Empty(scene.PendingReviewIDs);
            Assert.Equal(homeBefore, await File.ReadAllBytesAsync(Path.Combine(directory, "home.json"), token));
            Assert.Throws<InvalidOperationException>(() => page.MountOwnedTools(scene));
        }
        finally { window.Close(); browser.Dispose(); }
        // Actual native controls/composition only. No page effect, Home approval or installed engine is asserted.
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
