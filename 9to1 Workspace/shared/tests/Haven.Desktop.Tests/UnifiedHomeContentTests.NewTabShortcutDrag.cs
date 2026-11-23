using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaTheory]
    [InlineData(1440, 900, "Dashboard")]
    [InlineData(720, 640, "Library")]
    public async Task New_Tab_shortcut_grip_reorders_saved_references_with_pointer_and_keyboard(int width, int height, string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        var paths = new LayoutPaths();
        var request = Request("browse://newtab");
        var store = new VersionedAtomicSettingsStore(paths);
        var preferences = new NewTabLayoutPreferences(store, request.ProfileId, _ => Task.CompletedTask);
        var initial = await preferences.ReadAsync(token);
        var page = new UnifiedHomePage(request, new Registry(),
            () => throw new InvalidOperationException("Moving Shortcuts must not construct Dashboard."),
            () => throw new InvalidOperationException("Moving Shortcuts must not construct model discovery."),
            (_, _) => throw new InvalidOperationException("Moving Shortcuts must not navigate."),
            (_, _) => throw new InvalidOperationException("Moving Shortcuts must not acquire a model."), aiAvailable: false,
            createLibrary: () => throw new InvalidOperationException("Moving Shortcuts must not acquire Library."),
            newTabLayout: preferences, newTabWidgets: new NewTabWidgetSources(null, null, null, null, _ => Task.CompletedTask));
        var window = new Window { Width = width, Height = height, Content = page };
        try
        {
            await page.InitializeAsync(token);
            window.Show();
            SettleFixtureLayout(window);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-newtab-shortcut-drag" && button.IsEffectivelyVisible);
            var customise = SettingsControl<Button>(page, "home-newtab-customise-open");
            Assert.True(customise.Focus(NavigationMethod.Tab));
            PressSettingsKey(window, PhysicalKey.Enter);
            await SettleAsync();

            var source = Grip("Apps");
            var target = Grip(destination);
            var targetTile = ButtonByName("home-newtab-shortcut", destination);
            Assert.False(source.IsFocused);
            if (source.FindAncestorOfType<ScrollViewer>() is { } scroll &&
                source.TranslatePoint(new Rect(source.Bounds.Size).Center, scroll) is { } center)
            {
                // Keep both grips in a settled, hittable part of the actual compact viewport.
                var maximumOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                var offset = Math.Clamp(scroll.Offset.Y + center.Y - scroll.Viewport.Height / 2, 0, maximumOffset);
                scroll.Offset = new Vector(scroll.Offset.X, offset);
                SettleFixtureLayout(window);
            }
            // Native headless input flushes the compositor after the ScrollViewer offset settles.
            window.MouseMove(new Point(3, 3));
            var from = Center(source);
            var to = Center(targetTile);
            AssertWithinHit(source, from);
            AssertWithinHit(targetTile, to);

            BeginDrag(from, to);
            Assert.InRange(Card(source).Opacity, .65, .67);
            Assert.InRange(Card(target).Opacity, .77, .79);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR")))
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                CaptureOptionalFrame(window, frame, $"new-tab-shortcut-drag-{width}x{height}",
                    "Rendered Unified Home New Tab pointer drag");
            }
            PressSettingsKey(window, PhysicalKey.Escape);
            window.MouseUp(to, MouseButton.Left);
            await SettleAsync();
            Assert.Equal(1d, Card(source).Opacity);
            Assert.Equal(1d, Card(target).Opacity);
            Assert.Null((await preferences.ReadAsync(token)).Serialized);
            Assert.True(Grip("Apps").IsFocused);

            BeginDrag(from, to);
            var outside = new Point(3, 3);
            window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
            window.MouseUp(outside, MouseButton.Left);
            await SettleAsync();
            Assert.Null((await preferences.ReadAsync(token)).Serialized);

            BeginDrag(from, to);
            window.MouseUp(to, MouseButton.Left);
            await SettleAsync();
            var reloaded = new NewTabLayoutPreferences(new VersionedAtomicSettingsStore(paths), request.ProfileId, _ => Task.CompletedTask);
            var saved = (await reloaded.ReadAsync(token)).Layout;
            var sourcePlacement = initial.Layout.Shortcuts[0];
            var destinationIndex = Array.FindIndex(initial.Layout.Shortcuts.ToArray(), shortcut => shortcut.Label == destination);
            var expected = initial.Layout.Shortcuts.RemoveAt(0).Insert(destinationIndex, sourcePlacement);
            Assert.Equal(1, saved.Revision);
            Assert.Equal(expected.AsEnumerable(), saved.Shortcuts.AsEnumerable());
            Assert.Equal(initial.Layout.Widgets.AsEnumerable(), saved.Widgets.AsEnumerable());
            Assert.True(Grip("Apps").IsFocused);

            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
            window.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
            await SettleAsync();
            var keyboardSaved = (await reloaded.ReadAsync(token)).Layout;
            Assert.Equal(2, keyboardSaved.Revision);
            Assert.Equal(expected.RemoveAt(destinationIndex).Insert(destinationIndex - 1, sourcePlacement).AsEnumerable(),
                keyboardSaved.Shortcuts.AsEnumerable());
            Assert.True(Grip("Apps").IsFocused);

            var options = ButtonByName("newtab-widget-options", "Options for Up next");
            var edit = ButtonByName("home-newtab-shortcut-edit", "Edit Apps");
            await OpenAsync(options);
            Assert.False(Grip("Apps").IsEnabled);
            Assert.False(edit.IsEnabled);
            Assert.False(ButtonByName("home-newtab-shortcut-add", "Add shortcut").IsEnabled);
            PressSettingsKey(window, PhysicalKey.Escape);
            await SettleAsync();
            Assert.True(Grip("Apps").IsEnabled);

            await OpenAsync(edit);
            Assert.False(options.IsEnabled);
            Assert.False(ButtonByName("newtab-widget-picker-open").IsEnabled);
            Assert.False(ButtonByName("newtab-widget-drag", "Reorder Up next").IsEffectivelyEnabled);
            PressSettingsKey(window, PhysicalKey.Escape);
            await SettleAsync();
            Assert.True(options.IsEnabled);
            Assert.Equal(2, (await reloaded.ReadAsync(token)).Layout.Revision);
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { window.Close(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }

        Button Grip(string label) => ButtonByName("home-newtab-shortcut-drag", "Drag " + label);

        Button ButtonByName(string id, string? name = null)
        {
            var candidates = page.GetVisualDescendants().OfType<Button>().Where(button => button.Name == id && button.IsEffectivelyVisible);
            Assert.Contains(candidates, button => name is null || AutomationProperties.GetName(button) == name);
            return Assert.Single(candidates, button => name is null || AutomationProperties.GetName(button) == name);
        }

        async Task OpenAsync(Button button)
        {
            Assert.True(button.IsEnabled && button.Focus(NavigationMethod.Tab));
            PressSettingsKey(window, PhysicalKey.Enter);
            await SettleAsync();
        }

        static CuiContainerControl Card(Button grip) => grip.GetVisualAncestors().OfType<CuiContainerControl>()
            .First(control => control.Name == "home-newtab-shortcut-card");

        Point Center(Button grip) => grip.TranslatePoint(new Rect(grip.Bounds.Size).Center, window)!.Value;

        void AssertWithinHit(Button grip, Point point)
        {
            var hit = window.InputHitTest(point) as Visual;
            Assert.True(hit is not null && (ReferenceEquals(grip, hit) || grip.IsVisualAncestorOf(hit)),
                $"{AutomationProperties.GetName(grip)} at {point} hit {(hit as Control)?.Name ?? hit?.GetType().Name}; " +
                $"scroll offset {grip.FindAncestorOfType<ScrollViewer>()?.Offset}.");
        }

        void BeginDrag(Point from, Point to)
        {
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        }

        async Task SettleAsync()
        {
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
        }
    }
}
