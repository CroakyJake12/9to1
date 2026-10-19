# Acceptance handoff — R01 (shutdown) and R02 (Spaces entry / sidebars / catalogue)

Prepared for independent acceptance (Astra). This is an implementation handoff. It does **not**
certify any requirement: acceptance status for both milestones is **NOT_RUN** until independently
verified against the specification's seven criteria.

## Source identity to review

- Branch `opencode/v2-pc-sequential-20261018`, HEAD `00b6761f` (implementation fix commit `d659a062`).
- Base main `06a39628`. Nothing is pushed or merged; the owner preview is untouched.
- Specification authority: `docs/specification/9to1-development-specification-v2.md`,
  SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`, plus the direct owner
  refinement `docs/specification/owner-references/2026-10-18-haven-spaces/owner-correction.json`.
- Ledger: `current_milestone_id: R02`, `in_progress`, `acceptance_status: NOT_RUN`. Latest checkpoint
  `work/pc-worker/evidence/R02/checkpoint-2026-10-19T2058Z.json`
  (`record_sha256 353ca0d4b1751125c7beacbdb581d723166390dfe45e72c64dcb52556ca64e51`).

## R02 — what to test, and the evidence offered

Owner journey: selecting a Space opens a **new chat in that Space directly**, with **both** the
icon-only Spaces rail and that Space's chat/history sidebar retained; details/sources remain
deliberate manage operations.

| Requirement area | Native evidence (real apphost, AT-SPI) |
| --- | --- |
| One rail click → Space-scoped chat mounted inside the retained rail layout | `work/pc-worker/evidence/R02/NATIVE-SPACES-ENTRY-2026-10-19T1715Z.md` |
| Contextual sidebar: selected Space, one New chat, that Space's own conversations | `…/NATIVE-SPACES-SIDEBAR-2026-10-19T1741Z.md` |
| Each sidebar row opens its own stored identity and restores its unsent draft | `…/NATIVE-SIDEBAR-ROWS-2026-10-19T1831Z.md` |
| Header Space switcher, adjacent quick-action overflow, reversible collapse with one rail expand control | `…/NATIVE-SIDEBAR-CONTROLS-2026-10-19T1843Z.md` |
| Tab roundtrip keeps conversation `576fce50-…` and its draft while another tab holds web content | `…/NATIVE-TAB-ROUNDTRIP-2026-10-19T1835Z.md` |
| Ordinary close after the refused-action fix, with a Spaces conversation mounted (exit ≤4 s) | `…/NATIVE-CLOSE-REGRESSION-2026-10-19T2057Z.md` |

Headless route coverage lives in `9to1 Workspace/Browse/Host.Tests/`
(`SpacesConversationRouteTests`, `SpacesDetailsRouteTests`) and
`9to1 Workspace/Spaces/Tests/SpacesHomeCuiRenderingTests.cs`.

Catalogue: Home Apps projects **24 apps** natively with Dashboard absorbed into Haven and
Study/Tasks/Assistants/Research/Shopping/Translate absorbed as Space types and Dev into Build. Stored
IDs, pins, recents, grants, saved routes and compatibility aliases are preserved — the projection
hides nothing by deleting data.

### R02 is NOT complete — explicit open items for the reviewer

1. **Sidebar customisation through stable section/item/rule identities is only partly implemented.**
   The authored `space-sidebar-items` area, the stable `SpacesHomeSidebarRow` projection keyed by
   `ExtensionSidebarContribution.SidebarItemId` and the `OpenSidebarItem` command mapping exist
   (`aec18695`), and the domain surface truthfully reports no items so **nothing renders today**. The
   remaining read path is **held pending an owner answer to DEC-014**
   (`docs/specification/open-decisions.json`): an app-content session has no host contract to navigate
   its own tab to an arbitrary address or open another app surface, and no reviewed service resolves
   `ExtensionSidebarTargetKind`. Do not accept this requirement; do not accept a build that ships
   disabled placeholder rows as if it satisfied it.
2. In-tab `Go back` was observed **disabled** after a refused web navigation, and re-typing
   `9to1://spaces` starts a fresh Spaces Home instead of restoring the prior conversation. Recorded as
   an open Haven browser-history question entangled with the unavailable Gecko engine on Linux.
3. Offline model-inventory native entry was not re-driven this pass; the degraded "Local models are
   unavailable" state was observed on the real surface.

## R01 — what to test, and the evidence offered

- `work/pc-worker/evidence/R01/NATIVE-DRAFT-CLOSE-2026-10-19T1822Z.md`: the unsaved-draft close
  preparation on the real route. A dirty Space Builder editor makes close **refuse** rather than
  discard; a real Save / Discard / Cancel decision appears with "Unsaved changes."; Cancel restores the
  identical draft with the editor commands usable and the process alive; Save exits in 1 s and persists
  across relaunch; Discard exits in 2 s and genuinely does not persist; a clean close exits in 2 s.
- Refusal → close → exit measured at 1 s, relaunch exit 2 s, on the real apphost after the rejected
  native admission (`9to1://boards`).

### R01 open items

- A **native** run of a *failing* save is NOT RUN. The recovery-state guarantee is covered headlessly
  on the real route by
  `SpacesDetailsRouteTests.Actual_second_editor_preparation_fault_restores_first_lease_and_retains_the_source_in_failed_session_close`.
- A native run of the refused-UI-action close is NOT RUN; it is reproduced and fixed headlessly
  (`A_faulted_workspace_command_still_allows_an_ordinary_close`). Before the fix the refusal was
  retained and re-raised at close, so the tab could never close.

## Lane state at `d659a062` (guarded `/home/jacob/.local/bin/dotnet`, `-m:1`, serialized)

- `HavenOS.Spaces.Tests` 220/220 PASS · `CakeOS.Cui.Runtime.Tests` 405/405 PASS ·
  canonical CUI compilation 13/13 PASS · `HavenOS.Apps.Browse.Tests` 150/150 PASS ·
  `Haven.Desktop` build 0 errors PASS.
- `HavenOS.Browse.Host.Tests` **255 passed / 3 failed**, non-zero exit. The 3 failures are the
  pre-existing `UnifiedWorkspaceHostLifetimeTests` guard whose own text is "This owning host fixture
  requires actual Windows Home; Linux is unvalidated" — treat as **BLOCKED on this host**, never as a
  pass.
- One earlier run at 20:36Z reported **253/4** with the extra failure name not captured; three later
  runs and this one returned the 3-failure baseline. Treat the lane as carrying **one unreproduced
  intermittent failure to identify**, not as deterministically green.
- `shared/tests/Haven.Desktop.Tests` **BLOCKED**: `NETSDK1100`
  (`net10.0-windows10.0.19041.0` cannot build on this OS). `EnableWindowsTargeting` was deliberately
  not enabled because cross-building a Windows test host on Linux would not exercise Windows behaviour.
- `Haven.Infrastructure.Tests` **BLOCKED**: WebRtcVadSharp forces an x64 testhost on linux-arm64 →
  "Could not find 'dotnet' host for the 'X64' architecture".

## How to repeat the native journey independently

Build the apphost, then launch it with an isolated disposable profile (`0700`, `umask 077`) — never
the owner profile:

```
setsid env DISPLAY=:0 XAUTHORITY=/run/user/1000/.mutter-Xwaylandauth.0539W3 \
  DOTNET_ROOT=/home/jacob/.local/share/dotnet \
  HAVEN_DATA_DIR=$RUN/data XDG_CONFIG_HOME=$RUN/config \
  XDG_CACHE_HOME=$RUN/cache XDG_STATE_HOME=$RUN/state \
  "9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven" > "$RUN/app.log" 2>&1 < /dev/null &
```

Drive it with `/home/jacob/Documents/Codex/2026-10-17/new-chat/work/blank-window/atspi_tool.py`
(`--pid --exe --window`, roles are `button`/`entry`, `click --index N` for repeated authored rows) and
`window_tool.py` (`list`/`close`/`capture`). Find the real PID with
`ps -eo pid,ppid,args | grep bin/x64/Debug/net10.0/Haven`, not `pgrep -f`. Close the running instance
before rebuilding, or `window_tool.py` refuses with a `(deleted)` executable.

## Not in scope of this handoff

F01 authority/capability-manifest/permission foundations, Automations, Build/Dev,
Write/Present/Boards/Canvas and Files/accounts/cloud storage are not claimed here. The Automations
backend map for the next slice is recorded in `work/pc-worker/CHECKPOINT.md`.
