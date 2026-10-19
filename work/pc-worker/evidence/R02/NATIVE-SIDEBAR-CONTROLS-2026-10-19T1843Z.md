# R02 native sidebar controls — Space switcher, quick-action overflow and collapse (measured)

Worker: single PC implementation worker. Run: 2026-10-19 19:37–19:43 local (18:37–18:43 UTC).
This is **implementation evidence**, not independent acceptance.

## Exact identities

- Branch `opencode/v2-pc-sequential-20261018`, source HEAD `60c6a042` (the control slice itself).
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven`, rebuilt by the
  guarded `dotnet test HavenOS.Browse.Host.Tests.csproj -c Debug -m:1` run at 19:33 local, so it
  contains the source under test.
- Disposable profile (mine, isolated, `umask 077`, `0700` XDG roots):
  `/tmp/opencode/r02-sidebar-controls-20261019T1937Z`, `DOTNET_ROOT=/home/jacob/.local/share/dotnet`,
  `DISPLAY=:0`.
- Process `464239`, window `0x140001b`, frame `9to1`, 1440x920 logical, default theme.
- Driver: `work/blank-window/atspi_tool.py` and `work/blank-window/window_tool.py`
  (`close` = `WM_DELETE_WINDOW`, never a signal exit). Raw captures are in
  `native-20261019T1945Z/` beside this file.

## Why this slice

The applicable canonical range (specification 4763–4866, "Sidebar and navigation") requires that the
main Spaces sidebar is contextual to the selected Space, **collapsible**, and that its top control
**displays the currently selected Space and provides a Space switcher when activated**, with an
**adjacent overflow control** providing quick actions for that Space. The authored sidebar previously
presented only the Space name, New chat and the conversation rows: none of those three controls
existed. This is a product gap, not a labelling difference, so it was implemented rather than excused.

## Journey actually performed on the real surface

1. `Apps → Open Spaces → Open Study` mounted a Study Space conversation. The contextual sidebar
   header then presented `Switch Space` (its visible content is the selected Space name, `Study`),
   `Quick actions for this Space` and `Collapse this sidebar` (`inspect-header.json`).
2. Clicking `Switch Space` opened the switcher, which listed the canonical Space set — Chat, Study,
   Tasks, Assistants, Shopping, Research, Translate, Experiences — as real rows beside the rail
   (`inspect-switcher.json`). No action failure was shown.
3. Clicking the switcher's `Open Translate` row (recorded as `matched_count: 2`, `clicked_index: 1`,
   because the rail also carries a Translate entry) switched the same tab to
   `9to1://spaces/selected/b1000000-0000-0000-0000-000000000007/chat/9d4b0b45-6f73-4a6c-ab19-94fc02b760d7`
   and the header re-labelled itself `Translate` (`inspect-switched.json`). The switcher therefore
   selects and opens, consistent with the owner's direct-entry correction; it never opens a details
   interstitial.
4. Clicking `Collapse this sidebar` removed the contextual sidebar from the presented surface (no
   `New chat` control remained), left the conversation mounted with the **same address**, and put one
   explicit `Expand this Space's sidebar` control in the icon-only rail (`inspect-collapsed.json`).
5. Clicking `Expand this Space's sidebar` restored `Switch Space`, `Start a new chat in this Space`
   and `Collapse this sidebar` with the conversation identity unchanged (`inspect-expanded.json`).
6. Clicking `Quick actions for this Space` presented the selected Space's quick actions
   `Manage selected Space`, `Rename selected Space` and `Archive selected Space`
   (`inspect-overflow.json`). For this protected built-in Space they are present but not actionable;
   that enabled/disabled state is proven in the headless rendering test rather than asserted from
   this capture, because the AT-SPI dump does not expose the enabled flag.
7. `WM_DELETE_WINDOW` closed the window normally in **2 s**. No signal was used.

## Headless coverage added with the slice

`SpacesHomeCuiRenderingTests.Authored_contextual_sidebar_presents_switcher_quick_actions_and_collapse`
proves on the authored document: the header control carries the selected Space name and dispatches
`spaces.toggle-space-switcher`; the switcher projects the built-in and custom Spaces with their
canonical open labels; the overflow dispatches `spaces.toggle-space-overflow` and its three actions
stay visible but disabled for a built-in Space; the collapse control dispatches
`spaces.toggle-sidebar`, the sidebar stops being presented and the layout projection becomes
`72 0 1fr`; and the rail's expand control dispatches the same command to bring it back.

Lane state after the slice: `HavenOS.Spaces.Tests` 219/219, canonical CUI compilation 13/13,
`CakeOS.Cui.Runtime.Tests` 405/405, `HavenOS.Apps.Browse.Tests` 150/150,
`HavenOS.Browse.Host.Tests` 254 passed / 3 failed (the same three explicit Windows-Home platform
guards).

## What this proves and what it does not

It proves the three specification-required contextual-sidebar controls exist and work on the real
surface, while the icon-only rail and the conversation stay presented, and that collapsing is
reversible without losing the mounted conversation.

It does **not** yet prove the specification's further requirement that the main sidebar is
"customisable through stable section/item/rule identities" or that Plugin-contributed custom sidebar
elements appear in the selected Space's permitted custom-sidebar area with per-Space visibility. That
remains an open R02/Spaces item and is recorded as such. It satisfies no acceptance criterion by
itself.

## Limits

Real shipping apphost, real profile, real AT-SPI input, default theme at 1440x920, built-in Spaces
only (no custom Space was selected in this pass). No model request was issued. No owner profile or
owner preview instance was touched.
