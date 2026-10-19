# PC implementation worker checkpoint — 2026-10-19 21:58 BST (20:58 UTC)

Source identity: branch `opencode/v2-pc-sequential-20261018`, HEAD `d659a062`. Base main
`06a39628`. Nothing is pushed or merged; the owner preview is not running and no owner profile was
touched.

Session commits, oldest first: `c17761e2` R01 settled-failure contract → `8a989523` R02 catalogue →
`c8bf19de` R02 obsolete tests → `d450f4bf` R02 offline degraded inventory → `146622bc` R04 selector →
`e527da25` S2-DECISIONS registry → `3c4a2d45`/marker removal → `3db9326f` R01 CUI recovery view →
`819abf02` R02 queued-open failure reporting → `3525b860` R02 return-chat identity → `d8c6be77` R02
direct Space entry inside the retained rail → `6515199b` R02 native entry evidence → `71661cf3` R02
contextual sidebar → `37ff0b4d` R02 sidebar thread-affinity fix + identity tests → `7c2f27e0` R02
sidebar native evidence → `da581cf9` R01 draft-close evidence → `e7b39567` worker checkpoint →
`d6716ba5` canonical-surface route lane + Builder address projection → evidence commits →
`60c6a042` R02 sidebar switcher/overflow/collapse → `c51205fe` worker checkpoint → `aec18695` R02
authored custom-sidebar element area (read path pending) → `a1260fac` checkpoint for the unreproduced
intermittent lane failure → `e8d3cf30` DEC-014 open decision → `d659a062` refused Space UI action is
reported in-surface instead of retained as a close obligation.

## Ledger state

`current_milestone_id: R02`, `in_progress` / `NOT_RUN`. Ledger checkpoints this session: R01
`checkpoint-2026-10-19T1020Z`, R02 `1040Z`, R02 `1715Z`, R02 `1745Z`, R02 `1842Z`
(`record_sha256 c0c37ed5…`), R02 `1850Z` (`record_sha256 dc6e7729…`). The R01 record
`work/pc-worker/evidence/R01/checkpoint-2026-10-19T1830Z.json` is committed but the ledger rejected it
("Record must name the exact current milestone") because it keeps one current slice and R02 is
current; the coordinator can replay it after R02 is finished or blocked. Do not hand-edit
`MILESTONE-STATE.json`.

## Implemented and verified natively this session (implementation evidence, not acceptance)

R01 — shutdown after rejected native admission:
- Real apphost: `9to1://boards` refusal → one `WM_DELETE_WINDOW` → exit in 1 s (was 40 s+); relaunch
  on the same disposable profile restored the window and exited in 2 s.
- Unsaved-draft close preparation on the real route (`evidence/R01/NATIVE-DRAFT-CLOSE-…`): a dirty
  Space Builder editor makes close refuse rather than discard; the app shows a real Save / Discard /
  Cancel decision with "Unsaved changes."; Cancel restores the identical draft with Back/Save/Discard/
  Reload usable and the process alive; Save exits in 1 s and the saved name survives relaunch;
  Discard exits in 2 s and the discarded name is genuinely absent after relaunch; a clean close exits
  in 2 s.

R02 — Spaces entry, catalogue and both sidebars:
- One click on an icon-only rail entry opens a conversation scoped to that Space directly, mounted in
  the authored `space-app-content` region so the rail stays visible (`NATIVE-SPACES-ENTRY-…`).
- Authored `space-context-sidebar` presents the selected Space, one New chat action and that Space's
  own conversations. New chat creates a real second conversation; each row opens its own stored
  identity and restores that conversation's unsent draft (`NATIVE-SPACES-SIDEBAR-…`,
  `NATIVE-SIDEBAR-ROWS-…`).
- Canonical sidebar controls required by spec 4763–4866 now exist and work on the real surface: the
  header control displays the selected Space and opens a Space switcher (switching Spaces selects and
  opens, never a details interstitial), an adjacent overflow offers Manage / Rename / Archive for the
  selection, and the sidebar collapses with one explicit rail expand control while the conversation
  and address stay (`NATIVE-SIDEBAR-CONTROLS-…`).
- Tab roundtrip: with a Study Space conversation and a typed draft, a second tab was opened and
  navigated to `https://example.com/`; returning to the Spaces tab restored the same conversation
  address, the same unsent draft, the rail and the sidebar; the two-tab window closed in 3 s
  (`NATIVE-TAB-ROUNDTRIP-…`).
- Catalogue projects 24 apps natively with Dashboard/Study/Tasks/Translate/Assistants/Dev absorbed.

Real defects found and fixed by native passes (headless lanes were green before them):
1. Sidebar commands used `ConfigureAwait(false)` and resumed off the UI dispatcher, so the chat
   factory's `VerifyAccess` threw; the UI showed the generic CUI action failure and that instance then
   could not complete an ordinary close (still alive after 25 s; terminated as cleanup, not counted as
   a shutdown result). Fixed in `37ff0b4d`.
2. `UpdateAddress` ignored the published Builder route, so a later state change rewrote the tab
   address to the selected-Space form while the canonical Space editor was presented. Fixed in
   `d6716ba5`.
 3. A refused Space UI action made the tab permanently unclosable. The refusal was admitted as
    original work, joined at close and re-raised as a close cause, so every later close threw
    `AggregateException("The original Spaces work did not retire cleanly.")` — in the real app the
    instance simply never completed a close. Reproduced headlessly on the real session route and
    fixed in `d659a062`: an action refused before it can change anything (missing/archived
    conversation, unavailable/archived Space) now raises `SpacesWorkspaceActionRefusal`, which the
    owning session reports through the Spaces Home status line and completes normally. Genuine
    failures, work still running when close begins, and every save/close-preparation obligation keep
    their existing close semantics.

Test-lane alignment (expectations updated to enforce the specification, coverage preserved):
- 4 `UnifiedHomeContentTests` expected a donor `ListBox`; they now assert the authored canonical CUI
  (`home-app-tile` projection, `home-apps-results` region) with the same catalogue rule.
- 5 `SpacesDetailsRouteTests` expected Home "manage" to publish a separate `SpaceDetailsPage`; they
  now assert the canonical `SpaceBuilderPage` (SPUI-201) and keep their close-join, lease-fencing,
  visible-decision and draft-retention coverage.

## Test lanes (guarded `/home/jacob/.local/bin/dotnet`, `-m:1`, serialized)

- `HavenOS.Browse.Host.Tests` **255 passed / 3 failed** (258 total, at `d659a062`; 254 before the new
  close test). The 3 are the explicit platform guard "This owning host fixture requires actual Windows
  Home; Linux is unvalidated" — recorded as BLOCKED on this host, never as a pass.
- Flakiness observed and not hidden: one run at 20:36 UTC reported **253 passed / 4 failed**, i.e. one
  extra failure beyond the three platform guards. The failure name was not captured because the
  summary was grepped alone. Three subsequent runs (two `--no-build`, one with build) each returned
  254/3, so it did not reproduce. A reviewer should treat this lane as having one unreproduced
  intermittent failure to identify, not as deterministically green.
- `HavenOS.Spaces.Tests` 220/220 · `CakeOS.Cui.Runtime.Tests` 405/405 · canonical CUI compilation
  13/13 · `HavenOS.Apps.Browse.Tests` 150/150 · `Haven.Desktop` build 0 errors.
- `shared/tests/Haven.Desktop.Tests` BLOCKED: `NETSDK1100` (`net10.0-windows10.0.19041.0` cannot build
  on this OS). `EnableWindowsTargeting` was not enabled: cross-building a Windows test host on Linux
  would not exercise the Windows behaviour.
- `Haven.Infrastructure.Tests` BLOCKED: WebRtcVadSharp forces an x64 testhost on linux-arm64 →
  "Could not find 'dotnet' host for the 'X64' architecture".

## Open gates (do not treat as done)

- R02: the main sidebar is not yet "customisable through stable section/item/rule identities". The
  authored area, stable identity projection and command mapping exist (`aec18695`) but the markup
  renders nothing today, so no native journey is claimed for it. The remaining read path is **held for
  an owner response to DEC-014** (`docs/specification/open-decisions.json`): an
  `IBrowseNativeAppContentSession` has no host contract to navigate its own tab to an arbitrary
  address or open another app surface, and no reviewed service resolves
  `ExtensionSidebarTargetKind` to an authorised target. The identity and per-Space visibility sources
  are known (`SpaceCapabilityBinding(CapabilityId = Manifest.PackageId, Kind = Plugin, Override)` →
  `InstalledExtensionPackage.Manifest.SidebarContributions`, `ExtensionManager` singleton), but wiring
  them without an activation contract would ship visible rows that cannot do anything, so that change
  is held rather than invented around. The in-tab `Go back` control was
  disabled after a refused web navigation, and re-typing `9to1://spaces` starts a fresh Spaces Home
  rather than restoring the previous conversation — recorded as an open Haven browser-history question
  entangled with the unavailable Gecko engine. Offline model-inventory native entry was not driven
  this pass (the degraded "Local models are unavailable" state was observed on the real surface).
- R01: failing-save / failing-close recovery-state probes on the real route are still NOT RUN. The
  probe that a faulted UI action cannot permanently block an ordinary close is now covered headlessly
  on the real session route (`A_faulted_workspace_command_still_allows_an_ordinary_close`) and the
  real app's ordinary close with a Spaces conversation mounted was re-measured
  (`evidence/R02/NATIVE-CLOSE-REGRESSION-2026-10-19T2057Z.md`, exit ≤4 s); a native run of the actual
  refusal has not been driven because the disposable profile had no stale row to click.
- F01 authority/capability-manifest/permission foundation (spec 1–1076; only 1–560 read) is still
  unimplemented.
- Automations scheduling/persistence/execution backends, Build/Dev, Write/Present/Boards/Canvas and
  Files/accounts/cloud storage have not been started this session.

## Next executable actions

1. Custom sidebar elements stay held until DEC-014 is answered. Once the owner names the activation
   contract, wire `spaces.sidebarItems` from the attached-extension projection in Haven.Desktop,
   implement `spaces.open-sidebar-item` for each declared `ExtensionSidebarTargetKind`, add the
   per-Space visibility selection and run the native journey with a real attached extension.
2. Add the remaining R01 negative probes on the real route: a failing save retains recovery state, and
   a failing close leaves the canonical failure surface without wedging the tab.
3. Then move to the Automations backend slice (owner priority 3): genuine scheduling, persistence and
   execution, as bounded commits.
4. Keep refreshing this file plus the R01/R02 evidence so Astra can review independently. The R02
   native evidence set for review is now `NATIVE-SPACES-ENTRY-…1715Z`, `NATIVE-SPACES-SIDEBAR-…1741Z`,
   `NATIVE-SIDEBAR-ROWS-…1831Z`, `NATIVE-TAB-ROUNDTRIP-…1835Z`,
   `NATIVE-SIDEBAR-CONTROLS-…1843Z` and `NATIVE-CLOSE-REGRESSION-…2057Z`.

## Environment notes for the next session

- Native runs need `DOTNET_ROOT=/home/jacob/.local/share/dotnet`, `DISPLAY=:0`,
  `XAUTHORITY=/run/user/1000/.mutter-Xwaylandauth.0539W3`, and `setsid` (plain `nohup &` is reaped
  when the tool call returns). Find the real PID with
  `ps -eo pid,ppid,args | grep bin/x64/Debug/net10.0/Haven` (ppid 1652), not `pgrep -f`.
- `atspi_tool.py click` now supports `--index N` for repeated authored rows (recorded as
  `matched_count`/`clicked_index`); without it the strict single-match refusal still applies.
- Rebuilding the apphost while an instance runs makes `window_tool.py` refuse
  ("...Haven (deleted)"); close the instance before rebuilding.
- Avalonia donor submodules must stay at their pinned gitlinks (`XamlX` `009d4815…`,
  `Avalonia.Dbus` `864a0528…`) or ~1520 CS0246 appear.
- CUI lessons: an `<If>` host does not receive `ApplyProperties`, so `Column`/`Row` on `<If>` is
  ignored; `ColumnSizes` accepts a binding and is a supported dynamic property.
