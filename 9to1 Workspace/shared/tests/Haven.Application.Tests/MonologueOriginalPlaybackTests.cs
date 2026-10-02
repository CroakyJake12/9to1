using Haven.Application.Call;

namespace Haven.Application.Tests;

// Real physical canonical settings/CAS with a controlled playback protocol endpoint.
// These portable tests establish coordinator ordering and no replay, never native audio or issuance authority.
public sealed class MonologueOriginalPlaybackTests
{
    [Fact]
    public async Task Actual_metadata_pause_resume_reopen_preserves_same_run_and_controlled_original_handle()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var planned = await f.Plan(ct); var speech = new ControlledSpeech();
        var original = await MonologueOriginalPlayback.StartAsync(f.Service, speech, planned.SessionId,
            planned.Revision, planned.Monologue!.RunId, 0, "Already authorized section text.", "original-voice", null, () => true, ct);
        var pause = await original.PauseAsync(ct); Assert.True(pause.IsSuccess);
        Assert.True(pause.Value!.Monologue!.IsPaused); Assert.Equal(TimeSpan.FromSeconds(12), pause.Value.Monologue.Position);
        var resume = await original.ResumeAsync(ct); Assert.True(resume.IsSuccess);
        Assert.False(resume.Value!.Monologue!.IsPaused); Assert.Equal(planned.Monologue.RunId, resume.Value.Monologue.RunId);
        Assert.Equal(planned.ConversationId, resume.Value.ConversationId); Assert.Equal(speech.Handle.PlaybackId, original.PlaybackId);
        Assert.Equal(1, speech.Starts); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(1, speech.Handle.Resumes);
        var reopened = await f.Reopen().GetSessionAsync(planned.SessionId, ct);
        Assert.Equal(resume.Value.Revision, reopened.Value!.Revision);
        Assert.Equal(TimeSpan.FromSeconds(12), reopened.Value.Monologue!.Position);
        Assert.Null(original.PendingCheckpoint);
        Assert.NotNull(original.CanonicalReceipt); Assert.Equal(original.PlaybackId, original.CanonicalReceipt!.PlaybackId);
    }

    [Fact]
    public async Task Native_ack_is_retained_during_actual_settings_lock_cancel_and_metadata_retry_does_not_replay_control()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var planned = await f.Plan(ct); var speech = new ControlledSpeech();
        var original = await MonologueOriginalPlayback.StartAsync(f.Service, speech, planned.SessionId,
            planned.Revision, planned.Monologue!.RunId, 0, "Authorized narration.", "original-voice", null, () => true, ct);
        var before = await File.ReadAllBytesAsync(f.StatePath, ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Admission reads need the same actual settings lock. Release it only after the native
        // protocol ACK, so hold it synchronously from the endpoint immediately before return.
        FileStream? publicationHold = null;
        speech.Handle.BeforePauseReturn = () => publicationHold = new FileStream(f.StatePath + ".lock",
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pending = original.PauseAsync(cancellation.Token);
        Exception? observationFailure = null;
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (original.PendingCheckpoint is null)
            { Assert.True(DateTime.UtcNow < deadline); await Task.Yield(); }
            Assert.False(pending.IsCompleted); Assert.Equal(1, speech.Handle.Pauses);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath, ct));
            cancellation.Cancel();
            try { var denied = await pending; Assert.False(denied.IsSuccess); }
            catch (OperationCanceledException) { }
            Assert.Equal(speech.Handle.PlaybackId, original.PendingCheckpoint!.PlaybackId);
        }
        catch (Exception error) { observationFailure = error; throw; }
        finally
        {
            cancellation.Cancel(); publicationHold?.Dispose();
            try { await pending; } catch (OperationCanceledException) { }
            catch when (observationFailure is not null) { } // Preserve the primary assertion/read failure after observing cleanup.
        }
        speech.Handle.BeforePauseReturn = null;
        Assert.True((await original.PersistPendingAsync(ct)).IsSuccess);
        Assert.Null(original.PendingCheckpoint); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(1, speech.Starts);
        Assert.True((await original.ResumeAsync(ct)).IsSuccess); Assert.Equal(1, speech.Handle.Resumes);
    }

    [Fact]
    public async Task Saved_offset_cannot_start_replacement_synthesis_and_retired_original_cannot_control_or_erase_pending_ack()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var planned = await f.Plan(ct); var speech = new ControlledSpeech(); var current = true;
        var original = await MonologueOriginalPlayback.StartAsync(f.Service, speech, planned.SessionId,
            planned.Revision, planned.Monologue!.RunId, 0, "Authorized narration.", "original-voice", null, () => current, ct);
        speech.Handle.BeforePauseReturn = () => current = false;
        Assert.False((await original.PauseAsync(ct)).IsSuccess);
        Assert.NotNull(original.PendingCheckpoint); var before = await File.ReadAllBytesAsync(f.StatePath, ct);
        Assert.False((await original.ResumeAsync(ct)).IsSuccess); Assert.False((await original.PersistPendingAsync(ct)).IsSuccess);
        Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(0, speech.Handle.Resumes);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath, ct));
        current = true; Assert.False((await original.PersistPendingAsync(ct)).IsSuccess); // Retirement cannot resurrect.
        var progressed = await f.Service.RecordMonologueProgressAsync(planned.SessionId, planned.Revision,
            planned.Monologue.RunId, 0, TimeSpan.FromSeconds(3), true, ct); Assert.True(progressed.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => MonologueOriginalPlayback.StartAsync(f.Service, speech,
            planned.SessionId, progressed.Value!.Revision, planned.Monologue.RunId, 0, "No replay.", "original-voice", null, () => true, ct));
        Assert.Equal(1, speech.Starts);
    }

    [Fact]
    public async Task Actual_committed_checkpoint_with_lost_return_recovers_only_same_operation_receipt_without_another_native_call_or_write()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var planned = await f.Plan(ct);
        var observed = new ObservedActualCas(new VersionedAtomicSettingsStore(f.AppPaths));
        var service = new VisionVoiceSessionService(new MultimodalSessionStore(observed)); var speech = new ControlledSpeech();
        var original = await MonologueOriginalPlayback.StartAsync(service, speech, planned.SessionId, planned.Revision,
            planned.Monologue!.RunId, 0, "Authorized original narration.", "original-voice", null, () => true, ct);
        observed.AfterCommit = () => throw new IOException("Actual physical CAS returned; original caller loses its return.");
        await Assert.ThrowsAsync<IOException>(() => original.PauseAsync(ct)); Assert.NotNull(original.PendingCheckpoint);
        var committedBytes = await File.ReadAllBytesAsync(f.StatePath, ct); Assert.Equal(1, observed.Exchanges);
        var persisted = await f.Reopen().GetSessionAsync(planned.SessionId, ct); Assert.NotNull(persisted.Value!.Monologue!.PlaybackReceipt);
        observed.AfterCommit = null;
        Assert.True((await original.PersistPendingAsync(ct)).IsSuccess);
        Assert.Equal(persisted.Value.Monologue.PlaybackReceipt, original.CanonicalReceipt);
        Assert.Equal(1, observed.Exchanges); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(1, speech.Starts);
        Assert.Equal(committedBytes, await File.ReadAllBytesAsync(f.StatePath, ct));
    }

    [Fact]
    public async Task Competing_matching_progress_without_original_operation_receipt_cannot_be_adopted_as_own_checkpoint_commit()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var planned = await f.Plan(ct);
        var speech = new ControlledSpeech();
        var original = await MonologueOriginalPlayback.StartAsync(f.Service, speech, planned.SessionId, planned.Revision,
            planned.Monologue!.RunId, 0, "Authorized narration.", "original-voice", null, () => true, ct);
        speech.Handle.BeforePauseReturnAsync = async () => Assert.True((await f.Reopen().RecordMonologueProgressAsync(
            planned.SessionId, planned.Revision, planned.Monologue.RunId, 0, TimeSpan.FromSeconds(12), true, ct)).IsSuccess);
        Assert.False((await original.PauseAsync(ct)).IsSuccess); Assert.NotNull(original.PendingCheckpoint);
        Assert.Null(original.CanonicalReceipt); var competing = await File.ReadAllBytesAsync(f.StatePath, ct);
        Assert.False((await original.PersistPendingAsync(ct)).IsSuccess);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.StatePath, ct)); Assert.Equal(1, speech.Handle.Pauses);
    }

    [Fact]
    public async Task Context_retirement_after_actual_success_preserves_native_and_canonical_ack_but_denies_future_controls()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var planned = await f.Plan(ct); var current = true;
        var observed = new ObservedActualCas(new VersionedAtomicSettingsStore(f.AppPaths));
        var service = new VisionVoiceSessionService(new MultimodalSessionStore(observed)); var speech = new ControlledSpeech();
        var original = await MonologueOriginalPlayback.StartAsync(service, speech, planned.SessionId, planned.Revision,
            planned.Monologue!.RunId, 0, "Authorized narration.", "original-voice", null, () => current, ct);
        observed.AfterCommit = () => current = false;
        Assert.True((await original.PauseAsync(ct)).IsSuccess); Assert.NotNull(original.LastNativeCheckpoint);
        Assert.NotNull(original.CanonicalReceipt); Assert.Null(original.PendingCheckpoint);
        var bytes = await File.ReadAllBytesAsync(f.StatePath, ct);
        current = true; Assert.False((await original.ResumeAsync(ct)).IsSuccess);
        Assert.Equal(0, speech.Handle.Resumes); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath, ct));
    }

    [Fact]
    public async Task Returned_original_media_identity_is_retained_after_start_retirement_without_live_control_or_resynthesis()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var planned = await f.Plan(ct);
        var speech = new ControlledSpeech(); var current = true;
        speech.BeforeStartReturn = () => { speech.Live = false; current = false; };
        var before = await File.ReadAllBytesAsync(f.StatePath, ct);
        var original = await MonologueOriginalPlayback.StartAsync(f.Service, speech, planned.SessionId, planned.Revision,
            planned.Monologue!.RunId, 0, "Authorized narration.", "original-voice", null, () => current, ct);
        Assert.Equal(speech.Handle.PlaybackId, original.PlaybackId); Assert.Same(speech.Handle.Completion, original.Completion);
        Assert.False((await original.PauseAsync(ct)).IsSuccess); Assert.Null(original.PendingCheckpoint);
        speech.Live = true; current = true; Assert.False((await original.PauseAsync(ct)).IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => MonologueOriginalPlayback.StartAsync(f.Service, speech,
            planned.SessionId, planned.Revision, planned.Monologue.RunId, 0, "No replay.", "original-voice", null, () => true, ct));
        Assert.Equal(1, speech.Starts); Assert.Equal(0, speech.Handle.Pauses);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath, ct));
    }

    private sealed class ControlledSpeech : IContinuableSpeechOutputService, IOriginalSpeechPlaybackReceiptIssuer
    {
        public int Starts; public bool Live = true; public Action? BeforeStartReturn; public ControlledHandle Handle { get; } = new();
        public bool CanContinueVoice(string voiceId) => voiceId == "original-voice";
        public bool WasIssuedPlayback(ISpeechPlaybackContinuation playback) => ReferenceEquals(playback, Handle);
        public bool IsOriginalPlayback(ISpeechPlaybackContinuation playback) => Live && WasIssuedPlayback(playback);
        public Task<ISpeechPlaybackContinuation> StartContinuableAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Starts++; BeforeStartReturn?.Invoke(); return Task.FromResult<ISpeechPlaybackContinuation>(Handle); }
    }
    private sealed class ControlledHandle : ISpeechPlaybackContinuation
    {
        public Guid PlaybackId { get; } = Guid.NewGuid(); public string VoiceId => "original-voice";
        public Task Completion { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        public int Pauses, Resumes; public Action? BeforePauseReturn; public Func<Task>? BeforePauseReturnAsync;
        public async Task<SpeechPlaybackCheckpoint> PauseAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Pauses++; BeforePauseReturn?.Invoke(); if (BeforePauseReturnAsync is not null) await BeforePauseReturnAsync(); return new(PlaybackId, VoiceId, TimeSpan.FromSeconds(12), true); }
        public Task<SpeechPlaybackCheckpoint> ResumeAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Resumes++; return Task.FromResult(new SpeechPlaybackCheckpoint(PlaybackId, VoiceId, TimeSpan.FromSeconds(12), false)); }
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
            var activated = await Service.RecordMonologueProgressAsync(planned.Value!.SessionId, planned.Value.Revision,
                planned.Value.Monologue!.RunId, 0, TimeSpan.Zero, false, ct);
            Assert.True(activated.IsSuccess); return activated.Value!;
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
    private sealed class ObservedActualCas(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public int Exchanges; public Action? AfterCommit;
        public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class => actual.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class => actual.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct) => actual.RemoveAsync(key, ct);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken ct) => actual.ExportAsync(ct);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken ct) => actual.ImportAsync(value, ct);
        public async Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken ct)
        { var result = await actual.CompareExchangeAsync(key, expected, replacement, ct); Exchanges++; if (result.Exchanged) AfterCommit?.Invoke(); return result; }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    }
}
