# Amendment register and adoption notes

## Delivery and limits

This package is a detailed expanded edition of the existing 9to1 V2 specification, not a new product or a claim of implementation. The original Google Doc was read but **not edited**: the connected tools exposed reads/exports, not a document-write operation. No repository, branch, coordinator, worker, account, model, schedule or user document was changed by generating this package.

The full baseline text is preserved byte-for-byte after stripping only generated UX27 insertion regions. The expansion adds 176 stable requirement IDs and 102 concrete acceptance scenarios; every scenario starts NOT RUN. The new requirements are about 20,700 words, and the scenarios about 7,800 words. Exact machine counts appear in the manifest.

## Latest owner corrections

- Boards works locally without connecting Files, logging into an account or selecting a mandatory Boards folder. A recoverable local draft can optionally be saved as an editable .9to1board file in any permitted location. Normal OS permissions still apply.
- Enter in an ordinary paragraph creates/splits a real block; Shift+Enter is a soft break. Context-specific exceptions are spelled out for headings, lists, code, tables, titles and IME.
- The six-dot grip supports actual movement, not only decoration/context menus. Drag previews, drop targets, cancellation, nested/column targets, auto-scroll and undo/persistence are specified.
- Pen input must draw on the real page, including Edit plus Locked layout. Ink capture, transforms, pressure fallback, erasing and persistence are specified. A visible Pen icon is not acceptance.
- Remove the unintended second freeform-editor surface from Native pages without removing intentional Canvas pages, absolute layout or the legitimate DrawingLayer.
- Preserve the 25 October Boards shell: top toolbar; nested page/section tree; bottom Add/user controls; Boards identity top-left; no top-right AppFlowy product badge; Add page/section replaces the reference Templates affordance; theme changes do not rearrange layout.
- Preserve recent popup dismissal, compact table controls, missing editing/snap controls, browser rendering/scrolling and widespread app-launch corrections. Visible tests must use the actual route/build.
- Converge valid unique 9to1 development work onto main safely, preserving all source/history. This is not removal of version-control capabilities from Build/Stacks.

## Newly authored design details

Concrete spacing, logical sizes, motion durations, drag thresholds, hover timings, keyboard completions and interaction edge cases are labelled D where not established by an earlier explicit requirement. They are usable defaults for this requested expansion, not asserted measurements of Notion/AppFlowy or the inaccessible original Boards screenshots. More specific compatible owner-approved choices take precedence.

## Safe adoption

1. Preserve the native Google Doc and its revision history, images, tables and formatting.
2. Review the expansion-only document and its owner-versus-drafted provenance labels; retain the complete baseline scope.
3. Insert each numbered section beside its mapped canonical source section using an authorised editor. section-insertion-map.json describes this text edition’s exact placement.
4. Incorporate compatible annex/reference content through the established source-authority process; do not silently replace old pins or invent visual approval.
5. Pin the adopted revision and update requirement/test mappings. Existing milestone statuses and acceptance remain unchanged until actual implementation and independent verification occur.

## How to use the package

- Start with 9to1-v2-expanded.html for the navigable offline reader.
- Give the full expanded Markdown master to a coding agent together with the existing native reference assets. It includes every baseline line, the additions and the test catalogue.
- Use behavioural-expansion.md for just the new/clarified requirements.
- Use requirements.json and acceptance-scenarios.json for traceability. These are specification artifacts, not a replacement canonical milestone database or a deployed runtime schema.
- See evidence/recording-findings.md for bounded observations from the user’s current recording.

## Remaining coverage gaps

The complete original 25 October visual pack/design-review PDF was not retrieved in this run; exact historic geometry was not invented. The native Google Doc’s embedded visual/formatting structures are not reconstructed by the text export. Full donor-version runtime audits, all platform combinations, pen hardware tests and independent product acceptance remain to be performed. The 102 scenarios are a concrete floor, not the entire donor-parity manifest.
