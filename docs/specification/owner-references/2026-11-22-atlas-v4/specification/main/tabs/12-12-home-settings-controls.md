# 12. Home & Settings controls

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.f2hdpuo82co9

<a id="h.38tcbffsilra"></a>

# 12. Home, Settings, learning and sound controls

Control records here extend the shared input patterns. Settings navigation and repeated app-setting links are bound to exact destination routes in the control register; opening a configuration page never performs its primary command. New owner-requested learning, sound and Comic surfaces require matching Atlas designs.

Authority: [Master specification](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk); [pinned Interface Atlas v3.3](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html). Owner additions and the detailed gestures specified here are target requirements; static Atlas markup is design evidence, not proof of native execution. Related application/runtime requirements in the master remain in force.

Reading a record: Primary covers left click and activation; Secondary covers right click and contextual actions; Drag covers sources, targets and cancellation; Keyboard and touch supplies alternatives; State and integration defines scope, persistence and failure. Shared IX-BASE patterns apply unless the control record explicitly refines them. Acceptance describes required verification, not tests already performed.

<a id="h.gjgt7hu8tvyb"></a>

### IX-HOME-001 — Home sidebar destinations

Controls: Apps; Explore; New tab; Models; Assistants; Specialists; Library; Mesh & devices; Settings

Atlas surfaces: home-apps, home-dashboard, home-explore, home-models, home-library, home-mesh, home-assistants, home-specialists

Primary: Click a named destination opens its canonical Home page in the current Home tab and updates the selected navigation item. Apps opens the app catalogue; Explore opens task-to-app discovery; Dashboard opens the independent activity overview; New tab opens its distinct start/search surface; Models opens discovery; Assistants/Specialists open the corresponding entity collection; Library opens the canonical artifact index; Mesh opens paired devices. A secondary chevron expands a subgroup without navigating.

Secondary: Destination menu offers Open in new tab and Copy destination link. Pin shortcut adds a reference to New Tab; it does not install that app. The sidebar order is fixed by the navigation taxonomy and is not a freeform document list.

Drag: Sidebar destinations are not reorderable. Dragging an addressable destination to a New Tab shortcut target creates a shortcut only; dragging across other sidebar rows has no effect.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Home Dashboard and host New Tab have independent LayoutIDs and separately versioned persisted arrangements. They may reuse the Haven sidebar, widget components and canonical source resources without sharing one layout identity or overwriting each other's preferences. Study, Translate and Actions remain destinations within Spaces rather than newly installed apps. Home Assistant/Specialist cards delegate their entity actions to the Spaces catalogue records.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.9mlfqqe0dbbg"></a>

### IX-HOME-002 — App catalogue search and filters

Controls: Search apps; Clear app search; Filters; Installed; All apps; Pinned; Manage apps

Atlas surfaces: home-apps

Primary: Search filters the local catalogue by app name and description. Clear removes only the query. Filters opens Installed/All apps/Pinned single-choice options, with the active choice named on its trigger. Manage apps opens installed-package management and never starts an update. A zero-result Clear search action restores the filtered catalogue without changing pin or installation state.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Text search follows [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1); menu options follow [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk). Escape dismisses the filter before clearing any text. Search remains focused when results reorder.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Filtering is a view operation; it cannot install, enable or remove a package. A pinned result can be unavailable locally and must display that state.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.z0xp8paxxsly"></a>

### IX-HOME-003 — App card launch and its menu

Controls: App tile; Pinned app tile; Open app; Pin/Unpin app

Atlas surfaces: home-apps

Primary: A labelled app tile launches on single click, using its canonical app identity and existing launch/editor routing. Launching an installed app opens or focuses its view according to the host open policy; launching a missing package opens its package details. Double-click cannot create two instances accidentally. Card metadata and badges are not separate install triggers.

Secondary: Menu: Open; Open in new tab; Open standalone when packaged; Pin/Unpin; App settings; Package details. Uninstall appears only in package management with dependencies and work state visible. A local-only app does not acquire a cloud account because its card was opened.

Drag: Pinned app cards are reorderable through [IX-HOME-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.u8n6josja9rg). Ordinary catalogue cards can be dragged only to an explicit Pin area, yielding an app reference; they are not file payloads and cannot be dropped into a document as executable content.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Pin state and launch state are independent. Reordering or opening a card never changes canonical data owned by the app.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.u8n6josja9rg"></a>

### IX-HOME-004 — Pinned app order

Controls: Pinned app drag; Move left; Move right; Pin target

Atlas surfaces: home-apps

Primary: Move left/right changes the app’s position by one in the pinned list. Pin places a new app at the end; Unpin removes only its shortcut. Clicking the tile without dragging follows the app launch record.

Secondary: The tile menu exposes Move first/last, Move left/right and Unpin. Boundary moves are unavailable with the current position announced.

Drag: Drag the pinned tile body after threshold; show an insertion marker within the pinned region. Drop reorders by AppID. Filtering never reorders hidden items by accident: move the visible item relative to its full-list neighbours. Drop outside a declared target cancels. A completed drag consumes release so the app does not also launch.

Keyboard and touch: Alt+Left/Right matches the Atlas pinned-app ordering; explicit Move controls support touch and keyboard. Long press opens a menu; a visible drag handle in mobile edit mode starts reordering.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Commit one preference revision for order. Catalogue category order and installed-package records stay unchanged.

Acceptance: Drag a pinned app two places and release: it moves once and does not open.

<a id="h.r06smngi67ud"></a>

### IX-HOME-005 — Package management and update review

Controls: Details; Manage; Install; Download; Update; Repair; Uninstall; Review updates

Atlas surfaces: home-apps, settings-apps

Primary: Details/Manage opens the named package and its installed/available versions. Install stages and validates a selected package then activates when allowed. Download only obtains files. Update reviews version/dependencies and schedules activation without destroying open work. Repair verifies/restores package components. Uninstall removes the selected package after showing affected dependencies and keeps user artifacts unless a separate explicit data operation is chosen.

Secondary: Package row menu mirrors Open details, Open app, Review update and Repair, enabling only valid actions. It cannot bypass the same package checks.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Available, downloading, staged, validating, awaiting safe activation, installed, rollback available and failed are separate. Source/signature/compatibility failures retain the working version. Cancel download and Cancel staged update do not undo already completed activation.

Integration and persistence: Package activation waits for safe app/service state; it cannot kill the controlling agent to free its own GPU. Rollback targets a validated package and reports data-schema compatibility separately.

Acceptance: Stage an update with a dirty document open: activation waits while editing remains possible.

<a id="h.tgu5gbescslm"></a>

### IX-HOME-006 — New Tab search and explicit AI entry

Controls: Search apps or enter an address; Go; Ask AI; All apps

Atlas surfaces: home-dashboard, host-newtab, host-newtab-customise, host-newtab-widgets, host-notifications

Primary: Go submits the typed query to the selected host destination resolver. A registered app/route suggestion opens that destination; an ordinary web query uses the configured browser search route. Ask AI opens Spaces with the query as an unsent draft, retaining any existing draft through a new conversation/draft choice. All apps opens the catalogue. Empty Go is unavailable.

Secondary: The field retains native text editing. Ask AI and Go have no hidden context switch on right-click. Search results use destination-link menus.

Drag: Text/URI drop edits the query without submission. Files dropped outside a dedicated attachment target are rejected with an Open in Files suggestion; New Tab is not an implicit upload surface.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Typing and local suggestion filtering do not invoke a model. External search explicitly follows the chosen web route and current network policy. A model-generated recommendation is a separate declared operation.

Acceptance: Paste a prompt then click Ask AI: it becomes a visible draft, with no automatic model call.

<a id="h.m2m667tk1va2"></a>

### IX-HOME-007 — New Tab layout choice and customisation

Controls: Customise; Focus; Balanced; More widgets; Done; Reset layout

Atlas surfaces: home-dashboard, host-newtab, host-newtab-customise, host-newtab-widgets, settings-haven-newtab

Primary: Customise opens the shared layout editor. Focus, Balanced and More widgets select their density/layout preset while keeping individually configured content identities. Done exits customization; changes already committed remain. Reset layout shows the precise reset scope—layout, visibility, ordering and sizes—and applies the shipped default after confirmation; it does not erase Boards notes or Planner tasks.

Secondary: Preset buttons have no custom context menu. The customization background may expose Reset layout and Add widget as named equivalents, not hidden actions.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: The active preset is selected separately from an unsaved custom change. Preference save failure retains the edited layout with Retry and Revert.

Integration and persistence: New Tab views use their shared New Tab preference revision, while responsive placements may differ by device class. Home Dashboard uses an independent LayoutID and preference revision. Changing either surface's preset or arrangement does not alter the other surface or delete hidden widget configuration.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.dbgts97qvvuw"></a>

### IX-HOME-008 — New Tab background and upload

Controls: Neutral; Horizon; Paper; Upload; Remove custom background; Accent

Atlas surfaces: host-newtab-customise, host-newtab-widgets, settings-haven-newtab

Primary: Background presets immediately preview and commit the selected background preference. Upload opens a local image picker; selection validates and previews the asset before Use background commits it. Cancel preserves the prior background. Remove custom background returns to the last selected built-in preset. Accent opens the shared palette editor, not a separate New Tab color store.

Secondary: A custom image menu offers Replace, Fit/Fill and Remove background. It cannot delete the original local file. Preset menu has no additional action.

Drag: Drop an image only onto the labelled background upload target. Decode/type/size checks run before adoption; nonimage payloads are rejected. Dragging the background itself does not reorder widgets or pan the page.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Keep the previous background visible if decode or persistence fails. A referenced unavailable custom asset uses the selected fallback with an explanatory status.

Integration and persistence: Store a managed background asset reference and crop/fit metadata. Theme and accent changes do not overwrite the chosen image; contrast overlays may protect chrome text.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.6tngmwwryk1s"></a>

### IX-HOME-009 — New Tab shortcuts

Controls: Shortcut tile; Add shortcut; Remove shortcut; Shortcut drag handle; Move up; Move down

Atlas surfaces: home-dashboard, host-newtab, host-newtab-customise, host-newtab-widgets, settings-haven-pinned, settings-haven-shortcut-order, settings-haven-bookmarks

Primary: Click opens the tile’s canonical destination. Add shortcut opens an app/page/artifact picker, then saves the selected reference and label. Remove deletes only that shortcut. Move up/down changes one position in the persisted shortcut order; the first/last boundary control is disabled. Separate remove and move controls never activate the tile.

Secondary: Tile menu: Open, Open in new tab, Rename shortcut, Change icon, Move, Remove shortcut. Rename changes the shortcut label, not the underlying document title.

Drag: Only edit-mode handle is the reorder source; insert among shortcuts, not among widgets. URI/artifact drops into Add shortcut create a reviewed reference with resolved owner identity. Escape/invalid target cancels. Reorder preserves the same reference rather than copying an artifact.

Keyboard and touch: Alt+arrow and visible Move controls mirror drag. Touch long press offers the menu; edit mode exposes handles and Remove separately.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: The same order appears in Haven shortcut-order settings. A removed or revoked target remains a labelled unavailable shortcut with Replace/Remove choices.

Acceptance: Remove Study shortcut: Study remains a Space and its conversations remain.

<a id="h.pbw28rspf33w"></a>

### IX-HOME-010 — Widget picker and visibility

Controls: Add widget; Find a widget; Add Focus timer; Add Continue working; Add My tasks; Add Study progress; Show on Home; Close widgets

Atlas surfaces: host-newtab-widgets, host-newtab-customise, settings-haven-newtab

Primary: Add widget opens the searchable catalogue. Each Add action adds that widget identity once and returns focus to its added card or keeps the picker open with Added state. Already shown entries are disabled with that status. Show on Home expands visibility controls for Clock/date, Shortcuts, Up next, Pinned note, Focus timer, Continue working, My tasks and Study progress. Clearing visibility hides the instance while retaining configuration; Close returns to customization.

Secondary: Picker entries offer Details/source information only. A visibility checkbox has no delete-data context action.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Widgets with missing sources can be added as setup-needed cards. Their source picker must complete before showing live data. Search includes names/source apps and clears without affecting visibility.

Integration and persistence: My tasks refers to its selected Planner list; Study progress links to the Study Space; hiding either does not change tasks or learning evidence. The catalogue does not launch an app merely to add its widget.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.hittfg8nfqak"></a>

### IX-HOME-011 — Widget position, size, pin and lock

Controls: Widget drag handle; Move up/down; Make wide/Make compact; Pin; Lock layout; Remove widget; Options

Atlas surfaces: host-newtab-customise, host-newtab-widgets, home-dashboard

Primary: Options opens the selected widget’s layout/source controls. Make wide/compact selects its supported column span; Move changes grid order. Pin keeps an instance in the chosen designated region. Lock prevents positional/size edits while leaving its real content controls usable. Remove removes the Home instance, preserving the source artifact. These actions have separate visible affordances.

Secondary: Widget menu: Open source, Configure source, Resize to supported spans, Move before/after, Pin/Unpin, Lock/Unlock, Remove widget. Unsupported sizes are absent or disabled with a minimum-size reason.

Drag: Handle drag reorders into grid insertion slots; resize handle changes span within discrete valid sizes and shows the resulting footprint. Locked widgets reject both. Hovering content such as an event link cannot become a reorder source. Drop avoids overlap and commits one layout transaction; cancel restores original order/size.

Keyboard and touch: Move and size menu actions fully replace dragging. Touch edit mode exposes handles outside the content hit areas. Escape exits gesture before exiting customization.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Changing grid size never crops controls into unreachable regions. Live source updates cannot reorder cards during a gesture. Per-device layout retains the same WidgetID and source.

Acceptance: Lock a Planner widget then click its event: the event opens, but dragging the widget does nothing.

<a id="h.k4gbagucxdgn"></a>

### IX-HOME-012 — Widget content and timer controls

Controls: Open calendar; Open note; Continue working item; Task checkbox; Study progress item; Start/Pause/Reset timer

Atlas surfaces: home-dashboard, host-newtab, host-newtab-widgets

Primary: Open calendar opens the exact calendar/event; Open note opens its Boards-owned page/block. Continue working opens the canonical artifact. Task checkbox updates the referenced Planner task through its real owner. Study progress opens the relevant Study subject/session. Timer Start begins a local timer, Pause records elapsed/remaining time, Resume continues it, and Reset returns to its configured duration after confirming a running timer reset.

Secondary: Source rows offer Open source and Copy link, with task actions supplied by Planner. Timer menu offers Edit duration and sound settings; it has no generated study-progress action.

Drag: Content links are reference payloads only for valid app link targets. Text/checkbox/timer controls never reorder their widget. Use the widget’s dedicated handle.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Stale source data is marked with freshness; an error exposes Retry for that widget. A demo fixture in the pinned Atlas is not evidence of a working timer or current calendar.

Integration and persistence: Completion sound uses one real timer/event receipt. Data mutations appear in the owner’s undo/history and cannot be undone by hiding the widget.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.l82uq4szqvfi"></a>

### IX-HOME-013 — Explore prompt and suggested creation paths

Controls: Make a game; Write a document; Make a presentation; Draw an idea; Describe what you want to make; Find an app

Atlas surfaces: home-explore

Primary: A suggestion populates the prompt with that intent and displays matching app recommendations; it does not create an artifact or run a model. Find an app matches the submitted intent to registered capabilities, showing why each app fits and its availability. Clicking a recommendation opens that app’s start view; an explicit AI-assisted discovery route is separately labelled and governed.

Secondary: Suggested intent buttons have no custom menu; recommendation cards offer app details and Open in new tab.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: The user can edit suggested text before acting. The choice of an app does not silently enable unavailable capabilities or promise a finished generated game/document.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.1ion84wj6300"></a>

### IX-HOME-014 — Model catalogue navigation and filters

Controls: Discover; My models; Providers & routing; Model picker; Search model catalogue; Location; Capability filters; Reset filters

Atlas surfaces: home-models, home-models-installed

Primary: Discover opens the catalogue; My models opens saved/installed records; Providers & routing opens configuration; Model picker opens the current conversation’s selection dialog. Search and capability filters narrow metadata. All/This device/Cloud filters location; Chat/Code/Vision/Speech/Image/Video filter declared capability. Reset clears these filters and query without changing the selected model.

Secondary: Filter buttons have no menu beyond their declared selection popup. Model result cards use [IX-HOME-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.bhuhi24khcvb).

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Metadata freshness, unavailable catalogues and local capability checks are separate from residency. Fixture names such as Qwen Local are examples, not fixed recommendations or evidence of installed weights.

Integration and persistence: Discovery never downloads, loads, selects or contacts a model for inference. Provider catalogue refresh is an explicit metadata operation under connection policy.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.bhuhi24khcvb"></a>

### IX-HOME-015 — Model detail, saved model and use action

Controls: Details for model; Save model; Unsave; Use model; Example prompt; Manage local model

Atlas surfaces: home-model-details, home-models, home-models-installed

Primary: Details opens the exact provider/model/version record and requirements. Save model stores a bookmark to that identity; it does not download weights. Use model opens the picker with that candidate and intended role for explicit validated selection. Selection alone never loads or reallocates a model; only a separately named Load/Apply and reload command or eligible work admission can request allocation. An example prompt opens Spaces with an unsent draft. Manage local model opens that model’s files/residency view.

Secondary: Model card menu: Details, Save/Unsave, Use for Main/Voice/Vision when compatible, Local files where present. A cloud-only model cannot expose fictional Download or Train weights actions.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Compatibility, license/source, disk size, estimated fit, loaded state and actual context capacity remain distinct. Unsupported role selection is explained before resource allocation.

Integration and persistence: Selection does not automatically authorize tools, memory or remote context disclosure. Selecting a new model preserves the current chat and drafts; old runs retain their admitted identity.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.iuytv2egn1jn"></a>

### IX-HOME-016 — Local model files and residency controls

Controls: Download model; Pause/Resume download; Cancel download; Verify files; Load; Unload; Remove local files

Atlas surfaces: home-models-installed, home-model-details, settings-installed-models

Primary: Download selects a concrete variant/source and destination with disk requirements, then creates a tracked transfer. Pause/Resume acts on that transfer; Cancel removes only its incomplete staging according to the displayed retention choice. Verify checks actual files. Load requests residency/allocation; Unload requests release after active users drain or are explicitly stopped. Remove files reviews the exact local variant and leaves conversations and saved model references.

Secondary: Model row menu mirrors valid file/residency actions and exposes Open storage location. It never unloads active work merely because a menu was opened.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Downloading, verified, staged, loaded, queued for release and failed are distinct. File availability is not usable runtime availability. A rejected allocation retains the prior working model.

Integration and persistence: An independent controller owns the allocation transaction so its own session is not killed while clearing a GPU. Shared single-model multi-device inference and multiple independent workers are distinct configurations; the UI must name which is requested.

Acceptance: Unload a model used by two views: the controller identifies both users and preserves or explicitly stops them before releasing memory.

<a id="h.7u3ax6j3p68r"></a>

### IX-HOME-017 — Library filters and artifact entries

Controls: Recent; Favourites; Shared; All apps; Search Library; Artifact row

Atlas surfaces: home-library, settings-haven-recent-files

Primary: Recent/Favourites/Shared changes the indexed view. All apps selects owner-app filters; Search filters allowed metadata. A row selects the artifact; double-click or its Open action opens the owning app with exact ArtifactID/revision context. A single-click launch-style result must be visibly a link and use [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu) instead of row-selection semantics.

Secondary: Artifact menu: Open, Open in new tab, Favourite/Unfavourite, Copy link, Open containing location, Details. Rename/delete require the owning app/file service and its permissions; the Library index cannot become a second storage authority.

Drag: Dragging an artifact supplies a canonical reference plus permitted metadata. A document/link drop target inserts a reference; an explicit Copy/import target obtains bytes through the owner. A pin target creates a shortcut. Library view reordering is disabled while a sort/filter determines order.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Source missing, local saved, sync pending and synced are separate. Retry resolves availability without inventing a duplicate artifact.

Integration and persistence: Opening the same artifact from Library and Files preserves identity. Filtering cannot make a private artifact visible through a cached preview.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.4j9db4hhn8hk"></a>

### IX-HOME-018 — Device pairing and device details

Controls: Pair device; Pair a device; Device details; Connect viewer; View-only; Input control; Disconnect

Atlas surfaces: home-mesh, settings-mesh

Primary: Pair opens a challenge/code or scan flow for a concrete device identity. Confirm validates the matching identity and chosen scopes. Device details shows actual capabilities, connection and grants. Connect viewer opens an authorized display in view-only mode; Input control is a separate explicit grant/request. Disconnect closes that connection, not the remote computer.

Secondary: Device menu: Details, Connect view, Edit access, Copy device identifier, Forget pairing. Forget pairing revokes the relationship under the displayed scope and retains already-owned files.

Drag: Device cards cannot be reordered into a trust grant. Dropping a file on a device is accepted only through a labelled Send files target that shows destination/path and transfer semantics before dispatch; otherwise reject.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Offline, reachable, paired, authorized and input-enabled are different. A disconnected device retains its identity and stale timestamp. Reconnect revalidates scopes instead of assuming prior input capture.

Integration and persistence: Clipboard, file transfer, model inference and input are independently controlled. No device card drag or pairing event implicitly grants all four.

Acceptance: Close a remote viewer: the device stays running and prior input access is not broadened.

<a id="h.e7wwmk7yyrgv"></a>

### IX-HOME-019 — First-run local continuation and sign-in

Controls: Continue locally; Sign in to sync

Atlas surfaces: onboarding

Primary: Continue locally enters the functional local workspace without requiring an account. Sign in to sync opens an explicit account flow; successful authentication returns to the same onboarding state and shows the chosen sync scope. Cancel returns to local continuation with entered nonsecret preferences intact.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Offline sign-in shows an actionable connection error while Continue locally remains available. Sync readiness is distinct from authentication and storage availability.

Integration and persistence: Neither choice selects a particular inference model or uploads local content by itself. Comic mockup typography and visual hierarchy may style onboarding, but its illustrative intelligence/limit claims must be replaced with actual capability descriptions.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.goxw1usj1n7s"></a>

### IX-HOME-020 — Recovery and state-surface actions

Controls: View save recovery; New Board; Open file; Retry connection; Request access; Compare versions; Keep both; Retry save; Save a copy

Atlas surfaces: states, boards-recovery

Primary: View save recovery opens the failed artifact’s recovery panel. New Board creates a fresh board draft; Open file opens an owner-aware picker. Retry connection retries only the failed connection. Request access prepares the selected target/access request. Compare versions opens the two identified revisions. Keep both creates an explicitly named additional version/artifact with lineage. Retry save retries the retained draft against current revision; Save a copy chooses a distinct destination and identity.

Secondary: Recovery action buttons have no special context menu. Diagnostic text allows Copy details with secrets omitted.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: No recovery button appears successful until its service acknowledges. Keep both cannot overwrite the original; access denial remains until an actual grant. Save conflicts return to comparison instead of looping destructive retries.

Integration and persistence: The reference States screen illustrates these real owner actions. Its demo instances are inert samples; production surfaces require concrete target/revision bindings.

Acceptance: Retry a save after a remote revision changes: compare rather than overwrite silently.

<a id="h.yo6q9ebdaps1"></a>

### IX-SET-001 — Settings hierarchy and setting scope

Controls: Global; Local; Haven; Apps; Settings row; Settings search; Reset preference

Atlas surfaces: settings-general, settings-appearance, settings-ai, settings-security, settings-account, settings-local-general, settings-haven-general, settings-apps-shared

Primary: Click a category navigates to its canonical page; its chevron expands children independently. Global follows the account/workspace preference domain, Local affects this device, Haven governs host browsing, and Apps targets shared or the named app preferences. The heading and row expose scope. Navigation buttons use the exact route register. Reset preference restores the displayed default at that scope; it does not reset the entire product.

Secondary: A preference row menu offers Copy setting link, View effective value/scope and Reset this preference where editable. Category rows use destination-link menus; no drag reordering of the taxonomy.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Requested, effective, inherited and policy-managed values remain distinct. Managed settings expose their reason and permitted override path. A stale edit receives a compare/reload choice, not last-writer overwrite.

Integration and persistence: One-option-per-row layouts preserve accessible labels and help. Selecting an app settings page does not execute an app operation. A contextual Open button needing an active artifact asks for the relevant artifact when none is available.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.wgqshx7gz019"></a>

### IX-SET-002 — Editable account profile

Controls: Display name; Handle; Pronouns; Job title; Save profile

Atlas surfaces: account, settings-account, settings-profile

Primary: Fields edit an account-profile draft; optional pronouns/job title may be empty. Save profile validates and persists all changed fields as one revision. Handle validation reports conflicts and permitted format without changing the display name. Enter in an ordinary field does not silently save when a suggestion/IME owns the key; Save is explicit.

Secondary: Native text editing only; no field menu deletes the account.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Save is disabled when unchanged or invalid. A failed save retains every draft value. Concurrent profile updates require per-field comparison or reload.

Integration and persistence: Unsaved navigation prompts only when the draft cannot be recovered safely. Profile edits update authorized profile displays; they do not alter model memory facts or public posting settings automatically.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.r3sy1wfxdjs5"></a>

### IX-SET-003 — Profile icon upload and removal

Controls: Change icon; Crop; Use image; Remove icon

Atlas surfaces: account, settings-account, settings-profile

Primary: Change icon opens image selection and crop/preview. Crop edits a draft; Use image commits a managed profile asset. Remove returns to the deterministic initials/default avatar for this account. Cancel preserves the prior image.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: An image may be dropped into the labelled upload area. Within crop preview, drag moves the image under the crop mask; handles resize the crop while preserving required aspect ratio. Escape cancels the crop edit. Dragging the avatar itself does not move the account.

Keyboard and touch: Crop has keyboard position/zoom fields and reset, as well as touch pan/pinch within its labelled preview. Native file selection is reachable without drag.

State: Reject unsupported or unreadable images before replacing the old asset. Keep previews private until the account profile commit completes.

Integration and persistence: Removing the profile icon removes its profile reference, not the original uploaded file or other account data.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.ez7ezccja1wb"></a>

### IX-SET-004 — Active sessions and session revocation

Controls: Session row; Sign out this session; Sign out remote session

Atlas surfaces: account, settings-account, settings-security-general

Primary: A session row identifies device, last activity and current-session status. Its Sign out revokes that exact session. Signing out this session presents the local work/account consequence before ending credentials; signing out another does not close the current workspace.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Pending revocation remains pending until confirmed. Already expired sessions show resolved status. Offline revocation cannot be claimed complete; show queued/failed according to the actual service.

Integration and persistence: Revocation governs future requests and reports in-flight limitations. It is separate from device unpairing, deleting content, disabling a model provider or closing a tab.

Acceptance: Reorder the session list while revoking one row: the captured SessionID remains the target.

<a id="h.xaozm0mrtbx2"></a>

### IX-SET-005 — Account plan and resource allocation

Controls: Manage plan; Manage resources; Account storage Manage; Subscriptions

Atlas surfaces: account, settings-account, settings-subscriptions, settings-account-storage

Primary: Manage plan opens the current account’s actual plan/resource review. Sliders or allocations edit a quoted draft and display effective thresholds; Apply shows exact currency, billing period and effect before committing a paid change. Account storage Manage routes to account allocation/subscriptions, while local storage controls remain device-specific.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: The Atlas example price and capacity are fixtures, never live offers. Unavailable pricing or unknown allowance must be shown as unknown. Applying a quote revalidates its version before payment/plan change.

Integration and persistence: Navigation and slider previews do not purchase anything. Changing a plan cannot claim to update third-party model subscription quotas. Existing local artifacts remain independent of account storage allocation.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.3jeey18bng72"></a>

### IX-SET-006 — Package update preferences

Controls: Download updates automatically; Install updates; Update channel; Download on metered connections; Retain rollback packages; Choose app

Atlas surfaces: settings-apps

Primary: Automatic download enables background package retrieval under network policy; it does not permit immediate activation. Install updates chooses Ask before installing or When apps are closed. Channel chooses Stable/Preview after compatibility review. Metered allows downloads over a metered network. Retain rollback packages keeps validated previous binaries. Choose app opens the repair target picker, then the package repair review.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Switches use immediate versioned preference writes; a compatibility failure leaves the old channel selected. Metered off pauses eligible future transfer scheduling without corrupting downloaded chunks.

Integration and persistence: These controls govern package lifecycle, not artifacts or model weight training. Deleting rollback cache cannot remove the active package.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.u6y3c1aedq66"></a>

### IX-SET-007 — Processing privacy, diagnostics and data requests

Controls: Cloud processing; Product analytics; Crash report sharing; Prepare export; Review deletion

Atlas surfaces: settings-privacy, settings-security

Primary: Cloud processing selects Ask before cloud or Local only and constrains future affected requests. Analytics enables only the declared minimized measurements. Crash sharing selects Ask each time or Never send. Prepare export opens record/attachment selection and preview before creating a package. Review deletion opens exact records, replicas and retention consequences before confirmation.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Narrowing policy applies to future protected steps of ongoing work at safe boundaries; historical transmitted context is reported truthfully. Failed exports retain a resumable operation; deletion status distinguishes requested, tombstoned and physically removed where exposed.

Integration and persistence: Changing storage location is not authorization for remote processing. Export/deletion previews do not transmit or delete data. Privacy links to providers/memory/devices retain the current account/scope.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.1q3zl7o1kyex"></a>

### IX-SET-008 — Permission registry tabs and grant operations

Controls: Granted access; Blocked callers; Approval defaults; Narrow access; Revoke; Block caller; Default Read-only/Take actions (legacy settings label Write)

Atlas surfaces: settings-permissions, settings-apps-shared

Primary: Tabs filter the canonical registry by grant/caller/default view. Select a grant to inspect caller, target, operations and lifetime. Narrow access edits that grant’s allowed subset; Revoke removes it; Block caller prevents future requests from that caller identity at the shown scope. Default Read-only/Take actions (legacy settings label Write) sets requested editor mode, not an unconditional capability grant.

Secondary: Grant row menu mirrors Inspect, Narrow, Revoke and Block with the same captured identity. No bulk action applies across hidden filters without an explicit count/scope.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Review displays current version and affected active work. A resolved/revoked grant cannot be edited as though still valid. Access changes update eligible future protected steps and report already committed effects separately.

Integration and persistence: Blocking a caller differs from deleting its files or account. Approval history links open receipts; reading history never grants the reviewed request.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.m6w2tiicmt8a"></a>

### IX-SET-009 — Provider connection draft

Controls: Provider name; Provider type; Endpoint URL; Save connection; Cancel provider draft

Atlas surfaces: settings-provider, settings-providers

Primary: Name/type/endpoint edit a provider draft. Save validates the endpoint/type and stores a credential reference and explicit context policy; it does not make the provider active for inference. Cancel discards only the connection draft. A saved edit to an endpoint creates a new connection revision and requires capability revalidation before routing work to it.

Secondary: Text fields have native edit menus. Copy endpoint excludes embedded secret parameters; credentials are managed through the credential selector, not pasted into visible labels.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Invalid URLs, unreachable endpoints and unsupported adapters have distinct errors. A successful metadata connection is not evidence of sufficient quota, model availability or disclosure permission.

Integration and persistence: Existing runs retain their actual provider provenance. Replacing a provider does not silently redirect a Local only route or transplant another provider’s grants.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.h52nlw2jajy"></a>

### IX-SET-010 — Provider credential selection and connection test

Controls: Choose credential; Add credential; Test connection; Test again; View routes

Atlas surfaces: settings-provider

Primary: Choose credential opens the scoped secret-reference picker. Add credential stores a secret through the credential service and returns its opaque reference; the UI never displays the secret after entry. Test connection performs the declared bounded handshake/catalogue discovery and reports actual identities/capabilities. View routes navigates to routing configuration; it does not add the tested endpoint automatically.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Test is disabled until mandatory draft fields are valid. Each test captures draft revision; an older result cannot validate a subsequently edited URL/credential. Authentication, network, adapter and quota errors are separate.

Integration and persistence: Connection tests do not send conversation/source content. Secret rotation invalidates affected cached authentication and keeps existing receipt provenance. Cancelling a test stops its request where possible without deleting the credential.

Acceptance: Change endpoint during a slow test: old success cannot mark the new endpoint Ready.

<a id="h.vq7zxa2wfsqc"></a>

### IX-SET-011 — Provider context disclosure controls

Controls: Provider file context; Provider access to Persistent Memory; Provider access to Background Learning; Provider background requests

Atlas surfaces: settings-provider

Primary: File context chooses Ask before sharing or Do not share for explicitly selected request content. Persistent Memory controls that provider’s eligibility to receive admitted memories. Background Learning controls eligibility to receive approved learned context. Background requests makes the provider eligible for a configured background route. Each is independent and separately persisted.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: An off control narrows future affected retrieval/disclosure steps. A globally blocked cloud route remains blocked even when one provider checkbox is on. Historical disclosures remain inspectable and cannot be undone by a switch.

Integration and persistence: These controls do not turn on memory collection, Dreaming, web search or source-policy general knowledge. Source-bound Specialists still enforce their corpus and independent memory policy.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.vl5hjr3v8s2b"></a>

### IX-SET-012 — Model routing preferences

Controls: Add model; Local model route options; Cloud route options; Fallback policy; Image route; Voice route; Background route

Atlas surfaces: settings-models, settings-ai, settings-voice

Primary: Route options open the model/route editor with the exact role and policy scope. Fallback chooses Ask before cloud, Local only or No fallback. Image/Voice choose an explicit compatible route or inherit the active route; inheritance is shown as inherited. Background chooses a configured background candidate or active-route inheritance. Add model starts candidate selection, not loading or granting tools.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: Ordered route candidates have a dedicated handle and insertion line; reordering changes priority only after commit. A disabled/unavailable candidate cannot become eligible through order. No drag between role lists changes model capability.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Requested and effective fallback remain distinct; no eligible candidate yields an unavailable result rather than an unauthorized cloud request. Candidate failure records why the next allowed candidate was considered.

Integration and persistence: Routing preferences apply to new or explicitly re-admitted work; revocations narrow ongoing work as defined. Role, reasoning and context allocations retain the picker’s independent settings.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.lr82l7kfqtds"></a>

### IX-SET-013 — Memory frequency and inspectable learning

Controls: Never; Sometimes; Often; Always; Background Learning; Manage memory; Learning settings

Atlas surfaces: settings-personalisation, settings-models, settings-privacy

Primary: Memory Frequency selects how eagerly eligible information is proposed/stored under existing scope: Never disables new automatic memory collection, not access to authorized existing memory. Sometimes/Often/Always change the configured collection policy, with Always still subject to privacy and eligibility. Background Learning independently enables the declared inspectable Knowledge Bank workflow. Manage memory opens scoped records for inspection/correction/deletion.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Do not invent numeric frequency probabilities. Show actual enabled/disabled and affected scope. Temporary/private conversations retain their existing collection restrictions regardless of frequency.

Integration and persistence: Knowledge Bank learning is distinct from Dreaming weight updates. Turning one on cannot turn on the other, provider disclosure, source-bound general knowledge or unrestricted web research.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.tkw4oeudcbzb"></a>

### IX-SET-014 — Local runtime, residency and always-active preferences

Controls: Keep Dulche available; Model residency; Always-active AI; Inspect service; Review repair; Rebuild index; Export diagnostics

Atlas surfaces: settings-local-general, settings-installed-models, settings-advanced

Primary: Keep Dulche available permits the lightweight controller to remain after windows close. Model residency chooses Unload when idle or Keep selected model loaded, subject to admitted active users and resource limits. Always-active AI enables only configured background behavior. Inspect shows service identity/state; Review repair opens its scoped plan; Rebuild index targets the derived index; Export diagnostics previews redacted diagnostic content before writing it.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Running, degraded, unavailable and stopped come from real service observations. A repair cannot label the service healthy until verified. Unload waits for safe release and cannot kill the controller required to complete it.

Integration and persistence: Always-active does not grant microphone, camera, file, web or remote-service access. Rebuilding a derived index preserves source artifacts. Diagnostics omit secrets and require a separate send action before leaving the device.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.p4yeg4vs6pxe"></a>

### IX-SET-015 — Storage locations, cache cleanup and placement

Controls: View location; Manage models; Review cleanup; New conversation placement; Review pinned files; View device

Atlas surfaces: settings-storage, settings-haven-storage-cache

Primary: View location opens the actual Den/store location in Files with appropriate access. Manage models opens model storage. Review cleanup enumerates disposable cache classes, sizes and active dependencies; selection plus Remove performs that exact cleanup. New conversation placement chooses Local/Mesh/Cloud for newly created eligible conversations. Pinned files opens offline residency management. View device opens the chosen device details.

Secondary: Cache rows offer Inspect files and Select class, not Delete source artifact. Location links may be copied without revealing encryption keys.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Cleanup excludes active writes, current model files and unrecoverable drafts. Space estimates are estimates until deletion receipts arrive. Unavailable Mesh/Cloud placement is shown as unavailable rather than silently falling back.

Integration and persistence: Placement never migrates existing chats automatically and does not authorize cloud model processing. Removing local cache preserves canonical remote/local artifact ownership and reports when offline availability will be lost.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.mcp7n9jb6htr"></a>

### IX-SET-016 — Mesh namespace and capability grants

Controls: Personal namespace; Choose folders; Share local model inference; Edit access; Revoke

Atlas surfaces: settings-mesh

Primary: Namespace selects the exact Mesh namespace being configured. Choose folders opens a scoped picker and review of shared objects, with explicit include/exclude rules. Share local model inference exposes only the advertised inference capability to authorized paired devices. Edit access changes a selected device/capability grant; Revoke removes it.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: Folder selection uses the picker’s checked identities. Dropping a folder onto a generic device card cannot silently share the entire folder; only a labelled Add shared folder target opens the same review.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Offline peers show last-known state. Revocation prevents future permitted steps and reports acknowledgements per reachable peer; it does not claim already copied bytes vanished.

Integration and persistence: File sync, conversation placement, inference, clipboard and input remain separate scopes. Choosing a namespace does not change the current browser account or artifact owner.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.6u5kzs66jqh2"></a>

### IX-SET-017 — Accessibility preferences

Controls: Interface scale; High contrast; Reduced motion; Always show keyboard focus; Status announcements; Pointer size; Larger touch targets; View shortcuts

Atlas surfaces: settings-accessibility, appearance

Primary: Scale selects the displayed UI multiplier and previews responsive reflow; document typography is unchanged. High contrast changes semantic contrast tokens. Reduced motion minimizes movement. Always show focus preserves visible focus indication. Concise/Detailed announcements control status verbosity. Pointer size adjusts the device-level pointer where exposed; unsupported platforms show an unavailable reason. Larger touch targets expands hit regions without obscuring adjacent actions. View shortcuts opens the actual shortcut map.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Scale/announcement selectors use radio/menu semantics; pointer size follows slider bounds shown by the platform (Atlas preview 1–4 is a sample). Keyboard-only access must reach Revert after a scale change.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: Accessibility overrides Comic’s decorative angles, glow and travel animation where needed without changing command semantics. Reflow keeps the focused control and draft visible.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.5qhudgy4z2sc"></a>

### IX-SET-018 — Notification channels and privacy

Controls: Desktop notifications; Notification sounds; Content previews; App notification centre; App desktop notifications; App badge; Quiet hours; Quiet while presenting; History & retention

Atlas surfaces: settings-notifications

Primary: Desktop notifications requests/enables delivery through this device’s actual notification permission. Sounds gates event audio without muting visual status. Content previews chooses When unlocked/Always/Never. Per-app centre, desktop and badge switches independently control their named channel. Quiet hours opens a schedule editor when enabled; Quiet while presenting suppresses ordinary interruptions during an actual presentation session. History & retention opens notice retention controls.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: OS denied and app disabled are different. A global channel off preserves per-app choices. Quiet settings show next active interval and timezone; no hidden always-on timer is inferred from a checkbox.

Integration and persistence: Delivery preferences do not cancel underlying automations, messages or approvals. Notification history deletion does not delete the Action Log or task evidence.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.hd0n65ignubd"></a>

### IX-SET-019 — Contextual chat placement and keyboard mappings

Controls: Chat bar position; Off; Floating; Sidebar; Navigation shortcuts; Menus & focus; Reset shortcut

Atlas surfaces: settings-layout, settings-haven-key-navigation, settings-haven-key-menus, settings-haven-shortcuts

Primary: Position applies Off/Floating/Sidebar to contextual chat in the shared app layout. Off hides its entry surface while a named Spaces route keeps work accessible. Shortcut settings open the active platform map; assigning a chord validates reserved keys and collisions before Save. Reset restores that command’s default, with scope shown.

Secondary: A shortcut row menu offers Change, Clear override and Reset default. Position selector has no additional secondary action.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Conflicting shortcuts show both commands and require an explicit resolution, not silent override. Unsupported platform chords remain unavailable.

Integration and persistence: Changing placement retains conversation identity, source binding, input draft and active operations. Native text editing/IME gets shortcut priority. A preference does not add capabilities absent from the platform.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.gc3nalu2r2yg"></a>

### IX-LEARN-001 — Dreaming master switch and idle eligibility

Controls: Enable Dreaming; Dreaming status; Pause Dreaming; Resume Dreaming

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Enable Dreaming opens its configuration on first use and commits enablement only after trainable target, review agent, resource limits and data scope are valid. The enabled scheduler starts only when both the AI and computer meet the declared idle conditions. Status opens the current candidate/run. Pause prevents new episodes and checkpoints the current one at a safe boundary; Resume returns it to idle eligibility.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Disabled, waiting for idle, preparing, training, evaluating, paused, blocked and failed are distinct. Inference-only hosted models show Training unavailable; enabling a switch cannot imply access to their weights.

Integration and persistence: Foreground user input preempts eligible training according to the checkpoint policy. The independent controller remains alive through model swaps. Disable prevents future training and preserves inspectable checkpoints; it does not delete or automatically promote trained weights.

Acceptance: Start foreground work during a training episode: service checkpoints/preempts and restores usable inference without losing the original session.

Basis: Owner Dreaming decision and §3.6 training contracts; new control layout requires an Atlas extension.

<a id="h.wkkmcyqlelib"></a>

### IX-LEARN-002 — Dreaming target, review agent and budgets

Controls: Training target; Review agent; Dataset scope; GPU/memory budget; Energy/temperature limits; Idle delay; Save training configuration

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Target picker selects a versioned trainable base/adaptor and compatible backend. Review agent selects its separate role and evaluation policy. Dataset scope checks approved sources/feedback collections. Budget fields configure the exact device/resource ceilings and idle delay; Save validates the combination and stores a configuration revision. No training begins merely by changing a picker.

Secondary: Target menu offers Inspect capabilities and Open model details; dataset menu offers Inspect provenance. No context action silently imports private conversations.

Drag: GPU/device allocation uses named device checkboxes or a documented allocation editor, not arbitrary card drag. Dataset collection reorder changes curriculum order only if explicitly labelled; otherwise sources are an unordered approved set.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Reject unsupported adapters, invalid memory budgets and unusable reviewer routes with concrete reasons. Capture actual effective limits separately from requested limits.

Integration and persistence: Main and reviewer cannot approve their own unverified promotion by relabelling a conversation. Training consent, model residency and provider disclosure are independent controls.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

Basis: Owner Dreaming decision and §3.6; newly specified control behavior.

<a id="h.desoeog35drt"></a>

### IX-LEARN-003 — Training episodes, candidate evaluation and rollback

Controls: Episode row; Evaluation details; Compare candidate; Promote; Reject candidate; Roll back; Delete checkpoint

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Episode opens scenario, permitted input provenance, reward/evaluation outcome and checkpoint references. Compare displays candidate versus active model with measured results. Promote activates the selected passing candidate under the configured manual/automatic policy; Reject keeps the active model and records why. Roll back selects a retained compatible checkpoint and reviews affected future sessions. Delete checkpoint removes only a selected unreferenced retained checkpoint after showing dependencies.

Secondary: Candidate menu mirrors Inspect, Compare, Promote/Reject and Export metadata; an export of actual weights is separate with format/license scope. It cannot claim improvement from a single preference vote.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Training complete is distinct from evaluated, accepted and active. Failed evaluation preserves the prior model. Promotion is a revision-checked transaction; partial activation rolls back or reports recovery.

Integration and persistence: Running conversations retain captured model provenance and undergo explicit transition; historical answers are never rewritten as outputs of new weights. Rollback of weights does not undo past external actions.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

Basis: §3.6 checkpoint, evaluation and promotion contracts; new controls.

<a id="h.n4yjxwx4drq8"></a>

### IX-LEARN-004 — Response preference and feedback evidence

Controls: Prefer A; Prefer B; Both useful; Neither useful; Why?; Change choice; Exclude from training

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A/B preference selects one versioned response pair and records the displayed alternative identities. Both/Neither records those distinct outcomes rather than inventing a winner. Optional Why opens a concise feedback field. Change choice revisions the preference while retaining provenance. Exclude from training removes eligibility for future dataset admission at the stated scope.

Secondary: Response content retains its ordinary Copy/Inspect source menu. A vote button has no hidden approval action.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Pending feedback save and durable feedback are distinct. Already-admitted training examples show their dataset/checkpoint lineage; exclusion cannot falsely claim to erase completed training influence.

Integration and persistence: Preference evidence feeds the defined reward-learning/policy-update workflow only with eligibility enabled. A vote never instantly edits weights, modifies the original source or grants a permission.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

Basis: Owner preference-driven RL clarification and §3.6.

<a id="h.9wtvdt5x4a3u"></a>

### IX-SOUND-001 — Event sound assignment rows

Controls: Message finished; Error; Question awaiting input; Goal completed; Goal checkpoint; Change sound; Preview; Reset event

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Each named event row displays its assigned sound asset or None. Change sound opens the shared bank for that EventType. Preview plays that row’s selected asset locally once, with Preview becoming Stop until playback ends. Reset event restores its default mapping after showing the exact event; it does not reset other rows. Any available bank asset can be assigned to any event.

Secondary: Row menu: Choose sound, Preview/Stop, Use None, Reset this event, Copy mapping to another selected event. Copy applies only after destination events are explicitly selected.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Missing/corrupt assets show unavailable with Replace; no silent substitution outside the configured fallback. Preview failure leaves the assignment unchanged.

Integration and persistence: Preview is not an actual completion/error receipt and cannot trigger automation or analytics as though work completed. Event mappings remain independent of Natural Conversation mode.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

Basis: Owner sound-bank request and SOUND-001–012; new setting surface.

<a id="h.2gs95lugfscj"></a>

### IX-SOUND-002 — Sound bank search, audition and selection

Controls: Search sounds; Category; Sound row; Play preview; Stop; Use sound; None; Cancel

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Search/category filters the bank. Clicking a sound row selects it for preview; it does not commit the event mapping. Play auditions that asset and stops any previous audition. Use sound commits the selected asset to the target event and closes. None explicitly mutes that event. Cancel restores the previous mapping while stopping preview.

Secondary: Sound menu offers Preview, Details and availability/download status; no event-category restrictions hide otherwise eligible assets.

Drag: Sound rows are not reorderable by dragging. A drag from a sound to a labelled event mapping target may preview a proposed assignment, but requires the same explicit Use sound commit; arbitrary drops cannot overwrite mappings. Event reorder is presentation only.

Keyboard and touch: Arrows select rows; Space previews/stops when the bank list is focused; Enter on Use sound commits. Screen-reader focus never auto-plays audio. Touch has separate Play and Select targets.

State: Expose the current value and availability. An unavailable operation shows its specific prerequisite; a pending command cannot be submitted twice. A failure retains the prior committed value and offers a scoped retry.

Integration and persistence: The picker shows its target event throughout. Changing target mid-picker requires an explicit selection so the sound cannot be assigned to a stale row.

Acceptance: Audition three sounds and cancel: previous event mapping remains and all preview playback stops.

Basis: Owner sound-bank request; new controls.

<a id="h.a3dpf0p8vv43"></a>

### IX-SOUND-003 — Volume, mute and quiet behavior

Controls: Global volume; Event volume; Global mute; Event mute; Respect quiet hours; Preview volume

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Volume sliders preview their gain and commit one value on release; paired numeric entry accepts 0–100%. Global mute suppresses all event sounds while retaining mappings/volumes. Event mute suppresses only that event. Respect quiet hours applies notification quiet policy. Preview uses the explicit audition gain and never changes system volume.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Muted, unavailable output and zero gain remain distinguishable. Device/OS restrictions show actual availability. Rapid duplicate/replayed event receipts are deduplicated by EventID.

Integration and persistence: A true event selects the mapping once at delivery, honours quiet/device context, and does not replay on reconnect, opening history or compaction. Visual/error/question indicators remain available when sound is muted.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

Basis: SOUND-001–012; new controls.

<a id="h.9ss8ucd0qr3"></a>

### IX-THEME-001 — Theme chooser and Comic replacement

Controls: Choose theme; Professional; Glow; Playful; Frosted Glass; Bubble; Metallic; Retro; Western; Comic; Create theme

Atlas surfaces: appearance, settings-appearance, settings-theme-options

Primary: Choose theme opens the built-in gallery. Clicking a theme applies its versioned UI token set and keeps the chooser open for comparison; Done dismisses with the selected theme persisted. Comic is the current built-in replacing Cinematic. The old Cinematic choice is removed from all current pickers; legacy cinematic preferences resolve through the documented one-time migration to comic with the original ID retained in migration provenance. Create theme opens a draft editor.

Secondary: Theme-card menu offers Preview, Apply and Reset theme-specific overrides. Built-ins cannot be deleted. Custom themes additionally expose Rename, Duplicate and Delete custom theme without deleting authored documents.

Drag: Theme cards are not draggable product artifacts. A theme asset can be imported only through an explicit Import theme action with schema/token validation, not by dropping arbitrary CSS/code over the UI.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Selected, previewed and saving/error states are distinct. On failed persistence preserve the prior durable selection and an explicit retry/revert state. Reference-only Industrial/Synthwave/Snug art does not register extra built-ins.

Integration and persistence: Theme changes affect CUI chrome and semantic states, not document typography, authored webpage styles or model quality. Comic geometry never changes hit-test regions into misleading diagonals.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

Basis: Owner Comic mockup and replacement instruction, 21 November 2026; supersedes Atlas legacy Cinematic preference policy.

<a id="h.r2mm5ucajw2h"></a>

### IX-THEME-002 — Appearance mode, accent palette and samples

Controls: Light; Dark; Follow system; Contrast; Accent override Off/On/On per app; App scope; Choose palette; Electric blue; Iris; Lagoon; Sunset; Accent colour; Done; Primary/Secondary/Tertiary/Ghost samples

Atlas surfaces: appearance-palette, appearance, settings-theme-options

Primary: Light, Dark or Follow system changes the appearance of the selected theme under UI-010 while preserving its identity and independent accent choices. Contrast adjusts the theme's supported contrast relationships under UI-012. Accent override selects Off, On or On per app under UI-004: Off resolves native app accents, On applies the saved global override, and On per app applies each app's saved override or its native fallback. Palette presets and Accent colour edit the explicitly targeted global or named-app override, with effective application governed by the selected override mode; they do not unconditionally replace a shared suite accent. Accent colour opens an exact color picker and validates the value. Done returns to the invoking page. Primary/Secondary/Tertiary/Ghost are live style demonstrations only and do not execute app operations.

Secondary: No custom context menu. Secondary click does not activate this command, change selection or commit a value; text fields retain their native text-edit menu.

Drag: Within the color picker, drag hue/saturation/value handles for a preview and commit through the picker’s stated Apply/release boundary. Escape restores the pre-picker color. Palette cards themselves are not reorderable.

Keyboard and touch: Hex/RGB entry provides a precise alternative to the color field. Arrows/labels expose the selected color semantically; color alone never communicates a permission or selected state.

State: The palette resolves accessible foregrounds from semantic tokens. A chosen accent cannot make a destructive action visually indistinguishable from an ordinary action. Comic keeps the approved yellow hero action role as its native/default presentation; deliberate effective action-accent overrides and secondary/selection roles follow UI-004 while preserving the theme's identity artwork, semantic states and authored content.

Integration and persistence: Changing accent does not recolor authored images or document text. High contrast can replace decorative combinations while preserving selected mode/theme provenance.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.8gr6lil0k2s7"></a>

### IX-THEME-003 — Custom theme draft and material editors

Controls: Edit stops; Colour; Edit pattern; Add stop; Remove stop; Save theme; Cancel

Atlas surfaces: custom-theme

Primary: Edit stops opens the current gradient’s stop list, positions and angle. Add stop inserts at the chosen position; Remove cannot remove below the required minimum. Colour edits the selected semantic color token. Edit pattern opens the selected pattern’s scale/angle/repeat controls. Continue and Later inside Live preview are sample buttons and do not advance, save or leave the theme builder. Save theme validates and registers the custom theme draft; Cancel preserves the prior saved preference.

Secondary: Gradient stop menu: Change color, Set position, Duplicate stop and Remove where valid. A theme token row offers Reset inherited value and Copy token value. These menus cannot inject executable UI code.

Drag: Drag a gradient stop along its normalized track; maintain stable stop IDs and sorted positions, with keyboard numeric alternatives. Dragging past another stop reorders by position, not identity. Escape restores positions/colors. Pattern preview pans only when explicitly zoomed.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Incomplete custom composition decisions remain bounded by DEC-001: do not invent an unrestricted material ABI. Unsupported tokens are marked unavailable; validation errors retain the draft.

Integration and persistence: Custom previews use sample chrome and never alter the currently edited artifact. Deleting a custom theme falls back to a chosen built-in and preserves the draft export if requested.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.ahnbfjnkbcd3"></a>

### IX-THEME-004 — Custom theme identity and inheritance

Controls: Theme Name; Inherits; Material/style choice; Use inherited value

Atlas surfaces: custom-theme

Primary: Theme Name edits the draft label and validates uniqueness/display rules on Save theme. Inherits opens the existing theme registry and selects a parent by stable ThemeID/version. Changing parent recomputes inherited values while retaining explicit overrides and showing their sources. The Solid/Gradient/Pattern style selector changes the chosen role’s material type and preserves other material drafts for deliberate reuse; it does not automatically merge incompatible token shapes.

Secondary: A role/property menu offers Use inherited value, Inspect effective value and Reset override. Reset removes that override only; it does not replace the whole theme.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Cycles, missing parents and unsupported material schemas show errors before saving. Parent changes never destroy unknown future fields. DEC-001 still bounds unapproved material/accent-composition details.

Integration and persistence: The live preview reflects the draft. Current application theme changes only after Save theme and the explicit Apply selection; sample Continue/Later controls cannot commit it.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.4cjns9zgqf5"></a>

### IX-THEME-005 — Custom theme transparency, blur and roundness

Controls: Primary/Secondary/Tertiary Transparency; Background blur; Button Roundness

Atlas surfaces: custom-theme

Primary: Each role’s transparency/blur field edits that role’s draft value with its labelled unit. A slider drag previews the value, release records one draft undo step, and Escape restores the gesture’s starting value. Button Roundness accepts the inclusive 0–100% range; 0 uses the square end and 100 the maximum shape permitted by that component’s geometry. Exact numeric entry is available beside the slider.

Secondary: Each field offers Use inherited value and Reset override. Secondary clicking a slider does not jump or reset its value.

Drag: Only the field’s slider thumb captures the drag. A material preview is not a draggable window or app object. Parent page scroll remains available outside the slider.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: Renderers that cannot realize a blur show the effective fallback and requested token; they must not fake a successful native material. Values remain a theme draft until Save.

Integration and persistence: Transparency applies to the intended surface layer, not its text or hit-test semantics. Readability checks evaluate text and state overlays after material composition. Disabled future Hover Effects/Custom Cursors remain future settings, not newly activated by Comic.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.ev400kk4ow3l"></a>

### IX-THEME-006 — Custom theme font roles and preview fields

Controls: Main Font; Primary Button Font; Secondary Button Font; Tertiary Button Font; Heading Font; Body Font; Code Font; Family; Weight; Size

Atlas surfaces: custom-theme

Primary: Each of the seven role rows has independent Family, Weight and Size controls. Family opens the available font picker; Weight offers supported named/numeric weights; Size accepts a validated positive size in the stated unit. Use inherited value removes only that property override. A live preview shows all seven roles and marks unavailable font substitution; it never installs a font silently.

Secondary: Font-role menu offers Use inherited role, Reset this role and Copy role values to an explicitly chosen other role. Text fields retain native text editing.

Drag: Font rows and sample text are not reorderable. Numeric size may use its documented stepper/slider, not unlabelled label-drag scrubbing. Ordinary text selection in the preview cannot change a font.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: A missing font uses a named fallback in preview and requires explicit acceptance or replacement before a saved theme claims that exact font. Invalid size remains editable.

Integration and persistence: Project name, Continue, Later, Keep me organised, Show details and Volume in Live preview are sample controls. They demonstrate input/focus/selected/slider states without editing real projects, launching work or saving the theme. Authored document and code-editor content fonts remain under their respective ownership settings.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.hxwuuyrvutpa"></a>

### IX-SET-020 — Settings tree search and help

Controls: Search settings; Clear settings search; Settings help; Expand/Collapse category

Atlas surfaces: settings-general, settings-layout, settings-ai

Primary: Search settings filters category/row names and help text in the current allowed settings catalogue while preserving ancestors needed to explain scope. Clear removes the query and restores the tree expansion state. Clicking a result opens its exact setting and focuses/highlights that row. Help opens the explanation for the current page and effective scope; it does not send a diagnostic report. A category-only expander changes visible children without navigating.

Secondary: Search uses native text-edit commands; Help has no custom menu. Category menus may Copy settings link and Expand/Collapse this group only.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Tree arrows traverse visible nodes, Right expands, Left collapses/returns to parent, Enter opens a destination. Search results announce match counts without moving the caret. Escape dismisses results before changing the query.

State: A hidden or managed setting remains accurately labelled if discoverable. Search never exposes secret values or matches private credential contents.

Integration and persistence: Searching changes view state only; no selected preference or app setting is committed. A row focus highlight clears after interaction without changing its value.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.

<a id="h.rkhtastwwlcb"></a>

### IX-SET-021 — Diagnostic logs, schema checks and upload policy

Controls: Open log; Validate settings Inspect; Rebuild index Review; Automatic diagnostic uploads

Atlas surfaces: settings-advanced

Primary: Open log opens the actual redacted system log with time/service filters and Copy selected entries. Validate settings inspects supported schema versions and reports invalid fields while preserving unknown future fields; Apply repair is a separate reviewed operation. Rebuild index Review shows sources, expected scope and current index state before Start rebuild. Automatic diagnostic uploads enables only the explicitly declared redacted diagnostic channel after showing destination and categories.

Secondary: Log entries offer Copy redacted details and Open related service/event. No menu exposes secrets, raw message bodies or a generic Execute line action.

Drag: This control is not draggable and accepts no object drop. Moving a pressed pointer outside its hit area cancels button activation; ordinary text selection or surrounding scrolling remains available.

Keyboard and touch: Use [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6) for button activation and [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk) for any popup: Tab reaches the control, Enter/Space activates once, Escape dismisses and restores its trigger. Tap is equivalent; long press never runs the command.

State: An unknown schema is not automatically corrupt. Rebuild retains the last usable index until replacement is validated. Unavailable upload destination leaves upload disabled or visibly pending according to policy.

Integration and persistence: Enabling automatic diagnostics is separate from product analytics, crash-report review and any provider conversation disclosure. Explicit diagnostic export remains a local file operation until a separate send is invoked.

Acceptance: Activate, cancel and retry using pointer and keyboard. Confirm one operation against the intended identity, stable focus, and no false success after failure.
