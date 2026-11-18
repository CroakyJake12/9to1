# Boards — seamless pages, blocks and ink — Embedded artifact ownership and lifecycle; Collaboration and concurrency (part 05)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1618 · v2 lines 7115–7115

Zoom, DPI scaling, window resizing and panning are view transforms only and MUST NOT rewrite persisted Board coordinates. Absolute blocks and free ink use the same stable document-space basis where applicable so objects/strokes do not drift relative to each other. Conversion into CUI/layout coordinates occurs only at render/input boundaries.

### V2-B1619 · v2 lines 7118–7118

Embedded artifact ownership and lifecycle

### V2-B1620 · v2 lines 7121–7121

Every mutable embedded/hosted app artifact MUST carry explicit ownership semantics independent from its BoardPage reference. At minimum, Boards distinguishes BoardOwned artifacts created as part of that Board page from Referenced artifacts that are independently owned elsewhere. Attaching an existing Write/Canvas/Data/etc. artifact creates a reference and MUST NOT transfer ownership merely because it appears inside Boards.

### V2-B1621 · v2 lines 7124–7124

Deleting or recovering a page follows the referenced artifact's ownership contract. Recoverable deletion of a BoardOwned artifact remains recoverable with its owning page/Board. Permanent purge MAY cascade to BoardOwned artifacts only through an explicit destructive operation whose impact preview names the affected artifacts. Referenced artifacts MUST NOT be deleted by deleting the Board/page. Move, rename, nesting and Board switching never change artifact identity or ownership.

### V2-B1622 · v2 lines 7127–7127

Collaboration and concurrency

### V2-B1623 · v2 lines 7130–7130

Boards collaboration MUST resolve concurrent work at the narrowest stable entity possible. Independent block/object edits and independent ink strokes SHOULD merge without forcing a whole-page conflict. Page-tree operations MUST use deterministic stable ordering/parent semantics so simultaneous moves/inserts do not silently reorder unrelated pages.

### V2-B1624 · v2 lines 7133–7133

Concurrent incompatible edits to the same canonical block/object/artifact MUST surface the owning object's merge/conflict behaviour rather than blind last-write-wins. Presence, remote cursors, selections and transient viewport state are ephemeral collaboration state and MUST NOT be written into canonical Board content. Reconnect/offline replay MUST preserve operation identity so retries cannot duplicate mutations.

### V2-B1625 · v2 lines 7136–7136

Platform capability behaviour

### V2-B1626 · v2 lines 7139–7139

Boards MUST ship useful supported native behaviour on Windows and 9to1-OS and a useful web version under the global web-product contract. All clients operate on the same Board/Page/artifact identities and canonical revisions. Platform capability differences MUST be explicit and queryable rather than silently dropping features.

### V2-B1627 · v2 lines 7142–7142

Browser/native differences may include pen pressure/tilt, OS file drag-and-drop, clipboard richness, filesystem access, windowing and availability of specialised embedded surfaces. When a capability is unavailable, Boards MUST provide the closest useful supported interaction or a clear unsupported-capability state without corrupting or flattening the underlying artifact.

### V2-B1628 · v2 lines 7145–7145

Performance and resource behaviour

### V2-B1629 · v2 lines 7148–7148

Large Boards MUST use incremental/lazy loading and virtualisation. Opening a Board MUST NOT eagerly instantiate every nested page, every off-screen block, every source document or every embedded app surface. Large block lists, Data-backed views and page trees SHOULD virtualise off-screen content while preserving correct selection, search, keyboard navigation and accessibility semantics.

### V2-B1630 · v2 lines 7151–7151

Drawing rendering MUST cull/incrementally render off-screen strokes; previews and embedded-surface caches MUST be bounded and revision-invalidated; Board/search/source indexing MUST update incrementally; and expensive embedded artifacts SHOULD hydrate on demand. Performance acceptance MUST include representative large-Board, large-page, drawing-heavy and collaboration scenarios on the declared supported-platform baseline.

### V2-B1631 · v2 lines 7154–7154

Persistence, history and recovery

### V2-B1632 · v2 lines 7157–7157

Board/page/block edits MUST use revisioned, crash-safe persistence through the owning storage layer. A process/app crash MUST NOT corrupt the entire Board because one page/block write was interrupted. Recovery MUST identify the affected Board/Page and restore the latest consistent revision or surface a structured conflict/recovery state.

### V2-B1633 · v2 lines 7160–7160

Delete semantics for Boards and Pages MUST be recoverable by default unless an explicit permanent-delete/purge action is invoked under the applicable high-risk permission flow. Deleting a Board container MUST NOT silently permanently delete separately owned embedded artifacts without explicit cascade semantics and user-visible impact.

### V2-B1634 · v2 lines 7163–7164

Concurrent edits MUST use revision/conflict semantics rather than blind last-write-wins when the same canonical object is changed by multiple clients/Spaces/Automations.
Visible persistence and startup truthfulness: Autosave/status controls MUST distinguish actual dirty, saving, saved, failed, offline/reconnecting and revision-conflict states; the interface MUST NOT show success while changes are not durably stored. Save status should be subtly integrated with existing page chrome, not an oversized persistent diagnostic/status strip; storage/capabilities diagnostics belong in an appropriate utility surface. During startup, Board switching, reload or shared-engine hydration, render intentional, stable loading/recovery UI rather than a misleading wrong-theme/default-control shell, an unrelated provisional interface or unexplained content flashes. Investigate startup delay/scrolling/editor latency through measurement, and preserve selection/unsaved edits when safe while transitioning.

### V2-B1635 · v2 lines 7167–7167

Boards API

### V2-B1636 · v2 lines 7170–7201

9to1.Boards.ListBoards();
9to1.Boards.GetBoard(boardID);
9to1.Boards.CreateBoard(config);
9to1.Boards.UpdateBoard(boardID, patch);
9to1.Boards.DeleteBoard(boardID);
9to1.Boards.RestoreBoard(boardID);
9to1.Boards.SetActiveBoard(boardID);
9to1.Boards.ListPages(boardID);
9to1.Boards.GetPage(pageID);
9to1.Boards.CreatePage(boardID, pageType, parentPageID?, position?);
9to1.Boards.MovePage(pageID, parentPageID?, position);
9to1.Boards.UpdatePage(pageID, patch);
9to1.Boards.DeletePage(pageID);
9to1.Boards.RestorePage(pageID);
9to1.Boards.SetEditMode(pageID, Edit | View);
9to1.Boards.SetLayoutMode(pageID, Locked | Unlocked);
9to1.Boards.Block.List(pageID);
9to1.Boards.Block.Insert(pageID, block, location);
9to1.Boards.Block.Update(blockID, patch);
9to1.Boards.Block.Move(blockID, location);
9to1.Boards.Block.SetLayout(blockID, Flow | Absolute, layout?);
9to1.Boards.Block.Delete(blockID);
9to1.Boards.Drawing.AddStroke(pageID, stroke);
9to1.Boards.Drawing.UpdateStroke(strokeID, patch);
9to1.Boards.Drawing.DeleteStroke(strokeID);
9to1.Boards.Graph.Create(pageID, graph);
9to1.Boards.Graph.Update(graphID, patch);
9to1.Boards.Graph.SetViewVariable(graphID, variableID, value);
9to1.Boards.Chat.Send(pageID, prompt);
9to1.Boards.Chat.ListSources(pageID);
9to1.Boards.Chat.AddSource(pageID, source);
9to1.Boards.Chat.RemoveSource(pageID, sourceID);

### V2-B1637 · v2 lines 7204–7204

These are minimum canonical entry points, not the exhaustive catalogue. Every additional user-visible Boards operation — including donor-parity actions and contextual-island controls — MUST have an equivalent supported API/domain action under the global API-parity rule. APIs MUST use stable IDs, revision checks where required and structured partial/error results.

### V2-B1638 · v2 lines 7207–7207

Permissions and risk

### V2-B1639 · v2 lines 7210–7210

Read, edit, share/admin, external-action and destructive permissions MUST remain distinct. View mode does not grant edit permission, and Edit mode cannot override an account/object ACL. Chat/AI/tool actions inherit the effective caller/Agent permissions and the global Home permission broker.

### V2-B1640 · v2 lines 7213–7213

Permanent Board/Page purge, destructive bulk actions, permission/share changes and consequential external actions initiated from Chat/AI MUST use the elevated-risk approval rules defined globally. Ordinary text/block edits use normal edit scope but remain audited where performed by automation/AI.

### V2-B1641 · v2 lines 7216–7216

Failure behaviour

### V2-B1642 · v2 lines 7219–7219

Boards MUST return structured failures for BoardNotFound, PageNotFound, BlockNotFound, UnsupportedPageType, UnsupportedBlockType, InvalidLayoutTransition, RevisionConflict, PermissionDenied, SourceIngestionFailed, ScopedChatExternalAccessDenied, DependencyUnavailable and SharedEngineIncompatible, plus the global error contract.

### V2-B1643 · v2 lines 7222–7222

If Home or the Shared Productivity Engine becomes unavailable after Boards has opened, Boards MUST stop new unsafe mutations, preserve already loaded unsaved state for recovery where possible and surface a repair/reconnect state. It MUST NOT silently switch to an incompatible private document engine.

### V2-B1644 · v2 lines 7225–7225

Acceptance requirements

### V2-B1645 · v2 lines 7228–7258

Boards is implementation-complete only when acceptance coverage verifies at least:
* The landing page, Board switcher and nested page sidebar operate on persisted Boards/Pages and survive restart.
* Applicable AppFlowy donor features/block types are accounted for by the donor-parity manifest and tested against the selected donor revision.
* Native pages render on a seamless background with no page-card/sheet frame.
* Locked mode preserves AppFlowy-style flow behaviour; Unlocked mode supports arbitrary block placement; relocking preserves absolute coordinates.
* Rnote-derived drawing works in both Locked and Unlocked layout modes and survives save/restart.
* The floating top contextual island adapts across text, image, table, drawing and graph selections.
* The floating island remains usable during scrolling; its Paragraph/H1–H6/custom-style selector follows the caret, converts actual blocks, preserves content/valid marks and round-trips through undo/redo and restart.
* B/I/S/U applied from the top island changes selected text only without losing caret/selection or editor focus; keyboard shortcuts, active/mixed/disabled states and common lists/link/Insert controls behave correctly.
* Both the persistent + Insert and inline + / six-dot (::) controls insert or reorganise persisted blocks at correct positions; slash commands, context menus, drag/drop and keyboard navigation work without unintended text selection or movement.
* The library, Board switcher, nested Page tree, breadcrumbs and single editable Page title communicate stable Board/Page identities clearly at normal and narrow window sizes, without repeated titles, oversized button stacks or fake content.
* Runtime visual inspection verifies readable CUI hierarchy, valid icon assets (no missing-glyph boxes), clear hover/focus/cursor states, correctly anchored menus, usable scrolling and overflow, no native-styled visual leakage, and no toolbar/AI-bar occlusion.
* Editing/save/reload/restart exercises demonstrate truthful saved/dirty/failure/offline/conflict status and genuine persistence, history and recovery. Screenshots, successful builds and unit tests alone do not establish interaction-quality verification.
* Edit/View switching blocks authoring in View while graph sliders remain usable as non-persistent interactions.
* H1-H6 and named custom header/paragraph styles persist and round-trip.
* Graph blocks render multiple simultaneous plots, shaded regions and interactive slider variables without flattening.
* Enhanced image/table formatting persists and remains script/API-addressable.
* A shared object copied Boards → Write/Present/Canvas/Spaces and back retains equivalent shared schema/formatting.
* Write and Canvas PageTypes edit the same canonical artifact behaviour as their standalone surfaces.
* Chat can reason over Board context and use only authorised Den/app capabilities.
* Scoped Chat cannot search/use external information and reports insufficient source evidence when its Board/source corpus does not support an answer.
* Uploaded Chat source subpages expose correct ingestion state and become/revoke usable context deterministically.
* AI bar mutations use typed actions and cannot mutate Board content in View mode without authorised Edit semantics.
* Crash/restart and revision-conflict tests preserve Board/Page integrity without silent last-write-wins data loss.
* AppFlowy database-like Board items use canonical Data-backed state rather than a second Boards-only database engine.
* Absolute blocks and ink retain stable Board document-space coordinates across zoom, pan, DPI/window changes and restart.
* BoardOwned versus Referenced embedded-artifact deletion/recovery semantics are enforced without accidental cascade deletion.
* Concurrent independent block/ink/page operations merge deterministically while same-object conflicts never fall back to silent last-write-wins.
* Windows, 9to1-OS and web clients capability-negotiate declared differences while preserving the same canonical Board/Page/artifact identities.
* Representative large Boards/pages/Data views/drawing workloads remain responsive through lazy loading, virtualisation, incremental rendering/indexing and bounded caches.
* When Home is absent at process start, Boards refuses normal launch and enters the required Install Home bootstrap flow.

### V2-B2393 · v2 lines 13636–13636

## Boards

### V2-B2394 · v2 lines 13638–13638

### UX27-TEST-001 — Create without Files, account or cloud

### V2-B2395 · v2 lines 13640–13640

**Requirements:** UX27-BOARD-003, UX27-DATAFLOW-001 · **Status:** NOT RUN

### V2-B2396 · v2 lines 13642–13642

**Given:** Disposable profile; no Files connection, accounts, AI provider or designated Boards folder.

### V2-B2397 · v2 lines 13644–13647

**When:**
1. Launch Boards through normal Apps entry.
2. Activate New Board, type a title and a sentence.
3. Close and reopen using the normal application route.

### V2-B2398 · v2 lines 13649–13652

**Then:**
- Create is visible and usable before any connection.
- A stable local board/page is created, edits are durable, and reopening restores them.
- No mandatory folder, cloud or Files-connection dialog appears.

### V2-B2399 · v2 lines 13654–13654

### UX27-TEST-002 — Cancel Save As retains the draft

### V2-B2400 · v2 lines 13656–13656

**Requirements:** UX27-BOARD-004, UX27-BOARD-005 · **Status:** NOT RUN

### V2-B2401 · v2 lines 13658–13658

**Given:** Unnamed local board containing text, ink and a second page.

### V2-B2402 · v2 lines 13660–13663

**When:**
1. Invoke Save As.
2. Browse to a different folder, then cancel.
3. Continue editing; close and reopen the draft.

### V2-B2403 · v2 lines 13665–13668

**Then:**
- No chosen destination is falsely recorded as saved.
- All content remains available with truthful local-draft status.
- Cancelling the chooser neither deletes the draft nor sends the user through Files setup.

### V2-B2404 · v2 lines 13670–13670

### UX27-TEST-003 — Save an editable file outside a Boards folder

### V2-B2405 · v2 lines 13672–13672

**Requirements:** UX27-BOARD-005, UX27-BOARD-006 · **Status:** NOT RUN

### V2-B2406 · v2 lines 13674–13674

**Given:** A mixed board; a permitted arbitrary test folder unrelated to the app library.

### V2-B2407 · v2 lines 13676–13679

**When:**
1. Save As to that folder with extension .9to1board.
2. Inspect the actual written artifact.
3. Close the board and open that file through File Open and the OS association.

### V2-B2408 · v2 lines 13681–13684

**Then:**
- The file exists at the selected location and is an editable document, not an image or account-dependent pointer.
- Page hierarchy, rich blocks, tables, graphs, ink and owned assets round-trip.
- No silent relocation into a special folder occurs.

### V2-B2409 · v2 lines 13686–13686

### UX27-TEST-004 — Moved-file identity and fresh access

### V2-B2410 · v2 lines 13688–13688

**Requirements:** UX27-BOARD-007, UX27-BOARD-009 · **Status:** NOT RUN

### V2-B2411 · v2 lines 13690–13690

**Given:** A saved board with captured BoardID/PageIDs and no connected Files account.

### V2-B2412 · v2 lines 13692–13695

**When:**
1. Close its handles.
2. Move/rename the file using authorised OS operations.
3. Open the new path and restart the app again.

### V2-B2413 · v2 lines 13697–13700

**Then:**
- Logical board/page identities remain consistent.
- A new permitted handle is acquired instead of replaying a prior one-shot lease.
- Recent-location repair does not duplicate pages or create a dead tab.

### V2-B2414 · v2 lines 13702–13702

### UX27-TEST-005 — Save a Copy is an independent document

### V2-B2415 · v2 lines 13704–13704

**Requirements:** UX27-BOARD-007 · **Status:** NOT RUN

### V2-B2416 · v2 lines 13706–13706

**Given:** A saved board with owned objects and one explicitly external reference.

### V2-B2417 · v2 lines 13708–13710

**When:**
1. Use the deliberate Save a Copy/Duplicate action.
2. Edit the copy and reopen both documents.

### V2-B2418 · v2 lines 13712–13715

**Then:**
- Copy ownership/IDs follow the duplication contract without accidentally merging the documents.
- Original content remains unchanged.
- External references remain explicit and access-controlled rather than being secretly copied.

### V2-B2419 · v2 lines 13717–13717

### UX27-TEST-006 — Crash during save preserves a complete revision

### V2-B2420 · v2 lines 13719–13719

**Requirements:** UX27-BOARD-008, UX27-DATAFLOW-004 · **Status:** NOT RUN

### V2-B2421 · v2 lines 13721–13721

**Given:** Disposable board with a known valid file and a second modified revision; controllable storage fault injection.

### V2-B2422 · v2 lines 13723–13726

**When:**
1. Interrupt before final file replacement, then reopen.
2. Repeat around the final commit boundary.
3. Inspect recovery and file integrity.

### V2-B2423 · v2 lines 13728–13731

**Then:**
- The old complete or new complete revision opens, not a partial mixture.
- Unsaved recoverable edits remain available and failure is truthful.
- Saved is never announced merely because serialization began.

### V2-B2424 · v2 lines 13733–13733

### UX27-TEST-007 — Disk-full, read-only and disconnected destination

### V2-B2425 · v2 lines 13735–13735

**Requirements:** UX27-BOARD-008, UX27-SHARED-015 · **Status:** NOT RUN

### V2-B2426 · v2 lines 13737–13737

**Given:** Parameterised authorised failing destination; a board with new unsaved edits.

### V2-B2427 · v2 lines 13739–13742

**When:**
1. Attempt Save to disk-full/read-only/disconnected storage.
2. Choose a different permitted destination via Save As.
3. Reopen and compare content.

### V2-B2428 · v2 lines 13744–13747

**Then:**
- Failure identifies the actual destination and cause; edits remain intact.
- Recovery copy is not labelled as successful sync to the failed destination.
- New destination contains the complete editable revision.

### V2-B2429 · v2 lines 13749–13749

### UX27-TEST-008 — External edit conflict

### V2-B2430 · v2 lines 13751–13751

**Requirements:** UX27-BOARD-008, UX27-BOARD-046 · **Status:** NOT RUN

### V2-B2431 · v2 lines 13753–13753

**Given:** Two authorised editors of one disposable board at the same base revision.

### V2-B2432 · v2 lines 13755–13757

**When:**
1. Edit different and then overlapping content in separate runs.
2. Attempt conflicting saves and inspect reconciliation.

### V2-B2433 · v2 lines 13759–13762

**Then:**
- Revision conflict is detected rather than silently choosing the latest timestamp.
- Both revisions are recoverable; chosen resolution is explicit.
- Undo and references remain coherent after resolution.

### V2-B2434 · v2 lines 13764–13764

### UX27-TEST-009 — Portable content versus external references

### V2-B2435 · v2 lines 13766–13766

**Requirements:** UX27-BOARD-006, UX27-DATAFLOW-006 · **Status:** NOT RUN

### V2-B2436 · v2 lines 13768–13768

**Given:** Board with owned image/ink/embedded content and a private external referenced artifact.

### V2-B2437 · v2 lines 13770–13773

**When:**
1. Save and open the file in a clean authorised profile.
2. Inspect owned content and unavailable external target.
3. Inspect package contents for secrets.

### V2-B2438 · v2 lines 13775–13778

**Then:**
- Owned supported content opens portably.
- External access is requested or marked unavailable without copying private material or deleting the reference.
- No credentials, bearer tokens or absolute-path-only identity are packaged.

### V2-B2439 · v2 lines 13780–13780

### UX27-TEST-010 — Landing, recents and real transitions

### V2-B2440 · v2 lines 13782–13782

**Requirements:** UX27-BOARD-010, UX27-SHARED-017 · **Status:** NOT RUN

### V2-B2441 · v2 lines 13784–13784

**Given:** A fresh profile and another profile with an existing board plus an unavailable recent file.

### V2-B2442 · v2 lines 13786–13789

**When:**
1. Open the landing page in both profiles.
2. Search and open a real recent item.
3. Activate Locate/Remove from Recent for the unavailable item.

### V2-B2443 · v2 lines 13791–13794

**Then:**
- Landing and editing are distinct routed experiences.
- No seeded fake boards or duplicated Library heading.
- Removing a Recent item does not delete the actual document.

### V2-B2444 · v2 lines 13796–13796

### UX27-TEST-011 — Exact agreed shell topology

### V2-B2445 · v2 lines 13798–13798

**Requirements:** UX27-BOARD-001, UX27-BOARD-002, UX27-SHARED-005 · **Status:** NOT RUN

### V2-B2446 · v2 lines 13800–13800

**Given:** Board with enough nested pages to overflow the sidebar.

### V2-B2447 · v2 lines 13802–13805

**When:**
1. Open the board at wide and narrow widths.
2. Scroll the tree.
3. Change theme while retaining the same selected page.
