# Decision packet — CUI has no responsive layout construct

**Raised:** 2026-10-14 · **Gate:** Phase B/C · **Work package:** WP01 (CUI framework)

State: **OPEN — new framework capability required.** Not a visual-tolerance question.

---

## 1. Observation

CUI's public authoring vocabulary is fixed and complete:

`framework/CUI/Language/CuiLanguageVocabulary.cs`
```csharp
VisualElements    = { Page, Container, Text, Image, Audio, Video, Object, Button, Input, Layer }
NonvisualElements = { Anchor, Theme, Style, Property, Variable, Prefab, ActionEvent,
                      If, Else, Repeat, Import, Keyframe }
```

`Container` carries typed layout (`Type`, `ColumnSizes`, `RowSizes`, `Cells`/`CellGap`,
`Padding`, `Margin`, `CornerRadius`, `Overflow`, …) but **there is no viewport-driven
construct**. A search of `framework/CUI` for `Compact`, `Breakpoint`, `IsCompact` finds no
CUI layout primitive — only unrelated hits inside vendored Avalonia (`CommandBar.IsCompact`)
and internal allocator terms.

## 2. Why this matters

`02-DESIGN-CONTRACTS.md` requires:

- **D02:** "A narrow layout uses the specified responsive drawer behavior; it must not squeeze
  350 pixels of permanent navigation beside an unusable transcript."
- **D11:** baseline 1200×900 and 800×600, plus supported narrow/touch/scaling cases, with
  essential functions preserved through drawers/overflow.

The previous `SpacesHome.cui` declared `AdaptiveSplit compact-breakpoint="760"
compact-mode="drawer"`. `AdaptiveSplit` is not a CUI element, is not a registered Definition,
and has no CUI equivalent — it was an invented name in a non-CUI dialect, so the responsive
requirement was never actually implemented anywhere.

Migrating that document to canonical CUI (done — see `SpacesHome.cui`) therefore had to drop
the invented breakpoint. That is a deliberate, recorded loss, not a silent one.

## 3. What is needed

WP01 already requires the "full CUI contract" including "layout/scroll/coordinate/anchor/
layer/modal behavior". A responsive viewport/compact construct is missing from that contract's
implementation and must be designed as genuine CUI capability:

- a named breakpoint/container mechanism (not a per-app hack),
- an independently collapsible sidebar with an accessible reopen affordance,
- drawer behaviour at narrow widths,
- verified at 1527×947 (reference), 1200×900, 800×600 and narrow touch sizes.

## 4. Interaction with the Chat decision

Independent of `DECISION-2026-10-14-CHAT-SURFACE-AUTHORITY.md`. The missing CUI construct is a
WP01 framework gap regardless of which option is chosen there; it must be built either way
before D02/D11 responsive acceptance can be claimed.

## 5. Not claimed

No visual tolerance, breakpoint value, or responsive appearance is proposed as approved.
O22 (visual and performance acceptance budgets) remains open; the numbers above are the
source-stated test dimensions, not invented budgets.