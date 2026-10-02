using Haven.Application.Call;

namespace Haven.Application;

/// <summary>A detached reply issued only by its actual completed local Call turn.
/// This is an original-object locator, not Home/source/model or output permission.</summary>
public sealed class CallOriginalNarrationSelection
{
    internal CallOriginalNarrationSelection(CallCoordinator owner, Guid callId, Guid conversationId, Guid replyId,
        CancellationTokenSource lifetime, CallStartOptions options, string text)
    { Owner = owner; CallId = callId; ConversationId = conversationId; ReplyId = replyId; Lifetime = lifetime; Options = options; Text = text; }
    internal CallCoordinator Owner { get; }
    internal CancellationTokenSource Lifetime { get; }
    internal CallStartOptions Options { get; }
    internal int Attempted;
    public Guid CallId { get; }
    public Guid ConversationId { get; }
    public Guid ReplyId { get; }
    public string Text { get; }
}

public sealed partial class CallCoordinator
{
    private CallOriginalNarrationSelection? _originalNarration;
    private MonologueOriginalPlayback? _originalNarrationPlayback;

    private void PublishOriginalNarration(Guid callId, Guid conversationId, Guid replyId, CancellationTokenSource lifetime,
        CallStartOptions options, string text)
    {
        // Only bounded, completed output already produced by this Call's model is eligible.
        // There is no source-reference dereference, model retry or transcript reconstruction.
        if (text.Length is < 1 or > 65536 || !options.EnableSpeechOutput || string.IsNullOrWhiteSpace(options.VoiceName)) return;
        Volatile.Write(ref _originalNarration, new(this, callId, conversationId, replyId, lifetime, options, text));
    }

    public CallOriginalNarrationSelection? CaptureOriginalNarration()
    {
        var original = Volatile.Read(ref _originalNarration);
        return original is not null && IsOriginalNarrationCurrent(original) ? original : null;
    }

    public bool IsOriginalNarrationCurrent(CallOriginalNarrationSelection? original) =>
        original is not null && ReferenceEquals(original.Owner, this) && ReferenceEquals(Volatile.Read(ref _originalNarration), original) &&
        !_disposed && !_ending && IsActive && CurrentSession?.Id == original.CallId &&
        CurrentConversation?.Id == original.ConversationId && ReferenceEquals(_lifetimeCts, original.Lifetime) &&
        !original.Lifetime.IsCancellationRequested && ReferenceEquals(_options, original.Options);

    /// <summary>Explicit narration of the same completed reply within its original local Call's
    /// already enabled speech options. Caller separately admits this narration; this operation
    /// does not authorize referenced sources, other models, Home effects or a replacement Call.</summary>
    public async Task<MonologueOriginalPlayback> StartOriginalNarrationAsync(CallOriginalNarrationSelection original,
        VisionVoiceSessionService sessions, Func<bool> originalHostCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(originalHostCurrent);
        await _turnGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool Current() => IsOriginalNarrationCurrent(original) && originalHostCurrent();
            void RequireCurrent() { if (!Current()) throw new InvalidOperationException("Original Call narration retired."); }
            RequireCurrent();
            using var admittedLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, original.Lifetime.Token);
            var token = admittedLifetime.Token;
            if (_speechOutput is not IContinuableSpeechOutputService speech ||
                !speech.CanContinueVoice(original.Options.VoiceName!))
                throw new NotSupportedException("Selected original voice cannot continue native playback.");
            if (Volatile.Read(ref original.Attempted) != 0)
                throw new InvalidOperationException("Original narration was already attempted; retain its playback outcome.");
            var started = await sessions.StartAsync(original.ConversationId, null, VisionVoiceMode.Monologue,
                "Original local Call narration", true, cancellationToken: token).ConfigureAwait(false);
            RequireCurrent();
            if (!started.IsSuccess) throw new InvalidOperationException(started.Error!.Message);
            var planned = await sessions.PlanMonologueAsync(started.Value!.SessionId, started.Value.Revision,
                "Narrate the original completed Call reply", null, ["Original completed reply"], [], token).ConfigureAwait(false);
            RequireCurrent();
            if (!planned.IsSuccess) throw new InvalidOperationException(planned.Error!.Message);
            // Explicit requested metadata activation of this freshly planned exact run.
            // This CAS is not a native Playing or audible output acknowledgement.
            var activated = await sessions.RecordMonologueProgressAsync(planned.Value!.SessionId,
                planned.Value.Revision, planned.Value.Monologue!.RunId, 0, TimeSpan.Zero, false, token).ConfigureAwait(false);
            RequireCurrent();
            if (!activated.IsSuccess) throw new InvalidOperationException(activated.Error!.Message);
            if (Interlocked.CompareExchange(ref original.Attempted, 1, 0) != 0)
                throw new InvalidOperationException("Original narration was already attempted.");
            // Never release this attempt after native invocation: an uncertain Start is not retry permission.
            var playback = await MonologueOriginalPlayback.StartAsync(sessions, speech, activated.Value!.SessionId,
                activated.Value.Revision, activated.Value.Monologue!.RunId, 0, original.Text,
                original.Options.VoiceName!, original.Options.OutputDeviceId, Current, token).ConfigureAwait(false);
            _originalNarrationPlayback = playback; // Retain the original native outcome before the late lifetime check.
            if (!Current()) { await playback.StopOriginalAsync(CancellationToken.None).ConfigureAwait(false); return playback; }
            return playback;
        }
        finally { _turnGate.Release(); }
    }

    private async Task RetireOriginalNarrationAsync()
    {
        Volatile.Write(ref _originalNarration, null);
        var original = Interlocked.Exchange(ref _originalNarrationPlayback, null);
        if (original is not null) await original.StopOriginalAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
