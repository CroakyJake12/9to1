/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Call/VisionVoiceModels.cs, in the Vision & Voice application contract.
 * What: This file defines canonical session, capture, translation, privacy, state and structured-failure models.
 * How: Stable identifiers and immutable records make session snapshots safe to pass between the UI, API and Dulche adapters.
 * Why: Vision & Voice must present one truthful multimodal session without creating a second conversation or media runtime.
 * Maintenance: Keep runtime execution in Dulche; evolve persisted session metadata with explicit revisions and migrations.
 */

namespace Haven.Application.Call;

/// <summary>Identifies one of the four user-facing voice experiences.</summary>
public enum VisionVoiceMode
{
    Conversational = 0,
    Monologue = 1,
    LiveListener = 2,
    LiveTranslate = 3
}

/// <summary>Describes the active state of one canonical multimodal session.</summary>
public enum MultimodalSessionState
{
    Starting = 0,
    Listening = 1,
    UserSpeaking = 2,
    Processing = 3,
    AssistantSpeaking = 4,
    Paused = 5,
    Reconnecting = 6,
    Stopped = 7,
    Ended = 8,
    Failed = 9
}

/// <summary>Identifies an explicitly attached or continuously shared visual source.</summary>
public enum VisualSourceType
{
    UploadedImage = 0,
    Screenshot = 1,
    ScreenRegion = 2,
    WindowShare = 3,
    DisplayShare = 4,
    Camera = 5,
    AppProvidedVisualContext = 6
}

/// <summary>Describes whether new frames or image content can currently reach the runtime.</summary>
public enum VisualSourceState
{
    Active = 0,
    Paused = 1,
    Stopped = 2,
    Unavailable = 3
}

/// <summary>Identifies the interpretation direction for a Live Translate session.</summary>
public enum LiveTranslateDirection
{
    OneWay = 0,
    Bidirectional = 1,
    Multilingual = 2
}

/// <summary>Reports the microphone capture state independently of the broader session state.</summary>
public enum MicrophoneCaptureState
{
    Off = 0,
    Starting = 1,
    Listening = 2,
    Muted = 3,
    Paused = 4,
    PermissionDenied = 5,
    Unavailable = 6,
    Disconnected = 7
}

/// <summary>Reports the current optional retention policy for ambient audio and transcript text.</summary>
public sealed record VisionVoiceRetention(bool RetainAudio, bool RetainTranscript, DateTimeOffset? ExplicitlyEnabledAt)
{
    /// <summary>Default policy for ambient interpretation: retain neither raw audio nor a transcript.</summary>
    public static VisionVoiceRetention Ephemeral { get; } = new(false, false, null);
}

/// <summary>Identifies one selected visual source with stable session-local identity and provenance.</summary>
public sealed record VisualSourceDescriptor(
    Guid SourceId,
    VisualSourceType Type,
    string Provenance,
    string DisplayName,
    VisualSourceState State,
    bool IsContinuous,
    bool IsEphemeral,
    DateTimeOffset UpdatedAt);

/// <summary>Sets speech and caption preferences for one translated target locale.</summary>
public sealed record LiveTranslateOutputPreference(string Locale, bool CaptionsEnabled, bool SpeechEnabled);

/// <summary>Stores the languages and per-output preferences bound to a Live Translate session.</summary>
public sealed record LiveTranslateLanguageSet(
    string? SourceLocale,
    IReadOnlyList<string> Locales,
    LiveTranslateDirection Direction,
    IReadOnlyList<LiveTranslateOutputPreference> Outputs,
    IReadOnlyList<string> GlossaryIds);

/// <summary>Tracks the resumable outline and current position of a Monologue run.</summary>
public sealed record MonologueRun(
    Guid RunId,
    string Objective,
    TimeSpan? TargetDuration,
    IReadOnlyList<string> Sections,
    int CurrentSection,
    TimeSpan Position,
    bool IsPaused,
    IReadOnlyList<string> SourceRefs);

/// <summary>One useful derived event from contextual ambient listening.</summary>
public sealed record LiveListenerEvent(
    Guid EventId,
    string Category,
    string Summary,
    double Confidence,
    IReadOnlyList<string> SourceRefs,
    DateTimeOffset OccurredAt,
    bool RequiresUserReview);

/// <summary>Canonical user-facing and capture state for one shared conversation session.</summary>
public sealed record MultimodalSession(
    Guid SessionId,
    Guid ConversationId,
    Guid? SpaceId,
    VisionVoiceMode VoiceMode,
    string? RuntimeSessionReference,
    string EffectiveModelPolicy,
    string? VoiceModelId,
    string? VoiceId,
    MultimodalSessionState State,
    MicrophoneCaptureState MicrophoneState,
    IReadOnlyList<VisualSourceDescriptor> VisualSources,
    string? InputDeviceId,
    string? OutputDeviceId,
    DateTimeOffset CreatedAt,
    long Revision,
    VisionVoiceRetention Retention,
    LiveTranslateLanguageSet? LiveTranslateLanguages = null,
    MonologueRun? Monologue = null,
    bool IsTranscriptVisible = false,
    bool IsOriginalCaptionEnabled = false,
    bool IsTranslatedCaptionEnabled = false,
    bool IsLocalOnly = false)
{
    /// <summary>Finds invalid identity, revision, source and mode-specific session state.</summary>
    public string? Validate()
    {
        if (SessionId == Guid.Empty) return "A multimodal session requires a stable SessionID.";
        if (ConversationId == Guid.Empty) return "A multimodal session requires its canonical ConversationID.";
        if (Revision < 1) return "A multimodal session revision must be positive.";
        if (string.IsNullOrWhiteSpace(EffectiveModelPolicy)) return "A multimodal session requires an effective model policy.";
        if (Retention is null) return "A multimodal session requires an explicit retention policy.";
        if ((VoiceMode is VisionVoiceMode.LiveListener or VisionVoiceMode.LiveTranslate) &&
            (Retention.RetainAudio || Retention.RetainTranscript) && Retention.ExplicitlyEnabledAt is null)
            return "Ambient audio or transcript retention requires a separate explicit opt-in timestamp.";
        if (VisualSources is null) return "VisualSources must be an explicit collection, including when empty.";
        if (VisualSources.Any(source => source.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(source.Provenance)))
            return "Each visual source requires a stable identity and provenance.";
        if (VisualSources.Select(source => source.SourceId).Distinct().Count() != VisualSources.Count)
            return "Visual source identities must be unique within a session.";
        if (VisualSources.Any(source => source.IsContinuous && !source.IsEphemeral &&
            (source.Type is VisualSourceType.Camera or VisualSourceType.WindowShare or VisualSourceType.DisplayShare)))
            return "Continuous camera and screen frames must remain ephemeral.";
        if (VoiceMode == VisionVoiceMode.LiveTranslate)
        {
            var languageError = LiveTranslateLanguages?.Validate();
            if (languageError is not null) return languageError;
        }
        if (VoiceMode == VisionVoiceMode.Monologue && Monologue is { RunId: var runId } && runId == Guid.Empty)
            return "A Monologue run requires a stable RunID.";
        return null;
    }
}

/// <summary>Checks a Live Translate language/output set before starting or updating a session.</summary>
public static class LiveTranslateLanguageSetRules
{
    /// <summary>Returns null when the language/output set satisfies its selected direction.</summary>
    public static string? Validate(this LiveTranslateLanguageSet languages)
    {
        ArgumentNullException.ThrowIfNull(languages);
        if (languages.Locales.Any(string.IsNullOrWhiteSpace)) return "Selected language locales cannot be empty.";
        if (languages.Locales.Distinct(StringComparer.OrdinalIgnoreCase).Count() != languages.Locales.Count)
            return "Selected language locales must be unique.";
        var minimum = languages.Direction switch
        {
            LiveTranslateDirection.OneWay => 1,
            LiveTranslateDirection.Bidirectional => 2,
            LiveTranslateDirection.Multilingual => 3,
            _ => int.MaxValue
        };
        if (languages.Locales.Count < minimum)
            return languages.Direction switch
            {
                LiveTranslateDirection.OneWay => "One-way interpretation requires at least one target locale.",
                LiveTranslateDirection.Bidirectional => "Bidirectional interpretation requires at least two selected locales.",
                LiveTranslateDirection.Multilingual => "Multilingual interpretation requires at least three selected locales.",
                _ => "The Live Translate direction is unsupported."
            };
        if (languages.Outputs.Any(output => string.IsNullOrWhiteSpace(output.Locale) ||
            !languages.Locales.Contains(output.Locale, StringComparer.OrdinalIgnoreCase)))
            return "Every translated output must target a selected locale.";
        if (languages.Outputs.Select(output => output.Locale).Distinct(StringComparer.OrdinalIgnoreCase).Count() != languages.Outputs.Count)
            return "Each target locale can have only one output preference.";
        if (languages.GlossaryIds.Any(string.IsNullOrWhiteSpace)) return "Glossary identities cannot be empty.";
        return null;
    }
}

/// <summary>Stable machine-readable failure identifiers for Vision &amp; Voice operations.</summary>
public enum VisionVoiceErrorCode
{
    MicrophonePermissionDenied,
    CameraPermissionDenied,
    ScreenCapturePermissionDenied,
    InputDeviceUnavailable,
    OutputDeviceUnavailable,
    CameraUnavailable,
    CaptureSourceUnavailable,
    VoiceModelUnavailable,
    VisionCapabilityUnavailable,
    ModelCapabilityMismatch,
    SessionDisconnected,
    SessionNotFound,
    VisualSourceUnavailable,
    CaptureStopped,
    ProviderUnavailable,
    PrivacyPolicyDenied,
    AutomationInputMissing,
    DictationUnavailable,
    LiveTranslateLanguageUnsupported,
    LiveTranslateLanguageAmbiguous,
    LiveTranslateCapabilityUnavailable,
    LiveTranslateOutputUnavailable,
    PermissionRequired,
    PermissionDenied,
    InvalidRequest,
    Conflict,
    UnknownFailure
}

/// <summary>Structured failure information returned by a public Vision &amp; Voice action.</summary>
public sealed record VisionVoiceError(
    VisionVoiceErrorCode Code,
    string Message,
    string Target,
    string Action,
    bool IsRecoverable,
    TimeSpan? RetryAfter = null);

/// <summary>Typed success or failure returned by the Vision &amp; Voice product API.</summary>
public sealed record VisionVoiceResult<T>(T? Value, VisionVoiceError? Error)
{
    /// <summary>Gets whether the operation returned a value without a structured error.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Creates a successful result.</summary>
    public static VisionVoiceResult<T> Success(T value) => new(value, null);

    /// <summary>Creates a failed result with stable action and target identity.</summary>
    public static VisionVoiceResult<T> Failure(VisionVoiceError error) => new(default, error);
}
