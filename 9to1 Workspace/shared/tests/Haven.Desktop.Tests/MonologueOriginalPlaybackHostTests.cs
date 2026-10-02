using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Call;
using Haven.Desktop.Views.Pages.Call;
namespace Haven.Desktop.Tests;

// Real mounted native controls and physical canonical CAS. Speech is controlled protocol only;
// this class does not establish installed native voices/audio or source/model/output grants.
public sealed class MonologueOriginalPlaybackHostTests
{
    [AvaloniaFact]
    public async Task Actual_native_buttons_pause_resume_same_original_and_detach_cleanup_cannot_reissue_or_write()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var plan = await f.Plan(ct); var speech = new Speech();
        var original = await MonologueOriginalPlayback.StartAsync(f.Service, speech, plan.SessionId,
            plan.Revision, plan.Monologue!.RunId, 0, "Separately admitted narration.", "original-voice", null, () => true, ct);
        using var host = new MonologueOriginalPlaybackHost(original, () => true);
        var window = new Window { Content = host }; window.Show(); Exception? primary = null;
        try
        {
            await Press(window, host, "Pause original narration", ct);
            Assert.True(host.LastObservation!.IsSuccess); Assert.True(host.LastObservation.Value!.Monologue!.IsPaused);
            await Press(window, host, "Resume original narration", ct);
            Assert.False(host.LastObservation!.Value!.Monologue!.IsPaused);
            Assert.Equal(1, speech.Starts); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(1, speech.Handle.Resumes);
            var saved = await f.Reopen().GetSessionAsync(plan.SessionId, ct);
            Assert.Equal(original.CanonicalReceipt, saved.Value!.Monologue!.PlaybackReceipt);
            var bytes = await File.ReadAllBytesAsync(f.StatePath, ct);
            window.Content = null; await host.WhenActionIdleAsync();
            Assert.Equal(1, speech.Handle.Stops); Assert.True(original.Completion.IsCanceled);
            window.Content = host;
            Assert.All(host.Children.OfType<Button>(), button => Assert.False(button.IsEnabled));
            Assert.False((await original.ResumeAsync(ct)).IsSuccess);
            Assert.Equal(1, speech.Handle.Resumes); Assert.Equal(1, speech.Starts);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath, ct));
        }
        catch (Exception error) { primary = error; throw; }
        finally { window.Close(); try { await host.WhenActionIdleAsync(); } catch when (primary is not null) { } }
    }

    [AvaloniaFact]
    public async Task Native_surface_retirement_retains_actual_committed_CAS_return_without_replaying_control()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var plan = await f.Plan(ct);
        var store = new HeldReturn(new VersionedAtomicSettingsStore(f.AppPaths));
        var sessions = new VisionVoiceSessionService(new MultimodalSessionStore(store)); var speech = new Speech();
        var original = await MonologueOriginalPlayback.StartAsync(sessions, speech, plan.SessionId,
            plan.Revision, plan.Monologue!.RunId, 0, "Admitted original narration.", "original-voice", null, () => true, ct);
        using var host = new MonologueOriginalPlaybackHost(original, () => true);
        var window = new Window { Content = host }; window.Show(); Task? pending = null; Exception? primary = null;
        try
        {
            store.HoldNext = true;
            PressKeys(window, host, "Pause original narration");
            pending = host.WhenActionIdleAsync();
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.False(pending.IsCompleted); Assert.NotNull(original.PendingCheckpoint);
            var committed = await File.ReadAllBytesAsync(f.StatePath, ct);
            var reopened = await f.Reopen().GetSessionAsync(plan.SessionId, ct);
            Assert.Equal(original.PlaybackId, reopened.Value!.Monologue!.PlaybackReceipt!.PlaybackId);
            Assert.True(reopened.Value.Monologue.IsPaused);
            host.Dispose(); Assert.Equal(1, speech.Handle.Stops);
            store.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(host.LastObservation!.IsSuccess); Assert.NotNull(original.CanonicalReceipt); Assert.Null(original.PendingCheckpoint);
            Assert.All(host.Children.OfType<Button>(), button => Assert.False(button.IsEnabled));
            Assert.False((await original.ResumeAsync(ct)).IsSuccess);
            Assert.Equal(1, speech.Starts); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(0, speech.Handle.Resumes);
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.StatePath, ct));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            store.Release.TrySetResult();
            try { if (pending is not null) await pending; } catch when (primary is not null) { }
            finally { window.Close(); try { await host.WhenActionIdleAsync(); } catch when (primary is not null) { } }
        }
    }

    [AvaloniaFact]
    public async Task Native_action_observes_lost_actual_CAS_return_and_retry_recovers_own_receipt_without_control_replay()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var plan = await f.Plan(ct);
        var lostReturn = new IOException("Actual checkpoint committed before its return was lost.");
        var store = new HeldReturn(new VersionedAtomicSettingsStore(f.AppPaths)) { FailAfterCommit = lostReturn };
        var sessions = new VisionVoiceSessionService(new MultimodalSessionStore(store)); var speech = new Speech();
        var original = await MonologueOriginalPlayback.StartAsync(sessions, speech, plan.SessionId,
            plan.Revision, plan.Monologue!.RunId, 0, "Admitted original narration.", "original-voice", null, () => true, ct);
        using var host = new MonologueOriginalPlaybackHost(original, () => true);
        var window = new Window { Content = host }; window.Show(); Exception? primary = null;
        try
        {
            var observed = await Assert.ThrowsAsync<IOException>(() => Press(window, host, "Pause original narration", ct));
            Assert.Same(lostReturn, observed); Assert.Same(lostReturn, host.LastActionFailure);
            Assert.NotNull(original.PendingCheckpoint); Assert.Null(original.CanonicalReceipt);
            var committed = await File.ReadAllBytesAsync(f.StatePath, ct);
            var saved = await f.Reopen().GetSessionAsync(plan.SessionId, ct);
            Assert.Equal(original.PlaybackId, saved.Value!.Monologue!.PlaybackReceipt!.PlaybackId);
            await Press(window, host, "Retry saved playback checkpoint", ct);
            Assert.True(host.LastObservation!.IsSuccess); Assert.Null(original.PendingCheckpoint);
            Assert.Equal(saved.Value.Monologue.PlaybackReceipt, original.CanonicalReceipt);
            Assert.Same(lostReturn, host.LastActionFailure);
            Assert.Equal(1, store.Exchanges); Assert.Equal(1, speech.Starts);
            Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(0, speech.Handle.Resumes);
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.StatePath, ct));
        }
        catch (Exception error) { primary = error; throw; }
        finally { window.Close(); try { await host.WhenActionIdleAsync(); } catch when (primary is not null) { } }
    }

    private static void PressKeys(Window window, MonologueOriginalPlaybackHost host, string label)
    {
        var button = Assert.Single(host.Children.OfType<Button>(), item => Equals(item.Content, label));
        Assert.True(button.IsEnabled); Assert.True(button.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
    }
    private static async Task Press(Window window, MonologueOriginalPlaybackHost host, string label, CancellationToken originalTestToken)
    {
        PressKeys(window, host, label);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await host.WhenActionIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), originalTestToken);
    }
    private sealed class Speech : IContinuableSpeechOutputService, IOriginalSpeechPlaybackReceiptIssuer
    {
        public int Starts; public Handle Handle { get; } = new();
        public bool CanContinueVoice(string voice) => voice == "original-voice";
        public bool WasIssuedPlayback(ISpeechPlaybackContinuation playback) => ReferenceEquals(playback, Handle);
        public bool IsOriginalPlayback(ISpeechPlaybackContinuation playback) => WasIssuedPlayback(playback) && !Handle.Completion.IsCompleted;
        public Task<ISpeechPlaybackContinuation> StartContinuableAsync(string text, string? voice, string? device, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Starts++; return Task.FromResult<ISpeechPlaybackContinuation>(Handle); }
    }
    private sealed class Handle : IOriginalSpeechPlaybackStop
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid PlaybackId { get; } = Guid.NewGuid(); public string VoiceId => "original-voice";
        public Task Completion => _completion.Task; public int Pauses, Resumes, Stops;
        public Task<SpeechPlaybackCheckpoint> PauseAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Pauses++; return Task.FromResult(new SpeechPlaybackCheckpoint(PlaybackId, VoiceId, TimeSpan.FromSeconds(12), true)); }
        public Task<SpeechPlaybackCheckpoint> ResumeAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Resumes++; return Task.FromResult(new SpeechPlaybackCheckpoint(PlaybackId, VoiceId, TimeSpan.FromSeconds(12), false)); }
        public Task<bool> StopOriginalAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (_completion.Task.IsCompleted) return Task.FromResult(false); Stops++; _completion.TrySetCanceled(); return Task.FromResult(true); }
    }
    private sealed class HeldReturn(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public bool HoldNext; public Exception? FailAfterCommit; public int Exchanges; public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class => actual.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class => actual.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct) => actual.RemoveAsync(key, ct);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken ct) => actual.ExportAsync(ct);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken ct) => actual.ImportAsync(value, ct);
        public async Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken ct)
        {
            var result = await actual.CompareExchangeAsync(key, expected, replacement, ct);
            Exchanges++;
            if (result.Exchanged && FailAfterCommit is { } error) { FailAfterCommit = null; throw error; }
            if (HoldNext && result.Exchanged) { HoldNext = false; Entered.TrySetResult(); await Release.Task; }
            return result;
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-original-playback-" + Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(_root, "settings.json");
        private readonly IAppPaths _paths;
        public IAppPaths AppPaths => _paths;
        public VisionVoiceSessionService Service { get; }
        public Fixture() { Directory.CreateDirectory(_root); _paths = new Paths(_root); Service = Reopen(); }
        public VisionVoiceSessionService Reopen() => new(new MultimodalSessionStore(new VersionedAtomicSettingsStore(_paths)));
        public async Task<MultimodalSession> Plan(CancellationToken ct)
        {
            var started = await Service.StartAsync(Guid.NewGuid(), null, VisionVoiceMode.Monologue, "local-only", true, cancellationToken: ct);
            var planned = await Service.PlanMonologueAsync(started.Value!.SessionId, started.Value.Revision, "Original objective", null, ["Original section"], [], ct);
            Assert.True(planned.IsSuccess);
            var activated = await Service.RecordMonologueProgressAsync(planned.Value!.SessionId,
                planned.Value.Revision, planned.Value.Monologue!.RunId, 0, TimeSpan.Zero, false, ct);
            Assert.True(activated.IsSuccess); return activated.Value!;
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    }
}
