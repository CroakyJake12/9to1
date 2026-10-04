namespace Haven.Application;

/// <summary>Actual provider playback observation; not a canonical session update or permission.</summary>
public sealed record SpeechPlaybackCheckpoint(Guid PlaybackId, string VoiceId, TimeSpan Position, bool IsPaused);

/// <summary>An opaque provider-issued handle for one original utterance. It cannot select another current utterance.
/// A returned handle records retained original media; actual Playing must be acknowledged separately.
/// Pause/resume preserve its original media rather than synthesizing or replaying text.</summary>
public interface ISpeechPlaybackContinuation
{
    Guid PlaybackId { get; }
    string VoiceId { get; }
    Task Completion { get; }
    Task<SpeechPlaybackCheckpoint> PauseAsync(CancellationToken cancellationToken);
    Task<SpeechPlaybackCheckpoint> ResumeAsync(CancellationToken cancellationToken);
}

/// <summary>Optional actual playback capability. Ordinary speech adapters need not promise continuation.</summary>
public interface IContinuableSpeechOutputService
{
    bool CanContinueVoice(string voiceId);
    Task<ISpeechPlaybackContinuation> StartContinuableAsync(string text, string? voiceName,
        string? outputDeviceId, CancellationToken cancellationToken);
}

/// <summary>Optional deny-only genuine provider issuance observation. It grants no session,
/// source, output-device or model authorization and does not retain a playback lifetime lease.
/// A caller must never authenticate a supplied interface by matching public playback metadata.</summary>
public interface IOriginalSpeechPlaybackIssuer
{
    bool IsOriginalPlayback(ISpeechPlaybackContinuation playback);
}

/// <summary>Private historical object issuance only. A true result survives original retirement and
/// permits retaining an already returned acknowledgement; it grants no current/native control or audio-start claim.</summary>
public interface IOriginalSpeechPlaybackReceiptIssuer : IOriginalSpeechPlaybackIssuer
{
    bool WasIssuedPlayback(ISpeechPlaybackContinuation playback);
}

/// <summary>Optional cleanup for this exact provider-issued utterance. A successful request
/// retires its controls; it does not acknowledge silence, output-device state or another utterance.
/// A retired original cannot stop a replacement playback.</summary>
public interface IOriginalSpeechPlaybackStop : ISpeechPlaybackContinuation
{
    Task<bool> StopOriginalAsync(CancellationToken cancellationToken);
}
