# Inspect the running desktop interface

The debug inspector is for viewing the control tree, layout values and resource choices of a running development build. First identify the actual tab, host and selected theme. Inspect the nearest CUI control and trace its authored property or semantic resource to the component that supplies it.

An inspector adjustment affects that running instance. Reproduce the change in the owning CUI source or framework, then reload the managed candidate to verify that it survives. Keep debug tooling out of shipping builds.

## Shortcuts

- **F12** toggles the developer-tools window.
- **Ctrl+Shift+C** starts element-picking mode.
- **Ctrl+F** focuses the visual-tree filter while the tools window is active.
- **Escape** cancels element-picking mode.

## Source lookup limitations

Avalonia control templates generate runtime elements such as `ContentPresenter`, `Border`, and template panels that may not exist directly in Haven's authored AXAML. For reliable source navigation, select the nearest named parent or add an `x:Name` to the authored control.

The tools are activated only in `DEBUG` builds. The implementation uses public Avalonia APIs and does not reference `AvaloniaUI.DiagnosticsSupport` or `Avalonia.Diagnostics`.

[CUI ownership](ui/haven-ui.md) · [Managed preview](../../../docs/development/live-preview.md)
