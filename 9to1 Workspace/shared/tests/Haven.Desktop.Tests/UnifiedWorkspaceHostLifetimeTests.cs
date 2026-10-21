using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Owning Windows lifetime controls over the actual host, Browse and Home
/// owners. Reflection installs retained source tasks at the composition boundary;
/// these cases do not certify packaged admission, rendering or authenticated startup.</summary>
public sealed class UnifiedWorkspaceHostLifetimeTests
{
    private static readonly List<object> RetainedFailedOwners = [];

    [AvaloniaFact]
    public async Task Second_preflight_veto_keeps_App_shutdown_unadmitted_and_same_host_retryable()
    {
        var rig = new Rig(); RetainedFailedOwners.Add(rig);
        var factory = new ContentFactory();
        var creation = BrowseChrome.CreateAsync(rig.Paths, internalContentFactory: factory, homeProfileId: "fixture-profile");
        var chrome = await creation;
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        Set(rig.Host, "_initialization", creation); Set(rig.Host, "_chromeCreation", creation);
        Set(rig.Host, "_chrome", chrome); Set(rig.Host, "_workspace", workspace);
        var preparation = Call<Task>(rig.Host, "PrepareCloseAsync", CancellationToken.None);
        await preparation;
        var lease = preparation.GetType().GetProperty("Result")!.GetValue(preparation);
        Assert.NotNull(lease);
        var owner = factory.Owner!; owner.AllowClose = false;

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        Set(rig.App, "_actualDesktop", desktop); Set(rig.App, "_actualUnifiedWorkspace", rig.Host);
        Set(rig.App, "_actualDesktopBorrowers", new object[] { rig.Host });
        var attemptType = typeof(App).GetNestedType("OriginalDocumentCloseAttempt", BindingFlags.NonPublic)!;
        var attempt = Activator.CreateInstance(attemptType, nonPublic: true)!;
        Set(attempt, "Desktop", desktop); Set(attempt, "Windows", desktop.Windows.ToArray());
        Set(attempt, "Unified", rig.Host); Set(attempt, "UnifiedPreparation", preparation); Set(attempt, "UnifiedLease", lease);
        Set(attempt, "Prepared", true);
        await Call<Task>(rig.App, "PublishOriginalDocumentClosePreflightAsync", preparation, attempt);

        Assert.Null(rig.App.OriginalShutdownDelivery);
        Assert.Null(rig.Host.OriginalClose); Assert.Null(chrome.OriginalClose);
        Assert.True(workspace.IsOriginalPublicationCurrent);
        var declined = Assert.Single(rig.Host.OriginalCloseAttempts);
        await Assert.ThrowsAsync<BrowseCloseVetoException>(() => declined);
        await chrome.NewTabAsync(isPrivate: false); // Actual Browse commands remain usable after veto.
        Assert.Equal(2, chrome.State.Tabs.Count);
        owner.AllowClose = true;
        var close = rig.Host.CloseAndDrainAsync(); await close;
        Assert.Same(close, rig.Host.OriginalClose);
        Assert.Same(close, rig.Host.CloseAndDrainAsync());
        Assert.NotSame(declined, close);
        await rig.FinishAsync(); RetainedFailedOwners.Remove(rig);
    }

    [AvaloniaFact]
    public async Task Pending_startup_is_canceled_before_join_and_its_actual_finally_is_awaited()
    {
        var rig = new Rig(); RetainedFailedOwners.Add(rig);
        var startup = Field<CancellationTokenSource>(rig.Host, "_startupCancellation");
        var finallyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinally = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task OriginalStartup()
        {
            try { await Task.Delay(Timeout.Infinite, startup.Token); }
            finally { finallyEntered.SetResult(); await releaseFinally.Task; }
        }
        var original = OriginalStartup(); Set(rig.Host, "_initialization", original);
        Assert.True(await rig.Host.TryPrepareToCloseAsync(CancellationToken.None));
        var close = rig.Host.CloseAndDrainAsync();
        await finallyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(startup.IsCancellationRequested);
        Assert.Same(close, rig.Host.OriginalClose); Assert.Same(close, rig.Host.CloseAndDrainAsync());
        Assert.False(original.IsCompleted); Assert.False(close.IsCompleted);
        releaseFinally.SetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(original.IsCanceled); Assert.True(close.IsCompleted);
        // The same cancellation remains inspectable; it is not a successful drain.
        Assert.False(close.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public async Task Failed_Chrome_creation_with_failed_owner_cleanup_does_not_seal_canonical_session()
    {
        var rig = new Rig(); RetainedFailedOwners.Add(rig);
        var startupFailure = new IOException("Actual native initializer failed");
        var cleanupFailure = new IOException("Actual native retirement failed");
        var factory = new ContentFactory(startupFailure, cleanupFailure);
        var creation = BrowseChrome.CreateAsync(rig.Paths, internalContentFactory: factory, homeProfileId: "fixture-profile");
        var createFailure = await Assert.ThrowsAsync<AggregateException>(() => creation);
        Assert.Contains(startupFailure, createFailure.Flatten().InnerExceptions);
        Assert.Contains(cleanupFailure, createFailure.Flatten().InnerExceptions);
        var repository = new Repository();
        var session = new UnifiedWorkspaceSession(new WorkspaceSessionCoordinator(repository), null);
        var port = session.OpenWindow(session.MainWindowId, "fixture-profile");
        Set(rig.Host, "_initialization", creation); Set(rig.Host, "_chromeCreation", creation);
        Set(rig.Host, "_session", session);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Host.CloseAndDrainAsync());
        Assert.Null(Field<Task?>(rig.Host, "_sessionClose"));
        // An admitted projection can still settle: no false null-owner retirement
        // proof has sealed the canonical producer while failed owners remain alive.
        await port.SaveAsync(new([], null), CancellationToken.None);
        Assert.Single(repository.Writes);
    }

    [AvaloniaFact]
    public async Task Stale_startup_lease_reprepares_to_current_and_reaches_accepted_retirement()
    {
        // One approved close during loading, whose startup lease goes stale BEFORE publication, must be
        // retained: the production wiring aborts the old unaccepted lease and reacquires the CURRENT
        // preparation (no waiting on the whole startup), reaching accepted host retirement. This drives
        // the actual App→Unified close preparation/publication helper (PublishOriginalDocumentClosePreflightAsync).
        var rig = new Rig(); RetainedFailedOwners.Add(rig);
        var factory = new ContentFactory();
        var creation = BrowseChrome.CreateAsync(rig.Paths, internalContentFactory: factory, homeProfileId: "fixture-profile");

        // Loading phase: initialization pending, no chrome yet → PrepareCloseAsync yields a STARTUP lease.
        var heldInit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(rig.Host, "_initialization", heldInit.Task); Set(rig.Host, "_chromeCreation", heldInit.Task);
        Set(rig.Host, "_chrome", (object?)null); Set(rig.Host, "_workspace", (object?)null);

        var loadingPrep = Call<Task>(rig.Host, "PrepareCloseAsync", CancellationToken.None);
        await loadingPrep;
        var loadingLease = loadingPrep.GetType().GetProperty("Result")!.GetValue(loadingPrep)!;
        Assert.NotNull(loadingLease);
        Assert.True((bool)loadingLease.GetType().GetProperty("Startup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loadingLease)!);

        // Transition completes: initialization/chrome/workspace now present → the captured startup lease goes stale.
        var chrome = await creation;
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        Set(rig.Host, "_initialization", creation); Set(rig.Host, "_chromeCreation", creation);
        Set(rig.Host, "_chrome", chrome); Set(rig.Host, "_workspace", workspace);

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        Set(rig.App, "_actualDesktop", desktop); Set(rig.App, "_actualUnifiedWorkspace", rig.Host);
        Set(rig.App, "_actualDesktopBorrowers", new object[] { rig.Host });
        var attemptType = typeof(App).GetNestedType("OriginalDocumentCloseAttempt", BindingFlags.NonPublic)!;
        var attempt = Activator.CreateInstance(attemptType, nonPublic: true)!;
        Set(attempt, "Desktop", desktop); Set(attempt, "Windows", desktop.Windows.ToArray());
        Set(attempt, "Unified", rig.Host); Set(attempt, "UnifiedPreparation", loadingPrep); Set(attempt, "UnifiedLease", loadingLease);
        Set(attempt, "Prepared", true);

        await Call<Task>(rig.App, "PublishOriginalDocumentClosePreflightAsync", loadingPrep, attempt);

        // The single close is retained across the transition: the CURRENT post-startup preparation is
        // reacquired and reaches accepted host retirement (host + chrome OriginalClose published).
        Assert.NotNull(rig.Host.OriginalClose);
        Assert.NotNull(chrome.OriginalClose);
        await rig.FinishAsync(); RetainedFailedOwners.Remove(rig);
    }

    [AvaloniaFact]
    public async Task Early_close_during_welcome_held_initiation_retires_startup_without_waiting_for_click()
    {
        // A single early close must complete actual current startup retirement/cancellation even when
        // initialization is held on a Welcome pending action forever — it must NOT wait for any user
        // click. The publication drives the current (startup) lease's BeginClose → host CloseAndDrainAsync,
        // which cancels the startup token and retires the host.
        var rig = new Rig(); RetainedFailedOwners.Add(rig);
        var startupCtl = Field<CancellationTokenSource>(rig.Host, "_startupCancellation");
        async Task Init() { try { await Task.Delay(Timeout.Infinite, startupCtl.Token); } catch (OperationCanceledException) { } }
        var initTask = Init();
        Set(rig.Host, "_initialization", initTask); Set(rig.Host, "_chromeCreation", initTask);
        Set(rig.Host, "_chrome", (object?)null); Set(rig.Host, "_workspace", (object?)null);

        var prep = Call<Task>(rig.Host, "PrepareCloseAsync", CancellationToken.None);
        await prep;
        var lease = prep.GetType().GetProperty("Result")!.GetValue(prep)!;
        Assert.True((bool)lease.GetType().GetProperty("Startup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!);

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        Set(rig.App, "_actualDesktop", desktop); Set(rig.App, "_actualUnifiedWorkspace", rig.Host);
        Set(rig.App, "_actualDesktopBorrowers", new object[] { rig.Host });
        var attemptType = typeof(App).GetNestedType("OriginalDocumentCloseAttempt", BindingFlags.NonPublic)!;
        var attempt = Activator.CreateInstance(attemptType, nonPublic: true)!;
        Set(attempt, "Desktop", desktop); Set(attempt, "Windows", desktop.Windows.ToArray());
        Set(attempt, "Unified", rig.Host); Set(attempt, "UnifiedPreparation", prep); Set(attempt, "UnifiedLease", lease);
        Set(attempt, "Prepared", true);

        await Call<Task>(rig.App, "PublishOriginalDocumentClosePreflightAsync", prep, attempt);

        // Startup canceled + host retired WITHOUT any Welcome click (init completed only via cancellation).
        Assert.True(startupCtl.IsCancellationRequested);
        Assert.NotNull(rig.Host.OriginalClose);
        Assert.True(initTask.IsCompleted);
        await rig.FinishAsync(); RetainedFailedOwners.Remove(rig);
    }

    private sealed class Rig
    {
        internal readonly App App = new();
        internal readonly Paths Paths = new();
        internal readonly ServiceProvider Services;
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly App.UnifiedWorkspaceHost Host;
        internal Rig()
        {
            Assert.True(OperatingSystem.IsWindows(), "This owning host fixture requires actual Windows Home; Linux is unvalidated.");
            Directory.CreateDirectory(Paths.DataDirectory);
            Services = new ServiceCollection().BuildServiceProvider();
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home.json")),
                new OperatingSystemPrincipalSource(), Paths, new("9to1.host-lifetime." + Guid.NewGuid().ToString("N")));
            Host = new(App, Services, Home, CancellationToken.None, CancellationToken.None);
        }
        internal async Task FinishAsync()
        { await Home.CloseAndDrainAsync(); await Services.DisposeAsync(); Directory.Delete(Paths.DataDirectory, true); }
    }
    private sealed class ContentFactory(Exception? initializationFailure = null, Exception? closeFailure = null) : IBrowseInternalContentFactory
    {
        internal ContentOwner? Owner;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request) =>
            Owner = new(request.Address, initializationFailure, closeFailure);
    }
    private sealed class ContentOwner(AppContentAddress address, Exception? initializationFailure, Exception? closeFailure) : IBrowseAppContentSession
    {
        internal bool AllowClose = true;
        public AppContentAddress Address => address;
        public string Title => "Actual owner fixture";
        public string IconKey => "home";
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; }
        public Task? OriginalClose { get; private set; }
        public event EventHandler? StateChanged { add { } remove { } }
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??=
            initializationFailure is null ? Task.CompletedTask : Task.FromException(initializationFailure);
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => Task.FromResult(AllowClose);
        public Task CloseAndDrainAsync() => OriginalClose ??=
            closeFailure is null ? Task.CompletedTask : Task.FromException(closeFailure);
    }
    private sealed class Repository : IWorkspaceSessionRepository
    {
        internal readonly List<WorkspaceSessionSnapshot> Writes = [];
        public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken token) => Task.FromResult<WorkspaceSessionSnapshot?>(null);
        public Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken token) { Writes.Add(snapshot); return Task.CompletedTask; }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "unified-host-owner-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Set(object owner, string name, object? value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
    private static T Call<T>(object owner, string name, params object[] arguments)
    {
        try { return (T)owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, arguments)!; }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
}
