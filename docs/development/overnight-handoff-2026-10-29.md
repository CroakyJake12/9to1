# Overnight agent handoff — 9to1, 29–30 October 2026

Self-contained. This file IS tracked; the richer running ledger
`.handoff/boards-first-v3/work/STATUS.md` (1,080 lines), `REQUIREMENTS.md` and
`ASTRA-REPORT.md` are **local-only on the PC** (excluded via `.git/info/exclude`) and are NOT in the
clone. Read this first, then ask for specific excerpts if you need deeper history.

## 0. Identity and continuity

- Repo `/home/jacob/Documents/GitHub/9to1`, branch `main`, canonical integration line.
- HEAD at handoff and on `origin/main`: **`8aa8e906`** (verified by `git ls-remote`).
- Commits added on 29 October, all pushed:
  `34bc7888` repaired `Haven.Application.Tests` (would not compile since `3e46d169`; 189 tests had been
  unrunnable for 4 days) · `8a36217d` shared workspace-root rule · `1fa867b3` Files auto-provision,
  adopt-non-empty folder, re-register a moved root · `b5554d6b` Boards visible save + naming ·
  `8e0fa938` Files "9to1 directory missing — Re-locate or create new" · `b02c5946` decision record ·
  `e9ea14b3` reviewer-feedback record + AGENTS pointer · `3fcd302d` "don't ration context" ·
  `d8951794` create keeps its destination selection · `aa0d7b85` **plus-menu destinations + menu icons
  + gutter alignment (VERIFIED)** · `8aa8e906` **WIP grip menu — DOES NOT OPEN YET, deliberately RED**.

## 1. Required reading, in this order

1. `AGENTS.md` — at every startup. Two pinned items in it matter tonight: the reviewer-feedback
   countermeasures (pin the user flow before coding, compile after each edit unit, state the evidence
   level of every claim, never queue owner-drawn UI work behind measurement) and "do not ration
   effort or context".
2. `docs/development/model-performance-feedback-2026-10-29.md` — the reviewer feedback plus the
   owner's "don't worry about context" instruction.
3. `docs/specification/9to1-development-specification-v2.md` — the ONLY product authority, pinned
   `e1779d226848da492e926ab70e8d4722ad444476cfc91312bf5c2c067654fe81`. Read the applicable FULL
   sections for any surface you touch, not a summary.
4. `docs/specification/owner-references/2026-10-27-expanded-spec/` (176 `UX27-` IDs, adoption notes,
   PROVENANCE) · `2026-10-25-boards-ui-review/` · `2026-10-25-ui-report/` ·
   `2026-10-26-live-tab-feedback/` · `2026-10-20-eleven-native-ui-refinements/` ·
   `2026-10-20-haven-addressbar-shared-controls/`.
5. `docs/specification/owner-references/2026-10-28-reported-bugs/9to1-full-reported-bug-list-2026-10-27.md`.
6. **Visual authorities (the "mockups"): `/home/jacob/Pictures/Screenshots/` on the PC** — 60 owner
   screenshots, NOT repo files. The block/plus-menu mockups are the 29 October 09:00–09:04 batch (14
   images) plus 09:19–10:44. The 27 Oct 20:17–20:22 batch belongs to the hover/Add-menu report (D03/D04).
   Two 29 Oct captures (17:04, 17:28) are **ChatGPT desktop windows used as design reference — do not
   book their appearance as 9to1 defects.**

## 2. Owner's sequence (do not reorder)

1. Boards + Files issues get fixed first; one bug at a time, then rebuild/reload the SAME dev copy for
   the owner to retest.
2. The next ARM64 `.deb` lands **after** Boards+Files are fixed and **before** the owner's Browse work
   resumes in my lane. Browse/Spaces are proceeding *alongside* via a parallel worker because the
   owner's allowance is running down; that does not put Browse before Files/Boards.
3. **Tonight's package is HELD by explicit owner choice:** "Hold the whole deb until the menu works."
   Do not build or deliver a package until the block-actions menu genuinely opens.
4. The owner also allowed Astra's Browse fix into the deb **only if independently verified by ~21:45
   on 29 Oct**. That cut-off passed with no independent verification, so it is OUT by the owner's rule.
5. Last delivered package (do not re-deliver, only supersede): `8e0fa938` —
   `~/Downloads/9to1 Package - Boards Saving (arm64 8e0fa938).deb`, sha256 `49975284f387f791c80e67fbf0a11b5c1045b71b76ea517fe10a3f9603dca178`,
   90,422,712 B, tree manifest `c8492da7cb5bac63c56cea46e8f94ee3506ddeb148e9d7874830286d94b66bf3`.
   Superseded debs are archived in `~/Downloads/9to1-arm-previously-delivered/` and never deleted.

## 3. Task A — the block-actions grip menu (the blocker; highest priority)

BF-35. The owner's mockups: the grip opens a vertical menu — **Delete, Duplicate, Move Block ▸
(Move up / Move down), divider, Design ▸ (Turn into: Text, Heading, Checklist, Bulleted list, Numbered
list, Table, Image, Divider)**.

Current state at `8aa8e906`:
- The menu is authored in CUI (`Boards/app/BlockActionsMenuCui.cui`) and hosted by a C#-owned `Popup`
  (`Boards/app/BlockGripCui.cs`). The grip document (`BlockGripCui.cui`) holds only the 32-unit trigger.
- Why not an inline menu: the gutter gives the grip a **32-unit grid column**; a 232-unit menu cannot
  render inside the row subtree without clipping or pushing the text column. Why not CUI's native
  `Container Type="Menu"`: its own press handling collides with the grip's **drag-to-move** gesture that
  the block rows already route (`BoardsBlockActionsTests.Actual_grip_pointer_routes…`). The reviewed
  precedent for C#-owned surface + CUI content is `Program.cs:694` (BoardProperties dialog) and
  `ToolbarBuilder.cs:409` (insert-menu Popup).
- Verified working: both documents parse and compile (they do NOT fall back to the "unavailable"
  control), they load through the embedded CUI transport, the trigger is found by name, and the Popup is
  now hosted in the owner window's overlay layer (parented at construction through `ISetLogicalParent`,
  matching `ChatAddMenuNativePopupTests.cs:60`).
- Measured root cause #1 (fixed): **CUI owns Tag slots** — `framework/CUI/Runtime/CuiControlLoader.cs:3586`
  assigns `Button.Tag` while wiring and `:3808` assigns its own reference to the loaded document root, so
  a handle published via `Tag` was silently overwritten while the setting code looked correct. The handle
  is now a `ConditionalWeakTable` side table keyed on trigger and menu (`BlockGripCui.HostFor`).
- **UNPROVEN hypothesis to test first:** these authored rows materialise as `CuiMenuItem` (Avalonia
  `MenuItem`), not `Button`, so the Button-typed row searches return empty while the menu is actually
  present. Probe `host.Menu` for `MenuItem`/`CuiMenuItem` types and print what is really there.
- Instrumentation exists for exactly this: open-chain counters `PressCount / ClickCount /
  ToggleCommandsReceived / OpenAttempts / OpenedCount / ClosedCount / FallbackClickOpens` on
  `BlockActionsHost`, surfaced in the failing assertion message ("MENU OPEN CHAIN …").
- Two diagnostic tests are intentionally RED and must be made green, not relaxed:
  `The_authored_grip_document_loads_a_real_toggle_and_publishes_its_menu_host` and
  `Opening_the_authored_grip_menu_presents_every_mockup_row_in_the_floating_surface`.
- Two pre-existing tests regressed and must be restored: `Actual_grip_pointer_routes_move_click_cancel_and_revision_refusal`
  **(gesture: "click", both freePlacement values)**. At the 17:32 baseline (grip files stashed) only the
  `stale` and `move` variants failed.
- Measured suite state: **Boards app.tests 223 total, 215 passed, 8 failed.** Six are pre-existing
  (`EmbeddedPresentationLifecycleTests.Two_live_boards_keep_custom…`,
  `BoardsEditorTests.Boards_cui_exposes_dynamic_host_and_menus`, the `stale`/`move` pointer variants,
  and two counted above as baseline), two are the click regressions, two are the intentional RED
  diagnostics. Re-run and compare against this exact number.
- Related rows still open: **BF-37** hover preview — hovering Add Block Above/Below must show the
  insertion line where the block will land, hovering Replace Current Block must turn that block's outline
  red with a red filter. Its absence is asserted in the parse test (`DoesNotContain PointerEntered /
  BlockInsertionPreview`) so it cannot be forgotten. **BF-38** "Copy link to block" is blocked on an owner
  decision: no block-link scheme exists in the specification; my proposal `9to1://boards/<board-id>#<block-id>`
  is in `docs/specification/open-decisions.json`. Do not invent a scheme and do not ship a row that copies
  a string which opens nothing.

## 4. Task B — the parallel Browse lane (Astra) and its two uncommitted units

A worker session `ses_e774c7e03ffeLvJbQBL01og96l` (agent `build`, model `openai/gpt-6-astra`, the
owner's ChatGPT-credential integration) is working Browse then Spaces in the same checkout, with
exclusive scope. Three files are **uncommitted in the PC working tree** — their content is now
committed for safekeeping in **`733f35c3`** (labelled WIP, unverified by me, not integrated into any
package). Treat them as the lane's live work and do not overwrite them:

1. `9to1 Workspace/Browse/BrowseChrome.cs` — tab-group preservation. First gates `RestoreTabGroups` on
   `Settings.RestoreTabs`, then replaced to preserve closed-only and mixed open/closed group metadata
   with both explicit reopens asserted. SHA256 `980a75ed4853f66b6301460df59c1a746a28853d7e93898739097a172f499d05`.
   Self-reported GREEN 63/63 in the owning suites. **NOT independently verified by me.**
2. `9to1 Workspace/shared/tests/Haven.Desktop.Tests/UnifiedWorkspaceSessionTests.cs` — its test.
   SHA256 `39537d3090bd673d3196a0e8d2d2e88aaab8161ed3ca7af0749a3010a7f168bd`.
3. `9to1 Workspace/Browse/Tests/BrowseNativeOwnershipTests.cs` — new **RED** regression: fails at
   `:100` (cross-thread lifetime observation) with the notification-failure-lineage control passing.
   The matching production fix (invoke `Publish` outside `_nativeSourceGate`) was written but **could
   not be compiled because of the disk guard**, so the tree may not be in a compiling state for the
   Browse projects — verify before building on it.

**ABBA deadlock, the most valuable finding of the day, captured natively (not predicted):**
- UI thread blocks in `BrowseNativeWorkspace.IsOriginalPublicationCurrent` → `chrome.OriginalClose`
  (takes `chrome._nativeSourceGate` while holding `workspace._sourceGate`);
- worker A holds `workspace._sourceGate` (`NotifyAsync → Retain → PruneAdmissionRefusals`) and blocks in
  `chrome.UnlinkAdmissionPropagation` (needs `chrome._nativeSourceGate`);
- CDP receive worker holds `chrome._nativeSourceGate` (`BrowseChrome.NativeObservation.cs:84-100`,
  `CaptureOriginalPageCommit`) and publishes at `:97` into `workspace.OnChromeStateChanged`, waiting for
  `workspace._sourceGate`.
Stack capture: `/tmp/opencode/astra-captures/cpu-b-matched/managed-stacks.txt` (PC-only). This is the
leading mechanism for the owner's greyed/stuck tab strip and the Home → Apps → Present freeze. Keep it
**separate** from the tab-group preservation unit: two defects, two units, two evidence chains.

Honest non-claims from that lane: no app character-loss defect (typed
`https://example.com:443/2026-10-29?q=1234567890` read back identical via clipboard on native display
`:10`; the keystroke loss was the automation probe itself); first-navigation stall is **intermittent**
(1 stall, 2 successes across the pre-fix baseline runtimes) with no root cause attributed; CPU arm A
measured a 15-process stable sample mean **104.60** / peak **106.76** of one core over 24.32 s, arm B
failed setup, so **there is still no matched A/B comparison** and "the lag is fixed" is not established.

Also note: the block-menus feature was never implemented anywhere in history (`git log --all -S` on
"Add Block Above", "Replace Current Block", "Move Block", "Copy link to block" = 0 commits), so "the
menus are missing" is our queue, not a stale package.

## 5. Environment limits on the PC (they are binding, not optional)

- **Disk is the constraint**: ~8.01 GiB free, i.e. only ~11–17 MiB above the 8 GiB reserve. A normal
  incremental Browse core+test build needs ~2.3 MB of bin/obj and cannot fit. My attempt to reclaim by
  deleting 7 redundant probe runtime copies FAILED SAFELY — each matched only 7/12 sampled >1 MiB files
  against the donor, so they are not byte-identical supersets and I did not delete them. Retain
  `/tmp/opencode/native-probe/runtime-browser2` (pre-fix CPU baseline for the A/B) and
  `/tmp/opencode/native-probe/runtime-boards` (reviewed immutable donor; Haven.dll `2199f145…`, 917/917
  PDB source matches). Reclaim order: per-file superset diff → archive manifests → remove only
  proven-subset copies.
- **Memory**: the dip to 0.90 GiB available at 18:39 is why a **≥2 GiB MemAvailable gate** now precedes
  any native launch in either lane, recorded with the launch. This OpenCode session IS the resident
  local model host (~45 GiB resident); it must never be swapped, stopped or starved. Never launch a
  second model host.
- **Never touch the owner's live instance: pid 572980**, running the 28 Oct
  `…/runtimes/70f3e900e55c-boards-m1-c-28/Haven`, which still refuses to close (BF-27/A06). That close
  failure is what blocks replacing the live dev copy, so native verification happens in sandboxed
  instances only.
- Working sandboxes: at least one live app instance per lane; retire your own via WM_DELETE, wait 30 s,
  then `kill -TERM`, then `kill -KILL` **only your own pid**, recording each step and retaining any close
  failure as BF-27/A06 evidence. Never signal a process you did not start.
- **Serial SDK**: only SDK build verbs occupy the slot. Count exactly
  `pgrep -af "dotnet-sdk-10.0.301/dotnet (build|test|publish|restore)"` (persistent `dotnet Haven.dll`
  apphosts do NOT occupy it; the literal `pgrep -c dotnet` guard is defective because a running apphost
  keeps it at 1 forever). One at a time, with a timeout wrapper. Overlapping invocations on the same
  projects are what look like 28-minute hangs — individual runs take 0.5–2 s.
- Build form used all day (normal Layer1 compiler run, not the root-live allocation):
  `/home/jacob/Documents/Codex/2026-10-24/prepare-the-local-9to1-teams-from/work/dotnet-sdk-10.0.301/dotnet build|test "<project>" -c Release --nologo -v q --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false -p:EnableWindowsTargeting=true`
- The `eng/development/workflow.py execution-admission` gate and the guarded
  `guarded_root_live_sdk03/04.py` + `prepare_root_live_plan03/04.py` pair govern the **root-live heavy
  SDK / ARM export allocation**, pinned to candidate `b24ab70e` (ledger generation 13073, ROOT-LIVE 13072).
  Ordinary local source builds are NOT inside that allocation and must not be blocked by it; equally, do
  not touch pins, `eng/`, or the ledger to make a refusal pass, and never promote a relaxed-memory or
  PARTIAL result into accepted/reusable evidence. ARM packaging stays with the PC lane.
- Native preview recipe: `docs/development/live-preview.md` plus — copy a profile, delete ONLY the copied
  `BrowserProfile/engines/chromium/Singleton*`, keep the real `HOME` (redirecting HOME breaks the bundled
  Chromium lookup), isolate the workspace by rewriting the record's `RootDirectory` and the 8
  `.9to1-files/bindings.json` paths, and run under `Xephyr -screen 2048x1280x24 -ac -no-host-grab` on a
  display you own (`:9` was mine, `:10` Astra's, both now retired with exit 1). Screenshots under
  `/tmp/opencode/astra-captures/` with build/source identity, surface, theme, viewport, time and
  native-vs-headless labelling.
- Test-harness traps measured today (they cost hours): `TestUiThread.Run` is a single-threaded queue, so
  a nested `Run` inside another `Run`'s argument list deadlocks forever; a `Task` parameter cannot bind
  the `ValueTask` returned by view-model dispatch; `BoardsViewModel._isEditMode` starts **true**, so
  dispatching `ToggleEditView` turns editing OFF; `await` inside `TestUiThread.Run` is a compile error
  (pump with `Dispatcher.UIThread.RunJobs()`); never `--no-build` after a failed build (it runs the stale
  assembly and prints passes that do not exist — two false passes were recorded and withdrawn today);
  the Boards test assembly treats null-flow warnings as errors; and `Haven.Desktop.Tests` lists its files
  explicitly (`EnableDefaultCompileItems=false`) so a new test file must be registered in the csproj or a
  filter matching zero tests exits 0 and looks like a pass.

## 6. Boundaries for the overnight agent

- `main` only; no new branches, no worktrees (owner rule 28 October). Commit and push each unit; never
  commit while the tree does not compile, and never present a PARTIAL build as clean.
- Do not collide with the live lane's exclusive scopes: Boards/**, Files/**,
  `Haven.UI/Drawing/HavenIconCatalog.cs`, `Haven.Application/LocalWorkspaceFolders.cs`,
  `Haven.Desktop/Services/BoardsCreateRouting.cs`, `BoardsWorkspaceFolders.cs`,
  `App.UnifiedWorkspaceHost.cs` (its keyboard ownership at `:1376-1400`, subscription `:164`, is a known
  handoff point — reproduce natively before changing those guards).
- CUI is the only application-authoring language: structure, styling, state, behaviour, menus and
  interactions are authored in CUI with typed actions. C# may own only hosting/floating surfaces and
  low-level infrastructure. Never load or fall back to Avalonia FluentTheme/Fluent templates anywhere.
- One implementation per app: Haven embeds the SAME standalone app implementations; never maintain a
  second copy of an app.
- **Do not message the parallel worker from this run.** The owner reports that prompts arriving in the
  Astra session cause long prefill delays in their ChatGPT app. Coordination is asynchronous through the
  repo: write to `.handoff/boards-first-v3/work/ASTRA-REPORT.md` on the PC, or leave notes in the commit
  messages and this file. Only send a message if the owner explicitly asks.
- Do not weaken, skip or manufacture tests. If you change an existing expectation, state what it enforced
  before and what it enforces now (the gutter-test change at `aa0d7b85` is the model: the expectation was
  stale against today's owner-directed outline overlay, and the same geometry is still asserted).
- Independent acceptance stays PAUSED by owner instruction. Full seven-criteria acceptance is not
  tonight's job; focused genuine compile + smoke checks are.
- Every claim needs its evidence level: test / on-screen capture / owner-reported / not yet observed.
  ARM execution, installation, first run and on-screen native retest are **NOT RUN for every package
  delivered so far**. Do not call an app complete while known gaps remain.
- Open owner decisions you must not resolve yourself: BF-38 block-link scheme; the three Files roots
  (Cloud / 9to1 Local Workspace / This Machine, with unresolved Linux volume wording); orphan tab-group
  cleanup semantics. Keep them queued in `docs/specification/open-decisions.json`.

## 7. Reporting

Report progress as: 👀 what is now working (with evidence level), what is still broken (retained as
failures), exact suite counts compared to the numbers above, commit SHAs pushed, and the next executable
action. Use the owner's format in `docs/development/progress-reporting.md` for anything that reaches
them. Rough implementation/Accepted percentages are allowed and expected, labelled as estimates, but a
compiled package never counts as accepted.
