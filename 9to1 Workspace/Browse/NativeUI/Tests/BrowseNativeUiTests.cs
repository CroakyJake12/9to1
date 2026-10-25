using System.Reflection;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui.Themes;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using CakeOS.Cui.Runtime;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(HavenOS.Apps.Browse.Tests.BrowseTestAppBuilder))]
namespace HavenOS.Apps.Browse.Tests;

public static class BrowseTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<BrowseTestApplication>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));
}

public sealed class BrowseTestApplication : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Browse");
}

// This fixture observes actual CUI loader actions and explicit fake engine data.
// It is not an installed Home/profile grant or platform WebView certification.
public sealed partial class BrowseNativeUiTests
{
    private static readonly List<(BrowseNativeWindow Window, Exception Failure)> RetainedFailedOwners = [];

    [AvaloniaFact]
    public async Task Selected_tab_uses_neutral_default_then_only_explicit_accent_and_clears_back()
    {
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        var previousOverride = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousAccent = CuiSurfacePaletteCatalog.AccentOverride;
        try
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Professional;
            CuiSurfacePaletteCatalog.OverrideAccent = false;
            CuiSurfacePaletteCatalog.AccentOverride = null;
            await using var chrome = await BrowseChrome.CreateAsync(new Paths());
            await using var workspace = new BrowseNativeWorkspace(chrome, _ => true);
            var first = chrome.State.SelectedTabId;
            var firstRow = Assert.Single(Rows(workspace));
            Assert.Equal(Color.Parse("#FFF2F2F2"), firstRow.BackgroundColor);
            Assert.Equal(Colors.Black, firstRow.ForegroundColor);

            var second = (await chrome.NewTabAsync(false)).SelectedTabId;
            Assert.Equal(Colors.Transparent, Rows(workspace).Single(row => row.Key == first.ToString("D")).BackgroundColor);
            Assert.Equal(Color.Parse("#FFF2F2F2"), Rows(workspace).Single(row => row.Selected).BackgroundColor);

            CuiSurfacePaletteCatalog.OverrideAccent = true;
            CuiSurfacePaletteCatalog.AccentOverride = CuiAccentColour.Teal;
            await chrome.SelectTabAsync(first);
            var selected = Rows(workspace).Single(row => row.Selected);
            var dark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ||
                Application.Current?.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            var palette = CuiSurfacePaletteCatalog.For("Browse", dark ? CuiAppearance.SuperDark : CuiAppearance.SuperBright,
                CuiSurfacePaletteCatalog.ActiveTheme);
            Assert.Equal(palette.Accent, selected.BackgroundColor);
            Assert.Equal(palette.AccentInk, selected.ForegroundColor);

            CuiSurfacePaletteCatalog.OverrideAccent = false;
            CuiSurfacePaletteCatalog.AccentOverride = null;
            await chrome.SelectTabAsync(second);
            selected = Rows(workspace).Single(row => row.Selected);
            Assert.Equal(Color.Parse("#FFF2F2F2"), selected.BackgroundColor);
            Assert.Equal(Colors.Black, selected.ForegroundColor);
            Assert.Equal(Colors.Transparent, Rows(workspace).Single(row => row.Key == first.ToString("D")).BackgroundColor);
        }
        finally
        {
            CuiSurfacePaletteCatalog.ActiveTheme = previousTheme;
            CuiSurfacePaletteCatalog.OverrideAccent = previousOverride;
            CuiSurfacePaletteCatalog.AccentOverride = previousAccent;
        }
    }
    [AvaloniaFact]
    public async Task Actual_tab_rows_resolve_labels_and_select_current_source_issued_identity()
    {
        using var trace = new FixtureTraceScope(nameof(Actual_tab_rows_resolve_labels_and_select_current_source_issued_identity));
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var firstId = chrome.State.SelectedTabId;
            var oldFirst = Rows(workspace).Single();
            Assert.Contains("Firefox", oldFirst.Description);
            Assert.DoesNotContain("Firefox", oldFirst.Label);
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            Assert.Equal(2, chrome.State.Tabs.Count);
            Assert.NotEqual(firstId, chrome.State.SelectedTabId);
            var current = Rows(workspace).Single(row => row.Key == firstId.ToString("D"));
            Assert.NotSame(oldFirst.Target, current.Target);
            var command = workspace.OriginalCommand;
            await workspace.DispatchAsync("9to1.Browse.SelectTab", oldFirst.Target);
            Assert.Same(command, workspace.OriginalCommand);
            Assert.NotEqual(firstId, chrome.State.SelectedTabId);
            var actualRow = TabButton(window, workspace, current.Key);
            await Click(window, workspace, actualRow);
            Assert.Equal(firstId, chrome.State.SelectedTabId);
            Assert.False(workspace.TryGetItemValue(oldFirst, "Target", out _));
            Assert.True(workspace.TryGetItemValue(Rows(workspace).Single(row => row.Key == current.Key), "Label", out var actualLabel));
            Assert.IsType<string>(actualLabel);
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Same_key_row_refresh_uses_current_target_after_native_title_changes()
    {
        using var trace = new FixtureTraceScope(nameof(Same_key_row_refresh_uses_current_target_after_native_title_changes));
        var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var originalId = chrome.State.SelectedTabId;
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton"));
            var originalHost = Assert.Single(factory.Hosts);
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            var oldRow = Rows(workspace).Single(row => row.Key == originalId.ToString("D"));
            originalHost.PublishFixtureTitle("Current native title");
            await Observe(Task.WhenAll(workspace.OriginalNotifications.ToArray()), window);
            var current = Rows(workspace).Single(row => row.Key == oldRow.Key);
            Assert.NotSame(oldRow.Target, current.Target);
            Assert.Contains("Current native title", current.Label);
            var actualRow = TabButton(window, workspace, current.Key);
            await Click(window, workspace, actualRow);
            Assert.Equal(originalId, chrome.State.SelectedTabId);
            Assert.Same(originalHost, chrome.ObserveOriginalEngine(originalId));
            Assert.Single(factory.Hosts);
            Assert.NotNull(originalHost.OriginalNativeView.Parent);
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Actual_saved_rows_open_canonical_bookmark_and_history_and_decline_removed_row()
    {
        using var trace = new FixtureTraceScope(nameof(Actual_saved_rows_open_canonical_bookmark_and_history_and_decline_removed_row));
        var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        Trace("chrome:created");
        await chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton"));
            var originalHost = Assert.Single(factory.Hosts);
            originalHost.CommitFixturePage(new Uri("https://saved.example/bookmark"), "Saved bookmark");
            await Observe(Task.WhenAll(chrome.OriginalNativeSources.ToArray()), window); await Observe(Task.WhenAll(workspace.OriginalNotifications.ToArray()), window);
            await Click(window, workspace, Find(window, "BrowseBookmarkButton"));
            originalHost.CommitFixturePage(new Uri("https://other.example/page"), "Other visit");
            await Observe(Task.WhenAll(chrome.OriginalNativeSources.ToArray()), window); await Observe(Task.WhenAll(workspace.OriginalNotifications.ToArray()), window);
            await Click(window, workspace, Find(window, "BrowseOpenBookmarksButton"));
            var bookmarkTab = chrome.State.SelectedTabId;
            Assert.Equal(BrowserNativePage.Bookmarks, chrome.State.SelectedTab.NativePage);
            Assert.True(workspace.TryGetValue("Bookmarks", out var bookmarks));
            var bookmarkRow = Assert.Single(Assert.IsType<BrowseNativeWorkspace.SavedEntryRow[]>(bookmarks));
            var bookmarkButton = window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "BrowseBookmarkRowButton" && Equals(button.Content, bookmarkRow.Label));
            Assert.True(bookmarkButton.IsEffectivelyVisible);
            await Click(window, workspace, bookmarkButton);
            Assert.Equal(bookmarkTab, chrome.State.SelectedTabId);
            Assert.Equal(new Uri("https://saved.example/bookmark"), chrome.State.SelectedTab.Address.WebUri);
            Assert.Equal(2, factory.Hosts.Count); Assert.Same(originalHost, chrome.ObserveOriginalEngine(chrome.State.Tabs[0].Id));
            await Click(window, workspace, Find(window, "BrowseOpenHistoryButton"));
            Assert.True(workspace.TryGetValue("History", out var history));
            var visit = Assert.IsType<BrowseNativeWorkspace.SavedEntryRow[]>(history).Single(row => row.Label == "Other visit");
            var visitButton = window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "BrowseHistoryRowButton" && Equals(button.Content, visit.Label));
            Assert.True(visitButton.IsEffectivelyVisible);
            await Click(window, workspace, visitButton);
            Assert.Equal(new Uri("https://other.example/page"), chrome.State.SelectedTab.Address.WebUri);
            await Click(window, workspace, Find(window, "BrowseOpenHistoryButton"));
            await Click(window, workspace, Find(window, "BrowseClearHistoryButton"));
            var actualCommand = workspace.OriginalCommand; var hostCount = factory.Hosts.Count;
            await workspace.DispatchAsync("9to1.Browse.OpenHistory", visit.Target);
            Assert.Same(actualCommand, workspace.OriginalCommand);
            Assert.Equal(hostCount, factory.Hosts.Count);
            Assert.Empty(chrome.State.History);
            Assert.False(workspace.TryGetItemValue(visit, "Target", out _));
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Address_and_reset_actions_recover_an_unavailable_tab_through_persisted_site_policy()
    {
        using var trace = new FixtureTraceScope(nameof(Address_and_reset_actions_recover_an_unavailable_tab_through_persisted_site_policy));
        var paths = new Paths(); var factory = new FixtureFactory();
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Gecko,
            new Dictionary<string, BrowseEngineKind> { ["chromium.example"] = BrowseEngineKind.Chromium }));
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var originalId = chrome.State.SelectedTabId;
            Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
            Assert.True(workspace.TrySetValue("Address", "https://chromium.example/page"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton"));
            Assert.Equal(originalId, chrome.State.SelectedTabId);
            Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
            Assert.Equal(new Uri("https://chromium.example/page"), Assert.Single(factory.Hosts).State.Address);
            await Click(window, workspace, Find(window, "BrowseFirefoxTabButton"));
            Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
            await Click(window, workspace, Find(window, "BrowseResetTabEngineButton"));
            Assert.Equal(originalId, chrome.State.SelectedTabId);
            Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
            Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Authored_find_and_zoom_controls_restore_the_selected_tabs_actual_state()
    {
        using var trace = new FixtureTraceScope(nameof(Authored_find_and_zoom_controls_restore_the_selected_tabs_actual_state));
        var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton"));
            var first = chrome.State.SelectedTabId;
            Assert.True(workspace.TrySetValue("FindQuery", "first query"));
            await Click(window, workspace, Find(window, "BrowseFindNextButton"));
            await Click(window, workspace, Find(window, "BrowseZoomInButton"));
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            Assert.True(workspace.TryGetValue("FindQuery", out var empty)); Assert.Equal(string.Empty, empty);
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton"));
            Assert.True(workspace.TrySetValue("FindQuery", "second query"));
            await Click(window, workspace, Find(window, "BrowseFindNextButton"));
            var row = Rows(workspace).Single(item => item.Key == first.ToString("D"));
            var button = TabButton(window, workspace, row.Key);
            await Click(window, workspace, button);
            Assert.True(workspace.TryGetValue("FindQuery", out var restored)); Assert.Equal("first query", restored);
            Assert.True(workspace.TryGetValue("Zoom", out var zoom)); Assert.Equal("110%", zoom);
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Reopen_button_and_standard_keyboard_shortcuts_restore_tabs_without_reopening_private_state()
    {
        using var trace = new FixtureTraceScope(nameof(Reopen_button_and_standard_keyboard_shortcuts_restore_tabs_without_reopening_private_state));
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var originalId = chrome.State.SelectedTabId;
            Assert.False(Find(window, "BrowseReopenTabButton").IsEnabled);
            await Click(window, workspace, Find(window, "BrowseCloseTabButton"));
            await Click(window, workspace, Find(window, "BrowseReopenTabButton"));
            Assert.Equal(originalId, chrome.State.SelectedTabId);
            await Click(window, workspace, Find(window, "BrowseCloseTabButton"));
            await Shortcut(Key.T);
            Assert.Equal(originalId, chrome.State.SelectedTabId);
            await Shortcut(Key.P);
            var privateId = chrome.State.SelectedTabId;
            Assert.Equal(BrowserTabPrivacy.Private, chrome.State.SelectedTab.Privacy);
            await Click(window, workspace, Find(window, "BrowseCloseTabButton"));
            Assert.False(Find(window, "BrowseReopenTabButton").IsEnabled);
            Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == privateId);
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
        async Task Shortcut(Key key)
        {
            var before = workspace.OriginalCommand;
            window.KeyPress(key, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
            Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(before, workspace.OriginalCommand);
            await Observe(workspace.OriginalCommand!, window);
            await Observe(Task.WhenAll(workspace.OriginalNotifications.ToArray()), window);
        }
    }

    [AvaloniaTheory]
    [InlineData(1180, 800, CuiAppearance.SuperBright)]
    [InlineData(1180, 800, CuiAppearance.Bright)]
    [InlineData(1180, 800, CuiAppearance.Dark)]
    [InlineData(1180, 800, CuiAppearance.SuperDark)]
    [InlineData(360, 800, CuiAppearance.SuperBright)]
    [InlineData(360, 800, CuiAppearance.Bright)]
    [InlineData(360, 800, CuiAppearance.Dark)]
    [InlineData(360, 800, CuiAppearance.SuperDark)]
    public async Task Authored_shell_renders_and_pointer_tab_creation_works_at_desktop_and_narrow_sizes(
        int width, int height, CuiAppearance appearance)
    {
        using var trace = new FixtureTraceScope(nameof(Authored_shell_renders_and_pointer_tab_creation_works_at_desktop_and_narrow_sizes));
        Trace($"theory:{width}x{height}:{appearance}");
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness(), appearance);
        RegisterProductionImageResolver(window);
        window.Width = width; window.Height = height;
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using (var initial = window.CaptureRenderedFrame()) Assert.NotNull(initial);
            var button = Find(window, "BrowseNewTabButton");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(point);
            Assert.InRange(point.Value.X, 0, width); Assert.InRange(point.Value.Y, 0, height);
            // Compact desktop chrome retains a 32px square target; pointer activation is exercised below.
            Assert.True(button.Bounds.Width >= 32 && button.Bounds.Height >= 32);
            window.MouseMove(point.Value); window.MouseDown(point.Value, MouseButton.Left); window.MouseUp(point.Value, MouseButton.Left);
            var loader = Assert.IsType<CuiControlLoader>(typeof(CuiSceneHost).GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.SceneHost));
            await Observe(loader.WhenActionsIdleAsync(), window);
            Assert.NotNull(workspace.OriginalCommand); await Observe(workspace.OriginalCommand!, window);
            Assert.Equal(2, chrome.State.Tabs.Count);
            Settle(window);
            // Open the real profile overlay and Web Tools group so the bundled
            // tool/engine controls are actually visible for caption-fit checks.
            await Click(window, workspace, Find(window, "HavenProfileButton"));
            await Click(window, workspace, Find(window, "BrowseWebToolsHeader"));
            // Rendered toolbar labels must fit at the narrow width too; a
            // clickable clipped label is not satisfactory visual acceptance.
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (var id in new[] { "BrowseChromiumTabButton", "BrowseDiscardAddressButton", "BrowseResetTabEngineButton",
                "BrowseOpenHistoryButton", "BrowseOpenBookmarksButton", "BrowseOpenDownloadsButton", "BrowseSettingsButton" })
            {
                var action = FindMounted(window, id);
                action.BringIntoView(); Settle(window);
                action = FindMounted(window, id);
                var caption = Assert.Single(action.GetVisualDescendants().OfType<TextBlock>());
                Assert.True(caption.TextLayout.Width <= caption.Bounds.Width + 0.5,
                    $"{id} caption requires {caption.TextLayout.Width} but has {caption.Bounds.Width} at width {width}.");
                var center = action.TranslatePoint(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2), window);
                Assert.NotNull(center);
                Assert.InRange(center.Value.Y, 0, height);
            }
            await Click(window, workspace, Find(window, "BrowseCloseProfileMenuButton"));
            var address = window.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "BrowseAddressBox");
            Assert.True(address.Bounds.Width >= 80, $"The address entry is clipped to {address.Bounds.Width} at width {width}.");
            address.Focus(); address.SelectAll(); window.KeyTextInput("https://typed.example/path");
            Assert.True(workspace.TryGetValue("Address", out var typed)); Assert.Equal("https://typed.example/path", typed);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            Assert.Equal(width, frame.PixelSize.Width); Assert.Equal(height, frame.PixelSize.Height);
            var evidence = Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(evidence))
            { Directory.CreateDirectory(evidence); frame.Save(Path.Combine(evidence, $"browse-{width}-{appearance}.png")); }
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Fresh_gecko_unsupported_web_tab_fallback_selects_chromium_and_persists_across_restart_with_same_profile()
    {
        using var trace = new FixtureTraceScope(nameof(Fresh_gecko_unsupported_web_tab_fallback_selects_chromium_and_persists_across_restart_with_same_profile));
        var paths = new Paths(); var factory = new FixtureFactory();
        var session = new MemoryTabSessionPersistence();

        Guid originalId; Uri originalAddress;

        var chrome = await BrowseChrome.CreateAsync(paths, factory, tabSessionPersistence: session);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            // No internal-content factory is installed in this fixture: the real
            // default homepage is web, so unsupported Gecko offers available Chromium.
            Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
            Assert.True(chrome.State.SelectedTab.Address.IsWeb);
            Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
            Assert.True(workspace.TryGetValue("ShowChromiumFallback", out var initialFallback));
            Assert.Equal(true, initialFallback);
            var fallback = FindMounted(window, "BrowseInlineChromiumTabButton");
            // Navigating to a web URL yields a Gecko-unsupported web tab because the
            // fixture hosts only Chromium, which is exactly the fallback trigger.
            Assert.True(workspace.TrySetValue("Address", "https://fallback.example/page"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton"));
            originalId = chrome.State.SelectedTabId;
            originalAddress = chrome.State.SelectedTab.Address.WebUri!;
            Assert.True(chrome.State.SelectedTab.Address.IsWeb);
            Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
            Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
            Assert.True(workspace.TryGetValue("ShowChromiumFallback", out var show));
            Assert.Equal(true, show);
            AssertMounted(window, fallback);
            // The ordinary canonical Chromium action switches this SAME tab.
            await Click(window, workspace, fallback);
            Assert.Equal(originalId, chrome.State.SelectedTabId);
            Assert.Equal(originalAddress, chrome.State.SelectedTab.Address.WebUri);
            Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
            Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
            AssertFallbackHidden(workspace);
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }

        Trace("chrome:dispose:begin");
        await chrome.DisposeAsync();
        Trace("chrome:dispose:end");

        // Restart on the SAME profile / paths: restore the real stored tabs and the
        // persisted engine preferences through the actual persistence APIs.
        var chrome2 = await BrowseChrome.CreateAsync(paths, factory, tabSessionPersistence: session);
        Trace("chrome:created");
        var workspace2 = new BrowseNativeWorkspace(chrome2, _ => true);
        var window2 = BrowseNativeSurface.CreateWindow(workspace2, new FixtureReadiness());
        RegisterProductionImageResolver(window2);
        try
        {
            window2.Show(); await Observe(window2.InitializeAsync(), window2);
            var restored = chrome2.State.Tabs.Single(tab => tab.Id == originalId);
            Assert.Equal(originalAddress, restored.Address.WebUri);
            Assert.Equal(BrowseEngineKind.Chromium, restored.Engine);
            Assert.Equal(BrowseEngineState.Ready, restored.EngineState);
            Assert.Equal(originalId, chrome2.State.SelectedTabId);
            AssertFallbackHidden(workspace2);
            var prefs = await new BrowseEnginePreferencesStore(paths).LoadAsync();
            Assert.Equal(BrowseEngineKind.Gecko, prefs.DefaultEngine);
            Assert.Equal(BrowseEngineKind.Chromium, prefs.TabOverrides![originalId]);
            await Retire(window2);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window2, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Fallback_hidden_when_chromium_unavailable()
    {
        using var trace = new FixtureTraceScope(nameof(Fallback_hidden_when_chromium_unavailable));
        var factory = new FixtureFactory(chromiumSupported: false);
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            Assert.True(workspace.TrySetValue("Address", "https://fallback.example/page"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton"));
            Assert.True(chrome.State.SelectedTab.Address.IsWeb);
            Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
            // Chromium is not actually available here, so no fallback is offered.
            AssertFallbackHidden(workspace);
            AssertAbsentFromWindow(window, Find(window, "BrowseInlineChromiumTabButton"));
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Fallback_hidden_when_factory_lacks_per_engine_capability()
    {
        using var trace = new FixtureTraceScope(nameof(Fallback_hidden_when_factory_lacks_per_engine_capability));
        // A host factory with NO typed per-engine capability signal must not be
        // treated as proof that Chromium is available (fail-closed).
        var factory = new NonAvailabilityFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            Assert.True(workspace.TrySetValue("Address", "https://fallback.example/page"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton"));
            Assert.True(chrome.State.SelectedTab.Address.IsWeb);
            Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
            AssertFallbackHidden(workspace);
            AssertAbsentFromWindow(window, Find(window, "BrowseInlineChromiumTabButton"));
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Fallback_hidden_on_native_internal_page()
    {
        using var trace = new FixtureTraceScope(nameof(Fallback_hidden_on_native_internal_page));
        var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "BrowseOpenBookmarksButton"));
            Assert.Equal(BrowserNativePage.Bookmarks, chrome.State.SelectedTab.NativePage);
            Assert.False(chrome.State.SelectedTab.Address.IsWeb);
            // Native Browse pages never use a web renderer; no Chromium fallback.
            AssertFallbackHidden(workspace);
            AssertAbsentFromWindow(window, Find(window, "BrowseInlineChromiumTabButton"));
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Fallback_hidden_when_gecko_actually_works()
    {
        using var trace = new FixtureTraceScope(nameof(Fallback_hidden_when_gecko_actually_works));
        var factory = new FixtureFactory(geckoSupported: true);
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            Assert.True(workspace.TrySetValue("Address", "https://fallback.example/page"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton"));
            Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
            Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
            AssertFallbackHidden(workspace);
            AssertAbsentFromWindow(window, Find(window, "BrowseInlineChromiumTabButton"));
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Fallback_hidden_when_persisted_site_choice_already_selects_chromium()
    {
        using var trace = new FixtureTraceScope(nameof(Fallback_hidden_when_persisted_site_choice_already_selects_chromium));
        var paths = new Paths();
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Gecko,
            new Dictionary<string, BrowseEngineKind> { ["forced.example"] = BrowseEngineKind.Chromium }));
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        Trace("chrome:created");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            Assert.True(workspace.TrySetValue("Address", "https://forced.example/page"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton"));
            Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
            Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
            AssertFallbackHidden(workspace);
            AssertAbsentFromWindow(window, Find(window, "BrowseInlineChromiumTabButton"));
            // The persisted explicit site choice is preserved, not rewritten.
            var prefs = await new BrowseEnginePreferencesStore(paths).LoadAsync();
            Assert.Equal(BrowseEngineKind.Chromium, prefs.SiteOverrides["forced.example"]);
            Assert.Equal(BrowseEngineKind.Gecko, prefs.DefaultEngine);
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    private static void AssertFallbackHidden(BrowseNativeWorkspace workspace)
    {
        // TryGetValue reports whether the binding exists; a recognized Boolean FALSE
        // still returns true. Assert the binding exists then check the OUT value.
        Assert.True(workspace.TryGetValue("ShowChromiumFallback", out var show));
        Assert.Equal(false, show);
    }

    private static Button TabButton(BrowseNativeWindow window, BrowseNativeWorkspace workspace, string key)
    {
        // Titles legitimately repeat. Match the issued row's order, then prove its stable ID by clicking.
        var rows = Rows(workspace);
        var index = Array.FindIndex(rows, row => row.Key == key);
        Assert.InRange(index, 0, rows.Length - 1);
        return window.GetLogicalDescendants().OfType<Button>()
            .Where(button => button.Name == "BrowseTabButton").ElementAt(index);
    }
    private static BrowseNativeWorkspace.TabRow[] Rows(BrowseNativeWorkspace workspace)
    { Assert.True(workspace.TryGetValue("Tabs", out var value)); return Assert.IsType<BrowseNativeWorkspace.TabRow[]>(value); }
    private static CuiControlLoader Loader(BrowseNativeWindow window) => Assert.IsType<CuiControlLoader>(
        typeof(CuiSceneHost).GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.SceneHost));
    private static Control Authored(BrowseNativeWindow window, string name)
    {
        // Hidden CUI subtrees retain authored identities outside the actual window.
        var authored = Assert.IsAssignableFrom<IDictionary>(typeof(CuiControlLoader)
            .GetField("_authoredControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Loader(window)));
        return authored.Keys.Cast<Control>().Single(control => control.Name == name);
    }
    private static Button Find(BrowseNativeWindow window, string name) => Assert.IsType<Button>(Authored(window, name));
    private static void Settle(BrowseNativeWindow window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static Button FindMounted(BrowseNativeWindow window, string name)
    { var button = Find(window, name); AssertMounted(window, button); return button; }
    private static void AssertMounted(BrowseNativeWindow window, Button button)
    {
        Settle(window);
        Assert.Same(window, TopLevel.GetTopLevel(button));
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), item => ReferenceEquals(item, button));
        Assert.True(button.IsEffectivelyVisible);
    }
    private static void AssertAbsentFromWindow(BrowseNativeWindow window, Button button)
    {
        Settle(window);
        Assert.Null(TopLevel.GetTopLevel(button));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), item => ReferenceEquals(item, button));
    }
    private static bool AuthoredContains(Control root, Control target) => ReferenceEquals(root, target) ||
        root is CuiContainerControl container && container.AuthoredChildren.Any(child => AuthoredContains(child, target));
    private static bool IsTrue(BrowseNativeWorkspace workspace, string key) =>
        workspace.TryGetValue(key, out var value) && Equals(value, true);
    private static async Task Click(BrowseNativeWindow window, BrowseNativeWorkspace workspace, Button button)
    {
        Settle(window); Trace("click:prepare:" + button.Name);
        if (TopLevel.GetTopLevel(button) is null || !button.IsEffectivelyVisible)
        {
            // Prepare only the target's genuine authored menu, never an unrelated
            // menu for a detached page control or an unavailable inline fallback.
            Assert.True(AuthoredContains(Authored(window, "BrowseProfileMenu"), button));
            if (!IsTrue(workspace, "ProfileMenuOpen"))
                await ClickCore(window, workspace, FindMounted(window, "HavenProfileButton"));
            Settle(window);
            // A requested header is the one final click below. It is never also
            // clicked here as an opener, which would toggle it twice and close it.
            if (button.Name != "BrowseWebToolsHeader" &&
                AuthoredContains(Authored(window, "BrowseWebToolsGroup"), button) && !IsTrue(workspace, "ToolsOpen"))
                await ClickCore(window, workspace, FindMounted(window, "BrowseWebToolsHeader"));
            Settle(window);
        }
        await ClickCore(window, workspace, button);
    }
    private static async Task ClickCore(BrowseNativeWindow window, BrowseNativeWorkspace workspace, Button button)
    {
        Trace("click:begin:" + button.Name);
        Settle(window); button.BringIntoView(); Settle(window);
        AssertMounted(window, button); Assert.True(button.IsEnabled);
        var loader = Assert.IsType<CuiControlLoader>(typeof(CuiSceneHost).GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.SceneHost));
        var diagnostics = Assert.IsType<CuiControlDiagnostics>(loader.Inspect(button));
        Assert.True(diagnostics.DispatcherConnected); Assert.True(diagnostics.ActionsWired);
        var before = workspace.OriginalCommand;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var originalPipeline = loader.WhenActionsIdleAsync(); await Observe(originalPipeline, window);
        Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(before, workspace.OriginalCommand);
        var original = workspace.OriginalCommand!; await Observe(original, window);
        Assert.Same(original, workspace.OriginalCommand);
        await Observe(Task.WhenAll(workspace.OriginalNotifications.ToArray()), window);
        Settle(window); Trace("click:end:" + button.Name);
    }
    private static async Task<T> Observe<T>(Task<T> original, BrowseNativeWindow window,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(original))] string expression = "")
    { await Observe((Task)original, window, expression); return await original; }
    private static async Task Observe(Task original, BrowseNativeWindow window,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(original))] string expression = "")
    {
        Trace("await:begin:" + expression, original);
        var completed = await Task.WhenAny(original, Task.Delay(TimeSpan.FromSeconds(20)));
        if (!ReferenceEquals(completed, original))
        {
            Trace("await:timeout:" + expression, original);
            throw new TimeoutException("The SAME Browse source is pending; its window and raw source remain retained.");
        }
        await original;
        Trace("await:end:" + expression, original);
    }
    private static async Task Retire(BrowseNativeWindow window)
    {
        Trace("retire:begin");
        window.Close(); Assert.NotNull(window.OriginalClose); await Observe(window.OriginalClose!, window);
        Trace("retire:end");
    }
    private static readonly AsyncLocal<string?> CurrentTraceTest = new();
    private static readonly object TraceGate = new();
    private sealed class FixtureTraceScope : IDisposable
    {
        private readonly string? _previous;
        public FixtureTraceScope(string name)
        { _previous = CurrentTraceTest.Value; CurrentTraceTest.Value = name; Trace("test:begin"); }
        public void Dispose() { Trace("test:end"); CurrentTraceTest.Value = _previous; }
    }
    private static void Trace(string phase, Task? original = null,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        // Optional external diagnostic evidence. Flush every bounded record so a
        // process timeout retains the last real stage without buffered console.
        var path = Environment.GetEnvironmentVariable("BROWSE_FIXTURE_STAGE_LOG");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(Environment.CurrentDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture trace must be outside the owning Root source tree.");
        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.UtcNow, test = CurrentTraceTest.Value, phase = phase.Length <= 1024 ? phase : phase[..1024],
            caller, taskId = original?.Id, taskStatus = original?.Status.ToString()
        }) + "\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        lock (TraceGate)
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            if (stream.Length + bytes.Length > 1024 * 1024) return;
            stream.Write(bytes); stream.Flush(flushToDisk: true);
        }
    }
    private sealed class FixtureReadiness : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "browse-ui-fixture", "Explicit local Browse controls fixture")); }
    private sealed class FixtureFactory(IBrowserOriginalDownloadContent? originalContent = null, bool geckoSupported = false, bool chromiumSupported = true) : IBrowseEngineHostFactory, IBrowseEngineAvailability
    {
        public bool IsSupported => true; public string UnsupportedReason => "Fixture only";
        public List<FixtureTab> Hosts { get; } = [];
        public bool IsEngineSupported(BrowseEngineKind engine) =>
            engine == BrowseEngineKind.Gecko ? geckoSupported
            : engine == BrowseEngineKind.Chromium && chromiumSupported;
        public string UnsupportedReasonFor(BrowseEngineKind engine) => "Fixture has no " + engine;
        public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
        {
            // Match the real Windows factory: native controls belong to the UI
            // dispatcher even when Browse selects an engine after asynchronous I/O.
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            { token.ThrowIfCancellationRequested(); var host = new FixtureTab(request, originalContent); Hosts.Add(host); return (IBrowseEngineTab)host; }).GetTask();
        }
    }
    private sealed class NonAvailabilityFactory : IBrowseEngineHostFactory
    {
        // A host factory with NO typed per-engine capability signal (does not
        // implement IBrowseEngineAvailability). The capability query must fail
        // closed — unknown, never an offer.
        public bool IsSupported => false; public string UnsupportedReason => "Fixture has no supported engines; per-engine capability is unknown.";
        public List<FixtureTab> Hosts { get; } = [];
        public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            { token.ThrowIfCancellationRequested(); var host = new FixtureTab(request); Hosts.Add(host); return (IBrowseEngineTab)host; }).GetTask();
        }
    }
    private sealed class MemoryTabSessionPersistence : IBrowseTabSessionPersistence
    {
        private BrowseTabSessionProjection? _stored;
        public MemoryTabSessionPersistence(BrowseTabSessionProjection? initial = null) => _stored = initial;
        public Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken token) => Task.FromResult(_stored);
        public Task SaveAsync(BrowseTabSessionProjection projection, CancellationToken token)
        { _stored = projection; return Task.CompletedTask; }
    }
    private sealed class FixtureTab(BrowseEngineTabRequest request, IBrowserOriginalDownloadContent? originalContent = null) : IBrowseNativeEngineTab, IBrowseOriginalPageCommitSource, IBrowseOriginalDownloadContentSource
    {
        public IBrowserOriginalDownloadContent? ObserveOriginalDownloadContent(BrowserDownloadRecord currentCanonicalRow) =>
            currentCanonicalRow == originalContent?.OriginalRecord ? originalContent : null;
        public Control OriginalNativeView { get; } = new Border(); public bool CanRetireOriginalView => true;
        public BrowserSnapshot State { get; private set; } = new(request.InitialAddress, "Original fixture title", false, false, false, "Explicit fixture renderer");
        public event EventHandler<BrowserSnapshot>? StateChanged;
        public event EventHandler<BrowserSnapshot>? PageCommitted;
        public List<Uri> RequestedAddresses { get; } = [];
        public event EventHandler<BrowsePopupRequest>? PopupRequested { add { } remove { } }
        public event EventHandler<BrowseEngineCrash>? Crashed { add { } remove { } }
        public void PublishFixtureTitle(string title) { State = State with { Title = title }; StateChanged?.Invoke(this, State); }
        public void CommitFixturePage(Uri address, string title)
        { State = State with { Address = address, Title = title, IsLoading = false }; StateChanged?.Invoke(this, State); PageCommitted?.Invoke(this, State); }
        public Task NavigateAsync(Uri address, CancellationToken token)
        { token.ThrowIfCancellationRequested(); RequestedAddresses.Add(address); State = State with { Address = address, IsLoading = true }; StateChanged?.Invoke(this, State); return Task.CompletedTask; }
        public Task GoBackAsync(CancellationToken token) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken token) => Task.CompletedTask;
        public int StopCalls { get; private set; }
        public Task StopAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); StopCalls++; State = State with { IsLoading = false }; StateChanged?.Invoke(this, State); return Task.CompletedTask; }
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<string?>(script.Contains("'zoomed'", StringComparison.Ordinal) ? "zoomed" : "found"); }
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Paths : IAppPaths
    {
        public Paths() { DataDirectory = Path.Combine(Path.GetTempPath(), "browse-ui-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(DataDirectory); }
        public string DataDirectory { get; } public string DatabasePath => Path.Combine(DataDirectory, "fixture.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs"); public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
    }
}
