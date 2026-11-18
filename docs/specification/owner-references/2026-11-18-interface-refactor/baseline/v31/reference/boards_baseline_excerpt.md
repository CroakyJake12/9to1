# Supplied v3 Boards excerpt — baseline, not the full technical pack



Apply OWNER_DECISIONS_v3.1.md over conflicting UI wording. This extract preserves source text and named technical references; it does not contain the absent technical chapters.



## Boards seamless pages blocks and ink

Module boards    Current contract and exact reference designs

## Prerequisites

CUI, themes and shared interaction system. Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.

Shared productivity engine and canonical embeds. Real document/object engines, semantic editing, canonical app embedding and donor feature parity.

Shared AI controls and artifact collaboration. Contextual AI bars, shared model/permission controls, global collaborators and typed artifact proposals.

Artifacts, local persistence and recovery. Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Boards is a native flexible document workspace combining structured blocks, free placement and ink with nested pages. It must work locally without a Files account or connected cloud provider.

Current overrides: V3-OWNER-011.

## Layouts and visible states

V3-BOARDS-LAY-01  Keep a compact board identity/breadcrumb/switcher, nested page tree, bottom Add/avatar and a contextual top floating toolbar. Expose independent Edit/View, Locked and Snap controls.

V3-BOARDS-LAY-02  Represent page creation/type choice, page options, insertion-between-blocks, typing/caret, selected block/drag affordance, free ink and active tool-options states explicitly.

V3-BOARDS-LAY-03  Use a seamless authored surface rather than nesting an unnecessary second app canvas or presenting the page as disconnected generic cards.

## Features and behaviour

V3-BOARDS-FEA-01  Add, rename, nest, reorder, duplicate and remove pages with correct source semantics. Page types embed real canonical app surfaces where supported.

V3-BOARDS-FEA-02  Insert and edit text, lists, media, table/linked blocks, absolute objects and ink with unified selection/undo. Insertion lines and drag grips identify actual block boundaries.

V3-BOARDS-FEA-03  Locked controls constrain movement/layout as defined; View prevents editing; Snap changes manipulation behaviour. Do not collapse all three into one read-only toggle.

V3-BOARDS-FEA-04  Native .9to1board files may live at permitted locations. Local creation and editing must not require sign-in, a mounted Files provider or AI.

V3-BOARDS-FEA-05  Contextual AI and collaborators use typed board/page/block changes with visible review and source ownership.

## Canonical entities and persistence

V3-BOARDS-ENT-01  Persist BoardID, nested PageIDs, block/object/stroke IDs, order/layout coordinates, page type, embed references and revisions.

V3-BOARDS-ENT-02  Store local draft/save and optional external file/sync state separately; embedded source artifacts retain their owning identity.

## Permissions and recovery

V3-BOARDS-PER-01  Detach/remove references without silently deleting the original artifact. Shared-source permission changes show safe placeholders.

V3-BOARDS-PER-02  Recover typed and ink changes after interruption; stale AI edits must not overwrite concurrent page/block changes.

## Acceptance checks

V3-BOARDS-ACC-01  Test local create/open, page Add/type menu, page options, block insertion/typing/drag, pen options and undo/reopen.

V3-BOARDS-ACC-02  Verify Edit/View, Locked and Snap independently, keyboard navigation, canonical embeds and no Files account dependency.

## Exact design states

The following figures are the current reference for this module. Their interface geometry and notes are part of this contract. Each figure has a stable screen ID shared with the atlas and source pack.

Boards · Start  [boards-launch]

Boards · Draw on the page  [boards-ink]

Boards · Add page or section  [boards-add]

Boards · Typing within a block  [boards-typing]

Boards · Insert a block  [boards-insert]

Boards · Native page  [boards-editor]

Boards · Block actions  [boards-context]

Boards · Save recovery  [boards-recovery]

Mobile · Boards page drawer  [mobile-boards]

## Technical reading map

Use these smaller chapters for detailed APIs, data models, donor integration, platform and recovery requirements. The complete Markdown module includes them in source order.

Boards — exact shell, navigation and local-first file behaviour; The intended composition (part 01). technical/boards-01.md (2,331 words).

Backspace/Delete at boundaries; Lists, toggles and nesting (part 02). technical/boards-02.md (2,365 words).

Compact table interaction, not fifty buttons; Table cell editing and navigation (part 03). technical/boards-03.md (2,387 words).

Edit and View modes; Locked and Unlocked layout modes (part 04). technical/boards-04.md (2,371 words).

Embedded artifact ownership and lifecycle; Collaboration and concurrency (part 05). technical/boards-05.md (2,389 words).

Add page/section chooses the correct destination; Tree focus, expansion and rename (part 06). technical/boards-06.md (2,382 words).

Merge split and undo without data loss; Image resize crop and missing media (part 07). technical/boards-07.md (550 words).

Source clause IDs: UX27-BOARD-001, UX27-BOARD-002, UX27-BOARD-003, UX27-BOARD-004, UX27-BOARD-005, UX27-BOARD-006, UX27-BOARD-007, UX27-BOARD-008 and 100 additional IDs in the module source and source ledger.

## Boards Start

boards-launch    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. Start is a separate active view. The editor is not mounted under a welcome banner.

2. New, Open, Import, Templates and Recent resolve actual artifact identities. Missing or failed imports receive their own recovery states.

3. Direct-open and session restore bypass Start. Returning here preserves the existing document session.

Source: LAUNCH-001–006; Boards Home/start; UX27-AUTH-005 for Boards

## Boards Draw on the page

boards-ink    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. This is the same Native page and document-space viewport as the text/block editor. Ink is not a second Canvas panel or a raster background.

2. Edit + Locked still permits page ink. Layout locking, snap, user Edit/View and AI Read-only/Write remain independent.

3. Pen, highlighter, eraser and ink selection occupy the same contextual toolbar. Width, colour and opacity affect new strokes unless existing strokes are selected.

4. Pointer capture, pressure fallback, zoom/scroll transforms and one-stroke undo must use the shared editable ink model. Page-level ink retains its coordinates when flow text reflows.

5. The orange annotations are authored content. Theme changes restyle the controls and preserve their stored stroke colours.

Source: Owner revision: preserve the Boards base and add drawing; UX27-BOARD-029–036

## Boards Add page or section

boards-add    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. The Add popup anchors above the fixed sidebar footer. It creates pages or sections, independently from toolbar Insert and inline block +.

2. Destination is explicitly Product here. The canonical page types are Native, Write, Canvas, Chat and Scoped Chat; New section is a separate operation.

3. Opening the popup allocates no page. A successful choice creates exactly one entity, reveals it in the tree and focuses its title/editor.

4. Escape or an outside click restores focus to Add without modifying the page. At narrow widths this becomes a navigable sheet while retaining the footer entry point.

Source: Owner revision: preserve Boards and show Add; UX27-BOARD-014; Native/Write/Canvas/Chat/Scoped Chat page types

## Boards Typing within a block

boards-typing    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. The caret belongs to one paragraph BlockID. Text selection and keyboard input do not drag the block, paint ink or create a page-sized input frame.

2. Enter splits a paragraph into two siblings; Shift+Enter is a soft break within the same block. IME commit is handled before structural Enter.

3. The following empty paragraph has its own grip and insertion location. Formatting reflects the current caret/range and preserves valid inline marks.

4. Saving… is distinct from Saved locally. A production editor must update that label from actual persistence state and preserve focus during autosave.

Source: Owner revision: preserve Boards and show typing; UX27-BOARD-016–021 / 028 / 036

## Boards Insert a block

boards-insert    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. The visible insertion line is a boundary in the current page BlockTree. Inline +, slash and toolbar Insert share the same registered catalogue and target semantics.

2. The highlighted result is a keyboard focus state, not a committed insertion. Search, arrows, Enter and Escape preserve the original text/range.

3. Selecting a type replaces only the eligible slash token/empty block or inserts at this boundary; unrelated paragraph content remains intact.

4. Content insertion is distinct from footer Add page/section. Tables and embedded artifacts retain shared object semantics and ownership.

Source: Owner revision: block insertion/options; UX27-BOARD-022–028 / 037–040

## Boards Native page

boards-editor    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. Use a single seamless native page. Flow blocks, explicitly positioned content and page ink share one surface. No sheet frame or accidental Freeform canvas.

2. Sidebar is 256 logical units by default, with 32-unit rows and 16-unit nesting. Board switcher stays above the tree; Add and avatar stay in the fixed footer.

3. Edit/View, Locked/Unlocked and AI Read-only/Write are separate dimensions. Locked layout still permits page ink.

4. This mock-up defines a 760-unit readable content column, typography and exact spacing. Save As writes an editable .9to1board anywhere permitted.

Source: UX27-BOARD-001–047; UX27-AUTH-005; later owner Boards topology; reference screenshot as defect evidence

## Boards Block actions

boards-context    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. A grip hover reveals a specific block target. Click selects; drag moves only after the pointer threshold.

2. The full-row block menu anchors to its target. Escape restores focus and selection; an outside click dismisses the menu.

3. The persistent footer Add creates pages/sections. Toolbar Insert and block + add content to the current page.

Source: UX27-BOARD grip / block popup / nested navigation contracts; UX27-SHARED popup lifecycle

## Boards Save recovery

boards-recovery    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. Save failure is not an empty page. Preserve edits, last durable revision and the recovery draft.

2. Saving locally, saved locally and cloud sync are separate states. This example reports the failed destination without claiming successful file output.

3. Retry re-attempts the same save; Save a copy chooses a new destination. Neither silently discards the live document.

Source: UX27-BOARD-009–014; shared canonical save/recovery state vocabulary

## Mobile Boards page drawer

mobile-boards    Professional dark    390 by 844 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. The narrow layout collapses the nested page sidebar into a named drawer while preserving sections, hierarchy and stable page identities.

2. Boards identity remains top-left. The top toolbar retains Edit/View separately from Locked/Unlocked; they describe different constraints.

3. The drawer keeps Add and the user area at the bottom. Add opens page/section choices with a destination preview and never moves into a generic header action.

4. Drawer navigation uses 44-pixel effective targets, independent internal scrolling and focus containment. Escape, Close or the scrim restores the previous page and initiating focus.

5. Opening the drawer preserves page selection, scroll and any AI draft. Themes change appearance without rearranging this topology.

Source: UX27-BOARD-001–047 topology and nested pages; UX27-SHARED-005, 009–010; UX27-TEST-011–014, 033