# Vision & Voice — shared live session system — Overlay continuity; Voice tools and consequential actions (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0989 · v2 lines 4458–4458

Automation execution from Overlay uses the canonical Automation definition, permissions, approvals, Action Graph, logging, idempotency and error handling. Overlay MUST NOT independently emulate the Automation's actions or use it to bypass consequential-action confirmation.

### V2-B0990 · v2 lines 4461–4461

Overlay continuity

### V2-B0991 · v2 lines 4464–4464

Overlay MUST support Continue in Spaces, preserving the same conversation/request context and retained explicit sources. Closing the transient Overlay closes only the overlay UI; ephemeral captures are discarded according to retention policy unless deliberately retained.

### V2-B0992 · v2 lines 4467–4467

If a Voice session is already active, invoking Overlay MAY augment the same session, for example allowing the user to select a screen region while saying 'look at this bit' without spawning a second assistant.

### V2-B0993 · v2 lines 4470–4470

Voice tools and consequential actions

### V2-B0994 · v2 lines 4473–4473

Spoken or typed Voice requests may execute normal authorised 9to1 app/tool actions. Voice does not reduce permission requirements. High-risk confirmation prompts MUST identify the concrete target/action rather than relying on ambiguous verbal confirmation.

### V2-B0995 · v2 lines 4476–4476

AI-assisted dictation

### V2-B0996 · v2 lines 4479–4479

Dictation is a separate reusable speech-to-text editing capability, not a full Voice conversation mode. It MUST be available to supported editable text surfaces such as Write, Spaces, Mail, Boards, Present speaker notes, Planner descriptions, Sites, AI Studio and other CUI text editors through the shared platform capability.

### V2-B0997 · v2 lines 4482–4482

During Dictation the AI does not respond conversationally or speak back. It converts speech into editable text/actions in the host app. Dictation SHOULD support Raw, Clean and Polished modes or equivalent: Raw remains close to literal transcription; Clean removes filler/repetition/false starts and adds punctuation; Polished may lightly restructure rambling speech into well-written prose while preserving intended meaning.

### V2-B0998 · v2 lines 4485–4485

Dictation MUST support contextual correction/commands such as new paragraph, bullet list, heading changes, delete/replace recent text and spoken self-corrections, applied through the host app/shared productivity engine rather than being inserted literally when recognised as commands.

### V2-B0999 · v2 lines 4488–4488

Dictation MAY use limited authorised current-editor context to improve terminology, names and punctuation. It MUST NOT use unrelated private context merely to improve transcription.

### V2-B1000 · v2 lines 4491–4491

Background/session continuity

### V2-B1001 · v2 lines 4494–4494

An explicitly active Voice session may continue when the Vision & Voice window loses focus according to platform permission/background rules. Camera/screen sharing continues only while its visible capture state remains active; OS/session lock must suspend/stop sensitive capture according to platform policy.

### V2-B1002 · v2 lines 4497–4497

If Home/Dulche temporarily restarts, Vision & Voice MUST preserve durable conversation/session metadata, show truthful Reconnecting/failed state and reconnect where supported. It MUST NOT indicate that microphone/model processing is active while disconnected.

### V2-B1003 · v2 lines 4500–4500

Accessibility

### V2-B1004 · v2 lines 4503–4503

Vision & Voice MUST support captions, keyboard-only controls, screen-reader-labelled session/capture states, visual equivalents for audio state, adjustable caption/text sizing where applicable, reduced motion and non-colour-only state indication. No required action may be voice-only.

### V2-B1005 · v2 lines 4506–4506

Platform capability behaviour

### V2-B1006 · v2 lines 4509–4509

Native Windows/9to1-OS may provide the full OS-global Overlay, native window/display capture and background voice capabilities. Web may provide microphone, camera and browser-authorised screen sharing but MUST NOT claim an OS-global Overlay outside the browser. Mobile uses supported camera/microphone/share-sheet/screen-capture capabilities. All surfaces capability-negotiate rather than assuming parity.

### V2-B1007 · v2 lines 4512–4512

Vision & Voice API

### V2-B1008 · v2 lines 4515–4540

9to1.VisionVoice.Start(config);
9to1.VisionVoice.GetSession(sessionID);
9to1.VisionVoice.End(sessionID);
9to1.VisionVoice.SetMode(sessionID, mode);
9to1.VisionVoice.SendText(sessionID, text);
9to1.VisionVoice.SetMicrophone(sessionID, enabled);
9to1.VisionVoice.SetCamera(sessionID, source?);
9to1.VisionVoice.SetScreenShare(sessionID, source?);
9to1.VisionVoice.CaptureScreenshot(config);
9to1.VisionVoice.AddVisualSource(sessionID, source);
9to1.VisionVoice.RemoveVisualSource(sessionID, sourceID);
9to1.VisionVoice.SetTranscriptVisible(sessionID, visible);
9to1.VisionVoice.SetCaptions(sessionID, enabled);
9to1.VisionVoice.SetLiveTranslateLanguages(sessionID, languages);
9to1.VisionVoice.SetLiveTranslateDirection(sessionID, direction);
9to1.VisionVoice.SetLiveTranslateOutput(sessionID, config);
9to1.VisionVoice.SetLiveTranslateGlossaries(sessionID, glossaryIDs);
9to1.VisionVoice.Overlay.Open(context?);
9to1.VisionVoice.Overlay.Close();
9to1.VisionVoice.Overlay.CaptureRegion();
9to1.VisionVoice.Overlay.RunAutomation(automationID, context);
9to1.VisionVoice.ContinueInSpaces(sessionID);
9to1.Dictation.Start(context, mode);
9to1.Dictation.Stop(sessionID);
9to1.Dictation.GetState(sessionID);
9to1.Dictation.SetMode(sessionID, mode);

### V2-B1009 · v2 lines 4543–4543

Dulche remains execution authority for realtime audio/model streaming and exposes the corresponding Voice runtime APIs. Vision & Voice APIs own user-facing session/capture/presentation state and MUST NOT duplicate the Dulche runtime as a second incompatible engine.

### V2-B1010 · v2 lines 4546–4546

Failure behaviour

### V2-B1011 · v2 lines 4549–4549

Vision & Voice MUST return structured failures including MicrophonePermissionDenied, CameraPermissionDenied, ScreenCapturePermissionDenied, InputDeviceUnavailable, OutputDeviceUnavailable, CameraUnavailable, CaptureSourceUnavailable, VoiceModelUnavailable, VisionCapabilityUnavailable, ModelCapabilityMismatch, SessionDisconnected, SessionNotFound, VisualSourceUnavailable, CaptureStopped, ProviderUnavailable, PrivacyPolicyDenied, AutomationInputMissing, DictationUnavailable, LiveTranslateLanguageUnsupported, LiveTranslateLanguageAmbiguous, LiveTranslateCapabilityUnavailable and LiveTranslateOutputUnavailable in addition to normal Dulche/Translate/tool/action errors.

### V2-B1012 · v2 lines 4552–4552

Acceptance requirements

### V2-B1013 · v2 lines 4555–4585

Vision & Voice is implementation-complete only when acceptance coverage verifies at least:
* Conversational, Monologue, Live Listener and Live Translate operate as distinct modes on shared canonical session/conversation infrastructure.
* Typed text can be submitted during an active Voice session without ending it.
* Conversational Voice supports natural interruption plus explicit pause/mute/end state.
* Monologue preserves outline/progress and can resume after interruption/stopping without restarting completed content/actions.
* Live Listener extracts structured lesson/meeting information while raw ambient audio/transcript is not persisted by default.
* Explicit recording/transcription opt-in is visibly distinct from default Live Listener.
* Live Listener can create authorised notes/homework/date actions and trigger a low-risk contextual app action such as opening Canvas.
* Live Translate supports one-way, bidirectional and more-than-two-language sessions, including simultaneous target captions/output where supported.
* Live Translate uses Dulche Translate for language/glossary/context handling and does not create a second translation engine.
* Live Translate raw audio and ordinary transcript are ephemeral by default unless the user explicitly enables retention.
* Live Translate can use normal Agents/tools and perform authorised translated-context actions only through owning typed APIs and normal approval rules.
* Background/irrelevant speech is not indiscriminately written into notes and uncertain action cues do not silently cause consequential actions.
* Floating voice pill controls the same SessionID as the full app.
* Microphone/output device can hot-switch and device loss produces a recoverable truthful state.
* Voice named-voice selection stores VoiceID separately from VoiceModelID and the UI label becomes Voice when multiple voices exist.
* Discover/Universal Model Picker can recommend/select a compatible high-quality Voice Model without burying Voice under Audio.
* Voice + screenshot, Voice + continuous screen share and camera input work with visible capture state.
* Stopping camera/screen sharing immediately stops delivery of new frames.
* Semantic app context is preferred over screen scraping when available.
* Local-only/privacy policy cannot silently route audio/visual input to cloud.
* Overlay can capture selected context and use typed APIs rather than arbitrary input simulation.
* Overlay can execute an existing Automation using typed selected-text/context input and preserves the Automation's permission/Action Graph contract.
* Continue in Spaces preserves the active conversation/context.
* A high-risk spoken action still requires the correct explicit target-aware confirmation.
* Dictation works in at least Write, Spaces and Mail as non-conversational speech-to-text with Raw/Clean/Polished behaviour.
* Dictation removes filler/false starts in Clean/Polished modes without changing intended meaning and applies supported editing commands through host APIs.
* Home/Dulche restart produces a truthful reconnect/failed state without losing durable conversation state.
* Continuous capture frames remain ephemeral rather than being permanently attached by default.
* Web/native capability differences are reported accurately, including no false claim of OS-global Overlay on web.
* Mic/camera/screen/listening states and all required controls are accessible and not communicated by colour alone.
