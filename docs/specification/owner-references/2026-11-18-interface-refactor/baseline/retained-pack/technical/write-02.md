# Write — document authoring — Styles and custom styles; Tables (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1291 · v2 lines 5483–5483

Unlocked object controls MUST support position/size/rotation; page/Section/paragraph/character anchoring where meaningful; inline/square/tight/behind/in-front/custom wrapping; aspect and position locks; z-order; align/distribute; grouping; snapping to margins, columns, guides, text bounds and other objects; and accessible non-drag alternatives.

### V2-B1292 · v2 lines 5486–5486

Switching layout modes MUST preserve data and positions as far as the target representation supports. If an object cannot be represented safely under the requested mode/foreign format, Write MUST warn rather than silently discard/flatten it.

### V2-B1293 · v2 lines 5489–5489

Styles and custom styles

### V2-B1294 · v2 lines 5492–5492

Custom styles MUST be first-class and front-and-centre whenever the selected content supports styles. For text, the active semantic style control SHOULD normally be the first contextual toolbar control rather than leading with raw font/size controls.

### V2-B1295 · v2 lines 5495–5495

Write MUST support built-in and arbitrary custom character/paragraph/document styles with stable StyleID, inheritance/overrides where supported, modify/create/update-from-selection, usage inspection and import/copy of style sets. Styles MAY control typography, paragraph/line spacing, indentation, alignment, borders/background, list behaviour, pagination/keep-with-next, language, outline level and semantic/accessibility role.

### V2-B1296 · v2 lines 5498–5498

Changing a shared/custom style MUST update linked uses while preserving explicit local overrides according to the style model. Write SHOULD preview the scope/impact of broad style changes where useful.

### V2-B1297 · v2 lines 5501–5501

Equivalent reusable style concepts SHOULD exist for tables and other supported object classes where they provide real value; these remain canonical style definitions rather than visual presets copied independently into each object.

### V2-B1298 · v2 lines 5504–5504

Tables

### V2-B1299 · v2 lines 5507–5507

Tables MUST support mature Writer/Office-level editing plus a stronger native .9to1w styling model. The contextual toolbar for a table SHOULD prioritise active Table Style plus common Borders, Fill, Padding, Alignment and structural actions, with less-common relevant actions in […].

### V2-B1300 · v2 lines 5510–5510

Whole-table controls MUST include width/height and fixed/content/flexible sizing as supported; alignment; wrapping; position/anchor; table padding/margins; background/opacity; border; shadow; caption; repeating header rows; pagination/split behaviour; and native rounded-corner support.

### V2-B1301 · v2 lines 5513–5513

Native .9to1w tables MUST support table corner radius, including linked or independent top-left/top-right/bottom-right/bottom-left radii. Foreign export MUST preserve this where the target format can represent it or explicitly warn/use a documented compatibility representation when it cannot.

### V2-B1302 · v2 lines 5516–5516

Row/column controls SHOULD include explicit/flexible/min/max sizing, equal distribution, visibility/header designation, keep-together and appropriate responsive/fixed behaviour. Cell controls MUST include independent top/right/bottom/left padding, horizontal/vertical alignment, text direction, background/fill, border per side, border width/style/colour/opacity, merge/split, minimum dimensions and defined overflow behaviour.

### V2-B1303 · v2 lines 5519–5519

Border editing MUST distinguish outer border, inner horizontal, inner vertical and individual cell edges. Cell padding MUST be directly discoverable and support linked/unlinked values rather than being buried in an obscure properties dialog.

### V2-B1304 · v2 lines 5522–5522

Custom Table Styles MUST be reusable first-class definitions capable of controlling whole-table appearance, header rows, first/last columns, odd/even rows, typography, borders, corner radius, cell padding, alignment and backgrounds. Linked tables update when the style changes except for explicit local overrides.

### V2-B1305 · v2 lines 5525–5525

Images, drawings and positioned objects

### V2-B1306 · v2 lines 5528–5528

Images and visual objects MUST support professional crop, resize, aspect ratio, wrap, anchor, positioning, z-order, caption, alt text, replacement and supported compression/export controls. The shared Rnote-derived drawing object is used for handwriting/sketch/annotation/diagram content so supported drawings remain editable across Write, Boards and Canvas.

### V2-B1307 · v2 lines 5531–5531

Multiple selected free objects expose align/distribute/group operations. Object controls and […] overflow remain contextual; irrelevant image controls MUST not appear for text/table selections.

### V2-B1308 · v2 lines 5534–5534

References and document structure

### V2-B1309 · v2 lines 5537–5537

References is a first-class semantic capability even though Write does not expose a permanent References ribbon. +, contextual controls, right-click and […] provide footnotes/endnotes, citations/bibliography, table of contents, captions, cross-references, bookmarks, indexes and fields where supported.

### V2-B1310 · v2 lines 5540–5540

Citation/reference data MUST be structured rather than only formatted prose so Agents and APIs can insert, validate, update and export references safely. Section/heading links and cross-references SHOULD use stable IDs rather than visible text/page numbers as identity.

### V2-B1311 · v2 lines 5543–5543

Review, comments and collaboration

### V2-B1312 · v2 lines 5546–5546

Write MUST support comments, tracked/suggested changes, accept/reject, previous/next change, document comparison and version history using real document/review semantics. AI edits in Suggest mode create suggestions rather than silently accepted direct changes.

### V2-B1313 · v2 lines 5549–5549

Realtime collaboration MUST use the normal 9to1 shared-artifact model for presence, cursors/selections, concurrent editing, comments, permissions/sharing and revision/conflict semantics. Naive timestamp last-writer-wins overwrite is non-conformant.

### V2-B1314 · v2 lines 5552–5552

Native format and foreign document formats

### V2-B1315 · v2 lines 5555–5555

.9to1w is the versioned native Write format and MUST preserve the full native document model, stable Productivity object identity, Sections, custom styles/table styles, embedded 9to1 artifact references, review/collaboration metadata, drawings/graphs, supported AI provenance and native features that foreign formats cannot represent. Migrations MUST be deterministic and preserve compatible unknown optional data.

### V2-B1316 · v2 lines 5558–5558

Write MUST support reliable DOCX and ODT import/export plus supported Markdown/TXT interchange and PDF export. Import/export fidelity is a release criterion, not a demo feature. Unsupported or lossy foreign-format behaviour MUST be surfaced rather than silently deleting content to force compatibility.

### V2-B1317 · v2 lines 5561–5561

PDF opening and editing

### V2-B1318 · v2 lines 5564–5564

Write MUST open and edit PDFs as first-class fixed-layout documents; PDF support is not export-only and MUST NOT simply convert every PDF into HTML or an approximate web editor. The Write shell remains consistent while an appropriate PDF/page-object engine (including applicable LibreOffice Draw/PDF capabilities or equivalent mature implementation) owns fixed-layout PDF object semantics.

### V2-B1319 · v2 lines 5567–5567

A digitally structured PDF SHOULD preserve selectable/editable native text, fonts, images, vector/page objects, links, annotations, forms/layers where supported and page geometry. Such PDFs default to Pages view and Unlocked layout because fixed page geometry is intrinsic.

### V2-B1320 · v2 lines 5570–5570

Scanned PDFs MUST be recognised as image/scanned content rather than falsely presented as native text. Write SHOULD offer explicit Recognise text/OCR-style transformation, preserve correspondence to the original page image and expose uncertain/low-confidence recognition rather than claiming perfect extraction. Mixed PDFs are handled page/object-by-page/object rather than forcing one simplistic document-wide mode.

### V2-B1321 · v2 lines 5573–5573

PDF editing MUST support useful fixed-layout workflows where technically supported: editing/moving existing text blocks, inserting/deleting text, images and objects, resize/crop, drawings/shapes, page rearrange/insert/delete/rotate, combine/extract pages, links, annotations/comments/highlights, proper redaction, supported form fields/signatures and headers/footers/page numbers.

### V2-B1322 · v2 lines 5576–5576

Before the first destructive PDF save, Write MUST preserve an original recoverable revision/backup and maintain normal version history. PDF AI operations use semantic PDF text/object structure when available and MUST NOT OCR screenshots of text that the PDF already exposes semantically.

### V2-B1323 · v2 lines 5579–5579

Continuous MUST NOT be forced onto a fixed-layout PDF. PDF defaults to Pages; only views/reflow operations that are meaningful for the active PDF are exposed, with fidelity implications made explicit.

### V2-B1324 · v2 lines 5582–5582

Persistent AI bar in Write

### V2-B1325 · v2 lines 5585–5585

Write implements the platform-wide shared AI-bar Read-only / Write-mode contract. In Read-only, Dulche can semantically inspect/search/compare the full authorised document, including off-screen Sections, headings, objects, styles, comments, references, tables and embedded artifact metadata, but cannot mutate the document. In Write mode it can use typed Write/Productivity actions subject to host editing/review/permission state.

### V2-B1326 · v2 lines 5588–5588

The AI context MUST expose stable DocumentID, document revision, current SectionID/heading/object, selection ranges/ObjectIDs, document structure, styles, comments, references, tables/images/embedded artifacts and available typed actions as authorised. Viewport visibility is not the AI's document boundary. Visual rendering may supplement this when layout must be inspected, but semantic access is primary.

### V2-B1327 · v2 lines 5591–5591

AI Read/Write mode is separate from document Edit/Suggest/View and Locked/Unlocked. For example AI Write mode + Suggest creates tracked suggestions; AI Write mode + Edit may perform direct typed edits; AI Read-only always prohibits mutations. The bar MUST NOT use mouse/keyboard simulation instead of Write APIs.

### V2-B1328 · v2 lines 5594–5594

The AI bar supports actions such as rewrite/shorten/expand, restructure, create/move Sections, apply/change styles, manipulate tables, insert objects/references, inspect missing citations, translate, compare Sections and perform other authorised semantic edits. Broad edits remain revision-safe, undoable/versioned and subject to normal conflict handling.

### V2-B1329 · v2 lines 5597–5597

Agents, sources and Translate integration

### V2-B1330 · v2 lines 5600–5600

Write MAY expose applicable Global, Write-scoped, Space and contextual Dulche Agents through the shared Agent picker. Specialist writing Agents such as Editor, Proofreader, Citation Assistant or Long-form Writer are canonical Dulche Agents, not private hard-coded AI runtimes.

### V2-B1331 · v2 lines 5603–5603

Documents MAY reference authorised source sets/files without unnecessarily duplicating them into the document. Source-backed AI work SHOULD preserve provenance where appropriate.

### V2-B1332 · v2 lines 5606–5606

Selection-level Translate invokes Dulche Translate directly for simple translation. Continue in Translate opens/preserves context in the built-in Translate Space for larger localisation workflows. Whole-document translation can produce separate Write derivatives/locale variants while preserving structure and source provenance.

### V2-B1333 · v2 lines 5609–5609

Embedded 9to1 artifacts and interoperability

### V2-B1334 · v2 lines 5612–5612

Write MAY embed/reference supported Boards, Canvas, Data ranges/charts, Picture assets, Planner items, Maps, Graphs and other canonical 9to1 artifacts as live embeds, linked previews or explicit snapshots according to the target type and permissions. Embedding MUST reference canonical entities rather than creating unmanaged duplicate state.

### V2-B1335 · v2 lines 5615–5615

Shared Productivity Engine bundles and object identities MUST support rich cross-app copy/paste/conversion where semantics are compatible; unsupported conversion MUST fail explicitly rather than silently flattening required behaviour.

### V2-B1336 · v2 lines 5618–5618

Navigation, search and long-document performance

### V2-B1337 · v2 lines 5621–5621

Write MUST provide document search/find-replace and efficient navigation for large documents. Sections/headings are semantic navigation, not a DOM outline derived from whatever is currently rendered. Long documents MUST not require eagerly rendering every page/object to remain editable.

### V2-B1338 · v2 lines 5624–5624

Switching Continuous/Pages/Book, opening/collapsing Sections, enabling counts/marks and changing selections MUST avoid unnecessary full-document re-rendering where the document engine can update incrementally.

### V2-B1339 · v2 lines 5627–5627

Recovery, persistence and accessibility

### V2-B1340 · v2 lines 5630–5630

Write MUST support autosave, unsaved-draft protection, crash recovery, revision/version history, conflict recovery and safe interrupted-save behaviour. A crash or partial write MUST not casually lose a user's document. Save/reopen workflows are part of acceptance testing.

### V2-B1341 · v2 lines 5633–5633

Keyboard-only operation, screen-reader semantics, focus indicators, scalable typography/zoom, accessible toolbar/context menus, non-drag alternatives for movement/layout, readable selection/review state and narrow-screen adaptation are mandatory. The Sections drawer, toolbar, floating statistics and AI bar must remain usable without overlap at supported scale factors.

### V2-B1342 · v2 lines 5636–5636

Canonical Write API

### V2-B1343 · v2 lines 5639–5672

9to1.Write.Create(config);
9to1.Write.Open(documentRef);
9to1.Write.GetDocument(documentID);
9to1.Write.Save(documentID);
9to1.Write.GetSelection(documentID);
9to1.Write.Insert(documentID, location, object);
9to1.Write.Replace(documentID, rangeOrObjectID, content);
9to1.Write.Delete(documentID, rangeOrObjectID);
9to1.Write.Sections.List(documentID);
9to1.Write.Sections.Create(documentID, config);
9to1.Write.Sections.Move(documentID, sectionID, target);
9to1.Write.Sections.Update(documentID, sectionID, patch);
9to1.Write.SetViewMode(documentID, mode);
9to1.Write.SetLayoutMode(documentID, mode);
9to1.Write.Styles.List(documentID);
9to1.Write.Styles.Create(documentID, definition);
9to1.Write.Styles.Update(documentID, styleID, patch);
9to1.Write.Tables.Get(documentID, tableID);
9to1.Write.Tables.Update(documentID, tableID, patch);
9to1.Write.Tables.SetStyle(documentID, tableID, tableStyleID);
9to1.Write.References.Insert(documentID, type, config);
9to1.Write.Comments.List(documentID);
9to1.Write.Comments.Create(documentID, target, content);
9to1.Write.Changes.SetMode(documentID, mode);
9to1.Write.Changes.Accept(documentID, changeID);
9to1.Write.Changes.Reject(documentID, changeID);
9to1.Write.Images.Update(documentID, objectID, patch);
9to1.Write.Objects.Position(documentID, objectID, placement);
9to1.Write.PDF.Open(fileRef);
9to1.Write.PDF.RecogniseText(documentID, scope, options?);
9to1.Write.PDF.EditObject(documentID, objectID, patch);
9to1.Write.PDF.Pages.Reorder(documentID, order);
9to1.Write.Export(documentID, format, options?);
These are minimum canonical entry points. Every user-visible Write operation, including contextual toolbar, context-menu, Sections, view/layout, review, PDF and AI-triggered operations, MUST have equivalent typed API coverage under the global parity rule.

### V2-B1344 · v2 lines 5675–5675

Failure behaviour

### V2-B1345 · v2 lines 5678–5678

Write MUST return structured failures including DocumentNotFound, UnsupportedDocumentVersion, ImportUnsupported, ImportLossy, ExportUnsupported, ExportLossy, SectionNotFound, StyleNotFound, TableNotFound, ObjectNotFound, InvalidRange, RevisionConflict, PermissionDenied, ReadOnlyDocument, LayoutConversionLoss, PDFUnsupported, PDFObjectNotEditable, OCRUnavailable, OCRUncertain, PDFBackupFailed, RecoveryUnavailable and ProviderUnavailable in addition to shared Home/Dulche/Files failures.

### V2-B1346 · v2 lines 5681–5681

Acceptance requirements
