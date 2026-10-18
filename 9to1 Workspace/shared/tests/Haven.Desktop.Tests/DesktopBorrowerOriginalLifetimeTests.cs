using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Tests;

public sealed class DesktopBorrowerOriginalLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Workspace_close_joins_an_already_entered_real_factory_and_preserves_its_fault()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var cause = new IOException("Actual shell factory failed after retirement was requested.");
        var provider = new CallbackServices(() => { entered.Set(); Assert.True(release.Wait(Bound)); throw cause; });
        var service = new WorkspaceWindowService(provider, null!, null!);
        var restore = Task.Run(() => service.RestoreAdditionalWindowsAsync(Snapshot(), HavenShellEdition.New, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Task? close = null;
        try
        {
            Assert.True(entered.Wait(Bound, TestContext.Current.CancellationToken));
            close = service.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.RestoreAdditionalWindowsAsync(Snapshot(), HavenShellEdition.New, TestContext.Current.CancellationToken));
            Assert.Equal(1, provider.Calls);
        }
        finally { release.Set(); }
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => restore.WaitAsync(Bound, TestContext.Current.CancellationToken)));
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => close!.WaitAsync(Bound, TestContext.Current.CancellationToken)));
        Assert.Same(close, service.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Actual_shell_factory_cannot_join_its_service_even_under_a_restored_execution_context()
    {
        var context = ExecutionContext.Capture()!;
        var cause = new IOException("Factory has no shell to return.");
        WorkspaceWindowService? service = null;
        Task? attemptedJoin = null;
        var provider = new CallbackServices(() =>
        {
            ExecutionContext.Run(context, _ =>
            {
                // Observe the actual guarded call under the restored physical source
                // stack, then assert its settlement from this test's async flow.
                try { attemptedJoin = service!.CloseAndDrainAsync(); }
                catch (Exception error) { attemptedJoin = Task.FromException(error); }
            }, null);
            throw cause;
        });
        service = new WorkspaceWindowService(provider, null!, null!);
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => service.RestoreAdditionalWindowsAsync(Snapshot(), HavenShellEdition.New, TestContext.Current.CancellationToken)));
        Assert.NotNull(attemptedJoin);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attemptedJoin.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => service.CloseAndDrainAsync()));
    }

    [AvaloniaFact]
    public async Task Persisted_popup_native_close_is_vetoed_until_the_actual_tab_page_close_settles()
    {
        var page = new HeldPage();
        var (service, sessions, window) = await CreatePopupAsync(page);
        var closed = false; window.Closed += (_, _) => closed = true;
        try
        {
            window.Close();
            Assert.False(closed);
            await page.Requested.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.False(closed);
            Assert.Same(page, ((ContentControl)window.Content!).Content);
            page.Settled.TrySetResult();
            await service.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.True(closed);
        }
        finally
        {
            page.Settled.TrySetResult();
            await service.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            await sessions.SaveNowAndCancelPendingAsync(TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Failed_popup_child_close_retains_the_real_content_and_all_direct_causes()
    {
        var page = new HeldPage();
        var (service, sessions, window) = await CreatePopupAsync(page);
        var a = new IOException("Actual page source A"); var b = new InvalidOperationException("Actual page source B");
        var closed = false; window.Closed += (_, _) => closed = true;
        window.Close();
        await page.Requested.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        page.Settled.TrySetException([a, b]);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken));
        var causes = error is AggregateException group ? group.Flatten().InnerExceptions : [error];
        Assert.Contains(a, causes); Assert.Contains(b, causes);
        Assert.False(closed);
        Assert.Same(page, ((ContentControl)window.Content!).Content);
        Assert.Equal(1, OwnedCount(service, "_originalChildren"));
        Assert.Equal(1, OwnedCount(service, "_originalWindows"));
        Assert.Equal(1, OwnedCount(service, "_originalPopups"));
        Assert.Equal(1, OwnedCount(service, "_originalChildCloses"));
        // A failed child is intentionally retained. Hide only this headless fixture;
        // do not manufacture a successful production close by releasing its content.
        window.Hide();
        await sessions.SaveNowAndCancelPendingAsync(TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task Repeated_successful_persisted_popups_release_only_their_retired_child_and_closed_window_custody()
    {
        var sessions = new WorkspaceSessionCoordinator(new Repository());
        var service = new WorkspaceWindowService(new CallbackServices(() => throw new InvalidOperationException()), sessions, null!);
        try
        {
            for (var iteration = 0; iteration < 5; iteration++)
            {
                var page = new HeldPage(); page.Settled.SetResult();
                var tab = new WorkspaceTabViewModel("chat", "Bounded popup", page, true, HavenSurface.Home);
                typeof(WorkspaceWindowService).GetMethod("RestorePersistedPopUp", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(service, [tab, SavedWindow(tab.SessionId, WorkspaceWindowKind.PopUp)]);
                var window = Assert.Single((List<Window>)typeof(WorkspaceWindowService).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                Assert.Equal(1, OwnedCount(service, "_originalChildren"));
                window.Close();
                await closed.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
                Assert.True(page.Settled.Task.IsCompletedSuccessfully);
                Assert.Equal(0, OwnedCount(service, "_originalChildren"));
                Assert.Equal(0, OwnedCount(service, "_originalWindows"));
                Assert.Equal(0, OwnedCount(service, "_originalPopups"));
                Assert.Equal(0, OwnedCount(service, "_originalChildCloses"));
            }
        }
        finally
        {
            await service.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            await sessions.SaveNowAndCancelPendingAsync(TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Actual_normal_window_factory_releases_custody_only_after_resources_and_native_callback_succeed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "9to1-window-custody-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var preferences = new UserPreferencesService(new Paths(directory));
        var service = new WorkspaceWindowService(new CallbackServices(() => preferences), null!, null!);
        try
        {
            for (var iteration = 0; iteration < 5; iteration++)
            {
                // Use the actual maintained native window factory, with no legacy
                // MainView/application revival or alternate page owner graph.
                var window = (MainWindow)typeof(WorkspaceWindowService).GetMethod("CreateMainWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!;
                Assert.Null(window.OriginalWindowResourcesClose); // Getter creates no task/admission.
                Assert.Equal(1, OwnedCount(service, "_originalWindows"));
                window.Show(); window.Close();
                await window.OriginalNativeClosedCallback.WaitAsync(Bound, TestContext.Current.CancellationToken);
                Assert.True(window.OriginalWindowResourcesClose!.IsCompletedSuccessfully);
                Assert.Equal(0, OwnedCount(service, "_originalWindows"));
                Assert.Equal(0, OwnedCount(service, "_originalChildren"));
                Assert.Null(typeof(MainWindow).GetField("_originalClosedCustodyRelease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            }
        }
        finally
        {
            await service.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Failed_post_close_bookkeeping_remains_a_failed_actual_window_delivery()
    {
        var window = new MainWindow();
        var cause = new IOException("Actual parent custody bookkeeping failed.");
        window.RegisterOriginalClosedCustodyRelease(_ => throw cause);
        window.Show(); window.Close();
        Assert.NotNull(window.OriginalWindowCloseDelivery);
        var error = await Assert.ThrowsAsync<IOException>(() => window.OriginalWindowCloseDelivery.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Same(cause, error);
        Assert.Same(cause, window.OriginalWindowCloseFailure);
        await Assert.ThrowsAsync<IOException>(() => window.CloseAndDrainAsync().WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Null(typeof(MainWindow).GetField("_originalClosedCustodyRelease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
    }

    private static int OwnedCount(WorkspaceWindowService service, string name)
    {
        var value = typeof(WorkspaceWindowService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        return (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    }

    private static async Task<(WorkspaceWindowService, WorkspaceSessionCoordinator, Window)> CreatePopupAsync(HeldPage page)
    {
        var sessions = new WorkspaceSessionCoordinator(new Repository());
        var service = new WorkspaceWindowService(new CallbackServices(() => throw new InvalidOperationException()), sessions, null!);
        var tab = new WorkspaceTabViewModel("chat", "Test popup", page, true, HavenSurface.Home);
        // Exercise the actual maintained restoration endpoint with a genuine tab and
        // native host. Reflection avoids constructing an unrelated legacy MainView.
        typeof(WorkspaceWindowService).GetMethod("RestorePersistedPopUp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [tab, SavedWindow(tab.SessionId, WorkspaceWindowKind.PopUp)]);
        var window = Assert.Single((List<Window>)typeof(WorkspaceWindowService).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!);
        Assert.True(window.IsVisible);
        await sessions.SaveFinalSnapshotAndSealAsync(TestContext.Current.CancellationToken);
        return (service, sessions, window);
    }
    private static WorkspaceSessionSnapshot Snapshot() => new(1, [], [], [SavedWindow(Guid.NewGuid(), WorkspaceWindowKind.Normal)], DateTimeOffset.UtcNow);
    private static WorkspaceWindowSnapshot SavedWindow(Guid tab, WorkspaceWindowKind kind) => new(Guid.NewGuid(), kind,
        new(Guid.NewGuid(), WorkspaceLayoutKind.Single, SplitOrientation.Vertical, 1, [new(Guid.NewGuid(), tab, 0)]),
        [tab], tab, null, DateTimeOffset.UtcNow);
    private sealed class CallbackServices(Func<object> factory) : IServiceProvider
    {
        internal int Calls;
        public object? GetService(Type serviceType) { Interlocked.Increment(ref Calls); return factory(); }
    }
    private sealed class Repository : IWorkspaceSessionRepository
    {
        public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<WorkspaceSessionSnapshot?>(null);
        public Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class HeldPage : Control, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
    {
        internal readonly TaskCompletionSource Requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void RequestRetirement() => Requested.TrySetResult();
        public Task CloseAndDrainAsync() => Settled.Task;
        public void DemandExternalOriginalRetirementJoin() { }
    }
}
