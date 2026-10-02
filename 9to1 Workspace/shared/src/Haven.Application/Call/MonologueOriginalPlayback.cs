using System.Runtime.CompilerServices;
namespace Haven.Application.Call;

/// <summary>One original provider utterance associated with one original canonical section.
/// Supplied narration must already be authorized by its caller; this coordinator reads no SourceRefs
/// and grants no model, source, microphone or output-device access. Native continuation is available
/// only while this object and its genuine provider-issued playback remain alive.</summary>
public sealed class MonologueOriginalPlayback
{
    private sealed class OriginalStarts { public readonly HashSet<(Guid Session, Guid Run, int Section)> Runs = []; }
    private static readonly ConditionalWeakTable<VisionVoiceSessionService, OriginalStarts> Starts = new();
    private readonly VisionVoiceSessionService _sessions;
    private readonly IOriginalSpeechPlaybackReceiptIssuer _issuer;
    private readonly ISpeechPlaybackContinuation _playback;
    private readonly Func<bool> _originalCurrent;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Guid _sessionId, _runId;
    private readonly int _section;
    private long _revision;
    private SpeechPlaybackCheckpoint? _pending;
    private SpeechPlaybackCheckpoint? _lastNative;
    private MonologuePlaybackReceipt? _pendingReceipt, _canonicalReceipt;
    private bool _nativeOutcomeUnknown;
    private volatile bool _playbackRetired;
    private bool _paused;
    private volatile bool _contextRetired;

    private MonologueOriginalPlayback(VisionVoiceSessionService sessions, IOriginalSpeechPlaybackReceiptIssuer issuer,
        ISpeechPlaybackContinuation playback, Func<bool> originalCurrent, Guid sessionId, long revision, Guid runId, int section)
    { _sessions = sessions; _issuer = issuer; _playback = playback; _originalCurrent = originalCurrent;
        _sessionId = sessionId; _revision = revision; _runId = runId; _section = section; }

    public Guid PlaybackId => _playback.PlaybackId;
    public string VoiceId => _playback.VoiceId;
    public Task Completion => _playback.Completion;
    /// <summary>Exact returned native acknowledgement, retained before canonical metadata IO.
    /// It remains observable on metadata failure and never authorizes a replacement playback.</summary>
    public SpeechPlaybackCheckpoint? PendingCheckpoint => Volatile.Read(ref _pending);
    public SpeechPlaybackCheckpoint? LastNativeCheckpoint => Volatile.Read(ref _lastNative);
    public MonologuePlaybackReceipt? CanonicalReceipt => Volatile.Read(ref _canonicalReceipt);

    /// <summary>Starts one supplied, separately authorized section utterance once. A saved nonzero
    /// offset cannot be reconstructed by synthesis; continuing it requires the retained original object.</summary>
    public static async Task<MonologueOriginalPlayback> StartAsync(VisionVoiceSessionService sessions,
        IContinuableSpeechOutputService output, Guid sessionId, long revision, Guid runId, int section,
        string narration, string voiceId, string? outputDeviceId, Func<bool> originalCurrent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions); ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(originalCurrent);
        if (output is not IOriginalSpeechPlaybackReceiptIssuer issuer || string.IsNullOrWhiteSpace(narration)
            || string.IsNullOrWhiteSpace(voiceId) || !output.CanContinueVoice(voiceId))
            throw new NotSupportedException("The selected original voice cannot retain native playback.");
        // Same owning service cannot synthesize a second live utterance for its original run.
        // This is a private in-process association, not a cross-process playback recovery grant.
        var starts = Starts.GetValue(sessions, _ => new OriginalStarts()); var key = (sessionId, runId, section);
        lock (starts.Runs)
        {
            if (starts.Runs.Count >= 256 || !starts.Runs.Add(key))
                throw new InvalidOperationException("The original section already has a native start, or this owning service's bounded start history is full.");
        }
        var nativeAttempted = false;
        try
        {
            bool Current() { try { return originalCurrent(); } catch { return false; } }
            if (!Current()) throw new UnauthorizedAccessException("The original narration context retired.");
            var read = await sessions.GetMonologueContinuationAsync(sessionId, revision, runId, cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value!.CurrentSection != section || read.Value.Position != TimeSpan.Zero || !Current())
                throw new InvalidOperationException("Only the original section at its beginning can start a new utterance.");
            var canonical = await sessions.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (!canonical.IsSuccess || canonical.Value!.Revision != revision || canonical.Value.Monologue is not { IsPaused: false } canonicalRun
                || canonicalRun.RunId != runId || canonicalRun.CurrentSection != section || canonicalRun.Position != TimeSpan.Zero
                || canonical.Value.ConversationId != read.Value.ConversationId || canonical.Value.SpaceId != read.Value.SpaceId
                || !Current()) throw new InvalidOperationException("A paused or changed original run cannot be restarted by synthesis.");
            nativeAttempted = true;
            var playback = await output.StartContinuableAsync(narration, voiceId, outputDeviceId, cancellationToken).ConfigureAwait(false);
            // Retain the actual returned object before any further await. No post-return metadata read
            // can erase this acknowledged native start; subsequent controls verify the original CAS.
            if (!issuer.WasIssuedPlayback(playback) || playback.VoiceId != voiceId || playback.PlaybackId == Guid.Empty)
                throw new InvalidOperationException("The provider did not return its original selected playback.");
            // Completion cannot make the same section eligible for synthesis again. Its original
            // handle may be continued only while alive; another section requires a new canonical CAS.
            return new(sessions, issuer, playback, originalCurrent, sessionId, revision, runId, section)
                { _contextRetired = !Current(), _playbackRetired = !issuer.IsOriginalPlayback(playback) };
        }
        finally { if (!nativeAttempted) { lock (starts.Runs) starts.Runs.Remove(key); } }
    }

    /// <summary>Exact provider-issued original cleanup capability, never an ambient Stop route.</summary>
    public bool CanStopOriginal => _playback is IOriginalSpeechPlaybackStop && _issuer.WasIssuedPlayback(_playback);

    /// <summary>Retires this original's future controls and requests cleanup on this exact
    /// issued native object, even when its host has retired. Retains every pending native
    /// checkpoint and known canonical receipt. A returned true is a cleanup request, not silence.</summary>
    public async Task<bool> StopOriginalAsync(CancellationToken cancellationToken = default)
    {
        if (!_issuer.WasIssuedPlayback(_playback) || _playback is not IOriginalSpeechPlaybackStop originalStop)
            throw new NotSupportedException("The original provider has no exact playback cleanup port.");
        _playbackRetired = true;
        return await originalStop.StopOriginalAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<VisionVoiceResult<MultimodalSession>> PauseAsync(CancellationToken cancellationToken = default) =>
        ControlAsync(true, cancellationToken);
    public Task<VisionVoiceResult<MultimodalSession>> ResumeAsync(CancellationToken cancellationToken = default) =>
        ControlAsync(false, cancellationToken);

    private bool NativeCurrent()
    {
        if (_nativeOutcomeUnknown || _playbackRetired || !ContextCurrent()) return false;
        try
        {
            if (!_issuer.IsOriginalPlayback(_playback) || _playback.Completion.IsCompleted)
            { _playbackRetired = true; return false; }
            return true;
        }
        catch { _nativeOutcomeUnknown = true; return false; }
    }

    private bool ContextCurrent()
    {
        if (_contextRetired) return false;
        bool current; try { current = _originalCurrent(); } catch { current = false; }
        if (!current) _contextRetired = true;
        return current;
    }

    private async Task<VisionVoiceResult<MultimodalSession>> ControlAsync(bool pause, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pending is not null) return Failure("Persist or reconcile the retained checkpoint before another native control.");
            if (pause == _paused) return Failure("The original playback already has this confirmed pause state.");
            if (!NativeCurrent()) return Failure("The original native playback retired or its outcome is unknown.");
            var original = await _sessions.GetMonologueContinuationAsync(_sessionId, _revision, _runId, ct).ConfigureAwait(false);
            if (!original.IsSuccess || original.Value!.CurrentSection != _section || !NativeCurrent())
                return Failure("The original canonical section changed.");
            SpeechPlaybackCheckpoint checkpoint;
            try { checkpoint = await (pause ? _playback.PauseAsync(ct) : _playback.ResumeAsync(ct)).ConfigureAwait(false); }
            catch { _nativeOutcomeUnknown = true; throw; } // No uncertain native command may be replayed.
            if (checkpoint.PlaybackId != _playback.PlaybackId || checkpoint.VoiceId != _playback.VoiceId
                || checkpoint.IsPaused != pause || checkpoint.Position < original.Value.Position)
            { _nativeOutcomeUnknown = true; return Failure("Original native checkpoint identity or position is invalid."); }
            Volatile.Write(ref _pending, checkpoint); // Actual ACK retained BEFORE canonical CAS/await/cancellation.
            Volatile.Write(ref _lastNative, checkpoint);
            _pendingReceipt = new(Guid.NewGuid(), checkpoint.PlaybackId, checkpoint.VoiceId, _revision,
                _section, checkpoint.Position, checkpoint.IsPaused);
            _paused = checkpoint.IsPaused;
            return await PersistPendingCoreAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Retries metadata only, with the same private returned native checkpoint. Never calls
    /// Pause, Resume or synthesis. A committed write whose return was lost is reconciled by exact fields.</summary>
    public async Task<VisionVoiceResult<MultimodalSession>> PersistPendingAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await PersistPendingCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<VisionVoiceResult<MultimodalSession>> PersistPendingCoreAsync(CancellationToken ct)
    {
        if (_pending is not { } acknowledged || _pendingReceipt is not { } originalReceipt)
            return Failure("There is no retained native checkpoint to persist.");
        if (!ContextCurrent()) return Failure("The original narration context retired; its native acknowledgement remains retained.");
        // Native retirement does not erase an earlier actual ACK, but it cannot authorize a fresh control.
        // Canonical persistence still uses the exact original run, section and expected revision.
        var current = await _sessions.GetSessionAsync(_sessionId, ct).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        if (!ContextCurrent()) return Failure("The original narration context retired during metadata read.");
        var actual = current.Value!;
        if (_revision < long.MaxValue && actual.Revision == _revision + 1 && actual.VoiceMode == VisionVoiceMode.Monologue
            && actual.State is not (MultimodalSessionState.Ended or MultimodalSessionState.Failed)
            && actual.Monologue is { } run && run.RunId == _runId && run.CurrentSection == _section
            && run.Position == acknowledged.Position && run.IsPaused == acknowledged.IsPaused && run.PlaybackReceipt == originalReceipt)
        { AcceptReceipt(actual.Revision, originalReceipt); ContextCurrent(); return current; }
        var result = await _sessions.RecordMonologuePlaybackCheckpointAsync(_sessionId, _revision, _runId, originalReceipt, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            if (result.Value!.Revision != _revision + 1 || result.Value.Monologue?.PlaybackReceipt != originalReceipt)
                return Failure("The canonical checkpoint returned an unrelated receipt.");
            AcceptReceipt(result.Value.Revision, originalReceipt);
            ContextCurrent(); // Latch late context retirement without erasing an actual acknowledged CAS.
        }
        return result;
    }

    private void AcceptReceipt(long revision, MonologuePlaybackReceipt receipt)
    {
        _revision = revision; Volatile.Write(ref _canonicalReceipt, receipt);
        _pendingReceipt = null; Volatile.Write(ref _pending, null);
    }

    private static VisionVoiceResult<MultimodalSession> Failure(string message) =>
        VisionVoiceResult<MultimodalSession>.Failure(new(VisionVoiceErrorCode.Conflict, message,
            "MonologuePlayback", "ContinueOriginalPlayback", false));
}
