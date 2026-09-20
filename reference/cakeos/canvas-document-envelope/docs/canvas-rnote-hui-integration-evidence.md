# Canvas / Rnote / HUI integration evidence

This document records the current graphical integration proof for the Canvas migration. It is evidence, not a claim that the production migration is complete.

## Branch isolation

- Integration branch: `integration/canvas-rnote-hui-preview`.
- HUI baseline: stacked from `platform/hui-linux-graphical-preview` without modifying that worker branch.
- Rnote bridge: stacked from `migration/canvas-rnote-poc`.
- `main` was not merged or modified by this integration proof.
- The approved CakeOS VM was not modified.

## Native engine boundary

- Rnote release: `v0.14.2`.
- Consumed Rust packages: `rnote-compose` and `rnote-engine` with default/UI features disabled.
- Native bridge format: Rust `cdylib` with C ABI version 1.
- HUI-facing pointer data: document-space X/Y, pressure and retained X/Y tilt.
- Proven tools: Pen and Eraser.
- Proven engine operations: incremental begin/update/end stroke, undo/redo, SVG render frame, `.rnote` save/reload.
- Rnote GTK/Libadwaita UI is not linked into the bridge. CI rejects GTK4/Libadwaita in both the Cargo dependency graph and `ldd` output.

### Render-frame coordinate contract

Rnote Infinite-layout storage bounds and exported SVG bounds are not the same thing.

`Engine::extract_document_content()` selects the page range containing content (or the origin page when empty), while `StrokeContent::gen_svg()` normalizes that selected rectangle to a zero-based SVG coordinate system. The Canvas ABI therefore carries the original content/page rectangle in Canvas document coordinates alongside SVG bytes whose viewBox begins at zero.

HUI keeps its viewport in document coordinates. When drawing the normalized SVG it subtracts the frame's document-space origin from the active HUI viewport to select the correct SVG source rectangle. The much larger Infinite-layout storage extent is deliberately not used as render-frame metadata.

The Rust test suite mechanically asserts that the render frame contains the seeded stroke, is smaller than the expanded Infinite storage extent, and that the generated SVG has a zero-based viewBox.

## Managed HUI boundary

The Linux HUI preview is .NET 10 / Avalonia. `CanvasNativeSession` loads the native ABI by library name and validates ABI version 1 before creating an engine. Rnote renderer output remains renderer-neutral SVG; the ABI supplies the corresponding document-space frame rectangle separately from the normalized SVG coordinate system.

HUI owns device/input arbitration and surface-to-document mapping. The graphical bridge accepts mouse and pen ink. Touch is intentionally not converted to ink: touch is reserved for HUI viewport gestures. Avalonia pen pressure and X/Y tilt are passed through the managed boundary; Rnote 0.14.2 consumes pressure but not tilt, so tilt remains a CakeOS document/API concern.

HUI also owns viewport state. Cursor-anchored mouse-wheel zoom changes only the HUI source rectangle, and touch or middle/right-button drag pans that viewport. Pan and zoom do not regenerate the Rnote SVG. Primary mouse/pen ink is transformed through the active HUI viewport into Rnote document coordinates.

The HUI scene uses the platform-neutral HUI `Image` component with the internal source token `cake-canvas://rnote-frame`. The Linux backend resolves only that Canvas token to an in-memory SVG image. No temporary document file is required for rendering.

The Linux preview adds `Svg.Controls.Skia.Avalonia` `12.0.0.17` as the SVG decoder. Its upstream Svg.Skia project is MIT licensed. This does not change Rnote's GPL-3.0-or-later compliance obligations.

## Runtime proof

Workflow: `Canvas Rnote HUI graphical preview`

### First pointer-interaction proof

Run `34148065189` / SHA `ace45d710607a8ed28ef585109019066415a3fa4` first proved a real X11 mouse drag through Avalonia -> HUI -> managed C ABI -> Rnote -> SVG -> HUI and produced a visible stroke.

### Viewport and corrected frame-coordinate proof

Authoritative viewport run:

- Run ID: `34157173549`
- Head SHA: `e873d5e864a0871fd921559100ef25637dc9701f`
- Runner: Ubuntu 24.04 GitHub Actions
- Display: X11 under Xvfb + Openbox
- Result: success
- Evidence artifact: `canvas-rnote-hui-graphical-preview`
- Artifact digest: `sha256:44edb7bb17b64ef806b15bee5e174071dbce93fcdd017a63db4ef7232cc0d548`
- Screenshot: 960x600

The gate mechanically proved:

1. pinned HUI donor staging succeeds;
2. Rnote core dependency graph contains no GTK4/Libadwaita;
3. the native Canvas `.so` builds and all Rust/FFI tests pass, including the render-frame coordinate-contract test;
4. native `ldd` closure resolves and contains no GTK/Libadwaita;
5. the .NET HUI Linux host builds with warnings-as-errors and the managed Canvas adapter;
6. the HUI host native closure resolves;
7. the managed host loads ABI version 1 and obtains/decodes a Rnote SVG frame;
8. HUI pointer and keyboard self-test passes;
9. a real X11 wheel gesture changes the HUI-owned cursor-anchored zoom;
10. a real X11 middle-button drag pans the HUI viewport without creating ink;
11. a subsequent real X11 primary-button drag is transformed through the changed viewport into Rnote document coordinates, committed as a new Rnote stroke, and re-rendered through the HUI image command;
12. the application exits 0 and a 960x600 window screenshot is captured.

The corrected run reports an exported content/page frame of `1123 x 1587`, rather than the unrelated expanded Infinite storage extent used by the earlier prototype. Runtime markers include:

```text
CANVAS_RNOTE_HUI_RENDER_READY abi=1 format=svg coordinate=document width=1123 height=1587
CANVAS_RNOTE_VIEWPORT_ZOOM zoom=5 x=28.075 y=7.44 width=224.6 height=88.658
CANVAS_RNOTE_VIEWPORT_ZOOM zoom=6.25 x=50.535 y=13.392 width=179.68 height=70.926
CANVAS_RNOTE_VIEWPORT_PAN_COMMITTED zoom=6.25 x=60.386 y=20.288 width=179.68 height=70.926
CANVAS_RNOTE_POINTER_STROKE_COMMITTED pointer=Mouse pressure=0.5 width=1123 height=1587 zoom=6.25
```

The captured screenshot was manually inspected after the run completed. The Canvas surface, Rnote background pattern, and post-viewport black ink stroke are visibly present. The HUI status reports `Pointer ink committed through HUI to Rnote; viewport 6.25x; undo available`.

Runs `34156255395` and `34156694393` were mechanically green while the viewport implementation was being developed, but manual screenshot inspection exposed incorrect/blank visual cropping. They are superseded by run `34157173549`; their green status is not used as visual acceptance evidence.

## Evidence classification

- Rnote source/API: **inspected**.
- Headless Rnote core: **built and tested**.
- Native C ABI `.so`: **built and tested**.
- Render-frame document/SVG coordinate contract: **built and tested**.
- Managed .NET native adapter: **built and runtime-loaded**.
- HUI image rendering of Rnote output: **graphically runtime-proven under Xvfb/X11**.
- Mouse ink through HUI into Rnote: **runtime-proven under Xvfb/X11**.
- HUI cursor-anchored wheel zoom: **graphically runtime-proven under Xvfb/X11**.
- HUI middle-button pan: **graphically runtime-proven under Xvfb/X11**.
- Touch pan path: **implemented but not hardware/runtime-proven**.
- Synthetic pressure path: **tested**.
- Real stylus pressure/tilt hardware: **not runtime-proven**.
- Touch/palm rejection: **not hardware/runtime-proven**.
- Dirty-region/tile renderer: **not implemented**; current contract renders a whole content/page SVG and HUI crops it.
- Wayland runtime: **not runtime-proven**.
- Approved CakeOS VM: **not modified or runtime-proven**.
- CakeOS package/image integration: **not implemented**.
- Boot validation: **not attempted**.

## Next acceptance gate

Expose the already-tested native `.rnote` save/load functions through the managed HUI adapter and prove an in-memory managed -> native save -> fresh native engine reload -> HUI render round trip. This remains an engine-payload/interchange proof; `.rnote` must not become the canonical CakeOS Canvas document format. After that, add HUI-native Pen/Eraser/Undo/Redo controls before moving to real Wayland stylus/touch validation and dirty-region performance work.
