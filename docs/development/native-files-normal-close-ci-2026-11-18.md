# Native Haven Files Linux x64: actual normal-close/reopen CI acceptance

**18 November 2026.** This records the bounded integration into the existing source-pinned, SDK-serial `native-files-linux-smoke.yml` job. It is not an authenticated Google Drive release or a native package delivery.

## Pinned user flow and pass condition

1. Build the **existing** `Haven.Desktop` from the exact GitHub main revision using the repository's supported .NET 10 SDK, original publisher and single `files-candidate-main` concurrent SDK guard. Privately admit the generated tarball and check extracted bytes against the publisher output; never upload the apphost or credentials as public artifacts.
2. Under the already-used fresh Xvfb/Openbox display and separately isolated mode-0700 test profile, re-run the existing original-host, archive-extracted-host and **harness-terminated** same-profile reopen checks. Distinguish those from a normal user-close sequence.
3. Run all `test_native_files_normal_close.py` regression tests under **real X11** (including a separately compiled, synthetic Xlib protocol fixture). This verifies the checker, not Haven behaviour. The existing runner installs `libx11-dev` to compile that fixture without relying on the GitHub image's implicit packages.
4. Exercise `native_files_normal_close.py` with the **actual archive-extracted Haven apphost**, a different newly created private profile, two sequential independent native processes and process-owned X11 windows. Send X11 `WM_DELETE_WINDOW` each time; require process exit **code zero** each time within 60 seconds. If either window lacks that protocol, crashes, hangs, reports nonzero exit, or disappears, the job must fail. Harness signals are only failure cleanup, never a PASS.
5. Preserve the native runner's checkout-without-persisted-auth and source SHA checks. Output only bounded, credential-free JSON and truthful CI summary text. Source/test-helper edits select this existing CI path; no new publisher, public binary artifact, deployment or schedule is created.

## Commands and evidence categories

The real apphost is compiled inside the existing workflow; it is not available in this cloud container. The actual job invokes:

```bash
python3 -B -m unittest discover -s eng/development -p test_native_files_normal_close.py -v
python3 -B eng/development/native_files_normal_close.py \\
  --executable "$HAVEN_ADMITTED" \\
  --scratch-parent "$HAVEN_SMOKE_SCRATCH/normal-close" \\
  --timeout-seconds 60
```

The above command depends on the existing ephemeral X11 environment, verified source checkout and private admitted archive. Before an exact-source GitHub runner completes, its native result is **NOT RUN**; do not transfer earlier real Haven launches at `3c1d9d222` or synthetic X11 successes to a newer revision. A failed current-source normal-close test is a genuine failure requiring native owner review, not a reason to replace WM_DELETE with SIGTERM or label the app accepted.

Even a fully green job would prove only native compile, package-byte parity, process-owned window startup and WM_DELETE close/reopen with disposable profiles. It **cannot** prove in-app Google authorization, credentials retained across normal closes, authenticated folder listing/navigation/search, download/export, upload, folder mutation, restore, refresh, revoked/expired permissions or conflict recovery. Strata owns these native provider/account acceptance paths. The public repository has no authorized private native-binary upload path (see `docs/development/installer-delivery.md`); the current source-build instructions remain in `docs/development/native-files-source-candidate-2026-11-18.md`.
