# PC implementation worker checkpoint — 2026-10-19 19:35 BST (18:35 UTC)

Source identity: branch `opencode/v2-pc-sequential-20261018`, HEAD `da581cf9` (R01 native evidence)
with source HEAD `37ff0b4d`. Base main `06a39628`. Nothing is pushed or merged; the owner preview is
not running and no owner profile was touched.

Session commits, oldest first: `c17761e2` R01 settled-failure contract → `8a989523` R02 catalogue →
`c8bf19de` R02 obsolete tests → `d450f4bf` R02 offline degraded inventory → `146622bc` R04 selector →
`e527da25` S2-DECISIONS registry → `3c4a2d45`/marker removal → `3db9326f` R01 CUI recovery view →
`819abf02` R02 queued-open failure reporting → `3525b860` R02 return-chat identity →
`d8c6be77` R02 direct Space entry inside the retained rail → `6515199b` R02 native entry evidence →
`71661cf3` R02 contextual sidebar → `37ff0b4d` R02 sidebar thread-affinity fix + identity tests →
`7c2f27e0` R02 sidebar native evidence → `da581cf9` R01 draft-close evidence.

## Ledger state

`current_milestone_id: R02`. R01, R02 `in_progress` / `NOT_RUN`; R03, R04 `implemented` /
`PENDING_REVIEW`. Ledger checkpoints recorded this session: R01 `checkpoint-2026-10-19T1020Z`,
R02 `checkpoint-2026-10-19T1040Z`, R02 `checkpoint-2026-10-19T1715Z`,
R02 `checkpoint-2026-10-19T1745Z`. The R01 record `work/pc-worker/evidence/R01/checkpoint-2026-10-19T1830Z.json`
is committed but the ledger rejected it ("Record must name the exact current milestone") because the
ledger keeps one current slice and R02 is current; the coordinator can replay it after R02 is
finished or blocked. Do not hand-edit `MILESTONE-STATE.json`.

## What is actually implemented and verified this session

R01 (shutdown after rejected native admission):
- Real apphost: `9to1://boards` refusal → one `WM_DELETE_WINDOW` → process exited in 1 s (was 40 s+);
  relaunch on the same disposable profile restored the window and exited in 2 s.
- Unsaved-draft close-preparation journey now run on the real UI route
  (`evidence/R01/NATIVE-DRAFT-CLOSE-2026-10-19T1822Z.md`): a dirty Space Builder editor makes close
  refuse rather than discard, the app shows a real Save / Discard / Cancel decision with
  "Unsaved changes."; Cancel restores the identical draft with Back/Save/Discard/Reload usable and
  the process alive; Save exits in 1 s and the saved name is still there after relaunch; Discard
  exits in 2 s and the discarded name is genuinely absent after relaunch; a clean close exits in 2 s.

R02 (Spaces entry, catalogue, sidebars):
- One click on an icon-only rail entry opens a conversation scoped to that Space directly — no
  details/sources interstitial. Every canonical Space type (Chat, Study, Tasks, Assistants,
  Shopping, Research, Translate, custom) opens a Space-scoped conversation; the old Chat-only
  restriction is gone because those are Space types, not apps.
- The conversation is mounted in the authored `space-app-content` region of `SpacesHome.cui`, so the
  icon-only rail stays visible; the Space summary returns when no workspace is mounted.
- Authored `space-context-sidebar` (72 260 1fr) presents the selected Space's name, one New chat
  action and that Space's own recent conversations with stable conversation identities.
- Rail `Manage selected Space` keeps management a deliberate operation; management roundtrips restore
  the same chat identity; a failed queued open detaches the stale workspace and reports against the
  requested Space.
- Catalogue now projects 24 apps natively with Dashboard/Study/Tasks/Translate/Assistants/Dev
  absorbed (verified in the real app, `inspect-1.json` of the R02 entry evidence).
- Native proof: `evidence/R02/NATIVE-SPACES-ENTRY-2026-10-19T1715Z.md` and
  `evidence/R02/NATIVE-SPACES-SIDEBAR-2026-10-19T1741Z.md`.

Real-app-only defect found and fixed: the sidebar commands used `ConfigureAwait(false)` and resumed
off the UI dispatcher, so `OverlayChatSessionFactory.CreatePageAsync` threw
"The calling thread cannot access this object because a different thread owns it." The UI showed the
generic CUI action failure, and that instance then could not complete an ordinary close (still alive
after 25 s; terminated as cleanup, not counted as a shutdown result). Fixed in `37ff0b4d`; the
repaired build completes the same journey and closes in 3 s.

## Test lanes (guarded `/home/jacob/.local/bin/dotnet`, `-m:1`, serialized)

- `HavenOS.Spaces.Tests` 218/218 PASS.
- `CakeOS.Cui.Runtime.Tests` 405/405 PASS; canonical CUI compilation 13/13 PASS.
- `HavenOS.Apps.Browse.Tests` 150/150 PASS (includes BrowseAppContentTests, BrowseClosePreparationTests).
- `HavenOS.Browse.Host.Tests` 245 passed / 12 failed. The same 12 fail at HEAD `3525b860` with this
  work stashed, so nothing here is a new regression:
  - 5 × `SpacesDetailsRouteTests`: stale expectations that Home "Edit details" publishes
    `SpaceDetailsPage`, while the reviewed routing since `3dc468cd` publishes the canonical
    `SpaceBuilderPage`. Must be rewritten against the Builder surface, not deleted.
  - 4 × `UnifiedHomeContentTests`: `Assert.Single()` on an empty collection in the Browse-hosted Home
    sidebar/catalogue rendering. Not yet diagnosed.
  - 3 × `UnifiedWorkspaceHostLifetimeTests`: explicit platform guard "This owning host fixture
    requires actual Windows Home; Linux is unvalidated" — recorded as BLOCKED on this host.
- `shared/tests/Haven.Desktop.Tests` BLOCKED: `NETSDK1100` — it targets
  `net10.0-windows10.0.19041.0` and cannot build on this OS. `NativeAppContentRegistryTests` and
  `DesktopOriginalShutdownSequenceTests` are therefore NOT RUN here. `EnableWindowsTargeting` was
  not enabled: cross-building a Windows test host on Linux would not exercise the Windows behaviour.
- `Haven.Infrastructure.Tests` BLOCKED: WebRtcVadSharp forces an x64 testhost on linux-arm64 →
  "Could not find 'dotnet' host for the 'X64' architecture".

## Open gates (do not treat as done)

- R01: failing-save / failing-close recovery-state probes on the real route; a dedicated probe that a
  faulted UI action can never permanently block an ordinary close.
- R02: native sidebar **row** click (fresh conversations share the default title "New chat", so the
  AT-SPI name is ambiguous — identity is proven headlessly only); website-tab roundtrip with the same
  draft; the 5 stale `SpacesDetailsRouteTests` expectations.
- F01 authority/capability-manifest/permission foundation (spec 1–1076; only 1–560 read) is still
  unimplemented.
- Automations scheduling/persistence/execution backends, Build/Dev, Write/Present/Boards/Canvas and
  Files/accounts/cloud storage have not been started this session.

## Next executable actions

1. Diagnose the 4 `UnifiedHomeContentTests` failures (Browse-hosted Home sidebar) — Haven priority 1.
2. Rewrite the 5 `SpacesDetailsRouteTests` expectations against `SpaceBuilderPage`, preserving their
   close-join and draft-retention coverage.
3. Add the R01 negative probes: failing save retains recovery state; a faulted UI action still allows
   an ordinary close.
4. Give new conversations distinct default titles so the native sidebar row journey is drivable, then
   run that native journey.
5. Then start the Automations backend slice (owner priority 3) as bounded commits.

## Environment notes for the next session

- Native runs need `DOTNET_ROOT=/home/jacob/.local/share/dotnet`, `DISPLAY=:0`,
  `XAUTHORITY=/run/user/1000/.mutter-Xwaylandauth.0539W3`, and `setsid` (plain `nohup &` gets
  reaped when the tool call returns). Find the real PID with
  `ps -eo pid,ppid,args | grep bin/x64/Debug/net10.0/Haven` (ppid 1652), not `pgrep -f`.
- Rebuilding the apphost while an instance runs makes `window_tool.py` refuse
  ("...Haven (deleted)"); close the instance before rebuilding.
- Avalonia donor submodules must stay at their pinned gitlinks
  (`XamlX` `009d4815…`, `Avalonia.Dbus` `864a0528…`) or ~1520 CS0246 appear.
