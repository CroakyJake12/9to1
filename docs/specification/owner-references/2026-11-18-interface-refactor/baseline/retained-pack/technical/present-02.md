# Present — slide authoring and presentation — Present; Purpose, ownership and conformance (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1374 · v2 lines 5805–6155

Test slide editing, notes, media, save/reopen and playback from the exact final build. Missing display hardware or native-platform coverage stays BLOCKED/NOT RUN. A successful editor launch is not playback acceptance, and a playback screenshot is not proof that the deck remains editable.
<!-- UX27:END SECTION-09 -->
Present
Purpose, ownership and conformance
9-1 Present is the CUI presentation authoring, presenting and interactive-runtime application. LibreOffice Impress/Draw is the donor foundation for applicable mature presentation editing, interoperability and quality-of-life behaviour. 9-1 owns the CUI shell, the native .9to1p model, the Actions runtime, typed APIs, AI integration, component model and cross-app semantics.
Applicable donor functionality MUST be preserved unless a scoped donor-parity exception is recorded in the canonical donor-parity manifest. Present MUST NOT ship as a reduced slide editor whose only conformance evidence is file opening or basic text/shape editing.
Present MUST use Home's Shared Productivity Engine for every registered shared object type that it supports. Shared text, tables, charts/graphs, images/media, drawings/ink and other shared objects copied between Present, Write, Canvas, Boards and Spaces MUST preserve the shared object schema, semantic identifiers, formatting, accessibility metadata and clipboard representation.
Release boundary:
* Present owns PresentationID, SlideID, slide order, slide composition, presentation-level state, slide-local state, speaker notes references, presentation sessions and the Actions graph.
* Home owns shared productivity object schemas, permission brokerage, shared AI/runtime services, common clipboard bundles and other platform services defined in Home.
* Present MUST NOT introduce a second private implementation of a Home-owned shared object or platform service.
* The native .9to1p representation is authoritative for full-fidelity 9-1 behaviour.
Normative applicability and capability negotiation
Terms such as applicable, supported and available are not discretionary escape hatches in this section. They are resolved from explicit manifests or runtime capability declarations.
* A donor feature is applicable unless it is classified in the Present donor-parity manifest as Replace with 9-1-native equivalent or Not applicable with a recorded technical/legal reason.
* A platform capability is supported only when the platform capability registry declares the required capability and version for the active device/runtime.
* A file-format construct is representable only when the import/export capability matrix declares a mapping for the source construct and target format/version.
* Failure of a required capability check MUST produce a typed unavailable/unsupported result and the specified fallback or CompatibilityIssue; silent omission is prohibited.
* An implementation MUST NOT reinterpret a MUST requirement as optional merely because its current code path does not implement the required capability.
Landing page, file lifecycle and persistence
Present MUST implement the global launch-screen contract. The launch surface MUST expose Create New, Open/Import, recent presentations and real template/theme discovery. Recent items MUST be derived from persisted history; fabricated recent files are prohibited except for explicitly labelled onboarding or template content.
The release file contract MUST support native .9to1p plus PPTX and ODP import/export. Static output MUST support PDF and image export. Present MUST provide complete create -> edit -> save -> close -> reopen workflows with autosave, crash/interrupted-save recovery, revision tracking and conflict-safe persistence.
* A failed save, partial media write, unresolved revision conflict or recovery failure MUST be surfaced as an explicit error state.
* Present MUST NOT report success until the target revision and all referenced persisted objects required for the save have been committed.
* Recovery MUST preserve the last known-good durable revision and MUST NOT overwrite it with a partially reconstructed presentation.
Canonical data model and identity
All persistent entities MUST use stable opaque IDs. Slide number, slide title, list position, object name and file path are mutable presentation attributes and MUST NOT be used as persistent identity.
Presentation {
  PresentationID: StableID
  SchemaVersion: Version
  RevisionID: RevisionID
  SlideOrder: SlideID[]
  ThemeRef: ThemeID + ThemeRevision
  MasterRefs: MasterID[]
  PresentationVariables: VariableMap
  ComponentTypes: ComponentTypeID[]
  ActionsGraphRef: GraphID + GraphRevision
  Settings: PresentationSettings
  Permissions: PermissionDescriptor
}
Slide {
  SlideID: StableID
  MasterID?: StableID
  LayoutID?: StableID
  ObjectOrder: ObjectID[]
  SlideVariables: VariableMap
  NotesID: StableID
  Hidden: boolean
  ActionsBindings: ActionBinding[]
  RevisionID: RevisionID
}
Reordering, renaming, hiding, duplicating or moving a slide MUST NOT mutate the identity of an existing SlideID. Duplicating a slide MUST allocate a new SlideID and new IDs for duplicated slide-owned objects while preserving references that are explicitly shared by definition.
A reference to a slide or object MUST resolve by stable ID. Position-based references such as 'slide 4' MAY be accepted as user input, but MUST be resolved to a stable ID before persistence or execution.
Required release defaults:
SpeakerWordsPerMinute = 130
IncludeHiddenSlidesInStatistics = true
LiveDrawingMode = OFF
LiveInkPersistence = SESSION_ONLY
IncludeSpeakerNotesInAudienceOutput = false
PersistSessionState = false
Changing a release default MUST create an explicit persisted setting override. Absence of an override means the release default above; implementations MUST NOT infer a different default from platform, file origin or prior session state.
Authoring shell and slide navigation
The editing shell MUST expose a persistent slide navigator, thumbnail view, slide sorter/grid view and searchable presentation outline. The same SlideID MUST back every representation of a slide.
Required slide operations:
* Create, duplicate, delete/recover, reorder, hide/unhide and move slides between presentation sections.
* Jump to slide by outline/search result, stable reference or user-visible position.
* Multi-select slides for reorder, duplicate, delete, export and section assignment where the operation is semantically valid.
* Preserve selection and focus predictably after slide mutations; deleting the active slide MUST move focus to a deterministic adjacent surviving slide.
The authoring shell MUST provide undo/redo, zoom, fit slide, fit selection, rulers, guides, grid, snapping controls and a status surface showing at least current slide/total slide count, zoom and relevant selection/document state. Keyboard-only navigation MUST cover the navigator, canvas, notes pane and inspectors.
Conventional object editing and layout
Present MUST provide mature slide-object editing for text, shapes, lines/connectors, images, shared tables/charts/graphs, drawings, media, code objects, 3D objects, Generative UI objects and supported embedded/shared objects.
Every spatial object type that declares geometric editing MUST support the applicable subset of:
* Create, duplicate, delete, multi-select, group/ungroup and lock/unlock.
* Move, resize, rotate, flip, z-order change, align and distribute.
* Snap to grid/guides/objects and enter exact position, size and rotation values.
* Crop or mask only for object types whose renderer declares crop/mask support.
* Style copy/paste without silently copying object identity, runtime state or permissions.
Shape and text styling MUST persist actual stored values. Inspectors, toolbars and keyboard commands MUST read from and write to the same canonical object properties; a visible control that does not mutate the corresponding stored property is non-conformant.
Present MUST support object naming and a selection/object tree. The tree MUST expose stacking order, locked/hidden state and semantic object identity. Reordering the tree MUST change z-order only when the selected object type participates in z-order.
Themes, masters, layouts and templates
Present MUST support theme colours, theme fonts, backgrounds, reusable style tokens, slide masters, slide layouts, placeholders and reusable templates.
* A slide linked to a layout MUST inherit layout/master properties unless that property has an explicit slide-level override.
* Changing a master or layout MUST update linked slides without deleting valid slide-level overrides.
* Reset Layout MUST remove only slide-level overrides governed by the selected layout; it MUST NOT delete unrelated slide-owned objects.
* PPTX/ODP import MUST preserve representable master/layout relationships instead of flattening every slide to unrelated absolute objects.
Notes-page and handout layout data present in an imported format MUST be retained when the source construct maps to a declared Present capability. If no declared mapping exists, import MUST emit a CompatibilityIssue rather than silently dropping the construct.
Speaker notes and notes workflow
Every slide MUST have first-class speaker notes identified by a stable NotesID. Notes are authoring content associated with a SlideID and are not audience-slide content.
SpeakerNotes {
  NotesID: StableID
  SlideID: StableID
  Content: SharedRichTextDocument
  Locale?: LocaleID
  RevisionID: RevisionID
}
* Notes MUST be editable in a dedicated notes pane and a full Notes view.
* Notes MUST support shared rich-text editing, lists, links, proofing and supported shared embedded objects.
* Notes MUST be searchable and addressable by typed APIs.
* The shared Dictation capability MUST operate in speaker notes.
* Notes MUST appear in Presenter View and notes-page print/export when explicitly selected.
* Normal slideshow output, ordinary audience capture, PDF/image export and generated video MUST exclude speaker notes by default.
* A notes-inclusive export MUST require an explicit notes output mode; it MUST NOT be inferred from the presence of notes.
* PPTX/ODP import/export MUST preserve speaker notes when the destination format supports them. Loss or degradation MUST be listed in the compatibility report.
Word, character and speaking statistics
Present MUST provide live text statistics for presentation content. The statistics engine MUST expose the following explicit scopes: SELECTION, CURRENT_SLIDE, CURRENT_NOTES, ALL_SLIDES, ALL_NOTES and PRESENTATION_COMBINED.
PresentationTextStatistics {
  Scope: StatisticsScope
  WordCount: integer >= 0
  CharacterCountWithSpaces: integer >= 0
  CharacterCountWithoutSpaces: integer >= 0
  SlideTextWordCount?: integer >= 0
  NotesWordCount?: integer >= 0
  EstimatedSpeakingSeconds?: integer >= 0
}
Word boundaries MUST follow the platform Unicode word-segmentation implementation based on Unicode word-boundary rules. Character counts MUST count user-perceived Unicode grapheme clusters rather than UTF-16 code units. CharacterCountWithoutSpaces MUST exclude grapheme clusters classified by the platform Unicode implementation as whitespace.
* Slide-text statistics include authored text visible in text-bearing slide objects and component labels in the selected scope.
* Speaker-note statistics include only speaker-note rich-text content.
* Comments, alt text, object names, Actions node labels, code source, hidden implementation metadata and compatibility metadata MUST NOT be included in ordinary word/character counts.
* ALL_SLIDES MUST include hidden slides by default because they are part of the authored document; the statistics UI MUST provide an explicit Exclude hidden slides filter.
* PRESENTATION_COMBINED MUST expose slide-text and notes counts separately in addition to the combined totals.
Estimated speaking time MUST be calculated from notes WordCount using SpeakerWordsPerMinute. The release default is 130 words/minute. The user MUST be able to change this value without altering the note text. EstimatedSpeakingSeconds = round(WordCount / SpeakerWordsPerMinute * 60).
Counts MUST update after a committed text edit without requiring a save/reopen cycle and MUST be accessible from normal authoring UI without inserting count text into the presentation.
Search, proofing, comments and collaboration
Present MUST provide presentation-wide search and replace across slide text and speaker notes. Every result MUST identify the SlideID, object/notes target and exact text range required to navigate to the match semantically.
* Spell-check/proofing MUST operate on slide text and speaker notes and MUST preserve per-range language metadata.
* Comments and replies MUST be anchorable to a SlideID, ObjectID and, where supported, a text range.
* Realtime collaboration MUST preserve concurrent object, text, notes and comment edits using revision/conflict semantics; naive timestamp last-writer-wins data loss is prohibited.
* Presence indicators and remote selections are session UI state and MUST NOT mutate presentation content.
Actions runtime and state model
Present MUST NOT implement an independent legacy animation engine or an independent transition runtime. All dynamic, timed, interactive and transition behaviour MUST be represented by a canonical typed Actions graph executed by the shared graph runtime.
ActionGraph {
  GraphID: StableID
  GraphRevision: RevisionID
  Nodes: ActionNode[]
  Edges: ActionEdge[]
  Entrypoints: ActionBinding[]
}
PresentationRuntimeState {
  GlobalVariables: VariableMap
  SlideVariables: Map<SlideID, VariableMap>
  ComponentState: Map<ComponentInstanceID, VariableMap>
  SessionState: VariableMap
}
Slides, objects, interactive components, media, code objects, 3D objects and live-drawing surfaces MUST expose versioned typed events. Event payloads MUST be schema-validated before graph execution.
The release event vocabulary MUST include at least:
* PresentationStarted, PresentationEnded, SlideEntered, SlideLeft, NextSlideRequested and PreviousSlideRequested.
* Clicked, DoubleClicked, PointerEntered, PointerLeft, Focused, Blurred and supported key/gesture events.
* TimerElapsed and VariableChanged.
* MediaStarted, MediaPaused, MediaPositionChanged, MediaEnded and MediaError.
* ComponentValueChanged and ComponentSubmitted.
* DrawingChanged and code-result/code-event output.
Actions MUST support conditions, branching, sequencing, parallel branches, delays/timers, reusable subgraphs and typed error paths. Actions MUST address targets by stable semantic IDs and MUST NOT simulate mouse/keyboard input when a typed capability exists.
Graph execution MUST be deterministic for the same graph revision, initial local state and deterministic inputs. When multiple nodes become runnable simultaneously, ordering MUST be defined by explicit dependencies/priority and a stable NodeID tie-breaker. External service results are recorded inputs and do not make the local scheduling order unspecified.
Cycles are permitted only when the runtime execution guard can bound them. Every event cascade MUST have a finite execution budget and loop detection. Exceeding the budget MUST terminate the affected cascade with a typed runtime error rather than freezing the presentation.
Starting a presentation creates a new PresentationSession with a defined initial state. SessionState and component runtime state reset on restart by default. Persistence requires an explicit author-configured persistence binding and the normal permission/write rules.
Transitions builder
The Transitions sidebar MUST remain available as a simple authoring surface, but it is a specialised Actions-graph editor. It MUST NOT invoke a second transition engine.
* Choosing or editing a transition MUST create/update a marked Actions subgraph owned by that transition definition.
* The sidebar MUST expose Open in Actions to reveal the exact generated subgraph.
* Simple-editor changes MUST modify only nodes owned by the transition definition and MUST preserve user-authored extension nodes connected outside that owned region.
* If an advanced graph edit makes the transition no longer representable by the simple sidebar, the sidebar MUST show a Custom/Advanced state rather than deleting or approximating the graph.
A transition completes only when its graph reaches the transition completion node. Slide navigation MUST commit exactly once per NavigationRequestID.
Interactive and reusable components
Present MUST ship first-class accessible Button, Switch, Text Input, Dropdown, Slider and Flashcard component types.
* Components MUST expose typed properties, state, focus/keyboard semantics, accessibility role/name/value and versioned events.
* Built-in hover, focus, pressed and value behaviour MUST work without requiring an author to construct a graph.
* The same primitives MUST be exposed to authors through Actions; built-in components MUST NOT depend on a hidden private animation system.
ComponentType {
  ComponentTypeID: StableID
  DefinitionRevision: RevisionID
  VisualTree: ComponentNode[]
  PropertySchema: Schema
  StateSchema: Schema
  EventSchema: Schema
  ActionSchema: Schema
  DefaultValues: ValueMap
  Variants: Variant[]
  AccessibilitySemantics: AccessibilityDescriptor
  InternalActionsGraph: GraphID
  ExposedProperties: PropertyID[]
  ExposedEvents: EventTypeID[]
}
Users MUST be able to convert selected objects/components into a reusable Component Type. An instance MUST retain ComponentInstanceID, ComponentTypeID, ComponentDefinitionRevision, property overrides and instance state.
* Linked instances MAY receive compatible definition updates only through an explicit update policy.
* Detached/local copies MUST NOT receive future definition updates.
* An incompatible component-definition update MUST NOT silently rewrite or invalidate an existing instance; it MUST require migration or retain the older definition revision.
* Reusable component types MUST be storable in the user's/workspace component library.
Generative UI
Present's Insert surface MUST include Generative UI. The user may insert an existing generated UI artifact or request a new generated interactive surface through Dulche.
GenerativeUIObject {
  GenerativeUIObjectID: StableID
  SchemaRevision: RevisionID
  StateSchema: Schema
  EventSchema: Schema
  ActionsBindings: ActionBinding[]
  Permissions: PermissionDescriptor
}
* Generated UI MUST execute in the approved shared CUI/Generative UI runtime.
* Arbitrary raw HTML/JavaScript embedded directly into the presentation is prohibited.
* Generated UI events MUST be connectable to the presentation Actions graph.
* A generated UI object MAY be saved as a reusable Component Type only when its visual, state, event and action schemas use ComponentType-supported constructs and pass component validation.
* Write, Present, Boards and Canvas MUST use the same canonical Generative UI representation rather than app-specific incompatible formats.
Runnable code, media, 3D and live ink
Runnable code:
* A runnable-code object MUST store source, registered language/runtime ID, declared inputs, typed outputs, permissions and execution limits.
* Execution MUST occur in a sandbox. Filesystem, network, account and device access are denied by default and may be granted only through declared platform capabilities.
* CPU/time, memory and output limits MUST be enforced by the selected runtime provider. Limit violations MUST return typed errors.
* Code results/events MUST be consumable by Actions and MAY update authorised presentation objects through typed APIs.
Audio and video:
* Media objects MUST expose Play, Pause, Stop, Seek, SetVolume and SetPlaybackRate.
* Media events MUST include Started, Paused, PositionChanged, Ended and Error.
* Missing, offline or unsupported media MUST produce an explicit media error state; silent broken placeholders are prohibited.
Native 3D:
* The initial supported interchange formats MUST include glTF and GLB.
* 3D objects MUST support transform, camera/orbit controls, camera presets, lighting/environment, material rendering, hotspots and Actions integration.
* Model-intrinsic animation/timeline playback MUST be controlled through typed 3D Actions; it MUST NOT create a legacy presentation-animation subsystem.
Drawing/live ink:
* Present MUST use the shared Rnote-derived structured drawing/ink model.
* Live drawing modes are OFF, PRESENTER_ONLY and PARTICIPANTS where the collaboration surface supports participant input.
* Live ink persistence modes are SESSION_ONLY, AUTO_SAVE and EXPLICIT_COMMIT.
* DrawingChanged MUST be available to Actions and, when authorised, Dulche.
Slideshow runtime and Presenter View
PresentationSession {
  SessionID: StableID
  PresentationID: StableID
  SourceRevisionID: RevisionID
  ActiveSlideID: SlideID
  RuntimeState: PresentationRuntimeState
  RouteHistory: NavigationRecord[]
  StartedAt: Timestamp
  Status: RUNNING | PAUSED | ENDED | FAILED
}
Present MUST support full-screen and windowed slideshow playback and presenter/audience display selection when multiple display targets are available.
* Presenter View MUST display current slide, next slide, speaker notes, slide navigator, elapsed timer, clock and presentation controls.
* AudienceOutput and PresenterUI are separate render targets. Speaker notes, timers, controls, diagnostics and presenter-only overlays MUST NOT enter AudienceOutput unless the user explicitly selects a presenter-view recording/export mode.
* The runtime MUST support keyboard navigation, slide jumping, hidden-slide rules, black/white blank screen, presenter pointer/laser, permitted live ink, live captions/subtitles where configured and interactive components/Actions.
* Reduced-motion preference MUST be exposed to the Actions runtime. Motion-heavy Actions MUST provide a defined reduced-motion result rather than ignoring the preference.
* Presenter notes MUST support presenter-side text sizing and auto-scroll/teleprompter behaviour without mutating stored note content.
A presentation session MUST execute against a defined SourceRevisionID. If the document changes during a live session, the runtime MUST either remain pinned to SourceRevisionID or apply an explicit safe live-update policy; it MUST NOT mix arbitrary object revisions silently.
Rehearsal, timings and recording
Present MUST support rehearsal of timings and structured session recording.
* Rehearsal MUST record navigation and Action-path timing without converting the authored presentation into a fixed linear deck.
* Recording MAY capture narration, camera, pointer/ink and the actual executed Action path subject to device permissions.
* A recorded session MUST retain RouteHistory and timing data sufficient to replay or export the recorded route.
* Replacing an existing recording MUST be reviewable and undoable; destructive replacement without a recoverable prior revision is prohibited.
AI-aware authoring and live presentation
The persistent AI bar MUST receive permission-filtered semantic context for all authorised slides, off-screen objects, speaker notes, Actions graphs, component definitions/state, media metadata, runnable code metadata, 3D objects, drawings and current presentation-session state. Semantic context MUST be preferred over screenshot/OCR interpretation.
* Read-only AI mode MAY inspect, explain, summarise, rehearse and propose changes, but MUST NOT mutate the presentation or live runtime state.
* Write AI mode MAY create/edit slides, notes, objects, components and Actions only through typed Present APIs and the normal permission, revision, undo and conflict semantics.
* Requests such as branching on an audience answer, converting objects into a flashcard component or highlighting diagram regions on click MUST produce canonical typed graph/component mutations rather than UI automation.
* AI-generated edits MUST be attributable to a revision/audit event and MUST be reversible through the normal revision/undo model.
Video, audio, podcast, print and static export
Present MUST generate Video, Audio and Podcast outputs from the canonical presentation model rather than from a detached textual summary.
* Video rendering MUST execute the actual presentation runtime and timed Actions for a selected route.
* Audio output MUST use presentation content and speaker/narration configuration without rendering slide visuals.
* Podcast output MUST transform presentation content into a conversational treatment using selected Dulche Voice models/voices.
* Generated narration/script and timeline MUST be previewable and editable before final rendering.
An interactive presentation may have multiple valid routes. Export MUST require one of RECORDED_SESSION, DEFAULT_ACTION_PATH or EXPLICIT_BRANCH_PATH. Present MUST NOT guess arbitrary button/quiz choices.
Static output MUST support full slides, selected slides, notes pages, outline text and configurable handout layouts with multiple slides per page where the target supports them.
* PDF/image export MUST expose slide range, hidden-slide inclusion, background/transparency handling and notes inclusion.
* Speaker notes are excluded from audience-facing static output by default.
* Page size, orientation, margins and scaling MUST be explicit and previewable for print/handout output.
Import/export compatibility contract
Import/export MUST produce an explicit CompatibilityReport whenever a source or destination format cannot represent the complete native model.
CompatibilityIssue {
  IssueID: StableID
  Severity: INFO | WARNING | ERROR
  Direction: IMPORT | EXPORT
  FeatureType: string
  SourceTargetID?: StableID
  Description: string
  Resolution: TRANSLATED | DEGRADED | OMITTED | BLOCKED
}
* Compatibility severity semantics:
* INFO means the construct is translated without user-visible semantic loss; export/import may continue without acknowledgement.
* WARNING means the construct is degraded or omitted but the operation can still produce a usable artifact; the issue MUST identify the exact affected source/target and resulting behaviour.
* ERROR means continuing would violate the selected export/import policy, produce an invalid artifact or cause unaccepted destructive loss; the operation MUST block until the user changes the policy, removes the blocking feature or chooses a supported target.
* TRANSLATED means the destination preserves the source semantics; DEGRADED means semantics remain usable but differ; OMITTED means the construct is absent from the result; BLOCKED means no result is committed.
* PPTX/ODP legacy transitions and animations MUST be translated into Actions graphs when representable.
* Present MUST NOT retain a hidden executable legacy animation engine solely for compatibility.
* Unsupported or partially translated import behaviour MUST be listed in the CompatibilityReport.
* PPTX/ODP export MUST compile the representable Actions subset into destination-native constructs.
* Custom components, arbitrary stateful branching, runnable code, Generative UI, AI behaviours and richer interactive 3D logic that cannot be represented MUST be reported explicitly; silent disappearance is prohibited.
* Import/export validation MUST cover slides, masters/layouts, text, speaker notes, media, supported charts/tables, links, accessibility metadata and representable Actions.
.9to1p is the authoritative full-fidelity format. A successful destination file creation is not sufficient evidence of compatibility.
Accessibility
Present MUST expose accessible semantics in both authoring and presentation runtimes.
* Relevant visual objects MUST support alt text/description.
* Slides MUST have an explicit reading order; interactive content MUST have an explicit focus order when reading order is insufficient.
* Interactive components MUST expose accessible role, name, state/value and keyboard interaction.
* Authoring and presenting MUST be operable without a pointer for required workflows.
* Focus MUST be visible; required state MUST NOT be communicated only by colour.
* Zoom/text scaling and reduced-motion behaviour MUST be supported.
* An accessibility checker MUST identify at least missing required alt text, problematic reading/focus order and inaccessible interactive controls.
* Accessibility metadata MUST round-trip through .9to1p and MUST be preserved in PPTX/ODP when the destination construct can represent it.
Security, permissions and external side effects
Present Actions, generated UI, runnable code, media, participant input and AI integrations MUST obey the platform permission broker and risk classification. A presentation MUST NOT gain arbitrary filesystem, network, account or device control merely because it is opened or presented.
* Viewer interaction MUST NOT grant permissions that the presenting user/session does not already possess.
* External side effects invoked from a slideshow MUST use the same confirmation, idempotency, audit and typed-error rules as the equivalent app/Automation action.
* A denied permission MUST produce an explicit denied/error result that Actions can handle; the runtime MUST NOT silently substitute a weaker or different side effect.
* Presentation content from untrusted sources MUST NOT be able to modify Home permissions or disable sandbox restrictions.
Typed Present API and mutation semantics
Present MUST expose versioned typed APIs sufficient for Spaces, Automations, Dulche and other authorised apps to inspect and mutate presentation state without UI simulation.
MutationRequest {
  BaseRevisionID: RevisionID
  OperationID: StableID
  ActorContext: PermissionFilteredActorContext
}
MutationResult {
  RevisionID: RevisionID
  ChangedIDs: StableID[]
  Warnings: Warning[]
  CompatibilityIssues: CompatibilityIssue[]
}
Mutation operations MUST reject a stale incompatible BaseRevisionID with REVISION_CONFLICT rather than silently overwriting newer edits. OperationID MUST support idempotent retry for operations declared retry-safe.
Required public capability groups:
* Presentation/slide/object queries, including selection-safe semantic search.
* CreateSlide, DuplicateSlide, MoveSlide, DeleteSlide and restore/recovery operations.
* InsertObject, UpdateObject, DeleteObject and shared-object operations.
* GetSpeakerNotes and SetSpeakerNotes.
* GetStatistics with an explicit StatisticsScope.
* Theme/master/layout read and mutation operations.
* Actions.GetGraph, Actions.ValidateGraph and Actions.ApplyPatch.
* Components.CreateType, Components.UpdateType and component-instance operations.
* Media, 3D and drawing query/control operations.
* StartPresentation, GetPresentationSession, NavigatePresentation, PausePresentation, ResumePresentation and EndPresentation.
Typed errors MUST distinguish at least NOT_FOUND, INVALID_ARGUMENT, PERMISSION_DENIED, REVISION_CONFLICT, UNSUPPORTED_FEATURE, RUNTIME_ERROR and CANCELLED. APIs MUST NOT collapse these states into a generic success=false result with no machine-readable reason.
Release acceptance criteria
Release conformance requires behaviour evidence. Source presence, UI screenshots or a successful build are insufficient on their own.
The release validation suite MUST demonstrate, at minimum:
* Create -> edit -> save -> close -> reopen with slide/object identity, masters/layouts/themes and speaker notes preserved.
* Live word/character statistics for selection, slide, notes and full-presentation scopes, including separate slide/notes totals and speaking-time calculation.
* Mature conventional slide editing: multi-select, group, align/distribute, exact geometry, z-order, lock, crop/mask where applicable, guides/grid/snapping and object-tree operations.
* A non-trivial Actions graph containing an event, condition, parallel/sequential work, state mutation and navigation.
* A Transitions-sidebar edit that produces an Actions subgraph and survives Open in Actions plus a compatible round-trip back to the simple editor.
* All required built-in interactive components and one reusable custom Component Type with revisioned linked instances.
* A Generative UI object connected to Actions without raw HTML/JavaScript injection.
* One sandboxed runnable-code example that emits a typed result and one denied-capability test.
* Audio/video playback through typed controls and media events.
* A supported glTF/GLB model with camera/hotspot Actions.
* Structured live ink in at least SESSION_ONLY and EXPLICIT_COMMIT modes.
* Presenter View with current/next slide, notes and timer while AudienceOutput remains free of presenter-only state.
* Rehearsal/recording with recoverable RouteHistory.
* PPTX/ODP import containing legacy transitions/animations with translated or explicitly reported results.
* PPTX/ODP export with a CompatibilityReport for any non-representable native feature.
* PDF/handout/notes-page export with explicit slide range and notes policy.
* Keyboard-only authoring/presenting and accessibility-checker coverage.
* AI Read-only versus Write enforcement and typed mutation auditability.
* Crash/interrupted-save recovery and concurrent-edit/revision-conflict behaviour.
* Cross-app shared-object clipboard fidelity for Present <-> Write, Present <-> Canvas and Present <-> Boards, using at least one shared text object and one non-text shared object supported by each tested endpoint.
Any acceptance case that fails by silently omitting data, silently degrading behaviour, bypassing typed APIs or exposing presenter-only/private state to the audience MUST block release conformance.
