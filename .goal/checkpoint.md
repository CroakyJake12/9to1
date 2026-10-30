# Checkpoint 4 · 30 October 2026, 00:56 UTC

Status: CHECKPOINT_READY. The foreground menu/selection/drag/theme milestone is compiled and validated;
the next scheduled cycle may claim ACTIVE after reconciling live source and ownership. The Goal remains
active through tomorrow; this is not a usage stop or product-completion claim.

## Source and results

- Canonical checkout: /workspace/9to1, **main**, origin https://github.com/CroakyJake12/9to1.git.
  Fetched starting main 7199daab. Original work ref/history preserved; no implementation branch,
  worktree, reset, force-push or branch cleanup was used.
- 81400baec8add3db82e6b0c2dd8c20d681df5b83: menu lifetime, keyboard/focus, drag cleanup. Pushed and
  verified remotely. 25aee6160a36e4f9fd550c3b906fa0056405c6ec: safe Design conversion. Pushed and
  verified remotely. Title theme/host-fixture repair and this checkpoint are in the containing commit;
  resolve git rev-parse HEAD and verify origin/main before continuing.
- Full fresh Boards app suite: **249 passed / 249 executed, 0 failed, 0 skipped**. Baseline reproduced
  **215 passed / 223 executed, 8 failed**. All eight original test failures are resolved.
- Evidence remains **ordinary Layer1 PARTIAL / NOT REUSABLE**. Skia headless component capture exists;
  no native PC application or ARM install/run was observed. Full independent acceptance stays paused.
- Detailed report: cycle-reports/2026-10-30-01.md; flow/results and exact evidence hashes:
  docs/development/boards-menu-repair-2026-10-30.md and its .evidence.json companion.

## Next executable milestone

BF-37: the plus-menu destination preview. Hover/focus Above/Below must show the actual insertion edge;
Replace must show a red outline and translucent red tint. No preview may mutate content/history or move
row geometry. Retain chosen destination through the type stage and clear safely on dismissal/change,
commit, detachment, page/session/View-mode change. Delayed exit from an old row must not clear a newer
preview; different live Boards must remain isolated.

- Existing rows: ToolbarBuilder.AddDestinationStage, plus CreateInsertButton, Fill,
  DestinationHeader, OpenInsertMenu and InsertMenuContext. Actual insertion remains
  BoardsViewModel.InsertKindRelativeToAsync; don't conflate explicit replacement with Design conversion.
- Zero-footprint overlay host: BlockRenderer.WithSelection beside BlockRowOutline.
- No BlockInsertionPreview state exists yet. Add a typed transient model carrying session/page/block,
  placement and menu-owner generation. Publish changes without rebuilding/destroying the popup anchor.
- CUI's CuiActionEventContract recognises but rejects PointerEnter/PointerExit/Hover. Only Clicked and
  OnceClick execute; it also currently rejects multiple owned events/direct + event routes. Implement
  required generic runtime support and authored destination/preview presentation, with authentic tests.
  Do not add inert hover markup or an app-specific presentation/interaction bypass.
- BoardsAppTests currently asserts missing preview text in the wrong grip-menu document. Replace the
  absence diagnostic with checks of the actual authored destination/preview documents and runtime path.

## Remaining requirements

- BF-35 menu open/dismissal/keyboard, duplicate/move/delete routing and drag separation have focused
  executable coverage. Design paragraph ↔ heading preserves the same canonical block and all payloads,
  one undo/redo, persistence and the original revision ticket. Unsupported destinations stay discoverable
  but disabled with content-preservation reasons. Do not call all Design conversions implemented.
- OPEN-2026-10-30-BOARDS-LOSSLESS-DESIGN-CONVERSION tracks projected list-row identity/splitting and
  incompatible object mappings. The legacy toolbar converter still needs review independently.
- BF-38 link scheme, Files root wording and orphan tab-group cleanup remain owner decisions. ARM package
  stays held pending genuine menu/native verification and PC custody. Browse 733f35c3 remains preserved
  and excluded from the next package under the handoff; do not claim its independent/native check passed.

## Runtime and guard recovery

- SDK /workspace/.tools/dotnet/dotnet **10.0.401**, compatible global.json roll-forward; packages under
  /workspace/.tools/nuget/packages. XamlX/Avalonia.DBus exact pinned submodules are materialised.
- Private ordinary Layer1 runner: /workspace/scratch/9to1-overnight-20261030/run-layer1.py.
  It takes a fresh label and exact argv; locks one source/SDK lane, snapshots HEAD/diff/untracked files,
  bounds memory/disk/time, retains logs/TRX/receipts, and signals only its owned process group.
  Final full run: boards-full-green; peak group RSS 1,501,343,744 bytes, no guard violation, source unchanged.
- The existing private cloud ROOT-LIVE projection is absent; required recovery returned
  INFRASTRUCTURE/BLOCKED. No replacement ledger or heavy/ARM allocation was fabricated. The overnight
  handoff explicitly distinguishes ordinary source checks from that separate lane. Preserve that boundary.
- Desktop bridge was offline at setup, last observation 29 October 23:56:39 UTC with
  native_contract_changed. No PC process, profile, model or installed package was touched. Refresh actual
  availability; historical access is not current custody.
- Read-only Sol review helped diagnose source/fixture problems; it made no edits and ran no SDK tasks.

## Continuation

One hourly continuation is enabled through 2026-11-01 00:00 UTC, first configured trigger 01:10 UTC on
30 October. No scheduled execution has yet been observed. ACTIVE forbids another writer; CHECKPOINT_READY
permits the next cycle to reconcile and claim. Use the newest files, not the schedule's fallback prose.

Authoritative remaining usage is **UNKNOWN**; no allowance API was available. At an actual reading ≤5%,
finish the safe unit, commit/push/verify main, save the precise handoff and pause the task. Never infer the
threshold from context length or forecasts. No emails or other external communications are authorised.
