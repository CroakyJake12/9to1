# 11. Interaction rules

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.j832uc9l5tk

<a id="h.umcu21ynut2q"></a>

# 11. Interaction foundations and Haven controls

These records define the input-level behavior of the existing product. A control-specific record refines the shared pattern; it does not change canonical artifact ownership, grants or runtime contracts. Each gesture has a stated result or explicit no-action outcome. Screens are pinned Atlas references, not runtime acceptance evidence. Newly specified gestures and owner additions are normative target behavior.

Authority: [Master specification](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk); [pinned Interface Atlas v3.3](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html). Owner additions and the detailed gestures specified here are target requirements; static Atlas markup is design evidence, not proof of native execution. Related application/runtime requirements in the master remain in force.

Reading a record: Primary covers left click and activation; Secondary covers right click and contextual actions; Drag covers sources, targets and cancellation; Keyboard and touch supplies alternatives; State and integration defines scope, persistence and failure. Shared IX-BASE patterns apply unless the control record explicitly refines them. Acceptance describes required verification, not tests already performed.

<a id="h.42wlfc207wc6"></a>

### IX-BASE-001 — Command buttons and hit testing

Controls: Command button; Icon button; Nested trailing action

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Primary pointer-down focuses and arms the enabled button; pointer-up within its hit area invokes exactly one named command. Down outside followed by release inside is not activation. A rapid second click must not duplicate a still-pending save, send, purchase or execution. Double-click has no separate command unless a record explicitly defines it. A nested close, overflow or checkbox owns its input; its event must not activate the parent row.

Secondary: Right-click never invokes the primary command. A declared context menu targets the clicked identity without committing an edit; otherwise leave secondary activation inert. Middle-click is inert except for explicit navigation/tab records.

Drag: A command button is not a drag source. Pointer movement beyond the configured drag threshold cancels the armed click, unless this very control is a documented drag handle. Releasing outside or losing capture also cancels.

Keyboard and touch: Enter or Space on the focused button invokes once; holding a key does not repeat an irreversible command. Touch tap follows the same press/release boundary. Long press opens only a declared menu; release after it must not trigger the button. An icon-only control has a stable accessible name and hint.

State: Disabled, pressed, busy, selected and focused are distinct states. Keep an unavailable action discoverable with a reason. Pending results stay pending until the owning service acknowledges them; errors do not remove the target or draft.

Integration and persistence: Dispatch a typed command containing target identity and revision, not the current row index. Re-rendering a list between down and up must not redirect activation. Use the normal command service so alternative inputs share authorization, persistence and undo semantics.

Acceptance: Press an item, reorder the list remotely, then release: the original identity is acted on or activation cancels. Press a nested close icon: the parent must not open.

<a id="h.2bebxneic7h5"></a>

### IX-BASE-002 — Toggles, checkboxes and mutually exclusive options

Controls: Switch; Checkbox; Radio choice; Segmented selector

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Click an enabled checkbox or its associated label to invert once. An indeterminate selection becomes checked; a second activation clears it. A radio or segmented option selects that value without deselecting the group. Row whitespace activates only when explicitly associated with the control; embedded help links do not toggle it.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Space toggles a focused checkbox/switch. Arrow keys move among radio options and commit the newly selected option; Tab enters/leaves the group once. Tap is equivalent. A swipe scrolls the list rather than sweeping across switches.

State: Show pending versus durable state. On rejected writes restore the last durable value and retain an error beside the control. Dependent settings can be visually disabled while preserving their stored values; re-enabling a parent must not reset children.

Integration and persistence: Immediate settings commit one versioned preference change per gesture. Form-draft options change only the draft until the stated Save/Apply control. Group operations name the affected set and preserve mixed values until explicitly changed.

Acceptance: Tap label and switch separately; neither double-toggles. Disable a parent, change another preference, then re-enable: stored child choices survive.

<a id="h.gwmcnrcmysx1"></a>

### IX-BASE-003 — Text entry, search and suggestion fields

Controls: Text input; Search input; Multiline input; Clear

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Click places the caret; double-click selects a word and the platform triple-click selects its normal line/paragraph unit. Typing and paste change the draft. Search fields filter after the query changes, preserve focus and caret, and display a clear action only when nonempty. Clear removes the query, not the indexed content. Submit requires Enter or the named action; typing a URL never navigates by itself.

Secondary: Use the platform Cut, Copy, Paste, Select All and text-direction/spelling menu within permission constraints. Read-only fields allow Copy and Select All. A context menu never submits a query.

Drag: Dragging selected text uses the platform text move/copy operation within the field. External plain text drops insert at the indicated caret. File/object drops are rejected unless the field is explicitly an attachment/import target. A drag must not submit.

Keyboard and touch: Escape closes suggestions first, then restores a committed value when that field has one. Arrows navigate suggestions only when their list owns focus; Enter accepts a highlighted suggestion before a subsequent submission where acceptance merely edits the draft. Multiline Enter inserts a line break unless its record defines send. Touch selection handles retain native behavior.

State: IME composition is an unfinished edit; Enter while composing must not submit. Invalid input remains editable with a specific error. Search cancellation invalidates late results by query revision.

Integration and persistence: Search affects the displayed result set, not selection scope or data permissions. Preserve selected hidden items only when the UI exposes that selection count; never silently run a bulk action on filtered-out items.

Acceptance: Compose text with an IME, dismiss a suggestion and receive an older search response; no accidental submit or stale result replacement occurs.

<a id="h.vtjy0q6e2vlk"></a>

### IX-BASE-004 — Menus, dropdowns, disclosures and submenus

Controls: Overflow; Dropdown; Menu item; Disclosure chevron

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A click opens the declared popup anchored to its trigger; clicking the trigger again closes it. A disclosure expands inline content and keeps the trigger in place. A menu command runs the listed action once; a check/radio menu item changes its stated value. A submenu arrow opens a submenu rather than invoking a different command.

Secondary: Secondary click on an open menu item does not execute it or open a recursive menu. A context-menu request elsewhere retargets only after closing the old menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Enter/Space opens; arrows traverse enabled entries, Home/End reach ends, type-ahead finds labels. Right opens a submenu and Left returns. Escape closes the innermost popup and restores focus. Tap selects; touch opens submenus by tap rather than hover. A menu cannot depend solely on pointer hover.

State: Selected values have a checkmark and accessible state; disabled items retain reasons. Reconcile target deletion while open: disable stale actions and preserve the menu long enough to explain the change.

Integration and persistence: Only the topmost popup receives dismissal. Click outside dismisses a transient menu without activating an obscured destructive control. No menu gains permission merely by being visible; selection revalidates its command target.

Acceptance: Open nested menus by keyboard, delete their target elsewhere, then choose an action: stale work is rejected and focus returns safely.

<a id="h.a2i0alfkwp3d"></a>

### IX-BASE-005 — Rows, cards and multi-selection

Controls: Selectable row; Card; Selection checkbox; Header checkbox

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Where an explicit Open button exists, click the row to select and click Open or double-click the noninteractive body to open. A card whose entire surface is a labelled launch button opens on one click instead. Ctrl/Command-click toggles membership; Shift-click extends from the stable anchor through the current sorted set. The selection checkbox changes membership without opening.

Secondary: Right-click an unselected item selects that item and opens its item menu. Right-click within an existing multi-selection keeps the set and offers only valid shared actions. Menu text states the count; actions that require one item are unavailable with a reason.

Drag: Only a record that explicitly declares a draggable list supplies payloads. Dragging from a selected item moves the selected set; dragging an unselected item starts a single-item drag. Selection changes occur before drag admission, with the target set frozen for the gesture.

Keyboard and touch: Arrows move active row; Space toggles selection; Enter opens the active item. Ctrl/Command+A selects the shown query scope, with any server-side Select all results a separate labelled operation. Touch long press enters selection/menu mode; a vertical swipe scrolls.

State: Selection uses IDs and survives sorting. Removed/revoked items leave the action set with an announced update. Loading a next page does not silently select it.

Integration and persistence: Checkboxes, links and embedded actions do not bubble into row open. Bulk operations show per-item outcomes and keep failed items selected for retry.

Acceptance: Sort a multi-selected list and activate a row menu; the same object IDs remain the targets.

<a id="h.oj2yi5h8kpkm"></a>

### IX-BASE-006 — Drag and drop transaction

Controls: Drag handle; Reorder item; Drop target; Drag preview

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Pointer-down on a handle selects its declared source but commits no move. Start dragging only after the platform drag-distance threshold; where none is exposed use 6 logical pixels for mouse/pen and 8 for touch. The preview names the source/count and move/copy/link semantics. A click released before the threshold follows that handle’s non-drag action, usually selection only.

Secondary: Secondary click while idle opens the source menu if defined. During drag it cancels; it cannot drop and open a menu simultaneously.

Drag: Freeze source IDs/revisions and source container. Highlight only valid targets; show insertion marker, nesting or replacement explicitly. Reorder uses a line; nesting uses an outlined container. Edge dwell scrolls only the target scroller and stops immediately on exit. Escape, pointer cancellation, lost capture or release on an invalid target restores the pre-drag state. Commit only on valid release and authoritative acknowledgement; conflicts rebase by identity or show a conflict, never silently overwrite. Copy modifiers apply only to records that define copying; the cursor must show the actual operation.

Keyboard and touch: Every drag has named Move before/after, Move to, Resize or numeric alternatives. Touch drag starts from a visible handle; long press on a row without a handle opens its menu. Keyboard move mode announces source, target and position; Enter commits and Escape cancels.

State: Locked, read-only, revoked, busy or self-descendant targets are invalid with reasons. No synthetic drop success or source removal before destination acceptance.

Integration and persistence: After success restore focus to the moved identity and announce its destination. One gesture is one undo unit where reversible. Cross-owner copy/import records lineage; permissions and active execution never transfer because of a drag.

Acceptance: Drag across a scroller boundary, cancel, then repeat with the target deleted concurrently; neither attempt loses the source.

<a id="h.g4q68g51ix5k"></a>

### IX-BASE-007 — Dialogs and review sheets

Controls: Dialog close; Cancel; Apply; Save; Confirm

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Opening records the invoking target and focus. Cancel or close discards only the dialog’s uncommitted draft unless its owning record explicitly preserves a recoverable draft. In particular, dismissing the model/Context popup preserves its unapplied context value; dismissal never implies Apply. Apply/Save validates and commits exactly the displayed scope. Closing a progress dialog leaves its operation running unless a separate Cancel operation button is used. Confirm shows the affected identity and concrete effect.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Focus enters the first useful field or explanatory heading; Tab stays within a modal. Escape cancels a reversible draft and closes; it cannot imply acceptance. A destructive confirmation has no Enter default until its concrete action is focused. Mobile sheets expose explicit close/cancel; a dismiss gesture follows the same draft rules.

State: Validation stays beside fields. On save failure the dialog retains the edited values. An operation that has passed its cancellable point explains that state and offers status rather than a deceptive Cancel.

Integration and persistence: Background commands cannot receive clicks through a modal. After dismissal restore its trigger if present, otherwise the nearest owning heading/item. A remote revision change requires comparison/reload before overwriting.

Acceptance: Start a long operation, close its status sheet, reopen it: the same operation is still present rather than duplicated or cancelled.

<a id="h.5oiet5rdqq3c"></a>

### IX-BASE-008 — Sliders and continuous previews

Controls: Slider track; Slider thumb; Value field

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Track click moves to the nearest allowed value; dragging the thumb continuously updates a labelled preview. The owning record states its unit, range, step and whether release commits. Preview and committed values remain distinguishable. Double-click has no reset meaning; a named Reset provides that command.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: Capture the thumb once admitted; clamp at bounds and snap to the declared step. Release commits one change. Escape restores the value at drag start. A vertical page scroll starting outside the thumb does not alter the slider.

Keyboard and touch: Arrows move one step; Page Up/Down move ten steps within range; Home/End reach bounds. A paired numeric field offers exact entry. Touch uses a sufficiently large thumb and does not require precision placement.

State: An unavailable capability disables the slider with its reason and preserves the requested value separately from any effective value.

Integration and persistence: Do not run a resource allocation or network request for every pointer pixel. Coalesce previews and commit at the specified boundary. If resources reject the request retain truthful effective and requested values.

Acceptance: Drag rapidly while a backend response is delayed; the latest committed value wins without flooding commands. Across themes and hosted/standalone surfaces, verify the UI-013 thick pill-shaped track and substantial contrasting circular thumb for all first-party sliders, including styled native range inputs and Appearance/model settings. Preserve range, step, value, permissions, keyboard operation, interaction ownership and commit semantics. Authored document, media, artwork and guest-page content retain their own presentation.

<a id="h.v8vab0wwgfgp"></a>

### IX-BASE-009 — Focus and shortcut dispatch

Controls: Keyboard focus; Shortcut; Focus restoration

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Pointer focus follows the target without changing selection merely because the cursor hovers. Toolbar actions operate on the last valid editor selection while their own controls own keyboard focus.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Route shortcuts first to the focused text/IME/control, then active pane, then app, then host. Stop after one handler. Standard copy/cut/paste targets actual selection. Tab order follows semantic reading order, including overflowed controls through their menu. Platform Command substitutes for Control where conventional. Announce shortcut conflicts in remapping settings.

State: Focus rings remain visible independently of selected styling and theme. A disabled control has a discoverable explanation without trapping focus.

Integration and persistence: After deletion focus the next surviving sibling, then previous, then owning container. After an operation opens a new tab/pane move focus only if that activation was requested. Background results do not steal focus.

Acceptance: Invoke one shortcut from the nested editor and verify that the host does not also execute it.

<a id="h.b38hk5mijafm"></a>

### IX-BASE-010 — Touch, pen and responsive gesture parity

Controls: Tap; Long press; Touch scroll; Pen input

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Tap maps to primary activation. Long press on a declared item menu maps to secondary click; on plain buttons it does nothing except a descriptive hint. A long-press menu consumes release. Pen barrel-button maps to secondary input where exposed by the device.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: Vertical/horizontal movement in a scrolling region scrolls unless a visible handle or explicit drawing surface already captured the gesture. Do not overload the same undifferentiated swipe for navigation, delete and reorder. Edge resize handles have keyboard/numeric alternatives.

Keyboard and touch: Use the platform hold timing; fallback long press is 500 ms and cancels once movement exceeds the touch threshold. Screen-reader activation invokes the semantic command rather than synthesizing coordinates. Reduced-motion settings remove travel animation, not status feedback.

State: Responsive collapse moves commands into a named overflow without changing ownership or selected values. Buttons retain readable labels or accessible names; small icons get expanded hit areas.

Integration and persistence: Opening the software keyboard keeps the active input and primary submit action reachable. Fold/unfold preserves draft, selection, open review and current operation; no resubmission on layout change.

Acceptance: Long press then release on a mobile source row: its menu opens and the source is not also submitted or deleted.

<a id="h.apj0wbgoclc3"></a>

### IX-BASE-011 — Asynchronous commands and stale results

Controls: Busy action; Retry; Cancel operation; Status link

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Submitting records a command ID, target/revision and displayed scope. Retry targets the failed operation or its explicitly prepared replacement; it never repeats already successful external effects. Status opens the same command receipt. Cancel requests cancellation and waits for acknowledged state.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Idle, validating, queued, running, cancelling, succeeded, partially succeeded and failed are distinct. Unknown connection outcome is not failure or success; reconcile the original command ID before retry. A spinner does not replace essential cancel/status access.

Integration and persistence: Closing a tab or changing mode does not duplicate or erase admitted work. Older search/preview responses are ignored when their query revision is no longer current. Durable operations remain visible in the Action Log and their owning app.

Acceptance: Drop the connection just after submission; reconnect and retry produces one receipt and one external effect.

<a id="h.xj3xapubjjfz"></a>

### IX-BASE-012 — Numeric fields and steppers

Controls: Numeric field; Increment; Decrement; Unit selector

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Click edits the number; the plus/minus buttons change one declared step. By default Enter or blur commits valid input; an owning record with a named Apply/Save overrides that default. The context-limit field retains its draft on Enter/blur and changes allocation only through Apply and reload. Escape follows the owning draft-preservation rule. Invalid or out-of-range input stays as a marked draft rather than silently becoming zero. Unit changes preserve the represented quantity when conversion is defined.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Arrow keys step while the field is focused; a page wheel over an unfocused number scrolls the page. Keyboard entry accepts the configured decimal locale and shows the normalized unit. Touch opens an appropriate numeric keyboard; hold-repeat is allowed only for reversible stepping.

State: Mixed selection values display Mixed, not an arbitrary first object’s number. Entering a number explicitly assigns that value to the frozen selection.

Integration and persistence: One committed edit is one undo entry. Debounce live layout previews; changes cannot retarget another selection that arrived while the field draft was open.

Acceptance: Edit a mixed-width multi-selection, switch units and cancel: original per-object widths remain.

<a id="h.69w01b258ugu"></a>

### IX-BASE-013 — Navigation links and linked setting rows

Controls: Open; Review; Browse; Configure; Destination row

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A navigation command resolves the row’s canonical destination and target object, then opens its existing view. The label Review means opening a review surface, never approving the action. Configure opens editable configuration, never starts execution. The exact route and purpose are listed in the control register; no destination is inferred from a generic button label.

Secondary: A link menu offers Open in new tab, Copy link and Pin shortcut where that destination is independently addressable. It does not expose an unrelated object deletion. Middle-click or Ctrl/Command-click opens a background tab; Shift-click opens a new window when the host supports window creation and otherwise an explained new tab.

Drag: Addressable links may be dragged to a host tab strip to open, a bookmark list to add a reference, or an editor link target to insert a reference. No file bytes, secret or grant are embedded. Settings-navigation rows themselves are not reorderable.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: A route with a required but absent object first opens an object picker scoped to the owning app; it must not select a fixture or silently edit the last document. Missing routes give a named unavailable state.

Integration and persistence: Navigation preserves existing draft and does not automatically apply the destination’s primary action. Compatibility aliases resolve once to the current route and identity.

Acceptance: Use an app-setting link to a chart without a selected sheet: choose a sheet/selection; no random chart is created.

<a id="h.cb1djzj4tvqx"></a>

### IX-BASE-014 — Blank space, dividers and scrollbars

Controls: Empty toolbar space; Pane divider; Scrollbar; Background

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Clicking empty chrome focuses its container without invoking a hidden action. Clicking a canvas background uses that canvas’s selection/pan contract. A scrollbar track moves only its associated viewport.

Secondary: Empty host tab-strip space opens the host organization menu; empty editor space uses its canvas menu; other blank settings space has no custom menu.

Drag: Drag a divider only from its handle to resize adjacent panes; preserve minimum content widths and show a live boundary. Escape restores the initial ratio. Scrollbar-thumb dragging affects only scroll position. Double-click divider resets the documented default ratio; empty toolbar dragging never moves an artifact.

Keyboard and touch: Focusable dividers use arrows in the resize axis, Shift for larger increments, Home/End for allowed bounds. Touch handles have larger hit targets; pinch is reserved for declared canvas/document zoom, not arbitrary chrome scaling.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Persist layout per workspace/device class, not in document content. A breakpoint change keeps all panes reachable through named tabs or navigation.

Acceptance: Resize until minimum width, rotate the device and return: neither pane becomes inaccessible.

<a id="h.wju0avfyvtg0"></a>

### IX-BASE-015 — Reference-only samples and disabled fixtures

Controls: Theme preview; Component sample; Authored demo button; State illustration

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A visual sample demonstrates styling and state in its labelled preview. It may animate a reversible sample interaction; it does not install a package, send a message, delete real data or claim backend completion. A visibly unavailable sample remains unavailable. Production controls represented by that sample use their actual command records.

Secondary: No production command menu is created for decorative reference art. An image viewer can offer zoom and Copy image only when the actual image asset is available.

Drag: Samples do not become draggable product objects. Dragging an image preview pans its zoomed viewport only when the viewer says so.

Keyboard and touch: Focusable sample controls expose their demonstration purpose. Noninteractive decorative artwork is excluded from keyboard order. Tap cannot activate artwork depicting a button.

State: Reference-only themes and visual previews are not automatically registered built-ins. The Comic owner decision explicitly adds Comic; Industrial/Synthwave/Snug images retain their separately documented status.

Integration and persistence: The 18 design-system/theme-reference screens are governed by sample semantics. Authored third-party webpage buttons belong to that webpage; Haven cannot infer their business action from screenshot text.

Acceptance: Activate a Delete sample in Components: no real artifact changes.

<a id="h.aseep3t4mi4o"></a>

### IX-HOST-001 — Tab body and active tab

Controls: Tab icon; Tab label; Tab body

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: Single left-click selects that TabID, activates its pane and restores its last valid focus/scroll state. Clicking the already selected body focuses it without reloading. Double-click has no rename/close meaning. Middle-click on an unpinned ordinary tab requests close through [IX-HOST-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.7pwdlz7sqg89); a closable Home tab follows the same rule, while a pinned tab is not middle-click closed.

Secondary: Open the tab menu against the clicked TabID while preserving the previously active tab until an explicit action selects it. Commands: Activate; Duplicate view; Pin/Unpin; Move to group; Move to window; Split with…; Copy destination link when addressable; Close; Close other tabs; Close tabs to the right; Reopen last closed tab. Disable unavailable capabilities and identify pinned exclusions for bulk close.

Drag: The noninteractive tab body is the strip drag source. Cross the shared threshold before showing a ghost. Trailing close buttons and tab audio/media controls never start this drag. See [IX-HOST-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.op0l4utwzfwq)–006 for targets.

Keyboard and touch: The tab strip is one roving-focus group; arrows move focus and Enter activates, Ctrl+Tab cycles active tabs. Ctrl/Command+1–8 activates numbered tabs and +9 the last. Tap selects; long press opens the menu. Focused Close remains separately reachable.

State: Selected, keyboard-focused, loading, unsaved, attention-needed and playing-audio are separate indicators. Pinned tabs retain accessible full names. Missing/restoring views retain identity and recovery options.

Integration and persistence: Switching views never resends a message, applies an unsaved field or stops a run. Two tabs referencing one artifact keep one owner; Duplicate view must not copy its content.

Acceptance: Switch away during an admitted run and back; the same run and draft reappear without a second submission.

<a id="h.7pwdlz7sqg89"></a>

### IX-HOST-002 — Tab close, bulk close and reopen

Controls: Close tab; Close other tabs; Close tabs to the right; Reopen closed tab

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: Click the trailing close to request closure of that tab only, without first activating it. Closing the active tab selects the nearest surviving tab to its right, then left; if none remain create New Tab. Reopen restores the most recently closed recoverable view and its navigation context, never replays its last command. Bulk close enumerates the set at invocation and leaves pinned tabs unless explicitly included.

Secondary: The close icon has no separate destructive menu. Use the tab body menu for bulk operations. Middle-click is the same close request on ordinary unpinned tabs; no bypass of unsaved-work checks.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Ctrl/Command+W closes the active tab; Ctrl/Command+Shift+T reopens the last recoverable tab. Tap the separate close affordance; long press does not close. Unsaved-work dialogs follow [IX-BASE-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.g4q68g51ix5k).

State: If persistence is still safely owned by the service, closing the view is permitted. Otherwise show Save, Discard draft, Cancel for that document. Failed save keeps the view/draft reachable. Closing one view never discards another view’s shared session.

Integration and persistence: Closing a view does not cancel tasks, revoke sessions, delete an artifact or shut down a remote device. Bulk close stops at unresolved draft decisions and reports which views remain; already closed views can be reopened.

Acceptance: Close an inactive dirty tab with Cancel: the active tab and dirty tab remain unchanged.

<a id="h.op0l4utwzfwq"></a>

### IX-HOST-003 — Tab reordering and pinned boundaries

Controls: Tab drag; Tab insertion marker; Pin/Unpin

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: Pin/Unpin changes tab organization only. Pin places the tab at the end of the pinned region; Unpin puts it at the beginning of the ordinary region while keeping the active tab and artifact.

Secondary: During idle the tab menu offers Move left/Move right and Pin/Unpin. These are exact alternatives to strip drag, not commands that copy or reopen the tab.

Drag: Drag a tab body horizontally to an insertion marker between sibling tabs. Edge dwell scrolls the tab strip; it must not scroll the editor. A pinned tab reorders only within pinned tabs; crossing the boundary alone does not silently pin/unpin. Drop commits one stable-ID order transaction. Escape or an invalid release restores original order, active tab and pin state.

Keyboard and touch: Alt+Shift+Left/Right moves the focused tab one position within its region; an explicit Move to position command supports screen readers. Mobile uses a drag handle in the tab overview rather than compressing the desktop strip.

State: A tab whose view is busy may still be reordered. A removed source or destination cancels/reconciles the drag without dispatching commands against its new index.

Integration and persistence: Persist the session manifest order once per completed gesture. Reordering neither changes conversation context nor reloads renderers.

Acceptance: Drag a background tab over the active tab and cancel: active view and original order survive.

<a id="h.v1zskcga020y"></a>

### IX-HOST-004 — Tab groups and nesting targets

Controls: Group label; Group chevron; Group drop target; Move to group

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: Click a group label selects its last active member; its chevron only collapses/expands the member strip. A collapsed group can retain an active child indicator. Rename edits the group label on Enter; Escape cancels. Group color is organizational and cannot replace tab state indicators.

Secondary: Menu: Rename; Choose color; New tab in group; Collapse/Expand; Ungroup; Move group to window; Close group. Ungroup preserves every member view; Close group closes its member views through the normal draft path.

Drag: Dragging a group handle moves the whole ordered group. Dragging a tab onto a group’s explicit central target changes membership; a marker outside the group moves it before/after. Hover a collapsed group for 500 ms to preview expansion; only drop commits membership. No nested groups are created.

Keyboard and touch: Group menu has Move into group and Remove from group alternatives. Tap chevron toggles visibility, long press label opens the menu; neither launches a member action.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Group membership is stored in the host session. It is not a permission boundary, shared folder or conversation source scope. Ungroup and close have independent semantics.

Acceptance: Drop a tab over the gap beside a collapsed group: it must not become a member unless the nesting target was highlighted.

<a id="h.yzyv7o406wuo"></a>

### IX-HOST-005 — Tab transfer to another window

Controls: Move to window; New window; Detached tab drag

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: Move to window lists compatible existing windows and New window. Choosing a target transfers the view reference/session; its old position remains until the destination acknowledges acceptance. New window starts with that tab, not an extra document copy.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: Dragging outside the strip can show a separate-window preview only when the platform host supports window creation. Drop onto another host’s tab strip uses its insertion target and preserves profile, artifact, group eligibility and current operation. Release over arbitrary desktop/app content cancels unless a valid new-window preview was active. Unsupported mobile/window hosts expose Move to tab group instead of pretending to detach.

Keyboard and touch: The menu provides the complete transfer path without dragging. Touch uses the tab overview action. Escape cancels a transfer preview; system window close cannot commit a pending transfer twice.

State: Source remains recoverable until destination adoption completes. If renderer transfer is unavailable, remount the same canonical view without repeating actions. A failed transfer leaves the original tab usable.

Integration and persistence: Transfer does not move stored files, change browser profile, grant another window additional scopes or migrate a GPU task implicitly.

Acceptance: Close the destination window during transfer: the tab remains available in the source or acknowledged recovery state.

<a id="h.fsut3a7qcnf3"></a>

### IX-HOST-006 — Split views and tab drop zones

Controls: Split view; Split with; Pane activation; Divider; Exit split

Atlas surfaces: host-browser

Primary: Split view opens a picker for the second existing tab or destination. Clicking a pane makes it the sole target for pane-local commands. Exit split restores constituent tabs and preserves both view states. Close pane removes that pane from the arrangement; closing its tab remains a separate command.

Secondary: Pane header menu offers Swap sides, Open as separate tab, Close pane and Exit split. The divider menu offers Equal widths and the supported orientation; it does not merge documents.

Drag: Drag a tab onto a visible left/right or top/bottom split target to place its existing view there. Centre target replaces the pane view only after visibly identifying the displaced tab, which remains in the strip. A tab reorder insertion line cannot be interpreted as a split target. Divider uses [IX-BASE-014](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.cb1djzj4tvqx).

Keyboard and touch: F6 cycles major pane regions; a named Switch pane shortcut follows the active pane. The second-tab picker and Equal widths action are available by keyboard and touch. Narrow screens expose pane switching while retaining both states.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Each pane retains independent history, editor selection and browser profile. Shared host actions resolve the active pane exactly once. Dropping an artifact between panes uses the destination app’s link/copy semantics, not automatic ownership transfer.

Acceptance: Use Undo with the right pane focused: only its owning editor changes.

<a id="h.2lu0erxskeje"></a>

### IX-HOST-007 — New tab, tab search and tab overview

Controls: New tab; Search tabs; Tab search result; Close result

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: New tab creates and activates one empty New Tab. Search tabs opens a searchable list of existing TabIDs with group/window and dirty/run indicators. Enter or clicking a result activates that existing tab; its trailing close invokes [IX-HOST-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.7pwdlz7sqg89) without activating the row. Search uses titles and allowed destination text, not private document-content indexing.

Secondary: A result exposes the tab menu; New tab itself has no secondary creation shortcut. Middle-click New tab can create a background tab only where that explicit action is advertised; otherwise it is inert.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Ctrl/Command+T creates a tab; tab search has a remappable host shortcut shown in its tooltip. Escape closes the search and restores the prior tab focus. Mobile overview has separate New, Activate and Close targets.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: An empty result offers Create New Tab as an explicit action; it does not automatically run the query on the web or ask AI. Search draft survives result refresh.

Acceptance: Close a tab from search results and ensure the row’s activation does not also fire.

<a id="h.35b5mrzdf5hl"></a>

### IX-HOST-008 — Address mode and address editor

Controls: Address mode; Address field; Go; Location suggestion

Atlas surfaces: host-browser, files, files-grid

Primary: Mode chooser selects 9to1 (internal), Files or Web search interpretation while preserving the address draft. Clicking the address focuses/selects the displayed location for editing; a subsequent click places the caret. Enter/Go resolves and commits the typed destination to the active TabID. Selecting a suggestion uses its explicit kind: a destination navigates, a text-completion suggestion only fills the field. Escape restores the committed location after dismissing suggestions.

Secondary: Native text-edit menu plus Copy committed address. Paste and go is a separate explicitly labelled menu command, never the default Paste behavior. The mode button itself has only its normal menu.

Drag: Dragging selected address text exports text/URI only. A valid URI/text drop edits the address draft and requires submission; it cannot navigate on drop. File drops resolve a location preview in Files mode and require explicit Open. Tab/credential objects are rejected.

Keyboard and touch: Ctrl/Command+L focuses the address. IME-aware Enter follows [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1). Touch focus selects text and keeps Go reachable; long press offers native text editing.

State: Invalid schemes, unavailable handlers and inaccessible files retain the draft and explain why navigation failed. The real engine/profile and committed location remain visible.

Integration and persistence: Files mode opens the canonical Files location; web mode uses the selected browser profile. Switching mode does not grant native privileges, install handlers or submit personal context to AI.

Acceptance: Drop a URL into the address then press Escape: no request was sent and the old location remains.

<a id="h.xu84phecojhm"></a>

### IX-HOST-009 — Back, forward, reload and stop

Controls: Back; Forward; Reload; Stop loading

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: Back/Forward navigate the active tab’s own committed history without creating a new entry. Reload refreshes that view’s resource snapshot; while a web navigation is pending the same position shows Stop, which requests cancellation. Refreshing a native editor reloads shared read state and reconciles dirty edits rather than discarding them.

Secondary: Back/Forward open their available history lists, showing titles and locations; selecting an entry jumps within that tab. Reload has a web-only menu for Normal reload and Reload without cache where the engine provides that command; absent capabilities are unavailable with a reason.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Alt+Left/Right navigate history; Ctrl/Command+R reloads; Escape stops a pending web load only when no modal, menu, IME or drag owns Escape. Long press Back/Forward opens the history menu.

State: Unavailable Back/Forward remain disabled. Stop distinguishes request cancellation from already completed page activity. Failed navigation preserves a recoverable last page and editable destination.

Integration and persistence: None of these commands automatically resubmit a non-idempotent form or repeat an admitted app action. A required form resubmission uses the engine’s explicit review.

Acceptance: Reload a tab with an unsaved document draft and a completed send receipt: neither draft loss nor repeated send occurs.

<a id="h.d060z3c2jzsn"></a>

### IX-HOST-010 — Document identity and tools disclosure

Controls: Document title; Save state; Document tools; Settings tools

Atlas surfaces: home-apps, host-browser, host-newtab

Primary: Click document title opens the owning artifact identity menu; Rename edits only the title on explicit commit. Click save state opens actual persistence details and recovery when needed. Document tools discloses commands moved from the editor header, preserving their exact owner/action. Settings tools contains settings operations, not fake document history/comments.

Secondary: Document title menu may expose Copy link, Open containing location and Rename for the identified artifact. Save-state text offers Copy status only; it cannot force a save by right-click. Menu commands use the relevant editor record.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Saved locally, saving, sync pending, synced, conflict and save failed remain distinct. The title may show a draft name before a durable artifact exists; a draft must not imply a saved file.

Integration and persistence: Final Boards composition keeps board identity and save state in its sidebar; the host must not add duplicate controls. Generic Version history and Comments wrappers forward to the editor-family records; app-specific primary actions remain direct.

Acceptance: Open Document tools from two views of different files: each menu retains its own artifact ID.

<a id="h.20f3efbn4gcd"></a>

### IX-HOST-011 — Contextual AI bar and editor access

Controls: Open contextual AI; AI access: Read-only; Take actions; Start voice; Off/Floating/Sidebar

Atlas surfaces: home-library, settings-layout, write-editor

Primary: AI opens the existing conversation scoped to the active app/object selection; it does not send a prompt. Read-only/Take actions opens the current access menu and requests that access mode for future eligible steps. Start voice opens the source-aware voice session entry; capture begins only through the session’s explicit start control. Position setting moves or hides this bar while preserving its conversation and draft.

Secondary: Bar background menu offers Position: Off, Floating, Sidebar and Open conversation in Spaces. Individual Send/Voice buttons do not expose undocumented secondary commands.

Drag: Only a visible floating-bar handle moves the bar inside its safe viewport; text fields retain text selection. Drop near the named Sidebar zone previews docking; dropping elsewhere stores the floating offset. A resize handle controls the expanded conversation pane, not source scope.

Keyboard and touch: The AI button is reachable without relying on a global shortcut. Escape closes overlays before collapsing the bar. Touch drag uses the handle; keyboard Move/Dock/Reset position actions provide equivalents.

State: Take actions expresses requested access and does not override file permissions or action approvals. Unavailable capture shows the missing source/permission. Hiding the bar does not cancel admitted work.

Integration and persistence: Mode, placement and theme cannot change source binding, memory policy, tools or granted capabilities. The conversation carries its original ownership and history when opened in Spaces.

Acceptance: Move a source-bound chat from Floating to Sidebar, then enable Take actions: its corpus and web prohibition remain unchanged.

<a id="h.2xlp3zfzez5d"></a>

### IX-HOST-012 — Notifications menu and notification entries

Controls: Notifications; All; Needs attention; Mark all as read; Close notifications; Notification row

Atlas surfaces: host-notifications

Primary: Bell opens the notification surface without marking everything read. All and Needs attention filter the same receipt collection. Clicking a row opens its exact task/event/artifact review and marks that notice read. Mark all as read affects current readable notifications, not the underlying tasks, approvals or failures. Close dismisses and restores the bell.

Secondary: Row menu: Open; Mark read/Unread; Dismiss notice; Notification settings for its source. Dismiss removes the notification presentation only. Approval needed rows expose Review, never an unreviewed Approve shortcut.

Drag: Notification rows cannot be reordered or dragged into an approval. Swipe dismissal is available only through an explicit revealed Dismiss action; it does not accept the underlying request.

Keyboard and touch: Arrows traverse rows, Enter opens, Escape dismisses. Long press opens the row menu. Notification arrival announces without stealing current input focus.

State: Counts derive from current readable notices; stale approvals show their resolved state and open their receipt. Failed source loads retain last-known entries with freshness metadata.

Integration and persistence: Sound is emitted on the committed event according to event-sound settings, not again whenever the menu renders or history synchronizes.

Acceptance: Mark an approval notification read: the task still waits for the same explicit approval.

<a id="h.gp7jnqasyfuz"></a>

### IX-HOST-013 — Account menu and profile switching

Controls: Account avatar; Account settings; Profile selector; Sign out

Atlas surfaces: account, settings-account, home-apps

Primary: Avatar opens the account menu for the active account. Account settings navigates directly to its profile/resources page. Switching a profile rebinds the permitted workspace and visibly reports pending unsaved work; it does not mix browser credentials or conversation memory. Sign out targets the selected session only.

Secondary: Avatar has no separate right-click action. Within the menu, Copy account ID is available where useful, with secrets and credentials excluded.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: A local-only profile remains a usable identity without a signed-in service. Expired login is shown as expired, never as a successfully synchronized account.

Integration and persistence: Closing the account menu has no session effect. Logout preserves already durable local artifacts under their owner and handles inaccessible cloud views explicitly.

Acceptance: Open a second account menu while an unsaved local file exists; switching cannot silently discard or migrate it.

<a id="h.hlhkojlqnqll"></a>

### IX-HOST-014 — Browser menu, profile, engine and site permissions

Controls: Browser menu; Engine; Browser profile; Site information; Permission toggle

Atlas surfaces: host-browser

Primary: Browser menu opens actions for the active web tab. Engine selection requests a compatible renderer and preserves the actual destination; profile selection opens/rebinds a tab under that explicit profile without copying another profile’s cookies. Site information shows committed origin/security and per-origin permissions; changing a permission affects that origin and declared device/profile scope.

Secondary: Site identity right-click may Copy origin. Engine/profile buttons use their normal menus; secondary click does not reset permissions.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Unavailable engines show installation/repair requirements. Incompatible session transfers require a new navigation with visible state implications. Permission UI displays actual engine/system state and a request/blocked distinction.

Integration and persistence: Host theme changes browser chrome only. An inspector screenshot is not proof of live JS execution or system permission. Revocation stops future eligible capture/input actions through the owning engine, with in-flight limitations visible.

Acceptance: Change theme and engine choice independently; the webpage’s authored style and profile credentials are not silently rewritten.

<a id="h.e93bdgq4u1rd"></a>

### IX-HOST-015 — History and bookmarks

Controls: History entry; Clear history; Bookmark; Add bookmark; Bookmark folder; Remove bookmark

Atlas surfaces: host-browser-history, host-browser-bookmarks, settings-haven-bookmarks

Primary: Click an entry opens its destination with the captured profile boundary. Clear history opens a scoped deletion review with time range and profile; it removes browsing history records, not saved artifacts or current tabs. Add bookmark saves the committed address/title in the chosen folder. Rename edits bookmark metadata; Remove bookmark deletes the reference only.

Secondary: Entry menu: Open in new tab/window; Copy link; Add bookmark for history; Rename/Move/Remove for bookmarks. For a missing native artifact, offer Open owner/location instead of a fake page.

Drag: Bookmarks reorder by insertion line and nest into highlighted folders, rejecting descendants and incompatible types. Dragging a history entry into bookmarks creates a reference, not a history move. Dragging either to tab strip opens a view. Copy/link cursor states identify the operation.

Keyboard and touch: Keyboard Move to folder and Move up/down mirror drag. Long press opens entry menus. Search and selection use [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/005.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: History is scoped per browser/workspace policy. Private/Temporary destinations are excluded where their retention rules require. A native saved shortcut keeps canonical identity rather than a fixture URL.

Acceptance: Delete a bookmark for a document: the document and its open tab remain.

<a id="h.xfx3vy41nkqj"></a>

### IX-HOST-016 — HTML and CUI inspection panels

Controls: HTML; CUI; Refresh snapshot; Element tree; Filter elements; Computed; Attributes; Return to page

Atlas surfaces: host-devtools-html, host-devtools-cui

Primary: HTML/CUI selects the corresponding representation of the same identified target. Refresh snapshot reads a new bounded snapshot and labels its capture revision. Tree selection shows properties for that node; chevron expands its children without selecting another node. Computed/Attributes switches detail views. Return to page activates the captured target tab, not whichever route happens to be current.

Secondary: Node menu: Copy selector/path, Copy available markup/properties, Reveal in captured tree. Mutation actions are absent from the read-only inspector. A detached node remains labelled stale.

Drag: Tree nodes are not draggable live UI objects. Text/property selection can be copied. Divider resizing follows [IX-BASE-014](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.cb1djzj4tvqx); dropping a node onto an app cannot modify it.

Keyboard and touch: Tree arrows expand/collapse and traverse parents/children; Enter selects. Search retains focus. Touch chevrons have separate hit regions from row labels.

State: Unavailable native/engine properties show Unknown or Not exposed. A CUI snapshot cannot claim private runtime state absent an adapter.

Integration and persistence: Refresh may invalidate a selected node; retain its path and explain missing selection rather than selecting a same-index replacement. Inspection grants no edit/eval permission.

Acceptance: Refresh after the selected node disappears: property details must not show another node as if unchanged.

<a id="h.qs6igsjxsnax"></a>

### IX-HOST-017 — Bounded inspector query console

Controls: Console command; Clear console; Query result

Atlas surfaces: host-devtools-html, host-devtools-cui

Primary: Enter submits only the advertised bounded query language against the captured snapshot. Supported commands such as help, document.title, $0 and query selector return described read-only values. Unknown syntax is rejected with help; the preview must not evaluate arbitrary JavaScript. Clear console removes displayed query history for this inspector session.

Secondary: Native text edit menu and Copy result are available. No Run as administrator or Evaluate selection is implied by right-click.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Enter runs a query after IME composition ends; arrows recall entered queries only when the caret is in the command field. Escape clears an unfinished autocomplete popup before touching text.

State: Results name target/snapshot revision. A live-query adapter, if separately implemented, must declare its language and execution boundary before any additional commands become available.

Integration and persistence: Console clear does not clear app logs, browser history or the canonical Action Log. Query results are diagnostic, not evidence that a proposed code change ran.

Acceptance: Enter a JS mutation expression: the read-only query console rejects it and the app remains unchanged.

<a id="h.3brmldisktg3"></a>

### IX-HOST-018 — Window controls and background title region

Controls: Minimize; Maximize/Restore; Close window; Window title drag

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Minimize hides the window through the operating system without closing views or cancelling work. Maximize/Restore toggles the supported window geometry. Close window requests closure of all its views through the same unsaved-work handling as tab close. Double-click on the empty draggable title region toggles maximize/restore where the platform convention provides it; a double-click on a tab or embedded control does not.

Secondary: The empty title region opens the platform window menu with available move/size/minimize/maximize/close actions. It is distinct from the tab-strip organization menu and never replaces a tab-specific menu.

Drag: Drag only the designated noninteractive title region to move the window; platform snapping remains available. Tab bodies drag tabs, not windows. Window controls cannot become drag sources. Mobile hosts omit nonexistent window controls.

Keyboard and touch: Use platform window shortcuts and the accessible window menu. Touch title dragging is available only on hosts with floating windows; otherwise use the supported app/window navigation.

State: Window geometry is separate from maximized, full-screen and minimized state. Close failure leaves the affected draft and window/recovery reachable.

Integration and persistence: Closing the last visible window follows Keep Dulche available and background-task policy; it does not silently terminate admitted service-owned jobs. Window placement persists per device/display configuration.

Acceptance: Minimize a running operation and restore the window: it has the same receipt and current state.

<a id="h.jtoawfnk5d0s"></a>

### IX-HOST-019 — Account and browser tool submenu commands

Controls: Profiles; Browser Tools; Manage Page; AI & models; Appearance; Storage; All Settings; Back to account menu; Inspect this page; Reset permissions

Atlas surfaces: host-browser, host-devtools-html, host-devtools-cui

Primary: Profiles shows the current profile and Edit profile. Browser Tools opens Engine, Developer tools, New tab, History, Bookmarks, Reload page and Browser settings. Developer tools offers Inspect this page, HTML inspector and CUI inspector, each targeting the captured page. Manage Page opens Connection details and separate Camera, Microphone, Location and Notifications Ask/Allow/Block controls. Reset permissions returns those origin choices to Ask after showing the origin; it is not a global device reset. Back returns one menu level without navigating the tab. AI & models, Appearance, Storage and All Settings open their canonical pages.

Secondary: Menu items have no recursive custom menu. Permission choices never activate by secondary click. Copy origin is available from the origin identity area.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: The pinned Atlas labels Automatic/CUI preview/HTML preview and unverified certificate state honestly; production exposes only real installed engine adapters. A page Allow remains subject to OS permission and visible capture state; it cannot claim actual capture from a preference alone.

Integration and persistence: Account menu route buttons use [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu); history/bookmark commands use [IX-HOST-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.e93bdgq4u1rd); inspection uses [IX-HOST-016](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xfx3vy41nkqj)/017. The Atlas editor browser-tools capture mode belongs to Atlas authoring, not a production privilege to edit arbitrary pages.

Acceptance: Reset permissions for one origin: other origins and system microphone grants remain unchanged.
