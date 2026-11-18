# Write — document authoring — Write; Real document caret and multi-page editing (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1348 · v2 lines 5754–5754

Write MUST use Home’s Shared Productivity Engine for every shared object type it supports. Shared objects copied between Write, Present, Canvas, Boards and Spaces MUST preserve the common object schema, formatting and behaviour rather than being converted into Write-only representations.

### V2-B2789 · v2 lines 14855–14855

## Write

### V2-B2790 · v2 lines 14857–14857

### UX27-TEST-079 — Real document caret and multi-page editing

### V2-B2791 · v2 lines 14859–14859

**Requirements:** UX27-WRITE-001, UX27-WRITE-003 · **Status:** NOT RUN

### V2-B2792 · v2 lines 14861–14861

**Given:** Real multi-page mixed-format Writer-backed document.

### V2-B2793 · v2 lines 14863–14866

**When:**
1. Click/select/type across page boundaries.
2. Apply a style and undo.
3. Save and reopen.

### V2-B2794 · v2 lines 14868–14871

**Then:**
- Caret/selection belong to the rendered document engine, not an unrelated text field.
- Pagination/styles reflect the actual model.
- Reopened document retains content and formatting.

### V2-B2795 · v2 lines 14873–14873

### UX27-TEST-080 — Contextual commands and inspector remain synchronised

### V2-B2796 · v2 lines 14875–14875

**Requirements:** UX27-WRITE-002, UX27-WRITE-004, UX27-WRITE-005, UX27-WRITE-006 · **Status:** NOT RUN

### V2-B2797 · v2 lines 14877–14877

**Given:** Document with headings, image, table and tracked/review content where supported.

### V2-B2798 · v2 lines 14879–14881

**When:**
1. Navigate outline; format text/table/image from toolbar and inspector.
2. Inspect undo and document theme isolation.

### V2-B2799 · v2 lines 14883–14886

**Then:**
- Daily commands are usable without a permanent ribbon or inspector command dump.
- Outline selects actual content.
- All controls reflect one engine state and app theme does not rewrite authored styling.

### V2-B2800 · v2 lines 14888–14888

### UX27-TEST-081 — Import export and recovery are real

### V2-B2801 · v2 lines 14890–14890

**Requirements:** UX27-WRITE-007, UX27-DATAFLOW-005 · **Status:** NOT RUN

### V2-B2802 · v2 lines 14892–14892

**Given:** Supported external format with tables/media and a simulated interrupted save.

### V2-B2803 · v2 lines 14894–14897

**When:**
1. Import, edit and export through supported format.
2. Open exported artifact independently.
3. Recover interrupted editing.

### V2-B2804 · v2 lines 14899–14902

**Then:**
- Round-trip checks actual output, with disclosed unsupported features instead of fake parity.
- Recovery preserves edits.
- Export is not labelled complete before artifact validation.

### SCALABILITY-B0014 · scalability lines 42–42

WRITE-SCALE-201 — LONG DOCUMENTS. Write MUST support content well beyond conventional single-document character limits using visible/adjacent page rendering, section-based hydration, incremental line wrapping and pagination, indexed find/replace, accessible semantic headings, deep jumps, and correct undo/redo. Inserting text near page 1 of a very long document must not synchronously reflow every distant page. Preserve authentic document layout/export correctness once background work completes.
