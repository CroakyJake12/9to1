# 9to1 does not close normally — observed 27 October 2026

Owner report during the live Boards loop: **"9to1 doesn't close normally."**

## What was actually done to the running copy

Development copy: run `59810898-b62e-4b15-a800-22670443fce0`, source `8e42d2402432`,
runtime `8e42d2402432-boards08`, profile `/home/jacob/.local/share/9to1-development/profile`,
window `0x420001b` (client) inside frame `0x8000aa`, PID 1586024.

1. Window properties: `WM_PROTOCOLS` advertises `WM_DELETE_WINDOW` and `_NET_WM_SYNC_REQUEST`;
   `_NET_WM_ALLOWED_ACTIONS` includes `_NET_WM_ACTION_CLOSE`. So the window is closeable as far as
   the window manager is concerned.
2. `/tmp/opencode/close_window.py 0x420001b` sent `_NET_CLOSE_WINDOW` (the EWMH request a WM close
   action makes). Result: window still `IsViewable`, process alive, launcher still `RUNNING`.
3. `/tmp/opencode/close_window.py 0x420001b --wm-delete` sent `WM_DELETE_WINDOW` directly to the
   client (what a WM does on its close button). Result: identical — window still open, process
   alive, no dialog appeared in the X window tree, and `application.log` gained no line.

No signal was sent and no process was killed. The failed close attempts are retained as evidence.

**Postscript, same day:** the launcher ultimately recorded run `59810898` as `EXIT_FAILED` with
exit code **-15 (SIGTERM)**. So the application was terminated by signal rather than closed: the
cooperative close never completed. This receipt is retained because no other exit record exists
for that run; the review that named it as the superseded candidate is
`/home/jacob/.local/state/9to1-live-preview/boards10-failure-review.json`. It is evidence that the
normal close lifecycle was unavailable, not evidence about the close code's behaviour.

## Re-observed 28 October 2026 — the stall is now a named stage

The owned development copy (run `4653e38a-2350-4a71-b1af-4ffc75de558a`, source `70f3e900e55c`,
runtime `70f3e900e55c-boards-m1-c-28`, PID 572980, window `0x340001b`, profile
`~/.local/share/9to1-development/profile`) was asked to close twice, with no signal sent:

1. 21:41 — the EWMH request `wmctrl -c` makes. My message was **malformed** (a `_NET_CLOSE_WINDOW`
   client message is addressed to the window manager with `data.l[0]` = window, and I sent it to the
   client with `data.l[0]` = timestamp), so this attempt proves nothing about the application.
   Recorded rather than hidden. `wmctrl` itself is absent on this host and cannot be installed
   unattended (`sudo -n apt-get install wmctrl` → "a password is required").
2. 21:43 — an ICCCM `WM_DELETE_WINDOW` protocol request sent to the client, which is what a titlebar
   close button delivers. The window advertised `WM_PROTOCOLS: WM_DELETE_WINDOW,
   _NET_WM_SYNC_REQUEST`, exactly as in the 27 October measurement.

Both left the window open, the process alive, no dialog in the X window tree and no launcher state
change; `live_preview.py reload` correctly refused to replace anything ("Normal close was vetoed,
timed out or failed; no force/replacement was attempted").

**New measurement, from the application's own retained diagnostic** `~/.config/Haven/Logs/
close-preflight-diagnostic.log` (written by `App.WriteOriginalCloseDiagnostic`, added by earlier
work). The 21:43 attempt recorded:

```
[2026-10-28T21:43:22] close-preflight: window-close requested; preflight starts
[2026-10-28T21:43:22] close-preflight: captured 0 close shells and 0 preflight candidates
[2026-10-28T21:43:22] close-preflight: unified workspace close preparation acquired
```

…and nothing further, while the 09:51 and 10:03 runs continued with
`unified workspace close lease accepted=False` and `all close preflight candidates and the unified
lease joined` before also stopping there (the window still did not close in those runs either). The
11:20, 13:45 and 21:43 runs all stop immediately after `… preparation acquired`.

So the stall is **inside the unified close preparation** — between
`App.OriginalLifetime.RequestOriginalDocumentClosePreflight` awaiting
`UnifiedWorkspaceHost.PrepareCloseAsync(CancellationToken.None)` and the lease decision — not in the
shell preflight (zero shells were captured), and there is **no bound** on that await (`CancellationToken.None`).
This is now the closest thing to a reproduced diagnosis the file asked for, but the specific
participant inside the chrome preparation is still not named by the retained log.

**What was changed on 28 October as a consequence** (commit `b24ab70e`, logging only — no close
decision, cancellation, veto or join altered): the diagnostic is now `internal` and times each stage
(`<stage>: start` / `joined after Nms` / `failed after Nms`), the unified driver names the tab
participants by their actual content type and address and reports admission refusals and chrome
vetoes, and the Boards close participant (`BoardsWritableRichDocumentBrowseAppContentSession`, which
advertises reversible close preparation and is therefore a strong owner that the app close waits
behind) records its own stages: preparation begins → original initialization → document save →
dispatcher hand-off to the surface thread → presentation close preparation → no-preparation cases.
The next close attempt on a build containing it should name the stalled stage and owner instead of
going silent. Measurement of the owning close checks (`bugs-desktop`, `bugs-nativeapps`, plus
`UnifiedWorkspaceHostLifetimeTests` added to the `bugs-desktop` filter by the labelled successor
plan generator r15, because that owning test was in no stage filter at all) is cycle 13.

The live reload loop is still blocked: with this build the normal close does not happen, so the only
alternatives are the owner closing it themselves at the keyboard, or a Root-reviewed failed-exit
restart. Manufacturing a failed exit was not done — it is not the normal lifecycle and this worker is
not the reviewer of its own failure.

## Observed behaviour

The application **accepts the close protocol and then does not close**, with no visible prompt,
no status text and no log output. From the owner's side the window simply refuses to close.

## Code position (not yet a reproduced root cause)

`9to1 Workspace/shared/src/Haven.Desktop/App.OriginalLifetime.cs`:

- `OnOriginalPrimaryWindowClosing` (line 344) unconditionally sets `args.Cancel = true` and calls
  `RequestOriginalDocumentClosePreflight()`.
- `OnOriginalDesktopShutdownRequested` (line 351) also cancels and requests the same preflight.
- `RequestOriginalDocumentClosePreflight` then walks captured close shells and awaits each
  `PrepareCloseAsync` through the App-owned work queue; an early `return` on any owner that does
  not complete leaves the cancelled window open.

So the close path is a cooperative preflight that **cancels first and only re-issues a private
native close once every resource owner joins**. If one owner never completes — a Boards document
lease, the unified workspace, or an embedded artifact surface — the window stays open forever and
nothing tells the user why. That matches the symptom, but the specific owner that stalls has not
been identified: no input-event trace, no diagnostic log and no per-owner timing exist for this
run. Per the standing rule, this stays a **candidate cause, not a reproduced diagnosis**.

## Why it matters for the live loop

The one-bug-at-a-time reload cycle depends on the owner closing the window normally so the
launcher records `EXITED_CLEANLY` and `reload` can activate the next candidate. With the close
refusing, the only ways forward are a reviewed failed-restart (SIGKILL/SIGTERM recorded as
`EXIT_FAILED`, which is honest but not the normal lifecycle) or fixing the close path.

## Needed to diagnose properly

- A bounded diagnostic at each preflight stage: which shell/owner was captured, which
  `PrepareCloseAsync` is outstanding, and how long it has been pending, surfaced in the app status
  area rather than only in a log.
- Then a scoped fix so an owner that cannot settle is reported and can be overridden by the user,
  instead of the window silently never closing.

---

# Convergence queue — branches still needing real reconciliation

## Outstanding: `origin/astra/spaces-20261013`

**Owner decision requested.** On 27 October the owner directed, "converge every branch into main;
several things got fixed on other branches that are broken again." Seventeen remote frontier
branches were merged into one integration candidate (`local/main-converge-20261027`, then carried
into the live takeover line at `45e4b3e2cde4`). Every one of them was reconciled conflict by
conflict with the merge options listed in each commit message. This branch is the single
exception and is parked, not silently dropped.

Branch inventory at the time of review:

- `origin/astra/spaces-20261013` is 1265 commits behind `origin/main` and forks from an authoring
  of Spaces, Chat and the private Dulche relay system as of 13 October.
- Its committed content touches 432 files with 58629 insertions versus main, including whole new
  files: `HomeSpaceGraphOwner.cs` / `.Association.cs` / `.Recovery.cs`, a private Dulche relay
  runtime, a `.goal/` evidence library, and a distinct chat/Spaces composition.
- Merging it as-is would conflict against all 17 branches already reconciled today plus main's
  newer Spaces/Chat rewrites. The older authoring is a superset of neither side.

Why this needs an owner response rather than an automatic ours/take-all merge:

1. Several of its commits carry product statements whose later owner decisions supersede them
   (private relay deployment in `04e2cb07`/`ca3763a8` predates the recorded Dulche product
   owner decisions about bundled Dulche and the local default path).
2. Its `.goal/` library documents a checkpoint process and golden rules that have never been
   owner-amended into the 27 October expanded specification, so carrying them forward silently
   would re-inject retired process authority.
3. Reconciling it properly means deciding, file by file, whether the Space-chat message/action and
   Spaces-shell composition in this older authoring is intended to survive alongside main's newer
   authoring, or be retired with retained evidence.

Options the owner may choose:

- **(a) Retire the branch as preserved history** under the existing archive reference, retain it
  as evidence, and record the retirement reason here.
- **(b) Hand the branch to a dedicated reconciliation worker** (as the older-branch archive rule
  already expects: "individual content reconciliation") to be merged commit-by-commit with
  owner-reviewed semantics.
- **(c) Merge it now as a best-effort superset** with the stated caveat that the Spaces/Chat
  semantic ownership then has to be re-verified against the 27 October expanded spec before
  anything is called current.
