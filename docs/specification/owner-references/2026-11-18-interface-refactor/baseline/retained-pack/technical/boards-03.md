# Boards — seamless pages, blocks and ink — Compact table interaction, not fifty buttons; Table cell editing and navigation (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1502 · v2 lines 6783–6783

Page-level ink remains anchored in page coordinates as flow content reflows. Ink deliberately attached to an object uses an explicit attachment relationship and follows that object; the engine must not infer attachment from proximity. Content expansion must not stretch old ink or turn a selection border into a persistent vertical stroke.

### V2-B1503 · v2 lines 6785–6785

### UX27-BOARD-037 — Compact table interaction, not fifty buttons

### V2-B1504 · v2 lines 6787–6787

**O/B.** Selecting a table shows one compact relevant toolbar/context group and ordinary cell/row/column affordances. Do not display every command separately for every row or cell. Row and column actions appear from the corresponding header/grip menu; border/fill/alignment are grouped; rare actions are in overflow. The page remains the main surface rather than a grid of command buttons.

### V2-B1505 · v2 lines 6789–6789

Distinguish simple rich tables from Data-backed database views. Simple tables use the shared table model; database behaviours use canonical Data entities. Both must preserve editability and ownership when embedded in Boards. Selecting a cell must not replace the whole page with a private database editor or leave an unrelated text toolbar pretending to operate on the cell.

### V2-B1506 · v2 lines 6791–6791

### UX27-BOARD-038 — Table cell editing and navigation

### V2-B1507 · v2 lines 6793–6793

**B/D.** A single click selects/focuses the intended cell; typing or the documented activation enters editing. Tab advances to the next cell, Shift+Tab to the previous; at the final editable cell an explicit supported append-row behaviour creates one row, not repeated rows from a held focus event. Arrow keys navigate within text while editing and between cells in cell-selection mode. Escape exits cell editing without losing a committed value or applying a hidden page command.

### V2-B1508 · v2 lines 6795–6795

Range selection uses rectangular cell coordinates with visible bounds. Multi-cell paste previews or validates shape/type effects and preserves incompatible content through a truthful error path. Enter inside a cell follows the table editor’s contract, never the outer Boards paragraph-splitting handler. Leaving the table returns focus to a clear following block location.

### V2-B1509 · v2 lines 6797–6797

### UX27-BOARD-039 — Table structure and formatting

### V2-B1510 · v2 lines 6799–6799

**B/D.** Add/remove/reorder rows and columns by stable identities; resizing provides live feedback and commits once. Merged cells require a rectangular compatible selection. If multiple cells contain content, present a deterministic content-preserving merge policy or a clear choice; never silently erase non-anchor contents merely to imitate a reference product. Split/unmerge restores structure according to the saved operation and remains undoable.

### V2-B1511 · v2 lines 6801–6801

Header row/column styling, borders, fill, alignment and text marks remain distinct from cell values. Table width can fit the flow column, be a chosen width, or scroll horizontally when necessary without forcing the whole page into horizontal scrolling. Reopen must preserve structure, dimensions, styles and values.

### V2-B1512 · v2 lines 6803–6803

### UX27-BOARD-040 — Images and embedded media

### V2-B1513 · v2 lines 6805–6805

**B/D.** Insert from clipboard, permitted file chooser, drag/drop or an authorised reference. Local insertion must not require a Files account. Show actual loading/progress and preserve the source asset. Selection exposes resize/crop/rotate/fit/caption/alt-text/link controls; corner resize preserves aspect ratio by default, with an explicit modifier/control for other behaviour. Cropping is a non-destructive transform until an explicit export/rasterise operation.

### V2-B1514 · v2 lines 6807–6807

Broken/missing referenced media retains its identity and explanatory placeholder, with Locate/Retry where supported, not a fake thumbnail. A media click in Edit selects it; playback/open controls are deliberate and cannot unexpectedly consume a drag intended to move the block.

### V2-B1515 · v2 lines 6809–6809

### UX27-BOARD-041 — Graph expressions and view-safe sliders

### V2-B1516 · v2 lines 6811–6811

**B/D.** A Graph block contains editable ordered expressions/series, axes, range/viewport, styles and variable definitions in the shared graph model. Adding a second expression adds a second plot rather than replacing the first. Syntax/domain errors appear beside the relevant expression while valid plots continue. Slider bounds, steps and defaults are real values with validation; the graph and controls share the same state.

### V2-B1517 · v2 lines 6813–6813

In View, slider exploration is session-local unless an authorised explicit save changes defaults. Reset returns to saved defaults. Editing, undo, copy between supported apps, save/reopen and export retain expressions and variables; a screenshot of a graph is not an editable graph. The observed ability to change a plotted line does not prove all these contracts passed.

### V2-B1518 · v2 lines 6815–6815

### UX27-BOARD-042 — Write and Canvas PageTypes

### V2-B1519 · v2 lines 6817–6817

**B.** Creating a Write/Canvas page creates or attaches the actual canonical owning-app artifact and hosts its registered editing surface inside Boards. Board navigation remains available, but do not add two competing AI bars, duplicate save indicators or external windows solely to obtain normal editing. Open in Write/Canvas addresses the same artifact/revision, not a copied file.

### V2-B1520 · v2 lines 6819–6819

The page type’s source identity and ownership remain inspectable. Deleting the board page must distinguish a BoardOwned artifact from a Referenced artifact and describe cascade impact. A failed engine load preserves the reference and surrounding board, with a scoped recovery state rather than collapsing all of Boards.

### V2-B1521 · v2 lines 6821–6821

### UX27-BOARD-043 — Chat and Scoped Chat pages

### V2-B1522 · v2 lines 6823–6823

**B/D.** A Chat page retains its conversation/Agent identities across rename and tree moves. Its context shows which Board/pages/sources are available and respects current permissions. Scoped Chat is closed-corpus: the containing authorised board plus explicit attached sources is the permitted information set. Missing evidence produces an explicit insufficient-source response, not silent web retrieval or unlabelled general-knowledge substitution.

### V2-B1523 · v2 lines 6825–6825

Source subpages show ingestion Pending/Ready/Failed/Removed states, with retry and permission-aware removal. A Ready badge is set only after the usable indexed revision exists. Source removal prevents later reads without falsifying historic provenance. Text-only workers/models must not claim to have seen source images or screen recordings they cannot process.

### V2-B1524 · v2 lines 6827–6827

### UX27-BOARD-044 — Search, links and navigation restoration

### V2-B1525 · v2 lines 6829–6829

**B/D.** Board search indexes page titles and authorised content incrementally, including unloaded pages. Results show the containing board/page and a relevant snippet; selecting a result opens the correct stable target and reveals the match. Renames and moves preserve links. A copied block link resolves after reorder and can explain a trashed/restricted target without exposing its content.

### V2-B1526 · v2 lines 6831–6831

Back/forward navigates real page selections and restores scroll/caret/view state where safe. Switching between boards or returning from an embedded artifact must not create duplicate untitled pages. Page-tree filtering must not mutate the hierarchy or delete hidden nodes.

### V2-B1527 · v2 lines 6833–6833

### UX27-BOARD-045 — No accidental mutation on open or render

### V2-B1528 · v2 lines 6835–6835

**B/D.** Opening, switching theme, resizing, changing zoom, moving focus, rendering a preview or reading a board must not create new blocks, duplicate onboarding text, change revision unnecessarily or mark content dirty. Local view preferences may persist separately from document changes. Toolbar construction and placeholder rendering are never domain insert operations.

### V2-B1529 · v2 lines 6837–6837

Acceptance must compare canonical state before/after repeated opens and theme switches. A “Freeform canvas” empty-state appearing twice or content added merely because a component mounts is a failed lifecycle contract even when the view remains technically usable.

### V2-B1530 · v2 lines 6839–6839

### UX27-BOARD-046 — Collaboration and external changes

### V2-B1531 · v2 lines 6841–6841

**B.** Independent block/ink/page operations merge through the canonical revision/operation model, retaining deterministic order and stable IDs. Same-object conflicts remain explicit. The UI preserves the user’s active selection and draft while reconciling incoming changes; do not silently replace an editor’s text with a remote snapshot during composition. A permission revocation stops new mutations and preserves recoverable unsaved work according to policy.

### V2-B1532 · v2 lines 6843–6843

Presence/avatar controls represent real participants/status, not decorative fake collaborators. Offline local editing does not require invented cloud participants. Reconnection reconciles operations and exposes failures without replaying already committed actions.

### V2-B1533 · v2 lines 6845–6845

### UX27-BOARD-047 — Boards rejection checklist

### V2-B1534 · v2 lines 6847–6847

**O/B.** Reject the candidate when any of these is observed: Enter only grows a single text field; the six-dot grip cannot move a block; Pen is selected but draws no real stroke; Locked disables all ink; ordinary pages contain an unexplained second canvas; a table exposes a wall of repetitive buttons; Edit/Layout/Snap controls vanish without a legitimate mode reason; popup owners survive unrelated navigation; local creation demands a Files connection; Save As forces a Boards folder; `.9to1board` is not independently reopenable/editable; saved boards fail because a prior process’s lease expired; title/text contrast breaks on theme switch; or block/ink/page state is lost on reopen.

### V2-B1535 · v2 lines 6849–6852

These are mandatory regressions to test through the actual application entry point. A passing unit suite, menu inventory, new source files or a screenshot of the intended layout does not override a failing real journey.
<!-- UX27:END SECTION-02 -->
Boards
Product role and donor contract

### V2-B1536 · v2 lines 6855–6855

9-1 Boards is the flexible visual workspace, note-taking and project-composition app. AppFlowy is the primary donor for applicable workspace/page/database/block behaviours. Boards MUST preserve applicable AppFlowy features, quality-of-life behaviours and non-proprietary block types from the selected donor revision unless a scoped donor-parity exception is recorded.

### V2-B1537 · v2 lines 6858–6858

AppFlowy proprietary AI, AppFlowy account/billing/subscription infrastructure and proprietary paid services are not copied. Only donor functionality available under legally usable source/licensing is eligible for direct source parity. Proprietary AI capabilities are replaced by 9-1/Dulche behaviour; unavailable proprietary services require a documented 9to1-native replacement or explicit non-applicability decision.

### V2-B1538 · v2 lines 6861–6861

The implementation MUST maintain a donor-parity manifest for Boards identifying the selected AppFlowy revision and classifying each applicable user-visible feature/block as Preserve, Replace with 9to1-native equivalent or Not applicable. “Most AppFlowy features” MUST NOT be interpreted as permission to omit unreviewed donor functionality.

### V2-B1539 · v2 lines 6864–6864

Visual shell

### V2-B1540 · v2 lines 6867–6867

Boards MUST use native CUI and a clean, seamless workspace surface. Native Board pages MUST NOT render as a paper/card floating inside another background, and MUST NOT add a persistent page border, shadow or sheet frame around the editable content area. The page/workspace background fills the available content surface continuously.

### V2-B1541 · v2 lines 6870–6872

AppFlowy MUST be the primary interaction, block-editing and navigation-layout reference for Boards; OneNote, Notion and Milanote MAY inform complementary notebook, information-organisation and visual-composition conventions. Boards MUST still use its independent 9to1/CUI design language, not clone donor branding or styling.
Editor presentation and identity: The application shell MUST use compact global chrome with coherent Boards > active Board > active Page breadcrumbs and a single prominent, naturally editable Page title. Board name, Page title, editor placeholder and content heading MUST NOT be redundantly displayed as if they are the same entity (for example repeated "Untitled board" labels). The title MUST look like an integrated editor heading rather than a huge full-width grey form field. Native content MUST begin at a comfortable position below the floating tool island, use a readable central content width without a card frame, and avoid excessive unused vertical or horizontal gaps.
CUI presentation quality: The dark interface MUST establish a clear hierarchy between global app chrome, quieter sidebar, primary editing surface, contextual floating island and bottom AI bar. Sidebar navigation rows, primary commands and utility actions MUST have different visual weights; replacing a page tree with a stack of equally prominent oversized rectangular buttons is non-conformant. Buttons, dropdowns, popovers, focus/hover/pressed/disabled states and pointer cursors MUST be consistent, aligned and accessible. Icons MUST come from validated coherent assets (not missing-glyph boxes or arbitrary placeholder symbols). Use responsive spacing, compact professional controls, appropriate contrast and clean typography under the authoritative CUI tokens; avoid native Avalonia visual leakage.

### V2-B1542 · v2 lines 6875–6875

Landing page

### V2-B1543 · v2 lines 6878–6878

Boards MUST implement the global launch-screen contract. Its landing page uses a board-centric card/grid composition: Create New is immediately available, recent boards are presented as visual cards with title/icon/preview metadata and last-modified state, and the left navigation/sidebar remains available so the user can move directly into an existing board.

### V2-B1544 · v2 lines 6881–6882

The default landing view MUST expose Boards plus a Recent pages/components view. Templates and other discovery/idea surfaces MAY be added only when backed by real persisted/template data. The landing page MUST NOT fabricate recent/sample boards except explicitly labelled onboarding/template content.
Landing-page interaction hierarchy: Present a clear "Your boards" heading and directly available New Board command, a well-spaced responsive Board card/grid with genuinely persisted previews, icon/title and last-modified data, search/filter navigation and real Recent Page entry points. A Board card opens its persisted Board directly. Provide meaningful empty, loading, offline and failure states. Rare utility actions (manual refresh, low-level storage/capability diagnostics and similar) MUST NOT compete visually with primary create/open/navigation actions; locate them contextually.

### V2-B1545 · v2 lines 6885–6885

Sidebar and board switching

### V2-B1546 · v2 lines 6888–6888

The Boards sidebar MUST show the currently selected Board as a dedicated board switcher control above that Board’s page tree. The control contains at least the Board icon and name. Activating it opens the board picker, allowing the user to search/select another Board without leaving the Boards app or opening a separate management screen.

### V2-B1547 · v2 lines 6891–6891

The sidebar page tree is scoped to ActiveBoardID and MUST support nested pages/subpages, selection, add page, rename, reorder/move and delete/recover according to the user’s permissions. Switching Board changes the page tree atomically to the selected Board and MUST NOT mix pages from multiple Boards in the active hierarchy.

### V2-B1548 · v2 lines 6894–6896

ActiveBoardID and ActivePageID are client/window navigation state and are not Board identity. Board/Page identity MUST remain stable across rename, reorder and sidebar position changes.
Sidebar presentation and navigation: The Board switcher MUST occupy a distinct header area above a compact, legible nested Page tree. The tree MUST visually distinguish selected Page, parent/child nesting, disclosure state and available contextual actions, with hover/focus-accessible add-page, rename, move/reorder, duplicate, Trash and recovery workflows wherever supported by the applicable permissions. Single-click page activation, deliberate rename affordances, deterministic drag/drop targets and keyboard alternatives MUST avoid accidental navigation or text edits. The sidebar MUST scroll independently where appropriate, support a useful narrow-window/collapsed state, and never render page management as a flat wall of large generic buttons.
Board/Page hierarchy presentation MUST update atomically on a Board switch: sidebar page tree, breadcrumb, Page title, active editor content, style/selection context and AI context MUST reflect the newly active Board/Page. Page title or icon changes MUST update visible navigation without changing stable identity.

### V2-B1549 · v2 lines 6899–6899

Canonical Board and Page model

### V2-B1550 · v2 lines 6902–6902

Board MUST contain at least BoardID, Name, Icon?, CreatedAt, ModifiedAt, owner/scope, ordered RootPageIDs, settings and revision metadata. BoardID is stable and MUST NOT derive from the Board name or Files path.

### V2-B1551 · v2 lines 6905–6905

BoardPage MUST contain at least PageID, BoardID, ParentPageID?, PageType, Title, Icon?, SortKey, CreatedAt, ModifiedAt, permission metadata, revision and the type-specific content/reference payload. PageID is stable across move, nesting and rename.

### V2-B1552 · v2 lines 6908–6908

In the release-candidate scope, PageType MUST support Native, Write, Canvas, Chat and ScopedChat. Source subpages belonging to Chat/ScopedChat are represented as explicit source entities/subpages under that chat page and are not silently interpreted as ordinary Native pages.

### V2-B1553 · v2 lines 6911–6911

Native Board pages MUST store a versioned BlockTree, page-level DrawingLayer, layout settings and view settings. Write and Canvas pages reference/use their canonical shared-engine artifact model rather than flattening those app behaviours into Boards-specific copies. Chat/ScopedChat pages reference a persistent Agent/chat configuration plus their source collection.

### V2-B1554 · v2 lines 6914–6914

Shared Productivity Engine

### V2-B1555 · v2 lines 6917–6917

Boards MUST use Home’s Shared Productivity Engine. Shared blocks/objects use the same schemas, formatting semantics, renderer/editor and typed actions as their equivalents in Write, Present, Canvas and Spaces. A table, code block, graph, image, drawing or other shared object copied between supported surfaces MUST retain its shared representation rather than being rebuilt as a Boards-only object.
