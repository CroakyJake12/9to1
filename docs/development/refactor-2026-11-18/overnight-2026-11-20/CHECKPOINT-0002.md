# CHECKPOINT-0002 — G01-T01 native verification (partial) + tooling increment

2026-11-20 ~21:55Z · coordinator · repo `main` @ `010be319d` (pushed `9117169b3`→`76259b2c1`→`010be319d`).

## What is now true (verified)

* **Dev copy runs at HEAD on this PC** via the normal live-preview custody chain on the dedicated
  development profile. Custody had to be legitimately re-opened twice:
  * the 17/18-Nov crash lost the guardian (no exit receipt) → **new reviewed lane**
    `reconcile-lost-guardian` (feature commit `76259b2c1`, docs `010be319d`, 34/34 tests) recorded a
    real review binding the dead run; it fabricated no receipt;
  * the first framework-dependent candidate exited 150 (apphost saw only shared runtime 8.0.34,
    environment not product) → reviewed `recover-failed` to the guarded **self-contained** candidate
    `atlas-g01sc-010be319d` (publish 82.9s, guard PASS).
  Along the way 644 hard-linked read-only vendor build intermediates (framework/CUI/vendor obj/bin,
  18 MiB, regenerable) were unlink-repaired so the compiler could rewrite them.
* **Guarded builds green:** Haven.Desktop framework-dependent 115.5s + self-contained publish 82.9s
  (0 errors; 1 pre-existing WebRtcVad warning). **Haven.UI.Tests 177/177 PASS** at HEAD.
* **Native, on-screen (captures g01-01…09):** start + render + **session restore** are real. The 18-Nov
  session came back — Boards doc, Files, reddit, two Mesh tabs, plus the S3 route's restored
  **New tab** and **Home** tabs — and Files lists the user's 8 actual Drive folders (data preserved).

## Observed product findings (native, to fix under G01)

1. **Adaptation:** tab labels render **centered** in wide tabs; current S1/HAV-TAB requirement wants
   left-aligned icon+title with trailing close (only the model-picker role tabs are centered).
2. **Bug:** on this single 4384x2466 display the restored window frame sat **above the top edge**
   (client root origin negative), putting the whole tab strip off-screen. The app should clamp restored
   window bounds to the current workspace. This is exactly the kind of HAV-TAB/shell defect in scope.

## Precise blocker (kept, does not hold the queue)

**Interactive** native retests (clicking the tab strip, the plus-button New tab, the Home surface) are
**NOT RUN**: because the tab strip is off-screen-top, XTEST pointer/warp and WM move requests
(_NET_MOVERESIZE_WINDOW, configure, WM_DELETE) all fail to act on it, and `wmctrl`/`xdotool` are absent
(no sudo). Per AGENTS.md (two materially different failed approaches → record, keep moving) this is
recorded as a blocked operation. The app is left RUNNING for the owner. Next unlock attempt: find the
persisted window-geometry store in the dev profile, correct it while the app is closed, and reload so
the strip is on-screen; or run the copy under a compositor that clamps to the monitor.

## Statuses

G00 done (bound, checkpointed, pushed). G01-T01 = `implemented_pending_native` — build/test/launch/
render/restore evidence present; the interactive half is blocked as above. New bounded subtasks queued in
TASKS.json (tab-alignment adaptation; window-bounds clamp; interactive-retest unblock). Inference services
(official engine + V100 lane) untouched and healthy; Sunday multi-GPU work not started.
