# OpenCode desktop takeover — 26 October 2026

Jacob selects **OpenCode on `pc`** to continue 9to1 development locally. The aim is
one native development copy: report a bug, fix it, run the affected basic check,
build locally, close/reload that copy, and immediately retest. Do not batch twenty
bugs into another installer. Native code changes need a build/restart; do not
promise instantaneous hot reload.

Cloud authoring and new SDK launches stop after this handoff is pushed. The local
worker may proceed within the owner's already-authorized scope after observing its
exact source and exclusive ownership. No new user permission is needed for ordinary
implementation, basic checks, launching the requested development copy or its
normal reload. Do not mistake this document for a receipt that local execution has
already started. The cloud currently has **no supported write/reload channel to pc**.

## Acquire the exact source without disturbing local work

Repository: `https://github.com/CroakyJake12/9to1.git`.
Handoff branch: `codex/desktop-recordings-recovery-20261026`.
The startup message supplies the final full commit. Pin that commit once; verify
its tree and preserve all existing branches, local edits, untracked files and
worktrees. Use the existing clone at `/home/jacob/Documents/GitHub/9to1`, with a
new detached worktree under `/home/jacob/Documents/GitHub/9to1-worktrees` if needed.
Do not reset, blindly pull, replace the clone, or overwrite a running team's files.
Open the new worktree as the actual OpenCode project/session directory; check both
`pwd` and Git HEAD before model edits. `PWD` must agree with the real tool cwd.

Read in this order:

1. `AGENTS.md`, this document and `docs/development/live-preview.md`.
2. `docs/development/workflow.md` and the **existing local** coordination state.
   Reconcile the cloud handoff as evidence; do not overwrite the local ledger with
   a cloud snapshot or invent a replacement coordinator.
3. `docs/development/desktop-recordings-2026-10-26.md`,
   `docs/development/desktop-test-build-2026-10-26.md`, and
   `docs/specification/owner-references/2026-10-26-desktop-recovery/requirements.md`.
4. `docs/specification/9to1-development-specification-v2.md`, its later owner
   amendments, the Boards review and adopted 25 October UI report.
5. `docs/development/local-transition-2026-10-25.md` for retained owner preferences,
   Boards details and history. Its older source heads, connection observations and
   worker labels are historical, superseded by this handoff.

The earlier user checkouts were clean: main `903c858a`, detached transition
`106dfeed`. Their source was already published; **do not ask Jacob to push them
again**. Current development has additional recovery changes on the handoff branch.

## First local session: establish a genuine running copy

1. Inspect installed OpenCode's supported APIs/configuration and actual available
   subscription models. Use `openai/gpt-6.1-sol` as the lead and
   `openai/gpt-6-luna` for two disjoint implementation workers when available.
   Keep actual leader/team sessions visible in OpenCode. Preserve existing tabs,
   OAuth credentials and repair commits. No Mercury, paid fallback, replacement
   inference server, or automatic resurrection of historical nine-team cadence.
2. Inspect current PC RAM/disk and existing build processes. Retain the single SDK
   allocation with memory/disk/time/process and exact source guards. Do not raise
   guard limits just to make a failed run pass; choose a supported local allocation
   from observed resources. Existing cloud limits are not measurements of PC RAM.
3. Identify the actual installed launcher and running process, including executable,
   arguments, working directory and profile without printing secrets. Owner's
   `dpkg-query` confirmed `haven 0.1.0+test20261026.git59d66496`; its process lookup
   returned **no executable paths**. This verifies installation, not what was running.
4. Verify CUI dependencies. Retained pinned inputs include XamlX
   `009d4815470cf4bf71d1adbb633a5d81dcb2bb52` and Avalonia.DBus
   `864a05282841bf04006890f04d11d60d1a046aa9`; the prior handoff describes 602 files.
   Use the existing pinned dependency acquisition and hashes, not random packages.
   The successful cloud package used .NET SDK 10.0.401 and the normal source graph.
5. Use the local guarded owning build/publish into a new immutable runtime slot.
   `Haven.Desktop.csproj` is under `9to1 Workspace/shared/src/Haven.Desktop`.
   Target Linux **x86-64**, `net10.0`, not ARM or the Windows target. Preserve normal
   dependencies/CUI compilation. The CUI build-only host fix is already included.
6. Set up `eng/development/live_preview.py` using its documented real build receipt
   and an explicit stable development profile. Observe the actual current profile;
   do not guess or change it silently. Keep application data outside runtime slots.
   Do not run two copies against one profile or copy a live database. Keep the
   installed package available; no uninstall/reinstall is needed for this loop.
7. Start the real canonical app. Record loaded apphost and Haven.dll hashes,
   source revision, actual PID/start identity, profile choice and native screenshot.
   Demonstrate one normal-close/reload cycle. A close veto or unresolved save dialog
   must retain the existing app; never force kill it to obtain a successful reload.
8. Report the exact local result and ask Jacob to retest one concrete bug. A helper
   fixture pass or source commit does not establish actual native PC activation.

The launcher has `init`, `prepare`, `start`, `status`, `reload`. It launches a tiny
process guardian to retain the actual exit code; it does not run a model or server.
Automatic normal close uses the exact X11 main-window PID/title and requires
`xprop`/`wmctrl`. On a session without supported X11 control, resolve supported
normal app close locally; manual normal close plus verified exit is the documented
fallback. Do not expose a public shell or weaken process ownership. Its build
receipt is a trusted local guard/worker input, not a remote signed attestation.
The operator-recorded profile is explicitly distinguished from app-confirmed state.

## Bug loop and parallel work

The Sol lead owns integration, exact running source, affected checks and one local
build/reload at a time. Give one Luna worker a concrete Spaces/Home requirement and
one a Build/Boards requirement with exclusive files. Implement meaningful end-to-end
behavior. Let independent features progress while one reported bug is being fixed.
When work overlaps a bug's files, checkpoint/reassign that scope before editing.
Do not schedule fifty agents or create a second supervisor from old suggestions.

For each bug: record its visible reproduction, reproduce in the loaded build,
change its genuine canonical owner, run a relevant check, rebuild only what changed
through the existing guard, then reload and retest the same action. Preserve drafts,
profile and original object identities. Report what changed and what remains
uncertain. Capture a screenshot during the existing check when useful; do not stall
implementation just to take pictures. Full independent acceptance remains **PAUSED**.

Finish Home/Haven, Spaces, Build and Boards first, then remaining apps. Standalone
apps are the current implementations to integrate/finish, not disposable legacy
alternatives. One shared implementation per app. Integrate checked compatible
changes into main; keep unfinished work separate. Do not claim 100% while known
feature gaps remain or infer implementation percentages from test counts.

## Current exact changes and verification limits

- Delivered package source `59d664964649c04661eea87921ddad3bf0db1f74` passed package
  integrity and a narrow native Welcome/normal-close probe. Jacob subsequently
  reported severe lag, tab scrollbar and unfixed workflows. Those reports supersede
  any inference of desktop usability from the startup check.
- Video at 14:26 showed `/opt/9to1/UI/Home.cui` missing, while the delivered package
  includes that file under `/usr/lib/haven/UI/Home.cui`. `HomeCuiScene` resolves it
  from `AppContext.BaseDirectory`. **Running-binary identity is unresolved**, not a
  proven stale install. Verify locally before fixing only the wrong copy.
- `99b21c26`: actual Home Discover HF browser moved ahead of the large provider list
  so it is visible. Composed-route/render checks added; latest checks NOT RUN.
- `d3659582`: tab overflow uses arrows with hidden scrollbar; drag target uses clipped
  hit-testing, and indicator changes no longer republish/rebuild the whole workspace.
  New eleven-tab pointer/arrow/selection/close regression added. Latest runs were
  blocked by the cloud's 14 GiB memory guard before tests; no new pass or measured
  lag improvement is claimed. First attempt also used an incorrect project path;
  corrected path is `Browse/NativeUI/Tests/HavenOS.Apps.Browse.NativeUI.Tests.csproj`.
- Spaces: new CUI permission review uses the actual task permission owner, exact
  Task/Run/request and current identity. It shows declared sensitivity/inputs/actions,
  retires stale selections, preserves a real decision ACK when refresh fails, and
  never implicitly resumes a task. Source reviewed; SDK check pending. The
  conversation-keyed observation now correctly refuses to claim current command
  authorization solely from historical owner binding.
- Build **WIP**: template provider selection and exact file review, canonical manual
  project registration, per-file create-only writes and cold readback, opening the
  same project. UI dispatcher and failure ownership were repaired in review.
  **Remaining blocker:** the template path asks for an empty folder but does not yet
  reject nonempty folders before registration. Existing files can be registered
  before a starter-file collision is refused. Fix this through the existing issued
  capture/registration owner before exposing template creation as complete; do not
  add a raw-directory permission bypass. New zero-file capture/setup changes and
  tests are uncompiled. Preserve generic path guarantees and test actual empty and
  nonempty targets with no setup effects on refusal. Synthetic journal captures or
  forged-payload tests alone do not establish this real path.
- Live preview: focused tests use real small ELF children plus mocked X11 responses;
  actual Haven/PC close and reload remain NOT RUN. No automatic manifest watcher,
  remote worker dispatch or instantaneous XAML/CUI hot reload is claimed.

Relevant local verification after implementing the outstanding Build boundary:

- `Browse/NativeUI/Tests/HavenOS.Apps.Browse.NativeUI.Tests.csproj`: eleven-tab overflow,
  pointer swap/split/group, Escape/identity change and split view cases.
- `shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj`, `net10.0`:
  `HomeDiscoverCuiRenderTests`, actual central Browse Home models route,
  `Space_task_widget_reviews_and_denies_same_actual_pending_permission_without_resuming`,
  `DeveloperTemplateProjectEntryAdapterTests`, and actual Files directory setup tests.
- `Spaces/Tests/HavenOS.Spaces.Tests.csproj`: `SpaceTaskWorkspaceTests` (verify the
  actual project filename before invoking; do not repeat cloud's guessed-path mistake).
- `Dev/Tests/HavenOS.Dev.Tests.csproj` and
  `Home/Tests/HavenOS.Home.Tests.csproj`: template materializer and setup journal.
- `python3 -B -m unittest discover -s eng/development -p test_live_preview.py -v`.

All paths in that list are relative to `9to1 Workspace` except the Python command.
The portable Desktop test target has explicit file includes; newly added permission
and template tests must actually compile and be discovered, not silently omitted.

Older retained results are historical: core tab 117/117, mounted drag 12/12, shared
CUI hover 27/27 at their documented older revisions. A legacy compatibility hover
case still failed (`#ff292a2d` versus transparent). Do not skip/weaken it or substitute
raw AXAML styling for canonical CUI ownership. The whole-window 16ms Tidal gradient
loop predates this recovery; it is a performance suspect, not a proven new lag cause.
Measure the actual PC before claiming speed gains.

## Important user requirements to preserve

- Device-native colour emoji; compact ChatGPT-like attach menu with Specialists,
  Assistants, Skills (new icon), Plugin and Apps, no giant blue tiles.
- Light mode throughout, effective-current-colour hover, coherent tab title/close
  hover; drag preview with generous swap/split/group regions and corresponding glow.
- Dulche/local model discovery plus HF browsing/install. Gated HF keys must use the
  existing encrypted profile secret owner, never plaintext/config/arguments/logs.
  An installed model is not proof of engine/inference readiness.
- Boards: warmer purple default, real Glow gradients and coherent table colours,
  compact Present-style toolbar, editable font family/size dropdowns, quiet controls,
  configurable filled section icons, focused-only margin drag/add controls, normal
  inline maths, correct checkbox geometry, alignment and page/sidebar spacing.
- Send-button stacking is an intentional feature. Preserve the adopted UI report's
  exact contextual shelf order; do not remove it as a layout bug.
- Be direct, concise and autonomous; avoid repeated permission questions, inflated
  progress estimates and long acceptance-only pauses. Keep losses/failures visible.

Thunder is parked separately. Do not resume its gateway/model benchmarks or change
services to make this desktop test pass. Historical connected PC or laptop bridge
labels do not prove a current administration channel. Keep all credentials private.
