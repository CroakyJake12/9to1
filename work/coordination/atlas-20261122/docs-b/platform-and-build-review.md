# platform-and-build review

All 24 assigned paths were inspected against the current checkout. New relative Markdown targets resolved, fenced examples stayed balanced relative to their source, and the scoped diff passed `git diff --check`. The documentation review did not execute a device, build, deployment or service. Each item below records the scope of the edit.

- `.github/scripts/native16-arm64/PIPELINE.current.md` (authenticated input: `.github/scripts/native16-arm64/PIPELINE.md`) — Building and checking an ARM64 candidate
- `.github/scripts/native16-arm64/chromium-native/README.current.md` (authenticated input: `.github/scripts/native16-arm64/chromium-native/README.md`) — Pinned Chromium ARM64 runtime
- `9-1 OS (Android)/src/Haven.Android/Hui/README.md` — Android HUI primitive renderer
- `9to1 OS/HUI/LinuxHost/README.md` — Linux HUI preview host
- `9to1 OS/HUI/README.md` — HUI local provider contracts
- `9to1 OS/HUI/WindowsHost/README.md` — Windows HUI preview host
- `9to1 OS/compatibility/winboat/README.md` — WinBoat guest integration
- `9to1 OS/compatibility/wine/EXE-CHOOSER.md` — Open a Windows executable with Wine or WinBoat
- `9to1 OS/compatibility/wine/PACKAGING.md` — Windows compatibility package contract
- `9to1 OS/compatibility/wine/README.md` — 9to1 OS Windows compatibility broker
- `9to1 OS/docs/LOCAL-CAKEUPDATE.md` — Installing a local .cakeupdate bundle
- `9to1 OS/docs/SHARED-PLATFORM-FOUNDATION.md` — Shared 9to1 OS platform contracts
- `9to1 OS/docs/WORKER-CONTRACT.md` — 9to1 OS worker scope and evidence
- `9to1 OS/docs/parity/CANVAS-RNOTE-PARITY.md` — Donor baseline and capability matrix
- `9to1 OS/docs/parity/PARITY-WORKFLOW.md` — Reconciled earlier branch-only handoff with current canonical-main ownership; retained target-device parity steps.
- `9to1 OS/packaging/llamacpp/PACKAGING.md` — Model-free llama.cpp Debian package
- `9to1 OS/tooling/book4edge/README.md` — Samsung Galaxy Book4 Edge preparation
- `9to1 OS/tooling/book4edge/battery-test/README.md` — Book4 Edge battery telemetry test
- `9to1 OS/tooling/book4edge/installer/README.md` — Book4 Edge installer handoff
- `docs/packaging/package-preload.md` — Tied preload recipe to the actual release manifest and producer hash.
- `eng/README.md` — Repository build commands
- `eng/browser/README.md` — Validating browser acceptance evidence
- `eng/release-validation/boards-startup/README.md` — Original Windows probe and control coverage
- `eng/service-validation/README.md` — Corrected superseded statement that CAKE ID does not exist in this checkout; separated source fixtures from deployed checks.
