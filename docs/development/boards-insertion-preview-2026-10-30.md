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
