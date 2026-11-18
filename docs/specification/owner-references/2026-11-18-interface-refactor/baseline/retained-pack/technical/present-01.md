# Present — slide authoring and presentation — Floating rich toolbar; Primary actions versus inspector precision (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0024 · v2 lines 64–64

PRESENT-UX-102 — Floating rich toolbar. The toolbar MUST look like a single softly rounded floating CUI surface, separated subtly from the workspace with consistent margins/surface elevation; it MUST be predominantly icon-led, with modestly generous spacing, optically centred consistent icon hit areas, logical grouped separators, accessible names, shortcuts/tooltips and visible hover/focus/pressed/selected/disabled states. It MUST contain a substantial, useful set of daily commands rather than a sparse six-button strip. Stable primary groups include Undo/Redo, zoom/fit, selection, prominent + Insert, frequently used format/arrange options, inspector visibility and overflow. Contextual groups expose font family/type, font size and +/- sizing, bold, italic, underline, strikethrough, colour, text alignment, bullets/numbering, spacing, link and other applicable text commands, OR shape/image fill, outline, opacity, arrangement, align/distribute and other object-specific actions. Do not present irrelevant operations as enabled. Responsive width changes MUST use priority-based overflow instead of wrapping another toolbar row or clipping icons. Major commands MUST remain accessible in menus/shortcuts.

### V2-B0025 · v2 lines 67–67

PRESENT-UX-103 — Primary actions versus inspector precision. Common formatting operations MUST be directly usable from the toolbar and/or contextual command surfaces without repeatedly opening or scrolling an inspector. The optional resizable Properties inspector is for precise position/size/rotation, detailed typography, shape fill/stroke, image/crop options, slide settings and advanced configuration. Its sections MUST be logically grouped/collapsible as useful, with readable fields, appropriate labels, controlled scrolling and no arbitrary unrelated command dump (for example Import/Export/Present MUST NOT be inspector-only miscellany). The toolbar, inspector, context menu and keyboard shortcuts MUST be views of ONE authoritative canonical object/document state; changes must immediately synchronise and support logical undo. Routine changes such as fill colour SHOULD preview/apply directly instead of requiring a redundant Apply button. Popovers may use explicit Apply only when an actual staged operation justifies it.

### V2-B0026 · v2 lines 70–70

PRESENT-UX-104 — + Insert catalogue and shape gallery. A prominent + control MUST open a fully working, categorised Insert menu; the menu bar's Insert menu exposes the same domain actions. Cover all applicable donor/spec insertion types, including new slide/section/layout, text box, shapes, lines/arrows/connectors, images/media, tables, charts, links, comments and Generative UI/other canonical specialised objects according to feature-stage capability. Selecting Shapes MUST open a proper icon/preview gallery (e.g. rectangles, rounded rectangles, ellipses, polygons, lines, arrows, callouts, flowchart forms and other applicable donor-supported shapes); never insert a single default rectangle merely by opening Shapes. Selecting a shape MUST activate predictable draw-to-insert or explicit click-to-insert behaviour, with live preview, correct post-insertion selection and Escape cancellation. Visible actions MUST have real implementations or clearly truthful unavailable/capability states; no dead illustrative buttons.

### V2-B0027 · v2 lines 73–73

PRESENT-UX-105 — Layout and side surfaces. Keep the slide thumbnail/navigator on the left, the actual central 16:9 or document-aspect slide canvas visually dominant, optional contextual inspector on the right and collapsible/resizable speaker notes below. Thumbnails MUST use consistent mildly rounded corners, accurate previews, clear but restrained selected states, clean slide number/title spacing, readable truncated long labels and reliable thumbnail scrolling. The left navigation sidebar should be somewhat darker than the earlier medium-grey Present sidebar, consistent with the 9to1 dark palette; this is theme guidance, not a fixed hard-coded colour. Pane sizes and slide centring MUST respond to window, sidebar/inspector and notes changes without distorting the slide. The slide stage preserves aspect ratio, Fit-to-Window and real editable content; no tiny-slide regression or hard-coded zoom.

### V2-B0028 · v2 lines 76–76

PRESENT-UX-106 — Notes are real content. Each selected slide's existing notes MUST appear immediately from the canonical NotesID/SlideID mapping in a real editable speaker-notes surface; 'Add Notes' or equivalent is an empty-state placeholder ONLY when content is empty. Support focus, accessible caret, readable text, formatting consistent with the specified rich notes capability, correct switching across slides, autosave/save/reopen and failure feedback. Notes MUST not be replaced by a decorative card or lose state during navigation. Preserve the notes/Presenter View and export-privacy rules of the existing Present section.

### V2-B0030 · v2 lines 83–83

PRESENT-INT-102 — Explicit editor interaction state machine. MUST distinguish idle, hover/preselection, selected object, text-editing, moving, resizing, rotating, multi-selection, insertion, panning and zooming. Single click normally selects an object; double click or an appropriate keyboard command enters text editing; Escape exits editing/insertion or cancels an active gesture as appropriate. Dragging to select text in editing mode MUST NOT move the parent object. Dragging the body/border of a selected non-editing object MUST move it. Clicking empty slide space should clear selection predictably. Toolbar/inspector/selection UI MUST track the same state. Document object events and any runtime Actions events MUST not be confused with authoring gestures.

### V2-B0031 · v2 lines 86–86

PRESENT-INT-103 — Deterministic hit-testing and pointer capture. Define correct event-routing priority for open menus, handles/adorners, active text editor, selected object, frontmost other objects and empty slide. Respect z-order, locked/hidden states, overlapping objects, the clicked object and focus; do not let editable text intercept movement outside editing mode. Use correct pointer down/move/up, drag threshold, capture/release, cancellation and out-of-window handling. No jump at drag start: preserve pointer grab offset. Screen/window/viewport/slide/object coordinates and zoom/scroll/rotation transforms MUST be converted consistently, with no arbitrary pixel-offset patches or stale geometry.

### V2-B0032 · v2 lines 89–89

PRESENT-INT-104 — Hover outlines, selection geometry and handles. Hovering over a selectable object MUST show an unobtrusive preselection outline before click; a selected object MUST show a stronger accurate bounding outline and resize handles, clearly distinct from hover, without flicker or content mutation. For applicable rectangular resizable objects, expose eight handles (four corners and four side midpoints) plus rotation control where supported. Handles MUST have usable pointer hit targets, correct directional semantics and cursor feedback. Very narrow/tiny objects MUST NOT produce overlapping unusable handles; use an adapted adorner/hit target strategy. Outlines/handles MUST stay attached to actual selected geometry at all zooms, after transforms and across slide changes. The selection overlay MUST NOT be stored as slide content.

### V2-B0033 · v2 lines 92–92

PRESENT-INT-105 — Cursor and direct manipulation feedback. Provide distinct relevant default/select, text caret, move/grab, horizontal/vertical/diagonal resize, rotate, crosshair for shape insertion, pan/grabbing and unavailable cursor states; respect object rotation where appropriate. Moving, resizing, rotating and applicable object/formatting previews MUST update visibly DURING pointer gestures (not only after release), with accurate content, outlines and property previews. Keep gestures responsive without needless full-document reflow. Commit each gesture as one logical undoable operation; cancelling restores the preceding state. No unexpected movement, resizing, lost focus or selected-text modification.

### V2-B0034 · v2 lines 95–95

PRESENT-INT-106 — Selection and keyboard quality. MUST handle Shift-click multi-selection, suitable area selection, overlapping objects, group/ungroup, duplication, delete, copy/paste, z-order and keyboard nudging where required by the donor/spec. Keys obey state: arrows move a text caret when editing text, not the text-box object. Object transforms retain bounds; edits survive save/reopen with stable IDs. Provide right-click without breaking normal left-click or keyboard operations.

### V2-B0035 · v2 lines 98–98

PRESENT-INT-107 — Reliable scrolling and zoom. Normal mouse wheel scrolls the correct hovered scroll container (slide list, inspector, notes or oversized canvas), with appropriate event propagation rather than occasionally swallowing input. Ctrl+wheel over the editing canvas zooms incrementally; pinch zoom on supported trackpads behaves appropriately and must not confuse two-finger scrolling with zoom. Zoom SHOULD be anchored around the pointer location when practical, obey safe bounds, and share one state with the toolbar percent/Fit-to-Window controls. Changing zoom, scrolling, toggling panels or resizing window MUST NOT desynchronise object positions, hit-testing, resize handles or canvas centring. Fit-to-Window uses actual available viewport rather than arbitrary constants. Normal wheel behaviour outside the canvas remains panel scrolling.

### V2-B0045 · v2 lines 131–131

PRESENT-AC-102 — Functional acceptance scenes. In an identified real running build, verify: (1) create/open/save/close/reopen and truthful save status; (2) text select -> drag -> resize at all eight handles -> double-click edit -> text selection -> exit -> undo/redo; (3) thin, overlapping, rotated and edge-of-slide objects; (4) hover outline versus selected outline and cursor changes; (5) shape gallery -> choose distinct shapes -> live draw -> format -> duplicate -> delete; (6) toolbar formatting and inspector synchronisation; (7) right-click target-specific menus on objects, text, thumbnails and blank canvas; (8) notes existing content/placeholder, slide switching and persistence; (9) scrolling over each panel, Ctrl+wheel/pinch/toolbar zoom at fit/50/75/100/150/200% where supported; (10) layout/panel resize and correct fit; (11) presentation/playback and relevant import/export; (12) clipboard, accessibility, keyboard-only actions and realistic donor-parity journeys. Run actual pointer/keyboard interactions, not event-handler presence checks.

### V2-B0046 · v2 lines 134–134

PRESENT-AC-103 — Visual and integration evidence. Capture evidence of actual build identity (source commit, branch/worktree, package/route, dependencies), default shell, selected/edited text, selected shape, hover outlines/handles, + menu, multiple-shape gallery, right-click menus, font/colour popover, toolbar overflow, inspector, speaker notes, narrow and full windows, startup/theme transition, and zoomed canvas. Inspect real on-screen icon rendering, menu layering and selection geometry. Record passes/failures and timings; screenshots cannot alone verify interaction or CUI source compliance, tests cannot alone verify visual polish. A true independent reviewer must exercise the exact integrated artifact and apply existing seven APP-AC gates.

### V2-B0047 · v2 lines 137–137

PRESENT-AC-104 — Explicit fail conditions. Present FAILS usability/visual acceptance if it has a flat sparse toolbar with key format tools hidden in the inspector; insertion limited to a single default shape; no functional contextual right-click menu; selected-versus-edited text ambiguity; unreliable drag, cursor, hover, handle or scroll/zoom interactions; decorative notes or missing saved notes; broken SVGs; native theme leakage; white 5–10-second refresh without investigated resolution; or commands that only simulate success. An RCVP CHECKPOINT can be visible while work continues, but MUST NOT be labelled VERIFIED until its entire applicable baseline and the above essential user journeys pass on the real app. Full-stage enhancements remain governed by the existing four-part schedule.

### V2-B1350 · v2 lines 5757–5757

## 9. Present — routed editor, direct manipulation and playback

### V2-B1351 · v2 lines 5759–5759

### UX27-PRESENT-001 — Correct route and one implementation

### V2-B1352 · v2 lines 5761–5761

**O/B.** Trace the actual `9to1://present` registration to the current production surface and verify the installed build uses it. A badge or layout change in an unused standalone scene does not count. Preserve the 12 October Present amendments and all donor baseline capabilities; the source names PresentPage and PresentHavenScene are historical diagnostic leads, not permanent architecture requirements.

### V2-B1353 · v2 lines 5763–5763

Test the actual host activation and the standalone app against the same deck identity. A test that directly constructs a page and asserts a badge is present is useful unit evidence but is not proof of host route activation, visual correctness or a functioning presentation editor.

### V2-B1354 · v2 lines 5765–5765

### UX27-PRESENT-002 — Header, menu and editing toolbar

### V2-B1355 · v2 lines 5767–5767

**B/D.** Retain the specified compact document header, conventional menu row above the toolbar, and exactly one persistent primary floating editing toolbar. Group title/chevron, truthful save state and relevant history/collaboration actions coherently. Present is a prominent action. File/Edit/Insert/Slide/Arrange/View/Help open real menus; do not replace them with labels or scatter another ribbon below.

### V2-B1356 · v2 lines 5769–5769

Use the current approved Present identity/accent assets, not a hardcoded sample badge standing in for the full shell. Commands expose accessible names and contextual enabled/mixed states. Overflow preserves all commands without wrapping into an uncontrolled second permanent toolbar.

### V2-B1357 · v2 lines 5771–5771

### UX27-PRESENT-003 — Slide list, notes and canvas

### V2-B1358 · v2 lines 5773–5773

**B.** The left navigator contains real slide thumbnails and section structure, with selected state, slide order and context menus. The central slide remains visually dominant and preserves its actual aspect ratio under Fit/zoom. Notes are a real editable per-SlideID/NotesID surface, collapsible/resizable below; a placeholder appears only when notes are empty.

### V2-B1359 · v2 lines 5775–5775

Changing slide restores the correct notes, selection and content. Thumbnail generation follows the relevant deck revision and does not freeze editing. Adding/deleting/reordering a slide modifies the deck, not merely the UI list, and remains undoable/reopenable. The right inspector is optional and cannot squeeze the slide to an unusable postage stamp.

### V2-B1360 · v2 lines 5777–5777

### UX27-PRESENT-004 — Selection versus text editing

### V2-B1361 · v2 lines 5779–5779

**B/D.** The interaction state machine distinguishes hover, object selection, text editing, moving, resizing, rotating, insertion, multi-selection and panning. A single click selects the intended object; the supported double-click/keyboard action enters text editing. Text-range dragging in editing mode must not move the parent text box. Dragging the border/body in object-move mode preserves the pointer’s grab offset.

### V2-B1362 · v2 lines 5781–5781

Escape cancels the current insertion/gesture or exits text editing before clearing higher-level selection. Arrow keys move the text caret while editing and nudge an object only in the corresponding selection state. Context menus target the clicked object rather than a stale selection behind it.

### V2-B1363 · v2 lines 5783–5783

### UX27-PRESENT-005 — Handles, cursors and live transforms

### V2-B1364 · v2 lines 5785–5785

**B.** Applicable rectangular objects show eight resize handles and a rotation affordance where supported, with usable hit targets and directionally correct cursors. Hover outlines are distinct from selection. Tiny or narrow objects use an adapted hit-target strategy rather than overlapping handles. Moving/resizing/rotating updates content and adorners during the gesture, with correct zoom/scroll transforms.

### V2-B1365 · v2 lines 5787–5787

One finished gesture is one undo entry. Escape/cancel restores the previous geometry. Group/ungroup, multi-select, align/distribute and z-order operate on real object IDs. A selection frame must not detach from its object after viewport resize or theme change.

### V2-B1366 · v2 lines 5789–5789

### UX27-PRESENT-006 — Insertion catalogue and shape gallery

### V2-B1367 · v2 lines 5791–5791

**B/D.** Insert exposes slide/layout/section, text, shapes, images/media, tables, charts, links and other applicable registered types. Shapes opens a gallery of actual available forms with recognisable previews; merely opening the gallery must not insert a rectangle. Choosing a shape starts a defined draw-to-insert or explicit click placement, with preview and Escape cancellation.

### V2-B1368 · v2 lines 5793–5793

New objects enter a useful selected/editable state and can immediately be formatted, moved, saved and reopened. A gallery item without a real engine action is disabled with an honest reason or absent until supported, not treated as implemented insertion.

### V2-B1369 · v2 lines 5795–5795

### UX27-PRESENT-007 — Formatting and shared state

### V2-B1370 · v2 lines 5797–5797

**B/D.** Toolbar, inspector, context menu and shortcuts operate on one authoritative object/document state. Text controls include supported font, size, emphasis, colour, alignment, lists and spacing; object controls include fill/outline/opacity and geometry. Common formatting applies/previews directly where appropriate; an unnecessary Apply button is not required for every colour change.

### V2-B1371 · v2 lines 5799–5799

Mixed selections show mixed state and apply only valid shared operations. Changing a slide’s theme/layout/master uses donor-backed semantics, preserving custom content and providing undo. Do not flatten an editable object to an image to avoid handling its properties.

### V2-B1372 · v2 lines 5801–5801

### UX27-PRESENT-008 — Present, presenter view and recovery

### V2-B1373 · v2 lines 5803–5803

**B/D.** Present starts playback from the chosen slide using the actual deck, with reliable navigation, exit and supported media/transition behaviour. Presenter View shows the proper notes/current-next slide information on its intended display without leaking notes into audience output. Stopping playback returns to the same editor/slide/selection.
