# Data — worksheets, tables and explicit AI approval — Data — tables, formulas, views and real state; Cell selection versus editing (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1671 · v2 lines 8212–8212

## 11. Data — tables, formulas, views and real state

### V2-B1672 · v2 lines 8214–8214

### UX27-DATA-001 — Cell selection versus editing

### V2-B1673 · v2 lines 8216–8216

**B/D.** Data distinguishes active cell, selected range and cell-edit mode. Click selects, typing edits/replaces according to the documented spreadsheet behaviour, and the formula bar shows the actual expression/value of the active cell. Arrow/Tab/Enter navigation respects edit mode and does not move selection while an IME candidate is being committed. Multi-range selection and fill handles operate only where supported and visibly indicated.

### V2-B1674 · v2 lines 8218–8218

Column/row headers select the intended stable range and provide contextual operations, not a separate button for every cell. Copy/paste preserves applicable values, formulas, relative references and formatting through the canonical model.

### V2-B1675 · v2 lines 8220–8220

### UX27-DATA-002 — Formulas and recalculation

### V2-B1676 · v2 lines 8222–8222

**B/D.** Formula edits validate syntax and dependencies, distinguish pending calculation from an error, and update affected cells incrementally. Show actual error categories at the affected cells without replacing the workbook with a generic failure. Relative/absolute references, named ranges and cross-sheet references remain stable under supported insertion/reorder operations.

### V2-B1677 · v2 lines 8224–8224

Undo a formula change restores the expression and recalculated dependencies. Export/reopen verifies formulas and values, not only displayed strings. A model-generated number is not a formula engine result unless it was actually written and calculated through the domain API.

### V2-B1678 · v2 lines 8226–8226

### UX27-DATA-003 — Database views and Boards embedding

### V2-B1679 · v2 lines 8228–8228

**B.** Structured records, field schemas, filters, sorts, groups and supported table/kanban/calendar/gallery views are projections of one canonical Data artifact. Changing a view does not duplicate records. A Boards database block references that artifact and retains permissions, formulas and revision semantics; no Boards-only shadow database is introduced to imitate AppFlowy.

### V2-B1680 · v2 lines 8230–8230

View settings are explicit, persist appropriately and distinguish personal from shared configuration. Filtering hides records from the view without deleting them. Dragging a kanban card updates the specified grouping/status field and is undoable/permission-checked.

### V2-B1681 · v2 lines 8232–8232

### UX27-DATA-004 — Import, large data and errors

### V2-B1682 · v2 lines 8234–8234

**B/D.** Import previews delimiters, encoding, column types and conversions before committing where ambiguity can lose data. Preserve leading zeros, dates and text intentionally rather than silently coercing everything. Large data uses indexed/virtualised views and incremental processing; an unrendered row is still searchable/exportable.
