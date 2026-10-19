# R02 native contextual-sidebar gate — New chat inside the selected Space, rail retained (measured)

Worker: single PC implementation worker. Run: 2026-10-19 18:31–18:41 local (17:31–17:41 UTC).
This is **implementation evidence**, not independent acceptance.

## Exact identities

- Branch `opencode/v2-pc-sequential-20261018`. The first pass ran the apphost built from HEAD
  `71661cf3`; the defect found there is recorded below, and the repaired build is `37ff0b4d`.
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven`
  (`/home/jacob/.local/bin/dotnet build Haven.Desktop.csproj -c Debug -f net10.0 -m:1` → `0 Error(s)`).
- Disposable profiles (mine, isolated, `umask 077`, `0700` XDG roots):
  `/tmp/opencode/r02-spaces-sidebar-20261019T1730Z` (PID `325719`) and
  `/tmp/opencode/r02-diag2-20261019T1755Z` (PID `347242`), both window `0x140001b`,
  `DOTNET_ROOT=/home/jacob/.local/share/dotnet`, `DISPLAY=:0`.
- Driver: `work/blank-window/atspi_tool.py` (`inspect`, `click --name … --role button`) and
  `work/blank-window/window_tool.py` (`close` = `WM_DELETE_WINDOW`, never a signal exit).
- Raw captures are in `native-20261019T1755Z/` beside this file.

## Defect found and fixed by this pass (real app only)

On HEAD `71661cf3` the authored sidebar rendered correctly, but clicking **New chat** failed in the
shipping app: the AT-SPI tree showed the CUI action-failure text
"The action could not complete. Check the current state before trying again." and the address stayed
on the previous conversation. A temporary diagnostic (removed before commit) captured the actual
cause: `System.InvalidOperationException: The calling thread cannot access this object because a
different thread owns it.` at `OverlayChatSessionFactory.CreatePageAsync`, reached from
`SpacesBrowseAppContentSession.NewConversationOnUiAsync`. The new sidebar commands used
`ConfigureAwait(false)` after async store reads and therefore resumed off the UI dispatcher, while
the chat page factory verifies UI-thread ownership. The existing queued Space open path never did
this, which is why only the new commands failed.

Consequence observed in the same instance: after that failed action, a normal
`WM_DELETE_WINDOW` close did **not** complete within 25 s (the process stayed alive at 8 minutes).
That instance was terminated as cleanup, not counted as a shutdown result. The underlying action
failure is what poisoned the close; the repaired build closes normally.

## Repaired build (37ff0b4d), same real journey

1. `Apps → Open Spaces` → Spaces Home with the icon-only rail.
2. One click on `Open Translate` → chat `8f4850be-a687-4f71-8c4c-a3f85311df20` in Space
   `b1000000-0000-0000-0000-000000000007`, no failure text, and the contextual sidebar showed the
   selected Space name plus one conversation row (`inspect-a.json`).
3. One click on **`Start a new chat in this Space`** → the address became a **different**
   conversation `fff9204d-6206-4392-9566-43a6944a2bcb` in the same Space, the sidebar listed
   **two** conversation rows, and the icon-only rail (`Create a custom Space`, `Open Chat`,
   `Open Study`, …) stayed present in the same tab (`inspect-b.json`).
4. `WM_DELETE_WINDOW` closed that instance normally in **3 s**. No signal was used.

The offline degraded state was also observed on the real surface: the chat shows
"Local models are unavailable. Connect a configured model provider, then try again." while entry,
rail, sidebar and composer remain usable — no service was listening on 127.0.0.1:11434.

## Headless coverage added with the fix

`SpacesConversationRouteTests.Contextual_sidebar_new_chat_opens_a_real_conversation_in_the_selected_Space`
proves both sidebar commands against the real session route: New chat creates a second real
conversation scoped to the Space, and a sidebar row restores the exact earlier conversation
identity, with the Spaces Home layout still the tab's live view. The route fixture now retains every
conversation the session writes so `GetBySpaceAsync`/`GetAsync` answer with real identities while
still failing on any unexpected I/O.

Lane state after the fix: `HavenOS.Browse.Host.Tests` 245 passed / 12 failed (the same 12
pre-existing failures recorded at HEAD `3525b860`); `HavenOS.Spaces.Tests` 218/218;
`CakeOS.Cui.Runtime.Tests` 405/405; canonical CUI compilation 13/13.

## Honest remaining gaps for R02

- The sidebar row **click** was not exercised natively because both fresh conversations currently
  carry the same default title "New chat", so the AT-SPI name is ambiguous. The identity behaviour
  is proven headlessly; the native row-selection journey is still NOT RUN. Titling new
  conversations distinctly is a refinement, not a data change.
- Website-tab roundtrip (leaving the Spaces tab for a web page and returning with the same draft)
  remains NOT RUN.
- The five `SpacesDetailsRouteTests` failures (stale `SpaceDetailsPage` expectations versus the
  reviewed `SpaceBuilderPage` routing) and the four `UnifiedHomeContentTests` failures remain open.
  The three `UnifiedWorkspaceHostLifetimeTests` failures are an explicit platform guard: "This
  owning host fixture requires actual Windows Home; Linux is unvalidated" — recorded as BLOCKED on
  this host, not as a pass.
- A faulted Spaces UI action must not permanently block an ordinary close. The specific repro is
  gone with this fix, but that general guarantee is still unproven and needs a dedicated probe.

## Limits

Real shipping apphost, real profile, real AT-SPI input, default theme at 1440x920. No model request
was issued and no content generated. This proves the sidebar entry/creation journey and normal
close for the Chat/Translate Spaces only; it does not prove full Spaces, Space Builder, Voice &
Vision or web-service conformance, and it satisfies no acceptance criterion by itself.
