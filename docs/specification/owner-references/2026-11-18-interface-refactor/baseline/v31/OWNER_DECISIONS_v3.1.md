# v3.1 — Boards and theme-reference amendment
Revision: 1 November 2026. Source: the owner's decisions in this conversation and the supplied v3/atlas/ThemeRefs files. This is a design and work-order update, NOT a claim about what Astra or Qwen has implemented.

## Authority and preservation
Apply current explicit owner instructions within their scope, then this focused amendment, then compatible retained v3 clauses and normative annexes. Preserve all unrelated requirements, security, permission, persistence and donor obligations. Archive superseded UI wording rather than deleting its history. A screenshot is one design state, not permission to omit unpictured functionality. Implementation choices below are defaults, not authorisation to change product scope.

## V31-BOARDS-001 — Identity and file entry point
Use [Board Icon] [Board Name with menu affordance] [Undo] [Redo]. Remove the separate File button. Clicking the name opens the existing file menu; Rename is an explicit operation in that menu. Preserve existing New/Open/Save/Save As/Export and other applicable file actions, board switching, shortcuts and truthful save state. Preserve existing icon customisation if already supported; do not invent a new subsystem for it. Keep Home navigation and page navigation available.

## V31-BOARDS-002 — Grouping and containers
Use the discussed working layout: one rounded adaptive toolbar pill divided by subtle vertical separators, plus a separate fixed-actions pill. The important owner requirement is visible individual controls organised into logical groups, NOT one generic dropdown labelled “Style & Font”, NOT a separate pill for every control, and NOT one undifferentiated string of icons. Keep style/font separate from the bold/colour/underline group. Exact pixels in the revised atlas are implementation defaults; no fresh redesign of the entire app is authorised.

## V31-BOARDS-003 — Text context, in order
A. Style and font: separate Paragraph/heading-style dropdown; current font-family dropdown (e.g. Montserrat); current font-size display/edit and minus/plus; Aa casing menu.
B. Text formatting: directly accessible bold, italic, underline, strikethrough, text colour and highlighting, with appropriate lower-priority overflow.
C. Paragraph: one alignment control with an alignment dropdown, one list-options control, and compact indentation/spacing menus. Do not permanently list every bullet type, spacing value or alignment choice.
D. Properties: consistently positioned, selection-sensitive advanced settings. A compact contextual inspector is the working implementation default.
Use the actual caret/range formatting, including mixed values. Paragraph, Montserrat and 14px in a mock-up are examples, not hard-coded state. Do not confuse the style dropdown's “Paragraph” value with the paragraph-options group.

## V31-BOARDS-004 — Fixed actions and context
The fixed pill contains Add, Drawing and AI. Its actions remain discoverable and stable while adaptive controls follow the active text, image, table or drawing selection. Reuse existing context/command ownership; do not expose unsupported controls as working features. A shared data-driven grouping/overflow mechanism is preferred over duplicated per-mode toolbars, without rebuilding working infrastructure unnecessarily.

## V31-BOARDS-005 — Two Add actions, two destinations
Toolbar Add means insert content into the current page; it retains the existing toolbar Insert command/catalogue/target semantics. Sidebar footer Add means create a page or section. Preserve both. Tooltips and accessible names distinguish them. Opening or cancelling a chooser changes no data; choosing once creates exactly one appropriate entity. Do not repurpose the footer Add into a generic header action.

## V31-BOARDS-006 — Usability without regression
Preserve the functional existing editor. Fix reproducible menu anchoring, viewport-edge placement, clipping, overlap, spacing, search layout, nested dismissal, focus/caret retention, Enter/split focus, selection, hover feedback, ghost caret and contextual outlines. Check the live state first: the owner reported the formerly blank style dropdown was already fixed. Do not restore obsolete defects or arbitrary offset hacks. Tabs remain optional and off by default where that retained owner decision applies. Fix shared CUI causes at their source and test affected consumers. Collapse lower-priority controls into accessible overflow before clipping; retain all commands, selection and keyboard access. Do not treat speculative diagnoses in an old transcript as confirmed current bugs.

## V31-BOARDS-007 — Current architecture
Keep the existing 9to1 CUI frontend. Reuse appropriate AppFlowy Rust functionality incrementally after examining the actual donor APIs, bindings, data model and licence. Do not replace the UI with AppFlowy's UI, build an unused mock-up, or assert that any bridge is already working without evidence. Preserve document identity, editing, ink, undo, local save/reopen and migration/recovery. Avoid building a second competing editor/document model. Reuse existing donor components rather than fabricating simplified replacements.

## V31-DELIVERY-001 — Order
First preserve local work and integrate Astra's actual remote commits. Then finish, test and verify Boards against the complete applicable source contract. Only after that gate is met, start the shared theme-design pass using ThemeRefs. During Boards, shared fixes genuinely required by Boards are allowed; broad theme restyling is not a substitute for finishing Boards. No unrelated app implementation except an evidenced dependency. Old cloud deadlines, worker assignments and the 2% cloud-usage stopping rule do not apply to this local Qwen run.

## V31-THEMES-001 — Reference-led appearance pass
Inspect every supplied reference image, not just filenames or palette samples. Write a concise reference-to-component mapping of materials, edges, elevation, rounding, typography, density and interaction-state treatment. Implement original CUI resources/components; do not ship reference screenshots, logos, brands or unrelated reference-app functionality. Static pictures do not specify animation behaviour. Preserve the normative Professional annex, theme preference identities, accessibility/reduced motion and the separation between chrome and authored content.

## V31-THEMES-002 — Roster and unresolved names
The supplied baseline defines Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western; Cinematic remains legacy compatibility. ThemeRefs contains Professional, Glow, Playful, Frost, Bubble, Industrial, Retro, Western, Synthwave and Snug. A folder label alone does not authorise a rename, alias, replacement or new built-in. Frost/Frosted Glass and Industrial/Metallic require an explicit mapping from a later owner decision or verified repository source. Synthwave and Snug remain preserved reference-only categories until that authority exists. Continue clearly mapped themes without discarding the unmapped references. Record uncertainty rather than guessing. Existing Frosted Glass no-Gaussian/Mica wording is not silently overridden by a still image.

## V31-THEMES-003 — Theme acceptance
Correct materials and component designs, not merely accent colours. Review normal/hover/pressed/focused/selected/disabled/error states, menus, inputs, toolbar, sidebar, dialog, scrollbars and theme switching on real app surfaces. Verify every current theme in supported light/dark, high-contrast and reduced-motion conditions. Preserve layout topology, semantic command behaviour, stable selection/caret/scroll, dirty state and authored document/ink/image colours. Re-run Boards regressions after shared theme changes. Mark appearance pending visual review when the worker cannot genuinely inspect rendered images.

## Deliberate limits
No live repository, Astra commit, native build or current runtime was inspected while authoring this handoff. Full retained technical chapters are not reconstructed from their names. The included focused excerpts are not a replacement for the repository's companion module pack. If a required source is absent, record the exact gap and work on unblocked requirements; never claim full acceptance by narrowing the scope silently.
