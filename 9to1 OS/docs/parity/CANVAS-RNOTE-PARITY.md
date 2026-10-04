# Canvas / Rnote donor parity

Donor: Rnote 0.14.2 (`https://github.com/flxzt/rnote` rev `8cac558fa73ee63d958cec05124ea2fdbd7d608d`).
Donor UI inventory source: `crates/rnote-ui` (appwindow, mainheader, penssidebar,
canvasmenu, colorpicker, strokewidthpicker, workspacebrowser, dialogs,
penssidebar brush/eraser/selector/shaper/typewriter pages) plus reference
screenshots under `crates/rnote-ui/data/screenshots` (copied to the
controller's overnight dir for comparison).

CakeOS layers under review:
- ABI: `apps/canvas/rnote-poc` C ABI (currently v3)
- Bridge: `HUI/CanvasApp/CanvasBridge.cs` (`CanvasNativeSession`)
- Controller: `HUI/CanvasApp/CanvasController.cs` (+ viewport partial)
- UI: `HUI/CanvasApp/CanvasRoot.cs` (HUI tree, shared renderer)

Statuses: PASS / PARTIAL / MISSING / BLOCKED BY HUI / BLOCKED BY BRIDGE / BLOCKED BY UPSTREAM.

## Capability matrix

| # | Capability (Rnote 0.14.2) | Engine | ABI | Bridge | Controller | UI | Status |
|---|---|---|---|---|---|---|---|
| 1 | Pen brush, pressure, tilt | yes | yes | yes | yes | yes | PASS |
| 2 | Highlighter/marker | yes | yes | yes | yes | yes | PASS |
| 3 | Pen/marker colour | yes (`SmoothOptions.stroke_color`) | yes (v3) | yes | yes (palette+memory) | yes (swatches) | PASS |
| 4 | Pen/marker width | yes (`stroke_width`) | yes (v3) | yes | yes (slider 1-48) | yes | PASS |
| 5 | Eraser width 1-500 | yes | yes (v3) | yes | yes | yes (shared slider) | PASS |
| 6 | Eraser trash vs split | yes (`EraserStyle`) | yes (v3) | yes | yes | yes (Split toggle) | PASS |
| 7 | Selector/lasso strokes | yes | yes | yes | yes | yes (tool only) | PARTIAL |
| 8 | Selection trash / duplicate | yes (`trash_selection`, `duplicate_selection`) | no | no | no | no | MISSING |
| 9 | Shapes rect/ellipse/line/arrow | yes | yes | yes | yes | yes (chips) | PASS |
| 10 | Shape stroke colour/width | yes (shaper `smooth_options`) | yes (v3) | yes | yes | yes | PASS |
| 11 | Shape fill colour | yes (`fill_color`) | no | no | no | no | MISSING |
| 12 | Brush styles solid/marker/textured | yes (3 styles) | partial (marker via highlighter tool) | partial | partial | partial | PARTIAL |
| 13 | Pressure curve | yes (`PressureCurve`) | no | no | no | no | MISSING |
| 14 | Width presets | UI-level (donor `strokewidthpicker`) | n/a | n/a | slider only | slider only | PARTIAL |
| 15 | Undo / redo + enabled states | yes | yes | yes | yes | yes | PASS |
| 16 | Zoom / pan / fit | yes (camera) | yes | yes | yes (HUI-side viewport) | yes (wheel/slider/buttons/fit) | PASS |
| 17 | SVG render frame | yes | yes | yes | yes (viewBox crop) | yes | PASS |
| 18 | New / Open / Save / Save As (.rnote) | donor workspace UI | save/open/new only | yes | yes (atomic, truthful status) | yes (menu+dialogs) | PASS |
| 19 | Export SVG / PDF / Xopp | yes (`DocExportFormat`) | no (rnote-bytes only) | no | no | no | MISSING |
| 20 | Import (files, drag-drop, clipboard image) | yes (`engine/import.rs`) | no | no | no | no | MISSING |
| 21 | Clipboard stroke copy/paste | yes (engine ops) | no | no | no | no | MISSING |
| 22 | Typewriter tool | yes (`Pen::Typewriter`) | no | no | no | no | MISSING |
| 23 | Document pages add/remove | yes | no | no | no (single surface) | no | MISSING |
| 24 | Background colour/pattern/format | yes (doc background config) | no | no | placeholder paper | no | MISSING |
| 25 | Tabs / multi-document | donor UI-level | n/a | n/a | single document | no | MISSING |
| 26 | Workspace browser (files/folders) | donor UI-level | n/a | partial (paths) | single path | no | MISSING |
| 27 | Touch drawing | donor supports touch-as-pen | samples carry no device policy | HUI policy: touch pans | policy | policy | PARTIAL (policy gap, reversible) |
| 28 | Stylus button shortcuts | donor UI-level | n/a | n/a | no | no | MISSING |
| 29 | Document settings (format/size/margins) | yes | no | no | no | no | MISSING |
| 30 | App menu / settings panel | donor UI-level | n/a | n/a | doc menu only | doc menu only | PARTIAL |

## Remaining mismatches (ranked)

1. Export SVG/PDF/Xopp — engine-ready, needs ABI+controller+UI (Save As type picker).
2. Selection actions (trash/duplicate) — engine-ready, needs ABI+controller+contextual UI.
3. Shape fill colour — trivial ABI extension (`fill_color`), controller slot, UI toggle.
4. Background colour/pattern — needs ABI exposure of doc background config + page settings UI.
5. Import + clipboard image — needs ABI byte-import + drag-drop host plumbing.
6. Pages/tabs/workspace — largest gap; needs document-model work beyond the bridge.
7. Typewriter, pressure curve, textured brush, width presets — smaller gaps.
8. Touch-as-pen policy — product decision + input routing change.

## What the current CakeOS shell must NOT be mistaken for

The `CanvasRoot` HUI tree on `emergency/windows-apps-book4edge` is a
CakeOS-native application shell, NOT a donor reconstruction. It is the
integration base (engine, bridge, controller, persistence, ARM64 build),
pending faithful Rnote-parity UI from the desktop reconstruction track.
