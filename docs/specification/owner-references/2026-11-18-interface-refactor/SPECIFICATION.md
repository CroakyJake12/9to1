<a id="t.0"></a>

# 1. Overview, Rules, Philosophies

<a id="t.0--h.l8a5r4xc2at9"></a>

# 1.1 - Purpose and authority

This specification describes the behaviour and visual contracts of 9to1. The editable Interface Atlas supplies the linked screen examples; this document supplies the rules, interaction outcomes and states that a still image cannot show.

This specification combines the current owner-directed interface contracts with the retained framework, runtime, app, interaction and acceptance requirements. The coverage index identifies implemented previews, specified production behaviour and any remaining source or decision gaps.

**BASE-001** — Owner decisions take priority over older mockup notes. Preserve the latest accepted interaction and visual rules when a legacy screen still shows an earlier design. Record a conflict against its requirement and Screen ID instead of silently choosing whichever file is newest.

**BASE-002** — A mockup is evidence of intended presentation, not evidence of a connected implementation. Examples of people, conversations, balances, providers, models and files are fixtures unless the product actually reads them from a service or local store.

**BASE-003** — Keep three statuses distinct: Established design (explicit owner direction or a retained Atlas contract); Draft interaction (a concrete proposal for behaviour not yet settled); Preview coverage (what the offline Atlas actually demonstrates). A draft can be revised without pretending it was previously approved.

Updated 18 November 2026. The detailed interaction proposals in tab 6 are a starting specification, not a claim that all context menus have been implemented.



<a id="t.0--h.fay90z5c2elf"></a>

# 1.2 - Reading the specification with the mockups

[Open the editable Interface Atlas](design/9to1_Interface_Atlas_v3.3_Editable.html).

Every mockup reference uses its stable Screen ID, for example [host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab) or [boards-context](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context). Search that ID in the Atlas screen navigation. With the HTML open locally, appending #[host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab) to its file address opens that view directly; ?screen=[host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab) is the explicit query equivalent.

Links from this document open the Atlas file. The accompanying Screen ID identifies the exact view; it does not imply that the file-library page is a live hosted route. In the Atlas, Build notes links back to the relevant specification section.

**REF-001** — Keep Screen IDs stable when titles, copy or layout change. Keep compatibility routes for older saved references. Requirement IDs such as HAV-TAB-001 are stable too; add a superseding note rather than reusing an ID for a different rule.

**REF-002** — Each feature entry must identify its surface, trigger, visible result, state change, persistence scope, cancellation/undo path and keyboard equivalent. Link default, selected, expanded, empty, loading and failure examples when those states differ materially.

**REF-003** — Update the document and matching Atlas notes together. The reference index in tab 6 records the current starting coverage and the remaining interaction work.



<a id="t.0--h.87c0ypc2w1q6"></a>

# 1.3 - Shared product rules

**UX-001** — Use direct nouns and verbs: Settings, Appearance, Add widget, Apply and reload. Remove decorative introductions and generated-sounding taglines. Keep help text when it explains a consequence, permission, format, limit or recovery action.

**UX-002** — Give each visible control one job. A disclosure opens options; it does not also change a setting. Closing a menu does not commit a pending change. Navigation does not silently reset the selected model, accent, layout or conversation scope.

**UX-003** — Keep the primary work area quiet. Put frequent actions directly beside the work and secondary actions in labelled menus or optional panels. Consolidate repeated identity, save state and mode controls.

**UX-004** — Show truthful state. Pending edits, unsaved data, saving, saved, offline and failed are separate outcomes. Failure leaves recoverable input visible. A preview must not claim that an external action, permission grant, model reload or file save occurred.

**UX-005** — Preserve canonical identities and authored content across navigation. Changing a label, theme, layout or screen composition does not create a new Assistant, conversation or document.

**UX-006** — Support pointer and keyboard access to the same operations. Visible move buttons provide an alternative to drag and drop. Menus return focus to their trigger after Escape. Moving focus alone never performs a destructive action.



<a id="t.0--h.ac75flhfq1fx"></a>

# 1.4 - Scope and unfinished decisions

Global preferences follow the user across 9to1; Local preferences apply to this device. App settings reference shared preferences where relevant instead of creating competing copies. Retain the established canonical identities, revision-aware transactions, offline checkpoints, explicit replica manifests and conflict-preservation contracts. Use the retained technical chapters for exact storage and transport schemas; this design revision must not silently replace them.

CUI is the single public interface system, with Avalonia and retained donor engines as implementation details. Dulche owns the shared AI/runtime broker, routing, durable requests/TaskRuns, actions, memory and subagent integration. Preserve the v3.1 technical definitions and package contracts; current owner instructions supersede their conflicting visual presentation.

Remaining implementation and verification work includes rendered mobile layouts, the full app state matrix, native browser integration, actual model resource constraints, enforced permissions, durable collaboration and the proposed context menus. The corresponding product contracts are specified here and in the incorporated technical modules; a mockup or specification entry is not evidence that its production implementation is complete.

Cross-references: 2.1 CUI Framework; 2.3 Haven App; 3.2 Spaces Usability; 4. Boards; 5. Settings; 6. Interactions and references.



<a id="t.8lnv8r5vlfea"></a>

# 2. Haven & Frameworks

<a id="t.8lnv8r5vlfea--h.gcnq9fao70bj"></a>

# 2.1 - CUI Framework

<a id="t.8lnv8r5vlfea--h.fcieekkrartz"></a>

## Principles

**CUI-001** — All 9to1 surfaces use shared semantic roles for text, surfaces, borders, focus, selection and action emphasis. Native app identity artwork can keep its own colours; navigation selection and shared controls follow the chosen accent.

**CUI-002** — Share component behaviour across apps. A tab, dropdown, toggle, menu, setting row and contextual AI bar must expose the same focus, disabled, selected, pending and dismissal states wherever they appear.

**CUI-003** — The Professional theme uses rounded action buttons and restrained neutral containers. Large groups of controls are not each placed inside competing heavy capsules. Keep typography strong enough for messages and navigation; do not centre Haven tab content.

**CUI-004** — A panel is optional when its information is secondary to the main task. Closing a panel preserves the underlying document, conversation and selection. Disclosures reveal existing capabilities rather than duplicating those controls elsewhere.

Mockups: [appearance-palette](design/9to1_Interface_Atlas_v3.3_Editable.html#appearance-palette); [host-model-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker); [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex); [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor).



<a id="t.8lnv8r5vlfea--h.m3dtyvsdppas"></a>

## Language

**CUI-LANG-001** — The public component contract needs stable identity, semantic role, accessible name, value/state, supported actions and an explicit disabled reason when relevant. The HTML and CUI developer views must let a developer inspect the corresponding surface without confusing presentation with permission or runtime execution.

**CUI-LANG-002** — Keep content separate from presentation. A theme or font change must not alter the canonical document text, Assistant identity, model selection or action scope.

CUI syntax and public ownership are governed by the retained technical/cui-03.md and technical/cui-04.md chapters. Generated interfaces use the safe typed component/action registry and current brokered grants; text, HTML and generated code do not acquire execution authority. The Atlas's JavaScript and DOM remain prototype implementation, not the production language definition.

Mockups: [host-devtools-cui](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui); [host-devtools-html](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html); [spaces-generative-ui](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generative-ui).



<a id="t.8lnv8r5vlfea--h.p7f3vcvrqyuf"></a>

## Themes

**THEME-001** — The user selects one bright accent. Primary, secondary, tertiary and ghost are emphasis treatments of that choice, not four unrelated palette colours. With blue selected, secondary navigation must not switch to purple.

**THEME-002** — Primary actions use the strongest accent treatment. Secondary actions and selected sub-sections use a softer treatment of the same hue. Tertiary and ghost actions keep quieter surfaces while retaining clear hover, focus and selected states. Semantic error/warning/success colours remain distinct from decorative accent choice.

**THEME-003** — Accent previews show the shared action treatments together. Light/Dark uses the same rounded segmented-control language as the rest of the Professional theme. The selected segment is evident through shape/contrast and state, not colour alone.

**THEME-004** — Messages and navigation use a stronger weight than decorative metadata. Haven tabs are left aligned with stable icon, title and close positions; only the Main/Voice/Vision selector is centred within its own tab segments.

**THEME-005** — Scrollbars inherit the current surface and theme. Keep a visible usable thumb, transparent or matching track, and stronger contrast in high-contrast mode. Do not hide overflow to disguise a pale edge or clipped layout.

Mockups: [appearance](design/9to1_Interface_Atlas_v3.3_Editable.html#appearance); [appearance-palette](design/9to1_Interface_Atlas_v3.3_Editable.html#appearance-palette); [settings-theme-options](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options); [host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab).

Acceptance: choose blue, navigate through Home, Settings, model picker, Assistants and Boards, then reopen the file. Shared selected controls retain blue emphasis and a readable focus state; authored artwork is preserved.





<a id="t.8lnv8r5vlfea--h.vqmaylqp09je"></a>

# 2.2 - Dulche Framework

Dulche is the canonical shared AI/runtime broker described in the retained v3.1 Dulche technical chapters. It owns model routing, request admission/execution, durable TaskRuns and ActionGraphs, tool actions, memory and subagent integration. Haven hosts the surfaces and CUI defines the public UI; neither introduces a competing execution or state authority. Preserve existing API, transport, multi-engine and embedded-service contracts while applying the current presentation changes.





<a id="t.8lnv8r5vlfea--h.ut9knvngbzkc"></a>

# 2.3 - Haven App

<a id="t.8lnv8r5vlfea--h.lbrybk6tio42"></a>

## 2.3.1 - Shell and tabs

**HAV-SHELL-001** — Haven owns the outer tab strip, address/navigation row, model picker and shared utilities. Embedded apps do not repeat an entire Home/account/history/chat header. App-specific actions remain in the host or a compact app toolbar where they are meaningful.

**HAV-TAB-001** — Home and New tab are one customisable start surface. There is no fixed Home tab. New tab appears below Explore in the Home sidebar and remains available from the tab-strip plus button.

**HAV-TAB-002** — A normal Haven tab contains its icon, left-aligned bold title and visible close X. Selecting a Home sidebar destination changes the active tab's content/title while preserving its close control and tab identity. A close control has an accessible name and is separately focusable from the navigation target.

**HAV-TAB-003** — Closing Home/New tab selects the nearest valid tab to the right, then to the left; if none remains, open [host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab) and focus the active tab. Draft extension: reopening a recently closed tab restores its route and recoverable state. Do not discard unsaved document edits without the app's save/recovery contract.

**HAV-TAB-004** — Draft tab context menu: New tab; Reload; Duplicate; Pin/Unpin; Mute/Unmute when audio exists; Move to another window when supported; Close; Close others; Close tabs to the right; Reopen closed tab. Disable inapplicable items and keep destructive groups separated. See CTX-01 in tab 6.

Mockups: [host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab); [home-apps](design/9to1_Interface_Atlas_v3.3_Editable.html#home-apps); [home-models](design/9to1_Interface_Atlas_v3.3_Editable.html#home-models); [home-assistants](design/9to1_Interface_Atlas_v3.3_Editable.html#home-assistants); [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor).

Preview boundary: the functional native Close control currently covers shared Home/New tab and Home sidebar destinations. Other legacy work-tab X icons remain visual examples; general tab closing/reopening still needs implementation.

Acceptance: open New tab, choose Apps then Models then Assistants from the sidebar, and close the active tab. The X stays present, routes remain correct and focus moves to the resulting active tab. Repeat with a saved/edited Atlas screen.



<a id="t.8lnv8r5vlfea--h.mz808bphuj0r"></a>

## 2.3.2 - Home and New tab

**HOME-001** — The start surface has search, optional clock/date, pinned shortcuts and widgets. Home and New tab share layout, background, visibility, width and order preferences. Older [home-dashboard](design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard) references resolve to the same surface.

**HOME-002** — Customise is a visual editing mode. Drag handles appear on editable tiles; move buttons and keyboard alternatives operate on the same order. The editor previews layouts, backgrounds and available widgets instead of presenting only text toggles.

**HOME-003** — Adding, moving, switching a widget between compact and wide, or removing it changes layout metadata. Preserve its authored body and source identity. Reordering shortcuts never rewrites their targets. Done leaves the editor; Reset layout restores the declared defaults and has a recovery/undo path.

**HOME-004** — Search and Ask AI are separate actions. Search navigation must distinguish a supported page or address from an unsupported preview target. Failed resolution leaves the user's input visible and states the problem beside the field.

**HOME-005** — The scroll area stays inside the content region without a bright unexplained side line. At narrow widths, controls wrap and tiles stack without horizontal page overflow or obscuring the sidebar.

**HOME-006** — Draft context menus reuse the visible editor's move/remove actions and compact/wide widget choices. Edit shortcut label/target and widget-specific Configure are additional proposed flows; they are not yet demonstrated by the editor. Removing a shortcut/widget does not delete its source app, file or event.

Mockups: [host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab); [host-newtab-customise](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-customise); [host-newtab-widgets](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-widgets); [home-dashboard](design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard).

Preview boundary: drag/move/width/visibility choices and supported Atlas navigation are demonstrated. Calendar, note, activity and timer data are sample content; no live external source is implied.



<a id="t.8lnv8r5vlfea--h.xrco6lv5bzqg"></a>

## 2.3.3 - Model picker

**MODEL-001** — The picker contains centred Main, Voice and Vision tabs. This alignment is scoped to the picker; Haven's own tabs remain left aligned.

**MODEL-002** — Within a role, Reasoning stays visible. Context, Model and Assistant/Specialist are separate collapsible headings that show the current selection or status while closed; Context can show usage or Pending changes. Use one compact searchable list of model rows with name, short source label and selected state.

**MODEL-003** — Distinguish local device, remote app/device and cloud provider sources without repeating the same source description in large stacked cards. Browse models opens the model catalogue. Preserve a role's current choice when another section opens or the role tab changes.

**MODEL-004** — Changing Context creates a pending value. Show Apply and reload only when it differs from the applied value. Typing, Enter, dismissing a section or switching roles must not silently apply it. Invalid values leave the applied value unchanged and show an inline reason.

**MODEL-005** — The production implementation must validate a context limit against the selected model/provider and available resources. Do not infer a maximum from sample numbers. Applying starts an explicit reload operation; show loading, success or failure, and retain recoverable input if reload fails.

**MODEL-006** — Assistant/Specialist selection references canonical identities shared with Home and Spaces. Selecting None removes that participant choice for the role; it does not delete the underlying Assistant or Specialist.

Mockups: [host-model-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker); [home-models](design/9to1_Interface_Atlas_v3.3_Editable.html#home-models); [home-models-installed](design/9to1_Interface_Atlas_v3.3_Editable.html#home-models-installed); [home-model-details](design/9to1_Interface_Atlas_v3.3_Editable.html#home-model-details); [home-assistants](design/9to1_Interface_Atlas_v3.3_Editable.html#home-assistants); [home-specialists](design/9to1_Interface_Atlas_v3.3_Editable.html#home-specialists).

Preview boundary: Apply and reload stores the chosen context and redraws the offline view. It does not reload a real inference runtime. The preview shares one pending/applied context value across Main, Voice, Vision and the host context popup, saved as uiPreferences.modelContextTokens. Model, reasoning and participant selections are per-role in-session state and are not saved-file preferences. Production provider limits still need implementation.



<a id="t.8lnv8r5vlfea--h.cx3hialkcypo"></a>

## 2.3.4 - Browser tools, Manage page and inspectors

**BROWSE-001** — Browser tools is a compact command menu with engine, developer tools, history, bookmarks, reload and settings. Use normal command labels and submenus; do not replace the menu with long instructions about another browser.

**BROWSE-002** — Manage page describes the current page: origin/address, protocol and connection status, page-specific permissions and storage/site controls where supported. Separate an internal 9to1 page from an HTTPS site or insecure origin. Display the actual state; do not claim a security property from a decorative icon.

**BROWSE-003** — Permission changes are scoped to the displayed origin and permission. Show Ask/Allow/Block or the applicable supported states, plus Reset permissions. An unavailable capability remains explained and cannot silently broaden access.

**BROWSE-004** — HTML and CUI developer tools expose a tree, selection properties and a console area. The view clearly identifies which representation is being inspected. Selecting an item updates the corresponding properties without editing the source by default.

**BROWSE-005** — History and bookmarks have distinct views and actions. Reload acts on the current page. Changing the browser engine is an explicit preference whose production implementation must handle unavailable engines and any required reload.

Mockups: [host-browser](design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser); [host-devtools-html](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html); [host-devtools-cui](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui); [host-browser-history](design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-history); [host-browser-bookmarks](design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-bookmarks). Browser tools and Manage page are available from the host controls.

Preview boundary: the current inspectors show a bounded read-only snapshot and a query-only console. They do not evaluate arbitrary JavaScript. Engine and permission controls are preview preferences; native engines, certificates and operating-system grants are not changed.



<a id="t.8lnv8r5vlfea--h.oa4ewpkbzo55"></a>

## 2.3.5 - Contextual AI bar

**AI-BAR-001** — Use one bold, legible contextual prompt with an access dropdown. Read-only and Take actions are explicit options, not a cycling button. The menu displays the current choice and restores focus when dismissed.

**AI-BAR-002 — Settings → Appearance → Layout owns Chat bar position: Off, Floating or Sidebar. Position is a presentation preference. It does not grant actions, disable auditing or change a conversation's permission scope. Preserve specific app placement contracts: CANVAS-002 keeps Canvas's AI tool in its single options host; this preference must not create a second composer or remove that tool.**

**AI-BAR-003** — The current document's View/Edit mode, layout lock and AI access are separate controls. Take actions cannot override a read-only document or a denied capability. A proposed consequential change still follows the relevant review/approval flow.

**AI-BAR-004** — Keep one contextual AI entry point in an editor. Avoid an additional toolbar AI button that duplicates the bottom/side bar. Spaces' conversation permission control is a different concept and remains available in its composer.

Mockups: [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor); [present-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#present-editor); [settings-layout](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout); [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions).



<a id="t.snkmezredh0m"></a>

# 3. Spaces

<a id="t.snkmezredh0m--h.gcnq9fao70bj"></a>

# 3.1 - Spaces Architecture

**SPACE-001** — A Space is a task environment with its conversation, relevant sources, tools, permissions and outputs. An Assistant is a persistent identity that can appear in a Space; changing the view does not create a copy of that identity or its conversation.

**SPACE-002** — Main and secondary Assistant conversations keep independent thread identity under the same Assistant. A rename or portrait change updates presentation without replacing the canonical identity. A shared conversation remains the same object wherever it opens.

**SPACE-003** — Scope belongs to the selected conversation/Assistant and authorised resources. Team membership does not grant access to another Assistant's private memory or every file in the workspace. Sources and generated outputs retain their own identities and access boundaries.

**SPACE-004** — Runs, proposals, approvals and saved results are distinct. Opening a proposed change or a review page does not approve it. A user can inspect the scope, pending action and resulting artifact before an operation proceeds.

Mockups: [spaces-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat); [spaces-assistants](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistants); [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex); [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files); [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions).





<a id="t.snkmezredh0m--h.dnr7d7rvtzq0"></a>

# 3.2 - Spaces Usability

**SPACE-UI-001** — Make the conversation the primary reading area. Use one compact identity header, readable semibold messages and restrained vertical spacing. Avoid repeating the same title, model, privacy label and context chips across header, sidebar and composer.

**SPACE-UI-002** — Keep conversation navigation, creation and discovery in the sidebar. Secondary navigation groups may collapse. A selected Assistant stays clearly identifiable by the same name and portrait used in the roster and response attribution.

**SPACE-UI-003** — The workspace/source panel is optional. A clear Workspace control opens it and indicates its expanded state. Closing it preserves messages, scroll position, selected conversation and scope. On narrower layouts, it must not squeeze the conversation into an unusable column.

**SPACE-UI-004** — Composer controls provide attachments, input, voice/send as appropriate and a direct conversation permissions entry. Do not show literal HTML resize handles or implementation markup. Wrapping and scroll behaviour must keep the composer usable without overlapping a disclaimer or message.

**SPACE-UI-005** — Message actions appear beside the targeted message or in its menu. Draft commands include Copy, Copy link, Edit user message, Retry assistant response, Save output and Delete where allowed. See CTX-04; editing/retrying must disclose whether it creates a branch or replaces downstream history before that policy is implemented.

**SPACE-UI-006** — Empty, loading, offline, permission denied and failed response states keep the user's draft intact. A collapsed panel or menu never hides a blocking approval or converts a proposal into an applied change.

Mockups: [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex); [spaces-assistant-maya](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-maya); [spaces-assistant-rowan](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-rowan); [spaces-compose](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-compose); [spaces-attachments](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-attachments); [spaces-thinking](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-thinking); [spaces-export-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery).

Acceptance: open each Assistant, toggle Workspace, open conversation permissions and return. The same conversation, authored text, portrait, proposal links and source scope remain available; only secondary presentation changes.





<a id="t.snkmezredh0m--h.g895y7rstbh9"></a>

# 3.3 - Individual Spaces

The subsections below identify the existing Space types and their linked starting views. Detailed workflow requirements beyond the Assistant refinement remain incomplete; sample content should not be read as a separate production promise.



<a id="t.snkmezredh0m--h.m2fd5n7o0xjp"></a>

## Agent

**AGENT-001** — The general Agent conversation supports user input, attachments, visible activity and linked artifacts. A task run exposes its current status and any proposed action requiring review. Separate the user-facing activity summary from private implementation details.

**AGENT-002** — Coordinated subagents have visible task ownership and limited scope. Their presence does not imply access to unrelated private files. Link a generated file to its canonical app/document rather than embedding an untracked competing copy.

Mockups: [spaces-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat); [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-thinking](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-thinking); [spaces-subagents](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagents); [spaces-subagent-detail](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagent-detail); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files).



<a id="t.snkmezredh0m--h.umpfi2n7lxv2"></a>

## Assistants

**ASSIST-001** — The Assistants Space and Home catalogue expose the same persistent identities. My Assistants, Teams and creation/configuration lead to the relevant existing destinations; these are not separate sets of assistants.

**ASSIST-002** — A conversation has one compact Assistant identity header. Keep response attribution and the selected sidebar entry, but remove redundant large headings and repeated composer chips. Display the actual message text without rewriting authored conversation content during a layout change.

**ASSIST-003** — Workspace contains conversations, sources, tools and memory scope. It opens on demand. Configuration and secondary operations live behind labelled controls; conversation permissions stay directly reachable from the composer.

**ASSIST-004** — Main is permanent. Draft menus may rename, pin/unpin or archive a secondary thread; they must not offer a misleading operation that removes Main or deletes the Assistant. Deleting an Assistant is a separate clearly named operation with a stated effect on its conversations and memory.

**ASSIST-005** — Preserve proposals and approval links. An Assistant describing a plan is not proof that a Planner event or file was changed. Context filtering, team visibility and panel collapse affect presentation only.

Mockups: [spaces-assistants](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistants); [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex); [spaces-assistant-maya](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-maya); [spaces-assistant-rowan](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-rowan); [home-assistants](design/9to1_Interface_Atlas_v3.3_Editable.html#home-assistants); [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions).

Context-menu detail: CTX-03 (Assistant/thread), CTX-04 (message), CTX-05 (source). Configuration and persistence beyond the demonstrated controls still need implementation coverage.



<a id="t.snkmezredh0m--h.wdkb4gqz73fd"></a>

## Research

**RESEARCH-001** — Research separates the question, plan, source scope, run state and outputs. Outside-web access is an explicit source-scope choice. A selected budget is not a guaranteed completion time.

**RESEARCH-002** — Keep citations and missing-evidence information with the output. A report opens as a canonical document in the appropriate app. Do not display a finished report or completed progress before that output exists.

Mockups: [spaces-research](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-research); [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files).



<a id="t.snkmezredh0m--h.6hh55jc4tvwu"></a>

## Shopping

**SHOP-001** — Shopping separates requirements, comparison, sourced findings and any later consequential action. Comparison fixtures are not current prices or availability. Purchasing and external account actions require their own explicit, reviewable flow.

Mockup: [spaces-shopping](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-shopping). Product search, current offers, checkout and purchase recovery remain to be specified.



<a id="t.snkmezredh0m--h.ehzrofbr0txo"></a>

## Translate

**TRANSLATE-001** — Translation keeps source content, target language and output distinguishable. Users can revise the request and copy or save the result without losing the original. Locale and formatting decisions must be explicit when they change meaning.

Mockup: [spaces-translate](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-translate). Detailed language selection, file handling and error states remain to be specified.



<a id="t.snkmezredh0m--h.9ep550j3d1do"></a>

# 3.4 - Composer, admission and running work

Status: retained product contract, with the current compact visual treatment. These requirements extend the existing Spaces section; they do not introduce another runtime.

**SPACE-RUN-001** — Preserve the canonical conversation, editable draft, text selection, reading position, attachment references, voice session and active Task/Run when switching Space or hiding a sidebar. Revalidate resources against the destination scope; unavailable attachments remain explainable rather than silently receiving broader access.

**SPACE-RUN-002** — The compact composer expands upward for a nonempty/focused draft. Aa and native Unicode emoji appear only in the expanded focused state. Emoji insertion edits the current selection without sending. Attachment/resource discovery distinguishes Files, Skills, Plugins & Apps, Specialists and Assistants; attaching or mentioning a capability does not execute it.

**SPACE-RUN-003** — Keep draft text until request admission succeeds. Validation, unavailable models, denied scope or a full queue leaves the draft and attachment context recoverable. Assign the admitted request its own identity; displaying a response bubble or pressing a shelf arrow is not admission.

**SPACE-RUN-004** — Idle exposes Send and Voice; an empty disabled Send does not disable Voice. Running work exposes only supported Steer, Stop, Queue and Voice actions. Preserve the existing per-Space Steer/Queue default and documented Alt+Enter inversion. Steer applies to future work; Queue creates a separate pending message. Neither rewrites immutable prior user input or implicitly approves a tool action.

**SPACE-RUN-005** — Stop acknowledges the actual runtime boundary, preserves committed work and does not promise to reverse completed external actions. Paused, waiting for input, awaiting approval, cancelled, completed and failed have different state-specific controls. A reopened view attaches to the existing run without resubmitting it.

**SPACE-RUN-006** — If the user scrolls up, streaming must preserve that reading position. Offer an explicit return-to-latest affordance. Optional Summary shows Activity, Outputs and Sources without becoming a permanent fourth column. Activity contains observed steps and tool/status events; it does not expose private reasoning or invent progress percentages.

**SPACE-RUN-007** — Conversation permissions narrow durable grants. Cancel leaves scope unchanged; Save applies to future requests and cannot retroactively authorise past work. Approval identifies the exact proposed action, canonical target/revision, scope and consequence. Recheck policy at execution; a reviewed plan authorises only its declared scope. Every AI mutation in Data retains its stricter per-action approval requirement.

Acceptance: type and attach, switch Space and return, reject admission, then admit one request. While running, scroll up, steer, queue, stop and reconnect. Verify retained input, one request/run lineage, truthful pending work and no replay of committed effects. Open/cancel/save permissions and confirm the actual effective grant, not merely its icon.

Mockups: [spaces-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat); [spaces-compose](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-compose); [spaces-emoji](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-emoji); [spaces-attachments](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-attachments); [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions); [spaces-thinking](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-thinking); [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [mobile-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-chat); [mobile-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-permissions).

Sources: V3-SPACES-LAY-01–13; V3-SPACES-FEA-01–04; V3-SPACES-PER-01,05–07; V3-PERMISSIONS-FEA-01–04; V3-PERMISSIONS-PER-01; Atlas screen notes for the listed routes; [baseline/retained-pack/modules/spaces.complete.md](baseline/retained-pack/modules/spaces.complete.md); [baseline/retained-pack/modules/permissions.complete.md](baseline/retained-pack/modules/permissions.complete.md).



<a id="t.snkmezredh0m--h.4mky232lqwbj"></a>

# 3.5 - Conversation types, organisation and Space configuration

Status: retained product contract. Several creation/conversion and Builder states are specified in the baseline but have no dedicated current Atlas screen; do not substitute a fictitious implemented preview.

**SPACE-ORG-001** — Chat Type and Chat Location are independent. Types are Normal, Multi-Agent and Temporary; storage locations are Local, Mesh and Cloud. Model execution location is a separate policy-controlled choice. Local conversation storage does not claim local inference.

**SPACE-ORG-002** — Preserve the approved conversions Temporary→Normal, Temporary→Multi-Agent and Normal→Multi-Agent without replaying tools or fabricating earlier participant replies. Temporary first becomes durable local history; promotion to Mesh/Cloud is separate. Temporary chats do not automatically create/reinforce durable memory or transfer remotely. Optional reads of existing memory remain a recorded unresolved policy, not an inferred permission.

**SPACE-ORG-003** — Group and Project organise canonical conversations; they are neither Multi-Agent conversations nor new Spaces. Group adds scoped shared instructions/context; Project additionally has Sources, Rules and resource connections. Group→Project is supported while preserving identity/history/members where safe. Project→Group is rejected. Removing either container does not silently delete its member conversations.

**SPACE-ORG-004** — Context compaction preserves complete original message/output history. Summaries are derived records with source provenance and eligibility. They cannot become a replacement history or confer access to excluded source content.

**SPACE-ORG-005** — Space Override → User Preference → Default governs configuration, bounded by current policy. Locked values show their source. Basic, Advanced and Expert are projections of one SpaceID and Draft/Active revision, not competing configuration stores.

**SPACE-ORG-006** — Basic contains identity, instructions, resources and supported capability categories. Advanced adds Mandatory/Allowed/Blocked/Inherited capability policy, memory, actual model parameters, canonical Assistant/Specialist and Automation references, and theme/sidebar/message overrides. Expert reuses the shared Node Editor for Harness Logic, Model Logic, User Message Logic and nested Files. User Message Logic transforms derived representations while retaining the original message.

**SPACE-ORG-007** — Switching Builder views preserves Expert-only nodes and flags configurations that simpler views cannot express. Preview, Validate and Activate/Create are distinct. Invalid dependencies, typed ports, cycles/budgets or mandatory-but-blocked conflicts cannot replace the valid active graph. Imported packages retain owner/version/trust; folder membership does not activate them.

Acceptance: verify each supported/rejected conversion; remove an organisation container without losing its chats; compact context without deleting history; switch all Builder projections after adding Expert-only logic; reject invalid activation and retain the prior active revision.

Mockups: [spaces-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat); [spaces-manage](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-manage); [spaces-specialists](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-specialists); [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions). Builder and conversion workflows require additional dedicated mockups; their retained behaviour remains binding meanwhile.

Sources: V3-SPACES-FEA-05–08,14–18; V3-SPACES-ENT-01–05,07; V3-SPACES-PER-03–04; V3-SPACES-ACC-02–03,08; [baseline/retained-pack/modules/spaces.complete.md](baseline/retained-pack/modules/spaces.complete.md).



<a id="t.snkmezredh0m--h.ueir6qjn74g"></a>

# 3.6 - Task-specific Spaces and learning entry points

Status: retained contracts that complement the detailed app sections. Domain artifacts remain owned by their apps, with stable references from Spaces.

**SPACE-FLOW-001** — Tasks shows the current user's relevant task scope by default, with explicit All Tasks filtering. Its objective, plan, dependencies, blockers, approvals, sources, outputs and summary are views of the same Action Graph. A queued request is not an active task, and a completed preparation step is not an accepted output.

**SPACE-FLOW-002** — Research begins with a conversation, then exposes a reviewable plan, permitted sources, budget and selected deliverables before Start. Quick/Extended/Deep/Custom represent supported budgets; active execution time excludes waiting/paused periods and is not a completion guarantee. Outside-web access never broadens itself. Findings retain citations and gaps; the final report is a canonical Write artifact.

**SPACE-FLOW-003** — Shopping separates Required from Preferred constraints and strict matches from near matches/alternatives and contextual recommendations. Keep mismatch reasons visible. Products and merchant offers are different entities; currency, price, source and last-check state accompany offers. Save, Compare and Purchase are distinct operations; selection or a recommendation cannot authorise purchase.

**SPACE-FLOW-004** — Translate stays conversational. Language chips sit above the standard composer; each target has its own labelled response bubble. Variants are siblings of one TranslationSet. Refining one language preserves the other variants and the source. Replacing/publishing source content requires the owning app's separate authorised action.

**SPACE-FLOW-005** — Experiences stores canonical world/scene, character, inventory, evidence, objective, flag and checkpoint state. Validate and commit a transition before narrating its outcome. A checkpoint branch preserves both matching conversation and world-state revision and keeps the original timeline. Rewind cannot undo completed external actions. Hidden character knowledge and clues must not leak through side panels or generated UI. Formal contestants, rounds and scoring belong to Play.

**SPACE-FLOW-006** — Study Home, Subject Workspace and Session are distinct active views within the same Space. Opening a subject/session replaces the active work area, not a dashboard laid over a hidden editor. Modes Learn, Practise, Test and Review preserve conversation/topic/source policy and draft. Cards, Forms and Planner open their canonical artifacts, then return to the same Study context. The learning chapter owns evidence, proficiency, ranking and assessment rules; time spent, opening a lesson or flipping a card is not mastery.

Acceptance: Research cannot start an unreviewable plan; Shopping keeps a Required mismatch visible; Translate edits one variant without replacing siblings; Experiences restores a consistent branch; Study opens a real Cards/Forms artifact and returns without duplicate records. Failure in one output does not erase other completed work.

Mockups: [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-research](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-research); [spaces-shopping](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-shopping); [spaces-translate](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-translate); [spaces-experiences](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-experiences); [spaces-study](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-study); [study-subject](design/9to1_Interface_Atlas_v3.3_Editable.html#study-subject); [study-topic](design/9to1_Interface_Atlas_v3.3_Editable.html#study-topic); [study-session](design/9to1_Interface_Atlas_v3.3_Editable.html#study-session).

Sources: V3-SPACES-FEA-09–15,21–22; V3-SPACES-ENT-05; V3-SPACES-ACC-04,10; Atlas Experiences and Translate current screen notes; [baseline/retained-pack/modules/spaces.complete.md](baseline/retained-pack/modules/spaces.complete.md).



<a id="t.snkmezredh0m--h.e5y9rooz8xbx"></a>

# 3.7 - Subagents, generated UI and durable outputs

Status: retained product contract. Current interactive examples demonstrate presentation and selected local transitions, not connected execution.

**SPACE-OUT-001** — A subagent has a stable parent Task/Run relationship, objective, identity/route, delegated inputs, allowed outputs, current state, budget and output references. Its grant is the intersection of parent permission, agent capability and current policy. Enforce delegation depth/concurrency/budget limits; spawning another worker cannot bypass exhaustion or a required review.

**SPACE-OUT-002** — Structured joins retain each child's outcome. Cancel/retry/review is scoped to the selected task; a failed child does not delete completed sibling artifacts. Reconnect to existing runs rather than launch duplicates. Configured Assistants/Teams remain distinct from generic worker delegation; preserve canonical identities when they participate.

**SPACE-OUT-003** — Generated UI uses validated CUI component/action schemas. Model text or arbitrary HTML is not automatically trusted executable UI. Controls bind to authorised source entities and retain selection/input across streaming updates. Invalid generated output leaves previous valid content recoverable and exposes repair.

**SPACE-OUT-004** — An ephemeral proposal is distinct from a promoted durable artifact. A planner slider edits the proposal; Save to Planner opens the exact change review. Live Present/Canvas work shows the owning app's actual scene, revision, selection and responsible collaborator. Open in App retains that artifact/session and history, not a copied representation.

**SPACE-OUT-005** — A finished result requires an owning-app durable artifact receipt. Show name, owning app, canonical ID/revision, native/export type and actual local/sync state. Open resolves the existing artifact; Download requires verified existing bytes. Neither silently repeats generation.

**SPACE-OUT-006** — Export failure is scoped to the chosen export. Retain the saved native source, format/options, target, source revision, error and targeted Retry/Save copy actions. A retry rechecks current grants and idempotency; uncertain external outcomes require reconciliation before repetition. Never show Ready or Download for an absent/incomplete file.

Acceptance: run two children with mixed outcomes, revoke one scope, reconnect and verify no duplicate execution. Change a generated control, reject invalid schema, open the canonical artifact, then force a secondary export failure. Preserve completed work, source identity, revision and valid prior UI.

Mockups: [spaces-subagents](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagents); [spaces-subagent-detail](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagent-detail); [spaces-generative-ui](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generative-ui); [spaces-present-working](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-present-working); [spaces-canvas-working](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-canvas-working); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files); [spaces-export-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery).

Sources: V3-SUBAGENTS-LAY-01–02; V3-SUBAGENTS-FEA-01–03; V3-SUBAGENTS-ENT-01–02; V3-SUBAGENTS-PER-01–02; V3-GENERATIVE-UI-FEA-01–04; V3-GENERATIVE-UI-ENT-01–02; V3-GENERATIVE-UI-PER-01–02; [baseline/retained-pack/modules/subagents.complete.md](baseline/retained-pack/modules/subagents.complete.md); [baseline/retained-pack/modules/generative-ui.complete.md](baseline/retained-pack/modules/generative-ui.complete.md).





<a id="t.rd64lp4djv5h"></a>

# 4. Boards

<a id="t.rd64lp4djv5h--h.7e3tttoi2joo"></a>

# 4.1 - Boards structure and reading surface

**BOARD-001** — A Board contains sections/pages with stable identities. The sidebar owns the board switcher, page tree, undo/redo and save state. Selecting a page opens that same canonical page; it does not duplicate its content.

**BOARD-002** — Use one seamless native page where flow blocks, explicitly positioned content and page ink can coexist. Preserve the authored document while changing surrounding chrome. A design refinement must not rewrite text, callouts, lists, tables, drawings or their order.

**BOARD-003** — Keep the page itself the focus. Remove duplicated board/save labels and a separate decorative mode row. The toolbar contains frequent actions once; repeated Native page, locked-layout and snapping labels belong in the relevant Page menu.

Mockups: [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor); [boards-typing](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-typing); [boards-ink](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-ink); [boards-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-recovery).



<a id="t.rd64lp4djv5h--h.obx9zkn0r4f2"></a>

# 4.2 - Toolbar and secondary controls

**BOARD-UI-001** — Use one compact toolbar with direct Add, Drawing, Edit/View and Collaborate controls. Format opens the original text/paragraph/font/list/alignment controls. Page opens Properties, layout lock, snapping and the original page options.

**BOARD-UI-002** — Format and Page are real disclosures. The active trigger announces its expanded state. Escape and outside click close a popup; keyboard navigation reaches every enabled control and returns focus appropriately. Opening one popup closes an overlapping sibling.

**BOARD-UI-003** — Preserve the original actions behind their new grouping. Reorganising chrome does not remove styling, drawing, properties, page options or collaboration. The redundant toolbar AI entry is removed because the contextual AI bar already owns that entry point.

**BOARD-UI-004** — Name sidebar creation clearly: New page creates a page/section; toolbar Add and block insertion create content within the current page. The board-name control opens file actions; renaming is an explicit command, not a side effect of opening the switcher.

Mockups: [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor); [boards-add](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-add); [boards-insert](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-insert); [boards-file-menu](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-file-menu); [boards-alignment](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-alignment); [boards-list-options](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-list-options); [boards-properties](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-properties).



<a id="t.rd64lp4djv5h--h.v9jjd4cfi3uz"></a>

# 4.3 - Selection, modes and context menus

**BOARD-MODE-001** — View/Edit, layout lock and AI access are separate dimensions. View mode prevents document edits. Layout lock restricts layout manipulation while retaining the established page-ink behaviour. Take actions cannot bypass either permission or read-only restrictions.

**BOARD-SEL-001** — A block grip identifies one block. Click selects; dragging begins only after a deliberate movement threshold. A context menu uses the explicit target/selection and does not silently switch to an unrelated page or block.

**BOARD-SEL-002** — The block menu, overflow button and keyboard menu command expose the same applicable actions. Preserve selection and return focus after cancellation. For multiple blocks, show only actions that apply to the whole selection or state why they are unavailable.

**BOARD-CTX-001** — The existing Paragraph actions view contains Turn into, Colour & style, Move up/down, Move to…, Duplicate, Copy block link and Delete block. Draft additions are Copy, Cut and Comment where appropriate. Preserve those established actions and use the existing menu/insert views as the visual starting point. Read-only, locked, empty-selection and protected-content states determine availability.

**BOARD-CTX-002** — Draft page-tree menu: Open, Open in new tab, Rename, Duplicate, Move, Copy link, Export and Delete. Draft canvas menu: Paste, Insert block, Drawing, Select all, Properties and Layout controls. Exact ordering and app-specific extensions are recorded in CTX-06 to CTX-08.

Mockups: [boards-context](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context); [boards-typing](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-typing); [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor); [boards-ink](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-ink); [boards-properties](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-properties).



<a id="t.rd64lp4djv5h--h.efj52r5nvftr"></a>

# 4.4 - Saving, recovery and acceptance

**BOARD-SAVE-001** — Saved locally, saving, unsaved and save failure are distinct. File actions retain the original board name and location context. Save As creates an editable .9to1board in an allowed location; a failed save does not replace the last recoverable content.

**BOARD-SAVE-002** — Draft deletion is undoable where the data model permits. Show the affected page/block count. Deleting a page with descendants or an unrecoverable external change needs a specific consequence and recovery decision; do not use a generic confirmation for every harmless operation.

Acceptance: open the normal editor and each menu/state view. The authored board content is identical, frequent actions remain direct, formatting/page controls are reachable once, and there is one save-state source in the sidebar.

Acceptance: open Format then Page, cancel with Escape, switch Edit/View, inspect Properties, draw with the layout locked and follow the existing save-recovery route. Record which actions the offline preview demonstrates and which require the production editor.

Mockups: [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor); [boards-context](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context); [boards-file-menu](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-file-menu); [boards-properties](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-properties); [boards-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-recovery); [boards-ink](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-ink).



<a id="t.m73tfqsiwjxb"></a>

# 5. Shared Settings

<a id="t.m73tfqsiwjxb--h.56uyf3qhxnd4"></a>

# 5.1 - Settings navigation and scopes

**SET-001** — Use a three-tier hierarchy: collapsible text Group; icon/text Section; indented Sub-section in the secondary treatment of the selected accent. Group collapse is separate from section navigation.

**SET-002** — The groups are Global, Local, 9to1 Haven and Apps. Global follows the user across 9to1; Local is this device only. Shared app options reference a common setting rather than creating competing app-level copies.

**SET-003** — Use one vertical sidebar scroll container. Sub-sections scroll with their section using proximity snapping. Only headings with children stick; a restrained shadow appears while pinned, and the next heading pushes the previous one out. Preserve sidebar scroll when selecting related destinations. Navigation/remount reveals the active destination and its ancestry. Explicitly collapsing its group is allowed and moves focus to the group heading; reopening or navigating back reveals it again. Search keeps matching ancestry visible.

**SET-004** — Thread guides use a quiet vertical rail and rounded joins. Do not add arrows that look like navigation actions beside every child. There is one selected destination; parents provide ancestry without a second competing selected fill.

**SET-005** — Search matches group, section and sub-section terms and preserves ancestry in the results. Clearing search restores the normal hierarchy, collapse state and pre-search scroll position. Empty results have a direct clear action.

Mockups: [settings-general](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-general); [settings-appearance](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-appearance); [settings-haven-general](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-general); [settings-haven-shortcuts](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-shortcuts); [settings-apps-shared](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-apps-shared).



<a id="t.m73tfqsiwjxb--h.aiv1fzdxgj0w"></a>

## 5.1.1 - Navigation map

The following table is the starting navigation contract. Additional apps reuse the same structure. Apps reuse shared options and add app-specific options where needed; the listed Local and Apps sections do not have a separate sub-section tier.



<table><tbody><tr><td><p><strong>Group</strong></p></td><td><p><strong>Section</strong></p></td><td><p><strong>Sub-sections</strong></p></td></tr><tr><td><p>Global</p></td><td><p>General</p></td><td><p>—</p></td></tr><tr><td><p>Global</p></td><td><p>Appearance</p></td><td><p>Theme; Theme Options; Layout; Accessibility</p></td></tr><tr><td><p>Global</p></td><td><p>AI</p></td><td><p>Personalisation; Plugins/MCPs; Skills; Providers; Voice</p></td></tr><tr><td><p>Global</p></td><td><p>Security &amp; Privacy</p></td><td><p>General; Permissions; Sandbox; Privacy</p></td></tr><tr><td><p>Global</p></td><td><p>Account</p></td><td><p>Profile; Subscriptions; Storage; Mesh</p></td></tr><tr><td><p>Local</p></td><td><p>General</p></td><td><p>—</p></td></tr><tr><td><p>Local</p></td><td><p>Installed Apps</p></td><td><p>—</p></td></tr><tr><td><p>Local</p></td><td><p>Installed Models</p></td><td><p>—</p></td></tr><tr><td><p>Local</p></td><td><p>Notifications</p></td><td><p>—</p></td></tr><tr><td><p>9to1 Haven</p></td><td><p>General</p></td><td><p>New Tab; Tabs &amp; address</p></td></tr><tr><td><p>9to1 Haven</p></td><td><p>Bookmarks</p></td><td><p>Pinned destinations; Shortcut order</p></td></tr><tr><td><p>9to1 Haven</p></td><td><p>History</p></td><td><p>Back &amp; forward; Recent files</p></td></tr><tr><td><p>9to1 Haven</p></td><td><p>Keyboard Shortcuts</p></td><td><p>Navigation; Menus &amp; focus</p></td></tr><tr><td><p>9to1 Haven</p></td><td><p>Performance</p></td><td><p>Services; Models &amp; memory; Storage &amp; cache</p></td></tr><tr><td><p>Apps</p></td><td><p>Shared settings and each 9to1 app</p></td><td><p>—</p></td></tr></tbody></table>





<a id="t.m73tfqsiwjxb--h.remliby9i6g6"></a>

# 5.2 - Content rows and controls

**SET-ROW-001** — Show one option per row: label and only necessary explanatory text on the left; its value/control on the right. Group related rows under plain section headings. Avoid grids of large nested cards for ordinary preferences.

**SET-ROW-002** — The Settings title follows Home's title style. Embedded Settings does not duplicate Haven's top-row utilities. Account, history and browser options live in their shared destinations.

**SET-ROW-003** — A row's control makes its action clear. Toggles change an immediate binary setting; dropdowns select among named values; an action button opens a deliberate operation. Pending or restart/reload-required changes show an explicit apply action and their current state.

**SET-ROW-004** — Preserve the value if saving fails and show the reason next to that setting. A reset states its scope. A global reset does not silently clear device-local preferences, app data or credentials.

Mockups: [settings-profile](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-profile); [settings-theme-options](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options); [settings-layout](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout); [settings-plugins](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-plugins); [settings-notifications](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-notifications).



<a id="t.m73tfqsiwjxb--h.5612xasawprh"></a>

# 5.3 - Appearance and contextual AI

Appearance contains Theme, Theme Options, Layout and Accessibility. Theme and accent changes follow THEME-001 to THEME-005. Accessibility owns contrast, motion and relevant reading/input preferences.

Chat bar position appears under Layout with Off, Floating and Sidebar choices. The former generic AI button is not a duplicate switch. The contextual access menu remains Read-only/Take actions and follows AI-BAR-001 to AI-BAR-004. Apply CANVAS-002 for Canvas's specific AI tool/options host rather than adding a duplicate composer. Data retains APP-DATA-003 approval for every concrete AI mutation, even with Take actions selected.

Mockups: [appearance](design/9to1_Interface_Atlas_v3.3_Editable.html#appearance); [appearance-palette](design/9to1_Interface_Atlas_v3.3_Editable.html#appearance-palette); [settings-theme-options](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options); [settings-layout](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout); [settings-accessibility](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-accessibility).



<a id="t.m73tfqsiwjxb--h.o9rtbjskh8g5"></a>

# 5.4 - Acceptance and remaining work

Acceptance: expand/collapse each group; navigate every section/sub-section; use search; move focus through the sidebar; scroll with mouse and keyboard. Confirm the active section is visible, snapping does not trap scrolling, and the pinned boundary shadow does not obscure text.

Acceptance: with blue selected, revisit Global, Local, Haven and Apps. Section/sub-section emphasis retains blue, content stays one option per row, and the same preference has one source of truth.

Preview coverage: the Atlas provides the hierarchy and existing settings-control projections. Real account/device sync, provider credentials, sandbox enforcement, app installation and settings-save failures require backend implementation and dedicated states.

Draft Settings context menu: Copy setting link; Reset this setting when a default exists; Open related help. Do not expose secret values through Copy. See CTX-09.



<a id="t.i2f02j7wld0f"></a>

# 6. Interactions & References

<a id="t.i2f02j7wld0f--h.f9jarnmvh2hx"></a>

# 6.1 - Interaction contract

The following menu definitions are Draft interaction proposals unless a linked state already demonstrates them. They extend the accepted visual direction without claiming the offline Atlas implements every command.

**CTX-BASE-001** — Right-click, the keyboard ContextMenu key/Shift+F10 and the visible overflow/menu trigger target the same selected object. Opening a context menu must not execute an action. Keep native text editing menus in ordinary text fields unless a richer editor deliberately supplies an equivalent.

**CTX-BASE-002** — Position the menu within the available viewport and maintain a clear target. Up/Down move through enabled items; Enter activates; Escape closes and restores focus. Right opens and Left closes a submenu where applicable. Tab leaves the menu without performing its highlighted command.

**CTX-BASE-003** — Keep destructive actions separated. Show selection-specific wording and counts. Disable an unavailable command with a useful reason; do not fake success. Copy link must refer to a stable object identity and avoid including secrets or temporary authentication data.

**CTX-BASE-004** — State changes use the same action implementation whether invoked from a toolbar, context menu or shortcut. Undo applies to the actual mutation, not merely to the menu closing. Route-only previews must identify any operation that is illustrative.



<a id="t.i2f02j7wld0f--h.npb1u0b49l4v"></a>

# 6.2 - Right-click and overflow matrix



<table><tbody><tr><td><p><strong>ID / target</strong></p></td><td><p><strong>Commands</strong></p></td><td><p><strong>State and recovery</strong></p></td></tr><tr><td><p>CTX-01 · Haven tab</p></td><td><p>New tab; Reload; Duplicate; Pin/Unpin; Mute/Unmute; Move to window; Close; Close others; Close to right; Reopen closed</p></td><td><p>Draft. Disable irrelevant audio/window/closed-tab actions. Preserve unsaved work and select an adjacent tab after closing. References: <a href="#t.8lnv8r5vlfea--h.lbrybk6tio42"><u>HAV-TAB-001</u></a>–004; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab"><u>host-newtab</u></a>.</p></td></tr><tr><td><p>CTX-02 · Home shortcut/widget</p></td><td><p>Shortcut: Open; Edit; Move; Remove. Widget: Configure; Width; Move; Remove.</p></td><td><p>Draft menu. Move/remove and compact/wide reuse existing editor actions; Edit shortcut and Configure are additional proposed flows. Remove affects layout only. Offer undo; keep source content. References: <a href="#t.8lnv8r5vlfea--h.mz808bphuj0r"><u>HOME-002</u></a>–006; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-customise"><u>host-newtab-customise</u></a>.</p></td></tr><tr><td><p>CTX-03 · Assistant / thread</p></td><td><p>Assistant: Open Main; Configure; Pin/Unpin. Secondary thread: Open; Rename; Pin/Unpin; Archive.</p></td><td><p>Draft. Main cannot be deleted/archived as an ordinary secondary thread. Assistant deletion is a separate operation. References: <a href="#t.snkmezredh0m--h.umpfi2n7lxv2"><u>ASSIST-001</u></a>–005; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistants"><u>spaces-assistants</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex"><u>spaces-assistant-alex</u></a>.</p></td></tr><tr><td><p>CTX-04 · Conversation message</p></td><td><p>Copy; Copy link; Edit user message; Retry assistant response; Save output; Delete where allowed.</p></td><td><p>Draft. Role and permissions govern availability. Branch/replacement policy must be specified before Edit/Retry can change downstream history. Reference: <a href="#t.snkmezredh0m--h.dnr7d7rvtzq0"><u>SPACE-UI-005</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat"><u>spaces-chat</u></a>.</p></td></tr><tr><td><p>CTX-05 · Attached source</p></td><td><p>Open source; Copy source link; Details; Remove from conversation scope.</p></td><td><p>Draft. Remove unlinks this scope, not the original file. Permission details stay visible. Reference: <a href="#t.snkmezredh0m--h.gcnq9fao70bj"><u>SPACE-003</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions"><u>spaces-permissions</u></a>.</p></td></tr><tr><td><p>CTX-06 · Boards block</p></td><td><p>Existing view: Turn into; Colour &amp; style; Move up/down; Move to…; Duplicate; Copy block link; Delete block. Draft additions: Copy; Cut; Comment.</p></td><td><p>Existing block-menu view, expanded draft action contract. Preserve explicit selection; constrain read-only/locked operations; undo mutation. References: <a href="#t.rd64lp4djv5h--h.v9jjd4cfi3uz"><u>BOARD-SEL-001</u></a>–002; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context"><u>boards-context</u></a>.</p></td></tr><tr><td><p>CTX-07 · Boards page tree</p></td><td><p>Open; Open in new tab; Rename; Duplicate; Move; Copy link; Export; Delete.</p></td><td><p>Draft. Explain descendant deletion and recovery. Do not navigate accidentally on menu open. References: <a href="#t.rd64lp4djv5h--h.v9jjd4cfi3uz"><u>BOARD-CTX-002</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor"><u>boards-editor</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-file-menu"><u>boards-file-menu</u></a>.</p></td></tr><tr><td><p>CTX-08 · Boards page/canvas</p></td><td><p>Paste; Insert block; Drawing; Select all; Properties; Layout controls.</p></td><td><p>Draft. Respect Edit/View, lock, selection and clipboard availability. References: <a href="#t.rd64lp4djv5h--h.v9jjd4cfi3uz"><u>BOARD-MODE-001</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-insert"><u>boards-insert</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-ink"><u>boards-ink</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-properties"><u>boards-properties</u></a>.</p></td></tr><tr><td><p>CTX-09 · Settings option</p></td><td><p>Copy setting link; Reset this setting; Related help.</p></td><td><p>Draft. Reset only this value when a defined default exists; no secret copying. References: <a href="#t.m73tfqsiwjxb--h.remliby9i6g6"><u>SET-ROW-003</u></a>–004; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout"><u>settings-layout</u></a>.</p></td></tr><tr><td><p>CTX-10 · Model row</p></td><td><p>Select; Details; Manage local model / provider as applicable.</p></td><td><p>Draft. No implied install, download or provider connection from selecting a preview row. Reference: <a href="#t.8lnv8r5vlfea--h.xrco6lv5bzqg"><u>MODEL-002</u></a>–006; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-model-details"><u>home-model-details</u></a>.</p></td></tr><tr><td><p>CTX-11 · History / bookmark</p></td><td><p>Open; Open in new tab; Copy address; Bookmark / Edit bookmark; Remove entry.</p></td><td><p>Draft. Removing history/bookmark changes that record only. Separate clearing all history from one-item removal. References: <a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>BROWSE-005</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-history"><u>host-browser-history</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-bookmarks"><u>host-browser-bookmarks</u></a>.</p></td></tr><tr><td><p>CTX-12 · Inspector node</p></td><td><p>Copy identifier/path; Reveal corresponding element; Inspect properties.</p></td><td><p>Draft. Keep HTML/CUI representation clear. Current console is query-only and read-only. References: <a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>BROWSE-004</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html"><u>host-devtools-html</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui"><u>host-devtools-cui</u></a>.</p></td></tr></tbody></table>





<a id="t.i2f02j7wld0f--h.3bacpidlqaeu"></a>

# 6.3 - Mockup reference index

[Open the editable Interface Atlas](design/9to1_Interface_Atlas_v3.3_Editable.html). Search a Screen ID to jump to the relevant example, then use Build notes to return to its linked specification section. Labels below identify exact screens; file links open the editable Atlas rather than a hosted application.



<table><tbody><tr><td><p><strong>Specification</strong></p></td><td><p><strong>Screen IDs</strong></p></td><td><p><strong>Starting coverage</strong></p></td></tr><tr><td><p><a href="#t.0--h.l8a5r4xc2at9"><u>1. Overview / reference rules</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-apps"><u>home-apps</u></a></p></td><td><p>Shared rules and navigation context.</p></td></tr><tr><td><p><a href="#t.8lnv8r5vlfea--h.gcnq9fao70bj"><u>2.1 CUI / Themes</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#appearance"><u>appearance</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#appearance-palette"><u>appearance-palette</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options"><u>settings-theme-options</u></a></p></td><td><p>Chosen accent and shared control treatments.</p></td></tr><tr><td><p><a href="#t.8lnv8r5vlfea--h.lbrybk6tio42"><u>2.3.1 Shell / tabs</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab"><u>host-newtab</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-apps"><u>home-apps</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-models"><u>home-models</u></a></p></td><td><p>Home navigation close control; full tab-menu proposals remain draft.</p></td></tr><tr><td><p><a href="#t.8lnv8r5vlfea--h.mz808bphuj0r"><u>2.3.2 Home / New tab</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab"><u>host-newtab</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-customise"><u>host-newtab-customise</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-widgets"><u>host-newtab-widgets</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard"><u>home-dashboard</u></a></p></td><td><p>Shared start page and visual editor; Home data remains fixtures.</p></td></tr><tr><td><p><a href="#t.8lnv8r5vlfea--h.xrco6lv5bzqg"><u>2.3.3 Model picker</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker"><u>host-model-picker</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-models"><u>home-models</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-models-installed"><u>home-models-installed</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-model-details"><u>home-model-details</u></a></p></td><td><p>Compact model rows and pending context apply; no real model reload.</p></td></tr><tr><td><p><a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>2.3.4 Browser tools</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser"><u>host-browser</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-history"><u>host-browser-history</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-bookmarks"><u>host-browser-bookmarks</u></a></p></td><td><p>Browser/site preview controls and local views.</p></td></tr><tr><td><p><a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>2.3.4 Developer tools</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html"><u>host-devtools-html</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui"><u>host-devtools-cui</u></a></p></td><td><p>Read-only inspector examples; query-only console.</p></td></tr><tr><td><p><a href="#t.8lnv8r5vlfea--h.oa4ewpkbzo55"><u>2.3.5 Contextual AI</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor"><u>boards-editor</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-editor"><u>present-editor</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout"><u>settings-layout</u></a></p></td><td><p>Read-only/Take actions dropdown and position choices.</p></td></tr><tr><td><p><a href="#t.snkmezredh0m--h.gcnq9fao70bj"><u>3.1 Spaces architecture</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat"><u>spaces-chat</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running"><u>spaces-running</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files"><u>spaces-generated-files</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions"><u>spaces-permissions</u></a></p></td><td><p>Conversation, proposal and scope examples.</p></td></tr><tr><td><p><a href="#t.snkmezredh0m--h.umpfi2n7lxv2"><u>3.3 Assistants</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistants"><u>spaces-assistants</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex"><u>spaces-assistant-alex</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-maya"><u>spaces-assistant-maya</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-rowan"><u>spaces-assistant-rowan</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-assistants"><u>home-assistants</u></a></p></td><td><p>Conversation refinement and optional workspace; draft contextual actions remain.</p></td></tr><tr><td><p><a href="#t.snkmezredh0m--h.g895y7rstbh9"><u>3.3 Other Spaces</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-research"><u>spaces-research</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-shopping"><u>spaces-shopping</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-translate"><u>spaces-translate</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagents"><u>spaces-subagents</u></a></p></td><td><p>Initial workflow references; detailed specification incomplete.</p></td></tr><tr><td><p><a href="#t.rd64lp4djv5h--h.obx9zkn0r4f2"><u>4.1–4.2 Boards</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor"><u>boards-editor</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-typing"><u>boards-typing</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-ink"><u>boards-ink</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-add"><u>boards-add</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-insert"><u>boards-insert</u></a></p></td><td><p>Compact toolbar with preserved authored page.</p></td></tr><tr><td><p><a href="#t.rd64lp4djv5h--h.v9jjd4cfi3uz"><u>4.3 Boards menus</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context"><u>boards-context</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-file-menu"><u>boards-file-menu</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-alignment"><u>boards-alignment</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-list-options"><u>boards-list-options</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-properties"><u>boards-properties</u></a></p></td><td><p>Existing visual states plus planned page-tree/canvas interactions.</p></td></tr><tr><td><p><a href="#t.rd64lp4djv5h--h.efj52r5nvftr"><u>4.4 Save / recovery</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-recovery"><u>boards-recovery</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery"><u>spaces-export-recovery</u></a></p></td><td><p>Recovery examples; production durability unverified.</p></td></tr><tr><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5. Settings</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-general"><u>settings-general</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-appearance"><u>settings-appearance</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-general"><u>settings-haven-general</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-apps-shared"><u>settings-apps-shared</u></a></p></td><td><p>Three-tier taxonomy and one-option rows.</p></td></tr><tr><td><p><a href="#t.m73tfqsiwjxb--h.5612xasawprh"><u>5. Appearance / Layout</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options"><u>settings-theme-options</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout"><u>settings-layout</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-accessibility"><u>settings-accessibility</u></a></p></td><td><p>Theme, chat position and accessibility destinations.</p></td></tr><tr><td><p><a href="#t.i2f02j7wld0f--h.f9jarnmvh2hx"><u>6. Interaction states</u></a></p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-customise"><u>host-newtab-customise</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex"><u>spaces-assistant-alex</u></a>; <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context"><u>boards-context</u></a></p></td><td><p>Cross-surface acceptance and unfinished context-menu coverage.</p></td></tr></tbody></table>





<a id="t.i2f02j7wld0f--h.ehzycslzw460"></a>

# 6.4 - State coverage required for each feature

**STATE-001** — Default and selected: show the current value, active target and one primary selection. Reopening a surface restores the intended state rather than resetting it accidentally.

**STATE-002** — Hover, focus and pressed: make all actionable controls discoverable by keyboard and pointer. A control cannot depend on an invisible hover-only X or an unlabelled icon.

**STATE-003** — Expanded and dismissed: opening a menu/panel reveals its actual controls; cancelling leaves values unchanged and restores focus. Overlapping popups coordinate their lifecycle.

**STATE-004** — Pending and applying: keep the draft value distinct from the applied value. Commit only through the explicit apply action where required. Show progress only when an operation exists.

**STATE-005** — Empty, offline, denied and failed: keep recoverable input, explain the relevant next action and avoid fictional success. Remove stale capability claims when a source or permission is absent.

**STATE-006** — Saved/reopened and responsive: cover restored authored screens as well as newly generated markup. Verify long labels, large text, high contrast, reduced motion and narrow layouts. A structural check alone does not establish rendered layout quality.



<a id="t.i2f02j7wld0f--h.grvg4qfnokfv"></a>

# 6.5 - Starting verification cases

QA-01 — Haven close control: New tab → Apps → Models → Assistants; the active tab retains X throughout. Close with pointer and keyboard. Repeat from a saved screen and when only one tab remains.

QA-02 — New tab: switch light/dark/high contrast, scroll content, open Customise, drag a widget, use move controls, change width, remove/undo and reopen. Preserve authored widget content and keep the scrollbar visually integrated.

QA-03 — Model picker: centre only its role tabs; expand each heading; change context; dismiss and return; apply explicitly; test invalid input and a reload failure in the production implementation. No other interaction silently applies the pending value.

QA-04 — Settings: search a Haven sub-section, clear search, collapse its group, reopen it and move focus. Confirm hierarchy, selected accent and one-row options.

QA-05 — Assistants: open Alex/Maya/Rowan, toggle Workspace, open secondary options and conversation permissions, follow a proposal and return. Preserve the original conversation, scope and authored messages.

QA-06 — Boards: compare authored content before/after chrome changes; open Format/Page; check New page vs Add content; inspect block menu, ink, View/Edit and save recovery. No duplicate AI or save-state control remains.

QA-07 — Context menu: target the last item near a viewport edge; open by right-click and keyboard; cancel; activate a disabled item; test multiple selection; perform and undo a reversible action. Verify exact target and focus restoration.

QA-08 — Reference maintenance: every linked Screen ID exists; every Build notes specification link points to the right tab/heading; legacy routes still resolve; each draft menu is clearly distinguished from implemented preview behaviour.



<a id="t.i2f02j7wld0f--h.t9swna4myjhf"></a>

# 6.6 - Next specification work

Add dedicated mockups for the unimplemented context-menu states, including tab context-menu and reopening states, Home shortcut/widget actions, Assistant/thread/message menus and Boards page-tree/canvas menus. Reuse the existing Boards block/file menu views as the starting evidence.

Use the framework, runtime, interaction and app sections for the recovered production contracts. Complete the listed missing mockups and verify the corresponding real implementation states; do not reinterpret the current prototype as proof of backend completion.



<a id="t.pa5ml7516cye"></a>

# 7. Runtime, Data & Trust

<a id="t.pa5ml7516cye--h.pw1omlnpr6yj"></a>

# 7.1 - Architecture, authority and shared ownership

Retained baseline: the v3.1 platform, CUI, Dulche, persistence and permission contracts remain authoritative where the 18 November owner instructions do not replace them. The current Atlas specifies presentation and selected interactions; it does not prove that a production service is implemented. The older technical chapters remain the source for exact grammar, API and transport definitions.



**ARCH-001** — Maintain one app registry with stable AppID, ArtifactID, TabID, request, task, run and revision identities. Home, Haven, Files, Spaces and embeds must resolve the same owning app and canonical artifact. A new visual representation must not create another state store, model catalogue, permission broker or document engine.



**ARCH-002** — Native apps expose typed create/open/edit/import/export/navigation/event contracts. Preserve the retained donor engines and their functional parity. Manual local work remains available without sign-in, an AI route, a Files provider or a running browser engine unless that specific operation requires one.



**ARCH-003** — Keep view state, unsaved authoring, saved revisions, active runtime work and published output distinct. Closing a tab or Home window does not implicitly stop a background TaskRun or delete an artifact. Restoring a view reconnects to its existing state without replaying submitted commands.



Acceptance: open one Board through Home, Files and a Space; all three references resolve the same identity and revision. Restart the host while a task runs and recover the existing run. Disable AI and confirm ordinary editing and local saving still work.

Mockups: [home-apps](design/9to1_Interface_Atlas_v3.3_Editable.html#home-apps); files; [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files); [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor); [settings-advanced](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-advanced).

Sources: [baseline/retained-pack/modules/platform.complete.md](baseline/retained-pack/modules/platform.complete.md); [baseline/retained-pack/modules/productivity-engine.complete.md](baseline/retained-pack/modules/productivity-engine.complete.md); Atlas v12: [settings-advanced](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-advanced) notes.



<a id="t.pa5ml7516cye--h.n97k11359seg"></a>

# 7.2 - CUI public interface contract

Retained baseline: CUI is the single public UI system. Avalonia and retained donor engines are internal implementation details; raw AXAML and third-party controls do not become competing public interface languages. Detailed syntax belongs to technical/cui-03.md and ownership/conformance to technical/cui-04.md.



**CUI-NEW-001** — Public components use typed primitives, containers and templates with stable identity, semantic role, accessible name, state/value and declared actions. Preserve the source grammar, escaping, versioning and extension contracts when implementing the visual refresh. The Atlas DOM and JavaScript are prototype code, not a substitute production language specification.



**CUI-NEW-002** — Every shared control defines normal, hover, pressed, selected, disabled, focused, loading, validation and destructive states. Use predictable focus order, keyboard activation, a visible focus indicator and viewport-aware menus. One primary scrolling region contains the page; independently scrolling panels must have a clear bounded purpose.



**CUI-NEW-003** — Themes change material, typography, density and presentation without changing app topology, permissions or authored content. Validate custom tokens, contrast and focus treatment. An incompatible theme falls back to a usable baseline while preserving its identity for repair. Reduced motion removes ornamental movement; transparency has a readable fallback.



Current owner override: primary, secondary, tertiary and ghost are emphasis levels derived from the chosen accent. Blue selection must not produce purple shared controls. Professional uses adequately rounded controls and stronger navigation/message text. Haven tabs remain left aligned; only Main/Voice/Vision picker labels are centred. The old typography/theme references do not override these corrections.



**CUI-NEW-004** — Theme or accent changes do not alter document revisions, Assistant identity, model selection or action scope. Keep authored artwork and native emoji independent of interface tint. CUI inspectors expose component identity, properties, actions, theme resolution and accessibility state; inspection does not grant action authority.



Acceptance: switch accent and theme across Home, Settings, Spaces and Boards; shared controls update consistently while content remains unchanged. Validate keyboard access, reduced motion, invalid custom tokens and a missing theme package. Inspect one component through CUI without changing its permissions or persisted document.

Mockups: appearance; [appearance-palette](design/9to1_Interface_Atlas_v3.3_Editable.html#appearance-palette); [custom-theme](design/9to1_Interface_Atlas_v3.3_Editable.html#custom-theme); components; states; [host-devtools-cui](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui); [host-devtools-html](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html); [settings-accessibility](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-accessibility).

Sources: [baseline/retained-pack/modules/cui.complete.md](baseline/retained-pack/modules/cui.complete.md); Owner instructions: 18 November accent, type, rounding and tab alignment.



<a id="t.pa5ml7516cye--h.njwxs8z2o0q0"></a>

# 7.3 - Haven host and browser isolation

Retained baseline: Browse/Haven hosts genuine native app surfaces and browser engines. The original Gecko/Blink, profile, download and isolation responsibilities remain; the visual refresh does not replace them with an HTML imitation.



**ARCH-004** — Persist each tab's identity, type, canonical artifact or URL, engine/profile, history and permitted view state. Keep browser sessions separate from native app grants and context. A web page cannot acquire internal capabilities by imitating app chrome. Tab restoration restores state, not previous external effects.



**ARCH-005** — Native tabs navigate using owning-app routes and canonical identities; web tabs use actual URL, origin, engine and profile state. Browser Tools offers operational engine, developer-tools, history, bookmarks, reload and relevant browser controls. Manage Page exposes actual origin/protocol security, site permissions and applicable storage details; a mockup status is never evidence of a secure live connection.



Current owner override: Haven owns the outer tabs, address/navigation and shared utilities. Remove duplicate app-level chrome. Home and New Tab are one destination with New Tab beneath Explore, and no fixed Home tab. Selecting a sidebar destination preserves the tab's close affordance. Closing the last ordinary tab creates the shared New Tab destination; the v12 close interaction is currently implemented only for the Home/New Tab destination family.



**ARCH-006** — Closing an unsaved native view follows the artifact's real save/recovery policy. Engine crashes, failed navigation, denied origins and unavailable artifacts retain a recoverable tab rather than discarding unrelated work. Profile and site permission changes must address the selected origin/profile, not every open tab.



Acceptance: switch a Home tab to each sidebar destination and close it; restore an edited native tab without duplicate task submission. Test web-origin isolation, separate profiles, a crashed engine, denied page permissions and a native artifact that becomes unavailable. Verify both HTML and CUI inspector references identify the selected surface accurately.

Mockups: [host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab); [home-dashboard](design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard); [home-explore](design/9to1_Interface_Atlas_v3.3_Editable.html#home-explore); [host-browser](design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser); [host-browser-history](design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-history); [host-browser-bookmarks](design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-bookmarks); [host-devtools-html](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html); [host-devtools-cui](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui).

Sources: [baseline/retained-pack/modules/browse.complete.md](baseline/retained-pack/modules/browse.complete.md); [baseline/retained-pack/modules/platform.complete.md](baseline/retained-pack/modules/platform.complete.md); Owner instructions: 18 November shared chrome and Home/New Tab; Atlas v12: [host-newtab](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab) and [home-dashboard](design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard) notes.



<a id="t.pa5ml7516cye--h.rp1e87nyp3fx"></a>

# 7.4 - Dulche runtime and request lifecycle

Retained baseline: Dulche is the Home-managed, llama.cpp-derived shared model framework and AI/runtime broker. It owns capability-aware model routing, request execution, durable TaskRuns, tool actions, memory integration and subagent coordination. Home exposes shared service health and repair; individual apps must not introduce competing private brokers.



**RUNTIME-001** — Expose actual Starting, Ready, Degraded, Restarting and Stopped service states. A failed optional AI component leaves manual local editing available. Preserve the retained API, transport, multi-engine and embedded-service contracts; presentation changes do not authorise replacing the inference architecture.



**RUNTIME-002** — Record request admission, active work, waiting for input/approval, paused, cancelled, completed and failed states. Persist request, TaskID, RunID, ActionGraphID, admitted configuration, effective policy/version, route, status events and output references. A draft, queued admission and committed action are different states.



**RUNTIME-003** — Separate route fallback, tool retry and external-effect retry. Recovery attaches to the existing durable run/checkpoint. Retry only safe or idempotent operations; expose uncertain external outcomes for reconciliation. Never repeat a sent message, payment or file mutation simply because a view or network connection reopens.



**RUNTIME-004** — Steer, queue, pause, stop and resume reflect real runtime capability. Stop acknowledges the actual boundary reached; it prevents future work without pretending to undo committed effects. Preserve completed artifacts and resumable partial work. Surface real elapsed/activity metrics and high-level task/tool events; do not fabricate completion percentages or expose private internal reasoning.



**RUNTIME-005** — A child task inherits only the intersection of its parent's current grant, delegated scope, capability and policy. Persist the parent/child relationship, budget, context, outputs and join receipts. Propagate revocation, cancellation and budget changes. Child failure/retry must not erase completed siblings or evade limits by spawning replacements.



Acceptance: restart a runtime during work, while awaiting approval and after an external action with uncertain acknowledgment. Confirm no duplicate execution, honest recovery status and retained outputs. Cancel a parent with mixed successful/blocked children; each child reaches the correct state without lost sibling artifacts.

Mockups: [settings-advanced](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-advanced); [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-thinking](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-thinking); [spaces-subagents](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagents); [spaces-subagent-detail](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagent-detail); automations.

Sources: [baseline/retained-pack/modules/dulche.complete.md](baseline/retained-pack/modules/dulche.complete.md); [baseline/retained-pack/modules/subagents.complete.md](baseline/retained-pack/modules/subagents.complete.md).



<a id="t.pa5ml7516cye--h.jh44mj4ez0pc"></a>

# 7.5 - Identity, account and resource authority

Retained baseline: Home owns account identity and subscriptions; local manual use does not require a cloud account. CAKE ID is canonical and remains stable when display name, username or profile image changes.



**DATA-001** — Only name and username are required profile fields. Optional icon, pronouns and job are clearable. Authorise privileged identities, organisation membership and resources using canonical authenticated identity, never a friendly name. Keep account sessions, secure credentials, entitlement revisions and resource ledgers separate from editable profile fields.



**DATA-002** — Allocation, price, storage, AI usage and plan thresholds come from the authoritative service. Missing values remain unavailable. Atlas figures such as £10, 35,000 AI Dust and 250 GB are scenario data, not production entitlements or formulas derived from a plan label. Account storage and device disk are separate resources.



**DATA-003** — Retain the supplied Cloudflare frontend/Workers/D1/Durable Objects/queue and B2 storage responsibilities, authentication/PKCE and Stripe verification/idempotency contracts. Preserve reserve/consume/settle accounting and organisation pool/ceiling rules. The interface revision neither deploys these services nor demonstrates their readiness.



**DATA-004** — Sign-out and session revocation stop the affected future authenticated operations without deleting local artifacts or unsaved work. Expired authentication exposes reconnect/retry, preserving drafts. Publishing, billing changes, remote storage and execution require their own applicable authorisation; possession of an account is not blanket consent.



Acceptance: edit every optional field, change a display name and revoke one session; identity and unaffected documents remain stable. Replay a billing event and concurrent resource reservations without duplicate charging. Show unavailable allocation data honestly and continue local editing while offline or signed out.

Mockups: account; [settings-account](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-account); [settings-profile](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-profile); [settings-subscriptions](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-subscriptions); onboarding; [settings-account-storage](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-account-storage).

Sources: [baseline/retained-pack/modules/identity.complete.md](baseline/retained-pack/modules/identity.complete.md); [baseline/retained-pack/modules/cloud-services.complete.md](baseline/retained-pack/modules/cloud-services.complete.md); Atlas v12: [settings-account](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-account) notes.



<a id="t.pa5ml7516cye--h.jusr8dyt2o58"></a>

# 7.6 - Canonical artifacts, transactions and recovery

Retained baseline: each owning app maintains canonical artifact identity and revisions. Reference, imported copy, move, export and fork have different consequences and must remain explicit.



**DATA-005** — Expose actual local draft, changes pending, saving locally, saved locally, sync pending, synced, failed, read-only and conflict states. A rendered preview is not proof of durable saving; queued remote work is not confirmed success. Recover drafts/checkpoints after interruption.



**DATA-006** — All edits use typed commands with explicit target, expected revision, validation and transaction boundaries. Preserve undo/redo across supported app views. A fork receives a new identity with ancestry; an embed or collection reference resolves the existing artifact. Removing a reference is not deleting its source.



**DATA-007** — Exports identify source revision, format, range/options, destination and outcome. Offer Download only when the expected bytes exist and are verified. Export failure preserves the native source and offers retry from the known revision without regenerating unrelated completed work.



**DATA-008** — Do not add arbitrary global word, character, page, slide, object or byte caps merely to simplify rendering or saving. State real format, security, quota and device constraints explicitly; never silently truncate. Virtualise visible content, retain searchable/exportable unrendered content, use incremental updates and bounded caches, and stream/resume/cancel large operations with truthful progress.



**DATA-009** — Indexes, thumbnails and caches are derived. Repair or cache clearing cannot discard the only canonical file, Den record, authorised offline replica or unsaved change. Keep authored state separate from selection, scroll position, runtime status and configuration. Deletion and replica cleanup expose pending or failed states until the relevant stores confirm them.



Acceptance: edit and crash before save completion, recover a draft, then fail export and retry from the same source revision. Exercise a large document without hidden truncation. Clear caches and reopen the same artifact, confirming content, identity, grants and undo/checkpoints remain valid where the source contract promises them.

Mockups: states; [boards-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-recovery); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files); [spaces-export-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery); files; [settings-storage](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-storage); [write-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#write-editor); data.

Sources: [baseline/retained-pack/modules/persistence.complete.md](baseline/retained-pack/modules/persistence.complete.md); [baseline/retained-pack/modules/platform.complete.md](baseline/retained-pack/modules/platform.complete.md); [baseline/retained-pack/modules/generative-ui.complete.md](baseline/retained-pack/modules/generative-ui.complete.md).



<a id="t.pa5ml7516cye--h.f3jjjlqsl0qs"></a>

# 7.7 - Files, Library and organisational views

Retained baseline: Files is the resource browser/file manager. Home Library indexes artifacts across apps; it is not a second storage authority. An app remains usable without requiring every ordinary document to be imported into Files.



**DATA-010** — Preserve FileID, provider/resource identity, revision, metadata and operation receipts. List/grid, preview and Open in App resolve the same selected item and owning artifact. Recheck permission on preview, open, export and move; a cached listing does not confer access.



**DATA-011** — Support the retained Folder, Project, Sheet, Tree, Smart, Timeline, Pipeline, Catalogue, Stash and Matrix organisational views, with ordinary contents fallback. Their configuration, membership, order, query, axes and stages have stable identities. Membership normally references the source without copying or moving it. Smart queries remain scope-limited; Pipeline movement observes real gates.



**DATA-012** — Expose provider-supported create, import, rename, move, copy, delete and restore operations accurately. Show actual availability, progress, conflicts and partial batch outcomes. Move destinations require capability checks; collision choices such as Replace, Keep both and Skip must state their target and consequence. Removing collection membership is a separate action from deleting a file.



Acceptance: add the same Board to multiple collections and open it through Library and Files without duplicate artifacts. Remove one membership without deleting the Board. Test denied previews, conflicting moves, interrupted remote operations, all ten view configurations and their normal-contents fallback after reopen.

Mockups: [home-library](design/9to1_Interface_Atlas_v3.3_Editable.html#home-library); files; [files-grid](design/9to1_Interface_Atlas_v3.3_Editable.html#files-grid); [files-context](design/9to1_Interface_Atlas_v3.3_Editable.html#files-context); [files-organisation](design/9to1_Interface_Atlas_v3.3_Editable.html#files-organisation).

Sources: [baseline/retained-pack/modules/files.complete.md](baseline/retained-pack/modules/files.complete.md); Atlas v12: [home-library](design/9to1_Interface_Atlas_v3.3_Editable.html#home-library) and files notes.



<a id="t.pa5ml7516cye--h.8sb8vkcpiy4v"></a>

# 7.8 - Mesh, replication and remote execution

Retained baseline: Mesh coordinates explicitly authorised devices and resources. Storage placement, replication and where a model executes are separate dimensions.



**DATA-013** — Pair using stable authenticated DeviceID and trust records. A reachable or paired device does not expose every file, model, runtime or secret. Personal and organisation scopes remain separate. Home and Settings share the same roster, grants, capabilities and connection state.



**DATA-014** — Persist scoped replica manifests, revisions, transfer states and TaskRun references. Distinguish durable ownership/grants from transient connectivity. Offline copies expose their last confirmed state; do not present an unreachable peer as a current authoritative copy. Temporary conversations must not be silently transferred to Mesh or cloud.



**DATA-015** — Preserve independently edited revisions when conflicts arise. Reconnect reconciles pending transfers without replaying external effects. Revoke stops future access and reconciles pending work; it cannot truthfully claim to erase an offline peer before confirmation. Local, Mesh and cloud conversation placement remain distinct from inference locality.



**DATA-016** — Remote capability invocation checks both current caller scope and target-side permission/runtime capability. Pairing is not arbitrary execution or remote-desktop consent. Remote viewing, input control, clipboard, file and device forwarding have separate grants. Closing a viewer does not shut down the remote machine.



Acceptance: pair two devices, share one namespace, then deny another. Disconnect during replication, edit both revisions and reconcile visibly without loss. Revoke an offline peer and report pending confirmation. Run a permitted remote model while the conversation remains local and verify that placement and execution labels differ accurately.

Mockups: [home-mesh](design/9to1_Interface_Atlas_v3.3_Editable.html#home-mesh); [settings-mesh](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-mesh); [settings-storage](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-storage); [mini-computer](design/9to1_Interface_Atlas_v3.3_Editable.html#mini-computer); [spaces-manage](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-manage).

Sources: [baseline/retained-pack/modules/mesh.complete.md](baseline/retained-pack/modules/mesh.complete.md); [baseline/retained-pack/modules/persistence.complete.md](baseline/retained-pack/modules/persistence.complete.md); Atlas v12: [settings-storage](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-storage), [settings-mesh](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-mesh) notes.



<a id="t.pa5ml7516cye--h.fthskgy4ykf8"></a>

# 7.9 - Permissions, action approval and privacy boundaries

Retained baseline: permission is the intersection of platform policy, account/organisation rules, app grants, current resource/conversation scope and provider capabilities. Displaying an enabled control or model-generated instruction is never an execution grant.



**ARCH-007** — Keep document Edit/Suggest/View, AI Read-only/Take actions, conversation permissions, provider OAuth scopes and per-action approval separate. Read-only can inspect and propose; it cannot mutate canonical content. Take actions enables eligible requests under existing policy, not unrestricted authority. Every AI mutation in Data still requires explicit approval.



**ARCH-008** — Bind approval to stable caller identity, exact action, canonical target, expected revision, scope, consequence, expiry and audit receipt. Recheck grants and relevant policy at execution and on resume/retry. A stale approval, cached schema or friendly-name match cannot authorise a changed operation. Cancel leaves the prior scope intact.



**ARCH-009** — Narrowing, revoking or blocking prevents future protected operations and reports honest in-flight outcomes. Trust durations do not bypass protected-action review. Persist the admitted policy version and subsequent changes so execution can be explained without revealing private internal reasoning or secrets.



**ARCH-010** — Storage location, model route, external disclosure, memory, background learning, telemetry and diagnostics are independent choices. Configuring a cloud provider/fallback does not authorise sending every document to it. Retrieved pages, attachments, tool output and generated UI are untrusted data with provenance, not policy instructions.



**ARCH-011** — Context selection uses relevant authorised sources and current scope. Recheck access to cached content, stored citations and resumed tasks. Revocation/deletion updates eligible indexes and caches. Stop capture at the actual microphone/camera/screen runtime boundary; stopped or hidden capture must not continue feeding models.



Acceptance: revoke a source after retrieval but before execution, change a target revision after approval, and retry an expired request. Confirm denial or refreshed review instead of silent execution. Attempt cross-Assistant private-memory access, cloud fallback under Local only, and a generated control that requests broader access; all must obey the same broker.

Mockups: [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions); [settings-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-permissions); [settings-privacy](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-privacy); [settings-sandbox](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-sandbox); [data-approval](design/9to1_Interface_Atlas_v3.3_Editable.html#data-approval); [terminal-command-review](design/9to1_Interface_Atlas_v3.3_Editable.html#terminal-command-review); [vision-session-sources](design/9to1_Interface_Atlas_v3.3_Editable.html#vision-session-sources).

Sources: [baseline/retained-pack/modules/permissions.complete.md](baseline/retained-pack/modules/permissions.complete.md); [baseline/retained-pack/modules/privacy.complete.md](baseline/retained-pack/modules/privacy.complete.md); Owner instructions: AI permission dropdown terminology; Atlas v12: [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions) and [settings-privacy](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-privacy) notes.



<a id="t.pa5ml7516cye--h.s3dg4saz9mjr"></a>

# 7.10 - Models, providers and route lifecycle

Retained baseline: one Dulche catalogue serves discovery, installed-model management, picker choices and runtime routing. A display-name match does not establish provider/model/voice identity or capability equivalence.



**RUNTIME-006** — Distinguish available, downloading, verifying, installed, loaded, updating, failed and removable model states. Preserve manifests, artifact references, versions, hardware fit and capability metadata. Model weights are managed once, not copied into every conversation. Selection is not itself a successful download or loaded runtime; sending performs the required eligible lifecycle transition. Names such as Qwen 4 in the Atlas are fixtures, not evidence of downloadable weights or supported adapters.



**RUNTIME-007** — Filter capability, privacy, entitlement, hardware/resource availability and current policy before ordering eligible routes. Active and Background routes remain distinct. Local only never falls back to cloud. Changing a model revalidates inherited context and reasoning settings. A failed provider test or an untested credential draft must not appear connected and ready.



**RUNTIME-008** — Keep supported context capacity, selected context allocation, actual conversation usage, reasoning effort and provider allowance separate. Unknown counters remain unknown. Retain the documented discrete effort mapping: three levels 35/70/100, four 25/50/75/100, five 20/40/60/80/100, mapped to real provider level IDs rather than a floor formula. Inapplicable controls are unavailable for a stated capability reason.



Current owner override: the compact picker uses Main, Voice and Vision; Reasoning stays visible and Context, Model and Assistant/Specialist collapse. This presentation replaces the old picker modality grouping, while catalogue capability distinctions remain. A changed context value is pending until Apply and reload.



**RUNTIME-009** — Proposed implementation contract: retain active and pending context configuration separately; identify the affected EndpointID/SessionID scope, validate requested capacity before reload, show reload progress, and commit the new active configuration only after success. Failure retains the previous working route/configuration and the draft value for correction; never report an applied context the runtime rejected. Preserve conversation text and clearly handle any active run before reloading it. Do not restart unrelated sessions sharing the broker.



Acceptance: install/verify/load a real model, fail verification, exhaust disk and attempt removal while in use. Test capability filtering, exact effort mappings, expired provider auth and Local-only fallback. Change context, cancel, apply successfully and fail reload; visible active/pending values must match runtime state throughout.

Mockups: [home-models](design/9to1_Interface_Atlas_v3.3_Editable.html#home-models); [home-models-installed](design/9to1_Interface_Atlas_v3.3_Editable.html#home-models-installed); [home-model-details](design/9to1_Interface_Atlas_v3.3_Editable.html#home-model-details); [host-model-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker); [settings-models](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-models); [settings-provider](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-provider); [settings-providers](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-providers); [settings-voice](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-voice).

Sources: [baseline/retained-pack/modules/models.complete.md](baseline/retained-pack/modules/models.complete.md); [baseline/retained-pack/modules/dulche.complete.md](baseline/retained-pack/modules/dulche.complete.md); Owner instructions: 18 November picker/context changes; Atlas v12: [host-model-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker) notes.



<a id="t.pa5ml7516cye--h.rzxe6ixb0y4j"></a>

# 7.11 - Den, Assistant identity and memory

Retained baseline: Den stores canonical scoped Assistant/context knowledge. Persistent Assistants, reusable Specialist definitions and temporary worker subagents are different entities.



**DATA-017** — Preserve stable Agent/Den identity across Home, AI Studio, Spaces and editor collaboration. An Assistant's permanent Main conversation remains the same canonical thread. Configuring an Assistant changes that entity rather than replacing it. A Specialist invocation is a scoped run participant under its definition, not an accidental new personal Assistant.



**DATA-018** — Preserve source-defined versioned manifests, journals, namespaces and encrypted data contracts. Store memory record identity/version, owner, provenance, timestamps, permitted scopes and correction/deletion history. Canonical memory is distinct from ephemeral request context, retrieval snippets and learned summaries; model references do not duplicate weights.



**DATA-019** — Memory Frequency uses Never, Sometimes, Often and Always. Changes affect future autonomous memory behaviour and do not delete existing records. Background Learning is separately opted into and does not authorise external disclosure. Temporary chats must never create or reinforce durable memory; the retained unresolved question of reading existing memory needs an explicit product decision.



**DATA-020** — Memory access uses the broker and personal, Space, Assistant, team and organisation boundaries. Show actually used source provenance and actionable stale/conflict/denied states. Deleting or correcting a record updates eligible retrieval indexes and caches without claiming inaccessible replicas were already purged.



Acceptance: reopen one Assistant from three entry points and confirm the same identity/Main thread. Change memory frequency, correct/delete a record, revoke a source and run a Temporary chat. Verify no durable memory writes or cross-Assistant private access, and that source receipts name only material actually authorised and accessed.

Mockups: [home-assistants](design/9to1_Interface_Atlas_v3.3_Editable.html#home-assistants); [home-specialists](design/9to1_Interface_Atlas_v3.3_Editable.html#home-specialists); [ai-studio](design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio); [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex); [spaces-specialists](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-specialists); [settings-personalisation](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-personalisation).

Sources: [baseline/retained-pack/modules/den-memory.complete.md](baseline/retained-pack/modules/den-memory.complete.md); [baseline/retained-pack/modules/assistants.complete.md](baseline/retained-pack/modules/assistants.complete.md); Atlas v12: [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex) and [ai-studio](design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio) notes.



<a id="t.pa5ml7516cye--h.ltug83ic0vdw"></a>

# 7.12 - Skills, Plugins, MCPs and provider integrations

Retained baseline: these are registered, versioned capabilities and authorised provider connections. Installation, discoverability, assignment, authentication and execution permission are distinct.



**RUNTIME-010** — Retain package identity, version, provenance, signature/trust, dependency and compatibility contracts. Validate imports before activation. Installing text or assets does not itself execute unreviewed code. Space or Assistant assignments reference global packages instead of cloning incompatible copies.



**RUNTIME-011** — Tools declare stable IDs and typed schemas with timeout, cancellation, controlled error and receipt behaviour. Expose actual MCP authentication/transport/schema health. Revocation stops future access; changed or stale tool schemas require revalidation before invocation. A passive mention references a resource; explicit invocation follows its defined command/approval path.



**RUNTIME-012** — Integration connections retain provider account ID, scopes/version, resource mappings, sync tokens, pending operation IDs and confirmed receipts. A live reference, imported copy, cache and synchronised state are different. Check both provider scope and 9to1 grant at use time; one provider session does not grant a second account's data.



**RUNTIME-013** — Reconnect and queued retries preserve operation identity and use idempotency where supported. Report remote quota, unsupported operations, partial batch success and uncertain outcomes honestly. Expired credentials, transport failure and incompatible packages offer recovery without losing unrelated drafts.



Acceptance: install, validate, assign, invoke and revoke one capability; repeat with a missing dependency, schema mismatch and timeout. Connect two accounts to one provider and confirm isolation. Disconnect after a remote mutation and reconcile its receipt before retrying, avoiding duplicate messages/files.

Mockups: [settings-plugins](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-plugins); [settings-skills](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-skills); [settings-providers](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-providers); [settings-provider](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-provider); [ai-studio-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio-permissions); [spaces-attachments](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-attachments).

Sources: [baseline/retained-pack/modules/plugins-mcp.complete.md](baseline/retained-pack/modules/plugins-mcp.complete.md); [baseline/retained-pack/modules/integrations.complete.md](baseline/retained-pack/modules/integrations.complete.md).



<a id="t.pa5ml7516cye--h.3m9iva9pym0e"></a>

# 7.13 - Artifact collaboration and generated interfaces

Retained baseline: collaborator presence, proposed changes and generated controls operate on the owning app's canonical artifacts. They do not create a separate document engine or permission authority.



**ARCH-012** — Bind participants to canonical identity and the exact artifact/range/object/task scope. Inviting an Assistant does not grant access to every personal memory or file. Presence overlays and status belong to view/runtime state, not authored content or exports.



**ARCH-013** — AI proposals identify author, target, expected revision, current/proposed value and a semantic diff. Apply rechecks permission and revision, then commits one undoable transaction. Reject removes the proposal, not unrelated work. Concurrent change requires refreshed review; never silently replace it with stale content. Data retains its explicit approval rule for every AI mutation.



**ARCH-014** — Pause/stop controls address the selected participant/run and expose their actual boundary. Keep accepted edits and completed checkpoints; retry creates a new run under the task rather than replaying finished work. Progress and source receipts refer to actual events and artifacts.



**CUI-NEW-005** — Generated forms, charts, tables and selectors validate against the safe typed CUI registry. Component/state/action identity, schema version, source TaskRun and artifact revision remain inspectable. Arbitrary text, HTML or generated code does not become trusted executable UI. Invalid streaming output yields a recoverable error while preserving prior valid state and keyboard focus.



**CUI-NEW-006** — Ephemeral conversational interaction stays distinct from promotion to a durable artifact. Save/promote calls the owning typed API; Open resolves that artifact and Download requires verified bytes. Every generated action rechecks current grants. HTML and CUI inspector views are complementary representations of the selected surface, not evidence that a native developer-tools backend exists in the Atlas.



Acceptance: two collaborators propose changes to the same object; apply one and reject or refresh the stale second proposal. Revoke a participant mid-run without losing accepted edits. Stream an invalid generated component, recover, promote valid output and verify Open/Download reference the same saved source revision.

Mockups: [collaborators-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#collaborators-picker); [write-collaborators-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#write-collaborators-picker); [present-collaborators](design/9to1_Interface_Atlas_v3.3_Editable.html#present-collaborators); [write-collaborators](design/9to1_Interface_Atlas_v3.3_Editable.html#write-collaborators); [collaboration-review](design/9to1_Interface_Atlas_v3.3_Editable.html#collaboration-review); [spaces-generative-ui](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generative-ui); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files); [host-devtools-cui](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui); [host-devtools-html](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html).

Sources: [baseline/retained-pack/modules/shared-ai.complete.md](baseline/retained-pack/modules/shared-ai.complete.md); [baseline/retained-pack/modules/productivity-engine.complete.md](baseline/retained-pack/modules/productivity-engine.complete.md); [baseline/retained-pack/modules/generative-ui.complete.md](baseline/retained-pack/modules/generative-ui.complete.md); Atlas v12: [collaboration-review](design/9to1_Interface_Atlas_v3.3_Editable.html#collaboration-review) and generated-ui notes.



<a id="t.pa5ml7516cye--h.xoxs07kq4vhe"></a>

# 7.14 - Retained language and runtime bindings

Retained baseline: the recovered source pack contains the full language/API contracts. The following are implementation gates, not illustrative alternative designs. Read the complete chapters before changing parser, runtime or security code.



**CUI-NEW-007** — The ten visual/structural primitives are Page, Container, Text, Image, Audio, Video, Object, Button, Input and Layer. Extend through registered Type, Definition, Prefab or Object Type. CUI is a strict XML-like language; built-in keywords are case-insensitive and user symbols case-sensitive. Preserve typed properties, scoped ID/Name and Groups, imports, events, conditions, live Repeat, styles, theme inheritance and the declared compiler pipeline. Reject unknown/ambiguous syntax and production donor markup.



**CUI-NEW-008** — Preserve Text as literal content and Children as deliberately parsed, capability-limited markup. Invalid Children updates retain the last valid tree. Static Prefabs share internal runtime state, dynamic Prefabs do not; neither implies persistence. Active, Hidden and Interactive remain independent. Live bindings use dependency notification rather than per-frame polling. Compiled output records language version, runtime ABI/registry version and required capabilities. Inspectors expose winning property sources and dependency/accessibility state.



**RUNTIME-014** — Preserve documented API names, argument order and Result fields. RequestID is assigned before queueing; SessionID owns context, EndpointID identifies logical execution and port/URL identifies transport. A shared endpoint never means shared session. Retain Dulche.Start(port), Test(port), Prompt(request, endpoint), SetModel(model, port), Pause(port) and Stop(port), together with documented overloads. Request-ID controls never broadcast; endpoint-wide omitted-port commands remain confined to the caller's authorised scope.



**RUNTIME-015** — Preserve the configurable engine abstraction/dispatcher and public setInferenceEngine/getInferenceEngine contract. Automatic selection filters incompatibility before measured fit; incompatible manual selection fails explicitly. The retained source describes llama.cpp and Strata; treat Strata-specific interfaces as compatibility/history in light of the later owner plan to replace it. That plan names Thunder for MoE and Lightning for dense/llama.cpp, with Monstrosity also mentioned; exact adapter mapping and migration require repository confirmation. Do not describe those engines as already integrated or hard-code Strata as the required future backend.



**RUNTIME-016** — Persistent requests retain one RootRequestID, CompletionContract, dependency graph, attempt lineage, SteeringMessages, blockers and completed-action receipts across continuation boundaries. A progress report is not completion evidence. Stop remains cancellation; it must not auto-resume. Continue independent runnable work when one node is blocked, and use AwaitingUser only when the remaining work requires input.



**RUNTIME-017** — Context compaction is a derived execution view over durable history. Preserve obligations, permissions, identifiers/revisions, completed actions, evidence and unresolved conflicts. Context Summarisation uses bounded, provenance-preserving passes when ordinary compaction is insufficient. Cloud summarisation requires its own applicable disclosure grant; otherwise expose a non-destructive capacity blocker. Preserve Context.Compact, Context.Summarise and Context.GetState contracts.



**RUNTIME-018** — Derive a request-specific EffectiveCapabilitySet from the canonical Tool Registry. Resolve semantic intent and typed multi-tool plans before claiming inability; distinguish missing capability, unavailable capability, prerequisites and policy denial. Computer Use requires explicit invocation/control and retains its separate approval and excluded-target rules. It is not an automatic fallback when an app API fails.



**DATA-021** — UCML is a versioned context representation with provenance, owner, scope, consent, retention and security metadata. It is not encryption. Keep keys, credentials and raw authentication secrets in trusted key management; never inject them into models. The scoped Context Broker returns only authorised relevant context. Rotation preserves decryptability of legitimately retained data; closed-source packaging is not the security boundary.



Acceptance: run the retained CUI conformance suite against real app surfaces; test cross-session isolation, scoped endpoint broadcasts, manual-engine incompatibility, persistent restart/steering/Stop, cloud-summary denial and semantic capability resolution. Verify that context and diagnostic outputs contain no key material and that invalid markup cannot gain execution authority.

Mockups: [host-devtools-cui](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui); [host-devtools-html](design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html); [spaces-thinking](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-thinking); [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-subagents](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagents); [settings-advanced](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-advanced); [settings-privacy](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-privacy); [ai-studio](design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio).

Sources: [baseline/retained-pack/technical/cui-02.md](baseline/retained-pack/technical/cui-02.md): V2-B0303 core language; [baseline/retained-pack/technical/cui-03.md](baseline/retained-pack/technical/cui-03.md): language/compiler/conformance; [baseline/retained-pack/technical/cui-04.md](baseline/retained-pack/technical/cui-04.md): CUI-001, CUI-002, CUI-PRO-201..204; [baseline/retained-pack/technical/dulche-01.md](baseline/retained-pack/technical/dulche-01.md): V2-B1823, B1947..B1962, B2032..B2035; [baseline/retained-pack/technical/dulche-03.md](baseline/retained-pack/technical/dulche-03.md): V2-B2083; [baseline/retained-pack/technical/dulche-04.md](baseline/retained-pack/technical/dulche-04.md): V2-B2248..B2261, COMPANION-B0084/SPUI-194; Later owner context: 16 November engine direction, supplied by root; repository confirmation pending.





<a id="t.kixgfsrptana"></a>

# 8. Apps & Learning

<a id="t.kixgfsrptana--h.xbw5wig84iiy"></a>

# 8.1 - Shared app behaviour

**APP-001** — Retained baseline: each owning app keeps its canonical artifact, stable object IDs, current revision and undo history. Shared menus, theme, model picker and collaboration project that same state. Opening a document from Home, Files, Boards, Study, Link or an Assistant must resolve the original rather than create an untracked copy.

**APP-002** — Follow the latest Haven integration: one outer tab system and one source of document identity/save state. Do not reintroduce an app's duplicate file tabs or host utility row because an older screen note lists them. App-specific menus and contextual controls remain available; dragging a file between Haven tabs must preserve its identity and disclose the actual operation.

**APP-003** — For Write, Present and Canvas, Start is a separate view with New, Open, Import, Templates and Recent. Direct-open/session restore bypasses Start; returning to Start retains the existing document session. Missing files, unsupported imports and partially recovered artifacts have explicit recovery states. A recent-file entry is a reference, not proof of local availability.

**APP-004** — Authored fonts, colours, charts, images, ink and geometry are document values. A UI theme/accent change affects chrome and selected controls without recolouring the document. Legacy per-app accent notes are superseded for shared control emphasis; recognisable app icons may retain their artwork colours.

**APP-005** — Every implementation slice needs default, selection, editing, loading, empty, denied, offline and failure states where applicable. Preserve recoverable drafts on failure. Distinguish saving a project, exporting a representation, sharing access and executing an external action; none may be used as a misleading substitute for another.

**APP-006** — Draft interaction standard: the context menu and toolbar invoke the same command implementation, with identical target, permissions and undo transaction. Text fields retain useful native editing behaviour. Commands that depend on selection, media, clipboard, project status or platform capability are disabled with a reason rather than producing fictional success.

**APP-007** — This chapter is a current interaction/readout layer over the complete retained modules and their technical clauses. Unpictured capabilities remain requirements; catalogue entries and mockup buttons are not backend completion evidence. Ordinary local authoring remains usable without sign-in, Files or a running AI model unless that operation actually requires one. Context-menu lists marked Draft are implementation proposals under the shared command/permission rules, not evidence of earlier approval.

Acceptance: create/open an artifact, change view and theme, open it from another app, return, undo an edit and reopen the saved file. Identity, authored content and revision history remain consistent. Use the app-specific rules below where they impose stricter boundaries.

Mockups: [write-launch](design/9to1_Interface_Atlas_v3.3_Editable.html#write-launch); [present-launch](design/9to1_Interface_Atlas_v3.3_Editable.html#present-launch); [canvas-launch](design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-launch); files; [home-apps](design/9to1_Interface_Atlas_v3.3_Editable.html#home-apps).

Retained modules: [platform](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [productivity-engine](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [persistence](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [home](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: Atlas LAUNCH-001–006; UX27-HOST-001–006; current owner Haven integration instructions, 18 November 2026; [baseline/retained-pack/modules/platform.complete.md](baseline/retained-pack/modules/platform.complete.md); [baseline/retained-pack/modules/productivity-engine.complete.md](baseline/retained-pack/modules/productivity-engine.complete.md); [baseline/retained-pack/modules/persistence.complete.md](baseline/retained-pack/modules/persistence.complete.md); [baseline/retained-pack/modules/home.complete.md](baseline/retained-pack/modules/home.complete.md).



<a id="t.kixgfsrptana--h.igvu38mz15e3"></a>

# 8.2 - Write

**WRITE-001** — Pages, Continuous and Book are views of one DocumentID, section tree, comments, selection and revision. New native documents default to Continuous unless a template requires pagination; restored documents retain their view. Page View has real authored margins and page geometry. Continuous is one flowing reading surface without paper cards, shadows or artificial inter-sheet gaps.

**WRITE-002** — Continuous retains page-aware semantics: section/page breaks, margins, headers, footnotes and pagination. A real break has a quiet marker rather than an invented sheet. Sections are authored nodes; headings derive from heading styles. Outline collapse, width and scroll are user view state and must preserve the caret and selection.

**WRITE-003** — The caret, selected range, toolbar and Properties share one editing state. User Edit/Suggest/View, layout Locked/Unlocked and AI Read-only/Take actions are independent. The contextual AI area reserves usable space; text and status controls never paint behind it. Authored formatting survives display mode and UI-theme changes.

**WRITE-004** — Suggest mode anchors a pending change to the exact range. Accept commits the selected change as an undoable operation after revision/scope checks; Dismiss rejects it without changing unrelated content. Comments are separate from edits and retain stable anchors when their section moves. The review panel scrolls independently and can reveal the precise target without covering it.

**WRITE-005** — Collaborators use the same global Assistant/Specialist identity and document revision. Invitations state source/range scope and operation mode; Invite begins work, while accepting a proposal is a separate operation. Pause/Stop retain task history and already committed edits. A stale proposed change requires revalidation rather than silent last-writer-wins.

**WRITE-006** — Retain the full rich-text engine: paragraph/character styles, nested sections, tables, positioned objects, references/fields, review, native documents, foreign-format compatibility, PDF opening/editing and derived export. Preserve supported donor features and report conversion limitations explicitly; a quiet toolbar does not reduce document capability.

Draft context menus: selection offers Cut, Copy, Paste where available, formatting, Link, Comment and Suggest; a suggestion offers Accept/Dismiss; an outline item offers Navigate, Rename heading and section operations that the current mode permits. Source text, comment, heading and suggestion targets must stay distinct.

Acceptance: switch Continuous→Pages→Book→Continuous with a selected range and pending suggestion; confirm unchanged content and comment anchors. Move a section, reveal its comment, reject one suggestion and accept another; one Undo restores the accepted change only. Test inaccessible collaborator scope and interrupted save without losing the draft.

Mockups: [write-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#write-editor); [write-continuous](design/9to1_Interface_Atlas_v3.3_Editable.html#write-continuous); [write-review](design/9to1_Interface_Atlas_v3.3_Editable.html#write-review); [write-collaborators-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#write-collaborators-picker); [write-collaborators](design/9to1_Interface_Atlas_v3.3_Editable.html#write-collaborators).

Retained modules: [write](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: WRITE-UX-101–120; UX27-WRITE-001–007; Atlas owner revision: true Continuous view; Atlas global collaborator contracts; [baseline/retained-pack/modules/write.complete.md](baseline/retained-pack/modules/write.complete.md); V3-WRITE-FEA-01–04; V3-WRITE-ENT-01–02; V3-WRITE-PER-01–02.



<a id="t.kixgfsrptana--h.h0ppxfr4ay3a"></a>

# 8.3 - Present

**PRESENT-001** — Preserve the presentation's stable slide/object IDs, source revision and slide order. Within Haven, consolidate duplicate identity chrome while retaining conventional app menus and exactly one contextual toolbar. Insert and Actions remain visible entry points. Fit uses the central viewport remaining after Notes/Properties and never allows a 16:9 slide to overflow into controls.

**PRESENT-002** — Single click selects; double click or Enter edits text. Eight resize handles and rotation follow object geometry and are transient overlays, excluded from export. Slide Notes has its own resize edge and scroll area. Bottom-right zoom provides minus, slider, actual derived percentage, plus and icon-only Fit; panel changes must recalculate the displayed transform.

**PRESENT-003** — Insert opens a catalogue. Choosing Shapes opens the shared gallery without allocating an object. The custom-shape builder holds an uncommitted draft; Insert shape creates one editable canonical shape, while Cancel restores the caller's insertion context without leaving a stray object.

**PRESENT-004** — Transitions is a simple editor over the same canonical Actions graph, not a separate animation engine. A preset changes only its owned subgraph. Open in Actions reveals that graph; user extension nodes outside the owned region survive simple edits. Nonrepresentable advanced edits show Custom/Advanced instead of being approximated or deleted.

**PRESENT-005** — Actions target stable slide/object IDs. A navigation request commits once and has explicit completion/failure. Authoring interactions do not trigger playback. Runtime preview is deliberate, with transient state kept separate from the document; typed failures and event budgets belong to the shared runtime.

**PRESENT-006** — Presenter view is private: notes, next slide and elapsed time are excluded from audience output by default. Leaving playback restores authoring state. Session ink remains separate from permanent content unless explicitly committed. Variants is an integrated comparison surface: identify source/base/target revisions, preview selected semantic changes and refresh stale conflicts before merge; switching/merging never publishes or presents.

**PRESENT-007** — Retain layouts/masters, text/media, object alignment/arrangement, animation, keyboard nudge and proportional resize. Collaborators target the same deck/selected slide with expected revisions and typed reviewable changes. Playback grants no new write or external-action authority; failed export retains the native deck.

Draft context menus: slide thumbnail offers New, Duplicate, Reorder, Hide/Show, Notes, Transition and Delete; object offers Copy/Duplicate, Arrange, Group where valid, Properties, Actions and Delete. Visibility changes and destructive commands respect selection/mode and are undoable.

Acceptance: open Notes and Properties, Fit the slide, edit one object, create/cancel a custom shape and reopen its builder. Edit Fade in the simple panel and inspect the same graph; preserve an extension node. Present to an audience view and verify notes/selection handles are absent. Merge a stale variant only after refreshed comparison.

Mockups: [present-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#present-editor); [present-selection](design/9to1_Interface_Atlas_v3.3_Editable.html#present-selection); [present-insert](design/9to1_Interface_Atlas_v3.3_Editable.html#present-insert); [present-transitions](design/9to1_Interface_Atlas_v3.3_Editable.html#present-transitions); [present-actions](design/9to1_Interface_Atlas_v3.3_Editable.html#present-actions); [present-presenter](design/9to1_Interface_Atlas_v3.3_Editable.html#present-presenter); [present-shape-builder](design/9to1_Interface_Atlas_v3.3_Editable.html#present-shape-builder); variants; [collaborators-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#collaborators-picker); [present-collaborators](design/9to1_Interface_Atlas_v3.3_Editable.html#present-collaborators); [collaboration-review](design/9to1_Interface_Atlas_v3.3_Editable.html#collaboration-review).

Retained modules: [present](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [variants](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: PRESENT-UX-101–106; PRESENT-DES-201–207; PRESENT-INT-101–107/201–202; TF-V01–03; Atlas Actions/Transitions and collaboration contracts; [baseline/retained-pack/modules/present.complete.md](baseline/retained-pack/modules/present.complete.md); [baseline/retained-pack/modules/variants.complete.md](baseline/retained-pack/modules/variants.complete.md); V3-PRESENT-FEA-01–04; V3-PRESENT-ENT-01–02; V3-PRESENT-PER-01–02.



<a id="t.kixgfsrptana--h.zhah9zypzpqi"></a>

# 8.4 - Canvas and shared shapes

**CANVAS-001** — Canvas uses one coordinate model for drawing, hit testing, selection and viewport transforms. Infinite mode fills the viewport without an artificial paper edge. Object, layer and stroke identities survive zoom/pan, cross-app insertion and save/reopen. UI themes do not change authored background, stroke or fill colours.

**CANVAS-002** — The bottom-centre primary tool order is Select, Pan, Pen, Eraser, Insert, Tools, AI; only the selected tool shows its label. Reselecting Pen opens deeper preset options. One options host sits above the tools; AI replaces that host instead of creating a second permanently expanded composer. Layers is an explicit popup for selected-object work, not a permanently reserved rail.

**CANVAS-003** — Insert's first-level categories are Text, Shapes, Media, Table and Other. Opening a category allocates nothing until a choice is committed. Pen presets resolve properties onto new strokes so later preset changes do not rewrite old strokes. Pressure/tilt options reflect device capability; mouse input retains a usable fixed-width fallback.

**CANVAS-004** — Present and Canvas share the editable shape/path model. Anchors, segments, closed state and fill rule persist. Corner, Smooth and Symmetric alter tangent constraints; linked Bézier handles, numeric coordinates, add/remove/split, keyboard nudge and Boolean operations use coherent undo transactions. Preview excludes selection adorners.

**CANVAS-005** — Save to My shapes stores a reusable definition; inserting an instance keeps editable geometry. Cancel discards only the uncommitted builder draft. Source permissions and caller selection remain intact when returning to an app.

**CANVAS-006** — Retain grouping, connectors, transforms, drawing/erasing and supported hybrid raster/vector operations. Semantic AI edits target actual selected IDs and revision. Failed renders preserve native content; reconnect does not duplicate strokes or replay committed changes.

Draft context menus: selected objects expose Copy, Duplicate, Group/Ungroup when valid, layer order, Lock/Unlock, Properties and Delete; background exposes Paste, Insert and view controls; a path point exposes point type, split/add/remove and coordinate editing. Disabled operations explain geometry/lock constraints. All menus preserve the explicit target.

Acceptance: draw with mouse and a supported pressure device, edit a preset, reopen and verify earlier strokes remain unchanged. Pan/zoom then select a shape using the same geometry; move its layer and Undo. Create a path in Canvas, reuse it in Present and edit its anchors without rasterisation. Toggle AI and confirm exactly one options host.

Mockups: [canvas-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-editor); [canvas-ai](design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-ai); [canvas-pen-presets](design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-pen-presets); [canvas-selection](design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-selection); [canvas-insert](design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-insert); [canvas-shape-builder](design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-shape-builder); [present-shape-builder](design/9to1_Interface_Atlas_v3.3_Editable.html#present-shape-builder).

Retained modules: [canvas](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: UX27-CANVAS-001–005; Atlas owner Canvas tool/insert revision; Atlas shared editable-path builder contract; [baseline/retained-pack/modules/canvas.complete.md](baseline/retained-pack/modules/canvas.complete.md); V3-CANVAS-FEA-01–03; V3-CANVAS-ENT-01–02; V3-CANVAS-PER-01–02.



<a id="t.kixgfsrptana--h.n4c00hofq37q"></a>

# 8.5 - Study and Cards

**LEARN-001** — Study references canonical Subjects, Topics, paper/specification mappings and owning-app resources. Labels may change without replacing IDs. The desktop navigation retains New Chat, Subjects, My Progress, Planner, Revision Bank, recent chats and at most three Revise These recommendations across all subjects. Rank deterministically: recorded weak topics first, then overdue/oldest-reviewed, with stable TopicID ties; show the reason and fewer than three when eligibility requires.

**LEARN-002** — Time spent, streaks, resource opens and card flips are engagement, not demonstrated proficiency. Topic states are Not assessed, Needs work, Developing and Confident, derived from attributed answers and review records. Never label an unassessed topic weak/mastered. A learner's self-reported confidence is separate and cannot silently change proficiency or another learner's private evidence.

**LEARN-003** — Learn/Practise/Test/Review changes the next request within the same Study session, retaining ConversationID, TopicID, history, source selection and answer draft. Preserve meaningful mode-transition provenance. Practise records an actually submitted attempt with question/source, working, feedback, marking provenance and hint conditions. An unanswered displayed question adds no evidence.

**LEARN-004** — Test uses the canonical Forms Test/Quiz engine and inspectable rules/rubric. Selecting Test alone starts no timer and fabricates no grade. Learn worked examples and Review page opens do not record mastery. My sources only applies the configured closed-corpus rule; Wider knowledge allowed labels supplied sources, external research and general explanation while still respecting web/account permissions. Switching policy never rewrites earlier provenance.

**LEARN-005** — Revision Bank categories are Recommended, All, Flashcards, Notes, Papers and Quizzes, with Create New at the right. Resource cards reference Cards, Forms and Write artifacts; View/interact is default, editing/Open in App is explicit. Planner opens the canonical Study burrow, and linked topic context follows the composer rather than creating a second content store.

**LEARN-006** — Cards groups canonical CardIDs/SetIDs without duplicating content. Due counts, total cards, current position and completed reviews are separate measures. Review defaults to a focused vertical stack with shadowed neighbouring cards; the supported horizontal alternative remains available. View flips on activation and cannot edit; Edit uses click-to-edit with an explicit Flip control and never flips from an ordinary content click.

**LEARN-007** — Front and Back are independently editable structured content with fonts, backgrounds, images/graphs and ink preserved in native save/export. Add, duplicate, reorder, remove/restore and bulk changes retain IDs and recoverable revisions. Set topic links and card metadata are separate from private ReviewRecords; shared content edits never overwrite another learner's ratings.

**LEARN-008** — Red/amber/green ratings create private attributed review evidence and update a valid linked Study topic. An unlinked card offers topic linking instead of inventing a mapping. New cards remain unassessed. Save conflicts and partial bulk outcomes must be explicit; neither a fixture rating nor a page-open event proves a learning result.

**LEARN-009** — Read-only sets allow permitted review but prevent content edits; revoked source media shows unavailable. Empty Study has no invented grades or topics. Topic details expose attributed attempts, hint/open-book conditions, last/next review and source links. Ranking/rules are versioned; precise evidence weights, proficiency thresholds and review intervals remain an explicit unresolved product policy in STUDY-UI/AC source, not constants for the implementer to invent. Keyboard card navigation and Flip preserve focus; Edit/View switching preserves the draft.

Draft context menus: resource cards offer View, Open in App, Copy link and permitted organisation actions; topic rows offer evidence/review inspection and topic-bound practice; cards offer Edit, Duplicate, Move, Topic link and Remove. Ratings remain explicit review actions, never decorative menu side effects.

Acceptance: change session mode while drafting an answer, switch source policy, submit one attempt and trace its evidence to the topic. Flip a card without rating and confirm no proficiency change. Rate a linked card and verify only that learner's record changes. Mobile Overview/Subjects/Progress/Bank keeps Planner and full navigation reachable, and the composer stays above keyboard/safe areas.

Mockups: [spaces-study](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-study); [study-subject](design/9to1_Interface_Atlas_v3.3_Editable.html#study-subject); [study-topic](design/9to1_Interface_Atlas_v3.3_Editable.html#study-topic); [study-session](design/9to1_Interface_Atlas_v3.3_Editable.html#study-session); [cards-study](design/9to1_Interface_Atlas_v3.3_Editable.html#cards-study); [cards-review](design/9to1_Interface_Atlas_v3.3_Editable.html#cards-review); [cards-edit](design/9to1_Interface_Atlas_v3.3_Editable.html#cards-edit); [mobile-study](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-study).

Retained modules: [spaces](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [cards](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: STUDY-UI-202/205/210–220; LE-08–10; UX27-SPACES-012; UX27-OTHER-006; Atlas approved Study/Cards contracts; [baseline/retained-pack/modules/spaces.complete.md](baseline/retained-pack/modules/spaces.complete.md); [baseline/retained-pack/modules/cards.complete.md](baseline/retained-pack/modules/cards.complete.md); V3-CARDS-LAY-01–02; V3-CARDS-FEA-01–03; V3-CARDS-ENT-01–02; V3-SPACES-FEA-21–22; STUDY-UI-203/206/208/217–219; STUDY-AC-211–212; [baseline/retained-pack/technical/spaces-10.md](baseline/retained-pack/technical/spaces-10.md); [baseline/retained-pack/technical/spaces-11.md](baseline/retained-pack/technical/spaces-11.md).



<a id="t.kixgfsrptana--h.x06anm3e9h0j"></a>

# 8.6 - Data and Forms

**APP-DATA-001** — Retain the spreadsheet engine: formulas, dependency-aware recalculation, tables, structured fields, filtering, sorting, views and charts. A workbook/database, sheet/table, cell/record and source revision have stable identity. Spreadsheet, database and SQL projections share data only where an explicit mapping exists; changing view never creates parallel records. SQL names the actual connection/runtime. Manual commands, queries and AI mutations remain distinguishable.

**APP-DATA-002** — Active cell, selected range and editing are different states. The formula bar shows the active expression; reference tokens map to the corresponding ranges. Anchored function help preserves the edit. Commit/Cancel is explicit; Enter commits into the same cell before normal navigation. Results use actual typed values, errors, locale and units, never fixture success.

**APP-DATA-003** — Every AI mutation requires approval of that concrete action, including when general Take actions is enabled. Review identifies source/revision, affected cells or records, proposed diff, validation and recovery. Live-database changes require the applicable backup; failed backup blocks approval. A proposal, approved operation, executed result and failed/partial operation are separate records. Revalidate stale ranges rather than writing into shifted data.

**APP-DATA-004** — Chart insertion binds actual ranges, headers, categories and series. Preview is uncommitted; Insert creates one editable chart. Invalid references and empty series are visible. Authored chart colours persist across UI themes; the old green app-accent preference does not override the user's shared palette.

**APP-DATA-005** — Retain typed multi-value/structured cells, nested tables with owned-versus-referenced identity, multidimensional DataCubes, slices/pivots and explicit relational projections. These deeper source capabilities are not fully designed by the four spreadsheet mockups; use the complete Data engine contract.

Draft menus/keyboard: cells expose Copy/Paste, Insert/Delete row or column, Format and applicable data operations; charts expose Edit data, Properties and Remove. Selection defines scope. Escape cancels an uncommitted formula; keyboard range navigation must not accidentally submit an AI mutation.

Acceptance: edit/cancel a formula, insert a bound chart, change source data and observe the actual chart update. Attempt an AI write with global Take actions enabled: its concrete approval remains mandatory. Reject a failed-backup write and retain the original data.



**APP-FORMS-001** — Forms owns canonical FormID/TestID, stable QuestionIDs, sections, answers, validation/branching/scoring rules and versions. Reordering questions preserves branch references. Retain supported answer types, constraints, defaults, typed TableInput/repeating responses, Form/Test/Quiz presets, custom modes over the shared state runtime, and assessment maths/marking rules. The condition/regex tester exposes samples and failures; malformed or unsafe conditions cannot be published as valid.

**APP-FORMS-002** — Draft, respondent Preview and published version are distinct. Preview opens the selected draft with disposable answers; it neither publishes nor creates a real response. Return to Build preserves draft edits. Publish shows audience/access and exact version; changes to published forms preserve explicit response/version compatibility and response privacy. A real response is accepted only after durable persistence; export of responses is a separate operation.

Draft menus/keyboard: question rows offer Duplicate, Reorder, Required, Branching and Delete with reference checks. Tab reaches question and answer controls in respondent order; builder shortcuts never leak into respondent input. An empty form offers Add question; missing branch targets identify the offending question; a failed submission retains the respondent's answers and a safe retry.

Acceptance: reorder a branched question, preview both paths, cancel preview and compare unchanged response counts. Publish a known revision, submit once, recover a failed save without duplicate responses, and verify Study references that same assessment.

Mockups: data; [data-approval](design/9to1_Interface_Atlas_v3.3_Editable.html#data-approval); [data-formula](design/9to1_Interface_Atlas_v3.3_Editable.html#data-formula); [data-chart](design/9to1_Interface_Atlas_v3.3_Editable.html#data-chart); forms; [forms-preview](design/9to1_Interface_Atlas_v3.3_Editable.html#forms-preview).

Retained modules: [data](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [forms](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: V3-DATA-FEA-01–04; V3-DATA-ENT-01–02; UX27-DATA-001–004; CUI-PREF-203; V3-FORMS-FEA-01–03; UX27-OTHER-005; [baseline/retained-pack/modules/data.complete.md](baseline/retained-pack/modules/data.complete.md); [baseline/retained-pack/modules/forms.complete.md](baseline/retained-pack/modules/forms.complete.md).



<a id="t.kixgfsrptana--h.il23drmnilho"></a>

# 8.7 - Files and Link

**APP-FILES-001** — Files provides provider-native create/open/import, rename, move, copy, trash/restore and metadata through real capabilities. Preserve FileID/provider identity, path, revision and operation receipts. List and Grid show the same folder/selection; address, breadcrumbs, back/forward, search, sorting and details share navigation state. Local availability and provider sync are separate: a cloud outage does not block local folders or turn pending uploads into confirmed remote files.

**APP-FILES-002** — Move preserves identity; Copy deliberately creates another artifact. Collision handling offers explicit applicable Replace, Keep both or Skip, with per-item outcomes for bulk operations. Preview/Open in App resolves the owner and permissions. Opening a .9to1board edits the same board; Boards also works without Files installed. Empty folders, denied locations, disconnected providers and unsupported previews have useful alternatives without exposing inaccessible metadata.

**APP-FILES-003** — The ten organisation types are Folder, Project, Sheet, Tree, Smart, Timeline, Pipeline, Catalogue, Stash and Matrix. They share nesting/navigation and an ordinary Contents view without erasing specialised state. Tree separates editable visual structure from reader View. Smart queries are source-scoped; Pipeline movement enforces stage/gate rules; Matrix uses stable row/column axes rather than current display positions. Unshown specialised editors remain retained requirements, not completed mockups.

**APP-FILES-004** — Sheet arranges references, notes, drawings and shapes. Add existing defaults to Add reference. Removing a placement does not delete or move the source. The default file opening policy is Pop-up; in authoring, click selects and explicit Open enters the item. A source editor discloses when it edits the original. Authored geometry/connectors/opening policy persist; personal selection, viewport and chosen view do not become shared content. This approved Sheet contract supersedes earlier ambiguous spreadsheet-like wording.

Draft menus/keyboard: file rows offer Open, Open in App, Rename, Copy, Move, Share where supported, Details and Trash. F2 renaming, clipboard commands and Delete operate on explicit selection; a specialised placement offers Remove reference separately. Escape returns focus to the invoking row. Acceptance: switch List/Grid, move without breaking links, reject a collision, remove a Sheet placement and reopen its unchanged source.



**APP-LINK-001** — Flow is a continuous composed document with one primary vertical scroll. Preserve the report composition example: a Data section, Write content, image, a second section of the same Data source, and a report Assistant. Each placement records source identity, section selection and live/pinned revision policy. Arrange, duplicate placement and remove affect composition; Edit original independently checks source permission and returns to the same Flow position.

**APP-LINK-002** — Binder is one authored interconnected document, with nested notes, folders, tabs/stacks, sections, connections and shared objects. It is not a provider-folder copy. Readers navigate through outline, links, breadcrumbs and Back without authoring handles. Closing a container restores viewport/focus. Cancelling Insert leaves no source mutation. Missing/deleted sources, lost permission and newer source revisions remain visible and recoverable.

Draft menus/keyboard: a Flow placement offers Open original, Revision policy, Move, Duplicate placement and Remove placement; Binder objects expose applicable structure and link commands. Keyboard navigation follows reading order; editing commands require author mode. Acceptance: embed two regions of one Data artifact, edit the original, verify both declared revision policies, remove one placement without deleting data, and traverse/return from a Binder container with focus restored.

Mockups: files; [files-grid](design/9to1_Interface_Atlas_v3.3_Editable.html#files-grid); [files-context](design/9to1_Interface_Atlas_v3.3_Editable.html#files-context); [files-organisation](design/9to1_Interface_Atlas_v3.3_Editable.html#files-organisation); [link-flow](design/9to1_Interface_Atlas_v3.3_Editable.html#link-flow); [link-binder](design/9to1_Interface_Atlas_v3.3_Editable.html#link-binder); [link-binder-insert](design/9to1_Interface_Atlas_v3.3_Editable.html#link-binder-insert); [link-flow-block-edit](design/9to1_Interface_Atlas_v3.3_Editable.html#link-flow-block-edit).

Retained modules: [files](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [link](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: V3-FILES-FEA-01–05; V3-FILES-ENT-01–02; TF-F01–F11, especially TF-F04; UX27-FILES-001–004; UX27-BOARD-003–006; V3-LINK-FEA-01–03; TF-L01–03; TF-X01–02; [baseline/retained-pack/modules/files.complete.md](baseline/retained-pack/modules/files.complete.md); [baseline/retained-pack/modules/link.complete.md](baseline/retained-pack/modules/link.complete.md).



<a id="t.kixgfsrptana--h.z0pj1m8svz8t"></a>

# 8.8 - Planner and Automations

**APP-PLANNER-001** — Events and PlannerTasks keep stable identity across Day/Week/Month/Agenda, calendars and burrows. Dragging an unscheduled task onto the grid schedules that task without duplicating it. Burrow membership references the source. Task completion differs from dismissing a reminder; removing membership differs from deletion.

**APP-PLANNER-002** — Edit exposes the actual calendar, start/end, timezone, duration and reminder. Recurring edits choose this occurrence, following occurrences or entire series. AI scheduling proposes target calendar, times and conflicts before an authorised commit. Conflicting, unavailable or offline calendars retain the proposal and truthful pending state. An empty calendar is distinct from a failed load.

**APP-PLANNER-003** — The event's category colour is an inset nib with its own radius, not the event border. Collapse secondary panels before shrinking the grid beyond use; Agenda is the narrow alternative. Draft context menus expose Open, Edit, Reschedule, Complete for tasks, recurrence-aware Delete and calendar details. Keyboard alternatives support selecting an event and editing its time without drag precision.

Acceptance: schedule one existing task, open it through Study's canonical burrow and confirm identity; edit one recurrence without changing the series; reject a conflicting AI proposal; switch Agenda/Week without losing selection.



**APP-AUTO-001** — Automations uses the shared typed Node Graph: trigger, input, condition, action and output nodes, compatible ports, branches, parallelism, approvals, retries and typed app/tool operations. Preserve AutomationID, node IDs and revisions. Draft and Active are different; Save or Test never activates. Validate graph types, dependencies and effective permissions before activation, with errors attached to the offending node/edge.

**APP-AUTO-002** — Tests name sample input, target environment and live/mock status. A live test retains external-action approvals. Runs pin the starting revision and record each node's actual outcome. Durable waits survive restart; resumption cannot replay already committed effects. Space commands, Assistant schedules and sidebar entries reference the same AutomationID/revision.

**APP-AUTO-003** — Schedule settings include clock time, timezone, days, daylight-saving and missed/offline-run policy. Use the canonical scheduler/deduplication contract; changing timezone cannot silently create duplicate runs. Missing credentials or unavailable tools pause/fail clearly with recovery; empty graphs provide a valid starting trigger.

Draft menus/keyboard: nodes offer Configure, Duplicate, Disable where valid and Delete; edges expose compatible reconnect/remove commands. Enter opens node details, Escape closes them, and a keyboard route performs every essential graph operation. Activation, Run, Retry and Cancel are distinct commands.

Acceptance: save an invalid graph without activating it, correct the exact error, test with a declared input, activate a known revision and resume a delayed run after restart without repeating its completed side effect. Verify an offline missed schedule follows its selected policy.

Mockups: planner; [planner-event](design/9to1_Interface_Atlas_v3.3_Editable.html#planner-event); [planner-agenda](design/9to1_Interface_Atlas_v3.3_Editable.html#planner-agenda); automations; [automation-trigger](design/9to1_Interface_Atlas_v3.3_Editable.html#automation-trigger).

Retained modules: [planner](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [automations](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: LE-11; UX27-OTHER-001–002; V3-PLANNER-FEA-01–03; UX27-AUTO-001–006; V3-AUTOMATIONS-FEA-01–04; [baseline/retained-pack/modules/planner.complete.md](baseline/retained-pack/modules/planner.complete.md); [baseline/retained-pack/modules/automations.complete.md](baseline/retained-pack/modules/automations.complete.md).



<a id="t.kixgfsrptana--h.jkq97rwo95g9"></a>

# 8.9 - Picture, Wave and Motion

**APP-MEDIA-001** — Each native media project retains its own ID/revision, editable objects, original media and nondestructive edit history. Project Save is distinct from rendering/export. Missing media/fonts, unsupported decoders, unavailable processing and interrupted jobs preserve the project and expose relink/retry/cancel. A preview image or fixture waveform is never evidence of a real file or successful job.

**APP-PICTURE-001** — Automatic object composition and optional manual layers share one model without flattening. Preserve semantic/manual selections, layers, masks, text, crop/resize, vector/raster transforms, adjustments, ordering, visibility and opacity. Layer and mask are separately selected targets. Painting a mask never erases the original image; size, softness, opacity, visibility and inversion apply to the named mask. Hit testing and rendering use the same image transform. AI edits are revision-bound proposals with provenance and review.

**APP-PICTURE-002** — Export names format, dimensions, colour profile, alpha and destination, rendering actual composition rather than an editor screenshot. Draft layer menus offer Duplicate, Group where valid, Order, Add/Edit mask and Delete; view shortcuts never mutate pixels. Acceptance: edit/undo a mask, relink a missing source and export a known revision with alpha preserved.

**APP-WAVE-001** — Retain multitrack timeline/mixer, import/record, trim/split/move, fades, gain, automation, effects/bus routing, waveform/spectrogram repair, Enhance Audio, Denoise and mix export. Transport time, source waveforms, levels and recording states come from actual media/backend. Recording requires the selected device grant and armed tracks. Clip source offset, timeline start, duration and gain remain distinct; numeric fade and handle edits update the same envelope.

**APP-WAVE-002** — Split Stems is a first-class tool: declare source, backend, resource requirements and output tracks; preserve the original and align provenance-linked outputs. Pending/Running/Failed/Complete reflects the actual job. Export uses the selected range, revision and sample rate; unavailable output is not shown as complete.

Draft clip menus offer Split, Trim, Duplicate, Mute, Fades and Relink where applicable. Space controls transport outside text entry; selection movement and audition never overwrite source media. Acceptance: trim a clip, edit its fade numerically, cancel a stem job, successfully retry and compare aligned outputs; reopen the project with edit history intact.

**APP-MOTION-001** — Media bin, source/program viewer, contextual inspector and multitrack timeline share canonical clip/source identities. Preserve compositing, titles, effects, keyframes, animation, audio alignment and the shared-framework advanced node editor with Motion-specific node families. Playback uses the same frame/timebase as the playhead. At a selected cut, transition effect, duration, alignment and available source handles are explicit; insufficient handles produce a real warning.

**APP-MOTION-002** — Proxy/preview quality differs from final output. Render review identifies revision, range, format, resolution, frame rate, audio and destination, validating dependencies before queueing. A queue entry, project save and completed output are different states. Draft menus offer Split, Trim, Transition, Properties, Relink and Remove; keyboard frame stepping must match rendered frames. Acceptance: add a valid transition, reject one lacking handles, render a chosen range and verify output duration/audio alignment; failed render leaves editable sources intact.

Mockups: picture; [picture-mask](design/9to1_Interface_Atlas_v3.3_Editable.html#picture-mask); [picture-export](design/9to1_Interface_Atlas_v3.3_Editable.html#picture-export); wave; [wave-clip](design/9to1_Interface_Atlas_v3.3_Editable.html#wave-clip); [wave-stems](design/9to1_Interface_Atlas_v3.3_Editable.html#wave-stems); motion; [motion-transition](design/9to1_Interface_Atlas_v3.3_Editable.html#motion-transition); [motion-export](design/9to1_Interface_Atlas_v3.3_Editable.html#motion-export).

Retained modules: [picture](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [wave](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [motion](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: UX27-OTHER-007–009; V3-PICTURE-FEA-01–03; V3-WAVE-FEA-01–03; V3-MOTION-FEA-01–03; [baseline/retained-pack/modules/picture.complete.md](baseline/retained-pack/modules/picture.complete.md); [baseline/retained-pack/modules/wave.complete.md](baseline/retained-pack/modules/wave.complete.md); [baseline/retained-pack/modules/motion.complete.md](baseline/retained-pack/modules/motion.complete.md).



<a id="t.kixgfsrptana--h.tu49h9cciub6"></a>

# 8.10 - Build, AI Studio and Games

**BUILD-001** — Code, Sites and Stacks operate on the same ProjectID, canonical files and revision. Code retains real language services, completion/signatures, navigation, formatting/refactoring, diagnostics, build/test/debug and source control. Diagnostics target actual current file/ranges. AI edits produce typed diffs with validation and applicable approval; proposed terminal execution is separately reviewed against the actual session.

**BUILD-002** — Code and CUI visual building edit the same model. Invalid hot reload preserves the draft and labels the last valid preview stale. Completion accept/dismiss preserves editor focus; one accepted suggestion is one undo operation. Monospace belongs to code/diagnostics rather than all app chrome. New Project includes the canonical 9to1 Extension type with target/contribution filtering. Toolchain Manager exposes actual version/capability/provenance; missing runtimes and failed builds show actionable evidence.

**BUILD-003** — Sites retains pages, reusable components, layouts, typography, assets, links/forms and source-defined CMS/content settings. Visual and code views share objects. Responsive edits name the property/breakpoint, inherited base and explicit override/reset. A 390px preview does not create another page. Authored site styles are isolated from editor chrome. Development preview, staging and production differ; deploy names domain/environment/revision, validation and rollback, with consequential publication separately authorised.

**BUILD-004** — Stacks preserves Branch/Twig/Leaf identities, ancestry, root authority, cascade and revision history. Review names base/local/incoming versions. Reject, Accept without Merging and Accept and Merge remain separate. Freeze names domain/descendants, creator, reason, revision and any expiry; underlying authority prevents AI or descendant bypass. Thaw/expiry revalidates queued work instead of applying it silently. Commit, push, merge and deploy are distinct operations.

Draft menus: explorer files expose Open/Rename/Move/Find references and applicable source-control actions; responsive properties expose Override/Reset; conflicts expose their exact alternatives. Keyboard completion/navigation and graph alternatives remain accessible. Acceptance: inspect a stale diagnostic, recover a broken preview, reset one mobile override and reject a frozen-branch write without losing local edits.



**BUILD-AI-001** — AI Studio authors Agent/Specialist, harness, tool, Skill, Plugin and MCP definitions through registered schemas. Form and graph views share definition IDs. A global Agent keeps its DenID when edited. Validate types/models/dependencies and permission intersections. Draft/Active versions, test inputs/profile/model route and live/mock status remain explicit; evaluate suites with provenance and failure detail. Retain Context Inspector, Permission Simulator, Dependency Inspector, version/run comparison and definition-derived documentation. Passing a sample does not activate, install or grant authority.

**BUILD-AI-002** — A tool source-read grant does not grant unrelated writes. Protected operations remain checked by the owning app/shared broker. Draft menus expose Configure, Validate, Test, Compare version and applicable activation. Acceptance: test a read-only profile, attempt a forbidden write and retain its failure receipt. Training Lab is explicitly deferred/undefined in V3-AI-STUDIO-FEA-04; no finished training backend is implied.

**BUILD-GAMES-001** — Games preserves scene/entity/component/asset identities across Development, Creation and Rendering. Retain C#-first scripting with C++ for applicable low-level work, physics/interaction, animation, debugging/build/export, shared-versus-individual state/replication, declarative input bindings, CUI game UI and registered graph nodes. GDScript is not a supported public Games language under the retained contract. Play/Test is explicit runtime state; applying runtime changes to the authored scene requires review. Draft hierarchy menus offer Add, Duplicate, Parent, Properties and Delete; dependencies constrain removal. Acceptance: test a scene, exit without overwriting it, review one captured runtime change and retain build failures/provenance. The workspace mockup is not evidence of a playable engine.

Mockups: [build-code](design/9to1_Interface_Atlas_v3.3_Editable.html#build-code); [build-code-diagnostics](design/9to1_Interface_Atlas_v3.3_Editable.html#build-code-diagnostics); [build-sites](design/9to1_Interface_Atlas_v3.3_Editable.html#build-sites); [build-sites-responsive](design/9to1_Interface_Atlas_v3.3_Editable.html#build-sites-responsive); [build-stacks](design/9to1_Interface_Atlas_v3.3_Editable.html#build-stacks); [build-stacks-freeze](design/9to1_Interface_Atlas_v3.3_Editable.html#build-stacks-freeze); [ai-studio](design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio); [ai-studio-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio-permissions); games.

Retained modules: [build-code](design/9to1_Interface_Atlas_v3.3_Editable.html#build-code); [build-sites](design/9to1_Interface_Atlas_v3.3_Editable.html#build-sites); [build-stacks](design/9to1_Interface_Atlas_v3.3_Editable.html#build-stacks); [ai-studio](design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio); [games](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: UX27-BUILD-001–007; V3-BUILD-CODE-FEA-01–03; V3-BUILD-SITES-FEA-01–04; V3-BUILD-STACKS-FEA-01–03; UX27-OTHER-013/015; V3-AI-STUDIO-FEA-01–04; V3-GAMES-FEA-01–03; [baseline/retained-pack/modules/](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[build-code](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[.complete.md](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [baseline/retained-pack/modules/](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[build-sites](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[.complete.md](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [baseline/retained-pack/modules/](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[build-stacks](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[.complete.md](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [baseline/retained-pack/modules/](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[ai-studio](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[.complete.md](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [baseline/retained-pack/modules/games.complete.md](baseline/retained-pack/modules/games.complete.md).



<a id="t.kixgfsrptana--h.ic10k7u4zffb"></a>

# 8.11 - Connect, Vision & Voice, Maps, Shelf and Play

**APP-CONNECT-001** — Connect contains native messaging and full Mail mode. Rooms/threads/native messages and mail messages retain their own protocol semantics. Keep contacts, real From identity, recipients, provider, folders, search/rules, reply/forward, drafts, MIME/headers and attachments. Retain native disappearing messages, supported SMS/RCS and one-to-one/group voice/video calls; capture, transcription and AI participation retain their distinct grants. AI drafting never sends; reply context remains attached to its original channel/thread unless deliberately retargeted. Encryption badges describe the actual transport, never applying native encryption claims to SMS. Room membership does not automatically grant attachment access.

**APP-CONNECT-002** — Draft, queued, provider-submitted, sent, failed, scheduled and Undo Send are truthful distinct states. Schedule retains sender/recipients, timezone and attachment readiness; editing/cancelling before dispatch remains possible. Offline retry rechecks grants and deduplicates dispatch. Draft menus offer Reply/Forward, Archive, Move, Mark read and available message details; scheduled items offer Edit time/Cancel. Keyboard Send is explicit and must respect the user's selected shortcut. Acceptance: retain an attachment failure draft, schedule/cancel once, recover an offline outbox without duplicate delivery. An empty mailbox differs from authentication failure; uncertain dispatch is resolved before retry.

**APP-LIVE-001** — Vision & Voice preserves ConversationID/SpaceID/session identity across full app and compact Spaces view. Conversational, Monologue, Live Listener and Live Translate modes share the service. Named model/voice, locality, capability and limits must be real. Listening, recording, camera and screen sharing are independently visible controls/grants; mic access implies none of the others. Typed input, interruption and turn-taking remain available. Captions are not commands by default.

**APP-LIVE-002** — Ambient audio uses the configured ephemeral buffer by default; saving audio/transcripts requires explicit retention. Stop removes the live source without conflating retention deletion. Connection/latency/level displays use observed data; idle fixtures do not imply a live call. Source menus expose Select, Stop and retention controls; keyboard mute/stop and typed fallback remain available. Acceptance: enable microphone only, verify camera/screen remain off, revoke capture, reconnect without another conversation and inspect what was actually saved.

**APP-MAPS-001** — Maps owns canonical places, saved collections, typed community POIs, personal landmarks and Custom Journeys with ordered travel/stop/wait/activity/manual steps and declared provider/freshness. Search and AI are explicit modes. Entered origin, device location and unknown location differ. Stops, travel mode/preferences, distance/time/traffic use real provider output; ambiguous geocoding and outages show uncertainty rather than generated routes. Draft menus offer Save place, Add stop, Copy location and actual route options. Keyboard alternatives reorder stops. Acceptance: change mode/stop order on one route, deny location while using an entered origin, and show a failed route honestly.

**APP-SHELF-001** — Shelf curates LaunchItems/collections as references, distinct from Files and the Android Launcher. One item may appear in several collections without source copies. Add/Pin/Organise/Remove membership does not move files or uninstall packages. Open/Locate/Install uses actual target availability and the canonical owner registry. Draft menus expose those applicable actions; keyboard reorder is available. Acceptance: remove one membership, open the unchanged original elsewhere and repair a missing target without silent substitution.

**APP-PLAY-001** — Play owns ruleset revision, MatchID, participants, rounds/turns, legal actions, timing, checkpoints and scores. The pictured human-plus-ten-Agents quiz excludes outside AI tools and hides opponent answers until reveal. A selected answer is a draft until Submit; scoring follows completed canonical actions. Agents use only permitted game APIs/information. Reuse Forms quizzes where applicable; Replay never re-executes game side effects. Draft menus expose rules, participant details and replay where permitted; keyboard answers require explicit Submit. Acceptance: change an unsubmitted choice, reject an illegal turn, withhold hidden answers and resume an interrupted match from its recorded state.

Mockups: [connect-mail](design/9to1_Interface_Atlas_v3.3_Editable.html#connect-mail); [connect-messages](design/9to1_Interface_Atlas_v3.3_Editable.html#connect-messages); [connect-compose](design/9to1_Interface_Atlas_v3.3_Editable.html#connect-compose); [connect-schedule](design/9to1_Interface_Atlas_v3.3_Editable.html#connect-schedule); [vision-voice](design/9to1_Interface_Atlas_v3.3_Editable.html#vision-voice); [vision-session-sources](design/9to1_Interface_Atlas_v3.3_Editable.html#vision-session-sources); maps; [maps-route-options](design/9to1_Interface_Atlas_v3.3_Editable.html#maps-route-options); shelf; [shelf-add](design/9to1_Interface_Atlas_v3.3_Editable.html#shelf-add); play.

Retained modules: [connect](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [vision-voice](design/9to1_Interface_Atlas_v3.3_Editable.html#vision-voice); [maps](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [shelf](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [play](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: UX27-OTHER-003–004/010–011/014/016; UX27-SPACES-014; V3-CONNECT-FEA-01–04; V3-VISION-VOICE-FEA-01–03; V3-MAPS-FEA-01–03; V3-SHELF-FEA-01–02; V3-PLAY-FEA-01–03; [baseline/retained-pack/modules/connect.complete.md](baseline/retained-pack/modules/connect.complete.md); [baseline/retained-pack/modules/](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[vision-voice](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[.complete.md](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [baseline/retained-pack/modules/maps.complete.md](baseline/retained-pack/modules/maps.complete.md); [baseline/retained-pack/modules/shelf.complete.md](baseline/retained-pack/modules/shelf.complete.md); [baseline/retained-pack/modules/play.complete.md](baseline/retained-pack/modules/play.complete.md).



<a id="t.kixgfsrptana--h.fct8yarsvzvu"></a>

# 8.12 - Prototype, Paths and Replay

**APP-PROTOTYPE-001** — Prototype models interactive calculations, systems and simulations. Design/Logic/Run share one typed model and stable object/property IDs. Types, units, bounds, step, default/current values and bindings are inspectable. Validated formulas/dependencies drive real outputs; missing references, cycles and invalid values show errors. Renaming preserves bindings and undo.

**APP-PROTOTYPE-002** — Runtime inputs are session state; saving a new authored default is separate. The parameters fixture's pending 2.5kg is not its still-applied 2kg preview. Reset returns to declared initial state without replaying external effects. Typed external capabilities retain shared permissions. Draft menus expose Properties, Binding, Reset input and applicable model authoring; keyboard input provides the same validation as sliders. Acceptance: enter an invalid bound, keep the valid preview, apply a valid input, reset, then reopen without silently changing the default.

**APP-PATHS-001** — Paths is a branched authored route with stable StepIDs, step content, conditions and reader attempts. Outline and graph positions are views. A branch names target, typed condition, priority/fallback and missing-value behavior. Validate missing targets, unreachable outcomes and uncontrolled loops. A false condition does not choose a random route; nonlinear progress must not invent a fixed percentage.

**APP-PATHS-002** — Draft Preview uses disposable progress; real attempts persist separately. Restart/reset is explicit and does not silently delete history. Linked activities use canonical apps and current grants; checkpoints/idempotency prevent repeated external actions on resume. Draft menus offer Add step, Edit condition, Connect, Preview from here and reference-aware Delete; keyboard outline navigation offers a graph alternative. Acceptance: test true/false/missing values, repair an invalid target, exit Preview without altering a learner attempt and resume a real attempt at the recorded checkpoint.

**APP-REPLAY-001** — Replay records selected semantic source revisions, events and checkpoints; optional media supplements them. Capture start/stop/pause/failure/incomplete state is explicit and does not imply microphone/camera permission. Historical viewing stays read-only at its selected revision even after the source advances. Event details identify source, actor, timestamp, before/after and safe input/output provenance.

**APP-REPLAY-002** — Seeking reconstructs state; it never re-executes mutations. Missing dependencies and capture gaps visibly limit reconstruction. Open current checks current permissions. Continue from here asks the owning app to create an independent copy, never overwrite the current source. Redaction/share review does not erase the original capture. Draft menus offer Inspect event, Jump to checkpoint, Open current and permitted Continue from here; keyboard seeking follows actual event order. Acceptance: capture edits, advance the source, seek backward, inspect a gap, then branch an independent artifact while the current source remains unchanged.

Mockups: prototype; [prototype-parameters](design/9to1_Interface_Atlas_v3.3_Editable.html#prototype-parameters); paths; [paths-branch-rule](design/9to1_Interface_Atlas_v3.3_Editable.html#paths-branch-rule); replay; [replay-event-detail](design/9to1_Interface_Atlas_v3.3_Editable.html#replay-event-detail).

Retained modules: [prototype](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [paths](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [replay](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: TF-P01–03; TF-H01–03; TF-R01–03; TF-X02; V3-PROTOTYPE-FEA-01–03; V3-PATHS-FEA-01–03; V3-REPLAY-FEA-01–03; [baseline/retained-pack/modules/prototype.complete.md](baseline/retained-pack/modules/prototype.complete.md); [baseline/retained-pack/modules/paths.complete.md](baseline/retained-pack/modules/paths.complete.md); [baseline/retained-pack/modules/replay.complete.md](baseline/retained-pack/modules/replay.complete.md).



<a id="t.kixgfsrptana--h.t596isruul3c"></a>

# 8.13 - Terminal, Mini Computer, Admin, 9to1OS and Launcher

**APP-TERMINAL-001** — Terminal uses the actual PTY, shell/profile, target, working directory and process/pane. Command mode runs that shell; AI mode is separate and does not receive ordinary commands automatically. Switching panes must not retarget pending input. AI command review shows exact code, shell/session, directory and privilege context before Run once. Copy to prompt does not execute, and visual wrapping never alters code.

**APP-TERMINAL-002** — Transcript is previous output; a suggestion has no invented exit code. Disconnection preserves transcript with truthful attach/new-session recovery. Closing explains process impact. Context menus separate Copy selection, Paste and session actions; keyboard/multiline paste follows explicit policy. Acceptance: copy an AI command without executing, review/run once on the named target, switch panes and reconnect without falsely claiming the process resumed.

**APP-VM-001** — Mini Computer identifies VM/provider/session, configuration/runtime, disks/snapshots and input owner. View-only and Control gate real input. Clipboard, folder/device sharing and AI control are independent capabilities. Closing the viewer detaches while the VM continues; pause/save state/stop/shutdown are distinct actions with supported power-state constraints. Draft menus expose applicable connection, snapshot and power actions; keyboard capture/release remains available. Acceptance: detach/reconnect to the same VM, deny input in View-only, and show unavailable snapshot capability instead of fake success.

**APP-ADMIN-001** — Admin always names OrganisationID and current scope. Membership states include invited, active, suspended and removed. Product entitlement does not confer admin rights. Roles/policies control models/providers, Plugins/MCPs, shared resources and organisation limits; Business/Business+ resource pools/ceilings remain distinct from seat counts. Billing/invoices, subscription assignment, usage/rollover and device/team-Agent administration remain retained capabilities. Organisation authority never exposes personal conversations/memory; unrelated organisations remain isolated.

**APP-ADMIN-002** — Policies progress Draft→Validated→Published, then Delivered/Acknowledged/Failed per target. Offline devices are not automatically compliant. Review shows effective scope/targets/conflicts and an audit of who changed what; bulk changes retain per-item outcomes. Draft menus expose permitted member/role/policy actions; keyboard table selection retains explicit bulk scope. Acceptance: publish a scoped policy, inspect one offline target and one failure, and verify no unrelated organisation changes.

**APP-OS-001** — 9to1OS keeps native window controls, taskbar and Go with shared typed Search/AI actions. Its one-to-five taskbar layers are separate from virtual desktops, Desktop Spaces, Desktop Pages and LLM Spaces. Quick Settings reflects actual device/resource state; overlapping global popups obey mutual exclusion. Native OS surfaces do not acquire Haven's browser chrome.

**APP-OS-002** — Retain Files/device/disk/log utilities, window/resource control and declared Wine/WinBoat/Android compatibility where supported. Each compatibility environment identifies runtime/version/capabilities and permissions; a shell mockup proves no compatibility backend. Draft taskbar/window menus expose actual pin, layer and window actions; keyboard switching must preserve focus. Acceptance: move between layers without changing virtual desktop, open Go and inspect a genuinely unavailable runtime.

**APP-LAUNCHER-001** — Launcher is the Android/9to1OS home surface: pages, folders, widgets, app drawer and dock, with grid/icon size, gestures and wallpaper options. It is distinct from Shelf and Home's package catalogue. Widgets retain source identity/grants; Go and Dulche are shared entry points. Touch targets/safe areas and accessible keyboard/switch alternatives remain usable. Long-press menus distinguish Remove shortcut from Uninstall; drag has Move alternatives. Acceptance: place one package in two folders, remove one shortcut without uninstalling, and restore a widget after permission loss without fabricating data.

Mockups: terminal; [terminal-command-review](design/9to1_Interface_Atlas_v3.3_Editable.html#terminal-command-review); [mini-computer](design/9to1_Interface_Atlas_v3.3_Editable.html#mini-computer); admin; [os-go](design/9to1_Interface_Atlas_v3.3_Editable.html#os-go); launcher.

Retained modules: [terminal](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [mini-computer](design/9to1_Interface_Atlas_v3.3_Editable.html#mini-computer); [admin](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [os](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [launcher](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal).

Sources: UX27-OTHER-012/017; ADM-01–12; V3-TERMINAL-FEA-01–03; V3-MINI-COMPUTER-FEA-01–03; V3-ADMIN-FEA-01–03; V3-OS-FEA-01–03; V3-LAUNCHER-FEA-01–02; [baseline/retained-pack/modules/terminal.complete.md](baseline/retained-pack/modules/terminal.complete.md); [baseline/retained-pack/modules/](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[mini-computer](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal)[.complete.md](https://chatgpt.com/library?file_id=libfile_9008eb4db5c481919479d02850204a32&account_id=personal); [baseline/retained-pack/modules/admin.complete.md](baseline/retained-pack/modules/admin.complete.md); [baseline/retained-pack/modules/os.complete.md](baseline/retained-pack/modules/os.complete.md); [baseline/retained-pack/modules/launcher.complete.md](baseline/retained-pack/modules/launcher.complete.md).





<a id="t.usni9fu0ls33"></a>

# 9. Implementation & Handoff

<a id="t.usni9fu0ls33--h.9ao1yopveqs1"></a>

# 9.1 - Targeting, menus and keyboard lifecycle

Status: shared retained focus/dismissal principles plus proposed implementation defaults for details not fixed by existing screens. Apply these defaults consistently; they are not claims of current mockup capability.

**INPUT-001** — Resolve the innermost meaningful target before opening a contextual menu. Preserve an existing multi-selection when its member is targeted; otherwise target the clicked object deliberately. Record canonical target IDs and relevant revisions. Menu opening changes focus/selection only; no command runs until explicit activation.

**INPUT-002** — Right-click, ContextMenu/Shift+F10 and the visible overflow trigger expose the same applicable command set. Retain native text-edit menus inside ordinary inputs. On touch, provide an equivalent visible menu control; do not require a long press that prevents ordinary text selection or scrolling.

**INPUT-003** — Proposed default: one active transient menu chain per window; persistent inspectors are independent. Opening a sibling menu closes the prior chain. Submenus belong to their parent's target. Resize, navigation, target removal or lost permission closes/repositions the popup safely and invalidates stale commands; it does not execute the highlighted item.

**INPUT-004** — Use native button activation and predictable menu navigation. Up/Down traverse enabled menu items; Home/End reach the first/last; Right/Left open/close applicable submenus. Enter activates; Escape closes the innermost popup and restores focus. Tab exits a nonmodal menu without activation. Text fields retain text-navigation keys and composition handling.

**INPUT-005** — Focus returns to the trigger if it survives; otherwise to the nearest surviving target or owning region. Modal permission/review dialogs contain focus until dismissed. Nonmodal inspectors and workspace panels do not trap focus. The open/closed state and accessible name reflect reality.

**INPUT-006** — Recheck capability, current selection, target revision and scope at activation. A previously enabled command may become unavailable. Show the relevant reason and recovery; never substitute a different object or silently use a stale grant.

**INPUT-007** — One command implementation serves toolbar, menu and shortcut. Prevent duplicate dispatch when a child control bubbles through a parent handler. IME composition and native text editing retain their text keys. Otherwise the topmost active popup handles its navigation/dismissal first, then the focused app editor, then the app and Haven. A handled command must not also trigger an outer command.

**INPUT-008** — Dragging uses an explicit handle/threshold and a valid highlighted destination before commit. Keyboard move commands perform the same operation. Previewing a drop changes no canonical data; cancellation restores the original arrangement. Moving a reference between views does not implicitly copy/delete its underlying artifact.

Acceptance: test viewport corners, long labels, multi-selection, removed targets, revoked grants, text composition, Escape through nested popups, route changes while open and one shortcut shared by app/host. Assert one dispatch to the correct object and an intact cancellation path.

Mockups: [boards-context](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context); [boards-insert](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-insert); [spaces-attachments](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-attachments); [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions); [host-model-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker); [host-newtab-customise](design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-customise).

Sources: V3-CUI-FEA-02; V3-PRODUCTIVITY-ENGINE-FEA-03; UX27-SHARED popup lifecycle; Current CTX-BASE-001–004; Proposed defaults: INPUT-001,003–008 details beyond retained rules; [baseline/retained-pack/modules/cui.complete.md](baseline/retained-pack/modules/cui.complete.md).



<a id="t.usni9fu0ls33--h.sw2s5kf9m5de"></a>

# 9.2 - State, persistence and recovery acceptance

Status: retained persistence/approval invariants; proposed conflict presentation where a detailed app design is absent.

**RECOVER-001** — Treat document editability, layout lock, AI access, connection state, save/sync state and runtime state as separate dimensions. A global Busy indicator cannot erase an independent saved output or pretend a permission-held run is merely loading.

**RECOVER-002** — Keep draft, pending apply, applying and applied values distinct. Cancelling a popup preserves the applied value; an explicit Apply operation validates and commits one command. Failure preserves recoverable input and the last valid committed value. Undo reverses a real reversible mutation, not the popup closing.

**RECOVER-003** — A successful local save returns the canonical revision; optional sync reports its own pending/synced/failed/conflict state. Native save, derived export and publishing remain separate operations. Local editing continues when optional cloud services or the AI broker are unavailable.

**RECOVER-004** — Typed mutations carry expected revision and stable operation identity. On reconnect/retry, reconcile any uncertain result before replay. Reloading a screen, restoring a checkpoint or reopening a document must not resend messages, reinstall packages or repeat external actions.

**RECOVER-005** — Proposed default for an unresolved revision collision: preserve both recoverable versions, show which source/revision changed, offer inspect/compare and an explicit merge or Save copy path. Do not overwrite a newer revision silently. Where the production API cannot support a safe operation, disable that operation with a concrete recovery route.

**RECOVER-006** — Delete, remove from collection, detach, uninstall and archive retain their different consequences. Wording identifies the affected target/count and recovery scope. Routine reversible changes use meaningful Undo; irreversible loss or required external approval uses a specific consequence review. The user's general coding authorisation is not a product end-user's approval of an AI action.

**RECOVER-007** — Test interruption at admission, commit, local save, sync, export and approval separately. Verify content after restart, not only a success toast. Test expired/revoked permission, partial batch success, failed export and unavailable remote devices without losing unrelated local work.

Mockups: [boards-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-recovery); [spaces-export-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery); [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files); [settings-local-general](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-local-general); [host-model-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker).

Sources: V3-PERSISTENCE-LAY-01–02; V3-PERSISTENCE-FEA-01–03; V3-PERSISTENCE-ENT-01–02; V3-PERSISTENCE-PER-01–02; V3-PERSISTENCE-ACC-01–02; V3-PERMISSIONS-PER-01; Proposed default: RECOVER-005 conflict presentation; [baseline/retained-pack/modules/persistence.complete.md](baseline/retained-pack/modules/persistence.complete.md); [baseline/retained-pack/modules/permissions.complete.md](baseline/retained-pack/modules/permissions.complete.md).



<a id="t.usni9fu0ls33--h.iscu4dpyo0py"></a>

# 9.3 - Accessibility, responsive behaviour and evidence

Status: retained observable interface requirements; this section does not claim external certification.

**ACCESS-001** — Every actionable icon has a meaningful accessible name, visible keyboard focus and an independent usable target. Selection and state must remain understandable without colour alone. Stronger UI typography must not rewrite authored document typography or user content.

**ACCESS-002** — Preserve reading/interaction order when panels collapse. Narrow screens use labelled reopen controls and appropriate drawers/sheets; primary content does not shrink into unusable desktop columns. Keep composer, toolbars, menus and contextual AI clear of one another and the visible keyboard.

**ACCESS-003** — Reduced motion removes unnecessary movement without hiding state changes. Settings snapping is proximity-based and must not trap touch, wheel or keyboard scrolling. A focused offscreen item scrolls into view without repeatedly resetting the user's position.

**ACCESS-004** — Test keyboard-only use, large text, high contrast, reduced motion, long translated labels, empty/large lists and touch/narrow layouts. Check focus after navigation, deletion, popup dismissal and streamed updates. Read-only/disabled states must retain legible labels and explanations.

**ACCESS-005** — Acceptance evidence names the exact build/commit or artifact hash, screen, setup, action and observed result. Separate parser/unit checks, real interaction tests, live visual review and backend acceptance. Passing one category does not imply the others passed.

The supplied Atlas checks validate structure, selected events, routes and saved-snapshot preservation. They do not verify live rendered geometry, production runtime execution, real permissions or durable backend operation. Perform the missing visual/backend checks in the authorised receiving environment before claiming those areas complete.

Mockups: [mobile-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-chat); [mobile-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-permissions); [mobile-study](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-study); [settings-accessibility](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-accessibility); [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex); [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor).

Sources: V3-CUI-LAY-04; V3-CUI-FEA-02; V3-CUI-ACC-01; V3-SPACES-LAY-02; V3-SPACES-ACC-07,09; qa/v12\_surfaces\_integration\_report.json; qa/reference\_startup\_report.json; [baseline/retained-pack/modules/cui.complete.md](baseline/retained-pack/modules/cui.complete.md); [baseline/retained-pack/modules/authority.complete.md](baseline/retained-pack/modules/authority.complete.md).



<a id="t.usni9fu0ls33--h.5q5m94gttag6"></a>

# 9.4 - Implementation handoff and completion gates

Status: handoff procedure. A finished specification defines the intended product; it does not declare the product or offline prototype implemented.

**HANDOFF-001** — Apply the latest owner decisions within their stated scope, then this specification's explicit current overrides, then retained unsuperseded v3.1 contracts. Mockup geometry and notes supply screen evidence; fixtures do not define real balances, model availability or completed actions. Proposed implementation defaults remain identifiable and cannot overrule a retained decision accidentally.

**HANDOFF-002** — Read the package manifest and relevant current sections before code. Historical agent instructions, old deadlines and deployment/process directions are provenance, not fresh executable authority. Locate repository instructions, actual framework/service boundaries, source configuration, build commands and existing tests. Preserve working foundations; do not recreate CUI, Dulche or every app from the single HTML's demonstration JavaScript.

**HANDOFF-003** — Refactor the entire existing 9to1 codebase and working application to conform to the current specification and Atlas, including the relevant UI, runtime, backend services, persistence, schemas, migrations, permissions, shortcuts and integrations. Deliver complete, reviewable increments mapped to requirement IDs and Screen IDs. Begin with shared shell/components/routing/state and one canonical artifact workflow, then continue through all affected app families and required states. Slices are sequencing and verification gates; completing one slice or a visual prototype does not complete the assignment.

**HANDOFF-004** — Each increment must preserve previous authored content and route identity, cover its meaningful failure/cancellation state, and pass relevant checks before expanding scope. Add tests for behavioural risk, not a second copy of implementation syntax. Keep reference notes and acceptance evidence in step with changes.

**HANDOFF-005** — Treat ordinary reversible implementation details as engineering choices within the contract. Record the chosen default and rationale. If a missing source or unresolved policy affects authority, data loss, schema compatibility or an external effect, hold that affected operation, record a precise blocker and continue independent authorised work.

**HANDOFF-006** — Report implemented behaviour, actual verification and remaining limits separately. Include failed checks and unavailable dependencies. A screenshot, sample data, route transition or stubbed provider is not backend completion. Do not claim a live deployment or external action without its real result.

****

**HANDOFF-007** — Before editing, inventory existing apps, screens, workflows, commands, shortcuts, integrations, APIs, data models, formats and other capabilities. Map each to the current specification and classify it as conforming, needing adaptation, explicitly conflicting, an additional compatible feature, or incomplete. Preserve working foundations that fit the contract and refactor conflicting implementations where necessary.

****

**HANDOFF-008** — Preserve additional existing features that the specification and Atlas do not mention when they do not conflict with current requirements. Keep them usable and discoverable, adapting their appearance, placement and implementation to the shared design and architecture. A conflict requires a specific current requirement or explicit owner decision that existing behaviour violates. Omission from a mockup, condensed example, simplified navigation or chapter is not a conflict. Record the overriding requirement; change only the conflicting aspect where possible, preserving compatible capabilities and user data.

****

**HANDOFF-009** — Maintain a feature inventory with retained, adapted, replaced and blocked statuses and verification evidence. Verify both specification compliance and regressions in compatible extras, including backend behaviour, saved data, valid preferences, shortcuts, integrations, APIs, supported formats and recovery paths. Verify required migrations. Do not replace working capabilities with stubs or make them inaccessible to simplify a screen. Completion requires the full affected scope; report incomplete areas explicitly.

Completion gates: source/requirement traceability; real canonical state and action semantics; matching current design across the affected states; keyboard/responsive/error recovery; preserved unrelated content; reproducible build/test evidence. A feature with only a visual fixture is design-covered, not functionally complete.

The recovered complete source pack is supplied under baseline/retained-pack/, with v3.1 material separately under baseline/v31/. Load the current section and its Screen IDs first, then the relevant modules/\<module\>.md reading map and prerequisites. Read modules/\<module\>.complete.md or its named technical/ chunks for the work in progress; the technical clauses remain mandatory, but loading the entire historical corpus into one model context is unnecessary. Use catalog/ and history/ for disputed provenance. Consult the dated v3.1 amendments as retained source without allowing older interface or process wording to overrule current decisions.



Source and repository baseline — Current overrides mapped; actual code/service boundaries and relevant retained modules identified; an existing build or its precise failure recorded.

Shared shell and interaction primitives — Routes and canonical selection survive navigation; theme tokens, tabs, popup focus and keyboard dispatch behave as specified; unrelated authored content is preserved.

One canonical artifact workflow — Edit, scoped AI proposal, review, commit, save, restart/reopen and export operate on the same artifact/revision, including denied permission and failed export.

App and runtime expansion — Each added app uses its own model; admission, stop, reconnect, approvals and partial failures have real outcomes; screens and tests trace to requirements.

Handoff or release candidate — Relevant live visual, keyboard/responsive and backend checks are recorded with exact build evidence; failed/unavailable categories remain explicitly open.



Bounded defaults: use a visible overflow trigger when no right-click input exists; a drawer for a hidden narrow-screen inspector; the surviving owning region as lost-trigger focus fallback; and compare/merge or Save copy for an unresolved revision conflict. These are proposed implementation defaults, not owner quotations. Preserve a more specific app contract. Do not invent a new permission mode, irreversible conversion or transport behaviour to fill a design gap.

Sources: V3-AUTHORITY-FEA-01; V3-AUTHORITY-LAY-01–02; v3.1 Using the module pack / Source accounting; Current BASE-001–003 and REF-001–003; Current owner direction: handoff to Qwen 3.8 at 19:30; [baseline/retained-pack/modules/authority.complete.md](baseline/retained-pack/modules/authority.complete.md); V3-AUTHORITY-PER-01–02; [baseline/retained-pack/catalog/source\_ledger.json](baseline/retained-pack/catalog/source_ledger.json); baseline/v31/.



<a id="t.usni9fu0ls33--h.5172dd3tr5jo"></a>

# 9.5 - Acceptance checklist

**AC-RUN-01** — Rejected admission keeps draft/attachments; one successful admission creates one request; steer/queue preserve immutable history.

Requirements: SPACE-RUN-001; SPACE-RUN-003; SPACE-RUN-004.

Mockups: [spaces-compose](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-compose); [spaces-thinking](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-thinking).



**AC-RUN-02** — Stop, stale revision and mid-run revocation show actual outcomes; reconnect does not replay a committed effect.

Requirements: SPACE-RUN-005; SPACE-RUN-007; RECOVER-004.

Mockups: [spaces-running](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running); [spaces-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions).



**AC-ORG-01** — Supported conversions preserve identities; rejected reverse conversions do not mutate; removing a container preserves members.

Requirements: SPACE-ORG-001; SPACE-ORG-002; SPACE-ORG-003.

Mockups: [spaces-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat); [spaces-manage](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-manage).



**AC-GEN-01** — Invalid UI schema preserves last valid state; opening retains artifact identity; failed export leaves native source available; Download requires verified bytes.

Requirements: SPACE-OUT-003; SPACE-OUT-004; SPACE-OUT-005; SPACE-OUT-006.

Mockups: [spaces-generative-ui](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generative-ui); [spaces-present-working](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-present-working); [spaces-generated-files](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files); [spaces-export-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery).



**AC-AGENT-01** — Mixed child outcomes, cancellation, budget exhaustion and reconnect preserve completed siblings and never widen permission or duplicate runs.

Requirements: SPACE-OUT-001; SPACE-OUT-002.

Mockups: [spaces-subagents](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagents); [spaces-subagent-detail](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagent-detail).



**AC-MENU-01** — Pointer/keyboard target the same entity; focus restores correctly; stale command cannot execute; nested handlers dispatch once.

Requirements: INPUT-001; INPUT-004; INPUT-006; INPUT-007.

Mockups: [boards-context](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context); [spaces-attachments](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-attachments); [host-model-picker](design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker).



**AC-SAVE-01** — Interrupt save/sync/export independently and verify exact recovered content/revision after restart; conflicts preserve recoverable versions.

Requirements: RECOVER-003; RECOVER-004; RECOVER-005.

Mockups: [boards-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-recovery); [spaces-export-recovery](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery).



**AC-ACCESS-01** — Keyboard, large text, high contrast, reduced motion and narrow layouts retain operability, reading order and visible focus.

Requirements: ACCESS-001; ACCESS-002; ACCESS-003; ACCESS-004.

Mockups: [mobile-chat](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-chat); [mobile-permissions](design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-permissions); [settings-accessibility](design/9to1_Interface_Atlas_v3.3_Editable.html#settings-accessibility); [spaces-assistant-alex](design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex); [boards-editor](design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor).



<a id="t.usni9fu0ls33--h.nv1twuot0r27"></a>

# 9.6 - Qwen starter prompt

Refactor the entire existing 9to1 codebase and working application to conform to the current Specification and editable Interface Atlas. Work in the actual repository. Apply the agreed design and behaviour throughout the relevant UI, shared components, routing, runtime, backend services, persistence, schemas, migrations, permissions, shortcuts and integrations. Reuse working foundations where they fit the contract, and refactor conflicting implementations where necessary. A separate mockup, cosmetic wrapper or one completed screen does not complete this assignment.



Before changing code, inspect the repository and its operating instructions. Inventory the existing apps, screens, workflows, commands, shortcuts, integrations, APIs, data models and other capabilities. Map them to the specification and record whether each is conforming, requires adaptation, conflicts with an explicit requirement, is an additional compatible feature, or is incomplete. Record the existing build and relevant test results.



Preserve additional existing features even when the specification or Atlas does not mention them, provided they do not conflict with the current requirements. Adapt their appearance, placement and implementation to the shared design and architecture while keeping them usable and discoverable. Preserve working backend behaviour, user data, settings, shortcuts, integrations, APIs, supported formats and recovery paths. Do not remove a capability, replace it with a stub, or make it inaccessible merely because it is absent from a mockup, condensed example, navigation list or chapter.



A conflict requires a specific current requirement or explicit owner decision that the existing behaviour violates. Record that reference before changing the behaviour. Change only the conflicting aspect where possible and retain compatible capabilities. A cleaner interface can move secondary actions into an appropriate menu, drawer or contextual surface; it does not justify deleting their function. If a state or schema must change, provide and verify a migration that preserves existing data and valid user choices.



Read SOURCE\_PRECEDENCE.md, the current specification and the relevant module sections. Use the current Atlas under design/9to1\_Interface\_Atlas\_v3.3\_Editable.html. Load a retained module's reading map, prerequisites and needed complete module or technical chapters for each increment. Current owner decisions and explicit new overrides supersede conflicting older wording; all unsuperseded architecture, persistence, permission and app contracts remain active. Historical prompts, old deadlines and deployment/process directions are provenance, not fresh execution authority. Keep proposed defaults and fixture data distinct from established contracts and real runtime results.



Deliver the full refactor through complete, reviewable increments. Begin with shared shell/components/routing/state and one real canonical document workflow, then continue through all affected app families and required states. Slices are sequencing and verification gates, not a reduced final scope. Use the agreed accent system, stronger readable text, rounded Professional controls, three-tier Settings hierarchy, unified Home/New tab, compact model picker, quiet Assistants and compact Boards toolbar. Keep Haven tabs left aligned and model-picker tabs centred. Apply specific app contracts such as Canvas's single options host and Data's concrete per-action approvals.



Wire toolbar, context menu and keyboard commands to the same real operation. Implement right-click targeting, focus, responsive layouts, loading/empty/denied/failure states, save/reopen, cancellation and recovery. Preserve canonical identities and existing working service boundaries. Never claim model loading, permission enforcement, file durability, provider success or completed work from fixture values or a visual badge.



Maintain the feature inventory with retained, adapted, replaced and blocked statuses, requirement IDs, Screen IDs and verification evidence. Test both specification compliance and regressions in compatible additional features, including persisted data, backend behaviour, shortcuts and integrations. Review the actual rendered application in the receiving environment and verify relevant runtime outcomes. Report incomplete areas and failed or unavailable checks explicitly; a successful build or screenshot alone is insufficient.



Use normal engineering judgement for reversible details within the agreed contract and continue independent authorised work when one area is blocked. Record genuine unresolved decisions affecting grants, destructive behaviour, data compatibility or external effects precisely. Do not stop after a plan, a prototype or the first successful slice, and do not introduce an unrelated redesign.





<a id="t.4p1ufigmcz0n"></a>

# 10. Sources, Decisions & Coverage

<a id="t.4p1ufigmcz0n--h.fix4qdjk5uup"></a>

# 10.1 - Source precedence and incorporation

**SRC-001** — Read this handoff as one specification: current owner directions, the new narrative/interaction chapters, the exact current Atlas states, and the retained complete modules/technical chapters. The short app chapters are a navigation and current-design layer; they do not replace substantive engine, platform, API, permission, persistence or donor-compatibility requirements with screenshots.

**SRC-002** — Resolve a conflict in this order, within the conflicting clause's actual scope: (1) latest explicit owner instruction; (2) explicit current-spec amendment and current Atlas UI contract; (3) v3.1 amendments and incorporated normative addenda; (4) unsuperseded baseline. A recent screenshot can settle presentation without silently changing storage ownership or granting a new capability. When the current text and picture conflict, record the exact requirement and Screen ID, preserve the latest explicit direction, and correct the stale reference.

**SRC-003** — Omission is not repeal. A cleaner toolbar, closed inspector, simpler selector or missing screenshot does not remove the underlying supported feature. Preserve canonical identities, permissions, authored content, recovery, interoperability and unpictured technical workflows unless an explicit scoped amendment changes them. Do not restore an old duplicate header merely because its historic source paragraph remains in the package. This preservation rule also covers additional functioning features found in the existing codebase that are absent from the written sources. Inventory them, adapt them to the current design and architecture, keep them accessible, and verify their behaviour unless an explicit current requirement or owner decision establishes a conflict; see HANDOFF-007 to HANDOFF-009.

**SRC-004** — Use three independent labels: Established contract, Draft interaction, and Preview/implementation evidence. Owner directions and retained active clauses establish requirements. Newly proposed menu entries, unresolved defaults or runtime handshakes stay labelled Draft. A mockup, parser check, compilation or source-accounting pass is not a production runtime acceptance result.

**SRC-005** — Stable requirement IDs and Screen IDs survive renames. Link each feature to the precise relevant states and retained source module; link Atlas Build notes back to the native specification section. A library-file link opens the file, not a hosted application route. Within the local Atlas, the stable screen identifier resolves through its supported hash/query navigation. Legacy aliases resolve to canonical IDs; for example mail resolves to [connect-mail](design/9to1_Interface_Atlas_v3.3_Editable.html#connect-mail).

**SRC-006** — Historical prompts, agent conversations, operating instructions and old deadlines are source provenance, not fresh execution authority. They cannot authorise deployment, messages, purchases, credential use or destructive actions. The history ledger preserves replaced wording; it is not an alternative active specification.

Acceptance: a receiving implementer can trace any screen to its current contract, owning module and relevant historical source without choosing behaviour from file modification time. A conflict record identifies exactly what changed and what adjacent requirements remain active.



<a id="t.4p1ufigmcz0n--h.3nuwpd5l5vwp"></a>

# 10.2 - 18 November UI override ledger

**SRC-UI-001** — These entries capture the owner's current interface direction. They supersede conflicting older presentation only; all unaffected functional and safety contracts remain incorporated. “Current reference” identifies the relevant Atlas state, not proof that every production interaction is implemented.





<table><tbody><tr><td><p><strong>Area</strong></p></td><td><p><strong>Current direction</strong></p></td><td><p><strong>Current references</strong></p></td></tr><tr><td><p>Shared accents</p></td><td><p>One primary/secondary/tertiary/ghost system derives from the selected palette. Use bright colours like the sidebar blue; secondary must not turn purple merely because an older component hard-coded it. Preserve the user&#x27;s current selection and allow palette choice.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#appearance-palette"><u>appearance-palette</u></a>, components, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options"><u>settings-theme-options</u></a></p></td></tr><tr><td><p>Typography and controls</p></td><td><p>Strengthen message, tab and contextual AI-bar text. Haven tab contents remain left aligned. Use the Professional theme&#x27;s rounded controls; replace the incongruous Light/Dark swatch treatment. Authored document fonts/colours remain independent.</p></td><td><p>appearance, components, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab"><u>host-newtab</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat"><u>spaces-chat</u></a></p></td></tr><tr><td><p>Quiet surfaces</p></td><td><p>Apply the design throughout Dashboard, models, Assistants, Specialists and new-tab surfaces; group the app catalogue with headers. Remove ornamental taglines and repeated explanations while keeping text needed for consequences or recovery. This does not authorise unrelated Explore feature changes.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard"><u>home-dashboard</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-apps"><u>home-apps</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-models"><u>home-models</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-assistants"><u>home-assistants</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-specialists"><u>home-specialists</u></a></p></td></tr><tr><td><p>Composer</p></td><td><p>Remove the visible native HTML resizer/artifact and retain usable resizing/scrolling behaviour. Message text must use the stronger shared weight.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat"><u>spaces-chat</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-compose"><u>spaces-compose</u></a></p></td></tr><tr><td><p>Embedded headers</p></td><td><p>When running in Haven, consolidate duplicate top rows and utilities into the host bar. Settings and corresponding titles use the Home title treatment. Preserve app-specific editing commands without repeating identity or host options.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-general"><u>settings-general</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-editor"><u>present-editor</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#write-continuous"><u>write-continuous</u></a></p></td></tr><tr><td><p>Contextual AI</p></td><td><p>Replace the old AI visibility button with Settings → Chat bar position: Off, Floating, Sidebar. The editor Read-only/Take actions control is a dropdown; font weight matches the stronger bar. Spaces retains its separate conversation-permission contract.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout"><u>settings-layout</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor"><u>boards-editor</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-editor"><u>present-editor</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions"><u>spaces-permissions</u></a></p></td></tr><tr><td><p>Settings rows</p></td><td><p>One option per row, with restrained grouping and clear label/control alignment. The supplied Unsloth image is a layout reference, not a new data/permission model.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-profile"><u>settings-profile</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-appearance"><u>settings-appearance</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-general"><u>settings-general</u></a></p></td></tr><tr><td><p>Settings hierarchy</p></td><td><p>Collapsible text group → existing-style section → indented secondary-accent subsection. Groups are Global, Local, 9to1 Haven and Apps. Global follows the user; Local stays on this device. Avoid awkward repeated arrow branches.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-general"><u>settings-general</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-appearance"><u>settings-appearance</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-ai"><u>settings-ai</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-general"><u>settings-haven-general</u></a></p></td></tr><tr><td><p>Settings subsection navigation</p></td><td><p>Subsections scroll vertically, snap by section and use a shadow to clarify the scroll/sticky boundary. Include Haven subsections; active navigation follows the visible section. Preserve accessible focus/scroll alternatives.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options"><u>settings-theme-options</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-newtab"><u>settings-haven-newtab</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-key-navigation"><u>settings-haven-key-navigation</u></a></p></td></tr><tr><td><p>Model picker</p></td><td><p>Centre Main/Voice/Vision tab labels only within the model picker. Reasoning stays directly exposed; Context, Model and Assistant/Specialist use collapsible headings. Simplify the model rows. Context changes expose Apply and reload; pending selection is not claimed active early.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker"><u>host-model-picker</u></a></p></td></tr><tr><td><p>Browser Tools</p></td><td><p>Expose actual browser options including engine, developer tools, history, bookmarks and reload. Manage page contains page/site permissions and protocol-security information. Include both CUI and HTML inspector reference states.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser"><u>host-browser</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html"><u>host-devtools-html</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui"><u>host-devtools-cui</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-history"><u>host-browser-history</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-bookmarks"><u>host-browser-bookmarks</u></a></p></td></tr><tr><td><p>Home/New tab</p></td><td><p>Merge Home and New tab, remove the fixed Home tab, and add New tab below Explore in Home navigation. Make customisation visual with drag/drop plus accessible move alternatives. Preserve one canonical layout state.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard"><u>home-dashboard</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab"><u>host-newtab</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-customise"><u>host-newtab-customise</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-widgets"><u>host-newtab-widgets</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-explore"><u>home-explore</u></a></p></td></tr><tr><td><p>Tab/edge defects</p></td><td><p>Sidebar navigation must not remove a normal tab&#x27;s close affordance. Remove the unexplained white edge/overflow line from New tab; do not conceal useful scrollbars as a blanket fix.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab"><u>host-newtab</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard"><u>home-dashboard</u></a></p></td></tr><tr><td><p>Assistants and Boards</p></td><td><p>Reduce permanent competing panels and redundant controls. Preserve conversation/document identity and the useful secondary actions through deliberate drawers, menus or contextual surfaces.</p></td><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex"><u>spaces-assistant-alex</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-maya"><u>spaces-assistant-maya</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-rowan"><u>spaces-assistant-rowan</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor"><u>boards-editor</u></a>, <a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context"><u>boards-context</u></a></p></td></tr><tr><td><p>Specification links</p></td><td><p>Expand the new specification in 9to1 with strong links to the mockups and explicit right-click/overflow behaviour, state transitions, permissions, persistence, failure and keyboard access.</p></td><td><p>Every current Screen ID in the index</p></td></tr></tbody></table>





**SRC-UI-002** — Exact layout values and theme tokens come from the current design system/reference. Do not invent a new default palette, chat position, source policy or runtime merely to fill an implementation gap. Existing valid saved choices remain intact. The user's request for a finished handoff also does not transform an incomplete native backend into a verified one.



<a id="t.4p1ufigmcz0n--h.cj9mqmp0yvah"></a>

# 10.3 - Incorporated source inventory

**SRC-INV-001** — The retained modular source pack contains 62 complete modules and 133 technical chapters. Its source ledger accounts for 3,082 source blocks and records retained, amended, historical-process, structural, deferred and proposed material. These are source-retention counts, not implementation completion percentages.





<table><tbody><tr><td><p><strong>Source</strong></p></td><td><p><strong>Preserved text</strong></p></td><td><p><strong>Manifest word count</strong></p></td><td><p><strong>Recorded retrieval</strong></p></td></tr><tr><td><p>v2</p></td><td><p><a href="baseline/retained-pack/source/original_v2.md"><u>baseline/retained-pack/source/original_v2.md</u></a></p></td><td><p>223,194</p></td><td><p>2026-10-28</p></td></tr><tr><td><p>annex</p></td><td><p><a href="baseline/retained-pack/source/professional_annex.md"><u>baseline/retained-pack/source/professional_annex.md</u></a></p></td><td><p>2,304</p></td><td><p>2026-10-28</p></td></tr><tr><td><p>companion</p></td><td><p><a href="baseline/retained-pack/source/spaces_companion.md"><u>baseline/retained-pack/source/spaces_companion.md</u></a></p></td><td><p>19,629</p></td><td><p>2026-10-28</p></td></tr><tr><td><p>study</p></td><td><p><a href="baseline/retained-pack/source/study_annex.md"><u>baseline/retained-pack/source/study_annex.md</u></a></p></td><td><p>3,601</p></td><td><p>2026-10-28</p></td></tr><tr><td><p>scalability</p></td><td><p><a href="baseline/retained-pack/source/scalability_annex.md"><u>baseline/retained-pack/source/scalability_annex.md</u></a></p></td><td><p>1,062</p></td><td><p>2026-10-28</p></td></tr></tbody></table>





The five source texts total 249,790 manifest words. [baseline/retained-pack/catalog/source\_manifest.json](baseline/retained-pack/catalog/source_manifest.json) stores their original SHA-256 values and acquisition records. source\_blocks.json and source\_ledger.json preserve original wording, placement and status; heading\_catalog.json supports navigation. owner\_overrides.json and history/superseded-and-process.md make replaced wording traceable.

**SRC-INV-002 — baseline/9to1\_Development\_Specification\_v3.1.txt contains the recovered 55,336-word v3.1 document text; its heading outline is source/source-v31-outline.json, and the original DOCX is baseline/9to1\_Development\_Specification\_v3.1.docx.** The modular pack carries the more detailed underlying technical clauses. Read the relevant complete module plus its prerequisites before implementing a domain; a module's concise current text is not its entire technical source.

**SRC-INV-003** — Current interface authority is the saved editable Atlas used to produce screens-current.json: 251 unique registered Screen IDs. The older modular pack's catalog/screen\_mapping.json maps 163 historical screens, and its bundled Atlas/screenshots are historical design evidence. The 88 newer registered states are assigned in this index; older screenshot titles do not overwrite the current manifest's titles.

**SRC-INV-004** — The original modular ZIP was extracted as 454 files, 38,253,332 uncompressed bytes, and verified byte-for-byte against the archive; the archive SHA-256 is recorded in retained-source-verification.json. That check establishes extraction integrity. It does not demonstrate that every requirement has been implemented, every source claim independently verified, or every old screenshot updated.

**SRC-INV-005** — The receiving package should include the current specification, current linked Atlas, this route index, complete retained modules, technical chapters, the five original texts, ledgers and source checksums. Preserve relative source paths or update every affected link together. Record final package checksums after link/content integration rather than reusing an input-file checksum for modified output.



<a id="t.4p1ufigmcz0n--h.n36em6gqj5vz"></a>

# 10.4 - Remaining decisions and verification boundaries

**SRC-OPEN-001** — Study's evidence weighting, numerical proficiency thresholds and review intervals remain explicitly open in the incorporated Study material. The four statuses, private attributed evidence, canonical app reuse and deterministic recommendation ordering are established. Do not present invented constants as owner-approved policy.

**SRC-OPEN-002** — Temporary-chat reading of existing durable memory is an unresolved optional capability. No durable writes/reinforcement or automatic Mesh/cloud transfer follows from a temporary session. Preserve the applicable established policy; do not add implicit read authority. Detailed multi-agent turn-taking/human-participation policy also cannot be inferred from a conversation-type label.

**SRC-OPEN-003** — AI Studio Training Lab remains deferred/undefined. Current builder, schema, test, evaluation and permission-simulation requirements remain active; the unimplemented training area does not invalidate them and must not be filled with fictional backend claims.

**SRC-OPEN-004** — Later runtime-backend naming changes reported in the recovered handoff context need reconciliation against the actual repository/adapters. Neither a legacy Strata requirement nor later Thunder/Lightning/Monstrosity names proves shipped capability. Preserve established public runtime ownership and compatibility while resolving exact identities, migration and supported routes.

**SRC-OPEN-005** — Apply and reload is required. The pending/active configuration, cancellation and failure handshake proposed in the runtime chapter needs confirmation against the actual runtime. A control that merely changes a badge is insufficient. Similarly, HTML/CUI inspector reference states do not prove native engine integration or permission/security observations.

**SRC-OPEN-006** — Newly specified context-menu/shortcut details are concrete Draft interactions where the retained sources do not already settle them. Check command IDs, native editing conventions, focus, permissions and target revision during implementation. Do not reinterpret every unspecified pixel or routine engineering choice as a product blocker.

**SRC-VERIFY-001** — Parser/VM checks and screen accounting have bounded meaning. Live visual geometry, narrow layouts, keyboard/screen-reader use, native services, real provider operations and durable recovery require their corresponding implementation evidence. Tab-close coverage verified for Home/New tab must not be advertised as proof of every legacy work-tab close/unsaved-work path.

**SRC-VERIFY-002** — CUI/Dulche ownership, canonical artifacts, scoped permissions, source-backed persistence and Mesh recovery are established in the retained material. Remove earlier blanket “undefined framework/storage” placeholders rather than reopening recovered decisions. Document only genuine conflicts or missing implementation particulars.



<a id="t.4p1ufigmcz0n--h.4pvcrngz3ufl"></a>

# 10.5 - Complete current Atlas coverage

**SRC-IDX-001** — The following index contains every current registered Screen ID exactly once, grouped by product family. Titles come from the current 251-screen manifest. Section keys point to the new owning chapter or an existing native-spec heading alias; retained module paths supply the incorporated functional contract. A screen can be cited elsewhere without acquiring another canonical identity.

**SRC-IDX-002** — Settings app pages belong to Settings for navigation and preference scope, with the corresponding app module additionally referenced. Theme references belong to the shared CUI design contract. Study routes point to the learning chapter, while Spaces retains admission, conversation and orchestration ownership. Build's Code, Sites and Stacks keep their separate retained modules within one project contract.

**SRC-IDX-003** — A row means “specified and linked”, not “backend finished”. Read acceptance and missing-state coverage alongside it. Existing aliases remain compatibility routes and are not duplicated as new screens. The historical 163-row mapping is preserved as provenance; new assignments are explicit extensions, not fabricated historical entries.





<table><tbody><tr><td><p><strong>Screen ID</strong></p></td><td><p><strong>Screen / family</strong></p></td><td><p><strong>Specification section</strong></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#os-go"><u>os-go</u></a></p></td><td><p>9to1OS · Desktop and Go · 9to1OS</p></td><td><p><a href="#t.kixgfsrptana--h.t596isruul3c"><u>8.13 - Terminal, Mini Computer, Admin, 9to1OS and Launcher</u></a></p></td></tr><tr><td><p>admin</p></td><td><p>9to1 Admin · People and policy scope · Admin</p></td><td><p><a href="#t.kixgfsrptana--h.t596isruul3c"><u>8.13 - Terminal, Mini Computer, Admin, 9to1OS and Launcher</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio"><u>ai-studio</u></a></p></td><td><p>AI Studio · Agent builder and test · AI Studio</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#ai-studio-permissions"><u>ai-studio-permissions</u></a></p></td><td><p>AI Studio · Agent tool permissions · AI Studio</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#automation-trigger"><u>automation-trigger</u></a></p></td><td><p>Automations · Schedule trigger · Automations</p></td><td><p><a href="#t.kixgfsrptana--h.z0pj1m8svz8t"><u>8.8 - Planner and Automations</u></a></p></td></tr><tr><td><p>automations</p></td><td><p>Automations · Draft graph and activation · Automations</p></td><td><p><a href="#t.kixgfsrptana--h.z0pj1m8svz8t"><u>8.8 - Planner and Automations</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-add"><u>boards-add</u></a></p></td><td><p>Boards · Add page or section · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.obx9zkn0r4f2"><u>4.2 - Toolbar and secondary controls</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-alignment"><u>boards-alignment</u></a></p></td><td><p>Boards · Alignment dropdown · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.obx9zkn0r4f2"><u>4.2 - Toolbar and secondary controls</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-context"><u>boards-context</u></a></p></td><td><p>Boards · Block actions · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.v9jjd4cfi3uz"><u>4.3 - Selection, modes and context menus</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-editor"><u>boards-editor</u></a></p></td><td><p>Boards · Native page · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.7e3tttoi2joo"><u>4.1 - Boards structure and reading surface</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-file-menu"><u>boards-file-menu</u></a></p></td><td><p>Boards · Board-name file menu · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.obx9zkn0r4f2"><u>4.2 - Toolbar and secondary controls</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-ink"><u>boards-ink</u></a></p></td><td><p>Boards · Draw on the page · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.v9jjd4cfi3uz"><u>4.3 - Selection, modes and context menus</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-insert"><u>boards-insert</u></a></p></td><td><p>Boards · Insert a block · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.obx9zkn0r4f2"><u>4.2 - Toolbar and secondary controls</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-launch"><u>boards-launch</u></a></p></td><td><p>Boards · Start · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.7e3tttoi2joo"><u>4.1 - Boards structure and reading surface</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-list-options"><u>boards-list-options</u></a></p></td><td><p>Boards · List options · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.obx9zkn0r4f2"><u>4.2 - Toolbar and secondary controls</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-properties"><u>boards-properties</u></a></p></td><td><p>Boards · Contextual properties · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.obx9zkn0r4f2"><u>4.2 - Toolbar and secondary controls</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-recovery"><u>boards-recovery</u></a></p></td><td><p>Boards · Save recovery · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.efj52r5nvftr"><u>4.4 - Saving, recovery and acceptance</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#boards-typing"><u>boards-typing</u></a></p></td><td><p>Boards · Typing within a block · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.v9jjd4cfi3uz"><u>4.3 - Selection, modes and context menus</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-boards"><u>mobile-boards</u></a></p></td><td><p>Mobile · Boards page drawer · Boards</p></td><td><p><a href="#t.rd64lp4djv5h--h.7e3tttoi2joo"><u>4.1 - Boards structure and reading surface</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#build-code"><u>build-code</u></a></p></td><td><p>Build · Code workspace · Build</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#build-code-diagnostics"><u>build-code-diagnostics</u></a></p></td><td><p>Build · Completion and diagnostics · Build</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#build-sites"><u>build-sites</u></a></p></td><td><p>Build · Sites editor · Build</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#build-sites-responsive"><u>build-sites-responsive</u></a></p></td><td><p>Build Sites · Mobile breakpoint · Build</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#build-stacks"><u>build-stacks</u></a></p></td><td><p>Build · Stacks conflict review · Build</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#build-stacks-freeze"><u>build-stacks-freeze</u></a></p></td><td><p>Build Stacks · Freeze scope · Build</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-ai"><u>canvas-ai</u></a></p></td><td><p>Canvas · Contextual AI · Canvas</p></td><td><p><a href="#t.kixgfsrptana--h.zhah9zypzpqi"><u>8.4 - Canvas and shared shapes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-editor"><u>canvas-editor</u></a></p></td><td><p>Canvas · Drawing workspace · Canvas</p></td><td><p><a href="#t.kixgfsrptana--h.zhah9zypzpqi"><u>8.4 - Canvas and shared shapes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-insert"><u>canvas-insert</u></a></p></td><td><p>Canvas · Insert shapes · Canvas</p></td><td><p><a href="#t.kixgfsrptana--h.zhah9zypzpqi"><u>8.4 - Canvas and shared shapes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-launch"><u>canvas-launch</u></a></p></td><td><p>Canvas · Start · Canvas</p></td><td><p><a href="#t.kixgfsrptana--h.zhah9zypzpqi"><u>8.4 - Canvas and shared shapes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-pen-presets"><u>canvas-pen-presets</u></a></p></td><td><p>Canvas · Pen presets · Canvas</p></td><td><p><a href="#t.kixgfsrptana--h.zhah9zypzpqi"><u>8.4 - Canvas and shared shapes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-selection"><u>canvas-selection</u></a></p></td><td><p>Canvas · Shape selection &amp; layers · Canvas</p></td><td><p><a href="#t.kixgfsrptana--h.zhah9zypzpqi"><u>8.4 - Canvas and shared shapes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#canvas-shape-builder"><u>canvas-shape-builder</u></a></p></td><td><p>Canvas · Custom shape builder · Canvas</p></td><td><p><a href="#t.kixgfsrptana--h.zhah9zypzpqi"><u>8.4 - Canvas and shared shapes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#cards-edit"><u>cards-edit</u></a></p></td><td><p>Cards · Rich card editor · Cards</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#cards-review"><u>cards-review</u></a></p></td><td><p>Cards · Focused review · Cards</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#cards-study"><u>cards-study</u></a></p></td><td><p>Cards · Study overview · Cards</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#connect-compose"><u>connect-compose</u></a></p></td><td><p>Connect · Compose a message · Connect</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#connect-mail"><u>connect-mail</u></a></p></td><td><p>Connect · Mail and a reply draft · Connect</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#connect-messages"><u>connect-messages</u></a></p></td><td><p>Connect · Team conversation · Connect</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#connect-schedule"><u>connect-schedule</u></a></p></td><td><p>Connect · Schedule send review · Connect</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p>data</p></td><td><p>Data · Spreadsheet · Data</p></td><td><p><a href="#t.kixgfsrptana--h.x06anm3e9h0j"><u>8.6 - Data and Forms</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#data-approval"><u>data-approval</u></a></p></td><td><p>Data · Review an AI mutation · Data</p></td><td><p><a href="#t.kixgfsrptana--h.x06anm3e9h0j"><u>8.6 - Data and Forms</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#data-chart"><u>data-chart</u></a></p></td><td><p>Data · Chart insertion and preview · Data</p></td><td><p><a href="#t.kixgfsrptana--h.x06anm3e9h0j"><u>8.6 - Data and Forms</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#data-formula"><u>data-formula</u></a></p></td><td><p>Data · Editing a formula · Data</p></td><td><p><a href="#t.kixgfsrptana--h.x06anm3e9h0j"><u>8.6 - Data and Forms</u></a></p></td></tr><tr><td><p>appearance</p></td><td><p>Appearance · Theme and accent preferences · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#appearance-palette"><u>appearance-palette</u></a></p></td><td><p>Appearance · Accent palette · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p>components</p></td><td><p>Components · Buttons, inputs and control states · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#custom-theme"><u>custom-theme</u></a></p></td><td><p>Create Theme · Authoring and preview · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p>states</p></td><td><p>States · Recovery and persistence reference · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-bubble"><u>theme-bubble</u></a></p></td><td><p>Bubble · Material and controls · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-frosted"><u>theme-frosted</u></a></p></td><td><p>Frosted Glass · Corrected material and controls · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-glow"><u>theme-glow</u></a></p></td><td><p>Glow · Corrected material and controls · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-playful"><u>theme-playful</u></a></p></td><td><p>Playful · Corrected material and controls · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-bubble"><u>theme-reference-bubble</u></a></p></td><td><p>Theme refs · Bubble · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-frost"><u>theme-reference-frost</u></a></p></td><td><p>Theme refs · Frost · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-glow"><u>theme-reference-glow</u></a></p></td><td><p>Theme refs · Glow · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-industrial"><u>theme-reference-industrial</u></a></p></td><td><p>Theme refs · Industrial · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-playful"><u>theme-reference-playful</u></a></p></td><td><p>Theme refs · Playful · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-professional"><u>theme-reference-professional</u></a></p></td><td><p>Theme refs · Professional · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-retro"><u>theme-reference-retro</u></a></p></td><td><p>Theme refs · Retro · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-snug"><u>theme-reference-snug</u></a></p></td><td><p>Theme refs · Snug · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-synthwave"><u>theme-reference-synthwave</u></a></p></td><td><p>Theme refs · Synthwave · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-reference-western"><u>theme-reference-western</u></a></p></td><td><p>Theme refs · Western · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-retro"><u>theme-retro</u></a></p></td><td><p>Retro · Material and controls · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#theme-western"><u>theme-western</u></a></p></td><td><p>Western · Material and controls · Design system</p></td><td><p><a href="#t.0--h.87c0ypc2w1q6"><u>1.3 - Shared product rules</u></a></p></td></tr><tr><td><p>files</p></td><td><p>Files · Local files and details · Files</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#files-context"><u>files-context</u></a></p></td><td><p>Files · Selected file actions · Files</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#files-grid"><u>files-grid</u></a></p></td><td><p>Files · Grid and selected board · Files</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#files-organisation"><u>files-organisation</u></a></p></td><td><p>Files · Sheet and organisation types · Files</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p>forms</p></td><td><p>Forms · Assessment builder · Forms</p></td><td><p><a href="#t.kixgfsrptana--h.x06anm3e9h0j"><u>8.6 - Data and Forms</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#forms-preview"><u>forms-preview</u></a></p></td><td><p>Forms · Respondent preview · Forms</p></td><td><p><a href="#t.kixgfsrptana--h.x06anm3e9h0j"><u>8.6 - Data and Forms</u></a></p></td></tr><tr><td><p>games</p></td><td><p>Games · Scene and components · Games</p></td><td><p><a href="#t.kixgfsrptana--h.tu49h9cciub6"><u>8.10 - Build, AI Studio and Games</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser"><u>host-browser</u></a></p></td><td><p>9to1 · Browser · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>2.3.4 - Browser tools, Manage page and inspectors</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-bookmarks"><u>host-browser-bookmarks</u></a></p></td><td><p>Haven · Bookmarks · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>2.3.4 - Browser tools, Manage page and inspectors</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-browser-history"><u>host-browser-history</u></a></p></td><td><p>Haven · History · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>2.3.4 - Browser tools, Manage page and inspectors</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-cui"><u>host-devtools-cui</u></a></p></td><td><p>Haven · CUI inspector · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>2.3.4 - Browser tools, Manage page and inspectors</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-devtools-html"><u>host-devtools-html</u></a></p></td><td><p>Haven · HTML inspector · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.cx3hialkcypo"><u>2.3.4 - Browser tools, Manage page and inspectors</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-model-picker"><u>host-model-picker</u></a></p></td><td><p>9to1 · Model picker · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.xrco6lv5bzqg"><u>2.3.3 - Model picker</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab"><u>host-newtab</u></a></p></td><td><p>9to1 · New Tab · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.mz808bphuj0r"><u>2.3.2 - Home and New tab</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-customise"><u>host-newtab-customise</u></a></p></td><td><p>New Tab · Customisation · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.mz808bphuj0r"><u>2.3.2 - Home and New tab</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-newtab-widgets"><u>host-newtab-widgets</u></a></p></td><td><p>New Tab · Add a widget · Haven</p></td><td><p><a href="#t.8lnv8r5vlfea--h.mz808bphuj0r"><u>2.3.2 - Home and New tab</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#host-notifications"><u>host-notifications</u></a></p></td><td><p>9to1 · Notifications · Haven</p></td><td><p><a href="#t.usni9fu0ls33--h.9ao1yopveqs1"><u>9.1 - Targeting, menus and keyboard lifecycle</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-apps"><u>home-apps</u></a></p></td><td><p>Home · Apps · Home</p></td><td><p><a href="#t.kixgfsrptana--h.xbw5wig84iiy"><u>8.1 - Shared app behaviour</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-assistants"><u>home-assistants</u></a></p></td><td><p>Home · Assistants · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.rzxe6ixb0y4j"><u>7.11 - Den, Assistant identity and memory</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-dashboard"><u>home-dashboard</u></a></p></td><td><p>Home · New tab · Home</p></td><td><p><a href="#t.8lnv8r5vlfea--h.mz808bphuj0r"><u>2.3.2 - Home and New tab</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-explore"><u>home-explore</u></a></p></td><td><p>Home · Explore · Home</p></td><td><p><a href="#t.8lnv8r5vlfea--h.mz808bphuj0r"><u>2.3.2 - Home and New tab</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-library"><u>home-library</u></a></p></td><td><p>Home · Library · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.f3jjjlqsl0qs"><u>7.7 - Files, Library and organisational views</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-mesh"><u>home-mesh</u></a></p></td><td><p>Home · Mesh &amp; devices · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.8sb8vkcpiy4v"><u>7.8 - Mesh, replication and remote execution</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-model-details"><u>home-model-details</u></a></p></td><td><p>Models · Capability and requirements · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.s3dg4saz9mjr"><u>7.10 - Models, providers and route lifecycle</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-models"><u>home-models</u></a></p></td><td><p>Models · Discover · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.s3dg4saz9mjr"><u>7.10 - Models, providers and route lifecycle</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-models-installed"><u>home-models-installed</u></a></p></td><td><p>Models · Installed and saved · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.s3dg4saz9mjr"><u>7.10 - Models, providers and route lifecycle</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#home-specialists"><u>home-specialists</u></a></p></td><td><p>Home · Specialists · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.rzxe6ixb0y4j"><u>7.11 - Den, Assistant identity and memory</u></a></p></td></tr><tr><td><p>onboarding</p></td><td><p>9to1 · Welcome · Home</p></td><td><p><a href="#t.pa5ml7516cye--h.jh44mj4ez0pc"><u>7.5 - Identity, account and resource authority</u></a></p></td></tr><tr><td><p>launcher</p></td><td><p>Launcher · Mobile home · Launcher</p></td><td><p><a href="#t.kixgfsrptana--h.t596isruul3c"><u>8.13 - Terminal, Mini Computer, Admin, 9to1OS and Launcher</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#link-binder"><u>link-binder</u></a></p></td><td><p>Link · Binder overview and detail · Link</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#link-binder-insert"><u>link-binder-insert</u></a></p></td><td><p>Link Binder · Add content · Link</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#link-flow"><u>link-flow</u></a></p></td><td><p>Link · Continuous Flow document · Link</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#link-flow-block-edit"><u>link-flow-block-edit</u></a></p></td><td><p>Link Flow · Source section and placement · Link</p></td><td><p><a href="#t.kixgfsrptana--h.il23drmnilho"><u>8.7 - Files and Link</u></a></p></td></tr><tr><td><p>maps</p></td><td><p>Maps · Route and place details · Maps</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#maps-route-options"><u>maps-route-options</u></a></p></td><td><p>Maps · Route options and stops · Maps</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#mini-computer"><u>mini-computer</u></a></p></td><td><p>Mini Computer · Virtual machine · Mini Computer</p></td><td><p><a href="#t.kixgfsrptana--h.t596isruul3c"><u>8.13 - Terminal, Mini Computer, Admin, 9to1OS and Launcher</u></a></p></td></tr><tr><td><p>motion</p></td><td><p>Motion · Video editing workspace · Motion</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#motion-export"><u>motion-export</u></a></p></td><td><p>Motion · Render range and destination · Motion</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#motion-transition"><u>motion-transition</u></a></p></td><td><p>Motion · Transition at an edit · Motion</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p>paths</p></td><td><p>Paths · Branching learning route · Paths</p></td><td><p><a href="#t.kixgfsrptana--h.fct8yarsvzvu"><u>8.12 - Prototype, Paths and Replay</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#paths-branch-rule"><u>paths-branch-rule</u></a></p></td><td><p>Paths · Edit a route condition · Paths</p></td><td><p><a href="#t.kixgfsrptana--h.fct8yarsvzvu"><u>8.12 - Prototype, Paths and Replay</u></a></p></td></tr><tr><td><p>picture</p></td><td><p>Picture · Layered composition · Picture</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#picture-export"><u>picture-export</u></a></p></td><td><p>Picture · Export image · Picture</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#picture-mask"><u>picture-mask</u></a></p></td><td><p>Picture · Layer mask editing · Picture</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p>planner</p></td><td><p>Planner · Week and unscheduled tasks · Planner</p></td><td><p><a href="#t.kixgfsrptana--h.z0pj1m8svz8t"><u>8.8 - Planner and Automations</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#planner-agenda"><u>planner-agenda</u></a></p></td><td><p>Planner · Agenda view · Planner</p></td><td><p><a href="#t.kixgfsrptana--h.z0pj1m8svz8t"><u>8.8 - Planner and Automations</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#planner-event"><u>planner-event</u></a></p></td><td><p>Planner · Edit an event · Planner</p></td><td><p><a href="#t.kixgfsrptana--h.z0pj1m8svz8t"><u>8.8 - Planner and Automations</u></a></p></td></tr><tr><td><p>play</p></td><td><p>Play · Human and Agent quiz match · Play</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#collaboration-review"><u>collaboration-review</u></a></p></td><td><p>Present · Review an AI change · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#collaborators-picker"><u>collaborators-picker</u></a></p></td><td><p>Present · Invite collaborators · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-actions"><u>present-actions</u></a></p></td><td><p>Present · Actions graph · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-collaborators"><u>present-collaborators</u></a></p></td><td><p>Present · Working with collaborators · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-editor"><u>present-editor</u></a></p></td><td><p>Present · Editor · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-insert"><u>present-insert</u></a></p></td><td><p>Present · Insert gallery · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-launch"><u>present-launch</u></a></p></td><td><p>Present · Start · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-presenter"><u>present-presenter</u></a></p></td><td><p>Present · Presenter view · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-selection"><u>present-selection</u></a></p></td><td><p>Present · Selected object &amp; notes · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-shape-builder"><u>present-shape-builder</u></a></p></td><td><p>Present · Custom shape builder · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#present-transitions"><u>present-transitions</u></a></p></td><td><p>Present · Transitions · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p>variants</p></td><td><p>Present · Compare Variants · Present</p></td><td><p><a href="#t.kixgfsrptana--h.h0ppxfr4ay3a"><u>8.3 - Present</u></a></p></td></tr><tr><td><p>prototype</p></td><td><p>Prototype · Interactive spring explorer · Prototype</p></td><td><p><a href="#t.kixgfsrptana--h.fct8yarsvzvu"><u>8.12 - Prototype, Paths and Replay</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#prototype-parameters"><u>prototype-parameters</u></a></p></td><td><p>Prototype · Edit a typed input · Prototype</p></td><td><p><a href="#t.kixgfsrptana--h.fct8yarsvzvu"><u>8.12 - Prototype, Paths and Replay</u></a></p></td></tr><tr><td><p>replay</p></td><td><p>Replay · Historical checkpoint · Replay</p></td><td><p><a href="#t.kixgfsrptana--h.fct8yarsvzvu"><u>8.12 - Prototype, Paths and Replay</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#replay-event-detail"><u>replay-event-detail</u></a></p></td><td><p>Replay · Inspect a recorded change · Replay</p></td><td><p><a href="#t.kixgfsrptana--h.fct8yarsvzvu"><u>8.12 - Prototype, Paths and Replay</u></a></p></td></tr><tr><td><p>account</p></td><td><p>Settings · Account &amp; subscription · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-accessibility"><u>settings-accessibility</u></a></p></td><td><p>Settings · Accessibility · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.5612xasawprh"><u>5.3 - Appearance and contextual AI</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-account"><u>settings-account</u></a></p></td><td><p>Settings · Account &amp; subscription · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-account-storage"><u>settings-account-storage</u></a></p></td><td><p>Settings · Storage · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-advanced"><u>settings-advanced</u></a></p></td><td><p>Settings · Advanced · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-ai"><u>settings-ai</u></a></p></td><td><p>Settings · AI · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-admin"><u>settings-app-admin</u></a></p></td><td><p>Settings · Admin · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-ai-studio"><u>settings-app-ai-studio</u></a></p></td><td><p>Settings · AI Studio · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-automations"><u>settings-app-automations</u></a></p></td><td><p>Settings · Automations · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-boards"><u>settings-app-boards</u></a></p></td><td><p>Settings · Boards · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-build"><u>settings-app-build</u></a></p></td><td><p>Settings · Build · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-canvas"><u>settings-app-canvas</u></a></p></td><td><p>Settings · Canvas · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-cards"><u>settings-app-cards</u></a></p></td><td><p>Settings · Cards · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-connect"><u>settings-app-connect</u></a></p></td><td><p>Settings · Connect · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-data"><u>settings-app-data</u></a></p></td><td><p>Settings · Data · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-files"><u>settings-app-files</u></a></p></td><td><p>Settings · Files · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-forms"><u>settings-app-forms</u></a></p></td><td><p>Settings · Forms · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-games"><u>settings-app-games</u></a></p></td><td><p>Settings · Games · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-launcher"><u>settings-app-launcher</u></a></p></td><td><p>Settings · Launcher · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-link"><u>settings-app-link</u></a></p></td><td><p>Settings · Link · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-maps"><u>settings-app-maps</u></a></p></td><td><p>Settings · Maps · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-mini-computer"><u>settings-app-mini-computer</u></a></p></td><td><p>Settings · Mini Computer · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-motion"><u>settings-app-motion</u></a></p></td><td><p>Settings · Motion · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-paths"><u>settings-app-paths</u></a></p></td><td><p>Settings · Paths · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-picture"><u>settings-app-picture</u></a></p></td><td><p>Settings · Picture · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-planner"><u>settings-app-planner</u></a></p></td><td><p>Settings · Planner · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-play"><u>settings-app-play</u></a></p></td><td><p>Settings · Play · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-present"><u>settings-app-present</u></a></p></td><td><p>Settings · Present · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-prototype"><u>settings-app-prototype</u></a></p></td><td><p>Settings · Prototype · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-replay"><u>settings-app-replay</u></a></p></td><td><p>Settings · Replay · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-shelf"><u>settings-app-shelf</u></a></p></td><td><p>Settings · Shelf · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-spaces"><u>settings-app-spaces</u></a></p></td><td><p>Settings · Spaces · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-terminal"><u>settings-app-terminal</u></a></p></td><td><p>Settings · Terminal · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-vision-and-voice"><u>settings-app-vision-and-voice</u></a></p></td><td><p>Settings · Vision &amp; Voice · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-wave"><u>settings-app-wave</u></a></p></td><td><p>Settings · Wave · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-app-write"><u>settings-app-write</u></a></p></td><td><p>Settings · Write · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-appearance"><u>settings-appearance</u></a></p></td><td><p>Settings · Appearance · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.5612xasawprh"><u>5.3 - Appearance and contextual AI</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-apps"><u>settings-apps</u></a></p></td><td><p>Settings · Apps &amp; updates · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-apps-shared"><u>settings-apps-shared</u></a></p></td><td><p>Settings · Shared settings · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-general"><u>settings-general</u></a></p></td><td><p>Settings · General · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-bookmarks"><u>settings-haven-bookmarks</u></a></p></td><td><p>Settings · Bookmarks · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-general"><u>settings-haven-general</u></a></p></td><td><p>Settings · General · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-history"><u>settings-haven-history</u></a></p></td><td><p>Settings · History · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-key-menus"><u>settings-haven-key-menus</u></a></p></td><td><p>Settings · Menus &amp; focus · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-key-navigation"><u>settings-haven-key-navigation</u></a></p></td><td><p>Settings · Navigation · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-model-memory"><u>settings-haven-model-memory</u></a></p></td><td><p>Settings · Models &amp; memory · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-newtab"><u>settings-haven-newtab</u></a></p></td><td><p>Settings · New Tab · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-performance"><u>settings-haven-performance</u></a></p></td><td><p>Settings · Performance · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-pinned"><u>settings-haven-pinned</u></a></p></td><td><p>Settings · Pinned destinations · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-recent-files"><u>settings-haven-recent-files</u></a></p></td><td><p>Settings · Recent files · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-services"><u>settings-haven-services</u></a></p></td><td><p>Settings · Services · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-shortcut-order"><u>settings-haven-shortcut-order</u></a></p></td><td><p>Settings · Shortcut order · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-shortcuts"><u>settings-haven-shortcuts</u></a></p></td><td><p>Settings · Keyboard Shortcuts · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-storage-cache"><u>settings-haven-storage-cache</u></a></p></td><td><p>Settings · Storage &amp; cache · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-tab-history"><u>settings-haven-tab-history</u></a></p></td><td><p>Settings · Back &amp; forward · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-haven-tabs-address"><u>settings-haven-tabs-address</u></a></p></td><td><p>Settings · Tabs &amp; address · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-installed-models"><u>settings-installed-models</u></a></p></td><td><p>Settings · Installed Models · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-layout"><u>settings-layout</u></a></p></td><td><p>Settings · Layout · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.5612xasawprh"><u>5.3 - Appearance and contextual AI</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-local-general"><u>settings-local-general</u></a></p></td><td><p>Settings · General · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-mesh"><u>settings-mesh</u></a></p></td><td><p>Settings · Mesh &amp; devices · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-models"><u>settings-models</u></a></p></td><td><p>Settings · AI &amp; models · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-notifications"><u>settings-notifications</u></a></p></td><td><p>Settings · Notifications · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-permissions"><u>settings-permissions</u></a></p></td><td><p>Settings · Permissions &amp; trust · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-personalisation"><u>settings-personalisation</u></a></p></td><td><p>Settings · Personalisation · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-plugins"><u>settings-plugins</u></a></p></td><td><p>Settings · Plugins/MCPs · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-privacy"><u>settings-privacy</u></a></p></td><td><p>Settings · Privacy · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-profile"><u>settings-profile</u></a></p></td><td><p>Settings · Profile · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-provider"><u>settings-provider</u></a></p></td><td><p>Settings · Provider access detail · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-providers"><u>settings-providers</u></a></p></td><td><p>Settings · Providers · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-sandbox"><u>settings-sandbox</u></a></p></td><td><p>Settings · Sandbox · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-security"><u>settings-security</u></a></p></td><td><p>Settings · Security &amp; Privacy · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-security-general"><u>settings-security-general</u></a></p></td><td><p>Settings · General · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-skills"><u>settings-skills</u></a></p></td><td><p>Settings · Skills · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-storage"><u>settings-storage</u></a></p></td><td><p>Settings · Storage · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-subscriptions"><u>settings-subscriptions</u></a></p></td><td><p>Settings · Subscriptions · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-theme-options"><u>settings-theme-options</u></a></p></td><td><p>Settings · Theme Options · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.5612xasawprh"><u>5.3 - Appearance and contextual AI</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#settings-voice"><u>settings-voice</u></a></p></td><td><p>Settings · Voice · Settings</p></td><td><p><a href="#t.m73tfqsiwjxb--h.56uyf3qhxnd4"><u>5.1 - Settings navigation and scopes</u></a></p></td></tr><tr><td><p>shelf</p></td><td><p>Shelf · Curated launch collection · Shelf</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#shelf-add"><u>shelf-add</u></a></p></td><td><p>Shelf · Add an item · Shelf</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-chat"><u>mobile-chat</u></a></p></td><td><p>Mobile · Spaces conversation · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-permissions"><u>mobile-permissions</u></a></p></td><td><p>Mobile · Conversation permissions · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-alex"><u>spaces-assistant-alex</u></a></p></td><td><p>Assistants · Alex Main · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-maya"><u>spaces-assistant-maya</u></a></p></td><td><p>Assistants · Maya Main · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistant-rowan"><u>spaces-assistant-rowan</u></a></p></td><td><p>Assistants · Rowan Main · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-assistants"><u>spaces-assistants</u></a></p></td><td><p>Assistants · Persistent collaborators · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-attachments"><u>spaces-attachments</u></a></p></td><td><p>Spaces · Attach menu · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-canvas-working"><u>spaces-canvas-working</u></a></p></td><td><p>Spaces · Canvas work in progress · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.e5y9rooz8xbx"><u>3.7 - Subagents, generated UI and durable outputs</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-chat"><u>spaces-chat</u></a></p></td><td><p>Spaces · Conversation · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.4mky232lqwbj"><u>3.5 - Conversation types, organisation and Space configuration</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-compose"><u>spaces-compose</u></a></p></td><td><p>Spaces · Draft and contextual shelf · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-emoji"><u>spaces-emoji</u></a></p></td><td><p>Spaces · Native emoji picker · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-experiences"><u>spaces-experiences</u></a></p></td><td><p>Spaces · Experiences · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.ueir6qjn74g"><u>3.6 - Task-specific Spaces and learning entry points</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-export-recovery"><u>spaces-export-recovery</u></a></p></td><td><p>Spaces · Export failure and recovery · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.e5y9rooz8xbx"><u>3.7 - Subagents, generated UI and durable outputs</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generated-files"><u>spaces-generated-files</u></a></p></td><td><p>Spaces · Generated files and outputs · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.e5y9rooz8xbx"><u>3.7 - Subagents, generated UI and durable outputs</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-generative-ui"><u>spaces-generative-ui</u></a></p></td><td><p>Spaces · Generated interactive planner · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.e5y9rooz8xbx"><u>3.7 - Subagents, generated UI and durable outputs</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-manage"><u>spaces-manage</u></a></p></td><td><p>Spaces · Settings and permission scope · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.4mky232lqwbj"><u>3.5 - Conversation types, organisation and Space configuration</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-permissions"><u>spaces-permissions</u></a></p></td><td><p>Spaces · Conversation permissions · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.4mky232lqwbj"><u>3.5 - Conversation types, organisation and Space configuration</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-present-working"><u>spaces-present-working</u></a></p></td><td><p>Spaces · Presentation work in progress · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.e5y9rooz8xbx"><u>3.7 - Subagents, generated UI and durable outputs</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-research"><u>spaces-research</u></a></p></td><td><p>Research · Plan, sources and outputs · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.ueir6qjn74g"><u>3.6 - Task-specific Spaces and learning entry points</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-running"><u>spaces-running</u></a></p></td><td><p>Tasks · Run summary and approval · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.ueir6qjn74g"><u>3.6 - Task-specific Spaces and learning entry points</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-shopping"><u>spaces-shopping</u></a></p></td><td><p>Shopping · Requirements and comparison · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.ueir6qjn74g"><u>3.6 - Task-specific Spaces and learning entry points</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-specialists"><u>spaces-specialists</u></a></p></td><td><p>Spaces · Specialists · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.4mky232lqwbj"><u>3.5 - Conversation types, organisation and Space configuration</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagent-detail"><u>spaces-subagent-detail</u></a></p></td><td><p>Spaces · Subagent scope and activity · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.e5y9rooz8xbx"><u>3.7 - Subagents, generated UI and durable outputs</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-subagents"><u>spaces-subagents</u></a></p></td><td><p>Spaces · Coordinated subagents · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.e5y9rooz8xbx"><u>3.7 - Subagents, generated UI and durable outputs</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-thinking"><u>spaces-thinking</u></a></p></td><td><p>Spaces · Thinking and visible activity · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.9ep550j3d1do"><u>3.4 - Composer, admission and running work</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-translate"><u>spaces-translate</u></a></p></td><td><p>Translate · Conversational localisation · Spaces</p></td><td><p><a href="#t.snkmezredh0m--h.ueir6qjn74g"><u>3.6 - Task-specific Spaces and learning entry points</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#mobile-study"><u>mobile-study</u></a></p></td><td><p>Mobile · Study dashboard · Study</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#spaces-study"><u>spaces-study</u></a></p></td><td><p>Study · Learning dashboard · Study</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#study-session"><u>study-session</u></a></p></td><td><p>Study · Session modes and source policy · Study</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#study-subject"><u>study-subject</u></a></p></td><td><p>Study · Maths topic tracker · Study</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#study-topic"><u>study-topic</u></a></p></td><td><p>Study · Lesson and Revision Bank · Study</p></td><td><p><a href="#t.kixgfsrptana--h.n4c00hofq37q"><u>8.5 - Study and Cards</u></a></p></td></tr><tr><td><p>terminal</p></td><td><p>Terminal · Command workspace · Terminal</p></td><td><p><a href="#t.kixgfsrptana--h.t596isruul3c"><u>8.13 - Terminal, Mini Computer, Admin, 9to1OS and Launcher</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#terminal-command-review"><u>terminal-command-review</u></a></p></td><td><p>Terminal · Review an AI command · Terminal</p></td><td><p><a href="#t.kixgfsrptana--h.t596isruul3c"><u>8.13 - Terminal, Mini Computer, Admin, 9to1OS and Launcher</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#vision-session-sources"><u>vision-session-sources</u></a></p></td><td><p>Vision &amp; Voice · Sources and capture · Vision &amp; Voice</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#vision-voice"><u>vision-voice</u></a></p></td><td><p>Vision &amp; Voice · Live session · Vision &amp; Voice</p></td><td><p><a href="#t.kixgfsrptana--h.ic10k7u4zffb"><u>8.11 - Connect, Vision &amp; Voice, Maps, Shelf and Play</u></a></p></td></tr><tr><td><p>wave</p></td><td><p>Wave · Audio timeline and mixer · Wave</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#wave-clip"><u>wave-clip</u></a></p></td><td><p>Wave · Selected clip and fades · Wave</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#wave-stems"><u>wave-stems</u></a></p></td><td><p>Wave · Split stems · Wave</p></td><td><p><a href="#t.kixgfsrptana--h.jkq97rwo95g9"><u>8.9 - Picture, Wave and Motion</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#write-collaborators"><u>write-collaborators</u></a></p></td><td><p>Write · Working with collaborators · Write</p></td><td><p><a href="#t.kixgfsrptana--h.igvu38mz15e3"><u>8.2 - Write</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#write-collaborators-picker"><u>write-collaborators-picker</u></a></p></td><td><p>Write · Invite collaborators · Write</p></td><td><p><a href="#t.kixgfsrptana--h.igvu38mz15e3"><u>8.2 - Write</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#write-continuous"><u>write-continuous</u></a></p></td><td><p>Write · Continuous View · Write</p></td><td><p><a href="#t.kixgfsrptana--h.igvu38mz15e3"><u>8.2 - Write</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#write-editor"><u>write-editor</u></a></p></td><td><p>Write · Page View · Write</p></td><td><p><a href="#t.kixgfsrptana--h.igvu38mz15e3"><u>8.2 - Write</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#write-launch"><u>write-launch</u></a></p></td><td><p>Write · Start · Write</p></td><td><p><a href="#t.kixgfsrptana--h.igvu38mz15e3"><u>8.2 - Write</u></a></p></td></tr><tr><td><p><a href="design/9to1_Interface_Atlas_v3.3_Editable.html#write-review"><u>write-review</u></a></p></td><td><p>Write · Selection &amp; review · Write</p></td><td><p><a href="#t.kixgfsrptana--h.igvu38mz15e3"><u>8.2 - Write</u></a></p></td></tr></tbody></table>




