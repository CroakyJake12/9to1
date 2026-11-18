# Boards — seamless pages, blocks and ink — Backspace/Delete at boundaries; Lists, toggles and nesting (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1450 · v2 lines 6669–6679

| Context | Enter | Shift+Enter | Empty-block continuation |
|---|---|---|---|
| Paragraph | Split into following paragraph | Soft break in same block | Creates the next editable paragraph |
| Heading H1–H6/custom heading | Split; following block is a paragraph | Soft break in same heading | Leaves an empty paragraph ready for body text |
| Bullet/numbered list | New sibling item at the same depth; numbering recomputes | Soft break in current item | At root, exit to paragraph; nested empty item outdents one level |
| Checklist item | New unchecked sibling; do not inherit checked state | Soft break in current item | Same exit/outdent rule as lists |
| Quote/callout text | Continue that container’s text/block contract | Soft break | Explicit exit command or second empty boundary action leaves the container without losing its children |
| Code block | Insert a code newline with applicable indent | Code newline, unless an explicit editor binding differs | Never silently create a page block for a normal code newline |
| Simple-table cell | Edit the cell’s text/paragraph according to the shared table editor | Soft break in cell | Never insert an outer page block merely by pressing Enter in a cell |
| Page title/rename field | Commit title and focus the intended page editor | No hidden page-tree mutation | Validate an empty title through the title contract |
| Search/slash/menu query | Activate highlighted result when valid | Does not mutate document unexpectedly | No result means no hidden insertion |

### V2-B1451 · v2 lines 6681–6681

Expose an explicit keyboard command to exit code/table/embedded editors to a following page block. Do not globally bind Enter above the active editor and thereby break these exceptions.

### V2-B1452 · v2 lines 6683–6683

### UX27-BOARD-020 — Backspace/Delete at boundaries

### V2-B1453 · v2 lines 6685–6685

**B/D.** Backspace inside text deletes the preceding grapheme/range. At the start of a compatible paragraph, it merges into the preceding text block and places the caret at the join. At the start of a formatted list item, first perform the specified outdent/type transition rather than deleting a parent subtree. Forward Delete at the end uses the corresponding safe merge rule.

### V2-B1454 · v2 lines 6687–6687

Non-text blocks, locked embedded artifacts and incompatible block types must not be silently destroyed by a boundary merge. Select the adjacent non-text block or expose a clear deletion action according to its contract. Preserve nested children, inline marks, references and undo state. A broad key handler that deletes the whole block whenever the caret is at zero fails this requirement.

### V2-B1455 · v2 lines 6689–6689

### UX27-BOARD-021 — Lists, toggles and nesting

### V2-B1456 · v2 lines 6691–6691

**B/D.** Bullets, ordered lists and checklists retain explicit item type, depth, parent and order. Tab/Shift+Tab indent/outdent where a valid parent exists; the command must not insert literal tab characters into ordinary list text. Empty nested-list Enter outdents one level, not all the way to root. Numbering changes after reorder without rewriting item IDs. Completing a checklist toggles its checked state and does not move focus to a different item.

### V2-B1457 · v2 lines 6693–6693

Toggle disclosure expands/collapses its child content without changing that content’s stored existence. Child selection and focus must remain coherent when a parent collapses. Copying/moving a selected parent carries its child subtree; converting a container to a type unable to host children must preserve them through an explicit, reversible restructuring rather than drop them.

### V2-B1458 · v2 lines 6695–6695

### UX27-BOARD-022 — Slash insertion is a real contextual menu

### V2-B1459 · v2 lines 6697–6697

**B/D/R.** In an eligible text block, typing `/` at the defined insertion point opens a searchable block/action catalogue anchored near the caret. Show category headings, recognisable icons, names, short descriptions where useful and keyboard focus. Typing narrows results without losing the text range used for the command. Escape dismisses and leaves ordinary typed content intact unless the user explicitly cancels the command token.

### V2-B1460 · v2 lines 6699–6699

Selecting a block type replaces only the slash-command token/current eligible empty block or inserts at the specified boundary; it does not clear unrelated paragraph content. URLs and code containing slashes must not accidentally open the page insertion menu. Inline + and toolbar Insert use the same registered catalogue and target semantics. Unsupported types are truthfully unavailable, not decorative examples.

### V2-B1461 · v2 lines 6701–6701

### UX27-BOARD-023 — Six-dot grip: click versus drag

### V2-B1462 · v2 lines 6703–6703

**O/B/D/R.** Every eligible block exposes its six-dot grip on hover and keyboard focus, with an accessible “Block actions / move block” affordance. Clicking without a drag opens the block menu. Pointer-down followed by movement beyond the platform drag threshold starts a drag; where no platform threshold is supplied, use a 4-unit logical default. Starting a drag suppresses the click-menu action on release. Text-body drag selects text unless an explicit spatial-move tool is active.

### V2-B1463 · v2 lines 6705–6705

Use a stable hit area of at least 28×28 logical units in compact pointer mode, larger in touch mode. Pointer capture continues the gesture when leaving the glyph’s tiny visual bounds. Show a grab/grabbing cursor and a meaningful preview. An icon with no drag behaviour is a failed feature, not a finished grip.

### V2-B1464 · v2 lines 6707–6707

### UX27-BOARD-024 — Flow-block reordering and drop feedback

### V2-B1465 · v2 lines 6709–6709

**O/B/D.** While dragging a flow block, remove no canonical data until a valid drop commits. Show a translucent preview of the actual selected block/subtree and an insertion line between candidate siblings. Before/after targets are distinct from child/column targets; do not convert to absolute positioning merely because the pointer moved sideways. The source placeholder preserves reasonable layout without expanding to the full window height.

### V2-B1466 · v2 lines 6711–6711

On a valid drop, move the existing IDs and their children once, update ordering, focus/reveal the moved block and save the operation. Invalid targets show unavailable feedback. Escape, pointer cancellation or dropping outside valid targets restores the original order. A drag cancelled after scrolling must not leave duplicated blocks or a stuck capture layer.

### V2-B1467 · v2 lines 6713–6713

### UX27-BOARD-025 — Nesting, columns and drag auto-scroll

### V2-B1468 · v2 lines 6715–6715

**B/D/R.** Child nesting is offered only on types that accept children and must show the intended parent/indentation before drop. Column creation uses a distinct lateral drop zone and preview, rather than guessing from any horizontal pointer motion. The default lateral target band is 24 logical units where space allows; narrow layouts use an explicit Move/Columns menu instead of unreachable zones. Moving content out of a column removes an empty column container only through a reversible structural operation.

### V2-B1469 · v2 lines 6717–6717

Within 32 logical units of a scrollable edge, drag auto-scroll accelerates gradually up to a default 720 logical units/second; it stops when leaving the edge or cancelling. Hovering an eligible collapsed tree/container target for 600 ms may expand it without committing a move. These timings are D defaults. Retain correct target IDs under virtualisation and do not select a recycled row with stale identity.

### V2-B1470 · v2 lines 6719–6719

### UX27-BOARD-026 — Keyboard block movement and multi-selection

### V2-B1471 · v2 lines 6721–6721

**B/D.** The block menu offers Move up, Move down, Move to and permitted indent/outdent operations. Registered keyboard equivalents must be discoverable and must not conflict with active text/IME commands. Escape from ordinary text editing can select the current block; further navigation follows the defined selection model rather than stealing the first Escape from an open popup.

### V2-B1472 · v2 lines 6723–6723

Multi-block selection preserves contiguous or explicit selected BlockIDs. Dragging the selected group maintains its internal order and selected subtrees; do not move a child twice because both it and its ancestor appear in selection. Cut/paste uses the canonical object bundle, preserving supported types and remapping IDs only when copying. Clipboard failure leaves the source intact.

### V2-B1473 · v2 lines 6725–6725

### UX27-BOARD-027 — Block context menu

### V2-B1474 · v2 lines 6727–6727

**B/D/R.** Clicking the grip opens actions for that exact block: supported type conversion, Move To, duplicate, copy link/reference, copy/cut, delete/recover, colour/style and contextual object actions. Show only valid operations or an explicit disabled reason. A page link, image, code block and table must not all expose the same generic text menu.

### V2-B1475 · v2 lines 6729–6729

Duplicate creates new owned identities with equivalent content and legitimate external references; it must not alias a supposedly independent copy onto the original mutable object. Copy link targets the stable block, surviving reorder/rename. Delete is undoable and follows the owning artifact’s deletion policy. A menu opened before a remote revision is revalidated before a consequential mutation.

### V2-B1476 · v2 lines 6731–6731

### UX27-BOARD-028 — Text selection and floating formatting island

### V2-B1477 · v2 lines 6733–6733

**O/B.** Keep the top floating editing toolbar. For text it exposes current Paragraph/H1–H6/custom style; B/I/S/U; font/size where supported; colour/highlight; alignment; lists/checklist; link; Undo/Redo; and Insert through coherent groups. The active style and mixed-format state follow the real caret/selection, not a cached label from a previous block. The toolbar does not become a second document row or scroll offscreen accidentally.

### V2-B1478 · v2 lines 6735–6735

Applying Bold to two selected words changes those words, not the entire block, and retains selection. Converting paragraph to heading preserves text/valid marks and stable identity. A font/colour popup previews only according to the shared transaction policy and Escape restores a staged preview. Routine formatting must not require opening a distant Properties panel.

### V2-B1479 · v2 lines 6737–6737

### UX27-BOARD-029 — Native page, explicit Canvas page and drawing layer

### V2-B1480 · v2 lines 6739–6739

**O/B.** A Native page is the combined flow/spatial page described by V2. Its DrawingLayer is integrated into the same document-space viewport. There must not be an automatically appended “Freeform canvas” section, duplicate empty-state text, independent white scrollbox or “+ Box” diagnostic region below every paragraph. Empty Native pages show an unobtrusive editing hint once, not a second editor application.

### V2-B1481 · v2 lines 6741–6741

A user-created Canvas PageType or deliberately inserted Canvas artifact remains valid and uses its canonical engine. Such content has explicit identity, purpose, bounds and open/edit controls. Removing the unintended panel must not remove the page’s ink layer, legitimate absolute-position blocks or intentional embedded artifacts.

### V2-B1482 · v2 lines 6743–6743

### UX27-BOARD-030 — Edit/View, layout locking and snap are separate

### V2-B1483 · v2 lines 6745–6745

**O/B/D.** Keep three independently understandable concepts: Edit/View controls authoring permission at the surface; Locked/Unlocked controls casual spatial block movement; Snap to grid controls optional spatial snapping. A page can be Edit + Locked with working text editing, flow reorder and pen drawing. View prevents content/ink mutation but may retain explicitly non-persistent graph interaction. Unlocked does not grant permission to edit a read-only board.

### V2-B1484 · v2 lines 6747–6747

Display the persistent Edit/View control in the approved top-right page chrome and keep layout/snap controls discoverable in the editing controls. Do not silently remove them when a different object is selected. Disabled states explain why. Persist layout preference appropriately without confusing it with a server-side permission grant.

### V2-B1485 · v2 lines 6749–6749

### UX27-BOARD-031 — Absolute placement and snap semantics

### V2-B1486 · v2 lines 6751–6751

**B/D.** Unlocked mode permits an explicit Move freely/Place freely gesture to convert a flow block to an Absolute block with document-space X/Y/Width/Height/ZOrder. A normal six-dot flow reorder remains a flow reorder. Converting back to flow is a separate undoable action with a visible insertion location. Relocking preserves absolute coordinates exactly and only disables casual spatial handles.

### V2-B1487 · v2 lines 6753–6753

Snap affects active spatial moves/resizes, not typing, handwriting points or ordinary flow ordering. Use the page’s documented grid spacing and show alignment guides while relevant. Toggling snap off must permit continuous placement; toggling it on must not immediately move every existing object. Store precise coordinates, not rounded screen pixels. Grid visibility and snap enablement are separate settings.

### V2-B1488 · v2 lines 6755–6755

### UX27-BOARD-032 — Selecting the pen must actually enable drawing

### V2-B1489 · v2 lines 6757–6757

**O/B.** Pen selection changes the active authoring tool and its cursor/toolbar state. In Edit mode, pointer-down inside the page begins a real stroke, pointer movement renders it continuously, and pointer-up commits one stroke with a stable ID. Drawing works in both Locked and Unlocked layout modes. A selected pen icon that produces no ink is an acceptance failure, even when unit tests or a separate Rnote demonstration pass.

### V2-B1490 · v2 lines 6759–6759

The pen must operate on the actual routed Boards page using the integrated shared Rnote-derived implementation. Verify with mouse-drawn strokes as a baseline and with an actual supported stylus where available. A missing native library, wrong architecture, unregistered renderer or input handler attached to an unused scene must be diagnosed as an implementation/package failure, not hidden behind successful toolbar selection.

### V2-B1491 · v2 lines 6761–6761

### UX27-BOARD-033 — Stroke coordinates, samples and capture

### V2-B1492 · v2 lines 6763–6763

**B/D.** Convert pointer samples through the current screen-to-page transform at the time of the event, including DPI, viewport origin, scroll, zoom and any relevant page transform. The rendered preview and stored stroke use the same document coordinates. Pointer capture follows the active pointer until release/cancel; crossing a toolbar does not begin drawing on the toolbar or create a long line to the next unrelated pointer event.

### V2-B1493 · v2 lines 6765–6765

Use supported coalesced samples and pressure/tilt metadata without inventing values on unsupported devices. A pointer cancel/lost capture completes or cancels the current stroke under the engine’s documented policy, clears all transient state and never joins the next stroke to the previous one. Pen and mouse IDs are not interchangeable. Test two separated strokes, window exit/re-entry and zoomed drawing for spurious connectors and offsets.

### V2-B1494 · v2 lines 6767–6767

### UX27-BOARD-034 — Pressure, palm rejection and non-stylus fallback

### V2-B1495 · v2 lines 6769–6769

**B/D.** When pressure exists, map it smoothly through the selected brush’s pressure curve within configured width bounds. Where unavailable, use the chosen fixed width; do not make a mouse stroke invisible because its pressure field is zero/missing. Supported tilt and eraser-tip input are capability-negotiated. Surface honest unavailable controls rather than pretending every device supports every pen feature.

### V2-B1496 · v2 lines 6771–6771

An active pen can reject touch contacts identified as palms while preserving intended touch navigation under the selected input mode. Do not discard all touch interaction unconditionally. A “pen-only ink” preference distinguishes drawing from touch pan. Stylus-specific acceptance remains NOT RUN when only synthetic/mouse input was available.

### V2-B1497 · v2 lines 6773–6773

### UX27-BOARD-035 — Pen controls, eraser and undo

### V2-B1498 · v2 lines 6775–6775

**O/B/D.** Drawing context exposes pen/highlighter, eraser, selection/lasso, stroke width and colour through a compact contextual group. Show the active tool and chosen width/colour; changing a control affects subsequent strokes, not every old stroke unless those strokes are explicitly selected. Eraser mode clearly distinguishes whole-stroke and segment erasure if both are supported.

### V2-B1499 · v2 lines 6777–6777

A completed stroke is one undo transaction. Erasing, transforming or recolouring a selected stroke is independently undoable. Clear page/drawing requires an explicit scoped action; it must not be the accidental result of pressing Escape or selecting another tool. Undo/Redo, save/reopen, theme changes and page switches preserve actual editable ink rather than rasterising it into a background image.

### V2-B1500 · v2 lines 6779–6779

### UX27-BOARD-036 — Ink versus text interaction

### V2-B1501 · v2 lines 6781–6781

**B/D.** In text/select mode, ordinary pointer gestures select text or blocks and do not create ink. In Pen mode, pointer input on the page draws rather than unexpectedly selecting a text range; keyboard navigation can still reach tool controls. Switching tools preserves the prior caret/selection as appropriate without committing a phantom stroke. View mode refuses new strokes with clear disabled feedback.
