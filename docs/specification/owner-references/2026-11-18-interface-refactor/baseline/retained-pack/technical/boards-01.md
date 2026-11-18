# Boards — seamless pages, blocks and ink — Boards — exact shell, navigation and local-first file behaviour; The intended composition (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1393 · v2 lines 6555–6555

## 2. Boards — exact shell, navigation and local-first file behaviour

### V2-B1394 · v2 lines 6557–6557

### UX27-BOARD-001 — The intended composition

### V2-B1395 · v2 lines 6559–6559

**O/B.** The Boards workspace comprises a compact application/board header, a left navigation sidebar, one primary seamless page/editor surface, the retained floating top editing toolbar and the shared bottom AI surface. Put the Boards identity at the top-left. Remove AppFlowy branding from the top-right; upstream attribution belongs in About/licence information, not a donor badge impersonating the product. Keep one clear editable page title and a breadcrumb expressing Board and Page identities without repeating the same title in multiple unrelated fields.

### V2-B1396 · v2 lines 6561–6561

The sidebar has a board switcher/search area, the nested section/page tree, and a fixed bottom footer containing the Add page/section affordance and user/avatar controls. The 25 October instruction moves this Add control to the bottom; it does not remove the top editing toolbar or its separate Insert command. Do not confuse adding a page with inserting a block.

### V2-B1397 · v2 lines 6563–6563

### UX27-BOARD-002 — Geometry defaults, not guessed screenshot measurements

### V2-B1398 · v2 lines 6565–6565

**D.** Unless a recovered approved reference supplies more precise values, start with a 256-unit sidebar, user-resizable from 208 to 360; 32-unit tree rows; 16-unit indentation per depth; an approximately 48-unit fixed footer; and a 20-unit page-tree icon slot. Reserve the disclosure slot even for leaves so labels align. The content surface uses a readable 760-unit flow column by default, expandable through a real page-width preference; its background remains continuous rather than becoming a floating sheet/card.

### V2-B1399 · v2 lines 6567–6567

Keep toolbar clearance above the first content block and AI-bar clearance below the last editable line. These clearances follow actual visible chrome bounds, not an arbitrary permanent blank block. On narrow windows, collapse navigation deliberately and keep an explicit reopen control; do not push the editor off screen.

### V2-B1400 · v2 lines 6569–6569

### UX27-BOARD-003 — New Board without setup prerequisites

### V2-B1401 · v2 lines 6571–6571

**O/B/D.** From the Boards landing page, New Board creates a durable local draft with a stable BoardID and an initial editable Native page, then focuses its title or first content block. A name is editable inline; a file path, Files connection, login, cloud subscription, AI model or browser renderer is not required. Do not show a mandatory “connect Files” wizard, hide Create, or demand a predefined Boards folder.

### V2-B1402 · v2 lines 6573–6573

A simple creation popup may offer optional name/template details, but the default empty-board route must remain immediate and usable. Template content is explicitly chosen, not injected as fake recent work. A shared-core installation/repair requirement is handled by the packaged platform contract; ordinary users must not manually wire internal storage services to create a local board.

### V2-B1403 · v2 lines 6575–6575

### UX27-BOARD-004 — Drafts before Save As

### V2-B1404 · v2 lines 6577–6577

**O/B/D.** An unnamed board is a recoverable local draft, not disposable memory. Allocate canonical identity immediately and persist edits through the normal local owning-storage adapter. Show `Saved locally — draft` only after a durable commit. Keep recoverable drafts in the application’s normal private data area without making that implementation path a user-facing mandatory destination.

### V2-B1405 · v2 lines 6579–6579

Closing and reopening the application restores the draft through Recent/Drafts or session restoration. Cancelling Save As retains the draft, caret, page tree and undo history supported by the engine. A model/account/provider outage cannot erase it. Offer explicit draft deletion through recoverable Trash semantics; do not treat closing a tab as permission to delete a draft.

### V2-B1406 · v2 lines 6581–6581

### UX27-BOARD-005 — Optional editable .9to1board files

### V2-B1407 · v2 lines 6583–6583

**O/B.** File → Save As and its shortcut open the normal permitted location chooser, prefill a reasonable filename, and save an editable `.9to1board` document to the location the user chooses. Desktop, Documents, a different folder, removable media, a mounted network location or an authorised cloud-provider location are all eligible according to actual platform access. The app must not rewrite the chosen destination into a Boards-specific folder.

### V2-B1408 · v2 lines 6585–6585

Save updates the selected file for a file-backed board; for a draft it may open Save As. Save As is not a PNG/PDF export, a pointer requiring an unconnected Files account, or a library-registration operation masquerading as a file. Files indexing, account sync and library organisation remain optional integrations, not prerequisites.

### V2-B1409 · v2 lines 6587–6587

### UX27-BOARD-006 — File contents and portability

### V2-B1410 · v2 lines 6589–6589

**B/D.** The versioned `.9to1board` representation must preserve BoardID, page/section hierarchy, stable PageIDs/BlockIDs, rich text and custom styles, flow/absolute layout, drawing layers and stroke IDs, graph definitions, table formatting, relevant view settings and the supported owned embedded artifacts/assets. Use the existing canonical serializer/container architecture where suitable; do not introduce a second incompatible Boards model just to implement the extension.

### V2-B1411 · v2 lines 6591–6591

BoardOwned content is included or packaged through a documented portable representation. Externally Referenced artifacts retain explicit references, owner identity and permission boundaries; disclose references that will not be available elsewhere. Provide a deliberate “include permitted copies” export when supported, never an unauthorised automatic copy. Credentials, bearer tokens, absolute-path-only assumptions and private account secrets must not be embedded in the document.

### V2-B1412 · v2 lines 6593–6593

### UX27-BOARD-007 — Identity versus file location

### V2-B1413 · v2 lines 6595–6595

**B/D.** Renaming or moving a `.9to1board` file must not generate new logical page/block identities. Save As changes the active storage destination while retaining the board’s logical identity unless the user explicitly requests Save a Copy/Duplicate. A deliberate independent copy receives a new BoardID and remapped owned identifiers according to the canonical duplication contract; external references are retained only as authorised references.

### V2-B1414 · v2 lines 6597–6597

Two separately modified copies with the same logical origin must not be merged merely because their names match. Detect duplicate/conflicting origins and offer an explicit open-as-copy or reconcile path. Opening a moved file repairs its Recent locator without duplicating every page. Never derive BoardID from a folder name or current sidebar position.

### V2-B1415 · v2 lines 6599–6599

### UX27-BOARD-008 — Safe saves, failure and concurrency

### V2-B1416 · v2 lines 6601–6601

**B/D.** Save through a crash-safe transaction/atomic replacement supported by the storage provider. Keep the previous valid file until the new revision is complete and validated. Do not mark Saved on merely starting a write. Disk-full, disconnected removable storage, read-only destinations, revoked access and remote revision conflicts preserve the in-memory edits and a recoverable local journal, expose the exact failed destination, and offer Retry/Save As where appropriate.

### V2-B1417 · v2 lines 6603–6603

An external file change is checked against the loaded revision before overwrite. Preserve both divergent revisions or invoke the domain conflict resolver; never choose by wall-clock timestamp alone. Test failure during serialization and during final replacement, and verify that reopening yields either the old complete revision or the new complete revision, not a partially assembled board.

### V2-B1418 · v2 lines 6605–6605

### UX27-BOARD-009 — Reopen without stale leases

### V2-B1419 · v2 lines 6607–6607

**O/B/D.** Open supports File → Open, a recent item and the platform’s `.9to1board` association. Resolve the durable document locator and current permissions afresh. A one-shot Files/Home acquisition token from the previous process is not the persistent identity of the document and must not be replayed as the only reopen mechanism. Reacquire the necessary narrow handle through the actual local/storage adapter.

### V2-B1420 · v2 lines 6609–6609

Opening a normal local file must not require connecting the Files app. If the OS genuinely requires renewed access, ask for that precise file/location access and preserve the selected board target. A missing file produces Locate/Remove from Recent choices, not a dead restored tab or an unrelated empty board. Restore the last page where still valid, otherwise choose a deterministic existing page and explain unavailable targets.

### V2-B1421 · v2 lines 6611–6611

### UX27-BOARD-010 — Landing page and recent items

### V2-B1422 · v2 lines 6613–6613

**B/D.** The landing page is a distinct board-library experience, not a heading stacked above a live editor. New Board and Open File are visible immediately. Recent cards use actual board names, icons, last-opened/modified state and revision-correct previews. No fake successful provider rows, seeded boards or duplicated Library headings. Searching filters real accessible records and exposes a useful no-results state without clearing the original collection.

### V2-B1423 · v2 lines 6615–6615

A click opens the chosen BoardID directly. The card overflow offers available Rename/Locate/Remove from Recent/Trash actions with distinct semantics. Removing a recent entry is not file deletion. Failed preview generation must not prevent opening a valid board. Long filenames and unavailable external references remain readable and diagnosable.

### V2-B1424 · v2 lines 6617–6617

### UX27-BOARD-011 — Board switcher

### V2-B1425 · v2 lines 6619–6619

**B/D.** The board icon/name control at the top of the sidebar opens an anchored searchable switcher. It shows the current selection, recent/pinned authorised boards, and distinct Create Board/Open File/Manage actions. Choosing a board atomically changes page tree, breadcrumb, title, editor, tool state and AI context to that board; no frame may pair one board’s title with another board’s content.

### V2-B1426 · v2 lines 6621–6621

Retain the former board’s local draft, selected page and scroll state. A pending save/failure is dealt with through the save contract rather than silently discarded. A failed target open leaves the current usable board intact. The switcher is not the bottom Add page/section popup and must not replace it.

### V2-B1427 · v2 lines 6623–6623

### UX27-BOARD-012 — Nested page and section tree

### V2-B1428 · v2 lines 6625–6625

**O/B/D.** Represent sections and pages with stable identities and ordered children. A section is an organisational container; a page remains a content entity and may contain subpages. Do not infer either type from indentation in a string. Rows contain disclosure where applicable, an actual page/section icon, a single label, selected state and context actions available on hover/focus. Full-row hit areas activate pages without requiring a tiny text click; disclosure clicks expand/collapse without opening another page.

### V2-B1429 · v2 lines 6627–6627

Changing a parent preserves the moved page’s descendants, IDs, references and content. Reject ancestry cycles. Collapsing an ancestor does not close or delete its active descendant. Reopening/revealing the active page expands the necessary ancestors. Deep nesting scrolls and remains accessible rather than truncating all labels to nothing.

### V2-B1430 · v2 lines 6629–6629

### UX27-BOARD-013 — Tree keyboard and rename behaviour

### V2-B1431 · v2 lines 6631–6631

**B/D/R.** Up/Down move tree focus among visible rows; Right expands a collapsed container or enters its first child; Left collapses or moves focus to the parent; Home/End reach the first/last visible row. Enter opens the focused page. Arrow focus does not automatically create documents or trigger repeated expensive opens. Type-ahead locates matching visible labels; search can locate unloaded descendants through the index.

### V2-B1432 · v2 lines 6633–6633

Rename is deliberate through F2/context action or the approved inline affordance. Enter commits a nonempty valid title, Escape restores the previous title, and validation keeps focus without changing the stable ID. Update the breadcrumb, tab title and references from the same committed state. Do not interpret a drag or double-click on a disclosure as a rename.

### V2-B1433 · v2 lines 6635–6635

### UX27-BOARD-014 — Fixed bottom Add page/section popup

### V2-B1434 · v2 lines 6637–6637

**O/B/D.** The bottom sidebar Add control replaces the reference’s Templates affordance with Add page/section. Its popup offers New page and New section, plus the canonical page types when New page is selected: Native, Write, Canvas, Chat and Scoped Chat, and other already specified supported types. It exposes the destination (current section/page or root) clearly rather than guessing from a stale selection. Keyboard activation and Escape/outside dismissal work.

### V2-B1435 · v2 lines 6639–6639

After creation, insert exactly one new entity at the chosen destination, reveal it in the tree and focus the appropriate title/editor. The footer remains visible while a long page tree scrolls. User/avatar controls remain in that bottom area as agreed; an avatar must not unexpectedly launch board creation. Template browsing, when supported, stays a separate optional action rather than being the only creation route.

### V2-B1436 · v2 lines 6641–6641

### UX27-BOARD-015 — Page-tree drag, move and Trash

### V2-B1437 · v2 lines 6643–6643

**B/D.** Dragging a page row shows a preview and a visible insertion/parent target. Above/below changes sibling order; an explicit centre/nesting target changes parent. Hover expansion and edge auto-scroll permit moving into a collapsed or off-screen destination. Moving a page into itself or a descendant is invalid and visibly rejected. Escape/outside cancellation leaves the hierarchy unchanged. One successful move is one undo transaction.

### V2-B1438 · v2 lines 6645–6645

The same operation is available through a Move To popup with destination search. Trash removes the selected entity from normal navigation but retains recoverable content and parent/order metadata. Deleting a container shows the impact on descendants. Restore returns to the original parent where possible or a clearly identified recovered location; it does not silently orphan subpages.

### V2-B1439 · v2 lines 6647–6647

### UX27-BOARD-016 — A block is a structured entity, not a tall text field

### V2-B1440 · v2 lines 6649–6649

**O/B.** A Native page stores an ordered, versioned BlockTree. Each paragraph, heading, list item and other structural block has its own stable BlockID and typed payload. Text wrapping, caret painting and empty-state messages are rendering, not extra entities. Do not implement an entire page as one expanding multiline input and decorate it with a six-dot icon. A newline operation must update the block model where specified below, not merely increase the height of that input.

### V2-B1441 · v2 lines 6651–6651

Block identity survives reorder, indentation, formatting and save/reopen. Deleting text does not delete unrelated child blocks or drawing objects. A visible block grip must address the block the pointer actually targets, not whichever block was last focused before scrolling.

### V2-B1442 · v2 lines 6653–6653

### UX27-BOARD-017 — Enter in an ordinary paragraph

### V2-B1443 · v2 lines 6655–6655

**O/B/D.** With a collapsed caret in a paragraph, Enter splits at the caret into two sibling blocks. The prefix stays in the original BlockID; the suffix moves to a newly allocated BlockID immediately after it. The caret enters the new block at offset zero. At the end, this creates an empty following paragraph; at the start, the original becomes empty and the original content moves to the new sibling. Valid inline marks on both parts are preserved. A cross-range selection is deleted through the normal range-edit transaction before the split.

### V2-B1444 · v2 lines 6657–6657

The split, selection transition and insertion form one undoable operation. Undo restores the original text, block tree and caret; Redo restores one new sibling, not another duplicate. The new block must have an independent grip and be reorderable immediately. No giant purple selection edge or fixed-height blank region is permitted as a substitute for block creation.

### V2-B1445 · v2 lines 6659–6659

### UX27-BOARD-018 — Shift+Enter, wrapping and IME

### V2-B1446 · v2 lines 6661–6661

**B/D/R.** Shift+Enter inserts a soft line break inside the current text-capable block without allocating a sibling BlockID. Automatic wrapping changes neither block count nor text content. An IME composition commit must be handled by the text-input/composition contract before structural Enter processing: committing a candidate must not also split a block. On-screen keyboards and accessibility input produce equivalent semantic operations rather than depending only on physical keycodes.

### V2-B1447 · v2 lines 6663–6663

Test English text, non-Latin composition, emoji/grapheme clusters and mixed formatting. Offsets must not split a surrogate pair or grapheme. A generic “newline” event from the framework is insufficient to decide block semantics without its input context.

### V2-B1448 · v2 lines 6665–6665

### UX27-BOARD-019 — Enter behaviour by content type

### V2-B1449 · v2 lines 6667–6667

**B/D.** Use the following explicit defaults unless a more specific canonical block contract applies:
