# Canvas — drawing, objects and semantic composition — Canvas — deliberate spatial editing, not an accidental embedded panel; Canvas identity and tools (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1376 · v2 lines 6158–6158

## 10. Canvas — deliberate spatial editing, not an accidental embedded panel

### V2-B1377 · v2 lines 6160–6160

### UX27-CANVAS-001 — Canvas identity and tools

### V2-B1378 · v2 lines 6162–6162

**B/D.** Canvas is its own full drawing/visual-artifact surface with the canonical engine/model. It is not the unexplained white rectangle appended to every Native Boards page. Its document has explicit spatial/layer identities, viewport state and edit tools. When hosted as a Boards PageType, it remains the same artifact with the same supported semantics and a clearly intentional entry point.

### V2-B1379 · v2 lines 6164–6164

Select, pen, eraser, shape, text, pan and other declared tools have distinct active states and cursors. Tool switching does not create content. Unavailable capabilities are explained rather than represented by dead icons.

### V2-B1380 · v2 lines 6166–6166

### UX27-CANVAS-002 — Pointer capture, zoom and panning

### V2-B1381 · v2 lines 6168–6168

**B/D.** Drawing and manipulation use canonical document-space coordinates with consistent inverse viewport transforms. Pan moves the view, not the document objects. Pointer-anchored zoom retains the intended point under the pointer where supported; Fit computes actual bounds including relevant objects. Normal wheel/trackpad scrolling and modified zoom remain distinct.

### V2-B1382 · v2 lines 6170–6170

Test high/low zoom, negative coordinates, large documents, monitor-scale changes and out-of-window capture. Avoid strokes jumping to screen origin or resizing in physical pixels after a DPI change. Lost capture/cancel must leave no stuck drag state or spurious connecting stroke.

### V2-B1383 · v2 lines 6172–6172

### UX27-CANVAS-003 — Selection, layers and object transforms

### V2-B1384 · v2 lines 6174–6174

**B/D.** Hit testing follows z-order, visibility, locks and active tool. Lasso/marquee selects actual objects/strokes according to the declared intersection rule, with a visible selection preview. Move/resize/rotate/group/ungroup update live and commit once. Locked layers cannot be mutated through alternate shortcuts or AI actions.

### V2-B1385 · v2 lines 6176–6176

The layers/object panel reflects canonical order and hierarchy. Renaming, visibility and lock toggles have meaningful icons and accessible state; dragging a layer shows the actual destination and rejects cycles. Undo restores order and content. A layer hidden in the UI must not disappear from saved data or export without an explicit export-visibility rule.

### V2-B1386 · v2 lines 6178–6178

### UX27-CANVAS-004 — Brush and eraser fidelity

### V2-B1387 · v2 lines 6180–6180

**B/D.** Brush width/colour/opacity and supported pressure/tilt settings apply to the intended new/selected strokes. Segment and whole-stroke erasure remain distinguishable. Smoothing may improve presentation but must not introduce unacceptable lag, alter stored points unpredictably or discard intentional small marks. Mouse fallback works when stylus pressure is absent.

### V2-B1388 · v2 lines 6182–6182

Test editability after save/reopen and cross-app embedding, not just raster appearance. An imported image is not a replacement for native editable ink. Record which physical input devices and native platforms were actually tested.

### V2-B1389 · v2 lines 6184–6184

### UX27-CANVAS-005 — Bounds, export and recovery

### V2-B1390 · v2 lines 6186–6186

**B/D.** Export lets the user select whole document, page/artboard or selection with clear bounds, background and resolution/vector options appropriate to the format. Native save preserves layers, transforms, strokes and editable objects; static export is explicitly separate. Empty/offscreen content is not silently omitted because it was never materialised in the viewport.
