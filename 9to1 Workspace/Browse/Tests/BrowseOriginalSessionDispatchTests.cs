using Haven.Application;
using Haven.Browser;
using Xunit;
namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseOriginalSessionDispatchTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Original_session_or_disposed_selection_is_denied_at_held_execution_entry_without_emission(bool dispose)
    {
        using var session = new BrowserSessionService(new Paths()); var host = new GuardedHost(); session.Attach(host);
        var selected = session.CaptureOriginalSession(); var admission = new Admission();
        var operation = session.ExecuteOriginalSessionScriptAsync(selected, "effect", admission, default);
        await host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose) selected.Dispose(); else session.Attach(new GuardedHost());
        host.Release.TrySetResult(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        Assert.Equal(0, host.Emissions); Assert.Equal(0, admission.Checks);
    }
    [Fact]
    public async Task Original_session_is_rechecked_after_suspended_principal_admission_before_emission()
    {
        using var session = new BrowserSessionService(new Paths()); var host = new GuardedHost(); session.Attach(host);
        var admission = new Admission { Hold = true }; host.Release.TrySetResult();
        var operation = session.ExecuteOriginalSessionScriptAsync(session.CaptureOriginalSession(), "effect", admission, default);
        await admission.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); session.Detach(host);
        admission.Release.TrySetResult(); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        Assert.Equal(0, host.Emissions); Assert.Equal(1, admission.Checks);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_selected_adapter_calls_admission_at_execution_entry_and_only_emits_when_allowed(bool allowed)
    {
        using var session = new BrowserSessionService(new Paths()); var host = new GuardedHost(); session.Attach(host);
        host.Release.TrySetResult(); var admission = new Admission { Allowed = allowed };
        var operation = session.ExecuteOriginalSessionScriptAsync(session.CaptureOriginalSession(), "effect", admission, default);
        if (allowed) Assert.Equal("observed", await operation);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        Assert.Equal(allowed ? 1 : 0, host.Emissions); Assert.Equal(1, admission.Checks);
    }
    [Fact]
    public async Task Legacy_host_without_final_entry_capability_denies_without_unguarded_script_fallback()
    {
        using var session = new BrowserSessionService(new Paths()); var host = new LegacyOnlyHost(); session.Attach(host);
        var admission = new Admission();
        await Assert.ThrowsAsync<NotSupportedException>(() => session.ExecuteOriginalSessionScriptAsync(
            session.CaptureOriginalSession(), "effect", admission, default));
        Assert.Equal(0, host.LegacyCalls); Assert.Equal(0, admission.Checks);
    }
    [Fact]
    public async Task Selection_from_another_actual_session_issuer_cannot_emit_on_current_host()
    {
        using var original = new BrowserSessionService(new Paths()); using var replacement = new BrowserSessionService(new Paths());
        var host = new GuardedHost(); original.Attach(new GuardedHost()); replacement.Attach(host);
        var admission = new Admission();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => replacement.ExecuteOriginalSessionScriptAsync(
            original.CaptureOriginalSession(), "effect", admission, default));
        Assert.Equal(0, host.Emissions); Assert.False(host.Entered.Task.IsCompleted); Assert.Equal(0, admission.Checks);
    }
    private sealed class LegacyOnlyHost : IEmbeddedBrowserHost
    {
        public BrowserSnapshot State { get; } = new(null, "legacy", false, false, false, "test");
        public event EventHandler<BrowserSnapshot>? StateChanged { add { } remove { } }
        public int LegacyCalls { get; private set; }
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token)
        { LegacyCalls++; return Task.FromResult<string?>("legacy"); }
        public Task NavigateAsync(Uri address, CancellationToken token) => Task.CompletedTask;
        public Task GoBackAsync(CancellationToken token) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Admission : IBrowserScriptDispatchAdmission
    {
        public bool Allowed { get; set; } = true;
        public bool Hold { get; set; }
        public int Checks { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> CheckAsync(CancellationToken token)
        { Checks++; Entered.TrySetResult(); if (Hold) await Release.Task.WaitAsync(token); return Allowed; }
    }
    // Controlled execution-entry adapter lowerbound, not a real native webview or Home grant.
    private sealed class GuardedHost : IOriginalSessionGuardedBrowserHost
    {
        public BrowserSnapshot State { get; } = new(null, "test", false, false, false, "test");
        public event EventHandler<BrowserSnapshot>? StateChanged { add { } remove { } }
        public int Emissions { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string?> ExecuteScriptGuardedAsync(string script, IBrowserScriptDispatchAdmission admission, CancellationToken token)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(token);
            if (!await admission.CheckAsync(token)) throw new UnauthorizedAccessException();
            Emissions++; return "observed";
        }
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) => throw new InvalidOperationException("No legacy fallback permitted.");
        public Task NavigateAsync(Uri address, CancellationToken token) => Task.CompletedTask;
        public Task GoBackAsync(CancellationToken token) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory => "/unused";
        public string DatabasePath => "/unused/database";
        public string BrowserProfileDirectory => "/unused/browser";
        public string AttachmentsDirectory => "/unused/attachments";
        public string LogsDirectory => "/unused/logs";
        public string LegacyStatePath => "/unused/legacy";
    }
}
