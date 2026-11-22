# 5. Spaces

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.s5es88y3ngdy

<a id="h.tyu7mjdnw8yj"></a>

# Spaces

This chapter defines intended product behaviour. Atlas screens illustrate the interface contract; local prototype transitions do not certify production engines, provider connections, saved artifacts or completed runs. Every APP-identified requirement is defined here and retains its own acceptance obligations.

The [source Atlas](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html) is the editable v3.3 HTML at repository revision 1f92cd888f59025cc684f0720163efdbc580f8e3. Its late Haven shell, contextual chat, Assistant and Boards refinements override conflicting earlier renderers and screen notes. The associated 251-screen catalogue is mapped in the companion screen register. Requirements below use canonical names without fixing example identities, prices, timestamps, model choices, file counts or illustration content as production data.

<a id="h.1mb84adhanp2"></a>

## 5.1 Spaces ownership and artifact integration

Spaces is the owning application. Study, Translate and Actions are specialised Spaces within it. Each has a Space definition/configuration and conversations with its own task context; none requires a competing app package, permission broker or artifact store. Registered application commands open the original Decks, Forms, Write, Planner or other artifact when specialised editing is needed. Stable APP-prefixed requirement IDs below are reference keys and do not classify their subject as a standalone app.

APP-BASE-001 — Each owning application maintains the canonical artifact, stable document/object identities, revision and undo history. Home, Files, Boards, Flow, Spaces (including Study) and collaborators open or reference that artifact through its registered capability. Opening a reference does not create a private competing copy. Explicit Copy, Save a copy and branch operations declare the new identity and preserve the source.

APP-BASE-002 — Hosted applications use Haven’s outer tabs and shared window chrome. The active late Atlas shell places document identity in the existing tab and document actions in the host address row, with a Document tools disclosure when needed. Do not rebuild the removed third identity row or duplicate app file tabs from older notes. Standalone, mobile and authored preview surfaces retain their appropriate chrome. App-specific menus and contextual tools remain reachable.

APP-BASE-003 — Boards, Write, Present and Canvas have separate Start views with New, Open, Import, Templates and Recent. Start does not overlay a secretly mounted editor. Direct-open and session restoration bypass Start. Returning to Start retains the document session. Recent entries resolve actual identities and availability; missing, denied, unsupported and failed imports have distinct recovery states.

APP-BASE-004 — Native save, foreign-format import, export, sharing and external execution are separate operations. Each operation reports its actual target, source revision and outcome. Preserve editable native content when an export fails. Publish a capability matrix for implemented formats, including editable structures, unsupported features, fidelity losses and round-trip evidence. Do not infer support for every format from a generic Import or Export control.

APP-BASE-005 — Authored fonts, colours, shapes, charts, images, ink, media and geometry are document data. A shared theme or accent changes controls without recolouring or restructuring authored content. Selection handles, collaborator cursors and transient previews are excluded from ordinary document output. Shared AI access remains independent of human editing mode, layout locks, source ACLs and per-action approvals.

APP-BASE-006 — Toolbar, context menu, keyboard and accessibility actions invoke the same target-aware command. Each command resolves selection, current revision, capability, permission and undo boundary. Loading, empty, denied, offline, invalid, cancelled and failed states preserve recoverable input. Existing compatible capabilities remain available during interface refactoring; an absent mockup button is not a deletion instruction. Ordinary local authoring does not depend on sign-in, Files installation or a running model unless the selected operation requires it.

Acceptance — Create, edit, save and reopen a native artifact; open the same artifact from another app; switch theme and view; undo one logical change. Verify stable identities, preserved authored values and truthful availability. Cancel import/export and force a save failure without losing the working draft. Check one command through mouse, keyboard and context menu against the same target and revision.

Atlas anchors: boards-launch, write-launch, present-launch, canvas-launch, files, files-grid; active atlas-haven-shell-code and atlas-chat-position-code. Follow the explicit lifecycle and capability-preservation requirements in this specification.

<a id="h.8lj0rl79u5sx"></a>

## 5.2 Spaces, conversation and task runs

APP-SPACES-001 — A Space is a task environment containing conversation, relevant sources, tools, scope and outputs. Its mini-rail opens that environment directly and preserves the contextual sidebar. Preserve conversation identity, retained draft and selection, attachment references, scroll position and active run when navigating or reconnecting. Revalidate resources against the destination scope rather than silently broadening access. Streaming must respect a deliberate scroll-up and offer return to latest.

APP-SPACES-002 — The composer expands upward when focused/nonempty. Formatting and device-rendered Unicode emoji appear only while focused and expanded. Emoji insertion edits the retained selection and never sends. Attachments distinguish Files, Skills, Plugins & Apps, Specialists and Assistants; they show readiness, unavailable state and remove actions. Browsing, selecting a resource, mentioning a capability or rotating the icon-only action shelf does not execute work. Inline command treatment applies only to the bounded mention and its instruction, leaving surrounding prose as ordinary text. The shared composer Options disclosure groups response presentation and eligible output preferences, including in Study. Study activity and source-policy choices remain distinct groups with their own meanings even when presented in the same panel. Remove redundant always-visible response-option rows or permanent response-settings rails from the ordinary conversation layout; keep their controls discoverable in Options without removing their capability or changing its meaning.

APP-SPACES-003 — The separate conversation-permissions control opens effective tool/source scope. It is distinct from an editor’s AI access selector. Conversation settings can narrow inherited grants and cannot bypass blocked account grants, source ACLs, device policy or mandatory action review. Cancel changes nothing; Save applies to future requests. A lasting trust grant is a separate configuration operation. Dismissal restores the draft, context, selection and initiating focus.

APP-SPACES-004 — Keep draft text and attachments until admission succeeds. Rejected validation, unavailable models and a full queue preserve the input. Assign one identity to the admitted request and its run. Idle exposes supported Send and Voice actions; an empty disabled Send does not disable Voice. Running exposes only supported Steer, Queue, Stop and Voice controls. Steer affects future work; Queue is a separate pending message. Preserve the configured per-Space default and documented alternate shortcut without rewriting earlier submitted input.

APP-SPACES-005 — Active, queued, awaiting input, awaiting approval, paused, cancelled, completed and failed are distinct run states. Show actual elapsed activity and concise task/tool events; private internal reasoning is not a UI requirement. Stop acts at the real runtime boundary and preserves committed work. Reopening a view attaches to the existing run without replaying admission or external effects. A completed research step or prepared diff is not a saved final output.

APP-SPACES-006 — Message actions target the selected message and its canonical history. Copy, links, retry, editing and permitted deletion preserve provenance. Historical user-message edits use the three resolved operations in SPC-032 and DEC-006. Retry/regeneration preserves prior answer versions and action receipts; it cannot silently erase downstream history or replay external effects. Context compaction creates a derived summary while retaining complete original history and source eligibility.

APP-SPACES-007 — Ordinary Chat exposes a Temporary lifecycle toggle; Temporary is not a separate destination or response-presentation mode. Conversation participation, temporary lifecycle, storage location (Local, Mesh or Cloud) and model execution location remain separately represented. Local history does not imply local inference. Explicit saving/promotion from Temporary to regular or Multi-Agent Chat, and regular to Multi-Agent, preserve identity/history and never invent earlier replies. Temporary becomes durable locally before a separate Mesh/Cloud promotion. Its associated pre-start Personalised/Unpersonalised setting follows DEC-004 and SPC-084; neither choice writes durable memory while temporary.

APP-SPACES-008 — Groups and Projects organise canonical conversations; they are not additional chat types. Groups add scoped context/instructions; Projects additionally expose sources, rules and resource connections. Group to Project preserves members and identity; Project to Group is rejected where it would discard structure. Removing an organisation container does not delete its chats. Space Override, then User Preference, then Default determines configurable values within policy; locked fields show the governing source.

APP-SPACES-009 — Basic, Advanced and Expert Space configuration are projections of one SpaceID and Draft/Active revision. Basic exposes identity, instructions and resources. Advanced exposes capability policy, memory, supported model parameters, canonical collaborators/automations and presentation overrides. Expert uses typed graphs for Harness Logic, Model Logic and User Message Logic. Transformations retain the original user message. Switching views preserves Expert-only nodes. Preview, Validate and Activate are separate; invalid dependencies, cycles, budgets and mandatory-but-blocked capabilities leave the valid active revision intact.

Acceptance — Attach a file and type a draft; navigate and return; reject admission; admit once; scroll up during streaming; steer, queue, stop and reconnect. Verify the input, run lineage, actual committed work and unchanged grants. Exercise supported chat promotions, container removal and configuration-view switching. No generic UI event may create a duplicate request or widen scope.

Atlas anchors: spaces-chat, spaces-compose, spaces-emoji, spaces-attachments, spaces-permissions, spaces-thinking, spaces-running, spaces-manage, mobile-chat, mobile-permissions. Apply the admission, conversation-type and configuration requirements in this chapter.

<a id="h.pe0wy26a7ldy"></a>

### 5.2.1 Conversation identity, navigation and retained work

<a id="h.3khhjqvd848f"></a>

#### 5.2.1.1 Logical records and ownership

SPC-001 — A Space configuration contains a stable SpaceID, kind, owner, active configuration revision, draft revision, navigation preferences, resource references, effective capability policy and declared domain-state bindings. A Conversation contains ConversationID, owning SpaceID, chat type, placement, branch identities, participant references, messages, request/run references and lifecycle state. A Message contains MessageID, author principal, role, immutable submitted content revision, timestamp, parent/request/branch relationship and attachments or artifact references. Display names and positions are replaceable presentation; they never serve as identity keys.

SPC-002 — Separate the draft, submitted message, request, attempt, run and result. A Draft has text/structured content, selection, attachment readiness, selected responder, reply/branch target and draft revision. An admitted Request references one submitted snapshot and its effective configuration. An Attempt records a particular execution of that request or explicit retry. A Run carries observable lifecycle and action receipts. A Result references response messages, proposals, generated surfaces and canonical artifacts. These distinctions make admission failure, retries, alternate answers and recovery inspectable without erasing the user's original request.

SPC-003 — Navigation state is local presentation attached to a stable view identity: selected Space/conversation/branch, reading anchor, sidebar expansion, open artifact tabs, active domain view and draft focus. Persist the supported recovery subset without treating a view snapshot as canonical conversation history. A data refresh can reconcile content but must not replace an in-progress draft, jump a deliberately scrolled conversation to the bottom, or change the selected branch because its array position moved.

<a id="h.3nh5dco5sqil"></a>

#### 5.2.1.2 Enter, create, resume and switch

SPC-004 — Selecting a Space in the rail or configured dropdown opens its actual environment. Restore its last eligible conversation/workspace when available; otherwise open its designed start state. New Chat creates a conversation within the current Space, not a new Space configuration. Selecting Manage Spaces deliberately opens configuration. A newly created conversation receives an identity before related drafts/runs are persisted; a failed create retains the unsent draft and reports retry against the same creation operation.

SPC-005 — Switching Space performs one coherent context change. Save the outgoing view/draft checkpoint, resolve the destination's conversation, apply its effective policy and restore its view. Re-resolve attached sources and collaborator references for that destination. An unavailable attachment remains an identifiable unavailable reference where the user may see it; it is not silently replaced with another account's file. A current Voice & Vision session remains visible and associated with its real conversation; switching destinations must not quietly transfer capture to a different participant or begin a second capture session.

SPC-006 — Conversation lists expose title, relevant state and latest activity from actual records. Renaming changes presentation while keeping links, branches and task associations. Pinning and ordering change the list projection, not chronological message history. Archive removes a conversation from the ordinary active list while keeping it reachable through archive/search where permitted. Unarchive restores navigation eligibility. Removal, archive and privacy erasure are separate actions; a generic close or remove-from-list command must not erase the underlying conversation.

SPC-007 — Optional Spaces Tab View may expose real conversations, browser pages, app-owned artifacts and safe generated surfaces through existing host integrations. Each tab carries an explicit target kind and canonical identity; hiding the strip preserves its tabs. Close uses the target's dirty-state/recovery contract. An app viewer opened inside a Space and Open in App address the same artifact and revision, not two mutable copies. The shared Haven composition takes precedence over older proposals for duplicate app-title bars or nested global tab strips.

SPC-008 — Sidebar customisation operates on stable section/item IDs. A contributed item records its package/origin, target kind, canonical target, permitted Space scope, configuration revision and effective availability. Supported navigation targets and explicit actions remain distinguishable: opening a file/browser/app view is not the same as launching an Automation. Reordering and hiding preserve target identity. A removed contribution leaves a safe unavailable entry or a recoverable configuration reference, never a misleading working button. Current protected permission, recovery and navigation controls remain reachable through keyboard and narrow layouts.

SPC-009 — On reconnect, retrieve durable messages and run events after the last acknowledged sequence, reconcile duplicate deliveries by identity, then restore the reading anchor. A result arriving twice does not create two messages; an acknowledgment lost after request admission does not resubmit the user's command. If a server/local state disagreement cannot be reconciled, preserve both recoverable revisions and show the affected item as requiring reconciliation. Do not rebuild a conversation by replaying every historical tool call.

<a id="h.glhbnnj6bcwg"></a>

### 5.2.2 Composer, sources and explicit commands

<a id="h.29mqbtzagbbd"></a>

#### 5.2.2.1 Draft editing and attachment readiness

SPC-010 — The shared composer expands upward for focused or nonempty content, keeping the message list readable and its primary controls reachable. Formatting and device-rendered emoji appear in the expanded focused state. Emoji insertion replaces the retained selection; it never activates Send. Rich text, inline references and supported Canvas composition retain structured content plus an accessible text representation. Enter follows the configured send/newline preference; Shift+Enter and IME composition follow the documented editing behaviour. A composition-confirming Enter must not also admit a request. Options opens one labelled floating form panel anchored to its composer trigger, without reserving permanent sidebar width or reflowing the conversation into a narrower settings layout. Preserve the message/answer draft, selection, attachments and reading position. Compact grouped controls retain their existing immediate-change or Save/Apply boundaries: selected response preferences keep their next-message semantics, while unsaved permission changes are discarded on Cancel, outside dismissal or Escape and require explicit Save to take effect. Opening or dismissing the panel never submits the draft or grants access. Keyboard opening gives access to its form controls; Escape dismisses the topmost nested popup first, then the panel. Close, Cancel and dismissal return focus to Options unless an explicit navigation action opens another destination. Reposition or constrain the panel below the host chrome and within the available viewport; at narrow widths use a contained scrollable floating sheet with its heading and Close control reachable, respecting safe areas and the on-screen keyboard. The separate conversation-permissions operation and all permission/review boundaries remain intact.

SPC-011 — An attachment reference records canonical owner/type/identity, selected revision or live-reference policy, display metadata, disclosure scope and readiness. Distinguish selected, resolving, ready, unavailable, denied, failed and removed. Files, Skills, Plugins & Apps, Specialists and Assistants remain different resource kinds. A file can be readable locally yet ineligible for the chosen cloud model. Present that mismatch before dispatch and retain the attachment for a permitted route or removal; attaching is not consent to disclose it everywhere.

SPC-012 — Source context is assembled at admission from authorised references, selected ranges and the effective policy. Record which revision/selection was supplied and any extraction or size limitation. A folder reference uses bounded, lazy discovery rather than copying its whole contents into a prompt. A source changing between selection and admission is revalidated; a request needing a fixed revision either uses that authorised revision or explains the conflict. Do not present merely attached material as used evidence in the completed answer unless the runtime actually used it.

SPC-013 — Attachment removal edits the draft's references and derived preview. It does not delete the underlying file, uninstall the Skill, revoke an unrelated durable grant or erase prior submitted messages. If extraction is still running, cancel that draft-owned preparation where supported and ignore stale completions against the removed attachment revision. Failed or cancelled upload/extraction retains enough draft metadata to retry deliberately without duplicating successful source enrollment.

<a id="h.8811su5hgv6g"></a>

#### 5.2.2.2 Discovery, mentions and slash commands

SPC-014 — The contextual shelf and resource picker discover choices without executing them. Selection may attach a resource or set an explicit pending command; only its documented activation admits work. A mention resolves a canonical identity and bounded instruction/range. The rest of the message remains ordinary authored text. Pasted source text, quoted instructions, code fences and a URL containing a slash cannot turn into an executable command simply because they resemble a command label.

SPC-015 — A leading slash in an eligible composer opens a searchable command picker using registered command identities, descriptions, typed arguments and real availability. Provide these documented command aliases: /NewChat, /Branch, /Model, /Reasoning, /Pin, /Bug, /ResolveIssues, /Archive, /Stop, /ResetContext, /Restart, /BackgroundChat, /Memory, /Permission, /Attach, /Copy, /CopyLatest, /Read, /ReadLatest, /Move, /Dictate, /Summary, /Tabs and /Voice. Availability is contextual; an unsupported action is explained, not silently simulated or omitted as though it never existed. Escape restores literal draft text and selection.

SPC-016 — Commands invoke the same typed operation as their ordinary UI equivalent. /ResetContext rebuilds the derived execution context and does not delete original messages or memory. /Restart creates an explicit attempt and reconciles earlier effects; it is different from Resume. /BackgroundChat resolves a chosen existing or newly created compatible target, preserves the current draft and sends only the authorised content to that target. /Bug prepares a bounded report for review; it does not send a private conversation merely because the command was discovered. /Reasoning uses the current shared picker and requested/effective adapter mapping rather than restoring a conflicting legacy scale.

SPC-017 — Command arguments and completion state are saved as draft state until admission. Changing target, model or source policy revalidates the pending command. A command-specific form returns structured input to the composer; accepting its field values is not approval of unrelated downstream effects. Keyboard completion, selection and execution have separate actions and accessible announcements. A literal slash can be entered without opening an unwanted operation.

<a id="h.7bzyrm931wj8"></a>

#### 5.2.2.3 Voice and multimodal input

SPC-018 — Spaces uses the shared Voice & Vision session. The floating pill exposes supported mute, captions, pause/resume, end, voice/mode, screen sharing and camera actions with actual capture indicators. Starting dictation edits the same draft; starting a conversation voice session follows admission and source policy. Enabling voice does not enable camera or screen capture. Ending a source stops its frames/audio reaching the runtime, and the UI reflects the acknowledged stop rather than only hiding an icon.

SPC-019 — Device denial, lost audio input, unavailable voice model and reconnect have distinct recovery states. Preserve recognised unsent text and attachment context. Partial transcription is visibly provisional until committed; a user correction is retained as authored input. Switching mode or opening the full Voice & Vision surface retains session identity. Keyboard actions remain available when capture fails, and a disabled empty Send does not disable an otherwise supported Voice action.

<a id="h.vg5wuem30o0"></a>

### 5.2.3 Request admission, running work and visible evidence

<a id="h.yf2mwoccecp3"></a>

#### 5.2.3.1 Submission and state transitions

SPC-020 — Submission snapshots the draft, validates required fields, attachment readiness, route capability, source disclosure, queue capacity and effective grants, then attempts durable admission with an operation identity. Keep the original draft until that admission is acknowledged. A double click or network retry reconciles the same operation. After acknowledgment, append one submitted message and clear only the admitted draft revision; text the user typed after submission remains a new draft.

SPC-021 — Rejected admission reports the actual reason and a useful next action: resolve an attachment, change route, narrow scope, wait for capacity or correct input. Do not create an apparently running response, display a fabricated output or count rejected work as completed. If admission outcome is unknown, show Checking submission and reconcile its operation identity before allowing a retry that could duplicate the work. Cancellation of an unadmitted draft affects only its preparation; cancelling admitted work follows the real run boundary.

SPC-022 — Idle exposes supported Send and Voice actions. Running exposes supported Steer, Queue, Stop and Voice actions. Preserve each Space's configured Steer/Queue default; the documented Alt+Enter alternate selects the opposite operation when both are supported, with the active choice visible. Steer records a new prospective instruction against the active run. Queue snapshots a separate pending message. Neither edits already-submitted text or approves a held tool action. If a run finishes during activation, reconcile the requested operation explicitly instead of silently sending it into an unrelated new run.

SPC-023 — A queued message has its own identity, position, readiness and cancellation state. Editing a queued item updates its pending revision before execution under a conflict check; once admitted to execution, subsequent edits use a new instruction or attempt. Removing a queued item cancels that item and retains any recoverable source draft. Reordering is permitted only where dependencies and the runtime queue support it; expose a fixed dependency instead of pretending every queue is freely sortable.

SPC-024 — Keep queued, starting, working, waiting for input, awaiting approval, pausing, paused, cancelling, cancelled, completed, blocked and failed states distinct where applicable. Stop requests termination at a supported boundary and shows Cancelling until acknowledgment. Resume continues only an eligible paused execution with revalidated capabilities. End Pass closes the current resumable pass while preserving its messages and outputs. A cancelled model stream may leave completed tool effects; the result must identify those effects rather than promise reversal.

<a id="h.ku9yhjm95k5"></a>

#### 5.2.3.2 Summary, progress and throughput

SPC-025 — Summary is a dismissible top-right presentation of the same Activity, Outputs and Sources records, not an independent progress database. Showing or hiding it preserves conversation width, reading position, drafts and active approvals. Activity includes observed work events, tool outcomes, blockers and checkpoints. It does not claim access to private internal reasoning or manufacture steps from a plausible narrative. An event's elapsed interval distinguishes active execution from waiting when that distinction is available.

SPC-026 — Progress uses actual measurable units: completed plan nodes, processed files, verified segments or acknowledged outputs. When no reliable denominator exists, use descriptive state rather than a percentage. Optional per-response generation speed uses measured output-token count and a defined generation duration; unavailable provider counts remain unavailable. Queue wait, tool wait, reasoning effort, context usage and token generation speed are separate quantities. Preserve calculation provenance so changing a display preference cannot change the underlying measurement.

SPC-027 — Every result states its actual completion boundary. A drafted plan, generated proposal, native saved artifact, completed export and externally published item are different outcomes. A response may be complete while one requested export failed; identify the partial outcome and let the user open the valid native result. A run completed without independently passing its applicable acceptance criteria must not receive a Verified status.

SPC-028 — On a policy change or revoked source, stop future protected reads/actions and update the effective-scope display. Preserve permitted audit metadata and valid outputs without exposing newly inaccessible content. A reconnect must not restore a cached permissive toggle over the current broker decision. If a historical excerpt can no longer be shown, show a safe unavailable-reference state rather than retrieving it through a different account automatically.

SPC-029 — Acceptance: submit with a resolving attachment, reject admission, submit twice across a lost acknowledgment, then type a fresh draft before acknowledgment returns. Exactly one original request is admitted; the fresh draft remains. Queue and steer separately, scroll up during streaming, stop during a tool boundary and reconnect. The UI must preserve reading position, submitted provenance and completed effects while showing the real state of remaining work.

<a id="h.cp0f1025qv2q"></a>

### 5.2.4 Conversation types, placement and organisation

SPC-040 — Chat exposes a Temporary toggle in its existing header, without a separate navigation destination or response-mode entry. Normal/Multi-Agent participation, the temporary lifecycle flag, Local/Mesh/Cloud placement and model execution locality are distinct dimensions. Persist them separately and explain their actual effect. Local history can still be disclosed to a cloud model only under the applicable execution/source policy; a Local label must not imply that inference is local. Mesh and Cloud use canonical placement, entitlement, replication and conflict services rather than a Space-owned backup implementation.

SPC-041 — Temporary is local-only until explicitly saved. Its conversation, attachment references and derived session state are excluded from remote replication, account backup and automatic durable memory writes. Supported local recovery follows the declared temporary lifetime without implying permanence. Before the first message, Personalised enables eligible existing memories, custom instructions and plugins under current grants; Unpersonalised excludes all three. Current explicit user input and authorised attached sources remain usable under normal source/execution rules and do not silently enable personalisation. After the first message, the personalisation choice is fixed for that conversation. Normal account/workspace constraints, source-binding restrictions and permission checks still apply.

SPC-042 — Save Temporary first establishes a durable local conversation through an explicit conversion. Saving Temporary Chat as regular locally durable history, supported saving/promotion to Multi-Agent, and regular-to-Multi-Agent promotion preserve existing identity/history where compatible and never invent past participant replies. After saving, normal account/workspace personalisation and separately opted-in memory/learning policies apply to eligible saved content; conversion does not itself grant plugins, opt into training or start work. Promotion to Mesh or Cloud is a separate explicit operation. A regular chat with committed history cannot be retroactively made Temporary by changing the toggle; explain the boundary and offer a separate new Chat. Before the first committed message, toggling Temporary on or off only changes the current unsent Chat lifecycle flag under SPC-084. Direct transfer of an unsaved Temporary conversation returns must-save-first consistently from UI, API and Automation paths. Show pending, successful and failed saving truthfully; failure retains temporary state and cannot announce that the conversation is saved.

SPC-043 — A Multi-Agent conversation has explicit participant identities, roles and current effective scope. Adding an Agent affects future participation, not past authorship. Participants receive only the context eligible for that participant and run. Display name, Team membership or a shared conversation view cannot expose another Assistant's private memory. Generic delegated execution and multiple answer variants remain distinguishable from permanent conversation participants; enabling one does not silently convert the others.

SPC-044 — Group and Project are organisation containers over existing ConversationIDs. A Group adds scoped shared instructions/context; a Project adds authorised sources, rules and local/server/remote/cloud resource connections. Membership change references the same conversation and revalidates inherited context. The user can inspect why a rule applies. Moving a chat does not automatically republish its history, copy private sources or enable every connected Project resource for its prior runs.

SPC-045 — Group→Project is a compatible one-way promotion preserving identity, members and existing history. Project→Group is rejected if it would discard Project structure; a separately named copy/export operation can be offered when supported. Removing a container removes organisation and its scoped references, not member conversations or source documents. Preview any effect on future inherited configuration and preserve a route to formerly nested chats.

SPC-046 — Compaction creates a versioned derived context record with included message/source revisions, excluded or summarised ranges, active obligations and current scope. Original messages, results and receipts remain accessible through normal history. Context reconstruction respects current source eligibility and never treats a summary as a permission grant. Reset working context can discard or rebuild derived context without deleting canonical records. If context cannot fit, retain the input and explain the unresolved capacity rather than dropping obligations silently.

SPC-047 — Acceptance: create regular Chat, enable Temporary within a new Chat, and create Multi-Agent Chat. Use the allowed save/promotion paths, reject direct Temporary replication and retroactive conversion of committed regular history, then rename/move a conversation between containers. Confirm stable identity, no fabricated replies, proper context inheritance and preserved drafts. Compact a long thread and reset working context; the original message and artifact history must still open without repeating execution.

<a id="h.zd4up69vszcr"></a>

### 5.2.5 Space Builder: one configuration, three editor views

<a id="h.x66im95ogh4j"></a>

#### 5.2.5.1 Basic and Advanced

SPC-050 — Create/Edit Space opens one revisioned configuration through Basic, Advanced and Expert peer views. Switching view is not a conversion or activation. Basic and Advanced use the current CUI's cohesive configuration-document treatment; Expert uses the shared graph workspace. Existing Expert-only nodes remain stored and active when the user edits a Basic field. Show Expert configurations active with a link to relevant logic where the simpler view cannot express it; do not lock the simpler view.

SPC-051 — Basic exposes name, description, icon, custom instructions, availability gates for supported Skills/Plugins/Apps/Models/Memory and resource attachment. An enabled category with no eligible provider or tool displays that fact. A toggle controls intended availability within the effective policy and cannot grant credentials or object access. Binding an installed Skill or Plugin stores its canonical identity/version; installing a new package uses the owning registry's validation and trust workflow.

SPC-052 — Advanced adds per-capability Inherited, Allowed and Blocked choices and required/Mandatory resources where supported. Display both requested and effective state plus its policy source. Whole-category Off intersects item choices; it does not erase them. Mandatory-but-blocked is a validation error because execution cannot satisfy both rules. A higher policy restriction remains effective regardless of local control order. Changing a policy updates the draft and its validation outcome, not already-running work without the defined activation/revalidation path.

SPC-053 — Advanced exposes memory enablement/scope, eligible source retrieval, supported model/response parameters, canonical Assistant/Specialist defaults, Automation references, suggested prompts and reversible theme/sidebar/message-control overrides. Route, reasoning and context controls project the shared runtime settings, including pending/apply boundaries. Unsupported provider parameters display unavailable and are not sent as guessed fields. An Automation reference identifies its trigger/action, scope and canonical owner; creating or enabling a schedule is distinct from attaching its definition.

<a id="h.97a8nf7ao8i5"></a>

#### 5.2.5.2 Expert graphs and managed resources

SPC-054 — Expert's primary pages are Details, Harness Logic, Model Logic, User Message Logic and Files. Preserve selected page, graph selection, pan/zoom, unsaved draft and resource context while switching pages. Reuse the shared graph editor/validator used by Automations: stable GraphID/NodeID/PortID, typed connections, registered node schemas, selection, grouping, search, undo/redo and accessible keyboard/touch operations. Trigger, Condition, Event, Logic, App and Files categories remain recognisable through labels/icons and current theme tokens; historical sample colours do not override the active palette or create executable nodes.

SPC-055 — Harness Logic configures declared lifecycle/request events, bounded state, conditional routing, permitted tools, retrieval, delegation, supported generated UI and sidebar/composer templates. Its graphs cannot remove protected identity, source disclosure, permission, send/cancel or recovery semantics. Model Logic assembles permitted instructions, context and eligible route/parameter choices; an explicit forced model narrows routing and fails honestly if ineligible. The graph never substitutes an unrelated route to hide inability to satisfy the user's selection.

SPC-056 — User Message Logic operates on derived values. Preserve immutable original MessageID/content and record transformation identity, version, input revision, output and warnings. Truncation states what range was omitted; numeric extraction records locale/format interpretation and ambiguous tokens; word matching states case, boundary and normalisation rules. A preview of sample input exercises the same typed transformation used at execution. Transformation errors retain original input and last valid draft; they do not mutate earlier history or leak filtered source text to an external tool.

SPC-057 — Files is a nested resource organiser containing references to authorised files, Skills, Plugins, MCP configurations, connectors, configured models/model artifacts, mini apps and CUI definitions/assets where supported. Each entry exposes type, owner, version, dependencies, compatibility and access state. Rename/move changes the organisation reference unless the user explicitly invokes the owner's file mutation. Folder membership does not install, activate, trust or execute a resource. Remove detaches the Space reference; global uninstall/source deletion are separate named operations.

<a id="h.bj8ciw3lh252"></a>

#### 5.2.5.3 Validation, preview, activation and rollback

SPC-058 — Draft edits carry expected revision and produce structured validation results tied to that revision. Validate schema/ports, unresolved nodes, source eligibility, conflicting policies, model capability, bounded cycles, dependency availability, CUI safety/accessibility and orphaned resources. A loop needs a declared bound or cancellation-aware runtime contract; drawing a cycle is not sufficient execution authority. A Basic control colliding with Expert logic shows both affected meanings and a reviewable patch rather than deleting the graph silently.

SPC-059 — Preview creates an isolated test conversation with the draft configuration. Mark mocked dependencies visibly. Live model/tool calls require their genuine capability and current scope, and consequential effects retain the owning action's review rules. A preview does not become a normal conversation or activate schedules unless explicitly promoted through the corresponding operation. Preview evidence reports the draft revision and which dependencies were real, mocked, unavailable or untested.

SPC-060 — Activate validates the exact draft revision and atomically records a new active revision. A stale expected revision or invalid dependency leaves the previous active version intact. New requests use the new active configuration; existing runs retain their admitted configuration plus current enforced revocations, unless a supported explicit update changes their future behaviour. Rollback selects a known configuration revision for future work; it does not reverse already-sent messages, files or external effects. Templates and assisted configuration produce inspectable draft patches and never silently activate themselves.

SPC-061 — Acceptance: create a blank Space, set Basic resources, add Expert-only logic, change an Advanced policy and switch all three views repeatedly. Verify one SpaceID, intact nodes and correct effective values. Reject a mandatory/blocked conflict and an unbounded cycle; preview with one mock and one real eligible dependency; activate a validated revision; reject concurrent stale activation; roll back future configuration without losing run history. A screen showing connected nodes is not proof that the configured workflow executed.

<a id="h.1fe3hb6cpamk"></a>

### 5.2.6 Conversation permissions and scoped review

SPC-070 — The composer permission control projects effective conversation scope. For each supported capability/source class it shows requested override, effective state, governing source and an actionable reason when blocked. It is separate from editor Read-only/Take actions, human Edit/View, provider OAuth scope and durable trust. Scope cannot exceed account/device/organisation policy, source ACLs or the caller's actual grant. Cancel preserves the prior override; Save commits the versioned override for future eligible use.

SPC-071 — An approval record identifies caller, action, target IDs/revisions, affected range/count, relevant disclosed data, consequence, validity and decision state. Review opens the proposed operation without executing it. Approval permits only the reviewed operation or explicitly bounded plan scope. At execution, recheck target revision and current grants. A changed target/effect becomes stale and returns for corrected review; an old accepted button state cannot authorise it. Data retains its explicit per-AI-mutation approval requirement.

SPC-072 — Pending approval remains discoverable when Summary, Workspace or a sidebar is closed, and across mobile sheets or tab changes. Decline cancels only the pending operation as defined by its plan; independent safe work can continue. Stop cancels the run through its normal boundary. Marking its notification read changes only read state. A failed approval submission retains the proposed action and reports whether the decision was received before retrying.

SPC-073 — Plugin permission options and timeout controls bind to real supported semantics. A setting such as Ask once per App names the verified app/account/session, action classes and resource bounds it covers. It is not permission over unrelated applications or future changed scopes. None for a user-imposed timeout does not disable mandatory watchdogs or cancellation. Unsupported choices are unavailable, and effective configuration is inspectable after reconnect.

SPC-074 — Optional Peer Review uses the same task/run and review services. Single-pass review examines a bounded output revision; ongoing review operates at configured meaningful checkpoints. Reviewer findings retain objective, evidence, affected revision, reviewer identity and disposition. A claimed pass does not become Verified without applicable evidence. Reviewer powers are explicitly delegated; restarting work, replacing a worker, changing its model and resetting its context are different effects. Review exhaustion or reviewer unavailability holds only a required gated operation, preserving other permitted work.

SPC-075 — Review budgets distinguish productive worker active time W from waiting and aggregate reviewer active time R. For configured percentage p, enforce R ≤ (p/100) × W over the declared review scope. The retained initial setting is 25%, so four hours of productive worker activity permits at most one aggregate reviewer hour; it is not 25% of combined work plus review. Aggregate parallel reviewers' actual active consumption without duplicating the same invocation or counting simultaneous worker clocks twice. Expose Models, Add Model and Max Review Time through real configuration. Exhaustion never bypasses mandatory human review or labels unreviewed results complete. Record reservations, actual usage and unknown telemetry separately; no illustrative model is required to exist.

SPC-076 — Optional Multiple Responses creates distinct attempts/messages for configured responders, including repeated instances of one model. Keep responder identity, actual route, usage and partial failure visible. Choosing one answer affects subsequent context but preserves alternatives. Repeated instances are not asserted to be statistically independent. Coordinate side effects explicitly so three responders cannot perform the same purchase/publication three times merely because three textual answers were requested.

SPC-077 — Acceptance: open/cancel/save permission scope, revoke a source during work, retry a stale approval and dismiss its notification. The action must remain correctly held or denied, with the draft/results preserved. Exercise an unavailable required reviewer and mixed responder failures; retain successful answer variants without duplicate effects or a fabricated verification badge.

<a id="h.u1ofcq2goeyk"></a>

#### 5.2.6.1 Action Log as an inspectable product feature

SPC-078 — Preserve the Action Log as a usable product surface for the selected conversation/task, reachable from activity/Summary and the task workspace without entering a developer console. It shows actual actions and their outcomes, including proposals, admitted/running operations, held approvals, success, failure, cancellation and uncertain effects. This is a required user workflow in addition to retaining records through compaction. A concise progress summary or hidden event database cannot replace the inspectable log. Natural Conversation modes change prominence, never availability.

SPC-079 — Provide search/filtering over authorized records by task/run or attempt, actor/worker, action/tool/app, outcome and relevant time range. Grouping, collapsing and pagination retain stable event identity and causal order. An action with child events can expand into them without becoming multiple independently executed actions. Filters neither stop hidden work nor hide globally actionable approvals. Preserve the selected event and reading anchor as new events arrive; inaccessible records cannot leak through counts, snippets or target names.

SPC-080 — Event details expose the actual actor, action, target/range, time, input/output references, expected/resulting revision, originating request/plan step and operation receipt where applicable. Separate submitted intent, accepted execution and confirmed effect. Link supporting artifacts, tool results, verification evidence and source provenance; redact unavailable/private material through current policy. Public execution evidence and permitted model trace are labelled distinctly. The log must not substitute model-written claims for receipts or present private chain-of-thought as an execution record.

SPC-081 — Detail actions use the existing canonical operations: open the target/output, inspect a diff or receipt, review a pending approval and retry an eligible failed operation. Inspecting or selecting an event never re-executes it. Retry creates an attributable attempt and rechecks scope, target revision and completed effects; an uncertain external outcome first enters reconciliation. Review acts on the exact pending operation, while a historical approved record is inspectable rather than a reusable authorization button. A stopped or unavailable owner exposes its real recovery limitation.

SPC-082 — Action Log remains available after completion, failed export, cancellation, reconnect and supported compaction, subject to retention and permissions. Corrections append or supersede attributed records instead of rewriting an earlier failed event as successful. Opening the log from a linked Assistant, app or Actions tile resolves the same task/run and source events. Existing compatible log/detail/recovery capabilities must be retained when adopting the Atlas composition; theme or shell migration does not justify reducing them to a static narrative.

SPC-083 — Acceptance: perform one successful read, one approved mutation, one denied operation and one failed export; filter and inspect their real records and receipts. Retry only the export, reconnect, compact context and return from another authorized entry point. Verify stable event identity, preserved failures, one mutation effect, accessible keyboard detail/review and no fabricated progress. Repeat in Chat and Worker presentation to prove that log access is not mode-gated.

<a id="h.teatcthcndkw"></a>

### 5.2.7 Natural Conversations: automatic response presentation

<a id="h.fii2p226y2h8"></a>

#### 5.2.7.1 Scope and automatic selection

NCONV-001 — Natural Conversations automatically selects an appropriate response/presentation mode from Chat, Agentic Chat, Worker and Design, and may change that selection as the conversation develops. This is the owner's new product policy of 21 November 2026; it is not a claim that the current Atlas or repository already implements the selector. The change is ordinarily silent: the user should receive a suitable response, not a recurring mode announcement, modal choice or explanation of the assistant's internal personality instructions.

NCONV-002 — These modes configure tone, verbosity, progress presentation and the prominence of eligible live results. They never add/remove tools, change permissions, change source/disclosure scope, authorize actions or restrict capabilities to a particular mode. A preview, document, detailed activity log, reasoning summary or other supported result can be used in any mode when relevant. The selected model/route, context policy, placement, configured budget and action-approval contract remain governed by their existing controls. The automatic selector is a presentation policy, not a second model router or permission broker.

NCONV-003 — Natural Conversation response presentation is independent of Normal/Multi-Agent participation and the Chat Temporary lifecycle toggle; Local/Mesh/Cloud placement; a built-in/custom Space; Study's Learn/Practise/Test/Review activity; and Basic/Advanced/Expert configuration views. For example, a temporary Study conversation can use friendly Chat presentation for an explanation, Agentic Chat while preparing a lesson, and Design while showing an actual diagram preview. Those transitions cannot save the temporary chat, change its study mode or add a cloud grant.

<a id="h.a1lzuzcmaddo"></a>

#### 5.2.7.2 Four presentation behaviours

| Column 1 | Column 2 |
| --- | --- |
| Mode | Required response and progress behaviour |
| Chat | Friendly, creative and relatable default that closely follows the user's configured personality and explicit conversational preferences. When a thinking/planning status is useful, give one short useful line grounded in the work. Avoid internal personality meta-commentary. |
| Agentic Chat | Longer work uses concise, meaningful progress at real milestones, blockers or changes in direction. The updates explain useful activity and what happens next; they do not expose a full private thought process or fill the conversation with repetitive tool narration. |
| Worker | Task-driven, serious and professional delivery, while respecting relevant user preferences and instructions. Make the fullest actual model/provider-exposed reasoning or diagnostic trace available where it is permitted to be shown; otherwise provide an honest detailed activity record or reasoning summary without pretending it is a raw trace. |
| Design | Present Working while work is actually underway, with live image/UI/artifact preview when the real backend supports incremental preview. Show the actual preview/revision and progress state; do not invent intermediate images, stages or percentages. The same capabilities remain usable in the other modes. |

NCONV-004 — Chat is the ordinary conversational default. A useful one-line status describes the task, such as checking a supplied source or planning a response, rather than declaring how friendly, relatable or compliant the assistant intends to sound. If there is no useful current status, do not manufacture one. The final answer still supplies the depth requested by the user; a presentation default cannot turn a request for a detailed explanation into a short answer.

NCONV-005 — Agentic Chat emits progress from actual request/run events, saved checkpoints or meaningful execution changes. Coalesce repetitive events while preserving access to details. A long silent wait can be described truthfully as waiting for a tool/provider or a decision; it must not be narrated as active analysis. Progress wording adapts to user instructions such as “keep updates brief” without hiding an approval, actionable failure or terminal outcome.

NCONV-006 — Worker distinguishes public model reasoning fields, diagnostic/tool events and generated explanations in its presentation metadata. Render actual permitted exposed trace content according to the source capability. Do not guarantee access to hidden provider reasoning, reconstruct it from output, or relabel a generated explanation as a raw trace. If a route exposes only a summary, identify that limitation and show the useful available activity/evidence. Worker mode cannot reveal private source material, credentials or another participant's protected context. Detailed logs remain accessible in any other mode when requested or useful.

NCONV-007 — Design binds its preview to the actual job, output identity and preview revision. A supported partial preview is clearly provisional; the last valid preview can remain visible during an update or failure. When a backend supplies only a final result, show a truthful Working state and then the delivered result. It must not simulate a sequence of generation stages to imitate a live backend. Native editable output, an image preview and completed export retain their existing distinct identities and completion boundaries.

<a id="h.g16544ixtg6o"></a>

#### 5.2.7.3 Switching, user instructions and recovery

NCONV-008 — Selection follows the current user intent and work phase. A friendly question can lead to serious implementation work or a design preview without creating a new conversation. Apply a changed presentation policy at a coherent message/stream boundary; preserve ConversationID, branch, draft, sources, attachments, queue, task/run and pending approvals. Do not reset the conversation, resubmit the request, cancel work, reload a model or replay a tool merely because the selected mode changed. Earlier messages retain their original content and attribution.

NCONV-009 — Explicit user instructions override the inferred presentation choice within their stated scope. Requests for a particular tone, brevity, depth, useful progress, detailed available reasoning/activity or a live preview take precedence over automatic defaults. A professional response can remain warm where requested; a design task can receive a concise final reply. Automatic selection must not argue about which mode “allows” an instruction when the capability is otherwise available and authorized.

NCONV-010 — Store enough presentation metadata to reproduce the current view and explain a choice in settings/diagnostics when relevant: selected mode, policy revision, effective user override and the point at which it applies. This metadata is separate from security grants and durable content. Reconnecting restores the applicable mode around the same run; a missing/invalid preference falls back to Chat presentation without altering work. A manual or conversational override changes future eligible presentation, not the record of previously emitted progress. Expose the manual response-presentation preference and eligible output-preference controls together in the shared composer Options panel, with their scope and effective values visible there. A dedicated presentation/settings page may remain a fuller view of the same canonical preferences; it does not create a second store or require a duplicate always-visible conversation control. A changed presentation/output preference affects future eligible responses under its existing scope, not earlier messages, model routing, source grants or tool permissions.

NCONV-011 — A mode switch preserves accessibility: focused input stays focused, live preview controls retain identity, expanded logs do not disappear and status announcements remain concise. Reduced motion affects animations but not the actual task state. If a detailed stream is collapsed or coalesced, the underlying permitted events remain inspectable. Choosing a quieter presentation cannot hide required review or recovery controls; choosing Worker cannot override a user's request to suppress routine progress chatter.

NCONV-012 — Acceptance: in one conversation, ask a friendly question, request a multi-step task, steer into implementation and then ask for an image/UI design. Observe eligible automatic presentation changes with one ConversationID and unchanged tool/permission/model policy. Explicitly request a live preview while in Chat and detailed available activity while in Design; neither is blocked by mode. Test a route exposing a permitted reasoning trace and one exposing none, then a backend with live preview and one with final-only output. Verify honest trace/preview labeling, retained drafts/approvals, no fabricated stages and no repeated execution after a switch or reconnect. Open the composer Options panel in ordinary Chat and Study, change an eligible response preference and verify one canonical preference update without duplicate response controls. Exercise keyboard, explicit Close, outside dismissal, nested-popup Escape, narrow viewport and on-screen-keyboard states; verify reachable controls, preserved draft/selection and correct return focus, no permanently reserved side rail, no submitted request from disclosure/dismissal, and unchanged approval/permission behavior.

<a id="h.1yrmj72up81w"></a>

### 5.2.8 Assumptions Bank: visible working assumptions and corrections

<a id="h.qpji26k0114g"></a>

#### 5.2.8.1 Ownership, entry criteria and logical records

ASSUMP-001 — The Assumptions Bank is shared Dulche-owned data surfaced through Spaces and relevant work/context views. It is not a separate application, private per-Space database or parallel memory engine. An agent can record a meaningful autonomous assumption used to continue authorized work, and the user can inspect, correct, confirm or dismiss its review. This is new owner policy from 21 November 2026. Logging an assumption records uncertainty; it does not turn the statement into a fact or grant authority for an action.

ASSUMP-002 — Record assumptions that could materially affect interpretation, output, a dependent plan or later continuation. Avoid logging every trivial formatting choice, obvious inference or resolved fact. An entry states the assumed proposition in plain language, its applicable scope, why it was needed, evidence/source references, author/run provenance, decision time, affected work and revision, review priority, reversibility and current status. Optional confidence is useful only when its basis is clear; do not invent precise probabilities. A concise justification describes observable evidence and consequences, not raw private chain-of-thought.

ASSUMP-003 — Use stable AssumptionID and revision, with explicit scope such as current request, conversation, project or a broader authorized context. A wider scope is never inferred merely because the assumption recurs. Work references include the relevant ConversationID, TaskID/RunID, plan or artifact revision and dependent steps where known. Store the original proposition and successive corrections separately so historical outputs remain attributable to the assumptions used at the time. Source revocation and privacy rules also apply to evidence attached to an assumption.

<a id="h.bjbbk9woa4cb"></a>

#### 5.2.8.2 Lifecycle and review state

ASSUMP-004 — Keep truth/support status distinct from review state. A new entry is Tentative and Unreviewed. User confirmation records Confirmed with actor/time and the precise scoped proposition; it is still subject to later contradictory evidence or expiry. A correction creates a new revision, marks the superseded proposition Corrected and identifies what replaces it. An agent can retract an invalid assumption with a reason, or mark it Stale when its evidence/scope no longer applies. Dismissing a review prompt changes its review state to Dismissed; the proposition remains tentative unless separately confirmed.

ASSUMP-005 — Entries can be linked to resolve duplication without silently broadening scope. If two runs made the same assumption under the same eligible scope, their dependency links may share one versioned entry. Different source versions, affected subjects or conflicting propositions remain distinguishable. A later agent must inspect the current eligible revision before reuse. Confirmation of one proposition cannot confirm another merely because their text is similar, and an outdated prompt cannot overwrite a newer correction.

ASSUMP-006 — The Bank supports accessible inspection and filtering by scope, status, review priority, project/conversation, affected work and time. Each entry opens its source/context, concise consequence and known dependent outputs. Correct accepts replacement text and, when relevant, scope; Confirm commits the exact displayed revision; Dismiss suppresses that review without endorsing it. Failed saves preserve the user's correction draft and report whether it was received. Keyboard and narrow layouts expose all four actions without requiring hover or a hidden context menu.

<a id="h.cqg15qdz5n85"></a>

#### 5.2.8.3 Applying user corrections and dependent-work recovery

ASSUMP-007 — A user correction takes precedence over the agent's tentative interpretation within its stated scope. Record the correction before updating derived context. Invalidate or mark stale future context summaries, plans and pending operations that depend on the superseded revision. Notify affected running work at a supported safe boundary; display whether the correction is Received or Applied where the distinction matters. Independent work may continue. A correction must not silently cancel unrelated work or change unrelated assumptions.

ASSUMP-008 — Re-evaluate affected future work against the corrected proposition and current grants. A reversible local draft can be amended through its existing authorized owner operation. A pending plan or consequential proposal is revised and any affected prior approval is revalidated. Already committed effects remain in history: correcting “the audience is beginners” can revise an unpublished draft, but cannot make an already sent document disappear. Show any required remediation as concrete work with its own target, scope and applicable authorization. Do not claim that changing the Bank retroactively repaired an external effect.

ASSUMP-009 — An assumption never supplies missing action permission, source access, consent or a required critical decision. Materially uncertain scope, sensitive disclosure or an irreversible consequence follows its existing contract; writing “assume approval” in the Bank cannot authorize it. When authorized reversible work can proceed safely under a reasonable assumption, continue and record the meaningful consequence. Hold only the operation that genuinely needs the missing decision, while making useful permitted progress elsewhere. Bank review is not an additional blanket approval gate for ordinary work.

<a id="h.rsv4wloaey4h"></a>

#### 5.2.8.4 Deferred questions and natural return

ASSUMP-010 — A tentative entry may carry a deferred question with the exact proposition to confirm/correct, why an answer matters, affected future work and review priority. Queue it for an appropriate next naturally active user interaction, such as returning to the relevant conversation/workspace or resuming its task. The agent may revisit it then if it remains useful. Before surfacing, discard or resolve prompts made obsolete by a correction, changed scope, completed work or newer evidence. Group related questions and prioritise material uncertainty rather than interrupting for every minor choice.

ASSUMP-011 — Deferred review does not itself schedule a background agent run, send an email/message, start a call or create an external notification channel. Any separately configured proactive/scheduled behaviour retains its own authorization and delivery contract. A passive local review indicator can show pending entries without pretending they block all work. User activity is a suitable opportunity to ask, not blanket permission to reveal another project's private assumption or trigger a consequential operation. Answering one deferred question does not approve other queued questions.

<a id="h.sgy7jfjgwer4"></a>

#### 5.2.8.5 Memory, compaction and retention

ASSUMP-012 — Memory and Cumulative Compaction reference AssumptionID and the exact revision used, carrying its tentative/confirmed/corrected/stale status. Summaries cannot flatten “assumed audience: beginners” into an established fact about the user. When retrieving a compacted context, resolve the current eligible Bank revision and apply known corrections before relying on it. If the reference is missing or inaccessible, retain uncertainty or exclude it according to policy; never reconstruct a confident fact from an orphaned summary.

ASSUMP-013 — Retention follows the entry's declared scope and existing memory/privacy policy. A Temporary conversation's assumptions stay within its temporary local lifecycle and cannot automatically become durable cross-Space memory. Saving/promoting work re-evaluates which records may persist; a user-confirmed proposition is not automatically a global memory. Archive/dismissal controls presentation, while actual erasure follows the scoped data lifecycle and keeps only permitted dependency/audit metadata. A shared project exposes only assumptions and attached evidence authorized for its participants.

ASSUMP-014 — Acceptance: draft a report under the tentative assumption that its audience is new to the subject, record the affected outline/run and compact the conversation. Return in a naturally active interaction, correct the audience to experienced readers, and verify that the Bank revision, retrieved context and future outline all use the correction. Keep the earlier draft attributable. Dismiss another review and confirm it stays tentative; test a stale correction from another window, an inaccessible evidence source and a Temporary conversation. Attempt to authorize a consequential action merely by logging an assumption and verify it remains governed by the real permission contract, with no unsolicited external message or duplicated effect.

<a id="h.f7tb6sj5h268"></a>

### 5.2.9 Expansion traceability and resolved policy

This expansion selects and restates the following source material into explicit requirements. The paths and clause ranges identify provenance; they do not incorporate every historical instruction from those files. The owner correction supplies the current Actions name and confirms Study/Translate as Spaces. Logical record fields, transaction boundaries and recovery mechanics above are implementation contracts specified by this document, not claims that the current repository implements them.

| Column 1 | Column 2 |
| --- | --- |
| Expanded domain | Exact Atlas anchors and reference clauses |
| Conversation, composer, running state | spaces-chat, spaces-compose, spaces-attachments, spaces-emoji, spaces-thinking, spaces-permissions; later atlas-chat-position-code; retained [spaces.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/spaces.complete.md), UX27-SPACES-001 through UX27-SPACES-014, SPUI-147 through SPUI-193. |
| History operations | Retained [spaces.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/spaces.complete.md), SPUI-180 through SPUI-186; especially SPUI-183's three user-message edit operations. |
| Space Builder and review | spaces-manage; retained [spaces.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/spaces.complete.md), SPUI-201 through SPUI-211 and their stated Basic/Advanced/Expert semantics. |
| Assistants and Specialists | home-assistants, home-specialists, spaces-specialists, spaces-assistants, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, collaborators-picker, write-collaborators-picker; later atlas-assistant-space-code; retained [assistants.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/assistants.complete.md), V3-ASSISTANTS clauses and V2-B0064 through V2-B0084 for persistent identity/configuration/workflow. |
| Delegation | spaces-subagents, spaces-subagent-detail; retained [subagents.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/subagents.complete.md), V3-SUBAGENTS contracts and its typed lifecycle/join/checkpoint technical section. |
| Generated output and exports | spaces-generative-ui, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery; retained [generative-ui.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/generative-ui.complete.md) and shared artifact contracts. |
| Actions | spaces-running, current product name supplied by the owner; retained SPUI-149 status semantics and UX27-SPACES-007 canonical Summary/task behaviour. |
| Action Log, Goals, Plan and Checkpoints | Owner's explicit preservation clarification, 21 November 2026; retained [spaces.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/spaces.complete.md) V2-B0799, UX27-SPACES-007, SPUI-147–153 and SPUI-167; [dulche.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/dulche.complete.md) V2-B2083 persistent execution; [subagents.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/subagents.complete.md) durable tree/checkpoint/recovery contracts. Current pinned SpaceTasksDashboard.cui and SpaceTaskWorkspaceService.cs provide inspected dashboard, exact-task selection, permission-review and follow-up paths; inspection does not claim runtime acceptance. |
| Research and Shopping | spaces-research, spaces-shopping; retained [spaces.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/spaces.complete.md), V3-SPACES-FEA-12/13, UX27-SPACES-012, V2-B0801/0802 and SPUI-164 through SPUI-179; Atlas's current matching labels override historical tab wording. |
| Translate | spaces-translate; retained [dulche-translate.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/dulche-translate.complete.md), V3-DULCHE-TRANSLATE and V2-B1827 onward, including TranslationSets, protected terms, structured artifacts and segment/job contracts. |
| Experiences | spaces-experiences; retained [spaces.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/spaces.complete.md), V3-SPACES-FEA-15 and V2-B0804 through V2-B0809. |
| Study | spaces-study, study-subject, study-topic, study-session, mobile-study, with cards-study owned by Decks; retained technical/[spaces-10.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/technical/spaces-10.md) and technical/[spaces-11.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/technical/spaces-11.md), STUDY-UI-201 through STUDY-UI-235 and STUDY-AC-201 through STUDY-AC-219; legacy native specification sections 3 and 8.5 used as context. |
| Natural Conversations | Owner clarification, 21 November 2026: Chat, Agentic Chat, Worker and Design are automatically selected response/presentation modes; unchanged capabilities/permissions and unrestricted cross-mode access to supported previews/documents/available reasoning/activity. This is new product policy rather than a claim of Atlas/backend implementation. |
| Assumptions Bank | Owner clarification, 21 November 2026: agents may record meaningful assumptions, users may correct them and deferred review can occur at the next natural active interaction. Shared Dulche ownership, scoped lifecycle and correction/compaction semantics are specified above; this is new product policy rather than claimed existing code. |

All listed retained module/technical paths are under docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/ at pinned repository revision 1f92cd888f59025cc684f0720163efdbc580f8e3. The authoritative Atlas is docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html at that revision. Module filenames in the table are under modules/; technical paths are explicit.

DEC-006 resolution — The earlier broad uncertainty about user-message history editing is resolved by SPC-030 through SPC-039, especially SPC-032. Retained SPUI-183 explicitly distinguishes Replace messages below, Update context and new messages only, and Edit in branch. The new contract makes those choices branch-preserving and revision-attributed, and keeps real action receipts outside narrative deletion. Alternative answers/retries remain attributable rather than erasing previous work. No blanket history-policy decision remains necessary for these documented operations. Irreversible privacy erasure continues to use its separate explicit data-deletion contract.

Narrow policy retained — Exact Study evidence weights, proficiency thresholds and review intervals remain the already published APP-STUDY-003 learning-policy item. STUDY-023 bounds its effect to unsupported derived labels/dates; it does not block teaching, source handling, question/answer records, canonical app integration or manual planning. This expansion introduces no additional broad owner-blocking decision.

<a id="h.cpqkey81qkf3"></a>

## 5.3 Assistants, Specialists, delegation and generated results

APP-AGENT-001 — Persistent Assistants use the same identity, name and portrait across Home, Spaces, picker, editor collaboration and attribution. Renaming or replacing artwork preserves Agent identity. Main is permanent; secondary threads are independent conversations under that identity. Archiving a secondary thread and deleting an Assistant are different commands with declared consequences. The pictured Alex, Maya and Rowan are illustrative identities, not mandatory accounts.

APP-AGENT-002 — Assistant conversations use one compact identity row. Workspace opens sources, memory scope and secondary conversations; it defaults closed and remembers visibility per Assistant. Configuration routes to AI Studio, while conversation permissions remain directly accessible. Team visibility All, Relevant and None changes presentation without hiding consequential audit records or conferring access to another Assistant’s private memory. A narrow workspace opens a navigable overlay instead of crushing the conversation.

APP-AGENT-003 — Specialists are reusable job-defined expertise, distinct from persistent Assistants and generic delegated workers. Their intended use, scope and permission policy are inspectable before attachment. Configuration uses the canonical AI Studio definition. Selecting or attaching a Specialist does not begin a job.

APP-AGENT-004 — Each subagent is a real run with stable parent relationship, objective, identity, delegated inputs, allowed outputs, state, budget and owned artifact references. Effective access is the intersection of parent grant, agent capability and current policy. Concurrency, depth and resource limits remain enforceable. Cancel, retry and review target the selected run; retry creates an explicit new attempt. Completed sibling work survives another worker’s failure. No worker silently takes another worker’s write scope.

APP-AGENT-005 — Generated interfaces use validated CUI component/action schemas and canonical owning-app entities. A model cannot inject arbitrary executable UI or bypass permissions. Controls preserve draft/selection across streaming updates and support keyboard, loading, empty, denied and failure states. Invalid output keeps the previous valid view recoverable. An ephemeral planner proposal remains a proposal until its exact app mutation passes review and commit.

APP-AGENT-006 — Live Present and Canvas previews show actual owning-app scene/object IDs, revision, selection and responsible collaborator. Partial content remains visibly partial. Open in App enters the same artifact and work session. A finished file card requires a durable owning-app receipt and states native/export type, identity, revision and local/sync status. Open resolves that artifact; Download reads verified existing bytes. Export failure retains format, destination, chosen revision and scoped retry/save-copy actions without invalidating the native source.

APP-AGENT-007 — Editor invitations identify collaborator type, document/range scope, operation mode and any notes. Invite starts scoped work; inspecting or accepting a proposed change is separate. Presence and active cursors are transient. Pause/Stop retains proposals/checkpoints and cannot undo already committed edits silently. Applying a proposal validates its exact target and expected revision as one undoable operation. Rejecting one proposal preserves unrelated authorised work. Conflicts refresh the comparison instead of applying unconditional last-writer-wins.

Acceptance — Open one Assistant through Home and Spaces, then invite it to Write/Present. Verify one identity with separately scoped conversations and document permissions. Run independent children with mixed outcomes, cancel one and reconnect. Reject invalid generated UI; save a native artifact; fail one export. Completed sibling artifacts, native content and the prior valid UI remain available.

Atlas anchors: spaces-assistants, spaces-assistant-alex, spaces-assistant-maya, spaces-assistant-rowan, spaces-specialists, spaces-subagents, spaces-subagent-detail, spaces-generative-ui, spaces-present-working, spaces-canvas-working, spaces-generated-files, spaces-export-recovery, collaborators-picker, write-collaborators-picker, present-collaborators, write-collaborators, collaboration-review; active atlas-assistant-space-code.

<a id="h.mz1r4hjsouo3"></a>

### 5.3.1 Persistent Assistants, Specialists and collaboration

<a id="h.rh26n93tw5i7"></a>

#### 5.3.1.1 Identity, discovery and configuration

ASST-001 — An Assistant is a persistent configured identity spanning its Main conversation, secondary threads, Task/Runs, eligible memory, projects, artifacts and configured scheduled work. Opening it through Home, an editor or Spaces resolves the same identity. Renaming, replacing an avatar, editing instructions or starting another conversation does not create another Assistant. A Specialist is a reusable job-focused definition; a delegated child is an execution relationship. Neither becomes a personal Assistant merely because it has a name or model. Preserve an existing canonical AgentID where the implementation uses one; do not create per-app identity copies.

ASST-002 — The global Home catalogue supports creation, existing/recent identities, current work, configuration and lifecycle management. Its entries distinguish configuring, available, working, paused, disabled and archived states where applicable. Configuration belongs to the canonical AI Studio definition and includes role/instructions, permitted resources and tools, model policy, memory, delegation, usage limits and supported proactivity. Computer use, Virtual Device and Code, Sites and Stacks project controls appear when those capabilities exist. Save validates a new definition revision and retains the previous one for historical attribution. Cancel discards only the unsaved configuration patch.

ASST-003 — Definition fields and runtime facts remain separate. The preferred model is a policy, while each run records its actual route. A connected account is a resource reference, while its current authorization is evaluated when used. A configured memory source does not guarantee retrieval or make every memory relevant. An instruction such as “continue until finished” cannot override a current revocation, exhausted hard budget or stopped run. Identity, definition revision, effective grants and actual execution provenance must be inspectable together without exposing secret credentials.

<a id="h.tttpsyy27nj"></a>

#### 5.3.1.2 Main conversation, Workspace and lifecycle

ASST-004 — An Assistant conversation uses the latest Atlas compact identity row. Workspace defaults closed and remembers its visibility per Assistant. It exposes authorized sources, memory scope and secondary conversations; narrow layouts use a navigable overlay. Closing Workspace preserves active work, selection and conversation draft. Main remains the Assistant's stable primary thread; starting or archiving a secondary thread is independent of deleting the Assistant. A source added in one thread follows its declared scope and does not silently become available to every thread.

ASST-005 — Pause, disable, archive and delete have distinct effects. Pause addresses running work according to its supported runtime boundary. Disable prevents new automatic initiations while preserving inspectable existing records and stopping or explicitly reconciling pending automatic work. Archive changes ordinary discovery. Delete presents the identity and dependent-data scope affected, applies the canonical retention/deletion policy and does not silently delete separately owned shared artifacts. Turning an Assistant back on must not replay every missed event or previously uncertain external action. Show eligible pending work for deliberate continuation under its scheduling policy.

ASST-006 — Proactivity is explicitly configured by trigger, scope and destination. A schedule/event rule identifies the condition, eligible account or project, permitted action classes, notification channel and relevant quiet/usage limits. The same Automation/scheduling service and Task/Run system execute it. A conversational suggestion to “keep an eye on this” is not evidence of a functioning schedule: report the actual created rule or the missing capability. Existing authorization covers later actions within its exact scope; configuration does not grant unrelated accounts, files, external messaging or computer access.

<a id="h.s2vxcgyqag3a"></a>

#### 5.3.1.3 Specialists, Teams and artifact invitations

ASST-007 — Selecting a Specialist opens its role, intended inputs, eligible tools/sources, definition revision and effective policy before attachment or invocation. Attaching it to a draft creates a reference; invocation creates a run using the submitted context. Quick Actions have stable identities, labels, typed inputs where needed and a defined entry point. Their disabled states explain missing required inputs or capability. Editing a Quick Action does not rewrite the instructions attributed to an already executed invocation.

ASST-008 — Teams retain TeamID, membership, roles and conversation participation independently of visibility. All, Relevant and None control the presentation of Assistant-to-Assistant conversation. They cannot hide required approvals, stop/recovery controls or consequential action receipts, and do not grant private memory between members. Separate user-facing Assistant messages preserve speaker identity. A collapsed exchange may show a truthful summary and open the underlying permitted work record; it must not imply that a particular Assistant personally performed another member's action.

ASST-009 — An editor invitation specifies artifact identity, eligible revision or range, requested role and available operations. A collaborator may propose an edit without authority to commit it. Review, rejection and application produce distinct recorded outcomes. Apply rechecks the target revision and grant, uses the owning app's typed mutation and retains its receipt/undo relationship. Two invited workers modifying the same range must resolve a conflict or rebase a proposal; last arrival must not silently erase an accepted change. Removing a collaborator revokes future access without falsifying already recorded authorship.

ASST-010 — Acceptance: create an Assistant in Home, open it in Spaces, add a secondary thread, update its definition and return through an editor invitation. Confirm one identity, attributed definition revisions and intact Main history. Exercise scoped proactivity, a disabled trigger, a revoked resource, a Team visibility change and a stale artifact proposal. A bounded development task in Code, Sites or Stacks must continue through the same project, task, checkpoint and run after reopening; a second project copy or simulated tool transcript does not satisfy this journey.

<a id="h.n3ss2dhtm2ll"></a>

### 5.3.2 Delegated execution, joins and recovery

<a id="h.nilekt4i4mz9"></a>

#### 5.3.2.1 Child creation, admission and scope

DELEG-001 — Configuration of a potential child and execution of a child are separate operations. Execution allocates a stable child/run identity with exactly one causal parent and a root request. Record objective, input/context policy, requested route, effective definition, budget reservation, permission scope, depth and output contract before admission. The visible drawer and child detail view project these same records. A card is not evidence that a worker has started; Created, Queued, Running, Waiting, Pausing, Paused, Completed, Failed, Blocked, Cancelled and Timed out are distinguishable where applicable.

DELEG-002 — Child context may be None, an admitted parent snapshot or explicitly Selected content. Each option defines the actual supplied messages, sources and artifact revisions. It cannot expose a mutable parent transcript or unrelated sibling/private memory by default. Effective child authority is the intersection of current parent authority, caller policy, requested child scope and resource access. A broader child request is rejected or narrowed visibly. Revocation is rechecked before retrieval or effects, including work already queued before revocation.

DELEG-003 — Parallel creation returns handles correlated with the requested children, even if admission or completion order differs. The scheduler may run eligible independent children concurrently; concurrency permission is not a promise of simultaneous hardware execution. A single endpoint that supports one active generation queues work honestly. A waiting parent must release a scarce execution slot needed by its child, avoiding a self-imposed parent/child deadlock. An explicit model choice cannot be silently substituted merely to improve apparent parallelism.

DELEG-004 — Enforce configured limits on direct children, active parallel children, maximum depth and aggregate descendant consumption. Root depth is zero. Track reservations, settled usage and unknown usage separately for tokens, calls, active time or cost where supported. Child work contributes once to parent totals; overlapping wall time does not become serial elapsed time. Reaching a hard limit prevents additional admission or holds eligible work, with the limiting budget and recovery action visible. It does not erase completed outputs.

<a id="h.gjupeua5zdf"></a>

#### 5.3.2.2 Joining, cancellation and restart

DELEG-005 — A parent chooses an explicit join policy. Collect returns each child's result or failure; Require all holds success until every required child succeeds; Best effort admits a partial result with failed/missing children identified; Fail fast stops eligible unfinished siblings when the selected failure condition occurs. The join records its required child set and their attempts, so a late extra child cannot silently change a previously reviewed requirement. Joining any one successful result must identify which child won and the disposition of remaining workers.

DELEG-006 — Stop on a child cancels that child's unfinished subtree according to the runtime boundary. It does not stop unrelated siblings or its parent unless the declared join/failure policy requires it. Root Stop reaches unfinished dependent descendants. Pause prevents new dependent dispatch and states whether children pause, finish already admitted work or stop; a capability that cannot pause safely reports that limitation. Cancellation does not undo completed external effects, and their receipts remain available.

DELEG-007 — Retry creates a new attributable attempt referencing the failed attempt and selected recoverable input/output checkpoint. It preserves successful siblings and does not automatically replay a completed purchase, send or publication. Before retrying an uncertain effect, reconcile its operation identity with the destination or require an explicit safe recovery path. Resume continues a supported retained execution; restart re-admits work. Checkpoint restore exposes whether it is exact continuation or reconstruction from saved state, and never claims an unavailable backend continuation.

DELEG-008 — A durable child detail includes parent/root links, objective, supplied input references, actual model/endpoint, state transitions, budget/usage evidence, public tool/action events, artifacts and error/recovery reason. Show useful work summaries and results, not private chain-of-thought. A timed-out connection is not proof that a remote worker stopped. Reconnect queries the same child identity and reconciles its latest state before showing Retry. A dependency failure links to the failed prerequisite rather than generating a generic “something went wrong” loop.

DELEG-009 — Acceptance: run two independent children and one dependent child under a constrained endpoint. Verify honest queued/overlapping timestamps and parent slot release. Exercise each join policy, stop one subtree, revoke a source, exhaust a descendant budget and reconnect during an uncertain action. Retain successful outputs, show the exact failed dependency and prove that restart creates a new attempt without duplicate effects. An animated parallel graph without independently attributable execution fails acceptance.

<a id="h.54nbsv24nx1"></a>

### 5.3.3 Generated surfaces, canonical outputs and export recovery

<a id="h.4l4v5qllnzws"></a>

#### 5.3.3.1 Surface identity and interaction

OUTSP-001 — A generated surface has SurfaceID, schema/version, source run, state revision, component/action identities and canonical bindings. It uses approved CUI primitives and templates. Rendering a model-authored description does not execute arbitrary JavaScript or grant a tool capability. Supported local interactions, such as expanding a section, differ from typed actions, such as creating a Planner event. Each actionable control declares its input contract, target and effective permission requirement.

OUTSP-002 — Streaming patches apply to an expected surface revision. Validate structure, supported components, action bindings and resource access before application. Keep the last valid state if a patch is invalid. Preserve user input, focus and scroll where the affected component survives; a model refresh must not erase a partly completed form. A replaced or removed component receives explicit conflict handling if it holds unsent user data. Old action controls cannot execute against a newer incompatible target revision.

OUTSP-003 — Bound artifacts remain canonical references. An embedded Write document, Decks deck, Forms assessment or Planner proposal opens the owner renderer and appropriate revision. Revoked access redacts content/actions through the same permission system without leaking cached private text. Generated summaries describe what their source record establishes. A proposed schedule displayed in a polished card remains a proposal until a real Planner operation succeeds.

<a id="h.2ru3mcraanmw"></a>

#### 5.3.3.2 Promotion, native saves and exports

OUTSP-004 — Ephemeral conversation output and a saved artifact have separate lifecycles. Promote/Save selects an owner app, destination and expected source revision; the owner validates and creates or updates the canonical artifact. Add the resulting artifact to the conversation or collection only after a real receipt supplies its identity. A failed save retains the source output for retry. Multiple clicks use one creation operation unless the user deliberately requests another copy.

OUTSP-005 — Generated Present content consists of editable native objects and actions where that format supports them; Canvas output consists of its actual scene/object model. A screenshot may be a preview or imported image, but cannot be labelled an editable deck or scene. Keep asset provenance and allowed use attached to outputs. Native save, derived export and external publish are distinct commands with separate success states.

OUTSP-006 — An export job identifies the artifact and chosen revision, requested format, destination, options, progress, durable output reference and failure reason. Failure to export does not invalidate a saved native artifact. Retain its choices and permit retry or Save copy against the selected revision; a later document edit does not silently substitute another revision. Show Download ready only after bytes exist and can be retrieved. If a destination fails after conversion, reuse verified converted bytes where valid instead of rerunning content generation.

OUTSP-007 — Acceptance: stream a form while the user edits a field, reject an invalid patch without losing input, apply a scoped native save and revoke a bound source. Fail an export after native save, reopen recovery, then retry the same revision successfully. Confirm one artifact identity, no false ready state and no unrelated run restart. Keyboard focus returns to the initiating control or meaningful recovery heading after overlays close.

<a id="h.xo3bokgsrtcy"></a>

### 5.3.4 Specialist sources and knowledge workspaces

<a id="h.crruwbtap4s4"></a>

#### 5.3.4.1 Integrated workspace and canonical source records

KNOW-001 — Specialists support a source-grounded knowledge workspace within their existing configuration and Spaces interaction. Sources, chat, source inspection and saved sourced notes form one coherent workflow using the same SpecialistID, conversations, runs and canonical artifacts. A Specialist can bind to a chosen collection and answer from it across its authorised entry points. This adds NotebookLM-style source work without creating another standalone app, Specialist registry, file store or memory engine. The owner-defined binding rules below are normative; the external reference establishes only selected baseline interactions.

KNOW-002 — A SourceCollection has stable CollectionID, owner/scope, title, membership revision and access policy. Each membership references SourceID, selected revision/live-update policy, labels and enabled/disabled state. A Source has origin kind, canonical Files/artifact identity or normalised URL, title, provenance, ingestion state and revision lineage. A SourceRevision records source version/content digest where available, acquisition time, extraction capability/version, retained representation, coverage and location mappings. Logical fields describe required behavior, not a claim that current code already exposes these exact schemas.

KNOW-003 — Collections can be reused by permitted Specialists without duplicating source bytes or broadening access. Add, rename, label, select, deselect and remove membership are distinct from editing/deleting the original. Selected sources define the eligible corpus for the request; unselected sources remain available for later selection but cannot silently contribute to that answer. A selected revision is inspectable. Select all uses an explicit current membership snapshot; adding a source later does not retroactively change a submitted request or historical citation.

KNOW-004 — Ingestion distinguishes Selected, Queued, Fetching, Extracting, Indexing, Ready, Partially ready, Unsupported, Failed, Unavailable and Revoked as applicable. A listed upload is not necessarily readable. Supported files/links, pasted text, structured native artifacts, scanned documents, images, audio or video transcripts use registered adapters and honestly expose extraction coverage. Page limits, omitted tables/comments, OCR uncertainty, missing captions and unsupported media remain visible. No adapter may claim it understood an entire video when only its transcript was ingested. Failed parts can retry independently while successful revisions remain usable.

KNOW-005 — Adding an explicit URL authorises eligible acquisition of that specified source under current access/privacy policy; it does not enable general web search or crawling. Redirect destinations and canonical provenance remain inspectable. Nested links, related pages and search results are not automatically added to the collection. Authentication walls, inaccessible content and retrieval failures produce an honest blocked source. Refresh follows the selected source's declared policy and creates a new revision; it cannot silently substitute unrelated content because the original moved.

<a id="h.mlbt99xlrglo"></a>

#### 5.3.4.2 Independent source, web, general-knowledge and memory controls

KNOW-006 — SourceBinding records SpecialistID/definition revision, CollectionID, binding revision, selection policy and effective knowledge policy. Binding activates source-only factual answering by default. The following settings remain independently inspectable and configurable within the caller's existing authority; enabling one cannot enable another.

| Column 1 | Column 2 |
| --- | --- |
| Setting | Meaning when the Specialist is bound |
| Bound sources | Selected permitted source revisions establish the eligible factual evidence. |
| Web research | Disabled unless separately enabled. Existing tool installation or a model's browsing ability does not enable it. |
| General knowledge | Disabled as an additional factual basis unless separately enabled. This governs answering policy, not removal of model parameters. |
| Memory | Independently enabled/disabled and scoped under the existing memory policy. Binding does not automatically disable it or widen it. |

KNOW-007 — Web and general knowledge are different capabilities: allowing live research does not license uncited parametric facts, and allowing general explanation does not grant web access. With either enabled, distinguish bound-source evidence, retrieved external evidence and general explanation in the answer's provenance. Bound sources retain precedence for the defined subject's facts; contrary external information is shown as a conflict or possible update, not silently substituted. General knowledge cannot impersonate a source citation. Broadening a knowledge policy applies to newly admitted or explicitly re-admitted work. Narrowing or revocation governs future affected retrieval/disclosure steps of ongoing work at supported safe boundaries. Historical results retain their captured policy provenance; already transmitted context is reported truthfully.

KNOW-008 — Memory can retain permitted learner progress, difficulty, preferences, goals, prior activity and continuity while factual teaching remains source-bound. Canonical Study evidence, Forms results and Decks ReviewRecords remain their owners' records; memory references them rather than inventing replacement scores. The Specialist may use eligible progress to choose practice or explain what the learner previously attempted. Remembered subject claims or a prior unrestricted answer cannot bypass the corpus restriction. Where relevant, identify personal progress as personal evidence rather than pretending it came from the textbook. Disabled memory does not delete source content or already existing canonical assessment records.

KNOW-009 — Effective eligibility is the intersection of binding, request selection, current source access, disclosure policy, user/organisation restrictions and actual route capability. Natural Conversation modes retain the same underlying tools, but an otherwise available tool cannot obtain out-of-scope evidence for a bound request. Source-only does not disable useful permitted calculation, formatting, artifact editing or study-progress operations over eligible inputs. A tool/model failure cannot trigger an outside-search, general-knowledge, cloud-processing or broader-memory fallback contrary to the active policy.

<a id="h.xdggn182fphc"></a>

#### 5.3.4.3 Grounded answering, exact citations and uncertainty

KNOW-010 — A source-bound model still has pretrained knowledge; the product must not claim it erased that knowledge or guarantees zero unsupported output. Enforce the answer policy through scoped retrieval/context assembly, disabled outside-retrieval routes, claim-to-evidence attribution, citation validation and an unsupported-claim response path. Language comprehension, paraphrase and explicitly shown derivation are permitted; adding unsupported domain facts is not. Embedded source text is evidence, not an instruction capable of changing tools, source selection or policy. Verification records its actual coverage and limitations rather than awarding a decorative guarantee.

KNOW-011 — Citation records identify SourceID, SourceRevisionID, an actual supporting segment and resolvable location. Location may be document paragraph/block and range, PDF page plus text/region, slide/object, sheet/table cells, webpage section/text range, or media transcript time range, according to the adapter. A citation opens the captured permitted passage in context and identifies the revision. Page numbers, quotations and timecodes must come from extraction metadata, never model invention. When precise mapping is unavailable, disclose the coarser location and avoid claiming line-level accuracy.

KNOW-012 — Validate that cited evidence belongs to the admitted corpus, was actually available, matches the claimed quotation/range and supports the associated claim. Existence of a real source URL alone is insufficient. Distinguish direct source statements, synthesis across sources, calculations and tentative interpretation. A derived calculation retains source inputs, units and computation provenance; its result need not be a literal quotation. A plausible citation attached to an unsupported inference fails grounding. Streaming text may be provisional, but final verification and saved-output status cannot conceal unresolved support failures.

KNOW-013 — Missing evidence produces a bounded answer: state what the selected material establishes, what it does not establish and which relevant source/section is missing or unreadable. A retrieval miss is not proof that the entire source lacks the fact; distinguish insufficient retrieval coverage from inspected absence. Empty selections and wholly unavailable corpora do not fall back to general knowledge. The user can add/select an appropriate source or explicitly change the relevant policy. Continue useful supported work without requiring unrelated operations to wait for the missing evidence.

KNOW-014 — Conflicting passages retain both claims, exact revisions, dates/scopes and citations. Use a declared source-priority rule where one exists, explaining its application; otherwise report the unresolved difference. Do not silently average incompatible rules or choose whichever passage matches a prior assumption. Source precedence establishes the workspace's evidentiary basis, not a guarantee that a document is objectively correct. A user correction can update an instruction, mark a source disputed or supply a new source; it cannot retroactively make an unsupported factual statement source-supported.

<a id="h.ot1bywoyx0io"></a>

#### 5.3.4.4 Runs, updates, revocation and retained context

KNOW-015 — Admission snapshots Specialist definition/binding/policy revisions, selected SourceRevisionIDs, authorised memory scope and web/general settings. Runs, subagents, reviewers, tool calls and generated outputs carry that provenance. A delegated worker receives only its permitted subset and cannot broaden the parent's evidence policy. Switching mode/model or retrying does not remove the binding. A necessary scope change is explicit and revalidates affected work. Old conversation messages remain history; they are not automatically promoted into eligible source evidence for the current request.

KNOW-016 — Source edits create a distinguishable new revision and invalidate affected extraction/index mappings. New requests resolve the declared latest/pinned policy; running requests remain attributable to their admitted revision unless explicitly refreshed. Mark dependent answers, notes and assumptions as potentially stale when relevant source evidence changes; do not rewrite saved history or already exported files silently. A historical answer can remain valid for its named source revision while being out of date for the current collection. Reprocessing retains usable previous material only where current access and retention permit it.

KNOW-017 — Revocation immediately removes the source from future eligible retrieval and stops affected ongoing retrieval/disclosure at supported boundaries. Purge or restrict derived caches, indexes, excerpts and previews according to the same access/retention contract. Existing citations become access-limited without revealing revoked content through snippets. Removing collection membership, loss of original access and deliberate privacy erasure have different effects. Preserve only permitted dependency/audit metadata, and report pending cleanup honestly. An inaccessible required source cannot be reconstructed from a hidden old summary as a workaround.

KNOW-018 — Compaction and memory retain source-policy version and citation/reference provenance, including stale, contested, revoked and unsupported status. They cannot flatten a tentative inference into fact, replace a source passage with a model-written summary as if equally authoritative, or turn a past general-knowledge answer into closed-corpus evidence. Reopening resolves current eligible references before use. Source retrieval remains lazy and bounded; summaries/indexes accelerate access but do not become independent factual authorities merely because they fit the context window.

<a id="h.pcjmwbnnyc7s"></a>

#### 5.3.4.5 Notes, shared scopes and assumption checking

KNOW-019 — The workspace exposes a selectable sources list, readiness/freshness indicators, source viewer, normal chat and saved output references. Closing a pane preserves selection and drafts; narrow screens expose the same functions through reachable views. Save sourced note chooses a canonical Write document, Board page or another supported owner artifact, retaining claim citations, source revisions and generation provenance. The resulting owner receipt creates the workspace link. Reopen/Edit/Open in App resolve that same artifact, and failed save/export preserves the useful draft without a phantom note.

KNOW-020 — User notes and generated notes can become selected sources through an explicit add/bind operation. Preserve their authorship and upstream citation lineage: re-ingesting a generated summary does not turn it into independent corroboration or hide an unsupported ancestor. Editable notes distinguish retained citations, changed claims and unresolved support after edits. A user's annotation is evidence of the user's statement, not automatically proof of the annotated subject fact. Exports preserve useful source references and recipient-access limitations without granting source rights through the exported title or link.

KNOW-021 — Personal and shared collections, conversations, saved artifacts and memory have separate access scopes. Sharing a Specialist definition or collection does not grant access to every underlying file, private conversation or learner's memory/results. Each caller resolves source access independently; shared source bytes do not imply shared personal progress. Shared teaching materials may support different learners with their own permitted memory. Private source names, counts, excerpts and citation previews cannot leak through a shared output, search result or source-selection error.

KNOW-022 — The Assumptions Bank checks material assumptions against the selected eligible sources. Record AssumptionID/revision, source-policy revision, inspected evidence and a support outcome of Supports, Contradicts or Insufficient, with a concise explanation and exact source locations. This evidence outcome is independent of user Confirmed/Dismissed review state. An assumption contradicted by the corpus is challenged and affected future reasoning is revised or held as appropriate; a confirmation cannot erase the contradiction. Insufficient evidence preserves uncertainty rather than asserting a refutation. Source updates trigger eligible rechecks and mark dependent work stale without repeating committed external actions.

<a id="h.jfsfx2l6uzhz"></a>

#### 5.3.4.6 Failure recovery and acceptance

KNOW-023 — Distinguish access denied, fetch/authentication failure, unsupported format, incomplete extraction, index failure, stale source, revision conflict, missing citation target, insufficient support, policy denial and model failure. Recovery acts on the failed source/revision or output stage, preserving unaffected ready sources and drafts. Partial ingestion is selectable only with its coverage limitation visible. Cancellation releases unfinished ingestion/retrieval resources and retains committed revisions under policy. No status may equate Uploaded, Indexed, Cited and Verified: these establish different facts.

KNOW-024 — Acceptance A: create a Specialist, add a readable document, explicit webpage and unsupported file, inspect ingestion, then bind a selected subset with web/general knowledge disabled. Ask one supported and one unsupported question. Resolve every citation to the correct source revision/location; reject an invented locator and an unrelated real citation. Attempt a source instruction that asks the worker to search elsewhere and verify enforcement beneath the prompt. Save a sourced Write note, reopen through both entry points and confirm one artifact identity.

KNOW-025 — Acceptance B: enable private study-progress memory while retaining closed-corpus teaching. Use real Forms/Decks evidence to choose practice; introduce a remembered subject claim absent from the corpus and verify it cannot supply the answer. Separately enable web research and general knowledge in isolated runs and inspect distinct provenance. Delegate, switch presentation mode, retry and compact; confirm the effective policy survives all four. Share the Specialist with another learner and verify neither learner's memory, marks or restricted sources leak.

KNOW-026 — Acceptance C: record an assumption, obtain source support, then update the source to contradict it. Preserve the old answer's provenance, mark dependent work stale and show the new evidence outcome independently from user confirmation. Revoke a cited source mid-run, test access-limited historical citations and repair with an authorised source revision. Re-ingest a generated note and verify it is not counted as independent corroboration. Exercise empty selection, partial OCR, failed export and keyboard/mobile source inspection without silently broadening the corpus.

<a id="h.jq73btjozilt"></a>

#### 5.3.4.7 Reference baseline and source status

These are new owner-approved product requirements and implementation contracts, not a claim that the repository already implements them. The official Google help pages establish a limited comparison baseline: imported sources and per-source selection; source-linked chat citations; and saving chat output as reusable notes. The tighter independent web/general/memory settings, canonical 9to1 artifact ownership, revision-bound verification, delegation and Assumptions Bank behavior above are specified for 9to1. Google quotas, prices, account restrictions and its wider feature catalogue are not adopted.

| Column 1 | Column 2 |
| --- | --- |
| Official reference | Limited baseline used |
| [Add or discover new sources for your notebook](https://support.google.com/gemininotebook/answer/16215270?hl=en) | Import/select sources and expose source-type/extraction limitations. |
| [Use chat in Gemini Notebook](https://support.google.com/gemininotebook/answer/16179559?hl=en) | Ask about selected material, inspect citations in source context, and save responses as notes. |
| [Create and add notes](https://support.google.com/gemininotebook/answer/16262519?hl=en) | Reuse saved notes as selectable, citable material. 9to1 uses its existing artifact owners and explicit source-promotion policy. |

<a id="h.ozi49z4u2epg"></a>

## 5.4 Task-specific Spaces

APP-FLOW-001 — Actions is a structured Space over the canonical Action Graph, distinct from recurring Automations. Objective, plan, dependencies, blockers, approvals, outputs and Summary describe the same run. Default filtering shows the current user’s relevant scope with explicit broader filtering. A queue count reports admission state rather than decorative shelf position. A preparation milestone does not imply that proposed Planner or file changes were committed.

APP-FLOW-002 — Research separates question, reviewable plan, permitted sources, budget, execution and deliverables. Quick, Extended, Deep and Custom are budget choices rather than completion-time promises. Outside-web access is an explicit source-scope decision bounded by permissions. Sources, citations, gaps and unfinished findings remain attributable. A finished report is a canonical Write artifact; export preserves its source links and missing-evidence qualifications.

APP-FLOW-003 — Shopping distinguishes Required and Preferred constraints, strict matches, partial/near matches and contextual suggestions. Keep mismatch reasons visible. Product identity is distinct from merchant offer; each offer carries currency, source, price and last-check state. Save, Compare and Purchase are separate actions. A selected product or AI recommendation is not purchase authority. Fictional Atlas products/prices must never populate live recommendations.

APP-FLOW-004 — Translate is a Space and uses the ordinary Spaces conversation. Persistent target-language choices sit immediately above the composer; each target has its own labelled response bubble. Sibling variants belong to one TranslationSet. Refining a language preserves the source and other variants. Language choices persist with conversation/draft. Copy/save is separate from authorised replacement or publication in the source’s owning app; locale/format changes that alter meaning are explicit.

APP-FLOW-005 — Experiences stores canonical world/scene, character, inventory, evidence, objectives, flags and checkpoints. Validate and commit the structured transition before exposing a narrated outcome as usable state. Branching restores matching conversation and experience revisions while preserving the original timeline. Rewind does not undo completed external actions. Hidden clues, character knowledge and outcomes respect visibility policy across chat, panels and generated UI. Formal rounds, contestants and scoring belong to Play.

Acceptance — Inspect a task without approving its proposed mutation; stop Research with incomplete citations; compare a product violating a Required condition; refine one translation; branch an Experience checkpoint. Verify truthful outcomes, preserved siblings/source, and unchanged external systems until the corresponding authorised operation actually succeeds.

Atlas anchors: spaces-running, spaces-research, spaces-shopping, spaces-translate, spaces-experiences.

<a id="h.besgi6e4ajgg"></a>

### 5.4.1 Message actions, branches and history corrections

<a id="h.mw5onzyslyf5"></a>

#### 5.4.1.1 Addressing and presenting messages

SPC-030 — Each action targets ConversationID, BranchID, MessageID and the selected content revision. The default assistant toolbar exposes Copy, Speak, Retry, Branch, React and Sources; the default user toolbar exposes Copy, Edit and Delete. Toolbars are independently customisable through shared user/Space preferences, with keyboard reordering and overflow. Optional Automation actions reference a registered Automation and typed input binding; their presence does not grant execution permission. Restoring toolbar defaults changes presentation only.

SPC-031 — Copy distinguishes one message, selected content and the active thread. Whole-thread copy names the active branch and its inclusion of visible tool/source context. Speak uses the selected response's actual content and the shared speech service, with stop and unavailable states. Sources exposes supporting passages/artifact revisions/URLs actually used and authorised for display; it distinguishes cited evidence from background context and offered-but-unused resources. A response with no supporting sources has an honest no-source state.

<a id="h.vzqv4offy868"></a>

#### 5.4.1.2 Exact edit and retry operations

SPC-032 — Editing a submitted user message offers three distinct operations. Replace messages below creates a corrected revision and supersedes downstream messages only in the current active branch/view while retaining their recoverable history. Update context and new messages only records a prospective correction used by future execution without rewriting already-produced answers. Edit in branch creates a new branch at the chosen point and retains the original. These choices state their effect before commit; the implementation must not hide a destructive history rewrite behind a generic Edit action.

SPC-033 — A history edit stores its original message/revision, new content revision, branch relationship, operation identity, author and affected run boundary. Use the existing revision check to detect edits from another window. Relevant in-flight work is notified that its input was superseded or prospectively corrected; completed external effects remain recorded. A default new branch is a compatible reversible choice when a caller asks for an alternative without selecting replacement. No branch operation claims to undo a sent message, purchase, publication or file mutation.

SPC-034 — Retry or regenerate creates a distinguishable new attempt using the same eligible model or another selected route and optional retry instruction. Preserve the prior answer, model provenance, usage and tool receipts. The attempt identifies its source message/branch and which earlier effects it will reuse or deliberately repeat. Reconcile uncertain effects before repetition. Resume is reserved for a supported continuation from saved execution state; an engine incapable of exact continuation labels the action as a new attempt from checkpoint.

SPC-035 — Branch can create a new compatible conversation or attach a clearly identified branch to an existing compatible destination. Preview the branch point, copied/referenced messages, sources and resulting scope. Verify that every transferred reference is eligible for the destination's participants and placement. The operation retains origin provenance and never merges unrelated histories implicitly. A broader destination does not receive private sources merely because visible message text mentions them.

<a id="h.gad02ejhidp1"></a>

#### 5.4.1.3 Deletion, reaction and attribution

SPC-036 — Delete message and Delete message and response identify the precise messages/alternative answers affected. Ordinary deletion changes visible active history under the product's recoverable revision contract; it does not implicitly erase action receipts, approvals, required retention records or source artifacts. True privacy erasure is a separate scoped storage lifecycle with truthful pending/failure status. If a message is referenced by a live run or branch, present the dependency and use an allowed tombstone/reference state instead of corrupting the record graph.

SPC-037 — Response reactions are user-attributed feedback events tied to the exact MessageID and response provenance. Their optional use for response-style preferences follows the configured retention/memory scope. A reaction is not a new grant, a fabricated follow-up answer, an instruction to infer sensitive traits or consent to disclose the thread. Reporting inappropriate content prepares a reviewable report containing the selected excerpts and necessary identifiers. Actual reporting uses the configured authenticated reporting capability and returns delivery/reference or failure; it never silently emails the whole conversation.

SPC-038 — Display-name preference can prioritise Model Name, Assistant, Specialist and eligible custom name providers. Resolve the highest-priority available presentation value and keep a fallback. A custom name cannot replace the underlying author principal, actual model/provider/route, Agent identity or Team speaker record. Store enough presentation provenance to avoid retroactively misattributing historical messages when an Assistant or model is renamed.

SPC-039 — Acceptance: produce two alternative answers, edit the original request through all three edit modes, branch one alternative and copy its thread. Verify recoverable original history, exact branch membership and no repeated side effects. Remove one selected alternative without deleting its sibling. Retry with another eligible model, then revoke a transferred source before branching into a shared destination; the branch must be narrowed or blocked explicitly while the original remains intact.

<a id="h.mwg0613113yf"></a>

### 5.4.2 Actions Space: plans, work and results

<a id="h.i7ej9lc70l7r"></a>

#### 5.4.2.1 Canonical name and entry

ACTSPACE-001 — Actions is the canonical user-facing name for the former Tasks Space. Preserve saved SpaceIDs, conversation IDs, historical links and compatibility aliases that previously targeted Tasks. Resolve a legacy route to the same Actions destination; do not clone the Space or lose its history. Atlas ScreenID spaces-running remains stable; its snapshot title “Tasks · Run summary and approval” is historical labeling. Runtime TaskID/TaskRun terminology and Present's Actions graph are separate concepts and keep their established identities.

ACTSPACE-002 — Actions opens the selected ongoing task/conversation or a purposeful start state for describing work. A work list shows authorized relevant tasks with their actual objective, latest state, waiting condition, checkpoint and output references. Default Shown Actions to Actions Space Only; All Actions additionally shows eligible work initiated through other apps/conversations. Preserve legacy saved Task Space Only/All Tasks preference aliases while using current labels. Reach the scope menu through pointer context menu and an ellipsis/keyboard equivalent. Filtering references the same TaskIDs and does not relocate tasks or grant visibility. An empty list remains empty; demo tasks and the Atlas's example three planned sessions are not production defaults.

<a id="h.21wsi5oh3815"></a>

#### 5.4.2.2 Plan revision, execution and Summary

ACTSPACE-003 — The planning record contains objective, constraints, selected sources, intended outputs, plan revision, step identities/dependencies and verification criteria where required. Proposed steps remain draft until the relevant plan/admission contract is satisfied. A plan can contain read work, draft production and consequential operations with different gates. Approval of one bounded operation does not imply authorization of every later step. Editing a plan invalidates only affected pending approvals/dependencies; it retains completed work and explains which outputs require rechecking.

ACTSPACE-004 — Summary is an optional view of the same canonical action/task graph used by the run. Its cards distinguish preparing, admitted, actively working, waiting, awaiting permission, blocked, stopped, failed and saved result states. Toggling Summary does not start, approve, pause or delete work. Opening a step shows real inputs, dependency, operation receipt and output where available. Independent authorized preparation may continue while a consequential step waits, according to the graph's dependencies.

ACTSPACE-005 — Work outcome, verification and read state are independent fields. Completed means the work reached its declared terminal outcome. Verified requires applicable checks against the relevant output revision and shows their evidence; it is not a decorative stronger completion badge. An unread checkpoint remains unread until that checkpoint is viewed or deliberately marked read. Working, waiting for permission, self-recovering stall and externally blocked states use distinct labels/icons and CUI semantic tokens, with text available without relying on colour or motion.

ACTSPACE-006 — A checkpoint records the plan/run revision, completed steps, pending operations, saved artifacts, current constraints and recovery capability. Continue resumes eligible work; steering revises future intent; Stop follows the shared cancellation contract. When an outcome is uncertain, retain the uncertainty and reconcile its action receipt before reissuing the operation. A changed external target can require a revised proposal while leaving unrelated completed steps usable.

ACTSPACE-007 — Planner changes are typed proposals linking exact burrow, dates, durations and affected events/tasks. Review opens the concrete proposed changes. Apply records actual Planner receipts; Decline rejects the proposal; Stop stops the run. Rotating the action shelf or opening Summary does none of these. A recurring task is created only through the canonical Automations/scheduler contract with a real saved rule. Completing a one-off Actions run does not create recurring work.

ACTSPACE-008 — Acceptance: open a saved legacy Tasks link into Actions, prepare a multi-step plan, complete an independent draft, hold a Planner mutation, revise its date, reject the stale approval and approve the corrected revision. Confirm real saved output, one Planner effect, surviving history and correctly separated completion/verification/read state. Stop and reopen the run at its last durable checkpoint without claiming unsupported automatic continuation.

ACTSPACE-009 — A pending agent question has a distinct inline tile action and CUI pop-out containing the actual question/choices and a short answer field. Bind its answer to the exact TaskID/RunID/pending request. Show Received when durably delivered and Applied when consumed at a safe boundary; do not conflate acknowledgment with changed execution. Editing or cancelling an unsent reply retains normal draft behaviour. A stale question identifies that its run moved on and must not deliver an answer to a different request. Question and approval affordances remain reachable even when another primary badge is visible.

ACTSPACE-010 — Default ordering prioritises genuine active state and canonical task priority, not merely last opened time. Within equal effective priority and activity, a stable identity tie-break is an implementation default that prevents refresh jitter; user-selected sorting/pins retain their declared scope. Blocked state takes primary visual precedence over an unread checkpoint without deleting that checkpoint. Viewing/acknowledging a checkpoint records its specific revision, so a later checkpoint becomes unread again. Test cross-view updates, filtering, long labels and keyboard pop-out response without changing unrelated tasks.

<a id="h.su462iw1dmtv"></a>

#### 5.4.2.3 Goals, editable plans and checkpoint workflows

ACTSPACE-011 — Preserve goal/objective, Plan and Checkpoints as usable task-workspace features, with direct routes from the corresponding task and Summary. The goal shows the intended outcome, constraints, completion/acceptance criteria, linked work, current state and remaining blockers. Reuse the canonical task/root objective and existing goal identity where provided; do not create a second unrelated goals store. A prompt transcript or compacted summary alone does not satisfy the goal/plan interface. Existing compatible editing and inspection capabilities survive the interface rebuild.

ACTSPACE-012 — The Plan view exposes the current revision, step identities, dependencies, current step, assignee/worker where applicable, state, intended output and evidence links. Expand a step into its actual actions, approval, checkpoint and result. Reordering changes presentation or dependency order only as explicitly selected; it cannot make an unmet prerequisite complete. Add/edit/remove/defer supported future steps through a revisioned plan patch, validate targets and cycles, and retain previous revisions for comparison. Completed steps and external receipts remain attributable when a future plan changes.

ACTSPACE-013 — Goal or plan editing preserves the same task when continuing the same objective. Show the changed scope/criteria and affected pending work; deliver it at a supported planning boundary with Received/Applied state. Revalidate only affected approvals, dependencies and verification evidence. A new unrelated objective creates separate work explicitly. Editing a goal does not itself start a stopped run, schedule recurring work or authorize additional sources. A version conflict retains the user's proposed change and presents the newer plan for reconciliation.

ACTSPACE-014 — Distinguish a planned checkpoint/milestone from a saved execution checkpoint. A planned checkpoint has intended deliverables and applicable validation/review criteria; editing it creates a plan revision. A saved checkpoint records what actually existed at its timestamp: task/run/plan revision, completed and pending steps, output revisions, evidence, blockers and continuation capability. Its historical execution payload is immutable; corrections/annotations and later checkpoints remain attributable. The UI must not offer an ordinary text edit that falsifies a previously saved checkpoint's contents.

ACTSPACE-015 — The checkpoint list opens a checkpoint's actual summary, outputs, validation results and remaining work, with Jump to action/event and Open artifact routes. It distinguishes saved, review-needed, verified where supported, superseded and unavailable recovery state. Viewing acknowledges that checkpoint only. Selecting historical work resolves its exact task/run/context; it cannot substitute the newest task sharing that conversation. Compare with current work before Continue/Resume, showing changed sources or grants that affect recovery. Reopening a checkpoint for inspection does not roll the app or external world backward.

ACTSPACE-016 — Pause/Stop, Resume, Retry failed work and Cancel goal/work retain their different runtime semantics. Resume uses an eligible saved continuation; retry creates a linked attempt; cancellation ends the selected unfinished scope without erasing its partial outputs or completed effects. A failed dependency marks affected steps while independent runnable steps can continue. When all remaining work requires the user, show the concrete blocker and retained goal/checkpoint; do not label the goal complete or silently start another task to conceal the blockage.

ACTSPACE-017 — A persistent goal remains open across supported scheduling slices, provider leases and process recovery until its completion criteria are met or the user explicitly stops/cancels it. Completion requires the declared outputs, confirmed mutations and applicable validation; a saved checkpoint or progress message is not completion. Display completed-but-unverified separately from verified acceptance. A changed artifact or acceptance criterion invalidates affected verification with a reason while preserving the earlier evidence. Persistence never overrides an explicit user Stop or configured hard limit.

ACTSPACE-018 — Acceptance: create a goal with three dependent steps and a planned checkpoint, edit a future dependency while work is running, and observe the applied plan revision. Save/open a checkpoint, inspect its output and Action Log evidence, fail a dependent operation, then resume/retry through the supported path without duplicating successful effects. Cancel another goal and retain its partial results. Reopen after compaction/restart and verify that goal editing, plan comparison, checkpoint inspection and recovery controls still function, rather than merely finding their text in a retained summary.

<a id="h.wzro2h8xdke9"></a>

### 5.4.3 Research Space: evidence-led plans and durable reports

<a id="h.827u3oho1tfz"></a>

#### 5.4.3.1 Define the question and admitted scope

RESEARCH-001 — Research combines the ordinary conversation with typed question, plan, source scope, budget and deliverable records. A request can begin with natural language, but the admitted plan records the actual question, relevant constraints, included/excluded topics, supplied sources, permitted outside research and output target. Missing details that materially prevent research are resolved in the conversation; ordinary reversible research choices can be made and shown. Do not require a ceremonial approval of every read step when the user has already authorized that scope.

RESEARCH-002 — Quick, Extended, Deep and Custom are inspectable effort/budget policies. Retain the documented initial active-execution targets: Quick approximately 5–10 minutes, Extended approximately 15–20 minutes and Deep up to approximately one hour per research turn. Custom accepts a user-defined active-execution budget with a requested upper bound of 28 days per turn. All are bounded by actual runtime/account capacity, host availability and current authorization. These are resource targets/limits, not minimum delays, promised minutes or guaranteed completeness. Finish legitimate work sooner when possible. Waiting/paused time does not consume productive active time. Each follow-up turn has its own admitted scope rather than silently extending the original budget.

RESEARCH-003 — Supplied files and outside sources are separate collections. The plan specifies which source IDs/revisions may be used, whether authorized web retrieval is allowed and any date/language/domain constraints. A link found inside a supplied file is evidence of a reference, not automatic authority to access a private account or broaden source scope. If required evidence cannot be retrieved, mark the gap and offer an allowed alternative or narrower result. Never replace missing supplied evidence with unlabeled model recollection.

<a id="h.5ygw9zmch351"></a>

#### 5.4.3.2 Source ledger, findings and disagreement

RESEARCH-004 — The source ledger records stable source/reference identity, accessible URL or artifact reference, selected revision/snapshot, retrieval time, source type and evidence locations used. A finding links its claim to supporting evidence, contrary evidence and uncertainty. Quotes must be attributable to inspected content; inferred conclusions are labelled as inference. Deduplicate repeated retrieval of the same source while preserving distinct versions and changes that affect a conclusion. An inaccessible source title cannot serve as proof of its unseen contents.

RESEARCH-005 — Findings remain provisional until integrated into the declared report revision. Where sources disagree, preserve the disagreement, relevant dates and basis for preference rather than averaging incompatible claims into false certainty. A search query returning no useful result is a recorded coverage gap, not proof that no relevant evidence exists. Source count, elapsed time and token volume are process metrics; they do not by themselves establish research quality or verification.

RESEARCH-006 — The work view shows actual planned, running, completed, blocked and skipped research steps. A skipped step identifies its reason and effect on coverage. Replanning preserves the prior plan and evidence already collected. Stop saves eligible findings and marks unfinished coverage; Resume restores a supported checkpoint or begins an explicit new attempt using retained evidence. Waiting for access or a user decision is distinguishable from active research time.

<a id="h.ewdzf0ppnb5i"></a>

#### 5.4.3.3 Report, export and reuse

RESEARCH-007 — A completed report is a canonical Write artifact with title, report revision, linked source ledger, citations, limitations and output/run provenance. The conversation may preview it, but Edit/Open in Write addresses the same object. Partial research can be saved as an explicitly incomplete report or notes; it must not acquire a completed badge because a document exists. Updates produce new report revisions while retaining the source snapshot that supported each earlier claim.

RESEARCH-008 — Export preserves source references and material evidence gaps in the chosen format's supported representation. A format unable to express interactive citation detail receives usable footnotes, links or an accompanying reference section. Export retry targets the saved report revision and follows OUTSP-006. Publishing or sending the report is a separate action with its own destination and authorization; saving it internally does not imply that it has been sent.

RESEARCH-009 — Acceptance: research one supplied document plus an explicitly allowed external source, encounter one inaccessible required reference and one conflicting claim, then stop before all coverage completes. Preserve the evidence ledger and incomplete status. Resume, create a real Write report, export it and inspect source links and limitations. Revoke one source and verify that subsequent access, generated surfaces and reuse respect the changed grant while maintaining permitted audit attribution.

RESEARCH-010 — A new Research conversation begins in the ordinary conversational surface. After the initial request, open the richer workspace in the same ConversationID with Plan selected and a real reviewable generated plan. Provide Conversation, Sources, Plan, Findings and Report sections, with Outputs where relevant. The bottom composer remains usable for context, edits and follow-ups. Selecting Conversation opens the full existing thread and returning restores the workspace; neither creates a disconnected chat/task or clears work.

RESEARCH-011 — Before substantive execution, show the plan, chosen budget and deliverable choice: Report or Report & Artifacts. The latter can include actual code, datasets, experiments, notes or app-native outputs required by the agreed objective and supported tools. Start admits the selected plan revision and choices. Initial plan generation is not a final research report. Changing a running plan uses explicit steering/revision; preserve accepted outputs and identify changed coverage. The review/start step is the defined Research workflow, not a new approval gate on each already-authorized read or tool call.

<a id="h.t5gdiqu1vnp4"></a>

### 5.4.4 Shopping Space: requirements, offers and comparisons

<a id="h.k9uc2fdhst9y"></a>

#### 5.4.4.1 Requirements and result classes

SHOP-001 — A shopping request records item/category, intended use and structured constraints such as budget/currency, variant, dimensions, compatibility or delivery location where supplied. Each constraint retains its value/unit, Required or Preferred status and provenance from the user's request or explicit later change. The assistant may propose a clarification or tradeoff; it cannot silently relax a required constraint to manufacture results. Changing requirements re-evaluates existing candidates and records why their classification changed.

SHOP-002 — Strict matches satisfy every applicable required constraint with sufficient evidence. Partial/similar results clearly list unmet or unknown requirements. Contextual suggestions explain their relationship and remain separate from strict results; sponsored/promoted or adjacent items cannot displace the requested class without disclosure. A product with unknown compatibility is not labelled a verified match. When no strict match exists, show that state and the useful near-match evidence rather than relabelling near matches as exact.

<a id="h.7shlq9hwsre8"></a>

#### 5.4.4.2 Product and offer identity

SHOP-003 — Distinguish a product/model identity from a merchant offer and its exact variant. An offer records merchant/source, variant, price/currency, known shipping/tax terms, availability, retrieval/last-check time and relevant restrictions. Unknown shipping is not free shipping; unknown availability is not in stock. Total price is shown only for the components and destination actually known. Currency conversion, when offered, includes its actual rate/time/source and is labelled an estimate distinct from the merchant's transaction currency.

SHOP-004 — A shortlist contains references to selected products/offers and the saved requirement revision. Comparison uses consistent dimensions and units while preserving unknown values. Opening a saved shortlist shows the checked snapshot and whether refresh is needed; it does not turn an old price into a current quote. Refresh replaces current offer evidence through a new attributable observation, retains the earlier selection and re-evaluates material changes such as price, variant or availability.

<a id="h.wjbbfwcsxe29"></a>

#### 5.4.4.3 Save, compare and purchase boundaries

SHOP-005 — Save, Compare and Purchase are distinct operations. Save persists the shortlist or permitted artifact. Compare changes a view or creates a comparison artifact. Purchase opens the actual supported checkout/handoff for the selected merchant, variant, quantity and current terms. It requires the applicable explicit authorization for the concrete transaction. A generated “Buy” card must not imply that checkout integration exists; unsupported purchasing offers a truthful source link or handoff without a fake success receipt.

SHOP-006 — Before a consequential checkout step, revalidate the selected offer where supported and surface material changes. An uncertain transaction response is reconciled using its operation/merchant reference before retry. If a source fails, preserve requirements, shortlisted references and checked evidence with timestamps; retry retrieval for the affected source. Offline cached comparisons remain labelled as saved observations. Do not invent current products, live review counts, discounts or availability to fill the Atlas composition.

SHOP-007 — Acceptance: enter a required compatibility constraint and a preferred colour, retrieve a strict match and a cheaper incompatible offer, and verify their separate classes/reasons. Save and reopen the shortlist, change one requirement and encounter a stale price. Confirm honest reclassification and refresh, accessible comparison rows, preserved unknown fields and no purchase from Save/Compare. A checkout error must remain uncertain/failed until the real destination proves its outcome.

SHOP-008 — Initial conversation transitions after the shopping request into a dedicated workspace in the same ConversationID, with real search, applicable filters, results, offer inspection, comparison and saved items. Keep the floating composer available. Price range, features, delivery time, shipping cost, brand and category-specific controls edit the same canonical requirement revision as natural-language refinements. A proposed interpretation is corrected before it hardens a preference into a requirement. Filters/counts/results re-evaluate from real data, retain focus and support undo/revert where applicable. Current Atlas labels Strict matches and Partial / similar take precedence over historical Direct Results/Similar Results wording; contextual suggestions retain their separate semantics.

SHOP-009 — Support physical and digital categories when eligible sources provide them. Software/apps/games use relevant grounded fields such as platform, edition, licence/subscription model, regional eligibility and digital fulfilment. Physical shipping fields become Not applicable or an actual activation/delivery term for digital offers. Distinguish an external purchasable listing from an already owned/installed app or entitlement. Discovery never silently purchases, installs, downloads, subscribes or activates. Preserve unknown metadata rather than inventing compatibility or keys.

SHOP-010 — Home's selected display currency governs comparable price presentation, ranges, known shipping and totals. Preserve original merchant amounts and checkout currency for inspection. Convert only with an identifiable valid rate and time; stale/missing conversion produces a labelled estimate or unavailable comparable amount, not a false current price. Recompute filters and saved-offer projections consistently after currency changes without mutating merchant offer identity. Optional price watches use actual available price observations and the canonical Automation scheduler; the presence of a saved offer alone does not create monitoring.

<a id="h.yg4gfhcmvh8"></a>

### 5.4.5 Translate Space and shared translation jobs

<a id="h.vlz0gamhmsk5"></a>

#### 5.4.5.1 Conversation, language selection and translation identity

TRANS-001 — Translate is a built-in Space using the shared Dulche Translate capability. Write, Boards, Browse, Connect (Mail), Voice & Vision and other authorized consumers may use that capability in their own context; they do not require a separate Translate application or duplicate model/credential store. The Space uses the ordinary conversation. Persistent target-language chips sit immediately above the standard composer, with sources selected through shared context controls and no additional competing Translate context row.

TRANS-002 — A TranslationSet identifies its source message or artifact/reference and revision, source snapshot where permitted, source language/locale, instructions, glossary versions, model policy, created/modified times and sibling variants. Each Variant has stable identity, target language/locale, status, content or canonical output reference, revision, provenance, warnings/errors and segment mapping where available. Distinct target languages are independent outputs of one set; one failed target does not invalidate successful siblings or the source.

TRANS-003 — Use canonical language/locale identifiers. An explicit valid user choice overrides a detection suggestion. Ambiguous or unsupported labels produce a specific resolution requirement showing eligible choices; silently substituting a different language fails the contract. Detection returns its actual result and uncertainty where supplied. A target selector may advertise supported languages only within the actual route/capability policy, and unavailable combinations explain whether the constraint is language, model, modality or disclosure policy.

TRANS-004 — A submitted source can target the persistent set, a one-off subset or additional languages. A temporary request such as “only French this time” affects that request; an explicit “make French my default” updates the persistent configuration. Chip changes persist with the conversation/draft and affect future submission. Removing a language from the future set does not delete its earlier variant history. Each returned language appears in a separate assistant bubble with its language/locale label directly above and a link to the shared set.

<a id="h.b9yqk8mlon5l"></a>

#### 5.4.5.2 Refinement, protected terms and independent variants

TRANS-005 — Follow-up instructions resolve their target unambiguously: “make the German less formal” revises German; “keep ActionGraph unchanged” applies the specified protected-term rule; “add Japanese” adds a sibling. Retry, regenerate, manual edit, apply and export target a specific variant/revision. Preserve successful siblings and prior revisions. If the instruction could affect several variants, present the inferred target in the action or resolve the material ambiguity before replacing outputs.

TRANS-006 — A glossary/protected-term entry contains source term, target term or do-not-translate rule, applicable locale, required/preferred treatment, notes and scope/version. The job records which glossary revision it used. Conflicting required rules become a visible validation issue rather than a silent arbitrary choice. Manual edits are attributable variant revisions. A later automated pass cannot silently replace protected manual segments; it must preserve them or present the overlapping changes for review.

<a id="h.o2y876b14zuu"></a>

#### 5.4.5.3 Structured artifacts and segment alignment

TRANS-007 — Structured translation uses the owning app's semantic content. Write retains meaningful formatting, tables and links; Present retains objects, notes and supported actions while checking text fit; Data distinguishes human-readable labels/text from formulas, numbers and IDs; Boards retains block identity and relationships. Code, commands, URLs, schema keys and markup structure are protected unless the requested typed operation explicitly identifies translatable string/comment content. A visually plausible rendering with corrupted formulas or links is a failed translation.

TRANS-008 — Whole-artifact translation normally creates a derived language variant referencing the source revision. Replacing a selected range or publishing into an existing artifact is an explicit typed owner operation with a current target revision, reviewed effect and undo/receipt where supported. Translation completion alone never replaces the original. The preview shows scope and material formatting/layout warnings before application, including overflow or unsupported structures that require correction.

TRANS-009 — Segment identities connect source units to target units and their revisions. A source edit marks affected variants or segments stale; unchanged segments remain reusable. Incremental retranslation identifies the changed source revision, preserves unaffected manual edits and reports conflicting overlap. The user can inspect what is current, manually amended, stale or missing. When alignment is unavailable, say so and require a coarser replacement/review operation instead of pretending safe per-segment merging is supported.

<a id="h.7019ks777zhx"></a>

#### 5.4.5.4 Modality, execution and recovery

TRANS-010 — Translation observes source disclosure policy. A local-only source cannot silently fall back to a cloud translator. OCR, PDF extraction, audio transcription and live interpretation are eligible only through available supported capabilities. Extraction uncertainty, omitted material and layout loss remain associated with the translated result. Voice & Vision uses the same translation identity/provenance and current capture permissions; opening the Translate Space does not activate a microphone or camera.

TRANS-011 — A durable job distinguishes Preparing, Translating, Review available, Applying, Exporting, Completed, Paused, Failed and Cancelled where those phases occur. Progress reflects actual units/variants known to the worker; percentage is absent when no meaningful denominator exists. Persist completed variant/segment results so a single target failure can retry independently. Cancellation stops eligible unfinished work without deleting already saved variants. Language resolution, glossary conflict, missing source, revoked disclosure, segment conflict, unsupported format and provider failure have distinct recovery explanations.

TRANS-012 — Reopening a conversation restores its language chips and TranslationSet references. Apply/Publish records the real owner/destination operation and resulting revision. Export follows the exact selected variant/revision, retains format/destination on failure and never relaunches successful siblings. A stale source can still have a valid historical translation, labelled with its source revision; it is not presented as an up-to-date translation of changed content.

TRANS-013 — Acceptance: translate one source into French and German, fail German, then retry only German. Refine its formality, add Japanese, manually edit one aligned segment and change a source paragraph. Confirm independent history, exact target labels, stale-segment handling and preserved unaffected edits. Translate an authorized structured document, inspect protected identifiers/layout warnings and explicitly apply a selected revision. Deny a cloud fallback for a local-only source without discarding the draft or successful local outputs.

<a id="h.fdf49qtgp7b7"></a>

### 5.4.6 Experiences Space: story state and recoverable branches

<a id="h.9kpk78fjviu0"></a>

#### 5.4.6.1 Canonical state and action transitions

EXP-001 — Experiences provides open-ended stories, roleplay, simulations and mysteries within a normal Space conversation. Formal scored rounds and competitive game rules remain owned by Play where applicable. An Experience record has stable identity, branch, state revision, current scene, participant/character references, inventory/evidence/objectives/flags, visibility policy and checkpoint/run references as relevant to its scenario. Panels project this state; their values are not a separate untracked copy of the prose.

EXP-002 — A user action resolves against current state and permitted knowledge. Validate preconditions, targets, ownership and scenario rules; propose the typed state transition; commit against the expected revision; then present the resulting usable state. Creative prose may stream as provisional narration, but an inventory item, clue, objective completion or outcome becomes usable only after its canonical transition succeeds. If a concurrent action invalidates the proposal, reconcile against current state and explain the correction instead of retaining contradictory panel values.

EXP-003 — Inventory/evidence entries have stable identities, relevant quantity/location/ownership and visibility. Removing or transferring an item checks the current state; a renamed item retains identity. Character knowledge and user-visible knowledge are distinct where the scenario requires it. Hidden clues, solutions and private character state must not leak through generated UI, accessibility labels, source summaries or unrelated context retrieval. A permitted reveal is itself an attributable state transition.

<a id="h.ge1wh9m7d0g7"></a>

#### 5.4.6.2 Checkpoints, rewind and scope

EXP-004 — A checkpoint binds a conversation/branch position to the matching Experience state revision and relevant scenario version. Fork creates a new branch with that consistent pair while preserving the original. Rewind selects a prior supported checkpoint and makes its scope explicit. It does not reverse a real external message, file publication, payment or other external effect performed outside the story state; those receipts remain attached and visible. A narrative “undo” cannot claim a nonexistent external reversal.

EXP-005 — Scenario configuration identifies allowed resources, generated media capabilities, participant roles and content/state rules. Changing it creates a revision and states which future actions it affects. External retrieval or real app actions remain ordinary permission-bound operations; a fictional character's request does not authorize them. Missing image/audio capability leaves the text/state experience usable where possible, with an honest unavailable control rather than fabricated generated media.

EXP-006 — Recovery restores the last committed state and corresponding conversation checkpoint. A failed transition retains the user's input and any useful provisional narration labelled appropriately, then offers retry or correction against the current revision. Cancel before commit leaves state unchanged; cancel after commit retains the receipt and offers a legitimate state-level undo if supported. A reconnection cannot replay a committed action simply because the final narrative response was lost.

EXP-007 — Acceptance: begin a mystery, acquire a clue through a valid action, reject an invalid inventory use, reveal only eligible information and create a branch at a checkpoint. Confirm original and branch histories remain consistent with their own states. Disconnect between state commit and narrative delivery, then recover without duplicating the item or action. Verify hidden data stays hidden across panels, keyboard navigation and generated surface inspection.

Source implementation evidence — At the pinned commit, 9to1 Workspace/Spaces/UI/Tasks/[SpaceTasksDashboard.cui](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/9to1%20Workspace/Spaces/UI/Tasks/SpaceTasksDashboard.cui) declares task opening, scope/page controls, checkpoint/details and central permission review. 9to1 Workspace/Spaces/Source/Tasks/[SpaceTaskWorkspaceService.cs](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/9to1%20Workspace/Spaces/Source/Tasks/SpaceTaskWorkspaceService.cs) contains ReadAllTasksPageAsync, ReadSelectionAsync, ReadExactTaskContextAsync, RespondPermissionReviewAsync and SubmitFollowUpAsync over the canonical coordinator. These inspected paths support preservation of exact historical selection, revision checks and review/steering. They do not prove that every newly detailed Action Log filter, goal editor or continuation journey has passed runtime acceptance.

<a id="h.6hvtwk5hydm1"></a>

## 5.5 Study Space

Study is a specialised Space with subject, topic, source and learning-session context. Its dashboard and rich learning views remain part of Spaces. Decks, Forms, Write and Planner own the reusable artifacts that Study references; entering Study never creates a second assessment or document engine.

APP-STUDY-001 — Study Home, subject workspace and session are distinct views in the same Space. Subjects, topics, papers/specification mappings and resources retain canonical IDs. Navigation includes New Chat, Subjects, My Progress, Planner, Revision Bank, recent chats and at most three Revise These recommendations across all subjects. Recommendations state evidence and review reasons. Use deterministic ordering over eligible recorded difficulty/review data; do not manufacture weak topics to fill three slots.

APP-STUDY-002 — Learn, Practise, Test and Review change the next request while preserving conversation, topic, selected sources, history and answer draft. Practise records only a submitted attempt, including question/source, working, feedback, marking provenance and hint conditions. Test uses the canonical Forms Test/Quiz engine with inspectable rubric/rules; selecting the mode alone starts no timer. Learn examples, page opens, time spent, streaks and card flips are not assessed evidence.

APP-STUDY-003 — Demonstrated proficiency is Not assessed, Needs work, Developing or Confident and derives from attributed evidence. Self-reported confidence is separate. Topic details expose evidence and last/next review; labels and ranking must not claim precision absent a defined policy. Evidence weights, thresholds and review intervals remain an explicit unresolved learning-policy decision, not arbitrary implementation constants.

APP-STUDY-004 — My sources only uses the selected closed corpus; Wider knowledge allowed distinguishes supplied material, authorised external research and general explanation. Policy switches preserve earlier provenance and do not broaden web/account permissions. Revision Bank categories are Recommended, All, Flashcards, Notes, Papers and Quizzes with Create New. Resources open canonical Decks, Forms and Write artifacts; interaction/view is default and editing explicit. Planner opens the same Study burrow.

Acceptance — Switch Study modes while drafting, submit one answer and trace its evidence, then open the same Decks/Forms/Write artifact in its owning app. Mobile Overview/Subjects/Progress/Bank keeps Planner reachable and the composer clear of the keyboard. Navigation and practice remain in the same configured Space and conversation unless an explicit new conversation is created.

Atlas anchors: spaces-study, study-subject, study-topic, study-session, mobile-study. Study's canonical ownership is Spaces, as specified in this document.

<a id="h.e3oi22bszf4"></a>

### 5.5.1 Study Space: planning, learning, practice and review

<a id="h.5v7acn4g5hq0"></a>

#### 5.5.1.1 Ownership, logical records and first entry

STUDY-001 — Study is a first-party Space, with Study Home, Subject Workspace and Study Session as distinct active views under the same SpaceID. It supports formal courses and general learning without assuming a particular subject, school, qualification or exam board. The published APP-STUDY-001 through APP-STUDY-004 remain valid and are expanded here. Spaces owns learning orchestration and contextual views; Dulche owns inference/retrieval; Decks owns flashcards and personal ReviewRecords; Forms owns assessments/submissions/results; Planner owns schedules; Write and the other artifact apps own their materials. There is no standalone Study package, private Study model runner, second flashcard database or separate calendar.

STUDY-002 — The logical records below retain canonical references and relevant revisions. They may be implemented through compatible existing models rather than literal new database tables.

| Column 1 | Column 2 |
| --- | --- |
| Record | Required meaning and relationships |
| Subject | Stable SubjectID, name, optional course/specification metadata, owner/scope and topic roots. |
| Topic/concept | Stable TopicID, SubjectID, parent/order, source-backed coverage mapping and linked resource references. |
| Study session | ConversationID, active SubjectID/TopicID, mode, source policy, selected sources and transitions. |
| Question/attempt | Question identity/revision and owner, learner, submitted answer/working revision, conditions, marking status and result provenance. |
| Evidence | Attributed eligible activity, learner/TopicID, source artifact/question/attempt IDs, time, conditions, result and correction/retraction history. |
| Personal topic state | Evidence references, policy version, displayed proficiency, last eligible review and next suggested review when defined. |
| Bank entry | Catalogue identity, canonical artifact/reference, category, subject/topic links and permitted metadata. |
| Planned study work | Canonical BurrowID and Planner event/task/assignment ID, topic links and current scheduling state. |

STUDY-003 — On entering Study, restore an eligible active session/workspace where one exists. Otherwise show Study Home; explicit Home navigation also opens it. Home shows genuine recommended topics, upcoming authorized work, resumable conversations/resources and useful new-session/subject actions. It must not render a dormant focusable editor beneath a dashboard or require setup forms before an ordinary learning question. First use shows empty recommendations and unassessed progress until real records exist. Create subject is available without fabricating a course, grades or due dates.

STUDY-004 — Subject creation accepts a name and optional course/specification reference. Importing a syllabus or topic structure previews the proposed hierarchy, source/version and mapping before committing it. User-defined topics remain clearly user-defined; only an inspected trusted reference can support a claim of official specification coverage. Preserve external source identifiers and version alongside local SubjectID/TopicID. A later syllabus revision creates an inspectable mapping update; it cannot silently reclassify earlier attempts as evidence for newly added content.

<a id="h.x5lwe53q12ot"></a>

#### 5.5.1.2 Subject Workspace and navigation

STUDY-005 — A Subject Workspace shows its actual course metadata, searchable/filterable topic tree, coverage, status, sources and resource links. Selecting a topic exposes its overview, relevant Learn/Practise/Test/Review entry points, evidence, last/next review and materials. Rename/reorder retains TopicID and every attempt, Bank and Planner link. Moving a topic preserves identity and updates its hierarchy with an attributable revision. Removing a topic from a displayed syllabus cannot silently delete learner attempts or shared resources; archive/unlink and destructive deletion remain separate operations.

STUDY-006 — The default contextual sidebar is New Chat; Subjects; Study Tools containing My Progress, Planner and Revision Bank; Revise These; Recent Chats. Eligible customization follows Space Override > User Preference > Default, while supported essential routes remain discoverable. Subjects opens a substantive subject selection/workspace, not merely a list of arbitrary folders. Collapsing the sidebar retains the Space rail and restore control. The active subject/topic remains identifiable in the work context without duplicate global headers. The current and pending Study activity/source choices are inspectable in the composer Options panel; a separate always-visible control row is not required.

STUDY-007 — Opening a topic starts or resumes a genuine topic-bound conversation with authorized linked sources. A recommendation routes using TopicID and reason/evidence references, so duplicate topic names across subjects are unambiguous. A source or resource requiring access opens its truthful unavailable/request-access state; it does not leak content through its preview. Switching Home, Subject, Session, Bank or Planner preserves conversation draft, answer draft, request/run and relevant reading anchor. A background assessment remains associated with its real session rather than following whichever topic is currently visible.

<a id="h.yn5vpvigz5wd"></a>

#### 5.5.1.3 Modes, request context and source policy

STUDY-008 — Learn, Practise, Test and Review are compact accessible controls within the standard composer Options panel, alongside a separate source-policy group. They remain Study activities, distinct from Natural Conversation response-presentation modes; moving them into Options does not change their handlers, state owner or session semantics. Switching changes the next applicable request/activity while preserving ConversationID, history, active TopicID, source selection and unsent answer. Record the transition with previous/new mode, conversation and effective point. A mode switch during an admitted request does not rewrite that request's conditions or relabel its earlier answer. Show pending next-request selection clearly if an existing activity remains active.

STUDY-009 — Every submitted study request references its active subject/topic, mode, source policy and admitted source snapshot. These contextual fields are not merely UI labels: they determine eligible workflow, retrieval and result handling. A normal follow-up such as “why?”, “use another example” or “show my mistake” can remain in the same conversation and refer to the current attempt. The learner can change difficulty, ask a freeform question or inspect a source without being forced to finish a generated quiz first.

STUDY-010 — My sources only is an enforced closed-corpus answer policy over selected authorized material. It blocks disallowed outside retrieval and requires the response to identify insufficient support instead of inventing a citation. Ordinary language generation is not a claim that the selected material establishes a fact. Wider knowledge allowed can use eligible supplied material, authorized external research and general explanation, visibly distinguishing those origins. The latter mode does not itself grant web access or a connected account. Switching policy affects future work and preserves the provenance of earlier answers/attempts.

STUDY-011 — Source selection identifies artifact/file revision and relevant scope. An unready upload cannot be treated as read; an unreadable scan explains extraction limitations. Removing an attachment changes future eligible context and follows its source retention rules, rather than rewriting earlier attribution. When an important source is revoked mid-activity, hold the affected retrieval/marking operation, preserve the user's answer and explain the missing source. Offer allowed work with remaining material only if that change is clearly stated and does not pretend the original marking basis still applies.

<a id="h.jtt0xohf840c"></a>

#### 5.5.1.4 Learn and Practise workflows

STUDY-012 — Learn supplies conversational explanation, source-backed examples and supported subject-specific objects. Optional checks may establish understanding only after a real eligible answer is submitted. Reading an explanation, opening a source, watching time pass or viewing a worked solution does not create assessed evidence. Learner-requested scaffolding can reveal steps progressively while retaining whether a subsequent answer was assisted. A requested easier/harder explanation changes teaching approach without silently changing an existing assessment's rubric.

STUDY-013 — Practise creates or selects a question with stable identity/revision, TopicID, question content, response type, available hints and evaluation basis. Question content can be generated or sourced, but its provenance must be known; a generated item cannot be presented as an official past-paper question. Supported inputs include structured mathematical working, text, code, choices, graphs or media through shared controls. A question displayed without submission is unanswered. Saving an answer draft is distinct from submitting an attempt.

STUDY-014 — Attempt states are Draft, Submitted, Awaiting evaluation, Evaluated and, when needed, Needs review or Evaluation failed. Submission freezes the answer/working revision and relevant conditions: hints used, open-book/source access, timing where meaningful and question/rubric revision. Keep an operation identity so a reconnect or repeated click cannot record the same attempt twice. Later editing creates a new draft or retry attempt linked to the original; it does not rewrite the answer that received an earlier mark.

STUDY-015 — Evaluation uses an available deterministic validator where suitable and authorized, or an identified qualitative marking process with its rubric/source basis. Feedback should point to observed working and distinguish supported error types such as a conceptual mistake, calculation error, missing step or inconsistent reasoning. If the evaluator lacks enough evidence, report that limitation. An unavailable marker leaves the attempt awaiting review/evaluation; it cannot produce an optimistic score, green topic or completed evaluation badge.

STUDY-016 — Hint use and partial feedback remain in the attempt's conditions. A correct answer after a disclosed solution is not relabelled unaided success. The learner can inspect the rubric, ask about a disputed mark and submit a correction/review request. Correcting an evaluation creates a superseding attributed result, preserving the earlier result and reason for change. Topic evidence then recomputes under the applicable policy. Retrying an exercise preserves all prior attempts and supports comparison without presenting practice repeats as independent formal assessments.

<a id="h.x7nj4qnzr0xs"></a>

#### 5.5.1.5 Test and canonical Forms assessment

STUDY-017 — Selecting Test mode prepares its workflow; it does not start a timer. A real Test opens or creates a Forms assessment with canonical assessment/form revision, question set, marking rubric, allowed aids, attempt rules and timing policy. The pre-start view states these conditions and any unavailable required capability. Starting explicitly creates the learner's supported assessment attempt. Study retains context links; Forms remains authoritative for answers, submission, marks and results.

STUDY-018 — Timed assessment uses its declared authoritative timing policy, including start, allowed pauses, deadline handling and reconnection. The UI must not secretly convert an untimed exercise into timed work. Navigation away does not silently pause a timer when the rules say it continues; show that consequence before relevant navigation where needed. Interrupted responses recover through Forms' real draft/submission state. A submitted assessment reopened from Study and Forms must show the same attempt and results, without duplicate submission or locally invented marks.

STUDY-019 — Objective and qualitative results identify their actual evaluation basis. A percentage requires a defined obtainable mark and scored result; an official grade requires a justified applicable boundary/qualification basis. Free-response AI feedback shows uncertainty and a correction/review route. A polished result card is not examiner validation. Study consumes eligible result evidence by canonical ID and revision; later Forms corrections update the personal evidence projection without deleting the original assessment history.

<a id="h.mtu7irgt2w5"></a>

#### 5.5.1.6 Review, proficiency and recommendation rules

STUDY-020 — Review can target prior errors, recorded difficulty, due topics or eligible Decks decks. The learner can inspect why the topic is suggested and choose another focus. Opening notes, viewing a card's front/back or navigating a review queue is not evidence of successful recall. A real submitted answer, supported Decks rating or other permitted attributed review activity can create evidence. Completion updates last/next review only through the documented eligible-event policy.

STUDY-021 — Demonstrated proficiency has four displayed states: Not assessed, Needs work, Developing and Confident. It derives from eligible evidence and the versioned learning policy; it is not a claim of fixed ability or scientifically exact mastery. Keep self-reported confidence, scores, recency, time/streak engagement and demonstrated proficiency as different fields. “I feel confident” can update the subjective field but cannot itself establish repeated assessed success. Unassessed topics must remain visibly neutral rather than being assumed weak or green.

STUDY-022 — An evidence record identifies learner, TopicID, event type, canonical attempt/artifact/question/review ID and revision, timestamp, result, conditions, evaluation provenance and any correction/retraction. The deduplication key corresponds to the source event, preventing one Forms result from being counted again each time its page opens. Shared decks and papers have shared content identity but each learner has private review/evidence records. Sharing a resource or Space does not expose another learner's marks, answers or confidence.

STUDY-023 — Topic details explain the current label using its contributing evidence and policy revision. When evidence is corrected, retracted or becomes inapplicable to a changed topic mapping, recompute the derived state and show the reason for material change. Preserve raw evidence even when an aggregate cannot yet be calculated. Existing APP-STUDY-003 leaves exact weights, threshold values and review intervals as a narrow learning-policy decision; this expansion does not invent them. An unavailable policy blocks only unsupported calculated labels/dates, while teaching, source access, attempt capture, raw results and manually planned work remain usable.

STUDY-024 — Revise These contains at most three distinct eligible topics across all subjects combined. Apply the retained deterministic priority: recorded difficulty first, then overdue/oldest-reviewed eligible topics, with stable TopicID tie-breaking. Show a short reason and subject/topic context. Use actual review dates; absent dates do not create overdue warnings. Do not pad the list by classifying untouched topics as weak. AI suggestions can be separately labelled supplementary suggestions but cannot overwrite the authoritative evidence-based ordering. Recompute after eligible evidence or schedule changes using a recorded policy version.

STUDY-025 — Recommendations and goals remain editable/dismissible under normal permissions. Dismissing a recommendation changes presentation/preference, not the underlying attempt result. A suggested review date is distinct from a scheduled Planner event and from an externally imposed assignment deadline. Inspecting a recommendation or scheduling it does not constitute learning completion. Progress summaries disclose their coverage, such as assessed versus total mapped topics, rather than claiming full-subject mastery from a small selection of attempted questions.

<a id="h.vt55uw6ejqy9"></a>

#### 5.5.1.7 Revision Bank and canonical app handoffs

STUDY-026 — Revision Bank is a catalogue of authorized resources, not another file store. Default category filters are Recommended, All, Flashcards, Notes, Papers and Quizzes. Custom sections such as Homework may be added through stable category identities. Search/filter/sort uses appropriate subject, topic, type, date/status and source metadata; no-results, loading and unavailable states are distinct. Recommended uses eligible topic evidence and accessible linked materials; it cannot reveal a private artifact by showing an unauthorized recommendation preview.

STUDY-027 — Create New remains on the category row and offers Describe a Resource, Flashcard, Quiz, Document, Presentation, Canvas, Board and Other. Map these to the real owner services: Decks, Forms, Write, Present, Canvas, Boards and the supported Develop creation route respectively. Describe a Resource resolves the intended type and reviewable content before creation. Only a successful canonical creation receipt adds the Bank entry. A failed or cancelled create retains the useful draft and creates no empty phantom deck/document. “Other” does not imply arbitrary executable content is safe to load.

STUDY-028 — Enrol existing material by an authorized canonical reference, retaining its owner and revision/current-version policy. Removing it from Bank deletes only the catalogue link. Source deletion is an explicit owner operation. Rename/move follows identity rather than title/path where the source supports it; revoked or missing sources show a retained unavailable reference only when permissible. Re-enrolling the same object should resolve or explain duplicate catalogue membership rather than silently duplicate its content.

STUDY-029 — Resource activation opens the real owner viewer/interactor in an eligible embedded tab, normally in view/interact mode. Edit and Open in App are explicit, accessible actions sharing exact ArtifactID, revision and permission state. A quiz remains a Forms assessment and a deck remains Decks content. If embedding is unsupported, use a truthful owning-app route and preserve return context. Avoid a simulated local viewer whose edits never reach the canonical artifact.

STUDY-030 — Lightweight Decks creation/review uses the existing Decks engine: clear front/back, next, flip and rate actions, with actual per-user ReviewRecords. Rich formatting, drawing, graphs or bulk edits open the real editor. Ratings/answered recall that satisfy the Study evidence policy link to TopicID and their original Decks event. A flip alone never becomes a successful answer. Editing a shared card does not rewrite earlier learners' recorded answer conditions or leak their review history.

<a id="h.rm3404qgcxfp"></a>

#### 5.5.1.8 Planner, adapted inputs and accessibility

STUDY-031 — Planner opens the one canonical Study burrow with the same BurrowID, event/task/assignment identities, reminders, recurrence and permissions in both Study and standalone Planner. Upcoming work on Study Home is a projection of those records. Creating or rescheduling a topic review specifies the actual event/task, date/time/timezone, relevant recurrence changes and linked SubjectID/TopicID. Apply through the canonical Planner action and report its receipt; suggesting a date does not create an event.

STUDY-032 — Do not move unrelated events into the Study burrow or infer external attendees, sharing or calendar publication from a study recommendation. User-authorized scheduling within existing scope can proceed without redundant confirmation, while a materially changed destination/effect follows the normal review contract. Opening a planned session routes back to its topic-bound work. Changes made in Planner update Study's projections without a second calendar synchronization loop or duplicate reminders.

STUDY-033 — Subject-adapted objects use shared editable controls: mathematical equations/working and graphs, syntax-aware code input with authorized real execution where supported, and structured or freeform essay reasoning with criteria-based feedback. Course examples are illustrative. A code-run button must execute through an actual allowed development/tool capability or be truthfully unavailable; static example output is not test evidence. Mathematical/qualitative evaluation provenance remains attached to the attempt, and unsupported object input preserves a usable permitted fallback where meaningful.

STUDY-034 — Keyboard users can select subject/topic/mode, inspect sources, enter and submit an answer, request a hint, review results and open the owning resource without pointer-only interactions. Selecting a mode returns focus predictably without sending the draft. Assessment submission is distinguishable from newline/formatting keys and respects IME composition. Topic states have text/icons in addition to colour. Screen-reader announcements communicate question/evaluation state without reading private hidden feedback prematurely. Narrow/touch layouts retain navigation and composer access through safe areas and the on-screen keyboard.

STUDY-035 — Home, Subject and Session remain separate active views, not a long stack of simultaneously active dashboards/editors. Theme, reduced motion, selection/focus and common model/reasoning/permissions controls use the shared CUI contract. Voice & Vision uses the shared capture/runtime and real source policy; choosing a study mode does not start capture. A source-only voice answer follows the same evidence rules as text, and speech transcription alone does not count as a submitted assessed answer without the applicable explicit submission.

<a id="h.q8l16m7m2pss"></a>

#### 5.5.1.9 Failure recovery and end-to-end acceptance

STUDY-036 — Recover drafts, attempts and source/run context across navigation, restart and supported offline transitions. Provider unavailability can leave authorized notes, saved resources and drafts usable without pretending fresh tutoring or marking occurred. Missing Forms, Decks, Planner or embedding capability identifies the dependent operation and a valid available route; it does not claim the whole Study Space is complete through a mock replacement. An evaluation error retains submitted working for retry against the same question/rubric revision or an explicitly new evaluation revision.

STUDY-037 — Acceptance journey A: start with no records, create two subjects and a source-backed topic structure, rename one topic, open it through Subject Workspace and ask a sourced Learn question plus a follow-up. Switch to Practise in the same conversation with a retained answer draft. Submit incorrect, partial and assisted attempts, inspect feedback/provenance and correct a disputed result. Verify stable IDs, no evidence for page views and no lost original evaluation history.

STUDY-038 — Acceptance journey B: select Test without starting time, inspect the Forms rules, explicitly start/submit a supported assessment and recover across a reconnect. Reopen the same assessment/result in Forms. Enter Review, inspect a genuine due/weak reason, use a Decks review and verify that flip-only interactions contribute no assessed success. Check separate self-confidence, personal evidence and deterministic cross-subject recommendation ordering with zero to three entries.

STUDY-039 — Acceptance journey C: create and reopen actual Decks, Forms, Write and other supported Bank resources, compare embedded/standalone identities and remove an enrolment without deleting the source. Schedule a review in the Study burrow, change it in Planner and observe the same record on Study Home. Revoke a source, deny cloud disclosure, fail marking and use keyboard/narrow layouts. Record real outcomes, blocked capabilities and unrun cases; illustrative Atlas marks or fixture progress cannot establish a pass.

<a id="h.6gjglronkj4x"></a>

## Temporary-chat personalisation — confirmed owner controls

SPC-084 — Temporary is an accessible labelled on/off toggle in the ordinary Chat header. Before the first committed message, changing it affects only the current unsent Chat, retaining its draft and attachments without navigating or creating a duplicate conversation. Its associated Personalisation setting is a labelled Personalised/Unpersonalised two-choice input within the common composer Options panel, separate from response presentation and permission controls. Pointer click or keyboard selection changes only that future conversation choice; focus and selected state remain visible. Right-click exposes only a truthful existing context action, and dragging does not change the choice. Before first send, show what is eligible: memory, custom instructions and plugins for Personalised; none of those for Unpersonalised. The visible state reflects the remembered allowed account/workspace preference, with policy-constrained Personalised as the first-use default. Start/send freezes the personalisation choice at the first committed message; a disabled locked control explains why and offers a new Chat with Temporary enabled instead. New chat retains authorised preferences but starts an empty conversation and cannot carry old private content implicitly. Permissions, source binding and current account/workspace restrictions can narrow access at any point; revocation is effective immediately even though the chosen personalisation setting remains locked.

SPC-085 — Save Temporary, or switching Temporary off after the first committed message, opens the same explicit conversion confirmation that identifies durable local history and the applicable normal memory/personalisation/learning settings. Cancel leaves the Temporary toggle on and its session state intact. Confirm starts the real save transaction; the toggle stays on until acknowledgement establishes durable local history and clears the temporary flag. Preserve the existing conversation identity, draft and authorised attachments. Failure leaves the toggle on, a recoverable temporary conversation and a specific retry path. With either temporary personalisation setting, keep memory writes and Dreaming/training collection disabled. After successful conversion, apply ordinary eligibility and consent rules without granting tools, starting an agent or replaying prior effects. Source references and current input remain independently scoped; Unpersonalised does not prohibit explicit context supplied for the current chat.

SPC-086 — Acceptance: toggle Temporary on and off in a new unsent Chat without navigation, draft loss or duplicate identity. With Temporary on, choose each personalisation setting in Options before the first message and verify the correct context/plugin eligibility with no durable-memory writes. Start a conversation, reject a personalisation change, revoke an eligible plugin/read scope and verify that it cannot continue accessing that scope. Test custom instructions separately from current explicit input. Attempt delayed extraction and replication while temporary; both must remain blocked under their respective contracts. Switching Temporary off after start requests explicit conversion. Cancel conversion, then simulate a failed save, then acknowledge a successful local save; only the final case clears the toggle, appears in durable history and adopts eligible normal settings. Reject switching a committed regular chat into Temporary and offer a separate new Chat. Test a new Chat with Temporary enabled for empty history and remembered allowed preference. The Atlas stores only fixed demonstration state and a preference; it must not imply that exporting its fixture is a production temporary-content storage policy.

Interaction reference checked 22 November 2026: https://help.openai.com/en/articles/8914046-temporary-chat-in-chatgpt ; https://help.openai.com/en/articles/8590148-memory-in-chatgpt ; https://help.openai.com/en/articles/10169521-projects-in-chatgpt . The owner adopts the interaction model above. Retention, local recovery, execution routing and data governance remain the separately specified 9to1 contracts.
