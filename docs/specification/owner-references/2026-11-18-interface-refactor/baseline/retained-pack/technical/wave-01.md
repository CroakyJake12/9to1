# Wave — multitrack audio production — Wave; Product role and native-generation principle (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1668 · v2 lines 7831–8035

Wave
Product role and native-generation principle
9-1 Wave is the native CUI audio recording, editing, mixing, sequencing and sound-creation app. It combines an approachable multitrack editor with professional waveform/spectral editing, mixing, effects, music sequencing, sound-effect construction, text-to-speech and AI-assisted production.
All generated audio components MUST be native, structured Wave components whenever the requested result can be represented by Wave's own sequencer, synthesis, effect, speech or clip systems. AI MUST NOT normally return an opaque arbitrary waveform as the canonical result for music, sound effects, speech or other structured generative workflows. Rendering to audio is a derived/export/playback result; the editable native component remains canonical.
Example: asking AI to create a song produces editable sequencer/instrument/pattern/arrangement data; asking for a laser sound creates an editable Sound Effect Builder patch; asking for narration creates a native Text-to-Speech object retaining its text/voice/prosody settings.
Shared media/audio engine
Wave MUST share its core audio/media primitives with Motion, Present and other eligible 9to1 surfaces rather than creating incompatible per-app audio engines. The shared media/audio layer owns canonical audio assets, clips, time ranges, transport/playback, waveform/spectrogram analysis, effect descriptors, rendering/export primitives and realtime-safe audio processing where applicable.
Motion uses the same compatible audio clip/effect/timeline primitives for video projects. Present recording/narration uses compatible Wave/shared-media assets. Vision & Voice contributes shared microphone/device discovery, OS audio permissions and applicable capture primitives, while Wave remains the persistent production editor. Dulche Audio/Voice supplies model-backed transcription, analysis and speech capability without becoming a second project model.
GStreamer is an approved maintained media backend and GStreamer Editing Services or an explicitly documented equivalent may be used where appropriate. Backend choice MUST preserve the public Wave project/action contracts.
Canonical project model
WaveProject has stable ProjectID, sample-rate/channel configuration, timeline/timebase, tempo map where used, Tracks[], Markers/Regions[], native generated components, project settings, CreatedAt/ModifiedAt and Revision.
AudioTrack has stable TrackID, name, ordered Clips[], Gain, Pan, Mute, Solo, RecordArm, Effects[], AutomationLanes[], routing/bus state where supported and Revision.
AudioClip has stable ClipID, SourceAssetID or native GeneratedComponentID, source range, TimelineStart, Duration, Gain, fades, stretch/pitch settings, effects/automation references and Revision. Timeline editing MUST be non-destructive by default.
Generated native components receive stable IDs and can be inserted on tracks like other sources while retaining their editable structured source. Rendering a component for playback/export does not replace that structured source.
Main workspace and transport
Wave's primary workspace is a multitrack timeline with transport, track controls, selected clip/component inspector and optional mixer/effects/sequencer/spectral panels. Record, Edit, Mix and Generate are workflow emphases over one project rather than incompatible document modes.
Transport MUST include:
* Play, Pause, Stop and Record.
* Seek to start/end and previous/next marker or region.
* Loop playback/recording.
* Timeline position display with time/timecode and musical bars/beats where enabled.
* Playback speed and metronome/count-in where relevant.
Keyboard-first editing shortcuts are required for repetitive timeline operations.
Recording
Wave MUST support robust microphone/device recording including input-device selection, mono/stereo/multichannel where available, input monitoring, level meters/clipping indication, record-arm per track, count-in/pre-roll/post-roll, punch-in/punch-out, overdub, loop/take recording and pause/resume.
Recording is written incrementally/crash-safely so a process or device failure does not discard an entire long recording. Incomplete recoverable takes remain clearly marked until finalised.
Waveform and clip editing
Required clip/waveform operations include:
* Select time range, move, duplicate, copy/paste, split and join/compose where semantically valid.
* Trim, slip/source-range edit, delete, ripple delete and insert/ripple move.
* Fade in/out and crossfade.
* Gain/normalise, silence, reverse, resample, time-stretch and pitch-shift.
* Snap to grid, markers, transients, clips or musical timing where enabled.
Ordinary clip edits change clip/source-range/timeline metadata rather than destructively rewriting source bytes. A destructive source edit, where explicitly requested, creates a recoverable revision/new source asset.
Multitrack, mixer, routing and buses
Wave MUST support multiple simultaneous audio tracks for voice, music, sound effects, ambience, generated content, imported media and multiple takes. Track grouping and multi-selection allow coherent edits across related clips.
The mixer provides per-track and master gain, pan, mute/solo, meters, insert effects and supported sends/buses/routing. Advanced routing may be hidden by default but the canonical project supports it where exposed.
Master processing is explicit and non-destructive. Export rendering follows the current routing/effect graph.
Effects and parameter automation
Core non-AI effect families include at least:
* EQ/filtering including high/low-pass and parametric EQ.
* Compressor, limiter, gate/expander and de-esser.
* Reverb and delay.
* Chorus/flanger/phaser and modulation effects where supported.
* Distortion/saturation.
* Pitch shift/time stretch.
* Stereo width, gain/normalisation and utility processing.
Effects are non-destructive insert/processing nodes by default and can be reordered, bypassed, edited and removed.
Effect/track/instrument parameters may be automated over time using native automation lanes/curves. Automation data is structured and editable rather than baked into a rendered waveform.
Spectral editing and repair
Wave MUST provide Waveform, Spectrogram and combined Waveform+Spectrogram views. Time/frequency selections can drive attenuation, filtering, silence, repair and model-assisted cleanup operations.
Native/manual repair includes where supported:
* Click/pop removal.
* DC offset correction.
* Hum/notch filtering.
* Noise reduction and gating.
* Clipping repair where technically possible.
* Silence trimming and loudness normalisation.
AI-assisted cleanup MAY isolate speech, remove/reduce noise, reverb, clicks, breaths/coughs, restore damaged recordings and perform other model-backed repair, but outputs remain represented as native derived clips/effect operations/revisions rather than silently overwriting the source.
Audio enhancement and denoising tools
Wave MUST expose first-class Enhance Audio and Denoise tools for selected clips, tracks, time ranges and imported or recorded audio. Enhance Audio is a configurable restoration/mastering pipeline, not a single opaque AI button. At minimum it MUST be able to combine appropriate noise/hum reduction, de-click/de-pop, de-reverb where available, clarity/EQ correction, dynamics/level correction and loudness normalisation; speech-focused enhancement MAY use model-backed processing. Denoise MUST provide an automatic one-click path plus an advanced path with strength/amount and preview/bypass. Where the selected backend supports noise-profile learning, Wave MUST allow a selected region to be used as the noise profile.
Enhancement and denoising MUST be previewable, undoable and non-destructive. Long-running model-backed processing MUST expose progress and cancellation. The source asset remains intact; Wave stores the configured processing and/or derived result with provenance. Applying enhancement MUST NOT silently modify unrelated clips, tracks, effects or master routing.
Music Sequencer
Wave MUST include a first-class native music sequencer. Music generation, whether user-authored or AI-assisted, produces editable sequencer content rather than an arbitrary final audio file.
The sequencer supports stable SequenceID/PatternID/NoteEventID/InstrumentID identities, tempo/time-signature maps, piano-roll and step-sequencer editing, note pitch/start/duration/velocity, quantisation, swing/groove, looping patterns, arrangement sections, track/instrument assignment and parameter automation.
Drum sequencing supports drum-grid/pad-oriented patterns, per-step velocity/probability/variation where supported and reusable kits. Melodic sequencing supports chords, scales/key assistance where enabled, note expression and automation.
The arrangement view assembles patterns/regions into song structure such as Intro, Verse, Chorus, Bridge and Outro without flattening them. Users can duplicate/modify a pattern instance or create a linked instance according to explicit identity semantics.
Native instruments and sampling
The sequencer MUST provide a versioned instrument registry. Native instrument families MAY include synthesizers, drums/percussion, sampler instruments and other maintained engines. Each Instrument has structured editable parameters, preset identity/version and automation-capable controls.
Sampler instruments reference canonical audio assets and expose mapped key/range, start/end, loop, pitch/tuning, envelope/filter and other supported parameters without duplicating source bytes unnecessarily.
AI may choose/configure instruments, write notes/patterns, create arrangement structure, automate parameters and mix tracks, but MUST produce the same native Sequencer/Instrument/Effect objects a user can inspect and edit manually.
AI music generation
A request such as 'make a two-minute upbeat electronic track' MUST generate a native music project/section: tempo and meter, sequencer tracks, instrument choices/settings, editable note/pattern data, arrangement, automation and effects/mix state. The canonical AI result is NOT an opaque two-minute waveform.
AI may iterate by editing native structures: 'make the chorus heavier' changes relevant patterns/instruments/automation/effects; 'change the chord progression' rewrites note/chord events while preserving unrelated project structure.
Generated music can subsequently be manually edited in piano roll/step sequencer/mixer. Export renders the structured project into WAV/FLAC/MP3/etc. only when requested.
Sound Effects Builder
Wave MUST include a native Sound Effects Builder for procedural/constructed sound effects. A SoundEffectPatch has stable PatchID and a structured processing/generation graph rather than being merely a rendered audio clip.
The builder SHOULD use the Home Node Graph Framework for editing/visualisation where appropriate, with Wave-specific realtime audio nodes and execution handled by the Wave audio engine.
Core SFX node families include where supported:
* Oscillator/wavetable/source generators.
* Noise generators.
* Sampler/audio-asset sources.
* Envelope/ADSR and amplitude shaping.
* Pitch/frequency modulation and LFO.
* Filters/EQ.
* Distortion/waveshaping.
* Delay/reverb/modulation effects.
* Layer/mix/split/combine.
* Randomisation/probability and parameter modulation.
* Output/render.
A request such as 'create a futuristic laser blast' causes AI to build/configure an editable native patch graph, envelopes, oscillators/noise, pitch movement and effects. The generated patch can be auditioned and inserted onto the timeline, but the patch remains the canonical editable source.
The UI SHOULD offer simple high-level macro controls for common users while allowing advanced users to open the complete patch graph.
Text to Speech
Wave MUST include native Text-to-Speech components. A SpeechSynthesis object stores stable SpeechID, text/script, selected VoiceID/model/runtime, language/locale, pronunciation/phoneme overrides where supported, speed, pitch, style/emotion/prosody controls, timing/alignment metadata and revision.
Editing the text or voice/prosody regenerates the derived speech render while retaining the same structured SpeechSynthesis source. Speech may be split into utterances/paragraphs/regions and aligned on the timeline without flattening into an anonymous generated waveform.
Where a TTS backend exposes word/phoneme timings, Wave stores them for transcript/alignment editing. Export may render speech to ordinary audio, but the editable Wave project retains the speech component.
Voice cloning/conversion or user-supplied voice references, if supported, require the applicable consent/permission and provenance rules.
Other generated native components
The same native-generation rule applies to generated ambience, transitions and other constructible audio. AI should build Wave-native source/processing structures — for example layered samplers plus filters/envelopes/automation for ambience — instead of defaulting to opaque audio generation.
When a capability fundamentally requires an externally generated waveform that cannot be represented by existing native generators, that waveform MAY enter Wave only as an explicit GeneratedSample source asset with model/provenance metadata. It MUST then be treated like a normal editable sample/clip, and the UI must not pretend it has native sequencer/patch editability. Native structured generation remains the default and required path whenever representable.
Transcription and transcript-based editing
Wave transcription is time-aligned to project/source audio and can include speaker diarisation, word timing, language detection, punctuation and corrections where supported. Clicking/selecting transcript text maps to the corresponding audio range.
Transcript-based edits such as deleting a phrase or removing long pauses translate into ordinary native timeline/clip edits after preview/validation. The transcript is a semantic editing view over canonical audio/timeline state, not a replacement authority for the audio project.
Custom censorship
Wave MUST include Custom Censorship for a selected clip, track, time range or whole-project audio scope. A CensorshipRuleSet can contain exact words, phrases, user-defined terms and one or more semantic themes/categories. Word and phrase rules use time-aligned transcription where available; theme rules MAY use supported model-backed semantic classification. Detection MUST produce reviewable candidate time ranges before application, and the user MUST be able to add, remove or adjust matches to correct false positives and false negatives.
Each rule or rule set can choose a treatment including bleep/tone, mute/silence, attenuation, replacement with a user-selected sound, or timeline removal when explicitly chosen. Controls MUST include pre/post padding and fades, and MAY optionally redact matched text from captions/transcripts. Censorship remains a structured editable operation with stable identity; it can be disabled, reconfigured or undone without altering the original source. Export/render follows the active censorship state.
Audio separation and derived stems
Where configured model/backends support it, Wave can separate mixed audio into structured derived assets/tracks such as vocals, drums, bass, other/music, speakers or foreground/background. Source audio remains preserved and the separated outputs receive stable asset/track identities.
AI separation is a transformation workflow, not an excuse to destroy or replace the source.
Stem splitting MUST be exposed as a first-class Split Stems tool rather than only as an AI command. The UI MUST offer every separation mode supported by the configured backend, including common modes such as Vocals/Instrumental, Vocals/Drums/Bass/Other, speaker separation and Foreground/Background where available. The user can choose which derived stems become tracks, preview/audition the result and re-run separation with a different supported mode without replacing the original mixed source.
Markers, regions and chapters
Wave supports stable MarkerID and RegionID entities for navigation, looping, chapters, editing targets and export metadata. Markers/regions may carry names, colours/categories, notes and references without changing underlying clip data.
Podcast, narration and spoken-word workflows
Podcast/spoken-word workflows reuse normal Wave tracks, transcript, markers, effects and export rather than creating a separate engine. Useful capabilities include multi-speaker editing, chapter markers, silence cleanup, loudness targets, intro/outro, metadata and transcript export.
Present narration and other 9to1 speech assets can open in Wave for detailed editing while retaining canonical timing/asset references.
Motion and Present interoperability
Motion and Wave MUST share compatible audio asset/clip/effect/timeline primitives. A Motion audio track can be opened/edited in Wave and return as the same canonical audio/project reference where supported, avoiding an unnecessary export-edit-import loop.
Present recording/narration can use compatible Wave/shared-media assets and can deep-link to Wave for advanced cleanup/mix/editing without losing timing/reference identity.
Files and storage identity
Files remains authoritative for source/export asset storage identity, create/move/rename/delete/recover/share and provider/cloud hydration. Wave project structures reference stable FileID/asset identities rather than absolute file paths.
Recorded/generated/imported assets receive canonical storage/asset identity. Moving/renaming the underlying File does not detach the Wave reference.
Import and export
Wave import MUST support common maintained codecs/formats through the shared media backend. Source sample rate, channels, timing and metadata are preserved where applicable.
Export can target full project, selected region, track(s), stems, selected clips or selected native generated components. Common outputs include WAV, FLAC, MP3, AAC/M4A and OGG/Opus where supported, plus other maintained codecs.
Export options include sample rate, bit depth where applicable, bitrate/quality, channel layout, loudness/normalisation, metadata and relevant loop/chapter settings. Export never silently mutates the project.
Persistent AI bar
Wave MUST host the global persistent Dulche AI bar. Semantic context includes ProjectID, selected TrackIDs/ClipIDs/GeneratedComponentIDs, selected time/frequency range, playhead, markers/regions, sequencer patterns/notes/instruments, SFX graph selection, speech object/text, effects/automation state, transcript selection, sample rate/timebase and revision.
Write-capable AI changes Wave only through typed Wave/shared-media/NodeGraph actions. It MUST NOT simulate timeline mouse gestures when semantic APIs exist.
Wave API
Wave's first-party API catalogue is exhaustive under the global API-parity rule. Minimum canonical entry points include:
9to1.Wave.CreateProject(config);
9to1.Wave.Open(projectOrFileID);
9to1.Wave.AddTrack(projectID, config);
9to1.Wave.AddClip(projectID, trackID, source, placement);
9to1.Wave.SplitClip(projectID, clipID, time);
9to1.Wave.TrimClip(projectID, clipID, sourceRange);
9to1.Wave.MoveClip(projectID, clipID, placement);
9to1.Wave.SetTrackProperty(projectID, trackID, property, value);
9to1.Wave.AddEffect(projectID, targetID, effect, config?);
9to1.Wave.SetEffectParameter(projectID, effectID, parameter, value);
9to1.Wave.SetAutomation(projectID, targetID, parameter, points);
9to1.Wave.StartRecording(projectID, trackID, config);
9to1.Wave.StopRecording(recordingID);
9to1.Wave.Transcribe(projectID, target, options?);
9to1.Wave.CreateSequence(projectID, config);
9to1.Wave.AddPattern(sequenceID, config);
9to1.Wave.AddNote(patternID, noteEvent);
9to1.Wave.UpdateNote(noteEventID, patch);
9to1.Wave.CreateInstrument(projectID, instrumentType, config);
9to1.Wave.CreateSoundEffectPatch(projectID, config);
9to1.Wave.RenderSoundEffectPatch(patchID, options?);
9to1.Wave.CreateSpeech(projectID, text, voiceConfig);
9to1.Wave.UpdateSpeech(speechID, patch);
9to1.Wave.Cleanup(projectID, target, operation);
9to1.Wave.Enhance(projectID, target, config);
9to1.Wave.Denoise(projectID, target, config);
9to1.Wave.CreateCensorshipRuleSet(projectID, config);
9to1.Wave.UpdateCensorshipRuleSet(ruleSetID, patch);
9to1.Wave.DetectCensorship(projectID, target, ruleSetID, options?);
9to1.Wave.ApplyCensorship(projectID, target, ruleSetID, treatmentConfig);
9to1.Wave.Separate(projectID, target, mode);
9to1.Wave.Export(projectID, target, format, options);
Sound Effect Builder graph mutation uses 9to1.NodeGraph.* where the shared Node Graph Framework is the canonical graph-editing layer. Shared media operations SHOULD use their owning shared API namespace when that is canonical.
Automation and integration events
Automations, Spaces, Dulche and other authorised clients can use the exhaustive Wave action catalogue. Long recording/render/export/transcription/separation jobs expose progress, cancellation, retry/idempotency where relevant and typed result/error state.
Wave emits meaningful typed events such as project/revision changes, recording completed, export completed, render failed and generated component changed; it MUST NOT flood Automations with raw per-sample or pointer events.
Collaboration and revision conflicts
Project/track/clip/pattern/note/patch/speech mutations are revision-aware. Independent object edits MAY merge when safe; incompatible concurrent edits to the same canonical object surface conflict rather than blind last-write-wins.
A running render/export/recording is pinned to the required source/project revision and MUST NOT silently switch to a newly edited revision mid-job.
Accessibility
Wave MUST be keyboard-operable for transport, clip selection/editing, sequencer editing, mixer controls, transcript editing and generated-component workflows. Waveforms/spectrograms provide semantic time/level/frequency information and non-pointer equivalents for required operations.
Meters, clipping, mute/solo, automation and error states MUST NOT rely on colour alone. Screen-reader users can inspect track/clip/note/patch/speech object identity and values.
Performance and realtime safety
The realtime audio path MUST remain isolated from blocking UI, disk, AI/model, network and export work. Playback/recording cannot glitch merely because waveform thumbnails, transcription, generation or export are running.
Waveforms/spectrograms are incrementally analysed/cached; large recordings are streamed/tiled rather than fully decoded into RAM. Realtime-safe effect/instrument processing obeys bounded allocation/latency constraints.
Generated sequencer/SFX/TTS components render incrementally/cached for preview while preserving the structured source. Background renders are cancellable and caches are bounded/revision-invalidated.
Recovery and failure behaviour
Wave durably stores project structure, current recording chunks/takes, native generated-component state and edit history often enough to maximise crash recovery. Restart can recover incomplete recordings/project mutations without replaying committed destructive actions.
Structured failures include ProjectNotFound, TrackNotFound, ClipNotFound, GeneratedComponentNotFound, InvalidTimeRange, InvalidSequence, InvalidPattern, InstrumentUnavailable, InvalidSoundEffectGraph, VoiceUnavailable, RecordingDeviceUnavailable, RecordingFailed, CodecUnsupported, RenderFailed, ExportFailed, RevisionConflict, PermissionDenied and CapabilityUnavailable, plus shared backend/model errors.
A failed AI/native generation/render/export leaves the prior valid project and source assets intact.
Platform capability behaviour
Wave MUST provide useful Windows, 9to1-OS and web editing/creation experiences over the same canonical project model. Input devices, realtime audio APIs, codecs, plugin/instrument backends, MIDI/controller access and local/cloud model capabilities are explicit/queryable.
Web MUST remain a useful multitrack/sequencer/editor experience rather than a static player. Unsupported platform capabilities are disabled with a clear reason while preserving project state for another capable client.
Acceptance requirements
Wave is implementation-complete only when acceptance coverage verifies at least:
* A multitrack project can be recorded, edited, mixed, closed/reopened and exported while preserving stable TrackID/ClipID/revision identity.
* Recording supports device selection, metering, track arm and crash-safe recovery of an interrupted take.
* Waveform clip editing is non-destructive and split/trim/move/fade/crossfade/undo survive restart.
* Mixer/effects/automation are structured and editable, with realtime playback remaining stable during background work.
* Waveform/spectrogram editing and native/audio-model repair operate without destroying the original source.
* Music Sequencer persists editable note/pattern/instrument/arrangement/automation data and can render/export the result.
* An AI music request generates a native sequencer arrangement with editable notes, patterns, instruments and effects instead of an opaque waveform.
* Sound Effects Builder persists an editable native patch graph and an AI request such as a laser sound generates/configures that graph rather than only returning rendered audio.
* Text-to-Speech persists editable text/voice/prosody state and regenerated speech remains linked to the same native SpeechSynthesis component.
* Generated ambience/transitions use native Wave generators/patches/components whenever representable; an unavoidable external GeneratedSample is explicitly identified as a sampled fallback with provenance.
* Transcript-based deletion maps to ordinary timeline edits and remains undoable.
* Separation produces derived stable tracks/assets while preserving the mixed source.
* Enhance Audio and Denoise are available as ordinary manual tools, support preview/bypass or equivalent before commitment, remain undoable/non-destructive and preserve the original source.
* Custom Censorship can detect at least an exact word/phrase rule and a semantic-theme rule, exposes detected ranges for review/correction, applies the selected treatment and remains reversible without altering the source.
* Split Stems is directly accessible from the Wave UI, creates stable derived track/asset identities for a supported separation mode and preserves the original mixed source.
* Motion/Present interoperability uses shared canonical audio primitives rather than export/import duplication.
* The persistent AI bar edits tracks/clips/sequences/patches/speech through typed APIs rather than raw UI simulation.
* Large projects/recordings remain responsive through streaming, incremental analysis and bounded caches.
* Windows, 9to1-OS and web preserve the same project/generated-component model while capability-negotiating audio hardware/backend differences.
* When Home is absent at process start, Wave follows the global Install Home/bootstrap contract rather than launching an incompatible private runtime.
