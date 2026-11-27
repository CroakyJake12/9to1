using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Atlas_Apps_uses_the_shared_rail_real_catalogue_and_readable_selected_ink(string themeName)
    {
        var source = new AppsThemeSource(themeName);
        using var theme = new CuiThemePreferenceContext(source);
        var pins = new AppsPinOwner("spaces", "boards", "write");
        var page = NewAppsPage(pins, theme);
        var window = new Window { Content = page, Width = 1400, Height = 900 };
        try
        {
            window.Show(); await page.InitializeAsync(CancellationToken.None); AppsLayout(window);
            var sidebar = AppsControl<CuiContainerControl>(page, "home-sidebar");
            Assert.Equal(234, sidebar.Bounds.Width);
            var selected = AppsControl<Button>(page, "home-apps");
            Assert.Equal(0, selected.BorderThickness.Left);
            AssertAppsSelectedFillAndContrast(selected);
            Assert.Equal(AppsColour(selected, "CuiAccentPrimaryForegroundBrush"), AppsColour(selected.Foreground));
            Assert.Equal(AppsColour(selected.Foreground), AppsColour(Assert.Single(selected.GetVisualDescendants().OfType<TextBlock>()).Foreground));
            var icon = Assert.Single(selected.GetVisualDescendants().OfType<Image>());
            var drawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(icon.Source).Drawing);
            Assert.Equal(AppsColour(selected.Foreground), AppsColour(drawing.Pen!.Brush));
            Assert.NotNull(AppsControl<Image>(page, "home-brand-mark").Source);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control.Name == "home-canvas-backing");

            var realModes = BuiltInModeSeed.Modes.Where(HomeAppHubCatalogue.IsAppRecord).ToArray();
            Assert.Equal(realModes.Length, page.GetVisualDescendants().OfType<Button>().Count(button => button.Name == "home-app-tile"));
            Assert.Equal(3, page.GetVisualDescendants().OfType<Button>().Count(button => button.Name == "home-app-pin-open"));
            var grid = AppsControl<CuiContainerControl>(page, "home-apps-pinned-grid");
            Assert.True(grid.Bounds.Width > 900);
            Assert.All(page.GetVisualDescendants().OfType<CuiContainerControl>().Where(control => control.Name == "home-app-pin-card"),
                card => Assert.Equal(new CornerRadius(themeName == "Glow" ? 22 : 16), card.CornerRadius));
            Assert.All(page.GetVisualDescendants().OfType<CuiContainerControl>().Where(control => control.Name == "home-app-row"),
                row => Assert.Equal(new CornerRadius(themeName == "Glow" ? 999 : 11), row.CornerRadius));
            CaptureAppsFrame(window, "apps-" + themeName.ToLowerInvariant() + "-1400x900");

            var query = AppsControl<TextBox>(page, "home-app-search");
            query.Text = "Boards"; await page.WhenActionsIdleAsync(); AppsLayout(window);
            Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => button.Name == "home-app-tile");
            source.Change(themeName == "Glow" ? "Professional" : "Glow"); AppsLayout(window);
            Assert.Equal("Boards", query.Text);
            AssertAppsSelectedFillAndContrast(selected);
            var changedDrawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(icon.Source).Drawing);
            Assert.Equal(AppsColour(selected.Foreground), AppsColour(changedDrawing.Pen!.Brush));
            Assert.All(page.GetVisualDescendants().OfType<CuiContainerControl>().Where(control => control.Name == "home-app-pin-card"),
                card => Assert.Equal(new CornerRadius(themeName == "Glow" ? 16 : 22), card.CornerRadius));
            Assert.All(page.GetVisualDescendants().OfType<CuiContainerControl>().Where(control => control.Name == "home-app-row"),
                row => Assert.Equal(new CornerRadius(themeName == "Glow" ? 11 : 999), row.CornerRadius));
            window.Width = 700; AppsLayout(window);
            Assert.Equal(68, sidebar.Bounds.Width);
            Assert.True(query.Bounds.Width > 0);
            var queryPoint = query.TranslatePoint(default, window);
            Assert.NotNull(queryPoint);
            Assert.InRange(queryPoint.Value.X + query.Bounds.Width, 1, window.Bounds.Width);
            Assert.True(selected.IsEffectivelyVisible && selected.IsEffectivelyEnabled);
            CaptureAppsFrame(window, "apps-" + (themeName == "Glow" ? "professional" : "glow") + "-700x900");
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Atlas_App_pin_button_keyboard_and_drag_share_one_identity_order_and_do_not_launch()
    {
        var pins = new AppsPinOwner("spaces", "boards", "write");
        var launches = 0;
        var page = NewAppsPage(pins, queue: (_, _) => { launches++; return true; });
        var window = new Window { Content = page, Width = 1400, Height = 950 };
        try
        {
            window.Show(); await page.InitializeAsync(CancellationToken.None); AppsLayout(window);
            var original = pins.Snapshot.ToArray();
            var firstId = original[0].ModeId;
            var secondId = original[1].ModeId;
            var thirdId = original[2].ModeId;
            var firstCard = AppPinCard(page, firstId);
            var later = Assert.Single(firstCard.GetVisualDescendants().OfType<Button>(), button => button.Name == "home-app-pin-later");
            later.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync(); AppsLayout(window);
            Assert.Equal(new[] { secondId, firstId, thirdId }, pins.Snapshot.Select(pin => pin.ModeId));

            var firstButton = Assert.Single(AppPinCard(page, firstId).GetVisualDescendants().OfType<Button>(), button => button.Name == "home-app-pin-open");
            Assert.True(firstButton.Focus());
            window.KeyPress(Key.Right, RawInputModifiers.Alt, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.Alt, PhysicalKey.None, null);
            await page.WhenActionsIdleAsync(); AppsLayout(window);
            Assert.Equal(new[] { secondId, thirdId, firstId }, pins.Snapshot.Select(pin => pin.ModeId));

            firstButton = Assert.Single(AppPinCard(page, firstId).GetVisualDescendants().OfType<Button>(), button => button.Name == "home-app-pin-open");
            var start = firstButton.TranslatePoint(new Rect(firstButton.Bounds.Size).Center, window)!.Value;
            var target = AppPinCard(page, secondId);
            var finish = target.TranslatePoint(new Rect(target.Bounds.Size).Center, window)!.Value;
            window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X - 12, start.Y));
            window.MouseMove(finish); window.MouseUp(finish, MouseButton.Left);
            await page.WhenActionsIdleAsync(); AppsLayout(window);
            Assert.Equal(new[] { firstId, secondId, thirdId }, pins.Snapshot.Select(pin => pin.ModeId));
            Assert.Equal(0, launches);
            Assert.Equal(3, pins.Reorders);
            Assert.All(pins.Snapshot, pin =>
            {
                var saved = Assert.Single(original, value => value.ModeId == pin.ModeId);
                Assert.Equal(saved.Id, pin.Id); Assert.Equal(saved.PinnedAt, pin.PinnedAt);
            });
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Atlas_Apps_filters_and_unavailable_references_cannot_launch_a_substitute()
    {
        var launches = new List<string>();
        var page = NewAppsPage(new AppsPinOwner("boards"), queue: (intent, _) =>
            { launches.Add(intent.Address.Value); return true; });
        var window = new Window { Content = page, Width = 1100, Height = 900 };
        try
        {
            window.Show(); await page.InitializeAsync(CancellationToken.None); AppsLayout(window);
            await page.DispatchAsync("home.app-filter", "all"); AppsLayout(window);
            var missing = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "home-app-tile" && AutomationProperties.GetName(button) == "Unavailable: Train");
            Assert.False(missing.IsEffectivelyEnabled);
            Assert.Contains("not installed", Assert.IsType<string>(ToolTip.GetTip(missing)), StringComparison.OrdinalIgnoreCase);
            await page.DispatchAsync("home.open-app", "train");
            Assert.Empty(launches);
            await page.DispatchAsync("home.app-filter", "pinned"); AppsLayout(window);
            var only = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => button.Name == "home-app-tile");
            Assert.Equal("Open Boards", AutomationProperties.GetName(only));
            only.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await page.WhenActionsIdleAsync();
            Assert.Equal(new[] { "9to1://boards" }, launches);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Atlas_Apps_cannot_route_a_lookalike_registration_to_a_different_app_identity()
    {
        var real = BuiltInModeSeed.Modes.Single(mode => mode.Key == "boards");
        var lookalike = real with { Id = Guid.NewGuid(), Source = ModeSource.Created, Name = "Boards" };
        var launches = 0;
        var page = NewAppsPage(new AppsPinOwner("boards"), queue: (_, _) => { launches++; return true; },
            registry: new Registry([real, lookalike]));
        var window = new Window { Content = page, Width = 1200, Height = 900 };
        try
        {
            window.Show(); await page.InitializeAsync(default); AppsLayout(window);
            var rows = page.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "home-app-tile").ToArray();
            Assert.Equal(2, rows.Length);
            foreach (var row in rows)
            {
                row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await page.WhenActionsIdleAsync();
            }
            await page.DispatchAsync("home.open-app", "boards");
            Assert.Equal(0, launches);
            Assert.Contains("That exact app cannot be opened", AppsControl<TextBlock>(page, "home-apps-status").Text);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    private static UnifiedHomePage NewAppsPage(AppsPinOwner pins, CuiThemePreferenceContext? theme = null,
        Func<BrowseNavigationIntent, Task, bool>? queue = null, IModeRegistry? registry = null) => new(Request(), registry ?? new Registry(),
        () => throw new InvalidOperationException("Apps must not construct Dashboard."),
        () => throw new InvalidOperationException("Apps must not contact inference."),
        queue ?? ((_, _) => true), (_, _) => throw new InvalidOperationException("Apps must not start inference."),
        interfaceTheme: theme, appPins: pins, admitAppChange: _ => Task.CompletedTask);

    private static T AppsControl<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);
    private static CuiContainerControl AppPinCard(Control root, Guid id) =>
        Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), control => control.Name == "home-app-pin-card" &&
            control.DataContext is ICuiBindingContext binding && binding.TryGetValue("app.pinId", out var value) && Equals(value, id.ToString("D")));
    private static void AppsLayout(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void CaptureAppsFrame(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
    private static Color AppsColour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static Color AppsColour(Control control, string key)
    { Assert.True(control.TryFindResource(key, out var value)); return AppsColour(Assert.IsAssignableFrom<IBrush>(value)); }
    private static void AssertAppsSelectedFillAndContrast(Button selected)
    {
        Assert.True(selected.TryFindResource("CuiAccentPrimaryBrush", out var resource));
        var expected = Assert.IsAssignableFrom<ILinearGradientBrush>(resource);
        var actual = Assert.IsAssignableFrom<ILinearGradientBrush>(selected.Background);
        Assert.Equal(expected.StartPoint, actual.StartPoint);
        Assert.Equal(expected.EndPoint, actual.EndPoint);
        Assert.Equal(expected.SpreadMethod, actual.SpreadMethod);
        Assert.Equal(expected.Opacity, actual.Opacity);
        Assert.NotEmpty(actual.GradientStops);
        Assert.Equal(expected.GradientStops.Select(stop => (stop.Offset, stop.Color)),
            actual.GradientStops.Select(stop => (stop.Offset, stop.Color)));
        var ink = AppsColour(selected.Foreground);
        Assert.All(actual.GradientStops, stop =>
        {
            Assert.Equal(255, stop.Color.A);
            Assert.True(CuiContrast.Ratio(ink, stop.Color) >= 4.5,
                $"Selected navigation text contrast failed at gradient offset {stop.Offset}.");
        });
    }

    private sealed class AppsThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } =
            new(name, 1, "apps-fixture", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed;
        internal void Change(string next)
        { Current = Current with { Name = next, Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class AppsPinOwner(params string[] keys) : IOrderedPinRepository
    {
        internal IReadOnlyList<ModePin> Snapshot = keys.Select((key, index) => new ModePin(Guid.NewGuid(),
            BuiltInModeSeed.Modes.Single(mode => mode.Key == key).Id, index, DateTimeOffset.UnixEpoch.AddMinutes(index))).ToArray();
        internal int Reorders;
        public Task<IReadOnlyList<ModePin>> GetPinsAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Snapshot); }
        public Task UpsertPinAsync(ModePin pin, CancellationToken token) => throw new NotSupportedException();
        public Task DeletePinAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public async Task<bool> TryReorderAsync(IReadOnlyList<ModePin> expected, IReadOnlyList<Guid> order,
            CancellationToken token, Func<CancellationToken, Task>? admitCommit = null)
        {
            if (!Snapshot.SequenceEqual(expected)) return false;
            if (admitCommit is not null) await admitCommit(token); token.ThrowIfCancellationRequested();
            Snapshot = order.Select((id, index) => Snapshot.Single(pin => pin.ModeId == id) with { SortOrder = index }).ToArray();
            Reorders++; return true;
        }
        public async Task<bool> TrySetPinnedAsync(IReadOnlyList<ModePin> expected, Guid id, bool pinned,
            CancellationToken token, Func<CancellationToken, Task>? admitCommit = null)
        {
            if (!Snapshot.SequenceEqual(expected)) return false;
            if (admitCommit is not null) await admitCommit(token); token.ThrowIfCancellationRequested();
            Snapshot = pinned ? Snapshot.Append(new ModePin(Guid.NewGuid(), id, Snapshot.Count, DateTimeOffset.UtcNow)).ToArray()
                : Snapshot.Where(pin => pin.ModeId != id).ToArray();
            return true;
        }
    }
}
