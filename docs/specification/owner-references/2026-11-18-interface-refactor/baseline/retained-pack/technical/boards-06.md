# Boards — seamless pages, blocks and ink — Add page/section chooses the correct destination; Tree focus, expansion and rename (part 06)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2448 · v2 lines 13807–13810

**Then:**
- Top toolbar stays; Boards identity is top-left; no top-right AppFlowy product badge.
- Add page/section and user controls remain in the fixed bottom area.
- Theme changes do not rearrange this topology or lose page state.

### V2-B2449 · v2 lines 13812–13812

### UX27-TEST-012 — Add page/section chooses the correct destination

### V2-B2450 · v2 lines 13814–13814

**Requirements:** UX27-BOARD-014 · **Status:** NOT RUN

### V2-B2451 · v2 lines 13816–13816

**Given:** A board with a section and a nested page.

### V2-B2452 · v2 lines 13818–13821

**When:**
1. Open bottom Add; choose New section.
2. Open Add again, choose New Native page and an explicit nested destination.
3. Cancel a third creation attempt.

### V2-B2453 · v2 lines 13823–13826

**Then:**
- Exactly one entity is created per confirmed operation at the shown destination.
- Tree reveals it and focus reaches title/editor.
- Cancel creates no hidden node; template browsing is not compulsory.

### V2-B2454 · v2 lines 13828–13828

### UX27-TEST-013 — Tree focus, expansion and rename

### V2-B2455 · v2 lines 13830–13830

**Requirements:** UX27-BOARD-012, UX27-BOARD-013 · **Status:** NOT RUN

### V2-B2456 · v2 lines 13832–13832

**Given:** Three-level tree with stable IDs and an active nested page.

### V2-B2457 · v2 lines 13834–13837

**When:**
1. Navigate with arrows/Home/End.
2. Collapse the active page ancestor without activating another page.
3. Rename a row; test Enter, Escape and invalid input.

### V2-B2458 · v2 lines 13839–13842

**Then:**
- Expansion/focus and activation are distinct.
- Collapse retains the open descendant and its data.
- Committed rename updates all title/breadcrumb views without changing identity; Escape restores it.

### V2-B2459 · v2 lines 13844–13844

### UX27-TEST-014 — Move pages without cycles or orphaning

### V2-B2460 · v2 lines 13846–13846

**Requirements:** UX27-BOARD-015 · **Status:** NOT RUN

### V2-B2461 · v2 lines 13848–13848

**Given:** Nested sections/pages with references and descendants.

### V2-B2462 · v2 lines 13850–13853

**When:**
1. Drag a page above/below a sibling and into a valid parent.
2. Attempt to drop a parent into its own descendant.
3. Undo the valid move and cancel another drag.

### V2-B2463 · v2 lines 13855–13858

**Then:**
- Drop preview predicts the stored destination.
- IDs/content/descendants survive; cycles are rejected visibly.
- Undo/cancel restores structure without extra nodes.

### V2-B2464 · v2 lines 13860–13860

### UX27-TEST-015 — Trash and restore descendants

### V2-B2465 · v2 lines 13862–13862

**Requirements:** UX27-BOARD-015, UX27-DATAFLOW-006 · **Status:** NOT RUN

### V2-B2466 · v2 lines 13864–13864

**Given:** Section containing edited pages and an external reference.

### V2-B2467 · v2 lines 13866–13868

**When:**
1. Trash the section after reviewing impact.
2. Restore it; repeat after its original parent is unavailable.

### V2-B2468 · v2 lines 13870–13873

**Then:**
- Deletion scope is clear and recoverable.
- Descendants retain content and IDs.
- Restore returns to the original or explicitly recovered location; external original artifacts are not deleted by removing a reference.

### V2-B2469 · v2 lines 13875–13875

### UX27-TEST-016 — Board switching is atomic

### V2-B2470 · v2 lines 13877–13877

**Requirements:** UX27-BOARD-011 · **Status:** NOT RUN

### V2-B2471 · v2 lines 13879–13879

**Given:** Two boards with different pages, drafts and model context.

### V2-B2472 · v2 lines 13881–13884

**When:**
1. Switch through the searchable board selector.
2. Switch back.
3. Attempt to open an unavailable target.

### V2-B2473 · v2 lines 13886–13889

**Then:**
- Title, tree, content and context always belong to the same board.
- Each board retains its selected page/draft/scroll state.
- Failed target opening leaves the current usable board intact.

### V2-B2474 · v2 lines 13891–13891

### UX27-TEST-017 — Enter splits in the middle of a paragraph

### V2-B2475 · v2 lines 13893–13893

**Requirements:** UX27-BOARD-016, UX27-BOARD-017 · **Status:** NOT RUN

### V2-B2476 · v2 lines 13895–13895

**Given:** One paragraph ABCDEF with caret between C and D; capture its BlockID.

### V2-B2477 · v2 lines 13897–13901

**When:**
1. Press Enter once.
2. Inspect the two block entities and place the caret in each.
3. Undo and Redo.
4. Save/reopen.

### V2-B2478 · v2 lines 13903–13906

**Then:**
- Original ID holds ABC; one new sibling holds DEF; caret starts at the new sibling.
- Both blocks have functioning independent grips.
- Undo/Redo and reopen preserve exactly the expected structure, not a tall multiline input.

### V2-B2479 · v2 lines 13908–13908

### UX27-TEST-018 — Enter at start, end and with selected text

### V2-B2480 · v2 lines 13910–13910

**Requirements:** UX27-BOARD-017 · **Status:** NOT RUN

### V2-B2481 · v2 lines 13912–13912

**Given:** Parameterised paragraph with caret at start/end or a selected inline range.

### V2-B2482 · v2 lines 13914–13917

**When:**
1. Press Enter in each case.
2. Inspect text, inline marks, IDs and caret.
3. Undo once.

### V2-B2483 · v2 lines 13919–13922

**Then:**
- Start/end follow the declared split policy; selection is deleted and split atomically.
- Rich text survives without duplicate or missing characters.
- One Undo restores the original content/tree/caret.

### V2-B2484 · v2 lines 13924–13924

### UX27-TEST-019 — Shift Enter and wrapping do not create blocks

### V2-B2485 · v2 lines 13926–13926

**Requirements:** UX27-BOARD-018 · **Status:** NOT RUN

### V2-B2486 · v2 lines 13928–13928

**Given:** One paragraph near the flow-column width with a captured BlockID.

### V2-B2487 · v2 lines 13930–13933

**When:**
1. Insert Shift+Enter, then type enough text to wrap.
2. Resize the window.
3. Save/reopen.

### V2-B2488 · v2 lines 13935–13938

**Then:**
- Soft break and wrapping remain in the same BlockID.
- Resizing changes layout only, not model content/block count.
- No giant permanent blank region is inserted.

### V2-B2489 · v2 lines 13940–13940

### UX27-TEST-020 — IME and grapheme-safe splitting

### V2-B2490 · v2 lines 13942–13942

**Requirements:** UX27-BOARD-018 · **Status:** NOT RUN

### V2-B2491 · v2 lines 13944–13944

**Given:** Text containing emoji, combining characters and a supported IME composition.

### V2-B2492 · v2 lines 13946–13949

**When:**
1. Commit an IME candidate with Enter.
2. Perform an intentional structural split afterwards.
3. Undo/Redo and inspect text.

### V2-B2493 · v2 lines 13951–13954

**Then:**
- Composition commit is not also a block split.
- Intended split respects grapheme boundaries and creates the specified structure.
- No corrupted surrogate pairs, duplicate input or accidental loss.

### V2-B2494 · v2 lines 13956–13956

### UX27-TEST-021 — Heading, list and checklist continuation

### V2-B2495 · v2 lines 13958–13958

**Requirements:** UX27-BOARD-019, UX27-BOARD-021 · **Status:** NOT RUN

### V2-B2496 · v2 lines 13960–13960

**Given:** Heading, bullet, numbered list, nested list and checked checklist fixtures.

### V2-B2497 · v2 lines 13962–13964

**When:**
1. Press Enter mid/end and on empty items.
2. Test Shift+Enter and nested empty-item exit.

### V2-B2498 · v2 lines 13966–13969

**Then:**
- Headings continue into paragraphs under the stated default.
- Lists retain appropriate depth/numbering; new checklist item is unchecked.
- Empty root items exit and nested empty items outdent without deleting children.

### V2-B2499 · v2 lines 13971–13971

### UX27-TEST-022 — Code, title and cell Enter stay contextual

### V2-B2500 · v2 lines 13973–13973

**Requirements:** UX27-BOARD-019, UX27-BOARD-038 · **Status:** NOT RUN

### V2-B2501 · v2 lines 13975–13975

**Given:** One code block, page-title field and editable table.

### V2-B2502 · v2 lines 13977–13979

**When:**
1. Use Enter/Shift+Enter in each focused context.
2. Use the explicit exit-to-page command.

### V2-B2503 · v2 lines 13981–13984

**Then:**
- Code Enter creates a code line rather than an outside block.
- Title Enter commits/focuses deliberately.
- Cell editing follows its contract; no accidental outer block or hidden navigation.

### V2-B2504 · v2 lines 13986–13986

### UX27-TEST-023 — Backspace/Delete across structural boundaries

### V2-B2505 · v2 lines 13988–13988

**Requirements:** UX27-BOARD-020 · **Status:** NOT RUN

### V2-B2506 · v2 lines 13990–13990

**Given:** Adjacent compatible paragraphs, nested content and a selected non-text block.

### V2-B2507 · v2 lines 13992–13995

**When:**
1. Backspace at the second paragraph start.
2. Undo; Delete at the first paragraph end.
3. Attempt boundaries around structured/non-text content.

### V2-B2508 · v2 lines 13997–14000

**Then:**
- Compatible text merges preserve marks and intended caret placement.
- Structured content is selected/handled explicitly, not silently flattened or destroyed.
- Undo restores exact IDs/relationships according to the defined operation.

### V2-B2509 · v2 lines 14002–14002

### UX27-TEST-024 — Slash menu inserts only on confirmation

### V2-B2510 · v2 lines 14004–14004

**Requirements:** UX27-BOARD-022 · **Status:** NOT RUN

### V2-B2511 · v2 lines 14006–14006

**Given:** An empty paragraph and a paragraph with existing text.

### V2-B2512 · v2 lines 14008–14011

**When:**
1. Open slash insertion, filter results, navigate by keyboard.
2. Confirm a valid block, then repeat and cancel.
3. Click outside while the menu is open.

### V2-B2513 · v2 lines 14013–14016

**Then:**
- Menu has relevant icon-led choices and current block targeting.
- Confirmation inserts one actual block.
- Cancelling/outside dismissal preserves pre-menu content and does not leave a stuck overlay.

### V2-B2514 · v2 lines 14018–14018

### UX27-TEST-025 — Six-dot click versus actual drag

### V2-B2515 · v2 lines 14020–14020

**Requirements:** UX27-BOARD-023, UX27-BOARD-024 · **Status:** NOT RUN

### V2-B2516 · v2 lines 14022–14022

**Given:** Three flow blocks with recorded IDs/order.

### V2-B2517 · v2 lines 14024–14026

**When:**
1. Click a grip without crossing its drag threshold.
2. Close the menu; drag the grip beyond threshold and drop below another block.

### V2-B2518 · v2 lines 14028–14031

**Then:**
- Click opens that block menu; drag shows a real preview and insertion target.
- Drop changes stored order once and preserves the moved ID/content.
- A decorative six-dot icon or text selection instead of movement fails.

### V2-B2519 · v2 lines 14033–14033

### UX27-TEST-026 — Cancel drag and preserve undo

### V2-B2520 · v2 lines 14035–14035

**Requirements:** UX27-BOARD-024, UX27-SHARED-013 · **Status:** NOT RUN

### V2-B2521 · v2 lines 14037–14037

**Given:** Multi-line block in a page with history.

### V2-B2522 · v2 lines 14039–14041

**When:**
1. Drag across several targets, then press Escape.
2. Repeat with a valid drop and Undo once.

### V2-B2523 · v2 lines 14043–14046

**Then:**
- Cancelled preview causes no persistent reorder.
- Valid movement commits once, with one logical undo step.
- No duplicate blocks or delayed mutation after cancellation.

### V2-B2524 · v2 lines 14048–14048

### UX27-TEST-027 — Nesting and columns have explicit targets

### V2-B2525 · v2 lines 14050–14050

**Requirements:** UX27-BOARD-025 · **Status:** NOT RUN

### V2-B2526 · v2 lines 14052–14052

**Given:** Several compatible flow blocks and a container with children.

### V2-B2527 · v2 lines 14054–14057

**When:**
1. Drag to a sibling insertion target.
2. Drag into the explicit nesting zone.
3. Create an allowed side-by-side column target, then resize its divider.

### V2-B2528 · v2 lines 14059–14062

**Then:**
- Insertion, nesting and column targets are visibly distinct.
- Parent/order/column state match the preview and preserve content.
- No accidental nesting from merely crossing a label.

### V2-B2529 · v2 lines 14064–14064

### UX27-TEST-028 — Drag auto-scroll through virtualised content

### V2-B2530 · v2 lines 14066–14066

**Requirements:** UX27-BOARD-025, UX27-SHARED-020 · **Status:** NOT RUN

### V2-B2531 · v2 lines 14068–14068

**Given:** Large board whose destination is off screen and partially unmaterialised.

### V2-B2532 · v2 lines 14070–14073

**When:**
1. Start a grip drag; hover near the page edge.
2. Navigate to the distant target and drop.
3. Reopen and search for moved content.

### V2-B2533 · v2 lines 14075–14078

**Then:**
- Controlled auto-scroll exposes targets without freezing or losing capture.
- Unrendered blocks are not omitted.
- Correct distant order persists with the same moved BlockID.

### V2-B2534 · v2 lines 14080–14080

### UX27-TEST-029 — Keyboard and multi-block movement

### V2-B2535 · v2 lines 14082–14082

**Requirements:** UX27-BOARD-026 · **Status:** NOT RUN

### V2-B2536 · v2 lines 14084–14084

**Given:** Several adjacent and nested blocks, plus a text selection.

### V2-B2537 · v2 lines 14086–14089

**When:**
1. Select multiple blocks through the documented block-selection mode.
2. Move through a keyboard/context alternative.
3. Repeat in text-edit mode.

### V2-B2538 · v2 lines 14091–14094

**Then:**
- Block movement retains relative order and descendants.
- Keyboard users can perform equivalent movement without a mouse.
- Text-edit arrows/shortcuts do not unexpectedly move blocks.

### V2-B2539 · v2 lines 14096–14096

### UX27-TEST-030 — Block menu targets clicked identity

### V2-B2540 · v2 lines 14098–14098

**Requirements:** UX27-BOARD-027 · **Status:** NOT RUN

### V2-B2541 · v2 lines 14100–14100

**Given:** Different block types; focus initially in another block.

### V2-B2542 · v2 lines 14102–14105

**When:**
1. Open a block context menu after scrolling.
2. Duplicate or convert where supported; Undo.
3. Invoke a disabled action and inspect its reason.

### V2-B2543 · v2 lines 14107–14110

**Then:**
- Commands target the clicked/focused BlockID, not stale selection.
- Conversion is content-preserving or explicitly explains incompatibility.
- Disabled commands do not mutate; menu keyboard/outside dismissal works.

### V2-B2544 · v2 lines 14112–14112

### UX27-TEST-031 — Floating formatting preserves text selection

### V2-B2545 · v2 lines 14114–14114

**Requirements:** UX27-BOARD-028, UX27-SHARED-011 · **Status:** NOT RUN

### V2-B2546 · v2 lines 14116–14116

**Given:** A paragraph with a partial formatted selection.

### V2-B2547 · v2 lines 14118–14120

**When:**
1. Open colour/font/heading controls and apply a change.
2. Undo; move selection to another block and repeat.

### V2-B2548 · v2 lines 14122–14125

**Then:**
- Toolbar focus does not discard or redirect the intended selection.
- Mixed/disabled state reflects real content.
- Exactly selected content changes, stays editable and survives save/reopen.

### V2-B2549 · v2 lines 14127–14127

### UX27-TEST-032 — No accidental embedded freeform app

### V2-B2550 · v2 lines 14129–14129

**Requirements:** UX27-BOARD-029, UX27-BOARD-045 · **Status:** NOT RUN

### V2-B2551 · v2 lines 14131–14131

**Given:** Fresh Native page with text; no intentional Canvas PageType.

### V2-B2552 · v2 lines 14133–14135

**When:**
1. Type, create blocks, revisit the page and restart.
2. Inspect model entities and visual layout.

### V2-B2553 · v2 lines 14137–14140

**Then:**
- One seamless page surface remains.
- No unexplained white Freeform canvas widget, +Box app, repeated empty-state message or duplicate render-created object appears.
- Intentional Canvas pages remain available separately.

### V2-B2554 · v2 lines 14142–14142

### UX27-TEST-033 — Edit View and Locked Unlocked are orthogonal

### V2-B2555 · v2 lines 14144–14144

**Requirements:** UX27-BOARD-030 · **Status:** NOT RUN

### V2-B2556 · v2 lines 14146–14146

**Given:** Page containing flow text, an absolute object and ink.

### V2-B2557 · v2 lines 14148–14150

**When:**
1. Cycle Edit/View and Locked/Unlocked combinations.
2. Attempt text edit, block movement and pen drawing.

### V2-B2558 · v2 lines 14152–14155

**Then:**
- View is read-only.
- Edit plus Locked permits text and ink but prevents unintended layout movement.
- Controls remain visible with meaningful state; locked layout does not silently disable drawing.

### V2-B2559 · v2 lines 14157–14157

### UX27-TEST-034 — Absolute coordinates and snapping

### V2-B2560 · v2 lines 14159–14159

**Requirements:** UX27-BOARD-031 · **Status:** NOT RUN

### V2-B2561 · v2 lines 14161–14161

**Given:** Explicit absolute object plus flow text at non-100% zoom.

### V2-B2562 · v2 lines 14163–14166

**When:**
1. Move/resize with snap on then off.
2. Lock layout; reopen.
3. Explicitly convert between Flow and Absolute where allowed.

### V2-B2563 · v2 lines 14168–14171

**Then:**
- Snapping uses document coordinates and current grid settings.
- Locking preserves geometry and does not flatten absolute content.
- Conversion is deliberate, undoable and distinct from a drag-reorder gesture.

### V2-B2564 · v2 lines 14173–14173

### UX27-TEST-035 — Pen draws on the actual Native page

### V2-B2565 · v2 lines 14175–14175

**Requirements:** UX27-BOARD-032, UX27-BOARD-036 · **Status:** NOT RUN

### V2-B2566 · v2 lines 14177–14177

**Given:** Edit plus Locked; Native page with text; permitted mouse and stylus parameter runs.

### V2-B2567 · v2 lines 14179–14183

**When:**
1. Select Pen.
2. Draw a curved stroke over the actual page, not a separate widget.
3. Switch to text and type.
4. Save/reopen.

### V2-B2568 · v2 lines 14185–14188

**Then:**
- Continuous visible ink follows input and persists as editable strokes.
- Selecting Pen really changes event routing.
- Text remains editable and both content layers reopen correctly.

### V2-B2569 · v2 lines 14190–14190

### UX27-TEST-036 — Ink remains aligned through transforms

### V2-B2570 · v2 lines 14192–14192

**Requirements:** UX27-BOARD-033 · **Status:** NOT RUN

### V2-B2571 · v2 lines 14194–14194

**Given:** A stroke drawn beside a distinctive text block at 125% display scaling.

### V2-B2572 · v2 lines 14196–14199

**When:**
1. Scroll, zoom and resize the window.
2. Draw another stroke.
3. Move the window between supported monitor scales; reopen.

### V2-B2573 · v2 lines 14201–14204

**Then:**
- Stroke coordinates map consistently through page/viewport/display transforms.
- No screen-position drift, jump at start or unexplained offset.
- Existing strokes retain document geometry.

### V2-B2574 · v2 lines 14206–14206

### UX27-TEST-037 — Stroke capture and cancellation

### V2-B2575 · v2 lines 14208–14208

**Requirements:** UX27-BOARD-033, UX27-BOARD-035 · **Status:** NOT RUN

### V2-B2576 · v2 lines 14210–14210

**Given:** Active Pen with a disposable page.

### V2-B2577 · v2 lines 14212–14215

**When:**
1. Draw rapidly across viewport edges; release outside the canvas through supported capture.
2. Simulate cancellation/focus loss.
3. Undo a completed stroke.

### V2-B2578 · v2 lines 14217–14220

**Then:**
- Pointer capture ends cleanly and never leaves drawing stuck.
- Interrupted stroke follows the documented commit/cancel policy.
- One completed stroke is one undo operation, not hundreds of sample-level undos.

### V2-B2579 · v2 lines 14222–14222

### UX27-TEST-038 — Pressure and non-pressure fallback

### V2-B2580 · v2 lines 14224–14224

**Requirements:** UX27-BOARD-034 · **Status:** NOT RUN

### V2-B2581 · v2 lines 14226–14226

**Given:** Supported pressure stylus and a mouse test path, with device capabilities recorded.

### V2-B2582 · v2 lines 14228–14231

**When:**
1. Draw varying-pressure strokes.
2. Repeat with mouse.
3. Place a palm/touch contact during stylus input where supported.

### V2-B2583 · v2 lines 14233–14236

**Then:**
- Pressure affects the configured stroke property when supported.
- Mouse uses a stable fallback and still draws.
- Supported palm filtering prevents unintended content; unsupported hardware is recorded, not falsely passed.

### V2-B2584 · v2 lines 14238–14238

### UX27-TEST-039 — Eraser, colour and brush width

### V2-B2585 · v2 lines 14240–14240

**Requirements:** UX27-BOARD-035 · **Status:** NOT RUN

### V2-B2586 · v2 lines 14242–14242

**Given:** Several strokes of different settings near text blocks.

### V2-B2587 · v2 lines 14244–14247

**When:**
1. Change colour/width and draw new strokes.
2. Erase a selected portion/whole stroke per supported mode.
3. Undo/Redo, save and reopen.

### V2-B2588 · v2 lines 14249–14252

**Then:**
- New settings affect the intended new stroke without unexpectedly recolouring old content.
- Eraser follows its visible mode and does not delete nearby text.
- Restored strokes and settings match the persisted model.

### V2-B2589 · v2 lines 14254–14254

### UX27-TEST-040 — Pen versus selection and panning

### V2-B2590 · v2 lines 14256–14256

**Requirements:** UX27-BOARD-036, UX27-SHARED-012 · **Status:** NOT RUN

### V2-B2591 · v2 lines 14258–14258

**Given:** Native page with selectable text, ink and a scrollable viewport.

### V2-B2592 · v2 lines 14260–14263

**When:**
1. Select text in text mode.
2. Draw in pen mode.
3. Pan/scroll through the documented separate gesture.

### V2-B2593 · v2 lines 14265–14268

**Then:**
- Text selection, ink and panning have deterministic ownership.
- No gesture simultaneously selects text and draws or drags the whole page.
- Mode change releases stale capture and updates cursor/tool state.

### V2-B2594 · v2 lines 14270–14270

### UX27-TEST-041 — Table controls stay compact

### V2-B2595 · v2 lines 14272–14272

**Requirements:** UX27-BOARD-037, UX27-SHARED-018 · **Status:** NOT RUN

### V2-B2596 · v2 lines 14274–14274

**Given:** A 20 by 10 simple table at narrow and wide widths.

### V2-B2597 · v2 lines 14276–14278

**When:**
1. Select no cell, then one cell, then a range.
2. Hover headers/edges and open table overflow.

### V2-B2598 · v2 lines 14280–14283

**Then:**
- Only relevant contextual controls appear.
- No repeated permanent toolbar for every cell or fifty-button command dump.
- All required actions remain reachable with readable labels/shortcuts.

### V2-B2599 · v2 lines 14285–14285

### UX27-TEST-042 — Table edit versus cell selection

### V2-B2600 · v2 lines 14287–14287

**Requirements:** UX27-BOARD-038 · **Status:** NOT RUN

### V2-B2601 · v2 lines 14289–14289

**Given:** Table with mixed text and a selected cell range.

### V2-B2602 · v2 lines 14291–14293

**When:**
1. Use single/double click or documented edit command.
2. Type, Tab/Shift+Tab and arrows in both navigation and text modes.
