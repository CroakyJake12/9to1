# Provenance: CakeOS Canvas Rnote PoC

**HUI source boundary**: `CroakyJake12/CakeAI@7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb:src/Haven.UI`, staged through `HUI/stage-donor.*`. The commit and source path are pinned in `havenos.lock` and are not a submodule.

**Pinned Rnote dependencies**:
- `rnote-engine` v0.14.2 (`8cac558fa73ee63d958cec05124ea2fdbd7d608d`, package `rnote-engine`, default-features=false)
- `rnote-compose` v0.14.2 (`8cac558fa73ee63d958cec05124ea2fdbd7d608d`, package `rnote-compose`, default-features=false)

**Migration boundary**: This crate sits at the HUI → Rnote boundary. It owns:
- Canvas document coordinate normalization
- Rnote tool/style mapping (pen, marker highlighter, eraser, selector/lasso, and shape)
- SVG export via `rnote_engine::engine::export`
- Rnote payload save/load via `EngineSnapshot`
- Rnote camera viewport, zoom, and pan state
- Same-directory atomic Rnote replacement for Canvas file persistence

It does NOT own:
- GTK/Libadwaita integration (explicitly forbidden)
- Input device arbitration (HUI owns this)
- Platform windowing (HUI owns this)

**Acceptance criteria**:
- `CanvasManagedBoundaryProof.Run()` passes (see `HUI/LinuxHost/Canvas/CanvasManagedBoundaryProof.cs`)
- All Rust tests pass (`cargo test`)
- Native bridge tests pass (`cargo test --test native_bridge`)
