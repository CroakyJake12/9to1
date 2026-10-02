namespace Haven.Application.Call;

/// <summary>Canonical metadata receipt for one privately retained native checkpoint operation.
/// It is not provider issuance, a playback handle, or source/output authority.</summary>
public sealed record MonologuePlaybackReceipt(Guid OperationId, Guid PlaybackId, string VoiceId,
    long OriginalRevision, int Section, TimeSpan Position, bool IsPaused);
