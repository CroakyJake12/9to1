using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task New_Tab_customisation_opens_native_controls_retries_conflicts_and_persists_its_own_layout(int width, int height)
    {
        var paths = new LayoutPaths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var peerSettings = new VersionedAtomicSettingsStore(paths);
        var request = Request("browse://newtab");
        var layout = new NewTabLayoutPreferences(settings, request.ProfileId, _ => Task.CompletedTask);
        var peer = new NewTabLayoutPreferences(peerSettings, request.ProfileId, _ => Task.CompletedTask);
        var differentProfile = new NewTabLayoutPreferences(peerSettings, "other-profile", _ => Task.CompletedTask);
        var page = new UnifiedHomePage(request, new Registry(),
            () => throw new InvalidOperationException("New Tab must not construct Dashboard."),
            () => throw new InvalidOperationException("New Tab must not construct model discovery."), (_, _) => false,
            (_, _) => throw new InvalidOperationException("Layout editing must not acquire a model."), aiAvailable: false, newTabLayout: layout);
        var window = new Window { Width = width, Height = height, Content = page };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        const string dashboardKey = "dashboard.widget-layouts.v1";
        try
        {
            await settings.SetAsync(dashboardKey, "dashboard sentinel", token);
            var dashboardJson = (await settings.ExportAsync(token)).Settings[dashboardKey];
            await page.InitializeAsync(token);
            window.Show(); SettleFixtureLayout(window);

            await Click("home-newtab-customise-open", pointer: true);
            Assert.True(SettingsCategorySelected(SettingsControl<Button>(page, "home-newtab-layout-balanced")));
            AssertEditorGeometry();
            var beforeConflict = await peer.ReadAsync(token);
            Assert.Equal(new[] { "up-next", "pinned-note" }, beforeConflict.Layout.Widgets.Select(widget => widget.Id));
            Assert.True((await peer.SaveAsync(beforeConflict, beforeConflict.Layout with
            {
                ShowShortcuts = false,
                Widgets = [.. beforeConflict.Layout.Widgets.Reverse().Select(widget => widget with { IsWide = true })]
            }, token)).Saved);

            await Click("home-newtab-layout-focus", pointer: true);
            Assert.Contains("changed in another tab", SettingsControl<TextBlock>(page, "home-newtab-editor-status").Text);
            Assert.Equal(NewTabLayoutPreset.Balanced, (await peer.ReadAsync(token)).Layout.Preset);
            await Click("home-newtab-retry");
            var retried = (await peer.ReadAsync(token)).Layout;
            Assert.Equal(2, retried.Revision);
            Assert.Equal(NewTabLayoutPreset.Focus, retried.Preset);
            Assert.False(retried.ShowShortcuts); // The retained typed edit did not overwrite its peer's visibility.
            Assert.Equal(new[] { "pinned-note", "up-next" }, retried.Widgets.Select(widget => widget.Id));
            Assert.All(retried.Widgets, widget => Assert.True(widget.IsWide));
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is Control { Name: "home-newtab-shortcuts-section", IsEffectivelyVisible: true });

            await Click("home-newtab-background-horizon");
            await Click("home-newtab-visibility");
            await Click("home-newtab-visibility-clock");
            var edited = (await peer.ReadAsync(token)).Layout;
            Assert.Equal(4, edited.Revision);
            Assert.Equal(NewTabBackgroundPreset.Horizon, edited.Background);
            Assert.False(edited.ShowClock);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is Control { Name: "home-newtab-clock", IsEffectivelyVisible: true });

            await Click("home-newtab-reset");
            await Click("home-newtab-reset-cancel");
            Assert.Equal(edited.Revision, (await peer.ReadAsync(token)).Layout.Revision);
            await Click("home-newtab-reset");
            await Click("home-newtab-reset-confirm");
            var reset = (await peer.ReadAsync(token)).Layout;
            Assert.Equal(5, reset.Revision);
            Assert.Equal("haven.new-tab", reset.LayoutId);
            Assert.Equal(NewTabLayoutPreset.Balanced, reset.Preset);
            Assert.Equal(NewTabBackgroundPreset.Horizon, reset.Background);
            Assert.True(reset.ShowClock && reset.ShowShortcuts);
            Assert.Equal(beforeConflict.Layout.Widgets.AsEnumerable(), reset.Widgets.AsEnumerable());
            Assert.Equal(0, (await differentProfile.ReadAsync(token)).Layout.Revision);
            Assert.Equal(dashboardJson, (await peerSettings.ExportAsync(token)).Settings[dashboardKey]);

            await Click("home-newtab-customise-done");
            Assert.True(SettingsControl<Button>(page, "home-newtab-customise-open").IsFocused);
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { window.Close(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }

        async Task Click(string id, bool pointer = false)
        {
            var button = SettingsControl<Button>(page, id);
            Assert.True(button.IsEnabled, $"The mounted New Tab action {id} should be available.");
            Assert.True(button.Focus(NavigationMethod.Tab));
            button.BringIntoView(); SettleFixtureLayout(window);
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
            if (!pointer || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR")))
                PressSettingsKey(window, PhysicalKey.Enter);
            else
            {
                var point = button.TranslatePoint(new Rect(button.Bounds.Size).Center, window);
                Assert.NotNull(point);
                var hit = window.InputHitTest(point.Value) as Control;
                Assert.True(hit == button || hit?.GetVisualAncestors().Contains(button) == true,
                    $"Expected {id} to be clickable at {point}. The window hit {hit?.Name ?? hit?.GetType().Name ?? "nothing"}.");
                ClickSettingsButton(window, button);
            }
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
        }

        void AssertEditorGeometry()
        {
            var editor = SettingsControl<CuiContainerControl>(page, "home-newtab-editor");
            var layoutChoices = SettingsControl<CuiContainerControl>(page, "home-newtab-layout-choices");
            var backgroundChoices = SettingsControl<CuiContainerControl>(page, "home-newtab-background-choices");
            var placement = backgroundChoices.TranslatePoint(default, layoutChoices);
            Assert.NotNull(placement);
            Assert.True(SettingsControl<Button>(page, "home-newtab-customise-done").IsEffectivelyVisible);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is Button { IsEffectivelyVisible: true } button &&
                button.Name is "home-newtab-customise-open" or "home-newtab-visibility-clock");

            if (width >= 1000)
            {
                Assert.InRange(editor.Bounds.Height, 210, 320);
                Assert.InRange(placement.Value.Y, -3, 3);
                Assert.True(placement.Value.X >= layoutChoices.Bounds.Width);
                var focus = SettingsControl<Button>(page, "home-newtab-layout-focus");
                var more = SettingsControl<Button>(page, "home-newtab-layout-more");
                Assert.InRange(Math.Abs(focus.Bounds.Width - more.Bounds.Width), 0, 1);
                Assert.True(focus.Bounds.Width >= 130);
            }
            else
            {
                Assert.InRange(placement.Value.X, -2, 2);
                Assert.True(placement.Value.Y > layoutChoices.Bounds.Height);
                Assert.True(backgroundChoices.Bounds.Width <= editor.Bounds.Width);
            }
        }
    }

    [AvaloniaFact]
    public async Task New_Tab_customisation_deep_link_and_shared_accent_preserve_saved_empty_layout()
    {
        var paths = new LayoutPaths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var request = Request("browse://newtab/customise");
        var preferences = new NewTabLayoutPreferences(new VersionedAtomicSettingsStore(paths), request.ProfileId,
            _ => Task.CompletedTask);
        var initial = await preferences.ReadAsync(token);
        var explicitlyEmpty = await preferences.SaveAsync(initial, initial.Layout with
        {
            Widgets = [], Background = NewTabBackgroundPreset.Paper,
            BackgroundImageId = new string('a', 64), ShowCustomBackground = true
        }, token);
        Assert.True(explicitlyEmpty.Saved);
        var navigation = new List<BrowseNavigationIntent>();
        var page = new UnifiedHomePage(request, new Registry(),
            () => throw new InvalidOperationException("New Tab must not construct Dashboard."),
            () => throw new InvalidOperationException("New Tab must not construct model discovery."),
            (intent, _) => { navigation.Add(intent); return true; },
            (_, _) => throw new InvalidOperationException("Layout editing must not acquire a model."), aiAvailable: false,
            createSettings: () => throw new InvalidOperationException("The palette belongs to the Browse destination."),
            newTabLayout: preferences);
        var window = new Window { Width = 1200, Height = 880, Content = page };
        try
        {
            await page.InitializeAsync(token);
            window.Show(); SettleFixtureLayout(window);
            Assert.True(SettingsControl<Button>(page, "home-newtab-layout-focus").IsFocused);
            Assert.Contains("saved background image is unavailable", SettingsControl<TextBlock>(page, "home-newtab-editor-status").Text);
            var accent = SettingsControl<Button>(page, "home-newtab-background-accent");
            Assert.True(accent.IsEnabled && accent.Focus(NavigationMethod.Tab));
            PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(token);
            Assert.Equal(new BrowseNavigationIntent(request.TabId,
                AppContentAddress.Parse("browse://home/settings/appearance-palette"), true), Assert.Single(navigation));
            Assert.True(accent.IsEffectivelyVisible);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.True(SettingsControl<Button>(page, "home-newtab-customise-open").IsFocused);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is Button { Name: "home-newtab-layout-focus" });
            Assert.Contains("saved background image is unavailable", SettingsControl<TextBlock>(page, "home-status").Text);
            var unchanged = await preferences.ReadAsync(token);
            Assert.Empty(unchanged.Layout.Widgets);
            Assert.Equal(explicitlyEmpty.Current.Serialized, unchanged.Serialized);
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { window.Close(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }
    }
}
