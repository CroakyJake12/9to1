using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

public sealed class NativeAppContentRegistryTests
{
    private static readonly ModeDefinition Files = BuiltInModeSeed.Modes.Single(app => app.Key == "files");
    private static BrowseAppContentRequest Request(string address = "9to1://files") =>
        new(Guid.NewGuid(), AppContentAddress.Parse(address), "original-profile", BrowserTabPrivacy.Standard);
    private static NativeAppContentRegistry Registry(RegistrySource source, Factory factory, Func<bool>? current = null) =>
        new(source, [new(Files.Id, Files.Key, HavenAppRouteKind.Files, factory)], current ?? (() => true));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_close_acquires_the_actual_native_preparation_on_ui_without_holding_chrome_gate(bool window)
    {
        var owner = new StrongOwner(AppContentAddress.Parse("9to1://files"));
        BrowseChrome? chrome = null;
        owner.OnBegin = () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.VerifyAccess();
            // Production native host admission reads this SAME Chrome property.
            Assert.Null(chrome!.OriginalClose);
        };
        var paths = new StrongPaths();
        chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: Registry(new(), new(_ => owner)),
            homeProfileId: "original-profile");
        try
        {
            await chrome.OpenContentAsync(owner.Address);
            var tab = chrome.State.SelectedTabId;
            if (window)
            {
                var preparation = (await chrome.PrepareCloseAsync())!;
                await Task.Run(preparation.BeginClose).WaitAsync(StrongBound);
                Assert.True(chrome.OriginalClose!.IsCompletedSuccessfully);
            }
            else
            {
                await Task.Run(() => chrome.CloseTabAsync(tab)).WaitAsync(StrongBound);
                Assert.DoesNotContain(chrome.State.Tabs, item => item.Id == tab);
            }
            Assert.Equal(1, owner.Begins);
            Assert.True(owner.CanRetireOriginalView);
        }
        finally
        {
            try { await chrome.DisposeAsync().AsTask().WaitAsync(StrongBound); }
            finally { Directory.Delete(paths.DataDirectory, true); }
        }
    }

    [AvaloniaFact]
    public async Task Production_Write_and_Mesh_registrations_keep_their_actual_page_factory_identity()
    {
        foreach (var key in new[] { "write", "mesh" })
        {
            var app = BuiltInModeSeed.Modes.Single(mode => mode.Key == key);
            var actual = new NativeOwner(AppContentAddress.Parse("9to1://" + key));
            var factory = new Factory(_ => actual);
            var unused = new Factory(_ => throw new InvalidOperationException("Wrong native factory"));
            var bindings = NativeAppContentRegistry.NativeAppContentRegistrations.Build(unused, unused, unused,
                null, null, null, key == "write" ? factory : null, key == "mesh" ? factory : null);
            var registry = new NativeAppContentRegistry(new RegistrySource { App = app }, bindings, () => true);
            var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(registry.CreateSession(Request("9to1://" + key)));
            await session.InitializeAsync(); Assert.Same(actual.View, session.OriginalNativeView);
            Assert.Equal(1, factory.Calls); Assert.Equal(0, unused.Calls);
            Assert.True(await session.TryPrepareToCloseAsync()); await session.CloseAndDrainAsync();
            Assert.True(actual.CanRetireOriginalView);
        }
    }

    [AvaloniaFact]
    public async Task Production_Canvas_binding_uses_the_canonical_app_registration_and_never_an_unregistered_fallback()
    {
        var canvas = BuiltInModeSeed.Modes.Single(app => app.Key == "canvas");
        var source = new RegistrySource { App = canvas };
        var other = new Factory(_ => throw new InvalidOperationException("Another app cannot acquire Canvas."));
        var owner = new NativeOwner(AppContentAddress.Parse("9to1://canvas"));
        var factory = new Factory(_ => owner);
        var bindings = NativeAppContentRegistry.NativeAppContentRegistrations.Build(other, other, other, null, factory).ToArray();
        var binding = Assert.Single(bindings, item => item.AppKey == "canvas");
        Assert.Equal(canvas.Id, binding.AppId); Assert.Equal(HavenAppRouteKind.ModeWorkspace, binding.RouteKind);
        var registry = new NativeAppContentRegistry(source, bindings, () => true);
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(registry.CreateSession(Request("9to1://canvas")));
        Assert.Equal(0, factory.Calls);
        await session.InitializeAsync(); Assert.Same(owner.View, session.OriginalNativeView);
        Assert.Equal(1, factory.Calls); Assert.Equal(0, other.Calls);
        await session.CloseAndDrainAsync();

        source.App = canvas with { IsEnabled = false };
        var disabled = registry.CreateSession(Request("9to1://canvas"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => disabled.InitializeAsync());
        await disabled.CloseAndDrainAsync();
        Assert.True(disabled.OriginalInitialization!.IsFaulted);
        Assert.True(disabled.CanRetireOriginalView);
        Assert.Equal(1, factory.Calls);
        var unbound = new NativeAppContentRegistry(source,
            NativeAppContentRegistry.NativeAppContentRegistrations.Build(other, other, other), () => true);
        Assert.Throws<NativeAppContentRegistry.NativeAppContentAdmissionRefusal>(() => unbound.CreateSession(Request("9to1://canvas")));
        Assert.Equal(0, other.Calls);
    }

    [AvaloniaFact]
    public async Task Refused_start_before_owner_acquisition_can_drain_without_erasing_original_failure()
    {
        var source = new RegistrySource { App = Files with { IsEnabled = false } };
        var factory = new Factory(_ => throw new InvalidOperationException("No owner should be acquired"));
        var session = Registry(source, factory).CreateSession(Request());
        var startup = session.InitializeAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => startup);
        await session.CloseAndDrainAsync();
        Assert.Equal(0, factory.Calls);
        Assert.True(startup.IsFaulted);
        Assert.Same(startup, session.OriginalInitialization);
        Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_start_drains_only_when_the_actual_native_owner_cleanup_succeeds(bool cleanupFails)
    {
        var request = Request();
        var startupFailure = new IOException("Original failed initialization");
        var cleanupFailure = new IOException("Original cleanup failure");
        var owner = new NativeOwner(request.Address)
        {
            InitializeBody = _ => Task.FromException(startupFailure),
            CloseBody = () => cleanupFails ? Task.FromException(cleanupFailure) : Task.CompletedTask
        };
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        var startup = session.InitializeAsync();
        Assert.Same(startupFailure, await Assert.ThrowsAsync<IOException>(() => startup));
        if (cleanupFails)
        {
            var close = await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
            Assert.Contains(cleanupFailure, close.Flatten().InnerExceptions);
            Assert.False(session.CanRetireOriginalView);
        }
        else
        {
            await session.CloseAndDrainAsync();
            Assert.True(session.CanRetireOriginalView);
        }
        Assert.True(startup.IsFaulted);
        Assert.Same(startup, session.OriginalInitialization);
        Assert.Equal(1, owner.CloseCalls);
    }

    [AvaloniaFact]
    public async Task Failed_start_and_failed_original_task_observation_both_block_clean_retirement()
    {
        var startupFailure = new IOException("Exact startup failure");
        var observationFailure = new IOException("Original initialization getter failed");
        var reads = 0;
        var owner = new NativeOwner(Request().Address)
        {
            InitializeBody = _ => Task.FromException(startupFailure),
            InitializationObservation = original => ++reads == 2 ? throw observationFailure : original
        };
        var session = Registry(new(), new(_ => owner)).CreateSession(Request());
        var startup = await Assert.ThrowsAsync<AggregateException>(() => session.InitializeAsync());
        Assert.Contains(startupFailure, startup.Flatten().InnerExceptions);
        Assert.Contains(observationFailure, startup.Flatten().InnerExceptions);
        var close = await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
        Assert.Contains(startupFailure, close.Flatten().InnerExceptions);
        Assert.Contains(observationFailure, close.Flatten().InnerExceptions);
        Assert.False(session.CanRetireOriginalView);
        Assert.Equal(1, owner.CloseCalls);
    }

    [AvaloniaFact]
    public async Task Three_restored_failed_Boards_starts_close_individually_then_the_window_drains()
    {
        var boards = BuiltInModeSeed.Modes.Single(app => app.Key == "boards");
        var failures = new List<Exception>();
        var factory = new Factory(_ =>
        {
            var failure = new UnauthorizedAccessException("The one-use board selection is no longer available.");
            failures.Add(failure);
            throw failure;
        });
        var bindings = new[] { new NativeAppContentRegistration(boards.Id, boards.Key, HavenAppRouteKind.ModeWorkspace, factory) };
        var registry = new NativeAppContentRegistry(new RegistrySource { App = boards }, bindings, () => true);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var rows = ids.Select(id => new BrowserTabState(id, "Saved board", "9to1://boards/notebooks/" + id.ToString("D"),
            BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow) { ProfileId = "original-profile" }).ToArray();
        var paths = new StrongPaths();
        var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: registry, homeProfileId: "original-profile",
            tabSessionPersistence: new StartupProjection(new(rows, ids[0])));
        try
        {
            Assert.Equal(3, factory.Calls);
            Assert.Equal(3, chrome.DrainedStartupFailures.Count);
            foreach (var id in new[] { ids[1], ids[0], ids[2] })
            {
                Assert.Equal(BrowseEngineState.Unsupported, chrome.State.Tabs.Single(tab => tab.Id == id).EngineState);
                await chrome.CloseTabAsync(id);
                Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == id);
            }
            Assert.Equal(3, factory.Calls); // No detached retry may resurrect a failed tab.
            Assert.All(chrome.DrainedStartupFailures, record =>
                Assert.Contains(record.OriginalInitialization.Exception!.InnerException!, failures));
            await chrome.DisposeAsync();
        }
        finally
        {
            await chrome.DisposeAsync();
            Directory.Delete(paths.DataDirectory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registry_and_Chrome_retire_startup_lineage_only_after_actual_cleanup(bool cleanupFails)
    {
        var startupFailure = new IOException("Exact native initialization failure");
        var cleanupFailure = new IOException("Exact native resource cleanup failure");
        NativeOwner? native = null;
        var factory = new Factory(request => native = new NativeOwner(request.Address)
        {
            InitializeBody = _ => Task.FromException(startupFailure),
            CloseBody = () => cleanupFails ? Task.FromException(cleanupFailure) : Task.CompletedTask
        });
        var paths = new StrongPaths();
        var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: Registry(new(), factory), homeProfileId: "original-profile");
        var operation = chrome.NavigateAsync("9to1://files/failing");
        var outer = Forward(operation);
        chrome.LinkAdmissionPropagation(outer, operation);
        var observed = await Record.ExceptionAsync(() => outer);
        Assert.NotNull(native);
        Assert.Same(startupFailure, native.OriginalInitialization!.Exception!.InnerException);
        Assert.True(native.OriginalInitialization.IsFaulted);
        Assert.Equal(1, native.CloseCalls);
        if (cleanupFails)
        {
            var failure = Assert.IsType<AggregateException>(observed);
            Assert.Contains(cleanupFailure, failure.Flatten().InnerExceptions);
            Assert.Empty(chrome.DrainedStartupFailures);
            Assert.False(chrome.TryRetireAdmissionSource(outer));
            var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
            Assert.Contains(cleanupFailure, close.Flatten().InnerExceptions);
            // Failed resource retirement deliberately preserves its owner and store.
        }
        else
        {
            Assert.Same(startupFailure, observed);
            Assert.True(chrome.TryRetireAdmissionSource(outer));
            var diagnosis = Assert.Single(chrome.DrainedStartupFailures);
            Assert.Same(startupFailure, diagnosis.OriginalInitialization.Exception!.InnerException);
            Assert.False(chrome.TryRetireAdmissionSource(Task.FromException(startupFailure)));
            await chrome.DisposeAsync();
            Directory.Delete(paths.DataDirectory, true);
        }
        static async Task Forward(Task original) => await original;
    }

    private sealed class StartupProjection(BrowseTabSessionProjection projection) : IBrowseTabSessionPersistence
    {
        public Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken token) => Task.FromResult<BrowseTabSessionProjection?>(projection);
        public Task SaveAsync(BrowseTabSessionProjection snapshot, CancellationToken token) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Native_owner_is_initialized_once_and_its_exact_view_and_close_are_retained()
    {
        var request = Request(); var owner = new NativeOwner(request.Address);
        var factory = new Factory(_ => owner);
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Registry(new(), factory).CreateSession(request));
        Assert.Equal(0, factory.Calls);
        Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var original = session.InitializeAsync();
        Assert.Same(original, session.InitializeAsync());
        Assert.Same(original, session.OriginalInitialization);
        await original;
        Assert.Same(owner.View, session.OriginalNativeView);
        Assert.Equal(1, factory.Calls);
        Assert.True(await session.TryPrepareToCloseAsync());
        var close = session.CloseAndDrainAsync();
        Assert.Same(close, session.CloseAndDrainAsync());
        Assert.Same(close, session.OriginalClose);
        await close;
        Assert.True(session.CanRetireOriginalView);
        Assert.Equal(1, owner.CloseCalls);
    }

    [AvaloniaTheory]
    [InlineData("cui://files")]
    [InlineData("browse://files")]
    [InlineData("https://example.com/files")]
    public void Other_namespaces_cannot_reuse_privileged_native_factories(string address)
    {
        var factory = new Factory(_ => throw new Exception("Must never acquire an owner"));
        Assert.Throws<UnauthorizedAccessException>(() => Registry(new(), factory).CreateSession(Request(address)));
        Assert.Equal(0, factory.Calls);
    }

    [AvaloniaFact]
    public async Task Registration_is_checked_fresh_before_owner_acquisition_and_before_publication()
    {
        var source = new RegistrySource(); var request = Request();
        var owner = new NativeOwner(request.Address) { OnInitialize = () => source.App = Files with { IsEnabled = false } };
        var factory = new Factory(_ => owner); var session = Registry(source, factory).CreateSession(request);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.InitializeAsync());
        Assert.False(session.IsPublicationCurrent);
        await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
        Assert.Equal(1, owner.CloseCalls);

        var refused = Registry(source, factory).CreateSession(Request());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.InitializeAsync());
        Assert.Equal(1, factory.Calls);
    }

    [AvaloniaFact]
    public async Task A_copied_key_with_a_different_canonical_app_identity_is_refused()
    {
        var source = new RegistrySource { App = Files with { Id = Guid.NewGuid() } };
        var factory = new Factory(_ => throw new Exception("Not admitted"));
        var session = Registry(source, factory).CreateSession(Request());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.InitializeAsync());
        Assert.Equal(0, factory.Calls);
    }

    [AvaloniaFact]
    public async Task Unsaved_veto_preserves_view_and_admission_until_a_later_successful_intent()
    {
        var request = Request(); var owner = new NativeOwner(request.Address) { CanClose = false };
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Registry(new(), new(_ => owner)).CreateSession(request));
        await session.InitializeAsync();
        Assert.False(await session.TryPrepareToCloseAsync());
        Assert.True(session.IsPublicationCurrent); Assert.Null(session.OriginalClose);
        Assert.Same(owner.View, session.OriginalNativeView);
        owner.CanClose = true;
        Assert.True(await session.TryPrepareToCloseAsync());
        await session.CloseAndDrainAsync();
        Assert.True(owner.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Wrong_surface_type_keeps_the_created_owner_for_original_cleanup_even_on_failure()
    {
        var owner = new NonNativeOwner(Request().Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(Request());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync());
        Assert.Equal(0, owner.CloseCalls);
        var close = session.CloseAndDrainAsync();
        await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Same(close, session.CloseAndDrainAsync());
        Assert.Equal(1, owner.CloseCalls);
        Assert.False(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Failed_preparation_is_not_replaced_by_a_clean_attempt_or_lost_at_close()
    {
        var request = Request(); var owner = new NativeOwner(request.Address) { PreparationFailure = new IOException("save failed") };
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        await session.InitializeAsync();
        var original = session.TryPrepareToCloseAsync();
        await Assert.ThrowsAsync<IOException>(() => original);
        owner.PreparationFailure = null;
        Assert.Same(original, session.TryPrepareToCloseAsync());
        var error = await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message == "save failed");
    }

    [AvaloniaFact]
    public async Task An_active_publication_callback_cannot_join_its_own_retirement()
    {
        var request = Request(); var owner = new NativeOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        Exception? refusal = null;
        session.StateChanged += (_, _) => refusal = Record.Exception((Action)(() => session.CloseAndDrainAsync()));
        await session.InitializeAsync();
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.Null(session.OriginalClose);
        await session.CloseAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Owner_close_starts_before_joining_initialization_that_requires_its_cancellation()
    {
        var request = Request(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new NativeOwner(request.Address)
        {
            InitializeBody = _ => { entered.SetResult(); return raw.Task; },
            CloseBody = () => { raw.TrySetCanceled(); return Task.CompletedTask; }
        };
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        var initialization = session.InitializeAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var close = session.CloseAndDrainAsync();
        Assert.NotNull(await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
        Assert.True(close.IsCompleted); Assert.True(raw.Task.IsCanceled); Assert.True(initialization.IsCompleted);
        Assert.Equal(1, owner.CloseCalls); Assert.False(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Close_cancels_an_original_registry_read_before_any_native_owner_exists()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new RegistrySource { Read = async token =>
        {
            entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Files;
        } };
        var factory = new Factory(_ => throw new Exception("No owner may be acquired after retirement"));
        var session = Registry(source, factory).CreateSession(Request()); var initialization = session.InitializeAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var close = session.CloseAndDrainAsync();
        await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(initialization.IsCompleted); Assert.True(close.IsCompleted); Assert.Equal(0, factory.Calls);
        Assert.False(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Owner_close_cancels_active_preparation_before_joining_its_original_task()
    {
        var request = Request(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new NativeOwner(request.Address)
        {
            PrepareBody = _ => { entered.SetResult(); return raw.Task; },
            CloseBody = () => { raw.TrySetResult(false); return Task.CompletedTask; }
        };
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var prepare = session.TryPrepareToCloseAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await session.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(await prepare); Assert.Equal(1, owner.CloseCalls); Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Mismatched_initialization_receipt_keeps_returned_live_work_until_actual_settlement()
    {
        var request = Request(); var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new NativeOwner(request.Address)
        { InitializeBody = _ => raw.Task, ReportedInitialization = Task.CompletedTask };
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync());
        Assert.False(session.IsPublicationCurrent); Assert.False(raw.Task.IsCompleted);
        var close = session.CloseAndDrainAsync();
        await owner.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(close.IsCompleted); Assert.False(session.CanRetireOriginalView);
        raw.SetResult();
        await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.True(raw.Task.IsCompletedSuccessfully); Assert.Equal(1, owner.CloseCalls);
    }

    [AvaloniaFact]
    public async Task Mismatched_close_receipt_retains_both_original_tasks()
    {
        var request = Request(); var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new NativeOwner(request.Address) { ReportedClose = reported.Task };
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var close = session.CloseAndDrainAsync();
        await owner.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(close.IsCompleted); Assert.False(session.CanRetireOriginalView);
        reported.SetResult(); await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.True(reported.Task.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public async Task Observer_unsubscribe_failure_cannot_skip_an_already_acquired_original_close()
    {
        var request = Request(); var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new NativeOwner(request.Address)
        { CloseBody = () => raw.Task, UnsubscribeFailure = new IOException("observer removal failed") };
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var close = session.CloseAndDrainAsync();
        await owner.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(close.IsCompleted); Assert.False(raw.Task.IsCompleted);
        raw.SetResult(); var error = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message == "observer removal failed");
        Assert.False(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Synchronous_prepare_callback_cannot_reenter_its_wrapper_close()
    {
        var request = Request(); var owner = new NativeOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        Exception? refusal = null;
        owner.PrepareBody = _ => { refusal = Record.Exception((Action)(() => session.CloseAndDrainAsync())); return Task.FromResult(false); };
        var actual = session.TryPrepareToCloseAsync(); Assert.False(await actual);
        Assert.IsType<InvalidOperationException>(refusal); Assert.Null(session.OriginalClose);
        Assert.True(session.IsPublicationCurrent); await session.CloseAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Restored_execution_context_cannot_hide_an_active_publication_callback()
    {
        var before = ExecutionContext.Capture()!;
        var request = Request(); var session = Registry(new(), new(_ => new NativeOwner(request.Address))).CreateSession(request);
        Exception? refusal = null;
        session.StateChanged += (_, _) => ExecutionContext.Run(before, _ =>
            refusal = Record.Exception((Action)(() => session.CloseAndDrainAsync())), null);
        await session.InitializeAsync();
        Assert.IsType<InvalidOperationException>(refusal); Assert.Null(session.OriginalClose);
        Assert.True(session.IsPublicationCurrent); await session.CloseAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Restored_context_in_owner_subscription_cannot_reenter_wrapper_retirement()
    {
        var before = ExecutionContext.Capture()!;
        var request = Request(); var owner = new NativeOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        Exception? refusal = null;
        owner.OnSubscribe = () => ExecutionContext.Run(before, _ =>
            refusal = Record.Exception((Action)(() => session.CloseAndDrainAsync())), null);
        await session.InitializeAsync();
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.Null(session.OriginalClose); Assert.True(session.IsPublicationCurrent);
        await session.CloseAndDrainAsync(); Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Asynchronous_owner_cleanup_cannot_join_the_wrapper_that_is_draining_it()
    {
        var request = Request(); var owner = new NativeOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        Exception? refusal = null;
        owner.CloseBody = async () =>
        {
            await Task.Yield();
            refusal = Record.Exception((Action)(() => session.CloseAndDrainAsync()));
        };
        await session.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.IsType<InvalidOperationException>(refusal); Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Failed_initial_publication_withdraws_admission_and_survives_original_close()
    {
        var request = Request(); var session = Registry(new(), new(_ => new NativeOwner(request.Address))).CreateSession(request);
        session.StateChanged += (_, _) => throw new IOException("initial publication failed");
        await Assert.ThrowsAsync<IOException>(() => session.InitializeAsync());
        Assert.False(session.IsPublicationCurrent);
        var error = await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message == "initial publication failed");
        Assert.False(session.CanRetireOriginalView);
    }

    [AvaloniaFact]
    public async Task Failed_later_publication_is_owned_even_when_the_app_does_not_retain_observer_errors()
    {
        var request = Request(); var owner = new NativeOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        session.StateChanged += (_, _) => throw new IOException("late publication failed");
        Assert.Throws<IOException>(owner.Notify); Assert.False(session.IsPublicationCurrent);
        var error = await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message == "late publication failed");
        Assert.False(session.CanRetireOriginalView);
    }

    [AvaloniaTheory]
    [InlineData("cui://files")]
    [InlineData("9to1://home")]
    public async Task Native_owner_cannot_change_app_authority_during_initialization(string address)
    {
        var request = Request(); var owner = new NativeOwner(request.Address);
        owner.OnInitialize = () => owner.Address = AppContentAddress.Parse(address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.InitializeAsync());
        Assert.False(session.IsPublicationCurrent); await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
    }

    [AvaloniaFact]
    public async Task Owning_app_resource_navigation_is_allowed_but_later_foreign_authority_is_refused()
    {
        var request = Request(); var owner = new NativeOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        owner.Address = AppContentAddress.Parse($"9to1://files/store/{Guid.NewGuid():D}/folder/{Guid.NewGuid():D}");
        owner.Notify(); Assert.True(session.IsPublicationCurrent); Assert.Equal(owner.Address, session.Address);
        owner.Address = AppContentAddress.Parse("9to1://home");
        Assert.False(session.IsPublicationCurrent);
        Assert.Throws<UnauthorizedAccessException>(owner.Notify);
        Assert.Equal(request.Address, session.Address);
        await Assert.ThrowsAsync<AggregateException>(() => session.CloseAndDrainAsync());
    }

    [AvaloniaFact]
    public async Task Stale_reversible_close_can_reprepare_same_live_owner_then_close_without_cached_failure()
    {
        var request = Request(); var owner = new StrongOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var participant = Strong(session);
        var stale = (await participant.PrepareCloseAsync())!;
        owner.Revision++;
        Assert.Throws<InvalidOperationException>(() => stale.BeginClose());
        Assert.Null(session.OriginalClose); Assert.Null(owner.OriginalClose);
        await stale.AbortAsync(); Assert.True(session.IsPublicationCurrent);
        var current = (await participant.PrepareCloseAsync())!;
        Assert.True(current.IsCurrent);
        await current.BeginClose().WaitAsync(StrongBound);
        Assert.True(session.CanRetireOriginalView);
        Assert.Equal(1, owner.Begins); Assert.Equal(1, owner.Closes);
    }

    private static readonly TimeSpan StrongBound = TimeSpan.FromSeconds(8);
    private static IBrowseAppContentClosePreparationParticipant Strong(IBrowseAppContentSession session) =>
        Assert.IsAssignableFrom<IBrowseAppContentClosePreparationParticipant>(session);
    private static bool HasCause(Exception error, Exception cause) => ReferenceEquals(error, cause) ||
        error is AggregateException group && group.InnerExceptions.Any(inner => HasCause(inner, cause)) ||
        error.InnerException is { } nested && HasCause(nested, cause);

    [AvaloniaFact]
    public async Task Reversible_wrapper_preserves_exact_inner_and_wrapper_originals_without_second_preflight()
    {
        var request = Request(); var owner = new StrongOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request);
        var participant = Strong(session);
        Assert.False(participant.SupportsReversibleClosePreparation);
        await session.InitializeAsync(); Assert.True(participant.SupportsReversibleClosePreparation);
        Assert.True(await session.TryPrepareToCloseAsync());
        Assert.False(owner.Frozen); Assert.Equal(1, owner.Aborts); Assert.Null(session.OriginalClose);
        var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await participant.PrepareCloseAsync());
        Assert.Same(session, preparation.Session); Assert.True(preparation.IsCurrent); Assert.True(owner.Frozen);
        owner.OnBegin = () => Assert.True(session.IsPublicationCurrent);
        Exception? actualCallbackRefusal = null;
        owner.CloseBody = async () =>
        {
            await Task.Yield();
            actualCallbackRefusal = Record.Exception((Action)(() => session.CloseAndDrainAsync()));
        };
        var close = preparation.BeginClose();
        Assert.Same(close, session.OriginalClose); Assert.Same(close, preparation.BeginClose());
        Assert.Same(close, session.CloseAndDrainAsync()); await close.WaitAsync(StrongBound);
        Assert.IsType<InvalidOperationException>(actualCallbackRefusal);
        Assert.Equal(1, owner.Begins); Assert.Equal(1, owner.Closes); Assert.Equal(2, owner.Prepares);
        Assert.True(session.CanRetireOriginalView); Assert.False(participant.SupportsReversibleClosePreparation);
    }

    [AvaloniaTheory]
    [InlineData("foreign")]
    [InlineData("stale")]
    [InlineData("aborted")]
    public async Task Reversible_foreign_stale_and_aborted_preparation_never_caches_wrapper_close(string kind)
    {
        var request = Request(); var owner = new StrongOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var participant = Strong(session);
        if (kind == "foreign")
        {
            owner.ClaimedSession = new NativeOwner(request.Address);
            await Assert.ThrowsAnyAsync<Exception>(() => participant.PrepareCloseAsync().WaitAsync(StrongBound));
        }
        else
        {
            var lease = (await participant.PrepareCloseAsync())!;
            if (kind == "stale") owner.Revision++;
            else { var abort = lease.AbortAsync(); Assert.Same(abort, lease.AbortAsync()); await abort.WaitAsync(StrongBound); }
            Assert.False(lease.IsCurrent);
            Assert.IsType<InvalidOperationException>(Record.Exception((Action)(() => lease.BeginClose())));
            await lease.AbortAsync().WaitAsync(StrongBound);
        }
        Assert.Null(session.OriginalClose); Assert.Null(owner.OriginalClose); Assert.False(owner.Frozen);
        Assert.True(session.IsPublicationCurrent); Assert.Equal(0, owner.Begins); Assert.Equal(1, owner.Aborts);
        await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(StrongBound));
        Assert.Equal(1, owner.Closes);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reversible_preparation_revalidates_after_actual_acquisition_and_aborts_on_host_or_cancellation_change(bool cancel)
    {
        var request = Request(); var owner = new StrongOwner(request.Address); var current = true;
        var session = Registry(new(), new(_ => owner), () => current).CreateSession(request); await session.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.BeforeYield = () => held.Task; var prepare = Strong(session).PrepareCloseAsync(cancellation.Token);
        try
        {
            await owner.PrepareEntered.Task.WaitAsync(StrongBound);
            if (cancel) cancellation.Cancel(); else current = false;
            held.TrySetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => prepare.WaitAsync(StrongBound));
            Assert.Equal(1, owner.Aborts); Assert.False(owner.Frozen); Assert.Null(session.OriginalClose);
        }
        finally
        {
            held.TrySetResult(); current = true;
            await Record.ExceptionAsync(() => prepare.WaitAsync(StrongBound));
            await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(StrongBound));
        }
    }

    [AvaloniaFact]
    public async Task Reversible_actual_origin_and_loader_settle_before_yield_and_cannot_join_wrapper_retirement()
    {
        var request = Request(); var owner = new StrongOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var origin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? refusal = null;
        owner.BeforeYield = async () =>
        {
            await origin.Task;
            refusal = Record.Exception((Action)(() => session.CloseAndDrainAsync()));
            loaderEntered.TrySetResult(); await loader.Task;
        };
        var preparation = Strong(session).PrepareCloseAsync();
        try
        {
            await owner.PrepareEntered.Task.WaitAsync(StrongBound); Assert.False(preparation.IsCompleted);
            origin.TrySetResult(); await loaderEntered.Task.WaitAsync(StrongBound);
            Assert.IsType<InvalidOperationException>(refusal); Assert.Null(session.OriginalClose); Assert.False(preparation.IsCompleted);
            loader.TrySetResult(); var lease = (await preparation.WaitAsync(StrongBound))!;
            Assert.True(owner.PrepareOriginal!.IsCompletedSuccessfully); Assert.True(lease.IsCurrent);
            await lease.AbortAsync().WaitAsync(StrongBound); Assert.False(owner.Frozen);
        }
        finally
        {
            origin.TrySetResult(); loader.TrySetResult();
            await Record.ExceptionAsync(() => preparation.WaitAsync(StrongBound));
            await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(StrongBound));
        }
    }

    [AvaloniaFact]
    public async Task Reversible_raw_prepare_and_abort_fault_siblings_remain_actual_failures()
    {
        var request = Request(); var owner = new StrongOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var oce = new OperationCanceledException("faulted original, not caller cancellation");
        var io = new IOException("second actual prepare fault");
        var raw = new TaskCompletionSource<IBrowseAppContentClosePreparation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.PrepareOverride = raw.Task;
        var preparation = Strong(session).PrepareCloseAsync(); raw.SetException([oce, io]);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => preparation.WaitAsync(StrongBound));
        Assert.True(HasCause(failure, oce)); Assert.True(HasCause(failure, io)); Assert.True(raw.Task.IsFaulted);
        var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => session.CloseAndDrainAsync().WaitAsync(StrongBound));
        Assert.True(HasCause(closeFailure, oce)); Assert.True(HasCause(closeFailure, io));

        var secondOwner = new StrongOwner(request.Address);
        var second = Registry(new(), new(_ => secondOwner)).CreateSession(request); await second.InitializeAsync();
        var lease = (await Strong(second).PrepareCloseAsync())!;
        var abortIo = new IOException("actual abort second sibling");
        var abort = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); secondOwner.AbortOriginal = abort.Task;
        var actualAbort = lease.AbortAsync(); abort.SetException([oce, abortIo]);
        var abortFailure = await Assert.ThrowsAnyAsync<Exception>(() => actualAbort.WaitAsync(StrongBound));
        Assert.True(HasCause(abortFailure, oce)); Assert.True(HasCause(abortFailure, abortIo));
        Assert.Same(actualAbort, lease.AbortAsync()); Assert.Null(second.OriginalClose);
        var retirement = await Assert.ThrowsAnyAsync<Exception>(() => second.CloseAndDrainAsync().WaitAsync(StrongBound));
        Assert.True(HasCause(retirement, abortIo));
    }

    [AvaloniaTheory]
    [InlineData("mismatch")]
    [InlineData("getter")]
    [InlineData("begin")]
    public async Task Reversible_accepted_close_keeps_all_acquired_originals_and_cleanup_faults(string kind)
    {
        var request = Request(); var owner = new StrongOwner(request.Address);
        var session = Registry(new(), new(_ => owner)).CreateSession(request); await session.InitializeAsync();
        var lease = (await Strong(session).PrepareCloseAsync())!;
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFailure = new IOException("actual " + kind + " failure");
        var removalFailure = new IOException("actual unsubscribe failure");
        owner.CloseBody = () => returned.Task; owner.UnsubscribeFailure = removalFailure;
        if (kind is "mismatch" or "getter") owner.ReportedClose = reported.Task;
        if (kind == "getter") owner.CloseGetterFailure = callbackFailure;
        if (kind == "begin") owner.AfterBeginFailure = callbackFailure;
        var close = lease.BeginClose();
        try
        {
            Assert.Same(close, session.OriginalClose); Assert.Same(close, session.CloseAndDrainAsync());
            await owner.Removed.Task.WaitAsync(StrongBound); Assert.False(close.IsCompleted);
            var rawOne = new IOException("actual first raw close fault");
            var rawTwo = new OperationCanceledException("actual faulted raw close sibling");
            if (kind == "mismatch") returned.TrySetException([rawOne, rawTwo]); else returned.TrySetResult();
            if (kind is "mismatch" or "getter") Assert.False(close.IsCompleted);
            reported.TrySetResult();
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(StrongBound));
            Assert.True(HasCause(failure, removalFailure));
            if (kind != "mismatch") Assert.True(HasCause(failure, callbackFailure));
            if (kind == "mismatch")
            { Assert.True(returned.Task.IsFaulted); Assert.True(HasCause(failure, rawOne)); Assert.True(HasCause(failure, rawTwo)); }
            else Assert.True(returned.Task.IsCompletedSuccessfully);
            Assert.True(reported.Task.IsCompletedSuccessfully);
            Assert.Equal(1, owner.Begins); Assert.Equal(1, owner.Closes); Assert.False(session.CanRetireOriginalView);
        }
        finally
        {
            returned.TrySetResult(); reported.TrySetResult();
            await Record.ExceptionAsync(() => close.WaitAsync(StrongBound));
        }
    }

    [AvaloniaFact]
    public async Task Reversible_actual_Chrome_sibling_veto_aborts_wrapper_lease_and_restores_live_edits()
    {
        var first = new StrongOwner(AppContentAddress.Parse("9to1://files/first"));
        var second = new StrongOwner(AppContentAddress.Parse("9to1://files/second")) { AllowPreparation = false };
        var factory = new Factory(r => r.Address.ResourceSegments.Single() == "first" ? first : second);
        var paths = new StrongPaths(); var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: Registry(new(), factory), homeProfileId: "original-profile");
        Exception? primary = null;
        try
        {
            await chrome.NavigateAsync("9to1://files/first"); await chrome.OpenContentAsync(AppContentAddress.Parse("9to1://files/second"), true);
            var cohort = await chrome.PrepareCloseAsync(); Assert.Null(cohort);
            Assert.False(first.Frozen); Assert.True(first.Edit()); Assert.Equal(1, first.Aborts);
            Assert.Null(first.OriginalClose); Assert.Null(second.OriginalClose); Assert.Null(chrome.OriginalClose);
            second.AllowPreparation = true;
            var retry = (await chrome.PrepareCloseAsync())!; await retry.AbortAsync().WaitAsync(StrongBound);
            Assert.True(first.Edit()); Assert.True(second.Edit());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            try { await chrome.DisposeAsync().AsTask().WaitAsync(StrongBound); }
            catch (Exception cleanup)
            {
                if (primary is not null) throw new AggregateException("Actual cohort assertion and attempted retirement both remain visible.", primary, cleanup);
                throw;
            }
            finally { Directory.Delete(paths.DataDirectory, true); }
        }
    }

    private sealed class StrongPaths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "registry-strong-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    private sealed class StrongOwner(AppContentAddress address) : IBrowseNativeAppContentSession, IBrowseAppContentClosePreparationParticipant
    {
        public AppContentAddress Address => address;
        public string Title => "Real boundary owner"; public string IconKey => "files";
        public Control OriginalNativeView { get; } = new Border();
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && _close is null;
        public bool CanRetireOriginalView => _close is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; }
        private Task? _close, _returnedClose;
        public Task? OriginalClose
        {
            get { if (CloseGetterFailure is { } failure) { CloseGetterFailure = null; throw failure; } return _close; }
        }
        public Exception? CloseGetterFailure, UnsubscribeFailure, AfterBeginFailure;
        public Task? ReportedClose, AbortOriginal, PrepareOverride;
        public Func<Task>? BeforeYield, CloseBody; public Action? OnBegin;
        public IBrowseAppContentSession? ClaimedSession;
        public bool Frozen, AllowPreparation = true; public int Revision, Prepares, Aborts, Begins, Closes;
        public Task? PrepareOriginal;
        public readonly TaskCompletionSource PrepareEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler? StateChanged
        {
            add { }
            remove { Removed.TrySetResult(); if (UnsubscribeFailure is { } failure) throw failure; }
        }
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??= Task.CompletedTask;
        public bool SupportsReversibleClosePreparation => true;
        public bool Edit() => !Frozen && _close is null;
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => throw new InvalidOperationException("Strong owner must receive actual preparation");
        public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken token = default)
        {
            Prepares++; Frozen = true; PrepareEntered.TrySetResult();
            return (Task<IBrowseAppContentClosePreparation?>)(PrepareOriginal = PrepareOverride ?? PrepareBodyAsync());
        }
        private async Task<IBrowseAppContentClosePreparation?> PrepareBodyAsync()
        { if (BeforeYield is { } before) await before(); if (!AllowPreparation) { Frozen = false; return null; } return new Lease(this, Revision); }
        public Task CloseAndDrainAsync()
        {
            if (_returnedClose is null) { Closes++; _returnedClose = CloseBody?.Invoke() ?? Task.CompletedTask; _close = ReportedClose ?? _returnedClose; }
            return _returnedClose;
        }
        private sealed class Lease(StrongOwner owner, int revision) : IBrowseAppContentClosePreparation
        {
            private Task? _abort; private bool _accepted, _aborted;
            public IBrowseAppContentSession Session => owner.ClaimedSession ?? owner;
            public bool IsCurrent => !_aborted && !_accepted && owner.Frozen && owner.Revision == revision && owner._close is null;
            public Task AbortAsync()
            { if (_abort is not null) return _abort; _aborted = true; owner.Frozen = false; owner.Aborts++; return _abort = owner.AbortOriginal ?? Task.CompletedTask; }
            public Task BeginClose()
            {
                if (_accepted) return owner._returnedClose!;
                if (!IsCurrent) throw new InvalidOperationException("Exact boundary lease is stale");
                owner.OnBegin?.Invoke(); owner.Begins++; _accepted = true;
                var actual = owner.CloseAndDrainAsync();
                if (owner.AfterBeginFailure is { } failure) throw failure;
                return actual;
            }
        }
    }

    private sealed class Factory(Func<BrowseAppContentRequest, IBrowseAppContentSession> create) : IBrowseAppContentFactory
    {
        public int Calls;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request) { Calls++; return create(request); }
    }
    private sealed class RegistrySource : IModeRegistry
    {
        public ModeDefinition App = Files;
        public Func<CancellationToken, Task<ModeDefinition?>>? Read;
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Read?.Invoke(token) ?? Task.FromResult<ModeDefinition?>(App);
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult<ModeDefinition?>(App);
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModeDefinition>>([App]);
        public Task UpsertModeAsync(ModeDefinition app, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }
    private class NativeOwner(AppContentAddress address) : IBrowseNativeAppContentSession
    {
        public readonly Control View = new Border();
        public AppContentAddress Address { get; set; } = address;
        public string Title => "Files";
        public string IconKey => "folder";
        private EventHandler? _stateChanged;
        public event EventHandler? StateChanged
        {
            add { _stateChanged += value; OnSubscribe?.Invoke(); }
            remove { _stateChanged -= value; if (UnsubscribeFailure is { } failure) throw failure; }
        }
        public bool IsPublicationCurrent => OriginalInitialization?.IsCompletedSuccessfully == true && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose?.IsCompletedSuccessfully == true;
        private Task? _originalInitialization;
        public Func<Task?, Task?>? InitializationObservation;
        public Task? OriginalInitialization
        {
            get => InitializationObservation is { } observe ? observe(_originalInitialization) : _originalInitialization;
            private set => _originalInitialization = value;
        }
        public Task? OriginalClose { get; private set; }
        public Control OriginalNativeView => View;
        public bool CanClose = true;
        public Action? OnInitialize;
        public Action? OnSubscribe;
        public Func<CancellationToken, Task>? InitializeBody;
        public Func<CancellationToken, Task<bool>>? PrepareBody;
        public Func<Task>? CloseBody;
        public Task? ReportedInitialization, ReportedClose;
        private Task? _returnedInitialization, _returnedClose;
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? PreparationFailure;
        public Exception? UnsubscribeFailure;
        public int CloseCalls;
        public Task InitializeAsync(CancellationToken token = default)
        {
            if (_returnedInitialization is null)
            { OnInitialize?.Invoke(); _returnedInitialization = InitializeBody?.Invoke(token) ?? Task.CompletedTask; OriginalInitialization = ReportedInitialization ?? _returnedInitialization; }
            return _returnedInitialization;
        }
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => PreparationFailure is { } error
            ? Task.FromException<bool>(error) : PrepareBody?.Invoke(token) ?? Task.FromResult(CanClose);
        public Task CloseAndDrainAsync()
        {
            if (_returnedClose is null)
            { CloseCalls++; _returnedClose = CloseBody?.Invoke() ?? Task.CompletedTask; OriginalClose = ReportedClose ?? _returnedClose; Closed.TrySetResult(); }
            return _returnedClose;
        }
        public void Notify() => _stateChanged?.Invoke(this, EventArgs.Empty);
    }
    private sealed class NonNativeOwner(AppContentAddress address) : IBrowseAppContentSession
    {
        public AppContentAddress Address => address;
        public string Title => "Invalid surface";
        public string IconKey => "apps";
        public event EventHandler? StateChanged { add { } remove { } }
        public bool IsPublicationCurrent => false;
        public bool CanRetireOriginalView => false;
        public Task? OriginalInitialization => null;
        public Task? OriginalClose => null;
        public int CloseCalls;
        public Task InitializeAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task CloseAndDrainAsync() { CloseCalls++; throw new IOException("original cleanup failure"); }
    }
}
