using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

// Actual host/Chrome/registry/App ownership, with explicitly source-injected host
// startup fields. This is Linux headless lifetime evidence, not full product startup,
// Windows admission, a native window, clean shutdown or a definition-level ACL.
public sealed class UnifiedClosePreparationTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [AvaloniaFact]
    public async Task Actual_App_handoff_consumes_one_decision_after_same_origin_and_keeps_accepted_witness()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Owner.OnlyFirstPreparation = true;
        Exception? selfJoin = null;
        rig.Owner.OnBegin = () =>
        {
            Assert.True(rig.App.OriginalDocumentClosePreflightAttempt!.IsCompleted);
            Assert.True(rig.App.OriginalDocumentClosePreflightDelivery!.IsCompleted);
        };
        rig.Owner.CloseBody = async () =>
        { await Task.Yield(); selfJoin = Record.Exception((Action)(() => rig.Host.CloseAndDrainAsync())); };
        Call<object?>(rig.App, "RequestOriginalDocumentClosePreflight");
        var publication = Field<Task>(rig.App, "_actualDocumentClosePreflightPublication");
        await publication.WaitAsync(Bound);
        Assert.Equal(1, rig.Owner.Prepares); Assert.Equal(1, rig.Owner.Closes);
        Assert.IsType<InvalidOperationException>(selfJoin);
        Assert.Same(rig.Host.OriginalClose, rig.Host.CloseAndDrainAsync());
        Assert.True(rig.Host.OriginalClose!.IsCompletedSuccessfully);
        Assert.NotNull(rig.App.OriginalShutdownDelivery); // Accepted host no longer fails its old IsCurrent test.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.App.OriginalShutdownDelivery!.WaitAsync(Bound));
        Assert.Contains("not configured", failure.Message); // No fake native clean/exit fixture.
    }

    [AvaloniaFact]
    public async Task Legacy_bool_second_veto_stays_retryable_and_bool_strong_compatibility_releases_edits()
    {
        await using var rig = await Rig.CreateAsync();
        Assert.True(await rig.Host.TryPrepareToCloseAsync(CancellationToken.None));
        Assert.False(rig.Owner.Frozen); Assert.Equal(1, rig.Owner.Aborts); Assert.Null(rig.Host.OriginalClose);
        rig.Owner.Strong = false;
        Assert.True(await rig.Host.TryPrepareToCloseAsync(CancellationToken.None));
        rig.Owner.Allow = false;
        var declined = rig.Host.CloseAndDrainAsync();
        await Assert.ThrowsAsync<BrowseCloseVetoException>(() => declined.WaitAsync(Bound));
        Assert.Null(rig.Host.OriginalClose); Assert.Null(rig.Chrome!.OriginalClose); Assert.True(rig.Owner.Edit());
        rig.Owner.Allow = true;
        var close = rig.Host.CloseAndDrainAsync(); await close.WaitAsync(Bound);
        Assert.Same(close, rig.Host.OriginalClose); Assert.NotSame(declined, close);
        var observed = Prepare(rig.Host); await observed.WaitAsync(Bound); var accepted = Result(observed)!;
        Assert.True((bool)Property(accepted, "IsAcceptedOriginal")!);
        Assert.Same(close, Call<Task>(accepted, "BeginClose"));
    }

    [AvaloniaTheory]
    [InlineData("handoff-fault")]
    [InlineData("currentness")]
    [InlineData("borrower")]
    public async Task App_external_publication_joins_handoff_and_aborts_actual_unaccepted_lease_on_every_exit(string kind)
    {
        await using var rig = await Rig.CreateAsync();
        var preparation = Prepare(rig.Host); await preparation.WaitAsync(Bound); var lease = Result(preparation)!;
        Assert.True(rig.Owner.Frozen);
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = Attempt(rig, preparation, lease);
        var failure = new IOException("actual " + kind + " cause");
        if (kind == "borrower") Set(rig.App, "_actualDesktopBorrowers", new object[] { new VetoBorrower(failure) });
        var publication = Call<Task>(rig.App, "PublishOriginalDocumentClosePreflightAsync", source.Task, attempt);
        try
        {
            Assert.False(publication.IsCompleted); Assert.True(rig.Owner.Frozen); Assert.Equal(0, rig.Owner.Aborts);
            if (kind == "currentness") Set(rig.App, "_actualUnifiedWorkspace", null);
            if (kind == "handoff-fault") source.SetException([failure, new OperationCanceledException("actual faulted handoff sibling")]);
            else source.SetResult();
            var actual = await Record.ExceptionAsync(() => publication.WaitAsync(Bound));
            if (kind != "currentness") { Assert.NotNull(actual); Assert.True(HasCause(actual, failure)); }
            else Assert.Null(actual);
            Assert.True(source.Task.IsCompleted); Assert.Equal(1, rig.Owner.Aborts); Assert.True(rig.Owner.Edit());
            Assert.Null(rig.Host.OriginalClose); Assert.Null(rig.App.OriginalShutdownDelivery);
            var abort = Field<Task>(attempt, "UnifiedAbort"); Assert.True(abort.IsCompletedSuccessfully);
        }
        finally
        {
            source.TrySetResult(); Set(rig.App, "_actualUnifiedWorkspace", rig.Host);
            Set(rig.App, "_actualDesktopBorrowers", new object[] { rig.Host });
            await Record.ExceptionAsync(() => publication.WaitAsync(Bound));
        }
    }

    [AvaloniaFact]
    public async Task Strong_accepted_close_joins_actual_workspace_and_raw_fault_siblings_without_second_decision()
    {
        await using var rig = await Rig.CreateAsync();
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Owner.CloseBody = () => raw.Task;
        Task? close = null;
        var first = new IOException("first actual child close cause"); var second = new OperationCanceledException("faulted child sibling");
        try
        {
            var preparation = Prepare(rig.Host); await preparation.WaitAsync(Bound); var lease = Result(preparation)!;
            close = Call<Task>(lease, "BeginClose");
            Assert.Same(close, rig.Host.OriginalClose); Assert.Same(close, Call<Task>(lease, "BeginClose"));
            Assert.Same(close, rig.Host.CloseAndDrainAsync());
            await rig.Owner.CloseEntered.Task.WaitAsync(Bound);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask();
            Assert.NotNull(rig.Workspace!.OriginalClose); Assert.False(rig.Workspace.OriginalClose!.IsCompleted);
            Assert.False(close.IsCompleted); raw.SetException([first, second]);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(Bound));
            Assert.True(HasCause(failure, first)); Assert.True(HasCause(failure, second));
            Assert.Equal(1, rig.Owner.Prepares); Assert.Equal(1, rig.Owner.Closes);
            Assert.Same(close, rig.Host.OriginalClose); Assert.False(close.IsCompletedSuccessfully);
            rig.ExpectedFailedClose = close; // The SAME terminal failed original was asserted above.
        }
        finally { raw.TrySetResult(); if (close is not null) await JoinAssertedOrSuccessfulAsync(close, rig.ExpectedFailedClose); }
    }

    [AvaloniaFact]
    public async Task Accepted_host_remains_accepted_when_App_cohort_changes_while_actual_close_is_pending()
    {
        await using var rig = await Rig.CreateAsync();
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); rig.Owner.CloseBody = () => raw.Task;
        Task? publication = null;
        try
        {
            var preparation = Prepare(rig.Host); await preparation.WaitAsync(Bound); var lease = Result(preparation)!;
            var attempt = Attempt(rig, preparation, lease);
            publication = Call<Task>(rig.App, "PublishOriginalDocumentClosePreflightAsync", Task.CompletedTask, attempt);
            await rig.Owner.CloseEntered.Task.WaitAsync(Bound); Assert.NotNull(rig.Host.OriginalClose);
            Set(rig.App, "_actualUnifiedWorkspace", null); raw.TrySetResult(); await publication.WaitAsync(Bound);
            Assert.Null(rig.App.OriginalShutdownDelivery); Assert.Equal(0, rig.Owner.Aborts);
            Assert.True((bool)Property(lease, "IsAcceptedOriginal")!); Assert.False((bool)Property(lease, "IsCurrent")!);
        }
        finally
        { raw.TrySetResult(); Set(rig.App, "_actualUnifiedWorkspace", rig.Host); if (publication is not null) await publication.WaitAsync(Bound); }
    }

    [AvaloniaFact]
    public async Task Pending_startup_cancels_before_join_and_waits_actual_finally_without_a_prepared_fake_close()
    {
        await using var rig = await Rig.CreateAsync(withChrome: false);
        var cancellation = Field<CancellationTokenSource>(rig.Host, "_startupCancellation");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task OriginalStartup()
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token); }
            finally { entered.SetResult(); await release.Task; }
        }
        var original = OriginalStartup(); Set(rig.Host, "_initialization", original);
        var close = rig.Host.CloseAndDrainAsync();
        try
        {
            await entered.Task.WaitAsync(Bound); Assert.True(cancellation.IsCancellationRequested);
            Assert.False(original.IsCompleted); Assert.False(close.IsCompleted); Assert.Same(close, rig.Host.OriginalClose);
            release.SetResult(); await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(Bound));
            Assert.True(original.IsCanceled); Assert.True(close.IsCompleted); Assert.False(close.IsCompletedSuccessfully);
            rig.ExpectedFailedClose = close; // Expected startup cancellation, observed on its actual original.
        }
        finally { release.TrySetResult(); await JoinAssertedOrSuccessfulAsync(close, rig.ExpectedFailedClose); }
    }

    private static async Task JoinAssertedOrSuccessfulAsync(Task actual, Task? assertedFailure)
    {
        try { await actual.WaitAsync(Bound); }
        catch when (ReferenceEquals(actual, assertedFailure) && actual.IsCompleted && !actual.IsCompletedSuccessfully) { }
    }

    private static Task Prepare(App.UnifiedWorkspaceHost host) => Call<Task>(host, "PrepareCloseAsync", CancellationToken.None);
    private static object? Result(Task actual) => actual.GetType().GetProperty("Result")!.GetValue(actual);
    private static object Attempt(Rig rig, Task preparation, object lease)
    {
        var type = typeof(App).GetNestedType("OriginalDocumentCloseAttempt", BindingFlags.NonPublic)!;
        var attempt = Activator.CreateInstance(type, nonPublic: true)!;
        Set(attempt, "Desktop", rig.Desktop); Set(attempt, "Windows", rig.Desktop.Windows.ToArray());
        Set(attempt, "Unified", rig.Host); Set(attempt, "UnifiedPreparation", preparation);
        Set(attempt, "UnifiedLease", lease); Set(attempt, "Prepared", true); return attempt;
    }
    private sealed class VetoBorrower(Exception cause) : IDesktopOriginalRetirementJoinGuard
    { public void DemandExternalOriginalRetirementJoin() => throw cause; }
    private static bool HasCause(Exception error, Exception cause) => ReferenceEquals(error, cause) ||
        error is AggregateException group && group.InnerExceptions.Any(inner => HasCause(inner, cause)) ||
        error.InnerException is { } nested && HasCause(nested, cause);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner)!;
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
    private static object? Property(object owner, string name) => owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner);
    private static T Call<T>(object owner, string name, params object[] arguments)
    {
        try
        {
            var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException("The actual owner has no " + name + " contract.");
            return (T)method.Invoke(owner, arguments)!;
        }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
    private sealed class Rig : IAsyncDisposable
    {
        internal readonly App App = new();
        internal readonly Paths Paths = new();
        internal readonly ServiceProvider Services = new ServiceCollection().BuildServiceProvider();
        internal readonly CancellationTokenSource AppLifetime = new(), WindowLifetime = new();
        internal readonly ClassicDesktopStyleApplicationLifetime Desktop = new();
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly App.UnifiedWorkspaceHost Host;
        internal readonly Owner Owner = new();
        internal BrowseChrome? Chrome; internal BrowseNativeWorkspace? Workspace;
        internal Task? ExpectedFailedClose;
        private Rig()
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home.json")),
                new OperatingSystemPrincipalSource(), Paths, new("unified.close.test." + Guid.NewGuid().ToString("N")));
            Host = new(App, Services, Home, AppLifetime.Token, WindowLifetime.Token);
            Set(App, "_actualDesktop", Desktop); Set(App, "_actualUnifiedWorkspace", Host);
            Set(App, "_actualDesktopBorrowers", new object[] { Host });
        }
        internal static async Task<Rig> CreateAsync(bool withChrome = true)
        {
            var rig = new Rig();
            await rig.Home.StartOriginalAsync().WaitAsync(Bound);
            var profile = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None); Assert.NotNull(profile);
            if (withChrome)
            {
                var registry = new NativeAppContentRegistry(new Modes(), [new(Files.Id, Files.Key, HavenAppRouteKind.Files, new Factory(rig.Owner))],
                    () => rig.Host.OriginalClose is null);
                var creation = BrowseChrome.CreateAsync(rig.Paths, appContentFactory: registry, homeProfileId: profile.ProfileId);
                rig.Chrome = await creation; await rig.Chrome.NavigateAsync("9to1://files/fixture").WaitAsync(Bound);
                rig.Workspace = new(rig.Chrome, _ => true);
                Set(rig.Host, "_initialization", creation); Set(rig.Host, "_chromeCreation", creation);
                Set(rig.Host, "_chrome", rig.Chrome); Set(rig.Host, "_workspace", rig.Workspace);
            }
            return rig;
        }
        public async ValueTask DisposeAsync()
        {
            Owner.OnlyFirstPreparation = false; Owner.Allow = true;
            // Never waive pending cleanup, a timeout, or an unasserted original failure.
            var actual = Host.CloseAndDrainAsync();
            await JoinAssertedOrSuccessfulAsync(actual, ExpectedFailedClose);
            await Home.CloseAndDrainAsync().WaitAsync(Bound); await Services.DisposeAsync();
            AppLifetime.Dispose(); WindowLifetime.Dispose(); Directory.Delete(Paths.DataDirectory, true);
        }
    }
    private static readonly ModeDefinition Files = BuiltInModeSeed.Modes.Single(mode => mode.Key == "files");
    private sealed class Factory(Owner owner) : IBrowseAppContentFactory
    { public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request) { owner.Address = request.Address; return owner; } }
    private sealed class Modes : IModeRegistry
    {
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Task.FromResult<ModeDefinition?>(Files);
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult<ModeDefinition?>(Files);
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModeDefinition>>([Files]);
        public Task UpsertModeAsync(ModeDefinition app, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Owner : IBrowseNativeAppContentSession, IBrowseAppContentClosePreparationParticipant
    {
        internal bool Frozen, Allow = true, Strong = true, OnlyFirstPreparation; internal int Prepares, Aborts, Closes;
        internal Action? OnBegin; internal Func<Task>? CloseBody;
        internal readonly TaskCompletionSource CloseEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AppContentAddress Address { get; set; } = AppContentAddress.Parse("9to1://files");
        public string Title => "Actual lifetime boundary"; public string IconKey => "files";
        public Control OriginalNativeView { get; } = new Border();
        public event EventHandler? StateChanged { add { } remove { } }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; } public Task? OriginalClose { get; private set; }
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??= Task.CompletedTask;
        public bool SupportsReversibleClosePreparation => Strong;
        internal bool Edit() => !Frozen && OriginalClose is null;
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) { Prepares++; return Task.FromResult(Allow); }
        public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken token = default)
        {
            Prepares++; if (!Allow || OnlyFirstPreparation && Prepares > 1) return Task.FromResult<IBrowseAppContentClosePreparation?>(null);
            Frozen = true; return Task.FromResult<IBrowseAppContentClosePreparation?>(new Lease(this));
        }
        public Task CloseAndDrainAsync()
        { if (OriginalClose is not null) return OriginalClose; Closes++; OriginalClose = CloseBody?.Invoke() ?? Task.CompletedTask; CloseEntered.TrySetResult(); return OriginalClose; }
        private sealed class Lease(Owner owner) : IBrowseAppContentClosePreparation
        {
            private bool _aborted, _accepted; private Task? _abort;
            public IBrowseAppContentSession Session => owner;
            public bool IsCurrent => !_aborted && !_accepted && owner.Edit() == false && owner.OriginalClose is null;
            public Task AbortAsync() { if (_abort is not null) return _abort; _aborted = true; owner.Frozen = false; owner.Aborts++; return _abort = Task.CompletedTask; }
            public Task BeginClose() { if (_accepted) return owner.OriginalClose!; if (!IsCurrent) throw new InvalidOperationException("Stale fixture lease"); owner.OnBegin?.Invoke(); _accepted = true; return owner.CloseAndDrainAsync(); }
        }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "unified-close-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
