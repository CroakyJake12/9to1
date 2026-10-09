# Picture implementation checkpoint — 7 October 2026

Base: received C50 + checkpoint03 local snapshot `d2b2ea9f346579efdc6788f09c266341465fbeb3`.

## Implemented candidate

The native Picture window now opens raster sources and `.picture.json` documents; creates transparent canvases; applies crop, clockwise/counterclockwise rotation, horizontal/vertical flip and resampling; compares the current rendering with the retained original; and reverts edits without modifying source bytes. Save writes editable operation graphs, Save a Copy creates a new DocumentId, and PNG export renders the original source at full requested canvas dimensions. Existing local picker/storage boundaries remain explicit; no local path is fabricated as a canonical Files FileId.

`PictureEditorSession` uses the actual shared `Haven.Application.DocumentMutationHistory<T>` for atomic edit/undo/redo state. Reopening reconstructs the saved current operation branch by replaying its operations through that same engine. Source revisions are verified before editing, comparison and export. Saved documents detect changes made since open before overwriting; a conflict directs the user to Save a Copy. Window close, open and sibling navigation guard unsaved work. Keyboard Save/Open/Undo/Redo and accessible numeric transform controls are available.

The operation graph editor validates crop dependencies by replaying existing document operations before accepting removal, replacement or reordering. Its native stack controls follow in the next checkpoint.

The existing FluentTheme requested Inter but the standalone app did not register that font. Production and headless bootstrap now register the real packaged `Avalonia.Fonts.Inter`. Theme resources and shared host files were not edited.

## Quick evidence and boundaries

- Owning project build succeeded; an obsolete XAML placeholder warning was subsequently fixed.
- Owning test project rebuilt the real app and shared dependencies: 37 passed, 0 failed, 0 skipped; natural exit 0 (`picture-tests03.log` and TRX in `/workspace/c50-lane-evidence`). Tests exercise real raster pixels, editable save/reopen/copy, source preservation/conflicts, native control events, shared undo/redo and dependency-safe operation edits.
- Inspected headless renders at 1080 × 760 and 720 × 520. Toolbar and canvas remain readable; the inspector scrolls at small size. Retained screenshots are in `/tmp/opencode/picture-ui-qa` and copied into the evidence directory.
- Initial CLI-home failure and missing-font test failure logs are retained. A later lock-wait cancellation ran no tests; the successful rerun used the coordinator-issued sole build slot.
- Windows/native desktop execution: UNRUN on Linux. Headless checks are not Windows packaging or whole-app acceptance.

## Material remaining scope

Picture remains incomplete against the full canonical specification. Durable redo/branch history, automatic/manual object composition, shared vector/text/Rnote drawing integration, advanced raster adjustments/masks, generation, animation-preserving editing, profile/HDR handling, Files/Home/AI-bar hosted integration and web parity are not established by this checkpoint. Current undo replay recovers the active saved operation branch (bounded by the shared engine history limit); it does not preserve discarded branches or redo entries across restart. Raster codec and UI support remain bounded to the existing supported imports and PNG export. The existing standalone FluentTheme is not proof of integration with the canonical host theme.

## Receiving RC lane CUI correction — source candidate, checks UNRUN

The original external 37/37 Debug result belongs to the immutable incoming
Picture01 source. This subsequent correction has not yet compiled or run.
It keeps PictureDocument, PictureEditorSession, PictureCropService,
PictureOperationEditor and the same shared DocumentMutationHistory pipeline.
The prior pixel, metadata, source-preservation, save/reopen/copy and operation
controls remain. Their actual UI route now awaits explicit test-only readiness
and exercises the authored CUI scene rather than the former AXAML layout.

Visible workspace, save confirmation and metadata information are authored
through canonical primitive .cui documents. A typed picture.viewport Object
hosts the same native pixel image and pan/zoom behavior. Product creation,
editing, save, copy, comparison, crop, transforms, history, metadata privacy,
PNG export and keyboard commands use the same incoming implementations.
The app references the maintained pinned CUI runtime, uses its shared theme
and bundled font configuration, and excludes .axaml from accepted app markup.
No second theme, font assets, control framework or history engine was copied.
The root-owned shared CUI Montserrat correction is a dependency.

Initialization and commands are retained before source callbacks. Accepted
CUI commands are awaited; commands are serialized while picker/modal/save work
is pending. Close retains its own driver, seals the scene, and joins actual
initialization/command/CUI sources before disposing the rendered bitmap.
Observer/source and close failures remain original failures.

A default standalone process lacks authenticated Home attachment and shows
an honest install/open-from-Home state. The native composition constructor
accepts the owning host's actual readiness source. Readiness is awaited before
editing actions mount. Local tests explicitly supply fixture readiness; they
do not qualify installation, permissions, shared Files access or product auth.

Full Picture remains incomplete: canonical Home/Files native composition,
Dulche bar, shared productivity/vector/text/Rnote drawing reuse, layers and
hybrid composition, masks and advanced selection, AI/generative workflows,
colour/profile and HDR support, extensive export formats and web workflow.
This correction does not promote Picture to IMPLEMENTED, PACKAGEABLE,
PACKAGED, SMOKE-PASSED or EXTENSIVELY VERIFIED. Windows user acceptance and
all readiness evidence belong to the coordinator's release phase.


## 2026-10-08 editable raster operation stack source checkpoint08

Typed CUI Repeat rows select exact session-issued operation targets. Existing
crop/resize fields and bounded rotate/flip controls modify the selected edit;
move earlier/later and remove replay the actual dependent operation graph.
Every accepted change renders through the same PictureCropService, uses the
same DocumentMutationHistory, and persists with the original source and
PictureDocument format. Foreign/stale targets and invalid dependent crops
are rejected before history mutation. Equal-value/same-position changes do
not invent revisions. Saved explicit resizes that become no-ops remain in
the reopened graph and shared Undo/Redo.

Seven additional targeted cases cover actual red/green source pixels through
modify/reorder/remove/Undo/Redo/save/reopen, stale or foreign selection,
invalid dependency preservation, explicit no-op resize persistence, unchanged
edit behavior, and actual typed CUI row selection/controls on the same session.
The existing held-readiness fixture awaits its actual entered source or same
initialization driver failure before inspecting calls. No fake product Ready
or source gate weakening. Original37 pixel/data cases remain in the graph.

This packet depends on source01–06, includes sealed readiness07 correction,
and retains ledger03/close04 behavior. Owning SDK tests/build and acceptance
are UNRUN for this packet. Source XML parse is not native runtime qualification.
No full Picture app stage promoted. Enable/disable operations, durable full
history and variants, real layers/vector/Rnote/text/advanced selection, shared
Home/Files producer and other explicitly listed full requirements remain
unimplemented and retain their canonical product meaning.
