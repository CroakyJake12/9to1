using Haven.Application.Call;
namespace Haven.Application;

public sealed partial class VisionVoiceSessionService
{
    /// <summary>CAS-persist one exact retained native observation and its operation receipt.
    /// Caller separately verifies genuine native issuance; this method grants no playback/source access.</summary>
    public async Task<VisionVoiceResult<MultimodalSession>> RecordMonologuePlaybackCheckpointAsync(Guid sessionId,
        long expectedRevision, Guid originalRunId, MonologuePlaybackReceipt checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        const string action = "RecordMonologuePlaybackCheckpoint";
        if (checkpoint.OperationId == Guid.Empty || checkpoint.PlaybackId == Guid.Empty || string.IsNullOrWhiteSpace(checkpoint.VoiceId)
            || checkpoint.OriginalRevision != expectedRevision || expectedRevision < 0 || expectedRevision == long.MaxValue)
            return Failure(VisionVoiceErrorCode.InvalidRequest, "An exact original native checkpoint operation is required.", "PlaybackCheckpoint", action);
        var read = await ReadAsync(sessionId, action, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return read;
        var original = read.Value!;
        if (original.Revision != expectedRevision)
            return Failure(VisionVoiceErrorCode.Conflict, "The original session checkpoint changed.", "Revision", action);
        if (original.VoiceMode != VisionVoiceMode.Monologue || original.State is MultimodalSessionState.Ended or MultimodalSessionState.Failed
            || original.Monologue is not { } run || originalRunId == Guid.Empty || run.RunId != originalRunId)
            return Failure(VisionVoiceErrorCode.InvalidRequest, "The original Monologue run is unavailable.", "MonologueRun", action);
        if (checkpoint.Section < run.CurrentSection || checkpoint.Section == run.CurrentSection && checkpoint.Position < run.Position)
            return Failure(VisionVoiceErrorCode.InvalidRequest, "The native checkpoint cannot rewind canonical progress.", "Position", action);
        var next = run with { CurrentSection = checkpoint.Section, Position = checkpoint.Position,
            IsPaused = checkpoint.IsPaused, PlaybackReceipt = checkpoint };
        if (next.Validate() is { } error) return Failure(VisionVoiceErrorCode.InvalidRequest, error, "PlaybackCheckpoint", action);
        return await SaveAsync(original, original with { Monologue = next, Revision = expectedRevision + 1 }, action, cancellationToken).ConfigureAwait(false);
    }
}
