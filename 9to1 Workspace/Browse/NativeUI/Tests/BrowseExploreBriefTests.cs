using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaTheory]
    [InlineData(720)]
    [InlineData(1440)]
    public async Task Explore_brief_waits_for_sender_opens_one_root_app_and_returns_to_the_exact_original_conversation(int width)
    {
        var factory = new BriefFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory,
            internalContentFactory: factory, homeProfileId: "brief-fixture");
        var sourceId = (await chrome.NewTabAsync(false)).SelectedTabId;
        var source = Assert.IsType<BriefFixtureSession>(chrome.ObserveOriginalAppContent(sourceId));
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        window.Width = width; window.Height = 700; RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            var settleSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            const string text = "Write a document about a game.\nKeep this exact original brief.";
            var intent = new BrowseNavigationIntent(sourceId, AppContentAddress.Parse("9to1://write"), true)
            { Brief = new(source.ConversationId, text) };
            var tabs = chrome.State.Tabs.Count;
            Assert.True(workspace.TryQueueNavigationOriginal(intent, settleSender.Task, out var navigation));
            Assert.NotNull(navigation); Assert.False(navigation!.IsCompleted);
            Assert.Equal(0, factory.NativeCreations); Assert.Equal(tabs, chrome.State.Tabs.Count);
            settleSender.SetResult(); await Observe(navigation, window);
            await Observe(Task.WhenAll(workspace.OriginalNotifications), window); Settle(window);
            var targetId = chrome.State.SelectedTabId;
            Assert.NotEqual(sourceId, targetId); Assert.Equal(tabs + 1, chrome.State.Tabs.Count);
            Assert.Equal("9to1://write", chrome.State.SelectedTab.Address.Value);
            Assert.Equal(1, factory.NativeCreations); Assert.Null(source.OriginalClose);
            Assert.Same(source, chrome.ObserveOriginalAppContent(sourceId));
            Assert.True(IsTrue(workspace, "ExploreBriefVisible"));
            Assert.True(workspace.TryGetValue("ExploreBriefText", out var briefText)); Assert.Equal(text, briefText);
            var visibleText = window.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "BrowseExploreBriefText");
            Assert.True(visibleText.IsEffectivelyVisible); Assert.Equal(text, visibleText.Text);
            var viewport = Authored(window, "BrowseContentViewport");
            Assert.True(viewport.TranslatePoint(default, window)!.Value.Y > visibleText.TranslatePoint(default, window)!.Value.Y);
            if (Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"explore-brief-{width}x700.png"));
            }
            await SearchClick(window, Find(window, "BrowseBackToExplore"));
            Assert.Equal(sourceId, chrome.State.SelectedTabId); Assert.Equal(1, source.Restores);
            Assert.Same(source, chrome.ObserveOriginalAppContent(sourceId));
            Assert.Equal(tabs + 1, chrome.State.Tabs.Count); Assert.Equal(1, factory.NativeCreations);
            await Observe(chrome.SelectTabAsync(targetId), window);
            await Observe(Task.WhenAll(workspace.OriginalNotifications), window); Settle(window);
            await SearchClick(window, Find(window, "BrowseDismissExploreBrief"));
            Assert.False(IsTrue(workspace, "ExploreBriefVisible")); Assert.Equal(targetId, chrome.State.SelectedTabId);
            Assert.Equal(1, factory.NativeCreations); Assert.Equal(1, source.Restores);
        }
        finally { await Retire(window); }
    }

    [AvaloniaFact]
    public async Task Explore_brief_superseded_before_sender_settlement_acquires_no_app_and_keeps_close_available()
    {
        var factory = new BriefFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory,
            internalContentFactory: factory, homeProfileId: "brief-fixture");
        var sourceId = chrome.State.SelectedTabId;
        var source = Assert.IsType<BriefFixtureSession>(chrome.ObserveOriginalAppContent(sourceId));
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            var settlement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var intent = new BrowseNavigationIntent(sourceId, AppContentAddress.Parse("9to1://write"), true)
            { Brief = new(source.ConversationId, "This brief is about to be replaced.") };
            Assert.True(workspace.TryQueueNavigationOriginal(intent, settlement.Task, out var original));
            source.NewBrief(); settlement.SetResult(); await Observe(original!, window);
            Assert.Equal(0, factory.NativeCreations); Assert.Equal(sourceId, chrome.State.SelectedTabId);
            Assert.False(IsTrue(workspace, "ExploreBriefVisible")); Assert.True(original!.IsCompletedSuccessfully);
            Assert.Same(source, chrome.ObserveOriginalAppContent(sourceId));
        }
        finally { await Retire(window); }
    }

    [AvaloniaFact]
    public async Task Closing_the_original_Explore_tab_disables_return_and_never_recreates_a_conversation()
    {
        var factory = new BriefFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory,
            internalContentFactory: factory, homeProfileId: "brief-fixture");
        var sourceId = (await chrome.NewTabAsync(false)).SelectedTabId;
        var source = Assert.IsType<BriefFixtureSession>(chrome.ObserveOriginalAppContent(sourceId));
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            Assert.True(workspace.TryQueueNavigationOriginal(new(sourceId, AppContentAddress.Parse("9to1://write"), true)
                { Brief = new(source.ConversationId, "Keep this original text available in Write.") }, Task.CompletedTask, out var original));
            await Observe(original!, window); await Observe(Task.WhenAll(workspace.OriginalNotifications), window);
            var target = chrome.State.SelectedTabId;
            await Observe(chrome.CloseTabAsync(sourceId), window);
            await Observe(Task.WhenAll(workspace.OriginalNotifications), window); Settle(window);
            var count = chrome.State.Tabs.Count;
            Assert.True(IsTrue(workspace, "ExploreBriefVisible"));
            Assert.False(IsTrue(workspace, "ExploreBriefReturnAvailable"));
            Assert.False(Find(window, "BrowseBackToExplore").IsEnabled);
            Assert.True(workspace.TryGetValue("ExploreBriefTarget", out var action));
            await workspace.DispatchAsync("9to1.Browse.BackToExplore", action);
            Assert.Equal(target, chrome.State.SelectedTabId); Assert.Equal(count, chrome.State.Tabs.Count);
            Assert.Equal(0, source.Restores); Assert.True(source.OriginalClose!.IsCompletedSuccessfully);
            await Observe(chrome.CloseTabAsync(target), window);
            await Observe(Task.WhenAll(workspace.OriginalNotifications), window); Settle(window);
            Assert.False(IsTrue(workspace, "ExploreBriefVisible"));
        }
        finally { await Retire(window); }
    }

    [AvaloniaTheory]
    [InlineData("9to1://write/document", true)]
    [InlineData("9to1://write", false)]
    [InlineData("https://example.org/", true)]
    public async Task Brief_handoff_refuses_artifact_routes_web_and_in_place_replacement_before_effects(string destination, bool newTab)
    {
        var factory = new BriefFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), appContentFactory: factory,
            internalContentFactory: factory, homeProfileId: "brief-fixture");
        var sourceId = chrome.State.SelectedTabId;
        var source = Assert.IsType<BriefFixtureSession>(chrome.ObserveOriginalAppContent(sourceId));
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            Assert.False(workspace.TryQueueNavigationOriginal(new(sourceId, AppContentAddress.Parse(destination), newTab)
                { Brief = new(source.ConversationId, "This is presentation, not an artifact command.") }, Task.CompletedTask, out var original));
            Assert.Null(original); Assert.Equal(0, factory.NativeCreations);
            Assert.Same(source, chrome.ObserveOriginalAppContent(sourceId)); Assert.Null(source.OriginalClose);
        }
        finally { await Retire(window); }
    }

    // Real Browse navigation, original task settlement, controls and retirement; fixture content
    // owners do not certify a running production model or mutate any artifact.
    private sealed class BriefFixtureFactory : IBrowseAppContentFactory, IBrowseInternalContentFactory
    {
        internal int NativeCreations;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        {
            if (request.Address.Namespace == AppContentNamespace.NativeApp) NativeCreations++;
            return new BriefFixtureSession(request.Address);
        }
    }
    private sealed class BriefFixtureSession(AppContentAddress address) : IBrowseNativeAppContentSession, IBrowseNavigationBriefSource
    {
        private Control? _view;
        internal Guid ConversationId { get; private set; } = Guid.NewGuid();
        internal int Restores;
        public AppContentAddress Address => address;
        public string Title => address.Namespace == AppContentNamespace.NativeApp ? "Write" : "Explore fixture";
        public string IconKey => "home";
        public event EventHandler? StateChanged;
        public Task? OriginalInitialization { get; private set; }
        public Task? OriginalClose { get; private set; }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Control OriginalNativeView => _view ?? throw new InvalidOperationException("The fixture view is uninitialized.");
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??= Dispatcher.UIThread.InvokeAsync(() =>
        { token.ThrowIfCancellationRequested(); _view = new TextBlock { Text = "Retained original content" }; }).GetTask();
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
        public bool CanRestoreNavigationBrief(Guid id) => IsPublicationCurrent && id == ConversationId;
        public Task<bool> RestoreNavigationBriefAsync(Guid id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!CanRestoreNavigationBrief(id)) throw new InvalidOperationException("This fixture conversation has changed.");
            Restores++; return Task.FromResult(true);
        }
        internal void NewBrief() { ConversationId = Guid.NewGuid(); StateChanged?.Invoke(this, EventArgs.Empty); }
    }
}
