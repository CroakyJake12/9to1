using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using NineToOne.Web;

internal static class DashboardLayoutChecks
{
    internal static async Task Run(CuiDocument document)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DashboardTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var width in new[] { 1440, 900, 390 })
            {
                var store = new LocalPreferences();
                using var home = new BrowserHomeContext(document, _ => {}, isRegisteredRoute: route =>
                    route is "app.write" or "app.present" or "app.picture" or "app.wave" or "app.boards", localPreferences: store);
                var registry = new CuiControlRegistry();
                BrowserCanonicalIcons.Register(registry);
                using var loader = new CuiControlLoader(registry);
                loader.SetBindingContext(home); loader.SetActionDispatcher(home);
                var (root, diagnostics) = loader.TryLoad(document);
                Require(root is not null && !diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error), "Home CUI failed to load");
                loader.WireBindings(root!);
                using var availability = BrowserActionAvailability.Observe(root!, loader, document, home, home);
                using var presentation = new BrowserHomePresentation(root!, home);
                var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
                var window = new Window { Width = width, Height = 980, Content = scroll };
                window.Show();
                try
                {
                    Render("normal");
                    foreach (var id in new[] { "nav-home-icon", "nav-settings-icon", "shortcut-picture-icon", "shortcut-wave-icon" })
                        Require(FindRetained(id) is Image { Source.Size.Width: 24, Source.Size.Height: 24 }, $"Canonical catalog image {id} did not resolve without a glyph font");
                    Require(Find("dashboard-layout-controls") is null, "Customise controls visible on normal dashboard");
                    Require(FindRetained("nav-marker-home") is Border { IsVisible: true, CornerRadius.TopLeft: 2 } && FindRetained("nav-home") is Button { BorderThickness.Left: 0 }, "Navigation selection must be a detached marker, never the button border");
                    if (width < 760)
                    {
                        var retainedRail = FindRetained("navigation-rail");
                        Require(retainedRail is { IsVisible: false } && Find("navigation-rail") is null && Find("open-navigation")?.IsVisible is true, "Mobile navigation must retain the closed drawer outside the active tree");
                        home.DispatchAsync("ToggleDrawer", null).GetAwaiter().GetResult();
                        Render("drawer");
                        Require(Find("navigation-rail")?.IsVisible is true && Find("home-content-frame")?.IsEnabled is false,
                            $"Drawer did not open above inactive content: rail={Find("navigation-rail")?.IsVisible}, contentEnabled={Find("home-content-frame")?.IsEnabled}, ownerDrawer={home.DrawerOpen}, width={root!.Bounds.Width}");
                        home.DispatchAsync("ToggleDrawer", null).GetAwaiter().GetResult();
                        Render("normal");
                        Require(Find("navigation-rail") is null && ReferenceEquals(retainedRail, FindRetained("navigation-rail")), "Closing the drawer lost its identity or left it in the active tree");
                        home.DispatchAsync("ToggleDrawer", null).GetAwaiter().GetResult();
                        Render("drawer-reopened");
                        Require(ReferenceEquals(retainedRail, Find("navigation-rail")), "Reopening the drawer replaced its retained controls");
                        home.DispatchAsync("ToggleDrawer", null).GetAwaiter().GetResult();
                        Render("normal");
                    }
                    Require(Find("hero-description") is TextBlock { TextWrapping: TextWrapping.Wrap }, "hero does not wrap");
                    foreach (var id in new[] { "tile-continue", "home-hero", "hero-description", "hero-context", "app-card-write", "header-search" })
                    {
                        var control = Find(id) ?? throw new Exception("Missing " + id);
                        var origin = control.TranslatePoint(default, root!)!.Value;
                        Require(origin.X >= -0.5 && origin.X + control.Bounds.Width <= width + 0.5, $"{id} exceeds {width}px viewport: {origin.X} + {control.Bounds.Width}");
                    }
                    var description = Find("hero-copy")!; var context = Find("hero-context")!;
                    var a = description.TranslatePoint(default, root!)!.Value; var b = context.TranslatePoint(default, root!)!.Value;
                    Require(a.X + description.Bounds.Width <= b.X + 0.5 || a.Y + description.Bounds.Height <= b.Y + 0.5, "Hero columns overlap");
                    home.DispatchAsync("ToggleCustomise", null).GetAwaiter().GetResult();
                    Render("customise");
                    Require(Find("dashboard-layout-controls") is not null, "Customise mode did not open");
                    var ai = Find("ai-tiles-switch") as CheckBox ?? throw new Exception("Missing independent AI control");
                    // Exercise the native control, not just its command method.
                    ai.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    home.TryGetValue("AiTilesSwitch", out var enabled);
                    Require(enabled is true, "Native AI toggle did not update actual Home state");
                    var retainedTile = Find("tile-continue");
                    home.DispatchAsync("ToggleLocalHidden", null).GetAwaiter().GetResult();
                    home.DispatchAsync("ToggleCustomise", null).GetAwaiter().GetResult();
                    Render("hidden");
                    Require(Find("tile-continue") is null && FindRetained("tile-continue") is { IsVisible: false }, "Hidden tile still participates outside customisation");
                    home.DispatchAsync("ToggleCustomise", null).GetAwaiter().GetResult();
                    Render("hidden-customise");
                    Require(ReferenceEquals(retainedTile, Find("tile-continue")), "Customising did not reactivate the retained hidden tile");
                    Require(store.Json is not null, "Native interaction was not persisted");
                    var scrollHost = (CuiContainerControl)(FindRetained("home-content-scroll") ?? throw new Exception("Missing canonical Home scroll host"));
                    var originalLayout = scrollHost.Layout;
                    for (var cycle = 0; cycle < 5; cycle++)
                    {
                        // Reproduce native property retirement/restoration without
                        // allowing a deferred presenter layout between the writes.
                        CuiLayoutProperties.SetLiteral(scrollHost, "VerticalScrolling", "False", source: "Home lifetime regression");
                        CuiLayoutProperties.SetLiteral(scrollHost, "VerticalScrolling", "True", source: "Home lifetime regression");
                        Render("scroll-restored-" + cycle);
                        Require(ReferenceEquals(originalLayout, scrollHost.Layout), "Scroll transition replaced the authored Home layout");
                        var tileHost = (CuiContainerControl)FindRetained("dashboard-tile-host")!;
                        var order = tileHost.AuthoredChildren.ToArray();
                        tileHost.ReorderChildren(order.Reverse().ToArray());
                        tileHost.ReorderChildren(order);
                        Require(tileHost.AuthoredChildren.SequenceEqual(order), "Repeated reorder lost original tile identities");
                    }
                }
                finally { window.Close(); }

                Control? Find(string name) => root!.GetLogicalDescendants().OfType<Control>().Prepend(root!).FirstOrDefault(c => c.Name == name);
                Control? FindRetained(string name)
                {
                    return Visit(root!);
                    Control? Visit(Control control)
                    {
                        if (control.Name == name) return control;
                        var children = control is CuiContainerControl container ? container.AuthoredChildren : control.GetLogicalChildren().OfType<Control>();
                        foreach (var child in children) if (Visit(child) is { } found) return found;
                        return null;
                    }
                }
                void Render(string state)
                {
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    Require(frame is not null, "No rendered frame");
                    var output = Environment.GetEnvironmentVariable("HOME_REPAIR_CAPTURES");
                    if (output is not null) { Directory.CreateDirectory(output); frame!.Save(Path.Combine(output, $"home-{width}-{state}.png")); }
                }
            }
            RepeatedHomeComposition(document);
        }, CancellationToken.None);
    }
    private static void RepeatedHomeComposition(CuiDocument document)
    {
        var store = new LocalPreferences();
        var source = new ThemePreferenceSource();
        using var preference = new CuiThemePreferenceContext(source, new CuiThemeDefinitionCatalog());
        var window = new Window { Width = 1280, Height = 720 }; window.Show();
        try
        {
            for (var routeVisit = 0; routeVisit < 6; routeVisit++)
            {
                using var home = new BrowserHomeContext(document, _ => {}, isRegisteredRoute: route => route == "app.picture", localPreferences: store);
                var registry = new CuiControlRegistry(); BrowserCanonicalIcons.Register(registry);
                using var loader = new CuiControlLoader(registry);
                loader.SetThemePreferenceContext(preference); loader.SetBindingContext(home); loader.SetActionDispatcher(home);
                var result = loader.TryLoad(document);
                Require(result.Root is not null && result.Diagnostics.All(d => d.Severity != CuiDiagnosticSeverity.Error), "Repeated Home source failed to lower");
                var root = result.Root!; loader.WireBindings(root);
                using var availability = BrowserActionAvailability.Observe(root, loader, document, home, home);
                using var presentation = new BrowserHomePresentation(root, home);
                window.Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
                Render();
                foreach (var theme in new[] { "Glow", "Playful", "Bubble", "Cinematic", "Professional" })
                { source.Change(theme); Render(); }
                home.DispatchAsync("ToggleCustomise", null).GetAwaiter().GetResult();
                home.DispatchAsync("MoveLocalDown", null).GetAwaiter().GetResult(); Render();
                home.DispatchAsync("ToggleLocalHidden", null).GetAwaiter().GetResult();
                home.DispatchAsync("ToggleCustomise", null).GetAwaiter().GetResult(); Render();
                // Reproduce the root's route replacement then original teardown
                // order, including native value removal while old layout survives.
                using var destination = new CuiControlLoader();
                var page = destination.LoadMarkup("<Page><Container Type=\"Grid\"><Text>Route transition</Text></Container></Page>");
                Require(page.Root is not null && page.Diagnostics.Count == 0, "Destination composition failed");
                window.Content = page.Root;
                availability.Dispose(); presentation.Dispose(); loader.Dispose();
                source.Change("Glow"); Render();
                window.Content = null;
            }
        }
        finally { window.Close(); }

        void Render()
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Require(frame is not null, "Repeated route composition lost its native frame");
        }
    }

    private sealed class ThemePreferenceSource : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 1, "home-layout-test", true, CuiAccessibilitySettings.Default);
        public event EventHandler? Changed;
        public void Change(string name) { Current = Current with { Name = name, Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public sealed class DashboardTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<DashboardTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize()
    {
        CakeOS.Cui.Themes.CuiSurfacePaletteCatalog.ActiveTheme = CakeOS.Cui.Themes.CuiTheme.Professional;
        CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
    }
}
