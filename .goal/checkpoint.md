# Checkpoint 6 · 30 October 2026, 04:13 UTC

Status: CHECKPOINT_READY. Cycle 3 implemented visible BF-37 insertion/replace preview and the atomic
relative-edit repairs needed for its actual destination/type journey. BF-37 remains CHECKED PARTIAL:
unsupported destinations and native/ARM checks are open. Reconcile live source and ownership before
claiming the next cycle ACTIVE. This is not product completion or a usage stop.

## Source and validation

- Canonical checkout /workspace/9to1, main, origin https://github.com/CroakyJake12/9to1.git.
  Started clean and refreshed at a09091a6d985c1415d7a336d85cf8a2ca01c79a5. Remote still matched before push.
- Source commit **a376fda20fc9b982428599d260072c8d69ffa05b**, “Show Boards insertion previews and make relative
  edits atomic”, was pushed and read back from refs/heads/main. All 17 source/test files match the final
  validation manifest. This checkpoint/report is a subsequent documentation commit; resolve its containing
  main commit and refresh remote source before continuing.
- Fresh full Boards Release suite: **302 executed, 302 passed, zero failures/skips**, including 53 new
  preview/persisted-insertion cases. Affected app and all nine embedded CUI documents compiled.
  No source changed during the final run; no guard violation; peak group RSS **908,763,136 bytes**.
- Evidence: **headless development / ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE**. No app-completion or
  acceptance claim follows from totals. Native PC and ARM installation/execution remain NOT RUN;
  full independent acceptance remains PAUSED. Package held; ARM remains PC-owned.
- Details: cycle-reports/2026-10-30-03.md; pinned flow and results in
  docs/development/boards-insertion-preview-2026-10-30.md; all 25 attempts/case names/failures/source and
  artifact hashes in docs/development/boards-insertion-preview-cycle3-2026-10-30.evidence.json.
  Three final headless frames are committed under docs/development/evidence/boards-insertion-preview-2026-10-30/.

## Checked behavior and source seams

- Inline plus now reaches authored Above/Below/Replace destinations instead of skipping them. Typed
  CUI pointer/focus actions publish transient per-session/page/row/menu-generation preview. Top/bottom
  edges and red replacement outline/tint occupy no document flow space and do not mutate data/history.
- Destination choice remains pinned through the type stage. Change, Escape, dismissal, commit, row loss,
  detach, projection refresh, page/View changes and disposal retire ownership. Pointer and focus remain
  independent; stale stage/type rows cannot commit. Two Boards do not share preview state.
- CUI owns destination, preview and floating frame presentation. Visual review found and repaired the
  initial popup contrast mismatch. Native/installed application behavior has not been observed.
- Relative replacement prepares first, then uses one canonical mutation with revision/origin fences.
  Refused styles/types or cancelled/missing/corrupt image files preserve the original; successful edits
  support one undo. Relative style Above inserts at the chosen position. Images retain natural sizing.
- Projected list insertion preserves retained item identity/formatting, splits at the selected boundary
  and keeps a distinct inserted kind. Whole-list preparation fences all parent rows, protecting later-item
  draft edits during image picking. Free-layout preview groups one canonical list parent, matching the edit.
  Whole positioned replacement preserves geometry; unsupported spatial combinations refuse explicitly.
- Implementation seams: BoardsViewModel.InsertionPreview.cs (transient ownership), InsertMenuContext.cs
  (popup/stages/retained actions), InsertDestinationCui.cui, BlockInsertionPreviewCui.*, InsertMenuSurfaceCui.cui,
  ToolbarBuilder.Fill/AddGroup, BlockRenderer.WithSelection/list grouping, BoardsViewModel.Insertion.cs,
  ContractSessionAdapter.Insertion.cs and MergeDeltas.PrepareInsertion in BoardProjection.cs.
- Real mounted destination→type→commit→save/reopen checks verify placement and exact undo; list tests cover
  first/middle/last and whole-parent boundaries. Raw pointer/keyboard checks cover geometry, cleanup,
  disabled reasons, stale callbacks and isolation. These are ordinary development checks, not acceptance.

## Retained failures and limits

The missing destination route, eight destructive-refusal/style failures, popup contrast failure,
whole-list draft fence, large-image sizing and adjacent-list preview scope failures were reproduced,
repaired and rechecked. Intermediate compile/fixture failures remain retained.

cycle3-persisted-journeys2 deadlocked its fixture by nesting a synchronous call onto the same UI pump.
Only its owned SDK process group was terminated. Child exit 0 after SIGTERM is explicitly NOT a pass;
no completed TRX exists. Subsequent fixtures join bounded asynchronous work correctly. Three initial
popup journey tests clicked outside the scroll viewport; corrected tests scroll the actual row into
view before raw input. No failures remain in the final executed suite; feature gaps below remain open.

## Next executable milestone

Finish one remaining BF-37 relative insertion capability, beginning with the Attachment destination's
actual picker/asset ownership flow if dependency-ready. Inspect current toolbar AttachFileAsync and
Files host seams, pin what the user chooses and what is stored/replaced, reproduce the unsupported
path, then implement an atomic prepare/commit/cancel flow with save/reopen/undo and mounted checks.
Do not enable a row that cannot honor its shown destination or remove the original before preparation.

- Relative database/Tabber/freeform/attachment destinations are currently disabled with accessible reasons;
  their existing toolbar routes remain available. Above/Below on absolute blocks and interior-item
  replacement in positioned lists are refused. These are implementation gaps, not owner waivers.
- Lossless Design mappings beyond paragraph/heading and the legacy toolbar converter remain open.
  BF-38 links, Files root wording and orphan tab-group cleanup retain owner-decision status. Preserve
  Browse 733f35c3 and its separate evidence/package exclusion. Remaining Boards/Files defects follow.

## Environment and continuation

Cloud SDK /workspace/.tools/dotnet/dotnet **10.0.401**, packages /workspace/.tools/nuget/packages.
Private ordinary Layer1 runner /workspace/scratch/9to1-overnight-20261030/run-layer1.py retains serial
source/SDK locks, source snapshots, initial memory/disk admission, bounded RSS/floors/time and owned
process groups. Final label cycle3-boards-regression; no SDK remains active at publication.

Mandatory ROOT-LIVE recovery returned BLOCKED/INFRASTRUCTURE: private projection
/workspace/shared/9to1-context/development-workflow-state.json is absent. Ordinary source checks followed
the separate lane permitted by the handoff. Desktop status refreshed at 04:08 UTC: desktop_offline;
last native observation 29 October 23:56:39 UTC. No native/model/package process was launched, no private
ledger fabricated, no email/external message sent. Read-only Sol review made no edits or SDK launches
and found no remaining concrete commit blocker after its three findings were addressed.

Existing hourly task remains enabled through **2026-11-01 00:00 UTC**. Cycle 3 handled the 03:10 occurrence;
the nominal 04:10 trigger arrived while ACTIVE and was coalesced into this checkpoint/report, without
another writer or milestone. Next nominal trigger 05:10 UTC; cadence is not guaranteed execution.
Authoritative remaining usage is **UNKNOWN**. At observed ≤5%, finish the safe unit, commit/push/verify
main, write the precise handoff and pause the task. Never infer usage from context length or forecasts.
