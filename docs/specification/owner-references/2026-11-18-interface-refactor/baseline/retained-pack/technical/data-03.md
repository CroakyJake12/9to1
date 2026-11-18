# Data — worksheets, tables and explicit AI approval — Data; Cell modes formulas and canonical views (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2837 · v2 lines 15001–15001

## Data

### V2-B2838 · v2 lines 15003–15003

### UX27-TEST-088 — Cell modes formulas and canonical views

### V2-B2839 · v2 lines 15005–15005

**Requirements:** UX27-DATA-001, UX27-DATA-002, UX27-DATA-003, UX27-DATA-004 · **Status:** NOT RUN

### V2-B2840 · v2 lines 15007–15007

**Given:** Data artifact with formulas, filtered views and many rows; linked view in Boards.

### V2-B2841 · v2 lines 15009–15012

**When:**
1. Edit a cell in navigation/text modes; recalculate.
2. Change sort/filter and inspect linked view.
3. Export/import including off-screen rows.

### V2-B2842 · v2 lines 15014–15017

**Then:**
- Caret/navigation semantics are distinct; formulas refer to intended identities.
- Views share one canonical dataset and do not silently mutate source order where inappropriate.
- Full data survives virtualisation and round-trip.
