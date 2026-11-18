# Canvas — drawing, objects and semantic composition — Canvas; Purpose, ownership and boundaries (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1391 · v2 lines 6188–6552

Use culling/streaming for large drawings, retain recovery data and handle failed writes without overwriting the valid artifact. Reopen the native file and independently inspect representative object properties as well as its preview.
<!-- UX27:END SECTION-10 -->
Canvas
Purpose, ownership and boundaries
9-1 Canvas is the handwriting, sketching, drawing and freeform spatial-authoring application in 9to1. Canvas owns Canvas artifacts, spatial composition, page/infinite-canvas geometry, drawing/ink editing, Canvas tool state, Canvas-specific view state and the Canvas domain API.
Canvas is not a Boards replacement and MUST NOT become a generic workspace/page system. Boards owns structured mixed-content workspaces and pages; Canvas owns freeform spatial drawing documents. Boards MAY host a live Canvas artifact, but the embedded Canvas remains owned by Canvas and retains its Canvas ArtifactID.
Canvas MUST use CUI for the visible application shell, menus, toolbars, inspectors, dialogs, launch screen and ordinary controls. A specialised accelerated drawing/rendering surface MAY exist as a CUI control for performance; .cui markup MUST NOT require each stroke sample to become an independent generic UI widget.
Donor and upstream contract
Rnote is the authoritative donor for mature handwriting, drawing, stroke, selection, rendering and applicable import/export behaviour. The implementation agent MUST obtain the latest appropriate stable Rnote source when the Canvas implementation pass begins, record the exact upstream repository, commit/release identifier and licence, and include the selected donor source in the Canvas source tree/build graph in accordance with the global Source & Donor Apps contract.
Canvas MUST maintain a donor-parity manifest generated from inspection of the selected Rnote revision. Every user-visible donor capability MUST be classified as PRESERVE, REPLACE_WITH_9TO1_NATIVE_EQUIVALENT or NOT_APPLICABLE. An unclassified donor feature is a release blocker, not permission to omit it.
* PRESERVE means the donor behaviour, shortcuts, input semantics, quality-of-life behaviour and applicable data behaviour remain available through the 9to1 implementation.
* REPLACE_WITH_9TO1_NATIVE_EQUIVALENT is used when 9to1 already owns the equivalent platform capability, including CUI shell/UI, Dulche AI, Home permission handling, Files storage integration or other shared 9to1 services.
* NOT_APPLICABLE requires a concrete technical, legal or product-boundary reason and validation that its omission does not remove expected Canvas functionality.
Proprietary donor AI MUST NOT be copied. Any applicable AI-assisted behaviour is implemented through Dulche and the Canvas typed API.
Supported platforms and capability negotiation
Canvas MUST ship on every 9to1 platform for which the app is not explicitly exempt under the global platform/web rules. Desktop/native and web implementations MUST share the same Canvas artifact schema, stable identities, domain API semantics and server-side canonical entities.
Platform-specific input or rendering capability differences MUST be negotiated explicitly. Hardware capability absence MUST NOT be misrepresented as format incapability.
* If pressure is unavailable, Canvas records and renders strokes using the declared non-pressure fallback without deleting pressure data already stored in the artifact.
* If tilt, barrel buttons, palm rejection assistance or another device capability is unavailable, the UI MUST disable or omit only the dependent control and expose a capability-unavailable reason where the absence is user-relevant.
* Opening an artifact on a lower-capability platform MUST preserve unsupported stored data for later use on a capable platform.
Canonical artifact model and identity
The native full-fidelity Canvas format is .9to1c. Persisted Canvas data MUST be schema-versioned. Readers MUST migrate, preserve or reject incompatible schema versions explicitly and MUST NOT guess the meaning of unknown fields.
CanvasArtifact {
  ArtifactID: StableID
  SchemaVersion: Version
  RevisionID: RevisionID
  DisplayName: string
  CanvasMode: INFINITE | PAGED
  PageOrder: PageID[]
  SharedResources: ResourceRef[]
  CollaborationMetadata: CollaborationState
  DocumentSettings: CanvasDocumentSettings
}
CanvasPage {
  PageID: StableID
  Bounds: InfiniteBounds | PageBounds
  Background: BackgroundDefinition
  LayerOrder: LayerID[]
  ObjectOrder: ObjectID[]
  RevisionID: RevisionID
}
CanvasObject {
  ObjectID: StableID
  ObjectTypeID: TypeID
  LayerID: LayerID
  Geometry: Geometry
  Transform: Transform
  Style: ObjectStyle
  Accessibility: AccessibilityMetadata
  SharedObjectRef?: SharedObjectReference
  RevisionID: RevisionID
}
InkStroke {
  StrokeID: StableID
  ToolDefinitionID: ToolDefinitionID
  Samples: StrokeSample[]
  ResolvedBrushProperties: BrushProperties
  Transform: Transform
  RevisionID: RevisionID
}
ArtifactID, PageID, LayerID, ObjectID and StrokeID MUST remain stable across rename, move, reorder, zoom, pan, style change and save/reopen. Display names, page positions, z-order and file paths MUST NOT be used as persistent identity.
Document mode and page-type model
Canvas MUST support both INFINITE and PAGED document modes as first-class modes selectable through the top-right quick-settings surface.
* INFINITE mode provides a spatially unbounded working area for normal user workflows and MUST NOT expose an artificial page edge as document structure.
* PAGED mode contains ordered PageID entities with explicit page size, orientation where applicable and page/background settings.
* Page size, orientation, background and page ordering are document state and therefore collaborate, persist and travel with the artifact.
Changing between INFINITE and PAGED is a structural conversion, not a cosmetic toggle. The conversion workflow MUST preview the mapping of existing content, identify content that would cross/clash with generated page boundaries, and require explicit confirmation before committing a conversion that changes layout.
Conversion MUST be undoable/recoverable through document history and MUST NOT clip, discard or rasterise existing objects silently.
Persistence, autosave and recovery
Canvas persistence MUST be atomic at the smallest user-visible document mutation/revision boundary supported by the storage architecture. A partially written resource MUST NOT make the last known-good artifact revision unreadable.
* Autosave MUST persist committed document mutations without requiring a manual Save command.
* A save failure MUST surface an explicit unsaved/error state and retain recoverable local work.
* Crash recovery MUST restore the last durable revision and recover newer journaled/temporary operations where available.
* Imported source files MUST NOT be destructively overwritten merely because they were opened. Overwrite/export requires an explicit user operation appropriate to the source format.
* Closing an artifact while durable persistence is unresolved MUST provide an explicit recovery choice rather than silently discarding changes.
Launch screen and document lifecycle
Canvas MUST implement the global launch-screen contract and LAUNCH-001–006 using real Files/storage state. Required launch capabilities are Create Blank, templates/starter canvases where supported, Recent, Favourites/Pinned, Shared with Me, search, Open and Import. The launch composition and drawing workspace are distinct active views, not a welcome strip above a live canvas. Direct artifact opening, return to start and session restoration preserve the specified Canvas lifecycle, ArtifactID and view/tool state; intentional Canvas embedding in other apps remains supported.
Create Blank MUST create a real Canvas artifact with a new ArtifactID and selected/default CanvasMode. Launch entries MUST NOT be mock cards.
Opening, renaming, moving, favouriting, sharing and deleting an entire Canvas artifact MUST integrate with Files' canonical artifact/storage semantics. Moving or renaming a file MUST NOT change ArtifactID.
Primary Canvas shell
The normal editing surface MUST prioritise the canvas and MUST NOT permanently reserve a ribbon, large left tool rail or large inspector panel for routine drawing.
Top-left document controls:
* A compact ≡ pill opens the Canvas/application command surface for document/application commands assigned to it by the final CUI command model.
* A neighbouring document-name pill displays the current DisplayName, for example Notebook 3, and provides document-level operations including rename, location/document information and applicable document collaboration/navigation commands.
* Changing DisplayName MUST NOT change ArtifactID.
Bottom primary toolbar:
The canonical default order is Select, Pan, Pen, Eraser, Insert, Tools, AI. The toolbar is a floating horizontal CUI surface centred near the bottom of the primary canvas.
| Select | Pan | Pen | Eraser | Insert | Tools | AI |
* The currently selected tool MUST display both its icon and human-readable name.
* Non-selected tools MAY display icon-only in the compact state, but their names and primary shortcut MUST be exposed by hover and keyboard-focus tooltips.
* The toolbar MUST remain usable with mouse, touch, stylus and keyboard navigation.
* Responsive compaction MAY use controlled overflow or horizontal scrolling; tools MUST NOT silently disappear.
The active editing tool is document-session/editor state, not persisted canonical document content unless a specific tool setting is declared as a user preference.
Contextual tool-options host
When the active tool exposes contextual options, Canvas MUST render one secondary floating options surface immediately above the primary bottom toolbar. Tool implementations MUST declare their options through a common option schema rather than constructing unrelated private popup systems.
CanvasToolDefinition {
  ToolID: StableToolID
  DisplayName: string
  Icon: IconRef
  Shortcut?: Shortcut
  InputModes: InputMode[]
  Cursor: CursorDefinition
  OptionSchema?: ToolOptionSchema
  CapabilityRequirements: CapabilityID[]
  DomainActionMappings: ActionID[]
}
Selecting a tool activates it. Re-selecting the active tool MUST open/focus its deeper option surface when that tool has options. A long-press or context menu MAY expose secondary actions, but no required capability may exist only behind long-press.
Select and Pan
Select is the canonical object/stroke manipulation tool. It MUST support the applicable Rnote-derived selection modes plus shared-object selection semantics, including single selection, multi-selection and lasso/area selection where supported by the donor/object type.
* Applicable selected content MUST support move, resize, rotate, duplicate, delete/recover, cut/copy/paste, group/ungroup, z-order/layer movement, alignment/distribution where meaningful and lock/unlock.
* An operation unsupported by a selected object type MUST be disabled with a defined capability reason rather than appearing to succeed.
Pan is an explicit navigation tool. Pointer/stylus movement while Pan is active MUST navigate the viewport and MUST NOT create or modify drawing content.
Pen system and preset pens
Pen is the primary structured ink-authoring tool and MUST preserve applicable Rnote pen/brush behaviour. Pen options appear in the contextual tool-options surface above the bottom toolbar.
Canvas MUST ship with useful preset pens derived from the available drawing engines in the selected donor revision. Presets MUST cover the major donor-supported writing/drawing instrument classes that remain applicable, such as ordinary ink/ballpoint-style writing, fountain/variable-width ink, pencil and highlighter/marker behaviour where the donor supports them.
PenPreset {
  PenPresetID: StableID
  Name: string
  ToolEngineID: ToolEngineID
  TipOrNib: NibDefinition
  BaseWidth: Length
  PressureCurve?: Curve
  TiltResponse?: TiltDefinition
  Opacity: number
  Colour: Colour
  Smoothing: SmoothingDefinition
  Stabilisation: StabilisationDefinition
  EngineParameters: Map<string, TypedValue>
  Favourite: boolean
  RevisionID: RevisionID
}
The Pen menu MUST provide a Custom Pen workflow. Users MUST be able to create, preview, save, duplicate, rename, reorder, favourite, edit and delete/recover custom pen presets.
* The custom-pen editor MUST expose only properties supported by the selected ToolEngineID.
* Saving a pen preset MUST NOT retroactively alter existing strokes unexpectedly. Existing strokes retain resolved brush properties or an explicit versioned preset reference sufficient to reproduce their authored appearance.
* Preset and custom pen selection MUST be keyboard and touch accessible.
Eraser
The Eraser tool MUST expose at least two user-facing modes in its contextual options:
* Natural Eraser — drag over drawing content to erase according to the configured donor-supported erasure semantics, including partial/stroke behaviour where provided.
* Quick Eraser — click/tap a target stroke or object to remove the complete resolved target immediately.
Quick Eraser deletion MUST use the same document history/domain delete operation as other Canvas deletion and therefore MUST remain undoable/recoverable. The target-resolution rule MUST be deterministic when objects overlap.
Insert catalogue and parity
Insert is the structured object insertion catalogue. Its first-level menu MUST use the following category order and each category opens a submenu to the right on desktop:
Text   >
Shapes >
Media  >
Table  >
Other  >
The first-level menu is category-only. Selecting/hovering a category MUST NOT insert a default object silently.
Text submenu:
* Text Box — freely positioned shared rich-text object.
* Heading — text object with a semantic heading role and heading defaults; it MUST NOT be implemented only as visually larger text.
* Paragraph — text object with a semantic paragraph/body role and paragraph defaults.
Shapes submenu:
* Opens the registered shared shape/line catalogue supported by Canvas, including applicable lines/connectors and shape families exposed by the Shared Productivity Engine.
* Shapes MUST use the same canonical shared shape definitions and editing behaviour used by other 9to1 productivity surfaces rather than a reduced Canvas-only shape model.
Media submenu:
* Image.
* Audio.
* Video.
* Media objects MUST use the corresponding shared media representation and preserve editable metadata/behaviour rather than flattening to a thumbnail.
Table submenu:
* Canvas MUST expose the shared table creation experience and the full Canvas-applicable shared table feature set.
* Canvas MUST NOT implement a reduced Canvas-only table type. Table editing capabilities, structure and formatting come from the shared table engine; Canvas owns freeform placement/transform around the table.
Other submenu:
* Other contains registered Canvas-compatible shared object types that do not warrant their own first-level category.
* Graph MUST be available through Other using the canonical shared graph representation.
* Other MAY include Link, File/Artifact, Embed, Equation, Code, Generative UI and additional registered shared types when the shared capability registry declares them Canvas-compatible.
* Drawing MUST NOT appear as a separate Insert item because freeform pen/drawing is already a primary Canvas capability.
Insert catalogue parity has two mandatory dimensions:
* Catalogue parity — every shared productivity object type declared Canvas-compatible by the shared capability registry MUST be reachable through Insert.
* Behaviour parity — after insertion, the object MUST retain the shared object's complete applicable behaviour. Canvas MUST NOT replace a shared object with a feature-reduced Canvas-specific approximation.
InsertCategory {
  CategoryID: StableID
  DisplayName: string
  Icon: IconRef
  Order: integer
  Entries: InsertEntryID[]
}
InsertEntry {
  InsertEntryID: StableID
  ObjectTypeID: TypeID
  DisplayName: string
  Icon: IconRef
  CapabilityRequirements: CapabilityID[]
  CreateActionID: ActionID
}
The Insert UI MUST be generated from registered insertion capabilities plus the fixed category taxonomy above. Adding a registered shared object MUST NOT require a separate incompatible Canvas object implementation.
Desktop cascading menus MUST use conventional keyboard behaviour: Up/Down move, Right opens a submenu, Left returns, Enter/Space invokes and Escape closes the current menu level. Touch interaction MUST provide an explicit navigable submenu/back model and MUST NOT depend on hover.
Tools family
Tools is the home for specialised Canvas instruments that do not warrant permanent top-level toolbar slots. The exact list MUST be derived from the selected Rnote donor parity manifest and shared Canvas capabilities rather than an arbitrary hand-written subset.
Applicable geometry, line/arrow, ruler/measurement, fill/colour-picker, specialised selection and other donor tools MAY be organised here while retaining their donor behaviour. A tool moved into Tools is not optional merely because it is not a top-level button.
A future pin/favourite mechanism MAY allow users to promote specialised tools to the primary toolbar, but the default canonical toolbar remains Select, Pan, Pen, Eraser, Insert, Tools, AI.
Top-right quick-settings pill
A compact switch/settings pill MUST appear at the top-right of the Canvas editing surface. Activating it opens a dropdown for high-frequency document-level and editor-level settings. It is not a replacement for the complete Settings application.
Document-scoped quick settings MUST include:
* Canvas/page type: INFINITE or PAGED.
* Page size and orientation when PAGED.
* Background colour/design, including registered ruled/grid/dot or other supported background patterns and their applicable spacing/design parameters.
* Other document settings explicitly registered as quick-adjustable by Canvas.
Editor/user-scoped quick settings MUST include:
* Smart Pen.
* Snapping and guide/grid visibility where applicable.
* Stylus/touch interaction preferences and exposed palm-rejection/gesture settings supported by the active platform.
* Other registered non-document editor preferences appropriate to quick adjustment.
Document-scoped settings MUST persist with/collaborate through the artifact. Editor/user-scoped settings MUST persist as user/device preferences unless an individual setting explicitly declares document scope.
Smart Pen
Smart Pen is an editor setting that modifies new handwriting behaviour while enabled. Its purpose is to make freehand handwriting feel more controlled/snappy and visually neater without converting it into typed text.
* Smart Pen MUST preserve handwriting as structured editable ink.
* Smart Pen MUST NOT silently replace ink with text or discard original authored stroke semantics.
* The adjustment algorithm MAY smooth geometry, regularise locally unstable stroke motion and make other constrained handwriting clean-up changes that preserve the user's intended writing rather than redrawing arbitrary content.
* The resulting adjustment MUST be immediately undoable. The active undo/history window MUST retain enough pre-adjustment data to restore the user's original committed stroke.
* Smart Pen state is an editor/user preference unless an explicit future document-scoped mode is specified.
Input, stylus and gesture contract
Input quality is a core Canvas requirement. Canvas MUST preserve applicable high-fidelity stylus information provided by the runtime, including pressure and tilt when available.
* Pen/stylus drawing, touch navigation and mouse/trackpad interaction MUST be distinguishable so ordinary navigation does not create unintended ink.
* Applicable eraser/barrel-button semantics from the selected donor must remain available.
* Palm rejection MUST use platform/donor capabilities where available and MUST fail predictably when hardware/runtime support is absent.
* Pan, zoom and rotate gestures MUST NOT mutate document content unless the active tool/action explicitly maps the gesture to a content mutation.
* Stored pressure/tilt data MUST survive save/reopen and lower-capability viewing even if the current device cannot generate equivalent samples.
Layers, ordering and object manipulation
Canvas MUST preserve applicable Rnote layer functionality. Layers use stable LayerID identities and are document state.
* Users MUST be able to create, rename, reorder, hide/show, lock/unlock and delete/recover layers where supported by the donor model.
* Moving an object between layers changes LayerID membership without changing ObjectID.
* Hidden/locked layers MUST obey the same selection/edit restrictions through UI and API.
Z-order within a layer and layer order are distinct concepts and MUST NOT be conflated by APIs or UI.
Shared Productivity Engine and cross-app object semantics
Canvas MUST use Home's Shared Productivity Engine for every shared object type it supports. Canvas owns spatial placement; the shared engine owns the shared object's semantic content/model.
* Shared rich text, shapes, tables, media, graphs, equations, code, Generative UI and other registered shared objects MUST retain their shared schema when copied, embedded or referenced across supporting 9to1 apps.
* The shared Rnote-derived ink model MUST allow structured ink to remain editable ink in Boards, Present, Write or Spaces where the destination declares ink support.
* Unsupported destination/object combinations MUST fail explicitly or require an explicit user-selected flatten/export operation; silent rasterisation or semantic loss is prohibited.
Clipboard and drag/drop MUST carry a typed multi-representation payload containing stable/source references plus portable shared-object representations as applicable. The destination chooses the highest-fidelity compatible representation; it MUST NOT silently choose a lower-fidelity bitmap when a supported structured representation exists.
Files and Boards integration
Files is authoritative for storage/navigation semantics of Canvas artifacts. Canvas is authoritative for Canvas document semantics.
Boards MUST be able to host a live Canvas artifact/reference. A live Board embed references the same ArtifactID and canonical revision stream rather than creating an unmanaged duplicate.
* Editing an authorised live Canvas embed in Boards edits the canonical Canvas artifact through Canvas domain operations.
* Opening the same ArtifactID in Canvas and Boards MUST resolve to the same underlying artifact.
* A snapshot/static copy is permitted only through an explicit user operation that clearly identifies the result as a copy/export rather than a live embed.
Dulche contextual AI
The bottom-toolbar AI item is the sole normal Canvas entry point for expanding the shared Dulche contextual AI bar. Canvas MUST NOT show a second permanently expanded AI bar while the bottom toolbar is present.
When AI is selected, the shared AI bar floats immediately above the bottom toolbar and the AI toolbar item becomes the selected tool. The bar contains the platform-standard AI access-mode controls, including Read-only and Write modes.
Selecting a different Canvas tool hides the expanded AI bar but MUST NOT automatically cancel an authorised active AI session. AI surface visibility and AI session lifecycle are distinct state.
CanvasAIState {
  SessionID?: StableID
  Lifecycle: NONE | IDLE | ACTIVE | WAITING_FOR_USER | COMPLETED | FAILED
  SurfaceVisibility: HIDDEN | EXPANDED
  IsAIToolSelected: boolean
}
* IsAIToolSelected = true requires SurfaceVisibility = EXPANDED.
* If SurfaceVisibility = HIDDEN and Lifecycle is ACTIVE or WAITING_FOR_USER, Canvas MUST show a compact sparkle session indicator immediately beside the top-right quick-settings pill.
* Clicking the top-right sparkle indicator selects AI, restores the existing SessionID and expands the AI bar above the bottom toolbar. It MUST NOT create a duplicate session.
* If a hidden session completes or fails, the indicator MUST enter a non-intrusive completed/error attention state until inspected/dismissed; it MUST NOT force the AI bar open or steal editing focus.
Dulche MUST receive permission-filtered semantic Canvas context including the active ArtifactID/PageID, stable object/stroke IDs, spatial relationships, text/shared-object semantics, current selection, document revision, current Canvas tool state where relevant and the Canvas actions allowed by the current AI access mode.
* Read-only mode MAY inspect, search, reason, explain, navigate and propose changes but MUST NOT mutate the artifact or editor state.
* Write mode MAY mutate Canvas only through typed Canvas/shared-object actions and the normal revision, permission, undo and conflict model.
* A user reference such as 'these four boxes' MUST resolve to stable target IDs at request/execution binding time. Later selection changes MUST NOT silently retarget the running operation.
* If targeted objects change incompatibly before an AI commit, the operation MUST return a revision/conflict result rather than blindly overwriting newer edits.
Handwriting recognition, OCR or AI-generated descriptions are derived metadata unless the user explicitly invokes a conversion that creates a new typed/text representation. Derived recognition MUST NOT silently replace canonical ink.
Spaces and Automations integration
Spaces MUST be able to open/reference an authorised Canvas artifact and edit it conversationally using 9to1.Canvas typed actions. Spaces MUST NOT use mouse/keyboard simulation when the equivalent Canvas action exists.
Automations MUST receive exhaustive Canvas UI/API parity subject to the same permissions and platform capabilities. Canvas MUST publish typed events for meaningful document changes and MUST NOT flood workflows with raw pointer-move events.
Minimum automatable event families:
* Artifact opened/closed/saved/recovered.
* Page created/deleted/reordered and document mode changed.
* Object/stroke created/changed/deleted.
* Layer created/changed/deleted/reordered.
* Shared object inserted/changed/removed.
* Collaboration/revision conflict and recovery state changes that are meaningful for automation.
Collaboration and conflict semantics
Canvas collaboration MUST operate on canonical structured document entities rather than exchanging flattened screenshots or whole-document last-write-wins blobs.
* Concurrent edits to independent strokes/objects/pages SHOULD merge without destroying unrelated work.
* Conflicting edits to the same canonical entity MUST be detected using revision/operation semantics and resolved deterministically or surfaced as a conflict.
* Naive timestamp last-write-wins MUST NOT be the default conflict strategy.
* Remote cursors, selections and transient viewport positions are collaboration presence/session state and MUST NOT alter canonical document content.
The implementation MAY use CRDTs, operation logs or another maintainable internal algorithm provided the observable collaboration, revision and recovery contract is satisfied.
Undo, redo and history
All ordinary Canvas document mutations initiated by UI, AI, Spaces or Automation MUST enter the same canonical history/revision system unless an operation explicitly declares non-document session state.
* Undo/redo MUST operate on semantic user-visible operations, not arbitrary internal rendering steps.
* AI/API mutations MUST be attributable in history/audit metadata.
* Undoing deletion restores the same stable object/stroke identity where restoration semantics permit it.
* Structural mode conversion, bulk manipulation and imported-content insertion MUST be represented as coherent reversible operations or explicit multi-operation transactions.
Import and export
The native .9to1c format is authoritative for full-fidelity Canvas behaviour. Canvas MUST retain all applicable Rnote import/export capabilities from the selected donor revision in addition to 9to1-native integrations.
Import/export MUST preserve supported structured content and MUST produce an explicit compatibility report when the source or destination cannot represent part of the native Canvas model.
* Unsupported features MUST be classified as translated, degraded, omitted or blocked with the affected entity identified.
* Silent loss of layers, stroke properties, page structure, shared objects, accessibility metadata or other canonical content is prohibited.
* A successful file creation/open is not sufficient validation; representative round trips MUST compare semantic content.
Accessibility
Canvas MUST make ordinary authoring and document-management workflows operable by keyboard and assistive technology even though the primary document is spatial/visual.
* All toolbar, menu, submenu, quick-settings, document-pill and AI controls require accessible names, roles, focus states and keyboard operation.
* Meaningful Canvas objects MUST be inspectable through an accessible structured object/list view exposing object name/type, authored text, alt description and relevant ordering/spatial relationship information.
* Visual objects MUST support alt text/description where appropriate.
* Pure freehand ink is not required to become authoritative text automatically. AI/OCR descriptions MUST remain clearly derived metadata unless the user explicitly adopts/converts them.
* Required state MUST NOT be communicated only by colour; focus must be visible; zoom/display scaling and reduced-motion preferences must be respected.
Security and permissions
Canvas inherits Home's permission broker and global API permission model. Canvas MUST declare action risk classifications and MUST NOT bypass Home for protected external or destructive operations.
* Opening a Canvas file MUST NOT grant embedded code, Generative UI, media or linked artifacts arbitrary filesystem/network/account access.
* Embedded/shared objects execute only within the permissions of their owning capability/runtime.
* Untrusted imported Canvas content MUST NOT change Home trust grants, security policy or sandbox restrictions.
* Whole-artifact permanent deletion is routed through Files' canonical deletion semantics; ordinary object/stroke deletion remains Canvas document history unless explicitly made permanent through an authorised action.
Typed Canvas API
Canvas MUST expose a versioned first-party API namespace with UI parity. The canonical namespace is 9to1.Canvas; language bindings that cannot use 9to1 literally use the global aliasing rule without changing canonical action names.
CanvasMutationRequest {
  BaseRevisionID: RevisionID
  OperationID: StableID
  ActorContext: PermissionFilteredActorContext
}
CanvasMutationResult {
  RevisionID: RevisionID
  ChangedIDs: StableID[]
  Warnings: Warning[]
}
Required action families include:
* Artifact: Create, Open, Get, Rename, Save/Flush, Close, Recover, GetRevisionHistory, RestoreRevision and document-settings operations.
* Pages: List, Get, Create, Duplicate, Move/Reorder, Delete/Restore, SetBounds/Size/Orientation and set page/background properties.
* Layers: List, Create, Rename, Reorder, SetVisibility, SetLocked, MoveObjects, Delete/Restore.
* Objects/Strokes: query/get, insert/create, update style, transform, duplicate, group/ungroup, reorder, move layer, delete/restore and semantic search.
* Ink: begin/append/end stroke for authorised input/runtime integrations, create from structured samples, update allowed stroke properties and query stroke geometry/metadata.
* Pens: ListPresets, CreatePreset, DuplicatePreset, UpdatePreset, Delete/RestorePreset and favourite/reorder operations.
* Insert/shared objects: enumerate registered Canvas-compatible types and create each through its canonical shared-object action.
* Import/export: enumerate formats/capabilities, start long-running import/export jobs, query status, cancel and retrieve CompatibilityReport.
* AI/session integration: semantic-context queries and session binding/status operations; AI model execution remains owned by the shared Dulche platform.
* Collaboration: presence/query, comments if Canvas exposes them, share/reference state and conflict/revision operations consistent with the owning collaboration service.
Actions that enumerate unbounded collections MUST paginate/stream. Long-running import/export/render work MUST return JobID, status and cancellation. Retry-safe mutations MUST use OperationID idempotency.
Typed errors MUST distinguish at least NOT_FOUND, INVALID_ARGUMENT, PERMISSION_DENIED, REVISION_CONFLICT, CAPABILITY_UNAVAILABLE, UNSUPPORTED_FEATURE, FORMAT_INCOMPATIBLE, IO_ERROR, RUNTIME_ERROR and CANCELLED.
Performance and resource constraints
Canvas MUST remain responsive for realistically large documents and MUST NOT render/process the entire spatial document every frame solely because it exists.
* Viewport rendering MUST use spatial culling/virtualisation and appropriate caching so distant off-screen objects do not impose per-frame work proportional to total document size.
* Incremental edits such as adding one stroke SHOULD avoid rewriting/re-rendering unrelated content where the persistence/rendering architecture can preserve correctness without doing so.
* Caches MUST be bounded and invalidated by semantic revisions rather than growing without limit.
* Large images/media MUST use progressive/appropriate decoding and MUST NOT block the interactive input loop unnecessarily.
The implementation test suite MUST include large-canvas fixtures with high stroke/object counts, embedded images/shared objects and widely separated spatial extents. Performance claims require measured runtime evidence on the declared supported-hardware baseline; 'feels fast' is not acceptance evidence.
Failure and recovery behaviour
* If a shared object renderer fails, Canvas MUST preserve the object data and show a typed recoverable error representation rather than deleting or flattening it.
* If a donor tool/backend capability fails to initialise, Canvas MUST report the unavailable capability and keep unaffected tools usable.
* Transient save/sync failures MUST use bounded retry/backoff and expose unresolved state; infinite retries are prohibited.
* If collaboration connectivity is lost, local permitted editing MAY continue when the storage/conflict model supports it, but unsynchronised state MUST be visible and reconciliation MUST use revision/conflict rules on reconnect.
* If an import is partially parseable, Canvas MUST follow the declared import policy: either commit an explicitly reported partial result as a new artifact or fail atomically. It MUST NOT silently present a partial result as full-fidelity success.
Release acceptance criteria
Canvas is release-conformant only when behavioural evidence proves the complete agreed interaction model, donor parity and cross-app integration. Source presence, screenshots, disconnected controls or a successful build alone are insufficient.
The release validation suite MUST demonstrate at minimum:
* Create an INFINITE artifact, draw/write with pressure-capable test input, pan/zoom, select/manipulate objects, save, close and reopen with stable IDs and stroke properties preserved.
* Create a PAGED artifact, configure size/orientation/background, add/reorder pages and save/reopen.
* Convert between document modes through the preview/confirmation workflow without silent clipping/loss and verify undo/recovery.
* Bottom toolbar layout/order, selected-tool name, hover/focus tooltips and contextual options above the toolbar.
* Preset Pen use plus create/edit/save/duplicate/reorder/delete/recover Custom Pen workflows.
* Natural Eraser and Quick Eraser, including deterministic overlap targeting and undo.
* Insert cascading menu with Text, Shapes, Media, Table and Other; verify Text Box/Heading/Paragraph, shape/line catalogue, Image/Audio/Video, shared Table and Graph under Other.
* Verify Drawing is not exposed as a redundant Insert item.
* Shared-object parity tests proving Canvas does not substitute reduced private Text/Shape/Media/Table/Graph implementations.
* Top-right quick-settings pill with page type, background and Smart Pen plus correct document-vs-user persistence scope.
* Smart Pen creates neater structured ink, does not convert to typed text and restores exact pre-adjustment content through undo.
* AI selected -> shared floating Dulche bar appears above toolbar; changing tool hides the bar without cancelling an active session; active hidden session shows the top-right sparkle indicator; clicking that indicator restores the same SessionID.
* Read-only AI is unable to mutate; Write AI performs a real typed edit and respects revision conflict if targets change.
* Live Canvas embed in Boards edits the same ArtifactID, and an explicitly created snapshot produces a distinct copy.
* Files rename/move does not change ArtifactID; Files delete/recovery semantics work for the whole artifact.
* Spaces performs a semantic Canvas edit through the Canvas API without UI simulation.
* Automation/API parity for representative page, layer, stroke, shared-object, pen, settings, import/export and recovery operations.
* Cross-app clipboard fidelity for structured ink and at least two supported shared-object types with Boards plus at least one of Write/Present.
* Concurrent editing on independent objects preserves both edits; conflicting same-entity edits produce deterministic merge/conflict behaviour rather than silent last-write-wins loss.
* Keyboard-only operation of required shell/menu/tool workflows and assistive-technology inspection of meaningful Canvas objects.
* Representative Rnote donor-parity suite against the pinned donor manifest, with every manifest entry evidenced or explicitly accepted as a scoped exception.
* Native and web/open-on-lower-capability round trips preserve unsupported pressure/tilt and other stored data.
* Crash/interrupted-save recovery from a last-known-good revision plus recoverable newer work.
* Large-canvas stress fixtures demonstrate bounded memory/cache behaviour, viewport culling and acceptable measured pointer/pan/zoom responsiveness on the declared baseline.
Any acceptance path that silently drops user data, silently flattens a supported structured object, reports a disconnected UI control as working, creates duplicate canonical artifacts during live embedding, loses a running AI session solely because its UI is hidden, or omits an unreviewed donor feature MUST block release conformance.
