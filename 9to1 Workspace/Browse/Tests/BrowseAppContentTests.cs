using Haven.Core;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Fact]
    public async Task Native_app_replaces_same_tab_using_original_profile_and_no_web_origin_or_engine()
    {
        var engines = new ChromiumFixtureFactory(includeGecko: true); var apps = new AppFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), engines,
            appContentFactory: apps, homeProfileId: "original-home-profile");
        var id = chrome.State.SelectedTabId; var renderer = Assert.Single(engines.Hosts);
        await chrome.NavigateAsync("9to1://Write/document-42");
        var owner = Assert.Single(apps.Sessions); var request = Assert.Single(apps.Requests);
        Assert.Equal(id, request.TabId); Assert.Equal("original-home-profile", request.ProfileId);
        Assert.Equal(id, chrome.State.SelectedTabId); Assert.Equal("9to1://write/document-42", chrome.State.SelectedTab.Address.Value);
        Assert.Null(chrome.State.SelectedTab.Address.WebUri); Assert.Equal(1, renderer.CloseCalls);
        Assert.Equal(BrowseEngineState.NativeApp, chrome.State.SelectedTab.EngineState);
        Assert.Same(owner, chrome.ObserveOriginalAppContent(id)); Assert.Null(chrome.ObserveOriginalEngine(id));
        Assert.Empty(chrome.State.History); Assert.Equal(BrowseTransportSecurity.NoPage, chrome.State.Security.Transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium));
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.ToggleBookmarkAsync());
        Assert.True(owner.OriginalInitialization!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Failed_app_initialization_retains_actual_owner_task_and_does_not_replace_live_web_tab()
    {
        var engines = new ChromiumFixtureFactory(includeGecko: true);
        var failure = new IOException("Original app startup failed"); var apps = new AppFixtureFactory { InitializationFailure = failure };
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), engines, appContentFactory: apps, homeProfileId: "profile");
        RetainedFailedOwners.Add(chrome);
        var before = chrome.State.SelectedTab; var renderer = Assert.Single(engines.Hosts);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => chrome.NavigateAsync("9to1://write/id")));
        var owner = Assert.Single(apps.Sessions);
        Assert.Equal(before, chrome.State.SelectedTab); Assert.Equal(0, renderer.CloseCalls);
        Assert.Contains(owner.OriginalInitialization!, chrome.OriginalNativeSources);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions); Assert.Equal(1, owner.CloseCalls);
    }

    [Fact]
    public async Task Close_releases_held_candidate_initialization_before_joining_original_navigation()
    {
        var apps = new AppFixtureFactory { HoldInitializationUntilClose = true };
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps, homeProfileId: "profile");
        RetainedFailedOwners.Add(chrome);
        var navigation = chrome.NavigateAsync("9to1://write/pending");
        var owner = await apps.Created.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await owner.InitializationStarted.Task;
        Assert.False(owner.OriginalInitialization!.IsCompleted);
        var close = chrome.DisposeAsync().AsTask();
        await close.WaitAsync(TimeSpan.FromSeconds(10));
        await navigation;
        Assert.Equal(1, owner.CloseCalls); Assert.True(owner.OriginalInitialization.IsCompletedSuccessfully);
        Assert.Same(close, chrome.OriginalClose); Assert.True(owner.CanRetireOriginalView);
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task New_tab_admission_driver_is_retained_until_close_releases_held_initialization()
    {
        var apps = new AppFixtureFactory { HoldInitializationUntilClose = true };
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps, homeProfileId: "profile");
        RetainedFailedOwners.Add(chrome);
        var admission = chrome.OpenContentAsync(AppContentAddress.Parse("9to1://write/pending"), openInNewTab: true);
        var owner = await apps.Created.Task.WaitAsync(TimeSpan.FromSeconds(10)); await owner.InitializationStarted.Task;
        Assert.Contains(admission, chrome.OriginalNativeSources);
        var close = chrome.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission);
        Assert.Equal(1, owner.CloseCalls); Assert.True(owner.OriginalInitialization!.IsCompletedSuccessfully);
        Assert.True(owner.CanRetireOriginalView); Assert.Same(close, chrome.OriginalClose);
    }

    [Fact]
    public async Task Mismatched_initialization_tasks_are_both_retained_and_released_by_actual_owner_close()
    {
        var apps = new AppFixtureFactory { MismatchInitialization = true };
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps, homeProfileId: "profile");
        RetainedFailedOwners.Add(chrome);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.NavigateAsync("9to1://write/mismatch"));
        var owner = Assert.Single(apps.Sessions);
        Assert.NotSame(owner.ReturnedInitialization, owner.OriginalInitialization);
        Assert.Contains(owner.ReturnedInitialization!, chrome.OriginalNativeSources);
        Assert.Contains(owner.OriginalInitialization!, chrome.OriginalNativeSources);
        await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(owner.ReturnedInitialization!.IsCompletedSuccessfully);
        Assert.True(owner.OriginalInitialization!.IsCompletedSuccessfully); Assert.Equal(1, owner.CloseCalls);
    }

    [Fact]
    public async Task Mismatched_close_tasks_are_both_joined_and_failed_contract_keeps_original_owner()
    {
        var apps = new AppFixtureFactory { MismatchClose = true };
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps, homeProfileId: "profile");
        RetainedFailedOwners.Add(chrome);
        await chrome.NavigateAsync("9to1://write/mismatch"); var id = chrome.State.SelectedTabId;
        var owner = Assert.Single(apps.Sessions);
        var close = chrome.DisposeAsync().AsTask();
        // The preflight is asynchronous. Wait for the original close to be issued,
        // without substituting a completion receipt for either owner task.
        await owner.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotSame(owner.ReturnedClose, owner.OriginalClose);
        owner.CloseReturned.SetResult(); Assert.False(close.IsCompleted);
        owner.CloseReported.SetResult();
        await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(owner, chrome.ObserveOriginalAppContent(id)); Assert.Equal(1, owner.CloseCalls);
        Assert.Same(close, chrome.OriginalClose);
    }

    [Fact]
    public async Task Retirement_observer_failure_cannot_skip_actual_native_owner_close()
    {
        var apps = new AppFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps, homeProfileId: "profile");
        RetainedFailedOwners.Add(chrome);
        await chrome.NavigateAsync("9to1://write/id"); var owner = Assert.Single(apps.Sessions);
        var failure = new IOException("Retirement publication failed"); var followingObserver = false;
        chrome.RetirementStarted += (_, _) => throw failure;
        chrome.RetirementStarted += (_, _) => followingObserver = true;
        var retained = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, retained.Flatten().InnerExceptions);
        Assert.True(followingObserver); Assert.Equal(1, owner.CloseCalls); Assert.True(owner.CanRetireOriginalView);
    }

    [Fact]
    public async Task Unsaved_veto_keeps_app_tab_view_owner_and_navigation_alive()
    {
        var apps = new AppFixtureFactory(); var engines = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), engines, appContentFactory: apps, homeProfileId: "profile");
        await chrome.NewTabAsync(false); await chrome.NavigateAsync("9to1://write/id");
        var id = chrome.State.SelectedTabId; var owner = Assert.Single(apps.Sessions); owner.Unsaved = true;
        Assert.False(await chrome.TryPrepareToCloseAsync());
        var veto = await Assert.ThrowsAsync<BrowseCloseVetoException>(() => chrome.DisposeAsync().AsTask());
        Assert.IsAssignableFrom<InvalidOperationException>(veto);
        Assert.Null(chrome.OriginalClose); Assert.Equal(0, owner.CloseCalls);
        await chrome.CloseTabAsync(id); Assert.Same(owner, chrome.ObserveOriginalAppContent(id));
        await chrome.NavigateAsync("https://next.example/");
        Assert.Equal("9to1://write/id", chrome.State.SelectedTab.Address.Value); Assert.Equal(0, owner.CloseCalls);
        Assert.Null(chrome.OriginalClose);
        owner.Unsaved = false; await chrome.CloseTabAsync(id);
        Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == id); Assert.Equal(1, owner.CloseCalls);
    }

    [Fact]
    public async Task Ordinary_close_veto_can_retry_but_preparation_fault_is_preserved()
    {
        var apps = new AppFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps, homeProfileId: "profile");
        await chrome.NavigateAsync("9to1://write/id");
        var owner = Assert.Single(apps.Sessions);
        owner.Unsaved = true;
        await Assert.ThrowsAsync<BrowseCloseVetoException>(() => chrome.DisposeAsync().AsTask());
        Assert.Null(chrome.OriginalClose); Assert.Equal(0, owner.CloseCalls);

        owner.Unsaved = false;
        var failure = new IOException("Original preparation failed");
        owner.PreparationFailure = failure;
        var fault = await Assert.ThrowsAsync<IOException>(() => chrome.DisposeAsync().AsTask());
        Assert.Same(failure, fault);
        Assert.Null(chrome.OriginalClose); Assert.Equal(0, owner.CloseCalls);

        owner.PreparationFailure = null;
        var admitted = chrome.DisposeAsync().AsTask();
        var retained = await Assert.ThrowsAsync<AggregateException>(() => admitted);
        Assert.Contains(failure, retained.Flatten().InnerExceptions);
        Assert.Same(admitted, chrome.OriginalClose);
        Assert.Same(admitted, chrome.DisposeAsync().AsTask());
        Assert.Equal(1, owner.CloseCalls);
    }

    [Fact]
    public async Task Failed_web_factory_keeps_original_native_content_and_can_retry_same_tab()
    {
        var apps = new AppFixtureFactory(); var engines = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), engines, appContentFactory: apps, homeProfileId: "profile");
        RetainedFailedOwners.Add(chrome);
        await chrome.NavigateAsync("9to1://write/id"); var before = chrome.State.SelectedTab; var owner = Assert.Single(apps.Sessions);
        engines.FailEngine = BrowseEngineKind.Gecko;
        await Assert.ThrowsAsync<IOException>(() => chrome.NavigateAsync("https://next.example/"));
        Assert.Equal(before, chrome.State.SelectedTab); Assert.Same(owner, chrome.ObserveOriginalAppContent(before.Id)); Assert.Equal(0, owner.CloseCalls);
        engines.FailEngine = null; await chrome.NavigateAsync("https://next.example/");
        Assert.Equal(before.Id, chrome.State.SelectedTabId); Assert.Equal(1, owner.CloseCalls);
        Assert.True(chrome.State.SelectedTab.Address.IsWeb); Assert.Null(chrome.ObserveOriginalAppContent(before.Id));
        await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Restore_and_reopen_reregister_exact_native_resource_and_profile_without_duplicate_data()
    {
        var paths = new PreservedPaths(); var apps = new AppFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: apps, homeProfileId: "profile-original");
        await chrome.NewTabAsync(false); await chrome.NavigateAsync("9to1://write/document-42");
        var id = chrome.State.SelectedTabId;
        await chrome.CloseTabAsync(id); await chrome.ReopenClosedTabAsync();
        Assert.Equal(id, chrome.State.SelectedTabId); Assert.Equal(2, apps.Sessions.Count);
        await chrome.DisposeAsync();
        var restoredFactory = new AppFixtureFactory();
        await using var restored = await BrowseChrome.CreateAsync(paths, appContentFactory: restoredFactory, homeProfileId: "profile-original");
        var request = Assert.Single(restoredFactory.Requests);
        Assert.Equal(id, request.TabId); Assert.Equal("profile-original", request.ProfileId);
        Assert.Equal("9to1://write/document-42", request.Address.Value); Assert.Empty(restored.State.History);
    }

    [Fact]
    public async Task Late_native_callback_cannot_rewrite_replaced_tab_or_its_canonical_route()
    {
        var apps = new AppFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps, homeProfileId: "profile");
        await chrome.NavigateAsync("9to1://write/id"); var owner = Assert.Single(apps.Sessions);
        var late = owner.CaptureNotification();
        await chrome.NavigateAsync("browse://history"); var before = chrome.State.SelectedTab;
        owner.ChangeResource("9to1://write/other", "Late other resource", notify: false); late();
        Assert.Equal(before, chrome.State.SelectedTab);
    }

    [Fact]
    public async Task Genuine_internal_composition_keeps_stable_home_and_newtab_routes_without_renderers()
    {
        var factory = new AppFixtureFactory(); var paths = new PreservedPaths();
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: factory, homeProfileId: "profile");
        var home = Assert.Single(chrome.State.Tabs); Assert.True(home.IsHome); Assert.False(home.IsCloseable);
        await chrome.CloseTabAsync(home.Id); Assert.Equal(home.Id, chrome.State.SelectedTabId);
        await chrome.NewTabAsync(false); var newTab = chrome.State.SelectedTab;
        Assert.Equal("browse://newtab", newTab.Address.Value); Assert.True(newTab.IsCloseable);
        await chrome.SelectTabAsync(home.Id); Assert.Equal(home.Id, chrome.State.SelectedTabId);
        await chrome.NavigateAsync("https://example.com/");
        Assert.Equal(3, chrome.State.Tabs.Count); Assert.Contains(chrome.State.Tabs, tab => tab.Id == home.Id && tab.IsHome);
        await chrome.DisposeAsync();
        await using var restored = await BrowseChrome.CreateAsync(paths, internalContentFactory: new AppFixtureFactory(), homeProfileId: "profile");
        Assert.Equal(home.Id, Assert.Single(restored.State.Tabs, tab => tab.IsHome).Id);
    }

    [Fact]
    public async Task Legacy_assistants_route_is_canonicalized_before_factory_admission_and_restoration()
    {
        var paths = new PreservedPaths(); var apps = new AppFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: apps, homeProfileId: "profile");
        await chrome.NavigateAsync("9to1://assistants/library/assistant-42");
        Assert.Equal("9to1://spaces/assistants/library/assistant-42", Assert.Single(apps.Requests).Address.Value);
        var id = chrome.State.SelectedTabId; await chrome.DisposeAsync();
        using (var data = new BrowserDataService(paths))
            await data.SaveTabsAsync([new(id, "Legacy assistant", "9to1://assistants/library/assistant-42", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow) { ProfileId = "profile" }], CancellationToken.None);
        var restoredFactory = new AppFixtureFactory();
        await using var restored = await BrowseChrome.CreateAsync(paths, appContentFactory: restoredFactory, homeProfileId: "profile");
        Assert.Equal("9to1://spaces/assistants/library/assistant-42", Assert.Single(restoredFactory.Requests).Address.Value);
        Assert.Equal(id, restored.State.SelectedTabId);
    }

    [Fact]
    public async Task Missing_native_factory_preserves_typed_unavailable_route_without_external_search_or_web_permission()
    {
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths());
        await chrome.NavigateAsync("9to1://unknown/id");
        Assert.Equal("9to1://unknown/id", chrome.State.SelectedTab.Address.Value);
        Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
        Assert.Null(chrome.State.SelectedTab.Address.WebUri); Assert.Empty(chrome.State.History);
        var before = chrome.State.SelectedTab; await chrome.NavigateAsync("cui://unknown/id"); Assert.Equal(before, chrome.State.SelectedTab);
    }

    private sealed class AppFixtureFactory : IBrowseAppContentFactory, IBrowseInternalContentFactory
    {
        public Exception? InitializationFailure;
        public bool HoldInitializationUntilClose, MismatchInitialization, MismatchClose;
        public TaskCompletionSource<AppFixtureSession> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<BrowseAppContentRequest> Requests { get; } = [];
        public List<AppFixtureSession> Sessions { get; } = [];
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        { Requests.Add(request); var session = new AppFixtureSession(request.Address, InitializationFailure, HoldInitializationUntilClose, MismatchInitialization, MismatchClose); Sessions.Add(session); Created.TrySetResult(session); return session; }
    }

    private sealed class AppFixtureSession(AppContentAddress address, Exception? initializationFailure, bool holdInitialization = false, bool mismatchInitialization = false, bool mismatchClose = false) : IBrowseAppContentSession
    {
        private readonly TaskCompletionSource _initializationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _reportedInitializationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseReported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? ReturnedInitialization { get; private set; }
        public Task? ReturnedClose { get; private set; }
        public AppContentAddress Address { get; private set; } = address;
        public string Title { get; private set; } = "Actual fixture resource";
        public string IconKey => "document";
        public event EventHandler? StateChanged;
        public bool Unsaved;
        public Exception? PreparationFailure;
        public int CloseCalls;
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; }
        public Task? OriginalClose { get; private set; }
        public TaskCompletionSource InitializationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializeAsync(CancellationToken token = default)
        {
            ReturnedInitialization ??= holdInitialization || mismatchInitialization ? _initializationGate.Task : initializationFailure is null ? Task.CompletedTask : Task.FromException(initializationFailure);
            OriginalInitialization ??= mismatchInitialization ? _reportedInitializationGate.Task : ReturnedInitialization;
            InitializationStarted.TrySetResult(); return ReturnedInitialization;
        }
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => PreparationFailure is { } failure
            ? Task.FromException<bool>(failure) : Task.FromResult(!Unsaved);
        public Task CloseAndDrainAsync()
        {
            if (ReturnedClose is not null) return ReturnedClose;
            CloseCalls++; _initializationGate.TrySetResult(); _reportedInitializationGate.TrySetResult();
            ReturnedClose = mismatchClose ? CloseReturned.Task : Task.CompletedTask;
            OriginalClose = mismatchClose ? CloseReported.Task : ReturnedClose;
            CloseStarted.TrySetResult(); return ReturnedClose;
        }
        public Action CaptureNotification() { var original = StateChanged; return () => original?.Invoke(this, EventArgs.Empty); }
        public void ChangeResource(string value, string title, bool notify = true)
        { Address = AppContentAddress.Parse(value); Title = title; if (notify) StateChanged?.Invoke(this, EventArgs.Empty); }
    }
}
