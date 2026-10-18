# PC implementation worker checkpoint — 2026-10-18 22:20 BST (21:20 UTC)

Source identity: branch `opencode/v2-pc-sequential-20261018`, HEAD `7fee4501`
(launcher review fixes, focused fixture 4/4) on top of `889fbaba` (R01 shutdown diagnosability),
`b72bc513`/`58d82393` (canonical CUI launcher) and `e1d02a7d` (R01 causal-lineage repair).
Ancestry: `7fee4501 889fbaba b72bc513 58d82393 e1d02a7d 627ef494 47ab11cb ce5df608 c8357412
7d2db82c 04f34b8d c6adfa02`, base main `06a39628`. Working tree clean apart from untracked `work/`
(this file + evidence).

## Pre-pause state (22:43:38 BST, authoritative OS clock)
Ledger checkpoint recorded against `7fee4501`, record sha256
`dc0bfc9292909d22e564cd08b9c61968aa370ae94c6e4ec5ba9d540672354b88`, `acceptance_status: NOT_RUN`,
`next_milestone_id: R01`. Owner pause 22:56:37 and laptop poweroff 23:01:37 BST are respected; no
timer was extended or cancelled. No Haven instance of mine is left running (all closed normally
first; SIGTERM only where normal close failed, recorded as cleanup). Owner preview PID `1885357` /
window `0x140001b` / profile `/tmp/opencode/haven-data-4` untouched and still the only running
instance. No owner-preview replacement was performed with an unaccepted build.
Recovery condition for the next session: after the pause is lifted and the PC is back, resume R01 at
the CuiSceneHost retained-dispatch repair described in
`work/pc-worker/evidence/R01/shutdown-diag-20261018T2234Z/THIRD-PATH-FINDING.md`.
Its "Verified resume anchors" section (re-checked at 22:47 BST against `7fee4501`) lists the exact
`CuiSceneHost.cs:301/746/751/775`, `CuiControlLoader.cs:356/595/602/615` and
`NativeAppContentRegistry.cs:49` sites, and states the first executable action: add a scene-host
failing-before regression (publish a Browse surface, drive one verified effect-free admission
refusal, close the `CuiSceneHost`, assert close completes while the refusal stays inspectable),
capture it failing at `7fee4501`, then implement the typed settled-failure contract and prove
passing-after, then repeat the real apphost refusal → normal close exits → relaunch restores
tab IDs/selection journey. No framework source was changed for this in the pre-shutdown window.

Acceptance handoff for the launcher slice (for independent review, not self-certified):
`work/pc-worker/evidence/R02-LAUNCHER/ACCEPTANCE-HANDOFF-LAUNCHER-2026-10-18T2244Z.md`.

## Owner-directed priority (current)
Visible product defects take priority; R01 internals are frozen unless a concrete new failure
requires them. R01's real-runtime and CUI-recovery gates stay OPEN and must be returned to.

## Scheduled stop (owner instruction, do not auto-recover)
- Laptop poweroff is verified scheduled: `/run/systemd/shutdown/scheduled` `MODE=poweroff`,
  `USEC=1792360897028587` = 23:01:37 BST 18 Oct 2026, wall message "Owner requested shutdown in
  one hour; 9to1 worker will checkpoint beforehand."
- Coordinator will durably PAUSE this managed worker at 22:56:37 BST.
- **AI PC shutdown: NOT SCHEDULED.** This worker's shell runs on the laptop. Verified read-only:
  `/usr/bin/ssh` exists but `~/.ssh` contains only an empty `authorized_keys` — no config, no
  keys, no known_hosts, therefore no trusted remote management channel to the inference host and
  no way to verify its host identity. The `ai.9to1.uk` gateway is inference-only and blocks
  management. No new endpoint, no auth bypass, no secret exposure was attempted. The owner/root
  must schedule the AI PC poweroff through their own channel.

## Done this cycle (committed, real evidence)
1. **R01 native apphost gate finished — FAIL/BLOCKER recorded, not hidden.**
   `work/pc-worker/evidence/R01/NATIVE-APPHOST-2026-10-18T2037Z.md` + raw artifacts in
   `work/pc-worker/evidence/R01/native-20261018T2030Z/` (16 files).
   - Real refusal visible in the running app for `9to1://boards`: "Browse retained the original
     failed operation: This app does not have a registered native surface in the current host."
     Screenshot `refusal-visible.png` sha256 `b444d805f988ecd04d4d3db6973c98d5c1b4e2e44b1117ff03ca7a0464e6b5a6`.
   - Three normal `WM_DELETE_WINDOW` closes: content torn down (77→1 controls), session files
     written (`settings.json`, `Home/state.json`, `haven.db` + backup, `startup-recovery.json`),
     but the window stayed mapped and the process lived 367s with **no stdout/stderr diagnostic**.
     SIGTERM was used only to clean up my own disposable instance — recorded as cleanup, never as
     a passing shutdown.
   - Therefore: rejected-route-visible PASS; close-exits **FAIL**; same-profile relaunch/restore
     **BLOCKED** (first instance never exited); user-visible shutdown diagnostic **absent**, which
     independently confirms the still-OPEN CUI recovery requirement.
   - Concrete new R01 defect to return to: desktop host does not complete process exit after normal
     close. Candidate causes are listed as candidates only in the evidence file.
2. **Home app selector rebuilt as canonical CUI** (`58d82393`): authored search Input,
   `Repeat Panel="Wrap"` icon tiles bound to registered mode identity/description/`IconKey`,
   distinct empty vs no-match states, one action per tile opening in the same tab. Legacy
   `ListBox` + separate "Open app" button removed from `UnifiedHomePage`.
   - Root cause of the owner-visible "app list is now empty?" confirmed and fixed: **CUIR053** —
     the apps loader had no image-source resolver, so `Image Source="icon:close"` was rejected and
     `BuildAppsView` threw *before* `GetModesAsync`. Not a catalogue-storage emptiness.
   - Repair: `HomeAppsCuiDocument.CreateControlRegistry()` registers the same bounded symbolic
     icon resolver the browser/Spaces surfaces use (`apps/Web/BrowserCanonicalIcons.cs`, now linked
     into `Haven.Desktop`); no file/network schemes, per-loader registry, no global resolver.
   - Reviewer layout defects fixed: explicit Grid `Row`s; `Repeat Panel="Wrap"`; tile spacing via
     `Margin` (CellGap is rejected on a Repeat element).
   - Failure handling fixed: the launcher is built before any heading/viewport change; on failure
     the previous section keeps its own label and the real parser/loader diagnostic is surfaced.
3. New regression fixture `Browse/Host.Tests/HomeAppsLauncherTests.cs` (registered in the csproj)
   loads the exact embedded resource through the production loader + real bindings.

## Native proof of the repaired launcher (done, real product)
`work/pc-worker/evidence/R02-LAUNCHER/NATIVE-LAUNCHER-2026-10-18T2143Z.md` + raw artifacts in
`work/pc-worker/evidence/R02-LAUNCHER/native-r03/` (11 files). PID `2303823`, window `0x180001b`,
profile `/tmp/opencode/r03-apps-20261018T2125Z`.
- The owner-visible "app list is empty?" symptom is disproved on the repaired build: the authored
  launcher rendered **29 real registered tiles** with icons (`launcher-populated.png`,
  sha256 `d00c500ef3f88215fc6af745b9344d9060fd5b23331ab9c490e05dae9e1301e7`), plus the `Search apps`
  entry, `29 apps` count and the honest status line.
- Search narrowed 29 → 2 (`Open Boards`, `Open Canvas`) and revealed `Clear app search`; a
  non-matching query produced 0 tiles with the two-line honest no-match state naming the query;
  clicking clear restored 29 tiles.
- One tile click (`Open Files`) reached `9to1://files` with no failure banner. **The earlier
  "same tab" claim is withdrawn**: independent review of `after-open-files.png`/`inspect-files.json`
  shows two real tabs (the protected non-closable Home tab plus Files) because current code opens a
  new tab when `source.IsHome`. The required New Tab → Apps → app same-tab journey is therefore
  **NOT RUN** and must be measured with actual tab identity/count. The protected Home tab must not
  be removed to satisfy a journey. Review:
  `work/9to1-coordinator/cycle-20261018T213434Z/launcher-tab-identity-review.json`.
- This instance exited ~5s after one normal close.

## Still failing / NOT RUN (do not treat as accepted)
- **R01 third poison path identified with exact real-runtime evidence (fix NOT yet implemented).**
  `work/pc-worker/evidence/R01/shutdown-diag-20261018T2234Z/THIRD-PATH-FINDING.md`. Commit `889fbaba`
  added process-error reporting of the retained shutdown original (previously only `Debug.WriteLine`,
  so the apphost logged nothing at all). On a fresh disposable profile (PID `2318427`,
  `/tmp/opencode/r04-shutdown-diag-20261018T2234Z`) refusing `9to1://boards` and then closing
  normally, the captured cause is `CuiSceneHost.CloseOriginalCoreAsync` → `JoinOriginalTaskAsync` →
  `ThrowOriginalClose` re-joining the CUI loader's retained faulted **action task** and adding the
  typed `NativeAppContentAdmissionRefusal` to the close errors. That is a third path above the two
  already repaired in `BrowseChrome`/`BrowseNativeWorkspace`, and it is why `desktop.TryShutdown()`
  is never reached, the window stays mapped and the process lives (40s, 201s; SIGTERM only as
  recorded cleanup). Designed fix and required negative coverage are written in the finding file:
  a narrow typed CUI "settled action failure" contract honoured by `CuiSceneHost`, satisfied only by
  the non-forgeable admission/lineage proof, never by exception text.
- User-visible CUI recovery experience for a genuine unresolved close obligation (SHUTDOWN-ASSIGNMENT
  item 7 / spec 1791): still absent; only log diagnosability was added.
- Unsaved-draft close-preparation journey on the real UI route: NOT RUN.
- Canonical absorbed-app catalogue ownership: **OPEN**. The launcher now truthfully shows that
  Dashboard and the absorbed Spaces types (Chat, Study, Tasks, Translate) are still registered and
  projected as standalone applications. `HavenAppRoutePolicy.IsDeprecatedStandalone` still excludes
  only `assistants`. Must preserve stored IDs, pins, aliases and compatibility routes.
- Space selection opening a NEW CHAT in that Space with both the icon-only Spaces rail and the
  chat/history sidebar: **OPEN**.
- Measured tab/Space opening latency with before/after numbers: **OPEN**.
- R01 gates: same-profile relaunch/restore **BLOCKED/NOT RUN**; user-visible CUI shutdown recovery
  diagnostic **absent/OPEN**.
- `shared/tests/Haven.Infrastructure.Tests` remains **BLOCKED** (forced x64 testhost from
  WebRtcVadSharp on this linux-arm64 host: "Could not find 'dotnet' host for the 'X64'
  architecture" / "Test Run Aborted", 0 tests executed). `shared/tests/Haven.Desktop.Tests` cannot
  build on Linux (NETSDK1100, net10.0-windows).
- R01 acceptance is **NOT_RUN** and belongs to independent Astra review; this worker never
  self-certifies.

## Untouched / protected
Owner preview PID `1885357`, window `0x140001b`, profile `/tmp/opencode/haven-data-4` was not
read, written, closed or replaced. No merge to main, no owner-preview deployment. Avalonia donor
submodules remain at their pinned gitlinks.
