/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Call/MultimodalSessionLifecycle.cs, in the Vision & Voice application contract.
 * What: This file owns deterministic session-state and mode-transition rules.
 * How: Transitions produce a new revision and reject terminal or semantically invalid state changes.
 * Why: UI indicators must describe real capture/runtime state and cannot imply activity while disconnected.
 * Maintenance: Keep transient audio and frames outside these durable lifecycle snapshots.
 */

namespace Haven.Application.Call;

/// <summary>Validates user-facing state transitions and creates revisioned session snapshots.</summary>
public static class MultimodalSessionLifecycle
{
    private static readonly IReadOnlyDictionary<MultimodalSessionState, IReadOnlySet<MultimodalSessionState>> Allowed =
        new Dictionary<MultimodalSessionState, IReadOnlySet<MultimodalSessionState>>
        {
            [MultimodalSessionState.Starting] = Set(MultimodalSessionState.Listening, MultimodalSessionState.Reconnecting, MultimodalSessionState.Stopped, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.Listening] = Set(MultimodalSessionState.UserSpeaking, MultimodalSessionState.Processing, MultimodalSessionState.AssistantSpeaking, MultimodalSessionState.Paused, MultimodalSessionState.Reconnecting, MultimodalSessionState.Stopped, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.UserSpeaking] = Set(MultimodalSessionState.Processing, MultimodalSessionState.Listening, MultimodalSessionState.Paused, MultimodalSessionState.Reconnecting, MultimodalSessionState.Stopped, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.Processing] = Set(MultimodalSessionState.AssistantSpeaking, MultimodalSessionState.Listening, MultimodalSessionState.UserSpeaking, MultimodalSessionState.Paused, MultimodalSessionState.Reconnecting, MultimodalSessionState.Stopped, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.AssistantSpeaking] = Set(MultimodalSessionState.UserSpeaking, MultimodalSessionState.Processing, MultimodalSessionState.Listening, MultimodalSessionState.Paused, MultimodalSessionState.Reconnecting, MultimodalSessionState.Stopped, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.Paused] = Set(MultimodalSessionState.Listening, MultimodalSessionState.Processing, MultimodalSessionState.Reconnecting, MultimodalSessionState.Stopped, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.Reconnecting] = Set(MultimodalSessionState.Listening, MultimodalSessionState.Paused, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.Stopped] = Set(MultimodalSessionState.Starting, MultimodalSessionState.Listening, MultimodalSessionState.Processing, MultimodalSessionState.Ended, MultimodalSessionState.Failed),
            [MultimodalSessionState.Failed] = Set(MultimodalSessionState.Reconnecting, MultimodalSessionState.Ended),
            [MultimodalSessionState.Ended] = Set()
        };

    /// <summary>Returns whether the runtime may move from the current state to the requested state.</summary>
    public static bool CanTransition(MultimodalSessionState current, MultimodalSessionState next) =>
        current == next || Allowed.TryGetValue(current, out var states) && states.Contains(next);

    /// <summary>Creates a revisioned state update or returns a stable conflict explanation.</summary>
    public static bool TryTransition(
        MultimodalSession session,
        MultimodalSessionState next,
        out MultimodalSession updated,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Validate() is { } invalid)
        {
            updated = session;
            error = invalid;
            return false;
        }
        if (!CanTransition(session.State, next))
        {
            updated = session;
            error = $"Session cannot transition from {session.State} to {next}.";
            return false;
        }
        if (session.State == next)
        {
            updated = session;
            error = null;
            return true;
        }

        updated = session with
        {
            State = next,
            Revision = checked(session.Revision + 1),
            MicrophoneState = next switch
            {
                MultimodalSessionState.Starting => MicrophoneCaptureState.Starting,
                MultimodalSessionState.Reconnecting => MicrophoneCaptureState.Disconnected,
                MultimodalSessionState.Paused or MultimodalSessionState.Stopped =>
                    session.MicrophoneState == MicrophoneCaptureState.Off ? MicrophoneCaptureState.Off : MicrophoneCaptureState.Paused,
                MultimodalSessionState.Ended or MultimodalSessionState.Failed => MicrophoneCaptureState.Off,
                _ => session.MicrophoneState
            }
        };
        error = null;
        return true;
    }

    /// <summary>Changes mode without changing canonical conversation/session identity or carrying ambient retention into a new mode.</summary>
    public static bool TrySetMode(
        MultimodalSession session,
        VisionVoiceMode mode,
        out MultimodalSession updated,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State is MultimodalSessionState.Ended or MultimodalSessionState.Failed)
        {
            updated = session;
            error = $"A {session.State} session cannot change mode.";
            return false;
        }
        var retention = mode is VisionVoiceMode.LiveListener or VisionVoiceMode.LiveTranslate
            ? VisionVoiceRetention.Ephemeral
            : session.Retention;
        updated = session with
        {
            VoiceMode = mode,
            Retention = retention,
            IsTranscriptVisible = mode is VisionVoiceMode.LiveListener or VisionVoiceMode.LiveTranslate ? false : session.IsTranscriptVisible,
            Revision = checked(session.Revision + 1)
        };
        error = updated.Validate();
        if (error is null) return true;
        updated = session;
        return false;
    }

    private static IReadOnlySet<MultimodalSessionState> Set(params MultimodalSessionState[] states) => states.ToHashSet();
}
