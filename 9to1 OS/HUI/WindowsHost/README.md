# CakeOS HUI Windows host

Windows port of the HUI preview host. It runs the **real** Cake engines, not mocks:

- Canvas: `apps/canvas/rnote-poc` Rust cdylib (`cakeos_canvas_rnote_poc.dll` on
  Windows, ABI v2) through `Canvas/CanvasNativeBridge.cs`, rendered as SVG via
  Avalonia + `Svg.Controls.Skia.Avalonia` — the same boundary the Linux host uses.
- Layout/input/product code: shared `HUI/Platform` + vendored `Haven.UI` sources,
  unchanged. Layout passes `HavenPlatform.Windows`.
- External CUI surfaces: application assemblies implement the shared,
  platform-neutral `CakeOS.Platform.IHuiRootProvider` contract. The Windows host
  adapts an `IHuiRootElement.NativeRoot` `Haven.UI.Components.Page` into its
  existing `HuiAppSurface`, so the host has no app-level Windows UI project
  references.

## Deltas from `HUI/LinuxHost` (deliberately small)

- Namespace `CakeOS.HuiLinuxHost` -> `CakeOS.HuiWindowsHost`, assembly
  `cakeos-hui-windows-preview`.
- `HavenPlatform.Linux` -> `HavenPlatform.Windows` in the preview surface.
- Settings root: `%LOCALAPPDATA%\CakeOS\windows-host` (same atomic
  `VersionedSettingsStore`, tmp-write + atomic replace).
- Update identity: `StaticCakeOsSystemInfoProvider("0.0.0-windows-preview", arch)`.
  `PkexecCakeUpdateInstaller` is reused unchanged — it already fails gracefully
  off-Linux, so no privileged path can trigger on Windows.
- Update history: `%LOCALAPPDATA%\CakeOS\windows-host\updates`.
- Root-provider ABI: `cakeos.hui.windows-root-provider` v1
  (`HUI/Platform/HuiWindowsHostAbi.cs`, additive; the provider interface itself
  is shared with Linux).
- Touch keeps Linux parity: touch (and middle/right-drag) pans the Canvas
  viewport; pen and primary-mouse contact draw with the selected Rnote tool.

## Canvas documents (explicit, honest persistence)

- Location: `%USERPROFILE%\Documents\CakeOS\Canvas\canvas.rnote`
- Save: Rnote payload -> `<file>.tmp` + flush -> atomic `File.Move` overwrite ->
  size re-verified. The status line only says "Saved …" after verification.
- Open: bytes restored through `CanvasNativeSession.FromRnote` and render-checked
  **before** replacing the live session, so a corrupt file can never destroy the
  open document. Missing file is reported, not silently created.
- Dirty tracking: stroke commit / undo / redo mark "unsaved changes".

## Build (native ARM64)

```powershell
# Rust engine -> cakeos_canvas_rnote_poc.dll (needs MSVC link.exe)
cargo build --release --manifest-path apps/canvas/rnote-poc/Cargo.toml
# Managed host (self-contained win-arm64), DLL beside the exe for DllImport
dotnet publish HUI/WindowsHost/CakeOS.HuiWindowsHost.csproj -c Release -r win-arm64 --self-contained true -o artifacts\hui-windows-host\publish
```

## Run

```powershell
# Canvas (real Rnote engine)
$env:CAKEOS_HUI_CANVAS_PREVIEW = "1"
.\artifacts\hui-windows-host\publish\cakeos-hui-windows-preview.exe
# headless smoke: CAKEOS_HUI_PREVIEW_SELF_TEST=1 + CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS=8000

# Application-owned CUI surface
$env:CAKEOS_HUI_CANVAS_PREVIEW = $null
.\artifacts\hui-windows-host\publish\cakeos-hui-windows-preview.exe `
  --hui-root-provider-assembly C:\CakeOS\Apps\Application.dll `
  --hui-root-provider-type CakeOS.Applications.ApplicationRootProvider
```

Canvas takes precedence when `CAKEOS_HUI_CANVAS_PREVIEW=1`.
