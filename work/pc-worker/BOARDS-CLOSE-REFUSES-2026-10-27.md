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
