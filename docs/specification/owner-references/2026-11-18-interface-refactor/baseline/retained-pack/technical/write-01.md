# Write — document authoring — Write — mature document editing through the actual engine; Real document, page and caret (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1223 · v2 lines 5297–5297

## 8. Write — mature document editing through the actual engine

### V2-B1224 · v2 lines 5299–5299

### UX27-WRITE-001 — Real document, page and caret

### V2-B1225 · v2 lines 5301–5301

**B/D.** Write remains a mature Writer/LibreOfficeKit-backed word processor, not a block-based web editor. Show actual pages, margins, pagination, text flow and a caret positioned by the document engine. Clicking a line sets the correct insertion position; dragging selects text rather than moving a page container. Text entry, selection and paragraph creation must work across page boundaries without a separate input box detached from the visible document.

### V2-B1226 · v2 lines 5303–5303

Use the same canonical document for the canvas, outline, inspector, AI actions and exports. A page image with an invisible unrelated text field cannot satisfy editable-document behaviour. Test multi-page text, tables and mixed formatting through the real routed app and standalone entry point.

### V2-B1227 · v2 lines 5305–5305

### UX27-WRITE-002 — Contextual toolbar and persistent actions

### V2-B1228 · v2 lines 5307–5307

**B.** Preserve the original Home/backstage, persistent +/Eye/overflow controls and no-permanent-ribbon rule, together with the 12 October rich floating-toolbar amendment. Daily font/style, size, emphasis, alignment, list, colour and link actions are directly reachable. The contextual toolbar is one coherent surface, not several unrelated strips added by each selection type.

### V2-B1229 · v2 lines 5309–5309

Selecting text, a table or an image changes only relevant groups and retains selection. Disabled/mixed states reflect the real engine. Do not hide ordinary Bold/size/style behind a long Properties inspector or turn the entire app into a menu of diagnostic commands.

### V2-B1230 · v2 lines 5311–5311

### UX27-WRITE-003 — Styles and paragraph formatting

### V2-B1231 · v2 lines 5313–5313

**B/D.** The style selector shows the paragraph/character style currently effective at the caret, including mixed selection. Applying a paragraph style updates the intended paragraphs without deleting inline content or replacing document identity. Direct formatting and named-style changes remain distinguishable; changing a style definition updates its references through the engine rather than rewriting each paragraph to an unrelated local style.

### V2-B1232 · v2 lines 5315–5315

Indentation, line spacing, before/after spacing, lists, tabs and page/section breaks have real layout effects. Undo/Redo and save/reopen preserve them. The app theme does not alter authored document colours/fonts or turn a dark document preview into an exported dark page without explicit document styling.

### V2-B1233 · v2 lines 5317–5317

### UX27-WRITE-004 — Sections and navigation

### V2-B1234 · v2 lines 5319–5319

**B/D.** The Sections/outline sidebar reflects semantic document headings/sections with stable targets, nested disclosure and meaningful selection. Clicking navigates to the correct region and reveals it without losing the caret unnecessarily. Renaming a heading from an approved outline action edits the actual text; reordering a section moves its defined content range with preview/undo, not just the sidebar row.

### V2-B1235 · v2 lines 5321–5321

Search shows matches across unloaded pages and navigates by stable positions. A hidden/offscreen paragraph is not absent from search/export. Long-document navigation uses indexes and incremental layout rather than blocking the UI while all prior pages are rendered.

### V2-B1236 · v2 lines 5323–5323

### UX27-WRITE-005 — Tables, images and anchored objects

### V2-B1237 · v2 lines 5325–5325

**B/D.** Table row/column/cell controls operate on real document tables, with merge/split, headers, borders and repeat-header behaviour where the donor supports it. Images support actual wrap/anchor/position/crop/size properties. Selecting an image exposes handles that follow the document’s layout at every zoom; dragging updates a preview and commits one engine transaction.

### V2-B1238 · v2 lines 5327–5327

Changing text before an anchored object follows the chosen anchoring semantics, not arbitrary screen coordinates. Copy/paste and export preserve the relationship or disclose a genuine conversion limitation. Do not substitute a screenshot of a table or image container for its mature engine behaviour.

### V2-B1239 · v2 lines 5329–5329

### UX27-WRITE-006 — Inspector, comments and review state

### V2-B1240 · v2 lines 5331–5331

**B/D.** The optional Properties inspector is resizable and context-specific, with logically grouped controls and a single scroll owner. Common operations stay in the toolbar; precise advanced configuration belongs here. Comments and supported review/track-change states are attached to canonical document ranges, with author/time/revision provenance and navigation to the target.

### V2-B1241 · v2 lines 5333–5333

Accept/reject review actions operate on actual changes, remain undoable where supported and do not silently accept unrelated edits. Missing review support must remain a donor-parity gap, not be hidden because the ordinary text editor passes a smoke test.

### V2-B1242 · v2 lines 5335–5335

### UX27-WRITE-007 — File formats, persistence and export

### V2-B1243 · v2 lines 5337–5337

**B/D.** Preserve the native/shared model and required donor interoperability. Open/save/export use genuine supported formats, with explicit conversion limitations and exact output identity. A successful save is verified by closing and reopening the produced artifact, checking representative text, styles, images, tables, notes/review features and page layout where applicable.

### V2-B1244 · v2 lines 5339–5342

Saving in an ordinary local location must not unnecessarily depend on a cloud account. Large-document autosave is incremental according to the scalability annex. A failed export cannot leave a truncated file labelled complete or replace the only valid original.
<!-- UX27:END SECTION-08 -->
Write
Product role

### V2-B1245 · v2 lines 5345–5345

9to1 Write is the production document/word-processing surface. It MUST behave like a mature word processor rather than a web editor, site builder, Markdown editor, collection of HTML blocks or browser contenteditable surface. LibreOffice Writer/LibreOfficeKit is the mature donor/layout/format engine for text-oriented documents; 9to1 owns the CUI shell, shared Productivity Engine integration, collaboration, AI, Sections model and native extensions.

### V2-B1246 · v2 lines 5348–5348

The document surface MUST use an actual structured document model with paragraph/run/style/section/table/object/reference/review semantics and real layout/text measurement. Caret movement, selection, rich clipboard, IME input, undo/redo transactions, object anchoring, line wrapping, pagination, spelling/grammar ranges, accessibility semantics and large-document behaviour MUST be implemented as document-editor behaviour, not emulated with disconnected DOM widgets.

### V2-B1247 · v2 lines 5351–5351

Continuous view is a presentation of the same structured word-processing document, not conversion into HTML. A document MUST switch between supported views without changing its canonical content or losing document semantics.

### V2-B1248 · v2 lines 5354–5354

Donor parity and objective word-processor behaviour

### V2-B1249 · v2 lines 5357–5357

All applicable LibreOffice Writer functionality, quality-of-life behaviour, keyboard interactions, menus/workflows and file-format depth MUST be preserved or appropriately adapted. A claimed technical impossibility remains an open blocker unless the owner explicitly approves a precise scoped exception with its impact, substitute and evidence under APP-AC-004; documentation alone does not waive donor parity. The donor source is authoritative for inherited functionality; Write MUST NOT ship as a simplified rich-text clone simply because a smaller editor is easier to implement. Normal end-user installation must include the required engine dependencies through the approved packaging/runtime system, not require a separate manual donor installation.

### V2-B1250 · v2 lines 5360–5360

Inherited/core capability includes, where applicable, character and paragraph formatting; custom styles; page styles; headers/footers; sections; columns; page/section breaks; lists/outlines; tables; images/drawings/shapes; equations; fields; bookmarks; cross-references; captions; footnotes/endnotes; tables of contents; indexes; citations/bibliographies; comments; tracked changes; document comparison; spelling/grammar/language features; find/replace; hyphenation; print/page layout; mail merge; document properties; object anchoring/wrapping; export/print and other mature Writer functionality.

### V2-B1251 · v2 lines 5363–5363

Launch and file entry

### V2-B1252 · v2 lines 5366–5366

Without a requested document or supported session to restore, Write opens to a distinct start experience under the shared modern Launch Screen contract and LAUNCH-001–006, with Blank document, Templates, Recent, Favourites, Shared with me, search, Open and Import. Entries MUST be backed by real Files/Drive data rather than mock cards. Creating/opening/importing a document establishes the canonical Write artifact before transitioning into the editor. Direct document opening and supported session restoration enter the requested/restored editor without an intervening start page. The start screen and editor have separate compositions and active interaction scopes within the shared CUI shell: the document canvas and document-specific formatting tools MUST NOT sit below the start page, and launch-only content MUST NOT remain as a header above the editor. Deliberate return to Write’s start surface and resumption preserve the document and applicable session state under LAUNCH-004; the title/backstage menu retains its separately specified behaviour.

### V2-B1253 · v2 lines 5369–5369

Editor shell

### V2-B1254 · v2 lines 5372–5372

Write MUST NOT use a permanent Office-style ribbon. The editing shell consists of a document-title row, a floating contextual toolbar immediately below the title at the top of the document area, an optional collapsible Sections sidebar, the document canvas and the persistent shared AI bar at the bottom.

### V2-B1255 · v2 lines 5375–5375

The Home button sits immediately to the left of the document title and opens an Office-style backstage/file menu for document-level operations such as Info, New, Open, Recent, Save/Save a copy, Share, Export, Print, Version history, Document properties and Settings. Closing the menu returns to the same document/selection position.

### V2-B1256 · v2 lines 5378–5378

Floating contextual toolbar

### V2-B1257 · v2 lines 5381–5381

The floating toolbar stays in a stable position immediately below the title while its contextual controls change according to the current semantic selection. Text, heading, image, table, reference/review and other contexts expose their own relevant operations. An operation that cannot apply to the current selection MUST NOT appear merely because it exists elsewhere in Write; for example Crop image cannot appear when no image is selected.

### V2-B1258 · v2 lines 5384–5384

Three controls are persistent regardless of selection: +, Eye and overflow […]. They remain present even as all other controls change. When horizontal space is constrained, lower-priority contextual actions move into overflow before these persistent controls disappear.

### V2-B1259 · v2 lines 5387–5387

The overflow […] menu contains additional actions relevant to the current context that did not fit or were deprioritised on the main bar. It is not a generic dump of all Write commands. The menu for a table selection differs from the menu for text or an image and MUST exclude inapplicable actions.

### V2-B1260 · v2 lines 5390–5390

Right-click/context menus expose the same semantic action model in menu form. Context menus and the floating bar call the same typed Write/Productivity APIs rather than maintaining parallel behaviour.

### V2-B1261 · v2 lines 5393–5393

Pinned toolbar actions

### V2-B1262 · v2 lines 5396–5396

Users MUST be able to pin supported contextual actions to the top bar and reorder/reset their pinned preferences. Pinning means prioritise this action whenever it is applicable; it MUST NOT force an irrelevant disabled action to occupy the toolbar in unrelated contexts. Pinning is per-user UI preference rather than collaborative document state.

### V2-B1263 · v2 lines 5399–5399

Toolbar priority SHOULD resolve from the most important current-context control, then applicable user-pinned controls, then common contextual actions, followed by the persistent +, Eye and […] controls.

### V2-B1264 · v2 lines 5402–5402

Universal Insert (+)

### V2-B1265 · v2 lines 5405–5405

The persistent + control is the universal Insert entry point. It opens a searchable, context-aware insertion palette rather than requiring an Insert ribbon. The complete palette MUST expose applicable content/structure/reference/9to1 artifact options while prioritising items relevant to the current caret/object context.

### V2-B1266 · v2 lines 5408–5408

Insertion types SHOULD include tables, images, drawings, equations, code blocks, text boxes, shapes/callouts, horizontal lines, page/section breaks, columns, headers/footers, footnotes, citations, cross-references, bookmarks, captions, table of contents, special characters, fields, links/files and supported embedded 9to1 artifacts such as Canvas, Board, Data ranges/charts, Picture, Planner items, Maps and Graphs.

### V2-B1267 · v2 lines 5411–5411

Eye / visibility controls

### V2-B1268 · v2 lines 5414–5414

The persistent Eye control owns view/visibility options rather than document-format mutations. Its menu MUST include the current Write View mode and may include Sections visibility, Comments, spelling/grammar marks, formatting marks, ruler, page boundaries, object anchors, alignment/snap guides and the floating word/character count where applicable.

### V2-B1269 · v2 lines 5417–5417

Most Eye settings are per-user view preferences and MUST NOT change what collaborators see. Contextually useful visibility aids MAY appear only when relevant, for example object bounds/anchors/guides when an object is selected.

### V2-B1270 · v2 lines 5420–5420

Floating document statistics

### V2-B1271 · v2 lines 5423–5423

When enabled from Eye, an unobtrusive document-statistics indicator floats in the bottom-right of the document area without becoming document content or consuming page layout. It SHOULD show document word and character counts, and when text is selected it SHOULD prioritise selection word/character counts. Clicking it MAY reveal expanded statistics such as characters without spaces and page count where meaningful.

### V2-B1272 · v2 lines 5426–5426

The statistics indicator and bottom AI bar MUST coexist without overlap at supported sizes.

### V2-B1273 · v2 lines 5429–5429

Sections sidebar

### V2-B1274 · v2 lines 5432–5432

Write has a Google-Docs-inspired collapsible Sections sidebar on the left. The product terminology is Sections, not Tabs. A Section is a stable user-created structural division of the same Write document and MAY contain nested Sections. Headings inside each Section are derived automatically from document heading/outline styles.

### V2-B1275 · v2 lines 5435–5435

Every Section MUST have stable SectionID, DocumentID, optional ParentSectionID, Name, optional icon/metadata, ordering and revision/state information. Renaming/reordering/nesting MUST preserve SectionID so links, AI context and cross-references are not tied to visible names.

### V2-B1276 · v2 lines 5438–5438

The expanded sidebar presents Sections as stronger/root nodes with the heading hierarchy beneath each Section. Clicking a heading navigates to it; changing heading style updates the outline live. Moving a heading moves its heading plus subordinate content using structured document operations. Moving a Section moves the Section's canonical content safely.

### V2-B1277 · v2 lines 5441–5441

The sidebar has a + control for New section, New subsection, From selection and supported Import-as-section workflows. Section/headings support context actions such as rename, duplicate, move before/after/into, copy link, export section, delete and relevant AI/translation operations.

### V2-B1278 · v2 lines 5444–5444

The Sections sidebar MUST be collapsible to a narrow edge/rail and resizable when expanded. Collapsed/expanded state and width are per-user/device preferences. Collapsing immediately returns horizontal space to the document canvas; navigation/AI actions MUST NOT forcibly reopen it. On narrow layouts the sidebar becomes a temporary drawer. Keyboard and non-drag movement/navigation alternatives are mandatory.

### V2-B1279 · v2 lines 5447–5447

The Sections sidebar and an inserted Table of Contents are distinct. The sidebar is editing/navigation structure; + → Table of contents inserts a real document object generated from configured heading levels/styles.

### V2-B1280 · v2 lines 5450–5450

View modes

### V2-B1281 · v2 lines 5453–5453

Eye exposes three first-class Write View modes: Continuous, Pages and Book. Switching view is immediate presentation state over the same DocumentID, SectionIDs, ObjectIDs, selection, comments and revision. The user's view mode does not force collaborators into the same view.

### V2-B1282 · v2 lines 5456–5456

Continuous is the default for new .9to1w documents unless the chosen template requires another view. It presents one seamless pageless writing surface with a sensible readable content width and no artificial sheet gaps. Real page breaks remain canonical and are shown as unobtrusive markers rather than forcing paper sheets.

### V2-B1283 · v2 lines 5459–5459

Continuous view MUST retain page-aware document features such as headers/footers, footnotes, page numbers, margins, section/page breaks and columns in the underlying document model. The view may present them contextually rather than permanently rendering every page boundary.

### V2-B1284 · v2 lines 5462–5462

Pages renders exact page layout using modern rounded pages, restrained elevation, clean spacing and a neutral workspace while preserving true margins, headers/footers, footnotes, page numbers and print pagination. It MUST not use dated harsh page chrome merely because the donor does.

### V2-B1285 · v2 lines 5465–5465

Book renders publication-aware facing-page spreads and supports odd/even pages, right/left page semantics, mirrored inside/outside margins, binding gutter, different left/right headers/footers, section starts/front matter and deliberate blank-page behaviour. Narrow displays MAY show one page at a time while preserving spread semantics.

### V2-B1286 · v2 lines 5468–5468

View mode is independent from Locked/Unlocked layout mode. Supported combinations include Continuous + Locked, Continuous + Unlocked, Pages + Locked, Pages + Unlocked, Book + Locked and Book + Unlocked.

### V2-B1287 · v2 lines 5471–5471

Locked and Unlocked layout

### V2-B1288 · v2 lines 5474–5474

Write MUST implement the same first-class Locked/Unlocked layout concept as Boards. LayoutMode is independent from the user's current View mode and from AI Read/Write mode.

### V2-B1289 · v2 lines 5477–5477

Locked is the conventional word-processing flow and the default for ordinary documents. Paragraphs/inline content follow document structure and supported objects use anchors/wrapping rules so accidental free positioning is constrained.

### V2-B1290 · v2 lines 5480–5480

Unlocked adds a professional free-positioned visual object layer for supported images, text boxes, shapes, drawings, charts, embedded artifacts, callouts, equations and tables where applicable. Users may drag, resize, rotate, overlap, layer, align, distribute and snap these objects without converting the whole document into arbitrary floating text boxes.
