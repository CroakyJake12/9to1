# Observe the native Chat add-menu popup

Use the probe on an actual graphical session with the owning native build. Its purpose is to exercise the same add-menu popup interaction as the desktop regression with native popup behavior required. Keep the candidate identity, display and popup state with the receipt.

A headless pass can establish command routing while leaving window-system popup placement or focus unresolved. Record the platform and inspect opening, dismissal and keyboard focus restoration on the target desktop.

```sh
dotnet run --project "9to1 Workspace/shared/tests/Haven.Desktop.NativeProbe/Haven.Desktop.NativeProbe.csproj" --configuration Release
```

[Native preview](../../../../docs/development/live-preview.md) · [Native host boundaries](../../docs/App-Editing-Guide/BROWSER.md)
