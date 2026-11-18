# Actual native Haven Files same-profile reopen CI (18 November 2026)

## Pinned user flow and boundary
This change extends the **existing** `.github/workflows/native-files-linux-smoke.yml` Linux x64, non-deploying, SDK-serial build. The source candidate builds the real `Haven.Desktop` native host at the exact workflow checkout; the existing archive verifier admits it into a private directory and the original/extracted hosts each open an actual X11 window. The new acceptance runs the **same archive-extracted apphost twice** in one **new, disposable, mode-0700** HOME/XDG/HAVEN_DATA_DIR profile, with distinct native processes and observable process-owned X11 windows both times.

The new `test_native_files_profile_reopen.py` fixtures run before the native source build. The real reopen probe executes in the **same** Xvfb/Openbox session after both existing original/extracted window checks, using `--scratch-parent "$HAVEN_SMOKE_SCRATCH/profile-reopen"`. It is **not a second SDK job**, does not inherit Google OAuth variables, does not reuse any owner profile and does not upload a package.

## Evidence tiers
- Fixture regression: `python3 -B -m unittest discover -s eng/development -p 'test_native_files_profile_reopen.py' -v` exercises disposable Linux subprocess/filesystem test data, **not Haven or real Drive**.
- Real native window reopen: the GitHub runner must independently complete and log `native_files_profile_reopen.py` with `status=PASS`, two observed windows, separate PIDs and the same disposable profile. The harness itself signals test process groups; it has **not** tested a normal user's close/reopen. The marker is test-harness data, **not a saved Files artifact**.
- Real Google Drive: **NOT RUN** in this workflow. In particular sign-in, token expiry, authorized file listing, search, downloads/exports, upload/create/rename/move/trash/restore, and persistence of Drive file IDs across restarts remain independent acceptance tasks.
- Downloadable distribution: **NOT PROVIDED** by this workflow. The native tar is built into runner-private scratch. A separate authorised delivery mechanism is required before claiming a user-installable package.

## Reproduction
Run the full existing native workflow from an exact GitHub `main` source revision, or locally only after installing the project-pinned SDK and X11 dependencies. To test the isolated script without building a product:
```sh
python3 -B -m unittest discover -s eng/development -p 'test_native_files_profile_reopen.py' -v
```
Actual CI job/revision, pass/fail and package digest must be reported from the resulting GitHub Actions run, not inferred from this document. No live Drive mutation, production deployment, personal credentials or desktop configuration is authorised by this check.
