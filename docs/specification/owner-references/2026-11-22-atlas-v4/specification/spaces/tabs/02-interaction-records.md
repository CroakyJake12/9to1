# Interaction records

Source: https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE?tab=t.4u1kzupu761c

<a id="h.osru4z4mlro1"></a>

# Spaces interaction records

This catalogue expands the canonical Spaces chapter. Atlas screen labels and later refinement scripts establish shown design; control mechanics beyond the prototype are explicitly adopted implementation requirements. Static source/render inventory is not native or backend acceptance. Shared patterns [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6)–015 govern standard activation, fields, menus, selection, drag, dialogs, sliders, focus, touch, async commands, numeric editing, links and reference-only samples; every record below supplies its specific target and outcome. Unless a record states a distinct double/middle-click action, standard buttons have none and repeated activation must not duplicate an async effect. Study, Translate and Actions remain Spaces. Example identities, prices, marks and progress in Atlas are fixtures. Supplementary source-backed or owner-requested surfaces without a registered Atlas screen use an empty screens list. Literal legacy Atlas labels are retained only as observed evidence; current product labels use the canonical taxonomy.

Authority: [Master specification](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk); [pinned Interface Atlas v3.3](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html). Owner additions and the detailed gestures specified here are target requirements; static Atlas markup is design evidence, not proof of native execution. Related application/runtime requirements in the master remain in force.

Reading a record: Primary covers left click and activation; Secondary covers right click and contextual actions; Drag covers sources, targets and cancellation; Keyboard and touch supplies alternatives; State and integration defines scope, persistence and failure. Shared IX-BASE patterns apply unless the control record explicitly refines them. Acceptance describes required verification, not tests already performed.

<a id="h.rf8587emfxvz"></a>

### IX-SPACES-001 — Open and dismiss the shared model picker

Controls: Choose a model; .host-model; .hx-model-picker

Atlas surfaces: host-model-picker, spaces-permissions, spaces-emoji, spaces-chat, spaces-compose, spaces-attachments, spaces-running, spaces-specialists, spaces-assistants, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-study, study-subject, study-topic, study-session, spaces-research, spaces-shopping, spaces-manage, spaces-translate, spaces-generative-ui, spaces-thinking, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, spaces-experiences, home-assistants, home-specialists

Primary: Single click Choose a model opens the compact picker for the current conversation/session and retained role. It closes an open Context popup first. A second click dismisses the picker. Opening does not select a model, load weights or admit work; the trigger continues to show the confirmed Main selection and requested effort.

Secondary: No custom menu on the trigger. Secondary click does not change model or open a second picker. Double/middle click has no separate activation beyond the shared button pattern.

Drag: Trigger and popup are not draggable. Dragging within a search field selects text; scrolling the option list must not become host-window drag.

Keyboard and touch: Use [IX-BASE-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.g4q68g51ix5k)/009: focus enters the retained role tab or first meaningful control. Escape dismisses the topmost picker and restores trigger focus. Touch activation opens the same compact surface.

State: Loading discovery leaves selected identity visible. If it disappears or access is revoked, show Unavailable rather than choosing the first row. Dismissal preserves per-role search/disclosures and unapplied context draft.

Integration and persistence: The picker projects one shared runtime policy. Closing preserves composer text, selection, attachments and any active run; it does not apply a pending context limit.

Acceptance: Edit a context draft, open/close the picker twice and reopen through another eligible entry point; retain the draft and start no model or request.

Basis: Atlas host-model-picker; atlas-script-7.js (atlas-model-refresh-code); PICK-001/009 and UIX-PICK-001. Mounted controls are design evidence; runtime application follows the specified broker contract.

<a id="h.e043wipvv21"></a>

### IX-SPACES-002 — Main, Voice and Vision role tabs

Controls: Main; Voice; Vision; [data-hr-model-role]

Atlas surfaces: host-model-picker

Primary: Single click selects that role and replaces only its settings panel. Main, Voice and Vision retain their own model, requested reasoning, Assistant/Specialist, search text and disclosure state. Selecting Voice does not start listening; selecting Vision does not acquire a camera or screen.

Secondary: No role context menu. Secondary or middle click cannot create a role, clone settings or change the active conversation.

Drag: Tabs are fixed and not reorderable by drag. Swiping scrolls the containing surface where needed rather than reordering role identities.

Keyboard and touch: Left/Right wraps across the three tabs; Home selects Main and End selects Vision, matching the final script. The selected tab remains the roving focus stop. Touch selects once.

State: A role lacking eligible models still opens with its current/unavailable value and empty explanation. It cannot borrow Main silently. Role changes leave the shared context draft pending.

Integration and persistence: Role selection is picker view state, independent of Natural Conversation presentation and chat type. Switching roles does not mutate an admitted run or copy participant permissions.

Acceptance: Choose three distinct role models and queries, cycle by keyboard and pointer, and verify every value returns unchanged with no capture activation.

Basis: Atlas final model refinement, roleSelections/personSelections/reasoningByRole and selectRole; PICK-002.

<a id="h.siz5fq49sdyc"></a>

### IX-SPACES-003 — Context, Model and Assistant/Specialist disclosures

Controls: Context; Model; Assistant/Specialist; details[data-hr-section]

Atlas surfaces: host-model-picker

Primary: Single click a section summary expands/collapses its own body. Context exposes applied usage and pending limit; Model exposes search/rows/Browse models; Assistant/Specialist exposes participant search, groups and None. Closed summaries retain the current effective selection or Pending changes.

Secondary: No custom context menu on summaries. Secondary click does not reset the section or select its displayed value.

Drag: Summaries cannot be dragged or reordered. Field text selection and list scrolling continue normally within an expanded body.

Keyboard and touch: Enter/Space toggles the focused disclosure under [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk). Focus remains on its summary; Tab enters exposed controls. Collapsed controls leave the focus order.

State: Each role retains its disclosure state. A provider failure inside Model does not collapse Context or erase a limit draft. Current summaries must not show a pending value as applied.

Integration and persistence: Several sections may remain expanded; opening one is not selection or consent. Context picker and external Context popup project the same draft and applied state.

Acceptance: Expand Context and Model, edit both fields, collapse/reopen and switch roles; preserve intended state without an implicit Apply.

Basis: Atlas atlas-script-7.js section() and sectionOpen; PICK-001/002. The three sections are independent disclosures.

<a id="h.q2t12r8epkzj"></a>

### IX-SPACES-004 — Reasoning requested-effort slider

Controls: Reasoning; Quick; Deep; [data-hr-reasoning]; [data-hr-reasoning-value]

Atlas surfaces: host-model-picker

Primary: Click the track or move the thumb to the chosen 0–100 value in steps of 10 for the selected role. Update the visible requested percentage and accessible value together. Quick/Deep are endpoint descriptions, not extra presets. A change configures future eligible admission; it does not restart an active response.

Secondary: No custom menu; secondary click cannot select an undocumented provider level. Percentage text is not a confidence, quota or progress button.

Drag: Only the thumb supports bounded horizontal drag under [IX-BASE-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.5oiet5rdqq3c). Preview the value during movement, preserve pointer capture and finish one logical preference change on release; Escape restores the pre-drag requested value.

Keyboard and touch: Arrow keys change one step; Home/End choose endpoints. Touch thumb/track uses the same quantisation and accessible value announcement, without hijacking vertical page scroll.

State: Expose Unsupported when the route has no effort control. When mapped to provider levels, show the actual effective level separately; zero does not universally mean reasoning off.

Integration and persistence: Retain per-role requested value and revalidate mapping after a model change. No mode change, grant, source selection or context allocation follows from effort adjustment.

Acceptance: Set 40 for Voice and 70 for Main, then change provider; verify both requests persist while actual supported mapping is truthful and no historical response is relabelled.

Basis: Atlas atlas-script-7.js specifies min=0, max=100, step=10; PICK-007, UIX-PICK-005 and RUN-103 define truthful requested/effective mapping.

<a id="h.fczpf6q4r09j"></a>

### IX-SPACES-005 — Context usage ring and detail popup

Controls: Context usage ring; [data-hr-context-toggle]; Close context details; [data-hr-context-close]

Atlas surfaces: host-model-picker

Primary: Single click the ring toggles Context details, closing the model picker before opening. It shows actual used/capacity values and the shared context-limit editor. Close context details dismisses only this popup. Clicking the read-only usage meter does not compact context or resize it.

Secondary: No custom menu. Secondary click on the meter/ring cannot reset usage or allocate memory; textual numbers may use standard text selection/copy.

Drag: The usage ring/meter is not a slider or drag target. Drag selects displayed text only where text selection exists, never changes capacity.

Keyboard and touch: Opening focuses a meaningful popup control; Escape/Close returns focus to the ring. Touch uses the same toggle and shows full values in the detail surface.

State: Unknown usage is labelled unavailable, not zero. Over-capacity numbers remain truthful even if the graphical fill is capped. Pending limit and confirmed capacity remain distinct.

Integration and persistence: This popup and the picker share one context draft scoped to the current request/session. Closing does not apply, reload or discard it; unrelated conversations keep their own scope.

Acceptance: Open the ring while the picker is open, inspect unknown telemetry and close with Escape; exactly one popup is active and the composer remains intact.

Basis: Atlas atlas-script-7.js contextControl/contextPopoverContent; PICK-004/009 and UIX-PICK-001. Fixture token counts are not production telemetry.

<a id="h.4odanvh8hssf"></a>

### IX-SPACES-006 — Context limit edit and Apply and reload

Controls: Context limit; [data-hr-context-limit]; Apply and reload; [data-hr-context-apply]

Atlas surfaces: host-model-picker

Primary: Click the numeric field to edit its retained string. Positive whole tokens are valid; empty, zero, negative, fractional and nonnumeric values expose a specific error. Apply and reload appears for a changed value and submits that exact valid draft once to the identified runtime scope. Enter in the field, blur and role change never apply.

Secondary: Use the standard field editing menu for cut/copy/paste/select all. Apply has no custom context menu; secondary click does not commit.

Drag: Text selection is allowed; neither field nor Apply is draggable. A dropped textual value is parsed as input, never executed or applied.

Keyboard and touch: Use [IX-BASE-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xj3xapubjjfz). Tab reaches Apply when actionable; Enter/Space on that button applies. Escape dismisses the popup while preserving the unapplied value.

State: Disable duplicate Apply during allocation. Show actual pending/reloading/failed outcomes and retain the attempted value. Failure identifies the last confirmed allocation or unavailable session; partial multi-session application cannot masquerade as full success.

Integration and persistence: Preserve draft conversation/attachments and show affected scope. Restore authoritative context state only after an explicit external restoration or confirmed runtime result; unrelated preference changes do not overwrite the pending field.

Acceptance: Enter 0, then a valid changed value; press Enter, dismiss/reopen, and finally Apply twice. No early commit, one transaction and retained attempted value after failure.

Basis: Atlas contextEditor/applyContextDraft in atlas-script-7.js; PICK-005/006, UIX-PICK-004 and RUN-104/105. Atlas rerenders a fixture; production requires actual allocation.

<a id="h.ydo82pmvw7b5"></a>

### IX-SPACES-007 — Search the active role’s models

Controls: Search models; [data-hr-model-search]; No matching models.

Atlas surfaces: host-model-picker

Primary: Single click focuses search. Typing filters active-role candidates by displayed identity, source and route description. Clearing restores candidates without changing the selected model. The No matching models message is status text, not a selectable item.

Secondary: Standard text edit menu only; no model command is attached to secondary click on the search field or empty state.

Drag: Drag selects query text. No candidate is reordered by dragging the search field; a text drop only edits the query.

Keyboard and touch: Typing and IME update search after composition as required. Arrow/Tab navigation reaches matching rows; Escape first dismisses the picker without destroying the saved per-role query.

State: Discovery loading, confirmed no matches and provider failure are distinct. Do not clear a selected identity because filtering hides it. Stale asynchronous results must match the current role/query revision.

Integration and persistence: Search state is per role and presentation-only. It does not trigger inference, install models, filter another role or broaden discovery permissions.

Acceptance: Search a string hiding the selected model, switch roles while results load, then return and clear; original selection remains and late results do not cross roles.

Basis: Atlas modelChoices/filterModelChoices, atlas-script-7.js; PICK-008.

<a id="h.fkb723jci8ev"></a>

### IX-SPACES-008 — Select a model and inspect route availability

Controls: Model rows; [data-hr-model-option]; This device; named remote device/API; Cloud provider

Atlas surfaces: host-model-picker

Primary: Single click an eligible row selects its canonical model/route for the active role and updates the checkmark and collapsed summary without closing the picker. Selection is distinct from installation, loading and admission. Double click performs no additional request; a route label is explanatory unless it has its own explicit details affordance.

Secondary: No required row context menu in this compact picker. Secondary click does not load/unload, delete or choose the model. Detailed management is reached through Browse models.

Drag: Rows are not draggable or sortable. A model row cannot be dropped into the composer to activate a different route.

Keyboard and touch: [IX-BASE-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.a2i0alfkwp3d) selection is keyboard-operable with Enter/Space on the focused row. Touch selects once and exposes unavailable reason through the row/detail affordance.

State: An ineligible/unavailable row remains identifiable with its reason and repair route; it is not selected as a fallback. Concurrent model removal leaves the pending choice unavailable rather than substituting a same-named model.

Integration and persistence: Revalidate role capability, requested effort and source-disclosure compatibility. An admitted run keeps its recorded route unless explicitly changed through the runtime’s supported steering operation.

Acceptance: Choose a remote Vision model, revoke its endpoint, and submit later; show the exact unavailable identity and retain the prompt without routing it to cloud automatically.

Basis: Atlas compact model rows and click handler, atlas-script-7.js; PICK-002/008 and UIX-PICK-002. Example model names are fixtures.

<a id="h.82uzd18m5hjr"></a>

### IX-SPACES-009 — Browse models and return to the picker

Controls: Browse models; [data-go="home-models"]

Atlas surfaces: host-model-picker

Primary: Single click opens Home’s real model catalogue with the originating role and return context. Inspect/manage a model there, then return to the same conversation/picker. Catalogue browsing itself does not replace the selected role or start a new chat. An explicit choose-for-role action returns the identified candidate for validation.

Secondary: No custom menu on the button. If represented as a navigable link, middle click follows the host’s open-tab pattern while preserving the same return identity; it never selects a model.

Drag: The Browse button is not draggable. Catalogue model downloads use their own manager rather than a drag transfer from this button.

Keyboard and touch: Enter/Space opens the catalogue; Back/return restores picker focus and retained query. Touch route uses the same target.

State: Missing catalogue or unavailable candidate shows a scoped error with return/retry. A download continues under its manager; closing the picker does not falsely mark it installed.

Integration and persistence: Preserve context draft, per-role settings and composer state. Installing/loading a model remains an explicit owner operation and never grants tools or capture access.

Acceptance: Browse with a pending context edit, start a supported download, return and dismiss; retain the edit and correct independent download state.

Basis: Atlas modelChoices in atlas-script-7.js; UIX-PICK-003. Home catalogue interaction belongs to the Home chapter.

<a id="h.hngyah2gha8k"></a>

### IX-SPACES-010 — Choose an Assistant or Specialist for a model role

Controls: Find an assistant or specialist; [data-hr-person-search]; [data-hr-person-option]; None; Assistants; Specialists

Atlas surfaces: host-model-picker

Primary: Search filters permitted identities by name/role. Single click a person row binds that canonical identity to the active model role; None removes only that role’s binding. The picker remains open with the selected checkmark. Group headings are noninteractive. Choosing a source-bound Specialist retains its binding and evidence policy.

Secondary: No custom person-row menu here; management uses its Home/AI Studio route. Secondary click cannot duplicate, reset or delete an identity.

Drag: Person rows are not drag handles. Dragging a portrait does not grant memory, move ownership or attach a second person to the draft.

Keyboard and touch: Search follows [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1); row Enter/Space chooses. None remains reachable even when no search results exist. Touch chooses the same stable identity.

State: Unavailable/revoked identity is retained with an explanation until replaced or cleared. An empty search never selects None automatically. Late catalogue updates cannot alter another role’s person.

Integration and persistence: Selection affects eligible future requests and preserves configured identity history. It does not start work, copy private memory or remove source-only constraints.

Acceptance: Select a bound Specialist for Main and an Assistant for Voice, filter both away and choose None only in Voice; Main’s corpus and identity remain unchanged.

Basis: Atlas personChoices/selectPerson, atlas-script-7.js; PICK-002/008, ASST-001/007 and KNOW-006/015.

<a id="h.r8vpdlkn3wev"></a>

### IX-SPACES-011 — Space rail and direct environment navigation

Controls: Space rail icons; Chat; Study; Actions (legacy Tasks target); Research; Shopping; Translate; Experiences; Assistants entry

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, spaces-specialists, spaces-assistants, spaces-manage, study-subject, study-topic, host-model-picker, spaces-permissions

Primary: Single click a rail destination opens its actual environment and restores the last eligible conversation/workspace or purposeful start view. Actions resolves old Tasks links to the same Space. Selecting a rail item never opens configuration by default. The current item can refocus/restore its view without creating another conversation.

Secondary: A rail context menu may expose permitted Pin/Unpin and Open in new host tab using the same target. Manage belongs to an explicit command. No menu action deletes conversations or starts a task. Middle click a navigable destination opens a host tab under the shared tab pattern.

Drag: Reorder only eligible pinned items using their drag handle after the shared threshold; show insertion marker and commit ordering on release. Protected destinations reject removal. Escape restores original order; body scroll/text gestures do not start reorder.

Keyboard and touch: Keyboard focus names every icon. Enter navigates; the context key exposes the same commands. Touch tap opens; long press opens management without launching.

State: Loading/denied/unavailable destinations retain the outgoing draft. Disable only the affected route, with a repair/back path.

Integration and persistence: Checkpoint view, draft, attachments, scroll and run before navigation. Existing Vision & Voice capture remains visibly bound to its actual conversation, never silently transferred.

Acceptance: Navigate Study→Translate→Actions during a run and return; preserve all drafts/IDs and invoke no extra request or capture session.

Basis: Atlas Spaces shell, spaces-reference refinements and SPC-004/005; owner naming correction maps Tasks to Actions without changing saved identity.

<a id="h.7q1fkh1tvis"></a>

### IX-SPACES-012 — Space name selector, New Space and Manage Spaces

Controls: Space name/dropdown; New Space; Manage Spaces; Edit Spaces

Atlas surfaces: spaces-chat, spaces-manage, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, host-model-picker, spaces-permissions, spaces-emoji, spaces-compose, spaces-attachments, spaces-running, spaces-specialists, spaces-assistants, spaces-study, study-subject, study-topic, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-generative-ui, spaces-thinking, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, spaces-experiences

Primary: Click the Space name to open its selector. Choosing a listed Space performs direct navigation. New Space creates an editable definition draft after name/type selection; Manage/Edit Spaces opens the current definition intentionally. Cancel creation keeps the old Space and any unsent conversation text.

Secondary: The selector menu exposes destination selection and explicit creation/management, not hidden destructive shortcuts. Secondary click on its trigger does not switch Space. Configuration deletion, when available, names its exact scope and preserves separately owned conversations/artifacts.

Drag: Dropdown entries do not accept arbitrary drops. Reordering configured navigation uses the dedicated sidebar/rail editor, not menu drag.

Keyboard and touch: Use [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk)/007. Type-ahead selects matching destinations; Escape closes the menu and restores focus. Touch opens the same menu and creation dialog.

State: Duplicate names are allowed only with disambiguating owner/location; identity is never title-based. Failed definition creation retains draft values and retries one operation.

Integration and persistence: Creating a definition and creating a conversation are separate. Manage preserves the same SpaceID, active revision and draft configuration.

Acceptance: Choose another same-named Space, return and cancel New Space; resolve exact identities and create no empty duplicate conversation.

Basis: SPC-004/050 and Atlas spaces-manage; exact dropdown layout inherits current Spaces shell. New/configuration mechanics are explicit target specification.

<a id="h.omgywedf2gtb"></a>

### IX-SPACES-013 — Contextual sidebar collapse and customisation

Controls: Collapse sidebar; Restore sidebar; Sidebar options; Add item; Rename section/item; Hide/Show; Move item; Mini-rail; Dropdown

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, spaces-manage, study-subject, study-topic

Primary: Collapse hides the contextual pane and expands the work area; Restore brings back the same selected item/scroll. Sidebar options opens the scoped editor. Add selects a typed navigation/action target; Rename changes its label; Hide/Show changes membership visibility; Move changes ordering. Mini-rail/Dropdown chooses the Space switcher presentation in the configuration draft; Save applies it without changing pinned IDs or work. None of these activates a contributed target.

Secondary: Right click an eligible item exposes Open, Rename, Move and Hide/Remove from sidebar with scope stated. Protected permission/recovery routes remain reachable. Opening a contributed action is distinguished from executing it.

Drag: Drag an explicit item/section handle past the threshold to reorder within permitted groups; insertion line, auto-scroll and Escape rollback follow [IX-BASE-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.oj2yi5h8kpkm). Reject dropping conversations into arbitrary tool entries or reordering locked items.

Keyboard and touch: Keyboard Move up/down and destination menus are equivalent to drag. Touch long press opens options; scrolling from the item body must not reorder.

State: Locked overrides show their governing source. Missing contribution retains a safe unavailable reference and repair/remove choice; failed save preserves the edit draft.

Integration and persistence: Space Override > User Preference > Default resolves presentation. Canonical targets, conversations and documents survive sidebar removal and collapse.

Acceptance: Hide a Bank link, reorder a custom section, revoke its plugin and reopen; keep canonical resources and an accessible navigation/recovery path.

Basis: SPC-003/008, APP-SPACES-008 and Atlas Spaces contextual sidebar. Assistant narrow-navigation refinements are additionally covered by [IX-SPACES-052](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.85rfznudvfg4).

<a id="h.fvz2br1f1z8s"></a>

### IX-SPACES-014 — New Chat and recent conversation activation

Controls: New Chat; Recent Chats; conversation title row; Pin; Unpin; Archive; Rename; Open in new tab

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, spaces-assistants, spaces-specialists, host-model-picker, spaces-permissions

Primary: New Chat creates one conversation within the current Space and focuses its composer. Clicking a recent row opens that ConversationID and restores reading/draft state. A double click does not create another run or conversation. A saved title is not an editable field until Rename is chosen.

Secondary: Right click a row offers Open, Open in new tab, Pin/Unpin, Rename and Archive/Unarchive when applicable. Archive removes it from the ordinary list without erasure. Middle click opens a second view of the same identity, not a copied thread.

Drag: Drag only through a visible reorder/move affordance to an eligible pinned position or Group/Project. Preview destination and scope; dropping outside cancels. Ordinary text selection cannot move a conversation.

Keyboard and touch: Enter opens; F2 or Rename edits where the shared list pattern allows; context key exposes the same commands. Touch long press opens options without starting inference.

State: Unavailable/denied conversations do not fall back to another title match. Failed create retains the unsent draft and operation identity; title conflicts are resolved by revision.

Integration and persistence: Opening/renaming/pinning/archiving never changes message chronology, task ownership or source grants. Linked tasks continue independently.

Acceptance: Archive an active conversation, find it through archive/search, reopen in two tabs and rename concurrently; keep one identity and a recoverable conflict.

Basis: SPC-004/006 and Atlas Spaces sidebar. Exact context-menu commands restate the retained lifecycle rather than asserting Atlas backend functionality.

<a id="h.3ow4ayvp4a70"></a>

### IX-SPACES-015 — Chat type and storage location selectors

Controls: Normal; Multi-Agent; Temporary; Local; Mesh; Cloud; Save Temporary

Atlas surfaces: spaces-chat, spaces-manage

Primary: Selecting a type opens the conversion effect before applying a supported change. Save Temporary first establishes durable local storage; Temporary→Normal/Multi-Agent and Normal→Multi-Agent preserve history. Location choices inspect/pick eligible placement separately. Model execution locality is not changed by a storage label.

Secondary: No custom menu on radio choices. A contextual Details command may inspect placement/device/entitlement state, never bypass unsupported reverse conversion or save-first requirements.

Drag: Selectors are not draggable. Dragging a chat into a remote collection cannot circumvent the same conversion and placement checks.

Keyboard and touch: Use labelled radio/menu semantics and explicit Apply where conversion requires a staged choice. Escape cancels an unapplied conversion; touch exposes full effect text.

State: Unavailable Mesh/Cloud shows concrete eligibility or connection reason. A Temporary transfer fails with must-save-first; it cannot silently become durable. Failed migration retains source and recoverable state.

Integration and persistence: Keep type, storage, inference route and Natural Conversation mode independent. No historical participant replies, tool replay or automatic durable-memory write are created.

Acceptance: Attempt direct Temporary→Cloud, then save locally and promote explicitly; verify original IDs/history and actual placement status.

Basis: APP-SPACES-007, SPC-040–043 and retained Spaces chat type/location contracts; detailed selectors are specified controls beyond the individual static screenshots.

<a id="h.kt35lgbcln2v"></a>

### IX-SPACES-016 — Group and Project organisation controls

Controls: New Group; New Project; Move conversation; Group to Project; Sources; Rules; Resource connections; Remove container

Atlas surfaces: spaces-chat, spaces-manage, host-model-picker, spaces-permissions, spaces-emoji, spaces-compose, spaces-attachments, spaces-running, spaces-specialists, spaces-generative-ui, spaces-thinking, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery

Primary: Chat Groups opens the existing organisation-container list. Create selects Group or Project and names its scope. Move chooses an existing container for the same ConversationID; Sources/Rules/Connections open that Project’s real references/configuration. Convert Group to Project promotes the same container. Remove container previews organisation effects and preserves member conversations.

Secondary: Right click a container offers Open, Rename, scoped Add/Move member and permitted Convert/Remove. Project to Group is unavailable when it would discard structure. No context command silently deletes original documents.

Drag: Drag a conversation handle to an eligible container with highlighted target and scope preview. Commit membership only on valid drop; reject unauthorized destinations. Ctrl does not silently clone private history.

Keyboard and touch: Move menu and keyboard destination selection mirror drag. Escape cancels; touch long press exposes Move. Source/rule fields use shared form focus.

State: Concurrent membership/rule revision changes require revalidation. Denied sources stay unavailable; moving a conversation cannot broaden their grants.

Integration and persistence: Inherited instructions/context update future eligible requests while admitted runs remain attributable. A Project connection is distinct from granting every account/resource it can reach.

Acceptance: Move an active chat between containers with conflicting rules and promote a Group; retain the task, show effective policy and lose no source/history.

Basis: SPC-044/045 and retained Spaces organisation; these are operational detail decisions over existing canonical containers.

<a id="h.ylr4w14sczqh"></a>

### IX-SPACES-017 — Conversation history, search and return to latest

Controls: History/Search conversation; message result; Return to latest; branch selector

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Search filters authorized history and opens the selected MessageID/branch at its reading anchor. Return to latest scrolls to the latest visible event and re-enables following only for that view. Branch selection changes the visible branch without executing its old messages. Clicking a timestamp/details link opens provenance rather than editing it.

Secondary: Message context commands remain the message-action records below. Search results may offer Open in conversation and Copy link; neither copies hidden source content nor merges branches.

Drag: Drag selects message text; transcript scroll uses normal wheel/touch behavior. Messages are not draggable reorderable history records. Moving the scrollbar must not steer the model.

Keyboard and touch: Search focus and result traversal follow [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/005. Escape returns to prior anchor; touch scrolling suspends automatic follow. Keyboard Return to latest is discoverable.

State: Streaming adds events without jumping a deliberately scrolled view. Missing/redacted results have truthful unavailable states. Late query results do not replace another branch’s selection.

Integration and persistence: Search and view state are not compaction or deletion. Shared links resolve current authorization and exact message identity; reopening never replays tools.

Acceptance: Scroll up while streaming, find an older branch message, then return to latest; retain selection/draft and perform no execution.

Basis: SPC-003/006/009/030–039 and Atlas conversation/message surfaces; explicit history controls retain supported pre-refactor capabilities.

<a id="h.bai4aclewxqc"></a>

### IX-SPACES-018 — Composer text, selection and expansion

Controls: Message composer; placeholder text; expanded composer body

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Single click places the caret; double click selects a word and triple-click behavior follows the host editor. Focus/nonempty content expands upward. Editing preserves structured text, inline references and the retained selection. Clicking the placeholder only focuses; it does not submit a suggested request.

Secondary: Standard text editing menu offers Cut/Copy/Paste/Select all and documented spelling actions. Secondary click preserves the target caret/selection; it cannot open task actions or execute pasted commands.

Drag: Drag selects/repositions text according to the editor’s explicit text-drag support; source/file drops are handled by attachment admission, not text execution. Crossing the field boundary must not drag the entire window.

Keyboard and touch: Enter uses the configured send/newline preference, Shift+Enter supplies its documented alternate, and IME-confirming Enter never also sends. Touch selection handles and keyboard safe areas remain usable.

State: Draft stays through navigation, failed admission and resize. A response arriving cannot replace text entered after submission. Input disabled state names a real reason and permits recovery/copy.

Integration and persistence: The draft is distinct from submitted message/attempt. Expansion affects layout only; it does not change source policy, grants or active run state.

Acceptance: Compose with IME, select a phrase, resize, submit and type again before acknowledgement; send once and preserve the newer draft.

Basis: Atlas spaces-compose and shared Spaces composer; SPC-010/020, UX27-SPACES-005.

<a id="h.6hdqefnhcdxv"></a>

### IX-SPACES-019 — Composer formatting and Unicode emoji

Controls: Formatting toggle; Bold; Italic; Link; supported list/code controls; Emoji; emoji search/category/choice

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, spaces-emoji, spaces-permissions, mobile-permissions

Primary: Formatting opens the focused expanded composer’s tools. Each style applies to the retained selection or next typed span; Link edits a target URL in a bounded form. Emoji opens the picker at the retained selection; choosing one inserts its Unicode sequence and closes/refocuses the composer. It never sends or inserts a rendered-image substitute.

Secondary: Formatting buttons have no extra context menus. Emoji text remains normal authored text with the editor’s selection/copy menu; long press a supported variant chooses that variant without submitting.

Drag: Toolbar controls and emoji choices are not draggable. Text/selection drag remains owned by the composer. Scrolling emoji categories does not drag a reaction onto a message.

Keyboard and touch: Formatting shortcuts target the composer only. Picker search, categories and choices are keyboard accessible; Escape restores focus/selection without insertion. Touch variant selection stays distinct from send.

State: Collapsed/unfocused composer hides formatting tools without removing formatting. Invalid link input stays in its edit form. Missing glyph uses device fallback while preserving Unicode text.

Integration and persistence: Formatting/emoji are one undoable draft edit. They must not be confused with response reactions, tools or generated media.

Acceptance: Select text, open emoji, choose a variant and undo; exact selection changes once and no request is admitted.

Basis: Atlas spaces-emoji/spaces-compose; SPC-010 and device-rendered emoji requirement. Exact registered formatting set is capability-labelled, not an invented rich editor.

<a id="h.gn4bqu6fyedg"></a>

### IX-SPACES-020 — Attach menu: typed resource categories

Controls: Attach; Files; Skills; Plugins & Apps; Specialists; Assistants

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, spaces-attachments, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Attach opens the resource menu without executing work. Files opens canonical file/source selection; Skills and Plugins & Apps inspect eligible installed resources or their explicit install route; Specialists and Assistants choose canonical identities. Selecting a resource returns an attachment/reference to the same draft. Configuration and install remain separate actions.

Secondary: No custom menu on category rows. Resource results may offer Details/Open source and Remove from selection where meaningful; a context menu never invokes the tool just by opening.

Drag: Drop files/authorized canonical references onto the highlighted composer attachment zone to queue readiness checks. Show type/count and reject unsupported payloads. Dragging a menu category itself does nothing; file-system move/delete is never implied.

Keyboard and touch: [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk)/007 governs menu and chooser focus. Escape backs out one layer with draft intact. Touch uses explicit picker selection as the drag alternative.

State: Selected/resolving/ready/denied/failed states distinguish existence from usable context. Unsupported or access-denied resources remain explained and removable; do not insert a fake ready chip.

Integration and persistence: Attaching a tool/person is discovery/context configuration, not invocation, installation consent or broader disclosure. Source-bound Specialist policies follow the chosen identity.

Acceptance: Attach a local-only file and a Specialist through different categories, cancel a third picker, and verify two typed references with no tool execution.

Basis: Atlas spaces-attachments; SPC-011–014 and APP-SPACES-002.

<a id="h.m75614lb28d1"></a>

### IX-SPACES-021 — Attachment chips, source inspection and removal

Controls: attachment chip; source readiness icon; Open/Inspect source; Remove attachment; Retry preparation

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Single click a chip opens its identity, selected revision, coverage/disclosure and readiness details. Open source resolves its real owner; Remove detaches that draft reference; Retry preparation retries only failed upload/extraction. Removing a selected chip is not deleting the source file or uninstalling its tool.

Secondary: Right click offers Inspect/Open source, Retry when failed and Remove from draft. Menu applicability follows the chip’s actual resource kind. Copy reference copies an authorized locator, not private source bytes.

Drag: A chip may reorder only when context ordering is supported and a handle indicates it; otherwise it is not draggable. Dropping another file creates another reference, never replaces this identity by title. Escape cancels reordering.

Keyboard and touch: Tab reaches chip and explicit remove control; Delete on a selected chip detaches only that chip with undo. Touch tap inspects; long press opens the same actions.

State: A removed chip ignores late preparation completions. A changed/revoked source shows stale/unavailable state and preserves enough identity for correction. Failed remove/save keeps the draft’s last confirmed membership.

Integration and persistence: Submission captures eligible revisions; subsequent draft removal does not rewrite submitted provenance. Disclosure compatibility is rechecked when route or source policy changes.

Acceptance: Remove a chip while extraction runs, then complete extraction late; it must not reappear or enter the next request.

Basis: SPC-011–013 and Atlas attachment/context chips. Ownership and readiness must come from actual source services.

<a id="h.u7m4jgdj0f78"></a>

### IX-SPACES-022 — Mentions and bounded capability references

Controls: @ mention picker; person/tool/source suggestion; resolved mention chip

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Type @ at a permitted text position to discover eligible people/resources. Click a suggestion to replace the active token with its canonical reference, retaining surrounding prose. Clicking the chip inspects its target; choosing a capability does not run it. An inline instruction applies only to its declared referenced range.

Secondary: Chip context offers Inspect and Remove reference; Remove preserves ordinary text where meaningful. Quoted/pasted @ text stays literal unless deliberately resolved. Secondary click never invokes the selected specialist.

Drag: Mention chips may move within authored text only through the structured editor; an invalid drop leaves the original intact. They cannot be dragged to grant a broader account scope.

Keyboard and touch: Arrow keys navigate suggestions, Enter confirms the suggestion rather than sending, and Escape restores literal text. IME composition has priority. Touch selection resolves one suggestion.

State: Ambiguous duplicate names show identity/owner. Missing access or deleted targets become unavailable references, not replacement guesses.

Integration and persistence: Submission snapshots the resolved identity and allowed scope. A bound Specialist carries its source policy; a mention does not override it.

Acceptance: Resolve one of two same-named Specialists, paste a quoted @ instruction and submit; only the explicit identity is invoked under its grant.

Basis: SPC-014/017 and retained Spaces context architecture. Interaction details extend the shown attachment/composer design.

<a id="h.rn64mnyxk7s5"></a>

### IX-SPACES-023 — Slash-command discovery and target forms

Controls: / command picker; /NewChat; /Branch; /Model; /Reasoning; /Pin; /Bug; /ResolveIssues; /Archive; /Stop; /ResetContext; /Restart; /BackgroundChat; /Memory; /Permission; /Attach; /Copy; /CopyLatest; /Read; /ReadLatest; /Move; /Dictate; /Summary; /Tabs; /Voice

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: A leading eligible slash opens registered commands. Choosing a row opens its typed arguments or stages the exact command. NewChat/Branch/Move/BackgroundChat resolve an explicit destination; Model/Reasoning/Memory/Permission/Attach open their controls; Copy/Read distinguish thread and latest message; Pin/Archive/Summary/Tabs modify their named state; Stop/Restart/ResetContext/ResolveIssues invoke their defined runtime operations; Dictate/Voice control capture separately; Bug prepares a reviewable report. Discovery never executes.

Secondary: No custom menu on command rows. Quoted text, code, URLs and pasted scripts remain literal. Standard text-menu paste cannot become an execution shortcut.

Drag: Commands are not draggable executable objects. Dragging query text only edits it; unsupported dropped arguments remain invalid draft input.

Keyboard and touch: Arrows choose, Enter completes the command/argument before explicit execution, Escape cancels completion. Preserve IME, literal slash and draft undo.

State: Unavailable commands show capability/scope reason. Changed target or policy invalidates staged arguments; retain them for correction.

Integration and persistence: Use the same canonical UI command, identity and side-effect reconciliation as the normal control. ResetContext does not delete history; Restart is not Resume.

Acceptance: Complete /BackgroundChat into an existing target, cancel, then paste a script containing /Stop; neither path executes accidentally.

Basis: SPC-015–017 and retained SPUI-185/186; these commands are preserved product capabilities, not all displayed in the static Atlas.

<a id="h.vaxct2x5tchq"></a>

### IX-SPACES-024 — Composer action shelf: browsing versus activation

Controls: Selected action icon; Send message; Start voice conversation; Stop current task; Steer; Queue; action-stack browse gesture

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Click the front selected action to invoke that named command only. Browse the stack using its explicit next/previous affordance or supported gesture to change the selected action without invoking it. Tooltip/accessible name includes the actual command and position, such as Voice, 1 of 2. A visible stop icon always targets the current run rather than a hidden shelf choice.

Secondary: No custom action-menu on the icon unless an explicit shelf menu is exposed; secondary click cannot approve or submit. Double click is deduplicated for asynchronous admission.

Drag: A swipe/rotation gesture is limited to the shelf’s interaction region and only changes selection after its threshold. Release completes selection, not execution. Dragging across the composer text remains text selection; Escape restores prior shelf selection.

Keyboard and touch: Keyboard exposes every shelf action by name and a separate Activate command. Touch browsing has a distinct tap to execute. Reduced motion removes rotation animation without losing the selected action.

State: Empty Send is disabled while eligible Voice stays enabled. Running/paused/held states expose only applicable commands and truthful unavailable reasons.

Integration and persistence: Shelf browsing never grants approval, changes task state, starts capture or discards a draft.

Acceptance: Rotate past Voice and Stop during a held task; neither capture nor cancellation occurs until its action is deliberately activated.

Basis: Atlas rounded icon-only Send/Voice shelf and titles explicitly distinguish browsing from execution; APP-SPACES-004 and SPC-014/022.

<a id="h.nd3nsji6z29j"></a>

### IX-SPACES-025 — Send message and admission acknowledgment

Controls: Send message; composer submit key

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Click Send snapshots the exact draft/attachments/route/policy and requests one durable admission. Keep the original until acknowledged. After acknowledgment clear only that admitted draft revision, append one user message and expose the run. Text typed after the click remains a newer draft. Enter sends only under the configured editor preference.

Secondary: No custom menu on Send. Secondary or middle click cannot bypass validation; repeated clicks reuse the in-flight operation identity.

Drag: Send is not draggable. A drop onto the button never submits a file; attachment drop zones handle file preparation explicitly.

Keyboard and touch: Enter/Space on the focused button activates; IME-confirming Enter does not. Touch activation uses the same single-admission transaction.

State: Resolving/denied attachments, missing route and capacity limits block admission with correction targets. Unknown acknowledgment shows Checking submission and reconciles before retry; no fake assistant output appears.

Integration and persistence: Use selected Study/Translate/Research semantics and bound-source policy without changing conversation identity or permissions. Successful submission is not task completion.

Acceptance: Lose acknowledgment, click again and type a new sentence; observe one request and the intact new draft after reconciliation.

Basis: SPC-020/021 and Atlas send shelf; the static preview is not real request admission.

<a id="h.v4nl4ebn41mh"></a>

### IX-SPACES-026 — Steer, Queue and the alternate submit action

Controls: Steer; Queue; Alt+Enter; send-action preference

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Choose Steer to submit the draft as a prospective instruction to the active run at its next safe boundary; show Received then Applied. Choose Queue to snapshot a distinct pending follow-up. The selected per-Space default remains visible; Alt+Enter invokes the documented alternate when both are available. Neither rewrites the original request.

Secondary: A dedicated send-action menu may offer Steer and Queue with descriptions and Set default for this Space. Secondary click on a plain button does not submit or silently change that default.

Drag: The action selector is not draggable. Draft text drag remains editing; queued-item reorder is a separate operation.

Keyboard and touch: Expose both named commands to keyboard and touch. Enter uses the visible configured action; Alt+Enter does not fire during IME composition.

State: If the active run ends between choice and activation, reconcile against that RunID and offer the correct follow-up state; never steer an unrelated new run. Denied/ineligible queue contents remain recoverable.

Integration and persistence: Steering can revise future plan/scope within authority. It does not approve a pending mutation, reset conversation context or replay completed effects.

Acceptance: Steer a running Research plan, then queue a follow-up and finish the run concurrently; retain distinct instruction identities and honest Received/Applied states.

Basis: SPC-022/023; retained SPUI-169/170; Research’s active-run default is Steer.

<a id="h.2j3gyrkvvwd2"></a>

### IX-SPACES-027 — Queued message inspection, editing and cancellation

Controls: Queued message row; Edit queued message; Remove from queue; Move up/down

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Click a queued row to inspect its frozen text, attachments, target and readiness. Edit opens a pending revision; Save updates only while still queued. Remove cancels that pending item and retains a recoverable draft. Once admitted to execution, further edits become a new steering/follow-up instruction rather than mutation of submitted history.

Secondary: Context offers Inspect, Edit, Remove and dependency-valid Move up/down. An item already executing exposes its run controls instead of misleading queue deletion.

Drag: Use an explicit handle to reorder independent queued items after threshold. Show insertion and blocked dependencies; invalid drops and Escape restore order. Do not move across target conversations by accidental drag.

Keyboard and touch: Keyboard move commands mirror drag; touch long press opens the same menu. Focus returns to the nearest surviving row after removal.

State: Expected queue revision protects against admission or another window’s edit. A stale save preserves the proposed text and explains its new state.

Integration and persistence: Queue order affects only pending admission. Editing/removing never cancels an unrelated active run or deletes already committed output.

Acceptance: Edit a queued item as the runtime admits it; reject stale overwrite and preserve the proposed change as a usable follow-up.

Basis: SPC-023 and owner-preserved task/queue features; no dedicated Atlas screen claims this complete editor.

<a id="h.sviu14w774ix"></a>

### IX-SPACES-028 — Stop, Pause, Resume and End Pass

Controls: Stop current task; Pause; Resume; End Pass; Restart

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Stop targets the displayed current/root run and shows Cancelling/Stopping until acknowledged. Pause requests a supported safe suspension. Resume continues an eligible stopped/paused execution; End Pass closes that resumable pass while preserving work. Restart creates an explicit new attempt from supported state and reconciles prior effects. No control silently substitutes a different task sharing the conversation.

Secondary: No custom context menu on plain controls. Run options may expose these separately by exact state; a historical completed run cannot offer a misleading active Stop.

Drag: Controls are not draggable. Dragging an activity card does not cancel it or propagate cancellation to siblings.

Keyboard and touch: Enter/Space invokes the focused named command. Escape closes menus, not the run. Touch actions retain visible scope and pending acknowledgment.

State: Unsupported pause/resume names the limitation. Persistent Stop is not automatically treated as a transient interruption. Partial committed effects and unavailable exact continuation remain visible.

Integration and persistence: Closing a tab/popup is separate from stopping. Preserve outputs, receipts, checkpoint and draft; root versus child cancellation follows the explicit run tree.

Acceptance: Stop during a completed tool effect and failed stream, reopen and Resume/Restart as eligible; never duplicate the effect or auto-resume an explicit persistent cancellation.

Basis: SPC-024/034, ACTSPACE-016/017 and persistent runtime contract. The actual action boundary depends on the declared run state.

<a id="h.9mk0v3vx9caa"></a>

### IX-SPACES-029 — Voice launch and shared floating session controls

Controls: Start voice conversation; Voice pill; Mute; Captions; Pause/Resume; End; Screen share; Camera; Dictate

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Start voice opens the source-aware shared session entry with the current conversation/role; its explicit Start command begins capture through the chosen authorised sources. Mute suspends/resumes microphone capture for this SessionID, independently of speaker output and transcript retention; Captions changes display; Pause/Resume changes session activity; End stops capture/output; Screen share and Camera choose explicit sources. Dictate writes recognised text into the retained draft rather than starting a response. Opening full Vision & Voice continues the same session.

Secondary: Source/device options use their explicit menu; no generic right-click shortcut grants capture. Secondary click on End or Mute does not act.

Drag: Move the pill only by its designated handle, constrained to safe visible bounds; controls are not handles. Dragging a screen-preview region does not choose another capture source.

Keyboard and touch: All controls have named keyboard/touch actions. Escape dismisses a device picker without ending an established session. Capture state is announced without relying on animation.

State: Denied/lost device or unavailable voice route preserves text and reports which source stopped. Pending stop is distinct from confirmed stopped transmission.

Integration and persistence: Changing Space or Natural Conversation mode never secretly starts a second capture or transfers context. Source-only Specialist/Study policy remains in force.

Acceptance: Start dictation, switch view, revoke microphone and end the session; preserve the same draft/session and send no further samples after acknowledged stop.

Basis: SPC-018/019 and shared Vision & Voice contract; root/app interaction catalogue owns detailed device pickers.

<a id="h.8b5udrcgp0km"></a>

### IX-SPACES-030 — Suggested follow-ups and contextual response buttons

Controls: Review the plan; Make it lighter; Review proposed sessions; Review in Write; source/checking follow-up

Atlas surfaces: host-model-picker, spaces-chat, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: Review the plan or Review proposed sessions opens the existing proposed plan/approval for the displayed run. Review in Write opens that exact document proposal in its owner. Make it lighter stages the shown follow-up instruction in the composer, preserving existing draft via an explicit insert/replace choice if nonempty; it does not quietly submit. Other suggestions display the actual instruction before admission.

Secondary: No custom menu on a suggestion; a real target link can offer Copy link/Open in new tab through host navigation. Secondary click cannot approve a proposal.

Drag: Suggestion buttons are not draggable actions. Text can be selected/copied where exposed; dragging cannot publish or schedule anything.

Keyboard and touch: Enter/Space follows the named review/stage action. Escape closes review and restores focus. Touch shows full suggestion text before a truncated button can replace a draft.

State: A stale/deleted proposal opens an unavailable explanation or refreshed review, never a different latest target. Draft conflict retains both texts.

Integration and persistence: Navigation/staging, approval and execution remain separate. Current target revisions and grants govern later Apply.

Acceptance: Click Make it lighter with an existing draft and Review plan after its target changed; preserve text and reject stale approval.

Basis: Atlas conversational response buttons plus SPC-014/020 and canonical artifact review. Fixture text is an example, not an automatic policy.

<a id="h.ud0ujord1tq8"></a>

### IX-SPACES-031 — Copy response, message or thread

Controls: Copy response; Copy; Copy selected text; Copy thread

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Copy response copies the selected assistant message revision. The expanded Copy menu can choose message, selected content or whole active thread with its source/tool inclusion policy visible. User-message Copy copies that user message. A success indication requires the clipboard operation to succeed; it does not imply export or sharing.

Secondary: Right click message text uses selection copy; the message menu offers the same explicit copy scopes. No automatic whole-history copy from a selected passage.

Drag: Drag selects text. Copy controls are not draggable; dragging a message cannot reorder canonical history.

Keyboard and touch: Ctrl/Cmd+C respects the focused text selection; menu activation chooses the stated scope. Touch selection handles and Copy offer the same content.

State: Unavailable clipboard reports failure with manual-select fallback. Redacted/unavailable sources are excluded according to policy, with omitted content disclosed where needed.

Integration and persistence: Copy uses the active branch and revision, preserves formatting where supported and never changes message/model provenance.

Acceptance: Select one paragraph in an alternative answer, copy it, then copy the active thread; clipboard content must match each scope without hidden sibling history.

Basis: Atlas Copy response control; SPC-030/031 and retained SPUI-181/183.

<a id="h.k97ghycgspil"></a>

### IX-SPACES-032 — Read aloud and speech playback

Controls: Read aloud; Speak; Read thread; Stop speech

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Read aloud speaks the exact selected response through the permitted speech route. The thread command explicitly chooses the active thread. Stop speech ends output playback without cancelling an unrelated work run or microphone session. Playback controls show the real current message/position.

Secondary: Message menu offers Read this response or Read active thread, not an ambiguous read-all command. Secondary click on the plain Read button does not start audio.

Drag: Button is not draggable; a supported playback scrubber changes playback position only. Message text selection remains normal.

Keyboard and touch: Keyboard/touch activation has the same selected-message target; Escape closes options without falsely marking playback stopped.

State: Unavailable speech/language route leaves text usable and names the failure. A stale/deleted message cannot speak another message with the same index.

Integration and persistence: Speech uses current disclosure policy and the selected source revision. It does not turn captions on, begin capture or change the conversation’s model binding.

Acceptance: Read a historical alternative, switch the visible branch and Stop speech; output stays tied to the original selection and no run is cancelled.

Basis: Atlas Read aloud and SPC-031; shared Vision & Voice speech output.

<a id="h.75yo7aoklodw"></a>

### IX-SPACES-033 — Retry response with model and instruction

Controls: Retry response; Retry same model; Choose another model; Retry instruction; Generate alternative

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Retry opens the source response/branch target, current eligible route and optional instruction. Same model keeps the requested route when eligible; Choose another opens the shared picker scoped to this attempt. Confirm creates a new attributable attempt/alternative while retaining the original. A simple menu selection does not imply replay of every historical tool effect.

Secondary: Right-click response menu reaches the same Retry dialog. No separate hidden retry from secondary click on its icon. Resume is a distinct run control.

Drag: Retry controls and alternative tabs are not draggable execution commands. Text in the optional instruction can be selected normally.

Keyboard and touch: Escape cancels with original history intact; Enter in multiline instruction edits rather than commits unless the explicit form shortcut is used. Touch exposes route and effect scope.

State: Unavailable source/context/route reports the exact blocker. Reconcile uncertain consequential effects before retry; duplicate confirmation uses one attempt creation identity.

Integration and persistence: Preserve prior response, usage, artifacts, receipts and model provenance. New effort/source choices are revalidated without erasing old attribution.

Acceptance: Retry a failed response after a successful file mutation using another model; create one new answer while retaining and not repeating the mutation.

Basis: Atlas Retry response; SPC-034 and SPUI-181, with shared model picker.

<a id="h.rmbdr0prji"></a>

### IX-SPACES-034 — Branch and transfer into a compatible conversation

Controls: Branch; Branch from here; New conversation branch; Existing conversation destination

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Branch opens a preview of the exact message/branch point, included history/source references and destination. New creates a branch/chat; Existing chooses a compatible target with explicit relationship. Confirm preserves origin provenance and opens the resulting branch. Experience branching also selects the matching state checkpoint.

Secondary: Message context offers Branch from here. Destination context may inspect scope; it cannot merge unrelated histories or bypass participant access checks.

Drag: Messages are not drag-to-merge objects. A destination chooser may accept a canonical conversation reference only through its typed target field, with preview before commit.

Keyboard and touch: Keyboard selects destination and confirms; Escape cancels without switching. Touch exposes the full source/destination scope and retains return navigation.

State: Stale branch point or ineligible references retain the proposal and identify the conflict. Broader destination access cannot be inferred from a title match.

Integration and persistence: Branching never replays tools or claims external undo. Original conversation and historical outputs remain accessible under current grants.

Acceptance: Branch an answer with a private source into a shared conversation; block/narrow explicitly and preserve the original without leakage.

Basis: SPC-035 and EXP-004; Atlas Experiences Branch from here; branch mechanics preserve canonical history.

<a id="h.p7g7po5qwe6p"></a>

### IX-SPACES-035 — Edit a submitted user message: three distinct modes

Controls: Edit message; Replace messages below; Update context and new messages only; Edit in branch; Save correction; Cancel

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Edit opens the selected submitted content revision and an explicit effect choice. Replace messages below supersedes downstream content in the current active branch while retaining recoverable history; Update context and new messages only records a prospective correction; Edit in branch creates a new branch. Save commits that chosen revisioned operation. Cancel leaves submitted history unchanged.

Secondary: User-message context menu exposes Edit; it cannot use a generic inline save to hide branch replacement. Secondary click within text uses the editor menu.

Drag: Drag selects edited text; history rows cannot be reordered by dragging. A correction form is not an attachment or task drop target.

Keyboard and touch: Mode choices are labelled radio/menu controls. Escape prompts only for a genuinely dirty correction if closing would lose it; keyboard Save uses explicit form action.

State: Concurrent message revision changes retain the proposed correction and require reconciliation. In-flight work is notified at a safe boundary with accurate Received/Applied state.

Integration and persistence: Original revisions and real action receipts survive. Narrative replacement never retracts a sent message, purchase, publication or file mutation.

Acceptance: Apply all three modes to equivalent fixtures with downstream actions; verify their distinct branches/context effects and unchanged external receipts.

Basis: SPC-032/033 and resolved DEC-006, retained SPUI-183.

<a id="h.vj3u5lrbc2z9"></a>

### IX-SPACES-036 — Delete visible messages and select alternatives

Controls: Delete message; Delete message and response; alternative selector

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: The delete menu names the selected MessageID and whether its linked response is included. Multiple alternatives require an explicit target selection. Commit removes/tombstones the eligible active-view membership under recoverability rules; it does not silently delete every branch or artifact. Alternative selection changes the visible/context choice while preserving other answers.

Secondary: Context menu offers the two precisely scoped deletions and available Undo/Recover. It must not label irreversible account erasure as ordinary Delete message.

Drag: Alternative controls and messages are not draggable reorder handles. Text selection remains available; dropping a message into a bin is not an undocumented destructive shortcut.

Keyboard and touch: Keyboard Delete operates only when the actual message target is selected and the action is discoverable; normal text editing Delete retains text semantics. Touch menu shows full scope.

State: Live-run or dependent-branch references can require a tombstone or blocked operation. Failed deletion keeps the message and explains retry; no cosmetic disappearance as success.

Integration and persistence: Preserve approvals/audit/external effects according to policy. A recovery action restores eligible visibility, not replayed execution.

Acceptance: Delete one of two alternatives with a downstream artifact and undo; preserve the sibling, artifact identity and historical receipts.

Basis: SPC-036 and SPUI-183; privacy erasure remains a separate storage lifecycle.

<a id="h.6yypxbsrwpp3"></a>

### IX-SPACES-037 — Response reactions and bounded reporting

Controls: React; Like; Dislike; Good; Funny; Sad; Scary; Upsetting; Rubbish; Inappropriate; Review report

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: React opens the labelled response-feedback choices. Choosing one records feedback against that exact message/user/model revision, with supported undo/change. Inappropriate opens a report preview naming excerpts, context and destination; sending uses its authenticated reporting operation. A reaction alone does not send the entire thread or manufacture a follow-up answer.

Secondary: No custom menu on individual reactions. The response context menu may reach React/Report; secondary click never transmits a report.

Drag: Reaction choices are not draggable stickers or message reorder handles. Message text selection remains independent.

Keyboard and touch: Every emoji has a meaningful text label and keyboard activation; Escape closes without reacting. Touch long press can open the same labelled menu.

State: Pending/failed feedback is distinguishable from saved. Report failures retain the reviewable draft and actual delivery/reference state; retries reconcile uncertain sends.

Integration and persistence: Retention/personalization follow explicit scope; feedback is not a new memory grant or consent to disclose unrelated context.

Acceptance: React, change reaction, open Inappropriate and cancel; save only intended feedback and send no report until explicit final submission.

Basis: SPC-037; retained SPUI-182 exact reaction meanings. The static Atlas overflow does not prove reporting delivery.

<a id="h.vazrabgfyxh5"></a>

### IX-SPACES-038 — Sources and citation passage inspection

Controls: Sources; citation marker; Open source; Open current revision

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Sources lists actually used supporting provenance. Click a citation to open its captured source revision and mapped passage/page/cell/time range in context. Open current is a separate action when the source advanced. A source title alone does not claim its content was read; offered-but-unused sources remain distinguishable.

Secondary: Citation context offers Open captured source and Copy authorized citation/link. Open current is labelled separately. No menu grants source access or silently adds the source to another collection.

Drag: Dragging a citation may copy an authorized reference into a supported editor only as a reference; it cannot move the source or imply evidence for an unrelated claim. Ordinary passage text selection obeys access policy.

Keyboard and touch: Enter opens the citation; Escape returns to its marker/reading anchor. Touch opens a readable passage sheet with the same revision label.

State: Missing mapping, coarser extraction, revoked source and stale revision have distinct states. Do not synthesize unavailable snippets from memory.

Integration and persistence: Inspecting sources never changes the admitted corpus or historical answer. Source-bound notes retain exact lineage when saved.

Acceptance: Open a citation after its document changed and later lost access; first resolve the captured revision, then show access-limited state without leaked text.

Basis: SPC-031 and KNOW-011/012; actual source locator mappings come from the canonical evidence model.

<a id="h.7irt2gevi0pj"></a>

### IX-SPACES-039 — More message actions and toolbar customisation

Controls: More message actions; Response options; Message actions; Add Items; Remove Items; Rearrange; Restore defaults; Tokens per second

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, host-model-picker, spaces-permissions, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-permissions

Primary: Overflow exposes the applicable Copy, Speak, Retry, Branch, React, Sources or user Edit/Delete commands for the selected message. Customise edits the toolbar definition, not the message: Add selects a registered command/Automation, Remove hides its button, Rearrange changes order and Restore defaults resets presentation. Optional token speed displays actual measured output metrics.

Secondary: Right click the toolbar can offer Customise; message text keeps its own text menu. Custom Automation entries name their target and input rather than executing when merely added.

Drag: In explicit customise mode only, drag a command handle to an insertion point; Escape restores order. Outside that mode dragging selects text/scrolls, never edits toolbar order.

Keyboard and touch: Move up/down replaces drag; overflow remains keyboard/touch reachable. Focus returns to the initiating message after an action/dialog closes.

State: Missing commands show unavailable reason and repair/remove, not fake success. Unknown throughput stays unavailable; user alias never replaces author/model provenance.

Integration and persistence: Apply shared user/Space precedence and retain preferences through layout changes. Toolbar changes cannot remove mandatory recovery/approval access.

Acceptance: Add an Automation, reorder it, narrow the window and revoke it; overflow remains usable and the item does not execute during customization.

Basis: Atlas overflow labels; SPC-030/038 and retained SPUI-180/184.

<a id="h.otk7r5s57h9c"></a>

### IX-SPACES-040 — Open and close conversation permissions

Controls: Conversation permissions; Close conversation permissions; permission backdrop

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, spaces-permissions, mobile-permissions, host-model-picker, spaces-specialists, spaces-assistants, study-subject, study-topic, spaces-manage, mobile-study

Primary: Click Conversation permissions opens the effective scope for the exact conversation in a dialog/sheet. Close, backdrop dismissal and Escape dismiss without saving unapplied edits. They return to the preserved composer/caret. This control is separate from an editor’s Read-only/Take actions selector.

Secondary: No custom menu on trigger or close. Secondary click cannot toggle a grant or approve a tool action.

Drag: Dialog rows are not draggable permissions. Mobile sheet scroll moves content; dismiss gestures may close only through the defined unsaved-change behavior and never save.

Keyboard and touch: [IX-BASE-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.g4q68g51ix5k)/009 traps focus while modal and restores it on close. Touch close target remains reachable above the keyboard; row labels are announced with effective state.

State: Loading current policy is visible; failure preserves the conversation and offers retry. If authority changes while open, refresh effective restrictions without replacing edited values silently.

Integration and persistence: Closing cancels configuration edits only, not active work or pending action approvals. Opening does not acquire account permissions.

Acceptance: Open with an unsent draft, edit a permission, dismiss via backdrop and reopen; draft/selection and prior saved scope remain intact.

Basis: Atlas spaces-permissions/mobile-permissions; SPC-070/072 and APP-SPACES-003.

<a id="h.dygi9mq86h1g"></a>

### IX-SPACES-041 — Conversation permission rows and locked scope

Controls: Attached sources permission; Planner permission; Boards permission; Web research permission; External messages permission; Other Assistant memory permission; Allow reading; Ask each time; Blocked

Atlas surfaces: spaces-permissions, mobile-permissions

Primary: Open a row selector and choose Allow reading, Ask each time or Blocked from its eligible choices. This updates the dialog draft only. Allow reading does not authorize writing; Ask each time preserves the required scoped review; Blocked narrows use. External messages/Other Assistant memory can be disabled by governing policy, with its source explained.

Secondary: No custom context menu on choices. A Details/governing-policy link inspects the restriction without granting it. Secondary click never converts a locked item into an editable one.

Drag: Rows and options are not draggable. Reordering visual settings cannot change policy precedence.

Keyboard and touch: Use native/select-equivalent keyboard and touch semantics with the full capability label. Escape cancels the open selector’s uncommitted choice before closing the dialog.

State: Show requested, effective and governing state when they differ. Revocation applies to future protected work; disabled controls retain a readable reason rather than disappearing.

Integration and persistence: Selections intersect source ACLs, account/device policy and specific action approval. They cannot enable web/general knowledge/memory through a different row.

Acceptance: Set Planner to Allow reading and attempt a write; require its actual mutation contract, while the dialog accurately shows read scope.

Basis: Final Atlas select options in spaces-permissions/mobile-permissions; SPC-070/073. Fixture locked rows are policy examples.

<a id="h.jvwlpu50zd8i"></a>

### IX-SPACES-042 — Save/cancel conversation scope and manage app permissions

Controls: Save for conversation; Save; Cancel; Manage app permissions

Atlas surfaces: spaces-permissions, mobile-permissions

Primary: Save commits the exact conversation override revision, then closes only on acknowledged success. Cancel discards the dialog draft. Manage app permissions opens the canonical settings destination with this capability/account context, preserving unsaved conversation edits for return; it does not apply them first or create lasting trust implicitly.

Secondary: No custom menu on footer actions. Secondary click on Save/Manage does not grant or navigate.

Drag: Buttons are not draggable; text selection/scrolling remains available for long policy explanations.

Keyboard and touch: Tab order follows review fields then footer; Enter/Space on Save submits once. Escape cancels dialog edits under the same rule as Close. Mobile Save is the same command as desktop Save for conversation.

State: Stale revision produces a reviewable conflict; failed save keeps the pending changes and actual effective scope. Disable duplicate save during acknowledgment.

Integration and persistence: Broadening affects future admitted/re-admitted work; narrowing governs subsequent protected steps. Existing approval requests retain exact target scope. Settings navigation uses its own explicit grant flow.

Acceptance: Change account policy while a dialog is open, then Save; reject/reconcile stale scope without widening authority or losing the edit draft.

Basis: Atlas permission dialog footer; SPC-070 and current owner authority/permission separation.

<a id="h.f4pnbngb9rkb"></a>

### IX-SPACES-043 — Space Builder view and section navigation

Controls: Basic; Advanced; Expert; Details; Context & permissions; Sidebar; Models; Preview

Atlas surfaces: spaces-manage

Primary: Click Basic/Advanced/Expert to change the editor projection of one SpaceID/draft, not convert or activate it. Section tabs open the named fields/graph. Details edits identity; Context & permissions edits scope; Sidebar edits navigation; Models edits role policies; Preview opens isolated testing. Returning restores selection, draft and graph pan/zoom.

Secondary: No custom menu on view tabs. An Expert configurations active link opens the responsible graph area rather than disabling simplified controls.

Drag: View/section tabs are not draggable. Graph node dragging belongs only inside its graph workspace, not the tab strip.

Keyboard and touch: Shared tab pattern gives arrow/Home/End navigation and predictable focus. Touch chooses the same projection; Escape leaves the innermost popover first.

State: Unknown Expert-only values remain preserved and identified. Invalid draft fields stay visible when switching; view change cannot silently repair/delete nodes.

Integration and persistence: All views edit one revisioned definition. Active runtime keeps its admitted configuration until explicit activation/revalidation.

Acceptance: Add an Expert node, edit the Basic name, visit Preview and return; retain the node, same identity and all unsaved changes.

Basis: SPC-050–054 and final spaces-manage section labels. Complete three-view editing is preserved specification, beyond the static configuration card.

<a id="h.op0hxkpc4zsh"></a>

### IX-SPACES-044 — Basic identity, instructions and resource gates

Controls: Name; Description; Icon; Custom instructions; Skills; Plugins; Apps; Models; Memory; Add resource

Atlas surfaces: spaces-manage

Primary: Click fields to edit the definition draft. Icon opens an asset/icon selector and commits its selected reference to the draft. Category switches request availability within current policy; they do not install packages or grant accounts. Add resource opens the typed picker and binds the selected version. Instructions remain authored configuration, separate from conversation history.

Secondary: Text fields use editing menus. A resource context menu offers Inspect, Change binding and Detach; global uninstall/delete is not a Basic-row shortcut.

Drag: Drop an eligible canonical file/resource reference into the labelled resource area; validate type/version and show a staged binding. Reject executable text as an automatically trusted package. Icon drag changes only a supported image reference.

Keyboard and touch: Use field/toggle patterns with labels. Escape closes a selector without replacing the previous icon. Touch supports explicit Add rather than requiring drag.

State: Invalid name/schema/dependency keeps draft values. Enabled-but-unavailable categories show their actual missing capability; locked rules explain inheritance.

Integration and persistence: Save/Activate boundaries are separate. Disabling Memory here cannot erase stored canonical assessment/history records or source collections.

Acceptance: Enable an unavailable plugin category, attach a denied file and change the icon; retain truthful errors and no installation or active-run change.

Basis: SPC-051 and retained SPUI-202/211; these explicit fields expand the Atlas Details/context sections.

<a id="h.onksk6yhn2lh"></a>

### IX-SPACES-045 — Advanced per-capability policy and dependent settings

Controls: Inherited; Allowed; Blocked; Mandatory; whole-category Off; memory scope; model parameters; Assistant/Specialist defaults; Automation references; suggested prompts

Atlas surfaces: spaces-manage

Primary: Selecting a per-item state changes the requested draft policy and recalculates effective state. Whole-category Off intersects but preserves item choices. Mandatory identifies a required resource; Mandatory plus Blocked is an error. Memory, model parameter and collaborator fields edit their own scope; attaching an Automation reference does not activate a schedule.

Secondary: Inspect governing rule opens the inheritance source. Context can Restore inherited value or Detach reference; it cannot override a higher restriction or reveal inaccessible configuration.

Drag: Policy rows are not sortable permission precedence. Dragging a resource to a category can stage a typed binding only; an explicit policy choice remains required.

Keyboard and touch: Use labelled choice/toggle controls, keyboard Details and error focus. Touch exposes the same effective/requested distinction.

State: Unsupported provider parameters are unavailable rather than silently sent. Dependency conflicts identify both rules and block affected activation while preserving draft.

Integration and persistence: Changing one capability does not toggle independent web/general-knowledge/memory policy or overwrite Expert logic. Validation exposes collisions with a reviewable patch.

Acceptance: Block a mandatory source, switch category Off/On and reopen; original item choices persist and activation rejects the precise conflict.

Basis: SPC-052/053 and SPUI-203–205. Exact field interactions are specified over the retained Advanced behavior.

<a id="h.w0c9ilz3t341"></a>

### IX-SPACES-046 — Expert graph pages, nodes, ports and inspector

Controls: Details; Harness Logic; Model Logic; User Message Logic; Files; node catalogue; node body/handle; typed port; edge; property inspector

Atlas surfaces: spaces-manage

Primary: Click a node selects its stable ID; double click opens its properties/subgraph only where the node type declares one. Catalogue selection inserts one node at the chosen graph point. Selecting an edge opens its binding. Harness, Model and Message pages retain distinct legal nodes and inspector fields.

Secondary: Canvas context offers Add node/Paste at the captured point; node context offers Duplicate, Delete, Group and Inspect; edge context deletes that edge alone. Reference-aware Delete identifies affected connections and preserves protected core hooks.

Drag: Drag node handle/body after threshold to move selection with alignment cues; drag a port to compatible highlighted ports for a staged connection. Pan from blank canvas, zoom about pointer, auto-scroll near bounds. Escape cancels the in-progress move/connect; text/port gestures never become node movement.

Keyboard and touch: Keyboard catalogue search, move increments, Connect target chooser and Delete mirror pointer actions. Touch provides explicit connect/move modes.

State: Invalid port types, cycles, budgets or missing nodes retain the draft and exact error. Concurrent graph revision rejects stale inspector commits.

Integration and persistence: Undo/redo operates one logical graph edit. Graph preview cannot grant tools or remove protected send/cancel/source controls.

Acceptance: Connect incompatible ports, move two nodes, edit a text field and undo; reject only the invalid edge and preserve correct identities/focus.

Basis: SPC-054–058 and SPUI-206–209; reuse the shared Automation graph editor. Detailed graph controls are adopted target mechanics.

<a id="h.449ey3z34yw7"></a>

### IX-SPACES-047 — User Message Logic transformations and sample test

Controls: Truncate; Number extraction; Word search; sample input; Run transform preview; derived output

Atlas surfaces: spaces-manage

Primary: Choose a transformation and edit its explicit parameters. Truncate specifies range/limit; Number extraction declares locale/format; Word search declares case/boundary/normalization. Run preview applies the same typed transform to the sample input and displays derived output plus omissions/warnings. It does not alter the original submitted message.

Secondary: Standard sample-input editing menu; result context permits Copy derived output and Inspect transform provenance. No context action rewrites source history.

Drag: Text selection is normal. A transform node drags only under the graph record; dropping output into input stages text and never executes an external command.

Keyboard and touch: Preview has an explicit keyboard activation distinct from newline. Escape cancels open parameter editing without deleting source input.

State: Ambiguous numbers, invalid expressions and unsupported transforms show specific diagnostics with last valid output identified; failed previews do not become active behavior.

Integration and persistence: Record transformation/version/input revision and preserve original MessageID/content. Activation uses normal draft validation.

Acceptance: Test 1,234 under two locale rules, then invalid input; preserve the original string and expose differing/ambiguous derivations rather than silently rewriting it.

Basis: SPC-056 and retained SPUI-208; these are derived-data operations, not message-history editing.

<a id="h.pwwock6ef7z8"></a>

### IX-SPACES-048 — Builder resource tree, import and removal

Controls: Files page; folder disclosure; Add folder; Import resource; Rename; Move; Remove reference; Open resource

Atlas surfaces: spaces-manage

Primary: Click a folder disclosure expands membership; click a resource selects it and Open resolves its real owner/version. Add folder edits organization. Import validates the selected file/Skill/plugin/MCP/model/mini-app/CUI resource and dependencies before binding. Remove detaches the Space reference only.

Secondary: Resource context offers Open/Inspect, Rename reference, Move and Remove reference. Source delete/uninstall requires a separately named owner action; it cannot hide under Remove.

Drag: Drag selected references between eligible folders after threshold, with destination highlight and cycle rejection. External drops enter import validation. Escape cancels; dropped packages are not automatically trusted/activated.

Keyboard and touch: Tree arrows expand/collapse, Enter opens, F2 renames a label and a Move chooser replaces drag. Touch long press opens the same menu.

State: Missing owner/version/dependency remains an unavailable node with repair path. Failed import retains diagnostic/draft without a phantom ready resource.

Integration and persistence: Resource organization preserves canonical identity and does not change grants, runtime installation or source contents.

Acceptance: Move a model reference into a folder, remove it and reopen the model catalogue; original model/artifact remains intact and no weights were loaded.

Basis: SPC-057 and SPUI-209; the Files page holds typed references, not a competing filesystem.

<a id="h.ywhmz9qowryj"></a>

### IX-SPACES-049 — Builder Validate, Preview, Save, Activate and Reset

Controls: Validate; Preview; Save changes; Activate; Rollback; Manage study sources; Edit sidebar; Reset

Atlas surfaces: spaces-manage

Primary: Validate checks the exact draft and focuses actionable errors. Preview opens an isolated session marked live/mock by dependency. Save changes persists the draft; Activate atomically makes a validated revision active. Rollback selects a prior configuration for future work. Manage opens the selected source collection; Edit sidebar opens its scoped editor; Reset clears the specified sidebar/config override after showing its scope, not the Space’s conversations.

Secondary: No hidden action menu on these buttons. Version history may offer Compare and Restore as draft; neither auto-activates or reverses external effects.

Drag: Buttons are not draggable; preview drag is owned by the previewed control and cannot mutate the live graph accidentally.

Keyboard and touch: Keyboard errors link to fields/nodes. Escape closes preview with original draft intact. Touch exposes full reset/activation scope.

State: Invalid/stale activation leaves prior active version intact. Pending save/activation deduplicates; failure preserves draft and actual active state. A preview’s mocked result is visibly distinct.

Integration and persistence: Existing runs keep admitted revisions plus current revocations. Reset/rollback cannot delete history, cancel work or undo sent effects.

Acceptance: Save invalid draft, preview one mocked dependency, reject activation, fix and activate once; return to same Space with prior history preserved.

Basis: Atlas Save changes/Manage/Edit sidebar/Reset; SPC-058–061 and separate draft/active contract.

<a id="h.unc8nw6oc9nb"></a>

### IX-SPACES-050 — Assistant catalogue Open, Main and configuration

Controls: My Assistants; Open; Assistant card/name; Alex/Maya/Rowan Main conversation examples; Create Assistant; Configure; configuration icon

Atlas surfaces: spaces-assistants, home-assistants, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: My Assistants opens the catalogue; Search Assistants filters permitted names/roles without changing a selection. Open Main and a named Main conversation always open that Assistant’s canonical Main; generic Open/name resumes Main or the last explicitly selected thread. Create Assistant opens a new AI Studio draft. Configure/configuration icon opens the selected definition, not a new person. Card background follows the catalogue wrapper’s selection rule; its Open button navigates. View Specialists opens their catalogue. A recent collaborator-work card opens its exact canonical task/proposal/artifact, not a fresh run.

Secondary: Entity menu exposes Open, Configure and eligible Pause/Disable/Archive lifecycle commands with their distinct scopes. Rename is configuration, not creation. Delete is separately scoped and cannot erase shared artifacts implicitly.

Drag: A card is not a drag-to-create Assistant. Pin/order drag only changes catalogue presentation under the Home wrapper. Editor invitation drop is handled by its typed collaborator route.

Keyboard and touch: Keyboard Open/Configure are separate focusable actions; touch overflow exposes management without opening a conversation accidentally.

State: Unavailable definition or deleted identity retains a truthful link state. Repeated Open attaches to existing conversation; failed create retains definition draft.

Integration and persistence: Same identity across Home, Spaces and editors; configuration revision changes future work without resetting Main/history or copying memory.

Acceptance: Open Maya from Home and Spaces, configure its name, then return; one identity and Main history remain with no duplicated task.

Basis: Atlas spaces-assistants/home-assistants and late atlas-script-25.js; ASST-001–004. Root owns the basic Home card wrapper.

<a id="h.fo6pd25gptxi"></a>

### IX-SPACES-051 — Specialist catalogue Details, Attach and settings

Controls: Search by name, skill or task; Create Specialist; Specialist settings; Details; Attach; Open source workspace

Atlas surfaces: spaces-specialists, home-specialists, host-model-picker, spaces-permissions, spaces-emoji, spaces-chat, spaces-compose, spaces-attachments, spaces-running, spaces-generative-ui, spaces-thinking, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery

Primary: Specialists opens the catalogue. Search by name, skill or task and Filter Specialist expertise intersect over permitted definitions; Reset filters clears both without changing an existing attachment. Details expands the Home card disclosure or opens the Spaces detail surface, showing role, inputs, tools, source binding and scope. Specialist settings/Configure opens that definition; Create opens a new draft. Attach returns a reference to the originating composer without running it. Open source workspace opens that identity’s bound collection/chat, not a separate notebook app.

Secondary: Entity context offers Details, Attach to this draft, Configure and Open source workspace where accessible. A settings click is not attachment or invocation.

Drag: Dragging an explicit Specialist reference onto an eligible composer can stage the same attachment with its identity/binding; require a highlighted target and cancel elsewhere. Catalogue reorder does not clone definitions.

Keyboard and touch: Search, Details and Attach are independently keyboard/touch reachable. Escape returns to the originating draft selection.

State: Missing definition/version or source permission produces a specific unavailable state. Empty search leaves existing attached Specialists untouched.

Integration and persistence: Attach preserves source-only defaults and independent memory/web/general settings. It never grants another caller access to the Specialist owner’s files.

Acceptance: Inspect Exam Tutor, attach it to a draft and open its collection; preserve identity and enforce bound sources without starting a lesson.

Basis: Atlas spaces-specialists/home-specialists; ASST-007 and KNOW-001–009. Source-workspace entry is an owner-requested addition.

<a id="h.85rfznudvfg4"></a>

### IX-SPACES-052 — Assistant Workspace and narrow navigation overlays

Controls: Show workspace; Close workspace; [data-as-workspace-toggle]; [data-as-workspace-close]; [data-as-workspace-dismiss]; Show assistants; Close assistants; [data-as-navigation-toggle]; Workspace sidebar disclosure

Atlas surfaces: spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: Show workspace toggles the same Assistant’s sources/memory/thread pane; Close or its backdrop closes only the pane. Its visibility defaults closed and is remembered per Assistant. Show assistants opens narrow navigation; Close assistants dismisses it. On compact layouts, opening navigation closes the workspace overlay and vice versa; wide layout uses complementary panes.

Secondary: No custom menu on toggle/close. Workspace disclosure in navigation expands secondary Teams/Create/Runs/Shared sources links; it is not the main Workspace pane toggle.

Drag: Panes are not draggable windows. Scroll their content independently; backdrop/sheet drag must not move conversations or dismiss through a field-selection gesture.

Keyboard and touch: Opening focuses the corresponding close control; Escape closes the active overlay and restores its toggle. Trap focus only when modal. Touch backdrops have explicit equivalent close buttons.

State: Missing workspace disables its toggle with explanation. Resize recalculates modal/complementary state without losing selected thread/draft.

Integration and persistence: Closing panes never stops work, clears source bindings or creates another Assistant conversation. Persist only the declared per-identity visibility.

Acceptance: Open Rowan’s workspace, narrow the viewport, open assistant navigation, then reopen workspace; one modal at a time and retained identity/draft.

Basis: Final atlas-script-25.js workspaceOpen/setWorkspaceOpen/setNavigation/sync; ASST-004.

<a id="h.22jvk5ne5e49"></a>

### IX-SPACES-053 — Assistant conversation options and bound scope links

Controls: Conversation options; [data-as-options-toggle]; Configure; Conversation permissions; Review source scope; Configure selected Assistant

Atlas surfaces: spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: Click Conversation options opens the compact menu in the identity row. Configure navigates to this Assistant’s canonical definition; Conversation permissions opens the current thread override. Review source scope opens the actual supplied/bound source selection and effective policy, preserving the current answer/draft. These are different settings scopes.

Secondary: The explicit options menu is the custom menu; secondary click on its trigger does not execute an item or open duplicate chrome. Standard source text remains selectable.

Drag: Menu entries are not draggable. Selecting text in an explanation cannot reposition the identity row.

Keyboard and touch: Arrow keys navigate menu items; Enter activates; Escape returns to the options trigger. Touch opens the same named actions with full scope labels.

State: Configuration or policy that changed while open is refreshed/revalidated at action time. Missing access exposes the governing source, not a blank settings page.

Integration and persistence: Configuration affects future eligible work; permissions remain thread-scoped. Reviewing sources cannot enable web search or private cross-Assistant memory.

Acceptance: Open options while a thread permission changes, choose Configure and return; retain thread context and do not save a stale grant.

Basis: Final atlas-script-25.js toolbarMenu and Atlas Rowan source-scope link; ASST-004 and SPC-070.

<a id="h.ey4v142zpmad"></a>

### IX-SPACES-054 — Assistant Main and secondary conversation rows

Controls: Main; secondary thread rows; New conversation; Rename thread; Archive thread; Hand off to conversation

Atlas surfaces: spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: Click Main opens its permanent primary conversation. Click a secondary title resolves that exact ConversationID; New creates another thread under the same Assistant. Hand off opens a reviewable package of goal/plan/sources/task/checkpoint/output/remaining work to an eligible existing or new thread, preserving the same task when continuing the same objective.

Secondary: Thread context offers Open, Rename, Archive/Unarchive and Hand off where applicable. Main is not deleted by ordinary secondary-thread archive. A broader destination cannot receive private context merely by being selected.

Drag: Drag a thread handle only to reorder/pin eligible thread navigation; a handoff requires the explicit package/destination operation, not a drop into another title.

Keyboard and touch: Keyboard opens rows and context actions; touch long press distinguishes archive from opening. Cancel handoff restores initiating focus/draft.

State: Stale target, changed grants or unavailable thread retain the package for correction. Exact task identity must not be replaced with a newer task from that context.

Integration and persistence: No new Assistant or replayed effects from thread switch/handoff. Source messages stay in their original thread with traceable references.

Acceptance: Hand off a plan from Main to an existing implementation thread, reopen both and archive the secondary row; same work/identity survives without copied private history.

Basis: Atlas data-action=assistant-thread; ASST-004, retained same-Assistant handoff contract and SPC-035.

<a id="h.lprk24uejwz9"></a>

### IX-SPACES-055 — Assistant sources, memory and shared-resource entry points

Controls: Shared sources; Sources entries; Tools & memory; Private memory; source scope details

Atlas surfaces: spaces-assistants, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: Select a source entry to inspect its real owner/revision/access and open it through the canonical viewer. Shared sources opens the selected shared collection. Memory opens eligible memory controls for this Assistant/thread; inspect current scope before changing it. A private-memory label never reveals another Assistant’s content.

Secondary: Source context offers Open, Inspect revision and Detach from this scope where editable. Memory entries use their own inspect/correct/forget operations, not source-file deletion.

Drag: Drop authorized source references into an explicitly editable source list to stage bindings. Reject unauthorized/global-memory drops. Resource reorder changes presentation only unless source-priority is explicitly edited.

Keyboard and touch: List/tree navigation and explicit Add replace drag on touch. Escape closes details and restores the selected source row.

State: Revoked/unavailable resources retain safe metadata only. Turning memory off stops eligible future reads/writes without deleting canonical task/Study results.

Integration and persistence: Source collection, Assistant definition, conversation attachments and memory retain independent scopes. A source-bound Specialist cannot use remembered facts as unrestricted evidence.

Acceptance: Inspect a private source, revoke it and reopen memory/source panes; no cached snippet leaks and unrelated permitted resources remain usable.

Basis: Atlas Assistant Workspace text/source groups; ASST-003/004 and KNOW source/memory separation. Static labels become accessible inspection targets where required.

<a id="h.tqos69f1tgjd"></a>

### IX-SPACES-056 — Teams, participant visibility and lifecycle management

Controls: Teams; team roster; All; Relevant; None; Pause; Disable; Archive; Delete Assistant

Atlas surfaces: spaces-assistants, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: Teams opens canonical team membership/roles. All/Relevant/None changes visibility of permitted Assistant-to-Assistant exchanges, never membership or tool scope. Pause addresses selected work; Disable stops new automatic initiation under its configured handling of active work; Archive changes discovery; Delete previews the identity/dependent data scope and uses canonical retention.

Secondary: Team/member context offers Inspect, permitted role/membership edits and explicit lifecycle operations. Remove member revokes future participation without rewriting authorship or deleting private identities.

Drag: Roster reorder can change displayed order only through a handle. Dragging a member into a team requires a typed membership proposal and access check, not automatic private-memory sharing.

Keyboard and touch: Menu/radio/confirmation controls have keyboard and touch equivalents; Escape cancels pending membership edits rather than stopping work.

State: Changes show current revision and stale conflicts. Disabled/archived identities remain attributable; re-enabling does not replay every missed external action.

Integration and persistence: Consequential audit, approvals and recovery stay visible even under None. Proactivity uses canonical schedules and scoped authorization, not team visibility.

Acceptance: Set None while a team awaits approval, remove one member and archive its Assistant; approval remains reachable and existing artifacts/history survive.

Basis: ASST-005/006/008 and retained Teams behavior; static catalogue only shows Teams entry, detailed management is specified.

<a id="h.xilxalbm8y62"></a>

### IX-SPACES-057 — People working here and delegated worker cards

Controls: People working here; View agents; Artifact previews; View work; worker card

Atlas surfaces: spaces-generative-ui, spaces-thinking, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery

Primary: People working here/View agents opens the current parent run’s actual worker list. View work opens the card’s target artifact preview or worker detail as labelled; Artifact previews opens the existing output collection. Click a card selects its child identity; it does not start another child. Queued reviewers remain visibly queued.

Secondary: Card context can Inspect scope, Open outputs and eligible Stop/Retry. Each action names child/run scope; opening a menu never applies it to the whole team.

Drag: Worker cards are not drag-to-reassign operations. Text/scrolling remains normal; artifact references can be copied through explicit owner-supported actions.

Keyboard and touch: Keyboard selection and Open are explicit; touch tap opens the named detail, long press offers the scoped menu.

State: Real state/usage/outputs replace sample counts. A disconnected worker stays reconnecting/unknown until reconciled; disappearing from a filtered view is not cancellation.

Integration and persistence: Lists project the canonical execution tree and current grants. Opening outputs addresses the same artifact revision, not a new copy.

Acceptance: Open the queued source reviewer while two workers run, then select a finished output; preserve child lineage and perform no duplicate admission.

Basis: Atlas coordinated-subagent/result screens; DELEG-001–009 and OUTSP-001–007.

<a id="h.mf0mukc1mxwo"></a>

### IX-SPACES-058 — Subagent scope, activity and Stop this agent

Controls: Stop this agent; Assigned work steps; Delegated scope; Open preview; parent task link

Atlas surfaces: spaces-subagent-detail

Primary: Step selection inspects its actual inputs/events/results; Open preview opens the bound draft revision. Stop this agent cancels that child’s unfinished subtree at its supported boundary, preserving committed revisions. The parent link opens the exact parent task. No action in this pane stops unrelated siblings by default.

Secondary: Activity context offers Inspect receipt/Open target and eligible Retry failed step. Stop’s plain button has no extra context action. A permission detail is explanatory, not an editable broader grant.

Drag: Steps/scope rows are not draggable to widen authority or change assignment. Scroll and text selection are normal.

Keyboard and touch: Enter/Space on Stop submits once; Escape closes detail but leaves work running. Touch shows the child name/scope alongside the stop control.

State: Show Cancelling until acknowledged; unavailable owner or uncertain remote outcome remains explicit. Parent revocations narrow future actions immediately at supported boundaries.

Integration and persistence: Child scope intersects parent grants/budget. A stopped child’s native drafts and receipts remain in the owning apps and Action Log.

Acceptance: Stop the presentation child after a committed slide while Canvas continues; preserve the slide, sibling run and exact cancellation record.

Basis: Atlas spaces-subagent-detail; DELEG-002/006/008 and shown parent-limited scope.

<a id="h.xd8pjuzg5wl9"></a>

### IX-SPACES-059 — Delegation controls: join, retry, pause and checkpoint

Controls: Join all; Join any; Collect; Require all; Best effort; Fail fast; Pause children; Retry child; Checkpoint; Restore

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Join controls select the declared child set/policy and inspect each result. Retry creates a new linked attempt only for the selected failure. Pause declares whether admitted children pause, finish or stop. Checkpoint stores eligible tree state; Restore opens compatible state and explicitly distinguishes exact continuation from reconstruction. Configuration of a child is not execution.

Secondary: Child context exposes only current applicable operations with scope. Failed/blocked child detail explains prerequisite/budget rather than offering a blanket “retry everything.”

Drag: Tree nodes can reposition for inspection only if the graph has layout controls; drag cannot reparent a live child, change causal ancestry or duplicate execution.

Keyboard and touch: Keyboard tree/list alternative reaches each child and join outcome. Touch uses explicit scope selectors and named actions.

State: Concurrency/depth/budget limits show structured blockers. A waiting parent releases scarce execution capacity needed by its child. Uncertain effects require reconciliation before retry.

Integration and persistence: Completed siblings and outputs remain. Root Stop follows the dependent tree; a child operation cannot silently broaden scope or replace explicit models.

Acceptance: Join mixed success/failure, retry one child and restore after restart; retain all original outputs and do not replay a completed send.

Basis: DELEG-003–009 and retained subagent lifecycle; these controls preserve runtime capabilities beyond the single static detail screen.

<a id="h.tawkkeizx7pe"></a>

### IX-SPACES-060 — Artifact preview navigation and zoom

Controls: Open in Present; Open in Canvas; Preview zoom out; Preview zoom in; Fit preview; preview canvas

Atlas surfaces: spaces-present-working, spaces-canvas-working

Primary: Open in Present/Canvas opens the exact artifact in its real owner, retaining revision and permission. Zoom out/in changes preview scale around the current view center; Fit computes bounds from actual preview content. Clicking the preview selects/inspects only where the embedded owner allows it; it cannot mutate a read-only preview.

Secondary: Preview context offers Open in App, Fit and permitted Copy reference. No context action claims an image screenshot is an editable object model.

Drag: Drag blank preview space pans when zoomed; object drag is disabled in read-only preview. If explicit Edit is enabled by the owner, its editing contract takes over. Escape cancels pan; wheel/pinch zoom stays scoped.

Keyboard and touch: Named zoom/Fit buttons are keyboard/touch alternatives. Focus and scale survive an incoming preview revision where meaningful; no hover-only controls.

State: Loading/error retains last valid preview labelled with its revision. Missing owner/asset shows a scoped unavailable state while conversation remains usable.

Integration and persistence: Scale/pan is local view state, not artifact revision. New render revisions do not steal composer focus or imply saved/exported output.

Acceptance: Zoom a live draft, receive another revision and Open in App; preserve the canonical artifact and no unintended edits from pan.

Basis: Atlas working-artifact screens; OUTSP-003/005 and canonical Present/Canvas ownership.

<a id="h.r6gazy7b8gx9"></a>

### IX-SPACES-061 — Generated planner: hours, focus and topic selection

Controls: Weekly study hours; Study focus; Balanced; Upcoming deadlines; Weakest topics; Choose topics; Generated view options

Atlas surfaces: spaces-generative-ui

Primary: Hours slider changes the generated proposal’s requested weekly total; Focus selects Balanced, Upcoming deadlines or Weakest topics using actual eligible records. Choose topics opens the canonical Study topic selector and returns selected TopicIDs. Generated view options exposes this surface’s source/proposal identity and permitted reset/inspect controls, not arbitrary model code execution.

Secondary: No custom menu on slider/select. Options is the explicit surface menu; Inspect data/source opens actual bindings and limitations.

Drag: Drag the hours thumb within bounds; preview proposal recalculation without Planner mutation. Topic selection uses its tree/list, not dragging private topics into the surface. Escape restores an unfinished drag.

Keyboard and touch: Slider keys/select keyboard and explicit topic chooser mirror pointer. Touch vertical scroll is not hijacked by the horizontal thumb.

State: Recalculation shows pending/failed against the proposal revision and preserves user edits. Missing evidence cannot invent weakest topics/deadlines.

Integration and persistence: Field changes update one typed generated-surface state, preserving focus; they never create events until reviewed and applied.

Acceptance: Change hours while selecting a topic and fail recalculation; retain input and show no Planner event created.

Basis: Atlas data-gx-hours range 2–10 and Study focus select; OUTSP-001–003. Numeric range is the shown example surface’s contract, not a global scheduler limit.

<a id="h.t632b7zhpkj6"></a>

### IX-SPACES-062 — Generated planner session selection and draft edits

Controls: Existing session, read-only in this proposal; draft session card; Session / Add draft session; Selected session duration; Remove draft session; Review 3 additions

Atlas surfaces: spaces-generative-ui

Primary: Click an editable draft session selects it and binds duration controls. Choose 15/25/30/35/45/60 minutes to revise that selected proposal. Session adds a draft in its named available slot; Remove deletes that draft proposal. Existing read-only sessions cannot be edited here. Review additions opens the exact resulting plan and counts real proposed changes.

Secondary: Draft context can Inspect, Change duration and Remove draft; existing session offers Open in Planner only. No menu silently changes an existing calendar event.

Drag: No session drag capability is implied by the Atlas. Reposition uses explicit date/time editing in the proposal/Planner contract; ordinary touch gestures scroll. A future drag implementation must preview exact schedule changes before commit.

Keyboard and touch: Card selection, duration and remove are keyboard/touch accessible. Focus moves to the next draft after removal; Escape cancels open duration/menu selection.

State: Concurrent proposal revision invalidates stale field edits/review. Remove clears selected duration binding and preserves existing events.

Integration and persistence: Everything here remains draft state. Review is not approval; Apply later uses canonical Planner receipts and current scope.

Acceptance: Select a 30-minute draft, remove it and choose another duration; do not modify an old selected card or existing read-only event.

Basis: Atlas data-gx-session/data-gx-duration and add/remove-session-preview; OUTSP-003 and ACTSPACE-007.

<a id="h.kqyq10ehuaol"></a>

### IX-SPACES-063 — Generated surfaces: input patches, Save and native promotion

Controls: generated field/action; Save sourced/generated result; Save to app; Open saved artifact

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A generated field edits its schema-bound value; a button invokes its declared typed action/target. Save chooses the owner/destination and exact surface/result revision, then adds a saved link only after a canonical creation/update receipt. Open saved artifact resolves that same object. A model label cannot silently attach an unrelated action.

Secondary: Context exposes permitted Inspect binding/Copy value and owner commands; it cannot execute arbitrary scripts or change capability scope.

Drag: Drag only when the registered component declares a typed payload/target and preview. Otherwise text selection/scroll remains normal. An invalid patch/drop preserves the last valid surface.

Keyboard and touch: Controls use actual semantic roles and shared patterns. Keyboard/touch reach the same actions; patch updates preserve focused field and unsent input.

State: Expected-revision checks reject stale action bindings. Saving/loading/failure are real states; no phantom artifact from a failed response. Revoked bindings redact content.

Integration and persistence: Ephemeral result, saved native artifact and export remain different. Reopening/editing routes to the owner, not a competing Space copy.

Acceptance: Type into a generated form during a streamed patch, then save with a failing owner; retain input/valid surface and create no fake saved link.

Basis: OUTSP-001–005 and KNOW-019; explicit generic primitive interaction applies to registered typed surfaces.

<a id="h.9dy1w6chds4c"></a>

### IX-SPACES-064 — Generated files Open, Preview, Download and folder location

Controls: Open in Present; Open in Canvas; Download; Preview; Download PDF; Open folder; Work summary

Atlas surfaces: spaces-generated-files, spaces-export-recovery

Primary: Open in App resolves the saved native object/revision. Preview opens the selected PDF/output representation without regenerating it. Download/Download PDF retrieves actual existing bytes for the labelled revision/format; Open folder reveals the canonical destination; Work summary opens the originating run/worker evidence. Labels must state whether a file is native or derived.

Secondary: Output context may offer Open, Copy reference, Show location and permitted Save copy. It cannot label a missing export as downloadable or silently overwrite the native artifact.

Drag: An explicit file drag can supply an existing export/file reference only where the platform advertises it; it is not a regeneration or move command. Preview artwork is not a draggable native object unless the owner supports that operation.

Keyboard and touch: Separate keyboard/touch buttons for each output; no reliance on card double click. Restore focus after the viewer/save dialog closes.

State: Loading bytes, unavailable destination, missing owner and permission denial are distinct. Failed Download leaves native sources intact and exposes retry for that job.

Integration and persistence: Source revision stays fixed; a later source edit does not substitute an unreviewed export. Folder opening does not grant external sharing.

Acceptance: Download the PDF after editing its native source; retrieve the explicitly labelled saved revision or require regeneration, never silently mix versions.

Basis: Atlas generated-files/export-recovery; OUTSP-004–007 and canonical artifact/export identity.

<a id="h.kquih2qsfhsa"></a>

### IX-SPACES-065 — Export failure Retry, choose folder and Dismiss

Controls: Retry export; Choose another folder; Dismiss; source revision and destination details

Atlas surfaces: spaces-export-recovery

Primary: Retry export retries the failed conversion/write stage with retained format/options/source revisions. Choose another folder opens the real destination picker, then retries or stages the explicit revised target according to the shown action. Dismiss hides the alert without labelling the export successful or deleting native artifacts; unresolved status stays inspectable in work history.

Secondary: No custom menu on recovery buttons. Target/source details can copy an authorized locator; secondary click never starts retry.

Drag: Recovery controls are not draggable. Dropping a folder does nothing unless the destination field explicitly supports a typed folder reference and validation.

Keyboard and touch: Keyboard/touch destination chooser and cancel preserve prior target. Escape closes the chooser/alert layer without discarding native work.

State: Duplicate retry deduplicates one job. Unwritable/removed target remains specific; converted valid bytes may be reused without rerunning successful generation.

Integration and persistence: Retry targets the recorded source revisions, not latest automatic substitutes. Closing/Dismiss is not cancellation of unrelated work or privacy erasure.

Acceptance: Fail a destination write, dismiss, reopen and choose a new folder; export the same chosen revisions once and keep both native artifacts usable.

Basis: Atlas retry-export-preview/choose-export-folder-preview/dismiss-export-preview; OUTSP-006 and SPC-027.

<a id="h.y6wotie9sitg"></a>

### IX-SPACES-066 — Summary, Activity and View Action Graph

Controls: Summary; Activity; View Action Graph; Action Graph; summary step/status row

Atlas surfaces: host-model-picker, spaces-chat, spaces-running, spaces-thinking, spaces-experiences

Primary: Click Summary/Activity disclosure expands or collapses its existing top-right work view; an explicit navigation Summary link opens the same selected task’s summary. Click a step/status opens its factual detail. View Action Graph/Action Graph opens the canonical run graph with its current nodes/results, not Present’s authoring graph or a new task. Collapse preserves run and approvals.

Secondary: Summary context may offer Expand/collapse and Open full activity; no custom context action fabricates progress or restarts work. Plain disclosure secondary click does nothing.

Drag: The floating summary is not a freely draggable competing sidebar unless the host explicitly exposes a position handle. Graph panning moves its viewport; nodes are inspect-only for the execution graph.

Keyboard and touch: Enter/Space toggles disclosure; keyboard graph outline reaches nodes. Escape closes a detail pane then returns to the initiating row. Touch can inspect every status without hover.

State: Unknown/stale activity is labelled. Selected step retains identity through updates; permission/blocker remains discoverable when collapsed.

Integration and persistence: This is a projection of real events, outputs and sources. Opening/closing cannot approve, stop, mark verified or alter plan dependencies.

Acceptance: Collapse Summary during approval, inspect the graph and return; pending operation and source run remain exactly the same.

Basis: Atlas Summary/Activity disclosures and View Action Graph; SPC-025, ACTSPACE-004 and EXP-001.

<a id="h.gwjdgvu9bwqk"></a>

### IX-SPACES-067 — Actions list scope, ordering and task tiles

Controls: Shown Actions; Actions Space Only; All Actions; task tile/title; priority/sort; checkpoint badge; Review requests

Atlas surfaces: spaces-running, spaces-assistants, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan

Primary: Scope menu selects Actions Space Only or All Actions over authorized canonical tasks. Click a tile opens its exact TaskID/RunID, including historical tasks sharing a conversation. Sort/priority controls change declared ordering; opening a completed tile never marks it working. Checkpoint badge opens its exact unread checkpoint; Review requests opens pending decisions.

Secondary: Tile context offers Open, Inspect status, eligible run controls and scope menu access. Legacy Task Space Only/All Tasks values migrate by identity. Secondary click on a badge opens detail/menu only, not approval.

Drag: Manual pin/order drag uses a dedicated handle and stable insertion cue only when that sort mode permits it. It cannot change canonical priority or task ownership silently; Escape cancels.

Keyboard and touch: Keyboard scope menu, task navigation and context actions match touch long press. Status meaning has text/icons, not colour alone.

State: Blocked overrides an unread checkpoint badge without deleting the checkpoint. Filters preserve independent approvals/notifications; denied tasks never leak counts/previews.

Integration and persistence: Same canonical work across originating app and Actions. Scope filters do not relocate, stop or duplicate tasks.

Acceptance: Show an old completed task and a new active task in one conversation; open each from All Actions and resolve the correct run/checkpoint.

Basis: ACTSPACE-001/002/005/009/010; pinned SpaceTasksDashboard.cui and SpaceTaskWorkspaceService.cs; Atlas legacy Tasks label maps to Actions.

<a id="h.h88czbonwolr"></a>

### IX-SPACES-068 — Goal creation, objective editing and lifecycle

Controls: New task/New goal; objective; constraints; completion criteria; Edit goal; Pause/Stop; Resume; Cancel

Atlas surfaces: spaces-running

Primary: New creates a draft objective under the chosen Space/context. Edit opens the same goal/task revision with its constraints and completion criteria; Save commits a revision and delivers future-work changes at a safe boundary. Start admits authorized work; Pause/Stop/Resume/Cancel use their named runtime scope. A changed unrelated objective is explicitly separate work, not silent replacement.

Secondary: Goal context offers Open, Edit, inspect evidence/checkpoints and applicable lifecycle actions. Archive affects list visibility only; no hidden Delete that erases receipts.

Drag: Goals are not drag-to-execute. Reordering list presentation cannot change priority, dependencies or completion. Text field dragging follows the editor.

Keyboard and touch: Fields and explicit save/start actions are keyboard/touch accessible. Escape keeps or discards a dirty edit through its normal draft contract; it never cancels the run itself.

State: Concurrent edits retain a reconciliation draft. Failed/blocked/cancelled/completed/verified are distinct; missing criteria/evidence cannot become “done” by narration.

Integration and persistence: No extra approval gate for already-authorized safe work. Goal edits revalidate affected scope and never undo prior external effects.

Acceptance: Revise the goal while a step runs, then cancel and reopen; retain revision lineage, partial output and no automatic restart.

Basis: ACTSPACE-011/013/016/017 and owner preservation clarification; complete goal editor is specified, not wholly pictured in Atlas.

<a id="h.wkazd7o50045"></a>

### IX-SPACES-069 — Plan steps, dependencies and plan revision comparison

Controls: Plan; step row; Add step; Edit step; Remove/defer future step; dependency selector; Compare revisions

Atlas surfaces: spaces-running, spaces-research

Primary: Click a step selects it and opens inputs, desired output, assignee/state and actual action links. Add/Edit changes future work in a draft plan revision. Remove/defer affects only the explicitly selected future scope; completed actions remain historical. Dependency selector names prerequisites. Compare opens semantic differences between chosen plan revisions; commit validates the current base.

Secondary: Step context offers Inspect, Edit future step, Add dependent step, Defer/remove when legal and Open evidence. Historical completed nodes cannot be edited into unperformed work.

Drag: An order handle previews movement; dependency-valid reorder may commit on drop, while graph dependency drag uses typed ports and cycle validation. Reject illegal targets and Escape rollback. Cosmetic graph layout is separate from execution order.

Keyboard and touch: Move/dependency choosers mirror drag; keyboard outline traverses steps. Touch must expose all actions without tiny port-only targets.

State: Stale plan, missing prerequisite and cycles retain draft/error at the affected step. Received and Applied distinguish steering acknowledgment.

Integration and persistence: Revalidate only affected approvals/checks. Independent eligible work can continue; changing a display order cannot silently satisfy a dependency.

Acceptance: Move a blocked dependent before its prerequisite, then change a completed step’s criterion; reject invalid order and invalidate only affected verification.

Basis: ACTSPACE-012/013 and RESEARCH-010/011; retained objective/plan/current-step/dependency contract.

<a id="h.uv6ksplv7xhb"></a>

### IX-SPACES-070 — Checkpoint list, details and continuation

Controls: Checkpoints; planned milestone; saved checkpoint; Jump to event; Open artifact; Continue/Resume; Compare with current

Atlas surfaces: spaces-running

Primary: Click a saved checkpoint opens its exact task/run/plan snapshot, outputs, evidence and remaining work; viewing acknowledges that checkpoint alone. Planned milestone editing changes a plan revision, never a saved execution payload. Jump to event opens Action Log; Open artifact resolves the captured revision. Continue/Resume first shows changed scope/source compatibility and uses a supported continuation.

Secondary: Checkpoint context offers Inspect, Compare with current, Open outputs and eligible Continue. Annotation/correction is attributable; no Rename/Edit action falsifies saved execution state.

Drag: Checkpoints are not draggable time-travel states. Selecting or moving a visual timeline marker only navigates history; it never restores by drop.

Keyboard and touch: Keyboard list/timeline controls provide exact date/revision labels; touch tap inspects before any continuation action. Escape returns to the previous work view.

State: Unavailable/incompatible checkpoint explains restart-from-state versus exact resume. Historical selection cannot substitute a latest task with the same conversation.

Integration and persistence: Opening is read-only. Restore never reverses external effects or replays committed actions; earlier and current states remain attributable.

Acceptance: Open an old checkpoint after source revocation and attempt Resume; report the actual compatibility/grant issue while keeping outputs and current task intact.

Basis: ACTSPACE-014/015/018, DELEG-007 and retained checkpoint compatibility; owner explicitly preserves these product controls.

<a id="h.2g7nml8wj6wd"></a>

### IX-SPACES-071 — Action Log filters, groups and live reading

Controls: Action Log; Search actions; task/run/attempt filter; actor/tool/app/outcome/time filters; group disclosure; Load more/next page; Return to latest

Atlas surfaces: spaces-thinking, spaces-running

Primary: Open Action Log selects the current conversation/task scope. Search and filters query authorized records; group disclosure expands causal child events. Pagination appends/replaces the declared page without duplicate event identities. Return to latest restores following; a deliberate scroll-up remains stable while new events arrive.

Secondary: Group context offers Expand/collapse and filter to this actor/run. It does not delete records, stop hidden work or grant visibility to another scope.

Drag: Drag selects text or moves the scrollbar; event rows are not reorderable. Column widths/order may use explicit table handles without changing event chronology.

Keyboard and touch: Keyboard table/list navigation opens selected event; filter fields have labelled clear controls. Touch scrolling never activates a receipt or retry.

State: Confirmed empty, loading, unavailable and denied are distinct. Late queries must match the current filter/scope revision; counts cannot reveal inaccessible work.

Integration and persistence: Filters are presentation only. Natural Conversation mode, compaction and task completion cannot remove access to retained authorized action records.

Acceptance: Filter failed tool calls while new events stream, open an old event and paginate; retain anchor and no duplicated or leaked event rows.

Basis: SPC-078/079/082 and owner product-preservation clarification; the full log is a required operational surface beyond Atlas’s concise Activity panel.

<a id="h.sjus58iw2vil"></a>

### IX-SPACES-072 — Action Log event details, receipts and retry

Controls: event row; Inspect receipt; Open target/output; Review pending approval; Retry eligible failure; Reconcile uncertain outcome

Atlas surfaces: spaces-thinking, spaces-running

Primary: Click an event opens its actor/action/target/time/revision/input-output/provenance detail. Open target/output resolves the named object; Inspect receipt shows confirmed effects. Review opens only that live pending operation. Retry creates a new attributable attempt for the eligible failure; Reconcile queries the original uncertain operation before any repeat.

Secondary: Event context exposes those exact applicable commands plus Copy authorized reference. A historical accepted approval is inspectable, never a reusable Grant button.

Drag: No event drag-to-replay. Selecting/copying text retains redaction and cannot move action ownership.

Keyboard and touch: Keyboard enters detail and returns to the same event; touch exposes separate review/retry targets. Escape closes detail only.

State: Stale target, revoked grant or uncertain remote result prevents unsafe retry and keeps useful original evidence. Successful transport is not necessarily completed effect.

Integration and persistence: New attempts append lineage; earlier failures/receipts remain. Public events and exposed model trace are distinctly labelled, not fabricated internal thought.

Acceptance: Inspect a timeout after a confirmed remote send, choose Retry and reconcile; show the original receipt and send no duplicate.

Basis: SPC-080/081 and canonical mutation envelopes; current task service retains exact original task/revision review checks.

<a id="h.cdtu577ecj36"></a>

### IX-SPACES-073 — Inline approval: exact additions and Decline

Controls: Approve 3 additions (dynamic count); Decline; proposal rows; Permission required

Atlas surfaces: spaces-running

Primary: Inspect each proposed session’s target burrow, date/time and duration. Approve the displayed count accepts only that current proposal revision through the canonical approval operation; any required authenticated broker step remains bound to the same exact review. Commit starts the eligible Planner changes and reports their receipts. Decline rejects this pending proposal without deleting safe preparation or unrelated outputs.

Secondary: No custom menu on Approve/Decline. Proposal-row context can Open target/Inspect source; it cannot approve an undisclosed future plan or change existing events silently.

Drag: Approval rows/buttons are not draggable scheduling controls. Rescheduling occurs in the editable proposal/Planner operation and invalidates the affected prior review.

Keyboard and touch: Keyboard/touch review exposes full consequence text and each row; Enter/Space on the focused action submits once. Escape/close does not approve or decline implicitly.

State: Stale target/proposal, revoked grant and failed decision delivery keep the request held with reconciliation. Count reflects actual proposed additions, never fixture three.

Integration and persistence: No redundant confirmation for an already concrete authorized operation beyond required broker flow. Collapse/shelf browse cannot approve. Independent work may continue according to dependencies.

Acceptance: Change one session after review, then click Approve; reject stale scope and accept only a refreshed exact proposal, once.

Basis: Atlas spaces-running inline Planner review; ACTSPACE-007, SPC-071 and canonical approval broker.

<a id="h.naqhrstgwmd5"></a>

### IX-SPACES-074 — Pending question pop-out and Received versus Applied

Controls: question indicator; question text/choices; short answer; Send answer; Dismiss question

Atlas surfaces: spaces-running

Primary: Click the question indicator opens the actual TaskID/RunID/pending question in a compact pop-out. Select a choice or type a short answer; Send records it durably for the responsible agent’s next safe boundary. Display Received before Applied when not consumed yet. Dismiss closes the pop-out without answering or cancelling work.

Secondary: No custom menu on choices/send. Text uses the editor menu; inspecting the question cannot switch to a newer task’s question.

Drag: Question controls are not draggable. Scrolling long options or selecting text cannot submit a choice.

Keyboard and touch: Choices and explicit Send have keyboard/touch equivalents. Escape closes and retains the unsent answer draft for that question.

State: A stale/resolved question identifies its new state and preserves the proposed answer for deliberate reuse; it cannot deliver to another pending request. Failed acknowledgment reconciles first.

Integration and persistence: An answer is not blanket approval of other operations. Badges remain discoverable even when Blocked/Working is the primary status.

Acceptance: Answer while the run changes its question; do not misapply the reply, and preserve truthful Received/Applied status after reconnect.

Basis: ACTSPACE-009 and retained SPUI-151; this preserved workflow is not fully illustrated by the approval screenshot.

<a id="h.vake5r447eef"></a>

### IX-SPACES-075 — Task evidence, verification and output navigation

Controls: Completed; Verified; verification evidence; remaining blockers; Work summary; output link

Atlas surfaces: spaces-running, spaces-thinking, spaces-generated-files

Primary: Click a status/evidence affordance opens the actual completion criteria, check revision/results and remaining work. Completed means the declared terminal outcome; Verified requires applicable evidence. Output links open canonical owners. A user can inspect or request recheck without converting a label into evidence. Work summary returns to the originating task/children.

Secondary: Evidence context offers Open result/log/artifact and eligible Re-run check. No “mark verified” shortcut substitutes for checks; manual annotations remain labelled annotations.

Drag: Status badges are not draggable. Selecting an evidence range is ordinary text/table selection, not changing which checks count.

Keyboard and touch: Keyboard/touch can inspect every badge; information cannot depend on hover or gold/green colour alone.

State: Changed outputs/criteria invalidate affected verification with a reason. Unrun/failed/unavailable checks remain distinct from pass; stale results retain their tested revision.

Integration and persistence: Rechecking records new evidence and cannot repeat unrelated consequential actions. Closing evidence does not mark a checkpoint viewed unless that actual checkpoint was inspected.

Acceptance: Edit a previously verified artifact and reopen its badge; remove current Verified status until the affected checks pass against the new revision.

Basis: ACTSPACE-005/017 and SPC-027; current Atlas separates prepared work, saved native output and approval.

<a id="h.36fv076x8850"></a>

### IX-SPACES-076 — Research navigation and same-conversation workspace

Controls: New research; My research; Overview; Sources; Plan; Findings & notes; Outputs; Conversation; saved research row

Atlas surfaces: spaces-research

Primary: New research opens a new Research draft. A saved row opens its exact conversation/run. Overview/Plan/Sources/Findings & notes/Outputs switch the current workspace section, retaining context and active work. The first submitted question generates a reviewable plan in that same ConversationID; Conversation returns to the full thread without creating another chat.

Secondary: Saved research context offers Open, Rename and Archive; section tabs have no custom menu. Middle-click a navigable saved item opens another view of the same identity.

Drag: Sections are not drag-to-reorder plan steps. Files dropped into Sources use explicit ingestion and scope selection, not immediate outside research.

Keyboard and touch: Keyboard section navigation and touch tabs preserve composer focus/draft; Escape closes the innermost source/detail sheet.

State: Loading/empty/stale/error sections remain honest; no fabricated findings while a fetch fails. Late section data cannot overwrite another selected research item.

Integration and persistence: The bottom composer remains available for steering/follow-ups. Section changes do not restart research, extend budgets or broaden source policy.

Acceptance: Start a question, inspect its generated Plan, switch to Sources and back to Conversation; retain one conversation, draft and run lineage.

Basis: Atlas Research sidebar and RESEARCH-001/010/011; plan-generation transition is retained SPUI-164.

<a id="h.e7igt2dxbxjp"></a>

### IX-SPACES-077 — Research Quick, Extended, Deep and Custom budgets

Controls: Quick; Extended; Deep; Custom; custom active-time/cap fields

Atlas surfaces: spaces-research

Primary: Click a profile selects the proposed execution budget and displays its limits/capabilities: Quick approximately 5–10 active minutes, Extended 15–20, Deep up to one hour; Custom edits a bounded requested duration up to the documented 28-day ceiling per turn. Selection alone starts nothing. Complete useful work sooner; no timer-padding requirement exists.

Secondary: No custom menu on profiles. Inspect budget explains active versus waiting time, account/runtime bounds and included outputs without authorizing resource purchases.

Drag: Profiles are not draggable. Numeric/duration fields use ordinary text selection; a slider, if rendered, edits only the pending budget under the shared slider pattern.

Keyboard and touch: Radio/tab keyboard semantics identify selection; Custom fields have explicit units and validation. Touch exposes full budget explanation.

State: Unavailable runtime capacity constrains the effective limit visibly. Invalid/over-limit Custom input retains the draft. Running budget changes require explicit supported steering/re-admission.

Integration and persistence: Selected profile cannot widen tools, web scope, credits or host availability. Each subsequent research turn has its own admitted budget.

Acceptance: Select Custom above capacity, correct it, switch profiles and start; actual admitted limit matches the visible effective budget, with no false duration promise.

Basis: Atlas execution-profile controls; RESEARCH-002 and retained SPUI-165. Active-time targets are budgets, not guaranteed delays.

<a id="h.x8kqj5ld0d9r"></a>

### IX-SPACES-078 — Research Review plan, deliverables and Start

Controls: Review plan; Edit plan; Report; Report & Artifacts; Start research; plan step controls

Atlas surfaces: spaces-research

Primary: Review plan opens current question, scope, steps, budget and outputs. Edit changes a revisioned draft. Report selects canonical Write delivery; Report & Artifacts additionally names supported native/experimental outputs. Start admits that exact reviewed revision once. A generated plan is not a completed report and the review link does not itself execute the research.

Secondary: Plan context offers Inspect source/step, Edit future scope and Compare revisions. No context command silently starts a second research turn.

Drag: Reorder/dependency editing follows [IX-SPACES-069](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.wkazd7o50045). Dragging a source into a plan stages an eligible reference; it cannot enable web research.

Keyboard and touch: Keyboard review reaches constraints/deliverables then Start; Escape returns to conversation with draft intact. Touch shows scope before activation.

State: Stale plan or missing required source/capability holds only affected admission and retains edits. Repeated Start deduplicates.

Integration and persistence: Already-authorized reads do not require repetitive new approvals. Consequential app operations retain their independent exact action contracts.

Acceptance: Change a required source after plan review and click Start; revalidate the admitted revision and preserve earlier evidence without mixing scopes.

Basis: Atlas Review plan plus RESEARCH-003/010/011 and retained SPUI-166. Plan mechanics reuse [IX-SPACES-069](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.wkazd7o50045).

<a id="h.29wm428cr33f"></a>

### IX-SPACES-079 — Research sources, findings and evidence gaps

Controls: Sources; Add supplied source; Outside web scope; source row; finding row; Notes; citation marker

Atlas surfaces: spaces-research

Primary: Add supplied source selects real files/URLs under source policy. Outside web opens its separate scope control. Click a source inspects captured revision/retrieval/coverage; click a finding opens supporting/contrary evidence and uncertainty. Notes create/edit an attributed working note; citations open exact passages. An inaccessible title cannot be treated as inspected evidence.

Secondary: Source context offers Open captured/current, Refresh and Remove from future scope with effect shown. Finding context offers Inspect evidence and Save note; it cannot erase contradictory evidence by relabelling it.

Drag: Drop source references into the labelled source list to stage ingestion. Findings are not draggable proof; moving them into a report retains citation/status metadata.

Keyboard and touch: Keyboard lists and citation navigation preserve return anchors. Touch access exposes long provenance and missing-source reasons.

State: Source fetch failure, insufficient coverage, confirmed conflict and stale evidence remain different states. Removal/revocation revalidates future work without rewriting old attribution.

Integration and persistence: Selected source snapshots govern admitted work. General knowledge, outside research and supplied materials remain distinguished in findings and exports.

Acceptance: Record one conflicting source and one inaccessible source, save a note and reopen it; preserve both uncertainty and precise citations.

Basis: RESEARCH-003–006 and KNOW citation contracts; Atlas distinguishes attached sources and Web off.

<a id="h.m26rsjkikiqh"></a>

### IX-SPACES-080 — Research report opening, updates and export

Controls: Report; Outputs; Open in Write; Save incomplete report; Update report; Export; Publish/Send

Atlas surfaces: spaces-research

Primary: Report/Outputs shows actual draft/saved result state. Open in Write addresses the canonical artifact/revision. Save incomplete preserves partial findings with unfinished coverage identified; Update creates a new report revision from the selected evidence. Export selects format/revision/destination. Publish/Send names its external destination and uses that operation separately.

Secondary: Output context offers Open, Inspect source ledger, Export and permitted Save copy. No context menu can mark incomplete research complete merely because a document exists.

Drag: Report references can be dropped into supported collections as references; no implicit independent copy or publication. Text dragging belongs to Write.

Keyboard and touch: Keyboard/touch reach citations and owner/export commands. Escape closes export setup without deleting report or continuing research.

State: Export failure targets the selected revision and retains choices; missing citations/support stay disclosed. A later report update does not silently replace bytes promised for an older revision.

Integration and persistence: Use shared artifact recovery, not a Research-owned document store. Stopped research can retain valid partial output without fabricated completion.

Acceptance: Stop before coverage completes, save an incomplete report, update later and fail export; retain both report revisions and correct source ledgers.

Basis: RESEARCH-007–009 and OUTSP-006; explicit output operations preserve canonical Write ownership.

<a id="h.k0bpagxdbixx"></a>

### IX-SPACES-081 — Shopping workspace navigation and editable requirements

Controls: New comparison; My shortlists; Saved items; Requirements; Candidates; Compare; Edit; price/features/delivery/shipping/brand filters; Required/Preferred

Atlas surfaces: spaces-shopping

Primary: New comparison creates a draft in Shopping; saved rows reopen exact comparison identity. Requirements/Candidates/Compare switch the same conversation workspace. Edit opens typed constraints. Filter edits and conversational refinements change one canonical requirement revision; Required affects eligibility, Preferred affects ranking/explanation. Commit shows inferred constraints for correction when material.

Secondary: Requirement context offers Edit, change Required/Preferred and Remove constraint; no generic “improve results” command silently relaxes a hard condition.

Drag: Filters are not draggable into different meanings. Reordering displayed requirements does not change priority/required status; explicit order handle is presentation-only.

Keyboard and touch: Fields/selectors have keyboard/touch alternatives and units. Escape cancels an unapplied requirement edit; current comparisons remain.

State: Unsupported attributes/unknown data stay explicit. Debounced/remote results bind to the correct requirement revision; stale searches cannot replace new results.

Integration and persistence: One requirement state drives chips, counts, results and comparison. Source/merchant retrieval follows actual grants; no purchase or installation from editing filters.

Acceptance: Make colour Preferred and compatibility Required through different input paths; recompute real matches once and keep both controls synchronized.

Basis: Atlas Shopping sidebar/Edit; SHOP-001/008 and retained SPUI-172–174.

<a id="h.mkdzr64e3sr8"></a>

### IX-SPACES-082 — Shopping result classes, candidate selection and source

Controls: Strict matches; Partial / similar; contextual suggestions; candidate selection checkbox; View source; candidate card

Atlas surfaces: spaces-shopping

Primary: Category selection changes the visible result class without resetting requirements. Click a candidate’s explicit select control adds/removes it from comparison; View source opens the actual merchant/source evidence for that exact variant/offer. Card inspection shows known/unknown required matches and checked time. A card body does not purchase.

Secondary: Candidate context offers Inspect offer, View source, Save and Add/remove comparison. Partial candidates disclose precise mismatch/unknown properties; context cannot promote them to strict by label.

Drag: Dragging a candidate to a supported comparison tray stages its reference with a visible target; otherwise no drag behavior. It never initiates checkout or copies an offer under another variant.

Keyboard and touch: Checkbox/row activation is separate from link opening. Keyboard and touch reveal mismatch reasons without hover; middle-click source uses host navigation.

State: Unavailable/stale offers retain checked snapshot and source state. Unknown required compatibility does not count as verified strict match.

Integration and persistence: Product identity differs from merchant offer/edition; filtering, saved selection and provenance remain canonical.

Acceptance: Select a strict and an incompatible cheaper candidate, open both sources and compare; mismatch remains visible and neither becomes a purchase.

Basis: Final Atlas labels supersede retained Direct/Similar wording; SHOP-002/003/008.

<a id="h.8ckxi9mar6di"></a>

### IX-SPACES-083 — Save candidate, shortlists and Compare selected

Controls: Save candidate; Saved items; My shortlists; Compare selected; remove from shortlist; comparison dimension selector

Atlas surfaces: spaces-shopping

Primary: Save candidate persists the selected product/offer reference and requirement revision; a second explicit unsave removes that membership. My shortlists/Saved items reopen real collections. Compare selected opens the selected identities with consistent dimensions/units and unknown values. Removing a comparison column changes only that view/selection.

Secondary: Saved-item context offers Open offer, Refresh evidence, Remove from shortlist and Copy reference. Delete product/account/merchant listing is not implied.

Drag: Comparison columns can reorder through a handle, preserving offer identity; dragging selected references to a shortlist creates membership only. Invalid targets/Escape cancel.

Keyboard and touch: Keyboard select/unselect and Move column replace drag. Touch exposes Save state and removal clearly; long press cannot purchase.

State: Pending save reports actual commit; errors retain selection. Stale price remains labelled as checked snapshot until refreshed.

Integration and persistence: Saving/comparing are distinct from purchase, alerts or currency conversion. A saved offer does not start monitoring.

Acceptance: Save, reopen, reorder comparison and remove one item; preserve native offer IDs and change no merchant state.

Basis: Atlas save-candidate and Compare selected; SHOP-004/005.

<a id="h.f5fpoze8n1l6"></a>

### IX-SPACES-084 — Offer variant, display currency and freshness

Controls: variant/edition selector; merchant offer selector; price details; Last checked; Refresh offer; Home currency link

Atlas surfaces: spaces-shopping

Primary: Select a variant/merchant to inspect its exact terms, not merely change a label. Price details shows original amount/currency, known shipping/tax and conversion basis. Refresh retrieves current evidence for that offer. Home currency link opens the shared preference; a successful change reprojects prices/ranges consistently without changing checkout currency.

Secondary: Price/source context permits Copy observed value with provenance and View source. No menu assumes unknown shipping is free or edits a merchant quote.

Drag: Variant/currency controls are not draggable. Text selection copies the displayed observation, not a guaranteed transaction price.

Keyboard and touch: Keyboard/touch selectors expose edition/platform/region and unknown fields. Escape preserves the prior selected offer when changing is staged.

State: Unavailable FX produces an identified estimate/unavailable comparison, never invented totals. Digital goods show Not applicable for physical shipping and real activation constraints.

Integration and persistence: Refresh appends evidence; earlier shortlist choice remains attributable. No entitlement, installation or purchase follows from a variant change.

Acceptance: Switch a game edition and currency with missing FX; keep distinct licence/offer identity and no fictitious shipping or converted checkout guarantee.

Basis: SHOP-003/009/010 and retained physical/digital/currency contracts; detailed inspection extends Atlas source/price rows.

<a id="h.kke6ncq1qfz7"></a>

### IX-SPACES-085 — Purchase handoff and optional price watch

Controls: Purchase/Open checkout; quantity/offer review; Create price watch; Stop price watch

Atlas surfaces: spaces-shopping

Primary: Purchase opens the real supported merchant checkout/handoff for the selected variant, quantity and current terms. Revalidate material changes before the consequential step. Create price watch configures an actual canonical Automation using available price observations; Stop disables that rule. If integration is unavailable, expose a source-link handoff or unavailable action rather than fake checkout/watch success.

Secondary: No custom menu can bypass exact transaction review. Watch context can Inspect rule/Edit bounds/Disable through Automations; saving an offer is not enabling it.

Drag: Purchase/watch buttons are not drag targets. Dragging a product into a basket-like area is not an implicit authorized transaction.

Keyboard and touch: Keyboard/touch review includes merchant, currency, variant and consequence. Escape cancels setup/handoff where possible without claiming a committed purchase was undone.

State: Uncertain merchant result is reconciled by original operation reference before retry. Watch failures show real source/scheduler state.

Integration and persistence: Uses existing scoped authorization without redundant ceremonial approvals. No silent downloads, installations, subscriptions or external messages.

Acceptance: Lose checkout acknowledgment and retry; reconcile one merchant transaction. Save another offer and verify no watch exists until explicitly created.

Basis: SHOP-005/006/010. Purchase/watch controls are preserved scoped capabilities; Atlas screenshot has no functioning checkout backend.

<a id="h.4psugfm4yh61"></a>

### IX-SPACES-086 — Translate navigation and target-language chips

Controls: New translation; Translation sets; Glossaries; saved translation row; target-language chips; Add target language; remove target

Atlas surfaces: spaces-translate

Primary: New translation starts a draft in the same Space; saved rows open exact TranslationSets/conversations. Add target language opens canonical language/locale selection and adds a future target chip. Remove changes the future persistent set only. Clicking a chip inspects/changes that target explicitly. Chips stay immediately above the standard composer.

Secondary: Chip context offers Change locale and Remove future target; it cannot delete historical variants. Saved-set context offers Open/Rename and permitted archive, not automatic source replacement.

Drag: Chip reorder may change display order through a handle but never language identity or source text. Unsupported drops leave the set unchanged.

Keyboard and touch: Keyboard language search, Enter selection and accessible remove buttons mirror touch. Escape cancels picker without clearing targets or draft.

State: Ambiguous/unsupported language needs a specific resolution choice; no guessed replacement. Empty target set leaves translation unadmitted with useful correction.

Integration and persistence: One-off request subsets do not change persistent chips unless explicitly requested. New target additions do not rerun successful siblings automatically.

Acceptance: Translate to French/German, remove German from future chips and reopen history; retain German’s existing variant and source set.

Basis: Atlas conversational Translate and Add target language; TRANS-001–004.

<a id="h.x4sa14oj4akt"></a>

### IX-SPACES-087 — Copy and refine one language variant

Controls: Copy French/German variant (dynamic label); Refine French/German variant; Retry variant; Regenerate variant; manual variant edit

Atlas surfaces: spaces-translate

Primary: Copy copies only the named variant revision. Refine opens/stages an instruction explicitly targeted to that variant, such as tone or terminology; submit creates its next revision. Retry targets a failed variant; Regenerate produces a new attributable alternative. Manual edit retains authorship and protected segments. Other language bubbles remain intact.

Secondary: Variant context offers Copy, Refine, Retry/Regenerate where eligible, Edit, Apply and Export by exact variant. No menu deletes successful siblings implicitly.

Drag: Select/drag variant text as text; language bubbles are not draggable to overwrite the source. A reference drop into an owner editor requires its explicit insertion/replacement scope.

Keyboard and touch: Keyboard actions announce language/locale; Escape cancels refinement without sending. Touch overflow keeps target label visible.

State: Stale variant/source or revoked access retains proposal and explains conflict. Partial failures have independent retry/status.

Integration and persistence: Targeted changes preserve TranslationSetID, source revision and sibling lineage. Copying/refining is not publishing or replacing original material.

Acceptance: Refine only German after French succeeded and German failed; retain French bytes/revision and one targeted retry lineage.

Basis: Atlas language-labelled bubbles and actions; TRANS-002/005/006.

<a id="h.p3knhzh2etv"></a>

### IX-SPACES-088 — Translation source language and glossary rules

Controls: source language/detection; explicit locale; Glossaries; Add protected term; Required/Preferred; Do not translate

Atlas surfaces: spaces-translate

Primary: Inspect detection then choose an explicit source locale when needed. Glossaries opens versioned term rules; Add/edit a term states source, target or Do not translate, locale, required/preferred treatment and scope. Save records a glossary revision for future jobs. Explicit valid language choices override detection suggestions, not provider capability.

Secondary: Term context offers Edit, Duplicate scoped rule and Remove from glossary; source text is not altered. Copy term uses standard text selection.

Drag: Term rows may reorder presentation only; precedence is explicit in rule policy. Dropped text stages a term form, never an automatically required global rule.

Keyboard and touch: Labelled fields/choices and keyboard term navigation support touch alternatives. Escape retains/cancels an unsaved term through the draft form.

State: Conflicting required rules block affected translation with both terms identified. Unsupported language/locale produces a structured choice, not silent substitution.

Integration and persistence: Job records pin glossary/version and source snapshot. Updating glossary does not rewrite already exported variants or silently translate code identifiers.

Acceptance: Create conflicting protected rules for one locale, run validation and resolve them; keep prior job provenance and no ambiguous auto-selection.

Basis: TRANS-003/006 and shared Dulche Translate contract; detailed glossary editing is specified beyond the static conversation.

<a id="h.c9cowroz3yhi"></a>

### IX-SPACES-089 — Segment review, stale source and structured translation

Controls: segment row; source/target alignment; Manual edit; Keep manual change; Retranslate changed segments; Compare source revisions; layout warning

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Selecting a segment shows source/target revision and alignment. Manual edit creates an attributable target revision. Source change marks affected segments stale; Retranslate changed segments previews the exact replacement set while preserving unaffected/manual segments. Compare opens source differences. Layout warnings navigate to overflow/unsupported structures in the owning app.

Secondary: Segment context offers Inspect source, Edit target, Keep manual change and Retranslate selection. Protected code/URLs/keys/formulas cannot be converted into prose without explicit typed scope.

Drag: Segments are not drag-to-realign arbitrarily. Selection/range drag follows the owner editor; explicit alignment editing, when available, must validate semantic mapping rather than positional guesses.

Keyboard and touch: Keyboard source/target navigation and conflict resolution mirror touch. Escape cancels pending segment replacement, preserving manual text.

State: Missing alignment requires coarser review; overlapping edits produce a conflict. Unsupported extraction/layout stays visible before apply.

Integration and persistence: Source and translated artifact keep independent revisions. A screenshot preview is not editable structured content.

Acceptance: Manually edit one target segment, change a different source paragraph and retranslate; preserve the manual edit and correct only identified stale content.

Basis: TRANS-007–009 and shared structured-artifact translation; no dedicated Atlas screen claims this full review editor.

<a id="h.yczgm0jrzsmj"></a>

### IX-SPACES-090 — Apply, export and publish a translation

Controls: Apply translation; Replace selected range; Create language variant; Export variant; Publish translated output

Atlas surfaces: spaces-translate

Primary: Create language variant saves a derived owner artifact referencing the source. Replace selected range previews exact target/range/revisions and applies through the owner’s typed edit. Export chooses the named variant/revision and format. Publish identifies destination and uses its real authorized operation. Translation completion alone performs none of these.

Secondary: Variant context exposes the same labelled operations and source/target details. No generic Apply all silently replaces every sibling or the original.

Drag: Dragging translated text into an owner is an explicit insert/drop with that editor’s undo boundary, not publication. Whole artifacts are references unless Copy is chosen.

Keyboard and touch: Keyboard/touch review exposes source and target locale, range and revision. Escape cancels pending apply/export setup.

State: Target revision conflict retains the translation and requires refreshed review. Export failure preserves native variant; no Download ready until bytes exist.

Integration and persistence: Use canonical owner receipts/undo and existing grants. Applying one language preserves siblings and historical source.

Acceptance: Change the document after a replacement preview, then apply; reject stale range, retain translated content and avoid corrupting adjacent structure.

Basis: TRANS-008/012 and OUTSP-006; owner mutations remain separate from successful translation.

<a id="h.so57odm09d1j"></a>

### IX-SPACES-091 — Translation job progress, pause and target retry

Controls: translation job status; Pause/Resume; Cancel translation; Retry failed target; Open saved partial result

Atlas surfaces: spaces-translate

Primary: Status detail opens real Preparing/Translating/Review/Applying/Exporting outcomes. Pause/Resume use supported boundaries; Cancel stops unfinished work while retaining saved variants. Retry failed target addresses one target/segment attempt. Open saved partial result shows its actual source/target revision and incomplete scope.

Secondary: Job menu offers scoped operations only; no Retry all from a single failed language without an explicit choice. Inspect never starts capture or export.

Drag: Status/progress bars are not sliders; dragging cannot set percentage or completion. Scroll/text selection remains normal.

Keyboard and touch: Keyboard/touch controls name the target language/job. Escape closes status detail without stopping.

State: Progress uses real units or qualitative state, with no fake denominator. Local-only disclosure forbids silent cloud fallback; missing OCR/voice extraction has a specific recovery.

Integration and persistence: Reopen restores chips and set references. Vision & Voice capture is separately controlled, even when live translation uses the same service.

Acceptance: Fail one target and cancel another during export; successful siblings stay openable, with no cloud fallback or duplicated source mutation.

Basis: TRANS-010–013 and canonical job lifecycle.

<a id="h.asf9zhlia1u8"></a>

### IX-SPACES-092 — Experiences entry, scenarios and sources/rules

Controls: New experience; My experiences; saved experience; Story starters; Worlds & scenarios; Sources & rules; Experience Space options

Atlas surfaces: spaces-experiences

Primary: New experience selects a scenario/starting brief and creates its canonical state/conversation. Saved entries reopen their exact branch/checkpoint. Story starters stages a chosen prompt/scenario; Worlds & scenarios inspects definitions; Sources & rules opens the selected experience revision and eligible resources. Options changes scoped presentation/configuration, not hidden state by implication.

Secondary: Saved experience context offers Open, Rename, Branch and Archive. Scenario context offers Inspect and Start explicitly; inspecting does not create a permanent character or launch Play.

Drag: Scenarios are not drag-to-execute. Resource drops stage authorized references; story cards can reorder a collection only through its explicit handle.

Keyboard and touch: Keyboard/touch navigation preserves the active scene/draft. Escape cancels a starter/configuration choice without altering existing world state.

State: Missing scenario/resource and unavailable media are truthful. Config updates create revisions and identify future effect; no fake image/audio.

Integration and persistence: Sources and fictional character requests never grant real external actions. Formal scored competition uses Play’s distinct match state.

Acceptance: Inspect a starter, cancel, reopen a saved mystery and change a future rule; preserve its original branch and no automatic external action.

Basis: Atlas Experiences navigation; EXP-001/005.

<a id="h.sw20trhdtq24"></a>

### IX-SPACES-093 — Experience choices and state-aware narrative actions

Controls: Ask about the path; Check the tide times; choice buttons; conversation submission

Atlas surfaces: spaces-experiences

Primary: A choice displays/stages or submits its exact declared character action according to its explicit label; it targets the current scene/state revision. Validate prerequisites and visibility, commit the typed transition, then show usable resulting items/clues/objectives. Freeform submission follows the same path. Asking for real tide data requires an eligible real source operation rather than invented current facts.

Secondary: No hidden custom menu on choice buttons; Inspect action may explain preconditions without revealing hidden solutions. Secondary click never performs a turn.

Drag: Choice buttons are not draggable. Inventory-item drag to a target exists only through a declared typed Use action with preview; otherwise select item and choose Use explicitly.

Keyboard and touch: Keyboard/touch activation submits one action; Escape cancels a staged choice. Double activation cannot duplicate inventory changes.

State: Stale state/invalid ownership rejects the transition and preserves user input. Provisional narration remains labelled until commit; failed commit cannot leave a usable invented clue.

Integration and persistence: Character knowledge, player knowledge and hidden state remain separated across panels/generated UI. External effects retain ordinary permission/receipt boundaries.

Acceptance: Activate a choice twice during reconnect and then use an absent item; create one transition, reject invalid use and leak no hidden clue.

Basis: Atlas choice controls; EXP-002/003 and canonical state commit before usable outcome.

<a id="h.53i8ykoyclfj"></a>

### IX-SPACES-094 — Experience state panels, Summary and Action Graph

Controls: Summary; Action Graph; scene/character/inventory/evidence/objective panel; item detail

Atlas surfaces: spaces-experiences

Primary: Summary opens the permitted current state projection. Selecting an item/clue/objective opens its actual identity, ownership/state and eligible actions. Action Graph inspects experience/run transitions and real external-action receipts. Panels never maintain a contradictory local inventory separate from canonical state.

Secondary: Item context offers Inspect and valid Use/Transfer only when declared by scenario rules. Context text, disabled reasons and accessible labels must not expose hidden answers.

Drag: Panel scroll/layout is local. Any declared item drag uses exact typed source/target and validation; invalid drops leave state unchanged. No arbitrary drag to create/copy evidence.

Keyboard and touch: Keyboard list and explicit Use/Transfer menus replace drag. Touch opens readable details; state labels do not rely only on icons.

State: Loading/redacted/stale state shows actual version and limitation. Incoming valid transitions update panels coherently without stealing composer input.

Integration and persistence: Opening an inspector or graph is read-only. A story-state action cannot undo real sends/purchases; those receipts remain linked.

Acceptance: Hide a clue from the player and inspect every panel/context/keyboard label; no hidden fact appears until an authorized reveal transition.

Basis: Atlas Summary/Action Graph and EXP-001–003.

<a id="h.4se8vv7pew8l"></a>

### IX-SPACES-095 — Experience checkpoint, rewind and branch

Controls: Checkpoints; Branches; Branch from here; Rewind; Continue branch

Atlas surfaces: spaces-experiences

Primary: Checkpoints opens saved conversation/state pairs. Select one to inspect its matching state revision; Branch from here creates an independent branch preserving the original. Rewind chooses a supported prior state with scope shown. Continue opens the selected branch and its draft; it does not replay already committed actions.

Secondary: Checkpoint/branch context offers Inspect, Rename branch, Open and permitted Branch/Archive. Historical state edits are annotations/new revisions, not falsification of the checkpoint.

Drag: Timeline drag selects an inspection position only; release does not restore automatically. Branch rows are not drag-to-merge timelines.

Keyboard and touch: Keyboard/touch navigation exposes revision and external-effect warning where relevant. Escape leaves the current branch unchanged before commit.

State: Unavailable checkpoint/changed scenario explains exact recovery limit. Reconnect between commit and narration restores the committed state without another action.

Integration and persistence: Conversation and world revision move together. Rewind cannot reverse external effects, and their receipts remain in history.

Acceptance: Branch before acquiring a clue, then reconnect after acquiring it in one branch; retain consistent independent histories and no duplicated clue.

Basis: Atlas Branch from here/Checkpoints/Branches; EXP-004/006/007.

<a id="h.esgdf3lls7jl"></a>

### IX-SPACES-096 — Study Home, subjects and retained view navigation

Controls: Subjects; My Progress; Revision Bank; New Chat; Recent Study conversations; Summary

Atlas surfaces: spaces-study, study-subject, study-topic, study-session

Primary: Subjects opens the subject catalogue or selected subject context. My Progress opens the learner’s evidence summary; Revision Bank opens the current subject/topic resource catalogue. New Chat creates a topic-bound conversation when a topic is active, otherwise a Study conversation with subject selection. A recent row resumes its exact ConversationID and draft. Session Summary opens the current topic/subject evidence overview, distinct from a runtime task’s Summary.

Secondary: Recent-conversation context commands follow [IX-SPACES-014](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.fvz2br1f1z8s)/017. Navigation buttons have no custom secondary action. A subject reference may expose Open in new host tab without cloning its learning records.

Drag: Fixed Study tools are not reorderable by dragging their labels. Ordinary content scrolling remains available; dragging a recent conversation is governed by explicit group/project movement, never topic reassignment.

Keyboard and touch: Enter/Space activates the focused destination; Back restores the previous view and focus. On touch, the responsive navigation carries the same IDs and preserves draft input.

State: Loading, empty subject list, private/unavailable subject and no recorded evidence have separate states. A selected subject disappearing does not substitute a same-named subject.

Integration and persistence: Home, Subject and Session are separate active views. Switching them retains conversation, source policy, answer draft, scroll and ongoing evaluation; opening a page records no learning success.

Acceptance: Open a topic conversation, enter working, inspect My Progress and return through Recent; retain the draft and exact TopicID without generating an attempt.

Basis: Atlas spaces-study/study-subject/study-topic/study-session, including late Study source/mode controls; APP-STUDY-001 and STUDY-001–005. Navigation mechanics are adopted requirements.

<a id="h.aoj15cn48u9k"></a>

### IX-SPACES-097 — Subject creation, import and topic-tree management

Controls: Create subject; Import subject/topic structure; Subject search; Subject rows; Topic tree; Rename topic; Move topic; Remove topic mapping

Atlas surfaces: spaces-study, study-subject

Primary: Create subject opens a draft for title, optional course/qualification metadata and declared sources. Import previews extracted subject/topic structure and source links before committing stable IDs. Search filters authorised subjects/topics. Selecting a row shows its details; its explicit open action enters that subject or topic. Rename changes presentation while retaining ID and linked evidence.

Secondary: Topic context menu offers Open, Rename, Move within the permitted subject structure, Inspect linked evidence and Remove mapping. Remove previews affected links and detaches the mapping; it does not delete source documents, Forms attempts or Cards review history. Duplicate topic creation must be an explicit command with new identity.

Drag: An enabled tree move handle may drag a topic to a valid parent/insertion point. Show destination and descendants, reject self/descendant targets, auto-scroll within the tree and commit one move on release. Escape/outside drop cancels. A keyboard Move dialog supplies the same operation.

Keyboard and touch: Field editing follows [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1); tree arrows expand/collapse and Enter opens. Touch uses an explicit move command without relying on a narrow drag handle.

State: Invalid cycles, duplicate import identity, missing source and concurrent structure revision block the affected commit with the preserved draft. Partial imports show exactly which rows were accepted.

Integration and persistence: Source attachment authorises only its declared scope. Renaming/reparenting cannot rewrite assessed history; a changed topic mapping triggers the evidence policy’s explicit recomputation.

Acceptance: Rename and move a topic with recorded attempts, then reopen an old attempt; its TopicID and historical question mapping remain intact.

Basis: STUDY-002–006; retained deep-spaces-10.md/11.md subject/topic workspace. Atlas shows subject/topic structures; editor mechanics are target requirements, not proof of implemented commands.

<a id="h.rngw0a13h97p"></a>

### IX-SPACES-098 — Topic rows, recommendations and start actions

Controls: Open topic; Start practice; Open Study session; Revise These topic rows; Recommendation cards; 3 topics

Atlas surfaces: spaces-study, study-subject, study-topic, mobile-study, study-session

Primary: Open topic targets the row’s TopicID and shows its evidence, resources and recent work. Start practice starts or resumes a Practise draft for the selected topic; Open Study session opens its retained session without silently changing a selected mode. A recommendation opens that exact topic and displays its recorded reason. A topic-count link opens the matching filtered list, never all topics regardless of the count.

Secondary: Recommendation context offers Inspect reason, Open topic, Schedule review and Dismiss recommendation. Dismiss changes presentation/preference only; it cannot erase a weak result or mark learning complete. Topic context management follows [IX-SPACES-097](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.aoj15cn48u9k).

Drag: Recommendation cards are not drag-to-complete controls. Dragging their text selects it; scheduling uses an explicit Planner action, not an undocumented drop on a date.

Keyboard and touch: Each Open topic control includes the row’s subject/topic in its accessible name. Keyboard/touch activation follows the same identity and returns focus to the originating row when leaving.

State: Revise These shows at most three distinct eligible topics across all subjects, actual reasons and deterministic ordering. Unassessed topics stay neutral; missing review dates are not overdue. A removed/private topic exposes unavailable state without its private metadata.

Integration and persistence: Inspecting, dismissing, scheduling or starting a draft does not create successful evidence. Mode/source settings persist into the opened session and remain visible.

Acceptance: Create four eligible recommendations across two subjects, dismiss one, then open a count link; show at most three, preserve the underlying attempts and match the filtered count.

Basis: Atlas Study desktop/mobile screens; STUDY-006/007/024/025. Fixture topics and reasons are examples, not real learner results.

<a id="h.o01lksyi9hm8"></a>

### IX-SPACES-099 — Learn, Practise, Test and Review mode controls

Controls: [data-study-mode="Learn"]; [data-study-mode="Practise"]; [data-study-mode="Test"]; [data-study-mode="Review"]

Atlas surfaces: study-session

Primary: Single click selects the next Study workflow. Learn requests explanation/worked examples; Practise prepares answer-and-feedback work; Test opens assessment preparation and rules; Review uses prior learning or an eligible review queue. Selecting a mode updates its selected state and help text, then returns to the same answer/composer context. It does not submit a draft or start a Test timer.

Secondary: No mode context menu. Secondary click does not reset progress, start a fresh assessment or change knowledge policy. The descriptive tooltip is informational.

Drag: Modes are fixed choices, not draggable/reorderable cards. Swiping the containing row scrolls it on narrow layouts without selecting an intervening mode.

Keyboard and touch: Use an accessible single-selection group with arrow movement and Enter/Space activation. The displayed Learn/Practise/Test/Review name remains the accessible name; full explanatory text is a description, not the only name.

State: Unavailable Forms or review capability disables only the relevant launch command with a reason; it does not erase other modes. An in-flight evaluation keeps its captured mode and attempt conditions.

Integration and persistence: Preserve ConversationID, TopicID, sources, answer draft and history. These four Study modes differ from Natural Conversation presentation, Basic/Advanced/Expert setup and Normal/Multi-Agent/Temporary chat type.

Acceptance: Write an answer in Practise, choose Test and back to Practise; retain the draft, record no submission and start no timer.

Basis: Atlas late Study mode controls and their explanatory titles; APP-STUDY-002, STUDY-008–011/017/020.

<a id="h.obtkt1tmkblg"></a>

### IX-SPACES-100 — Study source-policy controls and source inspection

Controls: My sources only; Wider knowledge allowed; [data-study-sources]; Inspect selected sources

Atlas surfaces: study-session

Primary: My sources only scopes future factual answers to the selected eligible corpus. Wider knowledge allowed requests the displayed broader explanation policy for future admission; it does not itself enable web research, cloud disclosure or memory. Inspect selected sources opens the corpus, readiness, revisions and governing binding. A source-bound Specialist’s stricter policy is shown and cannot be silently overridden by a convenient Study toggle.

Secondary: No custom menu on the two choices. A source row uses source workspace commands in [IX-SPACES-111](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.engxvmzwan3)–115; secondary click on the policy label does not detach sources.

Drag: Policy controls are not draggable. Source files can be added only through the explicit attachment/collection drop target; dropping over a policy button cannot broaden it.

Keyboard and touch: Keyboard/touch selection announces the effective scope, including any governing restriction. Focus stays with the control; changing policy never sends the answer draft.

State: Empty/unreadable corpus produces an explicit insufficient-source state. A broadening attempt denied by current authority retains the effective narrow value and explains the conflict. An admitted attempt retains its original policy provenance.

Integration and persistence: Use actual source revisions and evidence distinctions. Personal progress memory can remain enabled independently; remembered subject facts cannot bypass a closed corpus. Broader mode changes need new admission for affected work.

Acceptance: Choose Wider knowledge under a bound Specialist with web and general knowledge disabled; show the governing restriction, preserve memory preference and make no outside request.

Basis: Atlas study-session source buttons; STUDY-009–012 and KNOW-006–018. Knowledge controls remain independent from memory and grants.

<a id="h.dw2plmzc0j88"></a>

### IX-SPACES-101 — Answer and working editor

Controls: Your answer and working; [data-study-answer]; Structured equation/code/essay inputs; Save answer draft

Atlas surfaces: study-session, study-topic

Primary: Click places the caret in the current question’s answer draft. Typing, paste and structured insertions change that draft only. Save draft persists its question/topic/source revision and local editing state; Check answer is the separate submission command. Supported equation/code editors retain structured content and accessible text. A code-run command uses an actual authorised execution route and labels its result separately from assessment.

Secondary: Use the normal text editing menu, plus explicit structured editor commands where provided. Copying working does not submit it; replacing selected text affects only the draft. No right-click answer correctness shortcut exists.

Drag: Drag selects/reorders text only according to the editor. A file/image drop is an attachment action with readiness/provenance, not an automatically interpreted answer. Reject unsupported objects while retaining current working.

Keyboard and touch: Enter inserts a newline where the editor is multiline; IME composition never submits. Tab reaches Hint and Check answer. Touch keyboard/safe areas leave the current line and submission controls reachable.

State: Autosave status distinguishes local draft, saving, saved and failed. A submitted answer becomes a preserved revision; later edits produce a new draft/attempt rather than modifying marked evidence.

Integration and persistence: Drafts are private learner data and persist across Study mode/view changes. No topic score or review date changes until an eligible submitted result exists.

Acceptance: Paste a multiline answer, navigate away during save failure and return; recover the draft, preserve the error and record no scored attempt.

Basis: Atlas study-session textarea and STUDY-013/014/033/034; structured controls use their canonical editors.

<a id="h.adt48yangmb2"></a>

### IX-SPACES-102 — Show a hint, Check answer and evaluation recovery

Controls: Show a hint; [data-action="study-hint-preview"]; Check answer; [data-action="practice-answer"]; Retry evaluation; Request marking review

Atlas surfaces: study-session, study-topic

Primary: Show a hint requests or reveals the next allowed hint and records the actual assistance associated with the draft. Check answer freezes the visible answer/working, question/rubric revision and conditions into one submitted attempt, then shows Awaiting evaluation. Feedback opens against that attempt. Retry evaluation retries the failed evaluation of the frozen submission; Try again creates a new answer draft linked to it. Request marking review records the disputed result and reason.

Secondary: No custom menu on Hint/Check answer. An evaluated result’s menu offers Inspect rubric, Inspect conditions and Request review; it cannot silently edit the learner’s frozen answer or award a mark.

Drag: Hint, submit and result controls are not draggable. Feedback text may be selected/copied; dragging it cannot change a mark or reveal hidden Test feedback.

Keyboard and touch: Explicit Enter/Space on Check answer submits; ordinary answer-editor Enter does not. Announce evaluating/completed/failed once, with focus retained on the result or error summary. Touch repeat taps share the same operation identity.

State: Prevent duplicate submission while pending. Missing question/rubric/evaluator yields a specific preserved-draft or awaiting-review state. Failure/reconnect never creates optimistic marks.

Integration and persistence: Hint use stays in evidence conditions; revealed solutions cannot become unaided success. Corrected marking supersedes with provenance rather than deleting the old evaluation.

Acceptance: Use a hint, submit twice during reconnect and fail marking; retain one assisted attempt and its frozen working, with no proficiency update until an eligible result.

Basis: Atlas study-session/study-topic; STUDY-013–016/036. Check answer is a real submission boundary, not a fixture feedback toggle.

<a id="h.19ike46t5qfg"></a>

### IX-SPACES-103 — Self-confidence, attempt inspection and progress evidence

Controls: Not sure; Fairly sure; Very sure; Inspect attempts; My Progress; Proficiency/status indicators; Review evidence

Atlas surfaces: study-session, study-subject, spaces-study, study-topic

Primary: Selecting Not sure, Fairly sure or Very sure stores the learner’s subjective confidence for the stated topic/attempt context. Inspect attempts opens attributed answer revisions, marks, hint conditions and evaluator provenance. Progress/status activation opens contributing evidence and policy revision. Select an attempt to inspect it; an explicit correction/review command creates an attributed revision and recomputes eligible derived state.

Secondary: Attempt context offers Open question/resource, Copy permitted reference and Request review. A confidence radio has no custom menu. Removing a recommendation is separate from retracting erroneous evidence and cannot hide it from an audit view.

Drag: Confidence choices and evidence rows are not drag-to-grade controls. Evidence ordering is derived by selected sort, never modified by rearranging rows.

Keyboard and touch: Expose a labelled radio group and keyboard selection. Proficiency includes text/icons as well as colour. Tables support focus and reading without announcing another learner’s private results.

State: Show Not assessed, Needs work, Developing or Confident only from available versioned policy. Missing threshold policy leaves raw evidence usable and labels/dates unavailable rather than inventing values. Subjective confidence never substitutes for assessed proficiency.

Integration and persistence: Canonical Forms/Cards events are consumed by stable ID/revision and deduplicated. A page view cannot count the same result again; shared resource access cannot expose other learners’ records.

Acceptance: Choose Very sure for an unassessed topic and reopen one Forms result three times; topic remains unassessed until eligible evidence, and the result is counted once.

Basis: Atlas study-session radio values and Study status text; APP-STUDY-003, STUDY-021–025.

<a id="h.22vby3e0zbc9"></a>

### IX-SPACES-104 — Paper Builder and real Test launch

Controls: Paper Builder; Test preparation; Assessment rules; Start test; Open assessment; Submit assessment

Atlas surfaces: study-subject, study-session

Primary: Paper Builder opens a draft assessment in the Forms-backed creation flow with subject/topics, eligible sources, question set, rubric, obtainable marks and allowed aids. Review the question/rule draft before explicit canonical creation. Selecting Test shows its preparation view. Start test creates the supported learner attempt under displayed timing/attempt rules. Open assessment and submission hand off to the same Forms form/attempt identity.

Secondary: Paper Builder has no implicit right-click Start. A saved assessment menu can Open, Inspect rules, Create a new revision or Enrol in Bank under current ownership. Revision creation does not rewrite an in-progress or submitted paper.

Drag: Question ordering uses the Forms editor’s explicit reorder transaction. Study navigation buttons do not accept question drops. Dragging a topic onto a paper must use a declared topic-selection target and cannot silently change the rubric.

Keyboard and touch: Keyboard navigation distinguishes preparation, Start and Submit. Touch shows time/rule consequences before relevant navigation; Escape before Start cancels preparation only.

State: Unsupported timing, missing marking route or unreadable required source blocks that specific start condition with saved draft. Reconnection resumes Forms’ real attempt/time state; leaving Study does not invent a pause.

Integration and persistence: Forms owns answers, submission, marks and corrections. Study projects linked evidence. No official grade appears without a justified applicable boundary basis.

Acceptance: Select Test, inspect rules, start explicitly, reconnect and reopen in Forms; find one identical attempt with correct timing and no duplicate submission.

Basis: Atlas study-subject Paper Builder and study-session Test; STUDY-017–019 and canonical Forms ownership. Atlas menu stub establishes entry, not a working assessment engine.

<a id="h.uni61ww5a6yv"></a>

### IX-SPACES-105 — Review queue and Cards recall interactions

Controls: Review mode; Review cards; Due topic/resource rows; Reveal/Flip; Rate recall; Next review item; Inspect recommendation reason

Atlas surfaces: study-session, study-topic

Primary: Review opens a queue of eligible prior errors/due topics or an explicitly chosen Cards deck. Inspect reason shows the actual evidence/date. Review cards opens that canonical deck in its real review controller. Reveal/Flip changes the visible side only. A supported recall/rating action records the learner’s actual Cards event, then Next advances according to its queue. Choosing another focus changes queue selection without completing the previous item.

Secondary: Resource/queue context offers Open source, Inspect reason and permitted Skip/Dismiss recommendation. Skipping is not a correct answer. Card editing opens the canonical editor and does not retroactively modify recorded answer conditions.

Drag: Card flip/rating controls are not drag-to-score targets. Any native Cards gesture must retain an explicit accessible equivalent; swiping a Study page cannot invent a recall rating.

Keyboard and touch: Use the Cards keyboard/accessible controls for reveal and separately labelled ratings. Touch scrolling/flip remains distinct from committed rating. Escape leaves the queue with its actual completed position.

State: Unavailable deck/card or changed revision identifies the affected item and allows another eligible item. Offline review only commits where the owner engine supports durable events; otherwise preserve a pending state honestly.

Integration and persistence: Eligible Cards events link to TopicID under the learning policy. Opening notes, revealing a card or reading a solution produces no successful recall evidence.

Acceptance: Open and flip five cards without rating, then rate one; Study receives only the eligible recorded rating, once, and retains the deck’s true review state.

Basis: Atlas Study Review and cards-review route; STUDY-020/024/030. Cards owns the review interaction and ReviewRecord.

<a id="h.esold5m3a8ky"></a>

### IX-SPACES-106 — Revision Bank filters and Create New menu

Controls: Recommended; All; Flashcards; Notes; Papers; Quizzes; Create New; Describe a Resource; Flashcard; Quiz; Document; Presentation; Canvas; Board; Other

Atlas surfaces: study-topic, spaces-study

Primary: Category click filters the current subject/topic’s authorised enrolments and keeps Create New on the category row. Search/sort refine that same scope. Create New opens its exact resource menu: Flashcard→Cards, Quiz→Forms, Document→Write, Presentation→Present, Canvas→Canvas, Board→Boards, Other→supported Build route. Describe a Resource collects the intended output and resolves its real owner before creation. A successful owner receipt enrols the resulting ArtifactID.

Secondary: Category tabs have no custom menu; custom categories use explicit Rename/Remove category management that leaves resources intact. The Create New menu has no secondary creation gesture. Right click never creates a blank placeholder artifact.

Drag: Fixed categories are not draggable. A declared enrolment drop zone may accept authorised canonical artifact references, preview type/topic and add membership on release; it does not copy the underlying content. Reject executable payloads masquerading as documents.

Keyboard and touch: Arrow navigation selects a category; Enter/Space opens a menu choice. Escape closes Create New and restores its trigger. Touch exposes every choice without horizontal overflow hiding the primary action.

State: Distinguish loading, empty filtered results and missing owner capability. Failed/cancelled creation retains a useful draft and produces no phantom Bank entry. Private resource metadata cannot leak through recommendations.

Integration and persistence: Bank is a catalogue, not a separate file store. Filter changes do not edit sources or evidence. A returned same-identity object resolves existing membership instead of silently duplicating content.

Acceptance: Choose Quiz, cancel Forms creation, then enrol an existing deck twice; no empty quiz appears and duplicate deck handling preserves one canonical artifact.

Basis: Atlas study-topic category row and revision-create entry; STUDY-026/027; retained Study UI resource catalogue.

<a id="h.hsebdaz5pkes"></a>

### IX-SPACES-107 — Revision resource open, options and removal

Controls: Resource options including Open in App; Review cards; Read notes; Start quiz; Open in App; Edit; Remove from Bank

Atlas surfaces: study-topic

Primary: The resource’s main action opens its real viewer/interactor: Review cards starts the deck’s review route, Read notes opens the actual Write document, and Start quiz opens Forms rules/attempt preparation. Resource options opens Open in App, Edit, Inspect source/revision and Remove from Bank. Open in App preserves ArtifactID and return context; Edit requires owner editing authority and changes that object, not a local imitation.

Secondary: Secondary click on the resource opens the same options menu. Remove from Bank removes only the enrolment after identifying its subject/topic scope; delete-original is available only as an explicit owner command with its actual consequence. Copy reference copies a permitted identity link, not hidden content.

Drag: A resource reference may drag to an explicit Bank category/topic enrolment target with an insertion/scope preview; release changes membership only. Copy/move semantics are stated in the preview, never inferred from title. Escape cancels. Resource editing surfaces keep their own drag rules.

Keyboard and touch: Enter activates the selected resource; context key exposes options. Touch long press opens the same menu, with Open/Edit available as visible accessible actions.

State: Missing/revoked artifact stays unavailable only where permitted metadata may remain. Unsupported embedding routes to the actual owner without fabricating a viewer. Concurrent removal preserves any genuine owner edits.

Integration and persistence: Rename/move tracks canonical identity. Opening a deck or quiz does not itself record successful learning, and removal does not erase historical evidence.

Acceptance: Edit notes through Open in App, return, remove their Bank enrolment and reopen in Write; same document and edit persist, with Bank membership gone.

Basis: Atlas study-topic resource tiles; STUDY-028–030; Cards/Forms/Write are canonical owners.

<a id="h.9v4ridjieg9r"></a>

### IX-SPACES-108 — Study Planner scheduling and return to topic

Controls: Planner; Open Study planner; Schedule review; Upcoming session rows; Reschedule; Open topic session

Atlas surfaces: spaces-study, study-subject, study-topic, study-session, mobile-study

Primary: Planner and Open Study planner open the single canonical Study burrow. Schedule review creates a draft event/task with the exact TopicID, destination, date/time/timezone and any recurrence/reminder fields; explicit commit applies the canonical Planner mutation. An upcoming session opens its linked topic-bound work. Reschedule edits that exact event/occurrence through Planner and returns its real receipt.

Secondary: Upcoming-item context follows Planner: Open, Edit/reschedule, inspect recurrence scope and permitted Cancel/delete. A study recommendation’s Schedule command does not imply invitations or publication. Remove recommendation remains separate from deleting a calendar item.

Drag: Calendar drag uses Planner’s event/occurrence preview, time snapping and explicit recurrence scope. Study recommendation cards themselves are not date-drop calendars; their keyboard Schedule command supplies the same reachable workflow.

Keyboard and touch: Dates/times are editable without pointer-only dragging. Touch opens a full scheduling form with timezone and destination visible; back navigation preserves a pending draft.

State: Missing Planner/burrow, timezone ambiguity, concurrent event edit or denied mutation retains the proposed schedule and explains the affected operation. No real receipt means no scheduled badge or reminder claim.

Integration and persistence: Shared BurrowID/event/task identities prevent a second calendar and duplicate reminders. A suggested date is not a scheduled event, completion or an external assignment deadline.

Acceptance: Schedule one topic review, reschedule it in Planner and return to Study; show the same event exactly once with new time and no added attendees.

Basis: Atlas Study Planner routes; STUDY-031/032 and canonical Planner integration. PlannerTask IDs remain distinct from runtime TaskIDs.

<a id="h.p4xbg1o9cx3q"></a>

### IX-SPACES-109 — Source collection creation, selection and identity

Controls: Sources workspace; Collection picker; Create collection; Rename collection; Collection scope; Open collection

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Open source workspace enters the selected Specialist’s collection/chat context. The collection picker selects an authorised CollectionID; selection previews membership and effective policy without rebinding another Specialist. Create collection opens title and explicit owner/scope fields; Save creates one canonical collection. Rename changes the title only. Open collection resumes selected sources, viewer position and conversation.

Secondary: Collection context offers Open, Rename, Inspect scope and permitted Archive/Remove collection. Removal states membership/binding consequences and never deletes original Files/artifacts implicitly. Copy reference copies only an authorised collection link.

Drag: Collections are not draggable between privacy scopes. A drag of the title cannot change owner/share status. List scrolling and text selection remain normal.

Keyboard and touch: Picker search and single selection use [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/005. Escape cancels creation with a retained unsaved draft; keyboard/touch has explicit scope and Open actions.

State: No collection, inaccessible collection and empty accessible collection differ. A stale save checks the collection revision and preserves user text. Duplicate creation after timeout resolves by operation identity rather than adding another collection.

Integration and persistence: The workspace is a Specialist capability inside Spaces, not another app or file store. Reusing a collection retains bytes and access rules. Scope selection cannot grant access to unauthorised participants.

Acceptance: Create a private collection, bind two permitted Specialists and rename it; both retain the same CollectionID while an unauthorised account receives no title/count preview.

Basis: Owner-defined Specialist workspace, KNOW-001–003/019–021; no dedicated registered Atlas screen. These are adopted controls over the existing Specialist/Files/artifact services.

<a id="h.d6tjuvgsdddk"></a>

### IX-SPACES-110 — Bind a Specialist to a collection and inspect effective policy

Controls: Bind sources; Collection binding; Save binding; Unbind collection; Effective knowledge policy; Binding revision

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Bind sources chooses an authorised collection and previews selected-source policy. Save binding commits SpecialistID/definition revision, CollectionID and binding revision once. The initial factual policy has web research and general knowledge disabled; memory is independently retained/configured, never automatically switched off. Effective policy opens the binding/request/access intersection. Unbind explicitly removes that association for future requests and exposes the resulting policy before commit.

Secondary: Binding summary has Inspect collection, Inspect revision and permitted Change/Unbind. Secondary click on its status cannot silently weaken evidence constraints or copy one Specialist’s private binding to another.

Drag: Binding is not changed by dropping a collection on the portrait or model picker. Use the explicit binding selector; ordinary text dragging has no authority effect.

Keyboard and touch: Keyboard/touch can inspect every policy separately and save/cancel. Escape closes the draft without changing the confirmed binding. Restore focus to the configuring Specialist.

State: Revoked collection or conflicting definition revision blocks Save with its exact reason. Existing admitted runs keep captured provenance; narrowing/revocation affects future eligible retrieval at supported boundaries with Received/Applied state where material.

Integration and persistence: Changing model, presentation mode or retry does not drop the binding. Unbinding is not approval for outside search, disclosure or unrelated tools; each remains governed by its current policy.

Acceptance: Bind with memory enabled, retry on another model and then revoke one source; memory remains independently enabled, binding persists and the revoked source is excluded from future retrieval.

Basis: KNOW-006–009/015 and Specialist configuration; new owner policy with source-only factual default and separately controlled memory.

<a id="h.engxvmzwan3"></a>

### IX-SPACES-111 — Add files, URLs, pasted text and canonical sources

Controls: Add sources; Upload/select files; Add URL; Paste text; Choose native artifact; Import preview; Add selected sources

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Add sources opens typed acquisition choices. Files selects real authorised files; URL accepts a specific source address; Paste text creates a scoped source with provenance identifying the supplied text; Native artifact chooses its canonical ID/revision. Import preview shows identity, type, scope and expected adapter before Add selected sources creates memberships and queues eligible ingestion. Adding a URL does not enable general web search or recursively add linked pages.

Secondary: Normal input edit menus apply. A candidate source menu offers Inspect origin and Remove from pending import; it cannot delete the original. Clipboard paste requires the user’s explicit gesture rather than background reading.

Drag: The labelled source-list import zone accepts supported files or canonical references after [IX-BASE-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.oj2yi5h8kpkm) threshold. Show candidate count/type and authorised scope, then commit memberships on drop. Reject arbitrary executable payloads and unrelated UI drags. A text drop into URL/text fields edits that field only. Escape cancels a pending drag.

Keyboard and touch: Each drop path has a file/reference picker equivalent. Keyboard validates URL/text fields without implicit submission; touch offers multi-file selection and individually removable candidates.

State: Duplicate source identity resolves existing membership or an explicit new revision. Unsupported type, auth wall, acquisition failure and insufficient access remain named states; uploading does not claim indexing or verified citation.

Integration and persistence: Source instructions remain untrusted content. Access/disclosure policy still governs extraction routes. Actual originals remain their owner objects.

Acceptance: Add a URL containing many links and a failed scanned PDF; ingest only the chosen URL, expose the PDF’s real coverage/failure and invent no sources or indexed pages.

Basis: KNOW-002–005/020 and canonical Files/artifact ownership; new source-workspace controls. Supported acquisition comes from registered adapters, not assumed media comprehension.

<a id="h.imlcgorowen3"></a>

### IX-SPACES-112 — Ingestion status, partial coverage and retry controls

Controls: Source readiness indicator; Ingestion details; Retry failed parts; Cancel ingestion; Remove pending source; Extraction coverage

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Activate a readiness indicator to inspect current Selected/Queued/Fetching/Extracting/Indexing/Ready/Partially ready/Unsupported/Failed/Unavailable/Revoked state and actual coverage. Retry failed parts resubmits only identified failed units under the captured source revision and current policy. Cancel ingestion requests cancellation of outstanding acquisition/extraction/indexing; Remove pending source additionally removes its membership after stating any retained original.

Secondary: Source status context offers Inspect progress, Retry eligible failure, Cancel pending work and Open original when authorised. Ready does not have Retry merely to fabricate fresh evidence; Refresh is a distinct revision action.

Drag: Progress bars and coverage maps are read-only, not seek/percentage sliders. Dragging status cannot reorder pipeline stages. Source-list scrolling must remain usable during updates.

Keyboard and touch: Keyboard/touch opens full status text rather than requiring hover. Announce meaningful transitions and final failure, coalescing per-segment noise. Cancel is reachable without dismissing the entire workspace.

State: Cancellation shows requested until acknowledged; an already completed part remains truthfully ready. Lost connections retain the actual unknown/pending state, not success. Partially ready lists missing pages/tables/transcript ranges and adapter limitations.

Integration and persistence: A run may use only eligible available material and carries coverage. Previous usable revisions remain available only under current access/retention. No status implies factual verification of content.

Acceptance: Fail indexing two of ten segments, retry them and cancel one; preserve the eight successful segments, record actual coverage and prevent a full-source-ready claim.

Basis: KNOW-004/005/023; source-workspace target controls, not a claimed Atlas ingestion implementation.

<a id="h.dm00is5vzrum"></a>

### IX-SPACES-113 — Source selection, select all and pinned revisions

Controls: Source checkboxes; Select all; Clear selection; Selected sources count; Use latest revision; Pin revision; Inspect selection snapshot

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A source checkbox changes eligibility for future request admission within the current collection. Select all selects the permitted current membership snapshot; Clear selection deselects without removing membership. The selected count opens the exact IDs/readiness. Use latest chooses the declared current-version policy; Pin revision selects an inspectable revision. Inspect selection snapshot shows what a specific admitted run actually received.

Secondary: Source context offers Open, Inspect revision, Select/Deselect and permitted Pin/Use latest. These commands affect eligibility, not original access or authorship. Select all is not a share or upload command.

Drag: Checkboxes are not drag handles. A pointer drag used for list selection cannot silently toggle every crossed source unless an explicit accessible range-selection gesture is active; range selection previews before commit. Reordering display does not change source precedence.

Keyboard and touch: Space toggles the focused checkbox, Shift selection uses visible ordering and Select all has a clear scope. Touch has separate row-open and checkbox hit targets; neither accidentally triggers the other.

State: Mixed header state reflects actual selected eligible rows. Disabled/revoked sources retain a reason, never a usable checkmark. A new membership after Select all is not retroactively added to the captured run.

Integration and persistence: Selection/view changes preserve chat draft and do not re-answer previous messages. Empty selection produces a bounded no-source outcome rather than outside fallback.

Acceptance: Select all, send, then add a source and pin another revision; the first run retains its exact original selection while future admission uses the new declared policy.

Basis: KNOW-002/003/015/016/019; source membership and request admission are distinct versioned operations.

<a id="h.9af1hpc07eaa"></a>

### IX-SPACES-114 — Source viewer, exact citations and passage navigation

Controls: Open source; Citation chip; Page/section/time navigation; Find in source; Open original; Copy passage; Close source viewer

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A source row opens its selected revision; a citation opens the captured SourceRevisionID and supporting passage in context. Page/section/cell/time navigation moves within the actual extracted mapping. Find searches the permitted representation and identifies coverage. Open original routes to the canonical owner/URL while retaining return context. Copy passage copies the selected permitted text with its source/revision reference; Close returns focus without clearing source selection.

Secondary: Passage context offers Copy, Copy citation/reference, Open original location and Save sourced note. It cannot add a fabricated quotation, page number or unseen transcript. A citation menu may inspect provenance and report unsupported/mismatched evidence.

Drag: Drag selects text or a valid mapped region. Viewer panning/scrolling follows its media controls; dragging a citation to a declared note target copies a reference, not unrestricted source bytes. No citation drag grants access.

Keyboard and touch: Keyboard can reach citations and precise destinations; use accessible location labels rather than colour alone. Touch supports text selection and a visible source menu without requiring hover.

State: Missing precise mapping discloses the coarser location. Revoked/unavailable revisions show access-limited state without cached snippets. A failed source load retains the answer and usable provenance metadata.

Integration and persistence: Validate citation belongs to the admitted corpus, was available and supports the claim. Retrieval miss differs from proven absence; viewer coverage must not conceal that distinction.

Acceptance: Open an old PDF citation after refresh and revocation; resolve its exact revision when permitted, otherwise show access limitation with no leaked excerpt or invented page.

Basis: KNOW-010–014/019 and source-location mapping. Citation controls also implement [IX-SPACES-038](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.vazrabgfyxh5) for a bound corpus.

<a id="h.bfome5y22002"></a>

### IX-SPACES-115 — Independent web, general knowledge and memory controls

Controls: Web research; General knowledge; Memory; Memory scope; Knowledge policy details; Save policy

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Web research enables/disables eligible outside retrieval for future admission. General knowledge controls factual explanation beyond the bound corpus without granting web. Memory independently controls eligible personal continuity, preferences and learner progress and exposes its scope. Save policy commits the exact displayed choices and revision. Details explains effective restrictions and source precedence. Changing one setting never toggles either of the others.

Secondary: No custom toggle context menu. Memory scope opens its own authorised selector; secondary click cannot expose private remembered data. Inspect stored memory and correct/forget use canonical memory controls.

Drag: Toggles and scope fields are not draggable. A source drag cannot enable web/general knowledge or broaden memory. Text descriptions remain selectable.

Keyboard and touch: Each toggle has its own label/state and keyboard activation. Touch keeps related descriptions visible so Memory is not mistaken for source content. Escape exits an unapplied policy draft.

State: A governing restriction locks only the affected choice and explains its origin. Failed save retains confirmed and pending values separately. Revocation affects future retrieval/disclosure at supported boundaries; already transmitted context is reported honestly.

Integration and persistence: Bound-source precedence persists with broader knowledge enabled. Personal learner history may select practice while remembered subject facts cannot bypass closed-corpus evidence. Natural modes and model effort cannot change these policies.

Acceptance: Enable Memory with web/general knowledge off, then toggle web alone; retain memory and general settings, label external evidence and never present it as a bound-source quotation.

Basis: Owner-defined KNOW-006–010/015 and independent control table in canonical chapter 5.3.4; no mode-based capability gates.

<a id="h.vvv4a0r4n80z"></a>

### IX-SPACES-116 — Bound chat admission and unsupported-answer recovery

Controls: Source-bound composer; Selected corpus summary; Send; Inspect answer support; Add/select missing source; Change knowledge policy

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Send admits the draft against the displayed Specialist/binding/policy and selected source revisions, including allowed memory scope. Inspect answer support opens claim-to-evidence links, derivations and unresolved support. When evidence is insufficient, Add/select missing source opens the source selector without discarding the question; Change knowledge policy opens the independent controls and requires explicit re-admission for broader work. A response may continue with its supported portion.

Secondary: Message context retains Copy, Branch, Retry and source inspection. Retry preserves the binding and captured/revalidated policy; it is not an outside-knowledge escape hatch. An unsupported claim can be reported/requested for review without asserting its opposite.

Drag: Composer text/attachments follow their existing drag rules. Dropping a source reference explicitly selects/attaches it only through the declared target; it cannot alter the corpus behind a submitted run.

Keyboard and touch: Keyboard/touch can inspect selected sources before Send and reach the missing-evidence repair action afterward. The bounded answer does not trap focus in a modal or prevent independent useful work.

State: Empty/unavailable corpus, failed retrieval and inspected absence are separate outcomes. Do not fabricate citations or silently route to outside search/cloud. Provisional streaming remains labelled until actual support checks finish.

Integration and persistence: Sources are evidence, not instructions. Delegates receive only eligible subsets. Old unrestricted conversation text cannot become source evidence through compaction or retry.

Acceptance: Ask for a fact absent from selected material, then retry with another model; retain the binding, explain the evidence gap and make no hidden broader retrieval.

Basis: KNOW-009–015/018; shared composer [IX-SPACES-018](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.bai4aclewxqc)–028 remains the interaction base.

<a id="h.jplbdj11rhs9"></a>

### IX-SPACES-117 — Refresh, stale evidence, removal and revocation

Controls: Refresh source; Choose revision; Stale indicator; Review affected outputs; Remove from collection; Revoke access

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Refresh acquires the declared source again and creates an inspectable revision with actual extraction status. Choose revision updates future selection/pinning. Stale indicator opens affected answers/notes/assumptions and their captured evidence. Review affected outputs identifies concrete update work; it does not rewrite them automatically. Remove from collection detaches membership. Revoke access uses the owner access service and stops future eligible retrieval/disclosure at supported boundaries.

Secondary: Source menu exposes Refresh, Inspect revisions, Pin/Use latest, Remove membership and permitted owner access controls. Delete original is an explicitly scoped owner command, never a synonym for Remove. Historical citations remain attributable where retention allows.

Drag: Revision lists are not drag-to-overwrite histories. Removal cannot be triggered by dropping on blank space. Any explicit removal target must show membership-only scope and offer the same keyboard command.

Keyboard and touch: Keyboard/touch exposes stale/access text and affected-work links. Escape before a commit leaves membership and revision unchanged; cancel refresh does not restore already revoked access.

State: Refresh failure keeps the last permitted revision identifiable and marks freshness honestly. Revoked content loses snippets/previews/caches under the access contract; pending cleanup is not reported complete.

Integration and persistence: Running work remains attributable to its admitted revision unless explicitly refreshed. New evidence can invalidate future context but cannot silently change exported files or committed external effects.

Acceptance: Refresh a source, remove its membership and revoke original access; distinguish all three effects, mark dependencies stale and prevent an old summary from reconstructing revoked text.

Basis: KNOW-016–018/023; membership removal, original access and privacy erasure are separate operations.

<a id="h.qnmiq0lx9vne"></a>

### IX-SPACES-118 — Source priority and conflicting-passage review

Controls: Source priority rules; Conflict indicator; Compare passages; Mark source disputed; Save priority rule; Clear priority rule

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Conflict indicator opens both actual claims with source revisions, dates/scopes and citations. Compare passages aligns their relevant context. Source priority rules show any existing precedence and allow an authorised explicit ordered/rule-based edit with scope. Save records that revision; Clear removes the rule for future admission. Mark source disputed records the user’s concern without deleting its text or automatically confirming the competing claim.

Secondary: Passage menu uses citation/source actions. Priority-rule context offers Inspect, Edit and Remove rule; it cannot rewrite historical outputs to pretend the rule existed earlier. No right-click Choose true action is provided.

Drag: If priority is an ordered list, only its explicit handles reorder eligible entries with insertion preview and Escape cancellation; source display order elsewhere does not set precedence. Keyboard Move up/down supplies the equivalent transaction.

Keyboard and touch: Keyboard/touch can read each passage and provenance separately. Focus remains on the selected conflict after opening a source viewer and returning.

State: Unavailable comparison material shows incomplete evidence. Concurrent rule edits require a revision check, preserving the draft. No applicable priority leaves an explicit unresolved difference rather than averaging incompatible statements.

Integration and persistence: A user correction can alter instructions or supply evidence but cannot make unsupported content source-supported. Support status remains separate from user confirmation in the Assumptions Bank.

Acceptance: Compare conflicting dated rules with no priority, then save one explicit rule; new work cites its application while old answers retain their original evidence and policy.

Basis: KNOW-010/012/014/022; priority establishes declared evidentiary basis without claiming documents are objectively correct.

<a id="h.eoupw98rggev"></a>

### IX-SPACES-119 — Save sourced notes and reopen canonical artifacts

Controls: Save sourced note; Choose Write/Boards destination; Note title; Open note; Edit note; Add note as source

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Save sourced note captures selected text or a reviewable generated synthesis plus upstream citations into an actual chosen Write/Boards artifact. Destination/title fields identify owner, scope and new versus existing target. Save waits for the canonical receipt. Open/Edit note uses that same ArtifactID and revision; return retains source/chat context. Add note as source is a separate explicit membership action that preserves its generated/user authorship and upstream lineage.

Secondary: Note reference menu offers Open, Edit where authorised, Inspect sources, Copy reference and Remove workspace link. Deleting original remains an owner operation. A generated note cannot label itself independent corroboration of its own source.

Drag: Drag selected source text to an explicit note draft target inserts quoted material plus provenance for review, not an immediate file write. Drag a note reference to the sources import zone invokes the separate Add-as-source preview. Escape cancels before commit.

Keyboard and touch: Keyboard has Save sourced note and destination selector alternatives. Touch exposes the source menu and save progress; focus moves to the created reference only after receipt.

State: Failed/denied save retains draft content and destination; no phantom note card appears. A newer owner revision uses normal conflict handling rather than overwriting edits.

Integration and persistence: Source restrictions apply to exported excerpts and collaborators. Notes are useful authored artifacts, not replacement source truth; updates/staleness preserve citation lineage.

Acceptance: Save a sourced note, edit it in Write and add it as a source; retain one owner artifact, its changed revision and explicit derived provenance without counting it as independent evidence.

Basis: KNOW-019–021; existing owner-app documents/Boards notes, not a notebook-specific note database.

<a id="h.yd5yu6cfkh4"></a>

### IX-SPACES-120 — Check assumption support against the selected corpus

Controls: Check against sources; Choose assumption revision; Evidence result; Supports; Contradicts; Insufficient evidence; Open supporting passages; Review dependent work

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Check against sources selects the exact AssumptionID/revision, eligible collection/selection and governing policy, then requests a bounded evidence check. The result records Supports, Contradicts or Insufficient evidence with actual passages, coverage and run provenance. Open passages uses the precise source viewer. Review dependent work opens the affected future plans/context; a user can separately confirm/correct/dismiss the assumption through the Bank.

Secondary: Evidence-result context offers Inspect sources, Copy permitted reference and Retry check with captured/current declared revisions. There is no Accept result command that automatically confirms the user proposition or authorises effects.

Drag: Support labels are read-only results, not draggable status chips. Dragging a cited passage into a correction draft adds a reference but does not submit it.

Keyboard and touch: Keyboard/touch can inspect the full proposition, source scope and evidence before requesting the check. Announce completed status without using colour as its only meaning.

State: Changed assumption/source revision marks the result stale. Unsupported or incomplete retrieval produces Insufficient evidence with scope, not Contradicts by absence. Failed checks retain the pending request and existing review state.

Integration and persistence: Support status and user Confirmed/Dismissed state remain independent. Contradictory evidence challenges future dependent work even when a user previously confirmed; a correction preserves historical effects and actual remediation needs.

Acceptance: Confirm an assumption, find a contradictory passage, then dismiss its review prompt; keep the contradiction visible and dependent work flagged, with neither confirmation nor dismissal erasing evidence.

Basis: KNOW-022 and ASSUMP-004–008; explicit owner-requested Assumptions Bank/source integration, not a new truth engine.

<a id="h.92mbjuvujej0"></a>

### IX-SPACES-121 — Natural Conversation presentation preferences and overrides

Controls: Automatic response presentation; Chat; Agentic Chat; Worker; Design; Use automatic; Presentation diagnostics

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: In response/settings controls, Automatic uses the inferred presentation. An explicit Chat, Agentic Chat, Worker or Design preference sets the stated conversation/request scope; Use automatic clears that override for future presentation. The user’s natural-language instruction is an equivalent scoped override. Diagnostics may show effective choice, policy revision and boundary applied. None of these commands sends/resubmits a message or creates a conversation.

Secondary: No custom secondary action. Right click cannot switch models, tools or permission profiles. Automatic choice does not inject a recurring mode announcement or personality explanation into chat.

Drag: Presentation choices are fixed, non-draggable options. Dragging a preview or document never changes the underlying capability set by selecting a mode.

Keyboard and touch: Single-selection keyboard/touch controls expose effective versus automatic choice. A switch preserves focused input, expanded logs and accessible preview controls. Escape closes preference UI without changing an unapplied choice.

State: Apply changes at a coherent message/stream boundary, preserving prior emitted content. Missing/invalid preference falls back to Chat presentation only. Reconnect restores the same run and effective user override.

Integration and persistence: Tools, grants, model/route, source policy, budget, context and capabilities remain unchanged. Chat stays friendly and responsive to personality; Agentic Chat uses meaningful progress; Worker is professional; Design gives real preview prominence. Explicit user tone/depth instructions prevail.

Acceptance: Override Design during a running task, then request a detailed log in Chat; keep one run and all capabilities, preserve the draft and start no repeated tool or model load.

Basis: Owner clarification 21 November 2026, NCONV-001–004/008–011; new policy, not an Atlas mode selector. Ordinary automatic switching is silent.

<a id="h.z2oyg7b3neqy"></a>

### IX-SPACES-122 — Thinking, Planning and available reasoning/activity details

Controls: Thinking/Planning status; Activity details; Expand/collapse reasoning or summary; Trace provenance; Show more events; Copy permitted activity

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: A useful Chat status contains one task-relevant line. Agentic updates use real milestones/blockers. Activate Activity details to inspect the permitted event stream, with separate labelled model-exposed reasoning, tool/diagnostic activity and generated summary where available. Expand/collapse affects presentation only. Show more retrieves the next scoped events; Copy copies the selected permitted content with its type/provenance.

Secondary: Status text has no action beyond an explicit details affordance. Event context offers Inspect related target and Copy permitted event/reference; it cannot reveal protected credentials, another participant’s private context or hidden provider reasoning.

Drag: Details are scrollable/readable, not draggable to change execution order. Selecting text or scrolling history suspends follow-latest according to the log contract; a new event must not steal the reader’s position.

Keyboard and touch: Keyboard/touch expansion keeps focus anchored and announces concise state changes. The same details are reachable in all Natural modes. Collapse coalesces announcements without deleting available events.

State: No exposed trace is labelled Unavailable from this route; show an honest activity record/summary instead. Waiting for a tool is reported as waiting, not fabricated analysis. Redacted/missing portions retain truthful status.

Integration and persistence: Worker offers the fullest actual permitted trace available but cannot guarantee hidden reasoning. User preferences can suppress routine chatter without hiding required review/failure controls. No raw private chain-of-thought is reconstructed or stored as an assumption.

Acceptance: Switch from a trace-exposing route to a summary-only route and request details; distinguish their actual outputs, preserve log access and fabricate no raw reasoning.

Basis: NCONV-004–006/011 and shared Action Log. Actual provider-exposed permitted reasoning differs from generated summary and diagnostic events.

<a id="h.6xbfh5msl8wl"></a>

### IX-SPACES-123 — Natural Design preview controls and final-only backends

Controls: Working status; Live preview; Preview revision; Zoom/Fit; Open actual output; Inspect job; Show/hide preview

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Show preview opens the actual job/output preview; Hide collapses it without cancelling the job. Zoom/Fit changes the viewer only. Preview revision identifies the displayed provisional/final content; Inspect job opens its real state. Open actual output navigates to the canonical artifact or delivered file when it exists. Working is visible only while actual work is underway.

Secondary: Preview context uses its real media/artifact commands: Open owner, Copy permitted reference and Download only an available output. No Generate next stage command fabricates intermediate images or progress.

Drag: Pan/zoom follows the real viewer; artifact manipulation follows the owning editor, never a decorative preview. A dropped source goes through explicit input attachment and does not silently restart generation.

Keyboard and touch: Keyboard/touch can zoom, fit, inspect and close; screen-reader description identifies provisional state and latest actual revision. Mode switching preserves focused viewer controls and user pan/zoom where content remains compatible.

State: A final-only backend shows truthful Working then its delivered result. A failed update can retain the last valid preview labelled with its revision/staleness. Empty preview states explain pending/unavailable output without simulated stages or percentages.

Integration and persistence: Preview identity differs from saved native output and completed export. Showing Design cannot grant tools or force a new job; a live preview remains usable in Chat/Worker.

Acceptance: Run final-only generation, switch to Design and reconnect; show no invented preview sequence, then open exactly the delivered canonical output when its receipt arrives.

Basis: Owner NCONV-007–012; shared preview semantics [IX-SPACES-060](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.tawkkeizx7pe)/063–065. This presentation is available in every mode.

<a id="h.5w64vspd8g23"></a>

### IX-SPACES-124 — Open and filter the shared Assumptions Bank

Controls: Assumptions Bank; Scope filter; Status filter; Review priority; Project/conversation filter; Affected work filter; Time filter; Search; Assumption rows

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Open Bank enters the relevant authorised scope and current review list. Filters independently select scope, truth/support status, review state, priority, project/conversation, affected work and time; Search matches permitted propositions/metadata. Selecting a row opens its exact AssumptionID/revision and preserves list position. Clear filters restores the scope’s full permitted list, not global private data.

Secondary: Row context offers Inspect, Confirm displayed revision, Correct, Dismiss review and Open affected work. Commands may be unavailable for stale/read-only entries with reasons. Bulk selection does not imply bulk confirmation; any bulk review command must enumerate exact revisions and scope.

Drag: Rows are not draggable between scopes. Reordering by drag cannot change review priority or turn a local assumption into global memory. Normal text selection/scrolling applies.

Keyboard and touch: List/filter keyboard interaction follows [IX-BASE-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.gwmcnrcmysx1)/005; touch exposes review actions without hover. Escape closes detail and returns to the selected row.

State: Loading, empty filter, inaccessible scope and stale reference differ. Counts include only permitted entries. A Temporary conversation’s Bank view follows its temporary retention; it is not automatically durable cross-Space memory.

Integration and persistence: Logging records meaningful uncertainty and dependencies, not every trivial choice. A passive pending count never blocks all work or authorises consequential actions.

Acceptance: Filter two projects with different access, open a tentative entry and clear filters; show only eligible scope and never leak another project’s proposition or count.

Basis: Owner ASSUMP-001–006/013, 21 November 2026; shared Dulche-owned data surfaced in Spaces, no separate app or database.

<a id="h.7x5z9so61gt0"></a>

### IX-SPACES-125 — Inspect assumption provenance, dependencies and history

Controls: Assumption details; Evidence/source links; Affected work links; Revision history; Decision time; Confidence basis; Review priority; Reversibility

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Details shows the exact proposition, scope, truth/support status and separate review state. Evidence opens permitted source/context references; Affected work opens the precise conversation/run/plan/artifact revision or step. Revision history selects a historical proposition without making it current. Confidence shows its stated basis where useful; absence of a numerical confidence remains valid. Priority/reversibility explains why review matters and which effects already exist.

Secondary: A reference menu offers Open and Copy permitted link. Historical revision context offers Compare with current and Inspect dependencies; Confirm/Correct always targets an explicitly displayed eligible revision, not an invisible newer one.

Drag: Reference dragging into an explicit draft links identity only; it cannot copy hidden evidence, alter dependency edges or change scope. Timeline scrolling is inspection, not state restoration.

Keyboard and touch: Keyboard/touch reaches all metadata and references with clear labels. History selection announces historical/current state and returns to the same entry when closed.

State: Missing/revoked evidence remains unavailable without reconstructed snippets. A stale assumption reference directs to the current eligible revision while retaining historical attribution. No outcome is declared repaired merely because a newer proposition exists.

Integration and persistence: Compaction/memory carries AssumptionID/revision/status and resolves current corrections before future reliance. Historical exported or external effects stay inspectable under retention.

Acceptance: Inspect a compacted assumption with revoked evidence and a newer correction; preserve uncertainty/history, expose the current revision and reveal no revoked text.

Basis: ASSUMP-002–005/007–009/012 and KNOW-022; visible concise evidence, not raw private reasoning.

<a id="h.rcxt9yl34tn"></a>

### IX-SPACES-126 — Confirm, correct, dismiss and retract assumptions

Controls: Confirm; Correct; Replacement proposition; Scope; Save correction; Dismiss review; Retract; Mark stale

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Confirm commits the exact displayed proposition/revision and stated scope with actor/time. Correct opens replacement text and relevant scope, then Save correction creates a new revision and marks the old proposition Corrected. Dismiss review suppresses that review while leaving a tentative proposition tentative. An authorised Retract records invalidity/reason; Mark stale records that evidence/scope no longer applies. These actions are distinct and individually labelled.

Secondary: The row’s secondary menu offers the same commands, not a one-click Accept all shortcut. Confirm is unavailable on a historical superseded revision unless the user deliberately creates a new scoped proposition from it. Dismiss is never labelled Agree.

Drag: Status chips are not drag targets for confirmation/correction. Dragging text into the replacement field edits only its draft; dropping onto Save cannot commit.

Keyboard and touch: Keyboard/touch can inspect the original while editing replacement. Escape preserves or explicitly discards the correction draft without changing confirmed state. Confirmation of the focused button is separate from newline in a multiline correction.

State: Revision checks reject stale-window overwrites and present the latest value alongside retained draft. Save failure distinguishes not received from received/pending propagation. Repeated clicks share one correction operation.

Integration and persistence: Record correction first, then mark dependent future context/plans stale and notify affected work at safe boundaries. User confirmation is not factual proof, permission or automatic global memory; existing external effects require actual remediation.

Acceptance: Dismiss a tentative entry, then correct it from two windows; keep it tentative after dismissal and prevent the older save from overwriting the newer correction.

Basis: ASSUMP-004–009/013; product review commands are separate from action authority and source support.

<a id="h.hwfdz2t4t1wa"></a>

### IX-SPACES-127 — Deferred assumption questions and correction application

Controls: Pending review indicator; Deferred question; Confirm/Correct/Dismiss this question; Received; Applied; Review affected plan; Resume affected work

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Pending review opens still-relevant questions for the current authorised work. A question displays its precise proposition, consequence and priority; Confirm/Correct/Dismiss targets only that entry/revision. Received means the durable correction arrived; Applied means affected future context/work acknowledged it at a supported boundary. Review affected plan opens its concrete revision/diff. Resume uses the actual task checkpoint after required dependencies and existing authority are satisfied.

Secondary: Question menu offers Open assumption and Open affected work. Dismiss this question affects review presentation only, not other queued questions or the proposition’s support. No right-click action sends external reminders or schedules a background run.

Drag: Questions are not drag-to-prioritise authorisations. Rearranging a visible list cannot change criticality or grant an action. Draft correction text remains normally selectable.

Keyboard and touch: Keyboard/touch response controls remain available when reopening the relevant conversation. Group related prompts with accessible individual targets; avoid surprise focus theft while the user is typing.

State: Before showing, remove/resolve obsolete prompts after corrections, scope changes or completed work. A stale answer opens the current revision. Propagation delay remains Received, never falsely Applied.

Integration and persistence: Independent useful work can continue. A critical missing permission/decision stays governed by its existing contract; logging an assumption cannot authorise it. No local pending indicator sends an email, call or external notification.

Acceptance: Queue two questions, correct one before return, then revisit the workspace; show only relevant prompts, apply one answer to its dependencies and send no unsolicited message.

Basis: ASSUMP-007–011; deferred review occurs at a naturally active interaction without unsolicited external messaging or new background permission.

<a id="h.hj0cr34yx5ow"></a>

### IX-SPACES-128 — Assumption evidence checks, retention and remediation review

Controls: Check against sources; Support result; Review future dependencies; Review existing effects; Open remediation proposal; Archive review; Erase scoped entry

Surface scope: Shared pattern or explicit target addition; no existing screen is fabricated.

Primary: Check against sources invokes the exact assumption/source check contract. Review future dependencies opens plans/context needing revalidation; Review existing effects opens actual receipts and outputs produced under the earlier revision. Open remediation proposal shows specific proposed corrections through each owner service. Archive review changes discoverability; any Erase command follows the scoped privacy/retention operation and states what permitted dependency metadata remains.

Secondary: Support result context offers Inspect evidence and Retry check; it does not confirm the user proposition. Existing-effect context opens actual owner/history and applicable repair commands, never an imaginary Undo all. Archive is not erasure or endorsement.

Drag: Effect/receipt links can be copied into explicit review drafts but cannot be dragged to claim rollback. Retention commands are not activated by dropping entries off the list.

Keyboard and touch: Keyboard/touch exposes current versus historical proposition and Received/Applied/remediation states. Safe navigation returns to the selected assumption without losing a correction draft.

State: Unsupported remediation is stated per effect. Corrections can revise authorised reversible drafts; sent/published effects remain real until a separate actual repair succeeds. Missing/revoked references retain uncertainty rather than a reconstructed fact.

Integration and persistence: Bank revisions remain linked through memory/compaction. Temporary retention and source privacy continue to apply; an entry’s user-confirmed status never broadens retention automatically.

Acceptance: Correct an audience assumption after one draft and one sent document exist; update eligible future work, retain the sent receipt and show actual remediation status without claiming it was undone.

Basis: ASSUMP-008/012/013 and KNOW-022; source checks [IX-SPACES-120](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.yd5yu6cfkh4) and canonical work/retention ownership.

<a id="h.975efm757oee"></a>

### IX-SPACES-129 — Mobile Space navigation and account access

Controls: Open Space navigation; Chat/Study title; Account and notifications; Mobile navigation sheet

Atlas surfaces: mobile-chat, mobile-study, mobile-permissions

Primary: Open Space navigation opens a responsive navigation sheet with current Space, recent work and available destinations. Selecting Chat/Study title opens its current Space context, not a duplicate conversation. Select a destination to navigate and close the sheet, preserving draft/run state. Account and notifications opens the compact shared account/notification entry surface; its exact items retain shared account and notification semantics.

Secondary: Navigation button has no custom context menu. A destination long press may expose the same Pin/Open commands as desktop, without navigating first. Account secondary click cannot switch identity or acknowledge all notifications.

Drag: Sheet scroll and edge dismissal must not reorder Spaces or move conversations. Only explicit enabled reorder handles use [IX-SPACES-011](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.r8vpdlkn3wev); a gesture starting in text input remains an editing gesture.

Keyboard and touch: Keyboard focus is contained in the opened sheet, Escape/Back closes it before navigation history, and focus returns to its trigger. Touch targets and safe areas follow [IX-BASE-010](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.b38hk5mijafm); a swipe never silently sends a draft.

State: Loading/denied destinations keep the outgoing draft. Account switching follows the shared save/ownership contract; a mobile panel cannot show another profile’s retained messages during transition.

Integration and persistence: Navigation sheet state is presentation only. Running work, pending review and capture remain visibly associated with their true identity and conversation.

Acceptance: Type a mobile draft, open navigation and account entries, cancel both and rotate/resume; preserve draft/caret and make no request or profile change.

Basis: Atlas mobile-chat/mobile-study/mobile-permissions; shared [IX-HOST-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.2xlp3zfzez5d)/013 and Spaces navigation [IX-SPACES-011](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.r8vpdlkn3wev)/012.

<a id="h.sgu1pwng0ung"></a>

### IX-SPACES-130 — Mobile composer, model entry and response options

Controls: Qwen · 70% effort (fixture model trigger); Attach context; Response options; Message draft; Text formatting; Insert emoji; Conversation permissions; Send message

Atlas surfaces: mobile-chat, mobile-permissions

Primary: The model/effort chip opens the shared role picker with actual current values. Attach context opens the typed attachment surface and returns to the same composer. Response options opens the selected message’s exact overflow operations; it cannot target whichever response later becomes last. Formatting and emoji act at the retained draft selection. Permissions opens the mobile scoped dialog. Send uses the same admission/acknowledgement as desktop.

Secondary: Long press on message text preserves text selection; an explicit Response options trigger exposes operations when selection conflicts with a gesture. Secondary click never retries or branches without choosing the command.

Drag: Text selection/attachment drag follows the composer contract. Vertical scroll must not change action-shelf selection or trigger Send. The on-screen keyboard does not create an implicit draggable bottom panel.

Keyboard and touch: IME completion, newline, accessible activation and explicit Send remain distinct. Keyboard opening keeps the caret and selected action visible; menus restore focus/selection on return. Native emoji picker inserts Unicode only after selection.

State: Safe-area/rotation changes preserve unsent draft, pending attachments and request state. Sending stays disabled while necessary attachments are unresolved and reports exact failure. A model trigger never starts Voice/Vision capture.

Integration and persistence: Mobile shares canonical conversation and picker policy with desktop. Closing permissions or an overlay does not clear text, stop a run or apply an unapplied context limit.

Acceptance: Open model, attachment and emoji overlays while composing with IME, then send twice; preserve caret and attachments and admit exactly one intended request.

Basis: Atlas mobile-chat/mobile-permissions; shared model picker and [IX-SPACES-018](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.bai4aclewxqc)–42. Example model name/effort is not production state.

<a id="h.4i9zxocgzkux"></a>

### IX-SPACES-131 — Mobile Study overview, subjects, progress and Bank

Controls: View all; Subject cards: Maths/Computer Science/Law fixtures; 3 topics; Recommendation card; Planner; Open Study chat; Overview; Subjects; Progress; Bank

Atlas surfaces: mobile-study

Primary: Overview opens the Study Home projection. Subjects/View all opens the subject catalogue; an individual subject card opens its real SubjectID. Progress opens personal evidence, with a count chip applying the filter represented by that count. Bank opens the current subject/topic catalogue. A recommendation opens its exact TopicID and reason. Planner opens the canonical Study burrow. Open Study chat opens/resumes the selected subject/topic conversation and preserves its mode/source policy.

Secondary: Subject/recommendation long press exposes the same explicit inspect/schedule/dismiss operations as desktop. Bottom destinations have no context menu or completion shortcut. A count chip never marks its topics reviewed.

Drag: Cards and bottom destinations are not drag-to-reorder learning records. Vertical swipes scroll content while the navigation/compose actions remain reachable; accidental lateral movement must not switch subject.

Keyboard and touch: Keyboard focus and touch labels distinguish bottom Subjects from individual subjects. Open Study chat has a clear accessible name and returns to the originating view. Safe areas and the keyboard preserve main navigation.

State: Empty/no evidence, missing subject and unavailable Bank/Planner state remain truthful. Example A-Level names/marks are fixtures; authorised real records determine titles, counts and recommendations.

Integration and persistence: All routes use the same IDs/evidence/resources as desktop. Mobile navigation cannot create a separate learner record, rescore a view or bypass source permissions.

Acceptance: Open three different subjects from mobile, return through Progress and Bank, then desktop; each retains its correct identity and identical private evidence with no view-counted success.

Basis: Atlas mobile-study and STUDY-034/035. Static fixture data-go targets do not permit all subjects to resolve to one sample subject.

<a id="h.x2wd4h548gr8"></a>

### IX-SPACES-132 — Invite an Assistant or Specialist to a canonical artifact

Controls: Invite to a document; Invite; [data-action="collab-open"]; Choose target artifact; Review invitation; Open collaborator work

Atlas surfaces: home-assistants, home-specialists

Primary: Invite to a document/Invite opens collaborator configuration for the exact AssistantID/SpecialistID and intended app/artifact. If no artifact is selected, choose an authorised canonical target before reviewing role, source/memory scope and allowed actions. The explicit invitation commit uses the owner collaboration service; opening the picker itself does not add a participant or start work. Open collaborator work routes to the actual task/proposal/artifact reference.

Secondary: Invite has no alternate secondary commit. Entity context may offer the same Invite action; it cannot imply attachment to every open document. Existing participant menu uses owner Inspect/Change role/Remove commands and preserves authorship.

Drag: Portraits and entity rows are not draggable into a document to invite or change scope. Editor/background drops are rejected rather than creating membership or starting work. Use the explicit Invite command; ordinary text selection and catalogue scrolling remain available.

Keyboard and touch: Focus enters the target/scope preview, Escape cancels without membership change, and touch exposes full scope before the explicit action. Double activation must not create two invitations.

State: Missing app/target permission, existing membership or revoked identity is stated before commit. Concurrent target changes revalidate the exact destination. Only a successful canonical receipt shows Invited/participating.

Integration and persistence: A source-bound Specialist keeps its evidence constraints; an Assistant does not automatically disclose private Main memory. Home cards are entry points, while owner app collaboration determines actual artifact access and proposal review.

Acceptance: Invite a bound Specialist to a document, cancel once and then commit twice during retry; create one correct membership with no broadened corpus or private-memory disclosure.

Basis: Atlas home-assistants/home-specialists collaborator buttons and ASST-007/010–012; canonical app collaborator controls own the invitation transaction. Atlas Write/Present/Build/Data destinations are illustrative selected targets.

<a id="h.oiggjnb69523"></a>

### IX-SPACES-133 — General conversation options and scoped command menu

Controls: Conversation options; [data-action="conversation-options"]; Rename; Pin/Unpin; Archive; Chat type; Storage location; Move to Group/Project; Conversation permissions

Atlas surfaces: spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-running, spaces-study, study-session, spaces-research, spaces-shopping, spaces-translate, spaces-experiences, spaces-generative-ui, spaces-subagents, spaces-subagent-detail, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, mobile-chat, spaces-permissions, spaces-specialists, spaces-assistants, spaces-manage, study-subject, study-topic

Primary: Conversation options opens a menu anchored to the current ConversationID/workspace. Rename edits its title; Pin/Unpin changes discovery; Archive changes list visibility. Chat type and Storage location open their distinct selectors. Move to Group/Project opens the organisation target chooser. Conversation permissions opens the current scoped permission draft. Where no conversation exists yet, only meaningful workspace/Space settings entries remain; unavailable conversation commands explain why.

Secondary: This visible menu is the command surface; secondary click on its trigger does not execute an item. A conversation row context exposes the same applicable identity commands. Rename is not a double-click side effect on unrelated header text.

Drag: The options trigger/menu is not draggable. Move is an explicit chooser or an eligible conversation-row drag transaction; dragging a menu item cannot move a thread or grant scope.

Keyboard and touch: Keyboard menu traversal and Escape use [IX-BASE-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.vtjy0q6e2vlk); closing restores the trigger. Touch exposes the same menu without hover. Destructive or storage-affecting commands remain explicitly named.

State: Each selected command revalidates its captured conversation/revision. A concurrent navigation cannot make an open menu rename/archive the new active thread. Failed changes retain the old confirmed state and entered draft.

Integration and persistence: Commands delegate to their canonical contracts rather than duplicating state. Opening options does not pause/send work, change Natural mode or apply model/context settings.

Acceptance: Open options for conversation A, switch views externally, then select Rename; target A explicitly or reject stale scope, never rename conversation B.

Basis: Atlas generic Conversation options triggers are menu stubs; SPC-001–005/017–026 and shared conversation requirements establish the adopted explicit menu. Assistant identity-row menus keep [IX-SPACES-053](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.22jvk5ne5e49) instead.
