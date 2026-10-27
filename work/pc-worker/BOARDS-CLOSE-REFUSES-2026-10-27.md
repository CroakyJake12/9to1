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
