# R02 native tab roundtrip — Spaces conversation and draft survive another tab (measured)

Worker: single PC implementation worker. Run: 2026-10-19 19:18–19:35 local (18:18–18:35 UTC).
This is **implementation evidence**, not independent acceptance.

## Exact identities

- Branch `opencode/v2-pc-sequential-20261018`, source HEAD `d6716ba5` (canonical-surface route lane
  alignment + Builder address projection fix).
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven`, rebuilt 18:13 UTC
  by the guarded `dotnet test HavenOS.Browse.Host.Tests.csproj -c Debug -m:1` run
  (`0 Error(s)`), so it contains the source under test.
- Disposable profile (mine, isolated, `umask 077`, `0700` XDG roots):
  `/tmp/opencode/r02-tabroundtrip-20261019T1818Z`, `HAVEN_DATA_DIR=$R/haven-data`,
  `DOTNET_ROOT=/home/jacob/.local/share/dotnet`, `DISPLAY=:0`.
- Process `419835`, window `0x140001b`, frame `9to1`, 1440x920 logical, default theme.
- Driver: `work/blank-window/atspi_tool.py` (`inspect`, `click --name … --role button`,
  `set-text … --name … --role entry`) and `work/blank-window/window_tool.py`
  (`close` = `WM_DELETE_WINDOW`, never a signal exit).
- Raw captures are in `native-20261019T1835Z/` beside this file.

## Journey actually performed

1. `Apps → Open Spaces → Open Study` mounted a conversation in the Study Space:
   `9to1://spaces/selected/b1000000-0000-0000-0000-000000000001/chat/576fce50-140f-43b9-bdcf-d42399845e4`.
2. A real composer draft was typed with AT-SPI text input: `Ask 9to1 anything = Roundtrip draft probe`
   (`inspect-t1.json`).
3. `New tab` opened a second tab at `browse://newtab` (tab strip then showed `Spaces` and `New Tab`
   with their own close controls, `inspect-t2.json`).
4. That second tab was navigated to `https://example.com/` through the address field and
   `Go to address`.
5. Clicking the `Spaces` tab strip entry returned to the first tab, which still showed
   **the same conversation address** `…/chat/576fce50-140f-43b9-bdcf-d42399845e4`,
   **the same unsent draft** `Roundtrip draft probe`, the chat surface, the icon-only rail
   (`Create a custom Space`, `Open Chat`, `Open Study`, …) and the contextual sidebar with two
   conversation rows (`inspect-t1-return.json`). Nothing was re-created or lost.
6. `WM_DELETE_WINDOW` closed the two-tab window normally in **3 s**. No signal was used.

## Honest negative finding in the same run

A second form of the roundtrip was attempted first, in the **same** tab: navigate the Spaces tab to
`https://example.com/`, then use `Go back`. The web navigation was accepted and the tab left the
Spaces surface, but `Go back` reported `atspi_error: Element not enabled. (1)` — the back control was
disabled, so the previous app-content route was not offered again. Typing `9to1://spaces` did return
to Spaces Home, but as a **new** navigation: the earlier conversation and its draft were not restored
(`inspect-return.json` showed the Space summary and "No conversations in this Space yet").

This is recorded as an open Haven browser-history question, not as a pass: it is entangled with the
degraded web engine on this platform, which the same capture states truthfully —
"The native Firefox renderer is not connected. Tab identity, bookmarks and browser settings are
retained." and "The Firefox (Gecko) engine is unavailable on Linux. WebKit cannot substitute for
Firefox." Whether an in-tab back step must be retained when the engine refuses a navigation is a
specification decision for the owner; no behaviour was invented or hidden here.

## What this proves and what it does not

This closes the R02 "website-tab roundtrip" gate in its tab-switch form: the Spaces session keeps its
exact conversation identity, unsent draft, rail and contextual sidebar while another tab exists and is
used for web content, and an ordinary close still completes.

It does not prove the in-tab back/forward form (see the finding above), and it does not prove real web
rendering on this host, because the Gecko engine is unavailable here. It satisfies no acceptance
criterion by itself.

## Limits

Real shipping apphost, real profile, real AT-SPI input, default theme at 1440x920. No model request
was issued and no content generated. No owner profile or owner preview instance was touched.
