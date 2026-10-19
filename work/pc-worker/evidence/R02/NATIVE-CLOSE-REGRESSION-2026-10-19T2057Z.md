# R01/R02 native regression after the refused-action close fix

Source: `d659a062` on `opencode/v2-pc-sequential-20261018` (base main `06a39628`).
Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven`,
rebuilt at 20:53Z with `/home/jacob/.local/bin/dotnet build -c Debug -m:1`
→ 0 errors, 1 warning. No pre-existing Haven instance was running.

Platform: Linux (arm64 host), X11/XWayland `DISPLAY=:0`, window `0x140001b`,
PID 635628. Isolated disposable profile only:
`work/pc-worker/evidence/R02/native-20261019T205356Z` (`0700`, `umask 077`) with
`HAVEN_DATA_DIR`, `XDG_CONFIG_HOME`, `XDG_CACHE_HOME`, `XDG_STATE_HOME` inside it.
Nothing in the owner's real profile was read or written.

## Why this run exists

Commit `d659a062` changes how a refused Space UI action is retained, which sits on
the path that decides whether an ordinary close can complete. The defect being
fixed was only ever observed in the real app, so the fix has to be shown not to
disturb the real app's normal Spaces entry and close behaviour.

## Journey actually driven (AT-SPI, real controls)

1. Launched the app; Home presented with the authored app catalogue.
2. Typed `9to1://spaces` into `Search or enter web address` and activated the
   address control; the Home page was still presented, so the journey continued
   through the real launcher control `Open Spaces` rather than the address bar.
3. Spaces Home presented with the icon-only Spaces rail (`Spaces` button,
   `Close Spaces`) **and** the contextual sidebar in the same tab:
   `Switch Space`, `Quick actions for this Space`,
   `Start a new chat in this Space`, the conversation list area, plus the
   Space management controls (`Rename selected Space`,
   `Make a copy of selected Space`, `Open selected Space`,
   `Archive selected Space`, `Edit Space extensions`, parent-Space controls,
   `Add a source to this Space`). Status line read `Spaces ready`.
4. Clicked `Open Chat`. The Chat Space conversation mounted **inside** the
   retained Spaces layout: rail controls 41/43/47 stayed present, the sidebar
   showed the Space's stored row `Open conversation New chat`, and the composer
   (`Ask 9to1 anything`, `Send message`) became available. 66 controls before,
   66 after; nothing in the rail or sidebar was replaced.
5. Typed an unsent draft `close-regression-draft-2056Z` into the composer.
6. Requested an ordinary window close through the real window manager close.

## Measured result

- The close was **not refused**: no save/discard decision surface appeared for the
  chat draft, because the chat draft is persisted by the conversation store rather
  than being a close obligation. (The unsaved-work decision surface is separately
  proven for the Space Builder editor in
  `evidence/R01/NATIVE-DRAFT-CLOSE-2026-10-19T1822Z.md`.)
- `window_tool.py close` returned `completed: true`, and 4 seconds later
  `ps -p 635628` reported no such process. So the real app reached exit within
  ≤4s of the close request with a Spaces conversation mounted. No signal was
  sent and no exit was relabelled: the process was simply absent.

## Headless companion proof (same source)

`HavenOS.Browse.Host.Tests` → `SpacesConversationRouteTests.
A_faulted_workspace_command_still_allows_an_ordinary_close` reproduces the defect
on the real session route: before the fix the close threw
`AggregateException("The original Spaces work did not retire cleanly.")` with two
retained copies of the refusal, i.e. the tab could never be closed after one
failed sidebar click. After the fix the refusal is reported in the sidebar status
line (`Assert.Contains("no longer available", …StatusMessage)`) and the same close
completes with `CanRetireOriginalView` true and the root emptied.

Lane totals at `d659a062`:

- `HavenOS.Browse.Host.Tests`: **255 passed / 3 failed** (258 total). The three
  failures are the pre-existing `UnifiedWorkspaceHostLifetimeTests` guard whose
  own message is "This owning host fixture requires actual Windows Home; Linux is
  unvalidated". They are NOT RUN on this platform, not regressions.
- `HavenOS.Spaces.Tests`: **220 passed / 0 failed**.

## What this does NOT prove

- It does not re-prove the R01 shutdown requirements (cold start, refusal, save,
  discard) — those remain in the R01 evidence files.
- The native run could not itself trigger a *refused* Space action, because the
  disposable profile had no stale conversation row to click. The refusal path is
  therefore proven headlessly on the real route only, and marked as such.
- R01 negative probes still NOT RUN: a failing save that retains recovery state,
  and a dedicated probe that a faulted UI action can never permanently block an
  ordinary close in the real app (the headless test now covers the second one).
- Acceptance remains **NOT_RUN**; this is implementation smoke evidence.
