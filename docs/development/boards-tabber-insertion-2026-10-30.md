# BF-37 Tabber destination — cycle 5 required flow

Pinned before code edits, 30 October 2026. Base main 0f9081f1a5501f2be34e99b4cf1c61ff50a2672e.
Cycle 5 owns relative Tabber insertion, its necessary pane lifetime protections and focused tests.
Authority: current owner continuation, checkpoint 7/queue 5, canonical v2 Boards stable nested identity,
Edit/View, shared implementation and persistence/history requirements, and the inline destination request.

## Required flow

1. On an editable Native parent page, use the actual inline plus, preview Above/Below/Replace, retain
   that destination through type selection, and choose Tabber. Escape/dismissal changes no document or
   history. Existing destination refusals remain: positioned Above/Below and interior positioned lists.
2. Capture the existing issued destination ticket, origin and lifetime. Under the adapter/canonical
   gates revalidate those facts, the Native page type and the absence of a Tabber owner on that page.
   Nested Tabbers stay unsupported. Do not fall back to toolbar insertion or another page.
3. Inside one rollback-protected canonical mutation, use HavenTabberOps.Add to create a real Tabber and
   its owned Native pane page. Move that same block to the resolved flow/list boundary. Do not use
   PrepareInsertion's metadata-only Tabber mapping, which cannot create owned pages. Replacement uses
   RemoveBlock, including old owned-pane cleanup, and preserves whole positioned geometry. Check the
   persisted document size before admitting history. Refusal leaves no partial block or orphan pane.
4. Render the actual new Tabber, edit its pane, save and reopen. Stable block/pane/page/parent/owner IDs
   survive persistence. One Undo restores the entire previous block and owned pages; Redo restores
   exactly the new IDs. Replacing an existing Tabber preserves unrelated containers and page content.
5. Preserve unsaved Data field drafts and pending creation/link work in all cached panes, including
   hidden panes. Apply the existing conservative structure-change refusal before replacement and
   recheck before commit; refusal must retain the live pane owners. Do not commit a whole-manager
   close preparation to remove one Tabber.
6. A removed pane editor must never redirect a retained command to CurrentPage's parent fallback.
   Bind child models to immutable page/parent/owner identity, revoke on removal before deferred UI
   disposal, and mount fresh children after Undo. Retired metadata controls also stay inert after
   removal/Undo. Ordinary edits inside a still-owned pane must leave current tab controls usable.
   A pending tab rename followed by a tab action commits together, so blur cannot rebuild away the
   clicked control. Leaving the group without activating an action still commits the pending name.

## Source and validation plan

Reuse BoardsViewModel.Insertion, ContractSessionAdapter.Insertion and HavenTabberOps. The existing CUI
destination/type/preview surface remains the real entry point. No parallel UI is added. Existing legacy
Tabber native controls are reused, with lifetime wiring only; their wider CUI migration is not claimed.
Keep lifecycle ownership in BoardsTabberBlocks/BoardsViewModel.Tabber and join deferred disposals.

First reproduce refusal with persisted Above/Below/Replace tests. Compile each edit unit, then validate
owned-page identity, exact undo/redo, flow-list boundaries, positioned whole replacement, nested/stale
refusal, retained child/control lifetimes and pending pane work. Exercise actual mounted menu input and
capture the changed real presentation. Run owning app/contract regression suites serially under the
existing bounded Layer1 runner, retain all failed attempts and review the final diff before main push.

Evidence is headless ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE. Native PC/ARM are NOT RUN; desktop is offline,
private ROOT-LIVE projection recovery remains BLOCKED, acceptance PAUSED and package HELD. Usage UNKNOWN;
observed <=5% and 2026-11-01T00:00:00Z are stop boundaries. Database/freeform, remaining spatial edges,
lossless Design mappings and legacy toolbar conversion remain outside this milestone. Browse is preserved.

## Cycle 5 result and evidence

Relative Tabber is checked for Above/Below/Replace on editable Native parent pages, including whole and
interior flow lists and whole positioned replacement. The actual embedded inline menu reaches Tabber;
its pane editor accepts text and saves/reopens the same block/pane/page/parent/owner identities. One
Undo/Redo restores the full prior/new structure. Nested Tabbers, cancellation, retired origins and
same-content-after-Undo tickets refuse without partial owned pages. Existing spatial refusals remain.

Removed pane models retire permanently before asynchronous cleanup; Undo mounts fresh editors. Current
metadata controls survive child-text projection refresh, while retained removed controls and late
attachment results cannot write into restored IDs. Hidden Data field drafts and pending creation/link
work prevent replacement. Real RecordsUI draft tests retain the same editor after refusal and allow
replacement after explicit discard. Only their Data repository/authority/provider ports are substituted.
Draft ownership/counts are published for worker-thread admission; control teardown runs on UI and joins
render/ink/Data drains. The existing toolbar attachment path still uses two edits, but its new projected
block is merged before canonical attachment lookup. A tab rename plus own tab-selection gesture now
commits together and has exact single Undo/Redo. This does not establish every legacy blur interaction.

Fresh Release checks: **367/367 full Boards app**, **113/113 full Boards contract**, zero failures/skips.
Standalone build passed with the existing WebRtcVadSharp AnyCPU-to-x64 warning. All 16 changed source/test
hashes match the final manifest. All 20 attempts, failures, source snapshots, commands and case outcomes
are retained in `boards-tabber-insertion-cycle5-2026-10-30.evidence.json` and the private cycle5 runner
directories. Two visually inspected actual embedded-host 1180x760 captures are under
`evidence/boards-tabber-insertion-2026-10-30/`. They are headless component evidence, not native acceptance.

BF-37 remains CHECKED PARTIAL. Relative database/freeform, positioned Above/Below and interior positioned
list replacement remain gaps. The existing legacy Tabber native controls still need broader CUI/focus
review: rename blur followed by a neighboring inline plus or child toolbar action is unverified; the
own metadata-group gesture is the checked scope. Reproduce that interaction first, then inspect/pin the
actual Data creation/reference/link ownership flow for the next dependency-ready relative capability.
Lossless Design mappings, legacy toolbar conversion, native PC/ARM and package gates remain open.
