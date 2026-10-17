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
        public Task? OriginalInitialization { get; private set; }
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
