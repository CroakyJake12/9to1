# Interaction records

Source: https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc?tab=t.yh4mk9q4cw20

<a id="h.2jhsftnd8kh"></a>

# Editor interaction records

These records specify app-local controls for all 47 assigned Atlas screens. Atlas labels and later refinement scripts establish the design; the complete interaction semantics are adopted target specification decisions, not claims of executed product behavior. Shared IX-BASE patterns apply only to common gestures and focus mechanics. Each record defines the actual command and canonical target. Static fixture rows, counts, names, selected values and rendered results are illustrative. Hosted shell, shared AI controls and common account/navigation controls inherit the shared interaction chapter. New target menus named here preserve existing capability and must use the owning app's typed operation, revision checks and undo contract. The screen register maps all 2,147 extracted control occurrences across the 47 assigned Atlas screens, including app commands moved into the host header. It also adds the designed fields, objects and handles rendered as div/SVG elements and therefore absent from the button/input extraction. Inherited IX-BASE, IX-HOST and IX-SPACES references are defined in the shared/Spaces interaction volumes; the command-specific target remains stated here. Fixture names, counts, timestamps and revision numbers are illustrative. Acceptance scenarios are requirements, not claims of executed tests.

Authority: [Master specification](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk); [pinned Interface Atlas v3.3](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html). Owner additions and the detailed gestures specified here are target requirements; static Atlas markup is design evidence, not proof of native execution. Related application/runtime requirements in the master remain in force.

Reading a record: Primary covers left click and activation; Secondary covers right click and contextual actions; Drag covers sources, targets and cancellation; Keyboard and touch supplies alternatives; State and integration defines scope, persistence and failure. Shared IX-BASE patterns apply unless the control record explicitly refines them. Acceptance describes required verification, not tests already performed.

<a id="h.ohlhrpxk47hb"></a>

### IX-EDITORS-001 — Start navigation and return to existing work

Controls: Start; Recent; Favourites; Shared; Templates; Home

Atlas surfaces: boards-launch, write-launch, present-launch, canvas-launch

Primary: Start opens the launch view; Recent, Favourites and Shared filter the authorised artifact list; Templates opens the app's template catalogue. Home opens shared Home. Single click changes only navigation/filter, retaining any live document session. Double click has no additional effect.

Secondary: No custom menu on navigation buttons; secondary click does not create, open or delete an artifact.

Drag: Navigation buttons are not drag sources or drop destinations. Scrolling the list does not reorder artifacts.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)/009/010. Enter/Space activates the focused destination; touch taps behave identically. Escape leaves a filter/search overlay before returning to the previous document.

State: Selected navigation is explicit. Loading, genuinely empty, denied Shared scope and failed listing differ; Retry reloads the same scope.

Integration and persistence: Filtering preserves artifact identity, unsaved editor state and list reading anchor. Fixture names and counts are not seeded production content.

Acceptance: Type an unsaved paragraph, visit Start then Recent, and reopen the same DocumentID with its dirty buffer intact.

Basis: Atlas launch() and LAUNCH-001–006; expanded_editors.md app lifecycle. Atlas shows controls; target semantics below.

<a id="h.elaoj5oloh05"></a>

### IX-EDITORS-002 — Blank creation and template instantiation

Controls: New board; Blank document; Blank presentation; Blank canvas; Project workspace; Study notes; Weekly plan; Essay; Research report; Project proposal; Story; Research update; Pitch; Ideas map; Blank grid; Storyboard

Atlas surfaces: boards-launch, write-launch, present-launch, canvas-launch

Primary: Click the blank tile to create one recoverable native draft and focus its title/content. Click a template tile to inspect its description and instantiate that selected template; inspection alone allocates nothing. Template content becomes an independent authored instance with remapped owned IDs, preserving explicit external references.

Secondary: Template menu: Preview opens the template without creating; Create from template creates one instance; Details shows requirements/source. Blank buttons have no custom menu.

Drag: Tiles are not draggable. Dropping files on a tile does not silently convert creation into import.

Keyboard and touch: Enter/Space opens the selected tile; preview Create commits once, Escape/Cancel closes without a draft. Touch uses the same preview and Create controls.

State: Creating disables repeat admission for the same operation. Missing template dependency or denied destination leaves a retryable preview; local blank creation needs no model/account.

Integration and persistence: Write blank defaults Continuous, Present creates a deck/initial slide, Canvas creates its native scene, Boards creates a Board/Native page. Opening does not overwrite existing work.

Acceptance: Activate a template twice during slow creation and verify one artifact; cancel another preview and verify zero artifacts.

Basis: Atlas launch() and LAUNCH-001–006; expanded_editors.md app lifecycle. Atlas shows controls; target semantics below.

<a id="h.hn1wj2hsao8v"></a>

### IX-EDITORS-003 — Open and Import resolution

Controls: Open / Import

Atlas surfaces: boards-launch, write-launch, present-launch, canvas-launch

Primary: Click opens a chooser with explicit Open and Import/Convert outcomes. Open resolves an existing native artifact/revision. Import inspects source format, destination and conversion losses before creating a native result. A cancelled chooser leaves the current launch/editor state unchanged.

Secondary: No custom menu on the button. Source-file context actions belong to the chooser/Files owner and cannot bypass import validation.

Drag: A file drop on the launch drop area enters the same inspection flow; show accepted formats and proposed destination. Reject unsupported files visibly, and never overwrite the dragged original.

Keyboard and touch: Enter opens the chooser; standard file selection and Escape cancel apply. Touch uses Browse; drag is never the sole route.

State: Distinguish unknown format, parser failure, missing source, denied access and supported lossy conversion. Preserve a failed import's options for retry without claiming a ready document.

Integration and persistence: Native open reuses canonical identity; imported copies receive new identity/provenance. Large import is cancellable before final commit; already committed results remain available.

Acceptance: Import a document with an unsupported effect, inspect loss reporting, cancel once, then commit and verify the original bytes are unchanged.

Basis: Atlas launch() and LAUNCH-001–006; expanded_editors.md app lifecycle. Atlas shows controls; target semantics below.

<a id="h.21er78igqlc"></a>

### IX-EDITORS-004 — Recent rows, search and missing-file recovery

Controls: Find your work; Recent artifact row; Name; Location; Status; Last opened

Atlas surfaces: boards-launch, write-launch, present-launch, canvas-launch

Primary: Typing filters authorised recent metadata; clearing restores the previous scope. Single click on a recent row opens its exact artifact; separate selection controls support multi-selection only when a bulk command exists. Double click is coalesced with the first open. Status text inspects actual save/sync details without opening a different file.

Secondary: Row menu: Open, Reveal location, Favourite/Unfavourite, Remove from Recent, and Locate for a missing target. Remove from Recent removes only the locator. Unavailable source actions carry a reason.

Drag: Recent rows do not reorder source files. A drag may export an explicit canonical artifact reference to an accepting owner; it never moves storage by default.

Keyboard and touch: Search uses [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1); arrows focus rows and Enter opens. Context-menu key/long press exposes the same commands; Escape clears the active popup before search text.

State: No-results differs from unavailable index. A moved file offers Locate; revoked access cannot expose cached content. Concurrent opens resolve one existing session.

Integration and persistence: Search and sort are personal view state; file rename/location repair preserves identity and undo history. Shared shell handles new-tab/window requests through the same target.

Acceptance: Remove a recent entry and verify its file remains; locate a renamed native file and verify no duplicate document is created.

Basis: Atlas launch() and LAUNCH-001–006; expanded_editors.md app lifecycle. Atlas shows controls; target semantics below.

<a id="h.ulr2f1647tsq"></a>

### IX-BOARD-001 — Board identity and file-menu commands

Controls: Board icon; Design workspace — board file menu; New board; Open…; Save; Save as…; Rename; Export…; Version history; Switch board…

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, mobile-boards

Primary: Board icon opens icon selection; choosing a value updates Board metadata. Board name opens the file menu, never inline Rename. New board creates a separate recoverable draft; Open resolves a native file; Save commits to the existing destination or opens Save As when unnamed; Save as changes the active locator while retaining BoardID. Rename edits metadata/name explicitly. Export creates derived output; Version history inspects revisions; Switch board selects another canonical Board.

Secondary: Board name secondary click opens the same file menu. Icon menu exposes Change icon and Reset icon. Menu items have no secondary execution. Export/History inspect before any destructive restore.

Drag: Identity/icon/menu controls are not drag sources. Menu scrolling does not move the board tree.

Keyboard and touch: [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk); arrows traverse enabled commands, Enter activates, Escape returns to board-name trigger. Rename Enter commits valid text, Escape restores. Touch uses the same visible trigger.

State: Save status remains separate from cloud sync; unavailable destination yields recovery. Renaming rejects invalid storage names without dropping the draft. History never fabricates revisions.

Integration and persistence: Menu close preserves page/caret. Save As preserves logical identity; explicit Save a copy creates a new BoardID. Switching preserves dirty board sessions and contextual AI target atomically.

Acceptance: Click the name twice, confirm no rename; cancel Save As, then switch back and verify the unnamed draft survives.

Basis: Atlas boardSidebar()/board(), v3.1 board overlay and final atlas-boards-refinement-code; expanded_editors.md §6.1. Target operation/gesture details adopted here.

<a id="h.tuvi3k6swmtq"></a>

### IX-BOARD-002 — Board Undo, Redo and save-state inspection

Controls: Undo; Redo; Saved locally; Saving…; Save failed · draft retained

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, mobile-boards

Primary: Undo reverses the latest eligible local edit transaction in this Board; Redo reapplies its matching transaction. A stroke, block move or style change is one transaction. Click save status opens actual durable destination/revision and sync details; it does not initiate an unrelated export.

Secondary: Undo/Redo expose no custom menu; secondary click cannot undo. Save-status details may expose Retry or Save a copy only for the corresponding failure.

Drag: Not draggable. Text selection in an expanded error/detail view does not invoke an action.

Keyboard and touch: Ctrl/Cmd+Z and redo shortcut act on the focused embedded editor's shared transaction scope before Board fallback. Button activation uses [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6). Touch keeps both history buttons reachable.

State: Disable Undo/Redo when their stacks have no applicable transaction or mutation is denied. A remote conflicting edit requires revalidation rather than reversing another author's work.

Integration and persistence: History preserves selected IDs/caret where possible, invalidates the local saved revision and reschedules persistence. Autosave itself is not a user-edit undo entry.

Acceptance: Draw one stroke, change paragraph style, undo twice and verify exactly the style and stroke are removed, without reverting unrelated collaborator changes.

Basis: Atlas boardSidebar()/board(), v3.1 board overlay and final atlas-boards-refinement-code; expanded_editors.md §6.1. Target operation/gesture details adopted here.

<a id="h.uwchdd6hcdt1"></a>

### IX-BOARD-003 — Page search, hierarchy and navigation

Controls: Search pages; Ctrl P; Product; Start here; Design system; Themes; Components; Research; Weekly plan; Project chat; Tree disclosure chevrons

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, mobile-boards

Primary: Page search opens/filters by authorised titles/content and reveals exact PageIDs. Clicking a page row opens it; clicking a section chevron expands/collapses without changing the active page. A section label selects its organisational target. Double click on a page does not rename; Rename is explicit. Chat/Write/Canvas pages open their canonical owner surface.

Secondary: Tree menu: Open, Rename, New child page, New section where valid, Move to…, Duplicate, Copy page link and Move to Trash. Commands target the invoked row; duplicate remaps owned IDs and retains external references.

Drag: Drag the row grip after [IX-BASE-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.oj2yi5h8kpkm) threshold. Before/after lines and inside-container highlight identify destination; hover expands, edges auto-scroll. Drop moves the captured subtree once. Reject self/descendant cycles or denied destination; Escape restores order.

Keyboard and touch: Up/Down visible rows; Right expand/enter; Left collapse/parent; Home/End endpoints; Enter open; F2 Rename. Touch row tap opens; disclosure remains distinct; Move to supplies non-drag relocation.

State: Selection survives filtering/collapse. Missing page opens a scoped recovery state. Remote reorder/reparent revalidates target IDs before drop.

Integration and persistence: Page navigation preserves draft and per-page scroll; selecting a different page retargets toolbar/properties/AI together. Removing a linked page does not delete its external artifact.

Acceptance: Drag a section toward its descendant and reject it; collapse the active page's ancestor, then Reveal active page without changing PageID.

Basis: Atlas boardSidebar()/board(), v3.1 board overlay and final atlas-boards-refinement-code; expanded_editors.md §6.1. Target operation/gesture details adopted here.

<a id="h.d9dz8edlb2vg"></a>

### IX-BOARD-004 — Navigation collapse and mobile page drawer

Controls: Collapse navigation; Open pages; Close pages; Pages drawer; Drawer scrim; New page; Add a page or section

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, mobile-boards

Primary: Collapse hides the desktop page tree and leaves a restore control. On narrow screens Open pages reveals the named drawer with the same hierarchy; Close/scrim dismisses it. Selecting a page opens that PageID and closes the drawer. Footer New page opens creation rather than inserting a block.

Secondary: No custom menu on collapse/close/scrim. Tree rows retain [IX-BOARD-003](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.uwchdd6hcdt1) menus inside the drawer; long press does not dismiss or navigate.

Drag: Drawer body scrolls independently; a tree drag uses its explicit grip and edge scrolling. Scrim and header are not page drop targets. Sliding a finger on a row before a drag hold scrolls normally.

Keyboard and touch: Drawer traps focus while open; Escape returns to initiating Open pages control, preserving underlying caret. Touch targets follow [IX-BASE-010](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.b38hk5mijafm) and account/footer actions stay reachable.

State: Collapsed/open state is view state. Resize transfers focus to an equivalent visible control; it cannot strand focus in hidden navigation.

Integration and persistence: Opening/closing drawer preserves active page, page scroll, ink tool and AI draft. Desktop width adaptation changes composition, not tree ownership or order.

Acceptance: Open mobile drawer over a dirty page, scroll, dismiss by scrim and reopen; page draft and prior drawer position remain intact.

Basis: Atlas boardSidebar()/board(), v3.1 board overlay and final atlas-boards-refinement-code; expanded_editors.md §6.1. Target operation/gesture details adopted here.

<a id="h.csg2xhmuhryk"></a>

### IX-BOARD-005 — New page destination and type creation

Controls: New page; Add a page or section; Destination; Native; Write; Canvas; Chat; Scoped Chat; New section; Close Add

Atlas surfaces: boards-add, boards-editor, boards-typing, boards-ink, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, mobile-boards

Primary: Footer trigger opens a destination preview. Destination selects the actual section/page/container. Native creates a native page; Write/Canvas create owner-backed artifact pages; Chat creates its conversation; Scoped Chat creates a conversation with explicit source binding; New section creates organisation only. A type click commits exactly one validated creation, reveals it and focuses title/editor. Close Add cancels.

Secondary: No custom secondary action on type choices; right click does not create. Destination tree uses navigation disclosure without changing the proposed destination until selected.

Drag: Popup rows are not draggable. Existing page references dropped onto the tree follow Move/Link semantics, never masquerade as a type selection.

Keyboard and touch: Arrows/Tab traverse; Enter activates the focused type, Escape restores footer focus. Touch sheet keeps destination and all six choices reachable.

State: Creating state coalesces double activation. Denied/missing destination preserves selection and lets the user choose another. Missing owner engine yields explicit Unavailable, not a fake native page.

Integration and persistence: Opening allocates nothing. Successful creation has one tree/owner transaction or explicit partial recovery receipt; AI/source settings obey independent scope policy.

Acceptance: Cancel after changing destination: zero entities. Create Scoped Chat twice rapidly: one page and one canonical conversation with the chosen source scope.

Basis: Atlas boardSidebar()/board(), v3.1 board overlay and final atlas-boards-refinement-code; expanded_editors.md §6.1. Target operation/gesture details adopted here.

<a id="h.9nl7ksetn9jp"></a>

### IX-BOARD-006 — Compact toolbar, Edit/View and independent layout settings

Controls: Add; Add content to current page; Drawing; Format; Formatting controls; Edit; View; Collaborate; Page; Page settings; Locked layout; Snap on; Properties

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, mobile-boards

Primary: Add opens the content catalogue at the captured range; Drawing selects the drawing context; Format opens contextual text or ink controls; Page opens page settings. Edit enables permitted local mutation; View prevents it. Locked layout prevents spatial repositioning but leaves flow editing and page ink available in Edit. Snap toggles transform snapping. Properties opens the selected object's inspector. Collaborate uses the scoped collaborator family.

Secondary: No custom menu on toolbar triggers. Right click in the document retains its own object/block menu. Page settings may expose page options through named commands rather than an extra permanent toolbar.

Drag: Toolbar controls do not drag. A pointer crossing the toolbar during a captured ink/move gesture does not activate it; gesture completion/cancel remains with the origin.

Keyboard and touch: Format/Page use [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk); ArrowDown opens and focuses first control; Escape closes nested menu then parent. Touch opening never moves the caret.

State: Edit/View, layout lock, Snap and AI access are independent states. Disable denied mutation controls with a reason; changing View during a gesture cancels its uncommitted preview.

Integration and persistence: Only one Format/Page popup opens; later refinement moves original controls, preserving their actions. Closing a popup restores captured editor selection. No second Boards AI button is introduced.

Acceptance: Set Edit + Locked + Snap off, draw ink successfully, then View and verify typing/ink cannot mutate despite AI Take actions being separately configured.

Basis: Atlas boardSidebar()/board(), v3.1 board overlay and final atlas-boards-refinement-code; expanded_editors.md §6.1. Target operation/gesture details adopted here.

<a id="h.eipf3qfk5dm9"></a>

### IX-TEXT-001 — Caret, text range and structural typing

Controls: Paragraph text; Rich text surface; Text caret; Empty paragraph; Page title

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, cards-edit

Primary: Click positions a logical caret; double click selects a word; triple click selects the paragraph. Shift-click extends from the fixed anchor. Typing replaces only the selected range. Board Enter splits a paragraph into siblings; Shift+Enter inserts a soft break; heading continuation becomes body text. Backspace at a compatible boundary merges once. Card Front and Back each retain their own rich-text range.

Secondary: Selected-text menu exposes Cut, Copy, Paste, Paste plain text, Link and applicable formatting. A noncollapsed range remains the target when opening the menu; spelling suggestions replace only their identified word.

Drag: Drag in text extends a range, never moves its block or paints. Dragging a selected range requires an explicit text-move gesture after selection; external drop previews insertion and accepted semantic/plain format.

Keyboard and touch: Arrow/word/Home/End navigation follows the editor; IME Enter commits composition before structural handling. Touch handles extend selection. Escape closes editing popups before clearing selection.

State: Read-only keeps selection/copy and blocks input. Invalid clipboard format leaves the range unchanged. Concurrent edits transform anchors or report a conflict; no stale offset overwrite.

Integration and persistence: Use grapheme-safe deletion and preserve unaffected marks. Input groups into local undo; autosave/reflow cannot steal caret or create empty blocks.

Acceptance: Select a marked phrase, open Format, cancel, then type with IME: only the intended range changes and no extra paragraph appears.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.3rf8vtrh1rhx"></a>

### IX-TEXT-002 — Paragraph style, font and font-size controls

Controls: Paragraph; Montserrat; Style; Font; Font size; Font size, 14 pixels; Decrease font size; Increase font size; 14px; 18; 32; Weight; Regular; Medium; Bold weight

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, cards-edit

Primary: Style opens applicable named styles; choosing one changes selected paragraphs and derived outline semantics. Font changes selected runs or future insertion marks. Size opens editable numeric units; minus/plus applies one declared size step. Selection containing different values displays Mixed. Live font/size previews remain provisional until choice/Enter; Escape restores the captured values. Weight chooses an available family weight for the selected runs; unavailable weights are labelled and never silently substituted without showing the fallback. A weight choice and the Bold toggle resolve to the same underlying style rather than accumulating contradictory flags.

Secondary: No custom menu on toolbar triggers. The font list may expose installed/font-source details; it never installs a font merely by highlighting it.

Drag: Controls and menu rows are not draggable. Numeric selection edits the field rather than scrubbing page geometry; slider previews, if exposed, use [IX-BASE-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.5oiet5rdqq3c) with one commit.

Keyboard and touch: Arrow keys navigate menus; typing searches family/style; Enter applies and returns to the saved caret/range. Numeric input uses [IX-BASE-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xj3xapubjjfz). Touch uses the same fields without hover-only options.

State: Reject nonnumeric/unsupported size or unavailable font with a retained editable value. Show actual fallback separately from requested font; do not falsely report successful loading.

Integration and persistence: Formatting updates selected content once and preserves object/page geometry except resulting text reflow. Named style updates and local overrides remain distinct; menu close alone writes nothing.

Acceptance: Select mixed-font text, choose one font, undo, and verify exact marks; enter an invalid size and confirm selection and previous value survive.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.fe57uq4vapk2"></a>

### IX-TEXT-003 — Inline emphasis and casing

Controls: Bold; Italic; Underline; Strikethrough; Text casing; Aa

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, cards-edit

Primary: Each emphasis button toggles only its named mark on the active range, or sets the insertion mark at a caret. Mixed selection is visibly mixed; activation applies the mark uniformly, second activation removes it. Text casing opens Sentence case, lowercase, UPPERCASE and Capitalise words as explicit text transformations, preserving semantic IDs and unaffected formatting.

Secondary: No custom secondary menu; right click does not toggle emphasis or transform text. Document context formatting invokes the same operations.

Drag: Not draggable; pressing then leaving without activation does not change text. Existing text selection remains captured while the toolbar takes focus.

Keyboard and touch: Ctrl/Cmd+B/I/U dispatch to the active editor only. Enter/Space activates focused controls. Touch taps use selected/mixed states without relying on colour alone.

State: Disabled for non-text targets or View mode. Missing glyph/font capability is a warning, not an excuse to change requested content. Casing respects locale and preserves the original for undo.

Integration and persistence: Each command is one owner undo transaction. Toggling at an empty caret changes future insertion state without rewriting the paragraph. Suggested-edit mode records a proposal instead of a committed text mutation.

Acceptance: Bold a mixed selection, undo, then uppercase a word containing non-ASCII letters and verify one reversible change and intact surrounding marks.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.7jexu5pdvs5h"></a>

### IX-TEXT-004 — Text colour and highlight

Controls: Text colour; Text highlight; Highlight; Colour & style

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, cards-edit

Primary: Open the relevant picker and apply an authored foreground colour or text highlight to the captured range. Clear highlight removes highlight only; default text colour clears the local colour override. Custom colour entry validates the chosen value and previews the exact text target. The block Colour & style submenu can separately address the block background.

Secondary: No custom secondary action on swatches or picker trigger. Document context Colour & style names whether text, highlight or block background is being edited.

Drag: Picker controls are not document drag sources. Hue/alpha drags preview within the picker; release commits once, Escape reverts, and dragging outside cannot paint the page.

Keyboard and touch: Swatches use arrow navigation and Enter; text/hex entry and labelled opacity input provide keyboard equivalents. Touch exposes current/custom/clear options visibly.

State: Mixed colour is explicit; unavailable editing disables Apply. Alpha or contrast preview does not silently replace the authored value with a theme colour.

Integration and persistence: Theme/accent changes affect chrome, not saved text/highlight. Reopening the picker reads actual range state; choosing the same value makes no redundant edit.

Acceptance: Highlight a range, change app theme, reopen and confirm the authored value; Clear highlight leaves foreground and block background unchanged.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.q3bcd39sbl1i"></a>

### IX-TEXT-005 — Paragraph alignment and spacing

Controls: Alignment; Left; Centre; Right; Justify; AlignLeft; AlignCenter; Paragraph spacing; Spacing before; Spacing after; Line spacing

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, cards-edit

Primary: Alignment opens Left, Centre, Right and Justify; choosing a value changes the touched paragraphs, not the whole Board/slide. Direct AlignLeft/AlignCenter buttons execute their named paragraph alignment. Spacing opens before/after and line-height fields; commit validates units and applies only to selected paragraphs. Properties and Format show the same values.

Secondary: No custom secondary action on alignment choices. Paragraph context may open the same spacing fields without relocating the caret.

Drag: Not draggable. Selecting field text or scrolling the properties pane does not select document blocks. Numeric changes preview within the captured paragraph scope.

Keyboard and touch: Menus use [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk); fields use [IX-BASE-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xj3xapubjjfz), Enter commits and Escape restores. Tab order follows before, after, line spacing. Touch exposes labels and units.

State: Mixed paragraph values remain mixed until set. Reject negative unsupported spacing or invalid line units without committing other invalid fields. View mode retains inspectable values.

Integration and persistence: Alignment is paragraph formatting; object alignment/slide positioning are separate commands. Reflow updates rendering and caret anchors while preserving page-coordinate Board ink.

Acceptance: Align one of two paragraphs, adjust spacing and cancel an invalid value; verify only the chosen paragraph changes and Board ink stays fixed.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.8psqfdw3e3qx"></a>

### IX-TEXT-006 — Lists, checklist items and indentation

Controls: List options; Bullets; Numbered list; Checklist; No list; List; ListOrdered; ListTodo; Indentation options; Checklist checkbox

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: List options selects Bullets, Numbered list, Checklist or No list for selected paragraphs. Direct List/ListOrdered/ListTodo invoke those types. No list removes list semantics while preserving content. Indentation changes list depth or paragraph indentation explicitly. Checklist checkbox toggles that item's completion; clicking its label edits/selects text, not the checkbox.

Secondary: List item menu adds Increase/Decrease indent, Restart/Continue numbering for numbered lists, and Convert list type. Checkbox secondary click does not mark complete.

Drag: Checkboxes and list-menu choices are not draggable. Block-grip reorder moves the item/subtree using explicit boundaries; text drag remains selection.

Keyboard and touch: Space toggles a focused checkbox. Enter continues the list with a new unchecked item; empty nested Enter outdents once, empty root Enter exits. Tab/Shift+Tab indent only in list context, with accessible menu equivalents.

State: Cannot indent under an invalid previous sibling. Mixed list types display Mixed. View mode prevents checklist mutation unless the owner declares a separate permitted interactive response.

Integration and persistence: Numbering recomputes from stable item/list identity; checking a task does not create a Planner task or Study evidence. Conversion and toggle are separately undoable.

Acceptance: Create nested checklist items, complete one, exit an empty nested item and undo; preserve completion flags and exact hierarchy.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.glshg7sod6k7"></a>

### IX-TEXT-007 — Inline links and format overflow

Controls: Link; More formatting; More commands

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, cards-edit

Primary: Link opens display text and destination for the captured range. Apply inserts/updates one link; Remove link keeps the visible text. In Edit, single click selects/places caret and a labelled Open link action follows the destination; View can activate directly. More formatting/commands discloses the same applicable registered commands omitted for width, retaining their precise targets.

Secondary: Link context menu: Open link, Copy link address, Edit link and Remove link, with mutation entries disabled in View. Right click itself never navigates.

Drag: Dragging link text follows text selection/move semantics. Drag-out exports an explicit URI/text payload; drop into the document previews insertion, never runs the URI.

Keyboard and touch: Dialog fields use normal text selection; Enter Apply, Escape Cancel. Keyboard/long-press context actions provide navigation without relying on modifier-click; IME input does not activate the link.

State: Reject malformed or disallowed schemes and show inaccessible target separately. A stale range requires refreshed target before Apply.

Integration and persistence: Changing link destination does not change source artifact ownership or grant access. Overflow is presentation only; commands remain discoverable and carry the same undo semantics.

Acceptance: Edit link text, cancel destination replacement, then Remove link and verify the words remain and undo restores the exact destination.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.f2vu9pn5oc1e"></a>

### IX-BOARD-007 — Inline insertion boundary and block catalogue

Controls: Insert at this boundary; Inline +; Slash command; Search blocks; Text; Heading; To-do list; Callout; Two columns; Table; Image; 9to1 artifact

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties

Primary: Inline +, Add and an eligible slash open one catalogue at a captured BlockID/range/boundary. Search filters without insertion. Text/Heading create the named text block; To-do list creates an unchecked item; Callout creates its text/icon container; Two columns creates two editable containers; Table starts a dimensions/schema choice; Image opens an asset choice; 9to1 artifact selects a real owner reference. Selection commits once and focuses the new block.

Secondary: No custom menu on catalogue rows. Secondary click outside the popup dismisses or opens the explicit block menu without inserting the highlighted type.

Drag: Catalogue rows are not drag sources in this contract. File/image drops onto a valid insertion boundary preview an asset block; rejected types leave the boundary/content unchanged.

Keyboard and touch: Up/Down chooses, Enter commits, Escape preserves typed slash/text and restores caret. Touch plus is reachable without hover; the catalogue can scroll without moving the page.

State: Highlight is not commitment. Validate permissions/dependencies before creating. Cancelling an asset chooser restores the pending insertion context without an empty phantom block.

Integration and persistence: Replace only the eligible command token/empty block or insert at the chosen boundary. Page creation uses [IX-BOARD-005](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.csg2xhmuhryk), not this catalogue. One insert has one undo transaction.

Acceptance: Type a slash inside a URL and confirm no catalogue; cancel Image selection, then insert a heading and verify unrelated paragraph text survives.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.7pktgmlqhpfe"></a>

### IX-BOARD-008 — Block grip, movement and full block menu

Controls: Block grip; Turn into; Colour & style; Move up; Move down; Move to…; Duplicate; Copy block link; Delete block

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties

Primary: Grip single click selects its BlockID; explicit menu affordance opens actions. Turn into converts the selected block through a compatible type preview; Colour & style opens its styling scope; Move up/down relocates one sibling position; Move to chooses a valid container; Duplicate remaps owned descendants; Copy block link copies the stable target; Delete block moves the addressed content to recoverable deletion.

Secondary: Right click the grip/selected block opens this menu without changing the existing multi-selection unless the invoked block lies outside it. Commands report exact single/multiple targets; Delete does not expand scope to unrelated descendants/references.

Drag: Grip drag begins after [IX-BASE-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.oj2yi5h8kpkm) threshold, suppresses click, captures selected IDs and previews before/after/inside/column destinations. Auto-scroll preserves IDs; Escape/rejected drop restores positions. Reject ancestor cycles and duplicate parent-plus-child movement.

Keyboard and touch: Context-menu key opens; arrows/Enter choose; Escape restores block focus. Move up/down/Move to supply keyboard/touch alternatives. Delete acts only when block selection, not a text caret, owns focus.

State: Boundary moves are disabled; incompatible conversion explains retained/lost structure before commit. Concurrent deletion/reparenting revalidates or cancels the move.

Integration and persistence: Moves retain IDs; duplicate creates owned IDs; external artifact deletion removes the reference only. Each committed operation is one undo unit; menu opening/hover is read-only.

Acceptance: Select a parent and child, drag once and verify one subtree move; copy its link, duplicate it and confirm original link still resolves to the original.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.8ydf1dv7ooqb"></a>

### IX-BOARD-009 — Flow columns, free placement and resize handles

Controls: Two-column divider; Positioned block border; Move handle; Resize handles; Return to flow; Locked layout

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties

Primary: Click a positioned block border selects geometry; click its content edits through the owner. Resize handles change the captured object's dimensions; a column divider changes sibling proportions while preserving readable constraints. Explicit Free placement converts a chosen block into document coordinates; Return to flow selects a valid boundary and previews the structural move.

Secondary: Geometry menu exposes Move, Resize, Return to flow, Lock/Unlock selection and applicable layer ordering. Content context menus retain their own semantics; secondary click does not relocate objects.

Drag: Drag the dedicated geometry/divider handle after threshold. Snap displays only active guides; Alt temporarily bypasses snap; Shift constrains axis/proportions for the active transform. Preview is transient; release commits once; Escape restores geometry. Reject size/location constraints visibly.

Keyboard and touch: Arrow nudge operates on geometry selection, not text; Shift uses a declared larger step. Numeric Properties and Move to flow provide precise keyboard/touch alternatives.

State: Locked layout rejects position/size drags while still allowing flow text/order and ink in Edit. A lost pointer or View transition cancels unfinished transforms.

Integration and persistence: Text reflow and page pan/zoom do not rewrite document coordinates. Resizing an embedded owner changes its viewport, not its canonical content. Column transactions preserve descendant IDs.

Acceptance: Resize a column, lock layout, attempt spatial move, then edit flow text and draw; only the explicitly allowed operations change the Board.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.dwxauklok2ep"></a>

### IX-BOARD-010 — Page drawing tools and stroke settings

Controls: Drawing; Text; Pen; Highlighter; Eraser; Select ink; 3 px; Ink colour; 100%

Atlas surfaces: boards-ink, boards-editor, boards-typing, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties

Primary: Drawing opens ink context; Pen and Highlighter select their respective stroke tools; Text returns to text editing at the retained caret. Width, Ink colour and opacity set future stroke properties, or explicitly modify selected strokes. Eraser selects its declared whole-stroke/segment mode. Select ink chooses lasso selection; tool selection alone creates no content.

Secondary: No custom menu on tool buttons. Stroke context after Select ink exposes Duplicate, Delete, Colour, Width, Opacity and ordering for that selected ink; it does not open a paragraph menu.

Drag: Pen/highlighter press-drag samples one stroke in page coordinates with capture outside the viewport. Eraser gesture previews removed geometry; lasso closes an explicit selection boundary. Settings sliders use [IX-BASE-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.5oiet5rdqq3c). Escape/cancel leaves no phantom stroke; touch pans unless explicitly drawing, with stylus palm rejection when available.

Keyboard and touch: Numeric width/opacity and colour controls are keyboard reachable. Delete removes selected ink; undo reverses one completed stroke/erase gesture. Tool shortcuts yield to active text/IME input.

State: Edit + Locked allows ink; View blocks it. Missing pressure uses fixed width. Unavailable segment erasure is named, never silently treated as raster flattening.

Integration and persistence: One shared viewport transform drives hit testing/rendering. Preset changes preserve previous strokes unless selected edits are explicit. Theme changes preserve authored ink colours.

Acceptance: Draw while locked, pan/zoom, erase a segment and undo; verify editable geometry and original page coordinates, not a raster screenshot.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.dlstnwclkplg"></a>

### IX-BOARD-011 — Selected ink transform and document-space hit testing

Controls: Ink lasso; Selected stroke handles; Page ink layer

Atlas surfaces: boards-ink, boards-editor, boards-context

Primary: Lasso in Select ink captures intersecting strokes under the chosen selection rule; click a stroke selects its stable identity. Shift adds/removes strokes. Click empty page clears ink selection without changing the text caret; double click has no text-edit behavior for a stroke.

Secondary: Selected-ink menu: Copy, Duplicate, Delete, Group/Ungroup where meaningful, stroke properties and ordering. Paste inserts editable strokes with new IDs at a visible location.

Drag: Drag inside selected bounds moves ink only; handles resize/rotate around a visible pivot. Snap applies to transformation, not handwriting. Page-ink locking follows the declared ink selection permission; Escape reverts the full transform.

Keyboard and touch: Arrows nudge selected ink and numeric inspector provides exact transforms. Touch lasso/move has explicit tool state, preventing a scroll gesture from painting.

State: Locked document layout does not disable established ink editing; View does. A selected stroke removed remotely is reconciled before transformation.

Integration and persistence: Moving ink never changes underlying BlockTree order. Selection adorners remain transient and are excluded from save/export. Group transforms preserve child stroke identity/properties.

Acceptance: Select ink above a paragraph after zooming, move it, then reflow the paragraph; ink remains at its authored page coordinates and undo restores the move.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.2cth9ju2xu9c"></a>

### IX-BOARD-012 — Inline tables and embedded artifact interaction

Controls: Inline table cell; Row/column headers; Table resize handles; Embedded Write/Canvas/artifact; Open in App

Atlas surfaces: boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties

Primary: Single click a table cell selects it; double click/Enter edits its content. Header selection chooses a row/column, not text. Embedded owner single click selects its container; an explicit Edit/Open in App enters the real owner surface. A preview's link follows its canonical artifact/revision, never a copied editor state.

Secondary: Table menu: Insert/delete row/column, Clear content, Merge/Unmerge compatible rectangular cells and table Properties. Embedded menu: Open in App, Copy reference, Replace reference and Remove from page; removal leaves an external original intact.

Drag: Drag from cell selection extends a rectangular range; border handles resize columns/rows with minima. Header-grip reorder preserves IDs. Embedded resize changes viewport geometry. Drop data previews dimensions/coercions before committing.

Keyboard and touch: Tab traverses cells; Enter in an active cell follows the cell editor, never splits an outer Board block. Escape exits cell edit before table selection. Keyboard menu supplies insert/delete/reorder alternatives.

State: Merged/nonrectangular invalid operations explain refusal. Revoked embeds show a bounded placeholder. Conflicting table structure invalidates stale range operations.

Integration and persistence: Merge preserves nonempty content through reversible policy; Data-backed tables delegate changes and AI approvals to Data. Outer shortcuts cannot steal owner input.

Acceptance: Press Enter in a table cell, merge two populated cells and undo; verify no extra Board block, retained text and unchanged external artifact.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.aj1tx8qh453c"></a>

### IX-BOARD-013 — Contextual Properties inspector and recovery actions

Controls: Properties; Close properties; Spacing before; Spacing after; Line spacing; Save a copy; Retry

Atlas surfaces: boards-properties, boards-recovery, boards-editor, boards-typing, boards-ink, boards-add, boards-insert, boards-context, boards-file-menu, boards-alignment, boards-list-options

Primary: Properties pins its panel location while content follows the selected block/ink/object. Spacing edits use [IX-TEXT-005](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.q3bcd39sbl1i); other fields name their exact object scope. Close properties hides the inspector without resetting the selection. On save failure, Retry repeats the failed destination/revision operation; Save a copy chooses a new destination and explicitly creates an independent Board copy.

Secondary: No custom menu on close/recovery buttons. Error details may be copied without activating Retry. Save a copy does not overwrite the original failed destination.

Drag: Inspector scroll/text selection are ordinary; fields and recovery buttons are not draggable. Panel resize, where exposed, changes personal view width only.

Keyboard and touch: Tab navigates fields/actions, Enter commits a valid field or invokes the focused recovery action; Escape cancels the active field before closing. Touch keeps recovery controls outside covered content.

State: Unsaved, Saving, Saved locally, Sync pending and Failed remain distinct. Revision conflict requires reconciliation; permission loss offers another permitted destination. Failed retry preserves both last durable file and current recovery draft.

Integration and persistence: Fields and copy operation use stable captured targets. Closing the banner/inspector is presentation only and cannot mark saved or discard work.

Acceptance: Fail Save, edit another block, Retry, and verify the current intended revision is validated; cancel Save a copy and retain all live edits.

Basis: Atlas textToolbar(), boardContent()/boardBlockMenu(), v3.1 toolbar and final atlas-boards-refinement-code; expanded_editors.md §§6.1–6.3. Controls observed; detailed interaction rules are target decisions.

<a id="h.s4biwivw4a4w"></a>

### IX-EDITORS-005 — Document history, undo and redo

Controls: Undo; Redo; Version history

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, variants, canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert, cards-study, cards-review, cards-edit, data, data-approval, data-formula, data-chart, forms, forms-preview, present-shape-builder, canvas-shape-builder

Primary: Undo/Redo operate on the active owning document transaction, with the focused text/graph/path editor's local pending transaction taking precedence. Version history opens attributed durable revisions of the exact artifact/branch. Select a revision to inspect; Open current returns to current work; Restore creates a new attributed revision or Save copy creates an independent artifact, using the explicitly chosen command.

Secondary: Undo/Redo have no custom secondary activation. A history revision menu offers Inspect, Compare to current and supported Restore/Save copy; inspecting never restores.

Drag: Buttons/history rows are not draggable. Scrolling history does not reorder revisions or change the current document; text may be selected in the diff.

Keyboard and touch: Editor-scoped undo shortcuts yield to IME and active form fields. Enter on a history row inspects; Escape closes detail then panel. Touch exposes named restore/copy controls.

State: Empty stacks disable actions. Stale restore comparison requires refresh. Denied history is not an empty history; unsaved current work is preserved before a restore operation. On a collection/start surface without a bound artifact, History is disabled with an explicit Select an artifact first path; it cannot invent a collection-wide document history.

Integration and persistence: Autosave, selection and panel toggles do not create content undo entries. Restore cannot rewrite audit facts or silently apply to another variant. Mode/View limits still apply. For external Data effects, undo/restore delegates to the verified recovery operation and exact applicable action review; a local history button cannot bypass those protections.

Acceptance: Inspect an older revision, close without change, then restore deliberately and verify current work remains recoverable as a distinct revision.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.jtipjppc4jw3"></a>

### IX-EDITORS-006 — Document comments and anchored discussions

Controls: Comments; Comment marker; Reply…; Resolve comment; Open discussion

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, variants, canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert, cards-study, cards-review, cards-edit, data, data-approval, data-formula, data-chart, forms, forms-preview, present-shape-builder, canvas-shape-builder

Primary: Comments opens the owning artifact's threads. Clicking a marker selects and reveals its stable range/object anchor; selecting a thread does not accept suggested content. Reply opens a draft; Send commits an attributed reply. Resolve changes thread status; Reopen restores discussion visibility without undoing document edits. A missing anchor remains an identifiable orphaned thread.

Secondary: Thread menu: Reply, Copy link, Resolve/Reopen and edit/delete own permitted comment. Document users cannot silently delete other authors' comments without the relevant role. Right click never resolves.

Drag: Thread content supports ordinary text selection. Threads are not draggable onto another anchor; Reattach, when explicitly offered, previews the new stable anchor as a separate operation.

Keyboard and touch: Tab reaches thread actions; Enter activates, Escape preserves an unsent reply draft. Touch selects markers with reachable thread actions. Keyboard navigation reveals targets without moving the editing caret unexpectedly.

State: Pending send, failed send, resolved, orphaned and permission-denied states differ. Retry reconciles the reply operation before adding a duplicate. Without a bound artifact, Comments requests an explicit artifact selection or is disabled; shared collection browsing does not imply an anonymous global comment thread.

Integration and persistence: Comments have their own IDs/permissions and retain author/time. Resolving has no effect on suggestion acceptance, slide notes, card review scores or Forms responses.

Acceptance: Move an anchored section/object, open its comment, reply and resolve; confirm anchor identity survives and no proposed edit is accepted.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.h8yflk3mgigc"></a>

### IX-WRITE-001 — Edit, Suggest and View modes

Controls: Edit; Suggest; View; Locked layout

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators

Primary: Edit applies authorised changes directly; Suggest records proposed insert/delete/format changes against exact ranges; View allows reading/selection without authored mutation. Selecting Suggest may open Review but does not accept existing suggestions. Locked layout constrains spatial object changes independently of text editing and review permission.

Secondary: No custom menu on mode buttons. Context commands derive their enabled state from mode and grants; right click cannot bypass View or Suggest by invoking an Edit-only operation.

Drag: Mode controls are not draggable. Switching mode during an active transform/composition first commits safe text composition or cancels its uncommitted geometry preview with clear focus retention.

Keyboard and touch: Enter/Space selects mode; Escape exits the deepest popup before changing selection. Touch exposes all three modes through hosted Document tools or the standalone equivalent.

State: Current mode is visible/accessibly selected. Insufficient permission disables Edit/Suggest separately; AI Take actions cannot change the user's editing mode or grant acceptance authority.

Integration and persistence: Mode preference is view/session state; accepted edit/proposal is document state. Switching modes preserves caret, comments, pending suggestions and dirty buffer.

Acceptance: Type in Suggest, switch View then Edit, and verify the pending proposal remains unapplied until explicit Accept.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.wk8levh4iich"></a>

### IX-WRITE-002 — Section outline creation, rename and movement

Controls: Sections; New section; Research notes; Learning that lasts; Start with recall; A repeatable session; Sources; Appendix

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators

Primary: New section inserts an authored section at the selected destination and focuses its name. Click an authored section reveals its content; click a derived heading scrolls to that heading without creating another section. Disclosure chevrons expand/collapse hierarchy independently. Rename edits an authored section's name; heading renaming edits the actual heading text.

Secondary: Section menu: Rename, Add before/after, Add subsection, Move to…, Duplicate, Copy section link and Delete section. Heading menu: Reveal, Copy link and Edit heading. Deleting a section discloses its contained content and anchors.

Drag: Drag a section grip to before/after/inside markers; hover-expand and auto-scroll preserve stable IDs. Reject cycles and incompatible nested destinations. Headings cannot become standalone section containers merely by a row drag.

Keyboard and touch: Tree arrows/Enter and F2 follow [IX-BASE-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.a2i0alfkwp3d)/009 with explicit section targets. Move to provides keyboard/touch relocation; Escape cancels name/drag before collapsing the panel.

State: Derived outline updates after style changes. Concurrent hierarchy changes revalidate destination; stale row indexes cannot move another section.

Integration and persistence: Moves preserve comments/bookmarks/suggestions and recompute numbering/layout. Outline width/collapse are personal; section ordering is authored and undoable.

Acceptance: Move a section containing a comment and suggestion, then switch views; all three retain stable identity and the heading outline remains derived.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.oaibxw73duwj"></a>

### IX-WRITE-003 — Document view, pagination and zoom

Controls: Views; Pages; Continuous; Continuous view; Pages view; Book view; 100%; Page indicator

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators

Primary: Views opens Continuous, Pages and Book. Each changes projection of the same DocumentID. Page indicator reveals the current logical page/section; selecting a page scrolls to its anchor. Zoom percentage opens an exact value and Fit width/page choices; zoom changes viewport only. Continuous removes visual paper gaps while preserving authored breaks/headers/footnotes.

Secondary: No custom menu on view buttons. Secondary click on document paper uses the document context menu, not automatic page creation. Zoom context exposes actual fit choices if present, not document scale export.

Drag: Scroll/pinch pans or zooms the viewport without changing margins. The view-mode control is not draggable; dragging page edges never resizes page geometry unless the explicit layout tool is active.

Keyboard and touch: Keyboard view choice returns to retained reading anchor; zoom has numeric keyboard alternative. Touch pinch maintains the anchor under the gesture; Escape closes the menu without changing view.

State: Selected view is explicit and restored. Pagination-in-progress keeps usable last layout with a recalculating state; it cannot fabricate page count.

Integration and persistence: Selection, section position, comments and review remain attached across views. Opening Properties/Review recomputes available width/fit without changing authored page dimensions.

Acceptance: Select text spanning a page break, switch Pages→Continuous→Book, open Review and verify the same range and unchanged page-break semantics.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.c26bgvunpsfo"></a>

### IX-WRITE-004 — Properties fields and keep-with-next

Controls: Properties; Close Properties; Style; Body; Font; Size; Weight; Line spacing; Before; After; Keep with next; Review selection

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators

Primary: Properties opens contextual fields for the real selection. Style/Font/Size/Weight use shared text operations; Before/After/Line spacing set paragraph geometry with units. Keep with next toggles that paragraph pagination constraint, not section membership. Review selection opens suggestions/comments intersecting the captured range. Close hides the panel and restores editor focus.

Secondary: No custom menu on inspector controls. Field context Copy/Paste concerns its value, never copying/deleting the whole document. Mixed values remain visible until explicitly set.

Drag: Panel scroll is independent; controls are not object handles. An exposed splitter resizes the panel as personal view state and preserves the central editing surface.

Keyboard and touch: Tab visits logical labelled fields; Enter commits valid numeric changes; Escape restores current field draft. Checkbox Space toggles Keep with next. Touch labels/units remain readable at narrow width.

State: Invalid values retain editable text/error and do not commit. Switching selection retargets only after resolving current field edit; unavailable object properties are disabled with reason.

Integration and persistence: Panel values derive from canonical text/paragraph state. Applying one field changes only its scoped property; Keep with next is preserved in Continuous even without visual pages.

Acceptance: Toggle Keep with next in Continuous, switch Pages, change selection during an invalid spacing edit and verify no wrong paragraph mutation.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.2ix396rtvet5"></a>

### IX-WRITE-005 — Insert catalogue, document objects and drawing

Controls: Insert; Pen; Inserted object; Object resize handle; Object anchor

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators

Primary: Insert opens the registered document catalogue for text structure, tables, images/media, equations, references/footnotes, breaks and canonical artifact references. A type choice resolves required parameters before one insertion at the captured range. Pen activates an explicit authored drawing layer/tool; it does not convert all text to an image. Click object border selects; double click/Enter enters its registered content editor.

Secondary: Object menu: Cut, Copy, Duplicate, Delete, Properties, Wrap/position and Open source for linked objects. Break/footnote commands address their semantic anchors. Incompatible actions are visibly unavailable.

Drag: Object border/handles move or resize under its anchoring/wrap policy; text drags remain selection. Drag/drop assets preview attachment/copy/link and insertion. Escape reverts preview; source bytes remain intact on failed insertion.

Keyboard and touch: Keyboard catalogue search and Enter select, Escape cancels. Object arrows nudge only in geometry selection; text IME takes precedence. Touch uses explicit handles and Properties alternatives.

State: Missing media/font/owner engine preserves a recoverable object placeholder. View mode blocks insertion; Suggest mode creates reviewable authored changes according to object capability.

Integration and persistence: Inline/anchored objects retain IDs, alt text and source references through reflow. Ink and object adorners are separate from exported content; cancelled insertion creates no blank object.

Acceptance: Insert a linked image, resize with wrap, cancel a second insert, then reopen and verify one object with the original source reference and alt text.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.laoa7llaoq3z"></a>

### IX-WRITE-006 — Review panel navigation and filtering

Controls: Review; Review open; Close Review; All changes; Previous change; Next change; Review selection; Suggestion card

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators

Primary: Review opens the same review state; Close hides it without rejecting changes. All changes filters by change/comment kind, author and status using actual records. Previous/Next traverses the filtered ordered changes and reveals the anchored target. Click a suggestion card selects/reveals it; selection is not acceptance. Review selection limits the projection to the captured range.

Secondary: Card context exposes Inspect change, Accept, Dismiss and Copy reference as permitted; filter trigger has no secondary activation. Right click preserves the intended proposal target.

Drag: Panel scrolling is independent of the document. Cards are not reorderable by drag; selecting proposed text copies it without moving the proposal anchor.

Keyboard and touch: Arrow/Tab navigation reaches cards/actions; Enter opens detail, not Accept. Escape closes nested detail/filter before panel; touch card tap reveals target and keeps separate action buttons.

State: No matches, no changes and failed fetch differ. A missing/deleted anchor is explicit. Previous/Next disables at boundaries and cannot jump to an unrelated author's inaccessible change.

Integration and persistence: Selection/reading anchor survive incoming suggestions. Filter/open state is personal; proposal lifecycle remains canonical and does not depend on whether Review is visible.

Acceptance: Filter to one author, select a suggestion and close Review; reopen and verify neither source text nor proposal status changed.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.t0bmlc4jab45"></a>

### IX-WRITE-007 — Accept, dismiss and suggestion options

Controls: Accept; Dismiss; Suggestion options; Proposed deletion; Proposed insertion

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators

Primary: Accept validates proposal identity, exact original range and expected revision, then commits only the shown replacement/format change as one owner transaction. Dismiss rejects that proposal and preserves the original. Suggestion options exposes Inspect provenance, Copy link and applicable author/status actions; it cannot accept unseen proposals through a generic menu click.

Secondary: Right click offers the same explicit Accept/Dismiss on the addressed suggestion. Bulk acceptance, when exposed, enumerates the selected proposals and conflicts; it never means all unreviewed work silently.

Drag: Proposed text supports ordinary text selection; it is not draggable into acceptance. Dragging the document section reanchors or conflicts the proposal through stable IDs.

Keyboard and touch: Enter/Space activates the focused named action; focus moves to next remaining suggestion or meaningful panel heading after completion. Escape cancels detail. Touch targets separate Accept and Dismiss.

State: Stale or conflicting original requires refresh/rebase, leaving both versions inspectable. Pending/accepted/rejected/failed are distinct; repeated activation uses one operation identity.

Integration and persistence: Accept changes authored content; Dismiss changes proposal status. Neither automatically resolves comments or stops unrelated collaborators. Undo reverses the accepted edit without falsifying proposal history.

Acceptance: Modify the target after proposal creation and reject stale Accept; refresh, apply once, undo and verify preserved formatting and attributable history.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.2utxd6i2a4ki"></a>

### IX-BOARD-014 — Board Trash and restore

Controls: Trash; Move to Trash; Restore; Delete permanently

Atlas surfaces: mobile-boards, boards-editor, boards-context

Primary: Trash opens deleted Board entries with original parent/order and deletion scope. Selecting an entry previews retained content; Restore returns it to its valid former parent or a clearly chosen recovery destination. Delete permanently names the exact owned data and dependencies; it is distinct from ordinary row removal.

Secondary: Trash row menu: Restore, Inspect and Delete permanently when eligible. A referenced external artifact is not purged by deleting its Board reference.

Drag: Trash rows are not dragged back by accident; Restore/Move to gives an explicit target. Dragging a live page to an explicit Trash target previews subtree scope and commits only on drop.

Keyboard and touch: Keyboard list selection plus named Restore provides parity; Escape closes preview without restoring. Long press exposes the same menu and never purges immediately.

State: Expired recovery data shows Unavailable, not empty restored content. Live references/retention may block permanent purge with the exact reason.

Integration and persistence: Restore preserves eligible stable IDs and reveals the page. Removing Trash from navigation does not delete entries; counts respect access policy.

Acceptance: Trash a parent with children and an external Canvas reference, restore it, and confirm IDs/order and external original survive.

Basis: Atlas mobile-boards Trash row; boards.complete.md page deletion/recovery; target lifecycle details adopted here.

<a id="h.y5oiawifqdrl"></a>

### IX-COLLAB-001 — Invite picker, identity search and selection

Controls: Invite Assistants and Specialists to this document; Collaborate; Invite another collaborator; Close collaborators; Search collaborators; All; Assistants; Specialists; Manage; Maya; Rowan; Alex; Source Checker; Proofreader; Presentation Editor

Atlas surfaces: write-editor, write-continuous, write-review, write-collaborators-picker, write-collaborators, present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review, variants, boards-editor, boards-ink, boards-add, boards-insert, boards-context, boards-recovery, boards-file-menu, boards-alignment, boards-list-options, boards-properties, canvas-editor, canvas-ai, canvas-selection, canvas-insert, cards-study, cards-review, cards-edit, data, data-approval, data-formula, data-chart, forms, forms-preview, boards-typing, present-shape-builder, canvas-shape-builder, canvas-pen-presets

Primary: Collaborate opens the shared picker over the retained artifact/selection. Search filters authorised canonical identities; All/Assistants/Specialists change type filter. Clicking a person row toggles its selected identity, not invitation or execution. Manage opens Home catalogue with return context. Close/Cancel abandons only this invitation draft.

Secondary: Identity row menu may Inspect definition/Open identity; it cannot grant access or invite. Picker buttons have no custom secondary action. Portraits preserve their canonical participant identity across apps.

Drag: Rows are not draggable into the document to invite. Search/text selection and list scrolling remain ordinary. Dropping an identity does not bypass the scope review.

Keyboard and touch: Use [IX-BASE-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.g4q68g51ix5k) focus containment; arrows/Space select a row, Tab reaches filters/scope, Escape cancels. Touch sheet preserves search, selected count and invite controls.

State: Selection count reflects real IDs; unavailable/disabled definitions explain their state. Search results cannot reveal inaccessible private identities. Reopen can retain a permitted invitation draft without starting work.

Integration and persistence: Picker creation preserves document revision, selection and scroll. Choosing a Specialist retains its source binding and independent memory/web/general policy; membership adds no unrelated resources.

Acceptance: Select two identities, change filter so one is hidden, cancel and verify no collaborator/run; reopen and inspect the same canonical identities.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.w5q04lw5pz1p"></a>

### IX-COLLAB-002 — Invitation scope, mode and task fields

Controls: Research notes section; Slides 1–3; Document context; Read & discuss; Suggest edits; Include comments in this section; 4 attached source files; Other files and private memory; Collaboration task

Atlas surfaces: write-collaborators-picker, collaborators-picker

Primary: Document context chooses explicit artifact/range/object IDs. Read & discuss grants inspection/discussion; Suggest edits permits proposals requiring separate application. Comments/source checkboxes include only listed authorised material. Other files/private memory remain excluded unless independently configured and granted. Task text states the requested work and is saved with the invitation snapshot.

Secondary: No custom menu on scope/mode/checkbox controls. Resource rows can Inspect source/permission; inspection does not include them. Task field has ordinary text context actions.

Drag: Controls are not draggable; a resource drop requests explicit inclusion and shows its identity/scope before commitment. Dragging text in the task field does not alter document selection.

Keyboard and touch: Radio arrow keys select mode, Space toggles eligible checkboxes, Enter in multiline task inserts a newline rather than inviting. Touch has visible source details and excludes implicit long-press grants.

State: Show unavailable/revoked sources, unsupported operation modes and missing required task inputs. A changed document selection does not silently retarget the already captured invitation.

Integration and persistence: Invitation scope intersects current ACLs and Specialist policy. General Take actions or selecting a role cannot widen it. Changes remain draft until Invite.

Acceptance: Capture slides 1–3, select another slide underneath, and verify invitation still names the captured range; revoke an attachment and block its disclosure.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.701sl5llkwjz"></a>

### IX-COLLAB-003 — Commit or cancel collaborator invitation

Controls: Invite 2 collaborators; Invite; Cancel; Close collaborators

Atlas surfaces: write-collaborators-picker, collaborators-picker

Primary: Invite validates selected identity/definition revisions, task, source/range scope and current grants, then creates attributed work/invitation records once. Show per-person admitted/denied/queued outcomes. Cancel or Close before commitment creates no work; closing after commitment hides the picker but cannot cancel already admitted work by implication.

Secondary: No custom secondary menu; right click does not invite/cancel. A changed selected count updates the button's actual target label.

Drag: Not draggable; pointer release outside the button does not commit. Popup scrolling cannot accidentally activate Invite.

Keyboard and touch: Enter activates Invite only when that button owns focus; task text entry retains its own semantics. Escape cancels the uncommitted dialog. Focus returns to collaboration entry or newly opened work panel.

State: Disable during duplicate admission and reconcile uncertain request outcomes before retry. Partial invitation failure preserves successful participants and gives scoped retry for the failed identities.

Integration and persistence: Work uses the existing artifact and canonical Task/Run; no private document copy. The current grants are captured and rechecked at later retrieval/effects.

Acceptance: Lose the invitation acknowledgement, retry and verify one run per selected identity and retained per-person failure details.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.e9xzz2upyegy"></a>

### IX-COLLAB-004 — Presence, scope inspection and activity

Controls: Participant avatar; Close collaboration panel; Suggest edits; Review proposed change; Review change; View activity; Current task

Atlas surfaces: write-collaborators, present-collaborators, collaboration-review

Primary: Avatar opens participant identity/presence and actual work, not a copy of the document. Scope opens the exact current grant/target. Review change opens its proposal without applying it. View activity opens the canonical Action Log filtered to this task/participant. Close panel hides the view while work continues under its actual state.

Secondary: Participant menu: Inspect identity, View activity, Pause/Resume, Stop and Remove future access where permitted. Removing access is distinct from deleting authored contributions. Right click on a proposal uses Inspect/Review, not Apply.

Drag: Presence avatars/worker cards are not draggable onto content to change scope. Remote cursor overlays do not block local hit testing or appear in exports.

Keyboard and touch: Keyboard traversal reaches scope, each worker and activity; Enter inspects selected work. Touch uses the same named actions; no essential status is hover-only.

State: Working, queued, paused, stopped, review-ready and committed are distinct observed states. Stale/offline presence is labelled and does not imply task failure or permission.

Integration and persistence: Selected work remains tied to ArtifactID/range and revision; view filtering does not retarget it. Source-bound Specialists retain their eligibility rules when embedded in this editor.

Acceptance: Close the panel during work, reopen from an avatar, and inspect the same run; no duplicate task, scope change or exported presence adorners.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.69oynf1if4b"></a>

### IX-COLLAB-005 — Pause, resume and stop controls

Controls: Pause this collaborator; Stop this collaborator; Pause all; Stop all; Resume

Atlas surfaces: write-collaborators, present-collaborators, collaboration-review

Primary: Pause requests a supported safe checkpoint for the named worker; Resume continues that retained execution. Stop cancels future work for that worker. Pause all/Stop all target the displayed task's participating runs, not unrelated Assistant work in other apps. A requested pause is not shown Paused until acknowledged.

Secondary: No custom secondary action on direct buttons. Participant context uses the same targeted operations and outcome details.

Drag: Not draggable. Selecting/copying progress text cannot pause or stop; dragging over a button during document editing does not activate it.

Keyboard and touch: Named buttons use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6); keyboard focus remains on the worker/action after status changes. Touch spacing prevents accidental Stop when choosing Pause.

State: Pausing, Paused, Stopping, Stopped, Completed and unsupported cancellation are distinct. A completed run cannot resume. Report already committed effects and any uncancellable in-flight operation.

Integration and persistence: Stop retains proposals/checkpoints/activity and does not revert committed document edits. Cancelled children reconcile with their parent graph; unrelated permitted work may continue.

Acceptance: Pause one worker while another finishes, stop the first, then verify its proposal/history remains and neither action reverts an accepted edit.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.jbx4jv4q462k"></a>

### IX-COLLAB-006 — Exact AI change review

Controls: Close change review; Keep original; Apply this change; Current value; Proposed value; Target range

Atlas surfaces: collaboration-review, write-collaborators, present-collaborators

Primary: Review displays author, source run, target IDs/range, current/proposed values and expected revision. Apply this change commits exactly that operation after scope/revision validation. Keep original rejects only this proposal. Close leaves it pending. Inspecting before/after content never authorises application or broadens the operation.

Secondary: No custom secondary apply behavior. Text in the diff may be copied; context commands cannot apply other proposals or start unrelated work.

Drag: Diff panes support text selection/scroll, not drag-to-accept. An object-preview pan/zoom is transient and cannot edit the underlying target.

Keyboard and touch: Dialog focus contains named actions; Enter acts on focused Apply only, Escape closes pending review. Touch keeps Keep original and Apply distinct and reachable.

State: Stale original, revoked scope or failed owner operation retains the proposal and explains recovery. Pending/approved/executing/applied/failed stay separate; duplicate clicks use one commit identity.

Integration and persistence: One accepted change is one owning-app undo transaction; surrounding formatting stays intact unless in the diff. Data targets additionally retain mandatory exact-action approval and recovery rules.

Acceptance: Edit the target while review is open, reject stale Apply, refresh, apply once, then undo and verify original formatting and participant attribution.

Basis: Atlas writeView()/writeSections()/writeReviewPanel(), collaborator renderers and final atlas-haven-shell-code; expanded_editors.md §§6.2–6.3. Observed controls; interaction details are adopted target decisions.

<a id="h.4vy1apr07mxl"></a>

### IX-PRESENT-001 — Presentation File menu and output boundaries

Controls: File; New; Open; Save; Save as; Export; Version history

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: File opens New, Open, Save, Save as, Export and Version history for the current deck/variant. New creates a separate draft; Open resolves identity; Save commits current native state; Save as selects its destination; Export chooses format/revision/bounds and creates derived bytes. Version history uses [IX-EDITORS-005](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.s4biwivw4a4w). Opening the menu performs none of these operations.

Secondary: No custom secondary menu on File or its items. Destination/provider context operations remain within their chooser; they do not implicitly publish the deck.

Drag: Menu controls are not draggable. A supported file dropped onto the editor enters explicit import/open inspection instead of replacing the current deck on hover.

Keyboard and touch: Menu keyboard pattern [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk); Escape restores the prior slide/object selection. Save shortcut dispatches once; touch uses the same labelled commands.

State: Saving, native saved, exporting, export-ready and failed are separate. Failed conversion preserves editable deck and options; unsupported Actions/Variants in a format are disclosed before loss.

Integration and persistence: Native persistence retains masters, notes, object IDs and actions. Static PDF/images are not editable deck replacements. Export excludes selection/presence/notes unless explicitly selected by the profile.

Acceptance: Export a deck with an unsupported action, cancel after loss review, and verify the native deck and active variant are unchanged.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.63hu1qubidh4"></a>

### IX-PRESENT-002 — Edit and Slide menu operations

Controls: Edit; Slide; Cut; Copy; Paste; Duplicate; Delete; Select all; New slide; Layout; Duplicate slide; Delete slide

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Edit opens Undo/Redo, Cut/Copy/Paste, Duplicate, Delete and Select all against the focus owner. Text focus edits text; object focus edits selected objects; rail focus edits slides. Slide opens New slide, Layout, Duplicate slide and Delete slide. Layout previews master/layout changes and preserves authored objects or identifies incompatible placeholders before apply.

Secondary: Context menus expose the same commands on the invoked slide/object. Right click outside the selection retargets explicitly; it cannot delete a hidden selection without showing its scope.

Drag: Menu controls do not drag. Paste/import previews supported object structure; a cut removes source only after valid acceptance. Rail drag uses [IX-PRESENT-004](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.9jompexzijah).

Keyboard and touch: Clipboard/delete shortcuts dispatch by focus, yielding to text/IME. Enter selects layout; Escape cancels preview. Touch exposes explicit Select all and Duplicate without modifier keys.

State: Disable invalid clipboard content, denied edits and impossible layout mappings with reasons. Duplicate creates new owned IDs and remaps internal slide/object links.

Integration and persistence: Slide deletion reports dependent action/link/variant targets. Undo restores supported deleted content; one object delete must not delete its slide. Selecting all is transient.

Acceptance: Delete text while editing a title, then delete an object with rail unfocused: neither operation removes the slide; duplicate slide remaps internal references.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.jwdt22krj7ll"></a>

### IX-PRESENT-003 — Arrange, View and Help menus

Controls: Arrange; View; Help; AlignStartVertical; BringToFront; More commands; Group; Ungroup; Align; Distribute; Order

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Arrange exposes Group/Ungroup, Align/Distribute and Bring forward/Send backward/Bring to front/Send to back. Each names selection-versus-slide reference. Direct AlignStartVertical aligns selected objects to the chosen top reference; BringToFront changes their stacking order. View toggles slide rail, Notes, Properties, zoom/fit and playback views. Help opens command help/shortcuts without changing the deck. More commands reveals remaining applicable entries.

Secondary: Object context offers identical Arrange commands; no secondary activation on menu triggers. Context ordering targets only selected siblings/groups under a named parent.

Drag: Menus are not draggable. Reordering through a layer list, if exposed, previews one new stacking position; it cannot alter slide order.

Keyboard and touch: Menu/keyboard patterns apply; group shortcuts only operate outside text edit. Touch action sheets expose exact alignment reference and distribute command.

State: Grouping needs compatible same-container targets; distribution needs enough objects. Disable invalid combinations rather than changing unrelated objects. Mixed lock states are explicit.

Integration and persistence: Grouping retains child IDs/transforms; ungrouping converts transforms without jumps. View panel choices are personal; Arrange mutations are one undo transaction and preserve authored colours.

Acceptance: Group rotated objects, align the group to slide, ungroup and undo; verify children do not jump and slide order is unchanged.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.9jompexzijah"></a>

### IX-PRESENT-004 — Slide rail selection, creation and reordering

Controls: Slide rail; Slide thumbnail; New slide; Slides

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Slide rail toggles navigation visibility. Click a thumbnail activates its SlideID and restores that slide's editing context; Shift selects a contiguous range and Ctrl/Cmd toggles a multi-selection. New slide inserts after the active slide with chosen/current compatible layout and focuses its title or first editable placeholder. Double-click thumbnail does not start playback.

Secondary: Thumbnail menu: New slide after, Duplicate, Delete, Layout, Copy slide link and Present from this slide. These target the captured selected SlideIDs, not thumbnail indexes.

Drag: Drag selected thumbnails after threshold; show insertion bar before/after, auto-scroll rail, preserve selected order. Alt-drag copies only when explicitly advertised with Copy badge; otherwise move. Escape or stale source cancels. Reject drops into descendants or non-slide areas.

Keyboard and touch: Up/Down/Home/End move rail focus; Enter activates; keyboard Move before/after provides reorder parity. Touch long press opens menu, while a scroll gesture does not reorder.

State: Active, selected and focused are distinct. Removing an active slide selects a valid neighbour; missing assets do not prevent selecting its recoverable placeholder.

Integration and persistence: Reorder preserves IDs, notes, actions and variant ancestry; references follow IDs. Rail collapse does not change active slide or export order.

Acceptance: Reorder selected slides after opening Notes, then undo; verify notes/action targets remain attached and no accidental playback starts.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.hnm8s667ojfw"></a>

### IX-PRESENT-005 — Slide object selection and text entry

Controls: Select; Slide object; Text box; Object border; Selection bounds

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Select activates object-selection mode. Single click selects the top eligible hit-tested object; Shift adds/removes from the set. Click empty slide clears selection. Double click/Enter on text enters its text engine at a logical caret; clicking text while editing positions caret rather than moving the box. A selected group can be entered through explicit Edit group to select its children.

Secondary: Object menu exposes Cut/Copy/Paste, Duplicate, Delete, Group/Ungroup, Arrange, Properties and applicable Edit content/Open source. Right click retains a multi-selection when invoked within it.

Drag: Drag from selected object border/body outside text edit moves the captured selection; box selection begins on empty slide. Left-to-right containment and right-to-left intersection selection are explicit target conventions, with visibly different marquee style. Escape cancels preview.

Keyboard and touch: Tab traverses slide objects; Enter edits text; Escape exits text/group before clearing selection. Touch tap selects, second explicit Edit text action avoids ambiguous double-tap-only access.

State: Hidden objects do not hit-test; locked objects remain inspectable and cannot transform until unlocked. Select tool state and selected objects remain separate.

Integration and persistence: Selection/presence handles never enter document bytes/export. Text operations use IX-TEXT; transforming a group preserves member identities and relative offsets.

Acceptance: Double click a title, select text, press arrow then Escape; verify text navigation precedes object nudge and the box does not move.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.4juic5856xqx"></a>

### IX-PRESENT-006 — Object move, resize, rotation and numeric geometry

Controls: Resize handles; Rotation handle; X; Y; Width; Height; Rotation; Properties; Close inspector

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Properties reveals exact object coordinates/dimensions/rotation. Enter in a geometry field validates and commits that named property; mixed multi-selection values stay mixed. Drag handles resize around the opposing anchor; rotation uses a visible pivot. Close inspector hides it without deselecting objects or changing geometry.

Secondary: Handle secondary click does not transform. Object menu offers Reset rotation and precise Properties; any aspect lock is an explicit state, distinct from text auto-fit.

Drag: Move captures initial transforms; Shift constrains axis; Alt bypasses snapping. Resize uses aspect-lock default for intrinsically proportioned assets and Shift toggles that constraint for the gesture; Alt resizes about centre. Rotation Shift snaps to declared angle steps. Escape/lost capture restores all selected transforms.

Keyboard and touch: Arrows nudge by one document unit; Shift by ten, shown as target defaults. Numeric fields provide exact keyboard/touch alternatives; text-edit focus suppresses nudging.

State: Reject negative/invalid dimensions and invalid transforms. Revoked edit rights or concurrent target replacement cancels preview. Overflow warning is separate from box geometry validity.

Integration and persistence: One transform gesture is one undo unit. Opening Notes/Properties changes viewport Fit, never authored X/Y. Multi-object transform preserves relative geometry.

Acceptance: Resize a rotated image with aspect lock, cancel, then enter exact width; verify no jump, one commit and unchanged source resolution.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.6a6llo65hnu"></a>

### IX-PRESENT-007 — Slide Notes editing and split-pane behavior

Controls: Slide Notes; Collapse Slide Notes; Notes text; Notes splitter

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Slide Notes toggles the active slide's note pane; Collapse hides it. Clicking notes enters its separate rich-text editor. Notes selection/input belongs to that SlideID and never changes the selected slide text box. Switching slide loads its notes after preserving the previous note draft.

Secondary: Notes text menu has ordinary Cut/Copy/Paste/format actions; pane header has no custom mutation menu. Presenter private notes are not audience slide content.

Drag: Drag the pane splitter within min/max sizes, preserving a usable slide and notes viewport. Scroll inside notes independently. Dragging selected notes text follows text semantics; pane resize cannot resize the slide.

Keyboard and touch: Tab reaches note editor/splitter; arrows adjust focused splitter and a Collapse button supplies touch alternative. Escape leaves notes editor without deleting its text.

State: Dirty/save failure note state belongs to the deck. A concurrent note revision conflicts visibly. Empty notes show a real placeholder, not fixture text.

Integration and persistence: Pane size/open state is personal. Fit/zoom recalculates from remaining space. Notes persist natively but are excluded from audience output unless a deliberately selected export profile includes them.

Acceptance: Edit notes, resize pane, switch slides and start playback; the audience sees no private notes, and returning preserves both note drafts.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.qrijhfl1rj65"></a>

### IX-PRESENT-008 — Slide zoom, percentage and Fit

Controls: Zoom out; Zoom slider; Zoom percentage; Zoom in; Fit to window; Fit

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Minus/plus steps viewport zoom within declared limits; slider/percentage select actual scale. Fit computes the largest scale fitting the slide into current stage after rail, Notes and Properties. The displayed percentage is calculated from that transform, not a hardcoded fixture. Manual zoom exits Fit mode until selected again.

Secondary: No custom menu on minus/plus/Fit. Percentage may expose 100%, Fit slide and Fit selection; each changes only the viewport.

Drag: Slider drag previews scale around the current view anchor; release persists personal zoom, Escape restores. Pinch/wheel zoom follows shared input conventions. Controls are not content drag sources.

Keyboard and touch: Slider uses [IX-BASE-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.5oiet5rdqq3c); numeric entry provides precise value. Fit has an accessible name despite icon-only presentation. Touch zoom does not select or move slide objects.

State: Min/max buttons disable at bounds. Resize while in Fit recomputes; resize in manual mode preserves scale and keeps content reachable.

Integration and persistence: Zoom never alters slide dimensions, object font size or export resolution. Notes/inspector toggles influence Fit immediately without covering handles.

Acceptance: Open Notes and Properties, activate Fit, then close one pane; actual percentage and selection handles must track the new available stage.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.p9x5iruc08qm"></a>

### IX-PRESENT-009 — Insert categories and placement tools

Controls: Insert; Text; Shapes; Image; Table; Chart; Media; Flowchart & connectors; Custom shape

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Insert opens the catalogue. Text arms text-box placement; Shapes opens shape choices; Image selects an asset; Table chooses dimensions/schema; Chart selects real Data/range binding; Media chooses supported audio/video; Flowchart & connectors opens typed nodes/connectors. Custom shape opens the shared builder. Browsing creates nothing; confirmed placement creates one editable object.

Secondary: No custom secondary action on category buttons. An inserted object's own menu exposes its owner-specific Edit/Properties/Open source after creation.

Drag: Choosing a drawable item arms crosshair placement; drag on the slide sets bounds and release commits; click creates a sensible default-size object. Asset drop previews location/import policy. Escape cancels armed tool or unfinished drag, not the whole slide.

Keyboard and touch: Keyboard choose + Enter places at a visible default centre with editable numeric geometry; touch choose-then-draw uses the same semantics. Tab never inserts accidentally.

State: Unavailable asset/type/engine is named; failed picker returns to catalogue without phantom object. Missing chart source cannot create a screenshot labelled live chart.

Integration and persistence: Placement uses slide coordinates and selection/revision captured for the active slide. Insertion updates Properties and selects the new ObjectID; undo removes that one object.

Acceptance: Choose Image then cancel, choose a shape and cancel drag, then insert a live chart: only the chart exists and retains its source binding.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.bc4uq0a49bq2"></a>

### IX-PRESENT-010 — Shape gallery and connector attachment

Controls: Square; RectangleHorizontal; Circle; Triangle; Diamond; Hexagon; Star; ArrowRight; ArrowUpRight; MoveRight; MessageSquare; Cloud; Flowchart & connectors

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-collaborators, collaborators-picker, collaboration-review

Primary: Each gallery tile arms its named editable shape. Square/Circle preserve their intrinsic equality by default; rectangle/triangle/polygon/star/arrows/callout/cloud retain editable geometry. Connector choice arms endpoints attached to real object ports or explicit free coordinates. Selecting a shape tile does not itself allocate an object.

Secondary: Gallery tiles have no custom menu. Created shape context exposes editable style/geometry and applicable Convert/Edit path; connector context exposes endpoint detach/reattach and waypoint edit.

Drag: Drag draws bounds; modifiers apply explicit proportion/centre/snap constraints. Connector drag highlights eligible ports, commits source/target IDs and rejects incompatible connections; dropping on empty space creates a declared free endpoint. Escape cancels.

Keyboard and touch: Keyboard placement and Properties offer alternatives; connector endpoint selection is operable via object/port list. Touch targets expose ports at usable size without requiring precise hover.

State: A deleted target leaves a broken/free endpoint state according to the declared policy, not a connector silently attached to another object. Locked target can be referenced without moving it.

Integration and persistence: Moving a target recomputes connector routing while preserving user waypoints. Geometry/style changes remain authored and theme-independent; aliases such as RectangleHorizontal are accessible human-readable shape names.

Acceptance: Connect two objects, move one, delete the other and undo; stable endpoint identity and editable path survive without a stale visual-only line.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.tyyk4y6jiv4f"></a>

### IX-PRESENT-011 — Transition presets, timing and advance policy

Controls: Transitions; None; Fade; Push; Duration; Ease in & out; On next; Also advance after a delay; Close Transitions

Atlas surfaces: present-transitions, present-editor, present-selection

Primary: Choose None, Fade or Push to update the current slide transition's owned graph region. Duration edits nonnegative seconds; Easing selects an actual interpolation; Advance chooses On next or another declared event. Also advance after a delay enables a separate validated timer field, not immediate playback. Close hides the panel while preserving committed settings.

Secondary: No custom menu on preset buttons. The panel can offer Reset current transition, which affects only its owned nodes, never arbitrary extensions.

Drag: Controls are not slide drag sources. Duration slider/field changes preview the value with one commit; dragging a preset does not reorder slides.

Keyboard and touch: Preset arrows/Enter, numeric fields and checkboxes follow IX-BASE patterns. Escape cancels an active field/menu, then closes the panel. Touch exposes duration/advance labels.

State: Invalid timing stays editable and unapplied. An advanced graph that cannot round-trip displays Custom/Advanced rather than pretending a standard preset fits.

Integration and persistence: Simple editor and Actions share graph identity. Preset replacement preserves external extension nodes and their typed boundary connections; timer state during preview is not an authored duration edit.

Acceptance: Add an extension in Actions, select Fade and change duration here; verify the extension survives and no slide advances until preview/playback.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.v3jsvavsuler"></a>

### IX-PRESENT-012 — Transition preview, apply-all and graph handoff

Controls: Preview; Apply to all slides; Open in Actions; Open Transitions; Transitions

Atlas surfaces: present-transitions, present-actions

Primary: Preview runs the selected transition in a bounded resettable session. Apply to all slides previews the target SlideIDs and compatible graph changes, then applies the reviewed transition settings once; incompatible/advanced targets report individual outcomes. Open in Actions reveals the same transition nodes; Open Transitions returns to its simple representation without copying the graph.

Secondary: No custom secondary action. Preview stop/reset controls affect preview only. Right click on Apply to all cannot commit a batch.

Drag: Not draggable. Graph or slide gestures are excluded while pressing preview controls; pointer crossing cannot start another preview.

Keyboard and touch: Enter/Space activates the focused command; Space preview shortcut works only in graph/stage focus, never a text field. Escape stops the preview before closing its panel.

State: Preview Running/Stopped/Failed states are actual. Batch stale revisions refresh; partial failures retain successful changes with undo/recovery metadata and no all-success claim.

Integration and persistence: Preview does not save transient playback state or issue external effects. Apply-all is authored and undoable at the declared batch boundary, preserving non-owned graph extensions.

Acceptance: Preview twice with cancellation, then apply to a mixed simple/advanced deck and inspect exact target outcomes without lost extension nodes.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.76f3218kxijs"></a>

### IX-PRESENT-013 — Actions graph selection, node creation and connections

Controls: Actions; Add action; Event node; Crossfade slides; Advance slide; Transition complete; Input/output ports; Graph edge

Atlas surfaces: present-actions, present-editor, present-selection, present-insert, present-transitions, collaborators-picker, present-collaborators, collaboration-review

Primary: Actions opens the presentation's graph. Click a node selects it and its inspector; double click opens its editable parameters, not execution. Add action opens the typed catalogue at the captured graph point. Clicking an edge selects the edge; clicking blank space clears selection. Nodes address real slide/object IDs.

Secondary: Blank graph menu offers Add action; node menu offers Duplicate, Delete, Properties and Reveal target; edge menu offers Disconnect. Delete node previews connected edges. Ctrl-click an edge disconnects only that edge under the shared graph convention.

Drag: Node-header drag moves layout after threshold; port drag previews compatible typed targets and commits one edge. Invalid direction/type/cycle rejects without removing existing connection. Edge endpoint retarget keeps old edge until a valid drop. Blank-space pan and marquee are separate tools/modifiers.

Keyboard and touch: Arrows navigate graph elements; Enter opens node; Delete respects selected node versus edge; keyboard Connect chooses source/target ports. Touch long press opens context menu and uses larger port targets.

State: Missing targets/types and unbounded self-triggering cycles produce diagnostics. Concurrent graph edits revalidate connection IDs; cancelled drag leaves topology unchanged.

Integration and persistence: Graph layout is authored only when deliberately moved; selecting/zooming never executes. Preset-owned region and extension nodes remain distinguishable.

Acceptance: Drag an incompatible port, cancel a valid retarget, then Ctrl-click one edge; verify only the explicitly disconnected edge changes.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.msbeoqorn1p6"></a>

### IX-PRESENT-014 — Actions inspector and preview runtime

Controls: Current slide Out; Next slide In; Duration; Easing; Sequence; Preview; Preview stopped; Space to preview

Atlas surfaces: present-actions

Primary: Selecting Crossfade shows its actual target references, duration and easing. Target pickers select stable slides/objects; changing one validates its role. Sequence reveals causal order and completion/error paths. Preview admits one bounded run from the chosen event using current draft revision; Stop ends future events and retains diagnostics.

Secondary: Target context offers Reveal target and Replace reference. Parameter controls have no custom secondary action. Preview log entries inspect receipts without replaying them.

Drag: Inspector fields are not node drag handles. Graph selection can change only after resolving a pending field edit; drag inside log text selects text, not events.

Keyboard and touch: Tab/Enter commit valid fields; Escape restores pending values. Space toggles preview only when graph owns focus. Keyboard target picker replaces pixel-only selection.

State: Invalid target/duration blocks the affected run; last valid graph remains inspectable. Show actual Running/Complete/Cancelled/Failed rather than a decorative animation.

Integration and persistence: One navigation request commits advancement once even if several completion paths converge. Preview runtime values and timers remain separate from saved graph properties and the live audience session.

Acceptance: Create two completion paths for one navigation, preview and verify exactly one slide advancement; cancel and preserve authored graph values.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.pw8l0klia8g5"></a>

### IX-PRESENT-015 — Graph pan, zoom and fit

Controls: Zoom graph out; Zoom graph in; Fit graph; 100%; Graph canvas

Atlas surfaces: present-actions

Primary: Minus/plus adjust graph viewport scale; percentage edits exact scale; Fit graph fits relevant node bounds including labels. Blank-space pan moves the view, not nodes. Clicking a node after zoom uses inverse transform and selects its actual ID.

Secondary: Blank graph menu opens Add action at transformed document coordinates. Secondary click on a node opens its menu without panning or starting preview.

Drag: Middle-button or explicit Pan drag moves viewport; wheel/pinch zoom preserves anchor. Marquee selection uses the selected selection tool. Escape cancels an active gesture; no view gesture rewrites edge topology.

Keyboard and touch: Keyboard zoom/fit and directional pan remain available. Touch two-finger pan/pinch does not connect ports or move nodes.

State: Bounded zoom prevents inaccessible controls. Dynamic node sizes/inspector opening update Fit calculations without moving authored nodes.

Integration and persistence: Viewport state is personal; graph geometry and runtime overlay remain separate. Fitting to a selected subgraph cannot delete or hide other nodes from the canonical graph.

Acceptance: Zoom and pan, connect two ports and Fit; verify hit-testing and edge endpoints use the same transform and graph topology changed only once.

Basis: Atlas presentHeader()/presentToolbar()/slideRail()/presentProperties()/presentStatus(), state-specific renderer and final atlas-haven-shell-code; expanded_editors.md §6.3 and present.complete.md. Controls shown; detailed interactions are target decisions.

<a id="h.mvb7a9x86p2t"></a>

### IX-PRESENT-016 — Start, navigate and end audience playback

Controls: Present; End presentation; Previous slide; Next slide; 1 / 3; audience output selector

Atlas surfaces: present-editor, present-selection, present-insert, present-transitions, present-actions, present-presenter, collaborators-picker, present-collaborators, collaboration-review

Primary: Single-click Present starts a playback session from the selected slide, using the current committed presentation revision; a start menu can explicitly choose From beginning or From current slide and the audience output. Previous/Next request the adjacent slide through the Actions graph once. The slide counter opens a slide chooser; choosing a thumbnail requests that SlideID. End presentation stops this session and restores the authoring slide, selection and viewport. Double-clicking a navigation button is two deliberate requests subject to the runtime navigation policy, never two commits for one event.

Secondary: Playback buttons have no custom right-click action. Secondary-clicking the preview opens only navigation/output commands; it must not expose authoring Delete or reveal private notes on the audience surface.

Drag: Buttons and preview slides are not draggable. A separate output selector moves the audience surface by explicit choice; dragging private presenter panels cannot leak them into it.

Keyboard and touch: With playback focus, Right/PageDown and Left/PageUp request next/previous; Escape ends or leaves an inner chooser first. Touch uses the same explicit controls; swiping a notes pane scrolls notes rather than advances the deck.

State: At first/last slide, the corresponding direction is disabled or an explicit end choice appears. Validate missing media/fonts and output availability. A disconnected audience surface shows the actual state while the private session remains recoverable; never claim it is connected from fixture text.

Integration and persistence: Playback locks its revision unless an explicit refresh is chosen. Authoring edits do not race into an active audience frame. Starting/ending changes session state, not document history; action failure shows the failed path and does not skip silently.

Acceptance: Start on slide two with notes open, navigate once and disconnect the output. The audience must never see the notes, the control shows disconnection, and End restores the exact editor selection.

Basis: Atlas present-presenter and Present toolbar; expanded_editors.md ED-PRESENT-004–006. Controls are shown; output selection, navigation dispatch and recovery details below are target decisions, not evidence of a functioning presentation service.

<a id="h.3wt5jp1kvbop"></a>

### IX-PRESENT-017 — Pointer, live ink and pause while presenting

Controls: Pointer; Live ink; Pause presentation; Resume presentation; Elapsed; presenter notes; next-slide preview

Atlas surfaces: present-presenter

Primary: Pointer toggles a transient audience pointer mode; Live ink toggles transient drawing mode and deactivates Pointer. In pointer mode a press/move positions the pointer, not a slide edit. Live ink captures one stroke until release. Pause freezes timed progression, the elapsed session clock and controllable media; its label changes to Resume. Explicit navigation remains available while paused and resets the slide-specific pending timer. The next-slide preview is informative and does not advance on click.

Secondary: Right-click in live ink exposes Clear current slide ink and Save ink to slide; the latter is an explicit authoring change with a target/revision check. Secondary-click on Pointer/Pause has no alternate command. Notes retain text selection/copy but are read-only during this view.

Drag: Ink drags start on the audience-preview surface and capture through release/cancel; cancelled strokes are discarded. Pointer drags have no persistent payload. Notes can scroll independently without generating strokes. Buttons are non-draggable.

Keyboard and touch: Keyboard activation toggles modes; Escape cancels an in-progress stroke before ending presentation. Touch/pen follows the selected mode, with a distinct scroll region for notes. A keyboard-accessible Clear/Save ink menu provides non-drag alternatives.

State: Expose paused, playing and media-not-controllable states separately. If a media engine cannot pause, identify that item instead of claiming the entire session stopped. Lost pointer capture ends the current transient gesture safely.

Integration and persistence: Ink is session-local until Save ink to slide or an explicit export choice. Ending playback offers retention only when transient ink exists; dismissal discards that ink without touching authored objects. Pausing does not change authored transition durations.

Acceptance: Draw a stroke, pause for a while, move to another slide, and return. The stroke is retained within the session, elapsed time excludes the pause, and the native deck remains unchanged until Save ink to slide.

Basis: Atlas present-presenter and Present toolbar; expanded_editors.md ED-PRESENT-004–006. Controls are shown; output selection, navigation dispatch and recovery details below are target decisions, not evidence of a functioning presentation service.

<a id="h.m0p3yfvvun83"></a>

### IX-PRESENT-018 — Variant selection, source/target and branch actions

Controls: Main · r18; Calm · r6; Bold colour · r3; Compare; Bring changes from; Into; Variant actions; Duplicate variant; Rename; Archive; Back to editor

Atlas surfaces: variants

Primary: Clicking a branch row selects that branch as the active editing context without merging it. Bring changes from and Into select source and target branch/revision; Compare creates or refreshes a read-only three-way comparison against the common base. Duplicate variant creates a distinct branch retaining ancestry; Rename changes its label without rewriting references; Archive removes it from the active list while retaining recoverable history. Back to editor opens the active variant, not necessarily Main.

Secondary: Branch-row context commands are Open, Compare into…, Duplicate, Rename and Archive; Main cannot be archived while it is the required main branch. Right-click selects the intended branch before opening its menu, without changing the current comparison until a command is chosen.

Drag: Branch rows are not merge drop targets. Text can be selected in metadata. Dragging a source preview onto the target cannot apply changes.

Keyboard and touch: Arrow keys move branch focus; Enter opens the chosen branch; F2 renames. Dropdowns follow [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk). Long press opens branch commands, and explicit buttons provide every mutation without drag.

State: Show selected versus active editing branch separately. Loading revisions or unavailable common ancestry disables Compare with a specific explanation. Rename/archive conflicts preserve the pending name or target for retry. An archived source remains addressable in saved comparisons.

Integration and persistence: All commands use BranchID and revision, never a display name or row index. Creating/renaming/archiving persists branch metadata. Compare and navigation do not modify slide content; archival does not delete the source/base snapshots.

Acceptance: Rename Calm while another view compares Calm to Main. The comparison must still reference the same BranchID and revision, and Back to editor must reopen that branch without applying any differences.

Basis: Atlas variants; expanded_editors.md ED-PRESENT-007. Branch labels and three-way comparison are shown; context menus, selection dispatch and exact commit handling below are adopted target interactions.

<a id="h.7reef5b9wwf"></a>

### IX-PRESENT-019 — Difference navigation and three-way conflict selection

Controls: Previous change; Next change; 1 / 4; Target · Main; Source · Calm; Base: Main revision 14; Heading text changed; Background changed; Shape position and fill changed; change selection checkboxes; conflict resolution choices

Atlas surfaces: variants

Primary: Previous/Next move the inspected difference and pan both previews to the same SlideID/object where possible. Clicking a difference row selects it for inspection; its separate checkbox includes or excludes it from the merge candidate. The count describes included compatible changes, not a completed merge. Selecting a conflict exposes Base, Source and Target values with Keep target, Take source or an explicit manual resolution. Inspecting a row or source preview never chooses a resolution.

Secondary: Right-click a diff offers Reveal source object, Reveal target object and Copy change reference. It must not offer an immediate merge bypassing preview. A deleted object has a labelled tombstone rather than a fabricated live object link.

Drag: Preview panes support bounded pan/scroll only. Changes cannot be dragged between branches. Selecting text in a diff does not toggle its checkbox. Splitter dragging changes comparison layout, not authored geometry.

Keyboard and touch: Arrow keys navigate focused diff rows, Space toggles inclusion, Enter opens details; checkbox state is announced independently. Touch uses the checkbox hit area for inclusion and the row body for inspection.

State: Conflicts remain unresolved until an explicit choice is valid. One side changing after comparison marks it stale and preserves the user’s candidate selections for revalidation. Deleted-versus-edited and incompatible ordering remain distinct conflicts.

Integration and persistence: Compare operates on stable slide/object/property identities. Authored slide background changes remain document colours, independent of the application theme. Notes, animation graphs and source references appear as semantic changes where altered; excluded fields must remain unchanged.

Acceptance: Exclude the background change, include heading text, then inspect a conflicting shape edit. The target remains unchanged and Preview merge contains only the chosen text change plus an unresolved conflict warning.

Basis: Atlas variants; expanded_editors.md ED-PRESENT-007. Branch labels and three-way comparison are shown; context menus, selection dispatch and exact commit handling below are adopted target interactions.

<a id="h.icob7dnt3ewu"></a>

### IX-PRESENT-020 — Preview and commit a selected variant merge

Controls: Preview merge; Preview selected changes into Main before committing; Commit merge; Cancel merge preview; Refresh comparison

Atlas surfaces: variants

Primary: Preview merge materialises a non-authoritative merged preview from the selected source/base/target revisions and resolutions. It displays exact affected identities and retained/excluded changes. Commit merge, within that preview, performs one revision-checked merge transaction into the named target. Cancel closes the candidate without applying it. Refresh comparison recomputes differences against current revisions and revalidates, rather than silently applying earlier selections to new content.

Secondary: No custom secondary action on Preview or Commit; right-click cannot skip review or choose a different target. Preview content context commands are inspection/copy only.

Drag: Buttons are non-draggable. A preview layout divider changes view state only. There is no drag-to-commit gesture.

Keyboard and touch: Enter/Space activates the focused explicit button; Escape cancels the preview but cannot roll back an already acknowledged merge. Touch uses the same explicit commit control with the target name visible.

State: Commit is disabled with unresolved conflicts, denied target write access, stale revisions or invalid object references. During submission disable duplicate commits and retain an operation identity. An uncertain acknowledgement queries that operation before retrying. Show success only once the target revision is durably acknowledged.

Integration and persistence: Commit produces a target revision and one coherent history entry, preserving the source branch and common base. Undo/revert uses the recorded semantic inverse or a new reverting revision; it does not delete subsequent unrelated work. Closing compare after commit cannot undo it implicitly.

Acceptance: Preview a two-change merge, advance Main in another editor, and press Commit. It must require refresh, retain the selected candidate intent, and apply once only after the new comparison is valid.

Basis: Atlas variants; expanded_editors.md ED-PRESENT-007. Branch labels and three-way comparison are shown; context menus, selection dispatch and exact commit handling below are adopted target interactions.

<a id="h.lrchqp27c3l7"></a>

### IX-PRESENT-021 — Text/object appearance in the selected-object inspector

Controls: Font; Montserrat; Size; 50; Weight; Bold; Text colour; Opacity; 100%; text overflow indicator

Atlas surfaces: present-selection

Primary: Font, Size and Weight apply the shared text commands to the captured text range or selected text object’s applicable runs. Text colour changes foreground runs, while a shape Fill field changes that object’s fill; the inspector must label those targets separately. Opacity sets the selected object’s compositing opacity from 0–100%, preserving its underlying colours and per-run alpha. The overflow indicator opens the exact clipped-content diagnosis with explicit Resize box, Change font size or chosen Auto-fit behavior; it cannot silently shrink other slides.

Secondary: Field right-click retains copy/paste and Reset this override where inheritance exists. A text colour menu exposes foreground and highlight separately. No custom secondary action on the opacity label; copying its value does not flatten the object.

Drag: An explicit opacity slider previews the selected object only and commits once on release; Escape restores its captured value. Colour sliders follow the same preview boundary. Numeric labels do not become unadvertised scrub handles. Text selection inside an inspector input never drags the slide object.

Keyboard and touch: Numeric fields support direct unit-aware entry and steppers. Focus identifies the captured object/range; Enter commits, Escape restores. Touch uses explicit sliders or fields with the slide preview still visible.

State: Mixed values stay Mixed until explicitly set. Reject invalid opacity/size units and non-finite numbers while preserving input. A removed or changed target produces a revision conflict rather than applying to the next selection. Unavailable fonts retain their authored identity and show rendering fallback.

Integration and persistence: Opacity, typography and fill are authored object properties and one scoped undo transaction per commit. App theme changes cannot recolour the object. Inspector opening/closing changes Fit geometry only. Auto-fit is an explicit object setting, separate from a resize or manually entered size.

Acceptance: Select a title at 100% opacity, preview 40%, cancel and enter 75%. The original preview is restored on cancel, only the committed title becomes 75%, and body text/other slides remain unchanged.

Basis: Atlas present-selection inspector; expanded_editors.md ED-PRESENT-002–003. Fields are shown; mixed-selection and commit details are target decisions.

<a id="h.dp0dbk6r4l72"></a>

### IX-SHAPE-001 — Builder draft, reusable shape and inserted instance

Controls: Ribbon leaf · Draft; My shapes; Save to My shapes; Insert shape; Cancel; Back to Insert

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Click the draft title to edit its name. My shapes opens the reusable-definition library; selecting a definition loads a draft copy with its version recorded. Save to My shapes creates a definition or explicitly updates the selected definition version; it does not insert into the caller. Insert shape validates the draft and creates one independently editable instance at the caller’s captured insertion context, then returns focus to that instance. Cancel discards the uncommitted draft; Back to Insert is a navigation close and preserves a recoverable unfinished draft until its explicit discard or insertion.

Secondary: Library rows offer Open as draft, Rename definition, Duplicate definition and Remove from library. Removing a definition does not delete already inserted independent instances. Builder action buttons have no custom secondary command.

Drag: The draft title and action buttons are not draggable. Library-to-scene dropping is not assumed; use Insert shape. Reordering library rows, if presented as personal sorting, cannot modify path topology.

Keyboard and touch: Keyboard activation follows button/dialog patterns; Escape exits the innermost library/field before asking to close a dirty draft. Touch uses explicit controls and never requires dragging to insert.

State: Invalid geometry or unavailable caller disables Insert with the offending contour identified. Name conflicts offer a distinct name or explicit version update. Unknown insert acknowledgement reconciles one operation before retrying.

Integration and persistence: Draft, definition and scene-instance identities are separate. One insertion is one caller undo step; library save has its own history. Updating a definition does not retroactively rewrite instances. Document History opened from the host still addresses the caller document, not unsaved path gestures.

Acceptance: Save a draft to My shapes, insert into Canvas, reopen it from Present and insert again. Editing either instance must leave the other and the saved definition unchanged.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.2lfzegh6mvu9"></a>

### IX-SHAPE-002 — Path tool modes and creation

Controls: Edit points; Pen; Curve; path canvas; Path 1; 6 points; Path options

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Edit points selects existing anchors/segments. Pen clicks append straight vertices to the active open contour; pressing and dragging while adding a vertex creates tangent handles. Curve selects the curved-segment creation mode using anchor/handle placement with a visible uncommitted segment preview. Clicking the starting anchor explicitly closes a valid contour. Path options offers New contour, Select contour, Reverse contour and Delete contour, each naming the current contour. Double-click finishes an open contour without adding a duplicate coincident point.

Secondary: Secondary-click on an anchor selects it and offers point-type and topology commands from [IX-SHAPE-004](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.udsz8d7nkge5)/005. Secondary-click on empty space offers Finish open contour and Cancel current segment; it does not insert a point.

Drag: In Pen/Curve, dragging a newly placed anchor’s tangent previews that segment; pointer-up commits one creation transaction. Dragging in Edit points moves the captured anchor/handle. Tool switching or Escape cancels an uncommitted segment, not previously committed vertices.

Keyboard and touch: Enter finishes the current open contour; Escape cancels the current preview, then returns to point selection. Keyboard Add point and coordinate fields provide precise alternatives. Touch shows the same explicit mode and enlarged handles.

State: Keep one active tool. A contour with too few vertices for closure shows an explanation. Self-intersection is retained as editable geometry with the chosen fill-rule preview; it must not disappear because a renderer cannot resolve it.

Integration and persistence: The path list and point count derive from the same draft representation. Creating a new contour preserves prior contours; reversing a contour reverses direction and remaps handles without moving the rendered path.

Acceptance: Place two Pen anchors, switch to Curve, cancel the next segment and finish. The two committed anchors remain, no phantom third point appears, and undo removes exactly the last committed creation.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.o15xvfwcpfjh"></a>

### IX-SHAPE-003 — Anchor, segment and tangent selection and dragging

Controls: path anchors; selected point; tangent handles; Point 3 of 6; selection marquee

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Single-click an anchor selects its stable PointID; click a tangent handle selects that handle and retains its owner anchor. Clicking a segment selects that segment for subdivision. Shift-click extends or toggles the explicit set. Empty-space click clears selection; double-click on a segment invokes Add point at that parameter, not arbitrary path replacement. Point number is a positional label, not identity.

Secondary: The anchor menu provides Corner, Smooth, Symmetric, Add point on adjacent segment, Remove selected point and Split path at point with applicability. Segment context offers Add point at pointer and Reverse contour. Right-click does not move geometry.

Drag: After the shared drag threshold, capture PointIDs and original geometry. Move anchors in draft coordinates; tangent dragging obeys the selected constraint. Shift constrains direction; Alt temporarily bypasses snapping, with the state visible. Marquee selects contained anchors as the adopted target convention. Pointer-up commits one transaction; Escape/pointer cancellation restores the entire captured geometry.

Keyboard and touch: Arrow keys nudge selected anchors by one draft unit; Shift+Arrow uses ten, explicit target defaults. Tab cycles anchors/handles without changing coordinates. Touch uses enlarged handles and an explicit point list; long press opens commands.

State: Locked or invalid selections remain inspectable but cannot move. If the selected anchor is removed by a concurrent draft update, cancel the gesture and refresh instead of moving a replacement index.

Integration and persistence: Coordinate fields, adjacent segments, tangent readouts and preview update from the same geometry. Panning/zooming only changes hit-test transforms. One drag is one local builder undo entry even if it emits many previews.

Acceptance: Zoom to 200%, drag a Smooth anchor and cancel. Its coordinates, both tangents and adjacent curves must return exactly to their pre-drag values.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.udsz8d7nkge5"></a>

### IX-SHAPE-004 — Point type, linked handles and numeric geometry

Controls: Corner; Smooth; Symmetric; Linked handles; X; Y; Handle length; Tangent

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Corner permits independent tangents; Smooth keeps opposing tangents collinear while allowing unequal lengths; Symmetric additionally couples lengths. Selecting one commits the corresponding constraint for selected anchors and displays any required adjustment preview. Linked handles toggles directional coupling for Corner points; Smooth/Symmetric enforce their stronger constraint and explain a disabled conflicting toggle. X/Y edit selected anchor coordinates; Handle length and Tangent edit the explicitly selected handle, never both handles implicitly unless the constraint requires it.

Secondary: Numeric fields retain standard text editing/copy/paste. Point-type buttons have no alternate right-click action. A handle menu may Reset handle to zero or Select opposite handle; it must name which handle changes.

Drag: Fields are not draggable by their labels; direct handle movement follows [IX-SHAPE-003](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.o15xvfwcpfjh). Slider-like scrubbing is absent unless visibly offered, avoiding accidental geometry edits during text selection.

Keyboard and touch: Enter commits a valid numeric value; Escape restores the field’s last committed value. Unit-aware input accepts the displayed coordinate/angle unit. Touch opens a numeric keyboard and leaves the geometry visible.

State: Multiple unequal values show Mixed rather than zero. Reject NaN, infinite values and unsupported units while preserving typed text. A zero-length handle has no fabricated tangent; show undefined until direction is set. Constraint changes must not silently collapse neighbouring segments.

Integration and persistence: Numeric commit is one draft undo transaction. Changing point type affects only selected anchors and dependent tangents, preserving PointIDs. Field focus captures the target so later selection changes cannot redirect an uncommitted value.

Acceptance: Select two anchors with different X values; confirm Mixed. Enter a common X, undo once, then make one anchor Symmetric and drag one handle; its opposite handle must remain collinear and equal in length.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.ohle50y26e39"></a>

### IX-SHAPE-005 — Add, remove and split topology

Controls: Add point; Remove selected point; Split path at point; Closed

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Add point inserts an anchor at the selected segment parameter, or arms a segment pick when none is selected; subdividing a curve preserves its rendered geometry within the declared numerical tolerance. Remove selected point removes the named anchor and reconnects its neighbours with a visible resulting path; it does not promise unchanged curvature. Split path at point breaks contour topology at the selected anchor while preserving geometry: a closed contour becomes open at that location, and an internal point of an open contour creates two contours with separate endpoint identities. Closed toggles the selected contour’s closing segment.

Secondary: Anchor/segment context menus expose the same commands. Split is disabled at an already open endpoint with no split to perform; it does not become Add point. Removing a whole contour is a separately named command, not a hidden effect of pressing Remove repeatedly.

Drag: These commands are non-draggable. A newly added anchor may subsequently be dragged as a new transaction; releasing the Add point click cannot accidentally start a path move.

Keyboard and touch: Delete removes selected anchors only in point-editing focus; outer document objects are unaffected. Keyboard menus can choose an adjacent segment and subdivision parameter. Touch exposes the commands without requiring a precise double tap.

State: Validate minimum contour topology and warn when removal would delete the last meaningful segment; retain a recoverable draft. Boolean/constraint failures leave the prior valid path. Closed is Mixed for differently configured contours.

Integration and persistence: All topology edits update stable identity mappings and point counts atomically. Undo restores original IDs, segments and open/closed state. Fill preview may change when a contour is opened; that is explained rather than silently changing the fill rule.

Acceptance: Subdivide a cubic segment and compare the rendered curve; then Split at the new anchor. Geometry remains in place but the open-contour count changes. One undo restores the prior topology.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.31b1w4drtz94"></a>

### IX-SHAPE-006 — Fill rule, appearance and preview

Controls: Non-zero; Fill rule; Fill #A8D3C3; Outline 3 px; Preview; Shape preview

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Fill rule opens Non-zero and Even-odd choices for the compound path. Fill opens a colour/alpha picker including None; Outline edits stroke width and its colour/style through the appearance disclosure. Preview switches the working surface to a clean shape rendering without anchors and also refreshes the preview pane; it does not insert the shape or save a definition. Clicking again returns to the same point selection.

Secondary: Colour fields support copy/paste of a valid colour value; preview offers Copy preview image as an explicit derived output, never Copy editable path under that label. No custom right-click on the Preview button.

Drag: Colour sliders preview appearance under [IX-BASE-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.5oiet5rdqq3c); release commits one change. Preview is not a drag source for insertion. Outline width fields allow text selection rather than label drag.

Keyboard and touch: Keyboard opens the picker and chooses swatches; numeric outline entry follows [IX-BASE-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xj3xapubjjfz). Escape cancels an uncommitted picker value and restores prior colour. Touch uses sufficiently large swatches and a separate alpha control.

State: Invalid colour/width input preserves the last valid appearance and the draft text. Compound contours that produce different fills under each rule update live; an unavailable rendering effect shows a diagnostic, not an empty successful preview.

Integration and persistence: Appearance is authored path data, independent of the application’s Comic or other interface theme. Preview visibility and zoom are view state. Fill/outline commits are undoable path-draft changes; they never flatten editable anchors.

Acceptance: Create nested contours, switch Non-zero to Even-odd, preview and return to Edit points. The hole changes correctly and all anchors remain editable with the same IDs.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.qottqah7x49n"></a>

### IX-SHAPE-007 — Boolean combination and path options

Controls: Combine; Union; Intersection; Difference; Exclude / XOR; Path options; Reverse contour; Delete contour

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Combine opens Union, Intersection, Difference and Exclude/XOR over at least two selected contours/shapes. Difference visibly identifies the minuend and subtrahend and offers Swap operands. Choosing an operation produces a preview with source outlines retained for inspection; Apply replaces only the declared draft operands with editable result contours, while Cancel keeps the originals. Reverse contour changes winding without geometry movement; Delete contour removes the explicitly selected contour and its points.

Secondary: Context menus on a multi-selection expose the same Boolean choices and Select operands. No contextual choice bypasses result validation. A click outside the menu closes it without applying any operation.

Drag: Operands cannot be established by dragging one path over another; overlapping geometry alone is not a Boolean command. Reordering an explicit operand list changes Difference order with a preview and commits only on Apply.

Keyboard and touch: Keyboard selection and operand ordering make every operation available without precise pointer input. Escape cancels a calculation or candidate result; touch uses explicit operation and Apply controls.

State: Empty intersection is a legitimate named empty result requiring an explicit Apply; numerical failure or unsupported open-path operands is an error and leaves sources intact. Large operations expose cancellable computation and cannot block unrelated editor input indefinitely.

Integration and persistence: One applied Boolean is one draft undo transaction containing original operands and result mappings. Editable vector output is required; rasterising silently is not a valid result. Existing definition instances elsewhere remain unaffected.

Acceptance: Select two overlapping contours, preview Difference, swap operand order and cancel. Neither contour changes. Apply Union, then undo and verify the original PointIDs and winding return.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.usdfck5c7rjv"></a>

### IX-SHAPE-008 — Shape viewport, snapping and local undo

Controls: Snap on; Toggle grid; Zoom shape out; Zoom shape in; 100%; Fit path; Undo path edit; Redo path edit

Atlas surfaces: present-shape-builder, canvas-shape-builder

Primary: Snap on toggles anchor/handle snapping using visible grid/guide candidates; Toggle grid controls grid visibility independently. Zoom out/in changes the draft viewport about its centre, percentage accepts an explicit zoom, and Fit path frames the full draft bounds with handles allowed for. Undo path edit reverts the latest draft geometry/appearance transaction; Redo reapplies it if no divergent edit invalidated the redo branch.

Secondary: No custom right-click on these controls. A viewport menu may expose Fit selected points and Reset view; those are navigation only. Right-click must not alter snapping or history.

Drag: Middle-drag or an explicit Pan gesture moves the viewport; wheel/pinch zoom preserves the pointer/gesture anchor. The controls themselves are not draggable. During a point drag, Alt temporarily bypasses snapping, and releasing it restores the saved snap mode.

Keyboard and touch: Ctrl/Cmd+Z dispatches to the builder while it owns focus; Ctrl/Cmd+Shift+Z redoes. Space-drag pans only outside active text entry. Touch two-finger pan/pinch never creates anchors.

State: Disable unavailable Undo/Redo accurately. Clamp zoom to supported bounds with visible feedback. Empty draft Fit uses a stable origin rather than invalid bounds. Snapping candidates cannot jump across hidden/unselected remote geometry unexpectedly.

Integration and persistence: Viewport/grid preferences persist as view state and are excluded from path geometry undo. Snap policy affects future gestures, not existing coordinates. Host document history remains separate from this local draft stack.

Acceptance: Pan and zoom after moving an anchor, then Undo path edit. Only the anchor move reverses; the user keeps the current viewport and grid preference.

Basis: Atlas present-shape-builder and canvas-shape-builder; expanded_editors.md ED-SHAPE-001–002. Controls are design evidence; geometric gesture rules, draft lifecycle and validation specifics below are target decisions.

<a id="h.j0a0f3l19qc"></a>

### IX-CANVAS-001 — Canvas application menu, identity and export bounds

Controls: [data-action=canvas-menu]; Learning map · Saved locally; [data-action=canvas-title]; Save; Export; Rename; Document information

Atlas surfaces: canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert

Primary: The application-menu icon opens New, Open, Import, Save, Save a copy, Export, Version history and Return to Start. The title opens Rename, Location and Document information; a title click does not immediately overwrite its name. Save flushes pending committed edits; Save a copy creates a distinct CanvasID. Export requires Native, SVG/PDF or raster capability selection, then explicit content bounds, page/artboard or selection, background/alpha and scale/resolution. Exporting the current viewport is a separately named choice. The currently unlabelled prototype icon must expose the accessible name Canvas menu.

Secondary: Right-click the title provides the same document commands plus Copy artifact reference. No custom right-click on the menu icon. Native file reveal delegates to Files without changing identity.

Drag: Title/menu controls are not draggable. A documented artifact reference may be dragged from its explicit handle into compatible destinations; that is a reference/copy operation, not a file move. Export region selection uses an explicit region tool.

Keyboard and touch: Keyboard menus preserve the prior tool focus; Escape closes the menu. Rename uses Enter to commit and Escape to cancel. Touch opens the same commands without relying on title hover.

State: Distinguish local durability, syncing and save error. Missing source assets or unsupported export effects produce a compatibility report; they do not destroy native content. Reconcile unknown save/copy acknowledgements before creating a second artifact.

Integration and persistence: File rename/move retains CanvasID. Export reads a pinned revision and includes offscreen objects within its selected extent. One failed derived-output job cannot alter the saved native canvas or clear the dirty indicator.

Acceptance: Place objects far outside the viewport, export content bounds, and reopen the result. All intended objects must appear; cancelling export leaves the native artifact and viewport unchanged.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.cnzhwqc3j9rl"></a>

### IX-CANVAS-002 — Quick settings, canvas mode and authored background

Controls: [data-action=canvas-settings]; Infinite canvas; Paged; Page size; Orientation; Background; Smart Pen; Snapping; Grid and guides; Stylus/touch preferences

Atlas surfaces: canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert

Primary: Quick settings exposes Infinite/Paged mode, page size/orientation in Paged mode, background colour/pattern and spacing, plus user-scoped Smart Pen, snapping, grid/guide visibility and available stylus/touch preferences. Switching document mode first previews object-to-page mapping and boundary crossings; Apply commits the structural conversion, Cancel retains the original mode. A background choice edits authored canvas/page background. Smart Pen enables bounded clean-up of future ink while keeping it editable handwriting. The currently unlabelled prototype quick-settings icon must expose the accessible name Canvas settings.

Secondary: Settings controls have no alternate right-click mutation. Pattern/colour fields support Copy value and Reset to document default where defined; Reset must name whether it changes one page or the entire document.

Drag: Numeric fields are non-draggable; page-boundary handles in the explicit conversion preview can adjust proposed bounds, committing only on Apply. Grid visibility cannot become a geometry drag target.

Keyboard and touch: Keyboard traverses grouped document and personal settings; radio/checkbox state is explicit. Touch alternatives provide numeric page dimensions and spacing without handles. Escape closes the current preview and retains the last committed values.

State: Unsupported pressure/palm-rejection options identify the missing device capability without removing stored pressure data. Invalid dimensions or conversion conflicts block Apply. Smart Pen failure preserves original ink and reports the unavailable adjustment.

Integration and persistence: Mode, page bounds and background persist with the artifact; grid visibility, Smart Pen and input preferences persist per user/device. They must not recolour authored content when the interface theme changes. Smart Pen keeps pre-adjustment data for exact undo of the clean-up.

Acceptance: Convert an infinite drawing spanning proposed page boundaries to Paged, cancel, then choose a ruled background and enable Smart Pen. Only the committed background affects the document; conversion cancellation loses no objects.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.cs1y98ktv4h2"></a>

### IX-CANVAS-003 — Bottom toolbar dispatch and specialised Tools

Controls: Select; Pan; Pen; Eraser; Insert; Tools; AI; active-session sparkle indicator

Atlas surfaces: canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert

Primary: Select activates object manipulation; Pan activates viewport navigation; Pen activates the current pen; Eraser activates the current eraser mode; Insert opens the category catalogue; Tools opens the registered specialised-instrument list; AI replaces the single options host with the contextual composer. Only the active tool’s label is expanded. Reselecting Pen focuses presets; reselecting another tool focuses its existing options without creating content. Tools entries expose their exact instrument name, applicability and options; the capability registry, not a guessed static menu, determines available geometry/measurement/fill/selection instruments.

Secondary: Tool buttons have no required custom secondary action. An optional context entry Open tool options invokes the same accessible option surface; it cannot be the only route. Right-click does not switch tools accidentally.

Drag: The primary toolbar is not reorderable by accidental drag. Beginning a canvas drag is dispatched by the captured active tool. Switching tools cancels an uncommitted gesture safely before activating the new tool.

Keyboard and touch: Keyboard activation changes tools only outside text/IME editing. Touch selects explicit buttons, and deeper options never require hover or long press. Escape exits an open options layer before altering canvas selection.

State: An unavailable instrument remains named with a reason and cannot show a successful active drawing cursor. Existing objects created by that instrument remain preserved. Switching away from AI hides its composer but does not stop a running session.

Integration and persistence: Tool state, selection and undo survive tool changes. One options host is visible; AI does not add a second permanent composer. A hidden active AI session exposes the sparkle indicator that reopens the same SessionID, not a new conversation.

Acceptance: Start a Canvas AI request, switch to Pen, make a stroke and reopen AI through the active-session indicator. There is one conversation, one stroke and no second composer.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.tamhtw4mk5am"></a>

### IX-CANVAS-004 — Pan, zoom and fit in document coordinates

Controls: Pan; Zoom out; Zoom in; 100%; Fit drawing; canvas viewport

Atlas surfaces: canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert

Primary: Pan pointer-down captures a viewport navigation gesture. Zoom out/in changes magnification about the viewport centre; percentage accepts an explicit value. Fit drawing frames actual visible-content bounds including distant objects, while an explicit Fit selection command uses selected bounds. Normal trackpad/wheel motion scrolls/pans; modified wheel/pinch zooms about its anchor. None of these controls changes object dimensions.

Secondary: Right-click blank canvas in Pan offers Fit drawing, Fit selection when selected and Reset view. It has no Paste/Draw side effect until explicitly chosen. Right-click zoom controls has no extra command.

Drag: Pan and middle-button dragging translate the viewport from the initial pointer/transform; they never move an object even when started above it. Pinch keeps the focal document point stable. Pointer cancellation ends navigation without creating a stroke.

Keyboard and touch: Space-drag temporarily pans outside text entry, returning to the previous tool on release. Keyboard zoom/fit commands are available through menus. Two-finger touch navigates, while stylus intent remains distinct under the selected input policy.

State: Clamp zoom safely; empty canvas Fit returns a stable origin. Rendering may stream offscreen content, but bounds derive from the model rather than only mounted objects. A scale/DPI change cannot restart a drag at screen origin.

Integration and persistence: Pan/zoom are personal view state and do not enter document undo. Hit testing and new stroke coordinates use the inverse current transform. Opening menus or changing window size recalculates the viewport without rewriting authored coordinates.

Acceptance: At high zoom and negative coordinates, pan over a shape and draw a short line. Its stored coordinates must match the intended location; undo removes the line and leaves the view unchanged.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.ixrkfhn2f7xv"></a>

### IX-CANVAS-005 — Pen strokes, colour, width and input fidelity

Controls: Pen; Ink pen; 3 px; Pen colour #254f40; Pen colour #c57852; Pen colour #5367a0; Pen colour #353638; More pen colours; Width; Opacity

Atlas surfaces: canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert

Primary: Pen press on the drawing surface begins a new structured stroke using a captured snapshot of the active preset, colour, width and opacity. Pointer release commits that stroke once. Colour swatches choose the literal authored colour; More pen colours opens an alpha-aware picker. Width/opacity fields or sliders affect future strokes unless an explicit Apply to selected strokes command is used. Clicking the current preset name opens its presets rather than drawing.

Secondary: Right-click colour opens Copy colour and Reset to preset colour. Right-click a stroke in Select mode offers its object commands; right-click in Pen mode opens pen options without marking the canvas. Text-selection defaults apply within numeric fields.

Drag: Stroke samples use canonical coordinates and retain supported pressure/tilt. Pointer capture continues outside the surface; cancellation discards only the uncommitted stroke. Slider dragging previews the next-stroke settings and commits the preference on release. A tool-options drag cannot generate ink behind the panel.

Keyboard and touch: Keyboard fields provide precise width/opacity alternatives; touch swatches have explicit names. Stylus can draw while permitted touch gestures navigate. Without pressure, use the declared fixed-width fallback and preserve pressure data in older strokes.

State: Invalid widths/opacity are rejected with typed input retained. Losing capture cannot join the last sample to a later unrelated pointer event. Delayed smoothing/rendering never changes the stroke to a different preset selected after pointer-down.

Integration and persistence: One stroke is one document undo step and stores resolved brush properties. Colour/preset changes do not recolour earlier strokes. Smoothing and Smart Pen must preserve editable geometry and make any adjustment recoverable.

Acceptance: Begin a stroke, change the selected colour through another input, then release. The entire stroke uses its captured colour, and the next stroke uses the new choice.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.ksiv2zqj1mvv"></a>

### IX-CANVAS-006 — Preset selection and custom-pen lifecycle

Controls: Ink pen; Fountain; Pencil; Highlighter; Custom pen; Edit preset; Balanced; Width; Opacity; Close pen presets

Atlas surfaces: canvas-editor, canvas-pen-presets

Primary: Click a preset to select its engine and resolved defaults for subsequent strokes. Width/Opacity adjust the active working settings; Balanced opens supported smoothing choices with their latency/shape effect explained. Custom pen creates a draft from an explicitly chosen engine; Edit preset opens the selected preset draft. The draft provides name, supported nib/pressure/tilt/smoothing parameters and a scratch preview, then Save or Cancel. Close pen presets hides options without changing the active tool.

Secondary: Preset context commands are Select, Duplicate, Rename, Favourite, Edit and Delete; built-in presets can be duplicated and reset but cannot silently remove the only valid fallback. Delete moves a custom preset to recoverable history; existing strokes retain resolved properties.

Drag: Explicit preset grips may reorder personal presets with an insertion marker; otherwise row drags scroll. The preview pad creates disposable test strokes only. Reordering does not apply a different preset to the document or change stroke z-order.

Keyboard and touch: Arrow/Enter chooses presets; touch selection and explicit Edit support the full workflow. Numeric inputs and a selectable pressure-curve point list provide alternatives to dragging graph handles. Escape cancels an uncommitted edit draft.

State: Only parameters supported by the selected engine are editable. Unknown engine versions produce a named unavailable preset with preserved definition data. Duplicate names are allowed only with unambiguous IDs/display disambiguation; save conflict preserves the draft.

Integration and persistence: Preset definitions are reusable settings, separate from document strokes. Save changes later use; updating existing selected strokes requires a separately named command and undo transaction. Cancelling a preset draft restores its prior saved definition.

Acceptance: Duplicate Fountain, lower its opacity, draw in the preview and cancel. The document gains no stroke and the original preset is unchanged. Save the custom preset next time and verify old document strokes retain their original appearance.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.6zd7u0fyo035"></a>

### IX-CANVAS-007 — Natural and Quick eraser target rules

Controls: Eraser; Natural Eraser; Quick Eraser; Segment erasure; Whole stroke erasure; Eraser width

Atlas surfaces: canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert

Primary: Eraser activates the stored mode and exposes Natural Eraser and Quick Eraser. Natural Eraser drags across content using the explicitly selected segment or whole-stroke policy; Quick Eraser click/tap deletes the topmost eligible unlocked target under the pointer. A visible target highlight precedes Quick deletion. Segment mode affects editable ink geometry, not unrelated image pixels or table contents. Raster erasure requires an explicit raster-layer target and shows that scope.

Secondary: Right-click an eraser target offers Choose overlapping target and eraser settings, not an immediate delete. The target chooser lists stable object names/types in z-order so overlapping strokes can be addressed deliberately.

Drag: Natural erasure captures the sweep and initial target revision set; a preview marks affected portions. Release commits one transaction. Escape/cancel restores all swept content. A Quick Eraser press is one target deletion; dragging after it cannot silently become a batch erase.

Keyboard and touch: Keyboard users can select an object and invoke Delete with the same history semantics. Touch uses a visible eraser cursor/target preview; an unavailable barrel-eraser input leaves the explicit toolbar usable.

State: Locked/hidden layers are excluded from mutation. Unsupported segment erasure on a selected type shows a reason and never silently upgrades to whole-object deletion. Conflicting geometry revisions require revalidation rather than applying old cuts to new paths.

Integration and persistence: Erasure preserves resulting editable fragments and provenance. One undo restores original strokes/IDs and layer order. Eraser size is tool preference; it does not affect selection handles or change document scale.

Acceptance: Place two overlapping strokes, lock the upper one and use Quick Eraser. Only the eligible lower target is removed. Undo restores it; a cancelled Natural sweep changes neither stroke.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.5kycx0fxd3kk"></a>

### IX-CANVAS-008 — Spatial selection and entering object content

Controls: Select; canvas objects; selection rectangle; lasso; 3 objects

Atlas surfaces: canvas-editor, canvas-ai, canvas-pen-presets, canvas-selection, canvas-insert

Primary: In Select mode, click selects the topmost visible eligible object, Shift-click toggles it in the explicit set, and blank click clears selection. Double-click or Enter enters the selected object’s semantic editor: text caret, table cell, graph editor or vector points. A group is selected as one unit unless Enter or an explicit Select child command enters it. The selection-count chip describes the actual set rather than a fixture count.

Secondary: Object context commands include Cut, Copy, Paste here, Duplicate, Delete, Group/Ungroup, Move to layer, Arrange, Lock and Properties, enabled by actual type/capability. Right-click an unselected object selects it before opening commands; right-click a selected member preserves the multi-selection.

Drag: Blank-space marquee is containment when dragged left-to-right and intersection right-to-left, an explicit target convention. Lasso identifies its inclusion rule before use. Moving a selected body invokes [IX-CANVAS-009](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.pz9v67bkfejv); dragging within an active text/table editor selects content rather than moving the object.

Keyboard and touch: Enter/Escape move into/out of the innermost object editor; Delete affects text while its caret owns focus and object selection otherwise. Keyboard object/layer lists provide a non-spatial selection path. Long press exposes commands without forcing a transform.

State: Locked objects may be inspected from Layers but cannot join a mutable transform silently. Hidden items cannot be hit through the canvas. Missing shared renderers show a selectable typed placeholder with recoverable metadata.

Integration and persistence: Selection is view state; IDs survive sorting/repainting/virtualisation. Selecting or opening content creates no revision. Context AI receives the explicit selected object IDs, not every object whose pixels happen to intersect the screen.

Acceptance: Double-click a table and select text within one cell. Dragging the selected text must not move the table; Escape returns to the table object without losing its ID.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.pz9v67bkfejv"></a>

### IX-CANVAS-009 — Selection transforms, grouping, alignment and stacking

Controls: Move selection; Rotate selection; Ungroup; Duplicate selection; Align objects; Arrange; Lock group; Selection options; Group selected; selection handles

Atlas surfaces: canvas-selection

Primary: Move selection arms body/handle translation; Rotate selection exposes a pivot and rotation handle. Ungroup removes only the selected group container while preserving child positions and IDs. Duplicate creates new object IDs and remaps internal links. Align objects opens left/centre/right/top/middle/bottom and distribution commands with an explicit selection-or-page reference. Arrange offers forward/backward one step and front/back within the current layer. Lock group sets the group lock. Selection options exposes cut/copy/delete, properties, group and Move to layer.

Secondary: Right-click selected geometry exposes the same applicable commands. Alignment never means text-paragraph alignment. Mixed locks or types display which targets can participate and require an explicit compatible subset rather than silently ignoring objects.

Drag: Transform captures selected IDs, original matrices and pivot. Body drag translates; corner handles resize; rotation handle rotates. Shift constrains direction/angle or toggles aspect preservation as labelled; Alt bypasses snapping. One release commits one composite transform; Escape restores all matrices. Dragging an object into a layer row uses an explicit highlighted reparent target.

Keyboard and touch: Arrow nudges one document unit and Shift+Arrow ten, as target defaults; numeric properties provide precise translation/size/rotation. Keyboard commands replace grip operations. Touch enlarged handles preserve the same pivot and constraints.

State: Disable Ungroup without a group and distribution without sufficient distinct targets. A stale member or lock change invalidates the transaction rather than moving only the remaining subset silently.

Integration and persistence: Group/ungroup and transforms are single undoable operations. Relative positions and connector identities remain; routes recompute from stable attachments. Z-order changes within a layer are distinct from moving the layer itself.

Acceptance: Group three objects, rotate them, then ungroup. Their appearance must not jump; one Undo restores the group with its transform and original child identities.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.fgj64585qn00"></a>

### IX-CANVAS-010 — Layer hierarchy, order, locks and visibility

Controls: Layers; Learning map; Main idea; Background; Question; Four directions; Annotations; Close layers; New layer; layer visibility; layer lock; layer disclosure

Atlas surfaces: canvas-selection

Primary: Click a layer/object row selects its stable identity and reveals it without moving it. A disclosure expands children. Visibility toggles rendering/export participation under the declared visible-layers policy; lock toggles edit eligibility. New layer creates one named layer above the selected layer and enters rename. Double-click a name or Rename changes only the label. Close layers hides the popup, preserving selection and drawing area.

Secondary: Layer context commands are Rename, Duplicate layer, Move up/down, Show/Hide, Lock/Unlock and Delete layer. Delete names the contained object count and removes owned contents as one recoverable operation; it cannot delete independent linked source artifacts.

Drag: Grip drag reorders layers with a clear before/after marker; object-row drag can move membership into a compatible layer or group. Reject cycles, self-parenting and locked destinations. Hover expands a candidate without committing; edge auto-scroll is bounded. Drop commits once; Escape restores order.

Keyboard and touch: Tree arrows traverse/expand; Space toggles the focused checkbox only; F2 renames. Context Move commands provide keyboard/touch alternatives to drag. Row text selection is distinct from its grip.

State: Mixed child lock/visibility is shown explicitly. Permission changes or a deleted destination during drag cancel the drop with selection retained. Hidden objects remain listed and saved.

Integration and persistence: Layer order, membership, visibility and locks persist with the canvas; popup size/expansion is personal view state. Moving an object retains ObjectID. Undo restores original membership, order and locks without recreating linked artifacts.

Acceptance: Move a group to another layer, hide the original layer and reopen the canvas. The group remains visible in its new layer with the same IDs; undoing the move restores exact membership.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.wcuazd4xz22h"></a>

### IX-CANVAS-011 — Insert catalogue, shape choice and connector endpoints

Controls: Text; Shapes; Media; Table; Other; Square; RectangleHorizontal; Circle; Triangle; Diamond; Hexagon; Star; ArrowRight; ArrowUpRight; MoveRight; MessageSquare; Cloud; Lines & connectors; Custom shape

Atlas surfaces: canvas-insert

Primary: Each first-level category only opens its submenu. Text offers Text Box, Heading and Paragraph; Media offers Image, Audio and Video; Table opens canonical table creation; Other exposes Graph and registered compatible shared types. Shapes exposes the named primitives; selecting one arms placement, and Custom shape opens the shared draft builder. Lines & connectors chooses free line or attached connector, visibly distinguishing them. No object is allocated merely by opening a category.

Secondary: Secondary-click category/shape tiles has no custom action or insertion. A placed object uses its owning context menu. A connector endpoint menu offers Attach to object/port, Detach to free endpoint and Remove connector.

Drag: After choosing a primitive, drag a rectangle to preview its bounds or click to use a visible default size; Escape cancels before allocation. Connector drag begins at a port/free point and highlights valid destination ports; dropping elsewhere creates a free endpoint only when that mode was explicitly chosen. Touch/keyboard can pick source and target sequentially.

Keyboard and touch: Cascading menus use arrows/Enter/Escape; touch has explicit submenu Back. Keyboard placement uses default dimensions and numeric properties, avoiding mandatory precision drag.

State: Unavailable registered object types remain discoverable with a named capability reason. Unsupported import/media leaves no empty successful object. Locked target layers block insertion before commit. Revalidate endpoint identities at drop.

Integration and persistence: Insertion is one undoable canonical object operation. Connectors retain attachments and waypoints when objects move. Custom shape follows [IX-SHAPE-001](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.dp0dbk6r4l72)–008; source media retains editable metadata and is not flattened to its preview.

Acceptance: Open Media and cancel, then draw a rectangle and attach a connector to it. Only the rectangle and connector are created; moving the rectangle updates the connector endpoint.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.9a7euvn1wpnh"></a>

### IX-CANVAS-012 — Embedded content and cross-app clipboard

Controls: text object; table object; graph object; image/audio/video object; Copy; Cut; Paste; Paste here; Open in owning app

Atlas surfaces: canvas-editor, canvas-selection, canvas-insert

Primary: Enter/double-click addresses a selected object’s canonical editor. Text uses shared rich-text semantics; a table focuses a named cell; a graph opens its structured editor; media exposes playback/crop/fit and metadata. Open in owning app follows the canonical reference and revision, not an automatically duplicated artifact. Copy produces typed representations plus compatible fallbacks. Cut removes owned content only after the clipboard accepts the payload; Paste inserts new owned instance IDs and preserves explicit independent references.

Secondary: Object context distinguishes Copy editable object, Copy artifact reference and Export image. Paste special exposes chosen plain/raster conversion and its losses; a bitmap must not silently win when the destination supports the semantic representation.

Drag: Typed-object dragging starts from a body/grip outside its inner editor and advertises copy/reference/move. Destination highlights the accepted semantic type; unsupported drops are rejected without deleting the source. Text selection, media scrubbing and table-range drags remain inside the inner editor.

Keyboard and touch: Focus arbitration follows [IX-BASE-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.v8vab0wwgfgp): clipboard/undo first reach the active inner editor. Escape exits inward-to-outward. Touch uses explicit Copy/Paste/Open controls when OS drag exchange is unavailable.

State: Denied source access produces a bounded placeholder without disclosing its contents. A renderer failure preserves native payload. Cross-app paste reports unsupported features and offers a deliberate fallback or cancellation.

Integration and persistence: Canvas owns placement and transform; the shared engine or source app owns content semantics. Inner edits and outer moves produce correctly scoped history. Linked content refresh cannot overwrite explicit local transforms or duplicate the original artifact.

Acceptance: Copy editable ink and a table into a compatible Boards page, then return to Canvas. Both retain semantic editability; an explicit image-only destination receives a labelled conversion while Canvas originals remain unchanged.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.syqb9864cyr0"></a>

### IX-CANVAS-013 — Canvas contextual AI selection and session continuity

Controls: AI; Ask about your canvas; Read-only; Send to AI; selected objects or whole canvas; active-session sparkle indicator

Atlas surfaces: canvas-ai

Primary: AI opens the existing Canvas-bound conversation in the single options host. Clicking the prompt field edits its text; Send to AI captures the CanvasID/revision, explicit selection IDs or an explicitly selected whole-canvas scope, and the effective source/access policy. Read-only opens the shared access selector; choosing Take actions changes eligibility only within granted scope, not source policy. The prompt example is editable seed text, not an executed request.

Secondary: Composer text keeps ordinary copy/paste. Secondary-click Send has no alternate execution or hidden grant. Canvas object context may offer Ask about selection, which attaches those IDs to the same conversation.

Drag: Selected object references can be attached through an explicit typed-reference drop target; dropping on the composer does not move/delete the canvas object. Composer/window scrolling cannot create ink. The Send button is non-draggable.

Keyboard and touch: Enter/send behavior follows the shared composer preference; Shift+Enter inserts a newline. Touch has a visible Send action. Keyboard attachment inspection lists actual selected IDs/names and permits removal before submission.

State: Display stale/deleted/denied attachments before submission. Read-only requests may explain or propose but cannot apply scene mutations. A target revision changing during AI work requires revalidation or a preview conflict, never a blind overwrite.

Integration and persistence: Source-bound Specialists retain their source policy; selecting Whole canvas cannot implicitly grant other files, web or memory. Switching tools hides UI without stopping work. Applied semantic edits use the canvas history and preserve authored colours/layers; hidden sessions retain the same identity.

Acceptance: Ask about two selected objects, deselect them while the request runs, and switch to Pen. The request retains its captured scope, remains Read-only, and reopening AI shows the same session.

Basis: Atlas canvas-editor/ai/pen-presets/selection/insert and late Canvas composition; expanded_editors.md ED-CANVAS-001–007; retained-apps/canvas.complete.md named tool, preset, eraser, quick-settings and insert sections. Atlas controls are shown; precise command/gesture details below are adopted target requirements, not verified native behavior.

<a id="h.z9k5gzyfad6q"></a>

### IX-CARDS-001 — Create, open and configure the canonical card set

Controls: Create set; Import; Set overview; View; Edit; Edit set; Set settings; Topic link; Set organisation; More

Atlas surfaces: cards-study, cards-review, cards-edit

Primary: Create set opens title, scope and optional subject/topic defaults, then Create allocates one new SetID. Import starts the mapping/validation workflow in [IX-CARDS-008](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.y7fbagvly7fs). Review/View opens the same set in non-editing review mode; Edit/Edit set opens its authoring view. Set overview returns to the selected set’s summary. Set settings edits title, default topic, grouping/navigation preferences and export/share entries. Card-level Topic link edits the selected CardID’s explicit override; Set organisation changes membership/grouping through a named action. More addresses the selected card with Duplicate, Remove from set, Delete card, Copy link and metadata.

Secondary: Set rows offer Open review, Edit, Rename, Duplicate set, Export and Set settings. Duplicate set must explicitly choose independent card copies or retained shared card references. Remove from set is distinct from deleting a card shared elsewhere; deletion lists affected memberships.

Drag: Set/settings buttons and topic labels are non-draggable unless a dedicated reference handle is shown. Dropping a topic reference on the labelled Topic link target sets that link after identity validation, not a new topic.

Keyboard and touch: Enter/Space activates the focused action; settings fields use explicit Save/Cancel or visible immediate-commit semantics. Touch opens settings and topic selection through buttons, with no hover dependency.

State: Denied edit access keeps View/review available where permitted. Invalid/deleted topics show Unlinked or unavailable status rather than silently choosing the current sidebar topic. A failed creation retains its draft and operation identity.

Integration and persistence: SetID/CardIDs remain stable across view mode, title, grouping and topic changes. Settings cannot expose another learner’s review records. View prohibits content mutation even with AI Take actions; personal ratings remain allowed.

Acceptance: Open a shared set in View, change your grouping, rate one card and open Edit with insufficient permission. Content stays unchanged, your rating persists privately and grouping does not duplicate cards.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.httz2zjzi6fg"></a>

### IX-CARDS-002 — Card collection navigation, search and review filters

Controls: Back to Revision Bank; Study overview; Quadratics; Algebraic fractions; Logarithms; Change grouping: currently topic; Group by set; Search sets; Search this set; All cards; Difficult topics; Due for review; Your review history; vertical/horizontal navigation

Atlas surfaces: cards-study, cards-review, cards-edit

Primary: Study overview opens the Cards collection summary; topic/set rows select the corresponding stable scope. Back to Revision Bank returns to the originating Study Space/topic without creating a second set. Grouping chooses Set, Topic or a named custom grouping. Search sets filters collection metadata; Search this set filters card content/metadata within that SetID. All cards, Difficult topics and Due for review choose review candidates using the active learning policy. Your review history opens the current learner’s attributed records.

Secondary: Sidebar context exposes Group by…, Navigation orientation and Set settings. A card-result menu offers Review from here, Edit and Copy card link. Search-result activation never counts as a review response.

Drag: Sidebar/result lists scroll; no implicit card movement from dragging a search hit. Explicit reference dragging to a compatible destination carries CardID/SetID. Vertical stack remains default; horizontal orientation changes the scrolling axis without changing membership/order.

Keyboard and touch: Arrow keys navigate focused results and Enter opens the selected identity. Escape clears an active search popup before leaving the set. Touch scrolling is separated from card flipping using the drag threshold.

State: Show empty search, no due cards and unavailable topic mappings distinctly. Reordering/filtering preserves the focused CardID when still eligible; otherwise move focus to a clearly identified next candidate. A recycled virtual row cannot inherit another card’s draft.

Integration and persistence: Grouping, filters and orientation are personal view preferences. Due counts and progress derive from personal review evidence, not shared content or fixture totals. Changing grouping cannot duplicate cards or reset review history.

Acceptance: Focus a card, switch from set to topic grouping, then search and clear the query. The same CardID and private review record remain; no rating is recorded merely by navigation.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.zdt74bhlz8yu"></a>

### IX-CARDS-003 — Focused review, flip and adjacent-card navigation

Controls: Flip quadratic formula card to show answer; focused card; Previous card; Next card; 3 / 12; neighbouring cards; All cards; Search this set

Atlas surfaces: cards-review, cards-study

Primary: In review mode, clicking the focused card flips Front/Back; the dedicated flip control exposes the same state. Previous/Next changes the active CardID, resetting the new card to Front unless its explicit session-resume state is being restored. Clicking a partially visible neighbour focuses that card without rating it. The position counter describes position within the current review sequence, not a permanent card identity. Embedded media controls consume their own click rather than flipping the whole card.

Secondary: Card secondary-click offers Flip, Edit card, Copy card link and View source/reference where permitted. It does not record a rating or open private history for another person. Text selection/copy remains available through an explicit selectable-content mode or native context.

Drag: A stack scroll/swipe crossing the navigation threshold changes focus; a tap below the threshold flips. Dragging a media timeline scrubs media and cannot navigate the stack. Card content is not reorderable in review mode.

Keyboard and touch: Space flips the focused card only outside input/media controls; arrow navigation follows the chosen stack orientation. Announce Front/Back and card position. Touch flip does not depend on motion; reduced motion preserves the same state transition.

State: At sequence boundaries disable unavailable directions. Loading the next card retains the current card until the replacement is ready. An inaccessible asset keeps a bounded placeholder while permitted text remains reviewable.

Integration and persistence: Flip, browse and time spent do not create proficiency evidence. The session tracks actual CardID/content revision; updated shared content is visibly refreshed at a safe boundary instead of changing the answer midway through a rating.

Acceptance: Flip a card, play an embedded clip and navigate twice without rating. The private review-record count must remain unchanged, and media clicks must not flip the answer unexpectedly.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.6u1x1lb1icvf"></a>

### IX-CARDS-004 — Explicit recall ratings and personal correction

Controls: Revisit; Developing; Confident; Your review history; Undo rating; reviewed this session; due and reviewed counts

Atlas surfaces: cards-review, cards-study

Primary: Revisit, Developing and Confident submit the corresponding explicit personal rating for the active CardID and reviewed content revision. In the focused review flow these are enabled after the answer has been revealed; the user may return to Front without losing eligibility for that same reviewed revision. A successful rating advances to the next eligible card and shows an Undo rating action naming the just-rated card. History permits an explicit correction with a retained supersession trail; it never overwrites another learner’s evidence.

Secondary: Rating buttons have no alternate right-click score. A personal history row offers Open card, Inspect evidence and Correct/Undo this rating. A shared-card context menu cannot export private review evidence implicitly.

Drag: Ratings are not drag targets; dragging a card across a coloured button cannot score it. History scroll/selection is independent of rating commands.

Keyboard and touch: Keyboard activation uses the focused named rating; no global numeric shortcuts may steal equation/text input. Touch buttons expose colour plus text, so colour is not the only meaning. Undo remains keyboard reachable after focus advances.

State: Disable duplicate submission while the same operation is unresolved. On network loss, retain a visible pending personal record and reconcile before advancing totals; never count both a retry and original. A content-revision change invalidates the captured rating target until reviewed again.

Integration and persistence: Persist principal, CardID, content revision, time, evidence type and explicit topic linkage. Valid Study links receive attributed evidence; unlinked cards keep private review without fabricated topic mastery. Undo/correction recalculates derived counts and schedule under the current policy.

Acceptance: Rate Confident, lose the acknowledgement, retry and then Undo. Exactly one attributable rating is recorded and superseded; the shared answer and another learner’s progress remain unchanged.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.apsnl5exdxdk"></a>

### IX-CARDS-005 — Card rail selection, creation, reorder and deletion

Controls: Add card; 1 ax² + bx + c · Standard form; 2 A useful first step · Completing the square; 3 Front + back · Quadratic formula; 4 b² − 4ac · The discriminant; 5 Read the graph · Number of roots; 6 Two linear factors · Factorising; card rail; More; Bulk edit

Atlas surfaces: cards-edit

Primary: Click a rail row selects its CardID and restores that card’s active-side editing state. Add card creates an empty Front/Back card after the active membership and focuses Front. Shift/Ctrl selection builds the explicit set for Bulk edit without overwriting the active card draft. More→Duplicate creates a new CardID with copied content and no copied private review history. Remove from set removes membership only; Delete card names all affected memberships and is a recoverable content deletion.

Secondary: Rail context includes Open/Edit, Duplicate, Move before/after, Remove from this set, Delete card and Copy link. A selected multi-set context must show the actual selected count. Secondary-click outside selection retargets the context without silently editing.

Drag: Explicit rail grip drag reorders membership with a before/after insertion marker and bounded scrolling. Drop commits order once; Escape cancels. Drag to another set must explicitly choose Reference existing card or Copy as new card; no identity change is inferred from location alone.

Keyboard and touch: Arrow keys focus rows, Enter selects, F2 edits the card label/metadata, and menu Move commands provide non-drag ordering. Touch long press opens commands; reorder uses a distinct handle.

State: Pending edits persist before switching cards or remain recoverable; failed save cannot silently discard the previous card draft. A removed/reordered target during drag cancels or refreshes the insertion point by identity.

Integration and persistence: Card position numbers are derived membership order, not CardIDs. Reordering preserves topic links, review records and both sides. Add/duplicate/remove/reorder each forms a coherent undo transaction.

Acceptance: Edit card three, reorder it to first, change grouping and reopen. Its content, two sides and private review link remain the same CardID; duplication creates a new ID without a copied rating.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.vw0pndoo2bz4"></a>

### IX-CARDS-006 — Rich Front/Back editing, backgrounds and Save

Controls: Card front; Card back; Show back of card; Front; Back; Flip to back; Flip to front; Montserrat; 24; Bold; Italic; Underline; Align centre; Warm paper card background; Sage card background; Lavender card background; White card background; Save; Undo card edit; Redo card edit

Atlas surfaces: cards-edit

Primary: Click content enters the active side’s rich-text editor; explicit Flip/Front/Back changes the active side and never flips because the text itself was clicked. Font, size, emphasis and alignment use IX-TEXT controls against the captured selection on that side. Background swatches set the selected card’s authored background choice as a card property. Save flushes the current set/card edits; Undo/Redo addresses the active content transaction, including side changes only when authored content was changed.

Secondary: Content context offers cut/copy/paste, formatting and insert commands. Background right-click permits Copy colour/Reset card background. Right-click the Flip control has no alternate mutation.

Drag: Text drag selects text; object drag moves that object within the active side’s supported layout. It must not turn into stack navigation or reorder the card. Flip and colour buttons are non-draggable.

Keyboard and touch: Keyboard rich-text/IME semantics follow [IX-TEXT-001](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.eipf3qfk5dm9)–007. Space inserts text when a caret owns focus; it must not flip. Flip has an explicit keyboard-focusable control. Touch supports text selection and independent flip buttons.

State: Unsaved state reflects actual durability. Changing the active card or side captures target IDs so an open formatting popup cannot apply to the wrong face. Read-only cards allow selection/copy and View but disable content/background mutation.

Integration and persistence: Front and Back are structured fields of one CardID, not separate cards. Formatting and background persist natively and remain independent of the interface theme. Save does not create review evidence; undoing a content edit does not undo a separate personal rating.

Acceptance: Bold text on Front, flip, add a Back paragraph and save. Reopen in review and verify both sides; pressing Space in the edit paragraph must add a space, not reveal the opposite face.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.hvna1b2poahf"></a>

### IX-CARDS-007 — Images, graphs, ink and formulas on a card side

Controls: Image; Graph; Ink; Formula; Insert on this side; inserted object handles

Atlas surfaces: cards-edit

Primary: Image opens an asset picker and preview/crop/alt-text workflow; Graph opens the canonical graph editor or an explicit existing graph reference; Ink enters a bounded editable drawing region; Formula opens the shared visual/LaTeX mathematical editor. Each command captures CardID, Front/Back and insertion anchor before opening a picker. Confirm inserts one semantic object on that side; Cancel leaves no placeholder. Double-click an inserted object reopens its owning editor.

Secondary: Object context provides Edit, Copy editable object, Copy reference when linked, Resize, Alt text and Delete from this side. Flatten/export image is explicitly named and reports loss of editability. Deleting the embed does not delete an independent graph/source artifact.

Drag: Handles resize within the card layout while preserving aspect where appropriate. Ink drag creates strokes only inside the drawing region. Typed asset drops use a visible insertion anchor and preserve semantic formats; unsupported drops leave the original untouched.

Keyboard and touch: Keyboard size/position fields and object-list selection substitute for handles. Formula visual and source modes edit the same expression. Touch uses enlarged handles and an explicit exit from Ink, preventing scroll gestures from becoming strokes.

State: An invalid formula retains its input and last valid rendered expression. Missing graph/media engines show a recoverable named unavailable state. If the user changes cards while a picker is open, confirm still targets the captured card or asks to retarget explicitly.

Integration and persistence: Objects preserve canonical identity/reference, editable metadata and source provenance. The card remains owned by Cards; Study consumes it. Insertion is one history transaction; a graph edit follows the graph owner’s revision rules.

Acceptance: Open Image on Front, switch the visible card in another view, then confirm. The image must appear only on the captured Front or the operation must stop with a clear retarget choice; it cannot land on the newly visible Back.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.y7fbagvly7fs"></a>

### IX-CARDS-008 — Bulk creation, editing and import/export mapping

Controls: Import; Bulk edit; Front column; Back column; metadata mapping; Preview changes; Apply bulk changes; Export set; rejected rows

Atlas surfaces: cards-study, cards-edit

Primary: Import chooses a source format, maps its fields to Front, Back and permitted metadata/assets, and displays a parsed-row preview before Create cards. Bulk edit captures selected CardIDs and offers explicit replace/append text, set property, set topic or membership operations with before/after examples. Apply commits the reviewed target set. Export selects native full-fidelity or a declared simpler format, includes selected/all cards explicitly and reports graphics/ink/structure losses before generating output.

Secondary: Preview-row context offers Inspect original, Exclude row and Copy diagnostic. Rejected rows can be repaired/reparsed; right-click cannot silently mark them successful. No custom secondary action on Apply.

Drag: Source files can be dropped only into the explicit Import target. Preview rows are not set-reorder handles. Column mapping uses dropdowns or labelled mapping targets with keyboard equivalents; ambiguous multi-field drops are rejected.

Keyboard and touch: Tab navigates mappings and per-row outcomes; Enter activates Preview or Apply only when its button is focused. Touch supports the same mapping controls without mandatory drag. Escape preserves a recoverable import draft while leaving the set unchanged.

State: Malformed rows, unresolved assets and unsupported fields remain inspectable. All-or-nothing commit is preferred; any supported partial result lists each created/skipped/failed item and its stable ID. Unknown completion reconciles the operation before retrying.

Integration and persistence: Bulk operations never copy other learners’ private reviews. A shared-card property update names that canonical target and affected memberships. Export cannot include private history by default. Undo restores the exact affected content/membership without removing unrelated concurrent edits.

Acceptance: Import five rows with one malformed Back and one missing image. Preview identifies both problems; a deliberate partial import produces explicit outcomes and retry cannot duplicate the three already-created cards.

Basis: Atlas cards-study/cards-review/cards-edit; expanded_editors.md ED-CARDS-001–006; retained-apps/cards.complete.md canonical content/review and focused-stack requirements. Visible labels are Atlas evidence; precise gestures, recovery and review dispatch below are target decisions.

<a id="h.bz5xeq9gxn8d"></a>

### IX-DATA-001 — Workbook command menus and command scope

Controls: File; Edit; Insert; Format; Data; Tools; View; Help

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: File opens New, Open, Import, Save, Save a copy and Export. Edit opens Undo/Redo, Cut/Copy/Paste/Paste special and Find/Replace against the focused grid/editor. Insert opens rows, columns, sheet, table and chart creation. Format opens cell text, number, fill/border, wrap, merge and row/column sizing. Data opens sort/filter, sources/connections, schema/relationships, pivot/cube, transformations and Visualise. Tools exposes Functions, SQL and the explicit Normalise/Flatten operations. View controls formula bar, gridlines, freeze panes and visible analytical panes. Help opens app/function documentation and capability information, without executing a formula/query.

Secondary: Menubar secondary-click has no alternate mutation. Menu items expose target scope and unavailable reasons; they must not use a generic handler that reports success for every fixture button. Close a submenu before navigating the outer menu.

Drag: Menubar is non-draggable. Moving across open top-level menus changes the displayed menu, not workbook content. Drag selection in a menu cannot become a sheet-range operation behind it.

Keyboard and touch: Keyboard menus follow [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk). Shortcuts dispatch to a live formula/text editor before workbook commands. Touch uses explicit submenu navigation and sufficiently large command rows.

State: Command enablement derives from selection, source type, write permission and installed capability. Unsupported analytical engines remain named/unavailable; spreadsheet actions cannot be routed to a connected database implicitly. A modal review captures its target even if visible sheet focus changes.

Integration and persistence: Every command addresses WorkbookID/SheetID or a separately named stable Data source/entity. View changes are personal view state; authored edits enter owning history. Menu placement does not create a second data model for the same table.

Acceptance: Open Data while editing a formula, close it and invoke Undo. The formula edit retains its own focus/history; the command must not undo an unrelated database update.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.lebee2kjq5zd"></a>

### IX-DATA-002 — Grid selection, value entry and clipboard rectangles

Controls: cells; active cell; F3:F9; row and column intersection; fill handle; Cut; Copy; Paste; Paste special

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Single-click selects a cell, Shift-click extends a rectangle, and Ctrl/Cmd-click explicitly adds a disjoint region where the command supports it. Double-click or F2 enters cell editing; typing while a cell is selected starts a replacement draft. Enter commits and moves down; Tab commits and moves right, with Shift reversing direction. Delete clears selected values under a named Clear contents action without deleting row identities. Paste previews incompatible dimensions/types and applies a rectangular typed payload to the captured destination.

Secondary: Cell context includes Cut, Copy, Paste/Paste special, Clear contents, Insert/Delete cells with shift direction, Format cells, Define name and Inspect value/source. A context click inside a selected range preserves that selection; outside it selects the intended cell first.

Drag: Dragging a selected range border moves/copies only after an explicit range-move affordance and overwrite preview. Dragging the fill handle extends a series/formula under a visible fill policy; absolute/mixed references remain correct. Regular cell drag selects a range. Escape cancels preview; drop commits one operation.

Keyboard and touch: Arrow keys move selection; Shift+Arrow extends; Ctrl/Cmd+Arrow moves to the next data boundary. IME composition cannot commit intermediate text. Touch uses range handles and explicit Fill/Move menus instead of precision handle requirements.

State: Protected cells, spill targets and connected read-only views reject edits with a specific reason. Multi-range commands either state their exact subset or require a compatible selection; they cannot edit a bounding rectangle silently.

Integration and persistence: Cell selection is view state. Value/paste/fill commits invalidate dependent formulas/charts once and are undoable. Clipboard uses typed values/formulas with explicit plain-text conversion; copying a database view does not move underlying records.

Acceptance: Copy a mixed-reference formula across three columns, then undo once. Relative references shift, absolute parts remain fixed, and all three cells return together without altering the source.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.96acegsvoj1"></a>

### IX-DATA-003 — Row/column headers, dimensions and structural changes

Controls: A–H column headers; 1–14 row headers; header boundary handles; Insert row; Delete row; Insert column; Delete column; Freeze panes; merged title cell

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Click a row/column header selects the whole addressed dimension; Shift extends a contiguous span and Ctrl/Cmd toggles explicit spans. Header-boundary double-click fits width/height to the declared content sample or complete available data, showing which scope was used. Insert/Delete row/column operates relative to the selected boundary and previews dependent formula/schema effects. Freeze panes chooses the current boundary and only changes the viewport. A merged title is one span with an identified anchor cell, not lost underlying values.

Secondary: Header context offers Insert before/after, Delete, Clear contents, Hide/Unhide, Resize, Auto-fit and Freeze. Sort appears for a data column with its affected table/range named. Merge cells warns about multiple populated cells and offers a content-preserving resolution before commit.

Drag: Drag header borders resizes the selected dimensions with a live size indicator and one commit. A dedicated header reorder grip moves columns/rows only in a model that permits structural reorder; a normal header drag extends selection. Reject drops through protected or incompatible regions and preserve stable table field/record identities.

Keyboard and touch: Keyboard header selection and numeric Resize provide parity. Touch handles are enlarged; context Insert/Move commands avoid mandatory drag. Escape restores the original size/order during a gesture.

State: Deletion with dependencies exposes references and the chosen referential action; an unknown impact is not displayed as zero. A filtered view must not silently delete hidden rows when the command names visible rows only.

Integration and persistence: Sheet structural edits update coordinate references according to the compatibility profile. Database sorting/reordering is a view operation unless an explicit schema/order mutation is chosen. Hiding preserves stored data; freeze/scroll never edits it.

Acceptance: Resize two selected columns and cancel, then delete a referenced column with a visible preview. Cancelled resize restores both widths, and deletion produces the declared reference result rather than shifting unrelated database keys.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.v4dmpkutq4jn"></a>

### IX-DATA-004 — Formula bar, range picker and function completion

Controls: F3:F9; fx; =C3*E3; Active cell F3; Functions; F10; =SUM(F3:F9); SUM; Tab to complete; Cancel formula edit; Commit formula

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: The name/range box selects a validated address or named range; it does not write a formula. The formula field edits the captured active cell, with fx/Functions opening searchable function help and argument insertion. The shown F10 editor captures F10 even if the background selection is F3:F9. Clicking a referenced range token highlights it. During formula editing, choosing cells/ranges inserts references at the caret. Commit formula validates and commits the draft; Cancel restores the prior formula/value.

Secondary: Text context provides copy/paste and function/reference help; a reference token offers Go to source, Toggle relative/absolute form and Remove reference. Right-click a grid cell while editing must explicitly choose Insert reference rather than accidentally open a destructive grid command.

Drag: Dragging over the grid in reference-pick mode inserts/adjusts the referenced range with coloured outlines. Dragging a token within formula text moves text only through normal editor semantics. Escape cancels the active range pick before cancelling the formula draft.

Keyboard and touch: Tab accepts the highlighted function completion; otherwise it follows the formula editor’s explicit commit/navigation rule. Enter commits when the expression is complete, Escape cancels, and F4 cycles relative/absolute reference form for the selected reference token as an adopted shortcut. Touch has explicit reference-pick and Commit buttons.

State: Parse errors retain typed input and identify the span. Valid formulas that evaluate to a typed error remain stored with that error; syntax failure cannot masquerade as zero. A stale target revision blocks overwrite and offers compare/rebase.

Integration and persistence: Committing targets one captured cell unless Fill/Apply to selection was explicitly chosen. It updates dependency edges atomically. Closing a helper popup preserves the formula draft; Cancel never writes a partial expression.

Acceptance: Open F10, choose SUM, pick F3:F9 and commit. Only F10 receives the formula; the selected source cells remain unchanged. Reopen, type an invalid expression and cancel to recover the prior formula.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.3dndqwkdg8uz"></a>

### IX-DATA-005 — Cell typography, fill, alignment and numeric formats

Controls: Montserrat; 11; Bold; Italic; Underline; Cell fill; Align left; Percentage; Format cells; number format; wrap; borders

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Font and size apply to the explicit cell range or active rich-text span when editing a cell. Bold/Italic/Underline toggle the captured target and show Mixed for mixed formatting. Cell fill changes background; Align left changes cell text alignment rather than object alignment. Percentage applies a display format to numeric values without multiplying stored data; additional Number/Currency/Date/Text choices are explicit format selections. Wrap changes layout; borders address specified edges. Merge follows [IX-DATA-003](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.96acegsvoj1) rather than a formatting shortcut that discards values.

Secondary: Format controls have no alternate secondary mutation; cell context opens Format cells. Colour picker offers literal colour, alpha where allowed and Clear fill. Copy format/Paste format is distinct from copying values.

Drag: Controls are non-draggable except colour/continuous preview sliders. Applying a swatch commits one format operation; dragging across cells selects a range rather than painting until an explicit format-painter tool is activated.

Keyboard and touch: Formatting shortcuts act on the active cell editor first. Numeric font sizes validate units; Escape cancels uncommitted fields. Touch presents selected/mixed state text and does not rely only on colour.

State: Text-formatted identifiers remain text; a display-format change cannot silently coerce long IDs or localised dates. Mixed type ranges show applicability and retain incompatible raw values. Unsupported format codes are preserved with a visible compatibility warning.

Integration and persistence: Formatting is authored workbook state and undoable separately from values. Chart calculations use typed values, not formatted strings. App theme changes cannot replace authored fills or create percentage/currency conversions.

Acceptance: Format numeric 0.064 as Percentage and a long text identifier as Text. The display changes appropriately, exported supported values retain their types, and Undo reverses formatting without recalculating either as a different value.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.h8n13kldwxtg"></a>

### IX-DATA-006 — Sheet tabs, creation and workbook summaries

Controls: Weekly sales; Products; Dashboard; New sheet; sheet tab context; 7 cells · Sum £49,905.30 · Average £7,129.33

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Click a sheet tab selects its SheetID and restores its own selection/scroll state. New sheet creates one blank sheet after the active tab, focuses its name and offers explicit table/other sheet-type choices through a creation menu where required. Double-click/F2 renames the tab without changing identity. The selection summary reports actual eligible selected values; click opens Count, numeric Count, Sum, Average, Minimum and Maximum choices with excluded/error counts visible.

Secondary: Tab context offers Rename, Duplicate, Move before/after, Hide, Delete and Copy sheet reference. Duplicate creates new owned cell/object identities and remaps internal sheet references. Delete shows dependent names/formulas/charts and remains recoverable. Summary context copies the calculated value with its unit/selection description.

Drag: Tab drag reorders within the same workbook using an insertion indicator and bounded autoscroll; cross-workbook transfer requires an explicit Copy sheet operation with dependency preview. Tab body dragging cannot retarget a running SQL query.

Keyboard and touch: Keyboard tabs follow roving focus; menu Move commands and touch controls provide non-drag reordering. Escape cancels rename/order previews. Summary values have accessible labels and units.

State: Pending formula edits must commit, cancel or remain recoverable before switching sheets; a background result returns to its original target, not the newly active tab. Mixed units cannot be summed as if equivalent.

Integration and persistence: Sheet order and names persist; scroll/selection is view state. Summaries derive from captured selection and update after recalculation, clearly showing stale calculations. A tab labelled Products may be a database projection; its actual type governs commands.

Acceptance: Start a query against Products, switch to Weekly sales and rename it. The query still returns to Products’ source identity; the renamed sheet’s selection summary remains correct.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.xc855ece2ik0"></a>

### IX-DATA-007 — Filtering and sorting visible data

Controls: Filter; filter header button; value checklist; filter condition; Apply filter; Clear filter; Sort ascending; Sort descending

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Filter opens rules for the selected table/range and identifies included headers. A header filter offers typed conditions and a value checklist; Apply changes the view, Clear restores visibility without clearing data. Ascending/Descending sort uses the chosen key/type/null policy; a multi-key sort editor records precedence. Sorting a selected column inside a table requires expansion to the full record range or an explicit isolated-range choice that warns about record misalignment.

Secondary: Header context exposes Sort, Filter by selected value, Clear this field’s filter and Inspect field. Right-clicking a displayed record does not remove it from the source. Filter menu search narrows the checklist, not the data until Apply.

Drag: Filter conditions may be reordered only when logical order has declared meaning; ordinary dropdown dragging scrolls. Sorting by dragging records is not assumed. Grid range handles select the scope before filter creation.

Keyboard and touch: Checkboxes and conditions support keyboard activation; Enter applies only from the explicit Apply action. Touch exposes full condition/value controls without hover. Escape discards an uncommitted filter draft.

State: Loading distinct values shows partial/loading state and supports server filtering without presenting incomplete values as exhaustive. Schema drift or a removed field invalidates that rule visibly. Errors retain the prior working view.

Integration and persistence: Database filters/sorts are query/view state and preserve stable record IDs. Saved view configuration is distinct from source mutation. Filtered fill/delete/export commands explicitly state visible-versus-all rows; hidden values cannot be changed accidentally.

Acceptance: Filter to three rows, select one record and sort descending. The selection follows that RecordID. Clearing the filter restores other rows with unchanged values and relationships.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.9qnfkfru77yt"></a>

### IX-DATA-008 — Native SQL mode, parameters and result handling

Controls: SQL; selected connection and dialect; query editor; parameters; Run query; Cancel query; results; execution history

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: SQL opens the native query workspace with a visibly selected target and dialect. Run captures text, parameters, connection and schema/source revision, parses every statement and dispatches to the real query engine. A proven read-only manual query executes under connection permissions without an AI approval dialog. Potentially mutating manual SQL shows exact target/effects and the required transaction/recovery plan through the normal execution flow. AI-originated mutations always use [IX-DATA-009](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.vooqmkajy7qg). Cancel stops pending execution where the engine can and reports the actual outcome.

Secondary: Query text has standard editing, Format query, Explain/Inspect plan and Copy query commands; result rows offer Copy values, Inspect source record and Materialise selected result. Secondary-click Run cannot bypass classification or change the target.

Drag: Editor text drag selects text. Result-column resizing changes view geometry; result rows may be dragged only as typed references into an explicit destination. Dragging a sheet tab cannot retarget the captured run.

Keyboard and touch: Keyboard Run requires query-editor focus and an explicit shortcut shown in the UI. Enter edits SQL text; it does not run. Touch has visible Run/Cancel and a target picker.

State: Unclassified statements are potentially mutating. Mixed batches expose statement-level effects, atomicity and partial completion. Error/timeout preserves the prior successful result labelled with its execution identity. Lost acknowledgement enters reconciliation; never blind-repeat a write.

Integration and persistence: Execution records retain target, statement outcomes and receipts. Results are typed/paged and distinct from materialised new data. A required protection failure blocks mutation. A changed active sheet or AI conversation does not change the submitted query.

Acceptance: Run a read-only query, then a failing query and switch sheets. The prior results stay labelled as prior execution, the failure is specific, and no model-generated text is displayed as native SQL results.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.vooqmkajy7qg"></a>

### IX-DATA-009 — Exact AI mutation review and recovery checkpoint

Controls: Review AI edit; Close review; Reject; Approve 12-row update; North shop / products; Production connection; Scope checked; Recovery checkpoint ready; Restore available in History

Atlas surfaces: data-approval, data, data-formula, data-chart

Primary: Review AI edit opens the immutable proposed operation: actual connection/database/table/field IDs, predicate/record set, before/after values, affected count, dependencies and recovery evidence. The fixture updates category on twelve products; the seven visible weekly-sales rows are not its target. Approve executes only that reviewed operation/revision scope. Reject records rejection and performs no write. Close review preserves a pending proposal for later inspection and is not approval or rejection. Recovery details open the verified checkpoint and restoration scope.

Secondary: Diff text supports selection/copy; context offers Inspect target schema and View affected records. Approval buttons have no secondary execution path. Copying a proposed SQL statement is not execution.

Drag: Review is non-draggable except ordinary text selection and panel resizing. Dropping data into it cannot enlarge the approved scope or change the proposed value.

Keyboard and touch: Focus lands on the review heading and target summary, then details and explicit Reject/Approve controls. Enter cannot approve from unrelated fields. Touch shows the complete target and operation count without truncating material scope.

State: Approve is disabled until exact scope and required recovery are verified. A stale row set, changed before-value, denied write, failed snapshot or changed operation invalidates the review and requires a fresh comparison. During execution prevent duplicate approval; uncertain outcome reconciles the same operation receipt.

Integration and persistence: AI Take actions is necessary eligibility, never blanket Data approval. Success creates a real execution/restore record and refreshes linked views; it does not treat the snapshot label as proof. Undo/restore of external writes uses a validated recovery operation, not a cosmetic local grid reversal.

Acceptance: Open the twelve-row proposal, change one matching record externally, then approve. Execution must stop for renewed review; Rejecting the refreshed proposal must leave all products unchanged.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.yyza8wmvsn19"></a>

### IX-DATA-010 — Chart creation, source mapping and persistent chart objects

Controls: Chart; Close Insert chart; Chart type; Column chart; Source range; Weekly sales!A2:A9, F2:F9; Category axis; Series; Use first row as headers; Show legend; Cancel; Insert chart

Atlas surfaces: data-chart, data, data-approval, data-formula

Primary: Chart opens a draft seeded from the selected range. Chart type chooses a registered type with validated input requirements. Source range edits explicit sheet/range references; Category axis and Series choose actual source fields and aggregation. Use first row as headers changes interpretation of the selected first row; Show legend changes presentation. Every change refreshes a labelled preview. Insert chart creates one persistent chart with source mappings and styling; Close/Cancel discards the draft. Double-click a committed chart reopens its properties.

Secondary: Preview has inspection/copy only; it is not a data mutation surface. A committed chart context offers Edit chart, Inspect source, Copy, Export image/vector and Delete chart. Deleting it removes the chart object, not source cells.

Drag: Draft controls are non-draggable except explicit range picking. A committed chart body/handles move/resize its object without changing source data. Series reorder uses an explicit ordered list; chart dragging cannot retarget it to a different sheet range.

Keyboard and touch: Source fields support direct address entry and keyboard range selection. Toggle semantics follow [IX-BASE-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.2bebxneic7h5). Escape exits a picker before cancelling the dialog. Touch has explicit numeric size and category/series controls.

State: Invalid/mismatched ranges, unavailable data and incompatible chart types block Insert with the relevant field highlighted. A changing source revision marks the preview stale and refreshes before commit. No placeholder chart is saved on cancelled or failed creation.

Integration and persistence: Chart identity, source refs, aggregation and authored colours persist. Source edits recalculate it; selecting a chart mark reveals underlying records when the mapping is reversible, otherwise explains aggregate provenance. The preview never changes the workbook theme or values.

Acceptance: Choose the shown day/revenue ranges, disable header interpretation and observe the diagnostic. Correct it, cancel, and verify no chart exists; repeat and Insert once to create one source-bound chart.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.wilieqkmawsw"></a>

### IX-DATA-011 — Structured cells, table schema and relationship inspection

Controls: Inspect value/source; expanded structured cell; Data → Schema; Data → Relationships; field type; nullability; primary key; lookup field; Normalise; Flatten

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Expand a structured cell to inspect its typed list/record/nested table/reference without replacing it with a summary string. Schema selects a stable field and edits type, nullability, default, validation and key role through a migration preview. Relationships selects source/target fields, cardinality and referential action in the same canonical schema. Normalise proposes tables/keys/mappings with explicit assumptions; Flatten proposes a projection with loss/duplication implications. Apply migration commits the reviewed schema/data changes; Cancel preserves them.

Secondary: Field context offers Rename, Inspect dependencies, Convert type, Set key and Delete field. Relationship context offers Inspect related records, Edit mapping and Remove relationship; removal is distinct from deleting either table. Nested-cell context distinguishes Edit owned value from Open referenced record.

Drag: Relationship drag begins at a named field port, highlights compatible targets and creates a draft edge only; Apply validates it. Moving ER nodes changes diagram layout, not schema. Nested-table row drag follows that table’s own identity/order rules.

Keyboard and touch: Keyboard source/target dropdowns replace edge dragging; nested values expose a breadcrumb and Back. Touch uses an explicit related-record picker. Escape leaves an inspection without mutating stored values.

State: Primary keys reject duplicates/nulls; type conversion lists rejected values. Unknown cascade impact stays unknown and blocks claims of zero affected rows. A missing referenced record is distinguishable from null. Concurrent schema changes invalidate previews.

Integration and persistence: Friendly labels never replace stable keys. Owned nested values follow parent lifecycle; references remain independent. A schema migration names historical/query/chart dependencies and required recovery. AI-originated mutations retain exact Data approval.

Acceptance: Rename a lookup label and sort its table. References remain attached to the same key. Attempt an invalid primary-key conversion; no schema or record changes are committed.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.jxth2nmr1ri6"></a>

### IX-DATA-012 — Pivots, cubes and analytical visualisation controls

Controls: Data → Pivot; Data → Cube; Visualise; Rows; Columns; Values; Filters; dimension members; aggregation; Add data; chart mark

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Pivot creates a draft configuration naming source, row/column/value/filter fields and aggregation. Cube selects explicit dimension members/hierarchies and measures; expanding a member changes the projection, not source records. Visualise opens an analytical lens with ranked valid forms and editable category/value/series/time/grouping mappings; choosing a form previews it without inserting a chart. Add data attaches another named range/source and validates joins/alignment. Save view persists that configuration; Insert chart is a separate operation.

Secondary: Field/member context offers Add to Rows/Columns/Values/Filters, Inspect source and Show underlying records. Aggregate marks expose calculation/provenance; unavailable row-level mapping is explained. Secondary-click cannot silently materialise a new table.

Drag: Field chips can drag between labelled mapping wells, showing compatible/invalid targets; dropping commits a view configuration change, not a source mutation. Reorder chips with clear markers. Legend/mark dragging has no effect unless a named selection brush tool is active.

Keyboard and touch: Keyboard menus add/move fields without drag; touch uses explicit target wells and Remove. Arrow navigation expands hierarchies and preserves stable member identity across sorting.

State: Prevent invalid aggregations, mixed-unit sums and averaging pre-aggregated percentages without an explicit weighting rule. Empty, missing and filtered-out values stay distinct. Failed refresh retains the last successful projection with its stale revision label.

Integration and persistence: Configurations reference canonical data; they are not copied sheets for each slice. Lens selection does not rewrite table order. Statistical anomalies are suggestions; no row is corrected merely because a plot highlights it.

Acceptance: Build a pivot from two measures with different units. The UI rejects a single unqualified sum; changing a dimension filter updates the projection while source record count and order remain unchanged.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.o4dp175njd7y"></a>

### IX-DATA-013 — Transform pipeline steps and refresh reconciliation

Controls: Data → Transformations; Add step; step list; step graph; Preview step; Refresh; Apply/materialise; schema drift warning

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Add step selects an explicit conversion, rename, split/merge, cleaning, filter, derived field, grouping, join, union, pivot/unpivot, deduplication or missing-value operation and opens its typed parameters. Click a step inspects its input/output; Preview step evaluates against captured inputs. List and graph views edit the same ordered/dependent pipeline. Refresh captures current sources and reports schema drift/local-edit conflicts. Apply/materialise writes only the reviewed output target; linked refresh and independent snapshot are distinct choices.

Secondary: Step context offers Edit, Duplicate, Disable/Enable where semantically valid, Move, Inspect input/output and Delete. Deleting a step previews downstream invalidation. Error context copies the actual diagnostic and reveals the failing input.

Drag: Step-list dragging reorders with type/dependency validation and a clear marker; graph edges connect compatible output/input ports. Reject cycles or incompatible schemas unless a specific bounded runtime supports them. Escape cancels; one drop creates one configuration transaction.

Keyboard and touch: Keyboard Move before/after and Connect output/input commands provide parity. Touch uses explicit connection pickers. Parameter editing cannot trigger a production write on Enter.

State: A failed step retains prior valid output with its revision label. Unresolved field remapping or source-access loss blocks refresh/application, not browsing old results. Long previews are cancellable and bounded.

Integration and persistence: Pipeline configuration is persistent authored state; preview results are derived. Linked outputs keep provenance and reconcile local changes; snapshots get new identities. Any AI-originated Data mutation still requires exact action review before applying output.

Acceptance: Rename a source field after a pipeline preview. Refresh must request a mapping or report drift; it cannot silently join on a different same-position field or overwrite the previous valid output.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.uxwqjinl9gxv"></a>

### IX-DATA-014 — Data import, export and explicit sharing

Controls: File → Import; Export; Share; delimiter; encoding; headers; date/number locale; type mapping; compatibility report

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Import chooses a source, delimiter/encoding/header/locale profile and typed column mapping, then previews values and rejected rows before committing to a new or explicitly selected destination. Export chooses native project or a declared spreadsheet/text/chart format, explicit sheets/tables/ranges and compatibility treatment. Share opens the artifact’s actual access scope, named recipients/link policy and permission level; applying that choice changes access without including connection credentials. Copy reference alone does not grant access.

Secondary: Preview rows offer Inspect original and Repair mapping. Export options can Copy compatibility report. Share context cannot grant permissions through right-click; document context can Copy artifact reference with access unchanged.

Drag: Files may drop into an explicit Import target. Dropping onto a grid requires a named import/paste decision and cannot replace the workbook blindly. Share and Export buttons are non-draggable.

Keyboard and touch: Keyboard mapping fields and preview navigation expose exact types; touch uses the same explicit Import/Export/Apply share actions. Escape cancels the pending operation without altering the source.

State: Preserve long identifiers, localised dates and formulas under the chosen profile. Unrepresentable charts/schema/cubes/queries produce an explicit loss report. Import failures keep rejected rows and original files. Export jobs report actual success/failure and allow cancellation.

Integration and persistence: Native export preserves models/relationships/queries without secrets. A compatibility output does not redefine native capabilities. Sharing a workbook is distinct from sharing permission to its live database connection; denied linked data remains bounded rather than being silently copied into the artifact.

Acceptance: Import identifiers with leading zeros and an ambiguous date, choose text and an explicit locale, then export CSV. The preview and output retain the chosen meaning, while native charts and queries remain intact.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.4vdac77rall"></a>

### IX-DATA-015 — Calculation states, spill conflicts and selection metrics

Controls: calculation mode; Recalculate; formula error indicator; spill range outline; stale result badge; selection summary

Atlas surfaces: data, data-approval, data-formula, data-chart

Primary: Automatic calculation schedules affected dependencies after committed edits; Manual mode visibly marks stale results until Recalculate is invoked. Clicking an error indicator explains parse/reference/type/divide-by-zero/circular/unavailable-function/spill-blocked causes and offers Reveal dependency or Open formula. Clicking a spill outline selects its owning formula; only the anchor edits the formula. The selection summary uses current typed computed values and identifies errors/exclusions rather than silently treating them as zero.

Secondary: Error context offers Copy diagnostic, Trace precedents/dependents and Edit formula. A blocked spill offers Reveal blocking cells; it must not offer unlabelled automatic overwrite. Calculation mode changes are explicit settings, not an error-dismiss action.

Drag: Dependency views may pan/rearrange nodes as layout only. A spill boundary is informative and cannot be resized to overwrite occupied cells. Grid selection/fill handles remain distinct from spill adorners.

Keyboard and touch: Keyboard can reach error details and dependency lists without pointer hover. Touch opens diagnostics on an explicit icon; dismissing a tooltip does not clear the underlying error.

State: Circular chains name the cycle; unavailable functions preserve formula text. Recalculation failure leaves identifiable last-valid results marked stale. A cancellation cannot publish partially calculated ranges as a coherent fresh snapshot.

Integration and persistence: Engine dependency updates are atomic with formula commits and scoped to affected nodes. Recalculation itself does not rewrite source formulas or unrelated workbooks. Cached values carry source/calculation revision so charts and exports can declare whether they are fresh.

Acceptance: Create a spill blocked by an occupied cell, clear the blocker deliberately and recalculate. The spill resolves without prior overwrite; Manual mode exports cannot label stale cached values as freshly calculated.

Basis: Atlas data/data-approval/data-formula/data-chart; expanded_editors.md ED-DATA-001–012; retained-apps/data.complete.md workbook, SQL, analytical model and explicit AI-review sections. Menu entries and gestures not drawn in the Atlas are adopted target specifications; the static fixture does not prove execution or database connectivity.

<a id="h.ee4wiar1nbge"></a>

### IX-FORMS-001 — Builder tabs, draft status and question focus

Controls: Build; Logic; Theme; Responses; Draft v6; Published v5 · 24 responses; Welcome; 1 Confidence check; 2 Solve the equation; 3 Explain your method; Finish; Assessment mode

Atlas surfaces: forms

Primary: Build shows the editable form canvas and contextual inspector; Logic opens the same form’s rules/graph; Theme opens authored appearance; Responses opens stored responses rather than sample preview data. Clicking a question-outline row selects its stable FieldID, scrolls it into view and updates the inspector. Welcome/Finish select their page content and completion behavior. Draft status opens draft revision information; Published status opens the active version and response availability. Assessment mode selects Form, Test, Quiz or an explicitly configured custom mode through a configuration preview.

Secondary: Question context exposes Rename label, Duplicate, Move, Copy question reference and Delete with dependency inspection. Status labels offer Inspect revision, not an implicit Publish. Right-click on a tab has no alternate command.

Drag: Tabs/status are non-draggable. Outline reorder uses [IX-FORMS-002](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.8jqufp3h4shh)’s explicit grip; ordinary dragging on question text selects text or scrolls rather than reorders.

Keyboard and touch: Keyboard tabs and outline rows use roving focus/Enter; selection is announced with question type and position. Touch makes each row/body distinct from its reorder handle. Escape closes an inspector popup before clearing question focus.

State: Selected question removal or schema conflict shows an explicit missing/stale target. A failed draft save remains visible while prior published status stays accurate. Switching tabs preserves uncommitted field text safely or requires its explicit resolution.

Integration and persistence: Builder selection and pane state are personal. Draft edits persist to FormProject only; Published v5 remains v5 until Publish. Response count comes from actual accepted responses, not preview interactions or fixture text.

Acceptance: Edit question two, switch to Logic and Responses, then return to Build. Its draft and stable FieldID remain, published content is unchanged, and no response is added.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.8jqufp3h4shh"></a>

### IX-FORMS-002 — Question/section creation, reordering and recovery

Controls: Add question; question outline grip; Add section; Duplicate question; Move question; Delete question; Welcome; Finish

Atlas surfaces: forms

Primary: Add question opens the typed field catalogue and inserts a selected type after the focused question or within an explicitly chosen section. It allocates one FieldID and opens relevant settings. Add section creates a PageID/section with its own title/ordering. Duplicate remaps owned option/criterion IDs and flags internal branch references for explicit retained/remapped choices. Delete shows logic, marking, binding and published-history dependencies; it removes the draft field only, preserving old version interpretation.

Secondary: Outline context provides Insert before/after, Duplicate, Move to section, Delete and Restore deleted question. Welcome/Finish cannot be deleted as ordinary answer fields; their content can be edited and optional display configured through named settings.

Drag: A question grip starts reorder after the shared threshold, showing destination section and insertion line. Hover-expansion/autoscroll is bounded. Drop commits order/membership once, retaining FieldID and rule references. Reject self/cyclic section drops and invalid mode constraints. Escape restores original order.

Keyboard and touch: Keyboard Move up/down/to section commands provide exact parity; touch reorder uses a distinct handle with scroll still available. Enter selects a field, not duplicates it.

State: A destination removed during drag cancels with recoverable selection. Deleted-question restoration handles a missing original parent by offering a named valid section. Duplicate operations reconcile uncertain completion before creating another field.

Integration and persistence: Changing question order does not reinterpret old response keys. Logic references stable identities, not question numbers. One structural command is one undo entry and updates canvas, outline, inspector and validation together.

Acceptance: Move question two before question one, preview a branch targeting it, then undo. The branch reaches the same FieldID throughout and historical responses remain interpretable.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.nx6xhl2hsw6g"></a>

### IX-FORMS-003 — Field types, choice options and repeating/table answers

Controls: Question type; field type catalogue; choice options; Add option; Remove option; repeating group; TableInput; column type; minimum/maximum rows

Atlas surfaces: forms

Primary: Question type opens typed text, number/currency, date/time, choice, rating/ranking, matrix/grid, upload, supported signature, person/reference lookup, calculated, mathematical/graph, repeating-group and TableInput entries. Changing an existing type previews answer-schema conversion and incompatible rules before Apply. Choice Add option allocates an OptionID; label edits preserve it. TableInput defines typed columns and respondent row limits/validation; a matrix is a separate type. Repeating groups define structured items, not flattened strings.

Secondary: Option/column context offers Rename, Duplicate, Move and Remove with dependency checks. Field context offers Inspect answer schema. Deleting an option identifies historical use rather than rewriting prior answers to another option with the same position.

Drag: Option/column grips reorder display order while retaining IDs. Respondent row movement follows an explicit order field where the form allows it; dragging cannot change a numeric answer or a lookup target. Palette drops show a validated insertion destination before allocating a field.

Keyboard and touch: Keyboard Add/Move/Remove commands replace all required drag operations. Type selection has searchable text names and capability explanations. Touch uses nested editors with clear Back, retaining the field draft.

State: Unsupported signatures/uploads/reference sources show capability unavailability. Conversion errors retain old schema and field content. A required field with no possible valid option blocks publication. Repeating/table constraints validate both cells and whole rows.

Integration and persistence: Typed schemas govern validation, marking and Data bindings. Labels/positions are not keys. Changing Draft schema cannot mutate historical FormVersion responses. Field catalogue availability does not grant access to a referenced people/database source.

Acceptance: Create a TableInput with numeric Amount and reference Category, add two response rows in preview and reorder columns. Values remain associated with ColumnIDs/RowIDs and are not converted into one text answer.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.x4zkblrzu0xv"></a>

### IX-FORMS-004 — Question content, required state, marks and accepted answer

Controls: question heading/help text; Not yet; Getting there; Confident; Your answer; Write your explanation…; Required; Yes; Marks; 2; Accepted answer; x = 5; Answer settings

Atlas surfaces: forms

Primary: Click question heading/help in Build enters authoring text; clicking a displayed answer sample selects its field, not a real respondent answer. Choice-label editing changes label content without creating a response. Required toggles required validation; Marks edits the field’s maximum points; Accepted answer edits the typed reference answer for the chosen marking method. The shown mathematical answer x=5 is authored rule input, distinct from the respondent’s Answer field in preview. Inspector updates apply to the captured FieldID.

Secondary: Content context provides rich-text formatting, link/asset insertion and Copy field reference. Numeric/answer fields retain text copy/paste. Right-click on Required or a sample option has no hidden submission/marking action.

Drag: Dragging text selects text; builder layout handles move/resize field presentation only within valid layout constraints. Dragging across answer samples cannot record a trial response. Option reordering uses the explicit grip in [IX-FORMS-003](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.nx6xhl2hsw6g).

Keyboard and touch: Enter commits a numeric setting or inserts text according to field role; multiline help uses normal editing. Escape restores an uncommitted inspector value. Touch distinguishes selected question state from respondent-style answer controls.

State: Marks must be finite/nonnegative under the mode policy; rubric totals cannot exceed the configured maximum silently. Required/hidden/default constraints are validated together. Invalid mathematical rule input retains the draft and last valid expression.

Integration and persistence: Content/setting commits are draft history. Required, unknown, blank and unavailable are distinct answer states. Topic links and points inform later attributed results but authoring or preview activity does not itself create Study evidence.

Acceptance: Change the accepted answer while clicking the builder’s sample choice. The draft rule changes once, no response exists, and the published version’s answer key remains unchanged.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.l1x8env4t34g"></a>

### IX-FORMS-005 — Marking-rule editor, testing and uncertain outcomes

Controls: Edit marking rules; accepted answers; regex pattern/flags; full/partial match; test case input; numeric tolerance/units; symbolic equivalence; AI rubric; human review; Save rules; Cancel

Atlas surfaces: forms

Primary: Edit marking rules opens the selected field’s versioned rule pipeline. Add accepted answer adds a distinct alternative; regex chooses full or partial matching with flags and test cases; numeric rules set tolerance and units; symbolic equivalence uses the maths engine; AI rubric defines criteria, points, examples and review policy; human marking creates a review task rather than a fabricated score. Test evaluates sample inputs without storing responses. Save commits the validated rule set; Cancel retains prior rules.

Secondary: Rule context offers Duplicate, Enable/Disable, Move earlier/later, Inspect test result and Delete. Criteria context preserves CriterionID on label edits. Secondary-click Test cannot publish or release a mark.

Drag: Rule/criterion grips reorder within typed constraints and show the resulting evaluation precedence. Dragging test output does not replace the accepted answer. Parameter fields support ordinary selection; numeric tolerance is not a visual guess from a chart.

Keyboard and touch: Keyboard ordering and direct numeric/source input provide parity. The mathematical visual and LaTeX/source editors share one expression. Touch test cases use explicit Run test and Save controls.

State: Invalid regex highlights the error and runs with bounded limits; timeout remains a distinct diagnostic. Engine/model unavailability produces NeedsReview or a blocked configured rule, not Incorrect. AI results store matched/missed criteria and uncertainty, not prose-only scoring.

Integration and persistence: Rules/model/rubric versions and provenance accompany real marks. Testing affects no grade or Study progress. Reordering a pipeline shows which stages may run; AI is invoked only where explicitly configured and source/permission policies permit.

Acceptance: Add a malformed regex and a symbolic equivalent answer test. Save is blocked for the invalid rule; fixing it allows tests, but neither test increases the live response count or releases a result.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.sx2pju6kpohc"></a>

### IX-FORMS-006 — Branching, mode logic and graph editing

Controls: Logic; Branch after answer; Next question; condition editor; target question/section; Open graph; logic ports; Validate logic

Atlas surfaces: forms

Primary: Branch after answer opens an explicit target picker including Next question, a named question/section and Finish where allowed. Conditional rules choose a typed answer/variable, operator and value, then target. Logic shows simple rules or the same typed graph; Open graph preserves their identities. Clicking a node selects it, double-click/Enter edits parameters, and Validate logic checks missing targets, incompatible types, unreachable states and uncontrolled loops. Graph preview runs against sample answers, not live responses.

Secondary: Rule/node context offers Edit, Duplicate, Disable where valid, Copy reference and Delete. Edge context offers Inspect condition and Disconnect. Removing a rule does not delete its target field. Right-click an event node cannot execute the live form.

Drag: Dragging from an output port creates a draft edge with compatible targets highlighted; drop validates and commits once. Node-body dragging changes layout only. Edge retargeting is atomic; rejected or cancelled drops retain the original connection. Rule-list reorder changes explicit precedence with a preview.

Keyboard and touch: Keyboard Connect/disconnect and source/target dropdowns replace mandatory edge dragging. Escape cancels the pending edge or parameter draft first. Touch uses sequential port selection and readable rule forms.

State: Hidden-answer policy is explicit: retain, clear on a defined transition, or exclude from submission; visibility alone cannot silently erase answers. Unknown/blank answer conditions are handled separately. Publish is blocked by invalid required logic, not by a merely folded graph view.

Integration and persistence: Simple controls and graph share one versioned model. Published logic remains pinned while Draft changes. UI hiding is not access control; protected answer keys/future fields are delivered only under runtime eligibility.

Acceptance: Create a branch to question three, reorder questions, and remove its target in Draft. The branch follows FieldID after reorder; deletion creates a named invalid dependency and prevents accidental publication.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.fazrzlfri4lx"></a>

### IX-FORMS-007 — Authored theme, layout and reusable component styling

Controls: Theme; typography; palette; background; content width; field/input appearance; buttons; progress/navigation; responsive preview; save theme preset; component overrides

Atlas surfaces: forms

Primary: Theme edits form typography, authored palette/background, surfaces, borders, radius/shadow, spacing, width, field/button appearance and progress/navigation style. Selecting a control previews the named draft scope—whole form, page or component override. Responsive preview chooses desktop/tablet/mobile/narrow widths without changing form schema. Save theme preset stores a reusable versioned definition; applying a preset copies declared tokens/overrides, with a preview of affected authored styles.

Secondary: Style-field context offers Reset override, Copy value and Inspect inherited token. Component context offers Edit instance or Edit reusable definition explicitly; it must not rewrite all instances because one was selected.

Drag: Layout handles change column widths/spacing in a preview with min/max constraints; drop commits a draft layout operation. Reordering fields uses the structure grip, not a style handle. Colour sliders follow preview/commit semantics and Escape rollback.

Keyboard and touch: Numeric widths/spacing, keyboard palette selection and explicit breakpoint previews provide alternatives to dragging. Touch keeps focus/error states visible at small widths; theme choice cannot remove logical focus order.

State: Invalid colour/size tokens retain the last valid value. Detect illegible contrast, overflow and inaccessible target sizes at the affected scope. Unsupported effects are reported and preserve their authored data; a renderer cannot silently replace the theme.

Integration and persistence: FormTheme is authored content, independent of the application’s Comic or other interface theme. Draft style edits do not change the published form until publication. Reusable components preserve field IDs/mappings and controlled overrides when restyled.

Acceptance: Change the form background and content width, inspect mobile preview and cancel a preset application. The two committed draft changes remain, field IDs are unchanged and the live published form retains its prior appearance.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.4n1amseud1ta"></a>

### IX-FORMS-008 — Respondent preview input, maths keyboard and navigation

Controls: Preview; Back to builder; Answer; Maths keyboard; Back; Next question; Question 2 of 3; Your answer is saved in this preview

Atlas surfaces: forms-preview, forms

Primary: Preview launches the selected draft revision in an isolated respondent session. Answer edits the typed mathematical response; Maths keyboard opens shared expression controls with visual/source parity and inserts at the current caret. Next question validates required/type constraints and follows the configured branch; Back returns to the previous eligible preview question, preserving entered answers under the mode’s navigation rules. Back to builder explicitly exits preview and restores author focus. The content Back button and host Back to builder have distinct target semantics; prototype navigation links do not define a real response engine.

Secondary: Answer text supports copy/paste; preview page context offers Restart preview and Inspect preview state to the author, never Submit as real response implicitly. Right-click navigation controls has no alternate action.

Drag: Answer text selection and equation selection remain within the field. Graph/table inputs use their canonical handles/row operations. Swiping a long question scrolls; it cannot advance past validation. Buttons are non-draggable.

Keyboard and touch: Enter in a mathematical input commits its expression editing state, not the whole assessment; Next is explicit unless an authored mode advertises a shortcut. Touch maths keyboard has accessible names and a dismiss control; dismissal preserves the input.

State: Invalid expressions show parse feedback and retain user text. Required failure focuses the relevant field. Mode restrictions explain a disabled Back. A newer draft revision does not silently replace the running preview; Restart with latest is explicit.

Integration and persistence: Preview answers persist only within the preview/recovery scope and do not create real ResponseIDs, grades, Data writes or Study evidence. Safe test bindings must be explicitly selected. Returning to Build preserves Draft and the currently published revision.

Acceptance: Enter 5, move Next, return Back and reopen the maths keyboard. The answer remains 5. Exit to builder and verify zero live responses and no published-version change.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.4zgevfkbgt6y"></a>

### IX-FORMS-009 — Publish, response availability and version continuity

Controls: Publish; Draft v6; Published v5 · 24 responses; audience/access settings; Open responses; Close responses; publish validation report

Atlas surfaces: forms

Primary: Publish opens a concrete summary of the draft revision, changed fields/logic/rules/theme, audience/access, response policy and required validation results. Publish this version creates/activates a new immutable FormVersionID once checks pass. Open/Close responses changes collection availability explicitly; closing does not delete accepted responses. Scheduled availability is configured only when the scheduling capability is available. Inspect published version opens its immutable definition without turning it into the editable Draft.

Secondary: Status context offers Inspect active version, Copy published link and View publication history. Copy link does not publish, change audience or open responses. Right-click Publish has no shortcut around validation.

Drag: Publication controls are non-draggable. Version rows are inspection/navigation items, not drag-to-activate targets. Selecting diff text does not acknowledge or dismiss validation failures.

Keyboard and touch: Keyboard focus moves through version/target/validation before the explicit Publish action. Escape cancels the pending publication, preserving Draft. Touch summary reveals material scope without hiding it under clipped text.

State: Invalid logic, unavailable required marking/input capabilities, broken bindings or unresolved schema changes block activation with actionable diagnostics. A failed publish leaves the prior live version available. Unknown acknowledgement reconciles the publication operation before retrying.

Integration and persistence: In-progress respondents remain on their starting version unless an explicit safe migration rule is configured and applied. Historical answers retain version meaning. Editing Draft, sharing an authoring reference and publishing to respondents are separate operations.

Acceptance: Attempt to publish a draft with a broken branch while v5 accepts responses. Publication fails with the branch identified; v5 remains available and an in-progress v5 attempt still uses its original question/answer mapping.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.sdmea25y2paj"></a>

### IX-FORMS-010 — Response lists, marking review and export

Controls: Responses; response filters; response row; answer detail; marking provenance; NeedsReview; Approve/adjust mark; release results; Export responses

Atlas surfaces: forms

Primary: Responses lists actual accepted/in-progress submissions by ResponseID and FormVersion, with filters for version, status, marking state and permitted participant scope. Single-click opens response detail; selecting an answer reveals the versioned question, typed answer, criteria and marking provenance. NeedsReview opens the human review workflow; an explicit mark adjustment records points/reason and reviewer. Release results applies the configured release policy to the named response set. Export responses selects fields/version scope/format and privacy-aware identity inclusion.

Secondary: Row context offers Open, Copy response reference, Review mark and Export selected. Deleting a response is a distinct retention-aware command naming attachments and downstream records; it is never a way to hide an uncertain mark. Secondary-click on a grade does not approve it.

Drag: Rows support ordinary selection and column resize; dragging answers cannot reorder questions or alter grades. Attachments use explicit download/open commands with source permissions. Bulk selection names exact response IDs before release/export.

Keyboard and touch: Keyboard row navigation and detailed criteria tables expose marks without hover. Touch provides explicit selection mode; tapping a row opens detail rather than toggling a hidden bulk state.

State: Pending submission, accepted response, marking pending, NeedsReview and Data-write failure remain different states. Unavailable AI/maths marking does not invent a score. Concurrent reviewer changes are compared by revision before applying another adjustment.

Integration and persistence: Mark adjustment/release are attributed events; response content is not rewritten. Study consumes canonical attempt/result evidence, not a second grade store. Export omits protected identity fields unless deliberately included under the user’s access.

Acceptance: Open an accepted response with a pending AI mark and failed Data sync. Review and release a human mark; the response remains accepted, the Data failure remains independently visible and no duplicate response is created.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.ttlgx1ys8vkq"></a>

### IX-FORMS-011 — Data-binding mapping and downstream recovery

Controls: Data bindings; direction; field/key mapping; manual/suggested/automatic/strict mode; Test binding; sync status; Retry Data write; Reconcile

Atlas surfaces: forms

Primary: Data bindings chooses the actual connection/table and explicit field/key mapping. Direction and manual/suggested/compatible automatic/strict mode state which side may change what. Repeating/TableInput mappings show parent and related child creation plans. Test binding runs in a visibly safe test context with no production writes. A response’s sync status opens the per-target receipt; Retry Data write retries only confirmed uncommitted work, while Reconcile checks an uncertain receipt before any repeat.

Secondary: Mapping context offers Inspect schema, Reveal field, Clear mapping and View affected versions. A response receipt offers Copy diagnostic and Inspect target record; it cannot silently delete an already accepted response.

Drag: Mapping lines may be dragged only between typed compatible fields with a clear draft preview; explicit dropdowns provide parity. Moving a field card changes diagram layout, not key mappings. Drop does not execute a write.

Keyboard and touch: Keyboard chooses source/target columns and validates mappings. Touch exposes complete field/type names. Escape cancels a mapping draft; connection permission prompts are handled by the shared connection flow, not guessed credentials.

State: Schema drift, key conflict and unavailable connection are distinct. If parent/child writes cannot be atomic, show each receipt and recovery plan. Lost acknowledgement must not create duplicate rows. AI-proposed mapping/data mutations retain Data’s exact action review.

Integration and persistence: Form submission durability and Data-write completion are separate. An accepted response remains available through downstream failures. Mapping/version changes preserve interpretation of historical answers and do not retarget queued writes to a new table silently.

Acceptance: Submit a repeating expense response, commit the parent write and lose a child acknowledgement. Reconcile identifies the existing child receipt and retries only missing work; the response and database contain no duplicates.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.

<a id="h.fo6vwd8xdico"></a>

### IX-FORMS-012 — Question banks, mode settings and live-session controls

Controls: Assessment mode; question bank selector; sampling constraints; question/option randomisation; attempt policy; timer; result-release policy; Host live session; Start; Next round; Reveal; End session; participant moderation

Atlas surfaces: forms

Primary: Mode settings chooses Form/Test/Quiz or a versioned custom state definition, then configures bank sampling, attempts, timers and result release. Question bank selection names stable bank/question versions and sampling constraints; preview shows a deterministic sample for its recorded seed. Host live session selects a published FormVersion and opens a separate session lobby. Start activates the eligible round, Next follows its state transition, Reveal releases permitted answers/results, and End closes that session without deleting its events or responses.

Secondary: Bank/question context offers Inspect, Add to assessment and Edit bank under the actual owner. Participant context offers permitted moderation actions with an attributed reason; no menu can secretly change score/time. A scoreboard cell is inspection, not an editable client-authoritative number.

Drag: Question/option reorder is authored order; randomised attempt order is stored separately and cannot be changed by dragging a live participant view. Team assignment drag, when offered, previews the named participant/team and commits an attributed host action.

Keyboard and touch: Keyboard and touch have explicit host controls; timer announcements are restrained. Respondent reconnection does not require repeating already-submitted answers. Host actions use clear state/eligibility labels rather than assuming every button is always valid.

State: Authoritative session state/clock rejects duplicate or stale events. AI marking delay follows the configured fairness/progression rule; it cannot silently become a speed penalty. Unsupported integrity capabilities are named unavailable, and focus events are contextual evidence rather than proof of misconduct.

Integration and persistence: LiveSession references canonical form/questions/marking and persists attempt sampling mappings. Clients cannot award points, extend time or reveal protected answers. Returning through Study or Play opens the same attempt/session evidence.

Acceptance: Sample a quiz, start a live round, disconnect a participant and reconnect before reveal. They retain the same questions, answer receipt and time eligibility; the client cannot change its score by replaying an old event.

Basis: Atlas forms/forms-preview; expanded_editors.md ED-FORMS-001–010; retained-apps/forms.complete.md field catalogue, marking, logic, themes, publication and response contracts. The two screens evidence the composition; detailed commands and additional panels below are target interactions, not proven runtime behavior.
