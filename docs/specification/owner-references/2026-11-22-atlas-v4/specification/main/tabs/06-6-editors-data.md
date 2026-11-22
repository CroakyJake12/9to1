# 6. Editors & data

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.1z0hcvqlwwzx

<a id="h.1to7137v5eip"></a>

# Editors and data

These application contracts use the shared app lifecycle, CUI interface, runtime and data/trust requirements. The requirements below define the applicable editor behaviour directly, without requiring any external product specification.

<a id="h.dg14qoyy48k7"></a>

## 6.1 Boards

APP-BOARD-001 — Boards stores stable board, section, page and BlockTree identities. A Native page is one seamless document-space surface where flow blocks, positioned content and page ink coexist; it is not a paper frame, a hidden second Canvas panel or a rasterised drawing background. Preserve authored text, lists, tables, drawings and order during chrome changes. Default desktop reference geometry is a 256-logical-unit sidebar, 32-unit tree rows, 16-unit nesting and a 760-unit readable column, adapted through the shared responsive rules.

APP-BOARD-002 — Board identity, board-name file menu, save state and Undo/Redo belong in the sidebar. Renaming is an explicit file-menu command. One compact toolbar contains Add content, Drawing, Format, Edit/View, Collaborate and Page. Format contains typography/paragraph/list/alignment controls; Page contains Properties, layout lock, snapping and page options. The contextual chat surface is the single AI entry. The footer label is New page in the active refinement and its accessible action is Add a page or section.

APP-BOARD-003 — Footer New page creates Native, Write, Canvas, Chat or Scoped Chat pages, or a separate section, in an explicitly shown destination. Opening/cancelling allocates nothing; commitment creates exactly one entity, reveals it and focuses title/editor. Toolbar Add, inline plus and slash insert content into the current page through one catalogue. They preserve the original range and replace only an eligible slash token/empty block or the selected insertion boundary.

APP-BOARD-004 — The caret belongs to one BlockID. Text entry/selection does not drag the block or paint ink. Enter splits paragraph siblings; Shift+Enter inserts a soft break; IME commit precedes structural Enter. Grip click selects its target; drag begins only after a deliberate threshold. Block menus retain target/selection, expose applicable operations and restore focus after dismissal. Turn into, style, move, duplicate, block link and deletion remain distinct commands.

APP-BOARD-005 — User Edit/View, layout Locked/Unlocked, snapping and AI access are independent. View prevents mutation. A locked layout still permits established page ink in Edit. Pen, highlighter, eraser and ink selection use one editable stroke model with pointer capture, pressure fallback, viewport transforms and one-stroke undo. Width, opacity and colour affect new strokes unless existing strokes are selected. Flow-text reflow preserves page-ink coordinates and authored stroke colours.

APP-BOARD-006 — Save As writes an editable .9to1board to a permitted destination without requiring Files. Unsaved, saving locally, saved locally, cloud sync and failure are distinct. Failure preserves live edits, last durable revision and recovery draft. Retry addresses the same save; Save a copy chooses another destination. Page/block deletion has recoverable undo where supported, while descendant deletion and external irreversible effects disclose the actual scope and consequences.

Acceptance — Create each page type; cancel creation and insertion; type with IME; split a paragraph; move a block; draw with layout locked; zoom/scroll; undo one stroke; save/reopen. Check compact menus and narrow page drawer. Force save failure, retry/save-copy and verify exactly one canonical content history with no discarded draft.

Atlas anchors: all 13 Boards screens, particularly boards-editor, boards-add, boards-insert, boards-typing, boards-ink, boards-recovery and mobile-boards; active atlas-boards-refinement-code.

<a id="h.qwhzut2xn4tr"></a>

### 6.1.1 Structure, files and navigation

ED-BOARD-001 — A Board contains its stable identity, owner/scope, metadata, ordered root entries and revision. A section is an organisational container; a page is a typed content entity that may itself contain subpages. Each page retains BoardID, PageID, parent identity, order key, type and type-specific content/reference. Native pages own a versioned BlockTree, a page-coordinate drawing layer and layout settings. Write/Canvas pages refer to the canonical owning artifact; Chat/Scoped Chat pages refer to a conversation/configuration and explicit source collection. Do not infer entity type or parentage from a visible label or indentation string.

ED-BOARD-002 — New Board immediately creates a recoverable local draft and initial Native page without requiring a filename, account, Files provider or model. Cancelling Save As retains this draft and editing state. Save As changes its active storage destination while preserving logical identity; Save a Copy explicitly creates a new BoardID and remaps owned descendant IDs. The portable .9to1board representation preserves page hierarchy, rich blocks, absolute geometry, ink, graph definitions, tables and supported owned assets. External references retain source identity and permissions and disclose unavailable dependencies. Credentials and one-shot acquisition tokens never become portable document data.

ED-BOARD-003 — A file reopen resolves a durable locator and reacquires current access rather than replaying an expired lease. A moved file repairs its Recent locator without cloning every page. Detect independently modified files sharing logical ancestry and offer reconciliation or explicit independent-copy opening. Save validates the loaded destination revision before replacement. A crash during serialization or replacement must leave a complete old or new file and a recoverable draft, never a partially assembled board. A missing target offers Locate and Remove from Recent; removal from Recent does not delete the file.

ED-BOARD-004 — Navigation distinguishes tree focus, expansion and active page. Up/Down traverses visible rows; Right expands or enters a child; Left collapses or selects a parent; Home/End reaches visible boundaries; Enter opens. F2/explicit Rename edits the label, with Enter validating and Escape restoring it. Collapsing an ancestor preserves the active descendant and reveal expands the required ancestors. Switching Board updates tree, breadcrumb, page content, selected-object tools and AI context atomically. Failed target opening leaves the previous board usable. The final Atlas board-name file menu remains authoritative; these operations must not resurrect its superseded name-click behaviour.

ED-BOARD-005 — Page movement declares before, after or inside a specific destination. Reject self-parenting and ancestor cycles, preserve the entire subtree and commit once. A Move To dialogue supplies the keyboard alternative. Drag previews and edge scrolling never mutate the tree; cancellation restores order and capture. Trash records original parent/order and descendant impact. Restore uses that parent if valid or identifies a recovered location. Deleting a referenced artifact page removes its reference; deleting board-owned content declares the cascade. Search indexes authorised unloaded pages and reveals stable targets without modifying the filtered tree.

<a id="h.tem8q8gv9ioh"></a>

### 6.1.2 Block, ink and embedded-editor semantics

ED-BOARD-006 — Paragraph Enter splits one BlockID at the caret: the prefix retains identity, the suffix receives one new sibling ID and the caret moves to its start. The deletion of a selected range and subsequent split form one logical undo. Heading continuation becomes body text; list continuation creates the same type/depth; an empty nested list outdents once and an empty root list exits to a paragraph. A new checklist item is unchecked. Code Enter belongs to the code editor and table Enter to the cell editor. Shift+Enter is a soft break where supported. IME commit and grapheme boundaries take precedence over structural shortcuts; wrapping never creates blocks.

ED-BOARD-007 — Backspace at a compatible paragraph boundary merges text and places the caret at the join. At list boundaries, apply the defined outdent/type transition before considering a merge. Incompatible or non-text neighbours must not disappear through a generic zero-offset handler. Preserve children, inline marks and references. Type conversion that cannot retain children requires an explicit reversible restructuring. Selection of a parent and its descendant must not move or duplicate that child twice. Copy duplicates owned IDs; Move keeps them; Cut removes source content only after a valid receiving operation is accepted.

ED-BOARD-008 — Slash, inline insertion and toolbar Add address one catalogue and captured insertion range. Escape keeps ordinary typed text; selection replaces only the eligible command token/empty block or inserts at the intended boundary. A slash inside a URL or code is not automatically an insert command. Grip click/menu and drag use the same target with a real movement threshold; starting drag suppresses click activation. Before/after, nesting and column targets are visibly different. Virtualised rows resolve stable IDs on drop. Moving a flow block sideways does not silently convert it into absolute content.

ED-BOARD-009 — Explicit free placement stores document-space position, dimensions and z-order. Return to flow shows the destination boundary and is undoable. Layout locking preserves exact coordinates and prevents casual spatial movement; it does not disable flow text, flow reorder or page ink in Edit. Snap applies to active moves/resizes, not handwriting, typing or all existing objects when enabled. Grid visibility is a separate preference. Page ink uses one inverse viewport transform for input and rendering, keeps page coordinates through text reflow and follows an object only when an explicit attachment relationship exists.

ED-BOARD-010 — A stroke owns ordered samples and resolved tool properties. Missing pressure uses the selected fixed width; supported pressure/tilt and palm rejection follow device capability. Pointer capture lasts through movement outside the page and ends on commit/cancel. One completed stroke is one undo operation; cancelled input creates no phantom stroke. Whole-stroke and segment erasure are separately named capabilities, and an erase gesture has its own transaction. Changing a brush preset does not mutate existing strokes. Tool switching preserves the prior caret/selection and cannot turn selection adorners into saved ink.

ED-BOARD-011 — Simple tables and Data-backed views share visual integration but have different ownership. Cell selection differs from text editing; Tab traverses cells, arrows edit text when appropriate, and outer page handlers cannot split blocks from a cell keystroke. Rectangular range operations validate shape and types. Merge preserves every nonempty cell through a documented reversible policy; unmerge restores the supported structure. Row/column IDs survive reordering. A wide table may scroll internally without making the entire board horizontally scroll. Graph blocks preserve ordered expressions, variables, axes and errors independently; valid plots remain visible when one expression fails. View-mode slider changes remain session-local unless saved explicitly.

ED-BOARD-012 — Embedded Write/Canvas surfaces use their registered engine and source revision; Open in App reaches that same artifact. Missing engines or revoked source access leave a scoped placeholder and the rest of the board usable. Scoped Chat uses the authorised board plus attached sources as its closed corpus; insufficient evidence is explicit. Ingestion states identify the usable indexed source revision. Source removal prevents future retrieval without rewriting historical citations. Rendering, opening, resizing and changing theme must not insert blocks, change document revision or manufacture default content.

<a id="h.vmwwe1hnkyzx"></a>

### 6.1.3 Boards acceptance workflows

ED-BOARD-013 — Create an unnamed board offline; type a mixed-format paragraph; split it in the middle, undo and redo. Create a nested checklist and exit one level from an empty item. Compose a non-Latin IME candidate and verify it does not also split. Add a table and press Enter inside its editor. The resulting BlockTree must contain exactly the intended entities and preserve formatting/graphemes. Repeat after save/reopen, checking that old input event handlers do not fire during restoration.

ED-BOARD-014 — Create a section with a nested page, an external Canvas reference and board-owned ink. Move the section through pointer and Move To commands, reject a cycle, cancel a drag during auto-scroll, then Trash/restore it. Draw while layout is locked; reflow a preceding paragraph and verify page ink remains anchored. Save to a user-chosen removable destination, interrupt the write, reopen from the prior complete file and recover the draft. Removing the Canvas reference must leave its original intact.

Reference detail — Atlas Boards screen family and final Boards refinement; selectively reconstructed technical behaviour from [boards.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/boards.complete.md), UX27-BOARD-003–046 and its canonical model. Historical toolbar, colour, donor-branding and name-click instructions are superseded by the active Atlas.

<a id="h.h4fqexkya5vo"></a>

## 6.2 Write

APP-WRITE-001 — Pages, Continuous and Book are views of one DocumentID, section tree, selection, comments and revision. New native documents default to Continuous unless a template requires pagination; restoration preserves prior view. Pages uses real page geometry/margins. Continuous is one flowing surface without paper cards or artificial sheet gaps, while retaining section/page breaks, headers, footnotes, margins and pagination semantics beneath the view.

APP-WRITE-002 — Sections are authored nodes; headings derive from styles. Outline collapse, width and scroll are personal view state. Caret, range, toolbar and Properties share one editing state. Edit/Suggest/View, layout lock and AI access are independent. The contextual chat layout reserves usable space; it does not cover text, selected ranges or status controls.

APP-WRITE-003 — Suggestions anchor exact changes to a revision/range and remain pending until accepted. Accept is an undoable validated change; Dismiss rejects only that proposal. Comments are distinct and keep stable anchors when a section moves. Review scrolls independently and reveals its exact target. Collaboration uses APP-AGENT-007 without copying the document.

APP-WRITE-004 — Support rich paragraph/character styles, nested sections, tables, positioned objects, references/fields, review, native documents, supported foreign-format conversion, PDF opening/editing and derived export. Preserve editable structures supported by the engine and report fidelity limitations through APP-BASE-004. A simplified toolbar must not replace these capabilities with plain text or screenshots.

Acceptance — Switch all three views with a selection, moved section, comment and pending suggestion. Accept one change and undo it; dismiss another. Save/reopen and test a supported foreign-format/PDF round trip against the declared capability matrix. Theme changes and export preserve authored formatting.

Atlas anchors: write-launch, write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators.

<a id="h.3ewtqap1e0r0"></a>

### 6.2.1 Document model and editing

ED-WRITE-001 — Model the document as stable sections, paragraphs/runs, styles, tables, anchored/inline objects, references, comments and proposed revisions. Pages and Book are layout projections; Continuous is an unpaginated projection of the same authored structure. Keep display pagination caches separate from stored section/page-break semantics. A section move updates ownership/order and derived numbering without replacing its object IDs. A comment or bookmark references an anchored range, not only a current page number or pixel coordinate.

ED-WRITE-002 — Text editing has a single active selection described by logical anchors, affinity and composition state. Character deletion is grapheme-safe; word/paragraph navigation follows the actual text engine. Insert, paste, replace and formatting operations preserve unaffected marks and paragraph properties. Toolbar controls report mixed selection values rather than pretending the first character’s style applies everywhere. IME composition remains provisional until committed and cannot trigger global shortcuts. Undo groups a meaningful input action without merging unrelated collaborator or autosave events into the same transaction.

ED-WRITE-003 — Styles have stable identity, semantic role, inheritance and explicit overrides. A named style update changes its inheriting content while preserving deliberate local overrides. Heading role drives the outline; visual font size alone does not turn a paragraph into a heading. Clearing direct formatting preserves semantic structure. Numbered lists store list identity, level and restart/continuation policy rather than literal typed numbers. Reordering recalculates displayed numbering; copying can continue or start a new list through an explicit option. Invalid cyclic style inheritance is rejected with the prior style intact.

ED-WRITE-004 — Page setup owns page size, orientation, margins, columns and section-specific rules; headers/footers, footnotes/endnotes and fields follow their proper section/anchor. Continuous shows a quiet break marker without discarding those properties. A page-layout change recomputes pagination and derived fields, preserving logical selection and anchors. Book pairing and reading order are view configuration, not duplicated document pages. Changing the display view must not change line-break characters, authored fonts or export pagination rules.

ED-WRITE-005 — A table owns stable rows, columns and cells, including spans, width rules, cell content and styles. Structural edits rebase anchors and preserve content; a merge cannot silently discard non-anchor text. Nested objects and tables follow supported engine limits reported in the format matrix. Images retain source/asset identity, alt text, crop and anchor/wrap policy. Dragging changes object geometry relative to its declared anchor; changing pages must not detach a floating object. Relink resolves a missing dependency without creating a duplicate layout object.

ED-WRITE-006 — Find identifies a query, scope and revision and searches content outside virtualised pages. Replace All previews the affected match count and scope and commits through one bounded operation or declared resumable batches. Regex or advanced search, where supported, has validation and cancellation. Comments, tracked changes and hidden/deleted text are included only according to the selected search scope. A stale match is re-resolved before replacement rather than editing whatever later occupies its offset. Navigation to a result preserves the prior location for Back.

<a id="h.yrf2g54ip7lo"></a>

### 6.2.2 Review, formats and document workflows

ED-WRITE-007 — A suggested change contains author, source revision, anchored target, change kind and proposed content. Insertions, deletions and formatting changes remain distinguishable. Accept/Reject targets one proposal or an explicitly selected set. A proposal whose original range was independently edited becomes conflicted; it cannot silently replace the newer text. Comment threads track author, replies, resolution and anchor movement independently of suggestion status. Resolving a comment is not accepting its proposal. Review filters alter visibility, not document content or another reviewer’s history.

ED-WRITE-008 — Imports create an editable native model with provenance and a conversion report. For each supported DOCX/ODT/RTF/plain-text or other registered format, declare preservation of styles, sections, tables, references, annotations and media separately rather than a single unsupported “perfect compatibility” claim. A foreign file remains unchanged unless the user deliberately saves through its supported round-trip path. Export snapshots the selected revision and recomputes derived layout/fields under declared rules. PDF export verifies pages and selectable/rendered content; PDF editing reports which text/objects are directly editable and which require a declared conversion or OCR workflow.

ED-WRITE-009 — PDF pages retain page boxes, rotation, annotations and source-page identity. Opening a scan does not manufacture selectable text. OCR, when supported, records language, confidence/provenance and an editable text layer distinct from the original image. Redaction, if exposed, must remove the selected underlying content in the produced bytes rather than merely drawing an opaque rectangle. Unsupported PDF features remain preserved or explicitly reported before a lossy rewrite. Font substitution is a visible compatibility finding, not silent evidence that layout is unchanged.

ED-WRITE-010 — Workflow A: import a document containing a custom heading style, continued numbering, a table, footnote, floating image and comment; inspect the conversion report. Move the section, change one inherited style and leave a local override; switch all views. Export a supported editable format and PDF, reopen both, and compare structural identity where the format carries it, numbering, anchors, authored values and layout. Record actual losses per feature, not only file-open success.

ED-WRITE-011 — Workflow B: collaborate on two separate ranges while a user composes text. Apply one valid suggestion, conflict another by editing its target, and resolve a comment without accepting the conflicted change. Find/replace a word across unloaded sections, cancel a second operation and undo the first accepted change. Verify logical selection, comment anchors, per-operation undo and saved revision agree. Repeat with an interrupted export; the native draft and unaccepted proposals remain available.

Reference detail — Atlas Write, review and collaborator screens; [write.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/write.complete.md)’s mature editing, style/table, format and donor-parity behaviour. Format names above are capability targets; actual supported variants and feature loss require implementation evidence.

<a id="h.ulby20m94ls9"></a>

## 6.3 Present, Actions and Variants

APP-PRESENT-001 — Slides and objects retain stable IDs, order and revisions. Hosted chrome follows APP-BASE-002; conventional menus and one contextual toolbar retain visible Insert and Actions. Fit uses the actual viewport after Notes/Properties and preserves slide aspect ratio without overlapping controls. Zoom has minus, slider, actual percentage, plus and icon-only Fit. Single click selects; double click/Enter edits text. Resize/rotation handles track geometry and are excluded from output.

APP-PRESENT-002 — Slide Notes has its own resizable scroll area. Presenter view privately shows notes, next slide and elapsed time; audience output contains only the selected audience content. Exiting playback restores authoring state. Session ink is temporary unless deliberately committed. Retain layouts/masters, rich text/media, arrangement/grouping, keyboard nudge, proportional resize and animation.

APP-PRESENT-003 — Transitions is a simple view of the canonical Actions graph. A preset edits only its owned subgraph; external extension nodes survive. Open in Actions reveals that same graph. A nonrepresentable advanced edit displays Custom/Advanced rather than deleting or approximating it. Targets are stable slide/object IDs; navigation commits once with explicit completion/failure. Authoring gestures do not trigger playback; preview runtime state remains separate from the document.

APP-PRESENT-004 — Variants is integrated into Present. Compare identifies source, base and target revisions and distinguishes additions, edits and conflicts without relying on colour. Preview selected semantic changes before merge; refresh stale comparisons. Switching/merging preserves branches and does not publish or present. Collaborators use the exact selected object/range and APP-AGENT-007.

Acceptance — Open Notes/Properties and Fit; edit one object; verify audience output excludes notes/handles/presence. Change Fade in the simple panel and inspect its graph; preserve an extension. Reject a stale merge until revalidated. Failed export leaves the editable deck and branch histories intact.

Atlas anchors: all 12 Present screens; the current Haven shell governs hosted chrome. Preserve the layout, master and editing behaviour required in this chapter.

<a id="h.joeta0yz38qk"></a>

### 6.3.1 Slides, objects and playback

ED-PRESENT-001 — A presentation owns ordered SlideIDs, dimensions, masters/layouts, theme assets, object trees, speaker notes and revision. A slide object owns type, geometry/transform, content, style and stable references. Slide order is not identity. Layout/master inheritance identifies which values are inherited and which are overridden; changing a layout cannot silently delete authored objects. Duplicating a slide remaps its owned object IDs and internal references while preserving permitted external references. Deleting a slide validates links, actions and variant relationships that target it.

ED-PRESENT-002 — Single click selects an object, Shift/selection commands modify the explicit set, and double click/Enter enters text editing. Escape exits the innermost editor before clearing the object selection or leaving a popup. A drag begins against a captured target and transform. Resize handles preserve the selected pivot/anchor and apply proportional constraints as chosen; rotation uses a visible pivot. Alignment/distribution operates on the declared selection or slide, not the entire deck. Grouping preserves each child’s identity and relative transform; ungrouping converts transforms without visual jumps.

ED-PRESENT-003 — Text boxes retain runs, paragraph styles, alignment, insets and overflow behaviour. Auto-fit, manual font size and text-box resize are distinct operations. An overflow warning identifies clipped content rather than silently shrinking every slide. Notes are authored per-slide content in a separate pane. Changing Notes/Properties dimensions recalculates the central Fit transform and displayed zoom from actual geometry; it does not change slide size. Insert opens the catalogue, then a selected item creates one object at a validated location. Paste preserves supported object structure and reports lossy conversion.

ED-PRESENT-004 — Audience playback, presenter controls and authoring have separate session state over one document revision. The playback session records current slide, reveal/action state, timer, media state and presenter/audience surfaces. Starting playback validates required assets and reveals any missing fonts/media. Presenter notes, next-slide preview, selection handles and collaborator cursors are excluded from audience output. Navigation shortcuts act only in playback focus. Ending playback restores the previous authoring selection and view; transient ink is retained only through an explicit commit/export choice.

<a id="h.2aicdvqql7ca"></a>

### 6.3.2 Actions graph and semantic branches

ED-PRESENT-005 — A presentation Actions graph contains stable nodes/edges, typed event inputs, target object/slide references, parameters and completion/error outputs. It is distinct from the Actions Space, which may coordinate work on the presentation. On-click, timed, media and navigation events enter the graph through defined runtime events, not simulated mouse input. Typed validation rejects missing targets and incompatible ports. A navigation request has one accepted commit identity; multiple downstream completions cannot advance twice. Runtime cancellation stops future events while preserving already committed navigation state.

ED-PRESENT-006 — Transition/animation simple editors own explicit graph regions. Preset changes replace only those regions and retain user extension nodes and connection contracts. If edits break the simple representation, display Custom/Advanced and expose the graph. Duration, easing, trigger and advance conditions are stored graph properties. Preview runs a bounded playback session with resettable transient state; preview does not write authored node values or send external commands. Deterministic ordering and budgets prevent self-triggering cycles from freezing the editor. A failed media dependency affects its action path and exposes a typed failure rather than reporting complete.

ED-PRESENT-007 — Variants contains branch identity, ancestry/base revision and proposed semantic differences. Compare pairs changes by stable slide/object/property identity, distinguishes move from delete-plus-add, and preserves authored ordering. Three-way merge uses a common base: non-overlapping property edits may combine, while edits to the same property, deletion versus modification and incompatible order constraints remain explicit conflicts. Selecting an item previews the exact merged effect. Commit validates that both inputs still match the preview; stale comparison refreshes. The source/base/target branches remain recoverable after a merge.

ED-PRESENT-008 — Native save preserves masters/layouts, notes, editable objects, Actions graphs and variants where supported. Each import/export format declares treatment of these structures, audio/video and custom fonts. Static PDF/image output is a derived representation and cannot be labelled an editable Actions-capable deck. Audience video export identifies its route/timing and whether it renders animations or a static slide sequence. Export failures are scoped to the job. A document containing an unsupported imported effect keeps its original payload where feasible and exposes a compatibility warning before replacing it.

ED-PRESENT-009 — Workflow A: create three slides from one master, override a title, group shapes, attach notes and a clip; alter the master and verify the intended inheritance. Open Notes/Properties, Fit, edit text, move the group and enter playback. Send only audience content to an independent output surface. Exit and verify authoring state and notes survive. Reopen native and exported representations, recording which effects remain editable.

ED-PRESENT-010 — Workflow B: choose a simple Fade, open the same Actions graph, add an extension outside its owned region, and change duration in the simple editor. Trigger one navigation and verify one commit; cancel another preview. Create two variants, edit different properties and then the same property, compare/merge the first pair and explicitly resolve the second. Advance a branch after preview and confirm the stale merge is rejected until refreshed.

Reference detail — Atlas Present, transition, Actions, presenter and variant screens; [present.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/present.complete.md) and the previous native Variants contract. The later Haven shell governs current toolbar placement.

<a id="h.d6vewb1ibgfx"></a>

## 6.4 Canvas and the shared shape builder

APP-CANVAS-001 — Infinite Canvas fills the viewport without an artificial page edge. Rendering, hit testing, selection, pan and zoom use one coordinate model. Object, layer, connector and stroke identities survive transforms, undo, save/reopen and cross-app use. Background and artwork colours remain authored values. Retain grouping, connectors, transforms, drawing/erasing and supported hybrid raster/vector operations.

APP-CANVAS-002 — Primary tools remain bottom-centre in order Select, Pan, Pen, Eraser, Insert, Tools, AI; only the selected tool shows its label. Reselecting Pen opens presets. One options host sits above the tools; AI replaces it rather than adding a permanent second composer. Layers is an explicit selected-object popup. Insert first-level categories are exactly Text, Shapes, Media, Table and Other.

APP-CANVAS-003 — Browsing galleries allocates no document object. Pen presets resolve properties onto new strokes; editing a preset does not rewrite old strokes. Pressure/tilt follows device capability with usable fixed-width mouse fallback. Existing stroke properties change only through an explicit selection edit. AI targets actual object IDs/revision and obeys the shared access/mode rules.

APP-SHAPE-001 — Present and Canvas share editable paths with stable anchors, segment types, closed state, fill rule, linked Bézier handles and numeric coordinates. Corner, Smooth and Symmetric alter tangent constraints. Add/remove/split, nudge and Boolean operations use coherent undo transactions. Builder preview excludes adorners. Insert shape commits one editable object; Cancel discards only the uncommitted draft and restores caller context. My shapes stores reusable definitions whose inserted geometry remains editable.

Acceptance — Draw with mouse and a supported pressure device; change a preset and verify previous strokes stay unchanged. Select after pan/zoom; reorder a layer and undo. Build a path, cancel one draft and commit another, reuse it in Present and edit anchors without rasterisation. Toggle AI and verify a single options host.

Atlas anchors: all seven Canvas screens and present-shape-builder.

<a id="h.1pxeoszgivjw"></a>

### 6.4.1 Spatial model and tool dispatch

ED-CANVAS-001 — A Canvas artifact owns scene bounds/mode, ordered layers, object tree, assets, editable strokes and viewport-independent coordinates. Infinite mode stores content extents without creating an arbitrary paper edge; bounded mode explicitly owns page/artboard dimensions. The viewport transform is derived from pan/zoom and never written into every object on navigation. Hit testing uses the inverse transform and layer/visibility/lock state. Selection bounds are computed from canonical geometry, including rotations, rather than cached screen rectangles.

ED-CANVAS-002 — Input dispatch first resolves modal UI, text/object editing, current tool and device intent. Select gestures select or transform; Pan moves the view; Pen creates strokes; Eraser applies its declared mode. A stylus can draw while permitted touch gestures pan, with capability-aware palm rejection. Pointer capture survives leaving the viewport and cancellation ends the session. Keyboard shortcuts cannot steal text/IME input. Wheel/pinch zoom preserves the intended anchor point; view resets and fit-to-selection compute real content bounds.

ED-CANVAS-003 — Layer order, group membership, lock and visibility are separate fields. A locked item remains visible and inspectable but rejects edits unless deliberately unlocked with permission. Hidden objects remain in the document and exports follow their declared visibility. Lasso/box selection names whether it selects intersections or complete containment; combined modifiers are consistent. Multi-object transforms apply one composite transform and preserve relative positions. Connectors attach to stable object/port identities or explicit free endpoints; moving an object recomputes routing while preserving intentional user waypoints.

ED-CANVAS-004 — Pen presets store reusable parameters; each committed stroke stores resolved colour, width/pressure curve, opacity and other supported properties. Updating a preset changes later strokes unless selected strokes are explicitly updated. Eraser whole-stroke versus segment modes report their scope. Segment erasure retains editable resulting geometry and provenance rather than flattening the scene. Stroke simplification, smoothing and render caches must preserve the declared edit/export fidelity and be bounded/cancellable for large drawings.

<a id="h.mewqnxpq5jrk"></a>

### 6.4.2 Shared shapes, assets and exchange

ED-SHAPE-001 — A shared path consists of stable points/segments, coordinate values, tangent constraints, open/closed contours and fill rule. Smooth constrains tangents to one line; Symmetric additionally couples handle lengths; Corner allows independent handles. Editing a point updates dependent segments and selection without replacing the whole shape identity. Subdividing a segment inserts a point without changing the rendered curve beyond declared numerical tolerance. Split path at a selected point is a separate topology operation that breaks the contour while preserving its existing geometry. Boolean operations retain editable output paths and identify their affected source objects; invalid/self-intersecting geometry has an explicit result or error, not silent disappearance.

ED-SHAPE-002 — Builder draft, reusable definition and inserted instance are distinct. Opening the builder allocates no scene object. Cancel discards that uncommitted draft; Insert produces one instance and returns to the caller’s insertion context. Saving My shapes creates/updates a versioned reusable definition without silently rewriting existing instances. Linking an instance to a definition, if supported, is explicit; otherwise it is an independently editable copy. Present and Canvas exchange the same path representation, authored fills/outlines and constraint semantics.

ED-CANVAS-005 — Text, shapes, media, tables and other registered objects enter through the shared insert catalogue. Media retains source asset, crop/fit and alt text; a missing linked asset leaves the object and geometry recoverable. Raster editing tools operate on a named raster object/layer and report resolution; they do not implicitly rasterise unrelated vector objects. Native export preserves editable layers/paths/strokes. SVG/PDF/raster exports declare supported features, extent, scale/resolution, alpha and clipping. Export from infinite mode requires explicit bounds such as selection, content bounds or a named artboard.

ED-CANVAS-006 — Workflow A: draw a pressure stroke and mouse fallback stroke, change the preset, pan/zoom, select through the transformed view and erase a segment. Verify old stroke values remain, pointer cancellation creates no extra stroke and undo restores editable geometry. Lock a layer, route a connector to its object, move another object and verify only valid geometry changes. Reopen and compare stable IDs, colours and coordinate values.

ED-CANVAS-007 — Workflow B: construct an open path, close it, adjust Smooth/Symmetric constraints, subdivide a segment, split a contour at a selected point and perform a Boolean operation. Save a reusable definition, insert copies in Canvas and Present, edit one instance and verify the others do not unexpectedly change. Cancel another builder draft. Export a selected region at a declared resolution with alpha; verify crop boundaries and absence of selection adorners while the native artifact remains editable.

Reference detail — Atlas Canvas and both shared-shape-builder screens; [canvas.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/canvas.complete.md)’s coordinate, ink, layer and exchange contracts. Final Atlas tool order and single options host override older tool layout descriptions.

<a id="h.s2099pct9c9l"></a>

## 6.5 Decks

APP-CARDS-001 — Decks groups stable CardIDs/SetIDs without duplicating content. Overview distinguishes due, total, session review count and current position. Focused review defaults to a vertical stack with visible neighbours; supported alternate grouping/navigation preserves identity. View activation flips without editing. Edit uses click-to-edit and explicit Flip; a canvas click must not flip while editing. Decks is the current display name; existing Cards routes, package/schema identifiers, CardIDs, SetIDs and review history remain compatible.

APP-CARDS-002 — Front and Back contain independently editable structured text, fonts, backgrounds, images/graphs and ink that survive native save/export. Add, duplicate, reorder, remove/restore and bulk changes operate on the canonical set with undo/recovery. Topic links and card metadata are separate from private review evidence. Shared content changes cannot overwrite another learner’s ratings.

APP-CARDS-003 — Red/amber/green ratings create attributed private ReviewRecords. Update a Study topic only through a valid topic link; an unlinked card offers linking and an unassessed card remains unassessed. Flipping/browsing alone creates no mastery. Read-only sets may permit review while preventing content edits. Save conflicts, unavailable source media and partial bulk outcomes remain explicit. Editor AI access never overrides View mode.

Acceptance — Flip an unrated card and confirm no proficiency change; rate a linked card and confirm only that learner’s evidence updates in the Study Space. Edit rich Front/Back and reopen the same canonical set. View, review and editing retain their independent permissions and evidence effects.

Sources — Atlas: cards-study, cards-review, cards-edit. Study is a consuming Space; its application integration contract is in section 5.5.

<a id="h.viclpp2oam0h"></a>

### 6.5.1 Content, organisation and personal review

ED-CARDS-001 — CardSet stores stable set/artifact identity, ordered card membership, title, assets, owner and optional subject/topic references. A Card stores independent structured Front and Back, metadata and revision. Set, topic and custom-group views organise the same CardIDs; they are not storage duplication commands. Distinguish a shared card referenced by several sets from an explicit independent duplicate. Set-level topic defaults and explicit card-level topic links are inspectable so a review does not update an arbitrary subject chosen from the current sidebar.

ED-CARDS-002 — Front/Back support rich text/styles, font and background, canonical images/graphs and editable ink. The active side, text/object selection and card identity determine every edit. Clicking content in Edit enters its editor; Flip is explicit. View activation flips and cannot mutate content. Reduced Motion changes the transition, not the accessible Front/Back state. Next/Previous and search preserve the current CardID when grouping/order changes. Vertical stack is the default; horizontal navigation is a saved user preference. Virtualisation must not bind a recycled card view to another card’s draft or rating.

ED-CARDS-003 — Bulk creation/import validates the mapping to Front, Back, metadata and assets before committing. Each incoming row/item has an outcome; malformed entries remain inspectable for repair. Bulk editing names selected CardIDs and exact properties, supports preview/undo and reports partial outcomes if atomic completion is unavailable. Removing a card from a set differs from deleting the shared card. Native round-trip preserves structured content; text/CSV or other simpler exports warn when graphics, ink or structure cannot be represented and leave the native source intact.

ED-CARDS-004 — A personal review record stores principal, CardID, content revision, optional topic, evidence type, explicit rating and review time. Content revision matters when a shared card’s answer changes after review; retain which version was reviewed instead of silently treating the new answer as demonstrated evidence. A rating correction appends or supersedes the personal evidence through a traceable operation rather than rewriting another user’s record. Due selection, difficult-card filtering and review ordering use the active learning policy and distinguish unseen, due and reviewed cards. Browsing/flipping produces no proficiency evidence.

ED-CARDS-005 — Study is a Space that consumes Decks artifacts and attributed review evidence; it does not own a duplicate card database. Inline Spaces flashcards and Revision Bank entries open/save the same CardSet. A valid topic link routes evidence to that learner’s Study context; an unlinked card remains reviewable and offers linking. Sharing a set does not share private review records. Denied source assets remain bounded placeholders while permitted card content and personal review remain accessible.

ED-CARDS-006 — Workflow A: create four cards with text, image, graph and ink on distinct sides, duplicate one deliberately, group by set/topic, switch orientation, search and bulk-update a selected field. Undo and reopen native data; original/duplicate IDs and side content must be correct. Export to a simpler format and inspect the declared losses without altering the native deck. Workflow B: flip without rating, review linked and unlinked cards, correct one rating, edit a shared answer, and reopen through Study. Verify evidence attribution/version, private history, no invented mastery and one canonical set.

Reference detail — Atlas cards-study, cards-review and cards-edit; [cards.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/cards.complete.md), LE-10. Learning-policy thresholds remain owned by the Study Space contract.

<a id="h.3twi8do771z8"></a>

## 6.6 Data and Forms

APP-DATA-001 — Data owns stable workbook/database, sheet/table, cell/record and revision identities. Spreadsheet, database and SQL views share records only through explicit mappings; changing view does not duplicate data. Retain formulas and dependency-aware recalculation, structured fields, filtering, sorting, tables and charts. SQL shows the actual connection/runtime. Typed multi-value cells, owned/referenced nested tables, DataCubes, slices/pivots and relational projections require explicit schemas and capability reporting, not plausible mock values.

APP-DATA-002 — Active cell, range selection and cell editing are separate states. The formula bar exposes the expression; reference tokens correspond to ranges. Function help stays anchored without obscuring the sheet. Commit/Cancel is explicit, and Enter commits the same cell before normal navigation. Typed errors, locale and units come from the real calculation engine. Charts bind editable source ranges/series; Preview allocates nothing until Insert, and authored colours survive theme changes.

APP-DATA-003 — Every AI mutation requires approval of the exact action even when general Take actions is enabled. Review names source/revision, affected range/records, diff, validation and recovery. Live-database mutations require the applicable recoverable backup; backup failure blocks approval. Revalidate stale data before execution. Proposal, approval, execution, partial result and failure are distinct states; rejection leaves the source unchanged.

APP-FORMS-001 — Forms owns Form/Test identities, stable questions/sections, answers, validation, branching, scoring and versions. Reordering preserves references. Support declared answer types, constraints/defaults, typed repeating/table inputs, Form/Test/Quiz presets, mathematical input and marking rules. A condition/regex tester shows samples/failures; invalid conditions and missing targets cannot publish as valid.

APP-FORMS-002 — Draft editing, respondent Preview and published revision remain distinct. Preview uses disposable responses against the selected draft and does not increment real response counts or publish. Publish identifies version/audience/access and preserves response/version compatibility. Accepted real responses require durable persistence; failed submission retains answers and safe retry. Response export is separate and respects response privacy.

Acceptance — Edit/cancel a formula; insert a live chart; change its source and observe real recalculation. Attempt an AI write under Take actions and verify mandatory approval; block a failed-backup write. Reorder a branched question, preview paths, publish a known revision and recover one failed submission without duplicate responses. Study must open the same assessment.

Atlas anchors: data, data-approval, data-formula, data-chart, forms, forms-preview. Use the active shared palette rather than the superseded fixed-green control emphasis; preserve distinctive icon artwork.

<a id="h.upavq0qm7wcv"></a>

### 6.6.1 Data models, calculation and projections

ED-DATA-001 — A Data project may contain workbooks, typed tables, sources/connections, saved queries, transformations, data models, cubes, pivots and visualisations without requiring separate projects for each paradigm. A spreadsheet coordinate is a location; independent tables, records, fields, relationships, queries and charts have stable IDs. Database sorting/filtering changes the view, not record identity. Native .9to1d can represent more than interchange spreadsheet formats; compatibility exports cannot dictate the entire native model.

ED-DATA-002 — Cells support typed scalar values, formulas and explicitly registered structured values: lists/sets, records, nested tables, canonical references and query-backed values. Collapsed summaries do not replace the stored value. Owned nested data follows the parent lifecycle; a relationship points to an independent record. Formula/filter/SQL behaviour for structured cells is explicit: return a typed result/error or require a named projection, never silently convert a complex object into comma-separated text. Expanding a nested value preserves cell/member identities and bounded navigation.

ED-DATA-003 — The calculation engine maintains dependencies and recalculates affected nodes in a valid order. Circular references, invalid references, unavailable functions, type errors and divide-by-zero remain distinguishable. Range edits invalidate their dependent formulas/charts rather than forcing unrelated workbooks to recalculate. Relative/absolute/mixed references, named ranges, table references, array/spill behaviour and copy/fill semantics follow the claimed compatibility profile. A spill blocked by occupied cells reports the target conflict without overwriting content. Manual and automatic calculation modes clearly identify stale displayed values.

ED-DATA-004 — Table schema defines field identity, type, nullability, defaults, validation, uniqueness and key/reference roles. A primary-key change validates duplicates, nulls and dependent relationships. Friendly lookup labels never replace stored stable keys. Relationships record source/target fields, cardinality, optionality and declared referential actions. The relationship designer and ER view project that same schema. Cascade changes preview the affected records where known; unknown impact stays unknown. Normalise proposes tables/keys/migrations with assumptions; Flatten/Denormalise is explicit. Neither tool blindly applies relational rules to intentionally hierarchical or multidimensional data.

ED-DATA-005 — A DataCube owns dimensions, members/hierarchies, measures and a fact/query definition. Slice/pivot selection chooses dimension members and aggregation without copying a separate sheet for every month or scenario. Projections state how missing values, totals and units behave. Pivot configurations retain source identity, row/column/value/filter fields and aggregation. Measures must not add incompatible units or average pre-aggregated percentages without an explicit calculation rule. Sorting/expanding a pivot never rewrites source record order.

<a id="h.47xm9jvaj4gs"></a>

### 6.6.2 SQL, transformation and analytical views

ED-DATA-006 — AI and SQL are explicit input modes. SQL executes natively against a visible selected target/dialect; it is not sent to a model to imitate execution. Query text, parameters, target and revision form an execution record with start/completion, typed results, affected rows/objects and errors. Changing sheet focus does not retarget a pending query. Preserve prior successful results when a new execution fails, visibly marking their execution identity. Results are paged/virtualised and can be materialised, exported or used by a pipeline without losing provenance.

ED-DATA-007 — Parse/classify every statement conservatively as proven read-only or potentially mutating. Multi-statement requests expose per-statement classification and atomicity/partial-execution limits. Manual read-only SQL follows connection permissions without unnecessary AI approval. Every AI-originated mutation still requires exact action review. Consequential live SQL identifies target, effect, dependencies and actual recovery strategy: transaction/savepoint, before-image, snapshot or other verified mechanism as supported. If required protection cannot be established, block execution. Lost acknowledgement enters reconciliation rather than blind retry under another identity.

ED-DATA-008 — Transform pipelines store ordered or graph-linked step IDs, typed inputs/outputs, schema expectations and source mappings. Supported steps include conversion, rename, split/merge, cleaning, filtering, derived fields, grouping, joins, union, pivot/unpivot, deduplication and missing-value handling. Simple step lists and graphs describe the same pipeline. A failed step exposes its input/error and leaves the prior valid output inspectable. Refresh pins source versions where available, detects schema drift and reconciles local edits instead of overwriting them. Snapshot conversion creates independent data; linked conversion retains mapping and refresh rules.

ED-DATA-009 — Visualise is an analytical lens, separate from inserting a persistent chart. Rank suitable visualisations from declared types/structure, then expose editable mappings for category, value, series, time, grouping, probability or rank. Support appropriate chart/distribution, tier, alignment, heatmap and other registered forms only when their input requirements hold. A chart/visualisation records sources, mappings, filters, aggregation and authored styling. Add data can reference another sheet/range without anonymous value copies. Selecting a mark resolves its underlying records where possible. Statistical anomalies remain suggestions rather than automatic data errors.

ED-DATA-010 — Import profiles declare delimiter/encoding, types, dates, locale, headers, missing values and source identity. Preview coercions and preserve rejected rows; dates and long identifiers must not silently become altered numbers. Spreadsheet-format compatibility covers formulas, values, styles, names, charts and unsupported constructs separately. Native/project exports preserve schema, relationships, cubes and queries without credentials. Cross-app conversion uses semantic source objects where available and reviews ambiguous mappings; screenshots/OCR are an explicitly different fallback with recorded uncertainty.

ED-DATA-011 — Workflow A: create a workbook with formulas, a spill, typed list field and a DataTable; sort records and verify stable relationships. Add a second dataset, build a pivot/visualisation, change one source and observe dependency-aware refresh. Introduce schema drift and a circular formula; valid results remain inspectable and errors specific. Export to XLSX/ODS and native formats according to the capability matrix, reopen and compare supported structures and reported losses.

ED-DATA-012 — Workflow B: connect a permitted local analytical target and a separate live test database. Run a read-only query in SQL mode, then draft an AI update, show exact target/impact/recovery, reject it and verify no mutation. Approve a different operation, lose the acknowledgement and reconcile its receipt without applying twice. Reject a required-backup failure. Build a Normalise proposal with a dependent foreign key and inspect the migration before any record changes.

<a id="h.3y6rursxh9c3"></a>

### 6.6.3 Forms schema, marking and publication

ED-FORMS-001 — FormProject owns pages/sections, fields/options, reusable components, mode definition, layout/theme, logic, question banks, marking and Data bindings. Fields have stable IDs and typed answer schemas; labels and question positions are not keys. Responses record FormVersionID and answers against that version. Choice options, table columns and rubric criteria retain independent IDs where addressable. Reusing a component instantiates its fields through an explicit identity map and controlled overrides rather than an opaque widget.

ED-FORMS-002 — The field catalogue includes typed text, numeric/currency, dates/times, choices, ratings/ranking, matrix/grid, upload, supported signature, person/reference/foreign-key lookup, calculated fields, mathematical/graph responses, repeating groups and TableInput. TableInput is a real collection of typed rows with per-column/cell/row validation and stable response-row IDs; it is not a multiple-choice grid or one stringified answer. Required, unknown, unavailable and deliberately blank values remain distinct. Dynamic hiding does not silently erase an answer without the configured policy being visible.

ED-FORMS-003 — Form, Test and Quiz are presets over one state/response engine. Form collects valid answers; Test applies whole-assessment marking/release rules; Quiz may evaluate and advance question by question. Custom modes define typed states, transitions, timers, scores and completion conditions through the shared graph. Simple conditional controls edit the same graph where representable. Validate missing targets, incompatible types, uncontrolled loops and trust-boundary violations before publication. Runtime visibility logic is not a security boundary for protected questions or answers.

ED-FORMS-004 — Marking rules can use accepted answers with declared normalisation, bounded regex, numerical tolerance/units, symbolic equivalence, structured rubric AI, human marking or a configured combination. Correct, Incorrect, Partial and NeedsReview remain distinct. Store rules/model/rubric version, awarded points, evidence/criteria and review provenance. AI uncertainty can require human review rather than inventing a definite grade. Regex testing declares full versus partial match and safe execution limits. Mathematical visual/LaTeX editing shares one expression; invalid input preserves the last valid version. Graph responses store points/functions/regions and marking tolerances rather than a screenshot.

ED-FORMS-005 — Question banks carry tags, topic/difficulty and marking metadata. Sampling/randomisation records the chosen question/option mapping per attempt so resume and later review see the same assessment. Timers, attempt limits and result-release policies belong to the versioned mode. Published revisions remain immutable for historical interpretation. Editing Draft does not affect respondents until deliberate publish; a failed publish leaves the prior version available. Migration of an in-progress attempt occurs only under an explicit safe rule, never by reinterpreting answers against new labels.

ED-FORMS-006 — A Data binding records direction, field/key mapping and synchronisation mode: manual, suggested, compatible automatic or strict. Repeating answers can create a parent plus related child records through a declared transaction/receipt plan. Forms submission and Data-write completion are separate statuses when atomic commitment is unavailable. Preserve accepted responses through downstream Data failure and expose retry/reconciliation. Schema deletion/type change reviews historical answers, published versions and logic; it cannot silently delete old evidence. AI changes affecting Data retain Data’s exact per-action approval requirement.

<a id="h.n6us8aocam9s"></a>

### 6.6.4 Live sessions and complete assessment journeys

ED-FORMS-007 — LiveSession references one FormVersion, participants/teams, state/round/question, authoritative clock/events and score policy. Joining/reconnecting preserves participant identity and submitted answers. Client clocks cannot award points, extend time, reveal answers or reinstate elimination. Lobby, active question, marking/reveal, transition and finished are explicit states. Buzzer/knockout/team policies define ordering, eligibility and aggregate scoring rather than relying on network arrival animations. AI marking latency follows a declared progression/fairness rule and cannot silently become a speed score.

ED-FORMS-008 — Integrity controls can restrict attempts, randomise questions/options and record declared focus/session events; such events are contextual evidence, not proof of misconduct. Sensitive capture/lockdown capabilities require their own configured supported permissions. Deliver protected questions/answers only when authorised. Host adjustments and moderation are attributed events. Accessible fields, equation/graph alternatives, predictable focus and restrained score announcements remain available in competitive modes. Study and other Spaces use the canonical assessment/response IDs; they do not fork marking or response storage.

ED-FORMS-009 — Workflow A: author a survey with a required field, conditional follow-up and repeating expense table, bind it to parent/child Data tables, preview without creating responses, then publish. Submit, interrupt the downstream Data write and recover without duplicate rows. Rename a field and publish another version; historical answers remain interpretable. Test an invalid condition and failed publish against the still-working active form.

ED-FORMS-010 — Workflow B: create a quiz with two accepted text answers, a mathematical expression, graph response and AI rubric item. Validate matching/tolerances, sample a question bank and run a live session with reconnect and late marking. Reject a client attempt to change its score; review an uncertain mark; release results under policy. Return through the Study Space to the same attempt and verify attributed evidence rather than a duplicated grade.

Reference detail — Atlas Data/Forms states; [data.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/data.complete.md)’s relational, SQL, Visualise and conversion contracts; [forms.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/forms.complete.md)’s typed modes, marking, publishing, Data integration and server-authoritative session contracts. Older fixed-colour, literal API signatures and standing worker instructions are not inherited.
