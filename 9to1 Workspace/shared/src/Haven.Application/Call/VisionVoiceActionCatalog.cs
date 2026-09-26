/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Call/VisionVoiceActionCatalog.cs, in the Vision & Voice public automation contract.
 * What: This file declares the typed, permission-aware action surface for Vision & Voice, Overlay and Dictation.
 * How: Each action carries argument/result schema, risk, reversibility, side-effect and affected-scope metadata.
 * Why: UI and automation must use the same owner-controlled operations and must not hide more powerful actions from Home.
 * Maintenance: Add an action here whenever a user-visible operation gains an API; never classify target-app risk from caller input.
 */

namespace Haven.Application.Call;

/// <summary>Risk classification assigned by the action owner.</summary>
public enum VisionVoiceRiskLevel
{
    Ordinary = 0,
    Elevated = 1,
    TargetDefined = 2
}

/// <summary>One declared argument in a public Vision &amp; Voice API action.</summary>
public sealed record VisionVoiceArgument(string Name, string JsonType, bool Required, string Description);

/// <summary>Machine-readable public action metadata consumed by Home's permission broker and API catalogue.</summary>
public sealed record VisionVoiceActionDefinition(
    string Name,
    IReadOnlyList<VisionVoiceArgument> Arguments,
    string ResultSchema,
    IReadOnlyList<string> PermissionScopes,
    VisionVoiceRiskLevel Risk,
    bool IsReversible,
    bool ProducesExternalSideEffects,
    string AffectedObjects,
    bool RequiresIdempotencyKey);

/// <summary>Canonical Vision &amp; Voice, Overlay and Dictation action catalogue.</summary>
public static class VisionVoiceActionCatalog
{
    private static VisionVoiceArgument Required(string name, string type, string description) => new(name, type, true, description);
    private static VisionVoiceArgument Optional(string name, string type, string description) => new(name, type, false, description);

    private static VisionVoiceActionDefinition Action(
        string name,
        string result,
        string scope,
        VisionVoiceRiskLevel risk,
        bool reversible,
        bool external,
        string affected,
        bool idempotency,
        params VisionVoiceArgument[] arguments) => new(
            name,
            arguments,
            result,
            [scope],
            risk,
            reversible,
            external,
            affected,
            idempotency);

    private static readonly IReadOnlyList<VisionVoiceActionDefinition> Definitions = Array.AsReadOnly(
    [
        Action("9to1.VisionVoice.Start", "MultimodalSession", "visionvoice.session.create", VisionVoiceRiskLevel.Elevated, true, true, "One session, conversation and declared sources", true,
            Required("config", "object", "Mode, canonical conversation/Space context, model policy and explicitly selected sources."), Required("idempotencyKey", "string", "Stable retry key for this start request.")),
        Action("9to1.VisionVoice.ListSessions", "Paged<MultimodalSessionSummary>", "visionvoice.session.read", VisionVoiceRiskLevel.Ordinary, true, false, "Sessions visible to the caller", false,
            Optional("cursor", "string", "Opaque continuation cursor."), Optional("limit", "integer", "Page size from 1 through 100.")),
        Action("9to1.VisionVoice.GetSession", "MultimodalSession", "visionvoice.session.read", VisionVoiceRiskLevel.Ordinary, true, false, "One session", false,
            Required("sessionID", "string", "Stable session identifier.")),
        Action("9to1.VisionVoice.End", "MultimodalSession", "visionvoice.session.end", VisionVoiceRiskLevel.Ordinary, true, true, "One active session and its capture sources", true,
            Required("sessionID", "string", "Stable session identifier."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetMode", "MultimodalSession", "visionvoice.session.configure", VisionVoiceRiskLevel.Elevated, true, true, "One active session and its runtime mode", true,
            Required("sessionID", "string", "Stable session identifier."), Required("mode", "VisionVoiceMode", "One of Conversational, Monologue, LiveListener or LiveTranslate."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SendText", "VoiceTurn", "visionvoice.session.send", VisionVoiceRiskLevel.Elevated, false, true, "One canonical conversation turn", true,
            Required("sessionID", "string", "Stable session identifier."), Required("text", "string", "Typed user content."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.Pause", "MultimodalSession", "visionvoice.session.control", VisionVoiceRiskLevel.Ordinary, true, true, "One session and its active media streams", true,
            Required("sessionID", "string", "Stable session identifier."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.Resume", "MultimodalSession", "visionvoice.session.control", VisionVoiceRiskLevel.Ordinary, true, true, "One paused or resumable session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.Interrupt", "MultimodalSession", "visionvoice.session.control", VisionVoiceRiskLevel.Ordinary, true, true, "Current assistant output in one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetMicrophone", "MultimodalSession", "visionvoice.microphone.control", VisionVoiceRiskLevel.Elevated, true, true, "Microphone capture and audio delivery for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("enabled", "boolean", "Whether microphone capture is enabled."), Optional("deviceID", "string", "Selected input device or null for automatic/default."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetInputDevice", "MultimodalSession", "visionvoice.device.configure", VisionVoiceRiskLevel.Ordinary, true, true, "Input device for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Optional("deviceID", "string", "Selected input device or null for automatic/default."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetOutputDevice", "MultimodalSession", "visionvoice.device.configure", VisionVoiceRiskLevel.Ordinary, true, true, "Output device for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Optional("deviceID", "string", "Selected output device or null for automatic/default."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.ListDevices", "Paged<AudioDevice>", "visionvoice.device.read", VisionVoiceRiskLevel.Ordinary, true, false, "Available microphone and output devices", false,
            Optional("cursor", "string", "Opaque continuation cursor."), Optional("limit", "integer", "Page size from 1 through 100.")),
        Action("9to1.VisionVoice.SetCamera", "MultimodalSession", "visionvoice.camera.control", VisionVoiceRiskLevel.Elevated, true, true, "Camera source and frames for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Optional("source", "VisualSourceConfig", "Explicit camera source or null to stop camera input."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetScreenShare", "MultimodalSession", "visionvoice.screen.control", VisionVoiceRiskLevel.Elevated, true, true, "Screen/window source and frames for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Optional("source", "VisualSourceConfig", "Explicit window/display source or null to stop sharing."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.CaptureScreenshot", "VisualSourceDescriptor", "visionvoice.capture.screenshot", VisionVoiceRiskLevel.Elevated, false, true, "One explicitly selected display, window or region capture", true,
            Required("config", "ScreenshotConfig", "Explicit full-display, selected-window or selected-region capture request."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.AddVisualSource", "MultimodalSession", "visionvoice.capture.attach", VisionVoiceRiskLevel.Elevated, true, true, "One explicitly attached image or semantic source", true,
            Required("sessionID", "string", "Stable session identifier."), Required("source", "VisualSourceConfig", "Typed source reference with provenance."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.RemoveVisualSource", "MultimodalSession", "visionvoice.capture.control", VisionVoiceRiskLevel.Ordinary, true, true, "One source identified by session-local SourceID", true,
            Required("sessionID", "string", "Stable session identifier."), Required("sourceID", "string", "Stable session-local source identifier."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.GetVisualSources", "Paged<VisualSourceDescriptor>", "visionvoice.capture.read", VisionVoiceRiskLevel.Ordinary, true, false, "Visual sources visible to the caller for one session", false,
            Required("sessionID", "string", "Stable session identifier."), Optional("cursor", "string", "Opaque continuation cursor."), Optional("limit", "integer", "Page size from 1 through 100.")),
        Action("9to1.VisionVoice.SetTranscriptVisible", "MultimodalSession", "visionvoice.presentation.configure", VisionVoiceRiskLevel.Ordinary, true, false, "Transcript presentation for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("visible", "boolean", "Whether a permitted transcript view is shown."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetCaptions", "MultimodalSession", "visionvoice.presentation.configure", VisionVoiceRiskLevel.Ordinary, true, false, "Original and translated caption presentation for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("originalEnabled", "boolean", "Whether original-language captions are shown."), Required("translatedEnabled", "boolean", "Whether translated captions are shown."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetRetention", "MultimodalSession", "visionvoice.retention.configure", VisionVoiceRiskLevel.Elevated, true, true, "Audio and transcript retention for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("retainAudio", "boolean", "Whether a user-approved audio recording is retained."), Required("retainTranscript", "boolean", "Whether a durable transcript is retained."), Required("explicitConsent", "boolean", "Must be true for either retained value."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetLiveTranslateLanguages", "MultimodalSession", "visionvoice.translation.configure", VisionVoiceRiskLevel.Ordinary, true, true, "Language and locale set for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("languages", "LiveTranslateLanguageSet", "Source, selected locales, direction and per-output preferences."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetLiveTranslateDirection", "MultimodalSession", "visionvoice.translation.configure", VisionVoiceRiskLevel.Ordinary, true, true, "Interpretation direction for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("direction", "LiveTranslateDirection", "OneWay, Bidirectional or Multilingual."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetLiveTranslateOutput", "MultimodalSession", "visionvoice.translation.configure", VisionVoiceRiskLevel.Ordinary, true, true, "Speech and caption preferences for one or more target locales", true,
            Required("sessionID", "string", "Stable session identifier."), Required("config", "LiveTranslateOutputPreference[]", "Per-target speech/caption preferences."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.SetLiveTranslateGlossaries", "MultimodalSession", "visionvoice.translation.configure", VisionVoiceRiskLevel.Ordinary, true, true, "Glossary references for one session", true,
            Required("sessionID", "string", "Stable session identifier."), Required("glossaryIDs", "string[]", "Stable Dulche Translate glossary identifiers."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.GetMonologueRun", "MonologueRun", "visionvoice.monologue.read", VisionVoiceRiskLevel.Ordinary, true, false, "Outline and playback position for one session", false,
            Required("sessionID", "string", "Stable session identifier.")),
        Action("9to1.VisionVoice.GetListenerEvents", "Paged<LiveListenerEvent>", "visionvoice.listener.read", VisionVoiceRiskLevel.Ordinary, true, false, "Derived semantic events for one session; no raw audio", false,
            Required("sessionID", "string", "Stable session identifier."), Optional("cursor", "string", "Opaque continuation cursor."), Optional("limit", "integer", "Page size from 1 through 100.")),
        Action("9to1.VisionVoice.ContinueInSpaces", "SpaceConversationReference", "visionvoice.session.continue", VisionVoiceRiskLevel.Ordinary, true, true, "Same ConversationID, session and retained explicit sources", true,
            Required("sessionID", "string", "Stable session identifier."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.Overlay.Open", "OverlaySessionReference", "visionvoice.overlay.open", VisionVoiceRiskLevel.Ordinary, true, false, "Transient Overlay surface and explicitly authorized context", true,
            Optional("context", "TypedContextEnvelope", "Semantic references and explicit user-selected sources."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.Overlay.Close", "OverlayCloseResult", "visionvoice.overlay.close", VisionVoiceRiskLevel.Ordinary, true, false, "Overlay UI; ephemeral captures are discarded by policy", true,
            Optional("overlaySessionID", "string", "Overlay session identifier; defaults to the active Overlay."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.Overlay.CaptureRegion", "VisualSourceDescriptor", "visionvoice.capture.region", VisionVoiceRiskLevel.Elevated, false, true, "One user-selected screen region", true,
            Optional("overlaySessionID", "string", "Overlay session identifier; defaults to the active Overlay."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.VisionVoice.Overlay.FindAutomations", "Paged<AutomationDescriptor>", "visionvoice.overlay.automation.read", VisionVoiceRiskLevel.Ordinary, true, false, "Existing Automations compatible with typed context", false,
            Optional("context", "TypedContextEnvelope", "Selected text/object references and source metadata."), Optional("cursor", "string", "Opaque continuation cursor."), Optional("limit", "integer", "Page size from 1 through 100.")),
        Action("9to1.VisionVoice.Overlay.RunAutomation", "AutomationJob", "visionvoice.overlay.automation.execute", VisionVoiceRiskLevel.TargetDefined, false, true, "Target Automation and its declared input object scopes", true,
            Required("automationID", "string", "Stable canonical Automation identifier."), Required("context", "TypedContextEnvelope", "Typed inputs and source metadata matching the Automation schema."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.Dictation.Start", "DictationSession", "dictation.session.start", VisionVoiceRiskLevel.Elevated, true, true, "One host editor selection and inserted text operation", true,
            Required("context", "DictationEditorContext", "Authorized host editor and selection identity."), Required("mode", "DictationMode", "Raw, Clean or Polished."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.Dictation.Stop", "DictationSession", "dictation.session.control", VisionVoiceRiskLevel.Ordinary, true, true, "One dictation session and active microphone stream", true,
            Required("sessionID", "string", "Stable dictation session identifier."), Required("idempotencyKey", "string", "Stable retry key.")),
        Action("9to1.Dictation.GetState", "DictationSession", "dictation.session.read", VisionVoiceRiskLevel.Ordinary, true, false, "One dictation session", false,
            Required("sessionID", "string", "Stable dictation session identifier.")),
        Action("9to1.Dictation.SetMode", "DictationSession", "dictation.session.configure", VisionVoiceRiskLevel.Ordinary, true, false, "Editing mode for one active dictation session", true,
            Required("sessionID", "string", "Stable dictation session identifier."), Required("mode", "DictationMode", "Raw, Clean or Polished."), Required("idempotencyKey", "string", "Stable retry key."))
    ]);

    /// <summary>Gets the immutable canonical set of public app operations.</summary>
    public static IReadOnlyList<VisionVoiceActionDefinition> All => Definitions;
}
