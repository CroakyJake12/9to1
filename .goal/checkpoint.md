# Checkpoint 7 · 30 October 2026, 05:58 UTC

Status: CHECKPOINT_READY. Cycle 4 completed the dependency-ready BF-37 relative Attachment milestone,
including real embedded-local picker wiring and payload persistence repairs exposed by its checks.
BF-37 remains CHECKED PARTIAL. This releases source for the next eligible cycle; it is not an app
completion, acceptance or usage-stop claim. Reconcile live remote/source/ownership before claiming ACTIVE.

## Source and validation

- Existing checkout /workspace/9to1, canonical main, origin https://github.com/CroakyJake12/9to1.git.
  Started clean at refreshed main 56d97238ae7d205c809023d370d97bb3c94d2587. Remote still matched before push.
- Source commit **71296260e95c830ae9f6948a06be8832f10b1a85**, “Enable atomic Boards attachment destinations
  and preserve payloads”, was pushed and read back from refs/heads/main at 05:58 UTC. All 11 changed
  source/test hashes match the final-run manifest and the committed blobs. This checkpoint/report is a
  subsequent documentation commit; use its containing main commit for continuation.
- Fresh final Release checks: **337/337 full Boards app tests**, **113/113 full Boards contract tests**,
  zero failures/skips. Standalone Boards build passed; one existing WebRtcVadSharp AnyCPU→x64 native DLL
  warning remains. Affected producers and all nine embedded CUI sources compiled. No source changes
  during these three final runs; no guard violation. Peak owned process-group RSS respectively
  1,192,693,760 / 432,672,768 / 1,311,059,968 bytes.
- **Headless development / ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE** only. Native PC/OS dialog and ARM
  execution/install remain NOT RUN, full independent acceptance PAUSED, package HELD and ARM PC-owned.
  Test totals are not accepted progress or app-completion evidence.
- Pinned flow/results: docs/development/boards-attachment-insertion-2026-10-30.md.
  All 21 attempts, failures, commands, source snapshots, case names and artifact hashes:
  docs/development/boards-attachment-insertion-cycle4-2026-10-30.evidence.json.
  Two actual 1180×760 headless captures are committed under
  docs/development/evidence/boards-attachment-insertion-2026-10-30/.
  Detailed report: cycle-reports/2026-10-30-04.md.

## Checked behavior and source seams

- Actual inline plus → Above/Below/Replace → Attachment now uses the existing transient preview/type
  stages and prepares the selected asset before one canonical edit. Picker cancellation, failure and
  stale origin preserve the target. One Undo/Redo restores exact content/IDs. Flow-list row/whole-list
  boundaries retain other items/formatting; whole positioned replacement retains geometry.
- BoardsFilePicker.cs is the shared OS adapter used by Program and OpenLocalDocumentAsync. Embedded
  local Boards now receive PickFileAsync. Late choices after detach/reattach or retired owner lifetime
  are rejected, returned storage handles disposed. External-save sources still explicitly refuse local
  attachment import even if a host provides a picker; paths are not fabricated Files identities.
- ContractSessionAdapter.Insertion holds its merge gate across import and atomic mutation, sharing the
  Save As serialization boundary. RichBoardSession imports under its own gate from one source stream,
  bounds actual copied bytes, hashes those bytes and publishes an immutable blob through an owned temp.
  Empty embedded files resolve. Late refusal retains completed blobs needed by history/shared references.
- Cumulative/JSON-escaped embedded size chooses a sidecar when needed, using the same prepared bytes.
  MutateIfAsync(requireReadableDocument) checks the actual persisted indented envelope inside rollback
  before history commit, including metadata duplicated by list splicing. Corrupt existing blobs are
  refused and preserved; failed-import cleanup removes only the owned temporary file.
- Ordinary Save As now copies existing sidecars. JsonFileHavenBoardStore validates destination identity
  under its store gate before publishing blobs, preserving another Board's file/directory on refusal.
- Real mounted embedded-host tests use raw pointer plus keyboard search and the production picker
  callback. Only the OS dialog result is substituted; content/save/reopen are real. Final screenshots
  show Replace/type selection and the attachment name/size with an explicitly synchronized host theme.

## Retained failures and limits

Reproduced and repaired: 15 initial relative-attachment refusals; empty-file resolution, ordinary Save
As sidecar loss and oversized-file exception; missing embedded picker callback; cumulative embedded size;
rejected Save As side effects; list-splice read-limit overflow; and JSON-escaping size overflow. Retained
intermediate fixture/compiler/missing-assets failures are described in report 04. The earlier 337-pass
run predates the final escaping fix; cycle4-boards-final is the fresh final result. No invalid terminated
suite is claimed as passing in cycle 4. No failures remain in the final executed suites.

The legacy toolbar attachment path still has its prior two-step create/attach edit; only the relative
route is claimed atomic. Native picker/PC/ARM behavior remains unobserved. Copy-helper cancellation and
actual filesystem-temp cleanup are separate evidence, not native-dialog acceptance.

## Next executable milestone

Inspect and pin the actual Tabber parent/pane ownership flow, starting at BoardsViewModel.AddTabberAsync,
ContractSessionAdapter.AddTabberAsync and the canonical Tabber operations. If dependency-ready, complete
one relative Tabber capability with prepare-before-commit, exact destination/preview, cancellation,
persisted nested identity and single-undo checks. Do not enable a row that cannot honor its destination.

- Relative database/Tabber/freeform remain disabled with reasons. Positioned Above/Below and interior
  positioned-list replacement remain unsupported; whole positioned replacement works in headless checks.
- Lossless Design mappings beyond paragraph/heading, legacy toolbar conversion and remaining Boards/Files
  defects remain open. BF-38 link scheme, Files root wording and orphan tab-group cleanup retain their
  owner-decision status. Preserve Browse 733f35c3 and its separate RED evidence/package exclusion.

## Environment and continuation

Managed cloud environment was recovered and source accessed, not inferred from metadata. SDK 10.0.401
at /workspace/.tools/dotnet/dotnet; packages /workspace/.tools/nuget/packages. Serial bounded private
runner /workspace/scratch/9to1-overnight-20261030/run-layer1.py retained exact source/commands, process
ownership, memory/disk floors and time/RSS limits. Final labels: cycle4-boards-final, cycle4-contract-final,
cycle4-standalone-final. No SDK remains active at publication.

Mandatory ROOT-LIVE recovery remains BLOCKED/INFRASTRUCTURE: missing private projection
/workspace/shared/9to1-context/development-workflow-state.json. Ordinary Layer1 followed the separately
permitted lane. Fresh desktop read returned desktop_offline; last native observation was
2026-10-29T23:56:39Z. No native/model/package launch, new coordinator/ledger, email or external message.
Read-only Sol review found the size/destination defects and confirmed no remaining concrete blocker
following repair; it made no source edits or SDK launches.

The existing hourly continuation remains enabled through **2026-11-01T00:00:00Z**. This was the nominal
05:10 occurrence (scheduler records 05:13:49); next nominal occurrence 06:10 UTC. No second writer was
admitted. Schedule metadata is not proof that a future cycle will execute.
Authoritative usage remains **UNKNOWN**. At observed ≤5%, safely wrap, commit/push/read back main, write
the precise handoff and pause without new work. Never infer usage from context length or forecasts.
