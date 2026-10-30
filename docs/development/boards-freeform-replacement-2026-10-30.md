# BF-37 Freeform box replacement — cycle 9

## Flow pinned before implementation

30 October 2026. Base e16a242ed4f0d155a2455afcf83cd469ba34762e, main.
The owner requested the remaining BF-37 destinations after the checked menu repairs.
This milestone implements whole positioned-block replacement with an existing-style
Freeform box. Flow Above/Below and flow replacement are separate placement work: a
canvas object has coordinates, not an index in the flow-block list.

1. In the shared standalone/embedded Native page, select an existing whole positioned
   block and open its inline plus. The authored destination menu shows Replace and its
   transient outline at the existing block. Merely opening, hovering or cancelling does
   not change the document, revision, history, object count or canvas extent.
2. Choose Replace, then Freeform box. Only an editable Native page with unlocked canvas
   objects and an exactly representable canvas frame enables this route. The current
   canvas renderer supports nonnegative x/y and width/height 24–5000. Other frames refuse
   visibly; no silent clamping. Interior positioned-list rows require the whole parent.
3. Capture/revalidate the page, issued block target, list parent, revision and editor
   lifetime. Pending Data creation/link/drafts still block destructive replacement.
4. Under the existing adapter/canonical mutation gates, add one Text canvas object
   labelled “New note”, with the original x/y/width/height/z-order, and remove the whole
   original block and its owned content in the same readable-document edit. Existing
   page canvas objects, ink, unrelated blocks, Data artifacts and other pages survive.
5. Close/clear the transient menu and preview. The existing shared absolute layer renders
   the new box at the previewed frame. One Undo restores the entire prior document;
   Redo restores the same new object identity and frame. Save/reopen preserves them.
   Existing object dragging/arrow movement remains available under the usual guards.

## Inspected ownership and limits

- Both hosts use BoardsViewModel.InsertKindAsync("freeform") → AddCanvasBoxAsync →
  ContractSessionAdapter.AddCanvasObjectAsync → HavenRichNotesOps.AddCanvasObject.
  Existing toolbar creation writes only page.Canvas, at (40,40), with no fake paragraph.
- BlockRenderer renders page.Canvas in the same AbsoluteBlocksHost as positioned blocks.
  This is the existing first-party canvas object path. No Boards build consumes Rnote;
  current donor provenance explicitly records that absence. No donor integration claimed.
- Relative insertion already owns issued targets, single canonical mutation, list-parent
  checks and Tabber page cleanup. Reuse those boundaries; do not simulate replacement by
  separately adding/removing through toolbar commands.
- Canvas objects need persisted optional/default-zero z-order to preserve the replaced
  block's stack order. Old documents retain their existing zero default.
- UX27 Native-page rules prohibit a second appended Freeform app/scrollbox. No new panel
  or paragraph toolbar is introduced. Existing canvas text editing/resize limitations
  are not expanded in this milestone.
- PC .handoff requirements/private ROOT-LIVE projection are unavailable in this cloud
  checkout. This tracked flow is the reviewable pin, not a fabricated PC ledger.

## Validation plan and evidence boundary

Compile each edit unit using serial bounded ordinary Layer1. Retain baseline refusals
and every failure. Check exact history/identity/frame/extent, whole lists/Tabber ownership,
stale targets, locked/unsupported destinations and retained pane lifetime. Exercise real
shared embedded menu pointer/keyboard preview, Escape, commit and save/reopen; capture
actual component rendering. Run owning app and contract regressions and standalone build.
Native PC/ARM NOT RUN; desktop online but native_queue_not_ready/native_contract_changed
at 12:14:09 UTC. Full acceptance PAUSED, package HELD, ARM PC-owned.

## Implemented and checked

The relative command now creates one canonical page.Canvas object and removes the
whole positioned block under the existing readable-document mutation gate. Its x/y,
width/height and new default-zero persisted z-order survive save/reopen. Paragraph,
whole-list and owned Tabber replacement preserve unrelated content; one Undo restores
the exact canonical document and Redo restores object identity. Stale issued targets,
cancelled/retired origins, locked objects, flow destinations, interior positioned-list
rows and unrepresentable frames refuse without inserting or adding history.

Three actual embedded menu cases cover the main page and a Tabber pane: real pointer
menu, authored preview, search, commit, Escape, keyboard movement, separate movement and
replacement Undo/Redo, and reopening the local document. Preview rectangle and resulting
box rectangle are exactly equal in the exercised views. The tests found and repaired:

- The old replacement outline included the 64px control gutter. CUI now binds the exact
  positioned content frame; ordinary flow outlines retain their existing geometry.
- The canvas renderer assigned CUI coordinates to the outer Border, but the absolute
  layout reads its child. Page boxes now use the same coordinate ownership as blocks.
- An empty-flow hint appeared despite real canvas content. It now requires no blocks
  and no canvas objects, preserving the single seamless Native page.

The existing hidden Data draft regression is extended with a Freeform replacement case;
the original Tabber case and all original preservation assertions remain. It explicitly
checks that this destination is available before proving the draft guard refuses it.
Its Data repository/authority/provider substitutions remain labelled in the evidence.

Final fresh Release checks: **436/436 full Boards app tests**, **113/113 full contract
tests**, zero failures/skips. Standalone build succeeds with zero errors and one existing
WebRtcVadSharp AnyCPU-to-x64 warning. Eleven source/test hashes match the final manifest.
All nine serial attempts, the baseline 0/2 and first UI 1/3 failures, and six actual
component captures (three final, three failed) are retained in the cycle 9 evidence JSON
and evidence/boards-freeform-replacement-2026-10-30/. Final peaks: app 1,026,838,528 bytes,
contracts 482,631,680 bytes, standalone 429,584,384 bytes; no guard violations/source drift.

This is **HEADLESS DEVELOPMENT / ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE**. Captures are
actual CUI/Skia output at 1180x900, Glow/Dark, not native screenshots or independent
acceptance. Desktop was reported offline on the 12:35 UTC status query (last observation
12:17:43, native_contract_changed); no native launch. Private ROOT-LIVE remains absent.

## Remaining and next

BF-37 remains checked partial. Flow Freeform destinations, positioned Above/Below and
interior positioned-list replacement remain unavailable. The new route intentionally
refuses negative coordinates or dimensions outside 24–5000 instead of changing the
frame. Existing toolbar box creation remains (40,40); existing Freeform text-edit/resize
limitations and Rnote integration are not resolved by this milestone.

Next pin document-space anchor geometry, scrolling and zoom ownership for Freeform
from a flow target before enabling another relative capability. Preserve single-toolbar,
Data recovery, Attachment/Tabber/lifetime repairs and all prior queue gaps. Package held,
full acceptance paused, native PC/ARM unrun. Usage remains the owner's approximate 8%
observation at 09:10 UTC; no newer allowance reading was observed.
