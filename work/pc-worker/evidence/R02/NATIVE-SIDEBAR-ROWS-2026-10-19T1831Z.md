# R02 native sidebar row gate — each history row opens its own stored conversation (measured)

Worker: single PC implementation worker. Run: 2026-10-19 19:24–19:31 local (18:24–18:31 UTC).
This is **implementation evidence**, not independent acceptance.

## Exact identities

- Branch `opencode/v2-pc-sequential-20261018`, source HEAD `d6716ba5`.
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven` (rebuilt 18:13 UTC
  by the guarded Browse-host test run, `0 Error(s)`), so it contains the source under test.
- Disposable profile (mine, isolated, `umask 077`, `0700` XDG roots):
  `/tmp/opencode/r02-rowclick-20261019T1850Z`, `DOTNET_ROOT=/home/jacob/.local/share/dotnet`,
  `DISPLAY=:0`.
- Process `434403`, window `0x140001b`, frame `9to1`, 1440x920 logical, default theme.
- Driver: `work/blank-window/atspi_tool.py` and `work/blank-window/window_tool.py`
  (`close` = `WM_DELETE_WINDOW`, never a signal exit). Raw captures are in
  `native-20261019T1900Z/` beside this file.

## Driver capability added (harness only, not product)

Every fresh conversation currently carries the store's default title "New chat", so the two authored
history rows share one accessible name and the previous strict single-match rule refused them.
`atspi_tool.py click` now accepts an explicit `--index N` (recorded in the result as
`matched_count` and `clicked_index`); omitting it keeps the old ambiguity refusal. No product code or
product titling was changed to make this journey drivable.

## Journey actually performed

1. `Apps → Open Spaces → Open Study` opened conversation
   `bf7d845f-6e34-40c5-bd01-eaafe67db97a` in Space `b1000000-0000-0000-0000-000000000001`.
2. A composer draft was typed with real AT-SPI text input
   (`Ask 9to1 anything = Sidebar row probe conversation`) and `Send message` was clicked. The send did
   **not** complete because no model is available on this host; the chat states this truthfully
   ("Local models are unavailable. Connect a configured model provider, then try again.") and the
   conversation keeps its default title. No fake success was claimed.
3. `Start a new chat in this Space` created a second real conversation
   `f3eb18d7-5cbf-442b-8198-ce38d20b476d` in the same Space; the sidebar then presented exactly two
   rows (`inspect-two.json`), newest first.
4. `click --name "Open conversation New chat" --index 0` → accepted, 2 matches, index 0 recorded; the
   address stayed on `…/chat/f3eb18d7-…` and no action failure was shown (`inspect-row0.json`).
5. `click … --index 1` → the address became
   `9to1://spaces/selected/b1000000-0000-0000-0000-000000000001/chat/bf7d845f-6e34-40c5-bd01-eaafe67db97a`,
   i.e. the **other** stored conversation identity, with no failure text and the icon-only rail
   (`Create a custom Space`, `Open Chat`, `Open Study`, …) still present (`inspect-row1.json`).
6. Reopening that row also restored its **unsent draft**: the composer read back
   `Ask 9to1 anything = Sidebar row probe conversation`. The row therefore re-opens the stored
   conversation and its pending input, rather than starting a new one.
7. `WM_DELETE_WINDOW` closed the window normally in **2 s**. No signal was used.

## What this proves and what it does not

This closes the last open native sub-item for the contextual sidebar: a history row opens its own
stored conversation identity inside the retained rail layout, and the draft belongs to that
conversation. Combined with `NATIVE-SPACES-ENTRY-2026-10-19T1715Z.md` (direct entry from one rail
click) and `NATIVE-SPACES-SIDEBAR-2026-10-19T1741Z.md` (New chat creates a real second conversation),
the owner's Spaces entry/navigation refinement is now exercised on the real surface.

It does not prove model-backed conversation content, because no model is available on this host, and
the send attempt above is recorded as a degraded-path observation, not a passing send. It satisfies no
acceptance criterion by itself.

## Limits

Real shipping apphost, real profile, real AT-SPI input, default theme at 1440x920, Study Space only.
No owner profile or owner preview instance was touched.
