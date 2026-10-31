# Boards layout corrections requested by the owner — 31 October 2026, ~21:45

Owner's list for the **current** Boards version (screenshots supplied alongside):

1. **Tab view is an option, and disabled by default** — "for abstraction". The 40-high content tab strip I added
   (commit `0fd48244`, verified rendering side by side in `k6.png`) must not be part of the default presentation.
   Required flow: the surface opens without the tab strip; a single control turns it on, the choice is remembered in
   the same Home-owned preference style the app already uses, and turning it on/off never changes data.
2. **Search bar clips its text** (visible in the owner's capture: "Search pages" is cut at the descender and the
   pill is tight). Rename the placeholder to **"Search this Board"** and give the field enough height/padding that
   both the placeholder and typed text are fully visible at the current font size.
3. **Hover behaviour is wrong: the highlight does not contain the whole button.** In the owner's capture the hover
   feedback covers only part of the row and the tooltip ("Open first page of Notes") floats where the row's own
   surface should have expanded. Required: the interactive surface and its hover state must match the hit region of
   the whole control, and the accessible/tooltip text must not replace that state.
4. (The owner's fourth bullet was left blank — recorded as unfinished input, not guessed. Will ask rather than infer.)

## Design menu changes requested in the same message

- Rename and re-icon the design entry to **"Block Type"**, and use a distinct icon for it (the palette icon shown in
  the owner's capture is the one being replaced).
- **"Turn into" applies only to blank elements**, because the existing **plus button is the replace path** — so the
  block menu's turn-into list must be conditional on the block being blank/empty.
- The list is **pick the block type** (the owner's capture shows the entries clipped and greyed: "Text", "Heading",
  "Checklist", "Bulleted lis", "Numbered lis", "Table", "Image", "Divider"), so the control must show every entry
  fully and select one on click.

## Where these live (measured before editing)

- tab strip: `Boards.cui` `BoardsTabBar` plus `EditorHost` row renumbering (`RowSizes="40 Auto Auto 1fr"`);
- search: `Boards.cui:92` `NavSearchBox` (placeholder `Search pages`, `padding="14 11"`, `Height="30"` which is the
  donor's row height, and 30 is too short for this font + pill padding — that is the clipping cause);
- nav rows: `NavHost` items and the section/page row containers (hover region and tooltip text);
- block menu: the `TURN INTO` group and the design entry in the block context menu CUI, with the existing
  conversion capability already on the session (`GetBlockConversionCapability`, `ApplyBlockConversionAsync`).

## Evidence discipline for this unit

Each item is compiled after its edit unit and verified on screen with `capture_window.py` plus a numeric pixel probe
for the search pill and the hover region, because "looks fixed" from a thumbnail is not evidence. Behaviour claims
that are not exercised stay labelled NOT RUN. No acceptance claim: independent acceptance remains paused, and the
AppFlowy Rust engine decision (`OPEN-2026-10-31-BOARDS-APPFLOWY-RUST-ENGINE`) is still outstanding.

## Hover design requirement (owner, 31 October ~22:10, dark-theme capture supplied)

*"the hover design has returned. it should be blank, only showing a ghost accent-themed caret where the user's
pointer is unless it's a container block like table, then the purple outline"*

Required behaviour, written before coding:
* hovering a **leaf/text block** paints **nothing** — no fill band, no grey panel — and shows only a **ghost caret in
  the accent colour at the pointer position**;
* hovering a **container block** (table, database, tabber, image, other containers) shows the **accent outline**
  instead, and still no fill;
* the caret is a pointer affordance, not a selection state: selection, focus and insertion previews keep their own
  existing treatment.

### Measured cause, so the fix aims at the real mechanism

* the fill on hover is **shared framework behaviour**, not Boards markup: `CuiControlTemplates.Frame(...)`
  (framework/CUI/Runtime/CuiControlTemplates.cs:99) attaches `new CuiHoverFeedback(owner, frame)` for
  `Button | TextBox | ComboBox | ListBoxItem | RangeBase | MenuItem | Thumb | NumericUpDown`, and
  `CuiHoverFeedback` paints through `CuiMaterialBorder.SetHoverTint(...)` — `Detach()` clears it with
  `SetHoverTint(null)` (framework/CUI/Runtime/CuiButtonFeedback.cs:140-215). Any Boards block that renders as a
  TextBox therefore gets the grey hover band automatically, which is why the design "returned".
* `CuiHoverFeedback` already supports a **required-class opt-in** (`_requiredClass`, :128-137), but the default
  template path attaches it unconditionally, so there is no per-control way today to say "no hover fill" from CUI.
* The block row already has an accent outline path in the host (`BlockRenderer.cs:482-530`: `BlockRowOutline`,
  1.5px accent at 0.55 on grip hover for **all** kinds, full-opacity accent when `selected && container`), so the
  container-outline half of the requirement can reuse that path once it is restricted to containers.
* The hover band the owner sees in the capture is the fill, i.e. `SetHoverTint`; the outline alone would not read as
  a filled rectangle.

### Fix being implemented (framework capability, with a test)

Add a CUI-visible property **`HoverTint`** with values `Default | None | Outline`:
* `Default` keeps today's behaviour for every existing surface (no regressions),
* `None` makes `CuiHoverFeedback` clear the tint and never apply one,
* `Outline` keeps the frame fill clear and instead paints the accent outline on hover.

Boards then authors block controls with `HoverTint="None"` (leaf blocks) and `HoverTint="Outline"` (container
blocks), and the ghost accent caret is added at the pointer in the block row host. A regression test must fail
without the property and pass with it, so the capability is pinned rather than patched only in Boards.

## State at 22:30 (owner leaving) - what is finished, committed and proven vs staged

**Done, tested, NOT YET COMMITTED:** the `HoverTint="None|Default|Outline"` CUI capability
- new framework vocabulary + policy: `framework/CUI/Runtime/CuiHoverTintPolicy.cs`
- accepted by the compiler/loader gate: registered in `CuiControlRegistry` (`("HoverTint", "symbol", ...)`) and
  added to the **common** element property set (the per-element gate is what produced
  `CUIR052: CUI property 'HoverTint' is not supported on Button` before that)
- applied in `CuiControlLoader.ApplyLiteralProperty` with value validation (bad values -> source diagnostic)
- honoured by the shared hover feedback in `CuiButtonFeedback.cs` (`PaintsFill` / `WantsOutline`)
- regression tests: `framework/CUI/Runtime.Tests/CuiHoverTintOptOutTests.cs` - **proven to fail without the fix**
  (`Assert.Empty() Failure: ... error CUIR052: Unknown or forbidden CUI property 'HoverTint'`) and **passing with
  it**: `Passed: 2, Failed: 0`. The second test asserts a *value* diagnostic and explicitly rejects the
  unknown-property message, so it cannot pass while the vocabulary entry is missing.
- full CUI suite ratchet was **started and killed by an unrelated SIGTERM**; it must be re-run
  (`python3 -B eng/development/cui_test_ratchet.py check --dotnet <guarded sdk dotnet> --timeout 1500`) before
  this framework change is described as regression-clean. Until then: focused tests pass, suite-wide status NOT RUN.

**Staged in the working tree, compiled NOT RUN:** `BlockRenderer.cs` hover wiring - hover outline restricted to
`container` kinds, a `BlockGhostCaret` border (2x18, accent at 0.45 alpha, never hit-testable, hidden at rest) that
follows the pointer via `PointerMoved`, and `CuiHoverTintPolicy.Set(..., None|Outline)` applied to the TextBox/Button
controls inside each block row. Needs: compile, on-screen capture, and the two remaining owner items -
menu alignment/spacing, and "pressing enter creates a new block that immediately loses focus".

**Unstaged CUI work still to commit from earlier this hour:** `BlockActionsMenuCui.cui` / insert-menu label
alignment, and the design-menu rename ("Design" + palette icon -> "Block Type", turn-into only for blank blocks,
because the + button is the replace path).
