# R01 native unsaved-draft close-preparation journey — veto, cancel, save, discard (measured)

Worker: single PC implementation worker. Run: 2026-10-19 18:10–18:21 local (17:10–17:21 UTC).
This is **implementation evidence**, not independent acceptance.

## Exact identities

- Branch `opencode/v2-pc-sequential-20261018`, source HEAD `37ff0b4d` (R02 sidebar thread fix on
  top of the R01 repair `c17761e2` / recovery view `3db9326f`).
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven`, built with
  `/home/jacob/.local/bin/dotnet build Haven.Desktop.csproj -c Debug -f net10.0 -m:1` → `0 Error(s)`.
- Disposable profile (mine, isolated, `umask 077`, `0700` XDG roots):
  `/tmp/opencode/r01-draft-20261019T1810Z`, `HAVEN_DATA_DIR=$R/haven-data`,
  `DOTNET_ROOT=/home/jacob/.local/share/dotnet`, `DISPLAY=:0`.
- Processes/windows: `358286` then relaunches `363668`, `364731`; window `0x140001b`, frame `9to1`,
  1440x920 logical, default theme.
- Driver: `work/blank-window/window_tool.py` (`close` = `WM_DELETE_WINDOW` only, never a signal) and
  `work/blank-window/atspi_tool.py` (`inspect`, `click --name … --role button`,
  `set-text "…" --name "Space name" --role entry`).
- Raw captures are in `native-draft-20261019T1810Z/` beside this file
  (`inspect-veto.json` sha256 begins `db6d80cf61ad4308`).

## Journey actually performed

1. `Apps → Open Spaces → Create a custom Space` opened the Space Builder for Space
   `df8a05f2-bd0a-4ba2-99d4-d6c76924cbc6` at `9to1://spaces/builder/df8a05f2-…`.
2. The Space name field was set to `Draft probe` through real AT-SPI text input, making the editor
   genuinely dirty (`inspect-dirty.json`: `Space name = Draft probe`, with
   `Save Space Builder configuration`, `Discard unsaved changes and reload` and
   `Reload saved Space Builder configuration` all present).
3. **Close attempt on a dirty editor is refused, not silently discarded.** `WM_DELETE_WINDOW` left
   the process alive after 15 s and the app presented a real visible three-choice decision:
   "Save your Space details?" / "Save these changes, discard them and use the saved details, or
   cancel to keep editing." with `Save Space details and prepare to close`,
   `Discard unsaved Space details and prepare to close` and
   `Cancel closing and keep editing Space details`, plus the status "Unsaved changes."
   (`inspect-veto.json`). No content was lost and no fake clean success was reported.
4. **Cancel keeps the exact draft and usable commands.** After
   `Cancel closing and keep editing Space details` the decision disappeared, the same editor was
   live again with `Space name = Draft probe`, and Back/Save/Discard/Reload were all actionable
   again; the process stayed alive (`inspect-editing.json`).
5. **Save then closes cleanly.** A second `WM_DELETE_WINDOW` re-presented the decision; choosing
   `Save Space details and prepare to close` made the process **exit 1 s later** with no signal used.
6. **The saved value really persisted.** Relaunching the same disposable profile restored the
   Builder tab at `9to1://spaces/builder/df8a05f2-…` with `Space name = Draft probe`
   (`inspect-reopen.json`).
7. **Discard really discards.** The name was then changed to `Should be discarded`, the close again
   presented the decision, and `Discard unsaved Space details and prepare to close` made the process
   **exit 2 s later**. Relaunching showed `Space name = Draft probe` again — the discarded edit was
   not written (`inspect-afterdiscard.json`).
8. A final close with no pending edit exited normally in **2 s**.

## What this proves and what it does not

This is the "unsaved draft fixture" sub-item of the shutdown assignment run on the actual product
route: reversible close preparation, a visible save/discard/cancel decision, a veto that leaves the
content and commands current and usable, a save that persists with a clean exit, and a discard that
does not persist. It also re-confirms the R01 shutdown repair on a Spaces route.

It does **not** prove the remaining R01 negative-safety probes on the real UI route (a failing save
or a failing close must retain recovery state rather than silently discard, delayed `DisposeAsync`
failure, late acquisition during cancellation, permission revocation). Those are covered by unit and
host tests only, so R01 stays `NOT_RUN` for independent acceptance.

One related risk is recorded separately and still open: on HEAD `71661cf3` a *faulted* Spaces
sidebar action left that instance unable to complete an ordinary close for the rest of its lifetime.
The faulting cause is fixed in `37ff0b4d`, but the general guarantee "a faulted UI action must never
permanently block close" has no dedicated probe yet.

## Limits

Real shipping apphost, real profile, real AT-SPI input, default theme at 1440x920, Space Builder
editor only. No model request was issued. Chat-composer drafts (as opposed to the Space editor
draft) were not exercised in this pass, and no owner profile or owner preview instance was touched.
