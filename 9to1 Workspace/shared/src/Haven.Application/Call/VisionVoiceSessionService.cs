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
        if (expectedRevision == long.MaxValue)
            return Failure(VisionVoiceErrorCode.Conflict, "The session revision is exhausted; metadata was preserved.", "Revision", "TransitionSession");
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
        if (languages is not null)
        {
            if (languages.Validate() is { } invalidLanguages)
                return Failure(VisionVoiceErrorCode.InvalidRequest, invalidLanguages, "Languages", "SetMode");
            languages = languages with { Locales = Array.AsReadOnly(languages.Locales.ToArray()),
                Outputs = Array.AsReadOnly(languages.Outputs.ToArray()), GlossaryIds = Array.AsReadOnly(languages.GlossaryIds.ToArray()) };
        }
        var current = await ReadAsync(sessionId, "SetMode", cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        if (current.Value!.Revision != expectedRevision)
            return Failure(VisionVoiceErrorCode.Conflict, "The session changed; refresh before retrying.", "Revision", "SetMode");
        if (expectedRevision == long.MaxValue)
            return Failure(VisionVoiceErrorCode.Conflict, "The session revision is exhausted; metadata was preserved.", "Revision", "SetMode");
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
        if (expectedRevision == long.MaxValue)
            return Failure(VisionVoiceErrorCode.Conflict, "The session revision is exhausted; metadata was preserved.", "Revision", "SetRetention");
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

    /// <summary>Reads the same canonical session; this operation creates no conversation or runtime.</summary>
    public Task<VisionVoiceResult<MultimodalSession>> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => ReadAsync(sessionId, "GetSession", cancellationToken);

    public async Task<VisionVoiceResult<MonologueRun>> GetMonologueRunAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await ReadAsync(sessionId, "GetMonologueRun", cancellationToken).ConfigureAwait(false);
        if (!session.IsSuccess) return VisionVoiceResult<MonologueRun>.Failure(session.Error!);
        if (session.Value!.Monologue is not { } run)
            return VisionVoiceResult<MonologueRun>.Failure(new(VisionVoiceErrorCode.InvalidRequest,
                "This canonical session has no Monologue plan.", "MonologueRun", "GetMonologueRun", false));
        return VisionVoiceResult<MonologueRun>.Success(run);
    }

    /// <summary>Reads one exact original continuation checkpoint without starting speech or changing pause state.
    /// Providers must separately admit their capabilities and confirm playback; this data never authorizes source reads.</summary>
    public async Task<VisionVoiceResult<MonologueContinuation>> GetMonologueContinuationAsync(
        Guid sessionId, long expectedRevision, Guid originalRunId, CancellationToken cancellationToken = default)
    {
        const string action = "GetMonologueContinuation";
        var read = await ReadAsync(sessionId, action, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return VisionVoiceResult<MonologueContinuation>.Failure(read.Error!);
        var original = read.Value!;
        if (original.Revision != expectedRevision)
            return VisionVoiceResult<MonologueContinuation>.Failure(new(VisionVoiceErrorCode.Conflict,
                "The original continuation checkpoint changed.", "Revision", action, false));
        if (original.VoiceMode != VisionVoiceMode.Monologue || original.State is MultimodalSessionState.Ended or MultimodalSessionState.Failed ||
            original.Monologue is not { } run || originalRunId == Guid.Empty || run.RunId != originalRunId)
            return VisionVoiceResult<MonologueContinuation>.Failure(new(VisionVoiceErrorCode.InvalidRequest,
                "The original active Monologue run is unavailable.", "MonologueRun", action, false));
        return VisionVoiceResult<MonologueContinuation>.Success(new(original.SessionId, original.ConversationId,
            original.SpaceId, original.Revision, run.RunId, run.Objective, run.TargetDuration,
            Array.AsReadOnly(run.Sections.ToArray()), run.CurrentSection, run.Position,
            Array.AsReadOnly(run.SourceRefs.ToArray())));
    }

    /// <summary>Persists one immutable outline for a Monologue session. It neither starts speech nor accesses SourceRefs.</summary>
    public async Task<VisionVoiceResult<MultimodalSession>> PlanMonologueAsync(Guid sessionId, long expectedRevision,
        string objective, TimeSpan? targetDuration, IReadOnlyList<string> sections, IReadOnlyList<string> sourceRefs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sections); ArgumentNullException.ThrowIfNull(sourceRefs);
        // Freeze caller selections before the first canonical read can suspend.
        var plan = new MonologueRun(Guid.NewGuid(), objective, targetDuration,
            Array.AsReadOnly(sections.ToArray()), 0, TimeSpan.Zero, true, Array.AsReadOnly(sourceRefs.ToArray()));
        if (plan.Validate() is { } invalid) return Failure(VisionVoiceErrorCode.InvalidRequest, invalid, "MonologueRun", "PlanMonologue");
        var current = await ReadAsync(sessionId, "PlanMonologue", cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        var original = current.Value!;
        if (original.Revision != expectedRevision || expectedRevision == long.MaxValue)
            return Failure(VisionVoiceErrorCode.Conflict, "The original session revision changed or is exhausted.", "Revision", "PlanMonologue");
        if (original.VoiceMode != VisionVoiceMode.Monologue || original.State is MultimodalSessionState.Ended or MultimodalSessionState.Failed || original.Monologue is not null)
            return Failure(VisionVoiceErrorCode.InvalidRequest, "An active Monologue session without an existing plan is required.", "MonologueRun", "PlanMonologue");
        return await SaveAsync(original, original with { Monologue = plan, Revision = expectedRevision + 1 }, "PlanMonologue", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records confirmed playback progress or pause/resume metadata for the exact original run. No audio or tools execute here.</summary>
    public async Task<VisionVoiceResult<MultimodalSession>> RecordMonologueProgressAsync(Guid sessionId, long expectedRevision,
        Guid originalRunId, int section, TimeSpan position, bool paused, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(sessionId, "RecordMonologueProgress", cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        var original = current.Value!;
        if (original.Revision != expectedRevision || expectedRevision == long.MaxValue)
            return Failure(VisionVoiceErrorCode.Conflict, "The original session revision changed or is exhausted.", "Revision", "RecordMonologueProgress");
        if (original.VoiceMode != VisionVoiceMode.Monologue || original.State is MultimodalSessionState.Ended or MultimodalSessionState.Failed ||
            original.Monologue is not { } run || originalRunId == Guid.Empty || run.RunId != originalRunId)
            return Failure(VisionVoiceErrorCode.InvalidRequest, "The original active Monologue run is unavailable.", "MonologueRun", "RecordMonologueProgress");
        if (section < run.CurrentSection || section == run.CurrentSection && position < run.Position)
            return Failure(VisionVoiceErrorCode.InvalidRequest, "Resume cannot rewind confirmed progress in this run.", "Position", "RecordMonologueProgress");
        var progress = run with { CurrentSection = section, Position = position, IsPaused = paused };
        if (progress.Validate() is { } invalid) return Failure(VisionVoiceErrorCode.InvalidRequest, invalid, "Position", "RecordMonologueProgress");
        return await SaveAsync(original, original with { Monologue = progress, Revision = expectedRevision + 1 }, "RecordMonologueProgress", cancellationToken).ConfigureAwait(false);
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
