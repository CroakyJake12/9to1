# Local continuation — 25 October 2026

## Start here

Jacob explicitly requested the transition to local development because his cloud
usage is low, and asked that the receiving agent inherit both the context and the
working approach. Cloud product authoring is now on HOLD. All six actual cloud
workers acknowledged the hold; preserve their checkpoints. This supersedes the
earlier 25 October cloud-resume instruction. Do not automatically restart cloud
teams or the old OpenCode cadence.

The product checkpoint before this handoff document is
`d40e5ae93529dd4c8b70e88fdfafa3c440cf4e6c`, on
`codex/resume-implementation-20261025`. The final documentation commit is recorded
in the separate transfer receipt, not self-referenced here. Read that exact commit,
not an old Source 78 checkout or the older main branch. The source is NOT accepted.
`main` remains `9505fcb85`; latest combined basic checks have not all passed.

Read in this order:

1. `AGENTS.md`, `docs/development/workflow.md`, this document and its JSON companion.
2. The current canonical specification and later owner decisions. The amended
   specification SHA-256 is `e1779d226848da492e926ab70e8d4722ad444476cfc91312bf5c2c067654fe81`;
   the older Source 78 handoff's original hash and estimates are historical.
3. `docs/specification/owner-references/2026-10-25-boards-ui-review/`, especially
   `owner-control-and-toolbar-clarification.md`, and the adopted 25 October UI
   report. Send-button stacking is an intended contextual shelf, not a bug.
4. Existing local `development-workflow-state.json`, native session bindings and
   the private transfer packet's source identities, failures and next actions.
5. `docs/development/opencode-teams.md` and the previous bootstrap grant for
   continuity. Do not treat historical session IDs or permission proofs as live.

## Work like the current coordinator

Be an autonomous, candid implementation partner. Jacob values visible usable
features and prompt fixes, frequent real screenshots, and concise plain-language
updates. He dislikes repeated permission questions, empty planning cycles and
half-hour reports containing only acceptance bookkeeping. Make routine authorized
choices, explain meaningful uncertainty and keep unrelated work moving. Never
claim a running worker, test, connection, speed gain, installer or delivery without
actual evidence. Do not promise a delivery time before its real gates are possible.

Finish **Home/Haven, Spaces, Build and Boards together first**. Boards is needed for
revision; Home, Spaces and Build should support continued local development.
Complete remaining canonical apps after this group reaches actual implementation
completion. Useful existing standalone apps are current implementations to finish
and embed in Home, not legacy replacements. Preserve one shared implementation,
canonical data, permissions, persistence and engines across standalone/embedded use.

Full independent acceptance remains PAUSED until all apps are implemented or the
owner explicitly resumes it. Continue affected compilation and genuine focused
basic smoke checks. Preserve all seven eventual acceptance criteria. Do not turn
a test count into an implementation percentage or call a partial app 100%.
Use supported Sol 6.1 and Luna workers with disjoint files and one heavy build at
a time; reuse existing local teams. Mercury is revoked. No paid fallback.
Thunder remains a separate protected workstream: do not send model requests,
restart gateways or interrupt its desktop worker without current scoped custody.

Use small coherent commits, inspect real APIs before coding, and integrate
compatible completed changes promptly. Compiler errors are not successful tests.
Fix genuine fixture defects while retaining their intended assertions. Do not
rebuild unchanged components merely for reporting. Keep exact failed logs and
candidate identities. The local coordinator may assign scopes and integrate after
its real source/custody ACK; it need not wait for a stopped cloud coordinator.

## Local custody and model continuity

The existing receiving chat is **Prepare local 9to1 teams**, thread
`01a16df8-1971-7d63-be3a-b8548a888274`, historically at
`/home/jacob/Documents/Codex/2026-10-24/prepare-the-local-9to1-teams-from`.
At 18:20 UTC today its actual app status was `notLoaded`; its latest retained
24 October turn ended in a usage-limit error. This is not a current worker ACK.
The laptop coordinator bridge was online but advertised only health, status and
read_evidence, with worker creation/supervision false. Cloud has no callable
thread-start or thread-message operation at this checkpoint. Source publication
and a private packet do not by themselves mean the local agent has begun work.

Preserve the nine real historical OpenCode sessions, auth/variant/capacity/receipt
repairs and original worktrees. Observe installed OpenCode APIs and actual model
availability again before activating. Earlier native models were openai/gpt-6.1-sol,
openai/gpt-6-luna and openai/gpt-6-astra through subscription OAuth; those old
observations are not fresh entitlement. Existing serial capacity reflected only
2.5GB RAM headroom and nearly full swap. Recheck resources; do not launch 50 workers
or extra supervisors from a historical suggestion. Keep one cadence invoker,
Sol 300s and Astra 1800s with its documented cooperative 300s review budget.

Fetch the final receipt's exact source into a **new isolated worktree** of the
existing clone. Verify commit/tree and preserve all dirty local branches. Never
blindly pull/reset the user's active checkout. Verify the two pinned CUI submodule
inputs (XamlX `009d4815470cf4bf71d1adbb633a5d81dcb2bb52`, Avalonia.DBus
`864a05282841bf04006890f04d11d60d1a046aa9`); the private packet retains all 602 exact
input files and modes. No credentials are part of the source handoff. Reuse the
existing local projection, reconcile the cloud snapshot and establish actual local
paths/build allocation; do not overwrite it with a stale cloud ledger.

## Boards: latest owner intent and implementation checkpoint

The appearance should be rounded, rich and calm, with warmer purple by default
and real user accent overrides. Glow has visible soft gradients, brighter hover
feedback and borderless ordinary controls. Professional stays distinct and
glow-free. Do not make every element an accented pill or add arbitrary borders.

The latest integrated source includes the compact one-line header, Boards icon
before File, small undo/redo, Editing and Vertical Stacking switches, three-dot
menu, board name with saved state in sidebar, selected page marker, larger section
labels, same-width toolbar/page, shadow-backed overflow arrows, compact title
inset, hidden idle block controls in the left margin, and bold purple checkboxes.
There is no redundant breadcrumb or Appearance button. Controls remain accessible.

Recent owner refinements implemented in source but still needing a fresh render:

- More visible continuous Glow: latest `5e6fd918` integrated as `d40e5ae9`.
  Workspace/shell share a ramp slightly darker than the page; transparent sidebar
  should prevent a seam. Explicit colours, Professional and contrast stay intact.
- Font family/size accept both dropdown choice and typed input; Enter/focus commit,
  stale selected-block protection, bounded valid values and existing history.
  Test actual selection preservation, undo and invalid input, not only properties.
- Softer coloured section/page icons, brighter selected-page accent/dropdown
  backgrounds, larger clean list/alignment icons; latest filled geometry no longer
  adds a heavy stroke to every closed shape. Inspect the rendered small glyphs.
- `+ Add` is bold, 168px wide (about 3/5 sidebar), with default white accent ink.
  Editing/layout containers have reduced padding; board identity is grouped with
  saved status and should align with icon/chevron. Verify actual bounds visually.
- Default table border uses the current accent and correct inner corner radius.
  Preserve explicit user colours. The old sample blue came from a fixture override
  removed in the latest source. Math's old yellow was also an explicit fixture
  highlight, not a LaTeX convention; full LaTeX support has not been established.
- Ordinary text and title glyphs should align. Toolbar-to-page gap is 6px and
  document content margin is 76px horizontally / 10px vertically.

The section picker has a searchable filled icon family, colour choices, genuine
Home-model-routed suggestions, up to three ephemeral previews and explicit Apply.
Only the section name is sent; fallback, tools and persistent memory are disabled.
Typed bounded geometry is persisted only when selected; cancellation/generation
epochs prevent late apply after navigation/disposal. Real provider invocation has
NOT been tested. The actual built-in picker capture path is present but not yet run
successfully on the newest source. Do not replace generation with static matching.

The owner explicitly requests a **test copy after every Boards change discussed
in this thread**, not merely after the newest ten tweaks. No test package has been
built or delivered. Broader app completion and full acceptance are separate.

## Exact evidence and next checks

Private packet includes command, source, TRX, raw logs and screenshots; no claim is
portable merely because a status label says PASS. All fresh cloud SDK evidence is
partial dependency coverage, NOT REUSABLE and not native or release acceptance.

| Check | Actual result | Next action |
|---|---|---|
| Dev features 1, `74d66af4` | 109/109 PASS, 17.696s | Preserve; new TRX work is separate WIP. |
| Runtime material 2, `26c63cbe` | 52/52 PASS | Latest stronger Glow changed inputs; run affected theme/material checks. |
| Runtime overflow 2, `209e4439` | 42/42 PASS | Preserve exact scope; real arrows, wheel/keyboard/extent. |
| Boards 38, `68aafb5d` | 113/114 | Old binding assertion repaired subsequently. Image is dated, not latest. |
| Boards 40, `1abd37a0` | Product compiled; test compile failed | ValueTask pump, binding enum namespace and shadowed variable repaired in `05ed7c17`/`d08df737`; rerun current Boards tests and populated/picker captures. |
| Boards contract 3, `41af495b` | 33/34 | Table bounds fixture repaired in `501de0e9`/`edc4ae4e`; geometry/placement passed. |
| Desktop features 4, `c17ba093` | Compile failed on formatting enum vs bool | Fixed `2560246c`/`641a3b7f`; run actual combined desktop feature checks. |
| Spaces graph 5, `3ffc179d` | 113 PASS, 3 FAIL, 0 skipped | Two cloud-fixture provider refusals must preserve real local-only context policy. Invalid original UTF-16 is replaced during typed serialization before normalization; validate before that conversion, keep the failing assertion. |
| ARM branch `46cf9fb7` | 128 ARM Python + 7 manifest tests PASS | Rust compilation, native/ARM/GNOME and installer execution NOT RUN. |

Old shell_flows3 had 26/35; same-operation receipt, Library metadata/search and
Files lifetime/cancellation fixes were subsequently integrated, but the current
combined smoke still needs execution. Do not erase that unresolved status.

Run with the repository's actual projects, SDK and CUI templates. Cloud used SDK
10.0.401, single MSBuild node, normal dependencies and shared compilation disabled.
Re-establish local resource/process admission instead of copying absolute cloud
runner pins. The packet contains precise selectors and commands. Keep full native
install/browser/chat/persistence/restart/normal-close gates queued for later.

## Preserved unfinished implementation

- **Build:** last coherent worker `0b820d01`, integrated stage authoring passed the
  focused suite. Untracked `DeveloperTrxTestResults.cs` is separately preserved
  as WIP, not integrated or tested. It needs strict outcome-string validation
  (Enum.TryParse comma combinations must not become Passed), cancellation for an
  empty filter, actual CUI/read fences, last-valid-report handling and tests.
  LSP/DAP, broad toolchain/pipeline and Test Explorer gaps remain; do not call92% a
  measured feature fraction or100% implementation.
- **Spaces:** configured source selection, captured-context activation and explicit
  normalization are integrated. The Task-question slice is approved but NOT begun:
  same canonical Task snapshot question/received/applied records, original response
  cleanup boundary, exact owner continuation, real CUI answer flow. Never alias an
  answer to Steer/Queue or mark Applied before the next request consumes it. Cold
  restart continuation requires a real third journal boundary, not copied status.
  Exact path grant/findings are in the worker checkpoint.
- **Home:** create/read original-task lifetimes, cancellation and recovery changes
  are integrated, along with package-removal policy work. Actual Linux root and
  normal startup/close/runtime checks remain. Keep retained evidence and genuine
  Home authority; do not invent a READ approval dialog or suppress real I/O errors.
- **ARM/native:** branch `codex/arm-test-delivery-20261025`, `46cf9fb7`, contains
  the remaining five exports and exact two Rnote source patches. It is preserved
  separately, not accepted or compiled. Root integrated only the earlier restore
  `78d37282`/`1abd37a0`. Review and reconcile before cherry-picking the successor.

## Critical ARM blocker and delivery truth

The actual Cargo native engine is Rnote `8cac558` (0.14.2), but
`CanvasRnoteDocument.DonorRevision` stamps and accepts `1a728d6` (0.15.0) in persisted
engine data and split provenance. This is a real false producer-label/compatibility
problem, not just documentation. Preserve old files and baselines; determine the
actual selected stable engine and support an explicit safe migration/refusal.
Never relabel old bytes or bump a version probe to pretend native capability.

The current source-only ARM successor supplies all28 managed symbol names, ABI3,
bounded FFI and actual keyed/split engine operations; this has NOT compiled.
Native/source/licence/payload checks and135 Python passes do not prove a runnable
library or .deb. Retain GPL source/patches/licences and exact native build inputs.

Cloud is x86_64, has insufficient space for the existing6GiB native preflight and
no established ARM execution path. The public GitHub repository's existing ARM
workflow intentionally refuses private package publication there. Do not remove
that guard, expose private packages or use the divergent old emergency workflow.
Existing owner-only OneDrive transport previously round-tripped43MB; a690MB ARM
installer transfer remains untested. Verify actual build, package bytes/SHA, private
custody and laptop receipt. Label GNOME/native untested honestly. Keep old startup
baseline alongside a new test build. No morning or immediate delivery promise.

## Reporting and resumption

Half-hour reports use 👀 Progress (rough Implemented / frozen Accepted), then
9️⃣ 9to1 Progress with concrete per-app changes, then quantitative 🌩 Thunder.
Last actual send:25October18:12UTC, message `1a17442c9462d41b`. Cloud reporting stops
with this handoff; only one local reporter may resume via supported authenticated
email. Preserve recipient/history privately and avoid duplicate catch-up messages.
Latest rough source estimates: Home85, Spaces90, Build92, Boards88, Automations55,
Write63, Present64, Canvas67, Data58, Files63, AI Studio25. Frozen Accepted:
15,15,10,15,0,20,15,25,15,15,0 respectively. These are judgments, not scope metrics.
Other canonical apps remain required even when absent from this short tracker.

No new Thunder inference, GPU comparison or measured speed gain occurred during
this cloud interval. Keep genuine earlier benchmark receipts separate. Usage is
not readable here; Jacob reported it low and explicitly requested this transition.

First local action: verify exact source and private packet, inspect existing local
work/sessions, record a real custody ACK and exclusive scopes, then fix the known
basic failures and capture current Boards. Continue Home/Spaces/Build in parallel.
Keep this file and machine state current through compaction; do not depend on chat
memory. Before another interruption checkpoint source, evidence, decisions, WIP
and executable next steps. Do not restart completed work without new evidence.
