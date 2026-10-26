# Desktop recording triage — 2026-10-26

This is a local recovery handoff for the two 9to1 recordings and the historical bug log. It is not an acceptance report. Recording A is 114s; Recording B is 48s. Timestamps below are offsets from each clip start.

**Evidence labels:** **Footage** means visible in the reviewed recordings. **Source** means the current checkout contains the named surface, check, or message; it does not prove a runtime cause or successful behavior. **Unverified** means the relevant interaction/build/runtime was not exercised.

## Baseline and current work

- Published `origin/main` is `903c858afd3d8c3cc00d411cee8eb4ebffdbc150`. Its source parent is `367f5d50`; the push adds generated Canvas outputs and an xdotool installer. The owner showed clean main and a clean detached `106dfeed` transition worktree, both already published. There is no outstanding source change in those inspected folders. The installed package identity is still inaccessible.
- Recovery is isolated on `codex/desktop-recordings-recovery-20261026`, draft [PR #38](https://github.com/CroakyJake12/9to1/pull/38). Preserve all local source and Thunder. Full acceptance remains paused; these repairs receive focused compilation and smoke checks.
- Focused repairs in the local branch: explicit vertical stacks for Home Library and conditional picker branches, Spaces history and task-permission branches; case-insensitive comparison only for model scope/category tokens (exact IDs preserved); Create board dialog uses the actual Home page CUI background resource; and the Files board save no longer re-enters `RequireWrite` after acquiring Home's retained fence. The authorization preclaim and final CAS guard remain. `fb04ea0d` corrects normal-return revision-conflict classification.
- Focused test results reported for this pass: CUI layout `19/19` at `6ca07929`; Home picker category contract `20/20` at `6ca07929`; desktop-save-5 `58/59` at `db42fbfa` (stale parent conflict flag); desktop-save-6 `59/59 PASS` at `fb04ea0d`, total `53.756s`. A separate isolated X11 startup smoke at `e1ea0da1` rendered Welcome and exited `0` after `WM_DELETE_WINDOW`.
- These checks are not installed-app acceptance. No GNOME/compositor, installed launcher, browser engine, model/provider, account/user, or real workspace workflow acceptance has been demonstrated. Do not mark these workflows fixed/verified from source or focused tests alone.

## Current owner additions and implementation

- **Attachment menu:** the actual Chat composer now opens a compact grouped list in an Avalonia popup anchored to Add. The card uses list rows instead of blue tiles, keeps the shared catalogues/search, and retires on dismissal or composer detachment. Go retains a transparent outside-click dismissal layer around the same compact card. Mention search preserves the composer caret. Native X11 and headless fallback have separate assertions; a headless pass cannot establish native window layering.
- **Device emoji:** the shared font fallback tries the installed platform colour font before the bundled monochrome fallback. The composer trigger and picker explicitly select Segoe UI Emoji, Apple Color Emoji or Noto Color Emoji when installed. Unicode sequences, search and insertion remain unchanged. Missing device fonts retain licensed fallback coverage.
- **Dulche discovery:** a SHA-verified installed local model is shown as installed even when its engine cannot be queried. That row stays unavailable, with no invented capabilities or inference readiness. Live matching models retain capabilities actually reported by their provider.
- **Hugging Face:** the existing Discover page now searches public GGUF repositories, displays pinned revision/file/licence/size information and installs eligible single-file models through the canonical importer. Downloads verify expected length, SHA256 and GGUF headers. Unsupported shards/formats are excluded. Installation and inference readiness are separate states.
- **Gated access:** optional tokens use the existing profile-scoped platform secure store (Linux AES-GCM with private key permissions; Windows Credential Manager). No plaintext fallback, settings persistence or token redisplay. Input is masked, cleared after submission and has undo disabled. Token presence is rechecked for the current actor; profile/auth changes retire stale operations. HF credentials are not forwarded to CDN redirects, process arguments or importer environment.

Public HF API metadata was actually queried without credentials. No gated account download, model inference, desktop installation or Thunder request was performed. Service tests use deterministic HTTP fixtures plus the real canonical importer and encrypted Linux store; UI fixtures establish interaction behavior only.

### Live themes, menu routing and tab interactions

The recovery now wires the attachment menu's **Specialists, Assistants, Skills,
Plugin and Apps** labels to the real catalogues. The actual unified Spaces host
receives the Assistants route; partial initialization is retired before the prior
chat is restored. The programmatic attachment popup has the composer as its real
logical parent so the native popup and overlay fallback share resource/template
ownership. The native X11 popup and headless fallback have separate passing checks; the installed desktop remains unverified.

Home appearance changes publish to both retained HUI content and existing CUI
scenes. Explicit scene overrides remain authoritative. The CUI-owned window's
TidalBackground now animates its gradient without recolouring global CUI aliases
on navigation. Hover adds a subtle wash derived from the effective brush, leaving
its authored fill and foreground intact; menu selection retains a keyboard cue.
Tab title and close parts share one row surface. The compatibility BrowserView's
base tint opacity is now a style, so pointer-over can actually override it.

The native Browse drag path retains canonical tab identities and shows a moving
preview. Generous horizontal target zones select edge swap, left split, group,
right split and edge swap. Split previews illuminate only the receiving half;
group previews illuminate the entire target. Directional splits explicitly open
side by side. Existing cross-profile split behavior is preserved: each tab keeps
its own engine/profile directory. Swap/group retain their stricter placement
rules. Preview cancellation and actual pointer-driven outcomes have owning checks;
source implementation alone does not establish an installed-desktop pass.

A separate confirmed Write defect was fixed: the final read fence now receives
the exact reviewed wrapped arguments, including the raw Notes request. The
selected-read and selector-substitution regressions passed **4/4** at `343e4daf`
(`notes-read-fence-2`). Authorization, digest/schema and ownership checks remain.

All checks retain exact source, command, logs and TRX in the existing private
coordination evidence. Failed fixture compiles and assertions are retained. These
are fresh component checks with partial dependency coverage, **not reusable
acceptance results**. The next handoff must read the current receipt index rather
than treat the older pass counts above as proof for every later change.

### Latest focused verification and resume point

The tab gesture checks now exercise real pointer input against the mounted CUI
surface, including its actual drag preview and non-hit-testable indicators. The
headless test host must commit a render frame before hit testing, and await the
original workspace notifications before observing controls changed by core calls.
Do not replace these steps with directly setting hover/drop flags. Accepted close
retirement now rejoins the original close graph instead of inspecting disposed
engines. The split/group gradient follows the current theme accent and stays
visible on a light selected tab.

| Check | Exact source | Result |
|---|---|---|
| Core tab identity, placement, profile and persistence | `8d22eabd` | 117/117 PASS |
| Mounted pointer swap, split-left/right, group, Escape, navigation/close/disposal cleanup | `d7c718ef` | 12/12 PASS; three actual rendered captures |
| Shared CUI appearance and effective-colour hover | `d1b7461d` | 27/27 PASS |
| Desktop appearance, attachment popup fallback, routes and compatibility tab hover | `43fc0155` | 26/27 PASS; legacy compatibility button fill assertion remains failing |
| Isolated native X11 attachment popup and Bright/Dark/Bright chat draft preservation | `5085e2a8` | PASS; four captures, normal exit |

The native popup/theme source used by the last row has not changed since that
check; later source changes concern Browse dragging, compatibility tab markup and
test fixtures. This is an applicability observation, not promotion to immutable
build reuse or installed-app acceptance. The compatibility hover test explicitly
loads the compiled legacy theme; the active CUI tab surface is checked separately.
The remaining legacy assertion expects a transparent title/close fill but receives
`#ff292a2d`: the CUI effective-property projection outranks raw legacy AXAML values.
Do not weaken that assertion or override the CUI property hierarchy just to pass it.
Reconcile the compatibility adapter or move the behavior check to its actual
canonical owner, with equivalent title/close coverage, before closing this item.
All preceding failures are retained alongside successors, including setup errors
and the genuine frozen-palette, menu-selection and close-retirement defects.

Resume from the published recovery branch and the latest versioned private
handoff in the existing coordination ledger. Read the retained check indexes,
then reconcile Git, current CI, actual worker liveness and available disk before
launching anything. The recovery procedure did reload instructions, state and
worktrees after context compaction; it still reports unavailable historical
execution exports/runners, so complete historical recovery is **not verified**.
No automatic compaction hook, native desktop write access or installed release
is claimed. SDK jobs remain serial and enforce the 256 MiB disk floor.

### Remaining runtime and infrastructure limits

The existing ARM preview bootstrap requires ARM64 artefacts and owns a different runtime contract from the model-free x64 broker package. Do not relax its architecture guard to make x64 appear ready. Reconcile the actual installed x64 package and runtime owner before claiming local chat works. The discovery evidence adapter currently projects its first eligible installed model, not a full model-store inventory.

PR #38's existing [Android compile run](https://github.com/CroakyJake12/9to1/actions/runs/40297375309) failed on broad desktop source inputs/missing compatible references; workflow invariants passed. No Android baseline comparison was executed, so the gate remains failing rather than waived as unrelated. Native GNOME, packaged browser engines, actual provider chat and installed-workflow acceptance remain outstanding.

## Bug matrix

| ID | User-observed footage | Current source corroboration | Status and next check |
|---|---|---|---|
| NAV-01 | A 00:03: shell reports no registered native surface. AI Studio tile is outlined, but the actual requested app/route is not established. | `BrowseAppContentContracts.cs` defines this refusal text. Source inspection has not identified the recorded route request. | **Unverified.** Capture the originating tab/app request and native registry admission/logs; do not infer route from the highlighted tile. |
| BOARD-01 | A 00:06–00:20: duplicate Library text; Create board dialog labels overlap, no visible Create/Cancel, Library shows through dialog. | Local fix: explicit stacks and dialog background bound to the Home page's live CUI background resource. CUI layout test passed `19/19` at `6ca07929`. | **Focused test passed; native UI unverified.** Check configured/unconfigured Files states, dialog actions, opacity, themes, sizes, and actual hit targets in the installed candidate. |
| BOARD-02 | A 00:16–00:19: cancellation notice remains when another create attempt is open. | Board creation flow and feedback are present in Home/host source; no operation-lifetime fix has been verified. | **Unverified.** Cancel, reopen, submit a valid name, and inspect persisted files and feedback ownership. |
| BOARD-SAVE-01 | Additional defect reproduced during this recovery pass: the real Files-backed Board save timed out waiting on its own Home-store lease. Recordings do not show a save result. | Local fix removes only the reentrant `RequireWrite` after Home's fence is acquired; preclaim authorization and final CAS guard remain. Save suites: `58/59` desktop-save-5 (stale-parent conflict-flag failure), then `59/59 PASS` desktop-save-6. `fb04ea0d` fixes normal-return conflict classification. | **Focused save verification passed; native workflow unverified.** Test a real disposable Board save/reopen and stale parent race in the installed app. Remaining source-only issue: `RetryAuditAndReceiptAsync` currently reports every known provider failure as `Conflict=true`; narrow to actual `RevisionConflict`, then cover and rerun focused save checks. Keep this separate from the fixed normal-return classification. |
| WEB-01 | A 00:26–00:35: Google URL tab remains on Firefox renderer placeholder; Gecko unavailable. At 01:21–01:23 settings offer Firefox/Chromium; changing default states it applies to new tabs. | Browse settings and unavailable-renderer UI are represented in source. No successful engine render is evidenced. | **Unverified.** Test installed Firefox and Chromium independently, including a harmless page, reload, navigation, new-tab default, and explicit existing-tab engine selection. |
| AI-01 | A 00:52–00:57: Dashboard “Edit with 9to1” attempt; historical log records refusal at `127.0.0.1:11434`. | The recorded source used `IOllamaClient` and could choose an arbitrary discovered model. Recovery now consumes the exact Home route through `IProviderModelClient`, carries reasoning constraints, disables fallback and persistent memory reads, bounds the response, and rejects plans if the selection changes during generation. | **Implementation changed; installed runtime unverified.** Exercise the configured Home/Dulche route and an unreachable state. Unsupported reasoning remains a refusal, not a silent fallback. |
| AI-02 | A 00:59–01:01: Discover says 9 configured providers, 0 models, and Ollama could not be queried. | Verified installed Dulche artefacts remain visible when runtime discovery fails; HF catalogue/install and protected optional credentials are implemented. | **Focused service/UI checks only.** Native x64 runtime and installed provider inference remain unverified. |
| AI-03 | A 01:53: bottom status says route snapshot scope/category does not match request. | Local change compares those two contract tokens case-insensitively, preserving exact route/provider/model IDs. Home picker contract tests passed `20/20` at `6ca07929`. | **Focused contract test passed; runtime unverified.** Trace actual request/response scope/category and preserve strict identity validation. |
| FILES-01 | A 01:13: Files explicitly asks for an empty workspace folder. | Files setup is a real prerequisite flow; this screen alone is not evidence that Files source is missing. | **Unverified.** Complete setup in a disposable empty folder, confirm configuration and browsing, then test Board creation from both setup states. |
| STORE-01 | A around 01:37–01:38: generic action failure appears. The long SQLite/OpenFailed cold-recovery details are historical-log evidence, not independently legible in reviewed frames. | CUI runtime has a generic fallback action failure (`CuiControlLoader.cs`); this does not corroborate the SQLite cause. | **Unverified.** Reproduce Web Automations with an isolated profile; inspect principal, directory/file type/link/key pairing, locks, owner/source recovery and cleanup. Preserve data and underlying errors. Do not attribute to model connectivity. |
| ACCOUNT-01 | A 01:42–01:46: user management unavailable; account view reports CAKE sign-in and owner Access transport unavailable; profile/plan/billing/username/icon features unavailable. | Account and user-management fallback surfaces are represented in the source. | **Unverified.** Establish intended optional services versus missing wiring/artifacts; test configured transport or record the external-service blocker accurately. |
| FEEDBACK-01 | A ~01:44–end and B 00:00–end: generic “action could not complete” banner persists across page/Spaces changes; origin is unknown. | `CuiControlLoader.cs` emits that exact generic fallback when no more specific action failure is available. Emitter does not establish which action failed or banner lifetime owner. | **Unverified.** Capture originating action/diagnostic and test scoped dismissal/lifetime across navigation while retaining diagnostic history. |
| SPACES-01 | B 00:01–00:03: attachment popover is under composer/status text; no click result shown. | The composer now owns a compact native popup and its logical-parent fallback; catalogue routes and retirement are covered separately. | **Native/fallback focused checks passed.** X11 popup/theme check at `5085e2a8` and fallback check at `43fc0155` pass; verify the installed destination before acceptance. |
| SPACES-02 | B 00:08, 00:10–00:11, and ~00:31: multiple saved-chat rows overlap Search/Clear and recent-chat notice. | History/search/conversation UI exists; local edit groups the history area in a vertical stack. | **Unverified.** Test multiple rows, search/clear, selection, scrolling, and empty state in the running candidate. |
| SPACES-03 | B 00:11–00:18: emoji UI expands composer; grid edge clips a visible row. Whether inline placement violates spec is not established. | Emoji/composer UI exists; no source/spec finding here proves intended popup placement. | **Partially observed; behavior unverified.** Check V2 behavior, sizing, scroll, focus, insertion, dismissal, draft preservation, and unobstructed controls. |
| SPACES-04 | B 00:00–00:37 and 00:39–00:47: local models unavailable message and Manage local models action are visible; destination is not shown. | Recovery action/model manager surfaces exist. | **Unverified.** Activate action and verify a usable model-manager destination and routing policy; do not assume all Spaces modes require local inference. |
| SPACES-05 | Historical screenshots/log only: formatting toolbar remains over Library. Not independently shown in A/B reviews. | Not confirmed in current source review. | **Historical claim; unverified.** Reproduce with toolbar open, navigate away/back, check ownership, dismissal, focus and draft retention. |
| THEME-01 | Historical screenshot/log claim: contrast fault in creation window. In A the dialog is overlapped/translucent, but no clear controlled theme comparison is shown. | Local dialog background follows the active Home page's CUI background resource. A headless test changes GlowBright to SuperDark and checks the dialog brush; no installed native theme-cycle evidence. | **Unverified.** Exercise light/dark themes and representative sizes in the installed candidate; check contrast, control visibility and hit targets. |
| SHUTDOWN-01 | Historical log claim: shutdown fails resolving `cakeos_canvas_rnote_poc` and native resource/borrower ownership. Not shown by A/B footage. | Canvas native bridge and library name exist in source; presence does not prove packaged resolution or cleanup. | **Unverified.** Launch the packaged candidate, exercise relevant Canvas path, close normally, inspect dependency resolution and cleanup logs after partial initialization. |
| BUILD-01 | A 01:07–01:10: empty saved-project list and manual path option. | Empty list is a valid state, not evidence of failure. | **No defect established.** Separately open a disposable project through the manual path and verify the actual project interface. |
| REGRESSION-01 | A shows many tabs/overflow and theme controls; B shows Spaces mode changes. No failure established from these states alone. | No source-confirmed regression here. | **Check only:** tab identity/overflow/close, mode switching, and contextual UI cleanup during broader acceptance. |

## Acceptance work still required

1. Package and launch the exact recovery candidate through the installed/user-facing path; record build identity and test with a disposable profile. Existing X11 Welcome/close smoke is isolated startup evidence only, not installed or GNOME acceptance.
2. Verify native Board creation/save/reopen and stale-parent conflict handling; Files setup; Spaces attachment layering, history, emoji interactions and model-manager destination; browser engines; provider enumeration/inference and Dashboard planner; Web Automations storage; account services; manual Build project opening; and ordinary shutdown.
3. Fix and test the remaining `RetryAuditAndReceiptAsync` conflict-flag classification separately. Resolve the persistent cancellation/generic feedback lifetime and investigate SQLite recovery without deleting user data or weakening checks.
4. Repeat critical workflows after a cold restart. Capture screenshots/logs and mark each substep separately. Focused tests and isolated X11 startup pass; all installed-app workflows above remain **NOT VERIFIED**. No overall acceptance claim is supported.
