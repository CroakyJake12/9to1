using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaTheory]
    [InlineData("hubs", "Hubs")]
    [InlineData("hubs/customise", "Your arrangement")]
    [InlineData("explore", "Explore")]
    [InlineData("specialists", "Specialists")]
    public async Task Home_added_destinations_pass_actual_Browse_canvas_admission_and_create_the_composed_owner(string route, string title)
    {
        using var lifetime = new CancellationTokenSource();
        var modes = new Registry(); var paths = new LayoutPaths(); var catalogue = new EmptySpecialistCatalogue();
        var home = new UnifiedHomeContentFactory(modes, () => throw new InvalidOperationException("Unused Dashboard"),
            () => throw new InvalidOperationException("Unused Models"), (_, _) => false,
            (_, _, _) => throw new InvalidOperationException("Navigation does not invoke AI."),
            (_, _) => Task.CompletedTask, () => true, lifetime.Token,
            explore: _ => new(modes, null, _ => Task.CompletedTask, _ => true, (_, _, _, _) => false),
            specialists: _ => new(catalogue, _ => Task.CompletedTask));
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: home, homeProfileId: "route-fixture");
        try
        {
            var address = "browse://home/" + route;
            Assert.True(home.RecognizesNestedCanvas(AppContentAddress.Parse(address)));
            var actual = await chrome.NavigateAsync(address).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(address, actual.SelectedTab.Address.Value); Assert.Equal(BrowseEngineState.NativeApp, actual.SelectedTab.EngineState);
            var owner = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(chrome.ObserveOriginalAppContent(actual.SelectedTabId));
            Assert.Equal(address, owner.Address.Value); Assert.Equal(title, owner.Title); Assert.True(owner.IsPublicationCurrent);
            var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(owner.OriginalNativeView).OriginalContent);
            Assert.Equal(title, page.TabTitle);
            if (route == "explore") Assert.Contains(AuthoredCuiControls.Enumerate(page), child => child is HomeExploreDestinationPage);
            if (route == "specialists") Assert.Contains(AuthoredCuiControls.Enumerate(page), child => child is HomeSpecialistsDestinationPage);
            Assert.Equal(0, catalogue.Mutations);
            var count = actual.Tabs.Count; var refused = await chrome.NavigateAsync("browse://newtab/" + route);
            Assert.Equal(address, refused.SelectedTab.Address.Value); Assert.Equal(count, refused.Tabs.Count);
            Assert.Contains("not registered", refused.Status, StringComparison.Ordinal);
        }
        finally { await chrome.DisposeAsync(); Directory.Delete(paths.DataDirectory, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData("explore")]
    [InlineData("specialists")]
    public async Task Home_added_destination_without_its_composed_owner_is_refused_before_replacing_the_current_tab(string route)
    {
        using var lifetime = new CancellationTokenSource(); var home = Factory(new Registry(), lifetime.Token); var paths = new LayoutPaths();
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: home, homeProfileId: "route-fixture");
        try
        {
            var before = chrome.State; var owner = chrome.ObserveOriginalAppContent(before.SelectedTabId);
            Assert.False(home.RecognizesNestedCanvas(AppContentAddress.Parse("browse://home/" + route)));
            var after = await chrome.NavigateAsync("browse://home/" + route);
            Assert.Equal(before.SelectedTabId, after.SelectedTabId); Assert.Equal(before.Tabs.Count, after.Tabs.Count);
            Assert.Same(owner, chrome.ObserveOriginalAppContent(after.SelectedTabId)); Assert.Contains("not registered", after.Status, StringComparison.Ordinal);
        }
        finally { await chrome.DisposeAsync(); Directory.Delete(paths.DataDirectory, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Actual_Home_Explore_handoff_retains_its_conversation_and_restores_it_after_in_place_Home_navigation()
    {
        using var lifetime = new CancellationTokenSource(); var paths = new LayoutPaths(); var modes = new Registry();
        var native = new ExploreRouteNativeFactory(); BrowseNativeWorkspace? workspace = null; Task? navigation = null;
        var home = new UnifiedHomeContentFactory(modes, () => throw new InvalidOperationException("Unused Dashboard"),
            () => throw new InvalidOperationException("Unused Models"),
            (intent, settled) => workspace!.TryQueueNavigationOriginal(intent, settled, out navigation),
            (_, _, _) => throw new InvalidOperationException("The unsent-chat owner is not used by Explore."),
            (_, _) => Task.CompletedTask, () => true, lifetime.Token,
            explore: request => new(modes, new ExploreRoutePlanner(), _ => Task.CompletedTask, mode => mode.Key == "write",
                (address, brief, id, settled) => workspace!.TryQueueNavigationOriginal(new(request.TabId, address, true)
                    { Brief = new(id, brief) }, settled, out navigation)));
        var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: native, internalContentFactory: home, homeProfileId: "route-fixture");
        workspace = new(chrome, _ => true); var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        var bound = TimeSpan.FromSeconds(20);
        try
        {
            RegisterProductionImageResolver(window); window.Show(); await window.InitializeAsync(lifetime.Token).WaitAsync(bound);
            await chrome.NavigateAsync("browse://home/explore").WaitAsync(bound); await RenderAsync();
            var sourceId = chrome.State.SelectedTabId;
            var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(chrome.ObserveOriginalAppContent(sourceId));
            var page = Assert.IsType<UnifiedHomePage>(Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView).OriginalContent);
            var explore = Assert.Single(page.GetVisualDescendants().OfType<HomeExploreDestinationPage>());
            var composer = Assert.Single(explore.GetVisualDescendants().OfType<TextBox>(), control => control.Name == "home-explore-composer");
            const string brief = "Write a document about designing a game.";
            composer.Text = brief; await explore.DispatchAsync("ExploreFind", null);
            Assert.Equal(2, explore.Conversation.Count); Assert.Equal(0, native.Creations);
            var choice = Assert.Single(explore.Choices);
            Assert.Single(explore.GetVisualDescendants().OfType<Button>(), control => control.Name == "home-explore-continue")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await explore.WhenActionsIdleAsync().WaitAsync(bound); Assert.NotNull(navigation); await navigation!.WaitAsync(bound); await RenderAsync();
            var destination = chrome.State.SelectedTabId;
            Assert.NotEqual(sourceId, destination); Assert.Equal("9to1://write", chrome.State.SelectedTab.Address.Value);
            Assert.Equal(1, native.Creations); Assert.Same(session, chrome.ObserveOriginalAppContent(sourceId));
            Assert.True(workspace.TryGetValue("ExploreBriefText", out var text)); Assert.Equal(brief, text);
            Assert.True(workspace.TryGetValue("ExploreBriefTarget", out var target)); Assert.NotNull(target);
            await chrome.SelectTabAsync(sourceId); await RenderAsync();
            await page.DispatchAsync("home.apps", null); Assert.Equal("Apps", page.TabTitle);
            Assert.Equal(2, explore.Conversation.Count); Assert.Same(choice, Assert.Single(explore.Choices));
            await chrome.SelectTabAsync(destination); await RenderAsync();
            await workspace.DispatchAsync("9to1.Browse.BackToExplore", target); await RenderAsync();
            Assert.Equal(sourceId, chrome.State.SelectedTabId); Assert.Equal("Explore", page.TabTitle);
            Assert.True(explore.IsEffectivelyVisible); Assert.Equal(brief, explore.OriginalBrief);
            Assert.Equal(2, explore.Conversation.Count); Assert.Same(choice, Assert.Single(explore.Choices)); Assert.Equal(1, native.Creations);
        }
        finally
        {
            window.Close(); if (window.OriginalClosePreparation is { } preparation) Assert.True(await preparation.WaitAsync(bound));
            Dispatcher.UIThread.RunJobs(); Assert.NotNull(window.OriginalClose); await window.OriginalClose!.WaitAsync(bound);
            Directory.Delete(paths.DataDirectory, recursive: true);
        }
        async Task RenderAsync()
        { await Task.WhenAll(workspace.OriginalNotifications).WaitAsync(bound); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    private sealed class ExploreRoutePlanner : IHomeExplorePlanner
    {
        public Task<HomeExplorePlanResult> PlanAsync(IReadOnlyList<HomeExploreTurn> conversation, IReadOnlyList<HomeAppItem> apps, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var app = apps.Single(item => item.Key == "write" && item.CanonicalId is not null);
            var plan = new HomeExplorePlan("Write owns this document.", [new(app.StableIdentity, "A document about game design", "The deliverable is a document.")]);
            return Task.FromResult(new HomeExplorePlanResult(true, plan.Message, plan));
        }
    }
    private sealed class EmptySpecialistCatalogue : ICatalogRepository
    {
        internal int Mutations;
        public Task<IReadOnlyList<AgentDefinition>> GetAllAgentsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AgentDefinition>>([]);
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAgentAsync(AgentDefinition value, CancellationToken token) => Mutate();
        public Task UpsertPromptAsync(PromptDefinition value, CancellationToken token) => Mutate();
        public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken token) => Mutate();
        public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken token) => Mutate();
        public Task DeleteCustomAgentAsync(Guid id, CancellationToken token) => Mutate();
        public Task DeleteCustomPromptAsync(Guid id, CancellationToken token) => Mutate();
        private Task Mutate() { ++Mutations; throw new InvalidOperationException("Navigation must not edit definitions."); }
    }
    private sealed class ExploreRouteNativeFactory : IBrowseAppContentFactory
    {
        internal int Creations;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        { ++Creations; Assert.Equal("9to1://write", request.Address.Value); return new ExploreRouteNativeSession(request.Address); }
    }
    private sealed class ExploreRouteNativeSession(AppContentAddress address) : IBrowseNativeAppContentSession
    {
        private Control? _view;
        public AppContentAddress Address => address;
        public string Title => "Write fixture";
        public string IconKey => "write";
        public event EventHandler? StateChanged { add { } remove { } }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; }
        public Task? OriginalClose { get; private set; }
        public Control OriginalNativeView => _view ?? throw new InvalidOperationException("The fixture is uninitialized.");
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??= Dispatcher.UIThread.InvokeAsync(() =>
        { token.ThrowIfCancellationRequested(); _view = new TextBlock { Text = "No document has been created." }; }).GetTask();
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
    }
}
