# Tabber rename and neighboring controls — cycle 6 required flow

Pinned before source/test edits, 30 October 2026. Base main
54625bf9cdd94a22584ae308de19f8072c39e9b3. Cycle 6 owns the remaining rename-blur
interaction review, its reproduced repairs and focused regression evidence.

Authority: owner continuation, checkpoint 8/queue 6; canonical Boards gesture,
stable identity, persistence/history and shared-implementation requirements;
UX27-SHARED-011 focus preservation and UX27-BOARD-027 contextual block actions.

## Required flow

1. Open a real local Board through the shared embedded presentation, with a Tabber,
   its editable Native child and a neighboring flow paragraph. Type a new pane name
   using raw keyboard input. Click the neighboring inline plus with an actual
   press/release, including a held press with dispatcher work before release.
2. The name must commit to the same stable pane. The first click opens the real
   destination menu; selection and insertion act on that neighboring block, never
   on a rebuilt/disconnected control. Escape cancels insertion without undoing the
   rename or adding an insertion history entry. Save/reopen retains the name.
3. Repeat from the name into the child toolbar and child paragraph editor. The
   first action must reach the intended child, and editing focus/caret must survive
   the rename. Verify canonical effects, history and persisted parent/pane identities.
4. Preserve the checked own-tab rename/select atomic gesture, retained-control
   retirement, removal/Undo fresh editors, View-mode and stale-origin refusal.
   Delayed work must not rename a different pane or revive a retired owner.
5. Use a narrow lifetime/render repair if the failure reproduces. Do not synthesize
   clicks, add arbitrary timing delays, weaken the stale-owner checks, or build a
   second presentation. CUI remains the application-authoring authority; broader
   legacy Tabber/toolbar CUI migration stays an explicit open requirement.

## Validation and boundaries

Compile each edit unit with the existing serial bounded ordinary Layer1 runner.
Retain the executable red case, all compiler/fixture failures and final nonzero
focused tests; run owning app checks once the repair is stable. Inspect an actual
headless component capture of the changed journey. This is development evidence,
ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE; native desktop/ARM NOT RUN, acceptance PAUSED,
package HELD. ROOT-LIVE recovery freshly refuses the absent private projection.
Usage UNKNOWN. No new branches, worktrees, models, private ledger or emails.

If the focus defect is confirmed, its coherent repair is this cycle's milestone;
Data creation/reference/link ownership and the next BF-37 capability remain next.
If not reproduced, record exact exercised cases before advancing the queue.

## Cycle 6 result

The six executable quick/held gesture cases reproduced lost neighboring insert,
child-toolbar and paragraph focus. Pure rename now updates only Tabber metadata
and its owned page title, preserving unrelated row/control identity and later
unmerged child drafts. The canonical projection supplies the new merge baseline.
Metadata ownership advances synchronously before releasing the adapter gate;
visual name updates run on UI with a fresh, terminal control lease. Structural
changes still replace projection and retire old owners.

A long caption wrapping the header reproduced another lost held-toolbar click.
Caption layout now waits for the actual pointer release/capture-loss routing to
finish; canonical name and lease updates are immediate. There is no delay timer,
synthetic click or replay into replacement controls. Cancellation and a neighboring
block below the Tabber are checked. A save completing before the UI callback and
ordinary detach/reattach each exposed a metadata-lifetime failure; both are repaired.
Session/theme subscriptions now last until Dispose, separately from Attach handlers.

Fresh final Release: **382/382 full Boards app tests** and **113/113 contract tests**,
zero failures/skips. Standalone build passed with the existing WebRtcVadSharp
AnyCPU-to-x64 native-DLL warning. All five changed source/test hashes match the final
manifest. All **18 attempts**, including fixture/compile failures and actual RED
gestures, remain in `boards-tabber-focus-cycle6-2026-10-30.evidence.json` and the
private runner directories. Two visually inspected actual 1180x900 embedded-host
captures under `evidence/boards-tabber-focus-2026-10-30/` show the reopened menu and
retained Above destination after rename. These are headless component captures;
the theme enum was not instrumented, and no theme-matrix/native acceptance is claimed.

Fifteen added cases cover first-click/focus preservation, actual insertion, distinct
rename/insertion undo boundaries, save/reopen identities, long-caption gestures,
capture cancellation, delayed publication, detach/reattach and later child drafts.
No final executed-suite failures remain. Full acceptance, native PC/ARM and packaging
remain gated. BF-37 database/freeform and remaining positioned destinations are open.

## New owner direction received during this cycle

The owner says: “No need to show a floating toolbar while editing a paragraph as
there's already a toolbar at the top.” Preserve the top toolbar and eliminate the
duplicate paragraph-formatting surface. Current source inspection found no separate
floating paragraph-format popup on the main Native page; the actual capture does
show a second formatting row inside Tabber panes. The working interpretation for
the next cycle is to route the top toolbar to the focused pane before removing that
duplicate, so paragraph formatting is not lost. A concise surface-location question
was sent; any owner answer supersedes this interpretation. No toolbar is removed or
claimed repaired in cycle 6. This owner-directed visible work precedes Data expansion.
