using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Browser;
using Haven.Desktop.Views;

namespace Haven.Desktop.Tests;

public sealed class BrowserOwnedNativeEntryTests
{
    [AvaloniaFact]
    public async Task Cancellation_does_not_release_lease_before_actual_evaluation_completion()
    {
        using var paths = new Paths();
        using var permissions = new BrowserSitePermissionStore(paths);
        var evaluation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new Lease();
        using var host = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            _ => { entered.TrySetResult(); return evaluation.Task; });
        using var cancel = new CancellationTokenSource();
        var action = host.ExecuteOwnedScriptAsync(Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection()), "effect", new Admission(lease), cancel.Token);
        await entered.Task;
        cancel.Cancel();
        Assert.False(action.IsCompleted);
        Assert.False(lease.Disposed);
        evaluation.TrySetResult("observed-evaluation");
        await Assert.ThrowsAsync<InvalidOperationException>(() => action);
        Assert.True(lease.Disposed);
    }

    [AvaloniaFact]
    public async Task Denied_lease_emits_no_effect_and_closes_observation_capability()
    {
        using var paths = new Paths();
        using var permissions = new BrowserSitePermissionStore(paths);
        var scripts = new List<string>();
        var admission = new Admission(new Lease { Current = false }, observe: true);
        using var host = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            script => { scripts.Add(script); return Task.FromResult<string?>("schema"); });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.ExecuteOwnedScriptAsync(Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection()), "effect", admission, CancellationToken.None));
        Assert.Equal(new[] { "schema-observation" }, scripts);
        Assert.True(admission.Lease.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => admission.Entry!.EvaluateObservationAsync("late-observation", CancellationToken.None));
        Assert.Single(scripts);
    }

    [AvaloniaFact]
    public async Task Disposal_during_actual_evaluation_waits_for_completion_then_reports_unknown()
    {
        using var paths = new Paths();
        using var permissions = new BrowserSitePermissionStore(paths);
        var evaluation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new Lease();
        using var host = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            _ => { entered.TrySetResult(); return evaluation.Task; });
        var action = host.ExecuteOwnedScriptAsync(Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection()), "effect", new Admission(lease), CancellationToken.None);
        await entered.Task;
        host.Dispose();
        Assert.False(action.IsCompleted);
        Assert.False(lease.Disposed);
        evaluation.TrySetResult("late-engine-completion");
        await Assert.ThrowsAsync<InvalidOperationException>(() => action);
        Assert.True(lease.Disposed);
    }

    [AvaloniaFact]
    public async Task Native_evaluation_fault_releases_lease_only_after_actual_fault()
    {
        using var paths = new Paths();
        using var permissions = new BrowserSitePermissionStore(paths);
        var evaluation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new Lease();
        using var host = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            _ => { entered.TrySetResult(); return evaluation.Task; });
        var action = host.ExecuteOwnedScriptAsync(Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection()), "effect", new Admission(lease), CancellationToken.None);
        await entered.Task;
        Assert.False(lease.Disposed);
        evaluation.TrySetException(new IOException("Actual callback failure"));
        await Assert.ThrowsAsync<IOException>(() => action);
        Assert.True(lease.Disposed);
    }

    [AvaloniaFact]
    public async Task Explicit_navigation_waits_for_actual_evaluation_and_lease_release()
    {
        using var paths = new Paths();
        using var permissions = new BrowserSitePermissionStore(paths);
        var evaluation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new Lease();
        using var host = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            _ => { entered.TrySetResult(); return evaluation.Task; });
        var action = host.ExecuteOwnedScriptAsync(Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection()), "effect", new Admission(lease), CancellationToken.None);
        await entered.Task;
        var navigation = host.NavigateAsync(new Uri("https://example.test/next"), CancellationToken.None);
        Assert.False(navigation.IsCompleted);
        Assert.False(lease.Disposed);
        evaluation.TrySetResult("callback-completed");
        Assert.Equal("callback-completed", await action);
        await navigation;
        Assert.True(lease.Disposed);
        Assert.Equal(new Uri("https://example.test/next"), host.State.Address);
    }

    [AvaloniaFact]
    public async Task Missing_actual_lease_support_never_uses_legacy_unleased_effect_path()
    {
        using var paths = new Paths();
        using var permissions = new BrowserSitePermissionStore(paths);
        var emitted = 0;
        using var host = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            _ => { emitted++; return Task.FromResult<string?>("forbidden-effect"); });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.ExecuteOwnedScriptAsync(Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection()), "effect", new MissingLease(), CancellationToken.None));
        Assert.Equal(0, emitted);
    }
    [AvaloniaFact]
    public async Task Original_displayed_native_document_denies_after_same_host_navigation_before_observation_or_effect()
    {
        using var paths = new Paths();
        using var permissions = new BrowserSitePermissionStore(paths);
        var scripts = new List<string>();
        using var host = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            script => { scripts.Add(script); return Task.FromResult<string?>("colliding-original-schema"); });
        var original = Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection());
        await host.NavigateAsync(new Uri("https://example.test/new-document"), CancellationToken.None);
        var admission = new Admission(new Lease(), observe: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.ExecuteOwnedScriptAsync(original, "effect", admission, CancellationToken.None));
        Assert.Null(admission.Entry); Assert.Empty(scripts);
        // A new document requires a genuine new display capture; it cannot rescue the old marker.
        var current = Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(host.CaptureDocumentSelection());
        Assert.NotSame(original, current);
    }
    [AvaloniaFact]
    public async Task Copied_host_marker_and_missing_original_marker_never_admit_native_effect()
    {
        using var paths = new Paths(); using var permissions = new BrowserSitePermissionStore(paths);
        var emitted = 0;
        using var first = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            _ => Task.FromResult<string?>("schema"));
        using var second = new NativeWebViewHost(new NativeWebView(), permissions, _ => Task.CompletedTask,
            _ => { emitted++; return Task.FromResult<string?>("effect"); });
        var original = Assert.IsAssignableFrom<IBrowserNativeDocumentSelection>(first.CaptureDocumentSelection());
        var admission = new Admission(new Lease());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second.ExecuteOwnedScriptAsync(original, "effect", admission, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second.ExecuteOwnedScriptAsync("effect", admission, CancellationToken.None));
        Assert.Null(admission.Entry); Assert.Equal(0, emitted);
    }

    private sealed class MissingLease : IBrowserOwnedScriptDispatchAdmission
    {
        public ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry, CancellationToken token)
            => ValueTask.FromResult<IBrowserScriptDispatchLease?>(null);
    }

    private sealed class Admission(Lease lease, bool observe = false) : IBrowserOwnedScriptDispatchAdmission
    {
        public Lease Lease { get; } = lease;
        public IBrowserNativeEntryObservation? Entry { get; private set; }
        public async ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry, CancellationToken token)
        {
            Entry = entry;
            if (observe) await entry.EvaluateObservationAsync("schema-observation", token);
            return Lease;
        }
    }
    private sealed class Lease : IBrowserScriptDispatchLease
    {
        public bool Current { get; init; } = true;
        public bool Disposed { get; private set; }
        public ValueTask<bool> CheckAsync(CancellationToken token) => ValueTask.FromResult(Current && !Disposed);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-owned-native-entry-" + Guid.NewGuid().ToString("N"));
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
