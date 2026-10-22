# Provenance: CakeOS Canvas Rnote PoC

## Current controlled Canvas backend

This is an existing Rnote-derived engine bridge, not a separate drawing engine.
The current first-party Canvas application is authored in canonical CUI. Its typed
managed boundary is `../RnoteCanvasEngine.cs`; CUI/native adapters own presentation
and input, while this crate reuses Rnote's engine, geometry, rendering, history
and file-format implementations. The historical HUI proof below remains provenance,
not a current application-authoring path or native acceptance result.

The actual Cargo engine and compose dependencies remain **v0.14.2 at
`8cac558fa73ee63d958cec05124ea2fdbd7d608d`**, with default features disabled.
The official upstream release API reported v0.14.2 as its latest stable release
when checked on 22 October 2026. The project's selected donor manifest instead
names `1a728d6a85db3528f9c79dc0990700e91b22696f`, whose upstream workspace
declares v0.15.0 and changes the geometry dependency/API. `eng/donor-sources.json`
already records this compatibility conflict. It remains unresolved: this bridge
does not claim a v0.15 upgrade, and ABI version 3 does not attest a donor revision.
Persisted Canvas producer labels and existing files require explicit compatibility
and provenance review; they must not be silently relabelled or stripped.

The added native operations reuse public Rnote APIs:

* Live stroke keys come from `Engine::take_snapshot` and retained slotmap keys.
* Selection export filters a detached snapshot and uses the donor loader/saver;
  it never replaces the original engine or its undo/redo history.
* Read-only Quick Eraser targeting uses genuine donor hitboxes and layer/chronology
  ordering. A miss is distinct from a failure; this operation does not delete ink.
* Xournal++ import uses `EngineSnapshot::load_from_xopp_bytes`, with bounded gzip
  expansion and validated DPI at the C boundary.

Selection API v1 and Quick targeting API v1 cover those operation families only.
Keyed delete/translate and detached split-erase materialisation are still absent.
Both inspected donor revisions keep the required store mutation methods private
to their crate; a maintained, individually reviewed upstream extension is needed.
Loading a replacement snapshot into the live engine would reset history and is
not a supported mutation workaround.

`include/cakeos_canvas.h` records the actual signatures, key packing and ownership
contract. Callers must supply valid live handles/pointers and serialize access.
The in-module tests exercise real donor import/export, key persistence, overlap
targeting, original history and refusal paths; source presence is not a passing
test result. Cargo compilation, full managed ABI coverage, dynamic-library closure,
producer identity, target-platform runtime and packaged Canvas acceptance require
independent retained evidence. No ready native/ARM application is implied here.

Rnote is **GPL-3.0-or-later**. Preserve the exact upstream source, `LICENSE`,
modifications, dependency notices and corresponding-source obligations when staging
or distributing this bridge. This note does not establish completed licence or
native dependency acceptance. Do not enable Rnote's optional GTK/UI dependencies
or ship its donor UI as the first-party Canvas shell.

## Historical migration proof (retained)

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
