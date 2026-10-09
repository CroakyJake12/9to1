using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
            Assert.True(workspace.TryGetValue("Bookmarks", out var bookmarks));
            var bookmarkRow = Assert.Single(Assert.IsType<BrowseNativeWorkspace.SavedEntryRow[]>(bookmarks));
            var bookmarkButton = window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "BrowseBookmarkRowButton" && Equals(button.Content, bookmarkRow.Label));
            await Click(window, workspace, bookmarkButton);
            Assert.Equal(new Uri("https://saved.example/bookmark"), originalHost.RequestedAddresses.Last());
            Assert.True(workspace.TryGetValue("History", out var history));
            var visit = Assert.IsType<BrowseNativeWorkspace.SavedEntryRow[]>(history).Single(row => row.Label == "Other visit");
            var visitButton = window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "BrowseHistoryRowButton" && Equals(button.Content, visit.Label));
            await Click(window, workspace, visitButton);
            Assert.Equal(new Uri("https://other.example/page"), originalHost.RequestedAddresses.Last());
            await Click(window, workspace, Find(window, "BrowseClearHistoryButton"));
            var actualCommand = workspace.OriginalCommand; var requestCount = originalHost.RequestedAddresses.Count;
            await workspace.DispatchAsync("9to1.Browse.OpenHistory", visit.Target);
            Assert.Same(actualCommand, workspace.OriginalCommand);
            Assert.Equal(requestCount, originalHost.RequestedAddresses.Count);
            Assert.Empty(chrome.State.History);
            Assert.False(workspace.TryGetItemValue(visit, "Target", out _));
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
        { var host = new FixtureTab(request, originalContent); Hosts.Add(host); return Task.FromResult<IBrowseEngineTab>(host); }
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
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) => Task.FromResult<string?>(null);
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
