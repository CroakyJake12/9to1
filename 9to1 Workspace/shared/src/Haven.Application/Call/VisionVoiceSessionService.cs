using Haven.Application.Call;

namespace Haven.Application;

/// <summary>Coordinates canonical Vision &amp; Voice session metadata and its revisioned store.</summary>
public sealed class VisionVoiceSessionService(MultimodalSessionStore store, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<VisionVoiceResult<MultimodalSession>> StartAsync(
        Guid conversationId,
        Guid? spaceId,
        VisionVoiceMode mode,
        string effectiveModelPolicy,
        bool isLocalOnly,
        LiveTranslateLanguageSet? languages = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
            return Failure(VisionVoiceErrorCode.InvalidRequest, "A canonical conversation is required.", "ConversationID", "StartSession");
        var now = _time.GetUtcNow();
        var session = new MultimodalSession(
            Guid.NewGuid(), conversationId, spaceId, mode, null, effectiveModelPolicy,
            null, null, MultimodalSessionState.Starting, MicrophoneCaptureState.Off,
            [], null, null, now, 1, VisionVoiceRetention.Ephemeral,
            LiveTranslateLanguages: languages, IsLocalOnly: isLocalOnly);
        if (session.Validate() is { } validation)
            return Failure(VisionVoiceErrorCode.InvalidRequest, validation, "Session", "StartSession");
        try
        {
            return VisionVoiceResult<MultimodalSession>.Success(await store.CreateAsync(session, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            return Failure(exception is InvalidOperationException ? VisionVoiceErrorCode.Conflict : VisionVoiceErrorCode.InvalidRequest,
                exception.Message, "Session", "StartSession");
        }
    }

    public async Task<VisionVoiceResult<MultimodalSession>> TransitionAsync(
        Guid sessionId,
        long expectedRevision,
        MultimodalSessionState next,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(sessionId, "TransitionSession", cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        if (current.Value!.Revision != expectedRevision)
            return Failure(VisionVoiceErrorCode.Conflict, "The session changed; refresh before retrying.", "Revision", "TransitionSession");
        if (!MultimodalSessionLifecycle.TryTransition(current.Value, next, out var updated, out var error))
            return Failure(VisionVoiceErrorCode.InvalidRequest, error!, "State", "TransitionSession");
        return await SaveAsync(current.Value, updated, "TransitionSession", cancellationToken).ConfigureAwait(false);
    }

    public async Task<VisionVoiceResult<MultimodalSession>> SetModeAsync(
        Guid sessionId,
        long expectedRevision,
        VisionVoiceMode mode,
        LiveTranslateLanguageSet? languages = null,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(sessionId, "SetMode", cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        if (current.Value!.Revision != expectedRevision)
            return Failure(VisionVoiceErrorCode.Conflict, "The session changed; refresh before retrying.", "Revision", "SetMode");
        var basis = current.Value with { LiveTranslateLanguages = mode == VisionVoiceMode.LiveTranslate ? languages ?? current.Value.LiveTranslateLanguages : null };
        if (!MultimodalSessionLifecycle.TrySetMode(basis, mode, out var changed, out var error))
            return Failure(VisionVoiceErrorCode.InvalidRequest, error!, "VoiceMode", "SetMode");
        // TrySetMode increments revision; changing the temporary language configuration is part of that same update.
        return await SaveAsync(current.Value, changed, "SetMode", cancellationToken).ConfigureAwait(false);
    }

    public async Task<VisionVoiceResult<MultimodalSession>> SetRetentionAsync(
        Guid sessionId,
        long expectedRevision,
        bool retainAudio,
        bool retainTranscript,
        bool userExplicitlyOptedIn,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(sessionId, "SetRetention", cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        if (current.Value!.Revision != expectedRevision)
            return Failure(VisionVoiceErrorCode.Conflict, "The session changed; refresh before retrying.", "Revision", "SetRetention");
        if ((retainAudio || retainTranscript) && !userExplicitlyOptedIn)
            return Failure(VisionVoiceErrorCode.PermissionRequired, "Retention requires separate explicit user opt-in.", "Retention", "SetRetention");
        var updated = current.Value with
        {
            Retention = new(retainAudio, retainTranscript, retainAudio || retainTranscript ? _time.GetUtcNow() : null),
            Revision = checked(expectedRevision + 1)
        };
        if (updated.Validate() is { } error)
            return Failure(VisionVoiceErrorCode.InvalidRequest, error, "Retention", "SetRetention");
        return await SaveAsync(current.Value, updated, "SetRetention", cancellationToken).ConfigureAwait(false);
    }

    private async Task<VisionVoiceResult<MultimodalSession>> ReadAsync(Guid id, string action, CancellationToken token)
    {
        if (id == Guid.Empty) return Failure(VisionVoiceErrorCode.InvalidRequest, "SessionID cannot be empty.", "SessionID", action);
        try
        {
            var session = await store.GetAsync(id, token).ConfigureAwait(false);
            return session is null
                ? Failure(VisionVoiceErrorCode.SessionNotFound, "Session was not found.", "SessionID", action)
                : VisionVoiceResult<MultimodalSession>.Success(session);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return Failure(VisionVoiceErrorCode.InvalidRequest, exception.Message, "Session", action);
        }
    }

    private async Task<VisionVoiceResult<MultimodalSession>> SaveAsync(
        MultimodalSession current,
        MultimodalSession updated,
        string action,
        CancellationToken token)
    {
        try
        {
            return VisionVoiceResult<MultimodalSession>.Success(await store.UpdateAsync(
                current.SessionId, current.Revision, updated, token).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException or InvalidDataException)
        {
            return Failure(VisionVoiceErrorCode.Conflict, exception.Message, "Revision", action);
        }
    }

    private static VisionVoiceResult<MultimodalSession> Failure(
        VisionVoiceErrorCode code, string message, string target, string action) =>
        VisionVoiceResult<MultimodalSession>.Failure(new(code, message, target, action, IsRecoverable: code is VisionVoiceErrorCode.Conflict or VisionVoiceErrorCode.SessionDisconnected));
}
