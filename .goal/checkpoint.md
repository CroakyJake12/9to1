# Checkpoint 5 · 30 October 2026, 02:15 UTC

Status: CHECKPOINT_READY. Cycle 2 completed the shared CUI prerequisite for BF-37 and the lifecycle
repairs exposed by authentic regression checks. The visible Boards preview remains NEXT. The next cycle
must reconcile current source/ownership before claiming ACTIVE. This is not product completion or a
usage stop; the Goal remains authorised through the end of 31 October UTC.

## Source and results

- Canonical checkout: /workspace/9to1, **main**, origin https://github.com/CroakyJake12/9to1.git.
  Cycle 2 started from clean refreshed main 0b758fc6747afef93de77716f74783e2fb131766.
- Source commit **e17b7ee164080481464ceb500bcb0db8862bdb18**, “Add CUI preview events and repair action
  lifecycles”, is pushed and was read back from refs/heads/main. All 13 source/test files match the
  final validation manifest. This checkpoint/report is a subsequent documentation commit; resolve its
  containing main commit and compare the current remote before continuing.
- Final fresh Release checks: focused CUI runtime **234/234**, full CUI compiler **204/204**, full Boards
  app suite **249/249**; **zero failures and zero skipped** in each. No source changed during these runs.
  These results do not establish full app completion, native behaviour or independent acceptance.
- Evidence is **headless development / ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE**. All 23 attempts,
  including failures and withdrawn early async passes, remain recorded. Detailed report:
  cycle-reports/2026-10-30-02.md; pinned flow/results and exact evidence:
  docs/development/boards-insertion-preview-2026-10-30.md and its .evidence.json companion.
- Cycle 1's checked menu/drag/focus, safe paragraph/heading conversion and title theme repairs remain
  preserved. Its source and checkpoint are recoverable in main history, including checkpoint 4 at
  0b758fc6 and cycle-reports/2026-10-30-01.md.

## What this prerequisite now supports

- Named Button PointerEnter/PointerExit actions plus the explicitly documented generic Focused/Blurred
  extension use the retained typed-action pipeline. Click aliases share one family; distinct event
  families coexist, and duplicate routes are rejected, including typed/Definition composition.
- Actual headless pointer/Tab/Space/arrow/Enter tests cover authored Buttons and context-menu rows,
  live parameters, current/query dispatchers, local state, Repeat ownership, retirement and isolation.
  Parameter evaluation cannot reenter and then dispatch a stale command from a retired origin.
- Input SubmitAction uses its authored input and captured frame. Popup NativeClose receipts retain
  later Closed-handler failures. Hidden floating hosts retire correctly; nested content can recover its
  measure path. Reentrant focus replacement cannot resurrect a retired child/cell.
- Bare async headless Dispatch calls in the affected event/input/floating fixtures are now fully joined.
  Earlier apparent async passes are explicitly invalid behaviour evidence, not promoted into results.
- Style overlays, Hover/OnceHover, external targets, propagation and Event context remain unsupported
  CUI gaps. No Boards app source, transient preview model or visible preview overlay changed in cycle 2.

## Next executable milestone

BF-37: implement the pinned plus-menu destination preview using the checked authored event routes.
Hover/focus Above/Below must show the actual insertion edge; Replace must show a red outline and
translucent red tint. Preview must not mutate content, IDs, revision, dirty state, undo or row geometry.

- Existing rows: ToolbarBuilder.AddDestinationStage, CreateInsertButton, Fill,
  DestinationHeader, OpenInsertMenu, InsertMenuContext. Commit still routes through
  BoardsViewModel.InsertKindRelativeToAsync; explicit replacement is distinct from Design conversion.
- Overlay seam: BlockRenderer.WithSelection, beside BlockRowOutline.
- Add typed transient session/page/block/placement/menu-owner-generation state. Publish it without
  rebuilding the popup anchor. Keep pointer and focus ownership independent; an old exit must not clear
  a newer or still-active preview. Different live Boards must remain isolated.
- Retain the destination through type selection; Change returns to destinations. Clear applicable state
  on departure/dismissal/Escape, commit, target loss/detachment and page/session/View-mode transitions.
- Replace the existing absence assertion against the wrong grip-menu document with authentic checks of
  the destination and overlay path. Check real input, geometry, history, insertion position and cleanup.
  Do not claim visible BF-37 implemented from this framework prerequisite.

## Remaining requirements and environment

- Lossless Design mappings beyond paragraph/heading and the legacy toolbar converter remain open.
  OPEN-2026-10-30-BOARDS-LOSSLESS-DESIGN-CONVERSION retains projected list-row identity/splitting and
  incompatible mappings. BF-38 link scheme, Files root wording and orphan tab-group cleanup remain owner
  decisions. Browse 733f35c3 stays preserved and excluded from the next package under the handoff.
- Native PC and ARM install/execution remain NOT RUN; full independent acceptance remains PAUSED.
  Package stays held pending genuinely working menu/native verification; ARM remains PC-owned.
- Cloud SDK /workspace/.tools/dotnet/dotnet **10.0.401**; packages
  /workspace/.tools/nuget/packages; pinned XamlX/Avalonia.DBus submodules retained.
- Private ordinary Layer1 runner: /workspace/scratch/9to1-overnight-20261030/run-layer1.py.
  Serial source/SDK locks, exact source snapshots, bounded memory/disk/time and owned process groups.
  Final labels: cycle2-runtime-regression3, cycle2-compiler-regression, cycle2-boards-regression.
  All had no guard violation and unchanged source; maximum final group RSS **1,123,299,328 bytes**.
- Mandatory ROOT-LIVE recovery was refreshed and again returned **BLOCKED/INFRASTRUCTURE** because
  /workspace/shared/9to1-context/development-workflow-state.json is absent. Ordinary source checks are
  separate, as the handoff permits; no ledger, heavy/ARM allocation or coordinator was fabricated.
- Desktop status was refreshed: **desktop_offline**, last native observation 29 October 23:56:39 UTC.
  No PC process, model, profile or installed package was touched. Refresh availability next cycle;
  historical access is not current custody. Read-only Sol review made no source edits or SDK launches.

## Continuation

The existing hourly continuation remains enabled through **2026-11-01 00:00 UTC**. Cycle 2 executed the
01:10 occurrence. The 02:10 nominal trigger arrived while cycle 2 was ACTIVE and was coalesced into its
checkpoint/report; it did not admit another source writer or begin another milestone. Next nominal
trigger is 03:10 UTC; the schedule is a trigger cadence, not guaranteed compute or delivery.

Authoritative remaining usage is **UNKNOWN**; no allowance API was available. At an observed **≤5%**,
finish the safe unit, commit/push/read back main, write the precise handoff and pause this task. Never
infer usage from context length, rate limits or forecasts. No email or external messages are authorised.
