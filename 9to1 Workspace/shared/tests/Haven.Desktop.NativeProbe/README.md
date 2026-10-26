# Native chat popup probe

Run the same `ChatAddMenuNativePopupTests.RunAsync(requireNativePopup: true)` flow used by the maintained regression, on a real desktop display:

```sh
dotnet run --project "9to1 Workspace/shared/tests/Haven.Desktop.NativeProbe/Haven.Desktop.NativeProbe.csproj" --configuration Release
```

The process uses Avalonia platform detection and the production CUI font/theme setup. Run it from an active X11 desktop session (with `DISPLAY` set). It exits nonzero if Avalonia falls back to the in-window overlay or if the native popup, anchoring, focus, interaction, or retirement assertions fail. The console runner shuts down explicitly after the probe completes.

To capture the mounted composer and actual native popup host as separate PNGs for visual inspection, set `HAVEN_NATIVE_POPUP_CAPTURE_DIR` to an output directory before running. The capture uses Avalonia `RenderTargetBitmap` on those live controls; it does not synthesize a replacement popup.
