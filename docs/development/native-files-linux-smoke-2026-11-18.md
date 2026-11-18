# Native Haven Files Linux x64 clean-profile smoke (18 November 2026)

## Product flow pinned before implementation

1. Run only the original `Haven.Desktop` host (which references `HavenOS.Files.NativeUI`) from the existing repository, not a detached Files app.
2. Compile/publish the real host for linux-x64 using the repository's `9to1 Workspace/shared/eng/publish-haven-linux.sh`, .NET `10.0.401` installed (the repository's `global.json` allows the runner's compatible `10.0.4xx`, observed `10.0.404`), pinned Avalonia submodules and original dependency graph.
3. Under an ephemeral GitHub Actions Linux runner, start a clean private HOME/XDG profile with no owner's files, OAuth configuration, account or saved token.
4. Under Xvfb, observe a real visible X11 window owned by **the launched Haven apphost PID** using `xprop` root/window properties. The actual process must survive a stability check. Terminate only its freshly created process group.
5. Publish raw source commit, native package SHA and a non-secret PASS/FAIL summary in GitHub Actions logs; do **not** upload a native binary or owner profile to the public repository.

## What this does and does not prove

A passing GitHub run is genuine native linux-x64 **build and headless X11 window startup** evidence for its pinned commit. `test_native_files_linux_smoke.py` uses synthetic apphosts, so its pass only demonstrates probe control-flow and private fixture safety. The workflow's full `dotnet publish` and its actual Haven window step are separate stronger evidence, *only if their individual runner steps pass*. This does **not** verify Files view selection, OAuth consent, Drive listing, user interaction, file mutations, normal-close persistence, a .deb installer, or owner-usable package distribution. No public binary artifact is permitted by this workflow.

The existing native Strata worker retains exclusive ownership of `9to1 Workspace/Files/**`, the actual Haven test window/profile on the owner's PC and Google Drive provider implementations; this separate CI workflow changes none of those. The older WebAssembly candidate is not a substitute for native Haven.

**Run and receipts:** `.github/workflows/native-files-linux-smoke.yml` is a non-deploying workflow, manually dispatchable after its first self-file push. It serializes against `files-candidate-main`. Local quick checks: `python3 -B -m unittest discover -s eng/development -p 'test_native_files_linux_smoke.py' -v`. Generated runner profiles and binaries stay on the disposable runner; the workflow deliberately has no artifact upload step. Any actual native build or window error is a failing workflow, not a green claim.

**Still needed for general Google Drive acceptance:** native secure token/consent/download file writes, root folder creation, >200 pagination, Google Workspace exports, search escaping and actual owner-approved file management/refresh/reopen. The pre-existing local Strata receipt reports owner-approved OAuth and real read-only Drive listing; that does not establish these other workflows.
