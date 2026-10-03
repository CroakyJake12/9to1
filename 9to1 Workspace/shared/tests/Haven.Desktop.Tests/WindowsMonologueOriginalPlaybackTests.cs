#if HAVEN_WINDOWS_DESKTOP
using Haven.Application;
using Haven.Application.Call;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

// Genuine installed Windows voice/media + physical canonical settings. No controlled speech provider,
// source/model/Home grant, physical microphone or output-device configuration is inferred.
public sealed class WindowsMonologueOriginalPlaybackTests
{
    [Fact]
    public async Task Actual_original_native_pause_ack_survives_physical_CAS_cancellation_then_resumes_same_playback_without_synthesis()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-windows-monologue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45)); var ct = deadline.Token;
        await using var output = new WindowsNaturalSpeechOutputService();
        Task<VisionVoiceResult<MultimodalSession>>? pending = null;
        Exception? observationFailure = null;
        var paths = new Paths(root); var actual = new VersionedAtomicSettingsStore(paths);
        using var store = new HeldActualCas(actual, Path.Combine(root, "settings.json.lock"));
        try
        {
            Assert.True(output.IsAvailable, output.UnavailableReason); var voices = output.Voices; Assert.NotEmpty(voices);
            var voiceId = voices[0].Id; Assert.True(output.CanContinueVoice(voiceId));
            var sessions = new VisionVoiceSessionService(new MultimodalSessionStore(store));
            var started = await sessions.StartAsync(Guid.NewGuid(), null, VisionVoiceMode.Monologue, "local-only", true, cancellationToken: ct);
            var planned = await sessions.PlanMonologueAsync(started.Value!.SessionId, started.Value.Revision,
                "Original native narration", null, ["Original section"], [], ct); Assert.True(planned.IsSuccess);
            var original = await MonologueOriginalPlayback.StartAsync(sessions, output, planned.Value!.SessionId,
                planned.Value.Revision, planned.Value.Monologue!.RunId, 0,
                string.Join(" ", Enumerable.Repeat("This original section retains its native media and its planned structure.", 100)),
                voiceId, "default", () => true, ct);
            var originalId = original.PlaybackId; var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), ct);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            store.HoldNext = true; pending = original.PauseAsync(cancellation.Token);
            await store.Entered.Task.WaitAsync(ct);
            var acknowledged = original.PendingCheckpoint; Assert.NotNull(acknowledged);
            Assert.Equal(originalId, acknowledged!.PlaybackId); Assert.True(acknowledged.IsPaused);
            Assert.True(acknowledged.Position >= TimeSpan.Zero); Assert.False(original.Completion.IsCompleted);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), ct));
            cancellation.Cancel();
            try { Assert.False((await pending).IsSuccess); } catch (OperationCanceledException) { }
            Assert.Same(acknowledged, original.PendingCheckpoint);
            store.Release();
            Assert.True((await original.PersistPendingAsync(ct)).IsSuccess); Assert.Null(original.PendingCheckpoint);
            var reopened = await new VisionVoiceSessionService(new MultimodalSessionStore(actual)).GetSessionAsync(planned.Value.SessionId, ct);
            Assert.Equal(planned.Value.Monologue.RunId, reopened.Value!.Monologue!.RunId);
            Assert.True(reopened.Value.Monologue.IsPaused); Assert.Equal(acknowledged.Position, reopened.Value.Monologue.Position);
            var resumed = await original.ResumeAsync(ct); Assert.True(resumed.IsSuccess);
            Assert.False(resumed.Value!.Monologue!.IsPaused); Assert.True(resumed.Value.Monologue.Position >= acknowledged.Position);
            Assert.Equal(originalId, original.PlaybackId); Assert.False(original.Completion.IsCompleted);
        }
        catch (Exception error) { observationFailure = error; throw; }
        finally
        {
            store.Release();
            try { if (pending is not null) await pending; } catch (OperationCanceledException) { }
            catch when (observationFailure is not null) { }
            finally
            {
                try { await output.StopAsync(CancellationToken.None); }
                catch when (observationFailure is not null) { }
                finally
                {
                    try { Directory.Delete(root, true); }
                    catch when (observationFailure is not null) { }
                }
            }
        }
    }

    // Holds the real physical settings lease immediately before forwarding actual CAS; no fabricated
    // result/authority or replacement store is supplied. Cancellation exercises the production writer.
    private sealed class HeldActualCas(VersionedAtomicSettingsStore actual, string lockPath) : IVersionedSettingsStore, IVersionedSettingsCompareExchange, IDisposable
    {
        private FileStream? _held; public bool HoldNext;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class => actual.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class => actual.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct) => actual.RemoveAsync(key, ct);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken ct) => actual.ExportAsync(ct);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken ct) => actual.ImportAsync(manifest, ct);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken ct)
        {
            if (HoldNext) { HoldNext = false; _held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); Entered.TrySetResult(); }
            return actual.CompareExchangeAsync(key, expected, replacement, ct);
        }
        public void Release() { _held?.Dispose(); _held = null; }
        public void Dispose() => Release();
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    }
}
#endif
