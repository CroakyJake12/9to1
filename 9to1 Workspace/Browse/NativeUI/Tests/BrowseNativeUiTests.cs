using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<Application>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));
}

// This fixture observes actual CUI loader actions and explicit fake engine data.
// It is not an installed Home/profile grant or platform WebView certification.
public sealed partial class BrowseNativeUiTests
{
    private static readonly List<(BrowseNativeWindow Window, Exception Failure)> RetainedFailedOwners = [];
    [AvaloniaFact]
    public async Task Actual_tab_rows_resolve_labels_and_select_current_source_issued_identity()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var firstId = chrome.State.SelectedTabId;
            var oldFirst = Rows(workspace).Single();
            Assert.Contains("Firefox", oldFirst.Label);
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            Assert.Equal(2, chrome.State.Tabs.Count);
            Assert.NotEqual(firstId, chrome.State.SelectedTabId);
            var current = Rows(workspace).Single(row => row.Key == firstId.ToString("D"));
            Assert.NotSame(oldFirst.Target, current.Target);
            var command = workspace.OriginalCommand;
            await workspace.DispatchAsync("9to1.Browse.SelectTab", oldFirst.Target);
            Assert.Same(command, workspace.OriginalCommand);
            Assert.NotEqual(firstId, chrome.State.SelectedTabId);
            var actualRow = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, current.Label));
            await Click(window, workspace, actualRow);
            Assert.Equal(firstId, chrome.State.SelectedTabId);
            Assert.False(workspace.TryGetItemValue(oldFirst, "Target", out _));
            Assert.True(workspace.TryGetItemValue(Rows(workspace).Single(row => row.Key == current.Key), "Label", out var actualLabel));
            Assert.IsType<string>(actualLabel);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Same_key_row_refresh_uses_current_target_after_native_title_changes()
    {
        var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var originalId = chrome.State.SelectedTabId;
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton"));
            var originalHost = Assert.Single(factory.Hosts);
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            var oldRow = Rows(workspace).Single(row => row.Key == originalId.ToString("D"));
            originalHost.PublishFixtureTitle("Current native title");
            await Task.WhenAll(workspace.OriginalNotifications.ToArray());
            var current = Rows(workspace).Single(row => row.Key == oldRow.Key);
            Assert.NotSame(oldRow.Target, current.Target);
            Assert.Contains("Current native title", current.Label);
            var actualRow = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, current.Label));
            await Click(window, workspace, actualRow);
            Assert.Equal(originalId, chrome.State.SelectedTabId);
            Assert.Same(originalHost, chrome.ObserveOriginalEngine(originalId));
            Assert.Single(factory.Hosts);
            Assert.NotNull(originalHost.OriginalNativeView.Parent);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Actual_saved_rows_open_canonical_bookmark_and_history_and_decline_removed_row()
    {
        var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        await chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton"));
            var originalHost = Assert.Single(factory.Hosts);
            originalHost.CommitFixturePage(new Uri("https://saved.example/bookmark"), "Saved bookmark");
            await Task.WhenAll(chrome.OriginalNativeSources.ToArray()); await Task.WhenAll(workspace.OriginalNotifications.ToArray());
            await Click(window, workspace, Find(window, "BrowseBookmarkButton"));
            originalHost.CommitFixturePage(new Uri("https://other.example/page"), "Other visit");
            await Task.WhenAll(chrome.OriginalNativeSources.ToArray()); await Task.WhenAll(workspace.OriginalNotifications.ToArray());
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
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Address_and_reset_actions_recover_an_unavailable_tab_through_persisted_site_policy()
    {
        var paths = new Paths(); var factory = new FixtureFactory();
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Gecko,
            new Dictionary<string, BrowseEngineKind> { ["chromium.example"] = BrowseEngineKind.Chromium }));
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
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
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Authored_find_and_zoom_controls_restore_the_selected_tabs_actual_state()
    {
        var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
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
            var button = window.GetLogicalDescendants().OfType<Button>().Single(item => Equals(item.Content, row.Label));
            await Click(window, workspace, button);
            Assert.True(workspace.TryGetValue("FindQuery", out var restored)); Assert.Equal("first query", restored);
            Assert.True(workspace.TryGetValue("Zoom", out var zoom)); Assert.Equal("110%", zoom);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Reopen_button_and_standard_keyboard_shortcuts_restore_tabs_without_reopening_private_state()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
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
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
        async Task Shortcut(Key key)
        {
            var before = workspace.OriginalCommand;
            window.KeyPress(key, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
            Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(before, workspace.OriginalCommand);
            await Observe(workspace.OriginalCommand!, window);
            await Task.WhenAll(workspace.OriginalNotifications.ToArray());
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
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness(), appearance);
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
            Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 24);
            window.MouseMove(point.Value); window.MouseDown(point.Value, MouseButton.Left); window.MouseUp(point.Value, MouseButton.Left);
            var loader = Assert.IsType<CuiControlLoader>(typeof(CuiSceneHost).GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.SceneHost));
            await Observe(loader.WhenActionsIdleAsync(), window);
            Assert.NotNull(workspace.OriginalCommand); await Observe(workspace.OriginalCommand!, window);
            Assert.Equal(2, chrome.State.Tabs.Count);
            // Rendered toolbar labels must fit at the narrow width too; a
            // clickable clipped label is not satisfactory visual acceptance.
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (var id in new[] { "BrowseChromiumTabButton", "BrowseDiscardAddressButton", "BrowseResetTabEngineButton",
                "BrowseOpenHistoryButton", "BrowseOpenBookmarksButton", "BrowseOpenDownloadsButton", "BrowseOpenSettingsButton" })
            {
                var action = Find(window, id);
                var caption = Assert.Single(action.GetVisualDescendants().OfType<TextBlock>());
                Assert.True(caption.TextLayout.Width <= caption.Bounds.Width + 0.5,
                    $"{id} caption requires {caption.TextLayout.Width} but has {caption.Bounds.Width} at width {width}.");
                Assert.InRange(action.TranslatePoint(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2), window)!.Value.Y, 0, height);
            }
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
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    private static BrowseNativeWorkspace.TabRow[] Rows(BrowseNativeWorkspace workspace)
    { Assert.True(workspace.TryGetValue("Tabs", out var value)); return Assert.IsType<BrowseNativeWorkspace.TabRow[]>(value); }
    private static Button Find(BrowseNativeWindow window, string name) => window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == name);
    private static async Task Click(BrowseNativeWindow window, BrowseNativeWorkspace workspace, Button button)
    {
        Assert.True(button.IsEnabled);
        var loader = Assert.IsType<CuiControlLoader>(typeof(CuiSceneHost).GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.SceneHost));
        var diagnostics = Assert.IsType<CuiControlDiagnostics>(loader.Inspect(button));
        Assert.True(diagnostics.DispatcherConnected); Assert.True(diagnostics.ActionsWired);
        var before = workspace.OriginalCommand;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var originalPipeline = loader.WhenActionsIdleAsync(); await Observe(originalPipeline, window);
        Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(before, workspace.OriginalCommand);
        var original = workspace.OriginalCommand!; await Observe(original, window);
        Assert.Same(original, workspace.OriginalCommand);
        await Task.WhenAll(workspace.OriginalNotifications.ToArray());
    }
    private static async Task<T> Observe<T>(Task<T> original, BrowseNativeWindow window)
    { await Observe((Task)original, window); return await original; }
    private static async Task Observe(Task original, BrowseNativeWindow window)
    {
        var completed = await Task.WhenAny(original, Task.Delay(TimeSpan.FromSeconds(20)));
        if (!ReferenceEquals(completed, original)) throw new TimeoutException("The SAME Browse source is pending; its window and raw source remain retained.");
        await original;
    }
    private static async Task Retire(BrowseNativeWindow window)
    { window.Close(); Assert.NotNull(window.OriginalClose); await Observe(window.OriginalClose!, window); }
    private sealed class FixtureReadiness : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "browse-ui-fixture", "Explicit local Browse controls fixture")); }
    private sealed class FixtureFactory(IBrowserOriginalDownloadContent? originalContent = null) : IBrowseEngineHostFactory, IBrowseEngineAvailability
    {
        public bool IsSupported => true; public string UnsupportedReason => "Fixture only";
        public List<FixtureTab> Hosts { get; } = [];
        public bool IsEngineSupported(BrowseEngineKind engine) => engine == BrowseEngineKind.Chromium;
        public string UnsupportedReasonFor(BrowseEngineKind engine) => "Fixture has no " + engine;
        public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
        {
            // Match the real Windows factory: native controls belong to the UI
            // dispatcher even when Browse selects an engine after asynchronous I/O.
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            { token.ThrowIfCancellationRequested(); var host = new FixtureTab(request, originalContent); Hosts.Add(host); return (IBrowseEngineTab)host; }).GetTask();
        }
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
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
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
