using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Desktop.Events;
using Haven.Desktop.Overlay;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

public sealed class OverlayControllerOriginalLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retirement_joins_the_same_held_registry_read_and_refuses_late_admission()
    {
        var settings = new Settings();
        var hotkey = new OverlayGlobalHotkey();
        var controller = Controller(new(settings), hotkey);
        var initialize = controller.InitializeAsync(Token);
        await settings.Entered.Task.WaitAsync(Bound, Token);
        var close = controller.CloseAndDrainAsync();
        try
        {
            Assert.False(initialize.IsCompleted);
            Assert.False(close.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => controller.InitializeAsync(Token));
        }
        finally { settings.Read.TrySetResult(null); }
        await initialize.WaitAsync(Bound, Token);
        await close.WaitAsync(Bound, Token);
        Assert.Same(close, controller.CloseAndDrainAsync());
        Assert.Throws<ObjectDisposedException>(() => hotkey.Start());
    }

    [Fact]
    public async Task Restored_physical_settings_source_cannot_join_and_its_actual_fault_survives()
    {
        var context = ExecutionContext.Capture()!;
        var settings = new Settings();
        var controller = Controller(new(settings), new());
        Task? attemptedJoin = null;
        settings.OnRead = () => ExecutionContext.Run(context, _ =>
        {
            try { attemptedJoin = controller.CloseAndDrainAsync(); }
            catch (Exception cause) { attemptedJoin = Task.FromException(cause); }
        }, null);
        var initialize = controller.InitializeAsync(Token);
        Assert.NotNull(attemptedJoin);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attemptedJoin.WaitAsync(Bound, Token));
        var a = new IOException("Actual registry source A");
        settings.Read.SetException(a);
        await Assert.ThrowsAnyAsync<Exception>(() => initialize.WaitAsync(Bound, Token));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => controller.CloseAndDrainAsync().WaitAsync(Bound, Token));
        var causes = AllCauses(failure).ToArray();
        Assert.Contains(a, causes);
    }

    [AvaloniaFact]
    public async Task Already_queued_dispatcher_work_is_joined_before_controller_retirement()
    {
        var settings = new Settings(); settings.Read.SetResult(null);
        var controller = Controller(new(settings), new());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(controller, async () => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(Bound, Token);
        var close = controller.CloseAndDrainAsync();
        try { Assert.False(close.IsCompleted); }
        finally { release.TrySetResult(); }
        await close.WaitAsync(Bound, Token);
    }

    [AvaloniaFact]
    public async Task Queued_actual_source_keeps_all_direct_fault_siblings_at_retirement()
    {
        var settings = new Settings(); settings.Read.SetResult(null);
        var controller = Controller(new(settings), new());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(controller, () => { entered.SetResult(); return actual.Task; });
        await entered.Task.WaitAsync(Bound, Token);
        var a = new IOException("Actual queued source A");
        var b = new InvalidOperationException("Actual queued source B");
        actual.SetException([a, b]);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => controller.CloseAndDrainAsync().WaitAsync(Bound, Token));
        var causes = AllCauses(failure).ToArray();
        Assert.Contains(a, causes); Assert.Contains(b, causes);
    }

    [AvaloniaFact]
    public async Task Native_close_waits_the_real_Go_page_original_before_destroying_its_window()
    {
        var (controller, page, window, id) = await MountAsync();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.RetainOriginalParentBorrower(held.Task, () => { }); // This test owns the exact held original.
        try
        {
            window.Close();
            Assert.False(window.OriginalNativeClosed);
            Assert.NotNull(window.Content);
            held.SetResult();
            await controller.CloseAndDrainAsync().WaitAsync(Bound, Token);
            Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
            Assert.True(window.OriginalNativeClosed);
        }
        finally
        {
            held.TrySetResult();
            await controller.CloseAndDrainAsync().WaitAsync(Bound, Token);
        }
    }

    [AvaloniaFact]
    public async Task Noncancelable_picker_settles_before_page_retirement_and_cannot_publish_after_seal()
    {
        var (controller, page, window, id) = await MountAsync();
        var attachments = (TaskAttachmentContext)typeof(GoPage).GetField("_attachments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        var filePath = Path.Combine(Path.GetTempPath(), "9to1-overlay-picker-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(filePath, "owned picker fixture", Token);
        var storageFileType = typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile")!;
        using var selectedFile = (IStorageFile)Activator.CreateInstance(storageFileType, new FileInfo(filePath))!;
        var attached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(controller, id, async () =>
        {
            await controller.AttachFilesToGoFromPickerAsync(id, page,
                () => Task.FromResult<IReadOnlyList<IStorageFile>>([selectedFile]));
            attached.SetResult();
        });
        await attached.Task.WaitAsync(Bound, Token);
        Assert.Contains(filePath, attachments.Files); // The actual live picker path remains functional.
        page.TakeAttachments();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = new TaskCompletionSource<IReadOnlyList<IStorageFile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(controller, id, () => controller.AttachFilesToGoFromPickerAsync(id, page, () =>
        { entered.SetResult(); return files.Task; }));
        await entered.Task.WaitAsync(Bound, Token);
        var close = controller.CloseAndDrainAsync();
        try
        {
            Assert.False(close.IsCompleted);
            Assert.Null(page.OriginalClose); // No page was retired underneath the live OS picker.
            Assert.False(window.OriginalNativeClosed);
        }
        finally { files.TrySetResult([selectedFile]); }
        try
        {
            await close.WaitAsync(Bound, Token);
            Assert.Empty(attachments.Files); // The late real selection did not publish after sealing.
            Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
            Assert.True(window.OriginalNativeClosed);
        }
        finally { File.Delete(filePath); }
    }

    [Fact]
    public async Task Linux_unavailable_hotkey_joins_no_native_thread_and_keeps_same_disposal()
    {
        if (OperatingSystem.IsWindows()) return; // This contract never registers a live Windows hotkey.
        var hotkey = new OverlayGlobalHotkey();
        Assert.False(hotkey.Start());
        Assert.False(hotkey.IsRegistered);
        Assert.NotNull(hotkey.UnavailableReason);
        Assert.True(hotkey.OriginalThreadCompletion.IsCompletedSuccessfully);
        var close = hotkey.DisposeAsync().AsTask();
        await close.WaitAsync(Bound, Token);
        Assert.Same(close, hotkey.DisposeAsync().AsTask());
        Assert.Throws<ObjectDisposedException>(() => hotkey.Start());
    }

    private static OverlayWorkspaceController Controller(OverlayWorkspaceRegistry registry, OverlayGlobalHotkey hotkey) =>
        new(registry, null!, null!, null!, null!, null!, null!, hotkey, null!);
    private static void Queue(OverlayWorkspaceController controller, Func<Task> source) =>
        typeof(OverlayWorkspaceController).GetMethod("RunOnUi", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Func<Task>)])!.Invoke(controller, [source]);
    private static void Queue(OverlayWorkspaceController controller, Guid id, Func<Task> source) =>
        typeof(OverlayWorkspaceController).GetMethod("RunOnUi", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Guid), typeof(Func<Task>)])!.Invoke(controller, [id, source]);
    private static async Task<(OverlayWorkspaceController, GoPage, OverlayWorkspaceWindow, Guid)> MountAsync()
    {
        var settings = new Settings(); settings.Read.SetResult(null);
        var registry = new OverlayWorkspaceRegistry(settings);
        var session = await registry.OpenSessionAsync("go", "Test Overlay", null, null, Token);
        var controller = Controller(registry, new());
        var page = new GoPage(new HavenEventBus());
        // Actual production mount/wiring; skip unrelated external catalogue discovery.
        var method = typeof(OverlayWorkspaceController).GetMethod("Mount", BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(OverlaySessionState), typeof(GoPage)])!;
        method.Invoke(controller, [session, page]);
        var windows = (Dictionary<Guid, OverlayWorkspaceWindow>)typeof(OverlayWorkspaceController)
            .GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        var window = windows[session.Id]; window.ShowAndActivate();
        return (controller, page, window, session.Id);
    }
    private static IEnumerable<Exception> AllCauses(Exception error)
    {
        yield return error;
        if (error is AggregateException group)
            foreach (var child in group.InnerExceptions)
                foreach (var cause in AllCauses(child)) yield return cause;
        else if (error.InnerException is { } child)
            foreach (var cause in AllCauses(child)) yield return cause;
    }
    private sealed class Settings : IVersionedSettingsStore
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<OverlayWorkspacePersistedState?> Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? OnRead;
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { Entered.TrySetResult(); OnRead?.Invoke(); return (Task<T?>)(object)Read.Task; }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken token) => Task.CompletedTask;
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new InvalidOperationException("Unrelated export");
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new InvalidOperationException("Unrelated import");
    }
}
