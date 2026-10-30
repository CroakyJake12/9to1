# BF-37 insertion preview — required flow and implementation sequence

Source: owner's 29 October plus-menu request, retained in the overnight handoff; canonical CUI
ActionEvent/typed actions and Boards inline insertion; UX27-SHARED-002/009/010/013. Starting main 0b758fc6.

## User flow pinned before implementation

1. In an editable native Board page, activate the inline plus for one stable block.
2. Hover or keyboard-focus Add Block Above/Below: show the actual corresponding insertion edge.
   Hover/focus Replace Current Block: show that block's red outline and translucent red tint.
3. Preview changes no content, IDs, revision, dirty state, undo history or row/text geometry. The overlay
   is noninteractive and cannot cover input targets. Other Boards/menu instances remain independent.
4. Choosing the destination advances to the existing type list while retaining its preview. Change returns
   to destination selection. Type activation continues through the existing typed insertion/replacement.
5. Pointer/focus departure, popup dismissal/Escape, commit, target loss, row detach and page/session/View
   transitions clear applicable transient state. An old row's delayed exit cannot erase a newer preview.

## Dependency sequence

The current destination rows live in ToolbarBuilder.AddDestinationStage; BlockRenderer.WithSelection
has a zero-footprint overlay host. No typed BlockInsertionPreview state exists. The CUI event contract
recognises PointerEnter/PointerExit but rejects them with CUIE003, so authored preview cannot execute yet.

First implement the existing canonical named-action PointerEnter/PointerExit subset in shared CUI.
Each event family has at most one route; direct Action, Clicked and OnceClick share the click family.
Pointer enter/exit may coexist with a click route. Dispatch uses the same retained async action pipeline,
live bound parameters, current or recorded query dispatcher, cancellation and retirement rules. Rewiring
must be idempotent; retired/replaced/disabled controls must not start work. An exit cannot become a click.
Keep unsupported Style overlays, stateful Hover/OnceHover, explicit targets, bubbling and Event context
diagnostics explicit; this small step is not full ActionEvent conformance.

Read-only framework review found no authored focus-to-action route: FocusRequested is an input request,
not observed focus state, and Effects have no exported native IsFocused binding. The prerequisite also
adds the bounded generic Button ActionEvents `Focused` and `Blurred`, mapping genuine direct-origin
GotFocus/LostFocus to the same typed pipeline. This is an explicit CUI extension needed by the owner's
keyboard-preview flow; those two names were not in the original CUI ActionEvent vocabulary. Each is its
own route family. Pointer and focus preview ownership must remain independent so departure from one
cannot erase a newer or still-active preview from the other. No native app-specific callback substitutes
for the authored actions. The canonical attachment remains unchanged.

Then author destination/preview presentation and publish typed transient session/page/block/placement/
owner-generation state without rebuilding the popup anchor. Resolve the keyboard-focus path through a
genuine canonical runtime capability, not app-specific native code-behind. Test actual pointer/keyboard,
geometry, cleanup, insertion position and isolation before claiming BF-37 implemented.

## Evidence limits

Compiler/runtime tests are Layer1 development evidence. No native PC/ARM execution is implied. Full
independent acceptance remains paused. Partial prerequisites do not establish the visible preview.

## Cycle 2 result — framework prerequisite checked

Starting main `0b758fc6747afef93de77716f74783e2fb131766` was clean and equal to refreshed origin/main.
This cycle implements the shared CUI prerequisite, including the lifecycle repairs exposed by genuine
regression execution. **The Boards transient preview model, destination document and visible overlay are
still pending.** No app-completion or acceptance claim follows from these checks.

- Named PointerEnter/PointerExit and the explicitly documented Focused/Blurred extension execute through
  the retained typed-action pipeline. Each family has its own invocation and subscription; click aliases
  remain one family. Duplicate routes are rejected, including typed documents and Definition composition.
- Actual headless pointer/Tab/Space/arrow/Enter input checks cover Buttons and authored context-menu items.
  Menu selection handles focus notifications internally, so owned focus actions observe those completed
  transitions while preserving native handling. Descendant focus does not invoke a parent owned route.
- Live parameters, current global dispatcher, retained query/Repeat ownership, local state isolation,
  repeated wiring, disabled ancestors, disposal and same-ID replacement are exercised. A parameter getter
  that retires/replaces/disposes its origin cannot start an old command afterwards.
- Input SubmitAction now checks its actual authored input and captured frame; it was incorrectly using the
  root-effect origin check. Real input tests cover modifiers, IME/read-only/disabled refusal, re-enabling,
  edit-revision deduplication and concurrent retained submissions.
- A floating popup's NativeClose receipt stays pending until all Closed handlers finish; a later original
  failure is retained. Hiding a floating host retires its popup before its cell leaves layout. Nested
  visible content recovers its retained measure path, with observation bounded to authored/content ownership
  and measure invalidation gated on actual membership changes. Reentrant focus replacement cannot resurrect
  the old child/cell. Authored identity and original failure receipts remain checked.

## Validation and retained failures

SDK `10.0.401`, Release, serial bounded ordinary Layer1 runner, fresh compilation for each edit unit.
The final source did not change during any final run; all three finished without guard violations:

| Run | Executed | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| Focused CUI runtime regression (`cycle2-runtime-regression3`) | 234 | 234 | 0 | 0 |
| Full CUI compiler suite (`cycle2-compiler-regression`) | 204 | 204 | 0 | 0 |
| Full Boards app suite (`cycle2-boards-regression`) | 249 | 249 | 0 | 0 |

Final peak process-group RSS was 1,123,299,328 bytes. Exact commands, SDK hash, source snapshots, case names,
counters, failure messages and log/TRX/receipt hashes are in the adjacent `.evidence.json`. All 23 attempts
remain under `/workspace/scratch/9to1-overnight-20261030/`; no failed evidence was deleted.

The initial compiler RED executed 22 cases with four expected unsupported-route failures. Intermediate
fixture compile errors and malformed markup remain recorded. Review then found that bare
`HeadlessUnitTestSession.Dispatch(async () => ...)` selected `Func<Task>` as a synchronous generic result:
one await joined only the outer Task. The initial event-runtime run and interaction red2/red3 passes are
explicitly **invalid behavior evidence**, preserved as compilation/diagnostic history only. Corrected
event, input-submit and floating fixtures join the full async body and cleanup. The first genuinely awaited
interaction run exposed six retired-command failures plus route-collision/fixture defects; the first
awaited input/floating run exposed 12 failures. Those were repaired and rechecked without removing cases.
Visibility/disabled fixtures now update or assert the real CUI-owned state; unsupported submit modifiers
are compared against an otherwise identical plain native editor rather than an invented text expectation.

All results are **headless development evidence / ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE**. The native PC
application, ARM installation/execution and full independent acceptance were not run. Desktop status was
refreshed and remains `desktop_offline`, with its last native observation at 29 October 23:56:39 UTC.
Mandatory private ROOT-LIVE recovery again returned BLOCKED/INFRASTRUCTURE because
`/workspace/shared/9to1-context/development-workflow-state.json` is absent. No replacement ledger,
heavy/ARM allocation, package, model process or external message was created.

Next: use these authored event routes for the pinned BF-37 flow. Keep pointer/focus ownership separate,
publish transient state without rebuilding the popup anchor, preserve chosen destination through the type
stage and verify actual geometry/history/cleanup/isolation. Unsupported Style overlays, Hover/OnceHover,
external targets, propagation and Event context remain explicit CUI gaps.

## Cycle 3 flow and source ownership

Starting clean refreshed main a09091a6, cycle 3 owns Boards insertion menu, transient preview state,
overlay and focused tests. The flow above remains the required behaviour. A live source review found
the existing inline menu never enters its destination stage: Fill reads list.Tag while only the trigger
and panel receive the context. First reproduce that with the actual mounted plus popup, then restore
the destination route and author its pointer/focus actions and overlay in CUI.

Keep rendered row identity separate from canonical insertion identity for projected list rows. Choosing
a destination pins the preview before retiring destination controls; capture the commit target before
closing the popup. Reopen explicitly resets/refills even when search is already empty. Keyboard traversal
must reach nested authored rows. Popup close/detach, invalid targets, page/session/View changes and
disposal revoke the owning generation. Preview publication must not rebuild the popup anchor or change
document/history/geometry. The existing type factory remains the actual insertion path; retain and repair
any destination-fidelity failures found by the mounted journey rather than hiding them behind VM-only tests.

## Cycle 3 result — visible preview and destination fidelity

The actual inline plus now opens the authored Above/Below/Replace destinations. CUI pointer and focus
actions publish transient session/page/row/menu-generation state; the zero-footprint CUI overlay shows
the actual top/bottom edge or red replacement outline/tint. Choosing a destination pins it through the
type stage. Change, Escape, dismissal, commit, row loss/detach, projection replacement, page/View changes
and disposal retire the appropriate owner. Pointer and focus channels are independent; retired stages
and old type buttons cannot commit a new destination. Two live Boards remain isolated.

The floating frame is now authored in CUI. Initial headless captures exposed a native-background/theme
mismatch despite passing behavioural tests; the corrected frame uses the same theme resources as its
rows. The existing type list and search remain hosted inside it. These are component rendering checks,
not a claim that the installed native application was updated.

Enabling the real destination stage exposed unsafe existing insertion behavior, repaired in this same
milestone so choosing a preview has a truthful result:

- Replacement prepares and validates the new block before one canonical, revision-fenced mutation.
  Missing styles, unsupported operations and cancelled/unreadable/invalid image choices preserve the
  original. Successful replacement is one undo step. Relative style Above inserts instead of formatting
  the selected block. The existing block factory/import mapping is reused.
- Projected list-item insertion splits the canonical list only where needed, retaining item identities,
  formatting and order; whole-list insertion uses the complete parent. Its content stamp covers all rows
  before asynchronous preparation and under both mutation gates, protecting later-item draft edits.
  Free-layout rendering now groups one canonical parent per preview/handle, so adjacent distinct lists
  cannot share a misleading target outline.
- Image bytes are decoded before mutation, with natural display sizing retained even for a valid
  9001-pixel source. Leaving and returning to a page, View mode, disposal or a changed target invalidates
  pending insertion. Whole positioned-block replacement preserves its geometry.

## Cycle 3 validation and retained failures

Fresh full Boards Release suite: **302 executed, 302 passed, zero failures/skips**, including 53 new
preview and persisted-insertion cases. SDK **10.0.401**, serial bounded ordinary Layer1 runner, source
unchanged throughout the final run, no guard violation, peak process-group RSS **908,763,136 bytes**.
The final run compiled the affected application and all nine embedded CUI documents. No shared CUI
runtime/compiler source changed in this cycle; cycle 2's separately pinned evidence remains historical.

Actual mounted raw pointer and keyboard input checks cover destination selection, edge geometry,
noninteractive overlays, unchanged content/status/undo/row/editor bounds and no rebuild on preview,
type-stage retention, stale callbacks, cleanup, two-Board isolation and genuinely disabled unsupported
rows. Real contract-backed destination → type → commit → save/reopen journeys verify Above/Below/Replace
placement and exact single undo. First/middle/last list boundaries retain IDs, item formatting, order and
history. Additional checks cover whole lists, adjacent free-layout lists, cancelled/missing/corrupt/large
image files, page-away-and-back refusal, pending later-item drafts and positioned-block geometry.

All **25 attempts** remain recorded in
[cycle 3 evidence](boards-insertion-preview-cycle3-2026-10-30.evidence.json), including source/SDK pins,
commands, case names, counters, failure messages, compiler diagnostics and log/TRX/receipt hashes.
Original raw artifacts remain in `/workspace/scratch/9to1-overnight-20261030/`.

- `cycle3-menu-red` reproduced the missing destination stage with the mounted plus.
- `cycle3-preview-and-replacement-red`: 5 passed, 8 failed; retained the destructive refusal and relative
  style failures before repair. Intermediate compile/fixture errors also remain recorded.
- `cycle3-preview-expanded`: 17 checks passed, but visual review failed popup contrast. Original and
  repaired captures are retained separately.
- `cycle3-persisted-journeys2` is **invalid completed-suite evidence**: a fixture nested a synchronous
  call onto its own UI pump and deadlocked. Only its owned SDK process group was terminated after
  diagnosis. The child reported exit 0 on SIGTERM; no passing suite or TRX is claimed. Its two observed
  image failures and termination explanation remain retained.
- `cycle3-persisted-journeys3`: 35 passed, 6 failed. Whole-parent fencing and image size failures were
  repaired. Three journey fixtures were corrected to scroll the actual type row into view before raw
  clicking. The subsequent 41-case run passed in full.
- `cycle3-list-scope-red`: 1 passed, 1 failed; reproduced the multiple-parent preview boundary. Both
  passed after repair, followed by the fresh 302-case full suite.

Final headless component frames (800×650, 96 dpi, same source as final tests):
[Above](evidence/boards-insertion-preview-2026-10-30/Above.png),
[Below](evidence/boards-insertion-preview-2026-10-30/Below.png),
[Replace](evidence/boards-insertion-preview-2026-10-30/Replace.png).
All three were visually inspected; the artifact hashes and rendering context are in the evidence JSON.

## Remaining scope and next action after cycle 3

BF-37 is **checked partial**, not accepted or complete across all block capabilities. Relative
database/Tabber/freeform/attachment destinations are visibly disabled with reasons; those existing
operations still use the toolbar path. Above/Below on positioned absolute blocks and replacement of an
interior item in a positioned list are refused explicitly; whole positioned replacement is supported.
These are implementation gaps, not owner waivers. Continue one dependency-ready remaining insertion
capability after pinning its actual transaction/placement/asset ownership flow and reproducing its
current behavior. Do not replace unrelated data to make an unsupported choice appear to succeed.

Lossless Design mappings beyond paragraph/heading, legacy toolbar conversion, remaining Boards/Files
defects and BF-38/root-wording/orphan-tab-group decisions remain open. Browse WIP stays preserved and
excluded from packaging under the handoff. Full independent acceptance remains PAUSED.

Evidence is **headless development / ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE**. Native PC and ARM
installation/execution remain NOT RUN. Desktop status refreshed again at 04:08 UTC: `desktop_offline`,
last native observation 29 October 23:56:39 UTC. Private ROOT-LIVE recovery remains
BLOCKED/INFRASTRUCTURE because `/workspace/shared/9to1-context/development-workflow-state.json` is
absent. No substitute ledger, heavy/ARM allocation, model launch or native reload was made. Package
remains held; ARM remains PC-owned. Usage remains UNKNOWN, with the authoritative ≤5% stop rule active.

The nominal 04:10 trigger arrived while cycle 3 owned ACTIVE source and was coalesced into this cycle's
report/checkpoint. It did not start a second writer or milestone. Continue from the newest published Goal
checkpoint after checking remote main, dirty work and current ownership.
