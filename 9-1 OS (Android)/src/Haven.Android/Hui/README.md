# Android HUI primitive renderer

AndroidHuiCanvasRenderer consumes the shared draw command stream and paints the supported primitives on Android Graphics Canvas. Use this path to test command translation and token resolution on Android. The desktop’s authored CUI routes use the native Haven host and their own tests. [Canvas renderer](AndroidHuiCanvasRenderer.cs)

`HavenSceneRenderer` → `IReadOnlyList<HavenDrawCommand>` → `AndroidHuiCommandAdapter` → `AndroidHuiCanvasRenderer` → `Android.Graphics.Canvas`.

The adapter consumes the shared `Haven.UI` drawing and surface-metrics contracts. It does not define a second UI model.

## Current checkpoint

The Android canvas executor supports transform/clip state, rounded-rectangle fills and strokes, lines, and ellipses. HUI text, text selection/caret, geometry, images, icons, shadows, and glows are reported as unsupported by the adapter rather than approximated.

The existing Avalonia Android host remains the active product path; this checkpoint does not rewire routes or claim visual parity, an Android OS/AOSP source tree, emulator behavior, or physical-device behavior. Token brushes must be supplied by an `IAndroidHuiTokenColorResolver`; unresolved tokens are skipped and reported at runtime.
