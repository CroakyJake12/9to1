# 9to1 V2 — Explicit interaction and product-behaviour expansion

**Edition:** 27 October 2026 · **Prepared for:** Jacob · **Status:** detailed specification revision; not an implementation or acceptance claim.

This edition expands the existing **9to1 Development Specification v2** rather than replacing its product scope. It incorporates the owner’s recent decisions, adds deterministic interaction rules where the source relied on broad product comparisons, and turns the reported regressions into explicit rejection cases. Existing APIs, security, donor-source obligations, platform requirements, interoperability, data ownership and all seven acceptance gates remain binding.

The connected Google Doc was read, not edited: this connection exposed document-read operations but no native document-write operation. The accompanying expanded master is an editable, source-preserving text edition. It contains the complete retrieved baseline text and the additions below. The original Google Doc remains the source for its existing embedded visual references and formatting; this text edition does not pretend to reproduce missing image assets.

## 0. Authority, provenance and how to use this edition

### UX27-AUTH-001 — Meaning of the labels

**O — Owner decision:** a direct requirement in the current conversation or a specifically retrieved recent owner instruction. **B — Baseline elaboration:** detailed behaviour necessary to fulfil an existing V2 requirement. **D — Drafted design default:** a new, explicit value or interaction choice introduced by this requested specification expansion; it is not a measured fact about Notion/AppFlowy and is not falsely attributed to an earlier owner approval. **R — Reference behaviour:** a narrowly documented behaviour in an official reference product or accessibility pattern. **E — Evidence:** a reported or observed defect, with the evidence type identified.

MUST means mandatory for this expanded edition. A D value supplies a concrete default where the original source is silent; a more specific, compatible owner-approved value takes precedence. Do not stop implementation for every ordinary D choice. Do not silently replace an explicit owner decision with a convenient default. When two explicit requirements genuinely conflict, preserve both in the decision record and resolve that specific conflict without discarding unrelated work.

### UX27-AUTH-002 — Scope preservation and priority

**O/B.** The current priority cohort is Haven/core foundations → Spaces/AI continuity → Automations and execution backends → Build/Dev → the full Write/Present/Boards/Canvas productivity suite. Bring forward Files/account/cloud services only where a real dependency requires them. This ordering includes both implementation and acceptance. Later products remain in V2; the amendment does not delete them or downgrade their baseline. Earlier historical deadlines retained in the baseline are historical, not current operating deadlines.

Build retains the specified Dev, Sites and Stacks capabilities. Assistants remains a Space rather than a replacement standalone app. Historical source identifiers such as Haven, CakeOS and Browse are not permission for destructive namespace or data-format renames. Main-convergence instructions in §17 concern development of 9to1 itself, not removal of the Build product’s version-control features.

### UX27-AUTH-003 — No implementation by analogy alone

**O/B.** “Like Notion”, “AppFlowy-style”, “mature word processor”, “normal browser” and similar phrases identify comparison products; they are not complete acceptance criteria. For each applicable donor capability, the implementation manifest MUST name its real entry point, interaction states, commands, persisted model, undo/recovery semantics, permission boundary, accessibility behaviour and executable acceptance scenario. The selected donor revision and any justified replacement remain traceable. This expansion supplies concrete contracts for the most failure-prone interactions; the donor-parity manifest still covers additional applicable capabilities rather than treating these examples as the entire donor.

### UX27-AUTH-004 — Baseline and recent amendments

**B/O.** Baseline source: Google Doc `17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY`, read on 27 October 2026; metadata last modified 20 October 2026. The UTF-8 export contains 194,630 whitespace-delimited words and 12,459 lines. Raw export SHA-256: `4d17ab8801ce12367f002df2f93a5d05dc5334ea45e33d421cbdcdcaa0fac277`. These counts describe the retrieved text, not test coverage.

Recent owner decisions incorporated: rounded filled colourful app icons with theme variants (21 October); selected-tab contrast and contextual composer shelf (22 October); Boards topology, nested pages, bottom Add/user area, branding and theme-independent layout (25 October); app launch, Chromium, Boards input/table/popup/editing/snap failures (25–26 October); main convergence and the current Enter/grip/pen/file-saving clarifications (27 October). The exact pixel geometry of the 25 October image set has not been re-measured here. Recovered explicit topology is authoritative; new dimensions below are labelled D rather than asserted as screenshot measurements.

### UX27-AUTH-005 — Explicit supersessions

**O.** Any implementation assumption that Boards requires a Files connection, a linked account, a cloud provider, or a designated “Boards” folder before creating/editing/saving a normal local board is rejected. A user can optionally save an editable `.9to1board` file anywhere the operating system permits. The shared platform’s internally packaged services and normal filesystem permissions remain valid; “no Files connection” does not mean bypass operating-system access controls or maintain an incompatible private document engine.

**O/B.** A Native Boards page is one seamless editing surface with flow blocks, explicit absolute-layout content and a page drawing layer. It is not a text area followed by an unexplained second “Freeform canvas” application. This does not remove the intentional Canvas PageType, legitimate embedded Canvas artifacts or free drawing in Locked layout.

### UX27-AUTH-006 — Source and review coverage

The entire baseline text was retrieved and structurally indexed, including app sections, requirement identifiers and reference-style statements. Detailed expansion concentrated on shared interactions, Boards, host/browser integration, Spaces, persistence, priority applications and their cross-app dependencies. Later-product contracts below add concrete interaction coverage while retaining their much larger baseline requirements. This is not a claim that every donor revision was run, every historic screenshot was recovered, every one of the baseline’s 194,630 words was independently rewritten, or any product passed acceptance.

The current recording is evidence of the installed UI, not the intended design. Sampled frames across its 240.4-second duration show a large blank text region, repeated “Freeform canvas” empty-state text and an embedded white canvas; pen and grip failures are also directly owner-reported. Input-event traces and source-level causes were not available from those frames alone. Never promote a screenshot-derived hypothesis to a reproduced code diagnosis.

## 1. Shared visual, input and state contracts

### UX27-SHARED-001 — Complete command contract

**B/D.** Every visible command MUST have a stable command identifier; a semantic label; a validated icon where useful; an accessible name; keyboard/touch activation; its target object IDs; enabled/disabled/busy rules; permission and revision checks; a specified result; and an error/undo policy. The toolbar, menu, shortcut, contextual popup, AI action and automation API must invoke the same domain action instead of implementing separate mutations. A disabled command must expose a concise reason without requiring it to run. A command is not implemented because a button, handler name or empty task exists.

Verification MUST activate representative commands from more than one surface, compare resulting canonical state, undo, save and reopen. Toolbar state must track external/API edits as well as local clicks.

### UX27-SHARED-002 — Control-state matrix

**B/D.** Implement distinct normal, hover, pressed, keyboard-focus, selected, checked, mixed, disabled, busy, error and unavailable states where meaningful. Hover must not masquerade as selection. Pointer-down shows pressed feedback but does not commit a consequential action; ordinary buttons commit once on valid release/activation. Moving out while pressed cancels activation unless the control’s gesture contract says otherwise. Space/Enter activation must not also cause a second pointer action. Focus remains visible independently of hover and selected fill.

Busy state preserves the control’s size and accessible label, prevents duplicate submission where necessary, and exposes cancellation only when actually supported. Never replace content with a spinner so early that the user loses the target or cannot determine what is pending.

### UX27-SHARED-003 — Geometry and scale defaults

**D.** Use logical display units through CUI’s registered layout system, not hardcoded physical-screen coordinates. Where no more specific approved design exists, use a 4-unit spacing scale; compact row heights of 32 units; ordinary toolbar hit areas of 32×32; 16–20-unit glyphs; and 44×44 effective hit targets in a touch/coarse-pointer layout. Allow density and accessibility settings to increase sizes. A glyph’s visual bounds and its hit target are separate.

Test at 100%, 125%, 150% and 200% display scaling, at representative narrow and wide windows, and when moved between monitors with different scale factors. Logical dimensions in this document must be mapped through the canonical CUI property system; they do not introduce a second public layout language.

### UX27-SHARED-004 — Icon identity and quality

**O/B/D.** Application icons use the owner’s rounded, filled, colourful, distinct design direction with light/dark variants. Boards uses the purple window-like identity; Data retains its teal identity; Spaces uses the agreed speech-bubble/Space-type composition when the actual approved asset is available. Do not substitute an unrelated donor logo, missing-glyph box, Unicode symbol or unreviewed generated path and call it the approved icon.

Command icons belong to one coherent family, with optical alignment and appropriate stroke/fill weight. Pair unfamiliar or destructive symbols with text in their menus. Test actual assets at 16, 20, 24 and 32 logical units, under light/dark, hover, selected and disabled states. Decorative icons are excluded from screen-reader naming; icon-only controls receive meaningful labels.

### UX27-SHARED-005 — Theme changes alter appearance, not topology

**O/B.** Professional/Glow and light/dark choices resolve through CUI tokens. Theme switching may change palette, typography tokens, elevation and permitted surface treatment; it MUST NOT relocate the Boards footer Add action to the header, flatten its page tree, replace page controls or change document layout semantics. Keep active page, selection, scroll position and open document identity stable. Theme application is one coordinated update, not a frame with old text colours on new backgrounds.

User-authored document colours/fonts and embedded artifact styling remain content, not app-theme values. Professional defaults follow the existing reference typography, including Montserrat where prescribed; code retains an appropriate monospace font. Do not manufacture one universal pill-shaped interface or overwrite explicitly different app layouts.

### UX27-SHARED-006 — Contrast and readability

**B/D/R.** Test real computed foreground/background pairs, not merely token names. Normal informative text targets at least 4.5:1 contrast; large text at least 3:1; essential control boundaries/focus indicators must remain distinguishable. A pale sidebar caption or dark title must not disappear after a theme switch. Disabled appearance must still be identifiable as a control, with an accessible reason; reduced opacity must not be applied to an entire container containing active children.

Selected tabs default to the owner’s light grey/white surface with black text unless an explicit accent override applies. The override must retain readable contrast and selection distinction. Dark themes are not permission for dark-on-dark labels. These are product requirements informed by W3C guidance, not a claim of complete WCAG certification.

### UX27-SHARED-007 — Motion token table

**D.** Define shared named motion tokens instead of ad hoc per-control animations: `Immediate=0 ms`, `Feedback=100 ms`, `Reveal=140 ms`, `Reflow=180 ms`, `Panel=220 ms`. These are authored 9to1 defaults, not measured Notion/AppFlowy timings. Use restrained ease-out for entering elements and ease-in for dismissal; interactive drag and ink track live input without waiting for an easing cycle. A new action can interrupt a transition without dropping input or replaying the old action.

Hover/focus colour transitions use Feedback; popovers use Reveal with at most a small opacity/4-unit translation; tree expansion and post-drop settling use Reflow; sidebar collapse uses Panel. Do not animate text caret positioning, text entry, file-save truth, permission grants or block identity. Motion must not create extra blank document space or move the hit target away from its rendered control.

### UX27-SHARED-008 — Reduced motion and truthful progress

**B/D/R.** Honour operating-system reduced-motion preference and a persistent app override. Remove nonessential translations, zooms, parallax and spring/bounce effects; retain immediate state changes and brief fades only where acceptable to the chosen setting. A loading animation denotes actual pending work, never fabricated progress. Determinate progress requires a real denominator; otherwise use an honest indeterminate state with the operation name and cancellation/retry where supported.

Background activity must not continuously animate every hidden tab. Pause unnecessary off-screen presentation work while preserving actual background tasks. Motion settings do not disable authored animation playback in a media/presentation preview; that preview has its own explicit controls.

### UX27-SHARED-009 — Popover lifetime and outside dismissal

**O/B/D.** A popup has an owner surface, anchor, target identity, focus-return target and lifetime. Open only one unrelated popup chain per window; submenus remain a chain. Outside pointer-down, Escape, activating a different tab/app, destroying the owner, or losing the target closes applicable transient popups. A profile menu must not remain over a different app after navigation. Updating theme or layout repositions the existing popup correctly or dismisses it; it must never become an orphaned overlay.

The outside-dismiss click must not accidentally activate a destructive background action. Escape closes the deepest submenu first, then its parent; it cancels a staged operation rather than silently committing. After dismissal return focus to the originating control or the nearest valid owner if that control no longer exists. Permission dialogs follow their explicit security contract and are not silently dismissed as ordinary menus.

### UX27-SHARED-010 — Popup placement, keyboard and scrolling

**B/D/R.** Anchor menus to the invoking control or pointer, with an 8-unit viewport safety margin by default. Flip above/below or left/right when necessary, then clamp without detaching the perceived anchor. Long menus have bounded internal scrolling; the document must not scroll underneath while the menu consumes wheel input. Arrow keys move between eligible items, Enter/Space activate, Escape dismisses, and type-ahead works where appropriate. A submenu opens on an intentional hover delay or keyboard action, not every incidental pointer crossing.

Checked and mixed states are conveyed visually and accessibly. Shortcut labels align in a separate column. Disabled items can expose reasons without executing. Use normal native accessibility semantics on native platforms and equivalent ARIA semantics on web, rather than styling arbitrary text to resemble a menu.

### UX27-SHARED-011 — Focus and selection are preserved through tools

**B/D.** Text selection belongs to the editor model, not merely a transient native control focus. Opening the formatting island, colour picker or link popup preserves the exact selected range and direction. Apply affects that range, not the whole page or a newly focused empty field. Closing the tool returns the caret/selection to the document and reveals it without scrolling to the page top. A selection changed by an external revision is revalidated before applying, with a clear conflict result if the target is stale.

Focus order follows visible hierarchy. Do not install global key handlers that consume ordinary text, IME composition or table navigation. Every popover, drawer and modal has an explicit focus policy, and no invisible control remains in the tab order after its surface closes.

### UX27-SHARED-012 — Scroll ownership and coordinate transforms

**O/B.** Wheel, touchpad, touch and scrollbar gestures target the hovered/active scroll container according to the platform convention. Apply OS natural-scroll direction once, never negate it again in an embedded engine. Preserve high-resolution fractional deltas rather than truncating small trackpad movement to zero. Boundary chaining is deliberate: a short menu scrolls itself; a page scrolls when the menu is closed; a canvas zoom gesture must not scroll the parent unexpectedly.

Persisted content uses domain coordinates. Screen/DPI/viewport/scroll/zoom transforms are composed once and inverted consistently for hit testing. Use the same transform for content, selection handles, drag previews and ink. Test zoom, monitor-scale changes and nested surfaces rather than adding unexplained offset constants.

### UX27-SHARED-013 — Undo transaction boundaries

**B/D.** One split, block move, page move, completed pen stroke, drag-resize, format action, insertion or accepted AI patch is one logical undoable transaction unless a richer existing domain contract specifies otherwise. A drag preview is not hundreds of separate undo entries. Text typing groups coherently, but crossing into a distinct action closes the group. Undo restores identities, relationships, text marks, content and selection as far as the domain permits; Redo reapplies the intended operation without duplicating a node.

External side effects such as sending mail, publishing a site or deleting a remote object are not automatically reversible document edits. Their UI must describe the actual reversal/cancellation boundary. Never display an Undo control that merely hides a toast while leaving an unintended external action unexplained.

### UX27-SHARED-014 — Error scope and recovery

**O/B.** An unavailable browser engine affects web tabs, not local Board creation. An unavailable model affects AI generation, not ordinary editing. An unavailable cloud account affects that account’s resources, not local files. Present errors at the narrowest owning surface with a named failed operation, preserved input, and genuine retry/recovery options. A permanent window-wide generic “action could not complete” banner is not adequate diagnosis for every later action.

Retry must reuse or reconcile the operation identity where duplicate effects are possible. Do not silently treat a timeout as failure to execute. Closing an error notification does not clear a real permission hold, erase failed-save state or falsely mark an artifact saved.

### UX27-SHARED-015 — Save-state vocabulary

**B/D.** Distinguish at least `New local draft`, `Changes pending`, `Saving locally`, `Saved locally`, `Sync pending`, `Synced`, `Save failed`, `Read-only` and `Conflict` where applicable. Display the destination and last durable revision in details without crowding the main workspace. A cloud upload pending after a successful local commit is not “not saved locally”; a failed local write is never “Saved”. No hardcoded “Saved · Just Now” label.

When closing a document with unsaved durable state, preserve a recovery draft and offer appropriate Retry/Save As/Discard/Cancel choices. Discard affects only the explicitly selected unsaved change set and must not delete unrelated saved files or owned embedded artifacts.

### UX27-SHARED-016 — Rendering must reflect actual route and build

**O/B.** UI changes count only when the route the user opens instantiates the changed surface in the identified installed build. Record application route, resolved registered surface, source commit, package/build identity and canonical document target. A standalone scene test or directly constructed page does not prove the host route activates it. Launch through the real user entry point and verify navigation, content, focus and persistence on that path.

Prevent multiple obsolete implementations from silently providing different behaviour under the same public app name. Shared services remain canonical; standalone and hosted entry points use the same domain model and supported surface implementation. Diagnostics may expose route/build identity without becoming ordinary document content.

### UX27-SHARED-017 — Loading, empty and offline states

**B/D.** An empty collection shows an explanation and an immediately usable create/open action, not a blank rectangle. Loading shows only the region awaiting data and retains safe navigation. Failure does not impersonate empty data. Offline/local-only state explains which optional services are unavailable without forcing an account wizard over a usable local editor. Restoration shows the actual target, including unavailable references, without fabricating placeholder documents as the recovered originals.

Skeletons reserve plausible dimensions only while data is genuinely pending, then release them. Do not leave a giant fixed-height placeholder inside an editable block. A loading message is not persisted as user content and must not duplicate on each render.

### UX27-SHARED-018 — Responsive layout and no control explosion

**B/D.** Each toolbar declares priority groups and an overflow policy. At reduced width, move lower-priority groups into a labelled overflow menu before clipping or adding uncontrolled rows. Keep essential create/save/mode controls reachable. Do not render every possible action for every cell/block simultaneously. Contextual tools replace the relevant group in the existing tool surface; they do not add a second wall of controls.

Remember user-resized panes within sensible bounds. Sidebars scroll independently of the document; fixed footers remain visible. An accessibility zoom setting may require a more spacious layout, but must not silently remove actions. Test narrow windows with long titles and translated labels.

### UX27-SHARED-019 — Accessibility and discoverability

**B/D/R.** Every pointer gesture has a practical keyboard/menu alternative. Trees expose hierarchy and expanded/selected state; sliders announce label/value/range; icon buttons have action names; canvas objects have meaningful semantic navigation where supported. Do not make hover the only way to discover an essential action on touch devices. Tooltips show a label, shortcut and useful disabled reason, with a default pointer delay of 400 ms; keyboard focus can expose the same information without a precision hover.

Tree focus and the page currently open are separate until activation; keyboard navigation must not trigger an expensive page load on every arrow press. Native accessibility adapters and web semantics require actual testing rather than accepting a label property in source as proof.

### UX27-SHARED-020 — Performance without content loss

**B.** Preserve the 20 October scalable-content annex: no arbitrary document/page/slide/block count cap used to hide an inefficient implementation. Prioritise input, selection, current-region edits and scrolling over distant rendering/indexing. Virtualise and hydrate by stable IDs, keep caches bounded, and use incremental persistence. Unrendered content remains real, searchable and exportable.

Measure input/frame/save/open behaviour on declared hardware and workloads. New motion timings above are presentation defaults, not performance guarantees. No pass is earned by silently dropping strokes, skipping blocks, truncating exports, disabling recovery or lowering canonical document fidelity.

## 2. Boards — exact shell, navigation and local-first file behaviour

### UX27-BOARD-001 — The intended composition

**O/B.** The Boards workspace comprises a compact application/board header, a left navigation sidebar, one primary seamless page/editor surface, the retained floating top editing toolbar and the shared bottom AI surface. Put the Boards identity at the top-left. Remove AppFlowy branding from the top-right; upstream attribution belongs in About/licence information, not a donor badge impersonating the product. Keep one clear editable page title and a breadcrumb expressing Board and Page identities without repeating the same title in multiple unrelated fields.

The sidebar has a board switcher/search area, the nested section/page tree, and a fixed bottom footer containing the Add page/section affordance and user/avatar controls. The 25 October instruction moves this Add control to the bottom; it does not remove the top editing toolbar or its separate Insert command. Do not confuse adding a page with inserting a block.

### UX27-BOARD-002 — Geometry defaults, not guessed screenshot measurements

**D.** Unless a recovered approved reference supplies more precise values, start with a 256-unit sidebar, user-resizable from 208 to 360; 32-unit tree rows; 16-unit indentation per depth; an approximately 48-unit fixed footer; and a 20-unit page-tree icon slot. Reserve the disclosure slot even for leaves so labels align. The content surface uses a readable 760-unit flow column by default, expandable through a real page-width preference; its background remains continuous rather than becoming a floating sheet/card.

Keep toolbar clearance above the first content block and AI-bar clearance below the last editable line. These clearances follow actual visible chrome bounds, not an arbitrary permanent blank block. On narrow windows, collapse navigation deliberately and keep an explicit reopen control; do not push the editor off screen.

### UX27-BOARD-003 — New Board without setup prerequisites

**O/B/D.** From the Boards landing page, New Board creates a durable local draft with a stable BoardID and an initial editable Native page, then focuses its title or first content block. A name is editable inline; a file path, Files connection, login, cloud subscription, AI model or browser renderer is not required. Do not show a mandatory “connect Files” wizard, hide Create, or demand a predefined Boards folder.

A simple creation popup may offer optional name/template details, but the default empty-board route must remain immediate and usable. Template content is explicitly chosen, not injected as fake recent work. A shared-core installation/repair requirement is handled by the packaged platform contract; ordinary users must not manually wire internal storage services to create a local board.

### UX27-BOARD-004 — Drafts before Save As

**O/B/D.** An unnamed board is a recoverable local draft, not disposable memory. Allocate canonical identity immediately and persist edits through the normal local owning-storage adapter. Show `Saved locally — draft` only after a durable commit. Keep recoverable drafts in the application’s normal private data area without making that implementation path a user-facing mandatory destination.

Closing and reopening the application restores the draft through Recent/Drafts or session restoration. Cancelling Save As retains the draft, caret, page tree and undo history supported by the engine. A model/account/provider outage cannot erase it. Offer explicit draft deletion through recoverable Trash semantics; do not treat closing a tab as permission to delete a draft.

### UX27-BOARD-005 — Optional editable .9to1board files

**O/B.** File → Save As and its shortcut open the normal permitted location chooser, prefill a reasonable filename, and save an editable `.9to1board` document to the location the user chooses. Desktop, Documents, a different folder, removable media, a mounted network location or an authorised cloud-provider location are all eligible according to actual platform access. The app must not rewrite the chosen destination into a Boards-specific folder.

Save updates the selected file for a file-backed board; for a draft it may open Save As. Save As is not a PNG/PDF export, a pointer requiring an unconnected Files account, or a library-registration operation masquerading as a file. Files indexing, account sync and library organisation remain optional integrations, not prerequisites.

### UX27-BOARD-006 — File contents and portability

**B/D.** The versioned `.9to1board` representation must preserve BoardID, page/section hierarchy, stable PageIDs/BlockIDs, rich text and custom styles, flow/absolute layout, drawing layers and stroke IDs, graph definitions, table formatting, relevant view settings and the supported owned embedded artifacts/assets. Use the existing canonical serializer/container architecture where suitable; do not introduce a second incompatible Boards model just to implement the extension.

BoardOwned content is included or packaged through a documented portable representation. Externally Referenced artifacts retain explicit references, owner identity and permission boundaries; disclose references that will not be available elsewhere. Provide a deliberate “include permitted copies” export when supported, never an unauthorised automatic copy. Credentials, bearer tokens, absolute-path-only assumptions and private account secrets must not be embedded in the document.

### UX27-BOARD-007 — Identity versus file location

**B/D.** Renaming or moving a `.9to1board` file must not generate new logical page/block identities. Save As changes the active storage destination while retaining the board’s logical identity unless the user explicitly requests Save a Copy/Duplicate. A deliberate independent copy receives a new BoardID and remapped owned identifiers according to the canonical duplication contract; external references are retained only as authorised references.

Two separately modified copies with the same logical origin must not be merged merely because their names match. Detect duplicate/conflicting origins and offer an explicit open-as-copy or reconcile path. Opening a moved file repairs its Recent locator without duplicating every page. Never derive BoardID from a folder name or current sidebar position.

### UX27-BOARD-008 — Safe saves, failure and concurrency

**B/D.** Save through a crash-safe transaction/atomic replacement supported by the storage provider. Keep the previous valid file until the new revision is complete and validated. Do not mark Saved on merely starting a write. Disk-full, disconnected removable storage, read-only destinations, revoked access and remote revision conflicts preserve the in-memory edits and a recoverable local journal, expose the exact failed destination, and offer Retry/Save As where appropriate.

An external file change is checked against the loaded revision before overwrite. Preserve both divergent revisions or invoke the domain conflict resolver; never choose by wall-clock timestamp alone. Test failure during serialization and during final replacement, and verify that reopening yields either the old complete revision or the new complete revision, not a partially assembled board.

### UX27-BOARD-009 — Reopen without stale leases

**O/B/D.** Open supports File → Open, a recent item and the platform’s `.9to1board` association. Resolve the durable document locator and current permissions afresh. A one-shot Files/Home acquisition token from the previous process is not the persistent identity of the document and must not be replayed as the only reopen mechanism. Reacquire the necessary narrow handle through the actual local/storage adapter.

Opening a normal local file must not require connecting the Files app. If the OS genuinely requires renewed access, ask for that precise file/location access and preserve the selected board target. A missing file produces Locate/Remove from Recent choices, not a dead restored tab or an unrelated empty board. Restore the last page where still valid, otherwise choose a deterministic existing page and explain unavailable targets.

### UX27-BOARD-010 — Landing page and recent items

**B/D.** The landing page is a distinct board-library experience, not a heading stacked above a live editor. New Board and Open File are visible immediately. Recent cards use actual board names, icons, last-opened/modified state and revision-correct previews. No fake successful provider rows, seeded boards or duplicated Library headings. Searching filters real accessible records and exposes a useful no-results state without clearing the original collection.

A click opens the chosen BoardID directly. The card overflow offers available Rename/Locate/Remove from Recent/Trash actions with distinct semantics. Removing a recent entry is not file deletion. Failed preview generation must not prevent opening a valid board. Long filenames and unavailable external references remain readable and diagnosable.

### UX27-BOARD-011 — Board switcher

**B/D.** The board icon/name control at the top of the sidebar opens an anchored searchable switcher. It shows the current selection, recent/pinned authorised boards, and distinct Create Board/Open File/Manage actions. Choosing a board atomically changes page tree, breadcrumb, title, editor, tool state and AI context to that board; no frame may pair one board’s title with another board’s content.

Retain the former board’s local draft, selected page and scroll state. A pending save/failure is dealt with through the save contract rather than silently discarded. A failed target open leaves the current usable board intact. The switcher is not the bottom Add page/section popup and must not replace it.

### UX27-BOARD-012 — Nested page and section tree

**O/B/D.** Represent sections and pages with stable identities and ordered children. A section is an organisational container; a page remains a content entity and may contain subpages. Do not infer either type from indentation in a string. Rows contain disclosure where applicable, an actual page/section icon, a single label, selected state and context actions available on hover/focus. Full-row hit areas activate pages without requiring a tiny text click; disclosure clicks expand/collapse without opening another page.

Changing a parent preserves the moved page’s descendants, IDs, references and content. Reject ancestry cycles. Collapsing an ancestor does not close or delete its active descendant. Reopening/revealing the active page expands the necessary ancestors. Deep nesting scrolls and remains accessible rather than truncating all labels to nothing.

### UX27-BOARD-013 — Tree keyboard and rename behaviour

**B/D/R.** Up/Down move tree focus among visible rows; Right expands a collapsed container or enters its first child; Left collapses or moves focus to the parent; Home/End reach the first/last visible row. Enter opens the focused page. Arrow focus does not automatically create documents or trigger repeated expensive opens. Type-ahead locates matching visible labels; search can locate unloaded descendants through the index.

Rename is deliberate through F2/context action or the approved inline affordance. Enter commits a nonempty valid title, Escape restores the previous title, and validation keeps focus without changing the stable ID. Update the breadcrumb, tab title and references from the same committed state. Do not interpret a drag or double-click on a disclosure as a rename.

### UX27-BOARD-014 — Fixed bottom Add page/section popup

**O/B/D.** The bottom sidebar Add control replaces the reference’s Templates affordance with Add page/section. Its popup offers New page and New section, plus the canonical page types when New page is selected: Native, Write, Canvas, Chat and Scoped Chat, and other already specified supported types. It exposes the destination (current section/page or root) clearly rather than guessing from a stale selection. Keyboard activation and Escape/outside dismissal work.

After creation, insert exactly one new entity at the chosen destination, reveal it in the tree and focus the appropriate title/editor. The footer remains visible while a long page tree scrolls. User/avatar controls remain in that bottom area as agreed; an avatar must not unexpectedly launch board creation. Template browsing, when supported, stays a separate optional action rather than being the only creation route.

### UX27-BOARD-015 — Page-tree drag, move and Trash

**B/D.** Dragging a page row shows a preview and a visible insertion/parent target. Above/below changes sibling order; an explicit centre/nesting target changes parent. Hover expansion and edge auto-scroll permit moving into a collapsed or off-screen destination. Moving a page into itself or a descendant is invalid and visibly rejected. Escape/outside cancellation leaves the hierarchy unchanged. One successful move is one undo transaction.

The same operation is available through a Move To popup with destination search. Trash removes the selected entity from normal navigation but retains recoverable content and parent/order metadata. Deleting a container shows the impact on descendants. Restore returns to the original parent where possible or a clearly identified recovered location; it does not silently orphan subpages.

### UX27-BOARD-016 — A block is a structured entity, not a tall text field

**O/B.** A Native page stores an ordered, versioned BlockTree. Each paragraph, heading, list item and other structural block has its own stable BlockID and typed payload. Text wrapping, caret painting and empty-state messages are rendering, not extra entities. Do not implement an entire page as one expanding multiline input and decorate it with a six-dot icon. A newline operation must update the block model where specified below, not merely increase the height of that input.

Block identity survives reorder, indentation, formatting and save/reopen. Deleting text does not delete unrelated child blocks or drawing objects. A visible block grip must address the block the pointer actually targets, not whichever block was last focused before scrolling.

### UX27-BOARD-017 — Enter in an ordinary paragraph

**O/B/D.** With a collapsed caret in a paragraph, Enter splits at the caret into two sibling blocks. The prefix stays in the original BlockID; the suffix moves to a newly allocated BlockID immediately after it. The caret enters the new block at offset zero. At the end, this creates an empty following paragraph; at the start, the original becomes empty and the original content moves to the new sibling. Valid inline marks on both parts are preserved. A cross-range selection is deleted through the normal range-edit transaction before the split.

The split, selection transition and insertion form one undoable operation. Undo restores the original text, block tree and caret; Redo restores one new sibling, not another duplicate. The new block must have an independent grip and be reorderable immediately. No giant purple selection edge or fixed-height blank region is permitted as a substitute for block creation.

### UX27-BOARD-018 — Shift+Enter, wrapping and IME

**B/D/R.** Shift+Enter inserts a soft line break inside the current text-capable block without allocating a sibling BlockID. Automatic wrapping changes neither block count nor text content. An IME composition commit must be handled by the text-input/composition contract before structural Enter processing: committing a candidate must not also split a block. On-screen keyboards and accessibility input produce equivalent semantic operations rather than depending only on physical keycodes.

Test English text, non-Latin composition, emoji/grapheme clusters and mixed formatting. Offsets must not split a surrogate pair or grapheme. A generic “newline” event from the framework is insufficient to decide block semantics without its input context.

### UX27-BOARD-019 — Enter behaviour by content type

**B/D.** Use the following explicit defaults unless a more specific canonical block contract applies:

| Context | Enter | Shift+Enter | Empty-block continuation |
|---|---|---|---|
| Paragraph | Split into following paragraph | Soft break in same block | Creates the next editable paragraph |
| Heading H1–H6/custom heading | Split; following block is a paragraph | Soft break in same heading | Leaves an empty paragraph ready for body text |
| Bullet/numbered list | New sibling item at the same depth; numbering recomputes | Soft break in current item | At root, exit to paragraph; nested empty item outdents one level |
| Checklist item | New unchecked sibling; do not inherit checked state | Soft break in current item | Same exit/outdent rule as lists |
| Quote/callout text | Continue that container’s text/block contract | Soft break | Explicit exit command or second empty boundary action leaves the container without losing its children |
| Code block | Insert a code newline with applicable indent | Code newline, unless an explicit editor binding differs | Never silently create a page block for a normal code newline |
| Simple-table cell | Edit the cell’s text/paragraph according to the shared table editor | Soft break in cell | Never insert an outer page block merely by pressing Enter in a cell |
| Page title/rename field | Commit title and focus the intended page editor | No hidden page-tree mutation | Validate an empty title through the title contract |
| Search/slash/menu query | Activate highlighted result when valid | Does not mutate document unexpectedly | No result means no hidden insertion |

Expose an explicit keyboard command to exit code/table/embedded editors to a following page block. Do not globally bind Enter above the active editor and thereby break these exceptions.

### UX27-BOARD-020 — Backspace/Delete at boundaries

**B/D.** Backspace inside text deletes the preceding grapheme/range. At the start of a compatible paragraph, it merges into the preceding text block and places the caret at the join. At the start of a formatted list item, first perform the specified outdent/type transition rather than deleting a parent subtree. Forward Delete at the end uses the corresponding safe merge rule.

Non-text blocks, locked embedded artifacts and incompatible block types must not be silently destroyed by a boundary merge. Select the adjacent non-text block or expose a clear deletion action according to its contract. Preserve nested children, inline marks, references and undo state. A broad key handler that deletes the whole block whenever the caret is at zero fails this requirement.

### UX27-BOARD-021 — Lists, toggles and nesting

**B/D.** Bullets, ordered lists and checklists retain explicit item type, depth, parent and order. Tab/Shift+Tab indent/outdent where a valid parent exists; the command must not insert literal tab characters into ordinary list text. Empty nested-list Enter outdents one level, not all the way to root. Numbering changes after reorder without rewriting item IDs. Completing a checklist toggles its checked state and does not move focus to a different item.

Toggle disclosure expands/collapses its child content without changing that content’s stored existence. Child selection and focus must remain coherent when a parent collapses. Copying/moving a selected parent carries its child subtree; converting a container to a type unable to host children must preserve them through an explicit, reversible restructuring rather than drop them.

### UX27-BOARD-022 — Slash insertion is a real contextual menu

**B/D/R.** In an eligible text block, typing `/` at the defined insertion point opens a searchable block/action catalogue anchored near the caret. Show category headings, recognisable icons, names, short descriptions where useful and keyboard focus. Typing narrows results without losing the text range used for the command. Escape dismisses and leaves ordinary typed content intact unless the user explicitly cancels the command token.

Selecting a block type replaces only the slash-command token/current eligible empty block or inserts at the specified boundary; it does not clear unrelated paragraph content. URLs and code containing slashes must not accidentally open the page insertion menu. Inline + and toolbar Insert use the same registered catalogue and target semantics. Unsupported types are truthfully unavailable, not decorative examples.

### UX27-BOARD-023 — Six-dot grip: click versus drag

**O/B/D/R.** Every eligible block exposes its six-dot grip on hover and keyboard focus, with an accessible “Block actions / move block” affordance. Clicking without a drag opens the block menu. Pointer-down followed by movement beyond the platform drag threshold starts a drag; where no platform threshold is supplied, use a 4-unit logical default. Starting a drag suppresses the click-menu action on release. Text-body drag selects text unless an explicit spatial-move tool is active.

Use a stable hit area of at least 28×28 logical units in compact pointer mode, larger in touch mode. Pointer capture continues the gesture when leaving the glyph’s tiny visual bounds. Show a grab/grabbing cursor and a meaningful preview. An icon with no drag behaviour is a failed feature, not a finished grip.

### UX27-BOARD-024 — Flow-block reordering and drop feedback

**O/B/D.** While dragging a flow block, remove no canonical data until a valid drop commits. Show a translucent preview of the actual selected block/subtree and an insertion line between candidate siblings. Before/after targets are distinct from child/column targets; do not convert to absolute positioning merely because the pointer moved sideways. The source placeholder preserves reasonable layout without expanding to the full window height.

On a valid drop, move the existing IDs and their children once, update ordering, focus/reveal the moved block and save the operation. Invalid targets show unavailable feedback. Escape, pointer cancellation or dropping outside valid targets restores the original order. A drag cancelled after scrolling must not leave duplicated blocks or a stuck capture layer.

### UX27-BOARD-025 — Nesting, columns and drag auto-scroll

**B/D/R.** Child nesting is offered only on types that accept children and must show the intended parent/indentation before drop. Column creation uses a distinct lateral drop zone and preview, rather than guessing from any horizontal pointer motion. The default lateral target band is 24 logical units where space allows; narrow layouts use an explicit Move/Columns menu instead of unreachable zones. Moving content out of a column removes an empty column container only through a reversible structural operation.

Within 32 logical units of a scrollable edge, drag auto-scroll accelerates gradually up to a default 720 logical units/second; it stops when leaving the edge or cancelling. Hovering an eligible collapsed tree/container target for 600 ms may expand it without committing a move. These timings are D defaults. Retain correct target IDs under virtualisation and do not select a recycled row with stale identity.

### UX27-BOARD-026 — Keyboard block movement and multi-selection

**B/D.** The block menu offers Move up, Move down, Move to and permitted indent/outdent operations. Registered keyboard equivalents must be discoverable and must not conflict with active text/IME commands. Escape from ordinary text editing can select the current block; further navigation follows the defined selection model rather than stealing the first Escape from an open popup.

Multi-block selection preserves contiguous or explicit selected BlockIDs. Dragging the selected group maintains its internal order and selected subtrees; do not move a child twice because both it and its ancestor appear in selection. Cut/paste uses the canonical object bundle, preserving supported types and remapping IDs only when copying. Clipboard failure leaves the source intact.

### UX27-BOARD-027 — Block context menu

**B/D/R.** Clicking the grip opens actions for that exact block: supported type conversion, Move To, duplicate, copy link/reference, copy/cut, delete/recover, colour/style and contextual object actions. Show only valid operations or an explicit disabled reason. A page link, image, code block and table must not all expose the same generic text menu.

Duplicate creates new owned identities with equivalent content and legitimate external references; it must not alias a supposedly independent copy onto the original mutable object. Copy link targets the stable block, surviving reorder/rename. Delete is undoable and follows the owning artifact’s deletion policy. A menu opened before a remote revision is revalidated before a consequential mutation.

### UX27-BOARD-028 — Text selection and floating formatting island

**O/B.** Keep the top floating editing toolbar. For text it exposes current Paragraph/H1–H6/custom style; B/I/S/U; font/size where supported; colour/highlight; alignment; lists/checklist; link; Undo/Redo; and Insert through coherent groups. The active style and mixed-format state follow the real caret/selection, not a cached label from a previous block. The toolbar does not become a second document row or scroll offscreen accidentally.

Applying Bold to two selected words changes those words, not the entire block, and retains selection. Converting paragraph to heading preserves text/valid marks and stable identity. A font/colour popup previews only according to the shared transaction policy and Escape restores a staged preview. Routine formatting must not require opening a distant Properties panel.

### UX27-BOARD-029 — Native page, explicit Canvas page and drawing layer

**O/B.** A Native page is the combined flow/spatial page described by V2. Its DrawingLayer is integrated into the same document-space viewport. There must not be an automatically appended “Freeform canvas” section, duplicate empty-state text, independent white scrollbox or “+ Box” diagnostic region below every paragraph. Empty Native pages show an unobtrusive editing hint once, not a second editor application.

A user-created Canvas PageType or deliberately inserted Canvas artifact remains valid and uses its canonical engine. Such content has explicit identity, purpose, bounds and open/edit controls. Removing the unintended panel must not remove the page’s ink layer, legitimate absolute-position blocks or intentional embedded artifacts.

### UX27-BOARD-030 — Edit/View, layout locking and snap are separate

**O/B/D.** Keep three independently understandable concepts: Edit/View controls authoring permission at the surface; Locked/Unlocked controls casual spatial block movement; Snap to grid controls optional spatial snapping. A page can be Edit + Locked with working text editing, flow reorder and pen drawing. View prevents content/ink mutation but may retain explicitly non-persistent graph interaction. Unlocked does not grant permission to edit a read-only board.

Display the persistent Edit/View control in the approved top-right page chrome and keep layout/snap controls discoverable in the editing controls. Do not silently remove them when a different object is selected. Disabled states explain why. Persist layout preference appropriately without confusing it with a server-side permission grant.

### UX27-BOARD-031 — Absolute placement and snap semantics

**B/D.** Unlocked mode permits an explicit Move freely/Place freely gesture to convert a flow block to an Absolute block with document-space X/Y/Width/Height/ZOrder. A normal six-dot flow reorder remains a flow reorder. Converting back to flow is a separate undoable action with a visible insertion location. Relocking preserves absolute coordinates exactly and only disables casual spatial handles.

Snap affects active spatial moves/resizes, not typing, handwriting points or ordinary flow ordering. Use the page’s documented grid spacing and show alignment guides while relevant. Toggling snap off must permit continuous placement; toggling it on must not immediately move every existing object. Store precise coordinates, not rounded screen pixels. Grid visibility and snap enablement are separate settings.

### UX27-BOARD-032 — Selecting the pen must actually enable drawing

**O/B.** Pen selection changes the active authoring tool and its cursor/toolbar state. In Edit mode, pointer-down inside the page begins a real stroke, pointer movement renders it continuously, and pointer-up commits one stroke with a stable ID. Drawing works in both Locked and Unlocked layout modes. A selected pen icon that produces no ink is an acceptance failure, even when unit tests or a separate Rnote demonstration pass.

The pen must operate on the actual routed Boards page using the integrated shared Rnote-derived implementation. Verify with mouse-drawn strokes as a baseline and with an actual supported stylus where available. A missing native library, wrong architecture, unregistered renderer or input handler attached to an unused scene must be diagnosed as an implementation/package failure, not hidden behind successful toolbar selection.

### UX27-BOARD-033 — Stroke coordinates, samples and capture

**B/D.** Convert pointer samples through the current screen-to-page transform at the time of the event, including DPI, viewport origin, scroll, zoom and any relevant page transform. The rendered preview and stored stroke use the same document coordinates. Pointer capture follows the active pointer until release/cancel; crossing a toolbar does not begin drawing on the toolbar or create a long line to the next unrelated pointer event.

Use supported coalesced samples and pressure/tilt metadata without inventing values on unsupported devices. A pointer cancel/lost capture completes or cancels the current stroke under the engine’s documented policy, clears all transient state and never joins the next stroke to the previous one. Pen and mouse IDs are not interchangeable. Test two separated strokes, window exit/re-entry and zoomed drawing for spurious connectors and offsets.

### UX27-BOARD-034 — Pressure, palm rejection and non-stylus fallback

**B/D.** When pressure exists, map it smoothly through the selected brush’s pressure curve within configured width bounds. Where unavailable, use the chosen fixed width; do not make a mouse stroke invisible because its pressure field is zero/missing. Supported tilt and eraser-tip input are capability-negotiated. Surface honest unavailable controls rather than pretending every device supports every pen feature.

An active pen can reject touch contacts identified as palms while preserving intended touch navigation under the selected input mode. Do not discard all touch interaction unconditionally. A “pen-only ink” preference distinguishes drawing from touch pan. Stylus-specific acceptance remains NOT RUN when only synthetic/mouse input was available.

### UX27-BOARD-035 — Pen controls, eraser and undo

**O/B/D.** Drawing context exposes pen/highlighter, eraser, selection/lasso, stroke width and colour through a compact contextual group. Show the active tool and chosen width/colour; changing a control affects subsequent strokes, not every old stroke unless those strokes are explicitly selected. Eraser mode clearly distinguishes whole-stroke and segment erasure if both are supported.

A completed stroke is one undo transaction. Erasing, transforming or recolouring a selected stroke is independently undoable. Clear page/drawing requires an explicit scoped action; it must not be the accidental result of pressing Escape or selecting another tool. Undo/Redo, save/reopen, theme changes and page switches preserve actual editable ink rather than rasterising it into a background image.

### UX27-BOARD-036 — Ink versus text interaction

**B/D.** In text/select mode, ordinary pointer gestures select text or blocks and do not create ink. In Pen mode, pointer input on the page draws rather than unexpectedly selecting a text range; keyboard navigation can still reach tool controls. Switching tools preserves the prior caret/selection as appropriate without committing a phantom stroke. View mode refuses new strokes with clear disabled feedback.

Page-level ink remains anchored in page coordinates as flow content reflows. Ink deliberately attached to an object uses an explicit attachment relationship and follows that object; the engine must not infer attachment from proximity. Content expansion must not stretch old ink or turn a selection border into a persistent vertical stroke.

### UX27-BOARD-037 — Compact table interaction, not fifty buttons

**O/B.** Selecting a table shows one compact relevant toolbar/context group and ordinary cell/row/column affordances. Do not display every command separately for every row or cell. Row and column actions appear from the corresponding header/grip menu; border/fill/alignment are grouped; rare actions are in overflow. The page remains the main surface rather than a grid of command buttons.

Distinguish simple rich tables from Data-backed database views. Simple tables use the shared table model; database behaviours use canonical Data entities. Both must preserve editability and ownership when embedded in Boards. Selecting a cell must not replace the whole page with a private database editor or leave an unrelated text toolbar pretending to operate on the cell.

### UX27-BOARD-038 — Table cell editing and navigation

**B/D.** A single click selects/focuses the intended cell; typing or the documented activation enters editing. Tab advances to the next cell, Shift+Tab to the previous; at the final editable cell an explicit supported append-row behaviour creates one row, not repeated rows from a held focus event. Arrow keys navigate within text while editing and between cells in cell-selection mode. Escape exits cell editing without losing a committed value or applying a hidden page command.

Range selection uses rectangular cell coordinates with visible bounds. Multi-cell paste previews or validates shape/type effects and preserves incompatible content through a truthful error path. Enter inside a cell follows the table editor’s contract, never the outer Boards paragraph-splitting handler. Leaving the table returns focus to a clear following block location.

### UX27-BOARD-039 — Table structure and formatting

**B/D.** Add/remove/reorder rows and columns by stable identities; resizing provides live feedback and commits once. Merged cells require a rectangular compatible selection. If multiple cells contain content, present a deterministic content-preserving merge policy or a clear choice; never silently erase non-anchor contents merely to imitate a reference product. Split/unmerge restores structure according to the saved operation and remains undoable.

Header row/column styling, borders, fill, alignment and text marks remain distinct from cell values. Table width can fit the flow column, be a chosen width, or scroll horizontally when necessary without forcing the whole page into horizontal scrolling. Reopen must preserve structure, dimensions, styles and values.

### UX27-BOARD-040 — Images and embedded media

**B/D.** Insert from clipboard, permitted file chooser, drag/drop or an authorised reference. Local insertion must not require a Files account. Show actual loading/progress and preserve the source asset. Selection exposes resize/crop/rotate/fit/caption/alt-text/link controls; corner resize preserves aspect ratio by default, with an explicit modifier/control for other behaviour. Cropping is a non-destructive transform until an explicit export/rasterise operation.

Broken/missing referenced media retains its identity and explanatory placeholder, with Locate/Retry where supported, not a fake thumbnail. A media click in Edit selects it; playback/open controls are deliberate and cannot unexpectedly consume a drag intended to move the block.

### UX27-BOARD-041 — Graph expressions and view-safe sliders

**B/D.** A Graph block contains editable ordered expressions/series, axes, range/viewport, styles and variable definitions in the shared graph model. Adding a second expression adds a second plot rather than replacing the first. Syntax/domain errors appear beside the relevant expression while valid plots continue. Slider bounds, steps and defaults are real values with validation; the graph and controls share the same state.

In View, slider exploration is session-local unless an authorised explicit save changes defaults. Reset returns to saved defaults. Editing, undo, copy between supported apps, save/reopen and export retain expressions and variables; a screenshot of a graph is not an editable graph. The observed ability to change a plotted line does not prove all these contracts passed.

### UX27-BOARD-042 — Write and Canvas PageTypes

**B.** Creating a Write/Canvas page creates or attaches the actual canonical owning-app artifact and hosts its registered editing surface inside Boards. Board navigation remains available, but do not add two competing AI bars, duplicate save indicators or external windows solely to obtain normal editing. Open in Write/Canvas addresses the same artifact/revision, not a copied file.

The page type’s source identity and ownership remain inspectable. Deleting the board page must distinguish a BoardOwned artifact from a Referenced artifact and describe cascade impact. A failed engine load preserves the reference and surrounding board, with a scoped recovery state rather than collapsing all of Boards.

### UX27-BOARD-043 — Chat and Scoped Chat pages

**B/D.** A Chat page retains its conversation/Agent identities across rename and tree moves. Its context shows which Board/pages/sources are available and respects current permissions. Scoped Chat is closed-corpus: the containing authorised board plus explicit attached sources is the permitted information set. Missing evidence produces an explicit insufficient-source response, not silent web retrieval or unlabelled general-knowledge substitution.

Source subpages show ingestion Pending/Ready/Failed/Removed states, with retry and permission-aware removal. A Ready badge is set only after the usable indexed revision exists. Source removal prevents later reads without falsifying historic provenance. Text-only workers/models must not claim to have seen source images or screen recordings they cannot process.

### UX27-BOARD-044 — Search, links and navigation restoration

**B/D.** Board search indexes page titles and authorised content incrementally, including unloaded pages. Results show the containing board/page and a relevant snippet; selecting a result opens the correct stable target and reveals the match. Renames and moves preserve links. A copied block link resolves after reorder and can explain a trashed/restricted target without exposing its content.

Back/forward navigates real page selections and restores scroll/caret/view state where safe. Switching between boards or returning from an embedded artifact must not create duplicate untitled pages. Page-tree filtering must not mutate the hierarchy or delete hidden nodes.

### UX27-BOARD-045 — No accidental mutation on open or render

**B/D.** Opening, switching theme, resizing, changing zoom, moving focus, rendering a preview or reading a board must not create new blocks, duplicate onboarding text, change revision unnecessarily or mark content dirty. Local view preferences may persist separately from document changes. Toolbar construction and placeholder rendering are never domain insert operations.

Acceptance must compare canonical state before/after repeated opens and theme switches. A “Freeform canvas” empty-state appearing twice or content added merely because a component mounts is a failed lifecycle contract even when the view remains technically usable.

### UX27-BOARD-046 — Collaboration and external changes

**B.** Independent block/ink/page operations merge through the canonical revision/operation model, retaining deterministic order and stable IDs. Same-object conflicts remain explicit. The UI preserves the user’s active selection and draft while reconciling incoming changes; do not silently replace an editor’s text with a remote snapshot during composition. A permission revocation stops new mutations and preserves recoverable unsaved work according to policy.

Presence/avatar controls represent real participants/status, not decorative fake collaborators. Offline local editing does not require invented cloud participants. Reconnection reconciles operations and exposes failures without replaying already committed actions.

### UX27-BOARD-047 — Boards rejection checklist

**O/B.** Reject the candidate when any of these is observed: Enter only grows a single text field; the six-dot grip cannot move a block; Pen is selected but draws no real stroke; Locked disables all ink; ordinary pages contain an unexplained second canvas; a table exposes a wall of repetitive buttons; Edit/Layout/Snap controls vanish without a legitimate mode reason; popup owners survive unrelated navigation; local creation demands a Files connection; Save As forces a Boards folder; `.9to1board` is not independently reopenable/editable; saved boards fail because a prior process’s lease expired; title/text contrast breaks on theme switch; or block/ink/page state is lost on reopen.

These are mandatory regressions to test through the actual application entry point. A passing unit suite, menu inventory, new source files or a screenshot of the intended layout does not override a failing real journey.

## 3. Shared persistence, interoperability and local service boundaries

### UX27-DATAFLOW-001 — Shared core is not compulsory cloud setup

**O/B.** Home Core and the Shared Productivity Engine may be packaged dependencies as V2 requires; their existence does not require the user to open a visible Home window, connect a cloud account, select an AI model or configure the Files app before ordinary local document work. Required local services start through the supported installation/runtime contract and provide a useful scoped error if genuinely missing.

Keep local storage adapters behind canonical owning-app interfaces. Do not respond to the no-Files-prerequisite correction by creating an incompatible Boards-only object engine or bypassing permission checks. Optional account/provider configuration appears only when the user invokes the corresponding network feature.

### UX27-DATAFLOW-002 — Artifact opening and ownership

**B/D.** An open request resolves type, canonical identity, revision, owner app, access scope and location. The receiving surface shows whether it is editing the original, an authorised reference, or an explicit independent copy. Open in App preserves identity; Save a Copy deliberately creates independent owned content. A thumbnail, cached preview or failed hydration must not be treated as the canonical artifact.

Cross-app copy uses the existing versioned ProductivityObjectBundle. Preserve rich text, table data/styles, graph expressions, image references and ink where supported. Unsupported properties remain preserved or are disclosed in an explicit conversion result; silently flattening an editable object to pixels is not interoperability.

### UX27-DATAFLOW-003 — Save and sync are separate transactions

**B/D.** Local commit, remote upload and remote semantic conflict resolution have separate states and receipts. A local document remains usable when cloud sync is paused or unavailable. Retry/resume uses the same pending revision identity where appropriate and must not create a new duplicate cloud document each time. Display a failed sync as failed sync, not as lost local save.

A user who moves a file outside a managed sync folder chooses a different storage relationship; do not silently move it back. Explain changed sync coverage while preserving content and identity. Connected cloud services retain their real quota/access restrictions without imposing a fictional universal document-size cap.

### UX27-DATAFLOW-004 — Crash recovery and interrupted close

**B/D.** Maintain a revision-aware recovery journal/snapshot through the owning storage layer. On restart, identify unfinished documents by stable identity, show recoverable revisions and let the user open/compare/discard them intentionally. A failed save must not poison all future close attempts. If close preparation fails, restore the same editable session and visible decision state instead of leaving an invisible modal or permanently unclosable window.

Test interruption before/during/after a durable commit and while an embedded artifact is saving. The recovered result must be a consistent prior/new revision with an honest gap report. Do not assert recovery merely because the process launches again.

### UX27-DATAFLOW-005 — Import/export completion and safety

**B/D.** Import detects actual supported format/version, validates structure and preserves the original until successful. Export states the format, destination, conversion limitations and real completion state. Cancellation or failure removes/quarantines incomplete output without corrupting the prior valid file. Large artifacts stream/chunk according to the scalability annex.

Treat file content and imported metadata as untrusted data, not instructions to the Agent or permission grants. Validate archive paths, expansion limits and resource use through the existing security contract. Reopening an exported native artifact and independently checking its contents is part of acceptance; a nonzero file size alone is not proof.

### UX27-DATAFLOW-006 — References and deletion

**B.** Every embedded artifact declares BoardOwned/DocumentOwned versus Referenced ownership as applicable. Deleting a host container removes its owned content according to recoverable Trash semantics but cannot permanently delete separately owned referenced files without a distinct authorised action. Broken references retain useful metadata and recovery affordances without leaking revoked content.

Restore operations preserve lineage and resolve name/location collisions explicitly. Global search, recent items and notifications update from canonical lifecycle events rather than keeping dead entries that appear openable indefinitely.

## 4. Haven/9to1 host, browser engines and application launching

### UX27-HOST-001 — One real launch path per advertised surface

**O/B.** Apps navigation, deep links, standalone executables and restored tabs resolve through the canonical application registry. Every listed installed app has a real registered surface and compatible services. Selecting Write, Boards, Present, Mesh or another app must not route to an unregistered placeholder or silently open an older implementation. Missing app/engine packages produce a scoped install/repair path with the requested target retained.

A smoke matrix must open every in-scope advertised app from the actual Home Apps view, not merely instantiate classes in a test host. Record the route, resolved surface, package identity and observed result. One shared launch regression affecting many apps has priority over polishing a single successful scene.

### UX27-HOST-002 — Home and New Tab identity

**B/D.** The Home-icon tab exposes Apps, Dashboard and Models/Discover as distinct sidebar views. New Tab exposes those views plus New Tab. Preserve the selected destination and its filter/scroll state. Apps is an actual catalogue; Dashboard is a source-backed briefing; Models is discovery/configuration; none is a reskinned empty chat screen or a duplicate list pretending to be another product.

Cards have real app icons, one primary name and status where relevant. Selecting an app opens its actual native surface in the intended tab. New-tab creation must not reset the browser profile or manufacture duplicate Home tabs unless requested. Search results distinguish installed, available-to-install and unavailable entries.

### UX27-HOST-003 — Tab selection, closing and overflow

**O/B/D.** Use the owner’s selected-tab light surface/black-text default unless an explicit accent override applies. Each tab has an icon, readable title, close affordance when appropriate and accessible state; overflow is navigable without shrinking every tab to an unusable glyph. Close hit targets must not overlap the neighbouring tab. Reordering previews the real insertion point and commits one organisational change.

Closing an editor tab follows save/recovery semantics and is not permission to cancel an explicitly background-capable run. Middle-click behaviour follows the configured platform/product preference, with no accidental touchpad-region interpretation in application code. Reopen closed tab restores a real target and state, not a new empty document with the same label.

### UX27-HOST-004 — Browser viewport and DPI correctness

**O/B.** Allocate the embedded web view using actual content bounds and physical-pixel backing size appropriate to the current scale. Do not stretch a low-resolution bitmap to fit the window, double-scale CSS/content coordinates, or preserve a stale viewport after sidebar/window changes. Resize and monitor-DPI changes must update the engine’s viewport, rendering surface and input transform together.

Test a known page with text, form fields and scrolling at several window sizes/scales. Check visual sharpness and that pointer hits match rendered targets. A screenshot alone cannot establish correct scroll/input transforms; native interaction evidence is required. Document the active engine/version and whether compositing or offscreen rendering is used.

### UX27-HOST-005 — Wheel, trackpad and browser navigation

**O/B.** Pass wheel deltas to the browser with the correct units, sign and scale exactly once. Small trackpad deltas accumulate smoothly instead of disappearing; a normal wheel notch must not move only an imperceptible amount because a second scaling factor was applied. Horizontal gestures, Ctrl+wheel zoom and ordinary two-finger scroll remain distinct. Back/forward and address changes use actual navigation entries, not simply swap titles.

When a native app tab and web tab share the host, the active target receives input; a hidden web view cannot consume scrolling from Boards. Preserve scroll state for each tab independently. Test with an actual mouse and trackpad where available, and report the device-specific gap otherwise.

### UX27-HOST-006 — Engine switching and failure states

**B/D.** Firefox/Gecko and Chromium/Blink remain separately identified adapters with isolated engine-private profiles. Changing an engine keeps the canonical tab/workspace target while recreating the supported web session; do not promise transfer of live DOM/JavaScript state. Persisted site preferences are inspectable and reversible. When one engine is missing or fails, offer the available authorised alternative or repair path without pretending that a renderer is connected.

A missing Chromium message must not disable local Boards, Write or Present. Preserve the pending URL and user-visible cause. Site authentication/verification failures require compatibility investigation using legitimate browser behaviour; do not bypass CAPTCHA, impersonate security state or weaken site protections to claim success.

### UX27-HOST-007 — Profile/account popup lifecycle

**O/B.** Profile/account menus belong to the current host anchor and follow the shared popup contract. Outside click, Escape and navigation that invalidates the context dismiss them. Switching account/profile shows the actual resulting identity before a consequential operation. A popup left over a different app is a failure even if its buttons still work.

Keep browser profile, 9to1 account and document ownership distinguishable. Changing a browser profile is not permission to copy cookies, credentials or private document context into another profile. Local document availability must not be represented as dependent on an unrelated cloud sign-in.

### UX27-HOST-008 — Session restoration and stale artifacts

**B/D.** Restore typed target identities, current locations and permitted view state. Revalidate current package/provider availability and access. Never persist a short-lived in-process object pointer or one-shot capability as the only reopen data. Show a recoverable unavailable-target card for a missing item, leaving other tabs usable.

A restored board must reopen its saved file/draft and actual page. Reloading the host must not replay AI prompts, external actions, saved graph executions or old permission responses. Test a mixed session with native apps and web pages, restart, and verify each tab independently.

### UX27-HOST-009 — Startup and dependency repair

**B/D.** Render the selected theme immediately and expose the real shell before slow document-dependent regions where safe. Instrument cold launch, warm launch, route activation, dependency discovery and first input separately. A brief truthful loading state is preferable to an unexplained white flash or a hidden window that never becomes interactive.

A failed optional dependency is scoped; a missing required local engine names the package/architecture and supported repair action. Never ask the user to connect accounts to compensate for a missing native drawing library. Installation, repair and restart remain permission-controlled and preserve open work.

## 5. Spaces, composer, models and agent visibility

### UX27-SPACES-001 — Space entry and retained sidebars

**O/B.** Selecting a Space from the chosen rail/dropdown opens its actual conversation environment directly, with the selected Space’s contextual sidebar intact. Do not insert an unnecessary management/details screen before chat. New Chat creates a conversation inside the current Space, not a new Space. Conversation rows resolve stable IDs and show the correct title/status without mixing histories from other Spaces.

The rail/dropdown preference persists and both expose equivalent navigation. Switching Spaces atomically updates context, history, model policy and tools while retaining each conversation’s draft. The Toolbox/Space-manager affordance remains a meaningful control, not an unexplained lone plus sign substituted for the approved layout.

### UX27-SPACES-002 — Composer shelf layout and activation

**O/B/D.** Replace a single static Send button with the owner’s contextual action shelf: at most five active contextual items, a primary visible action with the next item visually tucked/peeking, and a dot position indicator. Support the configured horizontal/vertical scroll or swipe browsing direction without triggering the selected action merely because the shelf was scrolled. Buttons have distinct semantic labels/icons and accessible position/state.

The shelf’s visual stack is not a queue of already submitted work. Browsing changes the selected control, not the conversation. Activation uses the currently displayed action and a current-state guard; if the run state changes between pointer-down and release, cancel or revalidate rather than sending the wrong command. Cyclic wrapping and extra unrequested actions are not inferred from the original owner examples; the default is bounded navigation.

### UX27-SPACES-003 — Composer action-state table

**O/B/D.** Required owner examples and safe default completion:

| Runtime/composer state | Shelf actions | Meaning |
|---|---|---|
| Idle, nonempty draft | Send, Voice | Send starts one new request with the retained draft/context |
| Idle, empty draft | Send disabled with reason, Voice | No empty accidental request; Voice starts only through its permission flow |
| Agent running, nonempty draft | Steer, Stop, Queue, Voice | Steer targets the active run; Queue creates ordered later work; Stop requests cancellation at its supported boundary |
| Agent running, empty draft | Stop, Voice; Steer/Queue unavailable until content exists | Do not fabricate an empty steering message |
| Response stopped/paused at a resumable state | Resume, End Pass | Resume continues the same eligible run; End Pass closes it deliberately without erasing prior artifacts |
| Permission/form hold | Contextual status and permitted user controls | A shelf action must not grant permission or answer the form implicitly |
| Offline/provider unavailable | Retained draft plus truthful retry/configuration state | No fake send success; no automatic paid provider change |

The rows completing unspecified empty-draft states are D defaults, not earlier owner quotations. Preserve more specific supported runtime contracts. The text draft is cleared only after the corresponding message/queue admission is established, not when the button animates.

### UX27-SPACES-004 — Steer, Queue, Stop and Resume semantics

**B/D.** Steer durably associates the new instruction with the active request and presents it at a supported safe boundary. It is not a hidden Replace/Restart operation. Queue retains order, stable message IDs and the target context; its admission receipt is distinct from execution. Stop displays Stopping until cancellation/settlement is actually confirmed, preserves partial results and does not declare every side effect undone.

Resume is available only for an eligible paused/stopped run under current permissions. End Pass preserves source/artifacts/history and closes the pass intentionally; it is not deletion. Repeated clicks or transport retries reconcile the same request rather than duplicate work. Explain unavailable operations instead of presenting dead controls.

### UX27-SPACES-005 — Drafts, attachments and input keys

**B/D.** Draft text, attachments, selected context and chosen responder survive tab/Space changes and supported restart recovery. Enter/Shift+Enter in the chat composer follow its documented send/newline preference and are not inherited from Boards block editing. IME commit never accidentally sends a request. Attaching a local file uses authorised local access without requiring an unrelated cloud account.

Each attachment has a visible name/type, ingestion/upload state and remove control. Failed processing does not appear Ready. Removing one attachment preserves the others and the draft. Image/audio/video capability must be truthful for the selected model; a text-only route cannot silently claim to analyse pixels or sound.

### UX27-SPACES-006 — Message rendering and actions

**B/D.** Render actual Markdown/rich content with distinct headings, paragraphs, code, lists, links, tables and citations. Streaming updates the existing message rather than duplicating its text or scrolling the reader to the bottom after they intentionally scroll up. Code blocks preserve whitespace and copy exactly the code; wide tables/code have deliberate horizontal scrolling without widening the entire conversation.

Per-message actions are contextual and target a stable message/revision: copy, applicable edit/retry/regenerate/branch/read-aloud/share operations from the canonical feature set, with unavailable actions explained. Editing an earlier message shows the affected continuation/branch semantics before mutation. A visual action icon without a functioning command does not satisfy message-action completeness.

### UX27-SPACES-007 — Summary, tasks and status truth

**O/B.** Keep the collapsible Summary surface in the agreed top-right placement, with Action Graph access/toggle rather than a second competing progress database. Task Space defaults to its own tasks, with All Tasks as an explicit filter. Working, completed, checkpoint, permission, stalled and blocked states use distinct meaningful icons/labels; a checkpoint badge clears according to the viewed-state contract, not simply because time passed.

Sort active/actionable work coherently and preserve stable task identity through filtering. A live process or growing token count is not completion. Show submitted instruction, queue admission, running work, saved checkpoint and independently accepted output as different evidence states.

### UX27-SPACES-008 — Custom sidebar targets and DEC-014

**O/B.** An extension entry has stable SidebarItemID, origin/package identity, target kind, configured target, permitted Spaces and current permission/revision state. Supported targets include URL/page, authorised app surface, authored CUI, GenerativeUI and declared Automation/action. The row’s icon/label aligns with ordinary navigation; users do not need a separate Plugin panel just to activate a permitted entry.

Use the established typed host/session navigation/action adapter. Visibility is not execution permission. Detaching/revoking an extension removes its active contributions and prevents orphaned actions without deleting unrelated conversations. No arbitrary string execution, guessed URI fallback or inactive rows presented as implemented functionality.

### UX27-SPACES-009 — Model Groups and lazy selection

**O/B.** Preserve the owner’s 16 October Model Groups decision where not yet consolidated: one stable group identity can contain different model versions, quantisations, providers/backends and role/modal capability profiles. Named levels may be Low/Medium/High/Ultra or other owner-defined labels; they are not necessarily numerical reasoning tiers. Persist the group and member routing in Den for cross-app use.

Selecting a member changes the intended responder; it does not immediately load/unload large models. Resolve and load the selected route on Send under capability/resource/permission checks. An active run remains pinned to its actual model/configuration; a later selection affects later work unless an explicit supported handover occurs. Display the effective backend/member so a group label does not conceal a paid or incompatible route.

### UX27-SPACES-010 — Reasoning effort is not confidence or quota

**O/B.** Retain the approved display mappings for models exposing discrete reasoning effort: three levels map to 35/70/100%; four to 25/50/75/100%; five to 20/40/60/80/100%. These percentages select supported effort tiers; they are not measured intelligence, correctness probability, usage consumption or a promise of continuously adjustable internal reasoning.

Expose only supported levels and distinguish unavailable/disabled reasoning. The UI label and tooltip identify effort. A model-group level and a reasoning level remain separate concepts. The actual selected provider tier is recorded with the request so later evidence is not inferred from a rounded display percentage.

### UX27-SPACES-011 — Usage display and unknown data

**O/B/D.** Usage is separate from reasoning effort and context usage. Hide provider-allowance meters for local models when no such allowance applies. When a likely metered provider supplies no usable data, show the agreed M/unknown indication rather than an invented percentage. Home model settings support pinned/custom limits with explicit unit and provider/model scope; custom values are labelled user-defined, not provider-reported.

Where several real limits apply, show the currently constraining/soonest-blocking limit with details for the others, subject to a user pin override. Never compare raw remaining messages with remaining tokens as if they were the same unit. Any time-to-exhaustion estimate must expose its basis and uncertainty. Retain last-observed timestamps and distinguish stale data from a current allowance reset.

### UX27-SPACES-012 — Research, Shopping and Study surfaces

**O/B/D.** Research presents a plan, source scope, actual run status and report/artifact outputs. Retain the established Quick/Extended/Deep/Custom options where supported, labelling duration as a selected budget rather than guaranteed execution. Shopping separates strict matches, partial/similar matches and other contextual suggestions, with currency/source/freshness and the reason a result qualifies. Do not silently replace a constrained result with a sponsored or merely adjacent item.

Study presents Topics, practice, revision materials and recorded progress from actual answers. Showing content does not count as mastering it. These Space types keep their distinct typed interfaces while sharing conversation, permissions and artifact models; they are not only different system prompts over an identical empty chat screen.

### UX27-SPACES-013 — Assistants, Main chats and teams

**O/B.** Assistants remains the specified Space. Each persistent Assistant has its permanent Main conversation and stable Agent identity; renaming or changing an avatar does not replace either. Team interactions use the canonical Dulche coordination and visibility policy, including the agreed All/Relevant/None presentation setting for Assistant-to-Assistant chat where applicable.

Presentation filtering does not hide consequential actions from the audit/permission record. Team membership does not grant every member access to all private context. New or restored conversations use the correct Assistant, tools and source scope without manufacturing another independent runtime.

### UX27-SPACES-014 — Voice & Vision and context indication

**B/D.** Listening, recording, screen sharing and camera use are visibly distinguishable and stop through real controls. Device selection, permission denial, reconnect and end states preserve session identity and transcript where policy allows. Typing during Voice remains available through the shared composer without unexpectedly ending the session. Captions and transcript edits are not silently treated as new commands.

Show which screen/window/source is shared and whether processing is local/cloud. A model that cannot process video/images must receive an appropriate supported representation or a truthful limitation; it must never claim visual inspection from a filename alone. Preserve the base privacy and ephemeral-audio rules.

## 6. Automations and shared node editing

### UX27-AUTO-001 — Home, Library and draft/active distinction

**B/D.** Home shows real active/waiting/failed/recent runs and upcoming triggers. Library lists canonical Automations with enabled state, active revision, last result and next trigger where actually calculable. Opening an item shows its graph/definition, not a disconnected dashboard clone. Create has a useful simple When/If/Then route and a real graph route over the same GraphID.

Editing changes a Draft; active schedules continue using the pinned Active revision until validation/activation. The UI clearly labels draft versus active and exposes the diff/validation result. Saving a draft does not publish it or restart an existing Run.

### UX27-AUTO-002 — Node insertion and connector gestures

**B/D.** Right-click empty graph space opens searchable registered nodes at the invocation position. Typing filters by action/event/type and permissions; selecting inserts at that graph coordinate. Dragging a typed port shows compatible targets and a live connection preview; incompatible targets are visibly rejected. Escape cancels without leaving a partial connection.

Preserve the specified Ctrl-click connector deletion, connection context-menu Delete and keyboard alternative. Removing a connector never deletes its nodes. Node selection/move, panning, marquee selection and text-field editing have distinct pointer states. One move/connection change is one undo transaction and survives save/reopen.

### UX27-AUTO-003 — Run visibility and truthful execution

**B/D.** Each run shows TriggerID/event, pinned definition revision, node states, inputs/outputs permitted for display, current wait/approval state, errors and causal/audit links. A pulsing node means the actual runtime is working/waiting in that state, not an animation simulating progress. Run status is not derived from whether the editor is open.

Test execution uses explicit test inputs/sandbox settings where available and distinguishes dry-run from consequential execution. A node that sends, publishes or mutates external data retains its normal permission boundary during a test. No fake sample result may be shown as the outcome of the user’s real run.

### UX27-AUTO-004 — Durable Wait, approval and restart

**B.** Wait/Delay persists continuation state and releases active execution resources according to the runtime contract; it is not only an in-memory timer. Restart resumes from the saved node boundary with the same RunID/revision and does not repeat committed side effects. Approval-required work remains held until its actual approval, regardless of a due-time query.

Verify suspend → stop process → reopen → resume → complete against the real persistent store. Reconcile the previously reported SuspendRunAsync/CompleteRunAsync same-ID risk in current source before accepting the lifecycle. A test that only round-trips continuation columns does not establish durable execution.

### UX27-AUTO-005 — Schedule and enable/disable controls

**B/D.** Schedule editing displays timezone, recurrence, next occurrences and ambiguity for daylight-saving transitions. Enable/disable affects future admissions according to an explicit policy; it does not silently terminate already running work or erase history. A missed trigger has a declared catch-up/skip policy, not uncontrolled replay after an outage.

A cloud-capable workflow must not unnecessarily depend on the visible desktop window. Show the actual executor’s availability and last observation, and distinguish queued, admitted, running and completed. Do not create duplicate timers when the Automation page mounts twice.

### UX27-AUTO-006 — Retry, cancellation and idempotency

**B/D.** Retry identifies which failed boundary is retried and what already completed. A timeout with unknown effect enters reconciliation, not automatic re-execution under a new ID. Cancellation requests stop at supported boundaries and records any work that already happened. No “Cancelled” result may imply external effects were reversed without evidence.

Expose safe retry/cancel controls only for states that support them. Preserve errors and partial outputs. Cross-app actions keep the owning app’s revision/permission semantics and cannot bypass View mode or protected source merely because they originate in an Automation.

## 7. Build/Dev, Sites and Stacks — explicit developer workflows

### UX27-BUILD-001 — Adaptive product, retained domains

**O/B.** Build integrates Dev, Sites and Stacks functions while preserving each canonical project/domain model. The same project can move between code editing, visual site/CUI editing, source review, build/test and deployment without copied shadow projects. The navigation communicates the active project, environment and source revision, and changes its tool surfaces according to the current artifact type.

Opening an existing repository consumes its real project files rather than requiring a proprietary replacement. New Project asks only applicable language/framework/runtime options, validates the destination and toolchain, and creates a genuinely buildable project. Missing tools get explicit supported configuration/install choices, not a simulated successful project.

### UX27-BUILD-002 — Editor input, tabs and diagnostics

**B/D.** Code editing preserves selection, indentation, undo, line endings and file encoding. Tab previews versus pinned editors are explicit; closing a dirty editor follows save/recovery. Language diagnostics target exact file/range/revision and clicking them navigates correctly. Stale results from an old build are labelled and must not overwrite current diagnostics silently.

Completion, rename/refactor, find/replace and formatting show the actual change scope and remain undoable. AI edits are inspectable diffs against a pinned base, not opaque whole-file replacements that discard concurrent changes. Protected/frozen source boundaries remain enforced outside the UI as specified.

### UX27-BUILD-003 — Run/build/test/debug pipeline

**B/D.** Run selects an explicit configuration/environment, restores/builds as required, and reports actual stages/exit codes. A failed compile cannot show a successful preview from an unrelated old executable without labelling it stale. Stop targets the owned run/debug process, not all similarly named processes. Test navigation links failures to logs and source; skipped/platform-blocked tests remain distinct from passes.

Debugging exposes breakpoints, pause/continue/step, stack, variables and console through the real debugger adapter. Editing while debugging follows a declared supported hot-reload/rebuild policy. Preview frame identity and source revision remain inspectable.

### UX27-BUILD-004 — Visual editor and CUI authoring

**B/D.** Visual selection, property edits, hierarchy changes and code/CUI edits update the same canonical source model. Drag handles, snapping, align/distribute, responsive preview and component insertion have actual undoable semantics. Generated ordinary UI is canonical CUI, not AXAML renamed after generation or a hidden legacy tree.

A component palette item is available only when its registered contract exists. Invalid properties produce useful diagnostics rather than swallowed parser failures. Preview and shipping build must use compatible rendering/runtime paths; a designer-only visual is not evidence of app integration.

### UX27-BUILD-005 — Source control and conflicts

**B.** Preserve the detailed Stacks Branch/Twig/Leaf/Root/Freeze semantics and source ownership, including its explicit review choices for AI conflict resolutions. A conflict view shows exact files/hunks, base/local/incoming identities and the result of each choice. Accept without Merging and Accept and Merge remain distinct. Reject restores the proposed-resolution state without destroying the original work.

The owner’s current request to consolidate 9to1 development onto main is a programme policy, not a deletion of branching capabilities from Build. Prune, force-push and protected-branch operations retain their elevated safeguards. Local commit, push, merge and published deployment are separate statuses.

### UX27-BUILD-006 — Sites publishing and rollback

**B/D.** Site editing, local preview, build artifact, staging and production deployment have explicit identities. Publish displays target/environment, relevant changes and required permissions; it never changes production merely because a preview is opened. A failed deployment preserves the previous live version and reports the actual failed stage.

Verify routes, assets, forms/data bindings, responsive layouts and the deployed build—not just a local screenshot. Rollback selects a known valid deployment and records its result. Secret references stay protected in logs and exports, and a template must not contain the owner’s live credentials or private sample data.

### UX27-BUILD-007 — Agentic development and review

**B/D.** The Agent view identifies task scope, source base, current owned changes, execution mode, tools, blockers and review status. Steer/queue/stop map to the same durable runtime as Spaces. A draft instruction is not an admitted task; a completed tool command is not accepted product work. Keep permission prompts and unresolved ambiguity visible.

Acceptance checks run against exact candidate/source artifacts, with author/reviewer separation recorded. A text-only worker can inspect source and automation output but cannot claim to visually inspect screenshots. It may capture them for a capable reviewer and leave that gate NOT RUN.

## 8. Write — mature document editing through the actual engine

### UX27-WRITE-001 — Real document, page and caret

**B/D.** Write remains a mature Writer/LibreOfficeKit-backed word processor, not a block-based web editor. Show actual pages, margins, pagination, text flow and a caret positioned by the document engine. Clicking a line sets the correct insertion position; dragging selects text rather than moving a page container. Text entry, selection and paragraph creation must work across page boundaries without a separate input box detached from the visible document.

Use the same canonical document for the canvas, outline, inspector, AI actions and exports. A page image with an invisible unrelated text field cannot satisfy editable-document behaviour. Test multi-page text, tables and mixed formatting through the real routed app and standalone entry point.

### UX27-WRITE-002 — Contextual toolbar and persistent actions

**B.** Preserve the original Home/backstage, persistent +/Eye/overflow controls and no-permanent-ribbon rule, together with the 12 October rich floating-toolbar amendment. Daily font/style, size, emphasis, alignment, list, colour and link actions are directly reachable. The contextual toolbar is one coherent surface, not several unrelated strips added by each selection type.

Selecting text, a table or an image changes only relevant groups and retains selection. Disabled/mixed states reflect the real engine. Do not hide ordinary Bold/size/style behind a long Properties inspector or turn the entire app into a menu of diagnostic commands.

### UX27-WRITE-003 — Styles and paragraph formatting

**B/D.** The style selector shows the paragraph/character style currently effective at the caret, including mixed selection. Applying a paragraph style updates the intended paragraphs without deleting inline content or replacing document identity. Direct formatting and named-style changes remain distinguishable; changing a style definition updates its references through the engine rather than rewriting each paragraph to an unrelated local style.

Indentation, line spacing, before/after spacing, lists, tabs and page/section breaks have real layout effects. Undo/Redo and save/reopen preserve them. The app theme does not alter authored document colours/fonts or turn a dark document preview into an exported dark page without explicit document styling.

### UX27-WRITE-004 — Sections and navigation

**B/D.** The Sections/outline sidebar reflects semantic document headings/sections with stable targets, nested disclosure and meaningful selection. Clicking navigates to the correct region and reveals it without losing the caret unnecessarily. Renaming a heading from an approved outline action edits the actual text; reordering a section moves its defined content range with preview/undo, not just the sidebar row.

Search shows matches across unloaded pages and navigates by stable positions. A hidden/offscreen paragraph is not absent from search/export. Long-document navigation uses indexes and incremental layout rather than blocking the UI while all prior pages are rendered.

### UX27-WRITE-005 — Tables, images and anchored objects

**B/D.** Table row/column/cell controls operate on real document tables, with merge/split, headers, borders and repeat-header behaviour where the donor supports it. Images support actual wrap/anchor/position/crop/size properties. Selecting an image exposes handles that follow the document’s layout at every zoom; dragging updates a preview and commits one engine transaction.

Changing text before an anchored object follows the chosen anchoring semantics, not arbitrary screen coordinates. Copy/paste and export preserve the relationship or disclose a genuine conversion limitation. Do not substitute a screenshot of a table or image container for its mature engine behaviour.

### UX27-WRITE-006 — Inspector, comments and review state

**B/D.** The optional Properties inspector is resizable and context-specific, with logically grouped controls and a single scroll owner. Common operations stay in the toolbar; precise advanced configuration belongs here. Comments and supported review/track-change states are attached to canonical document ranges, with author/time/revision provenance and navigation to the target.

Accept/reject review actions operate on actual changes, remain undoable where supported and do not silently accept unrelated edits. Missing review support must remain a donor-parity gap, not be hidden because the ordinary text editor passes a smoke test.

### UX27-WRITE-007 — File formats, persistence and export

**B/D.** Preserve the native/shared model and required donor interoperability. Open/save/export use genuine supported formats, with explicit conversion limitations and exact output identity. A successful save is verified by closing and reopening the produced artifact, checking representative text, styles, images, tables, notes/review features and page layout where applicable.

Saving in an ordinary local location must not unnecessarily depend on a cloud account. Large-document autosave is incremental according to the scalability annex. A failed export cannot leave a truncated file labelled complete or replace the only valid original.

## 9. Present — routed editor, direct manipulation and playback

### UX27-PRESENT-001 — Correct route and one implementation

**O/B.** Trace the actual `9to1://present` registration to the current production surface and verify the installed build uses it. A badge or layout change in an unused standalone scene does not count. Preserve the 12 October Present amendments and all donor baseline capabilities; the source names PresentPage and PresentHavenScene are historical diagnostic leads, not permanent architecture requirements.

Test the actual host activation and the standalone app against the same deck identity. A test that directly constructs a page and asserts a badge is present is useful unit evidence but is not proof of host route activation, visual correctness or a functioning presentation editor.

### UX27-PRESENT-002 — Header, menu and editing toolbar

**B/D.** Retain the specified compact document header, conventional menu row above the toolbar, and exactly one persistent primary floating editing toolbar. Group title/chevron, truthful save state and relevant history/collaboration actions coherently. Present is a prominent action. File/Edit/Insert/Slide/Arrange/View/Help open real menus; do not replace them with labels or scatter another ribbon below.

Use the current approved Present identity/accent assets, not a hardcoded sample badge standing in for the full shell. Commands expose accessible names and contextual enabled/mixed states. Overflow preserves all commands without wrapping into an uncontrolled second permanent toolbar.

### UX27-PRESENT-003 — Slide list, notes and canvas

**B.** The left navigator contains real slide thumbnails and section structure, with selected state, slide order and context menus. The central slide remains visually dominant and preserves its actual aspect ratio under Fit/zoom. Notes are a real editable per-SlideID/NotesID surface, collapsible/resizable below; a placeholder appears only when notes are empty.

Changing slide restores the correct notes, selection and content. Thumbnail generation follows the relevant deck revision and does not freeze editing. Adding/deleting/reordering a slide modifies the deck, not merely the UI list, and remains undoable/reopenable. The right inspector is optional and cannot squeeze the slide to an unusable postage stamp.

### UX27-PRESENT-004 — Selection versus text editing

**B/D.** The interaction state machine distinguishes hover, object selection, text editing, moving, resizing, rotating, insertion, multi-selection and panning. A single click selects the intended object; the supported double-click/keyboard action enters text editing. Text-range dragging in editing mode must not move the parent text box. Dragging the border/body in object-move mode preserves the pointer’s grab offset.

Escape cancels the current insertion/gesture or exits text editing before clearing higher-level selection. Arrow keys move the text caret while editing and nudge an object only in the corresponding selection state. Context menus target the clicked object rather than a stale selection behind it.

### UX27-PRESENT-005 — Handles, cursors and live transforms

**B.** Applicable rectangular objects show eight resize handles and a rotation affordance where supported, with usable hit targets and directionally correct cursors. Hover outlines are distinct from selection. Tiny or narrow objects use an adapted hit-target strategy rather than overlapping handles. Moving/resizing/rotating updates content and adorners during the gesture, with correct zoom/scroll transforms.

One finished gesture is one undo entry. Escape/cancel restores the previous geometry. Group/ungroup, multi-select, align/distribute and z-order operate on real object IDs. A selection frame must not detach from its object after viewport resize or theme change.

### UX27-PRESENT-006 — Insertion catalogue and shape gallery

**B/D.** Insert exposes slide/layout/section, text, shapes, images/media, tables, charts, links and other applicable registered types. Shapes opens a gallery of actual available forms with recognisable previews; merely opening the gallery must not insert a rectangle. Choosing a shape starts a defined draw-to-insert or explicit click placement, with preview and Escape cancellation.

New objects enter a useful selected/editable state and can immediately be formatted, moved, saved and reopened. A gallery item without a real engine action is disabled with an honest reason or absent until supported, not treated as implemented insertion.

### UX27-PRESENT-007 — Formatting and shared state

**B/D.** Toolbar, inspector, context menu and shortcuts operate on one authoritative object/document state. Text controls include supported font, size, emphasis, colour, alignment, lists and spacing; object controls include fill/outline/opacity and geometry. Common formatting applies/previews directly where appropriate; an unnecessary Apply button is not required for every colour change.

Mixed selections show mixed state and apply only valid shared operations. Changing a slide’s theme/layout/master uses donor-backed semantics, preserving custom content and providing undo. Do not flatten an editable object to an image to avoid handling its properties.

### UX27-PRESENT-008 — Present, presenter view and recovery

**B/D.** Present starts playback from the chosen slide using the actual deck, with reliable navigation, exit and supported media/transition behaviour. Presenter View shows the proper notes/current-next slide information on its intended display without leaking notes into audience output. Stopping playback returns to the same editor/slide/selection.

Test slide editing, notes, media, save/reopen and playback from the exact final build. Missing display hardware or native-platform coverage stays BLOCKED/NOT RUN. A successful editor launch is not playback acceptance, and a playback screenshot is not proof that the deck remains editable.

## 10. Canvas — deliberate spatial editing, not an accidental embedded panel

### UX27-CANVAS-001 — Canvas identity and tools

**B/D.** Canvas is its own full drawing/visual-artifact surface with the canonical engine/model. It is not the unexplained white rectangle appended to every Native Boards page. Its document has explicit spatial/layer identities, viewport state and edit tools. When hosted as a Boards PageType, it remains the same artifact with the same supported semantics and a clearly intentional entry point.

Select, pen, eraser, shape, text, pan and other declared tools have distinct active states and cursors. Tool switching does not create content. Unavailable capabilities are explained rather than represented by dead icons.

### UX27-CANVAS-002 — Pointer capture, zoom and panning

**B/D.** Drawing and manipulation use canonical document-space coordinates with consistent inverse viewport transforms. Pan moves the view, not the document objects. Pointer-anchored zoom retains the intended point under the pointer where supported; Fit computes actual bounds including relevant objects. Normal wheel/trackpad scrolling and modified zoom remain distinct.

Test high/low zoom, negative coordinates, large documents, monitor-scale changes and out-of-window capture. Avoid strokes jumping to screen origin or resizing in physical pixels after a DPI change. Lost capture/cancel must leave no stuck drag state or spurious connecting stroke.

### UX27-CANVAS-003 — Selection, layers and object transforms

**B/D.** Hit testing follows z-order, visibility, locks and active tool. Lasso/marquee selects actual objects/strokes according to the declared intersection rule, with a visible selection preview. Move/resize/rotate/group/ungroup update live and commit once. Locked layers cannot be mutated through alternate shortcuts or AI actions.

The layers/object panel reflects canonical order and hierarchy. Renaming, visibility and lock toggles have meaningful icons and accessible state; dragging a layer shows the actual destination and rejects cycles. Undo restores order and content. A layer hidden in the UI must not disappear from saved data or export without an explicit export-visibility rule.

### UX27-CANVAS-004 — Brush and eraser fidelity

**B/D.** Brush width/colour/opacity and supported pressure/tilt settings apply to the intended new/selected strokes. Segment and whole-stroke erasure remain distinguishable. Smoothing may improve presentation but must not introduce unacceptable lag, alter stored points unpredictably or discard intentional small marks. Mouse fallback works when stylus pressure is absent.

Test editability after save/reopen and cross-app embedding, not just raster appearance. An imported image is not a replacement for native editable ink. Record which physical input devices and native platforms were actually tested.

### UX27-CANVAS-005 — Bounds, export and recovery

**B/D.** Export lets the user select whole document, page/artboard or selection with clear bounds, background and resolution/vector options appropriate to the format. Native save preserves layers, transforms, strokes and editable objects; static export is explicitly separate. Empty/offscreen content is not silently omitted because it was never materialised in the viewport.

Use culling/streaming for large drawings, retain recovery data and handle failed writes without overwriting the valid artifact. Reopen the native file and independently inspect representative object properties as well as its preview.

## 11. Data — tables, formulas, views and real state

### UX27-DATA-001 — Cell selection versus editing

**B/D.** Data distinguishes active cell, selected range and cell-edit mode. Click selects, typing edits/replaces according to the documented spreadsheet behaviour, and the formula bar shows the actual expression/value of the active cell. Arrow/Tab/Enter navigation respects edit mode and does not move selection while an IME candidate is being committed. Multi-range selection and fill handles operate only where supported and visibly indicated.

Column/row headers select the intended stable range and provide contextual operations, not a separate button for every cell. Copy/paste preserves applicable values, formulas, relative references and formatting through the canonical model.

### UX27-DATA-002 — Formulas and recalculation

**B/D.** Formula edits validate syntax and dependencies, distinguish pending calculation from an error, and update affected cells incrementally. Show actual error categories at the affected cells without replacing the workbook with a generic failure. Relative/absolute references, named ranges and cross-sheet references remain stable under supported insertion/reorder operations.

Undo a formula change restores the expression and recalculated dependencies. Export/reopen verifies formulas and values, not only displayed strings. A model-generated number is not a formula engine result unless it was actually written and calculated through the domain API.

### UX27-DATA-003 — Database views and Boards embedding

**B.** Structured records, field schemas, filters, sorts, groups and supported table/kanban/calendar/gallery views are projections of one canonical Data artifact. Changing a view does not duplicate records. A Boards database block references that artifact and retains permissions, formulas and revision semantics; no Boards-only shadow database is introduced to imitate AppFlowy.

View settings are explicit, persist appropriately and distinguish personal from shared configuration. Filtering hides records from the view without deleting them. Dragging a kanban card updates the specified grouping/status field and is undoable/permission-checked.

### UX27-DATA-004 — Import, large data and errors

**B/D.** Import previews delimiters, encoding, column types and conversions before committing where ambiguity can lose data. Preserve leading zeros, dates and text intentionally rather than silently coercing everything. Large data uses indexed/virtualised views and incremental processing; an unrendered row is still searchable/exportable.

Permission failures, schema conflicts and partial external refreshes preserve prior valid state and identify affected sources. An external sync status is separate from local persistence. Test a real Data artifact embedded in Boards, edited from both surfaces and reopened with matching state.

## 12. Files — optional integration, mature file-manager behaviour

### UX27-FILES-001 — Navigation and selection

**B/D.** Preserve the selected Files donor’s mature sidebar, tabs, breadcrumbs/address entry, back/forward, search, list/grid/details views and keyboard selection behaviour through CUI. File/folder icons, type, modified state, availability and selection are legible at the chosen density. Double-click/Open activates the intended item; rename is deliberate and does not accidentally open it.

Tab history and view preferences persist by location where appropriate. Network/provider failure affects that location, not unrelated local browsing. A folder named Boards is an ordinary folder, not a hidden prerequisite imposed on the Boards app.

### UX27-FILES-002 — File operations and conflict dialogs

**B/D.** Copy/move/rename/delete create real operations with progress, destination, conflict policy and cancellation semantics. Drag/drop indicates copy versus move and validates cross-provider behaviour. A conflict dialog distinguishes replace, keep both, skip and scoped apply-to-all where supported; it must not silently overwrite because a name collision exists.

Trash is distinct from permanent delete. Undo reflects the actual provider’s recoverability, not a decorative button. Long operations continue through the supported operation service rather than disappear when the current folder view closes.

### UX27-FILES-003 — Open with and owning-app semantics

**B.** Opening `.9to1board` delegates to Boards’ canonical artifact parser/editor, with narrow current access. Files may index or synchronise the file but does not own its semantic block/ink merge. Open With and Reveal actions expose actual registered handlers and locations. A stale registration is repaired explicitly, not hidden by opening a different file with a matching title.

A user can create/open/save a board without ever connecting the Files UI. Optional hosted Files/cloud capabilities remain available through their proper adapters and consent, without replacing ordinary local Save As.

### UX27-FILES-004 — Sync, availability and offline status

**B/D.** Distinguish local-only, available-offline, cloud-only, uploading/downloading, conflict, revoked and unavailable items. A cloud-only entry does not falsely appear fully local; a local revision waiting to sync is not lost. Show the actual provider/account and last refresh, with bounded retries and actionable errors.

Structural file changes use Files’ canonical identity/revision model; semantic document changes remain with the owning app. Do not resolve simultaneous edits of rich artifacts through arbitrary byte-level last-write-wins. Retain private thumbnails/indexes according to current access and privacy policy.

## 13. Remaining V2 products — concrete interaction expansion without scope deletion

These products retain all baseline requirements and donor parity. Their presence here is not permission to divert critical capacity from the priority cohort. The rules below add observable interaction detail; they do not replace the original app chapters.

### UX27-OTHER-001 — Planner calendar and task scheduling

**B/D.** Day/week/month/agenda views are projections of the same canonical Events and PlannerTasks. Clicking an empty time slot opens a draft with the clicked date/time; dragging previews start/end before commit; resizing changes duration with visible time labels. A task dragged from an unscheduled list into the calendar schedules that same TaskID, not a duplicate Event. Removing its scheduled window does not delete the task or due date.

Recurring edits explicitly choose occurrence/following/series scope. All-day items remain date-based and do not drift with timezone conversion. Undo, cross-view updates, reminders and reopen use the same state. Overlaps and unavailable calendars are visible rather than silently shifted to a different time.

### UX27-OTHER-002 — Planner schedules, Burrows and reminders

**B.** Schedule progress shows actual current item, elapsed and remaining time, with the timezone and paused/ended state where relevant. Burrows retain the baseline’s intended organisational/task semantics rather than becoming a second calendar database. Reminder snooze changes reminder delivery, not the underlying event’s start or due time.

Navigation from a reminder opens its canonical source entity. Completing a task updates every view and its history; dismissing a notification is not task completion. Source-linked places/routes and artifacts remain canonical references with clear unavailable states.

### UX27-OTHER-003 — Mail reading and conversation navigation

**B/D.** Account/folder navigation, message list and reading pane remain distinct, with unread, selected, flagged, attachment and sync states. Selecting a message displays its real content and identity; loading another cannot flash the previous message under the new sender/title. Thread expand/collapse preserves individual message provenance and accessible navigation.

Remote images and active content follow privacy/security settings. A blocked resource explains its state without falsely claiming the message is empty. Search identifies the actual account/folder scope and returns accessible messages, not generated summaries presented as mailbox records.

### UX27-OTHER-004 — Mail compose, attachments and send

**B/D.** Compose retains recipients, subject, rich/plain content, attachments, sender identity and draft revision. Recipient chips resolve actual addresses and expose ambiguity rather than guessing. Attachment upload/availability is visible; remove acts on the selected attachment only. Send validates required fields and makes the actual sender/destination clear.

Outbox, provider submission, sent-folder confirmation, scheduled send and Undo Send are distinct states with real supported semantics. A timeout cannot be blindly resent under a new identity. AI drafting is not implicit permission to send. Closing compose preserves a recoverable draft unless the user deliberately discards it.

### UX27-OTHER-005 — Forms authoring and respondent mode

**B/D.** Author mode uses a structured question list/canvas and properties over stable QuestionIDs. Dragging reorders questions with insertion feedback and preserves branching/validation references. Adding a question opens appropriate settings for its type, not every possible property at once. Preview/respondent mode exercises the actual form definition without editing it; returning preserves author state.

Required-field errors appear at the affected questions with an accessible summary/focus path. Submission succeeds only after the configured durable response write, including Data integration where required. Partial writes/retries do not duplicate responses. Assessment, maths, table and multiplayer workflows retain their full baseline semantics rather than being reduced to a simple contact form.

### UX27-OTHER-006 — Cards study and editing

**B/D.** The card stack uses the agreed depth/overlap presentation with front/back editing and a distinct study mode. Selecting a card for editing must not mark it studied. Flip is an explicit action with an animation that respects reduced motion; the accessible equivalent exposes front/back content without requiring motion. Next/previous navigation targets real CardIDs and preserves order/history.

Ratings or answer outcomes update the actual study/review model only on an explicit response. Undo an accidental rating where supported. Card creation/import/export and cross-app Study integration use the canonical deck/cards rather than an isolated presentation-only stack.

### UX27-OTHER-007 — Picture image editing

**B/D.** Open an actual supported image with clear zoom/fit, colour/profile and document identity. Crop, rotate, transform, selection and adjustment tools have previews, Apply/Cancel where staged, and coherent undo. A crop handle tracks image coordinates at zoom; dragging a selection must not pan unless the pan tool/modifier is active.

Preserve source resolution and non-destructive edits where supported. Animated/multiframe inputs expose frame/playback state and warn before explicit flattening. Save/export distinguishes the editable source from a transformed output, preserving the original when conversion is lossy. Unsupported codecs are diagnosed rather than displayed as successful blank canvases.

### UX27-OTHER-008 — Wave audio timeline

**B/D.** Tracks, clips, waveform, ruler, playhead, selection and transport share actual timeline state. Clicking the ruler seeks; dragging a clip moves it with snap feedback; trimming changes its source range rather than resampling silently. Play/pause/stop and loop are visibly distinct, and meters reflect real signal rather than decorative animation.

Edits remain undoable, with clear destructive/non-destructive boundaries. Export uses explicit range/format settings and real rendering status. Missing media retains a relinkable reference. Background waveform generation is lower priority than responsive transport/input and cannot silently omit audible material.

### UX27-OTHER-009 — Motion video editing

**B/D.** Media bins, source/program viewer, timeline tracks/clips, inspector and effects have clear ownership and selection. Scrubbing updates the corresponding actual frame/timecode; move/trim/ripple/roll operations show their effect before commit and preserve sync relationships. Snapping is a visible setting, not an always-on hidden constraint.

Transitions/effects/keyframes remain editable structured state. Proxy quality and dropped preview frames are disclosed and do not redefine final output fidelity. Export identifies sequence/range, codec/settings and actual completed artifact; an interrupted render never appears finished. Relink/recovery and save/reopen preserve the project independently of temporary preview caches.

### UX27-OTHER-010 — Maps search, pins and routes

**B/D.** Search is the default deterministic mode, distinct from AI. Results show source/provenance and resolve to real places; selecting one opens relevant details/actions without inventing an exact address. Dropping a personal pin creates a private landmark at the chosen coordinates. Public community submission is a separate explicit action, not a side effect of saving a personal place.

Route stops are reorderable with clear start/destination/intermediate roles and recalculation state. Provider failure retains the intended route inputs and explains missing capability. Current location and saved private places remain permission-controlled. Planner/Boards links reference canonical Place/Route identities rather than copied address strings.

### UX27-OTHER-011 — Shelf collections and launching

**B/D.** Shelf presents real typed launch targets with useful icons/artwork, availability and explicit open actions. An item can belong to multiple collections without duplicating the underlying file/app. Dragging within a collection changes ordering; dragging to another collection adds/moves membership according to a visible chosen operation. Removing membership is not uninstalling or deleting the target.

Missing applications/files remain recoverable unavailable entries with Locate/Install where supported. AI organisation resolves actual targets and shows its proposed changes. Do not claim a game/app is installed or offline-capable from an invented label. Shelf remains distinct from the Android Launcher.

### UX27-OTHER-012 — Terminal sessions and process state

**B/D.** Tabs/panes show actual session, working directory and process status. Terminal text selection/copy is distinct from sending input; pasting multiline commands follows the configured safety/confirmation policy. Closing an active terminal makes the effect on its owned process clear instead of silently killing unrelated services.

Resize, scrollback, Unicode/colour handling and keyboard routing use the real terminal adapter. A disconnected remote terminal retains its transcript and explains reconnect semantics; a new shell is not falsely presented as continuation of the previous process. Secrets must not be copied into project reports merely because they appear in terminal output.

### UX27-OTHER-013 — AI Studio builders and test boundaries

**B/D.** Harness, Agent, Tool, Skill, Plugin, MCP, Schema, Router and Generative UI builders operate on typed versioned definitions with clear Draft/Active/Test states. Property forms expose meaningful validation and dependencies, not an arbitrary JSON editor as the sole required user experience. Graph edits and form edits update the same definition.

Playground/evaluation/replay results identify the model, inputs, definition revision, permissions and actual run. Publishing/installing a definition is distinct from testing it. Permission simulation cannot grant live permissions. A generated avatar preview does not imply the Agent is executing the activity shown unless a real event drives it.

### UX27-OTHER-014 — Connect and account health

**B/D.** Connection cards display provider/account identity, granted capabilities, actual health and last successful observation. Connect/reconnect shows the real provider authorisation flow; disconnect explains affected features and revokes future access through the supported contract. A healthy local app must not become unusable because an optional connector is absent.

Do not store credentials in exported documents, previews or logs. An account shown in a list is not proof that every requested scope is granted. Failed permission requests remain failed; the Agent cannot silently select a different account to avoid a denial.

### UX27-OTHER-015 — Games and creation workspaces

**B/D.** Development and Creation/Rendering are workspaces over the same canonical project/assets. Scene hierarchy, viewport selection, inspector, asset browser and timeline/animation tools share stable object identities. Selecting in the viewport highlights the corresponding hierarchy node; changing a property updates the real scene and is undoable. Play/test mode is explicitly distinct from authoring and does not silently save runtime state over the source scene.

Build/export, debugging, modelling, materials and animation retain their donor/baseline scope. Preview rendering settings and final output settings are distinct. This UI expansion introduces no new gameplay content or special permissions; it preserves ordinary safe creation and testing boundaries.

### UX27-OTHER-016 — Play and Experiences

**B/D.** Play shows actual match/round/turn state, contestants, legal actions, timing and score provenance. Controls cannot execute actions outside the current rules/visibility policy. Spectator view is not authority to reveal hidden state. Restart/replay reconstructs the recorded match without duplicating external tool effects.

Experiences retains its canonical structured state and visibility rules, separate from prose presentation. A narrative statement of a state change is not proof the domain transition committed. Preserve age-appropriate content controls and permission boundaries; this specification does not authorise bypasses or unsafe simulations.

### UX27-OTHER-017 — Mini Computer, Mesh and remote sessions

**B/D.** A remote/session surface identifies the actual machine/runtime, connection state, input ownership and active permissions. View-only and control modes are distinct and visibly indicated. Reconnect must not create a new machine/session while pretending to resume the old one. Pending consequential operations are reconciled before replay.

Clipboard/files/credentials crossing the boundary require their real authorised contracts. Closing a viewer is not automatically shutdown of the remote session. A failed Mesh/remote launch remains a scoped actionable error, not a fabricated screen or an unconditional reason to disable local editors.

### UX27-OTHER-018 — Links, Binders, Sheets and organisation surfaces

**B.** Preserve the baseline’s distinct owning products: a Binder/Flow references its canonical Link document; a Files Sheet is Files-owned organisation, not a renamed Link type. Nonlinear navigation, nested authored content, references, overview/outline and deep links remain coherent after rename/reorder/reopen. Moving a card in a view must not silently relocate or duplicate its referenced source artifact.

Embedded organisation pages in Boards clearly identify the owning artifact, read/edit capability and Open original action. Returning to the board restores context. A visually similar grid of labels is not acceptance of the real information model.

## 14. Acceptance protocol and executable scenario catalogue

### UX27-ACCEPT-001 — Seven gates and exact-artifact review

**B.** Every complete app and substantial journey must pass Usability, Product fit, Functionality, Integration, Separation and authenticity, User fit and Refinement against its full scoped manifest. A numerical average or a working subset cannot compensate for a failed gate. The implementation author’s checks, source review, automated runtime tests, visual review and independent acceptance are separate evidence categories.

Each result records the exact source revision, build/artifact hashes, dependency pins, OS/architecture, display scale, viewport, theme, input device, test profile, scenario parameters, commands/exit codes, reviewer identity and author/reviewer separation. A test status is NOT RUN until that scenario actually executes; writing this specification does not change any product’s acceptance state. Missing native Windows, browser, stylus or independent visual evidence stays explicitly missing.

### UX27-ACCEPT-002 — Input-to-model-to-persistence proof

**B/D.** For an interactive operation, verify four things together: the real routed control receives the intended input; the visible response matches the specified state; the canonical model changes correctly; and undo/save/reopen retain the intended result. Source declarations, a standalone component constructor, a menu label and a unit-test mock are insufficient proof of host integration.

For example, Enter must produce two actual BlockIDs, not simply two painted lines; a drag must change the stored parent/order while preserving identity; pen input must produce persisted stroke geometry; Save As must create a valid editable file at the selected destination. An inspection adapter may expose model IDs to a test runner, but developer diagnostics must not impersonate ordinary user UI.

### UX27-ACCEPT-003 — Fixtures and parameter coverage

**D/B.** Use disposable test profiles and clearly labelled synthetic test artifacts. Never mutate Jacob’s live board, account, browser profile or source merely to make a test convenient. Test at least an empty artifact, a typical mixed artifact, a large/virtualised artifact and meaningful failures for each owning domain.

Run applicable scenarios with mouse/keyboard and the supported pen/touch route; with representative light and dark themes; at 100%, 125%, 150% and 200% scaling; at narrow and wide viewports; and through both standalone and integrated entry points where specified. Use pairwise selection for routine regression runs and a documented broader matrix at integration/release; do not claim every combination was run from one representative case. Separate geometry/input failures from unavailable device/environment coverage.

### UX27-ACCEPT-004 — Independence and visual evidence

**B.** Independent app acceptance requires a reviewer other than the implementing agent to inspect the exact candidate and independently exercise representative workflows. Copying the implementing agent’s pass counts is not independent execution. A text-only agent can inspect source, accessibility trees and logs, and may capture screenshots for a capable reviewer; it cannot claim visual inspection of images it could not process.

Screenshots prove only what is visible at their capture time. Preserve interaction traces or short recordings for dynamic properties such as click-off dismissal, drag previews, scroll direction, caret behaviour and continuous ink. Reference mockups establish intended design, not successful implementation. Never compare screenshots from a different branch/build as though they accepted the current executable.

### UX27-ACCEPT-005 — Regression scope and large content

**B/D.** Each coherent change runs focused tests for its altered contracts and known rejection cases. Shared CUI input, theme, route, storage or command changes trigger impacted downstream journeys; unchanged exact-source evidence can be reused only with an explicit dependency-impact justification. Run broader integrated regressions before release instead of blocking every keystroke-sized change on every app’s entire suite.

Measure cold/warm load, pointer-to-preview latency, typing responsiveness, frame times, memory, save/reopen, deep navigation and full export at declared workload sizes. Preserve all content under resource pressure. A faster result obtained by omitting off-screen blocks, dropping strokes or silently truncating an export is a failure. Record observed numbers and environment, not guessed benchmark claims.

### UX27-ACCEPT-006 — Scenario catalogue is a floor

**B/D.** The companion acceptance catalogue provides concrete reproductions for the interactions clarified in this edition. Each scenario references the relevant UX27 requirements and begins NOT RUN. It supplements the full baseline/donor requirement manifest rather than replacing it. Add any missing donor workflow, security, collaboration, platform or accessibility scenario discovered during implementation.

A scenario passes only when all its stated expectations hold. A failed setup is BLOCKED with evidence, not PASS. A discovered defect is FAIL with exact reproduction. A prerequisite that genuinely does not apply requires a reason tied to the specification; absence of implementation is not a reason to mark NOT APPLICABLE.

## 15. Coverage, migration and explicit remaining source gaps

### UX27-COVERAGE-001 — What this expansion covers

This edition supplies detailed interaction contracts for shared CUI controls, motion, icons, focus, menus, scrolling and state; the Boards shell, hierarchy, block editor, dragging, ink, layout, tables, graphs and files; host/browser integration; Spaces/composer/models; Automations; Build; Write; Present; Canvas; Data; Files; and additional concrete workflows for the remaining app families.

The complete baseline text is retained in the source-preserving master. Requirements not repeated here remain binding, including mature donor functionality, APIs, security, permissions, performance, accessibility, collaboration, data ownership and platform parity. The additional rules do not imply a previously missing baseline capability is now implemented.

### UX27-COVERAGE-002 — Topology recovered; exact historic images not invented

The 25 October Boards decisions establish the top toolbar, bottom Add/user area, nested pages, top-left Boards identity, removal of the top-right AppFlowy badge, Add page/section popup and theme-independent layout. Those decisions are incorporated explicitly.

The original screenshot set and completed Boards design-review PDF from that discussion were not recovered as a complete visual reference pack in this run. Therefore new dimensions, spacing, animation durations and gesture thresholds are labelled D. They must not be represented as measured pixels or timings from those images. Preserve any accessible approved reference in the implementation’s source-authority register and reconcile differences without throwing away the owner’s explicit layout decisions.

### UX27-COVERAGE-003 — Adoption into the canonical specification

The connected Google Doc remains unchanged because no document-write operation was exposed during this task. To adopt this edition, use an authorised editor to incorporate the UX27 additions alongside their corresponding canonical sections, preserving the baseline’s native tables, images, links, styles and revision history. Do not replace the original structured document with a flattened text export merely to make the update easy.

The package includes a text master with insertion markers and a hash-verified baseline snapshot. Remove only its generated insertion regions to reconstruct the exact exported baseline bytes. The manifest records the actual preservation check. Native Google Docs formatting/embedded images are outside that text-byte claim; they remain in the original document.

Update source pins only through the existing documented revision process after an authorised adoption. The old pin and this edition’s hashes remain traceable; a new requirement document must not silently masquerade as the earlier pinned revision. No milestone status, runtime setting or worker permission is changed by creating this package.

### UX27-COVERAGE-004 — Implementation and defect provenance

The owner’s reports of broken Enter, dragging, pen input and unwanted Files/folder requirements are binding correction inputs, not claims that this task reproduced their code-level causes. Sampled recording frames independently support the visible observations in the evidence notes. The existing worker’s claims about routes, one-shot leases or engines remain hypotheses unless separately verified against source/runtime evidence.

Branch divergence, stale executables, incorrect route registration and event routing are possible investigation directions. Do not declare any of them the universal cause from screenshots alone. Preserve observed behaviour, reproduction steps, source findings and causal conclusions as separate fields.

## 16. Sources and provenance register

### UX27-SOURCES-001 — Primary product authority

1. **Existing V2 master:** https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit — native document read and full text export retrieved 27 October 2026; raw export SHA-256 is recorded in §0 and the package manifest. Contains the CUI, acceptance, donor, app and prior amendment baseline.
2. **Scalable Content & No Arbitrary Size Caps, 20 October:** https://docs.google.com/document/d/1B7XG1fuW7auRuzXMRZQeuq2dp59pqrS0jHckg9B-ol4/edit — retrieved during this task; preserved as a compatible source requirement, not a measured performance result.
3. **Direct owner amendments:** this conversation’s request for hyper-specific behaviour and its Enter/grip/canvas/pen corrections, optional `.9to1board` saving without Files connection or a required folder, and safe convergence of development onto main. Recent retrieved 21–26 October decisions are summarised in §0 and identified in individual clauses.
4. **Earlier owner context retained where applicable:** Model Groups/lazy responder loading, reasoning-effort display, Summary/Task/Research/Shopping/Assistants presentation. These clarify related sections; they do not grant new execution or spending permissions.
5. **Current recording:** `Screencast from 2026-10-27 12-48-26.webm`, approximately 240.4 seconds. The evidence folder contains selected captured frames and a timestamped observation note. The recording is a current-state example, not the approved design reference.

### UX27-SOURCES-002 — Narrow external behaviour references

The following official pages were consulted on 27 October 2026. Their role is limited to the short factual behaviours described here. All newly chosen geometry, timing, model transitions and detailed acceptance cases in this edition are 9to1 design requirements, not claims that Notion/AppFlowy use the same implementation or exact values.

- **Notion writing/editing basics:** https://www.notion.com/help/writing-and-editing-basics — block insertion and the distinction between opening a six-dot action menu and dragging its block.
- **Notion columns/headings/dividers:** https://www.notion.com/help/columns-headings-and-dividers — block movement, visible drop guidance and column organisation as user-facing reference concepts.
- **Notion keyboard shortcuts:** https://www.notion.com/help/keyboard-shortcuts — ordinary Enter versus a soft line break within a block, with context-specific editing shortcuts. This edition explicitly defines its own heading/code/table exceptions rather than guessing every donor edge case.
- **AppFlowy documentation:** https://docs.appflowy.io/docs — the donor documentation entry point. No complete running donor audit or exact animation measurement was performed here; selected-revision parity remains an implementation/acceptance obligation.
- **WAI-ARIA tree pattern:** https://www.w3.org/WAI/ARIA/apg/patterns/treeview/ — accessible hierarchy navigation and the distinction between focus, expansion and selection.
- **WAI-ARIA menu-button pattern:** https://www.w3.org/WAI/ARIA/apg/patterns/menu-button/ — semantic menu activation, keyboard operation and state exposure. Native CUI accessibility should expose equivalent semantics through its platform adapter, not pretend the native tree is HTML.
- **WCAG contrast explanation:** https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html — ordinary text contrast of 4.5:1 and large text 3:1 where applicable. This is one criterion, not a claim of complete WCAG conformance.
- **WCAG interaction-animation explanation:** https://www.w3.org/WAI/WCAG22/Understanding/animation-from-interactions.html — disabling nonessential interaction-triggered motion. The listed 9to1 timing tokens are new design defaults, not W3C-prescribed durations.

External product names describe interoperability/donor behaviour, not permission to copy proprietary source, protected assets, branding, account systems or inaccessible services. Preserve the original licensing and provenance rules.

## 17. Development-source convergence and real-build identity

### UX27-MAIN-001 — One authoritative integration line

**O/B.** For development of 9to1 itself, converge legitimate unique work into the existing main branch and establish one documented canonical build/test/launch checkout. Inventory actual local and authorised remote branches, worktrees, pending integration, unpushed commits, dirty/untracked files and their current owners before integration. Do not infer branch value from its name or age.

Preserve recoverable original tips and uncommitted work in an authorised private archive/bundle. Account for every candidate as integrated, duplicate, superseded, incompatible with newer explicit requirements or unresolved. Do not bulk-merge obsolete interfaces merely to reduce the branch count. Do not discard donor functionality to resolve a conflict more quickly.

### UX27-MAIN-002 — Non-destructive integration and retirement

**O/B.** Integrate in dependency order, preserving source authority, data formats, permissions and acceptance evidence. Run focused tests at coherent checkpoints and integrated user journeys on the resulting main build. Do not force-push, bypass branch protections, reset away work, delete an active worktree or overwrite another worker’s files. An unresolved active owner must be reconciled, not presumed absent.

Retire only fully accounted-for inactive first-party development branch references through normal authorised operations, retaining recoverability. Do not flatten third-party/upstream/vendor histories or unrelated repositories into main. A branch left temporarily for unresolved work is an explicit exception, not grounds to delete it to manufacture success.

### UX27-MAIN-003 — Verify what the user actually launches

**O/B.** Record final local main, published remote main where authorised, canonical checkout, build inputs, packaged dependencies and executable/artifact hashes separately. Verify the normal launcher opens that exact candidate and the intended host routes. A successful component test from another checkout cannot prove a visible fix in the running app.

Consolidated source is not product acceptance. Retest the owner’s actual launch, Present, Boards, browser and persistence failures against the resulting build, preserving every unmet requirement. Existing paused sessions, the separate laptop connection, models, spending limits and schedules remain unchanged unless separately authorised. Expired historical deadlines do not permit destruction or fabricated completion.
