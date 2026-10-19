# R02 native Spaces entry gate — actual rail click, real per-Space chat, retained rail (measured)

Worker: single PC implementation worker. Run: 2026-10-19 18:04–18:14 local (17:04–17:14 UTC).
This is **implementation evidence**, not independent acceptance.

## Exact identities

- Branch `opencode/v2-pc-sequential-20261018`, source HEAD `d8c6be77`
  ("R02: Space selection opens that Space's chat directly inside the retained icon-only Spaces rail").
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven`, produced by
  `/home/jacob/.local/bin/dotnet build Haven.Desktop.csproj -c Debug -f net10.0 -m:1`
  → `0 Error(s)`, `1 Warning(s)` (the existing WebRtcVadSharp AnyCPU warning).
- Disposable profile (mine, isolated, `umask 077`, pre-created `0700` XDG roots):
  `/tmp/opencode/r02-spaces-entry-20261019T1705Z` with `HAVEN_DATA_DIR=$R/haven-data` and
  `XDG_CONFIG_HOME/DATA_HOME/CACHE_HOME/STATE_HOME` under the same root.
  `DOTNET_ROOT=/home/jacob/.local/share/dotnet`, `DISPLAY=:0`,
  `XAUTHORITY=/run/user/1000/.mutter-Xwaylandauth.0539W3`.
- Processes/windows: run 1 PID `285661` window `0x140001b` (frame `9to1`, 1440x920 logical /
  2880x1840 capture, default profile theme). Relaunch PID `292174`, same window id.
- Driver tooling (read-only reuse of the shared harness): `work/blank-window/window_tool.py`
  (`list`, `capture`, `close` = `WM_DELETE_WINDOW`, never kill) and `work/blank-window/atspi_tool.py`
  (`inspect`, `click --name <name> --role button`).
- No other Haven instance was running; the owner preview was not started or touched.
- Raw AT-SPI captures, click results, close results, run logs and the screenshot are in
  `native-20261019T1705Z/` beside this file. `spaces-study-chat.png`
  SHA-256 `2a8cd7e3201e1f562a7e7fbf6262ea7c756034ee2bc7791a9a2c43b12c35bd6d`.

## What the real product did

1. Fresh profile opened on Home with Apps selected: **24 apps**, and the absorbed entries
   Dashboard, Study, Tasks, Translate, Assistants and Dev are no longer projected as standalone
   apps (`inspect-1.json`).
2. `Apps → Open Spaces` opened Spaces Home in the same tab: the icon-only rail
   (`Create a custom Space`, `Open Chat`, `Open Study`, `Open Tasks`, `Open Assistants`,
   `Open Shopping`, `Open Research`, `Open Translate`, `Open Experiences`) plus the selected-Space
   summary (`inspect-2.json`).
3. **One click on the `Open Study` rail icon opened a chat directly in that Space.** No details or
   sources interstitial appeared. The address entry became
   `9to1://spaces/selected/b1000000-0000-0000-0000-000000000001/chat/f24f332e-2fa8-4451-ab0a-35d75ffbe1e5`
   and the live surface was `9to1-native Chat page` with the `Ask 9to1 anything` composer, while
   **all nine rail controls remained present in the same tab** (`inspect-3.json`, screenshot).
   The AT-SPI click call itself returned in 0.19 s; the chat was fully present within the next
   poll (no measurable multi-second stall on the real route).
4. **Switching Space works the same way**: one click on `Open Translate` produced
   `9to1://spaces/selected/b1000000-0000-0000-0000-000000000007/chat/e083f5cc-ef12-48e1-9657-40aea9674dd1`
   with the rail still intact (`inspect-4.json`, click 0.21 s).
5. `Create a custom Space` created Space `1d43cfaf-60f8-4808-9139-5d547e012c67` and opened its
   Space Builder at `9to1://spaces/builder/1d43cfaf-...` with the real Basic fields
   (name, description, icon, custom instructions, Skills/Plugins/Apps/Models/Memory category
   state) and Back/Save/Discard/Reload (`inspect-5.json`).
6. **Management return-chat identity holds**: `Back to Spaces` restored the *same* Translate
   conversation `e083f5cc-...` inside the same Spaces Home layout, and the rail gained
   `Open New Space` plus the authored `Manage selected Space` entry (`inspect-6.json`).
7. The rail `Manage selected Space` control re-opened that same Space's Builder
   (`inspect-7.json`), and a second `Back to Spaces` again restored the same chat
   (`inspect-8.json`). Management therefore stays a deliberate operation and no longer costs the
   user their conversation.
8. **Ordinary close and reopen restores the saved conversation.** `WM_DELETE_WINDOW` closed the
   first instance in **1 s** (previously 40 s+ / never on this route). Relaunching the same
   profile reopened the Spaces tab at
   `9to1://spaces/selected/b1000000-0000-0000-0000-000000000007/chat/e083f5cc-ef12-48e1-9657-40aea9674dd1`
   with the rail and chat together (`inspect-reopen.json`). The second instance closed normally in
   **4 s**. No signal was used to end either instance.

## Honest remaining gaps for R02 (still open)

- **The selected Space's chat/history sidebar is not present in the live chat surface.** The rail
  is retained, but `9to1-native Chat page` renders no recent-conversations column, so the owner
  requirement "keep the icon-only Spaces rail *and* the selected Space chat/history sidebar visible
  while entering and using chats" is only half met. The authored `ChatSpace.cui` already declares a
  `recent-chats` region; the live Haven chat page does not. This is the next implementation item.
- Website-tab roundtrip (same draft/conversation after leaving and returning to the Spaces tab)
  was **NOT RUN** in this pass.
- Offline model inventory (no service on 127.0.0.1:11434) was not re-exercised in this pass; the
  degraded-inventory repair `d450f4bf` is covered by unit tests only, so the offline native entry
  gate remains separately open.
- `HavenOS.Browse.Host.Tests` has **12 pre-existing failures at HEAD `3525b860`** (verified by
  stashing this change and re-running): 5 in `SpacesDetailsRouteTests`, 4 in
  `UnifiedHomeContentTests`, 3 in `UnifiedWorkspaceHostLifetimeTests`. The same 12 remain after
  this change and no new failure was introduced (244 passed both times). The five
  `SpacesDetailsRouteTests` failures are stale expectations that the Home `Edit details` command
  publishes `SpaceDetailsPage`; the reviewed routing since `3dc468cd` publishes the canonical
  `SpaceBuilderPage`, which matches the specification's single-canonical-Space Basic/Advanced/
  Expert builder model. Those expectations still need to be rewritten against the Builder surface
  rather than deleted.

## Limits

This run used the real shipping apphost, a real profile and real AT-SPI input; it issued no model
request and generated no content. It proves the entry/navigation/management/restart journey above
for the default theme at 1440x920 only. It does not prove full Spaces, Space Builder, Voice &
Vision or web-service conformance, and it does not satisfy any acceptance criterion by itself.
