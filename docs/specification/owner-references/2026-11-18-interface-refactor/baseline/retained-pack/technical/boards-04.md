# Boards — seamless pages, blocks and ink — Edit and View modes; Locked and Unlocked layout modes (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1556 · v2 lines 6920–6920

Boards MAY add Boards-specific container/layout metadata around a shared object, but MUST NOT fork the shared object payload itself. Cross-app copy/paste uses the versioned ProductivityObjectBundle contract owned by Home.

### V2-B1557 · v2 lines 6923–6923

Edit and View modes

### V2-B1558 · v2 lines 6926–6926

Every Board page surface MUST expose a persistent Edit/View switch in the top-right app/page chrome. Edit and View are authoring modes and are independent from the Locked/Unlocked layout mode defined below.

### V2-B1559 · v2 lines 6929–6929

Edit mode permits mutations allowed by the user’s permissions and exposes authoring controls. View mode blocks document/page mutations while retaining navigation, selection/copy where permitted and explicitly view-safe interactive behaviour.

### V2-B1560 · v2 lines 6932–6932

View mode MAY unlock runtime interactions that are intentionally non-authoring. For example, a Graph block can expose sliders so a viewer can vary values and inspect the resulting graph. Such interaction state is session-local by default and MUST NOT modify the stored document or default graph configuration unless an explicit authorised save action is invoked in Edit mode.

### V2-B1561 · v2 lines 6935–6935

The persistent AI bar remains available in both modes. In View mode it is read-only with respect to Board/page content; an AI-requested mutation MUST require an authorised transition/action under Edit semantics rather than silently bypassing View mode.

### V2-B1562 · v2 lines 6938–6938

Locked and Unlocked layout modes

### V2-B1563 · v2 lines 6941–6941

Native Board pages MUST support a separate LayoutMode with values Locked and Unlocked. LayoutMode controls spatial placement behaviour only; it does not replace Edit/View permissions and it does not disable free drawing.

### V2-B1564 · v2 lines 6944–6944

Locked mode preserves AppFlowy-style block-flow behaviour for flow blocks: blocks participate in the normal ordered document/block tree and are inserted/reordered using the donor-compatible flow layout. Existing absolute-position blocks remain visible at their saved coordinates but their free-position move/resize handles are disabled while Locked.

### V2-B1565 · v2 lines 6947–6947

Unlocked mode retains normal flow editing and additionally permits blocks to be placed/repositioned freely anywhere on the page canvas. A freely positioned block receives Layout = Absolute with X, Y, Width, Height and ZOrder. A normal donor-flow block uses Layout = Flow.

### V2-B1566 · v2 lines 6950–6950

Switching from Unlocked back to Locked MUST NOT snap, reflow, discard or convert Absolute blocks. Their exact stored coordinates and dimensions remain unchanged. Locked mode simply prevents casual spatial repositioning while continuing to allow the block’s content to be edited in Edit mode.

### V2-B1567 · v2 lines 6953–6953

Unlocked drag/drop MAY convert a Flow block to Absolute only through an explicit spatial placement gesture. Converting an Absolute block back to Flow MUST be an explicit action because it changes document layout semantics.

### V2-B1568 · v2 lines 6956–6956

Free drawing and ink

### V2-B1569 · v2 lines 6959–6959

Native Board pages MUST support free drawing directly over the page canvas using the Home Shared Productivity Engine’s Rnote-derived drawing/ink implementation. Ink is stored in the page DrawingLayer using stable stroke/object IDs and MUST remain editable after save/restart.

### V2-B1570 · v2 lines 6962–6962

Drawing MUST work in both Locked and Unlocked layout modes. Layout locking MUST NOT disable pen/eraser/ink editing. View mode remains non-authoring and therefore MUST NOT create/erase strokes.

### V2-B1571 · v2 lines 6965–6965

Drawing coordinates MUST remain anchored to the page/canvas coordinate system so strokes do not drift when surrounding Flow blocks are edited. Where ink is attached/anchored to a specific object, that relationship MUST be explicit in the drawing object rather than inferred from screen pixels.

### V2-B1572 · v2 lines 6968–6968

Floating contextual formatting island

### V2-B1573 · v2 lines 6971–6971

Boards MUST expose a persistent floating contextual tool island near the top of the content surface while in Edit mode. It is app chrome, not document content. Common authoring/formatting controls MUST be directly reachable from this island rather than requiring the block insertion (+) menu.

### V2-B1574 · v2 lines 6974–6974

The island adapts to the active block/object/selection. For text it exposes the applicable style/heading, emphasis, text/paragraph formatting and related controls; for tables, images, drawings and graphs it exposes the relevant object-specific controls. Multi-selection exposes only actions valid for the current common selection.

### V2-B1575 · v2 lines 6977–6977

When no editable object is selected, the island MAY show page-level/default insertion/layout controls. The + menu remains an insertion/discovery surface; it MUST NOT be the sole location for routine formatting of the currently selected content.

### V2-B1576 · v2 lines 6980–6983

In drawing mode the island MUST expose the active Rnote-derived pen/eraser/ink controls. In View mode authoring controls are removed or replaced by view-safe controls while the Edit/View switch remains reachable.
Text-mode tool island composition (October 2026 UI acceptance refinement): The always-accessible floating island MUST include a current text/block-type dropdown whose label reflects the caret/selected block (Paragraph, Heading 1–6 or a selected named custom paragraph/header style). This is a text type selector, not a theme selector. It MUST expose the prominently grouped emphasis controls B (Bold), I (Italic), S (Strikethrough) and U (Underline), plus practical list/checklist, link, relevant text colour/highlight/alignment and paragraph actions, Undo/Redo and an obvious + Insert command according to the applicable donor/shared-engine capabilities. Do not hide routine text-type conversion or B/I/S/U in a generic Properties panel or require the inline + menu for ordinary text formatting.
Text selection and conversion semantics: Clicking or caret-navigating between Paragraph/H1/H2/H3/H4/H5/H6 and custom-style blocks MUST update the displayed text type immediately. Choosing a type MUST genuinely convert the selected eligible block(s), preserving text content, valid inline marks, shared-engine semantics, history and persistence. The B/I/S/U states MUST accurately reflect selection/caret formatting, including mixed selections; applying formatting MUST affect only intended selected text or eligible ranges. Using the island MUST NOT unexpectedly collapse, lose or redirect the editing selection. Focus restoration, keyboard shortcuts, toggle-off, valid disabled/unavailable states and undo/redo MUST be functional and testable, including after scrolling.
Island positioning and accessibility: The island MUST remain visually floating and available during Edit-mode scrolling without obscuring the caret or document content, creating unpredictable layout shifts or jumping on selection changes. Use compact but usable hit targets, meaningful tooltips, consistent grouping/separators, proper hover/focus/selected states, screen-reader labels, keyboard navigation and anchored popover layering. When space is constrained, preserve discoverability of the text-type selector and frequent emphasis/Insert commands with a deliberate adaptive overflow/wrap pattern rather than unlabelled clipped glyphs.

### V2-B1577 · v2 lines 6986–6986

Persistent AI bar

### V2-B1578 · v2 lines 6989–6989

Boards MUST implement the global persistent bottom AI bar. Its semantic context MUST include the active Board, active Page, selected block/object IDs, Edit/View state, LayoutMode and the user-authorised Board/Den context relevant to the request.

### V2-B1579 · v2 lines 6992–6992

AI mutations MUST use 9to1.Boards.* or the owning embedded artifact APIs. Proprietary AppFlowy AI actions MUST NOT be invoked or emulated through hidden donor services. Dulche is the AI runtime and Home remains the shared permission/context broker.

### V2-B1580 · v2 lines 6995–6995

Block system and AppFlowy block parity

### V2-B1581 · v2 lines 6998–6998

Boards MUST include every non-proprietary-AI AppFlowy block type available in the selected legally usable donor revision, with its applicable editing behaviour and quality-of-life interactions, unless the donor-parity manifest records a concrete exception. The implementation MUST NOT maintain a manually shortened hard-coded parity list that silently falls behind the selected donor source.

### V2-B1582 · v2 lines 7001–7001

Boards additionally MUST support built-in Heading 4, Heading 5 and Heading 6 block/text styles, in addition to the donor-compatible heading levels, and MUST support user-defined named header and paragraph styles. Custom styles MUST be stored as versioned style definitions referenced by stable StyleID so renaming a style does not detach existing blocks.

### V2-B1583 · v2 lines 7004–7004

A custom text style definition MUST be capable of specifying the applicable shared text properties including font family/reference, size, weight, emphasis, foreground/background/highlight, paragraph spacing, line spacing, alignment and other supported shared-engine text attributes. Unsupported properties MUST be preserved during cross-app round-tripping.

### V2-B1584 · v2 lines 7007–7009

New shared object/block types registered in Home’s Shared Productivity Engine MUST become available to Boards through the common registry whenever Boards declares support for that type. Boards-specific wrappers MAY supply page/layout controls, but duplicate object engines are non-conformant.
Inline block controls and direct manipulation: Boards MUST retain an inline + add-block affordance AND an inline six-dot/:: block grip, in addition to the persistent floating top toolbar. The inline controls MUST be discoverable on block hover/focus with keyboard-accessible equivalents and adequate target size. Clicking + opens a contextual insert menu positioned at the active block and inserts the chosen block in the intended place; clicking the grip opens a real block context menu, while dragging it reorders Flow blocks or triggers a deliberate permitted spatial-placement gesture according to LayoutMode. Clearly indicate drag state, target location and invalid targets; support edge auto-scroll, predictable pointer/cursor states and prevent hover flicker.
Editor gesture boundaries and keyboard behaviour: Text editing, caret movement, selection, block selection, drag/reorder, absolute positioning and resizing MUST remain distinguishable; editing or selecting text must not accidentally move a block, and ordinary clicking a grip must not start an unintentional drag. Insert menus MUST cover applicable registered/donor-parity blocks rather than a shortened hard-coded demonstration list, and MUST support good search/category discovery, outside-click/Escape dismissal, keyboard focus and valid screen-edge placement. Provide donor-compatible slash insertion, Enter/Backspace block split/merge, lists/checklists, link editing, copy/paste and undo/redo with stable BlockIDs and persisted operations.

### V2-B1585 · v2 lines 7014–7014

Graph block

### V2-B1586 · v2 lines 7017–7017

Boards MUST provide a shared-engine Graph block for mathematical/analytical graphing. A Graph block MUST support multiple simultaneous expressions/series, independently styled plots, points/markers, labelled axes, configurable viewport/domain/range and shaded regions/inequalities.

### V2-B1587 · v2 lines 7020–7020

GraphBlock data MUST include stable GraphID, ordered plot/expression definitions, variables, optional slider definitions, viewport/axes settings and style metadata. Multiple expressions MUST coexist in one graph without being flattened into a rendered image.

### V2-B1588 · v2 lines 7023–7023

A slider definition MUST include VariableID/name, minimum, maximum, step and default value. In Edit mode the author configures slider bounds/defaults. In View mode viewers MAY manipulate enabled sliders; temporary slider values are per-view session unless explicitly saved as new defaults by an authorised Edit action.

### V2-B1589 · v2 lines 7026–7026

Graph blocks MUST support shared-engine copy/paste and remain editable after transfer to another participating surface that declares Graph support. Exporting to static image is a separate explicit operation.

### V2-B1590 · v2 lines 7029–7029

Enhanced image formatting

### V2-B1591 · v2 lines 7032–7032

Image objects in Boards MUST support, at minimum, resize with aspect-ratio control, crop, rotate, fit/fill behaviour, alignment/positioning, caption, alt text/accessibility description and link/reference metadata. Non-destructive image transforms MUST preserve the source asset until the user explicitly exports/rasterises a transformed copy.

### V2-B1592 · v2 lines 7035–7035

Enhanced table formatting

### V2-B1593 · v2 lines 7038–7038

Table objects MUST support add/remove/reorder rows and columns, row/column resize, merge/split where structurally valid, horizontal/vertical cell alignment, text formatting, cell background/fill, borders, header row/column styling and table width/alignment controls. Formatting MUST remain separate from the cell data/value payload so copy/paste and automation can manipulate either deterministically.

### V2-B1594 · v2 lines 7041–7041

Cross-app parity blocks

### V2-B1595 · v2 lines 7044–7044

Boards MUST expose shared-engine blocks/objects that have equivalents in the other productivity surfaces instead of creating incompatible lookalikes. Required cross-app object families include rich text, tables, code, equations, images/media, ink/drawings and Graph. Additional shared-engine object types become eligible through the common registry.

### V2-B1596 · v2 lines 7047–7047

App-specific artifacts MAY be embedded as canonical artifact references. An embedded mutable artifact MUST retain its owning app identity/capabilities and MUST NOT be flattened to a screenshot/static copy except through an explicit static export action.

### V2-B1597 · v2 lines 7050–7050

Write and Canvas page types

### V2-B1598 · v2 lines 7053–7053

Add Page MUST offer Write and Canvas as first-class PageType options alongside Native. Creating a Write page creates/attaches the canonical Write document model inside that Board page and presents Write editing behaviour within the Boards shell. Creating a Canvas page does the equivalent for Canvas.

### V2-B1599 · v2 lines 7056–7056

Write/Canvas Board pages MUST use the same Home Shared Productivity Engine and underlying artifact behaviour as the standalone apps, not a visually similar Boards reimplementation. The Board page is a container/reference to the canonical artifact; edits made inside Boards and edits made through the standalone owning app or Spaces MUST resolve to the same artifact/revision history.

### V2-B1600 · v2 lines 7059–7059

Opening a Write/Canvas page inside Boards MUST NOT launch a second external app window merely to obtain basic editing behaviour. Shared engine capabilities are hosted through Home and rendered in the Boards page surface.

### V2-B1601 · v2 lines 7062–7062

Chat page type

### V2-B1602 · v2 lines 7065–7065

Chat is a first-class Board PageType backed by a persistent 9-1 Agent executed through Dulche. The Agent automatically receives permission-filtered semantic knowledge of the containing Board and its authorised pages/items. It MAY also use the user’s connected apps/integrations and relevant persistent context registered in Dulche Den when the Agent/tool permissions permit those actions.

### V2-B1603 · v2 lines 7068–7068

Chat MUST use typed 9to1 app/integration actions for consequential work. Board membership or Chat context MUST NOT grant implicit access to an external app, tool or Den namespace that the caller/Agent is not otherwise authorised to use.

### V2-B1604 · v2 lines 7071–7071

The Chat page stores a stable AgentID/definition revision plus ChatSession/Conversation references independently from the visual page title. Renaming/moving the page MUST NOT create a new Agent or lose history.

### V2-B1605 · v2 lines 7074–7074

Scoped Chat page type

### V2-B1606 · v2 lines 7077–7077

Scoped Chat is a closed-corpus notebook-style Chat PageType. Its answers MUST be grounded only in the authorised contents of the containing Board plus the explicit source collection attached to that Scoped Chat. It MUST NOT search the public web, invoke external-information retrieval tools or consult unrelated connected-app content to fill evidence gaps.

### V2-B1607 · v2 lines 7080–7080

When the permitted corpus does not contain enough information to support an answer, Scoped Chat MUST report that the available sources do not support the requested point rather than silently answering from general model knowledge. Transformation/reasoning over supplied material remains allowed.

### V2-B1608 · v2 lines 7083–7083

Scoped Chat responses MUST expose source provenance at the Board page/source-object level so the user can navigate back to the material supporting the response.

### V2-B1609 · v2 lines 7088–7088

Chat source subpages

### V2-B1610 · v2 lines 7091–7091

Chat and Scoped Chat MUST support additional source subpages beneath the Chat page. Uploaded source files become stable Source entities with SourceID, display name, source type, content/artifact reference, ingestion/index status, hash/revision metadata and permission state.

### V2-B1611 · v2 lines 7094–7094

Source ingestion MUST be asynchronous/observable for large files and MUST distinguish Pending, Ready, Failed and Removed states. Failed ingestion MUST NOT make the source appear successfully available to the Agent.

### V2-B1612 · v2 lines 7097–7097

For ordinary Chat, uploaded sources supplement Board/authorised connected context. For Scoped Chat, the Board plus explicit source collection defines the maximum information corpus. Removing/revoking a source prevents future reads while preserving audit/history semantics required by Dulche Den.

### V2-B1613 · v2 lines 7100–7100

Data-backed structured items

### V2-B1614 · v2 lines 7103–7103

Boards does not create a second private database engine. Where the AppFlowy donor exposes database-like blocks, records or views, Boards MUST implement the applicable user-facing behaviour by hosting or referencing canonical 9to1 Data artifacts/views through the Shared Productivity Engine and typed Data APIs. A Board item may therefore display and interact with a Data table/range/view while the underlying rows, fields, formulas, permissions and revisions remain owned by Data.

### V2-B1615 · v2 lines 7106–7106

Donor parity still applies to the Boards-facing workflow: applicable filtering, sorting, grouping, view selection, record presentation and related quality-of-life behaviour MUST either map to supported Data capabilities or be recorded as a concrete donor-parity exception. Boards MUST NOT silently fork Data state into an incompatible local database merely to imitate donor UI.

### V2-B1616 · v2 lines 7109–7109

Board document-space coordinates

### V2-B1617 · v2 lines 7112–7112

Persisted Native-page spatial placement MUST use Board document-space coordinates, not CUI's -1000…1000 proportional screen coordinate system. The page content origin is (0,0); X increases to the right and Y increases downward. X, Y, Width and Height MAY be decimal document-space values and MAY extend outside the initial viewport where the page supports panning/free placement.
