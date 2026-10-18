# R03 launcher native proof + R01 shutdown differential (measured, 2026-10-18 21:25–21:43 UTC)

Implementation evidence only. Acceptance is independent (Astra) and remains NOT_RUN.

## Exact identities
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven` built from branch
  `opencode/v2-pc-sequential-20261018` at HEAD `58d82393` plus the uncommitted test-only fix
  (`/tmp/opencode/r02-apps-native-build-2.log`, `BUILD_EXIT:0`).
- Disposable profile (worker-owned, isolated): `/tmp/opencode/r03-apps-20261018T2125Z` with
  `HAVEN_DATA_DIR=$R/haven-data` and `0700` XDG roots under the same directory, `umask 077`,
  `DOTNET_ROOT=/home/jacob/.local/share/dotnet`.
- Process/window: PID `2303823`, window `0x180001b`, frame `9to1`.
- Owner preview PID `1885357` / window `0x140001b` / profile `/tmp/opencode/haven-data-4` was not
  touched and is still the only remaining Haven process.

## The owner-visible "app list is empty?" symptom is disproved on the repaired build
Opening the actual Home route (the app publishes Home with the Apps section selected) rendered the
authored launcher with **29 real registered app tiles**, each with an icon and its registered name:
9to1 Haven, Automations, Boards, Canvas, Chat, Dashboard, Data, Dev, Files, Forms, Go, Imagine,
Launcher, Maps, Mesh, Motion, Plan, Play, Present, Shelf, Spaces, Studio, Study, Tasks, Terminal,
Training, Translate, Vision, Write. Screenshot `launcher-populated.png`; tree `inspect-1.json`.
The authored search field is present as entry `Search apps`, with the honest count label `29 apps`
and status `Select an app to open it in this tab.`

## Search, no-match and clear verified on the real UI
- `set-text "Search apps" = "boards"` → tiles narrowed 29 → 2 (`Open Boards`, `Open Canvas`), and the
  `Clear app search` control appeared (its `IsVisible` is bound to the projected query state).
- `set-text = "zzzz-not-installed"` → 0 tiles and the two-line honest no-match state:
  `No app matches “zzzz-not-installed”.` / `Clear the search, or use the address bar to open an app by name.`
- Clicking `Clear app search` → 29 tiles restored.
Trees: `inspect-search.json`, `inspect-nomatch.json`, `inspect-cleared.json`.

## Single-click activation stays in the same Haven tab — SUPERSEDED, see the correction below
Clicking the `Open Files` tile set the address entry to `9to1://files` and produced exactly one tab
control (`Close Files`); no second tab appeared, and no failure banner was shown. Before the click
there was no `Close *` tab control in the Home surface. Trees: `inspect-files.json`,
`after-open-files.png`.

## R01 differential finding (new, precise, still open)
This instance **exited ~5s after one normal `WM_DELETE_WINDOW` close** (`close-1.json`).
The two earlier disposable instances that had a **retained failed operation** never exited:
- PID `2215719` (profile `r01-native-20261018T2030Z`) had the refused `9to1://boards` admission:
  window mapped and process alive 367s across three normal closes.
- PID `2239605` (profile `r02-apps-20261018T2045Z`) had the CUIR053 Home-publication failure plus the
  refused `9to1://home/apps` admission: alive 2214s, still not exited after a further normal close,
  cleaned up with SIGTERM (recorded as cleanup, never as a passing shutdown).
Measured differential: a clean session closes normally; a session holding a retained failure does
not complete process exit. That is the R01 shutdown-poisoning class, still open, and the next R01
step is to make the retained-failure close path complete (or report truthfully) without ignoring the
failure. Both earlier instances are recorded in `evidence/R01/NATIVE-APPHOST-2026-10-18T2037Z.md`.

## Correction to the same-tab claim (independent evidence review, cycle-20261018T213434Z)
The "same tab" conclusion above is **withdrawn**. Re-inspection of `after-open-files.png` and
`inspect-files.json` shows **two real tabs**: the preserved Home tab plus `Files`. Home is a
non-closable tab (`BrowseWorkspace.cui` repeats the actual Tabs with `CanClose` controlling the
close-button visibility), so counting a single `Close Files` control proved nothing about tab count.
Current code deliberately opens a new tab when `source.IsHome`, while `newtab` takes a different
path. This Home journey is therefore **not** proof of the required New Tab → Apps → app same-tab
behaviour, which stays **NOT RUN** until measured with actual tab identity and count. The protected
Home tab must not be removed to make a journey pass.

The populated launcher (29 real tiles with icons), search narrowing 29 → 2, the honest no-match
state, clear restoring 29, and the successful `9to1://files` entry remain valid **within that scope**.
Review evidence: `/home/jacob/Documents/Codex/2026-10-17/new-chat/work/9to1-coordinator/cycle-20261018T213434Z/launcher-tab-identity-review.json`.

## Still open after this proof
The catalogue still projects Dashboard and the absorbed Spaces types (Study, Tasks, Translate, Chat)
as standalone applications, so the canonical ownership correction is NOT done; the launcher merely
reflects registered state truthfully. Space → new chat with both rails, and measured latency, remain
open.
