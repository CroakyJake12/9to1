# Native Haven Files: normal window-close and same-profile reopen acceptance probe

**18 November 2026 — cloud-disjoint implementation, not a completed Drive release.**

## Why and exactly what it checks

The previous same-profile X11 smoke (`native_files_profile_reopen.py`) terminated the real `Haven.Desktop` process group via harness signals. It demonstrated two genuine window startups, **not** that the existing app closes and exits normally. This helper keeps that earlier smoke intact and exercises a new, bounded real-desktop acceptance question:

1. Admit an already built actual Haven Linux x64 apphost and an unused scratch name beneath an existing private, owner-controlled `0700` parent (never the owner's real Haven data directory).
2. Launch the exact existing apphost in a new isolated HOME, XDG and `HAVEN_DATA_DIR` with the existing minimal environment allowlist. Observe a process-owned X11 window using the existing `_NET_WM_PID` probe.
3. Confirm this window advertises X11 `WM_DELETE_WINDOW`; send that normal client-close message, require exit code zero within the allotted timeout. Harness `SIGTERM`/`SIGKILL` remains a cleanup fallback only, **not** a successful close.
4. Without deleting or changing the disposable profile, relaunch the same apphost as an independent process, observe a new process-owned window, request normal close again, require exit zero. The script emits bounded, path-free JSON results and retains private mode-0600 stdout/stderr logs for inspection.

No Google Drive credentials are read, consent is never opened, and the script cannot validate that any Drive file or token was persisted: `drive_authentication`, `drive_data_persistence`, and `file_operations` are all explicitly `NOT_RUN`. This smoke does not choose a user file, bypass app permissions or mutate a real account. A successful X11 close/reopen is **not** proof of successful saving or of a usable Drive frontend.

## How to run on an already verified private Ubuntu linux-x64 candidate

See `docs/development/native-files-source-candidate-2026-11-18.md` for the real existing Haven build. The runnable candidate must have the expected `Haven`, `Haven.dll` and runtime config; do not substitute a mock. Start inside an actual X11 graphical session or a disposable Xvfb/Openbox desktop, with the matching .NET 10 runtime and `libX11.so.6` available.

```sh
cd /absolute/path/to/9to1
SCRATCH="$(mktemp -d)"             # a brand-new owner-private mode-0700 directory
python3 -B eng/development/native_files_normal_close.py \
  --executable "/absolute/private/current-candidate/installed/Haven" \
  --scratch-parent "$SCRATCH" --timeout-seconds 45
```

The app must advertise the window-close protocol. If it does not, `FAIL` (`wm-delete-not-supported`) is a real result requiring investigation; do not replace the close with a process signal and label the result normal. Always use a throwaway test profile, not the owner's existing real data. A current source SHA, apphost binary hash, build record and actual test receipt must accompany any genuine Haven run. The retained scratch directory is yours to remove after inspecting private logs; the tool does not delete arbitrary supplied directories.

## Concrete evidence and limits

- Source syntax: `python3 -m py_compile eng/development/native_files_normal_close.py eng/development/test_native_files_normal_close.py` **PASS** locally.
- Disposable subprocess/filesystem suite: `python3 -B -m unittest discover -s eng/development -p 'test_native_files_normal_close.py' -v`: **10/10 PASS, 1 X11 test skipped** without a live display, using test-owned synthetic shell apphosts (signals only simulate the normal exit event).
- Actual X11 client-protocol integration: the **same 11 tests under real disposable `xvfb-run` + Openbox**, with `gcc -lX11` compiling a synthetic ELF window host that advertises `WM_DELETE_WINDOW`, **11/11 PASS**, including two separate actual normal client-close requests, real process exits, and a stale X11 window ID rejection. The generated ELF is a **synthetic fixture**, not Haven. This verifies the protocol sender and observation/cleanup path, not actual product behaviour.
- Real current-source Haven normal-close/reopen under CI: **NOT RUN** in this initial isolated scope; the current serial native workflow and its existing SDK build remain owned elsewhere. Owner/CI integration is the next distinct scope after its workflow claim is released and the SDK guard permits it.
- Live OAuth, Google Drive list/search, upload/download/exports, rename/move/trash/restore, owner profile normal close, network/expired-token cases and application state persistence: **NOT RUN** by this tool.

This harness imports the repository's existing `native_files_profile_reopen` validation and isolated environment and the existing `native_files_linux_smoke.x11_windows` probe, rather than creating a second Files implementation. It never publishes packages to the **public** source repository; `docs/development/installer-delivery.md`'s private-custody boundary remains unchanged. No production deployment, live Drive mutation or owner PC operation is authorised.
