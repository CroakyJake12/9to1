# Haven Files: current-source Ubuntu linux-x64 candidate — 18 November 2026

This is an **owner-run source-to-runnable candidate** for the *existing* native `Haven.Desktop` / Haven Files implementation. It does not create a disconnected frontend, copy Drive storage, authenticate Google, deploy to a server, modify an existing Haven profile or publish a binary. A compiled and verified application is **not** a completely accepted Google Drive replacement.

## Build the real host from an exact clean checkout

Prerequisites: existing trustworthy CroakyJake12/9to1 checkout on Ubuntu x64; Git, Python 3.10+, tar, internet access for official NuGet dependencies, a compatible official .NET 10 SDK selected by repository `global.json` (declares `10.0.301` with `latestFeature`) and the original app's Linux native libraries. The app is framework-dependent and needs a compatible .NET 10 runtime to launch.

```bash
cd /absolute/path/to/9to1
git submodule update --init --recursive --depth 1 -- \
  framework/CUI/vendor/Avalonia/external/XamlX \
  framework/CUI/vendor/Avalonia/external/Avalonia.DBus
REV="$(git rev-parse HEAD)"
git status --short                 # must show no changes
dotnet --version                   # compatible official 10.0 SDK

mkdir -p -m 700 "$HOME/.local/share/9to1-native-candidates"
chmod 700 "$HOME/.local/share/9to1-native-candidates"
python3 -B eng/development/native_files_source_candidate.py \
  --repo "$PWD" --expect-revision "$REV" \
  --candidate-dir "$HOME/.local/share/9to1-native-candidates/haven-$REV"
```

The owner selects a new, user-controlled mode-0700 directory **outside the repository**. The helper requires an exact lowercase 40-character source commit and a clean Git checkout before and after compilation. It runs the existing `9to1 Workspace/shared/eng/publish-haven-linux.sh`, not a new mock app, with a private disposable HOME/XDG/NuGet cache. It allows through only benign compiler environment variables; Google, GitHub, other account tokens and the owner's existing private profile are not inherited. It refuses an incompatible selected SDK, unsafe/symlinked parent, existing output, changed source, bad archive, non-Linux-x64 host, archive corruption and mismatched publish/extraction bytes.

The original publisher now writes stdout/stderr only into a newly created private mode-0600 `publish.log` inside the candidate directory. On build failure the command reports its exit status without printing potentially sensitive local paths or output; inspect that local log yourself to diagnose SDK, NuGet or native compile errors. A timeout signals only the build-created process group. No failed run is labelled launch-ready.

A successful build preserves its *actual* tarball under `artifacts/haven-linux-linux-x64.tar.gz`, uses the existing FD-anchored `native_files_package_admission.py` to admit and privately extract that exact package, verifies that every regular file byte matches the original publish, and writes a mode-0600 `receipt.json` with exact source SHA, SDK, tarball SHA-256 and `launch_command`. An archive digest calculated from your own build identifies its bytes, but does not independently sign their origin. No receipt is created on failure; failed private build roots may remain for user inspection. Never reuse a digest from another source revision.

Launch the recorded executable explicitly in an Ubuntu graphical session, for example by running the emitted `launch_command`. **It does not perform Google consent automatically.** If approved, configure the separate Desktop OAuth client locally with `native_files_drive_setup.py` / `native_files_drive_import.py` and check it with `native_files_drive_preflight.py`; do not put secrets, browser consent URLs or refresh tokens in Git or chat. An existing ChatGPT Google Drive connector is *not* an app OAuth authorization.

## Verification tiers

The script is a build-and-admission command; it reports `oauth_checks: NOT_RUN`, `real_drive_checks: NOT_RUN` and `graphical_launch: NOT_RUN`. The independent graphical runtime is checked in the existing serialized, nondeploying Linux CI workflow under `files-candidate-main`. That workflow must preserve its real process-owned X11 native-window checks. The public repository's workflow does **not** upload artifacts: this source-build route is an owner-accessible alternative, not a current binary download URL.

Fixture suite:
```bash
python3 -B -m unittest discover -s eng/development \
  -p 'test_native_files_source_candidate.py' -v
```

Before first publication, **14/14 local** disposable Git repository + actual subprocess + fake Bash publisher + synthetic ELF/tar checks passed. The authoring sandbox used a deliberately local stand-in for the existing archive-admission library: this suite's local outcome alone does not prove actual package admission or native compilation. Existing `native_files_package_admission` tests and the genuine X11 build/window CI receipts are separate evidence. Only an exact-commit CI build can establish that the command really compiled the current Haven app and opened a window.

## What a first-use Drive acceptance still needs

The Strata-owned native `9to1 Workspace/Files/**` implementation previously reported owner-consented OAuth and real Drive listing in issue #27, but this command does not repeat that check. Still not accepted: safe native consent-URL/token/download writes; folder-root create, names containing apostrophes/backslashes in search, more than 200-item pagination, supported Google Workspace exports, uploads and download, rename/move, recoverable trash/restore, cancellation/network failures, expiry/revocation, refresh/close/reopen and permission handling. Live mutations require a separately authorised disposable test-owned account; the cloud build cannot silently operate on the owner's real Drive.

Latest scoped receipts, precise code ownership, test outcomes and remaining blockers are retained in [the existing coordination issue](https://github.com/CroakyJake12/9to1/issues/27).
