# 9to1 V2 — Acceptance scenarios, 27 October 2026

**102 authored scenarios. All are NOT RUN.** These are specifications for testing, not claims that tests were executed. Read UX27-ACCEPT-001–006 for exact-artifact, independence, parameter and evidence rules. Use disposable profiles; never mutate the owner’s live documents.

## Boards

### UX27-TEST-001 — Create without Files, account or cloud

**Requirements:** UX27-BOARD-003, UX27-DATAFLOW-001 · **Status:** NOT RUN

**Given:** Disposable profile; no Files connection, accounts, AI provider or designated Boards folder.

**When:**
1. Launch Boards through normal Apps entry.
2. Activate New Board, type a title and a sentence.
3. Close and reopen using the normal application route.

**Then:**
- Create is visible and usable before any connection.
- A stable local board/page is created, edits are durable, and reopening restores them.
- No mandatory folder, cloud or Files-connection dialog appears.

### UX27-TEST-002 — Cancel Save As retains the draft

**Requirements:** UX27-BOARD-004, UX27-BOARD-005 · **Status:** NOT RUN

**Given:** Unnamed local board containing text, ink and a second page.

**When:**
1. Invoke Save As.
2. Browse to a different folder, then cancel.
3. Continue editing; close and reopen the draft.

**Then:**
- No chosen destination is falsely recorded as saved.
- All content remains available with truthful local-draft status.
- Cancelling the chooser neither deletes the draft nor sends the user through Files setup.

### UX27-TEST-003 — Save an editable file outside a Boards folder

**Requirements:** UX27-BOARD-005, UX27-BOARD-006 · **Status:** NOT RUN

**Given:** A mixed board; a permitted arbitrary test folder unrelated to the app library.

**When:**
1. Save As to that folder with extension .9to1board.
2. Inspect the actual written artifact.
3. Close the board and open that file through File Open and the OS association.

**Then:**
- The file exists at the selected location and is an editable document, not an image or account-dependent pointer.
- Page hierarchy, rich blocks, tables, graphs, ink and owned assets round-trip.
- No silent relocation into a special folder occurs.

### UX27-TEST-004 — Moved-file identity and fresh access

**Requirements:** UX27-BOARD-007, UX27-BOARD-009 · **Status:** NOT RUN

**Given:** A saved board with captured BoardID/PageIDs and no connected Files account.

**When:**
1. Close its handles.
2. Move/rename the file using authorised OS operations.
3. Open the new path and restart the app again.

**Then:**
- Logical board/page identities remain consistent.
- A new permitted handle is acquired instead of replaying a prior one-shot lease.
- Recent-location repair does not duplicate pages or create a dead tab.

### UX27-TEST-005 — Save a Copy is an independent document

**Requirements:** UX27-BOARD-007 · **Status:** NOT RUN

**Given:** A saved board with owned objects and one explicitly external reference.

**When:**
1. Use the deliberate Save a Copy/Duplicate action.
2. Edit the copy and reopen both documents.

**Then:**
- Copy ownership/IDs follow the duplication contract without accidentally merging the documents.
- Original content remains unchanged.
- External references remain explicit and access-controlled rather than being secretly copied.

### UX27-TEST-006 — Crash during save preserves a complete revision

**Requirements:** UX27-BOARD-008, UX27-DATAFLOW-004 · **Status:** NOT RUN

**Given:** Disposable board with a known valid file and a second modified revision; controllable storage fault injection.

**When:**
1. Interrupt before final file replacement, then reopen.
2. Repeat around the final commit boundary.
3. Inspect recovery and file integrity.

**Then:**
- The old complete or new complete revision opens, not a partial mixture.
- Unsaved recoverable edits remain available and failure is truthful.
- Saved is never announced merely because serialization began.

### UX27-TEST-007 — Disk-full, read-only and disconnected destination

**Requirements:** UX27-BOARD-008, UX27-SHARED-015 · **Status:** NOT RUN

**Given:** Parameterised authorised failing destination; a board with new unsaved edits.

**When:**
1. Attempt Save to disk-full/read-only/disconnected storage.
2. Choose a different permitted destination via Save As.
3. Reopen and compare content.

**Then:**
- Failure identifies the actual destination and cause; edits remain intact.
- Recovery copy is not labelled as successful sync to the failed destination.
- New destination contains the complete editable revision.

### UX27-TEST-008 — External edit conflict

**Requirements:** UX27-BOARD-008, UX27-BOARD-046 · **Status:** NOT RUN

**Given:** Two authorised editors of one disposable board at the same base revision.

**When:**
1. Edit different and then overlapping content in separate runs.
2. Attempt conflicting saves and inspect reconciliation.

**Then:**
- Revision conflict is detected rather than silently choosing the latest timestamp.
- Both revisions are recoverable; chosen resolution is explicit.
- Undo and references remain coherent after resolution.

### UX27-TEST-009 — Portable content versus external references

**Requirements:** UX27-BOARD-006, UX27-DATAFLOW-006 · **Status:** NOT RUN

**Given:** Board with owned image/ink/embedded content and a private external referenced artifact.

**When:**
1. Save and open the file in a clean authorised profile.
2. Inspect owned content and unavailable external target.
3. Inspect package contents for secrets.

**Then:**
- Owned supported content opens portably.
- External access is requested or marked unavailable without copying private material or deleting the reference.
- No credentials, bearer tokens or absolute-path-only identity are packaged.

### UX27-TEST-010 — Landing, recents and real transitions

**Requirements:** UX27-BOARD-010, UX27-SHARED-017 · **Status:** NOT RUN

**Given:** A fresh profile and another profile with an existing board plus an unavailable recent file.

**When:**
1. Open the landing page in both profiles.
2. Search and open a real recent item.
3. Activate Locate/Remove from Recent for the unavailable item.

**Then:**
- Landing and editing are distinct routed experiences.
- No seeded fake boards or duplicated Library heading.
- Removing a Recent item does not delete the actual document.

### UX27-TEST-011 — Exact agreed shell topology

**Requirements:** UX27-BOARD-001, UX27-BOARD-002, UX27-SHARED-005 · **Status:** NOT RUN

**Given:** Board with enough nested pages to overflow the sidebar.

**When:**
1. Open the board at wide and narrow widths.
2. Scroll the tree.
3. Change theme while retaining the same selected page.

**Then:**
- Top toolbar stays; Boards identity is top-left; no top-right AppFlowy product badge.
- Add page/section and user controls remain in the fixed bottom area.
- Theme changes do not rearrange this topology or lose page state.

### UX27-TEST-012 — Add page/section chooses the correct destination

**Requirements:** UX27-BOARD-014 · **Status:** NOT RUN

**Given:** A board with a section and a nested page.

**When:**
1. Open bottom Add; choose New section.
2. Open Add again, choose New Native page and an explicit nested destination.
3. Cancel a third creation attempt.

**Then:**
- Exactly one entity is created per confirmed operation at the shown destination.
- Tree reveals it and focus reaches title/editor.
- Cancel creates no hidden node; template browsing is not compulsory.

### UX27-TEST-013 — Tree focus, expansion and rename

**Requirements:** UX27-BOARD-012, UX27-BOARD-013 · **Status:** NOT RUN

**Given:** Three-level tree with stable IDs and an active nested page.

**When:**
1. Navigate with arrows/Home/End.
2. Collapse the active page ancestor without activating another page.
3. Rename a row; test Enter, Escape and invalid input.

**Then:**
- Expansion/focus and activation are distinct.
- Collapse retains the open descendant and its data.
- Committed rename updates all title/breadcrumb views without changing identity; Escape restores it.

### UX27-TEST-014 — Move pages without cycles or orphaning

**Requirements:** UX27-BOARD-015 · **Status:** NOT RUN

**Given:** Nested sections/pages with references and descendants.

**When:**
1. Drag a page above/below a sibling and into a valid parent.
2. Attempt to drop a parent into its own descendant.
3. Undo the valid move and cancel another drag.

**Then:**
- Drop preview predicts the stored destination.
- IDs/content/descendants survive; cycles are rejected visibly.
- Undo/cancel restores structure without extra nodes.

### UX27-TEST-015 — Trash and restore descendants

**Requirements:** UX27-BOARD-015, UX27-DATAFLOW-006 · **Status:** NOT RUN

**Given:** Section containing edited pages and an external reference.

**When:**
1. Trash the section after reviewing impact.
2. Restore it; repeat after its original parent is unavailable.

**Then:**
- Deletion scope is clear and recoverable.
- Descendants retain content and IDs.
- Restore returns to the original or explicitly recovered location; external original artifacts are not deleted by removing a reference.

### UX27-TEST-016 — Board switching is atomic

**Requirements:** UX27-BOARD-011 · **Status:** NOT RUN

**Given:** Two boards with different pages, drafts and model context.

**When:**
1. Switch through the searchable board selector.
2. Switch back.
3. Attempt to open an unavailable target.

**Then:**
- Title, tree, content and context always belong to the same board.
- Each board retains its selected page/draft/scroll state.
- Failed target opening leaves the current usable board intact.

### UX27-TEST-017 — Enter splits in the middle of a paragraph

**Requirements:** UX27-BOARD-016, UX27-BOARD-017 · **Status:** NOT RUN

**Given:** One paragraph ABCDEF with caret between C and D; capture its BlockID.

**When:**
1. Press Enter once.
2. Inspect the two block entities and place the caret in each.
3. Undo and Redo.
4. Save/reopen.

**Then:**
- Original ID holds ABC; one new sibling holds DEF; caret starts at the new sibling.
- Both blocks have functioning independent grips.
- Undo/Redo and reopen preserve exactly the expected structure, not a tall multiline input.

### UX27-TEST-018 — Enter at start, end and with selected text

**Requirements:** UX27-BOARD-017 · **Status:** NOT RUN

**Given:** Parameterised paragraph with caret at start/end or a selected inline range.

**When:**
1. Press Enter in each case.
2. Inspect text, inline marks, IDs and caret.
3. Undo once.

**Then:**
- Start/end follow the declared split policy; selection is deleted and split atomically.
- Rich text survives without duplicate or missing characters.
- One Undo restores the original content/tree/caret.

### UX27-TEST-019 — Shift Enter and wrapping do not create blocks

**Requirements:** UX27-BOARD-018 · **Status:** NOT RUN

**Given:** One paragraph near the flow-column width with a captured BlockID.

**When:**
1. Insert Shift+Enter, then type enough text to wrap.
2. Resize the window.
3. Save/reopen.

**Then:**
- Soft break and wrapping remain in the same BlockID.
- Resizing changes layout only, not model content/block count.
- No giant permanent blank region is inserted.

### UX27-TEST-020 — IME and grapheme-safe splitting

**Requirements:** UX27-BOARD-018 · **Status:** NOT RUN

**Given:** Text containing emoji, combining characters and a supported IME composition.

**When:**
1. Commit an IME candidate with Enter.
2. Perform an intentional structural split afterwards.
3. Undo/Redo and inspect text.

**Then:**
- Composition commit is not also a block split.
- Intended split respects grapheme boundaries and creates the specified structure.
- No corrupted surrogate pairs, duplicate input or accidental loss.

### UX27-TEST-021 — Heading, list and checklist continuation

**Requirements:** UX27-BOARD-019, UX27-BOARD-021 · **Status:** NOT RUN

**Given:** Heading, bullet, numbered list, nested list and checked checklist fixtures.

**When:**
1. Press Enter mid/end and on empty items.
2. Test Shift+Enter and nested empty-item exit.

**Then:**
- Headings continue into paragraphs under the stated default.
- Lists retain appropriate depth/numbering; new checklist item is unchecked.
- Empty root items exit and nested empty items outdent without deleting children.

### UX27-TEST-022 — Code, title and cell Enter stay contextual

**Requirements:** UX27-BOARD-019, UX27-BOARD-038 · **Status:** NOT RUN

**Given:** One code block, page-title field and editable table.

**When:**
1. Use Enter/Shift+Enter in each focused context.
2. Use the explicit exit-to-page command.

**Then:**
- Code Enter creates a code line rather than an outside block.
- Title Enter commits/focuses deliberately.
- Cell editing follows its contract; no accidental outer block or hidden navigation.

### UX27-TEST-023 — Backspace/Delete across structural boundaries

**Requirements:** UX27-BOARD-020 · **Status:** NOT RUN

**Given:** Adjacent compatible paragraphs, nested content and a selected non-text block.

**When:**
1. Backspace at the second paragraph start.
2. Undo; Delete at the first paragraph end.
3. Attempt boundaries around structured/non-text content.

**Then:**
- Compatible text merges preserve marks and intended caret placement.
- Structured content is selected/handled explicitly, not silently flattened or destroyed.
- Undo restores exact IDs/relationships according to the defined operation.

### UX27-TEST-024 — Slash menu inserts only on confirmation

**Requirements:** UX27-BOARD-022 · **Status:** NOT RUN

**Given:** An empty paragraph and a paragraph with existing text.

**When:**
1. Open slash insertion, filter results, navigate by keyboard.
2. Confirm a valid block, then repeat and cancel.
3. Click outside while the menu is open.

**Then:**
- Menu has relevant icon-led choices and current block targeting.
- Confirmation inserts one actual block.
- Cancelling/outside dismissal preserves pre-menu content and does not leave a stuck overlay.

### UX27-TEST-025 — Six-dot click versus actual drag

**Requirements:** UX27-BOARD-023, UX27-BOARD-024 · **Status:** NOT RUN

**Given:** Three flow blocks with recorded IDs/order.

**When:**
1. Click a grip without crossing its drag threshold.
2. Close the menu; drag the grip beyond threshold and drop below another block.

**Then:**
- Click opens that block menu; drag shows a real preview and insertion target.
- Drop changes stored order once and preserves the moved ID/content.
- A decorative six-dot icon or text selection instead of movement fails.

### UX27-TEST-026 — Cancel drag and preserve undo

**Requirements:** UX27-BOARD-024, UX27-SHARED-013 · **Status:** NOT RUN

**Given:** Multi-line block in a page with history.

**When:**
1. Drag across several targets, then press Escape.
2. Repeat with a valid drop and Undo once.

**Then:**
- Cancelled preview causes no persistent reorder.
- Valid movement commits once, with one logical undo step.
- No duplicate blocks or delayed mutation after cancellation.

### UX27-TEST-027 — Nesting and columns have explicit targets

**Requirements:** UX27-BOARD-025 · **Status:** NOT RUN

**Given:** Several compatible flow blocks and a container with children.

**When:**
1. Drag to a sibling insertion target.
2. Drag into the explicit nesting zone.
3. Create an allowed side-by-side column target, then resize its divider.

**Then:**
- Insertion, nesting and column targets are visibly distinct.
- Parent/order/column state match the preview and preserve content.
- No accidental nesting from merely crossing a label.

### UX27-TEST-028 — Drag auto-scroll through virtualised content

**Requirements:** UX27-BOARD-025, UX27-SHARED-020 · **Status:** NOT RUN

**Given:** Large board whose destination is off screen and partially unmaterialised.

**When:**
1. Start a grip drag; hover near the page edge.
2. Navigate to the distant target and drop.
3. Reopen and search for moved content.

**Then:**
- Controlled auto-scroll exposes targets without freezing or losing capture.
- Unrendered blocks are not omitted.
- Correct distant order persists with the same moved BlockID.

### UX27-TEST-029 — Keyboard and multi-block movement

**Requirements:** UX27-BOARD-026 · **Status:** NOT RUN

**Given:** Several adjacent and nested blocks, plus a text selection.

**When:**
1. Select multiple blocks through the documented block-selection mode.
2. Move through a keyboard/context alternative.
3. Repeat in text-edit mode.

**Then:**
- Block movement retains relative order and descendants.
- Keyboard users can perform equivalent movement without a mouse.
- Text-edit arrows/shortcuts do not unexpectedly move blocks.

### UX27-TEST-030 — Block menu targets clicked identity

**Requirements:** UX27-BOARD-027 · **Status:** NOT RUN

**Given:** Different block types; focus initially in another block.

**When:**
1. Open a block context menu after scrolling.
2. Duplicate or convert where supported; Undo.
3. Invoke a disabled action and inspect its reason.

**Then:**
- Commands target the clicked/focused BlockID, not stale selection.
- Conversion is content-preserving or explicitly explains incompatibility.
- Disabled commands do not mutate; menu keyboard/outside dismissal works.

### UX27-TEST-031 — Floating formatting preserves text selection

**Requirements:** UX27-BOARD-028, UX27-SHARED-011 · **Status:** NOT RUN

**Given:** A paragraph with a partial formatted selection.

**When:**
1. Open colour/font/heading controls and apply a change.
2. Undo; move selection to another block and repeat.

**Then:**
- Toolbar focus does not discard or redirect the intended selection.
- Mixed/disabled state reflects real content.
- Exactly selected content changes, stays editable and survives save/reopen.

### UX27-TEST-032 — No accidental embedded freeform app

**Requirements:** UX27-BOARD-029, UX27-BOARD-045 · **Status:** NOT RUN

**Given:** Fresh Native page with text; no intentional Canvas PageType.

**When:**
1. Type, create blocks, revisit the page and restart.
2. Inspect model entities and visual layout.

**Then:**
- One seamless page surface remains.
- No unexplained white Freeform canvas widget, +Box app, repeated empty-state message or duplicate render-created object appears.
- Intentional Canvas pages remain available separately.

### UX27-TEST-033 — Edit View and Locked Unlocked are orthogonal

**Requirements:** UX27-BOARD-030 · **Status:** NOT RUN

**Given:** Page containing flow text, an absolute object and ink.

**When:**
1. Cycle Edit/View and Locked/Unlocked combinations.
2. Attempt text edit, block movement and pen drawing.

**Then:**
- View is read-only.
- Edit plus Locked permits text and ink but prevents unintended layout movement.
- Controls remain visible with meaningful state; locked layout does not silently disable drawing.

### UX27-TEST-034 — Absolute coordinates and snapping

**Requirements:** UX27-BOARD-031 · **Status:** NOT RUN

**Given:** Explicit absolute object plus flow text at non-100% zoom.

**When:**
1. Move/resize with snap on then off.
2. Lock layout; reopen.
3. Explicitly convert between Flow and Absolute where allowed.

**Then:**
- Snapping uses document coordinates and current grid settings.
- Locking preserves geometry and does not flatten absolute content.
- Conversion is deliberate, undoable and distinct from a drag-reorder gesture.

### UX27-TEST-035 — Pen draws on the actual Native page

**Requirements:** UX27-BOARD-032, UX27-BOARD-036 · **Status:** NOT RUN

**Given:** Edit plus Locked; Native page with text; permitted mouse and stylus parameter runs.

**When:**
1. Select Pen.
2. Draw a curved stroke over the actual page, not a separate widget.
3. Switch to text and type.
4. Save/reopen.

**Then:**
- Continuous visible ink follows input and persists as editable strokes.
- Selecting Pen really changes event routing.
- Text remains editable and both content layers reopen correctly.

### UX27-TEST-036 — Ink remains aligned through transforms

**Requirements:** UX27-BOARD-033 · **Status:** NOT RUN

**Given:** A stroke drawn beside a distinctive text block at 125% display scaling.

**When:**
1. Scroll, zoom and resize the window.
2. Draw another stroke.
3. Move the window between supported monitor scales; reopen.

**Then:**
- Stroke coordinates map consistently through page/viewport/display transforms.
- No screen-position drift, jump at start or unexplained offset.
- Existing strokes retain document geometry.

### UX27-TEST-037 — Stroke capture and cancellation

**Requirements:** UX27-BOARD-033, UX27-BOARD-035 · **Status:** NOT RUN

**Given:** Active Pen with a disposable page.

**When:**
1. Draw rapidly across viewport edges; release outside the canvas through supported capture.
2. Simulate cancellation/focus loss.
3. Undo a completed stroke.

**Then:**
- Pointer capture ends cleanly and never leaves drawing stuck.
- Interrupted stroke follows the documented commit/cancel policy.
- One completed stroke is one undo operation, not hundreds of sample-level undos.

### UX27-TEST-038 — Pressure and non-pressure fallback

**Requirements:** UX27-BOARD-034 · **Status:** NOT RUN

**Given:** Supported pressure stylus and a mouse test path, with device capabilities recorded.

**When:**
1. Draw varying-pressure strokes.
2. Repeat with mouse.
3. Place a palm/touch contact during stylus input where supported.

**Then:**
- Pressure affects the configured stroke property when supported.
- Mouse uses a stable fallback and still draws.
- Supported palm filtering prevents unintended content; unsupported hardware is recorded, not falsely passed.

### UX27-TEST-039 — Eraser, colour and brush width

**Requirements:** UX27-BOARD-035 · **Status:** NOT RUN

**Given:** Several strokes of different settings near text blocks.

**When:**
1. Change colour/width and draw new strokes.
2. Erase a selected portion/whole stroke per supported mode.
3. Undo/Redo, save and reopen.

**Then:**
- New settings affect the intended new stroke without unexpectedly recolouring old content.
- Eraser follows its visible mode and does not delete nearby text.
- Restored strokes and settings match the persisted model.

### UX27-TEST-040 — Pen versus selection and panning

**Requirements:** UX27-BOARD-036, UX27-SHARED-012 · **Status:** NOT RUN

**Given:** Native page with selectable text, ink and a scrollable viewport.

**When:**
1. Select text in text mode.
2. Draw in pen mode.
3. Pan/scroll through the documented separate gesture.

**Then:**
- Text selection, ink and panning have deterministic ownership.
- No gesture simultaneously selects text and draws or drags the whole page.
- Mode change releases stale capture and updates cursor/tool state.

### UX27-TEST-041 — Table controls stay compact

**Requirements:** UX27-BOARD-037, UX27-SHARED-018 · **Status:** NOT RUN

**Given:** A 20 by 10 simple table at narrow and wide widths.

**When:**
1. Select no cell, then one cell, then a range.
2. Hover headers/edges and open table overflow.

**Then:**
- Only relevant contextual controls appear.
- No repeated permanent toolbar for every cell or fifty-button command dump.
- All required actions remain reachable with readable labels/shortcuts.

### UX27-TEST-042 — Table edit versus cell selection

**Requirements:** UX27-BOARD-038 · **Status:** NOT RUN

**Given:** Table with mixed text and a selected cell range.

**When:**
1. Use single/double click or documented edit command.
2. Type, Tab/Shift+Tab and arrows in both navigation and text modes.

**Then:**
- Cell navigation and text caret movement are distinct.
- Typing replaces/edits the intended range according to the visible mode.
- Moving out of the last cell follows a declared policy without destroying the page structure.

### UX27-TEST-043 — Merge split and undo without data loss

**Requirements:** UX27-BOARD-039 · **Status:** NOT RUN

**Given:** A table range whose cells all contain distinct content.

**When:**
1. Merge selected cells, reviewing any required content decision.
2. Split the merged region.
3. Undo/Redo and save/reopen.

**Then:**
- No non-anchor content is silently discarded.
- Structure/formatting and text follow the explicit merge policy.
- Undo restores the original cells and their content.

### UX27-TEST-044 — Image resize crop and missing media

**Requirements:** UX27-BOARD-040 · **Status:** NOT RUN

**Given:** Owned image and external referenced media in a Native page.

**When:**
1. Resize with aspect constraints; crop where supported.
2. Undo and reopen.
3. Make the external media unavailable.

**Then:**
- Actual media geometry changes, with appropriate handles and no arbitrary page blank area.
- Owned media remains portable.
- Missing reference is labelled without blocking unrelated text or deleting its locator.

### UX27-TEST-045 — Graph input and view-only sliders

**Requirements:** UX27-BOARD-041 · **Status:** NOT RUN

**Given:** Graph with two expressions and a parameter slider.

**When:**
1. Edit one expression and inspect the plot.
2. Enter invalid syntax.
3. Switch View mode and change an allowed exploratory slider, then reopen.

**Then:**
- Plots correspond to valid canonical expressions; errors are local and precise.
- Multiple expressions remain distinct.
- View-only exploration does not silently overwrite the saved authoring parameters.

### UX27-TEST-046 — Intentional Write and Canvas pages

**Requirements:** UX27-BOARD-042 · **Status:** NOT RUN

**Given:** Board with Native, Write and Canvas PageTypes.

**When:**
1. Switch among page types; edit and save their real content.
2. Return to Native, close and reopen.

**Then:**
- Each PageType activates its actual registered engine, not a static placeholder.
- Embedded ownership and saved content remain coherent.
- Native page is not replaced by the Canvas editor.

### UX27-TEST-047 — Scoped Chat uses current authorised sources

**Requirements:** UX27-BOARD-043 · **Status:** NOT RUN

**Given:** Scoped Chat page with an authorised board source and a revoked source reference.

**When:**
1. Inspect current scope, send through a supported provider, then revoke access and retry.

**Then:**
- The actual admitted request uses current permitted source scope.
- Revoked material is not injected.
- Chat failure preserves page/draft and does not require unrelated cloud setup for local editing.

### UX27-TEST-048 — Search across collapsed and unloaded content

**Requirements:** UX27-BOARD-044, UX27-SHARED-020 · **Status:** NOT RUN

**Given:** Large board with a unique term in a collapsed distant page.

**When:**
1. Search the term.
2. Activate its result and return to the previous page.

**Then:**
- Search includes canonical unloaded content.
- Correct ancestors expand and the target is revealed.
- Previous page context is retained; hidden data is not treated as nonexistent.

### UX27-TEST-049 — Re-render never mutates the document

**Requirements:** UX27-BOARD-045 · **Status:** NOT RUN

**Given:** Board whose model IDs and counts are recorded.

**When:**
1. Repeatedly open tabs, change theme/scale, scroll through virtualisation and restart.

**Then:**
- No extra placeholder, canvas, empty block or event subscription is persisted by rendering.
- Counts/IDs remain stable apart from explicit user edits.
- One click still invokes one domain action.

## Shared UI

### UX27-TEST-050 — Consistent icons and control states

**Requirements:** UX27-SHARED-001, UX27-SHARED-002, UX27-SHARED-004 · **Status:** NOT RUN

**Given:** Representative toolbar, tree, dialog and disabled/busy commands.

**When:**
1. Inspect normal, hover, press, focus, selected, mixed, disabled and busy states at actual display sizes.
2. Activate with pointer and keyboard.

**Then:**
- Icons render with correct bounds/weight and semantic names.
- States are distinguishable; one valid activation commits once.
- Unavailable actions expose reasons without impersonating working controls.

### UX27-TEST-051 — Dark/light contrast without topology change

**Requirements:** UX27-SHARED-005, UX27-SHARED-006 · **Status:** NOT RUN

**Given:** Boards, Library and editor with selected content and open navigation.

**When:**
1. Switch all supported theme/brightness combinations.
2. Inspect ordinary text, icons, selection and focus against computed surfaces.

**Then:**
- Readable labels/title do not become dark-on-dark.
- Layout, selection and content remain stable.
- Record measured contrast where applicable; no blanket conformance claim from one screenshot.

### UX27-TEST-052 — Reduced motion preserves the result

**Requirements:** UX27-SHARED-007, UX27-SHARED-008 · **Status:** NOT RUN

**Given:** Menus, sidebars, tree expansion and drag reflow under ordinary motion.

**When:**
1. Measure displayed transitions.
2. Enable reduced motion and repeat.
3. Trigger an operation that completes immediately.

**Then:**
- Declared timing defaults apply where no owner-specific token overrides them.
- Nonessential movement is removed/reduced without missing final state or focus.
- No artificial minimum spinner delay or fake progress is introduced.

### UX27-TEST-053 — Outside click Escape and navigation dismiss popups

**Requirements:** UX27-SHARED-009, UX27-HOST-007 · **Status:** NOT RUN

**Given:** Open account, block, colour and nested menu variants.

**When:**
1. Click outside; press Escape; switch tab/route; close the target.
2. Repeat with a child menu.

**Then:**
- Correct overlay closes and focus returns sensibly.
- Child handling does not prematurely close the parent when interacting inside it.
- No account popup remains orphaned across unrelated routes.

### UX27-TEST-054 — Popup near viewport edges

**Requirements:** UX27-SHARED-010 · **Status:** NOT RUN

**Given:** Long menu near bottom/right of a small scaled window.

**When:**
1. Open, keyboard-navigate and scroll the menu.
2. Resize while it is open.

**Then:**
- Popup flips/constrains within the viewport, with controlled internal scrolling.
- Focused items remain reachable and visible.
- Page content does not unexpectedly consume menu scroll.

### UX27-TEST-055 — Scaling touch targets and overflow

**Requirements:** UX27-SHARED-003, UX27-SHARED-018, UX27-SHARED-019 · **Status:** NOT RUN

**Given:** Dense toolbar and nested tree at multiple scales and coarse-pointer mode.

**When:**
1. Resize from wide to narrow and inspect icon hit areas.
2. Invoke overflow actions with keyboard/touch.

**Then:**
- Commands move to planned overflow rather than wrapping uncontrolled rows or clipping.
- Effective touch targets are usable.
- Accessible labels and focus order remain coherent.

### UX27-TEST-056 — Scoped error and truthful retry

**Requirements:** UX27-SHARED-014, UX27-SHARED-017 · **Status:** NOT RUN

**Given:** A local editor with an optional unavailable provider.

**When:**
1. Trigger the provider action, then continue local editing.
2. Retry with changed state.

**Then:**
- Error identifies affected action/cause instead of repeating a global generic banner.
- Unrelated local editing remains usable.
- Retry revalidates state and cannot duplicate an uncertain external effect.

### UX27-TEST-057 — Undo through menus and gesture previews

**Requirements:** UX27-SHARED-011, UX27-SHARED-013 · **Status:** NOT RUN

**Given:** Document with selected text and selected object in separate runs.

**When:**
1. Apply a menu formatting action; Undo.
2. Perform a long move with many previews; Undo.

**Then:**
- Tool interaction retains intended target.
- One conceptual change yields one logical undo, independent of preview/sample count.
- Undo updates all views of canonical state.

## Host

### UX27-TEST-058 — Every advertised app has a real activation path

**Requirements:** UX27-HOST-001, UX27-SHARED-016 · **Status:** NOT RUN

**Given:** Inventory of visible app entries with expected routed surface/build.

**When:**
1. Launch each advertised in-scope app from Apps, direct route and standalone executable where specified.
2. Record source/build and instantiated owning surface.

**Then:**
- No fake success or launch-only placeholder.
- Actual activated source matches the built candidate.
- A failing optional app is isolated and its missing dependency is actionable.

### UX27-TEST-059 — Home and New Tab retain distinct identity

**Requirements:** UX27-HOST-002, UX27-HOST-003 · **Status:** NOT RUN

**Given:** Mixed native/web tab session with Home and New Tab.

**When:**
1. Switch Apps/Dashboard/Models and New Tab views.
2. Create/select/close tabs and overflow a long tab row.

**Then:**
- Correct view/selection is visible with readable selected-tab contrast.
- Home/New Tab do not become duplicate nested shells.
- Closing follows dirty-state protection and moves focus predictably.

### UX27-TEST-060 — Chromium viewport and scrolling

**Requirements:** UX27-HOST-004, UX27-HOST-005 · **Status:** NOT RUN

**Given:** A supported real browser engine and test page with known geometry/scroll markers.

**When:**
1. Resize and change display scale.
2. Use mouse wheel and trackpad in both axes; zoom and navigate.

**Then:**
- Page is sharp and proportioned with accurate hit targets.
- Scroll direction and magnitude follow settings, with no double scaling.
- No security/CAPTCHA bypass is used as an alleged rendering fix.

### UX27-TEST-061 — Missing engine does not disable native apps

**Requirements:** UX27-HOST-006, UX27-HOST-009 · **Status:** NOT RUN

**Given:** Browser engine deliberately unavailable in a disposable installation.

**When:**
1. Open a web page, then a local Board and Present document.

**Then:**
- Web failure names the unavailable engine and supported repair route.
- Native editors remain usable.
- No fabricated web screenshot or mandatory account connection hides the missing engine.

### UX27-TEST-062 — Mixed session restore and failing target isolation

**Requirements:** UX27-HOST-008, UX27-DATAFLOW-004 · **Status:** NOT RUN

**Given:** Saved session with a board, deck, website and one missing local file.

**When:**
1. Restart the host.
2. Reopen valid tabs and inspect the failed target.
3. Verify pending action logs.

**Then:**
- Valid tabs restore stable identity and state.
- Missing file gets Locate/recovery rather than blocking the whole shell.
- Prior AI/Automation actions are not replayed just because tabs restore.

## Spaces

### UX27-TEST-063 — Direct Space entry and retained draft

**Requirements:** UX27-SPACES-001, UX27-SPACES-005 · **Status:** NOT RUN

**Given:** Two Spaces each with a draft and stored conversation.

**When:**
1. Open one from the chosen rail/dropdown.
2. Switch Space, create a chat, then return and restart.

**Then:**
- No unwanted management interstitial.
- Both retained navigation structures and the correct scoped history remain.
- Drafts and attachments belong to their original conversations.

### UX27-TEST-064 — Composer shelf state and browse versus activate

**Requirements:** UX27-SPACES-002, UX27-SPACES-003 · **Status:** NOT RUN

**Given:** Idle, working-with-draft and stopped states.

**When:**
1. Scroll/swipe the action shelf without pressing its action.
2. Activate one action; change runtime state during another press.

**Then:**
- At most five contextual actions with peek/dots follow the state table.
- Browsing sends nothing.
- State drift cancels/revalidates instead of invoking the wrong consequential action.

### UX27-TEST-065 — Queue Stop and Resume are durable distinctions

**Requirements:** UX27-SPACES-004 · **Status:** NOT RUN

**Given:** Active eligible run with a queued draft and permission-controlled action.

**When:**
1. Queue once with a retried transport receipt.
2. Stop and inspect settlement.
3. Resume where supported.

**Then:**
- One durable queued item, not duplicate work.
- Stopping is distinct from settled Stop; previous effects are not falsely reversed.
- Resume retains identity and cannot answer a permission hold.

### UX27-TEST-066 — Rich message rendering and read position

**Requirements:** UX27-SPACES-006 · **Status:** NOT RUN

**Given:** Streaming reply with Markdown, code, table and citations.

**When:**
1. Scroll up while generation continues.
2. Copy code and invoke an applicable message action.

**Then:**
- Streaming updates one message without hijacking deliberate scroll position.
- Copy preserves code whitespace.
- Actions target the selected message/revision and disabled states are truthful.

### UX27-TEST-067 — Summary and task filters use real state

**Requirements:** UX27-SPACES-007 · **Status:** NOT RUN

**Given:** Task Space with current and other-Space tasks in several statuses.

**When:**
1. Inspect Summary/Action Graph, default task filter and All Tasks.
2. Open a checkpoint then change filter.

**Then:**
- The agreed surfaces and meaningful badges reflect actual canonical records.
- Filter changes do not duplicate or complete tasks.
- Checkpoint viewed state follows its actual contract.

### UX27-TEST-068 — Sidebar activation rechecks permissions

**Requirements:** UX27-SPACES-008 · **Status:** NOT RUN

**Given:** Extension entries for URL, app, authored CUI and declared action.

**When:**
1. Activate permitted targets.
2. Revoke/detach the extension while an entry is displayed, then activate it.

**Then:**
- Typed owning routes/actions execute when authorised.
- Stale/revoked contribution cannot execute through cached visibility.
- Unrelated conversations remain intact and no string-execution fallback occurs.

### UX27-TEST-069 — Model group selection loads only on Send

**Requirements:** UX27-SPACES-009 · **Status:** NOT RUN

**Given:** Group containing two compatible local members and a permission-restricted remote route.

**When:**
1. Switch members repeatedly without sending.
2. Send with the selected member; inspect actual routing.
3. Change selection during the run.

**Then:**
- Picker changes do not repeatedly load/unload models.
- Send resolves current capability/permission and records actual member/backend.
- Running request remains correctly pinned; no silent paid fallback.

### UX27-TEST-070 — Effort and allowance are not conflated

**Requirements:** UX27-SPACES-010, UX27-SPACES-011 · **Status:** NOT RUN

**Given:** Three-/four-/five-tier model fixtures, a local model and a metered provider with unknown usage.

**When:**
1. Change effort and compare displayed tiers.
2. Switch models; inspect usage source/timestamp and custom pin.

**Then:**
- Mappings match the explicit effort table.
- Local allowance meter is absent where inapplicable; unknown usage is not invented.
- Units and custom/provider-reported limits remain distinct.

### UX27-TEST-071 — Specialised Spaces retain their own workflows

**Requirements:** UX27-SPACES-012, UX27-SPACES-013, UX27-SPACES-014 · **Status:** NOT RUN

**Given:** Research, Shopping, Study, Assistants and Voice fixtures with authorised providers.

**When:**
1. Exercise one real plan/report, constrained result group, practice response, Assistant Main chat and Voice permission transition.

**Then:**
- Specialised canonical state exists beyond a changed system prompt.
- Outputs, progress and team visibility follow their domain rules.
- Recording/vision availability and consent are truthful.

## Automations

### UX27-TEST-072 — Draft versus active graph and UI views

**Requirements:** UX27-AUTO-001, UX27-AUTO-002 · **Status:** NOT RUN

**Given:** One active workflow and a modified draft.

**When:**
1. Edit via simple and graph modes.
2. Change a node/edge, undo and validate.
3. Inspect which revision a trigger uses.

**Then:**
- Both editors share one graph definition.
- Invalid connections have useful feedback.
- Draft edits do not silently replace the active executable revision.

### UX27-TEST-073 — Durable Wait across process restart

**Requirements:** UX27-AUTO-003, UX27-AUTO-004 · **Status:** NOT RUN

**Given:** Real SQLite store and a workflow with a committed side effect followed by Wait.

**When:**
1. Run to Wait; stop owned test process.
2. Reopen, resume and complete the same RunID.
3. Retry a terminal receipt.

**Then:**
- Continuation resumes at the saved boundary without rerunning the committed effect.
- Suspend/complete does not collide on identity.
- Actual node outputs and Run history persist.

### UX27-TEST-074 — Approval is not inferred from due time

**Requirements:** UX27-AUTO-004, UX27-AUTO-006 · **Status:** NOT RUN

**Given:** Due run waiting for a required permission/approval.

**When:**
1. Execute scheduler sweep and retry the run without approval.
2. Then provide approval through the authorised test path.

**Then:**
- No unapproved side effect occurs.
- Held state remains visible until real approval.
- One eligible continuation proceeds; retries remain idempotent/reconciled.

### UX27-TEST-075 — Timezone recurrence and missed admission

**Requirements:** UX27-AUTO-005 · **Status:** NOT RUN

**Given:** Scheduled workflow spanning a clock-change boundary with declared catch-up policy.

**When:**
1. Inspect next occurrences.
2. Disable future admission, restart scheduler and later re-enable.

**Then:**
- Timezone/ambiguity is visible and admission follows the configured policy.
- No duplicate timer/trigger on reopening UI.
- Disabling future work does not falsely undo completed or running effects.

## Build

### UX27-TEST-076 — One canonical project across editing surfaces

**Requirements:** UX27-BUILD-001, UX27-BUILD-002, UX27-BUILD-004 · **Status:** NOT RUN

**Given:** Existing code/CUI/site project with language diagnostics.

**When:**
1. Edit source, visual properties and hierarchy.
2. Navigate diagnostics and undo a refactor.

**Then:**
- Visual/code views update one source model.
- Diagnostics point to the correct revision/range.
- Generated ordinary UI remains canonical CUI, with no shadow project.

### UX27-TEST-077 — Failed build cannot present a stale success

**Requirements:** UX27-BUILD-003 · **Status:** NOT RUN

**Given:** Known runnable project followed by an intentional compile failure.

**When:**
1. Run, then introduce the failure and Run again.
2. Inspect preview, exit code, diagnostics and Stop.

**Then:**
- Failed build is reported.
- Any retained old preview is explicitly stale, not the new successful candidate.
- Stop affects the owned process only; debug/test adapters remain genuine.

### UX27-TEST-078 — Conflicts and publishing have distinct boundaries

**Requirements:** UX27-BUILD-005, UX27-BUILD-006, UX27-BUILD-007 · **Status:** NOT RUN

**Given:** Disposable branch conflict and site staging deployment.

**When:**
1. Inspect base/local/incoming changes.
2. Exercise the authorised review choice; preview site and then publish explicitly.

**Then:**
- Review/merge/push/deployment statuses are distinct.
- No destructive default or publish-on-preview.
- Agent-authored repairs receive appropriate separate review.

## Write

### UX27-TEST-079 — Real document caret and multi-page editing

**Requirements:** UX27-WRITE-001, UX27-WRITE-003 · **Status:** NOT RUN

**Given:** Real multi-page mixed-format Writer-backed document.

**When:**
1. Click/select/type across page boundaries.
2. Apply a style and undo.
3. Save and reopen.

**Then:**
- Caret/selection belong to the rendered document engine, not an unrelated text field.
- Pagination/styles reflect the actual model.
- Reopened document retains content and formatting.

### UX27-TEST-080 — Contextual commands and inspector remain synchronised

**Requirements:** UX27-WRITE-002, UX27-WRITE-004, UX27-WRITE-005, UX27-WRITE-006 · **Status:** NOT RUN

**Given:** Document with headings, image, table and tracked/review content where supported.

**When:**
1. Navigate outline; format text/table/image from toolbar and inspector.
2. Inspect undo and document theme isolation.

**Then:**
- Daily commands are usable without a permanent ribbon or inspector command dump.
- Outline selects actual content.
- All controls reflect one engine state and app theme does not rewrite authored styling.

### UX27-TEST-081 — Import export and recovery are real

**Requirements:** UX27-WRITE-007, UX27-DATAFLOW-005 · **Status:** NOT RUN

**Given:** Supported external format with tables/media and a simulated interrupted save.

**When:**
1. Import, edit and export through supported format.
2. Open exported artifact independently.
3. Recover interrupted editing.

**Then:**
- Round-trip checks actual output, with disclosed unsupported features instead of fake parity.
- Recovery preserves edits.
- Export is not labelled complete before artifact validation.

## Present

### UX27-TEST-082 — Host route renders the intended editor

**Requirements:** UX27-PRESENT-001, UX27-PRESENT-002 · **Status:** NOT RUN

**Given:** Normal integrated host and standalone candidate from the same source.

**When:**
1. Open 9to1://present through actual host navigation.
2. Inspect runtime surface identity and header/menu/toolbar composition.

**Then:**
- The routed editor—not an unused standalone scene—shows the intended CUI chrome.
- Menus are above exactly one persistent main editing toolbar.
- A direct component-constructor badge assertion alone cannot pass.

### UX27-TEST-083 — Slide list and notes retain content

**Requirements:** UX27-PRESENT-003 · **Status:** NOT RUN

**Given:** Deck with several slides and distinct notes, including one empty note.

**When:**
1. Select/reorder/duplicate slides and edit notes.
2. Resize navigator/notes panels; save/reopen.

**Then:**
- Slide/Notes IDs and text remain correctly paired.
- Empty placeholder appears only for empty notes.
- Canvas remains aspect-correct and dominant with accurate thumbnails.

### UX27-TEST-084 — Text editing versus object transforms

**Requirements:** UX27-PRESENT-004, UX27-PRESENT-005 · **Status:** NOT RUN

**Given:** Slide with overlapping text boxes, a shape and image.

**When:**
1. Select object, enter text editing and drag to select text.
2. Exit editing; move/resize/rotate object at varied zoom.
3. Escape a gesture and undo a completed one.

**Then:**
- Text selection never drags the parent inadvertently.
- Handles/cursors remain aligned and previews update during gestures.
- Cancel/undo restore correct geometry and content.

### UX27-TEST-085 — Real insertion gallery and shared formatting

**Requirements:** UX27-PRESENT-006, UX27-PRESENT-007 · **Status:** NOT RUN

**Given:** Editable slide with a selected object.

**When:**
1. Open Shapes without choosing one; then select/draw a shape.
2. Cancel another insertion.
3. Format from toolbar/inspector/context menu.

**Then:**
- Opening the gallery inserts nothing.
- Confirmed action creates the chosen real object once.
- All formatting surfaces share state and preserve selection.

### UX27-TEST-086 — Playback and save/reopen

**Requirements:** UX27-PRESENT-008 · **Status:** NOT RUN

**Given:** Deck with notes/media and a second display where available.

**When:**
1. Start presentation/presenter mode.
2. Exit, edit, save and reopen.
3. Test unavailable display/media conditions.

**Then:**
- Playback is the actual deck and does not overwrite authoring state.
- Notes privacy follows the canonical presenter/export rules.
- Unsupported platform/display coverage is recorded, not fabricated.

## Canvas

### UX27-TEST-087 — Purposeful spatial input and exported content

**Requirements:** UX27-CANVAS-001, UX27-CANVAS-002, UX27-CANVAS-003, UX27-CANVAS-004, UX27-CANVAS-005 · **Status:** NOT RUN

**Given:** Intentional Canvas artifact with layers, shapes and brush content.

**When:**
1. Pan/zoom; select/move/reorder layers; draw/erase and undo.
2. Save/reopen and inspect export.

**Then:**
- Spatial coordinates and input remain stable.
- Real layers/brush data persist rather than a screenshot only.
- Export contains the full intended region, not only materialised viewport content.

## Data

### UX27-TEST-088 — Cell modes formulas and canonical views

**Requirements:** UX27-DATA-001, UX27-DATA-002, UX27-DATA-003, UX27-DATA-004 · **Status:** NOT RUN

**Given:** Data artifact with formulas, filtered views and many rows; linked view in Boards.

**When:**
1. Edit a cell in navigation/text modes; recalculate.
2. Change sort/filter and inspect linked view.
3. Export/import including off-screen rows.

**Then:**
- Caret/navigation semantics are distinct; formulas refer to intended identities.
- Views share one canonical dataset and do not silently mutate source order where inappropriate.
- Full data survives virtualisation and round-trip.

## Files

### UX27-TEST-089 — Ordinary file handling without domain takeover

**Requirements:** UX27-FILES-001, UX27-FILES-002, UX27-FILES-003, UX27-FILES-004 · **Status:** NOT RUN

**Given:** Mixed files including .9to1board, with one remote-only unavailable item.

**When:**
1. Navigate/select, copy/move through a collision, Open with Boards and inspect sync state.

**Then:**
- File operations have explicit conflict/undo/progress semantics.
- Boards handles its document; Files does not become a mandatory connected account.
- Local save and remote availability/sync are separately truthful.

## Other apps

### UX27-TEST-090 — Planner moves actual events and tasks

**Requirements:** UX27-OTHER-001, UX27-OTHER-002 · **Status:** NOT RUN

**Given:** Calendar/tasks with timezones, recurrences and reminders.

**When:**
1. Drag an event, resize its duration and inspect edit scope for recurrence.
2. Complete a task and test reminder scheduling.

**Then:**
- Visible changes target the correct canonical event/task.
- Recurrence and timezone decisions are explicit; reminder timing is real.
- Undo and restart preserve state without duplicate alarms.

### UX27-TEST-091 — Mail reply send and attachment state

**Requirements:** UX27-OTHER-003, UX27-OTHER-004 · **Status:** NOT RUN

**Given:** Authorised disposable mail account/test recipient and draft with attachment.

**When:**
1. Open a thread, reply, edit recipient, attach a file and send through explicit action.
2. Inspect ambiguous/failing transport in a test run.

**Then:**
- Thread/recipient identity is correct; draft survives navigation.
- Attachment readiness and send receipt are truthful.
- Uncertain submission reconciles rather than sending a duplicate message.

### UX27-TEST-092 — Forms published revision and responses

**Requirements:** UX27-OTHER-005 · **Status:** NOT RUN

**Given:** Draft and published form with validation/branching.

**When:**
1. Change draft field; preview and submit valid/invalid test responses.
2. Publish explicitly and export response data.

**Then:**
- Preview and published form use identified revisions.
- Branching/validation work and responses retain schema identity.
- Editing a draft does not silently change a live form or discard previous answers.

### UX27-TEST-093 — Cards state and learning evidence

**Requirements:** UX27-OTHER-006 · **Status:** NOT RUN

**Given:** Card deck with authored content and recorded practice history.

**When:**
1. Edit cards, practice recall and choose an actual result.
2. Filter/sort and reopen.

**Then:**
- Authored card data and review scheduling/state are distinct.
- Merely displaying a card is not recorded as mastery.
- Undo/recovery preserve actual history and canonical identity.

### UX27-TEST-094 — Picture layers and pixel geometry

**Requirements:** UX27-OTHER-007 · **Status:** NOT RUN

**Given:** Layered image with masks and nontrivial zoom.

**When:**
1. Select/transform a layer, brush a mask, undo and export.

**Then:**
- Pointer/image coordinates and layer targeting are correct.
- Original/edited state remains recoverable.
- Export is actual composed image data, not a UI screenshot.

### UX27-TEST-095 — Wave and Motion timeline editing

**Requirements:** UX27-OTHER-008, UX27-OTHER-009 · **Status:** NOT RUN

**Given:** Audio/video projects with multiple tracks and source assets.

**When:**
1. Scrub/play, trim/move clips, adjust supported automation and undo.
2. Reopen and render/export a range.

**Then:**
- Transport, timeline, waveform/frame and audio remain aligned.
- Nondestructive edits preserve source assets.
- Export processes the actual selected range and reports missing media honestly.

### UX27-TEST-096 — Maps and Shelf use real objects

**Requirements:** UX27-OTHER-010, UX27-OTHER-011 · **Status:** NOT RUN

**Given:** Map context with a selected location and Shelf with saved artifacts.

**When:**
1. Pan/zoom/search/select; inspect route/source freshness.
2. Save, organise and open a Shelf item.

**Then:**
- Selection stays attached to the intended real object.
- Data/source limitations are visible; a decorative map is not a route result.
- Shelf items open canonical artifacts rather than copies with diverging identity.

### UX27-TEST-097 — Terminal and remote input ownership

**Requirements:** UX27-OTHER-012, UX27-OTHER-017 · **Status:** NOT RUN

**Given:** Authorised disposable terminal and remote/session surface.

**When:**
1. Inspect target identity; execute a harmless bounded command.
2. Disconnect/reconnect and inspect pending input/results.

**Then:**
- Input goes to the shown session under its permission boundary.
- Reconnect does not silently create another machine or replay consequential commands.
- Viewer close and remote shutdown remain distinct.

### UX27-TEST-098 — AI Studio and Connect permission boundaries

**Requirements:** UX27-OTHER-013, UX27-OTHER-014 · **Status:** NOT RUN

**Given:** Draft agent/tool definition and a connector with limited granted scopes.

**When:**
1. Edit graph/form views; test the definition.
2. Inspect connection health; deny or revoke a requested scope.

**Then:**
- Views share a typed versioned definition and real test identity.
- Testing does not equal publishing.
- No permission bypass, invented healthy status or leaked secret in exports/logs.

### UX27-TEST-099 — Games Play and organisation artifacts

**Requirements:** UX27-OTHER-015, UX27-OTHER-016, UX27-OTHER-018 · **Status:** NOT RUN

**Given:** Safe creation project, turn-based test match and referenced Binder/Link/Files Sheet.

**When:**
1. Edit a scene then run test mode.
2. Perform a legal match action and replay.
3. Move an organisation card and open its source.

**Then:**
- Runtime scene state does not silently overwrite authoring.
- Match rules/visibility and recorded actions remain real.
- Organisation move does not duplicate or relocate a referenced source artifact unintentionally.

## Integration

### UX27-TEST-100 — Large board keeps every block and stroke

**Requirements:** UX27-SHARED-020, UX27-ACCEPT-005 · **Status:** NOT RUN

**Given:** Declared large board fixture with known counts, IDs and content hashes.

**When:**
1. Edit early/late blocks, drag to a distant target and draw near the viewport.
2. Search unloaded content, save/reopen and export all.

**Then:**
- Input remains responsive within measured hardware-specific budgets.
- All blocks/strokes are retained regardless of virtualisation.
- Full output hash/count comparison catches silent omissions; no arbitrary limit is invented.

### UX27-TEST-101 — Independent review of actual main build

**Requirements:** UX27-MAIN-001, UX27-MAIN-002, UX27-MAIN-003, UX27-ACCEPT-001, UX27-ACCEPT-002, UX27-ACCEPT-004 · **Status:** NOT RUN

**Given:** Reconciled candidate with all original branch work accounted for and a separate reviewer available.

**When:**
1. Inspect integration inventory and source/artifact identities.
2. Launch using Jacob's documented normal entry point.
3. Independently run representative repaired journeys and record all seven gates.

**Then:**
- No unmerged/dirty work was deleted to achieve one branch.
- Launched build corresponds to the recorded main candidate.
- Consolidation alone is not acceptance; missing runtime/platform/independence gates stay unpassed.

### UX27-TEST-102 — Full manifest and honest unavailable coverage

**Requirements:** UX27-AUTH-003, UX27-AUTH-006, UX27-COVERAGE-001, UX27-COVERAGE-002, UX27-COVERAGE-004, UX27-ACCEPT-003, UX27-ACCEPT-006 · **Status:** NOT RUN

**Given:** Baseline, expansion and selected donor/reference versions available to a reviewer.

**When:**
1. Map every applicable requirement to app/entry point/engine/storage/evidence.
2. Compare the scenario catalogue to that manifest.
3. Record absent devices, images, platforms and tests explicitly.

**Then:**
- Catalogue is a floor, not a scope reduction.
- Draft defaults are not represented as measured historic screenshot details.
- Every claim has source/runtime provenance, and NOT RUN is not relabelled PASS.
