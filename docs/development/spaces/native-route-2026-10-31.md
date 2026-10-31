# Actual Spaces native route — 31 October 2026

Root owns the build and native run. This is launcher guidance from current source, not a launch receipt.

- Build ordinary `9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj`, target net10.0 on Linux, with `ProductInitialApp` unset. Assembly name is `Haven` and normal output is `bin/Release/net10.0/Haven.dll` (use actual build output if platform/config overrides change it).
- Existing `/workspace/scratch/boards-native-tools/native.py` can launch that DLL despite its Boards help text. Its two required launch values are absolute dotnet runtime path and absolute DLL path. Use a fresh run name. It sets DISPLAY `:93`, `HAVEN_DATA_DIR` to `runs/<name>/profile` and independent XDG directories; it records PID/window/profile/argv in `launch.json`. No HOME override or user profile copy.
- No `--spaces` flag exists. Plain Linux Desktop starts the original Home composition through `StartOriginalWindowsHomeAsync` -> `home.StartOriginalAsync`; do not invent native Root locators. Real profile/service startup checks remain required.
- Once Home is ready, invoke the actual address bar with `native.py key <run> ctrl+l`, type `9to1://spaces`, then `native.py key <run> Return`. Ctrl+L targets the actual `BrowseAddressBox` (`App.UnifiedWorkspaceHost.cs`).
- This route uses the production `NativeAppContentRegistry` Spaces factory -> `SpacesBrowseAppContentSession` -> `SpacesHomePage` and its authored `SpacesHome.cui`. It is not the legacy `NativeSpacesPage` or a demo.
- Bare route currently leaves the actual summary for default Chat; selecting the Chat rail button invokes the existing direct Space Open action and mounts NewChatPage. Capture both states. The bare-entry behavior is an additional unverified UX27-SPACES-001 gap; no workaround is claimed as its fix.
- Top rail action has accessible name `Manage Spaces`. It opens the canonical manager. Inside, `Create a custom Space` creates through the registry and opens existing details; archive/restore then uses current canonical identity. Capture current Spaces context, source rows and recovery controls as well as the resulting state.
- If original Home/profile startup fails, retain the startup failure screenshot and log. Do not bypass admission or synthesize a profile/service to obtain a passing visual.

Use native.py capture/status for the same run. Close through the actual window lifecycle after checks, retaining the profile and screenshots. A native X11 result in this environment is distinct from the owner's installed PC and the separate Windows/browser acceptance matrix.
