# Boards — seamless pages, blocks and ink — Merge split and undo without data loss; Image resize crop and missing media (part 07)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2603 · v2 lines 14295–14298

**Then:**
- Cell navigation and text caret movement are distinct.
- Typing replaces/edits the intended range according to the visible mode.
- Moving out of the last cell follows a declared policy without destroying the page structure.

### V2-B2604 · v2 lines 14300–14300

### UX27-TEST-043 — Merge split and undo without data loss

### V2-B2605 · v2 lines 14302–14302

**Requirements:** UX27-BOARD-039 · **Status:** NOT RUN

### V2-B2606 · v2 lines 14304–14304

**Given:** A table range whose cells all contain distinct content.

### V2-B2607 · v2 lines 14306–14309

**When:**
1. Merge selected cells, reviewing any required content decision.
2. Split the merged region.
3. Undo/Redo and save/reopen.

### V2-B2608 · v2 lines 14311–14314

**Then:**
- No non-anchor content is silently discarded.
- Structure/formatting and text follow the explicit merge policy.
- Undo restores the original cells and their content.

### V2-B2609 · v2 lines 14316–14316

### UX27-TEST-044 — Image resize crop and missing media

### V2-B2610 · v2 lines 14318–14318

**Requirements:** UX27-BOARD-040 · **Status:** NOT RUN

### V2-B2611 · v2 lines 14320–14320

**Given:** Owned image and external referenced media in a Native page.

### V2-B2612 · v2 lines 14322–14325

**When:**
1. Resize with aspect constraints; crop where supported.
2. Undo and reopen.
3. Make the external media unavailable.

### V2-B2613 · v2 lines 14327–14330

**Then:**
- Actual media geometry changes, with appropriate handles and no arbitrary page blank area.
- Owned media remains portable.
- Missing reference is labelled without blocking unrelated text or deleting its locator.

### V2-B2614 · v2 lines 14332–14332

### UX27-TEST-045 — Graph input and view-only sliders

### V2-B2615 · v2 lines 14334–14334

**Requirements:** UX27-BOARD-041 · **Status:** NOT RUN

### V2-B2616 · v2 lines 14336–14336

**Given:** Graph with two expressions and a parameter slider.

### V2-B2617 · v2 lines 14338–14341

**When:**
1. Edit one expression and inspect the plot.
2. Enter invalid syntax.
3. Switch View mode and change an allowed exploratory slider, then reopen.

### V2-B2618 · v2 lines 14343–14346

**Then:**
- Plots correspond to valid canonical expressions; errors are local and precise.
- Multiple expressions remain distinct.
- View-only exploration does not silently overwrite the saved authoring parameters.

### V2-B2619 · v2 lines 14348–14348

### UX27-TEST-046 — Intentional Write and Canvas pages

### V2-B2620 · v2 lines 14350–14350

**Requirements:** UX27-BOARD-042 · **Status:** NOT RUN

### V2-B2621 · v2 lines 14352–14352

**Given:** Board with Native, Write and Canvas PageTypes.

### V2-B2622 · v2 lines 14354–14356

**When:**
1. Switch among page types; edit and save their real content.
2. Return to Native, close and reopen.

### V2-B2623 · v2 lines 14358–14361

**Then:**
- Each PageType activates its actual registered engine, not a static placeholder.
- Embedded ownership and saved content remain coherent.
- Native page is not replaced by the Canvas editor.

### V2-B2624 · v2 lines 14363–14363

### UX27-TEST-047 — Scoped Chat uses current authorised sources

### V2-B2625 · v2 lines 14365–14365

**Requirements:** UX27-BOARD-043 · **Status:** NOT RUN

### V2-B2626 · v2 lines 14367–14367

**Given:** Scoped Chat page with an authorised board source and a revoked source reference.

### V2-B2627 · v2 lines 14369–14370

**When:**
1. Inspect current scope, send through a supported provider, then revoke access and retry.

### V2-B2628 · v2 lines 14372–14375

**Then:**
- The actual admitted request uses current permitted source scope.
- Revoked material is not injected.
- Chat failure preserves page/draft and does not require unrelated cloud setup for local editing.

### V2-B2629 · v2 lines 14377–14377

### UX27-TEST-048 — Search across collapsed and unloaded content

### V2-B2630 · v2 lines 14379–14379

**Requirements:** UX27-BOARD-044, UX27-SHARED-020 · **Status:** NOT RUN

### V2-B2631 · v2 lines 14381–14381

**Given:** Large board with a unique term in a collapsed distant page.

### V2-B2632 · v2 lines 14383–14385

**When:**
1. Search the term.
2. Activate its result and return to the previous page.

### V2-B2633 · v2 lines 14387–14390

**Then:**
- Search includes canonical unloaded content.
- Correct ancestors expand and the target is revealed.
- Previous page context is retained; hidden data is not treated as nonexistent.

### V2-B2634 · v2 lines 14392–14392

### UX27-TEST-049 — Re-render never mutates the document

### V2-B2635 · v2 lines 14394–14394

**Requirements:** UX27-BOARD-045 · **Status:** NOT RUN

### V2-B2636 · v2 lines 14396–14396

**Given:** Board whose model IDs and counts are recorded.

### V2-B2637 · v2 lines 14398–14399

**When:**
1. Repeatedly open tabs, change theme/scale, scroll through virtualisation and restart.

### V2-B2638 · v2 lines 14401–14404

**Then:**
- No extra placeholder, canvas, empty block or event subscription is persisted by rendering.
- Counts/IDs remain stable apart from explicit user edits.
- One click still invokes one domain action.
