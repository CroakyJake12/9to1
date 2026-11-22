# Interaction records

Source: https://docs.google.com/document/d/1tBC60DTC70QAZha8o7wn1SvgK1V3qL6vvKxDapYExl0?tab=t.kh6hghkncf6x

<a id="h.uq57av7ze9hy"></a>

# App interaction records

These records specify application-owned controls and editing regions. They refine the canonical application, runtime, storage and permission requirements; shared Haven/navigation/settings controls use IX-BASE patterns and their own records. Atlas screen IDs identify visual evidence, while the interaction outcomes below are adopted target contracts, not proof that a fixture handler or native backend already implements them. The control inventory was statically mounted with LinkeDOM after all 32 Atlas scripts, including refinements. This checks final control structure, not browser rendering or native runtime execution. A selection, preview, saved artifact, submitted operation and confirmed external effect retain distinct identities and receipts. New Projector controls are specified explicitly without claiming pre-existing Atlas screens. All 54 assigned existing Atlas screens are mapped below by observed control ordinal and exact label; inherited shared records retain their own semantics. Styled editor fields/regions that are visible but absent from the HTML control extraction are additionally named as semantic regions. The Atlas's Launcher app label is historical: the target live name is Home Launcher while launcher/settings-app-launcher remain compatibility IDs. Projector is the parent experience surface; Desktop is one of its registered experiences. Additional semantic field/region mappings below cover styled content and contract-defined target controls requiring accessible Atlas registration; they are separate from the 1,732 observed control occurrences and do not assert rendered handlers.

Authority: [Master specification](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk); [pinned Interface Atlas v3.3](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html). Owner additions and the detailed gestures specified here are target requirements; static Atlas markup is design evidence, not proof of native execution. Related application/runtime requirements in the master remain in force.

Reading a record: Primary covers left click and activation; Secondary covers right click and contextual actions; Drag covers sources, targets and cancellation; Keyboard and touch supplies alternatives; State and integration defines scope, persistence and failure. Shared IX-BASE patterns apply unless the control record explicitly refines them. Acceptance describes required verification, not tests already performed.

<a id="h.idxbov4hu0mh"></a>

### IX-FILES-001 — Location history, breadcrumb and query

Controls: Back; Forward; Parent folder; location breadcrumb/address; folder search

Atlas surfaces: files, files-grid, files-context, files-organisation

Primary: Back/Forward visits the corresponding resolved location history; Parent opens the current parent. A breadcrumb opens that exact ancestor. Address submission resolves the typed provider/location; search filters or queries the named scope and retains the entered text on failure. Neither operation opens the selected document.

Secondary: Secondary click on a breadcrumb offers Open location and Copy location; ordinary address/search text uses its native editing menu. Opening that menu does not navigate.

Drag: These controls do not start a drag. Fields retain text selection and horizontal caret scrolling; dragging empty chrome cannot move a canonical item.

Keyboard and touch: [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/009: Enter submits an address/query; Escape exits editing and restores the prior resolved location. Touch breadcrumb overflow exposes the same ancestors.

State: Back/Forward disable at history ends. Disconnected, denied and missing locations remain distinct from empty results; current local folders remain usable.

Integration and persistence: Location changes update listing, breadcrumb and details together. Preserve per-location view state and selection by FileID, never row index; stale query results cannot replace a later folder.

Acceptance: Navigate into two folders, submit an unavailable provider address, then Back: the prior listing returns without opening or duplicating its selected file.

Basis: Atlas files files-grid files-context files-organisation; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.5g10u2yjc1us"></a>

### IX-FILES-002 — Rows, tiles, selection and owner opening

Controls: File rows/tiles; List; Grid; column headings; details selection; Open in Boards/Open

Atlas surfaces: files, files-grid, files-context

Primary: Single click selects FileID and updates details; Ctrl/Command toggles and Shift selects a contiguous visible range. Double click opens the selected item through its owner; folders navigate. Explicit Open opens once. List/Grid preserves folder and identities. Column heading selects sort key/direction without renaming content.

Secondary: Right click an unselected row first targets it; on a selected row retains the multi-selection. Expose the file-action menu for the exact set; blank-area menu targets the folder.

Drag: Drag selected items after the shared threshold with canonical references and count. Destination preview identifies move/copy; dropping on another folder uses its owner operation, never an implicit visual reorder in a sorted listing.

Keyboard and touch: [IX-BASE-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.a2i0alfkwp3d)/009/010: arrows move focus, Space toggles selection, Enter opens; touch tap selects with explicit Open and long press exposes actions.

State: Selection survives sorting and refresh if identities remain. Missing/denied previews show bounded metadata and do not hydrate forbidden bytes.

Integration and persistence: Open preserves the original artifact/revision and source ACL. Double-click must not also dispatch a second open from the single-click handler.

Acceptance: Select two items, switch List/Grid, sort, right-click one and verify the same two targets remain; double-click a board creates one owner view.

Basis: Atlas files files-grid files-context; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.ldd9uwbczx5m"></a>

### IX-FILES-003 — Selected-item operations and properties

Controls: Open; Open with; Rename; Move; Copy; Trash; Properties; File actions

Atlas surfaces: files, files-grid, files-context

Primary: Open uses the registered owner; Open with chooses an eligible handler without changing identity. Rename edits the selected name and commits once. Move chooses a destination and retains identity; Copy creates a declared independent destination item. Trash uses the provider's recoverable-delete contract. Properties reads actual selected metadata.

Secondary: The context menu and File actions overflow expose identical applicable commands. Multi-target Rename requires a named bulk-rename tool, not silent renaming of only the first item. Unavailable commands explain their missing capability.

Drag: Menu items are not draggable. File dragging invokes the same Move/Copy operations; rename text drag only selects text. Do not allow a drop to bypass collision or permission checks.

Keyboard and touch: Enter commits a valid rename; Escape restores the old name. Delete requests Trash, never purge. Touch long press opens the same menu and returns focus to the selected item.

State: Rename/move conflicts retain the proposed name/destination. Replace, Keep both and Skip identify each collision. Transfers show per-item committed/failed outcomes.

Integration and persistence: Cross-provider Move copies and verifies before authorised source deletion. Removing an organisational appearance is a separately named action, never an alias for Trash original.

Acceptance: Interrupt a cross-provider move after copy; retry reconciles destination identity, then deletes only the confirmed source once.

Basis: Atlas files files-grid files-context; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.elzsi3lze6rx"></a>

### IX-FILES-004 — Creation menu and organisation view switching

Controls: New; Folder; Project; Sheet; Tree; Smart; Timeline; Pipeline; Catalogue; Stash; Matrix; Contents

Atlas surfaces: files, files-organisation

Primary: New opens the ten-type catalogue. Choosing a type opens its name/backing-destination draft; only Create commits an organisation. Contents switches an existing specialised organisation to ordinary membership view; its specialised view restores the same saved layout/rules.

Secondary: Blank-area New uses the captured current container. Organisation context actions include Rename, Open Contents and its legal specialised view; they do not convert the object type.

Drag: Creation menu controls are not draggable. Dropping a source into an organisation creates an explicitly previewed membership/reference; owned nesting rejects cycles and unsupported destinations.

Keyboard and touch: Menu arrows and typeahead select a type; Enter opens its draft, Escape allocates nothing. Touch uses the same menu and Create boundary.

State: Missing provider creation capability disables Create with the reason. Tree's visual view requires editor rights; Contents can remain eligible without leaking protected layout.

Integration and persistence: Creation returns one stable organisation ID and focuses its name/content. View changes are personal state; rules, sections and shared geometry remain authored data.

Acceptance: Create a Sheet, place references, switch to Contents and back, then cancel New Matrix: geometry remains and no empty Matrix exists.

Basis: Atlas files files-organisation; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.yq09vz9s39aq"></a>

### IX-FILES-005 — Sheet objects, opening policy and connectors

Controls: Add existing/Add reference; note/drawing/shape; Sheet canvas; placement handles; connector endpoints; Open; File opening Pop-up/Owning app; Open original in pop-up; Open image; Open in Write; Show original location; Remove appearance

Atlas surfaces: files-organisation

Primary: In authoring, click selects a placement, not its source. Add existing chooses sources and inserts references; note/shape creates Sheet-owned content. Open uses Pop-up by default or the saved Owning app policy; a one-off alternate open changes no default. Open original in pop-up resolves the selected reference in its declared viewer; Open image does the same for its asset. Open in Write routes to that canonical document's owner. Show original location opens its actual folder/provider and preserves the organisation view. Remove appearance removes only this PlacementID, not the source or other appearances.

Secondary: Placement menu: Open, Open original, Duplicate appearance, Remove appearance, layer/order and properties. Source deletion is a separately named owner operation. Canvas menu adds owned objects or references.

Drag: Move/resize handles preview Sheet geometry; drag endpoints onto valid object anchors to connect. Auto-scroll near edges; Escape restores geometry. Dropping a file previews a reference, not a move of its original.

Keyboard and touch: Arrow nudges and numeric position/size fields offer non-drag editing; touch selection reveals handles and overflow. Connector selection plus Delete removes only the connector.

State: Locked/read-only layout rejects geometry edits while eligible source opening remains available. Broken references retain safe repair metadata.

Integration and persistence: Geometry/connectors/default opening policy form one undoable Sheet transaction; source edits use the source owner's undo. Selection/viewport stay personal.

Acceptance: Move a board appearance, edit the original through its popup, undo the move and verify the source edit remains.

Basis: Atlas files-organisation; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.elmbjnwr7zv9"></a>

### IX-FILES-006 — Project sections, Tree hierarchy and Matrix cells

Controls: Project section headers/order; Tree groups/connections; Matrix row/column headers/cells; Unsectioned/Unassigned

Atlas surfaces: files-organisation

Primary: Section/header click selects its local identity; disclosure collapses personally. Rename changes the section/axis label. Add section/axis creates local structure. Selecting a cell filters memberships; source files remain independent.

Secondary: Menus offer Rename, Move before/after and Remove with reassignment, Unsectioned/Unassigned or appearance-removal choices. Tree menus include Add child and Connect; viewer access does not reveal editor-only geometry.

Drag: Drag membership between sections/cells changes local placement; drag a section/axis handle changes order. Tree reparenting previews ancestry and rejects cycles. Source-file bytes never move from these gestures.

Keyboard and touch: Keyboard Move commands and destination pickers perform the same operations; touch long press targets the header or membership rather than conflating them.

State: Concurrent axis deletion invalidates a pending drop. Empty cells and denied sources are distinct; failed commits retain the prior structure.

Integration and persistence: Stable SectionID/AxisID/CellID survive renaming/reorder. Removing structure does not delete referenced originals and must preserve a recoverable placement result.

Acceptance: Rename and reorder an axis, then remove it with Unassigned selected: links retain identity and files remain intact.

Basis: Atlas files-organisation; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.usbc7pkp8zym"></a>

### IX-FILES-007 — Smart, Timeline, Pipeline and Catalogue local metadata

Controls: All/Any rules; locations; manual pins; date source; stage lanes/gates; catalogue fields

Atlas surfaces: files-organisation

Primary: Smart rule edits preview matches and commit the query; pins can retain labelled nonmatches. Timeline chooses Created/Modified/Deadline/Assigned explicitly. Pipeline stage selection inspects requirements. Catalogue cells edit typed local values unless a declared source-property mapping is selected.

Secondary: Menus explain Why matched, Pin/Unpin, Move to stage and local field properties. Remove dynamic result warns it returns while the rule matches. Source metadata edits are named separately.

Drag: Assigned-date dragging previews a local date; source-derived date dragging opens the explicit metadata-change proposal. Pipeline card drop validates gates before stage commit. Column dragging reorders local fields only.

Keyboard and touch: Numeric/date editors use [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/012; stage/date destination controls replace dragging on keyboard/touch. Escape cancels the draft.

State: Unknown metadata is not false. Offline queries are partial/stale; unmet stage gates retain the old lane. Invalid type conversion preserves cell text.

Integration and persistence: Local dates/stages/ratings do not silently rewrite file metadata or another collection. Configured approval gates apply only to that Pipeline transition.

Acceptance: Move a card into a gated lane and fail the gate; its canonical stage remains unchanged and no mapped metadata is written.

Basis: Atlas files-organisation; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.wbccznifd86a"></a>

### IX-FILES-008 — Stash reading and comparison

Controls: Stash sequence; Previous/Next; Reviewed; Compare A/B; reorder handles

Atlas surfaces: files-organisation

Primary: Selecting an EntryID opens its permitted preview at the stored personal position. Previous/Next follows current sequence. Reviewed changes personal review state. Compare assigns two independent reference slots without advancing the main reader.

Secondary: Entry menu: Open original, Assign A, Assign B, Move before/after, Remove appearance. Source deletion remains separate.

Drag: Reorder from the handle with insertion indicator; current reading follows EntryID after commit. Drop a source to add a reference at the shown slot; duplicate membership is explicit.

Keyboard and touch: Arrow navigation and Move commands replace drag. Touch switches clearly labelled A/B panes on narrow displays; long press opens the entry menu.

State: Missing preview keeps the entry and offers owner opening/relink. Removing the current entry selects a defined neighbouring entry without changing review records for others.

Integration and persistence: Sequence is shared authored state; reading position and Reviewed are personal. Comparison does not create files.

Acceptance: Reorder the current entry from last to first while comparing two others: reader and A/B targets stay bound to their IDs.

Basis: Atlas files-organisation; expanded_apps.md, corresponding APP/ED-FILES clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.mcnmkzi51hnx"></a>

### IX-PLANNER-001 — View, date and calendar navigation

Controls: Today; previous/next interval; Day/Week/Month/Agenda/Tasks; calendar visibility; event rows; Calendar; Tasks; Schedules; Burrows; New; Search events; Calendar settings; Previous week; Next week

Atlas surfaces: planner, planner-agenda, planner-event

Primary: Today selects the current date in viewer timezone. Previous/next moves one interval of the active view. View buttons change projection only. Calendar toggles filter visibility. Clicking an event selects it; explicit open or double-click edits that EventID/occurrence. Calendar opens the calendar projection, Tasks opens canonical Planner tasks, Schedules opens ordered schedule modules and Burrows opens their organisational groupings. New opens the explicit module-type chooser; it does not infer creation from the selected row. Search events queries authorised events in the visible scope and retains the query on failure. Calendar settings opens the selected calendar's display/timezone and allowed owner settings; a visibility/colour change is separate from sharing authority.

Secondary: Date-cell menu offers New event and New task for that date; event menu offers Open, Edit, Duplicate as new, Unschedule where a task, and Delete under recurrence scope.

Drag: Navigation controls are not draggable. Calendar grid/event gestures use the event manipulation record, not navigation history.

Keyboard and touch: Arrow/date picker navigation, Enter to open focused item and touch Agenda provide the same data access; Escape returns focus to the original date.

State: Loading/unavailable calendars retain known data with freshness and do not appear as empty. Overlapping events remain individually focusable.

Integration and persistence: View/date/calendar visibility are personal preferences, not event revisions. Switching Agenda preserves selection and unsaved edit draft.

Acceptance: Open an event in Week, switch Agenda and filter another calendar; the open draft remains the same occurrence.

Basis: Atlas planner planner-agenda planner-event; expanded_apps.md, corresponding APP/ED-PLANNER clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.wvovqbgv0l2p"></a>

### IX-PLANNER-002 — Create, move and resize scheduled intervals

Controls: Empty time slots; event body; start/end resize handles; unscheduled task cards; New event

Atlas surfaces: planner, planner-event, planner-agenda

Primary: Single click an empty slot selects a tentative start; New opens an event draft. Dragging a blank range previews a new interval but commits only through the creation form. Clicking a task does not schedule it. New event opens an unsaved event draft using the selected date/interval and a visible calendar target; if none is selected, it uses the declared creation default and exposes it before Save event.

Secondary: Event menu exposes explicit Edit time, Unschedule for scheduled tasks and Delete. Right-click a blank cell targets its date/time without creating an item.

Drag: Event-body drag moves its interval; edge handles resize one boundary. Show exact start/end, calendar and timezone throughout. Task-to-grid drop schedules the same PlannerTask. Escape cancels; recurrence scope is chosen before committing a series-related edit.

Keyboard and touch: Keyboard Edit time and date/time fields are equivalent. Touch long press selects before moving; scrolling cannot accidentally create an event.

State: Invalid durations, readonly calendars, occupied policy constraints and stale revisions block commit. DST ambiguity presents the configured explicit resolution.

Integration and persistence: One accepted gesture produces one owner mutation and supported undo. Due date remains distinct from scheduled interval. Drag does not notify attendees unless the chosen save action does.

Acceptance: Schedule an unscheduled task, resize it, cancel a recurrence move and confirm no duplicate task or unintended invitation.

Basis: Atlas planner planner-event planner-agenda; expanded_apps.md, corresponding APP/ED-PLANNER clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.vfmi0csr14zh"></a>

### IX-PLANNER-003 — Event form, recurrence, reminders and save

Controls: Title; calendar; start/end; timezone; all-day; recurrence; reminder; Save; Cancel; Delete; Date; Starts; Ends; Repeat; Location; Notes; All-day event; Save event; Close event dialog

Atlas surfaces: planner-event, planner, planner-agenda

Primary: Fields edit a draft for the captured EventID. All-day uses date spans; timezone edits preview affected instants. Recurrence opens the rule and occurrence/following/series scope. Save validates and commits; Cancel restores the prior revision. Delete names the selected recurrence scope. Date, Starts and Ends form a validated interval with explicit timezone; Location stores a location/call reference without joining it, and Notes edits event content without sending an update. Save event is the labelled owner commit. The close control names the event but follows the same pending-draft rule as Cancel; an invalid pending edit remains recoverable rather than being silently saved.

Secondary: Text fields retain native menus. Form overflow may expose Duplicate and source properties; no secondary click silently saves/deletes.

Drag: Form chrome is not draggable. Date/time text selection and picker scrolling do not move the calendar event.

Keyboard and touch: Enter follows the focused control, not a universal save while a picker is open. Explicit Save is keyboard/touch reachable; Escape closes the topmost picker before the draft.

State: Keep the edit draft on invalid time, unavailable provider or revision conflict. Reminder delivery state never masquerades as event save.

Integration and persistence: Save-only and send attendee updates are separately labelled. Snooze changes reminder delivery, not event time. A confirmed local save can remain sync pending.

Acceptance: Change timezone and reminder, fail provider sync and reopen: saved/pending states are honest and no invitation was sent by autosave.

Basis: Atlas planner-event planner planner-agenda; expanded_apps.md, corresponding APP/ED-PLANNER clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.akiujlecwkb6"></a>

### IX-PLANNER-004 — Tasks, completion and Burrow membership

Controls: Unscheduled tasks; completion checkbox; due date; Burrow/module membership; task details; Add task

Atlas surfaces: planner, planner-agenda

Primary: Task checkbox commits that PlannerTask's completion; selecting its row opens detail. Due-date editing changes the deadline, not a calendar interval. Add/remove Burrow membership organises references. Dismiss reminder only changes its notification. Add task creates an unsaved PlannerTask draft with a visible list/Burrow destination and focuses its title. Committing the draft creates one PlannerTaskID; it neither starts an AI run nor schedules an event until those distinct commands are used.

Secondary: Task menu exposes Open, Schedule, Unschedule, Set due date, Move membership and Delete task with distinct names.

Drag: Task drag to a calendar schedules its identity; drag within a manual task list changes order; drop into a Burrow adds/moves a membership as previewed.

Keyboard and touch: Space toggles a focused completion checkbox; Enter opens detail. Touch overflow exposes Schedule and membership commands without requiring drag.

State: Mixed selection shows mixed completion. A denied calendar cannot prevent permitted local completion; failed commit restores the checkbox with recoverable intent.

Integration and persistence: Runtime Actions TaskID and PlannerTaskID stay distinct; links to AI work do not turn checkbox completion into a run-completion receipt.

Acceptance: Complete a due-but-unscheduled task, dismiss its reminder and remove its Burrow membership: these create three distinct outcomes without deleting the task.

Basis: Atlas planner planner-agenda; expanded_apps.md, corresponding APP/ED-PLANNER clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.ppl7wsu3cd1p"></a>

### IX-PLANNER-005 — Schedule, Countdown and Assignment controls

Controls: Schedule ordered items; Countdown target; Assignment required/optional items; progress override

Atlas surfaces: planner, planner-agenda

Primary: Open resolves the named module. Schedule time/order edits its references. Countdown follows its selected target or an explicit independent date. Required-item completion feeds Assignment policy; optional items remain separately visible. Manual override records actor and reason.

Secondary: Menus expose Edit target, Open source, Reorder, Remove membership and inspect progress calculation; none substitutes a source title for identity.

Drag: Schedule order handles preview the affected sequence. Countdown and Assignment progress are not drag-to-invent values; date/weight fields use explicit edits.

Keyboard and touch: Use move-before/after and target pickers for keyboard/touch. Escape cancels target replacement without breaking the existing link.

State: Unavailable linked dates display unavailable/stale rather than counting down to a guessed date. Invalid weighting cannot produce a completed Assignment.

Integration and persistence: Derived time/progress updates do not mutate linked source dates. Overdue state alone never sends a message; communication belongs to an authorised Automation.

Acceptance: Move a linked event and verify its Countdown updates; complete only optional work and confirm required-work status remains incomplete.

Basis: Atlas planner planner-agenda; expanded_apps.md, corresponding APP/ED-PLANNER clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.qy6b4ynb4ivc"></a>

### IX-AUTO-001 — Definition selection and graph viewport

Controls: Automation list; graph canvas; node cards; pan/zoom; node properties; Simple/graph builder; Home; Library; Run history; Connections; New; Graph; When / If / Then; Runs; Revision history; Automation actions; Insert node; Zoom out; Fit graph

Atlas surfaces: automations, automation-trigger

Primary: Select an automation to open its draft/active revisions; select a node to show its typed properties. Simple-builder changes edit the same graph. Blank click clears selection; double-click a node opens its editor, never runs it. Home shows automation overview, Library lists definitions, Run history/Runs opens execution receipts, Connections manages declared service bindings and New creates a fresh definition draft. Graph and When / If / Then are views of the same typed definition. Revision history opens immutable definition revisions. Insert node opens the registered node catalogue; Zoom out/Fit graph changes only viewport. Automation actions exposes rename, duplicate draft, export, activation state and archive/delete definition with their exact scope, never a hidden Run.

Secondary: Right-click canvas opens node search at that captured graph point. Node menu offers Configure, Duplicate, Disable node and Delete; unavailable required nodes retain diagnostics.

Drag: Drag node body beyond threshold to move graph geometry; pan from designated canvas gesture; marquee only from selection mode. Text editing/port dragging never pans. Escape restores the in-progress geometry.

Keyboard and touch: Arrow selection, node-search insertion and numeric/move controls provide keyboard access; touch uses explicit pan/select modes and long-press search.

State: Readonly definition permits inspection only. Selecting unavailable nodes keeps their schemas/errors, not a fabricated working card.

Integration and persistence: Geometry and authoring are draft changes; active runs remain pinned. Selection and viewport do not advance draft execution.

Acceptance: Edit a field, pan, move a node and reopen: no downstream action runs and the active revision is unchanged.

Basis: Atlas automations automation-trigger; expanded_apps.md, corresponding APP/ED-AUTO clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.z5kfsskql74h"></a>

### IX-AUTO-002 — Typed connectors, insertion and deletion

Controls: Input/output ports; edge selection; connector Delete; node search/catalogue

Atlas surfaces: automations

Primary: Click a port exposes its type and compatible targets; choose a target to add one validated edge. Choosing a catalogue action inserts a real registered node with missing-input markers. Edge selection targets that edge alone.

Secondary: Connector context menu offers Inspect types and Delete edge. Node deletion previews incident edges. Canvas search contains only eligible registered capability schemas.

Drag: Port drag previews legal targets and a connection line; illegal types show rejection. Drop commits an edge once. Ctrl-click connector deletion matches explicit Delete; ordinary click only selects. Escape removes the preview.

Keyboard and touch: Keyboard Connect opens a typed target picker; Delete removes the focused edge; touch port selection uses the same picker without fine motor dragging.

State: Cycles require declared loop/budget nodes; missing capabilities and incompatible conversion reject the connection with the graph retained.

Integration and persistence: Removing an edge does not delete either node or an external artifact. Conversion is an explicit node; missing/null/error remain distinguishable values.

Acceptance: Try connecting a text output to a date-only input, then add an explicit converter; only the valid graph accepts the edge.

Basis: Atlas automations; expanded_apps.md, corresponding APP/ED-AUTO clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.obker3czw0oh"></a>

### IX-AUTO-003 — Schedule trigger editing

Controls: Time; timezone; days; missed-run policy; trigger Save/Cancel; active schedule summary; Start time; Run on Monday–Sunday; Respect quiet hours; Save trigger; Close Weekday schedule

Atlas surfaces: automation-trigger, automations

Primary: Open the trigger to edit its draft schema. Choose time, timezone and days; missed-run policy names skip/catch-up behaviour. Save writes the draft trigger and returns focus to its node; Cancel discards only that pending form. Weekday chips retain full weekday identities and accessible labels even where the visible initials repeat T or S. Respect quiet hours follows the configured account notification quiet-hours policy for notification delivery; it does not silently rewrite the trigger time or suspend non-notification work. A separate explicit trigger condition is required to pause execution during quiet hours. Close follows Cancel for uncommitted trigger changes.

Secondary: No custom field menu beyond native editing; trigger-node menu may inspect the currently active schedule without replacing it.

Drag: These controls do not start a drag. Fields retain text selection and horizontal caret scrolling; dragging empty chrome cannot move a canonical item.

Keyboard and touch: Keyboard day toggles and date/time fields match touch pickers. Escape closes the picker before dismissing the draft. Save remains explicit.

State: Show invalid/nonexistent local time and the selected daylight-saving rule. Missing executor or background scheduling capability is a named unavailable state.

Integration and persistence: Saving does not activate the schedule or execute downstream actions. Next-fire preview is a calculation with timezone/policy, not scheduler acceptance.

Acceptance: Edit a weekday/time, save and inspect the active schedule: it stays on its former revision until deliberate activation.

Basis: Atlas automation-trigger automations; expanded_apps.md, corresponding APP/ED-AUTO clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.jjnwuw4foygc"></a>

### IX-AUTO-004 — Save, validate, test and activate

Controls: Save; Validate; Test/Run test; Activate; deactivate/enable control; issues list; Validate & activate; Test draft

Atlas surfaces: automations, automation-trigger

Primary: Save persists the draft. Validate checks the captured node/edge/schema/scope revision and links failures. Test draft opens sample input/environment/tool profile; its explicit Run test starts one named test using that configuration, without a second generic permission question. Activate atomically selects a validated revision for future starts. The Atlas's combined Validate & activate captures the current draft once, validates it and activates that exact revision only if validation and scope are still current; a failure leaves the existing active version running. Disable prevents new starts and exposes the declared existing-run policy.

Secondary: Primary buttons have no custom context menus. Issue menu can Copy details/Open node; it cannot suppress a required validation failure.

Drag: Buttons and issue rows are not draggable; selecting error text cannot start a test.

Keyboard and touch: [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)/007/011: keyboard/touch use the same explicit commands. Escape dismisses test configuration without running it.

State: Busy actions deduplicate by operation ID. Invalid drafts remain saved but cannot activate. A live test's external actions retain ordinary effective grants/protected review.

Integration and persistence: Test success does not activate; activation does not alter an already running revision. Mock results are marked simulated rather than real service receipts.

Acceptance: Keep a valid automation active, save an invalid draft, fail validation and run a mock test: none replaces or executes the active definition.

Basis: Atlas automations automation-trigger; expanded_apps.md, corresponding APP/ED-AUTO clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.7bet3fvwa0an"></a>

### IX-AUTO-005 — Run inspection, durable waits, retry and cancellation

Controls: Run history; node result; Waiting; Resume; Retry failed node; Cancel run

Atlas surfaces: automations

Primary: Selecting a run opens its pinned graph and receipts. Node result opens actual input/output/error. Resume satisfies the exact durable wait condition. Retry targets eligible unfinished attempts after uncertain effects are reconciled. Cancel stops future dispatch and reaches supported boundaries.

Secondary: Run menu offers Open definition revision, Copy safe diagnostics and Cancel; completed-node menu offers Inspect receipt, not a blind Run again shortcut.

Drag: Run history and recorded graph are not authoring drag surfaces; panning does not move saved nodes. Reordering definitions cannot reorder historical effects.

Keyboard and touch: Keyboard focuses run/node and activates named controls; touch detail sheets preserve RunID when dismissed.

State: Queued/working/waiting/failed/cancelling/completed are actual states. Disconnected executor is unavailable, never implicit successful completion.

Integration and persistence: Completed branches survive sibling failure. Closing inspection leaves the run alive. Resume/reconnect never repeats committed sends; desktop/mobile executor bindings remain explicit.

Acceptance: Restart during a wait and replay the same trigger; exactly one admitted run resumes with earlier action receipts intact.

Basis: Atlas automations; expanded_apps.md, corresponding APP/ED-AUTO clauses. Gesture and transaction detail below is the target contract, not executed fixture behaviour.

<a id="h.3vj1f8kgf6b"></a>

### IX-FILES-009 — Locations, favourites, details and clipboard

Controls: Recent; Favourites; This device; 9to1 Drive/Library; Shared with me; Trash; saved locations; Cut; Copy; Paste; Add to Favourites; Details; Close details; New Files tab

Atlas surfaces: files, files-grid, files-context, files-organisation

Primary: Each location opens its canonical listing/query; Trash opens recoverable deleted items. Add to Favourites changes membership. Details toggles the selected item's inspector; Close details preserves selection. Cut records a move-intent payload, Copy records a copy payload, and Paste previews the current destination then commits the appropriate transfer. New Files tab opens another Haven-native Files view.

Secondary: Location menu exposes Open, Open in new tab and remove shortcut for user pins. Trash items expose Restore and separately named permanent deletion under provider policy.

Drag: Dragging a location into favourites creates a pin; file drag uses transfer semantics. Clipboard cut does not delete the source before a successful paste. Inspector dividers resize presentation only.

Keyboard and touch: Ctrl/Cmd X/C/V and F2 route to Files only outside text editors. Touch overflow exposes clipboard commands, Restore and Details.

State: Paste disables for incompatible/expired payloads. Offline destinations and collisions preserve the clipboard intent and report unfinished items.

Integration and persistence: Clipboard references carry source identity/revision and capability; repeated pastes are deliberate separate copies, while transport retries deduplicate one paste. New tab is not a second internal tab strip.

Acceptance: Cut a cloud-only file, fail Paste, close Details and navigate back: the source remains and the transfer is visibly unfinished.

Basis: Final static mounted Atlas inventory/interaction_screens/files.html; expanded_apps.md corresponding FILES contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.wf4kuxnpio9q"></a>

### IX-FILES-010 — Sheet authoring tools and viewport

Controls: Edit/View; Select; Pan; Add files; Text; Sticky note; Image; Shape; Pen; Eraser; Connector; Snap to objects; Show grid; Zoom out/in; Fit content; Close inspector

Atlas surfaces: files-organisation

Primary: Edit/View switches authoring affordances without changing source rights. Select targets placements; Pan moves viewport. Text/Sticky note create Sheet-owned text, Image chooses an asset reference, Shape chooses geometry, Pen creates strokes, Eraser removes eligible Sheet-owned stroke content, Connector starts endpoints. Snap and grid change their named aids; Fit frames content. Add files opens the authorised reference picker and inserts selected identities under [IX-FILES-005](https://docs.google.com/document/d/1tBC60DTC70QAZha8o7wn1SvgK1V3qL6vvKxDapYExl0/edit?tab=t.kh6hghkncf6x#heading=h.yq09vz9s39aq). Zoom out/in changes scale around the current focus, preserving document coordinates; Close inspector hides the inspector without deselecting or deleting anything.

Secondary: Canvas/selection menus expose the same tool insertion, placement removal and properties; no source-file deletion is hidden under Eraser.

Drag: Creation drags show provisional geometry/strokes; commit at the completed gesture, cancel with Escape. Pan cannot move objects. Snap previews actual anchors; zoom stays centred on pointer/focus.

Keyboard and touch: Tool buttons and numeric geometry provide touch/keyboard alternatives. Long press selects before opening actions; two-finger navigation never paints.

State: Read-only mode disables authoring tools, not navigation. Failed insertion retains the prior canvas and creation intent.

Integration and persistence: Tool choice/viewport are personal; created objects/geometry are undoable Sheet content. Closing inspector does not delete selection.

Acceptance: Pan while a file placement is selected, erase a Sheet stroke and Fit: placement/source stay unchanged.

Basis: Final static mounted Atlas inventory/interaction_screens/files-organisation.html; expanded_apps.md corresponding FILES contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.8lm93qcsb25i"></a>

### IX-PICTURE-001 — Canvas tools and transformed selection

Controls: Select; Move; Crop; Brush; Eraser; Text; Shape; Sample colour; artwork; Zoom out/in; Fit artwork

Atlas surfaces: picture, picture-mask, picture-export

Primary: Select creates a temporary pixel/object selection; Move targets the active editable object. Crop previews the document boundary. Brush/Eraser act on the explicit layer or mask target. Text inserts editable text; Shape inserts geometry; Sample colour reads the addressed artwork pixel. Zoom/Fit alter viewport only.

Secondary: Selection menu exposes add/subtract/invert selection, transform and Convert selection to object. Object menu identifies layer/mask scope; secondary click does not paint or crop.

Drag: Selection/crop/shape drags preview image-coordinate bounds; move/resize uses transformed handles and snapping. Brush strokes commit one undoable stroke. Escape cancels provisional geometry; panning uses a separate gesture.

Keyboard and touch: Keyboard tools and numeric crop/transform fields replace precise dragging; touch uses explicit tool modes, not an inferred paint action during navigation.

State: Locked/missing layers reject edits. Hit testing and handles follow the same zoom/rotation transform as rendering.

Integration and persistence: Crop, resize pixels and resize canvas remain distinct. Erasing a mask changes mask values, never original pixels; theme changes do not recolour artwork.

Acceptance: Zoom/rotate, paint one mask stroke and undo: only the intended mask coordinates change.

Basis: Final static mounted Atlas inventory/interaction_screens/picture.html; expanded_apps.md corresponding PICTURE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.6y50r3mapm1m"></a>

### IX-PICTURE-002 — Layers, masks and compositing order

Controls: Add layer; New layer; Layers and properties; layer/mask thumbnails; visibility/lock; opacity; Add mask; Undo; Redo

Atlas surfaces: picture, picture-mask, picture-export

Primary: Layer thumbnail selects pixels/object; its separate mask thumbnail selects the linked mask. New/Add layer chooses a concrete type and inserts once. Add mask creates a linked mask for the selected layer. Visibility/lock/opacity change only their named property. Undo/Redo traverses document operations.

Secondary: Layer menu includes Rename, Duplicate, Group/Ungroup, Move up/down, Add mask and Delete layer; deleting a group identifies owned descendants. Mask deletion names the mask rather than the layer.

Drag: Drag a layer handle to reorder with insertion/parent indicator; reject cycles and illegal group placement. Canvas selection is not overwritten by beginning a sidebar scrollbar drag.

Keyboard and touch: Arrows navigate layers; F2 renames; Space operates a focused visibility toggle; touch long press opens the target's own menu.

State: Mixed multi-selection reports mixed property values. Locked layers remain inspectable. A failed commit retains prior layer order.

Integration and persistence: Automatic composition and Manual Layers view the same graph. Undo restores grouping/order and mask links, not a flattened screenshot.

Acceptance: Select a mask, reorder its layer, undo and reopen: the mask remains linked to the correct layer.

Basis: Final static mounted Atlas inventory/interaction_screens/picture.html; expanded_apps.md corresponding PICTURE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.jb77xnxr1bwh"></a>

### IX-PICTURE-003 — Mask inspector and nondestructive adjustments

Controls: Return to layers; Show mask overlay; Link mask to layer; Invert; Reset; size/softness/opacity; Adjust; Effects; Paint colour

Atlas surfaces: picture-mask, picture

Primary: Return to layers changes inspector target. Show overlay alters inspection only. Link mask controls whether transforms follow the layer. Invert flips mask values; Reset restores the declared mask baseline through undo. Brush parameters alter subsequent strokes. Adjust/Effects open scoped previews with explicit apply/bypass. Paint colour selects mask coverage semantics: black hides, white reveals and intermediate greys partially reveal; it does not recolour the underlying layer. Size sets brush diameter in document units, Softness its edge falloff and Opacity the applied stroke coverage. The Tool field chooses a supported mask editing tool while keeping the active mask target explicit; choosing it does not switch to painting the underlying image layer.

Secondary: Mask menu offers inspect/remove/disable mask; effect menu offers reset, bypass and remove, each targeting the selected effect. No operation silently deletes source pixels.

Drag: Brush parameter sliders use [IX-BASE-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.5oiet5rdqq3c); dragging in the mask paints only with the active brush. Adjustment curves/handles commit the scoped parameter transaction, not canvas movement.

Keyboard and touch: Numeric fields and keyboard sliders match handles; touch preview/Apply remains reachable. Escape cancels an uncommitted adjustment.

State: Unavailable processors leave the prior image and a named failure; hidden overlay does not disable an active mask.

Integration and persistence: Mask reset/invert/effect application are undoable. Preview cache is derived; closing a preview without Apply retains the native source.

Acceptance: Disable overlay, invert, unlink and move the layer: rendered and inspector mask relationships remain consistent.

Basis: Final static mounted Atlas inventory/interaction_screens/picture-mask.html; expanded_apps.md corresponding PICTURE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.8pt1oatsa16a"></a>

### IX-PICTURE-004 — Image export draft and verified output

Controls: Export; format; width/height; Lock aspect ratio; colour profile; Transparent background; destination; Export PNG; Cancel; Close Export image; File name; Save to

Atlas surfaces: picture-export, picture, picture-mask

Primary: Export opens a configuration draft pinned to a source revision. Format updates valid settings; aspect lock couples dimensions; transparency controls alpha only where the format allows it. Export PNG starts the reviewed rendering job; Cancel/Close before start discards options, not the native project. File name and Save to identify the actual output path/provider. Transparent background uses an export-only visibility override for the declared background layer (Paper in the Atlas fixture) and preserves source-layer visibility unless the user separately edits it. It must show this compositing consequence rather than claiming alpha solely because a checkbox is on.

Secondary: No custom menu; secondary click does not activate the command. Editable text retains its native selection/copy menu.

Drag: Control chrome is not draggable; native field text selection and scrolling remain available.

Keyboard and touch: [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)/003/007/009/010 apply: keyboard and touch invoke the named action once; Escape cancels the topmost transient draft and restores its invoker.

State: Missing linked assets/fonts, incompatible alpha/profile, invalid dimensions and unwritable destination block start with specific repair. Rendering/verified output differ.

Integration and persistence: Export produces separate bytes without flattening the source document. Repeated clicks deduplicate one job; closing progress does not imply cancellation. Completed output references the pinned revision.

Acceptance: Change dimensions with aspect lock, cancel, reopen and export with a missing font: no phantom download or source flattening occurs.

Basis: Final static mounted Atlas inventory/interaction_screens/picture-export.html; expanded_apps.md corresponding PICTURE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.in31cqpu52i8"></a>

### IX-WAVE-001 — Transport, playhead and recording

Controls: Return to start; Play; Stop; Record in toolbar/transport; Loop range; track record arm; input/monitoring

Atlas surfaces: wave, wave-clip, wave-stems

Primary: Play starts at the current playhead; Return to start seeks to the sequence start. Stop halts playback and finalises an owned recording. Both Record controls invoke the same selected-input/armed-track command. Loop range repeats the defined range, not the entire project implicitly.

Secondary: Transport has no custom action menu. Timeline ruler menu sets markers/range boundaries; armed-track menu exposes input and monitoring configuration.

Drag: Ruler drag scrubs/seeks; range handles adjust loop/in/out. Recording controls are not draggable. Track movement and playhead movement remain separate gestures.

Keyboard and touch: Space toggles playback outside text editing; explicit Record/Stop work by keyboard/touch. Escape does not discard a take.

State: Missing input/grant or no armed track prevents recording; real meters/timebase show actual signal. Device loss retains a partial take with finalisation status.

Integration and persistence: Monitoring is not recording. Audio source permissions and retention are explicit; transport Stop does not stop unrelated background jobs.

Acceptance: Start one armed take, lose its input and Stop: recover the real partial audio without inventing recorded silence or deleting earlier takes.

Basis: Final static mounted Atlas inventory/interaction_screens/wave.html; expanded_apps.md corresponding WAVE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.qz8tum687s5f"></a>

### IX-WAVE-002 — Clip selection, trims, fades and stretch

Controls: Named timeline clips; clip body/edges; fade handles; source offset/start/duration/gain; Preserve pitch; Reset clip; Done; Close clip; Fade in; Fade out; Fade curve

Atlas surfaces: wave, wave-clip

Primary: Click selects ClipID and inspector; double-click opens its detailed range. Numeric start/offset/duration/gain edit distinct properties. Preserve pitch configures time stretch; Reset clip restores its declared arrangement properties through undo. Done closes the inspector after valid edits, not an export. Fade in/out are durations relative to this clip's audible bounds; Fade curve selects its declared gain interpolation. Editing fades cannot exceed the clip's valid range or trim the source destructively. Source offset changes which source samples are referenced, while timeline start changes placement; Duration is validated against rate and available source.

Secondary: Clip menu: Split at playhead, Trim to selection, Slip, ordinary Delete, Ripple delete, Duplicate and Open source. Ripple names affected tracks; locked tracks remain protected.

Drag: Body drag moves timeline start; edges trim source range; fade handles adjust the same envelope as numeric fields. Modifier previews expose ripple/slip/stretch instead of switching silently. Escape restores the whole affected arrangement.

Keyboard and touch: Keyboard/numeric time fields and named menu commands provide equivalents; touch reveals distinct edge/fade handles after selection.

State: Missing source, invalid range and concurrent project edits retain the prior valid clip. Preserve pitch unavailable is explained, not ignored.

Integration and persistence: Split segments reference one source. Undo ripple restores neighbours and grouping. Closing inspector does not reset a valid edit.

Acceptance: Change fade numerically then drag its handle, split and undo a ripple: source offset, duration and neighbours stay coherent.

Basis: Final static mounted Atlas inventory/interaction_screens/wave.html; expanded_apps.md corresponding WAVE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.xlxqb4rn2qnp"></a>

### IX-WAVE-003 — Tracks, mixer, effects and timeline aids

Controls: Import; track selection; gain/pan; mute/solo/arm; Effects; Snap: beat; Undo/Redo; M; S; track/master gain faders; Pan; output device; tempo; time signature

Atlas surfaces: wave, wave-clip, wave-stems

Primary: Import selects media and creates verified asset references/clips at an explicit insertion point. Track selection scopes controls. Mute/Solo affect monitoring/mix as declared; arm selects recording destination. Effects opens the selected track/clip chain; Snap selects the named grid/marker targets. Undo/Redo traverses project edits. M toggles that track's mute, S toggles solo under the explicitly shown solo mode, gain faders adjust track/master level in the declared decibel scale and Pan changes channel balance under the routing profile. Output selects the actual audio endpoint independently of recording input. Tempo/time-signature edits update musical grid/time mapping with a preview of time-stretched or beat-locked content; they are not inferred from the fixture's displayed numbers.

Secondary: Track menu offers Rename, Add track, Duplicate, routing, Remove track; effect menu offers bypass/reset/remove. Remove track does not delete original media.

Drag: Drag track handles reorder; clip drag obeys arrangement semantics. Mixer faders preview gain with unit readout; automation points edit their time/value, not track order.

Keyboard and touch: Numeric gain/pan, routing pickers and move commands support keyboard/touch. Long press distinguishes track header from clip.

State: Invalid routing cycles, absent plugins and incompatible channel maps show errors. Missing effects cannot silently yield a successful dry export.

Integration and persistence: Waveform/spectral caches are derived. Mixer/automation and render use the same graph/timebase. Snap preference cannot change existing clip positions.

Acceptance: Bypass an effect, automate gain and import missing media: the project stays editable and the export dependency remains visibly unresolved.

Basis: Final static mounted Atlas inventory/interaction_screens/wave.html; expanded_apps.md corresponding WAVE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.5hj1mq9ke5bk"></a>

### IX-WAVE-004 — Stem separation and generated outputs

Controls: Split stems; selected source/backend/output tracks; Keep original track; Start separation; Cancel; Close Split stems; Processing; Separation mode; Vocals; Drums; Bass; Other instruments

Atlas surfaces: wave-stems, wave

Primary: Split stems opens a job draft for the actual source/range. Select a declared backend and named output groups. Start separation admits one job after resource/source validation. Keep original track controls its arrangement retention; original media remains recoverable regardless. Cancel before start discards the draft; running Cancel requests real job cancellation. Processing names the actual local/cloud backend and its disclosure boundary. Separation mode defines available output groups; each Create tracks choice chooses an output to insert after successful separation. Unsupported group combinations are rejected rather than replaced with approximate labelled tracks.

Secondary: No custom menu; secondary click does not activate the command. Editable text retains its native selection/copy menu.

Drag: Control chrome is not draggable; native field text selection and scrolling remain available.

Keyboard and touch: [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)/003/007/009/010 apply: keyboard and touch invoke the named action once; Escape cancels the topmost transient draft and restores its invoker.

State: Pending/Running/Failed/Complete track actual execution. Unsupported backend or insufficient resources is a named unavailable state. No stem clips are inserted before verified output.

Integration and persistence: Completed stem clips retain provenance and original alignment. Retry reconciles one job; closing the sheet is not cancellation. Prior completed results remain until explicitly replaced.

Acceptance: Cancel separation halfway, reopen and retry: no empty fake stem tracks appear and the original still plays.

Basis: Final static mounted Atlas inventory/interaction_screens/wave-stems.html; expanded_apps.md corresponding WAVE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.g3j0z0ltcfzc"></a>

### IX-WAVE-005 — Audio export and editable generation

Controls: Export audio; output range/format/sample rate/channels/loudness/destination; narration/music/SFX generation

Atlas surfaces: wave, wave-clip, wave-stems

Primary: Export audio reviews the selected range/revision and audio settings, then starts one render. Narration generation retains text/voice/prosody; sequencer/SFX generation retains its native component where available. Accept inserts the confirmed component; an opaque audio result is labelled as such.

Secondary: Generated component menu exposes Edit source definition, Regenerate, Render cache and Remove placement. Render-cache removal cannot delete its editable source.

Drag: Control chrome is not draggable; native field text selection and scrolling remain available.

Keyboard and touch: Keyboard/touch follow explicit Generate, Accept and Export boundaries; Escape dismisses configuration without starting generation/render.

State: Missing plugin, source, encoder or incompatible channels blocks render. Clipping warnings state the selected output policy.

Integration and persistence: Native save, preview playback, generated candidate and exported audio have separate receipts. Export cannot be reported complete merely because its queue entry exists.

Acceptance: Export an exact loop range after cancelling generation; output duration matches the range and no rejected component appears.

Basis: Final static mounted Atlas inventory/interaction_screens/wave.html; expanded_apps.md corresponding WAVE contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.rtonbnuo0586"></a>

### IX-MOTION-001 — Workspace, bin and sequence transport

Controls: Edit; Colour; Audio; Deliver; Transitions; Inspector; Workspace options; Import media; Previous edit; Play sequence; Next edit

Atlas surfaces: motion, motion-transition, motion-export

Primary: Workspace tabs change pane composition over one sequence. Import media stages verified sources in the bin. Previous/Next edit seeks an actual cut; Play follows the sequence playhead. Inspector toggles properties of current selection; Transitions opens the effect catalogue for a captured cut. Deliver/Render opens output review.

Secondary: Bin source menu offers Open, Relink, Properties and Remove from bin with dependency review. Workspace menus change layout, not source files.

Drag: Drag a verified bin asset onto a compatible track with insertion/overwrite preview; reject unavailable media or wrong track type. Pane-divider drag alters layout only.

Keyboard and touch: Space plays/pauses outside editors; edit-jump commands and source insertion pickers provide keyboard/touch equivalents.

State: Decode/proxy delays are visible and cannot present stale frames as current. Missing media remains in the bin for relink.

Integration and persistence: One sequence timebase drives viewer and audio; Colour/Audio workspace changes never create duplicate projects or flatten titles.

Acceptance: Switch workspaces during playback, relink a missing asset and jump to the next cut: sequence identity and exact cut remain stable.

Basis: Final static mounted Atlas inventory/interaction_screens/motion.html; expanded_apps.md corresponding MOTION contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.yex57lwanob2"></a>

### IX-MOTION-002 — Timeline clips, tools and keyframes

Controls: Selection; Blade; Snapping; Zoom out/in timeline; title/video/audio clips; trim/roll handles; keyframes

Atlas surfaces: motion, motion-transition, motion-export

Primary: Selection targets ClipID; Blade splits at the indicated frame without changing the source. Snapping toggles declared targets; timeline zoom changes the view only. Title double-click edits actual text. Keyframe selection exposes value/time/interpolation.

Secondary: Clip menu distinguishes split, trim, ripple, roll, slip, slide, lift/extract, duplicate and unlink audio/video. Keyframe menu selects interpolation or deletion.

Drag: Body/edge drags show frame-exact destination and affected linked tracks. Keyframe horizontal drag changes time; value handles change value. Locked tracks block illegal group changes. Escape restores the entire preview.

Keyboard and touch: Numeric time/frame/property controls and explicit edit commands replace dragging; touch exposes selected handles and clear pan mode.

State: Insufficient source handles and stale revisions reject the edit; a missing proxy is repairable without moving canonical timing.

Integration and persistence: All timeline edits preserve source identity and are undoable across affected neighbours. Nested sequences reject recursion; graph edits remain Motion semantics.

Acceptance: Ripple linked audio/video across a locked track, reject the operation and verify neither track moved or desynchronised.

Basis: Final static mounted Atlas inventory/interaction_screens/motion.html; expanded_apps.md corresponding MOTION contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.iegyrqyh5d6p"></a>

### IX-MOTION-003 — Transition selection, preview and handle validation

Controls: Transition effect; duration; alignment; Use available handles; Preview transition; Done; Close Transition; Blend curve

Atlas surfaces: motion-transition, motion

Primary: Choose an effect for the captured cut; duration/alignment edit a pending transition. Use available handles constrains the requested adjustment to real source range. Preview plays that candidate on the real sequence timebase. Done commits the valid transition and closes the panel; Close retains only already committed edits. Blend curve selects the declared interpolation through the transition; changes affect only this cut's transition, not each source clip's permanent opacity. Duration uses this sequence's frame rate/timecode and round-trip frame identity.

Secondary: Transition menu exposes Remove transition and Reset parameters; it never deletes adjacent clips. Secondary click on Preview does not apply anything.

Drag: Transition extent handles adjust duration around its selected alignment. Dragging another cut does not retarget an open proposal; Escape cancels the preview.

Keyboard and touch: Numeric duration/alignment and keyboard Preview/Done provide touch/keyboard parity.

State: Show each source's available handles. Reject or explicitly adjust insufficient handles; do not stretch arbitrary frames silently.

Integration and persistence: Preview does not render/export or replace the source clips. Commit returns an undoable effect revision for that exact cut.

Acceptance: Change the source trim while the panel is open, then Done: stale handle validation prevents the old proposal being applied.

Basis: Final static mounted Atlas inventory/interaction_screens/motion-transition.html; expanded_apps.md corresponding MOTION contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.ljtzx9bz7i7e"></a>

### IX-MOTION-004 — Render review, queue and output

Controls: Render; Deliver; range; codec/container; resolution/frame rate; Include audio; destination; Add to render queue; Cancel; Close Render sequence; Quality; File name; audio codec/sample rate/channels

Atlas surfaces: motion-export, motion, motion-transition

Primary: Render/Deliver opens the same pinned sequence review. Include audio controls the chosen output track mix. Add to render queue admits a job with exact settings/dependencies. Cancel before admission abandons settings; job Cancel is a separate stop command. Range chooses full sequence or explicit in/out boundaries. Format/codec selects a real encoder capability; resolution, frame rate and Quality/bitrate determine derived output and show any resampling. File name and Destination resolve the real output artifact. Audio codec/sample rate/channels are validated when Include audio is on and retained as a draft when it is off.

Secondary: No custom menu; secondary click does not activate the command. Editable text retains its native selection/copy menu.

Drag: Control chrome is not draggable; native field text selection and scrolling remain available.

Keyboard and touch: [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)/003/007/009/010 apply: keyboard and touch invoke the named action once; Escape cancels the topmost transient draft and restores its invoker.

State: Missing media/font, unavailable encoder, incompatible frame/audio settings and destination capacity block start. Queue/Rendering/Finalising/Verified are distinct.

Integration and persistence: Final render uses canonical sources unless explicitly labelled proxy output. Failed/cancelled jobs clean only their own partial files and preserve native project and prior valid output.

Acceptance: Queue a selected range, edit the sequence later and finish the job: the output corresponds to its original pinned revision.

Basis: Final static mounted Atlas inventory/interaction_screens/motion-export.html; expanded_apps.md corresponding MOTION contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.2ggtoyd5p7d5"></a>

### IX-LINK-001 — Flow insertion, sources and inline reading

Controls: Insert an authorised source or authored block; Sources panel; choose source section; Open selected Data source; Undo/Redo; Document; Sources; History; Find in Flow; Flow outline; Read the Flow without authoring controls

Atlas surfaces: link-flow, link-flow-block-edit

Primary: Insert chooses a source reference/section or an owned content object and shows the destination index. Sources opens actual SourceIDs and live/pinned policies. Open selected Data source opens that owner with the selected region; returning restores the Flow reading anchor. Undo/Redo changes composition operations. Document returns to the composition, Sources opens its dependency manifest, History opens this Flow's authored revisions and Find in Flow searches permitted rendered/authored content. Outline entries navigate stable content anchors; none changes source ownership or grants. Read the Flow without authoring controls switches to reader mode while preserving the same FlowID, selection/reading anchor and source-policy state; it does not create or export another document.

Secondary: Block menu exposes placement properties, move earlier/later, duplicate placement and remove placement. Source menu offers Open original/Change section, not hidden source deletion.

Drag: Drag block handle reorders the inline sequence with insertion indicator and auto-scroll; dropping a source creates a reference after section selection. Normal content drag selects text and must not reorder a block.

Keyboard and touch: Move earlier/later and section pickers replace drag; keyboard/touch preserve one primary vertical reading scroll.

State: Missing source/section/access produces a bounded placeholder. Live refresh preserves the nearest surviving content anchor.

Integration and persistence: Flow owns composition and native objects; references keep original ownership. Multiple regions of one Data artifact remain distinct placements, not duplicated files.

Acceptance: Insert two regions of one Data source, reorder one and open original: both links survive and the return anchor is unchanged.

Basis: Final static mounted Atlas inventory/interaction_screens/link-flow.html; expanded_apps.md corresponding LINK contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.wgy17dy9bvhy"></a>

### IX-LINK-002 — Flow placement inspector and source policy

Controls: Move block earlier/later; Duplicate placement; Block options; Source section; Revision policy; Content width; Source formatting; Open original; Remove placement; Done; Close panel; Space before; Space after

Atlas surfaces: link-flow-block-edit, link-flow

Primary: Move changes placement order one position; Duplicate creates another placement referencing the same source. Section changes the stable selected source region. Revision policy explicitly chooses Live or a pinned revision. Width/formatting change permitted presentation overrides. Remove deletes placement only; Done closes after valid changes. Space before/after edit typed layout spacing for this PlacementID, with units and finite bounds; invalid text remains a pending field and cannot be silently coerced. They do not insert blank content into the referenced original. Source formatting explicitly chooses preserved meaningful formatting or the supported integrated styles, retaining inaccessible/unsupported source structure.

Secondary: Block options exposes the same actions plus inspect source/permission. No control silently requests an independent source copy.

Drag: Handle drag changes Flow order; width resize previews the placement width. Source selector values are not draggable to retarget another block accidentally.

Keyboard and touch: Numeric width, discrete move buttons and keyboard menus mirror dragging. Touch overflow remains reachable on narrow layout.

State: Readonly source disables Edit original but can allow layout edits; missing pinned revision and stale proposal fail without replacing the source.

Integration and persistence: Layout undo cannot undo unrelated original edits. Closing the panel retains the Flow position, selected PlacementID and saved source policy.

Acceptance: With source view-only, change width and pin revision, then try Open original for editing: layout succeeds while source mutation stays denied.

Basis: Final static mounted Atlas inventory/interaction_screens/link-flow-block-edit.html; expanded_apps.md corresponding LINK contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.58qhz4rsq7ag"></a>

### IX-LINK-003 — Binder selection, nesting and reader mode

Controls: Explore this Binder in reader mode; Select; Pan; Connect items; Fit to contents; container items; Back/breadcrumb/outline; Close detail and return to overview; Undo; Open the Data original without moving it

Atlas surfaces: link-binder, link-binder-insert

Primary: Select targets an ItemID; double-click or Open enters its declared container. Reader mode hides authoring handles but keeps nonlinear navigation. Pan/Fit change viewport. Close detail returns to the captured overview/focus. Open Data original uses the real owner and does not move its file. Undo reverses the latest Binder-owned authoring transaction, not a previously committed source edit or reader navigation. The explicit Open the Data original without moving it command opens its canonical owner and returns to the same Binder selection.

Secondary: Item menu: Open, Rename, Duplicate owned content/appearance, Move, Remove and properties. Container removal distinguishes owned descendants from references.

Drag: Authoring handles move/resize items; nesting drops preview the target container and reject containment cycles. Connect mode draws edges between valid stable item anchors. Reader mode dragging scrolls/pans only.

Keyboard and touch: Outline navigation and Move-to commands cover keyboard/touch; Escape closes the innermost container/detail and restores a surviving anchor.

State: Readonly layout permits reader navigation. Missing referenced source or denied content stays distinct from an empty folder.

Integration and persistence: Authored container starting state differs from personal open tabs/reading state; rearranging Binder does not move external source files.

Acceptance: Open two nested containers out of order, close them and re-enter Reader: the same items and overview positions remain.

Basis: Final static mounted Atlas inventory/interaction_screens/link-binder.html; expanded_apps.md corresponding LINK contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.6xr53wu3edmh"></a>

### IX-LINK-004 — Binder insertion palette and source references

Controls: Add authored content or a reference to this folder; Note/card; Folder; Tabbed binder; Stack; Section; Connection; Text; Image; Table; source picker; Add reference; Cancel; Close panel; Search authorised files; destination container selector; Insert an authored container, note or source reference; Author in Binder; Reference a file

Atlas surfaces: link-binder-insert, link-binder

Primary: Palette captures destination ContainerID. Each type creates that exact owned structure on commitment; Connection requires endpoints. Image/Table choose a native object or explicit source reference. Source selection previews identity and Add reference creates one placement; Cancel/Close creates nothing. Search authorised files filters only accessible candidate originals and shows existing appearances. The destination selector chooses a permitted ContainerID within this Binder, visibly updating the creation target; an existing source appearance is disclosed rather than mistaken for a second original. The Document tools Insert entry opens this same palette with the currently selected Binder container as its explicit destination. Author in Binder selects creation of Binder-owned content; Reference a file selects the authorised-original picker. These modes only change the insertion draft until the named creation/Add reference command commits it.

Secondary: Palette has no custom menu. Source row context offers inspect/open metadata without adding it.

Drag: Source drag into the palette merely selects a permitted reference/destination preview; it cannot move the original. Canvas insertion preview uses explicit coordinates and Escape cancellation.

Keyboard and touch: Typeahead/arrows choose the type; Enter opens its parameters; explicit Add commits. Touch uses visible type tiles and destination summary.

State: Invalid destination, changed permissions or missing source blocks creation and retains choices. A dismissed modal restores focus to the container's Add button.

Integration and persistence: Direct authored children belong to Binder; external references preserve original identity. Inserting an empty folder is a real single owner transaction, not a navigation placeholder.

Acceptance: Choose a source, change the target container concurrently and commit: revalidation prevents insertion into the wrong container.

Basis: Final static mounted Atlas inventory/interaction_screens/link-binder-insert.html; expanded_apps.md corresponding LINK contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.gg04aefm3xhw"></a>

### IX-LINK-005 — Composition sharing and source-access review

Controls: Review source access before sharing; recipient/scope review; live reference versus retained copy; Share/Cancel

Atlas surfaces: link-flow, link-flow-block-edit, link-binder, link-binder-insert

Primary: Share review enumerates the composition, referenced source revisions and recipients' actual access. Choose the supported reference/copy/export mode with its disclosure consequence. Commit shares only the reviewed payload/scope after owner receipts; cancel leaves grants and content unchanged.

Secondary: No custom menu; secondary click does not activate the command. Editable text retains its native selection/copy menu.

Drag: Control chrome is not draggable; native field text selection and scrolling remain available.

Keyboard and touch: [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)/003/007/009/010 apply: keyboard and touch invoke the named action once; Escape cancels the topmost transient draft and restores its invoker.

State: Inaccessible/private dependencies cannot be silently packaged or exposed through previews. Pending provider status is not confirmed sharing.

Integration and persistence: Composition access does not grant original-source access. Retained copies require copying authority and get declared identities; live links continue enforcing source ACLs.

Acceptance: Share a Flow containing a private Data region with an unentitled recipient: the review identifies the dependency and never sends hidden bytes.

Basis: Final static mounted Atlas inventory/interaction_screens/link-flow.html; expanded_apps.md corresponding LINK contracts. Detailed gestures are adopted interaction requirements; fixture routing is not runtime acceptance.

<a id="h.4tlqngljke75"></a>

### IX-CONNECT-001 — Mailbox scope, folders, messages and provider changes

Controls: Messages; Mail; Calls; People; Inbox; Drafts; Sent; Archive; Trash; Connect settings; Search this mailbox; Refresh mailbox; Filter messages; Newest first; message rows; Archive; Move to Trash; Message actions

Atlas surfaces: connect-mail, connect-compose, connect-schedule

Primary: Mail selects the current account mailbox; the other section controls open native conversations, call history or contacts while retaining any mail draft. Folder selection filters by the selected account and folder ID. A message-row single click selects and displays that message; double click opens its reader without resending or duplicating it. Search, Filter messages and ordering affect the view. Refresh requests incremental provider reconciliation. The toolbar Archive and Move to Trash act on the selected MessageIDs, whereas sidebar Archive and Trash merely open those folders. Connect settings opens account and channel configuration.

Secondary: Message actions/right-click expose Reply, Reply all, Forward, Mark read/unread, Star/unstar, Archive, Move to folder and Move to Trash, with applicable account capability and selection counts. A folder menu offers refresh and folder properties; it cannot silently delete a provider mailbox.

Drag: Dragging selected messages to an allowed folder previews a provider move for their account. Cross-account destinations are rejected; dragging into a draft attaches an explicit message representation, not a mailbox move. Escape cancels; pending provider operations remain identifiable.

Keyboard and touch: Arrow keys select messages; Enter opens; multi-selection uses [IX-BASE-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.a2i0alfkwp3d). Touch tap reads and long press opens row actions. Delete invokes the same selected-message Trash command, never permanent deletion.

State: Pending moves show their operation IDs and reconcile errors per message. Offline cached content is labelled. An authentication or refresh failure retains the last known view and draft. No provider receipt means no confirmed archive/delete state.

Integration and persistence: Mailbox operations belong to MailAccountID/MessageID, not the currently visible folder label. Snooze schedules attention; it does not erase mail. Undo uses a supported inverse move and reports failure. Closing a reader preserves local and synced draft status independently.

Acceptance: Select a Sent message, open Archive in the sidebar, then retry a failed move: the operation must still target its captured message/account IDs and must not archive the first newly visible row.

Basis: Final static mounted Atlas inventory and interaction_screens/connect-mail.html; expanded_apps.md corresponding CONNECT contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.o9fqbfg3b1y9"></a>

### IX-CONNECT-002 — Compose identities, rich text and attachments

Controls: New message; Reply; From; To; Cc / Bcc; Subject; Message body; Attach file; Paperclip; Remove attachment; Formatting; Bold; Italic; Underline; Link; Smile; Discard draft; Close New message

Atlas surfaces: connect-mail, connect-compose, connect-schedule

Primary: New message creates a DraftID under the selected sending identity and focuses To. Reply creates or resumes a draft tied to the selected MessageID and correct account; Reply all and Forward retain their distinct recipient rules. From chooses an authorised account/alias and revalidates recipients and attachments. Cc / Bcc expands those explicit fields without merging their roles. Recipient tokens retain channel address and resolved contact identity. Formatting reveals the editor controls; Bold/Italic/Underline affect the selected range or next typing, Link inserts or edits a validated hyperlink, and Smile inserts an emoji. Attach file/Paperclip opens a source picker; Remove attachment detaches that draft reference. Discard draft removes the draft through the draft lifecycle; Close retains the saved draft and restores its invoker.

Secondary: Recipient menus expose view identity, edit address and remove; attachment menus expose open, inspect readiness and remove. Body secondary click preserves editor spelling/link/text actions; it never sends mail.

Drag: Drop files into the body or attachment zone to stage attachments. Show filename, size, source and upload/readiness, reject inaccessible or prohibited files, and distinguish link insertion from byte attachment. Recipient chips may move among To/Cc/Bcc with role preview. Dragging text uses normal editor semantics.

Keyboard and touch: Tab follows From, recipients, Subject, body and actions. Enter accepts a highlighted recipient candidate; it does not Send. Escape dismisses the current picker before the composer. Touch selects text or long-presses an attachment; controls remain reachable above the keyboard.

State: Unresolved recipient tokens, revoked sender identity or incomplete attachments block Send with field-specific recovery. Autosave may be local-only, syncing or conflicted; 'saved' does not claim delivery. Recipient selection never guesses between equal display names.

Integration and persistence: Draft edits form the draft's undo history. Switching folders or mail accounts cannot retarget an existing draft's From identity. Attachment references preserve original ownership, and formatting changes do not mutate the attached canonical artifact.

Acceptance: Create a Bcc recipient and attach a cloud-only file, then switch accounts: Bcc stays Bcc, From changes only explicitly, and Send remains unavailable until the actual attachment and identity are ready.

Basis: Final static mounted Atlas inventory and interaction_screens/connect-mail.html; expanded_apps.md corresponding CONNECT contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.i9vqm7q7ayk1"></a>

### IX-CONNECT-003 — Send, provider acknowledgement and uncertain outcomes

Controls: Send; Send options; draft delivery status; retry; cancel queued send; Undo send

Atlas surfaces: connect-mail, connect-compose

Primary: Send captures the DraftID and exact draft revision, sender, resolved recipients, subject, body and ready attachment manifest, then submits one idempotent SendOperation. No extra confirmation is required solely because the user pressed the explicit Send command; an existing applicable permission/policy review remains binding. Send options opens scheduling or configured pre-submission hold settings. A pre-submission queued item may be cancelled; Undo send is available only while that hold can actually prevent submission. Retry reconciles an existing operation before any new submission.

Secondary: Send has no right-click shortcut that bypasses validation. A delivery-status menu may open the provider receipt or copy a non-secret operation reference; it cannot convert an unknown outcome into Sent.

Drag: Send, delivery badges and retry controls are not drop targets. Dragging a file to the composer still stages an attachment and invalidates any captured send snapshot that has not been submitted.

Keyboard and touch: Activate Send by its focused button; keyboard shortcuts must be visibly documented and route through the same validation. A held touch or repeated Enter while submitting is coalesced to the same operation. Escape closes Send options, not an already-submitted send.

State: State is Draft → Queued → Sending → provider accepted, with Failed or Outcome unknown branches. Disable repeated submission for the same operation. A timeout after upload is Outcome unknown until provider reconciliation; attachments and original draft evidence remain available.

Integration and persistence: Provider acceptance is distinct from recipient delivery/read receipts. Editing after a queued snapshot creates a new draft revision and requires an explicit update of the scheduled/queued operation; it cannot change bytes already submitted. Sent storage and thread display use the provider identity returned by the receipt.

Acceptance: Disconnect immediately after provider acceptance and press Send again: show and reconcile the original uncertain operation instead of issuing a second message; never display 'undone' after the provider has received it.

Basis: Final static mounted Atlas inventory and interaction_screens/connect-mail.html; expanded_apps.md corresponding CONNECT contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.hpk1752q5bcu"></a>

### IX-CONNECT-004 — Scheduled sending and execution location

Controls: Schedule; Send options → Schedule; Date; Time; Timezone; Close Schedule send; Back to draft; Schedule send; scheduled item edit/cancel

Atlas surfaces: connect-compose, connect-schedule

Primary: Schedule opens a review of the captured draft revision with date, local time, timezone, recipients, sender and attachment readiness. Date/time edits resolve to an instant and retain the selected timezone; ambiguous or nonexistent local times require choosing a valid interpretation. The review names the executor—provider scheduling, cloud service or this device—and its availability requirement. Schedule send commits the schedule and returns a ScheduleID/acknowledged state. Back to draft returns without scheduling; Close dismisses the review and preserves the draft. Editing or cancelling an existing schedule targets that schedule's revision.

Secondary: No custom menu on command buttons; secondary click never executes the command. Editable text retains its selection and copy menu.

Drag: Schedule controls are not draggable. Selecting text or scrolling the date/time picker does not change the selected timezone or execution location.

Keyboard and touch: [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/007/012 govern fields and modal focus. Calendar arrows move the date selection, Enter accepts a date, and Escape closes the picker before the schedule dialog. Touch uses explicit date/time controls.

State: A missing executor capability or unavailable sender blocks scheduling. This-device scheduling states that the device must be available; it cannot claim server delivery. Late cancellation reports whether the send was still prevented, already submitted, or uncertain.

Integration and persistence: Scheduling saves an execution instruction, not a Sent message. Queued edits are revision checked; cancellation races reconcile with the same SendOperation. No extra general approval is introduced beyond existing action policy and the explicit Schedule send command.

Acceptance: Schedule 09:00 Europe/London, change the device timezone, and reopen: retain the chosen timezone and resolved instant. Cancel during submission and show the real provider outcome rather than returning to an unsent draft falsely.

Basis: Final static mounted Atlas inventory and interaction_screens/connect-compose.html; expanded_apps.md corresponding CONNECT contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.ldiemptirnqi"></a>

### IX-CONNECT-005 — Native conversation selection, threads and message composition

Controls: Messages; named rooms and direct-message rows; Search conversation; Pinned messages; message body; Add attachment; Emoji; Mention; Send message; reaction chip; reply count; message context menu

Atlas surfaces: connect-messages

Primary: A room/direct-message selection opens its ConversationID and preserves the previous conversation's draft. Search conversation searches authorised messages in that conversation; Pinned messages lists references and scrolls to the selected message. Clicking a reply count opens that message's thread; selecting a reaction adds/removes the user's own reaction. Add attachment stages an authorised artifact or file, Emoji inserts at the cursor, and Mention inserts a resolved member token. Send message commits one draft snapshot to the shown channel/thread. Clicking an embedded artifact opens its canonical owner and obeys that artifact's independent access.

Secondary: Message secondary click offers Reply in thread, Copy text, Copy message link, React, Pin/unpin when authorised, and Edit/Delete only under the message's ownership/retention rules. Conversation menus separate Mute, Leave and Block; none of those silently delete shared history.

Drag: Files dragged into the composer become pending attachments with previews; message text dragged into another editor is copied text with provenance, not a forwarded send. Room lists may reorder personal favourites only through an explicit handle; moving a message between rooms is not an implicit drag operation.

Keyboard and touch: Enter sends only in the visible configured Enter-to-send mode; Shift+Enter inserts a newline. Mention navigation uses arrows and Enter. Escape closes mention/emoji pickers before the thread. Touch long press opens the message menu without sending or reacting.

State: Sending, server accepted, delivered and read are separate states. Offline sends retain one client message ID. Changed room membership or revoked file access revalidates before submission; existing text and drafts remain recoverable. A pending edit displays the affected message revision.

Integration and persistence: Native room encryption badges describe the actual transport/device state and do not imply mail, SMS or attached-file encryption. Room membership does not grant access to referenced artifacts. Editing/deleting a message preserves attributable event history within policy; retry cannot duplicate the message.

Acceptance: Queue a reply offline, switch to another room, then reconnect: send it once to its captured thread, preserve its attachment access outcome, and do not expose the referenced document to new room members automatically.

Basis: Final static mounted Atlas inventory and interaction_screens/connect-messages.html; expanded_apps.md corresponding CONNECT contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.a5gl2w3zxojr"></a>

### IX-CONNECT-006 — Room details, voice/video calls and capture

Controls: Room details; Start voice call; Start video call; participant roster; Mute; Camera; Share screen; Record; End call; Mute conversation; Block; Leave

Atlas surfaces: connect-messages

Primary: Room details shows the current roster, roles, invitation state, expiry/encryption state and permitted management commands. Start voice call targets the selected conversation and requests the call's audio channel; Start video call additionally requests a camera channel. The call view exposes actual capture and output state. Mute stops outgoing microphone samples; Camera and Share screen independently choose and enable sources; End leaves the call and releases that user's capture. Recording is a separate command governed by the existing all-participant approval policy, not implied by joining or by captions.

Secondary: Participant menus offer identity/details and permitted role/removal actions. Mute conversation affects notifications; Block affects future communication according to channel policy; Leave changes membership. Call chrome has no shortcut menu that enables capture.

Drag: Participant/video tiles may reorder local layout by a drag handle; the preview never changes call roles or sharing grants. Dragging files onto the call does not send them unless an explicit conversation composer attachment target is shown.

Keyboard and touch: Keyboard focus identifies the active call before any mute/end shortcut. Touch tap toggles the named media source; long press opens device selection, not recording. Escape closes a selector; it does not silently end the call.

State: Connecting, ringing, connected, reconnecting and ended follow call receipts. Missing microphone/camera permission shows unavailable and keeps unrelated channels usable. A new participant joining during recording pauses capture until the recording policy is satisfied; recorder failures never show an active recording indicator.

Integration and persistence: Call and conversation identities remain separate. Closing a participant panel leaves the call active; closing the call surface follows the explicit background-call control. No media from a revoked/ended source may be admitted later through queued frames.

Acceptance: Start a voice call, turn the camera on, then revoke screen access: microphone and camera remain accurately represented, screen capture stops, and recording cannot continue through an unapproved participant joining.

Basis: Final static mounted Atlas inventory and interaction_screens/connect-messages.html; expanded_apps.md corresponding CONNECT contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.wvg5mim6ya4a"></a>

### IX-VISION-001 — Session capture, device selection and termination

Controls: Devices; Mute; Camera; Share screen; End; Microphone; Camera selector; Screen selector; source activity indicators

Atlas surfaces: vision-voice, vision-session-sources

Primary: Devices opens controls for the active SessionID. Mute toggles incoming microphone capture for that session, not the speaker volume or transcript retention. Camera selects and enables a camera only after its specific source grant; Share screen selects a display/window and exposes its actual capture extent. The source selectors change their own channel and preserve other channels. End terminates the session's capture and output and produces an ended event; it does not undo effects already completed by authorised tools. Activity indicators reflect admitted samples/frames and actual device state.

Secondary: Device rows expose select device, inspect source, and stop that channel. Capture buttons have no custom shortcut that skips the source picker or permission boundary. Secondary click on transcript text remains text selection.

Drag: Capture chrome is not draggable. Screen/window choices are selected by identity; dragging a preview cannot expand capture to the entire desktop. Preview layout dragging, when shown, changes layout only.

Keyboard and touch: Buttons use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)/002; device lists use [IX-BASE-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.a2i0alfkwp3d). Escape cancels the topmost device chooser. Keyboard/touch End uses the same session termination. System capture indicators remain perceivable while the app is backgrounded.

State: Requested, starting, active, muted, stopped, denied and disconnected are distinct. A source generation changes when selection or grants change; late frames from the old generation are rejected. A disconnected camera does not stop typed conversation or imply recording ended successfully.

Integration and persistence: The app, Space pill and Overlay control one SessionID rather than creating parallel accidental microphones. Hiding the Overlay keeps an explicitly active session; End closes it. A new app tab cannot silently acquire an old camera grant for a different session.

Acceptance: Switch from one shared window to another, then End during a pending frame delivery: the old window's late frame and all post-End capture must be excluded, while the transcript remains available under its retention policy.

Basis: Final static mounted Atlas inventory and interaction_screens/vision-voice.html; expanded_apps.md corresponding VISION contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.txtzdcvm6939"></a>

### IX-VISION-002 — Typed input, speech interruption and mode changes

Controls: Type something instead; Send typed message; Session options; Conversation; Monologue; Live Listener; Live Translate; pause/resume speech; language and output controls

Atlas surfaces: vision-voice, vision-session-sources

Primary: Send typed message submits the visible draft to the same ConversationID/SessionID as speech, then clears only after command admission. Session options chooses Conversation, Monologue, Live Listener or Live Translate and shows the resulting input/output configuration. Conversation supports turn taking; Monologue follows the requested objective and sections; Listener derives agreed notes without treating every utterance as a command; Live Translate has explicit source/target languages, direction, captions and speech destinations. Interrupting playback stops or ducks spoken output according to the session control, while a separate stop-generation command governs an unfinished response.

Secondary: Transcript entries offer Copy, inspect source/time and correction where that entry is editable. A translation can reveal its source segment. Mode selectors have no right-click permission shortcut and cannot turn every captured sentence into an action.

Drag: Drag-select transcript text for copy; dropping text into the typed composer stages a draft. Transcript dragging cannot reorder turns or rewrite their speaker identity. Output panels may resize locally without changing language or destination.

Keyboard and touch: Enter/Send follows the visible composer policy; Shift+Enter inserts a newline. Escape first dismisses a picker, then may stop explicitly focused speech playback when shown; it never silently revokes unrelated media. Touch has named interrupt and send controls.

State: Partial speech recognition and provisional translation remain marked until committed. Corrections replace the linked segment revision, not append a false second utterance. An unavailable mode/model route shows the missing capability and leaves the existing session usable.

Integration and persistence: Changing mode is a presentation/workflow change, not an increase in source or action grants. Monologue pause retains its section position; resume cannot repeat completed external effects. Typed messages and audio-derived messages share ordering but preserve origin metadata.

Acceptance: Type while a translated segment is provisional, correct the speech transcript, and interrupt playback: retain one corrected segment lineage, one typed turn and the same session; do not resend a tool action merely because speech resumes.

Basis: Final static mounted Atlas inventory and interaction_screens/vision-voice.html; expanded_apps.md corresponding VISION contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.84tzcul1fmcv"></a>

### IX-VISION-003 — Session sources and independent retention controls

Controls: Hide context; Add source; Add a source; Remove source; Close Session sources; Keep transcript; Record audio; Done; shared-source rows

Atlas surfaces: vision-voice, vision-session-sources

Primary: Hide context collapses the source panel without removing bindings. Add source/Add a source opens the authorised source picker; accepting it adds an identified SourceBinding to this session. Remove source revokes future admissions from that binding and invalidates its active derivative context according to the source policy; it does not erase provenance from the prior record. Keep transcript changes future transcript persistence; Record audio changes raw audio recording independently. Done closes the panel after acknowledging the applied changes; Close dismisses it without ending the session. Retention changes explicitly show whether they affect only future material or include a separately requested deletion.

Secondary: Source rows offer Open original, inspect binding scope and Remove source. Retention toggles have no custom secondary action that substitutes deletion or exports raw audio. Transcript text keeps normal text actions.

Drag: Drop an eligible artifact onto Add source to stage a binding preview; identity, selected section and permitted revision policy must be shown before adding. Drop of a screenshot/file into the chat composer is an attachment, not an implicit persistent source grant. No drag changes retention.

Keyboard and touch: Toggle changes are keyboard/touch operable with accessible actual state. Escape closes a source picker before the panel; focus returns to Add source or Devices. Remove is a distinct named action, not a swipe that can be confused with hiding the panel.

State: Unavailable recording support or a revoked microphone grant shows a reason. Pending permission does not display Record audio as active. Retention write failures leave an explicit unsaved/failed state and preserve the user's requested choice for recovery without promising storage.

Integration and persistence: Source binding, web/general-knowledge modes, memory and raw-media retention remain independent. A current Space label supplies identity/context policy, not all documents in that Space. Enabling transcript retention does not enable audio recording or model training.

Acceptance: Hide the context panel, remove a source through Devices, and enable Keep transcript only: no new retrieval may use the removed source, the audit retains earlier attribution, and no audio file is recorded.

Basis: Final static mounted Atlas inventory and interaction_screens/vision-voice.html; expanded_apps.md corresponding VISION contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.iiexy1w1ril2"></a>

### IX-VISION-004 — Derived notes, dictation and session artifact review

Controls: Live Listener note/task candidates; Monologue outline/sections; dictation Raw/Clean/Polished; insert dictation; save session notes; open derived artifact

Atlas surfaces: vision-voice

Primary: Derived notes and task suggestions are editable candidates with transcript segment references. Save notes writes the reviewed revision through its owning artifact API; creating a Task or plan uses its canonical identity and records a command receipt. Selecting a Monologue section navigates the objective outline; it does not mark unspoken sections complete. Raw, Clean and Polished dictation choose a transformation of the same recognised span; Insert applies the selected text to the explicitly focused editor as one undoable edit. Open derived artifact opens that original rather than recreating it.

Secondary: Candidate menus expose inspect source segments, edit, accept or dismiss. Dictation text supports copy and correction. A task's menu opens its owner and does not supply an unauthorised completion shortcut.

Drag: Drag a reviewed note candidate into an eligible editor as attributed content; dragging a Task uses its canonical reference. Drop previews distinguish insertion from linking. No raw audio is exported by dragging a note.

Keyboard and touch: Keyboard selection of a mode announces Raw/Clean/Polished; insertion is explicit. Escape cancels dictation insertion and restores the previous editor focus. Touch has separate accept and dismiss actions, with text editing available before commit.

State: An unavailable target editor, changed selection or stale document revision prevents blind insertion and offers reselect/review. Model-derived candidate status is distinct from saved notes or completed tasks. Reconnecting preserves accepted receipts and does not regenerate duplicates.

Integration and persistence: Listener output does not grant tools permission to execute speech as instructions. Save/insert is an owner mutation; transcription or analysis alone is read-only. Canonical goals, plans, action logs and checkpoint references survive cumulative compaction through their actual records.

Acceptance: Accept one suggested task, compact the conversation, and reconnect: the existing TaskID and creation receipt remain, the candidate is marked accepted, and accepting it again cannot create a duplicate task.

Basis: Final static mounted Atlas inventory and interaction_screens/vision-voice.html; expanded_apps.md corresponding VISION contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.liiry2in6prt"></a>

### IX-MAPS-001 — Search, place selection and map navigation

Controls: place query; Search/AI modes; place markers; saved place rows; Saved places; Locate me; Zoom in; Zoom out; map canvas

Atlas surfaces: maps, maps-route-options

Primary: Submitting a place query displays identified results with provider and freshness; selecting a marker/result opens that PlaceID's details. A single click on empty map space changes focus only; a map-coordinate context command may create a private custom landmark. Saved places opens the user's canonical saved references. Locate me requests a current location fix within the existing permission boundary and centres only after an actual result. Zoom buttons change map scale around the visual centre; mouse wheel/pinch anchors to the pointer/gesture centre. Search and AI modes operate on the same place and route records.

Secondary: Place menus expose details, save/unsave, use as origin, use as destination and add as stop. A coordinate menu exposes copy coordinates and create landmark. Menus preserve the selected place ID, not a new search for its display name.

Drag: Drag empty map to pan; marker activation is suppressed after a pan threshold. Dragging a custom landmark handle previews new coordinates and commits one undoable landmark revision on release. Provider-owned places cannot be repositioned. Escape restores pre-drag coordinates.

Keyboard and touch: Arrow keys pan a focused map, plus/minus zoom, and Tab reaches an accessible results list. Touch pinch/pan follows the same transform; long press opens the coordinate menu. Screen-reader users can select places and routes without operating the canvas.

State: Illustrative Atlas geography remains explicitly illustrative until backed by a real dataset. Stale/offline map data shows coverage and timestamp; no fix means no fabricated blue dot. Search responses arriving after a newer query are ignored.

Integration and persistence: Saved places and custom landmarks are private by default and remain independent of a conversation's sources. Map panning and selection do not alter a route or publish location. Location sharing is a separate explicit channel.

Acceptance: Request location while panning, deny the permission, then select a saved place: preserve the map and place selection, show no invented live fix, and do not change the planned route origin.

Basis: Final static mounted Atlas inventory and interaction_screens/maps.html; expanded_apps.md corresponding MAPS contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.mzio6ubmxk7u"></a>

### IX-MAPS-002 — Ordered route stops, travel mode and option drafts

Controls: Walk; Cycle; Drive; route alternatives; Add a stop; Route options; Prefer quieter streets; Avoid steep climbs; Step-free route; Cancel; Apply options; Close Route options

Atlas surfaces: maps, maps-route-options

Primary: Walk/Cycle/Drive select the requested mode and recalculate alternatives for the same ordered stops. Selecting an alternative changes the selected RouteVariantID. Add a stop opens a place/coordinate picker and inserts at the shown position; the duplicate Add a stop inside Route options edits that options draft. Route options opens a draft with current stops and constraints. Each preference toggles a named routing constraint; Apply options validates and commits one route revision, while Cancel/Close discards uncommitted option edits and restores focus.

Secondary: No custom menu on mode buttons. Stop rows offer inspect place, move earlier/later and remove; origin and destination are explicitly named. A manual journey step offers edit duration/activity, not provider-place deletion.

Drag: Drag a stop handle to an insertion marker in the route list; edge autoscroll reveals off-screen positions. Release commits order and recalculates; Escape restores order. Dragging stop text selects it. Reordering does not move the underlying saved landmark. Unsupported stop types or duplicate constraints show a rejected drop.

Keyboard and touch: Modes use radio/segmented selection semantics. Stop reorder has Move earlier/later equivalents. Touch holds the handle before moving; ordinary swipes scroll. Constraint toggles expose requested state and whether the provider honoured it.

State: Recalculation shows pending for the captured stop/mode/options revision. Failed or unsupported routing constraints leave the prior route available with a clear incompatibility; no 'step-free' guarantee is inferred from absent provider accessibility data. Late routes cannot replace a newer request.

Integration and persistence: Route plans retain manual waits, activities and checkpoint steps through recomputation. Applying options does not start navigation or share location. Travel time and distance belong to the chosen provider route snapshot and carry freshness.

Acceptance: Reorder an intermediate stop, choose Step-free route and change to Cycle before calculation completes: only the latest compatible route may activate, and any unverified accessibility constraint remains visibly unverified.

Basis: Final static mounted Atlas inventory and interaction_screens/maps.html; expanded_apps.md corresponding MAPS contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.z5l4k2gkgj0y"></a>

### IX-MAPS-003 — Route start, progress and offline coverage

Controls: Start route; stop/pause navigation; next instruction; route progress/checkpoints; offline area download; saved journey

Atlas surfaces: maps

Primary: Start route captures the selected route revision, checks its executable instructions and required location capability, and opens a NavigationSession. Starting with a manually supplied origin does not pretend a live location exists. Progress follows actual location or an explicit manual checkpoint command. Pause pauses guidance; Stop ends the navigation session while retaining the saved plan. Offline download selects a bounded dataset manifest with storage/coverage information and starts an identifiable job; it is separate from saving a route reference.

Secondary: Route menus offer inspect itinerary, save journey and permitted export; a checkpoint menu offers details and manually mark reached with its provenance. No contextual action overwrites provider instructions or disguises manual progress as GPS.

Drag: Dragging the navigation map pans the viewport and temporarily releases follow mode; a visible recenter control restores following. It does not move the route or user's location. Download-region handles preview the selected coverage before committing the download job.

Keyboard and touch: Guidance controls are operable without fine gestures. Accessible itinerary rows expose the current instruction and progress. Touch recenter and Stop remain reachable; Escape leaves a details sheet before ending navigation.

State: Outdated route, missing coverage, lost location, background restrictions and failed download have distinct recoverable states. No live traffic or exact arrival claim appears without actual data. Reconnection reconciles progress under the same session rather than restarting arrival actions.

Integration and persistence: Reaching a checkpoint cannot repeat a previously committed external action. Saved journey templates remain separate from one execution's progress and personal location history. Provider and device permissions are rechecked at future protected steps.

Acceptance: Start a route, lose location and mark a checkpoint manually: retain source='manual', suspend automatic position claims, and resume without duplicating the checkpoint or altering the original route template.

Basis: Final static mounted Atlas inventory and interaction_screens/maps.html; expanded_apps.md corresponding MAPS contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.vli3x974ly89"></a>

### IX-SHELF-001 — Collection navigation, search, views and launching

Controls: All items; Favourites; Recently opened; named collections; Search collection; Manual order; Card view; List view; Open [item]; item tiles/rows

Atlas surfaces: shelf, shelf-add

Primary: Collection selection filters membership without moving originals. Search filters the selected collection and shows target type; Manual order chooses the membership ordering policy, while Card/List changes presentation. Clicking a tile selects it; the named Open action, Enter or double click invokes its typed LaunchItem target exactly once. Targets may be a canonical document, Space, calendar view, folder, website or installed application profile; its label never determines execution. Recently opened reflects acknowledged successful opens, not mere selection.

Secondary: Item secondary click exposes Open, Open in new tab/window where the target declares that route, Rename display label, Add/remove favourite, Show original location, Move membership and Remove from collection. Uninstall appears only through the package owner's explicit operation and is separate from Remove.

Drag: Drag selected memberships by their handles to reorder Manual order or move them between collections. The insertion preview states Move membership or Add reference; a copy modifier adds another reference. Unsupported cross-target drops are rejected. Edge autoscroll reveals positions; Escape leaves both collections unchanged.

Keyboard and touch: [IX-BASE-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.a2i0alfkwp3d) selection applies; F2 renames the local display label. Touch tap selects and the visible Open action launches; long press opens the item menu. Search remains usable above the keyboard and does not trigger a launch when Enter only accepts a filter.

State: Broken, unavailable or unauthorised targets show repair/open-original choices and never guess another item with the same name. An opening spinner follows the launch receipt. A cloud-only target may require an actual fetch before readiness.

Integration and persistence: Shelf membership stores references, so removing a tile cannot delete a canonical document or uninstall an app. Favourites and collection order are user metadata. Opening the Study Space routes to Spaces; it does not create a new Shelf-owned subject.

Acceptance: Remove a document from Study while it also belongs to All items and North project: preserve the original and other membership, and opening it later must resolve the same ArtifactID.

Basis: Final static mounted Atlas inventory and interaction_screens/shelf.html; expanded_apps.md corresponding SHELF contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.j1wd4kfcw0q3"></a>

### IX-SHELF-002 — Add target, local label and collection management

Controls: Add item; New; New collection; Close Add to Study; Item type; Search documents; target candidate rows; Display name; Collection; Cancel; Add item in dialog

Atlas surfaces: shelf, shelf-add

Primary: Add item opens the add-target dialog; New opens a menu distinguishing Add existing target from New collection and explicitly routed owner-app creation. Item type limits the target resolver; search candidates show canonical location and identity. Selecting a candidate sets the LaunchItem target and suggests an editable display label. Collection chooses the destination membership. The dialog Add item commits that reference and focuses it in the destination; Cancel/Close preserves the original and adds nothing. New collection creates a named local collection after unique-identity validation, without copying its items.

Secondary: Candidate menus expose Open/inspect target; collection menus expose rename, reorder and remove collection. Removing a collection removes its memberships after the shown scope, not original files. The name field retains text-editing actions.

Drag: Drop an existing eligible target into the picker to resolve it and show a preview; it is not added until Add item. A URI requires its typed target representation and scheme validation. Dropping a folder does not recursively import its contents without an explicit import command.

Keyboard and touch: Tab traverses type, query, candidates, display name, collection and commit. Enter in search selects a highlighted candidate; it must not bypass the Add item review. Escape closes an inner picker before the dialog; touch provides the same controls.

State: Duplicate membership is detected by target identity and opens the existing membership or offers an explicit second shortcut only if that collection permits it. Missing target, revoked access and invalid launch parameters block commit with a concrete repair path.

Integration and persistence: Save membership and creation of a target are different transactions. Cancelling Add item cannot discard a target already created in its owner app; it merely leaves it unlinked. Display-label edits cannot rename the target document.

Acceptance: Add an existing Write document with a custom Shelf label, cancel and repeat: no duplicate document appears, and the original filename remains unchanged when the shortcut is finally committed.

Basis: Final static mounted Atlas inventory and interaction_screens/shelf.html; expanded_apps.md corresponding SHELF contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.y3paj3tmr72w"></a>

### IX-SHELF-003 — Typed launch profiles and broken-target repair

Controls: LaunchItem details; target type; arguments; working directory; profile selection; Locate original; Repair target; Remove shortcut

Atlas surfaces: shelf

Primary: Details reveals the target's canonical identity, launch route, declared arguments, working directory and capability requirements. Editing launch parameters saves a new profile revision; merely viewing details never executes it. Locate original resolves the stored identity. Repair target requires selecting a replacement and reviewing the changed target/parameters rather than substituting a similar filename automatically. Remove shortcut removes only the selected membership. Opening an executable launch profile passes the captured values to the owning launcher with correct argument boundaries.

Secondary: Profile menus offer Open, duplicate shortcut configuration, inspect target and Remove shortcut. Copying a profile excludes secrets and does not copy an executable or artifact. Dangerous system arguments remain visible under the existing execution policy rather than being hidden by a friendly label.

Drag: Target-picker drops can stage a candidate repair; arguments accept literal text, not arbitrary dropped executable payloads. A failed drop cannot overwrite the prior working profile. No drag executes a command.

Keyboard and touch: Profile fields use normal editing and [IX-BASE-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xj3xapubjjfz) validation where numeric. Keyboard/touch Save applies the profile; Open is separate. Escape cancels a repair draft and retains the original unresolved target.

State: Missing provider, removed application, disconnected device or unsupported target type yields a named unavailable state. A repair conflict shows both revisions. Profile validation is distinct from successful launch.

Integration and persistence: Launch settings are Shelf metadata but execution belongs to the target's owner/runtime. Shell quoting and argument arrays cannot be reinterpreted as an unreviewed command string. Successful invocation is not proof the launched app completed its work.

Acceptance: Rename or disconnect a referenced executable's device, then Open: show an unresolved target and retain its exact arguments; do not run a different local program sharing the display name.

Basis: Final static mounted Atlas inventory and interaction_screens/shelf.html; expanded_apps.md corresponding SHELF contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.o5ktveq0920o"></a>

### IX-PLAY-001 — Answer selection, submission and reveal

Controls: answer choices A–D; selected-answer state; Submit answer; round timer; answer reveal; result details

Atlas surfaces: play

Primary: A single click on an answer chooses the local candidate and changes the selected marker; a subsequent choice replaces it before submission. Submit answer sends the selected answer with MatchID, RoundID, participant identity and round revision. Repeated activation reuses the same submission identity. The shown timer follows the canonical round deadline and synchronisation state. Answers and correctness reveal only when the rules permit the round to close; clicking an unrevealed opponent does not disclose their answer. Result details show the actual scoring basis after release.

Secondary: No answer-choice custom menu reveals correctness or submits. Text selection/copy follows the match's actual allowed-assistance policy. A participant menu shows public identity/status only, never private model context.

Drag: Answer cards are not draggable, and dropping text or files cannot submit an answer. Scroll/selection gestures cannot change the chosen option after crossing a movement threshold.

Keyboard and touch: Choices form a radio group with arrow navigation; Space selects, and the distinct focused Submit button commits. Touch tap selects and a separate tap submits. Escape cancels a transient rule panel, not a submitted answer.

State: Submitting disables changes until acceptance or a reconciled failure. A late/closed round rejects with the canonical state; an uncertain timeout retains the original candidate and submission reference rather than sending a new answer. Unselected Submit is disabled with explanation.

Integration and persistence: Selection is a local draft; submission is a match event. AI/human contestants share the declared rules while retaining private response state. A reopened screen restores the acknowledged answer and cannot award points twice.

Acceptance: Select B, switch to C, submit during a disconnect and reconnect after reveal: reconcile the one submitted C answer, show its real score, and never count B or expose unrevealed opponents.

Basis: Final static mounted Atlas inventory and interaction_screens/play.html; expanded_apps.md corresponding PLAY contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.ribrwbmvj9l1"></a>

### IX-PLAY-002 — Rules, roster, score and match lifecycle

Controls: Match rules; contestant roster; leaderboard rows; pause/resume match; next round; replay match

Atlas surfaces: play

Primary: Match rules opens the pinned ruleset, scoring, assistance policy, participant identities and deadline handling for this match. Roster selection shows permitted public participant details. Leaderboard rows explain scores from released round events. Host-authorised Pause freezes progress according to the displayed clock rule; Resume continues that same match, and Next round advances only after the prior round's finalisation. Replay opens recorded match events read-only and never submits fresh answers.

Secondary: Roster context commands are limited to the user's declared role and do not give access to an Agent's private scratch context. Match-history menus may copy a result link or open scoring evidence. No right-click overrides a score.

Drag: The roster and leaderboard are ordered by their declared policy, not by free dragging. A setup-stage reorder, if offered, uses an explicit roster editor before match start and is outside a running round. No drag can change standings.

Keyboard and touch: Rules and roster dialogs follow [IX-BASE-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.g4q68g51ix5k). Accessible announcements identify round start, remaining time changes at configured useful intervals, submission acceptance and final score without continuously stealing focus. Touch exposes the same host controls.

State: Roster counts are dynamic; the Atlas's eleven contestants are an example, not a fixed limit. No-tools matches use runtime capability denial for participants, not just a prompt instruction. Policy or scoring-engine failures stop affected progression and retain accepted submissions.

Integration and persistence: Rule changes create a new match revision and cannot silently reinterpret a completed round. Scores derive from recorded outcomes; a manual adjudication is a separate attributable event. Pause never retracts already-revealed information.

Acceptance: Open Match rules in a no-tools round and attempt a participant tool call: the actual runtime must deny it, while replaying the completed match must leave scores and submitted answers unchanged.

Basis: Final static mounted Atlas inventory and interaction_screens/play.html; expanded_apps.md corresponding PLAY contracts. The Atlas shows the controls; the detailed gestures below are adopted target requirements, not claims of native execution.

<a id="h.e6zdc9s1hxku"></a>

### IX-BUILD-001 — Project explorer, editor tabs and buffer ownership

Controls: Explorer tree; file rows; editor tabs; Outline; Split editor; source text editor; encoding/line-ending selectors; Save; Source control

Atlas surfaces: build-code, build-code-diagnostics

Primary: Single click selects a file and may open a replaceable preview tab; double click or editing pins that tab. Folder disclosure expands children without opening them. Outline selects the stable symbol and reveals its source range. Split editor creates another view of the same buffer/revision and retains an explicit focused input target. Source text edits the document buffer; Save persists that buffer through the project owner and reports its revision. Encoding/line-ending changes preview the affected file and become a saved edit. Source control opens Stacks for this ProjectID and selected source scope.

Secondary: Explorer menus offer New file/folder, Rename, Move, Reveal location, Open beside and Delete with the exact source scope. Tab menus offer close view, close other views and split; closing a view is distinct from deleting a file. Editor menus offer code actions, copy and navigation.

Drag: Drag file tabs to reorder or move a view between split groups; no file bytes move. Drag tree files/folders shows a source-move destination and impacted references before one revision-checked project mutation. Reject cycles and moves outside granted roots. Escape restores the prior tree and focus.

Keyboard and touch: Arrow/Enter tree navigation and keyboard tab switching identify the focused editor. Rename uses the explicit file operation; ordinary text selection never renames a file. Touch uses tab overflow and tree menus rather than requiring tiny drag handles.

State: Dirty, saving, saved, externally changed and conflicted are distinct. An external edit cannot silently overwrite an unsaved buffer; present reload, keep local or merge choices with revisions. Missing language support still allows faithful text editing and states absent diagnostics.

Integration and persistence: All editor views share the same canonical file identity, while caret, folds and scroll are per view. Closing the final dirty view preserves recoverable work under the save policy; Source control cannot commit an unsaved buffer by pretending it is disk state.

Acceptance: Split a dirty file, change it in either pane and rename it from Explorer: both panes retain one buffer and its edits, while a failed source rename leaves the original file and references intact.

Basis: Final mounted Atlas interaction_screens/build-code.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.2z6gd14otlgr"></a>

### IX-BUILD-002 — Completion, diagnostic fixes and stale analysis

Controls: completion list; Use Title; Problems rows; Output; Problems; Terminal; Tests; diagnostic range; quick-fix preview; completion Tab/Enter/Escape

Atlas surfaces: build-code, build-code-diagnostics

Primary: Typing requests analysis tagged to buffer revision, caret context and language/schema. Selecting a completion previews documentation and replacement range; Tab/Enter accepts the focused candidate as one undoable edit. Use Title applies the displayed replacement to its diagnosed symbol only after revalidating that range. A Problems row focuses its file/range; selecting Output, Problems, Terminal or Tests changes the lower panel without executing a process. Multi-file refactors show the full diff and apply one declared transaction to still-matching revisions.

Secondary: Diagnostic menus offer details, copy message and available registered code actions; suppressions require an explicit persisted rule and scope. Completion documentation is selectable. Right click cannot accept an unfocused suggestion.

Drag: Drag-select source text independently of a completion popup. Diagnostic rows can be copied as reports but cannot be dragged onto source to execute a fix. Panel splitters resize after a threshold and leave the buffer unchanged.

Keyboard and touch: Arrow keys navigate the suggestion list; Escape dismisses it without reverting typed text. Tab/Enter insert only while the list owns focus, otherwise they retain normal editor behaviour. Touch taps a candidate then its accept affordance, with the replacement range visible.

State: Results for older revisions are discarded or labelled stale and cannot apply blindly. An invalid current draft may keep the last valid preview, which is explicitly labelled with its source revision. Analysis failure is not '0 errors'; the state is unavailable/failed.

Integration and persistence: CUI diagnostics use the current registered grammar and resolver; historical example syntax is not proof of a shipped parser ABI. A fix updates the authored file only, and its success is distinct from compilation, test results or preview refresh.

Acceptance: Request completion for Task.Titl, edit the identifier before the result arrives, then click Use Title: a stale replacement must revalidate or refuse; it cannot overwrite the newly typed property or mark the old preview Current.

Basis: Final mounted Atlas interaction_screens/build-code.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.gxu7qn5uxw3"></a>

### IX-BUILD-003 — Run, test, debug and owned process controls

Controls: Run / Run the selected local configuration; configuration selector; Tests; test case rows; run/stop/debug actions; Output log; Terminal process link

Atlas surfaces: build-code, build-code-diagnostics

Primary: Run snapshots the selected ProjectID, configuration, source/build revision, runtime, working directory, arguments and environment references, then admits one process run. Tests opens results; an explicit test-run action executes the chosen suite/case IDs. Debug starts the selected debug configuration and exposes pause/continue/step only for that DebugSession. Stop targets the owned process tree. Clicking a log source link opens the associated file/revision or command receipt; it does not rerun that command.

Secondary: Run-history menus offer inspect inputs, open logs, copy a safe command representation and rerun with an explicit current or pinned configuration. Test rows offer run selected, debug selected and inspect failure. Output copy excludes secret values under the logging policy.

Drag: Drag panel boundaries to resize; dragging a test row selects or exports its reference, never starts a test. Source files dropped on configuration fields stage an explicit path value, with shell-safe argument boundaries and validation.

Keyboard and touch: Focused run controls use standard activation. Terminal keyboard signals affect only the terminal's focused process; Build Stop remains an explicit owner command. Touch exposes separate Run and Stop states without overloading a long press.

State: Queued, compiling, starting, running, stopping and completed/failed states follow actual receipts. A successful old build stays historical when source changes. Missing toolchains, denied capability or resource admission waits are named; duplicate Run clicks coalesce while admitting the same run.

Integration and persistence: Saving source, compiling, launching and passing tests are separate milestones. Resource orchestration must not kill the control plane or the model session directing a GPU test. Closing a log panel detaches the view and does not terminate a run without the configured explicit close action.

Acceptance: Run a test configuration in one split and a preview server in another, then Stop the test: terminate only its owned tree; keep the server and orchestration session alive and report the test's real result.

Basis: Final mounted Atlas interaction_screens/build-code.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.g35lhkw2i5uf"></a>

### IX-BUILD-004 — Live preview, design bindings and source fidelity

Controls: Live preview; Viewport; preview State; source/design selection; Open source; last-valid/current badge; preview refresh

Atlas surfaces: build-code, build-code-diagnostics, build-sites, build-sites-responsive

Primary: Preview selects a declared target viewport and initial state for the captured source revision. Clicking preview elements in inspection mode maps to a canonical node/source range; in interactive preview mode it runs only the allowed preview commands. State chooses a preview fixture/state, not a live user's Task state. Open source reveals the selected node's source in the same project. A refresh renders the latest valid draft while preserving explicitly retained preview state; the badge identifies source revision and whether it is current.

Secondary: Element inspection menus offer reveal source, inspect properties and select parent. A preview-only external action remains blocked or uses its explicit test profile; a context menu cannot elevate it to a live command. Unknown custom components can be inspected as preserved opaque nodes.

Drag: Dragging layout handles in design mode stages source-backed changes and shows affected constraints. Interactive drag inside preview affects only that preview session. These modes must be visibly distinct; an unknown source construct is not deleted merely because the visual editor cannot represent it.

Keyboard and touch: Mode switching is keyboard accessible; Escape exits a nested interaction/selection before clearing it. Touch offers inspect and interact modes explicitly. Viewport sizing has numeric equivalents to resize handles.

State: Parse or binding errors retain the last valid rendering with a stale/current-draft error explanation. Preview crashes preserve source and diagnostics. Runtime callbacks tagged to obsolete preview generations cannot mutate current project state.

Integration and persistence: Native authored interface structure, style, state, behaviour and orchestration remain CUI-owned; platform/native adapters supply infrastructure and declared capabilities. This interaction contract does not silently assert the old ten-primitive syntax as today's implementation ABI.

Acceptance: Break a CUI binding while its prior preview shows a completed checklist: display Last valid, reject stale preview writes to the live Task, and preserve unknown authored source when switching between visual and text views.

Basis: Final mounted Atlas interaction_screens/build-code.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.dgtdn97yc0b7"></a>

### IX-BUILD-005 — Sites pages, component insertion and canvas selection

Controls: Pages; Assets; page rows; Page outline; Add section; Insert / Insert a registered section or component; Select; Insert component; Undo; component canvas; source/design selection

Atlas surfaces: build-sites, build-sites-responsive

Primary: Pages lists canonical SitePageIDs; selecting a page changes the editor target without publishing it. Assets opens registered project assets. Page outline and canvas selection synchronise by node ID; Select enters inspection/selection mode rather than activating page links. Add section/Insert/Insert component opens the registered palette with a destination insertion point. Choosing a component stages its supported properties and inserts one authored node revision. Undo reverses the latest local authoring transaction when its dependencies still allow it.

Secondary: Component menus offer select parent, duplicate, move, inspect source and remove with dependent-link consequences. Asset menus distinguish remove project reference from delete original asset. Links within edit mode expose edit target/open preview; they do not navigate away unexpectedly on selection.

Drag: Drag a palette item to allowed layout slots with insertion and parent highlights. Drag an existing component moves that node, not a screenshot; a copy modifier duplicates with new IDs and valid internal reference remapping. Reject invalid nesting/cycles and out-of-scope parents. Edge autoscroll and Escape preserve the previous tree.

Keyboard and touch: Outline keyboard commands provide move earlier/later and change parent equivalents. Touch long press selects a move handle; ordinary scroll does not rearrange content. Escape closes the palette before clearing selection.

State: Unsupported registered components show a capability reason. Concurrent tree edits revalidate the chosen parent/anchor before commit and offer a new position when it disappeared. Saving draft, previewing and publishing have separate indicators.

Integration and persistence: CMS, Forms and Data embeds keep their owning canonical records; inserting an embed creates a reference and declared view, not a duplicate database. Authored CUI/project source remains the source of truth for the visual editor; code-only nodes survive round trips.

Acceptance: Insert a Forms embed, move its section above Hero, then Undo: restore the layout without creating/deleting the form or changing its submissions, and retain the same embed target through the movement.

Basis: Final mounted Atlas interaction_screens/build-sites.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.4mppgsrxgr22"></a>

### IX-BUILD-006 — Responsive layout properties and inherited overrides

Controls: Desktop preview; Tablet preview; Mobile preview; Preview width; Orientation; Display; Content order; Gap; Max width; Reset; Padding X; Padding Y; Narrow navigation; Reset overrides; Done; Close panel; Alignment; Background

Atlas surfaces: build-sites, build-sites-responsive

Primary: Desktop/Tablet/Mobile choose the inspected breakpoint and viewport preset, not separate page copies. Preview width changes the preview width independently of the breakpoint's authored rule. Orientation changes the viewport dimensions. Display and Content order set the selected section's breakpoint-scoped layout; Gap, Max width and Padding X/Y accept typed values and units. Reset beside a property removes that property's local override and exposes its inherited value. Narrow navigation chooses the responsive header presentation while retaining link identities. Reset overrides previews and removes the selected breakpoint's overrides; Done commits valid pending fields and closes, while Close follows the panel's explicit pending-edit policy without applying invalid text. Alignment sets the selected section’s declared alignment constraint. Background edits that section’s authored colour/token or supported paint at the shown breakpoint; it does not change the app theme or other pages. A token reference remains a reference rather than being silently replaced with a sampled colour.

Secondary: Property menus show inherited origin, copy value and reset override. They cannot edit another breakpoint silently. A source property that is unsupported visually remains inspectable and preserved.

Drag: Viewport resize handles adjust preview width after a threshold; layout handles edit only the selected property/breakpoint with a live preview and one final undo transaction. Escape restores the start value. Dragging text within a numeric field selects it, not a canvas object.

Keyboard and touch: Numeric controls follow [IX-BASE-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xj3xapubjjfz) and expose units. Arrow/step controls honour declared bounds; typing intermediate invalid input remains a draft. Touch uses labelled fields and handles, with keyboard-accessible reset commands.

State: Mixed multi-selection and inherited/explicit states are distinguishable. Invalid units, impossible constraints or stale section revisions block commit and identify the field. A narrow viewport does not automatically rewrite authored desktop layout.

Integration and persistence: Changing layout preserves semantic reading order and accessibility. Visual reordering that differs from logical order must expose that relationship; no decorative CSS transform may hide the actual focus order. Preview resolution is not a device capability claim.

Acceptance: Change Mobile Gap from inherited 32 px to 20 px, then Reset that property: Mobile inherits the current base again, Desktop remains unchanged, and closing with '20 p' invalid text must not commit a malformed value.

Basis: Final mounted Atlas interaction_screens/build-sites.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.tr7twmun0qwo"></a>

### IX-BUILD-007 — Site preview, publication and deployment receipts

Controls: Preview / Open responsive preview; Publish / Review target and changes before publishing; target environment; domain; publish review; deployment status; rollback

Atlas surfaces: build-sites, build-sites-responsive

Primary: Preview opens the selected draft/build in an isolated preview session with its revision and test capability profile. Publish opens a concrete review of source revision, generated output, target environment/domain, changed assets/routes and any data/form consequences. The final Publish command submits that reviewed deployment using one idempotency identity. Deployment status follows provider acknowledgement and live verification. Rollback selects a retained deployable revision and executes the provider's supported rollback operation; it is not a fictional browser Undo.

Secondary: Deployment history menus offer inspect manifest/logs, open the target URL, compare revisions and a declared rollback action. Copy link copies the preview or production URL with the correct scope. Secondary click cannot silently switch staging to production.

Drag: Domain and environment controls are not drag targets. Files dropped into Assets are imports governed by asset ownership, not publication. A preview may be resized without modifying the reviewed publish target.

Keyboard and touch: Keyboard/touch publication uses the same review and final command. Escape cancels an unsubmitted review; once submitted it closes only the view unless an explicit provider-supported Cancel action is invoked.

State: Draft saved, preview ready, uploading, provider accepted, active/verified and failed/unknown are distinct. Target changes invalidate the review. A timeout is reconciled by deployment ID before retry; no claim of a live site is made from local build success.

Integration and persistence: Authored source remains the canonical editable artifact; export/build output and deployed hosting are derived revisions. Publishing existing authorised work does not require a new generic approval gate, but the exact target and effect must remain visible. Secrets never enter public client output.

Acceptance: Review a staging publish, change the domain to production, then activate Publish: require a refreshed concrete review and ensure retry after an uncertain timeout resolves the original deployment rather than producing a duplicate release.

Basis: Final mounted Atlas interaction_screens/build-sites.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.c318r1ywpc0i"></a>

### IX-BUILD-008 — Stacks domain selection, boundaries and change review

Controls: Branches; Main/Branch/Twig/Leaf rows; Current domain; Boundaries; Review changes / Review exact proposed changes; Inspect source and dependency scope; View history / Inspect domain history; conflict rows

Atlas surfaces: build-stacks, build-stacks-freeze

Primary: Selecting a domain changes the inspected DomainID and its effective source boundary, showing local/incoming counts and inherited restrictions. Boundary selection opens the named resource's scope; it does not freeze all files in the domain. Review changes computes the proposal against captured base/local/incoming revisions and focuses the first unresolved conflict. Inspect source and dependency scope exposes the exact affected resources and protected roots. View history opens attributable domain events; a conflict row opens its side-by-side diff and proposed resolution.

Secondary: Domain menus expose allowed create child, rename, compare, sweep and prune operations with exact scope. Root protection, resource Freeze and ordinary branch selection remain distinct. History menus open a revision or copy its identifier; they do not move the active head.

Drag: Drag a domain in the tree only within an explicit structural-move mode with inheritance/dependency preview; normal row dragging selects text or ranges. Diff hunks cannot be dropped into protected source as an implicit merge. Escape cancels a scope preview without changes.

Keyboard and touch: Tree navigation uses stable IDs and accessible relation labels. Keyboard selection of a diff reveals its base and proposed ranges. Touch opens detail before offering domain mutations; long press has no hidden merge behaviour.

State: Stale base/local/incoming revisions invalidate resolution validation. A missing dependency prevents a complete review rather than being treated as an empty file. Frozen/protected state is effective and inherited, with the reason and authority displayed.

Integration and persistence: Code/Sites/Stacks share ProjectID and source identity. Selecting a domain cannot switch an already-reviewed run or deployment's target. Sweep collects a reviewable proposal; Prune uses its existing stronger protected-source policy and cannot be reduced to deleting a row.

Acceptance: Select a Leaf with one frozen resource and inspect another editable file: only the matching resource scope is blocked; switching domains must not retarget an open merge review to a similarly named file.

Basis: Final mounted Atlas interaction_screens/build-stacks.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.w9cgwtfigpa6"></a>

### IX-BUILD-009 — Conflict Reject, accept draft and atomic merge

Controls: Reject / Reject proposal and retain original source; Accept without Merging / Save reviewed resolution without merging; Accept and Merge / Commit the reviewed resolution and merge; resolution editor; validation status

Atlas surfaces: build-stacks, build-stacks-freeze

Primary: Reject discards the selected proposal/resolution and retains original source and head references; it records the rejection and leaves unrelated proposals intact. Accept without Merging saves the reviewed resolution as a proposal/draft revision without moving the target head. Accept and Merge revalidates base/local/incoming source and policy, commits the exact resolution and moves only the declared merge target atomically. Editing the proposed pane invalidates its previous validation result and preserves the edited draft for review.

Secondary: The resolution pane offers copy, select hunk and explicit take local/incoming actions with a preview of the replacement range. Those actions edit the resolution draft; they do not merge. The three final commands have no alternative right-click execution path.

Drag: Text drag within the resolution editor is one draft edit. Dragging a hunk from local/incoming into the proposed pane shows the replacement and remains undoable before commit. Whole-file source moves and head changes are not drag operations.

Keyboard and touch: Tab order reaches local, proposed, validation and the three distinctly named actions. Keyboard shortcuts cannot map both accept commands to the same event. Touch preserves explicit labels rather than ambiguous icon-only acceptance.

State: Validation must match the exact resolution digest and all source/policy revisions. Concurrent edits invalidate it and show what changed; a failed merge keeps the saved resolution and unchanged head. Partial source writes are rolled back or surfaced as a recoverable transaction, never 'merged'.

Integration and persistence: Draft acceptance and source mutation are separate commands/receipts. Protected scopes, freeze and dependency checks run at commit. Undo after a completed merge means an explicit new revert proposal, not erasing attributable history.

Acceptance: Accept without Merging, reopen Code and inspect its source: the target head remains unchanged. Edit incoming source before Accept and Merge and verify the old validated resolution cannot be committed without revalidation.

Basis: Final mounted Atlas interaction_screens/build-stacks.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.a1hvd6nelbx6"></a>

### IX-BUILD-010 — Resource Freeze scope, reason and thaw review

Controls: Close; reason field; Edit scope; View history; Review thaw; affected descendant scope; queued upstream changes; expiry

Atlas surfaces: build-stacks-freeze

Primary: Boundary details shows target resource, scope parent, descendants included/excluded, creator, reason, creation revision and expiry policy. Edit scope opens a revisioned draft of that exact resource's inheritance boundary; changing the reason alone does not alter affected files. View history displays freeze/thaw and queued-change events. Review thaw lists active operations and pending upstream changes, identifies what will become eligible, and validates the proposed policy transition. The final thaw command changes the boundary policy; Close merely dismisses the panel.

Secondary: Scope-row menus expose inspect domain and explain effective inheritance. Queued-change menus inspect the change or withdraw it when authorised; they do not apply it while frozen. The reason field supports normal text editing.

Drag: Drag-to-select scope ranges is allowed only inside the explicit scope editor with inclusion previews; a normal tree drag cannot expand the freeze. Queued upstream changes cannot be dragged out to bypass the boundary.

Keyboard and touch: Keyboard/touch expose expand descendant, include/exclude permitted branch and review affected scope. Escape leaves the active freeze unchanged while preserving or discarding the scope draft according to its explicit controls.

State: Unsupported expiry expressions, stale policy revisions and conflict with protected roots block a policy commit. Expiry does not imply queued changes were merged. Effective state remains frozen until a successful transition receipt.

Integration and persistence: A resource Freeze applies only to its named resource within the scoped subtree, not to every edit in that Leaf. Thaw revalidates queued changes against current source and authority; it does not automatically apply obsolete operations. Existing protected-root safeguards remain intact.

Acceptance: Thaw a resource after an upstream queued change's base has diverged: record the thaw, keep that change pending/conflicted, and do not overwrite newer source or thaw an outside sibling Twig.

Basis: Final mounted Atlas interaction_screens/build-stacks-freeze.html and corresponding overlay; expanded_apps.md BUILD contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.4qiw5k5gkfe7"></a>

### IX-AISTUDIO-001 — Definition navigation, configuration and typed logic

Controls: Home; Projects; Build; Harnesses; Agents; Tools; Playground; Evaluations; Test Suites; Replay; Schemas; Model Routers; Generative UI; Run Profiles; Library; Templates; Configuration; Logic; Versions; Agent configuration; Basic/Expert views; Resources

Atlas surfaces: ai-studio, ai-studio-permissions

Primary: Registry navigation opens the selected definition category without changing the current active Agent. Configuration and Logic are views of the same DefinitionID/revision; Versions shows immutable saved/active lineage. Agent configuration opens the selected draft's identity, permissions and resources. Basic/Expert presentation preserves fields that one view does not expose. Typed graph-node selection focuses that node's inspector; form changes update the same typed model. Creating from a template records its provenance and a new definition identity, not the template author's authority.

Secondary: Definition menus expose duplicate draft, export package, inspect dependencies and archive under registry rules. Node menus expose inspect type, rename label, duplicate with remapped IDs and delete with dependent-edge review. Credentials are never copied into exported packages.

Drag: Drag registered typed nodes into the graph, then connect only compatible ports with a validity preview. Moving nodes changes layout; connections change logic. Reject unsupported cycles, missing required inputs and edges crossing definition scopes. Escape cancels the in-progress edge.

Keyboard and touch: Keyboard graph navigation identifies node type and ports; add/connect commands have dialog equivalents. Tab and touch select inspectors without triggering Test or Activate. Zoom/pan gestures are separate from node movement.

State: Draft validation reports type/schema errors and absent dependencies. Unknown newer fields are preserved and labelled rather than dropped by Basic view. Active revision remains visible separately from unsaved and saved draft revisions.

Integration and persistence: Agent/Assistant Den, Specialist, harness and tool definitions keep their declared kinds. Model selection is a requested route constrained by actual provider support; a graph grants no capability merely by naming a tool. The same runtime permission and command contracts apply in every view.

Acceptance: Switch a definition with an Expert-only permission field through Basic and back: preserve it unchanged, and a node positioned differently must not change the definition's capability grants or active revision.

Basis: Final mounted Atlas interaction_screens/ai-studio.html and corresponding overlay; expanded_apps.md AISTUDIO contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.yixyomshe328"></a>

### IX-AISTUDIO-002 — Permission source scope and action policy drafts

Controls: Identity; Permissions; Resources; Source scope; Read subject notes; Read unrelated private files; Action policy; Discard; Save draft; Close panel

Atlas surfaces: ai-studio, ai-studio-permissions

Primary: Source scope chooses explicitly authorised bindings such as the current Study subject or selected sources. Read subject notes enables the declared read capability within that scope; the unrelated-private-files row remains outside scope unless a distinct authorised grant is deliberately added through the appropriate permission flow. Action policy selects this definition's permitted handling of Create practice—such as Ask before creating or Disabled—and does not disable organisation/runtime mandatory rules. Save draft validates and persists the policy revision; Discard restores the last saved draft. Close preserves only according to the panel's explicit draft policy and never activates changes.

Secondary: No custom menu bypasses source authority. A permission row may expose explain effective grant, inspect source owner and inspect inherited restriction. It cannot copy another Agent's token or broaden scope from a label.

Drag: Permission toggles and scope fields are not draggable. Dropping an artifact onto an explicit source picker stages its binding and access preview; dropping it onto the graph does not grant read/write capability.

Keyboard and touch: Checkboxes announce checked/unchecked/unavailable and inherited states. The labelled Source scope and Action policy selectors are keyboard/touch operable. Escape cancels the current picker before closing the policy draft.

State: Missing owner access, revoked grants or unavailable tool methods show a specific unavailable state. A saved policy is still a draft until activated. Concurrent organisation policy changes recompute effective permissions and may invalidate validation.

Integration and persistence: The Atlas's Ask before creating is this configured Agent policy, not a new universal requirement to ask before ordinary local work. Effective permission is the intersection of user/source/tool/runtime/org authority. Source binding, memory and web/general knowledge remain independent controls.

Acceptance: Save a draft that permits reading selected subject notes, then test it against an unrelated private file: the runtime denies that read, the active Agent remains on its earlier policy revision, and the denied result is visible in the test.

Basis: Final mounted Atlas interaction_screens/ai-studio.html and corresponding overlay; expanded_apps.md AISTUDIO contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.eaihwhgbaqar"></a>

### IX-AISTUDIO-003 — Draft tests, profiles, logs and evaluation evidence

Controls: Run test / Run this draft under the selected test profile; test prompt; Sandbox; Run profile; test transcript; tool-call rows; Evaluations; Test Suites; Replay; stop test

Atlas surfaces: ai-studio, ai-studio-permissions

Primary: Run test snapshots the current saved/testable draft, prompt, source fixture revisions, model route and selected RunProfile, then starts an identifiable TestRun. Sandbox/profile selection states which tools are mocked, denied or permitted live. The test transcript shows actual output, tool requests, approvals/denials and receipts; clicking a tool row inspects its captured inputs/results. Evaluations and Test Suites run declared cases with pinned criteria and report per-case evidence. Replay reconstructs recorded test events without invoking tools again. Stop cancels future admitted work and reconciles in-flight effects.

Secondary: Test menus offer inspect configuration, copy safe output, rerun a selected case with explicit current/pinned inputs and compare runs. They cannot relabel a mocked result as a live side effect. Evaluation rows expose rubric and source evidence.

Drag: Drag a test fixture to the input area stages it as a named test source; it does not attach all files in a folder automatically. Graph movement during a test edits a future draft and cannot alter the running snapshot.

Keyboard and touch: Enter in a multiline test prompt inserts text unless the visible send shortcut is used. Keyboard/touch Run and Stop retain unique run identity. Escape closes output detail without cancelling the test.

State: Queued/running/awaiting configured approval/cancelled/completed/failed are actual states. A stale result remains tagged to its tested revision. Provider, resource or permission failures are case outcomes rather than passing tests. Test cancellation does not undo already-completed permitted effects.

Integration and persistence: Testing never activates the draft or changes live grants. A read-only sandbox may propose a Forms quiz but must not create it; a deliberately configured live test follows exact existing action policy. Quality metrics are evidence, not a guaranteed improvement claim.

Acceptance: Run a read-only test, edit the draft while it streams, then open a tool call: the call identifies the old pinned draft and mock/denied status; activation cannot claim the new untested draft passed that run.

Basis: Final mounted Atlas interaction_screens/ai-studio.html and corresponding overlay; expanded_apps.md AISTUDIO contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.b545cczg58k5"></a>

### IX-AISTUDIO-004 — Compare, activate, revert and package export

Controls: Compare / Compare draft with active revision; Activate / Review and activate this validated definition; Versions; select active revision; revert activation; export definition

Atlas surfaces: ai-studio, ai-studio-permissions

Primary: Compare shows draft versus active identity, instructions, graph, tools, sources and model routes, including effective permission differences. Activate opens the exact validated revision and its test/dependency status; the final activation commits that DefinitionRevision as active for future runs. Existing runs keep their captured revision unless an explicitly supported migration is chosen. Revert selects an earlier compatible revision and performs a new activation event. Export writes a portable package manifest of the chosen revision and declared dependencies without secrets.

Secondary: Version menus offer compare, inspect, export and activate a compatible revision. 'Delete draft' cannot delete the active version or event history. A copied package carries source provenance but no inherited account credentials.

Drag: Version rows are not draggable into the active slot to bypass validation. Export destination selection may accept a valid folder through the storage picker; it does not publish the package externally.

Keyboard and touch: Keyboard/touch activation uses the named final command and restores focus to the definition status on success. Escape cancels a pre-submit review. Duplicate activation clicks coalesce to one operation.

State: Validation and tested status are bound to the exact revision/digest and current capability catalogue. Missing tools, revoked sources or changed mandatory policy prevent activation with a concrete explanation. A provider error retains the earlier active revision; no optimistic 'active' badge appears.

Integration and persistence: Activation is a registry change, not model training or a new provider grant. The currently selected model's capabilities constrain reasoning/context controls truthfully. Export/save/activate are separate receipts and can fail independently without corrupting the canonical draft.

Acceptance: Validate draft r8, change its action policy and click Activate: the old validation cannot activate the modified content, and cancelling review leaves the previous active revision serving new conversations.

Basis: Final mounted Atlas interaction_screens/ai-studio.html and corresponding overlay; expanded_apps.md AISTUDIO contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.xv5sr0i1eyw2"></a>

### IX-GAMES-001 — Scene tree, transforms, assets and components

Controls: Scene tree; Assets; 3D; 2D; UI; Select; Move; Rotate; Scale; local/world transform selector; transform fields; Add component / Choose a registered purposeful component; Open development workspace; Movement speed; Jump height; Ground detection; Collision shape

Atlas surfaces: games

Primary: Scene-tree and viewport selection resolve the same SceneObjectID. Select picks objects without changing transforms; Move/Rotate/Scale choose manipulators, while typed position/rotation/scale fields edit the selected transform in the displayed coordinate space. 3D/2D/UI changes the authored view, not object identity. Add component opens the registered component palette and validates required properties before attaching a component. Open development workspace exposes the same project source and assets; it does not create another project. Movement speed and Jump height edit typed component parameters with displayed units and declared bounds. Ground detection chooses the registered detection method; Collision shape edits the selected object’s declared collider type and dimensions. These are authored component settings, not claims of measured runtime behaviour, and invalid combinations are rejected before commit.

Secondary: Object menus offer frame selection, duplicate with remapped child IDs, parent/unparent, inspect dependencies and remove. Asset menus distinguish instantiate in scene, add reference and remove project reference. Component menus expose reset declared defaults, enable/disable and remove with dependency review.

Drag: Drag a transform handle previews constrained motion; release commits one undo transaction and Escape restores start. Drag assets into the scene shows instantiate/reference semantics and placement; tree reparenting rejects cycles and previews transform preservation. Edge autoscroll applies to tree moves.

Keyboard and touch: Keyboard provides numeric transform and nudge controls with displayed units; touch selects a handle before movement and has explicit axis constraints. Escape cancels an active transform before deselecting. Multi-selection reports mixed component fields.

State: Missing assets/components show placeholders with stable references. Invalid physics/transform values or incompatible component combinations block the affected edit. Concurrent selection changes cannot apply a delayed inspector value to another object.

Integration and persistence: Scene authoring, gameplay implementation and native app UI remain separate owners. CUI owns the 9to1 app surface; game-specific C#/C++/engine adapters retain their defined domain. Moving an instance does not alter the shared source asset by default.

Acceptance: Duplicate a parent with children, drag it under an invalid descendant, then cancel a transform: no cycle is created, child references remain correct, and the original asset and transform are unchanged.

Basis: Final mounted Atlas interaction_screens/games.html and corresponding overlay; expanded_apps.md GAMES contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.5y51yhhqb3u0"></a>

### IX-GAMES-002 — Animation tracks, keys and preview

Controls: Animation selector; Preview animation; timeline ruler/playhead; property tracks; keyframes; clip duration; interpolation controls

Atlas surfaces: games

Primary: Selecting an animation loads its canonical AnimationID and bound tracks. Preview animation plays it in the editor's preview state; it does not start gameplay or save the sampled transforms as defaults. Clicking the timeline ruler seeks the preview; selecting a key reveals time/value/interpolation and its target property. Editing a key or duration commits an authored animation transaction, with dependent keys beyond a shortened duration explicitly reviewed.

Secondary: Key menus offer duplicate, delete, copy value and interpolation choice. Track menus expose binding inspection, mute preview and remove track. Copying a key cannot silently retarget it to a property with incompatible type/unit.

Drag: Drag key handles along time with a visible snap policy; selected keys move as a group preserving relative time. Drag value handles edits the supported property dimension. Crossing bounds is clamped or rejected visibly; Escape restores the prior keys. Timeline panning is distinct from key movement.

Keyboard and touch: Arrow keys nudge selected keys by the displayed time quantum; numeric time/value fields provide precise alternatives. Space plays/pauses only while the animation pane owns focus. Touch uses large playhead/key handles and a separate selection mode.

State: Unresolved target bindings keep keys intact and show an error. Playback reports loading/playing/paused/stopped from the preview engine; unsupported interpolation is unavailable rather than approximated invisibly. Older preview callbacks cannot overwrite a newer clip.

Integration and persistence: Animation preview has its own generation and state; Stop restores the authoring pose according to the explicit preview policy. Undo acts on authored keys, not on elapsed playback. Animation assets can be referenced by multiple instances without copying the entire scene.

Acceptance: Preview an animation, change the selected object, then drag a key: the key retains its original bound object/property, and stopping preview cannot overwrite the newly selected object's authored pose.

Basis: Final mounted Atlas interaction_screens/games.html and corresponding overlay; expanded_apps.md GAMES contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.slejfn5d4tlw"></a>

### IX-GAMES-003 — Play scene, runtime state and controlled apply-back

Controls: Play scene / Run a test session separate from authoring state; pause; step; stop; runtime inspector; apply runtime changes; test log; runtime input capture

Atlas surfaces: games

Primary: Play scene snapshots the project/scene revision and starts a distinct TestSession with its declared input/network profile. Pause freezes the supported simulation state; Step advances one declared simulation step; Stop ends the session and returns to authoring state. The runtime inspector marks transient values explicitly. Apply runtime changes opens a diff of supported transferable changes and commits only the reviewed changes to still-matching authored revisions; it is a separate action from Stop.

Secondary: Runtime object menus expose inspect state and request an apply-back candidate where the engine declares a safe mapping. They cannot directly mutate the authored scene. Log menus copy safe text or open source references, not rerun captured network effects.

Drag: Dragging inside captured gameplay sends game input; authoring manipulators are unavailable or clearly separated. Pointer release is always accessible. Dragging a runtime object into Assets stages an explicit supported extraction candidate; unsupported extraction is rejected.

Keyboard and touch: Keyboard/touch controls identify whether input targets the game or authoring UI. A documented release chord and visible Release input button return control. Escape cannot be trapped by the game so the user cannot reach Stop.

State: Start failures retain authoring data; actual runtime/network/renderer support is shown. Multiplayer/shared state and per-player input are distinct. Reconnecting a test reattaches its identity rather than duplicating a server or inventing continuity.

Integration and persistence: Authoring source, saved project and disposable runtime state have separate revisions. Stop discards transient changes unless explicitly applied. Tests use declared permissions and resource admission, so a game preview cannot claim broader file/device authority than its host profile.

Acceptance: Move an object only at runtime, stop and reopen the scene: the authored transform stays unchanged. Apply a reviewed runtime transform after a concurrent authoring edit and require conflict resolution instead of overwriting it.

Basis: Final mounted Atlas interaction_screens/games.html and corresponding overlay; expanded_apps.md GAMES contracts. Shown labels are design evidence; detailed behaviours below are target requirements, not a verified native ABI.

<a id="h.g4itooa00nl"></a>

### IX-TERMINAL-001 — Session/profile selection and split input targeting

Controls: New session / Create a session from a shell or provider profile; Sessions; Profiles; session rows; Split pane / Split the current terminal layout; pane header; close/detach session

Atlas surfaces: terminal, terminal-command-review

Primary: New session opens the shell/provider profile chooser and establishes a new SessionID only after its actual target, shell, working directory and privilege are acknowledged. Selecting an existing session focuses or reattaches that same session. Split pane creates a second view or a separately requested session and labels which one; it does not duplicate the running process implicitly. Clicking a pane sets the input target and visibly names its runtime/device. Profile edits affect future sessions unless a specific migration is offered. Closing a view detaches it; ending a process/session is a separate named command.

Secondary: Session menus expose focus, rename local label, split view, inspect target, detach and terminate owned session. Profile menus expose edit/duplicate configuration without copying secrets. A disconnected session cannot be relabelled Running through a menu.

Drag: Drag pane dividers to resize and tabs to rearrange layout; command input and pending reviews retain their captured SessionID. Dropping a profile into a split stages New session, with target visible before connect; it never rehomes an existing process.

Keyboard and touch: Keyboard pane navigation updates the visible input target. Touch taps a pane header before input; drag selection remains local to that pane. Escape closes the profile chooser without changing a running shell.

State: Connecting, ready, busy, detached, disconnected and exited reflect actual process/provider state. Reattach verifies session identity and capabilities. An unavailable SSH/WSL/local profile shows the missing prerequisite and keeps existing sessions intact.

Integration and persistence: Terminal transcripts and processes belong to SessionID/EnvironmentID, not pane positions. Shared collaborator access is separately authorised; invitation to a document does not grant shell input. Session detachment preserves status and logs within retention policy.

Acceptance: Open a command review for pane A, rearrange panes and focus B: the review must still name and target A; detaching B cannot kill A's process or replace its transcript with a new shell.

Basis: Final mounted Atlas interaction_screens/terminal.html and corresponding overlay; expanded_apps.md TERMINAL contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.okzqipu3jtd0"></a>

### IX-TERMINAL-002 — Command input, faithful PTY text and process signals

Controls: Command; AI; command input; Execute command; terminal output selection; command history; interrupt; copy/paste; dropped paths

Atlas surfaces: terminal, terminal-command-review

Primary: Command mode sends submitted literal input to the selected shell/PTY; it does not route ordinary commands to a model. AI mode treats the composer as a request for a proposal under the AI review workflow. Execute command captures the visible command, session, shell, cwd and privilege; Enter in a focused interactive PTY follows the shell's actual input semantics. Selecting output copies/selects text without execution. History selection inserts a prior command for review; executing it remains explicit. Interrupt sends the shell/process signal to the focused owned session, not all processes with the same executable name.

Secondary: Secondary click opens a terminal menu with Copy, Paste, Select all, Clear view and Inspect session. Paste is explicit; the right-click itself never executes clipboard text. Clear view clears presentation, not process output history or command receipts.

Drag: Path drops insert the correct literal/quoted representation for the current shell and visible input mode. A multiline paste/drop follows the profile's preview rule before transmission; it cannot be silently split into execute-on-newline commands. Dragging selected output exports text, not an executable action.

Keyboard and touch: Copy uses the displayed terminal copy shortcut (for example Ctrl+Shift+C on its declared platform); Ctrl+C retains PTY interrupt semantics when no copy operation owns it. Keyboard/touch switching Command/AI preserves each draft and announces the mode. Escape cancels only transient completion/history.

State: Disconnected input is not silently queued for a future arbitrary session. A shell waiting for an interactive response is shown as such; command completion requires process/output evidence. Terminal text, control codes and exit status retain their real meaning rather than model-rewritten output.

Integration and persistence: Sensitive environment values are not exposed merely to explain a command. The input target is checked at dispatch. Closing the command composer does not stop a server already running; Clear view cannot erase accountable command receipts.

Acceptance: Drop a path containing spaces and shell metacharacters into Bash, then switch to PowerShell: re-render/revalidate quoting for the new explicit target; never execute the unreviewed path or send it to AI mode as if it were a command.

Basis: Final mounted Atlas interaction_screens/terminal.html and corresponding overlay; expanded_apps.md TERMINAL contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.2nx4nzhlxmr3"></a>

### IX-TERMINAL-003 — AI command proposal review and one-time execution

Controls: AI-proposed command; Computer; Shell; Working directory; Privileges; What will happen; Review AI commands before execution; Copy to Command; Run once; Close panel

Atlas surfaces: terminal-command-review

Primary: The review displays the exact proposed command bytes or argument structure, target SessionID/device, shell, working directory, privilege and known effects. Copy to Command inserts that proposal into the Command draft and focuses it without executing. Run once submits the captured reviewed command under the applicable action policy and creates one CommandOperation; repeated activation reuses it. Close cancels the unsubmitted proposal only. Review AI commands before execution edits the optional profile preference; mandatory organisation/runtime rules remain effective and are explained.

Secondary: Command text can be copied and target details inspected. No secondary action runs a truncated/display-only command or bypasses review. An effect explanation is descriptive evidence, not a separate executable payload.

Drag: Review fields are not execution drop targets. A path/text drop into an editable proposal creates a new proposal revision and invalidates previous validation; it cannot alter a command already submitted.

Keyboard and touch: Keyboard/touch Copy and Run once remain distinct labelled actions. Enter activates only the focused action; pressing Enter while selecting command text does not execute. Escape closes the unsubmitted review and restores the AI composer.

State: A target/cwd/privilege or command change invalidates its reviewed snapshot. Unavailable sessions disable Run once with a reconnect/reselect path. An uncertain dispatch reconciles the existing operation before retry, and the panel remains explicit about not executed/running/completed.

Integration and persistence: Approval is bound to the exact operation and scope, not the natural-language request in general. A user's existing authorisation is respected and is not requested again merely due to this specification. Tests retain their actual runtime permissions; '--no-build' is not presented as a sandbox.

Acceptance: Review a proposed test command for the local session, switch to an SSH pane and press Run once: execute only the reviewed local target or block if it changed; never silently send it to SSH, and Copy to Command must not execute.

Basis: Final mounted Atlas interaction_screens/terminal-command-review.html and corresponding overlay; expanded_apps.md TERMINAL contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.6hjs8i95qr17"></a>

### IX-TERMINAL-004 — Process completion, reconnect and termination recovery

Controls: process status; exit code; reconnect/reattach; Stop owned process; terminate session; transcript export; safe output disclosure

Atlas surfaces: terminal

Primary: Process status links to the actual CommandOperation and owner tree. Stop requests cancellation of that tree and reports graceful stopping before a declared force-stop option; terminate session separately closes the shell and its explicitly listed owned processes. Reconnect reattaches a verifiable remote/local session or reports that it is gone. Transcript export selects the captured range and redaction profile and writes a derived artifact. Opening an old transcript never runs its commands.

Secondary: Process menus expose inspect owner/children, copy safe receipt, stop and view logs. A process name match is insufficient authority to terminate unrelated system/model processes. Export menus show the source session and covered time range.

Drag: Output selection/scroll and split resizing are available while commands run. Dragging a PID or log row cannot kill a process. File drops during a disconnected state remain a local draft rather than deferred executable input.

Keyboard and touch: Stop/force-stop have distinct accessible names and focus. Terminal interrupt chords go to the focused shell; the app's reconnect button is outside PTY capture. Touch retains a reachable release/stop toolbar.

State: Unknown process state after disconnect stays Unknown until reconciled; it is not shown as success or killed. A failed cancellation reports remaining owned processes. Export failure leaves the original transcript and a retryable job.

Integration and persistence: Control-plane safety protects the orchestrator/session directing model reloads or dual-GPU tests. A resource-reset action must be orchestrated outside the engine it may unload. Completed effects and receipts survive terminal reconnection and cumulative context compaction.

Acceptance: Disconnect during a build, reconnect after its shell exits and inspect logs: show the real exit state or Unknown, never recreate the old prompt as a still-running process; stopping it cannot terminate the AI session supervising the operation.

Basis: Final mounted Atlas interaction_screens/terminal.html and corresponding overlay; expanded_apps.md TERMINAL contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.m7wkvrpe9cxv"></a>

### IX-VM-001 — Machine selection, viewer input and release

Controls: Your machines; Viewer; Control enabled; View-only / Release input control while retaining the viewer; Full screen; Close viewer / Close the viewer and keep the machine running; input capture/release

Atlas surfaces: mini-computer

Primary: Selecting a machine opens its MachineID and actual lifecycle state. Viewer attaches a display session. Control enabled grants input only to the focused viewer under its input authority; View-only releases input while keeping the display visible. Full screen expands the viewer and retains a reachable escape/release control. Close viewer detaches the view and leaves the machine running, as stated by the Atlas. A separate machine lifecycle command is required to stop it.

Secondary: Viewer context menu offers release input, fit/actual-size display and inspect session. Guest secondary clicks are delivered to the guest only while input capture owns them; host controls remain outside that capture. Machine rows offer open, inspect configuration and lifecycle commands.

Drag: Pointer motion/drags inside captured guest content are guest input with transformed coordinates; chrome/splitter drags are host layout operations. Dragging a host file into the guest is rejected unless a distinct authorised file-transfer/shared-folder channel is enabled and shown.

Keyboard and touch: The documented release chord (Right Ctrl in the illustrated profile) and visible release button always work; a profile may display its actual equivalent. Touch gestures map to the selected pointer/touch profile and expose a host toolbar. Escape/full-screen exit cannot be trapped permanently by the guest.

State: Connecting, streaming, view-only, control-owned, disconnected and permission-denied are explicit. Remote control contention identifies the current owner; switching to another machine never retargets buffered input. Lost connection clears queued guest input unless a proven session-safe transport applies.

Integration and persistence: Viewer access, keyboard/pointer control, clipboard, mic/audio, devices and shared folders are separate grants. Closing the viewer does not claim shutdown, and captured guest screenshots remain subject to the same disclosure scope.

Acceptance: Focus machine A, queue pointer input, switch to B and lose the connection: no A input reaches B; View-only must stop input immediately while the display remains attached, and Close viewer must leave A's actual machine state unchanged.

Basis: Final mounted Atlas interaction_screens/mini-computer.html and corresponding overlay; expanded_apps.md VM contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.8sejt548fp8g"></a>

### IX-VM-002 — Machine power, pause, save state and configuration

Controls: Pause / Pause this virtual machine; Power; Start; Resume; Save state; Shutdown; Reset; Force stop; Configuration; Storage; Network; Shared folders; machine status

Atlas surfaces: mini-computer

Primary: Pause suspends the supported execution state; Resume continues it. Power opens explicit Start, graceful Shutdown, Reset, Save state and Force stop commands with availability determined by the provider and current state. Start admits declared resources and launches the selected MachineID; Save state persists the provider-supported runtime snapshot before stopping execution. Configuration edits show restart-required versus live-applicable settings. Storage/Network/Shared folders inspect and edit their own declared device resources; status shows actual allocated/used values and timestamps.

Secondary: Machine menus expose the same named lifecycle commands; Reset and Force stop show their concrete unsaved-state consequences before execution under the existing destructive-action policy. A menu cannot disguise reset as closing the viewer.

Drag: Resource fields and lifecycle controls are not draggable. Storage media may be attached through an explicit device picker/drop zone that previews read/write mode and ownership. No drop moves a disk file out from under a running machine.

Keyboard and touch: Keyboard/touch Power opens a menu with full action names and focused target identity. Escape cancels a pre-submit lifecycle choice; it cannot undo a Reset already acknowledged. Resource numeric fields validate provider limits.

State: Unsupported pause/snapshot/device operations show unavailable rather than emulate success. Requested resource allocation differs from admitted and observed usage. Busy transitions serialize incompatible commands; a timeout becomes Unknown/reconciling instead of an invented Off state.

Integration and persistence: VM lifecycle belongs to its provider. Restarting a VM cannot restart the host/runtime or clear the GPU used by the control plane without separate resource coordination. Shared-folder enablement does not grant arbitrary host filesystem access.

Acceptance: Request Save state while an unavailable provider capability is selected: leave the machine running and show unavailable. During graceful shutdown, a second Start cannot spawn another MachineID or falsely mark the original Ready.

Basis: Final mounted Atlas interaction_screens/mini-computer.html and corresponding overlay; expanded_apps.md VM contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.iqulhl65ecre"></a>

### IX-VM-003 — Snapshots, restore, clones and export

Controls: Snapshot / Create a named machine snapshot; Snapshots; Inspect snapshot history; Manage; snapshot name/reason; restore; delete snapshot; clone; export machine

Atlas surfaces: mini-computer

Primary: Snapshot opens a named snapshot form and states whether the provider captures disks, memory and configuration. Commit creates an identifiable snapshot job and marks it usable only after completion. Inspect snapshot history/Manage opens lineage. Restore previews the chosen snapshot and which current state will be replaced, then invokes the provider's exact operation. Clone creates a new MachineID with declared full or linked storage dependencies. Export produces a supported format manifest and reports excluded credentials/devices.

Secondary: Snapshot menus expose inspect contents, rename label, restore, clone and delete. Deletion checks linked clones and retention dependencies and cannot silently remove a live disk. An unavailable export/restore profile remains a named unavailable command.

Drag: History rows are not draggable to imply restore. Dragging a snapshot to an export zone stages an export job preview only. Storage file moves are handled by the storage owner, not visual rearrangement of lineage.

Keyboard and touch: Keyboard/touch selection opens snapshot details before execution. Escape cancels an unsubmitted restore; an in-progress job exposes only provider-supported cancellation and its actual consequences. Names are editable text, not shell paths.

State: Creating, complete, incomplete, failed and orphaned snapshots have distinct states. A failed snapshot does not replace the last usable one. Restore races recheck current MachineID/state and dependency revision; progress follows provider events.

Integration and persistence: Snapshot lineage and disk ownership are preserved independently of viewer tabs. Deleting a VM, deleting a snapshot and removing a library entry are different scopes. Restoration records an attributable event and does not erase prior logs.

Acceptance: Create a linked clone from a snapshot, then delete that snapshot: the dependency check must prevent breaking the clone or require a supported flatten transaction; closing Manage must not cancel a completed snapshot or shut down the machine.

Basis: Final mounted Atlas interaction_screens/mini-computer.html and corresponding overlay; expanded_apps.md VM contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.ymh6lrcyzdlm"></a>

### IX-VM-004 — Clipboard, media, shared folders and storage permissions

Controls: Clipboard sharing; audio output; microphone passthrough; shared folders; removable devices; Network mode; attached image/disk rows

Atlas surfaces: mini-computer

Primary: Each channel has an explicit direction and target: clipboard host-to-guest/guest-to-host, audio output, microphone input, named host-folder mapping or a specific removable device. Enabling a channel shows its effective scope and provider acknowledgement. Shared folders select an authorised host root and read-only/read-write mode; changing it revalidates guest access. Network mode shows the actual provider topology rather than a generic privacy badge. Attached image rows identify read-only installation media separately from writable guest disks.

Secondary: Resource menus expose inspect identity, detach and revoke channel. Removing a folder mapping does not delete the host folder. Ejecting installation media does not delete the shared ISO. Disk-delete operations live under their distinct storage scope.

Drag: Drop to a shared-folder picker stages the selected root; reject a path outside the user's grant. A file dragged into an ordinary viewer with transfer off is rejected with an enable-channel explanation, never copied silently.

Keyboard and touch: Directional toggles and read-only flags are keyboard/touch operable and state announced. Escape cancels a folder/device picker. Host clipboard paste into captured guest input requires the declared clipboard channel, even if keyboard control is enabled.

State: Unavailable device passthrough or revoked host access disables the affected channel without fabricating a successful attach. Pending detach/clipboard revocation rejects later data for the old channel generation. Other viewer channels remain usable.

Integration and persistence: Machine control is not a blanket host-data grant. Credentials and clipboard contents are not copied into exports, snapshots metadata or AI context unless explicitly included by their own policy. Logs retain safe channel receipts rather than private clipboard payload.

Acceptance: Enable view control with clipboard and shared folders off, then paste or drop a host file: neither content crosses the boundary; revoking one named folder leaves other authorised mappings intact.

Basis: Final mounted Atlas interaction_screens/mini-computer.html and corresponding overlay; expanded_apps.md VM contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.f7km4izhdrf6"></a>

### IX-PROTO-001 — Design/Logic selection, typed objects and dependency graph

Controls: Design; Logic; Run; Objects; Inputs; output rows; Library Text/value, Slider, Switch, Chart, Button; Diagnostics; property/formula inspector

Atlas surfaces: prototype, prototype-parameters

Primary: Design edits the authored layout and typed controls; Logic edits bindings/formulas; Run opens a disposable reader session. All three resolve the same ModelID and stable object/property identities. Selecting an object or output focuses its inspector and dependent graph; Library insertion creates a supported typed node with a unique ID. Formula edits validate types, units and declared dependencies before committing. Diagnostics selects the source property/edge responsible for a validation failure.

Secondary: Object menus expose rename label, duplicate, inspect dependents and delete with affected bindings shown. Renaming a label preserves stable references; deleting a used property cannot silently retarget formulas by name. Formula text supports ordinary copy/edit.

Drag: Drag design objects to allowed layout positions with snap/constraint previews; graph nodes move their visual position separately from logic. Connection drags accept only compatible typed ports and reject unbounded cycles unless an explicit stored-step/state operator defines their semantics. Escape cancels the edge or layout change.

Keyboard and touch: Keyboard provides insert/select/move and connection-dialog equivalents. Touch distinguishes scroll, move handle and editable control; a Run slider gesture cannot move the authored widget. Escape exits a draft inspector before leaving the model.

State: Validation distinguishes authored mathematical results from measurements. Missing input, invalid unit, division by zero or graph cycle reports the affected dependency and a recoverable error; it never draws a plausible fabricated result.

Integration and persistence: Source model, design layout and reader-session state retain separate revision fields. Formula evaluation is deterministic for its declared inputs, seed and timestep policy. No model control becomes an external action without a registered typed capability and existing grant.

Acceptance: Rename Mass to Load, reorder its widget and inspect the period formula: stable bindings remain correct. Delete the property and ensure dependent outputs show a validation issue rather than silently adopting another number field.

Basis: Final mounted Atlas interaction_screens/prototype.html and corresponding overlay; expanded_apps.md PROTO contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.lbvop591lbb4"></a>

### IX-PROTO-002 — Typed input defaults, bounds, units and apply

Controls: Accessible label; Property type; Default value; Units; Minimum; Maximum; Step; Discard; Apply default; Close panel; dependent-output list

Atlas surfaces: prototype, prototype-parameters

Primary: Selecting a typed input opens its pending property draft. Accessible label edits its user-facing name; Property type chooses Number/Boolean/Text and previews incompatible values/bindings. Default value edits the authored initial value. Units, Minimum, Maximum and Step define the number's semantics and valid control range. Apply default validates the complete property and updates the saved model revision as one transaction; Discard restores the saved property. Close resolves the pending draft explicitly and never treats an invalid text field as an applied default.

Secondary: Field secondary menus retain copy/paste and, for authored properties, inspect dependency/reset-to-saved actions. No right-click changes a live reader's value or converts units implicitly.

Drag: Number-field text selection and slider preview are separate. A default-value scrub gesture previews only the pending default; release does not bypass Apply default. Property rows cannot be dragged over another input to reuse its binding identity.

Keyboard and touch: [IX-BASE-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xj3xapubjjfz) applies to numeric fields, including intermediate invalid input, declared precision and unit display. Tab exposes all bounds before Apply. Touch has numeric/text entry, not only a fine drag gesture. Escape restores the pending field or closes the topmost picker first.

State: Minimum greater than maximum, nonpositive step, incompatible units/type or a default outside declared bounds blocks Apply with a field-specific reason. Type conversion must be explicit and loss-aware. Concurrent model changes revalidate dependencies before commit.

Integration and persistence: Applying a default changes future/newly reset Run sessions; it does not overwrite other readers' current inputs. A local preview changes to the new default only according to the explicit apply/reset action. Undo restores the prior authored property revision.

Acceptance: Edit the default from 2 to 2.5 kg but do not Apply: the running preview remains at its existing value. Apply with Maximum below the new default must fail without changing either the saved property or another reader's session.

Basis: Final mounted Atlas interaction_screens/prototype.html and corresponding overlay; expanded_apps.md PROTO contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.viyqipuhd8ns"></a>

### IX-PROTO-003 — Reader simulation, sliders and reset boundaries

Controls: Run / Run the current model in a separate preview session; Reset / Restore the declared simulation inputs; run sliders; switches; simulation buttons; chart cursor; pause/resume

Atlas surfaces: prototype, prototype-parameters

Primary: Run snapshots the valid model revision and creates a SimulationSession with declared initial inputs/seed. Sliders/switches edit only that session's inputs and recompute dependent outputs. Buttons execute their registered model transition; they do not make arbitrary host calls. Reset restores this session's declared initial inputs, simulation clock and seed policy, and records a reset event. Pause/resume controls simulation time; a chart cursor inspects a computed sample without altering the model.

Secondary: Run controls expose inspect current value, copy value and reset this input when defined. Chart menus expose selected data/sample provenance and permitted export. Neither menu saves current values as defaults; that is a separate authored edit.

Drag: Dragging a slider previews bounded input changes with monotonic session-event ordering; release commits the final input event, while Escape restores the gesture's start value. Charts pan/zoom separately from model inputs; no drag changes units or bindings.

Keyboard and touch: Arrow/Page/Home/End adjustments use the control's declared range and step. A numeric alternative lets users enter precise values. Touch slider and chart gestures have distinct hit targets. Reset remains a separate labelled command.

State: Invalid model or missing dependency blocks new runs. Runtime evaluation errors retain last valid output with explicit stale/error status rather than displaying it as current. Fast input updates may coalesce computation but must preserve the final committed value and event lineage.

Integration and persistence: A reader's exploration is independent of authoring defaults and other sessions. Replay reconstructs declared model events, not external effects. Saved results identify the model revision, inputs, seed and calculation state needed for interpretation.

Acceptance: Run two sessions, change one slider and press Reset there: the other session and saved default stay unchanged; if evaluation fails mid-drag, the output must be labelled stale until a valid recomputation completes.

Basis: Final mounted Atlas interaction_screens/prototype.html and corresponding overlay; expanded_apps.md PROTO contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.bveq1cjx8odi"></a>

### IX-PATHS-001 — Step authoring, graph layout and choice insertion

Controls: Steps outline; Add a step; Information; Choice; Activity; Checkpoint; Outcome; Add step / Insert a typed step; Connect selected steps; Fit route; Step title; Add choice / Add a labelled choice with an explicit target; graph nodes

Atlas surfaces: paths, paths-branch-rule

Primary: Outline and graph selection focus the same StepID. Add step opens the typed step palette; choosing Information, Choice, Activity, Checkpoint or Outcome creates that type with required fields. Step title changes its label while preserving references. Add choice creates a labelled edge draft for the selected Choice step with an explicit target. Connect selected steps opens a source/target/type review and validates the edge. Fit route changes viewport to the visible graph; it does not reorder execution.

Secondary: Step menus offer duplicate, move in outline, inspect incoming/outgoing routes and delete with dependency review. Edge menus edit label/condition/target or remove the edge. Deleting an Outcome cannot silently redirect readers to another outcome.

Drag: Drag graph nodes changes layout only. Drag outline handles reorders editorial presentation, not automatically route semantics. Port-to-port drag stages a typed edge with target highlight; reject invalid targets and uncontrolled cycles. Edge autoscroll and Escape preserve the previous graph.

Keyboard and touch: Keyboard outlines expose move earlier/later, and Connect has a dialog equivalent to dragging. Touch selects before entering move/connect mode. Enter opens step editing; it does not advance a real reader attempt.

State: Incomplete steps and unreachable required outcomes are shown by validation. Concurrently deleted targets make edge drafts invalid. Layout save failures do not alter canonical route transitions, and graph fixture arrows are not evidence of valid execution.

Integration and persistence: Route content, editorial order, graph coordinates and reader progress are separate data. Referenced Forms, Cards or artifacts remain owned by those apps. A duplicated Step receives a new identity and explicit remapped edges.

Acceptance: Drag a Choice node to the other side of the graph and reorder the outline: route targets remain identical. Delete a referenced target and Validate must identify the broken edge rather than route to the next visual neighbour.

Basis: Final mounted Atlas interaction_screens/paths.html and corresponding overlay; expanded_apps.md PATHS contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.gm3l95p80atn"></a>

### IX-PATHS-002 — Branch conditions, missing values and draft tests

Controls: Choice label; Target step; Required value; Comparison; Threshold; If the value is missing; Test confidence; Test route; Validate & save; Close panel

Atlas surfaces: paths, paths-branch-rule

Primary: Choice label edits the displayed option. Target step selects a stable StepID. Required value binds to an authored response/state field; Comparison and Threshold define a type-checked predicate. If the value is missing explicitly chooses the missing-data outcome, such as stay and ask or show unavailable, rather than treating missing as zero. Test confidence or other test fields supply disposable sample inputs; Test route evaluates this draft and shows each choice's availability without changing a reader. Validate & save validates the full edge and saves its revision. Close discards or preserves the explicit unsaved draft without applying it invisibly.

Secondary: Predicate fields support copy, inspect bound source and clear binding. There is no context-menu bypass to force an unavailable route. Test-result rows may explain why a condition evaluated true/false/missing.

Drag: Condition controls are not draggable into a reader route. Dragging a response reference onto Required value stages a typed binding preview; incompatible or ungranted source fields are rejected. Text remains normally selectable.

Keyboard and touch: Selectors and threshold fields use [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/004/012. Keyboard/touch Test and Save are separate. Escape closes a value picker before the editor. Test input labels announce that they are sample values.

State: An invalid comparator/type, absent target, impossible threshold or ambiguous priority prevents Save. Missing response is a first-class test case. Repeated Test operates on a pinned draft digest so late results cannot label newer rules valid.

Integration and persistence: Condition truth exposes an option; it does not automatically choose it when the route requires reader choice. Priority/fallback rules must be explicit for automatic routing, and no random choice substitutes for an undefined missing state.

Acceptance: Set confidence ≥3, test 2 and then missing: the independent route is unavailable for 2 and follows the declared missing rule for absent data; neither test changes a live attempt or saves the edge until Validate & save.

Basis: Final mounted Atlas interaction_screens/paths.html and corresponding overlay; expanded_apps.md PATHS contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.9uopw7dn2iei"></a>

### IX-PATHS-003 — Validate, disposable preview and durable reader attempts

Controls: Validate / Check routes, conditions and reachable outcomes; Preview / Test / Start disposable test progress; reader choices; Back; continue checkpoint; resume/restart attempt; publish route

Atlas surfaces: paths, paths-branch-rule

Primary: Validate checks a captured route revision for required outcomes, dependencies, condition types and bounded transitions, then links issues to steps. Preview/Test creates disposable progress on that draft; choosing an option advances only the preview. A real Start creates an AttemptID against a published route revision; Resume restores its checkpoint. Reader Back navigates the recorded route according to its policy without repeating completed effects. Restart creates an explicit new attempt or reset event, distinct from resuming. Publish creates a new available route revision without mutating existing attempts.

Secondary: Attempt menus expose inspect current route revision, checkpoint history and restart. Preview menus can reset test progress. Neither menu edits historical responses or completed external effects in place.

Drag: Reader content scrolls and permits text selection; choices are not draggable between steps. Graph manipulation remains unavailable in reader mode. Dragging a referenced artifact opens/link-copies only under its owner policy.

Keyboard and touch: Choice options are keyboard/touch controls with available/unavailable reasons. Escape exits an auxiliary sheet before leaving the attempt. Leaving/reopening restores acknowledged progress rather than submitting the current answer again.

State: Missing referenced content, expired permission or failed external step holds only the affected transition with retry/skip choices defined by the route. A saved checkpoint is acknowledged separately from a rendered next screen. Unknown side-effect outcome reconciles its original command receipt.

Integration and persistence: Attempt progress, goal/plan/checkpoint obligations, responses and action receipts remain canonical through conversation compaction. New publication cannot silently change a reader's conditions mid-attempt; migration requires a declared compatibility path.

Acceptance: Complete a checkpoint that creates one Task, navigate Back, close and Resume: retain the same TaskID/receipt and progress; previewing a newer draft must not create another Task or rewrite the existing attempt's route revision.

Basis: Final mounted Atlas interaction_screens/paths.html and corresponding overlay; expanded_apps.md PATHS contracts. Detailed command/gesture semantics are adopted target requirements; static fixtures do not prove runtime execution.

<a id="h.1k7iqxj289rk"></a>

### IX-REPLAY-001 — Historical timeline, checkpoints and source selection

Controls: Chapters; captured source rows; Previous checkpoint; Play replay; Next checkpoint; timeline/playhead; playback speed; event track; checkpoint annotation

Atlas surfaces: replay, replay-event-detail

Primary: Selecting a chapter or checkpoint seeks to its recorded event/time boundary. Previous/Next checkpoint seek the adjacent captured checkpoint, not current editor history. Play replay advances reconstruction through captured semantic/media events; pause retains the exact cursor. Clicking the timeline seeks; playback speed changes presentation time only. Captured-source selection switches the historical artifact shown at that same boundary. Selecting an event opens its detail; editing a replay annotation edits replay metadata rather than the captured source.

Secondary: Checkpoint/event menus offer inspect capture, copy source reference and add/edit permitted annotation. Historical content is read-only; its context menu cannot invoke the captured edit again. A source menu may open its current original through a separately named action.

Drag: Drag the playhead to seek with preview and final cursor commit; dragging a range selects a sharing/export interval. Range movement does not delete data. Track panning/zoom is distinct from event selection, and captured events cannot be dragged into a different historical order.

Keyboard and touch: Left/right step through declared timeline increments; checkpoint buttons have explicit names. Space controls playback only when the replay pane owns focus. Touch scrub has a sufficiently large handle; an accessible event list provides equivalent navigation.

State: Loading, playing, paused, incomplete checkpoint and unavailable source are distinct. A partial capture renders only proven state with gaps labelled. Late reconstruction results cannot replace a newer seek. An old captured revision never receives a Current badge.

Integration and persistence: Reconstruction is a read of captured immutable records, not command replay against today's artifacts. Canonical action log/plan/checkpoint identity and revision references are retained; playback does not reset a plan or repeat completed effects.

Acceptance: Seek back over a recorded email send and document replacement, play twice and switch sources: neither email nor edit executes again, and the displayed artifact revision remains the captured one.

Basis: Final mounted Atlas interaction_screens/replay.html and corresponding overlay; expanded_apps.md REPLAY contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.mu3hk12s0n5l"></a>

### IX-REPLAY-002 — Event detail, current original and independent continuation

Controls: Close panel; recorded Actor/Time/Action/Source location; Before/After; Open current file; Continue from here / Create an independent copy from the captured revision

Atlas surfaces: replay, replay-event-detail

Primary: Event detail shows its recorded actor, timestamp, semantic command, source location and before/after data with revision identities. Open current file opens the canonical current original in its owner app and clearly exits the historical context. Continue from here creates a new independent artifact from the selected reconstructable captured revision, records its lineage and opens the new ArtifactID. It does not roll back the original. Close panel returns to the same playback cursor and selected event.

Secondary: Before/After text supports copy and source inspection. Continue offers the permitted owner/destination choices before creation; it cannot select a different captured revision silently. A missing or redacted source cannot be restored by context-menu copy.

Drag: Selected text may be dragged as a copy with historical attribution. A captured artifact dragged to an authorised creation destination stages an explicit independent-copy operation; dragging to the current original is not an implicit restore/overwrite.

Keyboard and touch: Keyboard/touch distinguish Open current file from Continue from here. Escape closes detail without moving the timeline. Creation progress restores focus to the new artifact only when its owner acknowledges the result.

State: Unavailable source bytes, missing capture dependencies or incompatible schema block continuation with exact missing parts. Repeated Continue clicks reuse one creation operation until acknowledged. Current-original access can fail while historical access remains valid, and vice versa.

Integration and persistence: The new artifact has its own save/undo history and identity; its provenance points to ReplayID/checkpoint/source revision. Referenced external artifacts retain their own access and are not secretly duplicated into the copy. Opening current content cannot overwrite the playback cache.

Acceptance: Continue from captured revision 12 while the original is at 20: create a new artifact derived from 12, leave the original at 20, and report failure rather than substituting current bytes if revision 12 cannot be reconstructed.

Basis: Final mounted Atlas interaction_screens/replay.html and corresponding overlay; expanded_apps.md REPLAY contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.vd5l0jorvl35"></a>

### IX-REPLAY-003 — Source inclusion, redaction and share/export review

Controls: Share / Share the reviewed replay scope; Review included material / Inspect source, revision and media disclosure before sharing; Include this recorded event; range trim; redaction controls; audience/destination; export

Atlas surfaces: replay, replay-event-detail

Primary: Review included material lists captured sources/revisions, semantic events, annotations, raw media and derivative previews to be disclosed. Include this recorded event toggles its inclusion in the sharing draft, not the canonical capture or current source. Range trim narrows playback scope; redaction removes identified content from the actual exported/shared derivative, including thumbnails, transcripts and searchable caches. Share reviews audience/destination and the exact derived manifest, then submits the authorised share. Export writes a derived package with the same disclosure checks; saving a review draft is not publishing.

Secondary: Included-source menus inspect access/provenance and exclude a source. Redaction menus reveal the affected derivative set and undo the redaction draft before commit. There is no menu that grants source access automatically merely because the Replay itself can be shared.

Drag: Drag range handles previews selected start/end boundaries. Dragging an event outside the interval excludes it from the draft view only and must state that the original remains retained. Redaction rectangles require a declared media target/time range; Escape restores the pre-gesture mask.

Keyboard and touch: Inclusion toggles and time ranges have keyboard/numeric alternatives. Touch selects handles before trimming. Escape cancels an unsubmitted share review; after submission only a supported revoke operation can change access.

State: Unreadable sources, revoked rights or incomplete redaction invalidate the manifest before submission. Share failure retains the reviewed draft and shows actual provider state; an uncertain outcome reconciles by share operation ID.

Integration and persistence: Excluding a recorded edit does not undo that edit in its original. Trimming is not secure deletion. Permission and identity are checked on every included dependency; recipients receive only the intentionally authorised derivative/source access, never a blanket grant.

Acceptance: Exclude an event containing a private value and share: inspect the delivered package to ensure the value is absent from frames, captions, thumbnails and search index, while the source document and canonical replay history remain unchanged.

Basis: Final mounted Atlas interaction_screens/replay.html and corresponding overlay; expanded_apps.md REPLAY contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.u8zp2idr0pap"></a>

### IX-REPLAY-004 — Capture start, pause, stop and completeness

Controls: New recording; source selection; record semantic changes; microphone/camera/screen options; Start capture; Pause capture; Stop/finalise; incomplete capture recovery

Atlas surfaces: replay

Primary: New recording opens a manifest of explicitly selected canonical sources and optional media channels. Start capture records granted semantic events and selected media under one CaptureID. Pause stops new capture according to the displayed channel policy; it does not pause source applications. Stop/finalise closes admission, drains admitted events to a consistent watermark and finalises a reconstructable manifest. Recovery exposes incomplete ranges after interruption and retains the last durable checkpoint rather than inventing missing events.

Secondary: No custom menu enables capture channels. Source rows expose inspect grant, narrow section and remove source for future capture; removal preserves earlier event provenance under retention policy.

Drag: Drop a permitted artifact onto the source picker to stage a binding. Dragging a running window into a media source area requires a deliberate capture choice and grant; a document reference does not imply desktop recording. Recording controls are not draggable.

Keyboard and touch: Keyboard/touch Start, Pause and Stop have distinct labels and states. Escape closes source selection before capture starts; closing the recorder view follows the explicit background-capture policy and cannot be mistaken for Stop.

State: Requested/recording/paused/finalising/ready/incomplete/error reflect actual journalling and media state. Revocation stops future capture immediately; late events are checked against source generation and committed watermarks. Storage exhaustion reports the last durable extent.

Integration and persistence: Semantic history capture, raw media retention and AI context inclusion are independent. Capturing an app does not grant permission to execute its commands. Full plans, goals, action receipts and checkpoints remain canonical even when a generated replay omits their presentation.

Acceptance: Stop while a tool effect receipt is arriving: finalise only after its admitted event is consistently included or the gap is explicitly marked; never label an incomplete capture Ready/complete with a missing final action.

Basis: Final mounted Atlas interaction_screens/replay.html and corresponding overlay; expanded_apps.md REPLAY contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.kn5o37aw1dm5"></a>

### IX-ADMIN-001 — Organisation navigation, people selection and invitations

Controls: organisation selector; Overview; People; Roles & permissions; Policies; Models, apps & features; Plugins & settings; Usage & credits; Billing; Team Space & Drive; Connect; Devices & Mesh; Team Agents; Audit & activity; Members/Invitations/Suspended; Search people; pagination; Invite people

Atlas surfaces: admin

Primary: Organisation selection changes the visible authority scope and clears stale selection from the previous organisation. Navigation opens the named administrative registry; it does not apply a policy. People filters Members, Invitations and Suspended by canonical membership state; search/pagination retain a stable query. Row click selects a MemberID and opens detail. Invite people opens recipient identity/address resolution, role and seat-capacity review; the final Invite submits the exact invitation set and reports per-recipient outcomes.

Secondary: People-row menus expose inspect identity, role changes, resend/cancel invitation, suspend/reactivate or remove only when allowed for that member/state. A display-name match is insufficient identity for an invitation or removal. Role changes are separate from private-file sharing.

Drag: People rows are not draggable into roles to bypass the role-change review. Bulk selection uses explicit checkboxes/ranges and snapshots exact MemberIDs. Dropping an import list stages a parsed, deduplicated invitation preview with invalid rows identified.

Keyboard and touch: Keyboard table navigation preserves row identity across pagination; selection counts announce scope. Touch has explicit select mode and row actions. Escape cancels an unsubmitted invitation draft without sending anything.

State: Capacity, role authority, duplicate/pending invitation and invalid address checks are current at submit. A failed invitation is not Active membership. Partial batch success keeps receipts and retries only failed recipients. Switching organisations invalidates the previous invitation target.

Integration and persistence: Membership, role capabilities, seat entitlement and individual artifact access are separate. The Atlas's example member counts and roles are fixtures. Inviting a member does not grant every private document, conversation, source or model credential.

Acceptance: Select two members, filter the table and switch organisation before a bulk action: the review must retain or invalidate the original exact IDs, never act on the first visible rows of the new organisation.

Basis: Final mounted Atlas interaction_screens/admin.html and corresponding overlay; expanded_apps.md ADMIN contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.z1f3yalwvwkz"></a>

### IX-ADMIN-002 — Member roles, suspension and attributable bulk changes

Controls: Member actions; role selector; Suspend; Reactivate; Remove member; invitation Cancel/Resend; bulk-action review; member detail

Atlas surfaces: admin

Primary: Member actions opens commands for that exact MemberID/state. Changing a role previews effective additions/removals and protected ownership requirements. Suspend prevents declared future access while preserving the account/artifact ownership policy; Reactivate restores only grants still valid. Remove member applies the organisation membership removal and explicitly handles owned resources/transfer policy; it is not account deletion. Resend/Cancel invitation operates on the InvitationID. Bulk review lists each target, proposed change and validation result before submitting one attributable batch.

Secondary: No menu may remove the last required owner or expand the acting admin's authority. A member's private data is not shown by selecting an administrative role. Copy identity copies a safe identifier/address, not credentials.

Drag: Role fields and member actions are not draggable. Selection changes after a review opens do not alter its target set; an explicit refresh review revalidates revised selection. Table sorting cannot reorder batch meaning.

Keyboard and touch: Keyboard/touch actions use full labels and return focus to the affected row or batch report. Escape cancels pre-submit review only. A repeated activation reuses the same batch operation rather than suspending/reactivating twice.

State: Last-owner protection, stale member revision and reduced admin authority block the affected item with a clear result. Partial success is retained per target. Suspended/removed state appears only after the authoritative receipt; device/session revocation progress is shown separately.

Integration and persistence: Administrative status changes invalidate future permissions under the runtime contract; historical action attribution remains. Existing policy-driven review is honoured without inventing an extra approval for routine navigation or reversible draft edits. Undo is a new inverse administrative event, not history erasure.

Acceptance: Attempt to remove the last Owner alongside another member: block the protected Owner specifically, report the other result truthfully, and retain audit identities even when membership changes.

Basis: Final mounted Atlas interaction_screens/admin.html and corresponding overlay; expanded_apps.md ADMIN contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.rl3zxvljfqhd"></a>

### IX-ADMIN-003 — Policy draft, affected scope and device enforcement

Controls: policy preview; affected role/group; Preview policy scope before validation and publication / Review affected people; model allowlist; required plugin/version; external action policy; Validate; Publish policy; device acknowledgement rows

Atlas surfaces: admin

Primary: Policy preview opens its DraftRevision and effective target groups. Review affected people computes the exact current scope and differences from the active policy. Model/app allowlists, required plugin versions and external-action rules edit a draft, with dependency/capability validation. Validate evaluates the draft against its scope and current catalogues. Publish policy commits the validated policy revision for future admission and initiates endpoint distribution; device rows show each acknowledgement or remaining old revision.

Secondary: Policy menus expose compare active, duplicate draft, inspect inheritance and revert through a new policy revision. Device rows expose inspect pending reason, retry distribution and view last acknowledgement; they cannot be marked compliant by a cosmetic toggle.

Drag: Draft scope chips can be moved only within an explicit scope editor showing resulting inclusions/exclusions. Dragging a device onto a policy does not enrol it or grant management authority. Reordering rules must show semantic priority changes before commit.

Keyboard and touch: Keyboard/touch review exposes affected people and device states, not only a colour badge. Escape cancels unsaved scope edits. The final Publish control identifies the revision and target organisation.

State: Offline devices remain Pending and show last acknowledged policy. A stricter server/runtime rule applies at relevant future protected admission even before a device UI refresh; unsupported local enforcement is disclosed. Changed scope/catalogue invalidates validation.

Integration and persistence: Organisation policies constrain existing grants and cannot create access to private artifacts by themselves. Model choice in an allowed list is still subject to runtime capability and resource availability. Required plugin installation and policy acknowledgement are separate receipts.

Acceptance: Publish a stricter mail-send policy while two devices are offline: show both pending, retain their last revision, and prevent newly admitted server-mediated actions from bypassing the new rule; do not report all devices compliant.

Basis: Final mounted Atlas interaction_screens/admin.html and corresponding overlay; expanded_apps.md ADMIN contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.cl9tq89o4i52"></a>

### IX-ADMIN-004 — Usage, resources, billing and audit evidence

Controls: Usage & credits; Billing; resource pools; allocation controls; plan/seat review; quote details; Audit log / Inspect attributable organisation changes; audit filters; export audit

Atlas surfaces: admin

Primary: Usage opens measured consumption with time range, units and freshness; resource-pool selection distinguishes allocated entitlement, reservation and observed use. Allocation edits show affected members/devices and admission consequences before commit. Billing opens the actual provider plan, charges and invoice references; a purchase/change uses a current itemised quote, currency, recurring terms and exact final action. Audit log opens attributable administrative events; filters narrow by actor/target/time, and export writes the authorised redacted range.

Secondary: Usage rows offer inspect measurement source and copy a safe reference. Billing rows open provider invoice/details; no right-click converts example credits into purchasable money. Audit rows expose immutable event details and linked receipts, not edit/delete history.

Drag: Charts permit range selection and pan/zoom without changing quotas. Allocation handles, if presented, preview amounts with numeric alternatives and commit only on explicit Save. No chart drag purchases a plan.

Keyboard and touch: Keyboard/touch numeric allocations expose units and effective scope. Quote confirmation uses a named purchase/change action; Escape cancels the unsubmitted quote. Audit tables retain accessible sorting and pagination.

State: Expired quotes require refresh; stale metrics are labelled. A payment timeout remains pending/unknown until provider reconciliation. Allocation beyond actual pool capacity fails with the remaining capacity shown; it does not fabricate resources.

Integration and persistence: Entitlements, billing money and hardware scheduling are distinct. An allocation request is not evidence that GPU memory or a cloud route is presently available. Audit retention follows policy and retains target identities after membership removal.

Acceptance: Move a resource allocation slider without Save and filter a usage chart: no resource or billing change occurs. Submit a quote that expires during review and require a refreshed exact quote rather than charging an old amount.

Basis: Final mounted Atlas interaction_screens/admin.html and corresponding overlay; expanded_apps.md ADMIN contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.k870844virr5"></a>

### IX-OS-001 — Go surface, results and taskbar layers

Controls: Go; Go query; Search; AI; All; Apps; Files; Actions; Settings; Desktop; Everyday; Gaming; Touch; result rows; Open; close Go; taskbar app icons; layer indicator

Atlas surfaces: os-go

Primary: Go opens the OS search/command surface and focuses its query. Search/AI changes the requested interaction mode under the shared Go contract; filters scope results by typed category. Selecting a result previews its target; Open or Enter executes that selected route/action through its owner and current permission policy. Desktop/Everyday/Gaming/Touch choose the named OS presentation/workspace profile, not model grants. Taskbar icons focus an existing window or launch the typed app route. The layer indicator selects one of the configured discrete taskbar layers and preserves each layer's layout.

Secondary: Result menus expose Open, inspect target, pin/unpin and owner-specific actions. Taskbar menus distinguish focus, new window, pin and close window; closing a window is not uninstalling the app. Background menus expose OS layout settings rather than application document edits.

Drag: Drag a taskbar app within an unlocked layer to reorder; insertion preview and Escape restore the prior order. Drag onto another explicit layer target moves/adds that shortcut with stated semantics. Result drags carry typed references and do not execute action results automatically.

Keyboard and touch: Arrow keys navigate Go results, Enter opens the selected result and Escape closes Go. Touch supports search and explicit result actions. Focus returns to the previous window on closing. Layer controls have labelled keyboard alternatives to swiping.

State: Result loading and stale query results follow [IX-BASE-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.apj0wbgoclc3). Unknown/unavailable targets show repair or capability reasons. Go and Quick Settings follow their mutual-exclusion rule; opening one closes the other's transient popup without cancelling committed actions.

Integration and persistence: Taskbar layers are not virtual desktops, Desktop Pages, Desktop Space or model/context layers. Their UI arrangement never changes active conversation sources. Search labels such as 'Create a presentation' route to the actual owner rather than executing a fixture link.

Acceptance: Search presentation, switch filters, then open a result: the acknowledged target must match the selected typed result; changing taskbar layers must not move or reset an unsaved Write document.

Basis: Final mounted Atlas interaction_screens/os-go.html and corresponding overlay; expanded_apps.md OS contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.37h7nmv943b3"></a>

### IX-OS-002 — Window placement, snapping and display recovery

Controls: window titlebar; minimise; maximise/restore; close; resize edges; snap zones; display picker; taskbar window selector

Atlas surfaces: os-go

Primary: Click a window activates it without altering document selection. Minimise hides it to the taskbar; Maximise/Restore changes bounds; Close invokes the app's close/save lifecycle and does not erase unsaved data. Resize edges change view bounds within declared minimums. Selecting another display moves the window to that display's usable region. Snap previews identify exact placement and commit only on release or explicit keyboard selection.

Secondary: Titlebar menus offer move, resize, minimise, maximise/restore and close with the correct target WindowID. A taskbar group lists actual windows; selecting one focuses it. No window menu terminates arbitrary processes by executable name.

Drag: Drag titlebar after threshold to move; edges resize; snap targets preview geometry. Escape restores start bounds. Disconnecting a display cancels stale drag coordinates and returns windows to reachable bounds on an available display, preserving relative ordering where possible.

Keyboard and touch: Keyboard move/resize/snap commands provide precise equivalents; touch uses drag handles and reachable window controls. A full-screen app must expose a reliable exit path. Focus restoration respects the last active window after popup dismissal.

State: Bounds are stored with display/scale metadata, not blindly replayed pixel positions. Unavailable display IDs show a fallback placement. A hung app's force-close option names its unsaved-state risk and affected process owner distinctly from normal Close.

Integration and persistence: Window layout is OS metadata. Document state, app lifecycle, VM power and terminal processes retain their separate owner contracts. Closing a VM viewer or Projector control window follows its explicitly defined background-session policy rather than generic process termination.

Acceptance: Move an unsaved editor to an external display, unplug it mid-drag and reopen the taskbar group: the window is reachable with its buffer intact; restoring old pixel coordinates must not leave it off-screen.

Basis: Final mounted Atlas interaction_screens/os-go.html and corresponding overlay; expanded_apps.md OS contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.gq76alkh8575"></a>

### IX-OS-003 — System controls, resource actions and device identity

Controls: clock/calendar; Quick Settings; network; volume/output device; brightness; power/session commands; process/resources view; device settings

Atlas surfaces: os-go

Primary: Clock/calendar opens current system time/calendar data with its actual timezone. Quick Settings displays requested and observed system controls; changing volume, output, brightness or connectivity targets the named local device capability. Device settings opens detailed configuration. Resource/process view shows measured usage and ownership. Power/session commands clearly identify the affected device and action—lock, sign out, restart or shutdown—with the established unsaved-work and destructive-operation review.

Secondary: Device-control menus expose select device, inspect state and open settings. Process menus offer inspect and permitted stop by owned process identity; a friendly label cannot confer authority to kill a protected system/control-plane service.

Drag: Volume/brightness slider drags preview actual requested values with final state feedback; unsupported endpoints are unavailable. Panel movement changes layout only. No drag of a process row into a blank region terminates it.

Keyboard and touch: Keyboard/touch toggles and sliders report actual state; Escape dismisses Quick Settings before any pending power review. Output selection never starts microphone capture. Screen-reader status names disconnected/unknown rather than pretending Off.

State: Unavailable hardware/provider integration produces a named unavailable state. External device changes update the observed value without silently overwriting a user's pending draft. Resource usage is timestamped; unknown measurements are not zero.

Integration and persistence: System resource actions obey the separate surviving control-plane rule. GPU reset/model unload must not destroy the session orchestrating it. Taskbar visual profile changes do not alter action permissions, installed packages or hardware allocation.

Acceptance: Switch audio output while a voice session runs and deny microphone access: output routing may change, but capture remains denied; an unavailable brightness endpoint must not animate to a false successful hardware value.

Basis: Final mounted Atlas interaction_screens/os-go.html and corresponding overlay; expanded_apps.md OS contracts. These are adopted target interaction semantics, not native-runtime acceptance.

<a id="h.x485e6cbsx58"></a>

### IX-LAUNCHER-001 — Home Launcher: Mobile home, app drawer and typed launch targets

Controls: app icons Spaces/Boards/Write/Cards/Files/Maps/Connect/Shelf; dock; page indicators; app drawer; Search apps, files and actions; AI; app shortcut menu

Atlas surfaces: launcher

Primary: Tap an app icon invokes the installed package/activity or canonical route for its profile. Dock and page icons are placements of that target, not duplicate installations. Opening the drawer lists actual available packages; search filters typed apps/files/actions through shared Go, and AI enters its explicit mode. Page indicators move among Home Launcher pages without starting apps. A widget's embedded Open action targets its owner; static clock/weather text is not treated as a hidden command unless a registered action is exposed.

Secondary: Long press/secondary click on an icon opens app shortcuts, app info, edit placement label where permitted, remove from home and uninstall through the package owner. Remove from home removes that placement only. Unavailable actions show their capability reason.

Drag: An ordinary swipe scrolls pages/drawer. Icon dragging requires the explicit long-press edit threshold; it cannot begin from a normal tap and accidentally launch. Drag semantics and folder placement are defined in [IX-LAUNCHER-002](https://docs.google.com/document/d/1tBC60DTC70QAZha8o7wn1SvgK1V3qL6vvKxDapYExl0/edit?tab=t.kh6hghkncf6x#heading=h.vp8fmplxrt5l).

Keyboard and touch: Keyboard/assistive navigation moves through page icons and dock with accessible names; Enter launches once. Touch long press opens the menu without launching. Back/Escape closes the drawer/search before leaving the launcher surface.

State: Launching, package missing, work-profile locked and activity unavailable have distinct states. Restoring a removed package can repair its placement by identity; similarly named packages are not substituted. Recent launches require actual acknowledgement.

Integration and persistence: Home Launcher is distinct from Home's app catalogue, Shelf collections and OS taskbar. Removing a Home Launcher placement does not remove a Shelf entry or document. File/action results still use their canonical owner and permission boundary.

Acceptance: Remove the Files home icon, open it from the drawer and restore its shortcut: the package remains installed, its documents are untouched, and no second app identity is created.

Basis: Final mounted Atlas interaction_screens/launcher.html and corresponding overlay; expanded_apps.md LAUNCHER contracts. These are adopted target interaction semantics, not native-runtime acceptance. The snapshot's Launcher label/route is legacy; the live app is named Home Launcher. Keep launcher and settings-app-launcher as compatibility route IDs.

<a id="h.vp8fmplxrt5l"></a>

### IX-LAUNCHER-002 — Home Launcher: Home edit mode, icon folders and placement transactions

Controls: long-press home edit mode; icon drag handles; page/dock drop slots; folder targets; folder title; remove placement; wallpaper/layout actions

Atlas surfaces: launcher

Primary: Enter home edit mode through empty-space long press or an explicit Edit layout action. Selecting an icon shows placement controls. Creating a folder combines Home Launcher placements under a new FolderID; editing the folder title changes only that local label. Removing a folder exposes whether its placements return to the page or are removed, without uninstalling targets. Wallpaper/layout actions configure the Home Launcher surface rather than authored document themes.

Secondary: Blank-space menu offers wallpaper, widgets and layout settings. Icon/folder menus offer open, rename placement/folder, move, remove placement and declared package actions. Folders cannot recursively contain themselves.

Drag: Drag an icon after edit threshold to an eligible grid/dock slot; show occupied-space reflow and the exact resulting page. Hover over an eligible icon/folder shows an explicit folder-add target. Edge dwell changes pages with a preview; release commits one placement transaction, Escape/Back cancels. Reject full locked docks or cyclic folder targets visibly.

Keyboard and touch: Move left/right/up/down/to page controls provide non-drag equivalents. Touch haptics may acknowledge entry but are not the sole feedback. Keyboard focus stays on the moved placement or its resulting folder.

State: Collision handling is deterministic and previewed; orientation/density changes must not lose placements. Locked layouts reject moves. Concurrent sync changes revalidate slot identity and offer a conflict resolution rather than deleting either placement.

Integration and persistence: Home Launcher placement/order is user/profile metadata. Dragging a document reference creates an explicit Home Launcher shortcut only after resolving its canonical target; it never moves the original file. Undo restores the prior placement structure without uninstalling or relaunching apps.

Acceptance: Drag an icon into a folder, rotate the device before release and press Back: restore the original placement once, preserve both target identities, and prevent accidental launch or duplicate folder membership.

Basis: Final mounted Atlas interaction_screens/launcher.html and corresponding overlay; expanded_apps.md LAUNCHER contracts. These are adopted target interaction semantics, not native-runtime acceptance. The snapshot's Launcher label/route is legacy; the live app is named Home Launcher. Keep launcher and settings-app-launcher as compatibility route IDs.

<a id="h.odgm6zc6ckih"></a>

### IX-LAUNCHER-003 — Home Launcher: Widgets, resizing and live-data boundaries

Controls: widget picker; Add widget; widget configuration; resize handles; widget options; Remove widget; Today/up-next card; weather/clock display

Atlas surfaces: launcher

Primary: Widget picker lists installed registered widget providers and their required data/capabilities. Add widget stages a page placement and opens required provider configuration before activation. Resizing changes the widget's declared layout within supported bounds; it does not change the underlying calendar/task data. Widget options opens provider settings or removes this instance. A Today/up-next event's explicit Open action opens the canonical EventID; static displayed values have no hidden mutation.

Secondary: Widget long press exposes resize, configure, refresh when declared and remove instance. Content-region menus remain provider-owned and cannot remove a calendar event merely because Remove widget is available.

Drag: Resize handles preview grid bounds and collision reflow, with numeric/size presets as alternatives. Dragging the widget header in edit mode moves its instance; scrolling its inner list outside edit mode scrolls content. Escape cancels the geometry transaction.

Keyboard and touch: Keyboard/touch selection distinguishes widget container focus from its internal controls. Screen readers announce stale/offline state and accessible widget actions. Long press inside an editable field remains text selection rather than always entering home edit mode.

State: Provider unavailable, revoked grant, missing configuration and stale data are distinct placeholders with retained instance identity. No fabricated weather/time/calendar result is shown as live. Loading state cannot erase the last valid data without indicating its age.

Integration and persistence: Each WidgetInstanceID holds provider configuration and placement separately. Removal revokes only that instance's subscription/grants according to provider policy and cannot delete original events/tasks. Mobile safe areas, density and keyboard insets preserve reachable controls.

Acceptance: Revoke calendar access while a Today widget is visible, then resize and remove it: show unavailable data, retain its placement until removal, and leave all original events unchanged.

Basis: Final mounted Atlas interaction_screens/launcher.html and corresponding overlay; expanded_apps.md LAUNCHER contracts. These are adopted target interaction semantics, not native-runtime acceptance. The snapshot's Launcher label/route is legacy; the live app is named Home Launcher. Keep launcher and settings-app-launcher as compatibility route IDs.

<a id="h.legnxuqv3ftt"></a>

### IX-PROJECTOR-001 — Target discovery, display identity and trust

Controls: new Projector target picker; display/device rows; Connect; target details; trust classification Public/Shared/Private/Trusted; Disconnect

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Opening Projector discovers actual registered display/runtime targets and shows their identity, connection route and executor capabilities. Selecting a target previews it; Connect establishes a SessionID only after acknowledgement. The trusted controller chooses Public/Shared/Private/Trusted disclosure policy for that target and shows its practical consequences. An unknown or newly discovered display is not assumed trusted because its name resembles a prior device. Disconnect detaches the controller/display according to the explicit session policy and reports whether the experience remains running.

Secondary: Target menus offer inspect identity/capabilities, rename local label, forget remembered pairing and disconnect. Forgetting a target is distinct from terminating a running experience. No context menu silently enables remote desktop control or shares private content.

Drag: Targets are not draggable to launch arbitrary content. Dropping an artifact onto a target stages a presentation/experience selection with target and disclosure preview; it never bypasses source access or starts screen mirroring automatically.

Keyboard and touch: Keyboard/touch selection exposes target identity and trust policy before Connect. Escape closes the picker without altering active sessions. A phone's trusted controls remain private and are not mirrored as part of the target's audience surface.

State: Discovered, connecting, connected, unavailable, unsupported and identity-changed states are distinct. Stable identity is used only when the transport proves it; reconnect cannot guess continuity from screen order. A disconnected target retains only its supported checkpoint.

Integration and persistence: Trust classification narrows disclosure and capability use; it does not create file, camera, desktop or network authority. Notifications, private chat, presenter notes and clipboard are excluded from public display unless explicitly routed under their own policy.

Acceptance: Reconnect a different display with the same friendly name: require explicit target selection and reapply safe disclosure defaults, while retaining the old session checkpoint without sending its private notes to the new screen.

Basis: New owner-requested Projector surfaces, expanded_apps.md §7.8 and sources/retained-apps/projector inspection manifest. No existing Atlas screen ID or completed executor is asserted; these controls require registration in the Atlas.

<a id="h.4s7lqvf06lo7"></a>

### IX-PROJECTOR-002 — Experience gallery, selection and launch switching

Controls: new Gallery; categories Videos/Games/Desktop/Music/TV/Haven/Presentation/Browser/Photos/Workspaces/Generated Experiences; experience card; details; Launch; current experience; Return to gallery

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Category selection filters registered ExperienceDefinitions; it does not imply every category has an installed executor or entitlement. A card selects an ExperienceID and shows required source, runtime, controls and capabilities. Launch captures target SessionID, definition/source revisions and disclosure policy, then requests the actual executor. Switching experiences prepares the replacement and preserves the prior checkpoint until the declared transition is acknowledged. Return to gallery shows the gallery while retaining or stopping the current experience according to an explicit visible choice.

Secondary: Cards offer details, pin/unpin, remove recent reference and manage definition through its owner. Removing a card does not uninstall an app or delete its canonical media. Launch has no right-click bypass to a different target.

Drag: Gallery cards may reorder pinned items only in an explicit edit mode with insertion preview and Escape rollback. Dropping media onto a compatible experience stages its source selection; incompatible file types show a rejection reason rather than inventing a generic player.

Keyboard and touch: Arrow navigation selects cards; Enter opens details or uses the explicitly focused Launch command. Touch separates selection/details from launch and provides reachable Back. The display may show a neutral loading surface while the phone remains in control.

State: Capability checking, preparing, launching, active, suspended and failed are actual states. Failed launch retains the prior experience or gallery with the failure reason; it cannot display an Active badge from category selection alone. Unsupported executors remain unavailable.

Integration and persistence: Desktop sharing, local playback, cloud streaming and browser sessions are distinct executor profiles with their own auth/capture/input rules. Current source inspection demonstrates only the implemented Desktop shared-host path; other experience controls are target requirements, not shipped claims.

Acceptance: Select Games on a target lacking the registered game executor and press Launch: show the missing capability, preserve the current presentation, and never substitute an unrelated remote desktop session.

Basis: New owner-requested Projector surfaces, expanded_apps.md §7.8 and sources/retained-apps/projector inspection manifest. No existing Atlas screen ID or completed executor is asserted; these controls require registration in the Atlas.

<a id="h.foyvhctvzxv7"></a>

### IX-PROJECTOR-003 — Source selection, audience composition and presentation

Controls: experience source picker; Open original; presentation audience/notes view; slide navigation; media selection; browser destination; workspace scene selection; audio output route

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Source selection resolves a canonical artifact/media/site/workspace identity and chosen revision/section; Open original edits in its owner app. Presentation launch binds the audience view to the target while notes and private controls remain on the trusted controller. Previous/Next slide change the active presentation session; they do not reorder slides. Media controls select the declared asset and output route. Browser destination navigates the chosen browser executor/profile; workspace selection launches its declared arrangement without assuming every source can be disclosed.

Secondary: Source menus inspect provenance/access, choose a different authorised source or reveal original. Audience-view menus expose fit/aspect/background options that affect presentation only. No menu promotes private notes into audience content implicitly.

Drag: Drag media or a document into the source picker stages a typed source choice. Dragging slide thumbnails in the controller is navigation only unless an explicit owner-editor mode is entered. Audience layout handles preview presentation geometry and do not mutate canonical content.

Keyboard and touch: Keyboard/touch slide/media commands are scoped to the selected Projector SessionID. Presenter navigation remains available when the display loses focus. Escape exits a controller detail panel before stopping the experience.

State: Missing source permission, unsupported format, stale pinned revision and unavailable audio destination show explicit blockers. Live-source refresh requires its declared revision policy; a changed artifact cannot silently replace a pinned presentation in progress.

Integration and persistence: Presentation, save/export and delivery remain distinct. Projecting a canonical artifact does not transfer ownership or automatically grant the audience access to its file. Audio routing is a separate resource; selecting a display cannot enable microphone capture.

Acceptance: Project a presentation with private notes, edit the original and change audio output: preserve the chosen revision policy, keep notes on the controller, and change only the acknowledged audio route rather than mirroring the phone screen.

Basis: New owner-requested Projector surfaces, expanded_apps.md §7.8 and sources/retained-apps/projector inspection manifest. No existing Atlas screen ID or completed executor is asserted; these controls require registration in the Atlas.

<a id="h.ofo8ko3hcp1o"></a>

### IX-PROJECTOR-004 — Phone remote input, focus and capability release

Controls: new phone remote; pointer/touchpad; keyboard; media controls; game controls; presentation controls; focus target; Release control; clipboard; audio/input settings

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: The remote presents controls declared by the active experience profile: pointer/touchpad and keyboard for a desktop/browser, media transport for media, game input for a game, slide navigation for presentation. Each input event carries selected SessionID, experience generation and target focus. Selecting another experience/controller pane does not retarget queued input. Release control stops the input grant while retaining view/status. Clipboard, microphone, audio output and other channels have separate explicit enable/direction controls.

Secondary: Remote controls have no secondary action that changes the target or grants control. A focused object may receive a secondary-click gesture only through the declared input profile; a host long press remains available for release/device options outside that captured region.

Drag: Touchpad drag sends pointer movement/drag only after the profile's gesture threshold. Scroll, pinch and drag mappings are declared per experience; no global gesture silently becomes a file transfer. Buffered gestures are cancelled on generation/target change.

Keyboard and touch: Keyboard commands name the active target and provide a reliable host release chord; phone touch always retains a reachable Release control/End surface. Accessibility input sends the same typed events rather than pixel-only shortcuts where semantic actions exist.

State: Control-owned, view-only, connecting, interrupted and unavailable states are visible. Lost connectivity drops non-replayable transient input rather than replaying old taps on reconnect. Conflicting controllers require the explicit ownership policy; they are not both shown as exclusive owners.

Integration and persistence: View permission does not imply input, clipboard or host capture authority. Remote input is routed through the experience's adapter and logged with safe metadata, not private keystroke payloads. Controller UI remains private by default.

Acceptance: Type into desktop A, switch to a presentation before the network flushes, and reconnect: old keystrokes must not reach the slide deck or another desktop; Release control stops all future admitted input for that generation.

Basis: New owner-requested Projector surfaces, expanded_apps.md §7.8 and sources/retained-apps/projector inspection manifest. No existing Atlas screen ID or completed executor is asserted; these controls require registration in the Atlas.

<a id="h.mfoukg5u063j"></a>

### IX-PROJECTOR-005 — Natural-language request, candidate matching and plan review

Controls: new bottom chat box; request text; Send request; candidate experience list; source/target clarification; proposed launch plan; Cancel request; Generate experience

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Send request captures the text, selected display/session, permitted context and request generation. The planner first matches registered experiences and available sources; a candidate list identifies exact definitions/targets when the request is ambiguous. Selecting a candidate resolves that choice without launching an unrelated profile. A proposed plan names source, executor, display, disclosure and required actions; execution follows the user's request and existing permissions without a new universal approval gate. Generate experience is an explicit path when a new validated definition is needed, not a cosmetic rename of an existing card.

Secondary: No contextual action treats untrusted displayed content or a source document as authority to launch another app. Request text supports copy/edit; plan rows offer inspect source/capability and change target before admission.

Drag: Dropping an artifact into the chat box attaches its identified reference to the request scope; it does not grant the planner all files in its parent folder. Cards dragged into the request become typed suggestions, not executable prompt text.

Keyboard and touch: Enter/Send follows the visible composer rule; Shift+Enter adds a line. Keyboard/touch candidate choices identify why they match and the target. Escape closes an inner picker; Cancel request stops further plan generation/admission for that request.

State: Planning, candidate selection, capability validation, launching and failed/unsupported are distinct. A newer request invalidates stale candidate/plan callbacks; already-completed effects retain receipts and are not concealed by cancelling the chat.

Integration and persistence: Natural language is interpreted into typed owner/runtime commands and preserves provenance. Model mode/context budget does not broaden source authority. The planner must state unsupported requirements instead of inventing a live TV/game/streaming entitlement.

Acceptance: Ask to show a named deck on display A, then immediately replace the request with photos on B: the old plan cannot launch later on either screen, and an unavailable photo source yields a concrete resolution choice rather than a fabricated gallery.

Basis: New owner-requested Projector surfaces, expanded_apps.md §7.8 and sources/retained-apps/projector inspection manifest. No existing Atlas screen ID or completed executor is asserted; these controls require registration in the Atlas.

<a id="h.9kruorts1icq"></a>

### IX-PROJECTOR-006 — Generated experience validation, save and pin

Controls: new generated-experience editor; definition preview; validation errors; Test preview; Save experience; Pin; recent experience; discard draft; export definition

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A generated experience is a typed definition with declared view structure, state, actions, assets and capabilities. Preview validates it before rendering and executes only its declared test profile. Errors point to the invalid field/node. Save experience persists the reviewed definition and lineage under a canonical ExperienceID; Pin adds a gallery reference, while recent history records actual acknowledged use. Discard draft removes only the uncommitted definition. Export writes a safe package manifest without credentials or hidden source grants.

Secondary: Definition menus expose inspect generated source, dependencies, permitted editing, duplicate and remove saved definition with reference consequences. Preview content cannot execute arbitrary scripts merely because it was generated. A card label is not an executable payload.

Drag: Editor node/layout drags follow the registered CUI/component rules and commit typed changes; asset drops stage owned or authorised references. Unsupported nodes, external URLs/actions or loops are rejected with exact capability/type reasons. Escape restores the draft gesture.

Keyboard and touch: Keyboard/touch has a validation list, source selection and named Save/Pin commands. Escape closes preview before discarding anything. Focus returns to the invalid field when validation blocks Save or launch.

State: Draft, valid, tested, saved and launched are separate. Generated content is not presumed safe or functional from successful model output. Missing adapter support blocks the affected action; unknown fields are not silently executed or discarded.

Integration and persistence: CUI owns authored 9to1 experience structure/style/state/behaviour/orchestration; infrastructure adapters expose bounded capabilities. Saving an experience is not publishing it to others. Reopening resolves its saved ID/revision and does not regenerate a different definition from the original prompt.

Acceptance: Generate a dashboard with an undeclared shell action, then Save/Launch: validation must identify and block that capability while retaining the editable draft; pinning a corrected definition must reference the same saved ExperienceID.

Basis: New owner-requested Projector surfaces, expanded_apps.md §7.8 and sources/retained-apps/projector inspection manifest. No existing Atlas screen ID or completed executor is asserted; these controls require registration in the Atlas.

<a id="h.21iva7wvixnx"></a>

### IX-PROJECTOR-007 — Session stop, target loss, reconnect and checkpoint restoration

Controls: End experience; Stop projection; Return to gallery; reconnect; resume checkpoint; target changed warning; session history; release resources

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: End experience stops that executor session and finalises its declared checkpoint. Stop projection clears the target audience surface, releases capture/input/audio/resource leases and reports acknowledgement; it is distinct from closing the phone control view. Return to gallery uses its explicit retain/stop choice. Reconnect verifies target identity and session generation before offering Resume checkpoint. A changed or unproven identity requires selecting the target again and applying disclosure rules, never automatic private restoration.

Secondary: No menu replays past commands to fake session continuity. Session history offers inspect receipts/checkpoint, reopen a saved experience or remove a recent reference. Deleting a recent row does not stop an active session.

Drag: Session rows are not draggable between displays as an implicit migration. An explicit Move to target operation previews transferable state and permissions, starts the supported destination handoff and releases the old target only after its declared transition.

Keyboard and touch: Keyboard/touch Stop remains available outside experience input capture. Escape leaves a detail sheet before invoking session lifecycle commands. A disconnected controller exposes safe reconnect and local-stop requests with their actual reachability.

State: Stopping, stopped, disconnected, checkpoint available, checkpoint incomplete and unknown remote state are distinct. Lost network cannot prove the public display was cleared; show uncertainty and any available control path. Late frames/events from ended generations are rejected.

Integration and persistence: The surviving control plane manages executor unload/cleanup and records completed effects. Resource release must not kill the session responsible for cleanup. Restore uses supported checkpoint state and cannot replay a sent message or republish a document merely to reconstruct the view.

Acceptance: Unplug a display during playback and reconnect a different one: keep the prior checkpoint, reject old queued input/frames, require the new target's disclosure policy, and never claim the old screen was cleared without acknowledgement.

Basis: New owner-requested Projector surfaces, expanded_apps.md §7.8 and sources/retained-apps/projector inspection manifest. No existing Atlas screen ID or completed executor is asserted; these controls require registration in the Atlas.

<a id="h.kldmh0qxe4r8"></a>

### IX-FILES-011 — Organisation sharing and source dependency review

Controls: Share; organisation audience; included placements/authored content; source-access review; reference/copy/export choice; Cancel

Atlas surfaces: files-organisation

Primary: Share opens the organisation's exact membership/layout/authored-content revision and a dependency manifest of referenced originals. Audience selection resolves actual identities. The review distinguishes sharing the organisation, independently granting source access where authorised, and exporting declared copies. The final Share commits only the reviewed payload and grants through their owners; Cancel creates no share. Adding someone to a collection is not inferred permission to every source.

Secondary: Dependency rows expose Open original, inspect current access and exclude appearance from the derived share. No secondary action silently packages private originals or changes source ACLs.

Drag: Audience controls are not drop-to-share targets. A placement dragged out of a sharing preview changes an explicit inclusion draft only; it does not remove the saved organisation or delete its source.

Keyboard and touch: Keyboard/touch review reveals every inaccessible or separately granted dependency and offers explicit inclusion choices. Escape cancels the unsubmitted review; an acknowledged share requires its separate revoke command.

State: Revoked source access, stale revisions, unavailable providers and ambiguous recipients invalidate the affected share scope. Partial grant/share results retain receipts and show which audiences can actually see each source.

Integration and persistence: Organisation metadata and canonical originals retain different owners. Saved authoring work remains local/synced according to its storage status; Share is a separate disclosure action. Previews and thumbnails must not leak an excluded/private source.

Acceptance: Share a Sheet containing one private Write document with a new viewer: the review must not imply access to that document, and the delivered organisation must use a permitted placeholder or an explicitly authorised source grant.

Basis: Final mounted Atlas interaction_screens/files-organisation.html; expanded_apps.md FILES contracts. These are adopted target control semantics; static fixture fields are not proof of implemented handlers.

<a id="h.d8th2kgq99ik"></a>

### IX-PICTURE-005 — Text/object property fields and non-destructive appearance

Controls: Text properties; Font; Size; Opacity; Fill; Blend; object bounds/rotation; adjustment values; effect bypass/apply

Atlas surfaces: picture, picture-mask, picture-export

Primary: Selecting an editable text/object layer exposes its actual properties. Font chooses an available font identity and shows substitution when unresolved; Size edits typed text size. Opacity affects the selected layer/object, Fill its declared colour/paint and Blend the compositor operation. Bounds/rotation fields apply transforms in document coordinates. Adjustment/effect values preview a scoped non-destructive stack entry; bypass disables that entry without deleting its parameters, while Apply commits the declared effect edit rather than flattening the whole image silently.

Secondary: Property menus expose reset-to-saved/default, copy value and inspect inherited/object state. Font menus cannot install an untrusted asset merely by selecting its name. Mixed values remain mixed until explicitly changed.

Drag: Numeric scrubs preview one property transaction and Escape restores the starting value. Colour-picker dragging previews selected fill only. Transform handles share the same document-space matrix as rendering; locked layers reject the gesture before modification.

Keyboard and touch: Typed numeric/colour entry provides a precise alternative to scrubbing. Keyboard/touch fields expose units, mixed state and validation. Escape cancels an open picker before discarding an editor draft.

State: Missing fonts, unsupported blend/effect modes and invalid numeric ranges show concrete unavailable states; a fallback preview is labelled. Reflow and text metrics update after actual font availability, not from a guessed equivalent.

Integration and persistence: Text remains editable text until an explicit rasterise/flatten operation is chosen. Property undo targets only the selected layer revision; exporting an image uses a captured revision and cannot change native editability or hidden layer visibility.

Acceptance: Change the font on one selected text layer while its font asset is missing: retain its canonical font reference and show substitution, preserve other layers, and do not rasterise the text or claim an exact final export.

Basis: Final mounted Atlas interaction_screens/picture.html; expanded_apps.md PICTURE contracts. These are adopted target control semantics; static fixture fields are not proof of implemented handlers.

<a id="h.cvi733dp56ze"></a>

### IX-MOTION-005 — Selected object inspector, titles and authored animation

Controls: Inspector; selected title text; Typeface; Position X/Y; Scale; Opacity; Animation; keyframe add/remove; interpolation; colour/audio property panels

Atlas surfaces: motion, motion-transition, motion-export

Primary: Inspector targets the selected ClipID/object and shows properties valid for its type. Title text and Typeface remain editable authored content. Position X/Y and Scale use the sequence coordinate system; Opacity affects that object. Animation selects its typed animation/keyframe property, and an explicit add/remove-key action changes the key at the current sequence time. Colour and Audio panels edit their named clip/track property stacks, not a second copy of the sequence.

Secondary: Property/keyframe menus expose reset, copy value, inspect binding and supported interpolation. Applying a value across selected clips shows mixed state and the exact scope. A transition has its own inspector and is not edited by the title opacity control.

Drag: Numeric scrub/onscreen transform handles preview the selected object's value; release commits one undo transaction. Dragging keys changes declared sequence time/value with snap feedback; Escape restores originals. Panel dragging/resizing does not move the clip.

Keyboard and touch: Keyboard fields provide units and precise values; key navigation and Add key have accessible alternatives. Touch selection occurs before handle movement. Changing workspace preserves selected object/time and pending validated edits.

State: Missing fonts/effects and out-of-range values block the affected change or render with an explicit diagnostic. A late inspector result cannot apply to a newly selected clip. Current playback frames identify the source revision being previewed.

Integration and persistence: Canonical source media remains unchanged; titles/transforms/effects are sequence-owned edits. Moving the playhead only samples values and never inserts a key unless auto-key is explicitly enabled and shown. Preview success and final render readiness remain distinct.

Acceptance: Seek through an animated title, change selection to a video clip and type an opacity value: apply only to the captured current target, and seeking alone must not create new keys or alter the title's saved text.

Basis: Final mounted Atlas interaction_screens/motion.html; expanded_apps.md MOTION contracts. These are adopted target control semantics; static fixture fields are not proof of implemented handlers.
