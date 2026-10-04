# CakeOS Canvas Rnote Proof of Concept

This crate provides a renderer-neutral bridge between the Rnote 0.14.2 engine core and the CakeOS HUI layer. It deliberately depends on `rnote-engine` and `rnote-compose` with default features disabled — no GTK, Libadwaita, or `rnote-ui` is imported.

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

- **Engine lifecycle**: `cake_canvas_engine_new`, `cake_canvas_engine_free`, `cake_canvas_engine_from_rnote`
- **Rnote tools**: `cake_canvas_set_stroke_tool` (pen, marker highlighter, eraser, selector/lasso, and shape), plus `cake_canvas_set_shape`
- **Stroke events**: `begin_stroke`, `update_stroke`, `end_stroke`
- **History**: `undo`, `redo`, `can_undo`, `can_redo`
- **Viewport**: `cake_canvas_set_viewport_size`, `cake_canvas_zoom_to`, `cake_canvas_pan_by`, and `cake_canvas_set_viewport_center`
- **Rendering**: `cake_canvas_render_frame` (returns SVG + document-space bounds)
- **Persistence**: `cake_canvas_save_rnote`, `cake_canvas_buffer_release`

All coordinates are in **Canvas document space** (Rnote's infinite coordinate system). The render frame carries the original document-space content rectangle while the SVG bytes are normalized to a zero-based viewBox. HUI subtracts the frame origin when selecting a source rectangle and keeps its viewport in document coordinates.

## Building

```bash
cd apps/canvas/rnote-poc
cargo build --release
# Output: target/release/cakeos-canvas-rnote (cdylib + rlib)
```

## Testing

```bash
cargo test
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
