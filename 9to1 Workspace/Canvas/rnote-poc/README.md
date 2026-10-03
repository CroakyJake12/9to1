# CakeOS Canvas Rnote Proof of Concept

This crate provides a renderer-neutral C ABI over controlled Rnote 0.15 under `../Source/Rnote`, revision `1a728d6a85db3528f9c79dc0990700e91b22696f`. Local `rnote-engine` and `rnote-compose` default features are disabled. GTK/Libadwaita UI is not required; native GLib/GIO, Cairo, Pango, fonts and libxml remain build/runtime dependencies. Preserve the donor GPL licence and provenance.

## Architecture

```
┌─────────────────┐      ┌──────────────────────┐      ┌─────────────────┐
│   HUI Input     │─────▶│  C ABI Boundary      │─────▶│  Rnote Engine   │
│   (pointer      │      │  (cakeos_canvas.h)   │      │  (headless)     │
│   samples)      │      │                      │      │                 │
└─────────────────┘      └──────────────────────┘      └─────────────────┘
                                │
                        ┌───────▼───────┐
                        │  Renderer-    │
                        │  Neutral Frame│
                        │  (SVG +       │
                        │  bounds)      │
                        └───────────────┘
```

## Exports

The stable C ABI (`include/cakeos_canvas.h`) exposes:

- **Engine lifecycle/import**: `cake_canvas_engine_new`, `cake_canvas_engine_free`, `cake_canvas_engine_from_rnote`, `cake_canvas_engine_from_xopp`
- **Rnote tools**: `cake_canvas_set_stroke_tool` (pen, marker highlighter, eraser, selector/lasso, and shape), plus `cake_canvas_set_shape`
- **Stroke events**: `begin_stroke`, `update_stroke`, `end_stroke`
- **History**: `undo`, `redo`, `can_undo`, `can_redo`
- **Viewport**: `cake_canvas_set_viewport_size`, `cake_canvas_zoom_to`, `cake_canvas_pan_by`, and `cake_canvas_set_viewport_center`
- **Rendering**: `cake_canvas_render_frame` (returns SVG + document-space bounds)
- **Persistence**: `cake_canvas_save_rnote`, `cake_canvas_buffer_release`

All coordinates are in **Canvas document space** (Rnote's infinite coordinate system). The render frame carries the original document-space content rectangle while the SVG bytes are normalized to a zero-based viewBox. HUI subtracts the frame origin when selecting a source rectangle and keeps its viewport in document coordinates.

## Building

```bash
cd "9to1 Workspace/Canvas/rnote-poc"
cargo build --locked --release
# Linux output: target/release/libcakeos_canvas_rnote_poc.so (cdylib + rlib)
```

## Testing

```bash
cargo test --locked
```

The tests verify:
- Pressure clamping and tilt preservation at the HUI boundary
- Stroke event lifecycle enforcement (no mid-stroke tool changes, undo, etc.)
- Undo/redo history participation
- Render frame coordinate metadata
- Rnote marker highlighter, selector/lasso, and shape builders
- Eraser trashing + history restoration
- Camera zoom/pan with document-coordinate preservation
- Atomic Rnote save → reopen round-trip with no retained temporary file
- Genuine Xournal++ structured import and native rendering round trip

Managed Canvas checks ABI 3 and owns handles/buffers with SafeHandle. Rnote's persisted generated geometry rounds to three decimals; import/preview uses durable donor precision and native `.9to1c` samples retain original pressure/tilt. SVG resource IDs are generated afresh; semantic comparisons normalize them while retaining geometry/style/reference topology.

Linux tests/library builds do not establish full donor parity, accelerated spatial rendering or Windows/Android/web package support.
