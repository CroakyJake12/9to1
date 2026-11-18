# Canvas — drawing, objects and semantic composition — Canvas; Purposeful spatial input and exported content (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2831 · v2 lines 14984–14984

## Canvas

### V2-B2832 · v2 lines 14986–14986

### UX27-TEST-087 — Purposeful spatial input and exported content

### V2-B2833 · v2 lines 14988–14988

**Requirements:** UX27-CANVAS-001, UX27-CANVAS-002, UX27-CANVAS-003, UX27-CANVAS-004, UX27-CANVAS-005 · **Status:** NOT RUN

### V2-B2834 · v2 lines 14990–14990

**Given:** Intentional Canvas artifact with layers, shapes and brush content.

### V2-B2835 · v2 lines 14992–14994

**When:**
1. Pan/zoom; select/move/reorder layers; draw/erase and undo.
2. Save/reopen and inspect export.

### V2-B2836 · v2 lines 14996–14999

**Then:**
- Spatial coordinates and input remain stable.
- Real layers/brush data persist rather than a screenshot only.
- Export contains the full intended region, not only materialised viewport content.
