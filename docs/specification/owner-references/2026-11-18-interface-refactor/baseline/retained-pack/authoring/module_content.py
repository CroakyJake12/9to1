"""Current v3 module contracts. Historical source is retained by build_module_pack.

These are implementation requirements, not claims that the product is implemented.
"""
GROUP_LABELS = {
 'layouts':'Layouts & visible states', 'features':'Features & behaviour',
 'entities':'Canonical entities & persistence', 'permissions':'Permissions & recovery',
 'acceptance':'Acceptance checks'
}
COMMON_GROUPS = {}
MODULES = {}
def L(s): return [x.strip() for x in s.strip().split('\n') if x.strip()]
def M(mid,title,prerequisites,overview,layouts,features,entities,permissions,acceptance,kind='app',contract=''):
    MODULES[mid]={'title':title,'kind':kind,'prerequisites':prerequisites.split() if prerequisites else [],'contract':contract or overview,'overview':overview,'requirements':{'layouts':L(layouts),'features':L(features),'entities':L(entities),'permissions':L(permissions),'acceptance':L(acceptance)}}

M('authority','Authority, scope and implementation reading map','',
 'This v3 pack reconciles the supplied expanded v2 development specification, the normative Professional annex, the incorporated Spaces companion and the current owner review. It provides current copyable contracts while retaining detailed technical requirements and source provenance. It does not declare any application implemented or independently accepted.',
 '''The current atlas supplies exact screen/state references. Presentation slides 1–16 are authoritative design references. Later slides are historical inspiration; the older Study concept remains an owner-approved rough layout/behaviour reference under the current wrapper.
 Each module uses the same section order: prerequisites, overview, layouts, features, entities, permissions/recovery, acceptance, source IDs and technical reading map. A complete Markdown version includes its retained source requirements.''',
 '''Apply current owner directions within their stated scope, then explicit later addenda, normative annexes and unsuperseded baseline clauses. Do not turn an illustrative screenshot into permission to remove an unpictured backend feature.
 Keep confirmed requirements distinct from proposed visual options and unresolved product decisions. A current mock-up may resolve geometry, not an unrelated unresolved policy.
 Treat each screen as one observable state, not the whole application. Implement the source-backed workflows, error paths, supported platforms and substantive donor capabilities preserved in the module.
 The technical source appendices are part of the implementation contract. Concise primary chapters are a reading and design layer, not permission to ignore data models, APIs, compatibility or acceptance gates.''',
 '''Every nonblank source block has a stable ledger ID, original text, destination, status and digest. Replaced wording retains its original form in the history ledger.
 Current requirements have V3 module IDs; source clauses retain their original IDs. Repeated source text may reference a retained copy without losing its original location.''',
 '''Historical agent instructions, deadlines and deployment process are archived as provenance. They do not create fresh authority to spend, deploy, send messages or access user data.
 Where current scope changes one clause, preserve adjacent security, persistence, platform and backend requirements. Record the amendment narrowly and retain the original.''',
 '''Verify every registered screen maps to an owning module and every source block has a retention or explicit historical status.
 Verify the copied Spaces chapter works as an implementation unit with explicit prerequisite contracts and source-backed acceptance rather than only links.
 Do not mark runtime acceptance passed from mock-ups, screenshots, compilation or source accounting alone.''',kind='governance',contract='Current source precedence, narrow owner overrides, source accounting and the required module structure.')

M('platform','Platform, app registry and host integration','authority',
 'The ecosystem shares canonical applications, artifacts and services across native, browser, mobile and 9to1OS hosts. Host presentation may differ while object identity, permissions and typed actions remain the same.',
 '''Expose authentic app launch/create/open/import/recents surfaces separately from active editor workspaces. A launch page must not remain as a decorative dashboard above an open document.
 Keep a familiar Home house icon in app headers. Respect host-safe top regions, viewport insets and narrow-screen drawer behaviour.
 Distinguish installed, available, offline, unsupported, starting, ready and failed states; do not make a static tile imply a working runtime.''',
 '''Register app capabilities and routes once; Home, Browse, OS and Launcher use that registry. Opening an artifact resolves its registered owner and stable target, including embedded AppSurfaces.
 Offer manual local editing without requiring sign-in, a connected Files provider or a running AI service unless the underlying operation itself needs it.
 Preserve the retained platform/backend language, donor reuse and UI/API parity requirements. CUI wraps existing powerful engines rather than replacing them with a reduced demonstration.
 Integrate typed creation, selection, edits, exports, task events and deep links. Automation uses these semantic APIs, not simulated pointer interaction.''',
 '''Keep stable AppID, artifact ID, TabID, route, revision and request identities. Do not duplicate documents when moving between native, embedded and browser views.
 Draft, active configuration, current view state and published output are separate records. Closing a view does not implicitly cancel an admitted background Task/Run.''',
 '''Check permissions at execution and dereference time, including cached/search and embedded surfaces. Explicitly distinguish unavailable capabilities from denied permissions.
 Preserve unsaved work through host navigation, process restart and session restore; Save/Discard/Cancel must refer to actual dirty state.''',
 '''Open the same artifact through Home, Files, a host tab and a Space; verify stable identity, current revision and consistent grants.
 Test app launch→create/open→edit→save→reopen, narrow layout, offline recovery and unsupported-platform states.''',kind='foundation',contract='Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.')

M('cui','CUI, themes and shared interaction system','authority',
 'CUI is the single public UI system for every 9to1 surface. The current Professional annex defines the canonical restrained default; theme identity changes materials, typography and motion without changing app topology or repainting authored content.',
 '''Offer Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western, plus Create Theme. Cinematic is an explicit legacy compatibility choice rather than a ninth current default.
 Preserve each theme’s character: Professional restrained dark charcoal; Glow luminous accent-dependent controls; Playful soft coloured surfaces; Frosted Glass translucent layered material with a legible fallback; Bubble watery dimensional surfaces and gentle wobble; Metallic brushed reflective material; Retro monospace; Western wood and sepia.
 Use Montserrat by default except the explicit Retro monospace treatment or authored document fonts. Use the exact Professional annex tokens, geometry and states where specified rather than inventing near-equivalent colours.
 Show normal, hover, pressed, selected, disabled, focused, loading, validation, destructive and reduced-motion states. Icon and text contrast must remain legible over every theme material.
 Use native platform emoji rendering for emoji characters. Keep application glyphs and identity marks coherent, distinct from decorative emoji.''',
 '''Use CUI public primitives and typed templates. Avalonia and donor engines are internal implementation details; do not expose raw AXAML or third-party controls as a competing public UI language.
 Keep one deliberate primary scroll container per workspace, bounded panels and predictable focus/keyboard order. Popovers reposition at viewport edges; Escape/outside dismissal returns focus appropriately.
 Shared menus, tooltips, sliders, switches, inputs, tabbars, avatars, toasts, dialog surfaces and selection controls inherit the same theme and semantic action variants.
 App-specific content retains its own colours: slides, pages, photographs, charts, canvases and web previews do not receive theme-tint filters.
 Reduced motion disables wobble, bounce and ornamental animation while preserving state changes and feedback. Transparency and material effects need performant, accessible fallback modes.''',
 '''Store theme identity/version, compatible custom tokens, accent, typography/density and accessibility preferences in the Home-owned account/device preference contract. Space-specific overrides are explicit and reversible.
 Keep semantic tokens separate from app identity accents and document content. Theme switching must not mutate artifact revision or user-authored styles.''',
 '''Custom themes are validated against supported tokens and accessible contrast/focus requirements; importing one is not permission to execute arbitrary UI code.
 Restore a valid baseline if a theme resource is missing, incompatible or fails to load; preserve the user’s chosen identity for recovery.''',
 '''Review all eight current themes, a custom theme and Cinematic compatibility with keyboard focus, mixed selected/disabled states, narrow screens and reduced motion.
 Verify the Professional annex’s menu/toolbar/selection/notes and Present geometry exactly; compare rendered state captures rather than only token files.
 Change themes while editing and verify selection, drafts, scroll, runtime state and authored content survive unchanged.''',kind='foundation',contract='Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.')

M('identity','Account, identity and resource entitlements','platform cui privacy',
 'Home owns account identity and resource subscriptions while local applications remain usable without a mandatory cloud account. Entitlements are server-authoritative resource capabilities, not arbitrary feature flags inferred from a profile name.',
 '''Account settings show canonical identity, name, username, optional icon/pronouns/job, account state, subscription/resource allocation and secure session controls.
 Clearly distinguish personal and organisation scope, signed-out/local use, pending verification, active subscription, payment failure and unavailable account service.''',
 '''Use CAKE authentication and the retained identity/account contracts. Required profile fields are name and username; optional fields must remain optional.
 Preserve named Free, Boost, Plus, Pro, Ultra, Ultra+ and Max packages and the source-defined currency/prices/resource thresholds. Do not invent unavailable allocations or derive limits from marketing labels.
 Apply verified reserved-account/business exceptions only to authenticated canonical account identity; a typed display name or matching username cannot grant privileges.
 Treat resource purchases, organisation seats and application capabilities as separate dimensions. UI estimates must not be presented as settled billing.''',
 '''Keep AccountID, session credentials, entitlements, plan revision and resource ledgers canonical. Store secrets in the appropriate secure credential store.
 Server-confirmed payment, allocation, reserve/consume/settle and webhook idempotency remain backend contracts; account UI reads their actual state.''',
 '''Authenticate sensitive account operations, respect organisation policy and display revocation/session expiry honestly. A settings mock-up never performs a real purchase or account mutation.
 Preserve local drafts and manual workflows when identity services are offline or the user signs out; recheck access before cloud sync or model use.''',
 '''Test signed-out local creation, authenticated settings, profile validation, session expiry and reauthentication without losing work.
 Verify package/resource display against the retained server contract and prohibit privilege escalation through profile edits.''',kind='foundation',contract='Canonical CAKE account identity, secure sessions, server entitlements and resource allocation state.')

M('privacy','Privacy, data boundaries and context isolation','authority',
 'Privacy applies to storage, model execution, context retrieval and every app handoff independently. Local storage does not imply local inference, and a connected resource does not imply permission to send its contents to an external model.',
 '''Expose effective local/remote execution, storage location, capture indicators and relevant source scope where they influence a meaningful user decision.
 Offer explicit private/temporary and memory states with truthful limitations, not hidden privacy assumptions.''',
 '''Keep app, conversation, personal, shared/team and organisation context boundaries. Retrieve only relevant authorised material, with source provenance and current grants.
 Temporary conversations are local-only until an explicit supported conversion; they do not create or reinforce durable memory or transfer to Mesh/Cloud automatically.
 Stop screen/camera/microphone capture at the actual runtime boundary when the user ends sharing. Hidden frames must not continue to reach models.
 Treat imported web, document, plugin and retrieved content as untrusted data; text within it cannot elevate permissions or rewrite platform policy.''',
 '''Track provenance, scope, consent, retention and revocation for stored/transferred context. Encryption protects data at rest/in transit; it is not a substitute for access control or an executable prompt-injection proof.
 Separate context snapshots from canonical source artifacts and preserve permission-aware deletion/tombstones in caches and indexes.''',
 '''Recheck authorisation when rendering stored citations, opening cached files or continuing a background job. Revocation must promptly remove eligible access without leaking cached content.
 Provide inspection, correction, deletion and export controls where retained policy permits, without treating frequency changes as deletion requests.''',
 '''Test cross-app denied access, removed sources, temporary conversations, cloud-route refusal and capture shutdown using observable runtime receipts.
 Verify logs and UI traces expose useful high-level provenance without private internal chain-of-thought or unrelated user context.''',kind='foundation',contract='Privacy scopes, consent, local/remote boundaries, source provenance, retention and revocation.')

M('permissions','Permissions, approvals and execution scope','platform privacy',
 'Permissions intersect platform policy, account/org policy, app capability grants and the current operation scope. A visual control shows effective authority; it never creates authority merely by being enabled.',
 '''Keep Spaces Conversation permissions separate from editor AI Read-only/Write and document Edit/Suggest/View. Name the active scope and blocked reason visibly.
 Approval requests identify the proposed action, target, revision, data/source scope and consequence. Pending approval is distinct from paused, denied and failed.''',
 '''Use typed action grants and recheck at execution. A narrower conversation override cannot widen durable app or organisation grants.
 Require action approval wherever the source specifies it, including every AI mutation in Data. A reviewed Plan may grant only its declared scope; unrelated operations remain outside it.
 Distinguish provider OAuth scopes, installed capability availability, user grants and one-operation approval. Installing or enabling a Plugin/Skill does not automatically authorise its data access.
 Preserve review and independent acceptance gates when budgets run out; continue unrelated permitted work without pretending a required review happened.''',
 '''Persist approval/action IDs, grant scope, policy source, expiry, revocation and audit outcome. Grants reference canonical targets rather than display names.
 An admitted request records the effective policy/version and subsequent permission changes for recoverable audit.''',
 '''Reject stale target revisions and revoked grants with actionable recovery. Retry must not duplicate external effects or reuse an expired approval.
 Redact inaccessible resources from pickers, suggestions and exports; show safe unavailable metadata only where authorised.''',
 '''Test Read-only proposals without mutation, Write with action-specific approval, mid-run revocation, stale revisions and denied provider scopes.
 Verify keyboard and mobile permission controls show the same effective scope as desktop and never imply broader access.''',kind='foundation',contract='Effective scoped grants, action approvals, policy intersections, revocation and audit.')

M('persistence','Artifacts, local persistence and recovery','platform permissions',
 'Applications persist canonical artifacts and revisions independently of their display surfaces. Local saving, replication, export and publishing are different state machines with honest visible receipts.',
 '''Use explicit states such as local draft, changes pending, saving locally, saved locally, sync pending, synced, failed, read-only and conflict.
 Generated files distinguish saved native artifact, pending export, verified downloadable bytes and failed export. Do not show successful download for a preview-only object.''',
 '''Apply typed commands with expected revision, transactional validation and meaningful undo/redo. A restored view does not replay previously executed external actions.
 Autosave and explicit save must survive interruption with recoverable draft/checkpoint semantics. Optional sync cannot be required for basic local editing.
 Keep exports reproducible from a known revision with declared format/range/options; preserve native content when a secondary export fails.''',
 '''Use stable artifact/object IDs, revisions, source references, timestamps and provenance. Preview caches and rendered exports are derived from canonical content.
 Separate view state from authored state and runtime state from source configuration. A duplicate/fork receives a distinct identity with traceable ancestry.''',
 '''Resolve collisions and revisions explicitly. Avoid silent overwrite, duplicate send or side-effect replay after reconnect/retry.
 Delete, remove-from-collection, detach and uninstall are distinct operations and require the correct owner semantics.''',
 '''Interrupt local save/export/sync, restart, reopen and verify canonical content and pending work recover.
 Verify reference removal never deletes an unrelated source file and failed derived export retains the native revision.''',kind='foundation',contract='Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.')

M('productivity-engine','Shared productivity engine and canonical embeds','cui persistence',
 'Boards, Write, Present, Canvas, Data and other productivity surfaces reuse real document/object engines and canonical embedded app surfaces. They must retain substantive editing power while sharing a coherent CUI shell.',
 '''Embedding an app presents its genuine editor/viewer, with capability-appropriate compact controls and an Open in App route to the same artifact.
 Selection, toolbar and inspector context follow the actual focused object, not a synthetic card representation.''',
 '''Use semantic object operations for blocks, pages, text ranges, strokes, shapes, cells, tables and media. Preserve donor-engine features and format compatibility identified in the retained technical clauses.
 Contextual AI produces inspectable typed changes against current revisions. Manual editing remains available without AI.
 Clipboard, drag/drop, keyboard editing, comments, history, search and accessible selection follow shared contracts without flattening each app’s model.''',
 '''Keep object IDs stable across embedded/native views, revisions and export where possible. Embed records reference canonical owning artifacts and scoped view settings.
 Preserve undo boundaries and dirty state across app transitions; storing a reference must not clone or silently move the source.''',
 '''Respect artifact and embedded resource permissions independently. Revoked or missing sources show an honest placeholder and recovery path.
 Read-only embed state must prevent both direct editing and AI mutation while retaining permitted viewing/navigation.''',
 '''Edit a canonical embed, open in its owner, and verify the same revision and history. Switch focus between nested objects and verify correct tool routing.
 Exercise copy/paste, undo/redo, source revocation, format round-trip and local recovery.''',kind='foundation',contract='Real document/object engines, semantic editing, canonical app embedding and donor feature parity.')

M('node-editor','Node Editor — shared graph framework','cui persistence permissions',
 'The Home-owned Node Graph Framework is the common editor, validator and runtime presentation system used by Automations, AI Studio and custom Spaces. Apps supply domain node semantics; they do not create competing graph engines.',
 '''Expose typed nodes, ports, connections, palette/search, selection/grouping, pan/zoom, validation, properties and real runtime highlights.
 Recognisable categories include Trigger, Condition, Event, Logic, App and Files. Use accessible theme-aware category treatments and labels rather than colour alone.''',
 '''Reuse stable graph schemas and registered node APIs. Validate types, missing dependencies, cycles, bounded execution, policy intersections and unsupported nodes before activation.
 Support undo/redo, copy/paste, keyboard/touch manipulation, graph search and contextual AI edits through typed commands.
 Preview, validate and activate are separate operations. Invalid draft logic must not replace the last known-good active revision.''',
 '''Persist GraphID, NodeID, PortID, connection IDs, schema/version, draft/active revisions and runtime references. View pan/zoom is separate from graph logic.
 App-specific settings and graph projections share the same canonical configuration; switching representations cannot erase hidden logic.''',
 '''Graphs execute only authorised app/Dulche/Files actions under the shared permission model. A new node cannot manufacture a model backend, memory broker, scheduler or privilege.
 Rollback changes future configuration; it does not pretend to undo external effects already performed.''',
 '''Round-trip a graph through simplified settings and Expert view without losing nodes, IDs or advanced behaviour.
 Test invalid ports/cycles, revoked tools, disconnected dependencies, draft recovery and genuine runtime highlighting.''',kind='foundation',contract='Shared typed graph schemas, editing/validation, draft activation and runtime presentation.')

M('shared-ai','Shared AI controls and artifact collaboration','cui permissions dulche assistants',
 'Contextual AI is shared across app surfaces, with canonical model routing, scoped artifact context and global Assistants/Specialists. Its visual affordances remain contextual to the owning app and never replace the app’s own editor model.',
 '''Editor AI bars expose Read-only/Write and actual target context. Spaces exposes Conversation permissions instead. Keep AI bars clear of bottom tools, timelines and active popovers.
 Header collaborator avatars and pickers show real configured Assistant/Specialist identities and their current artifact roles. Review has an inspectable proposed change state before application.
 Use the canonical model picker and notification access; do not duplicate hidden provider routing controls per app.''',
 '''Read-only AI may explain, inspect and propose within granted scope without mutating the artifact. Write remains constrained by current permission and any required action approvals.
 Collaborator proposals use owning-app semantic edits, expected revisions, diff/preview and meaningful undo. Accept/reject applies a defined change set, not arbitrary narrated success.
 Show task/tool-level activity, approval blockers, evidence and output links. Thinking text is a concise work summary, never private internal chain-of-thought.
 Provide Steer/Stop/Queue/Resume semantics only for valid runtime states and maintain the request draft until admission succeeds.''',
 '''Link user request, Assistant/Specialist, TaskID/RunID, target artifact/revision, proposal and receipt. Opening the same collaborator from Home or an editor preserves identity.
 A collaborator presence badge is a view of canonical task/role state rather than a second chat store or independent agent runtime.''',
 '''Do not expand context or write scope automatically when adding a collaborator. Display inherited, restricted, waiting approval and unavailable states truthfully.
 Recover partial work without reapplying accepted changes or claiming failed exports are available.''',
 '''Exercise a read-only explanation, scoped proposal, review/reject, accepted revision and stale-target conflict in two different editors.
 Verify toolbar overlap, focus restoration, reduced motion and mobile access to the same permission controls.''',kind='foundation',contract='Contextual AI bars, shared model/permission controls, global collaborators and typed artifact proposals.')

M('dulche','Dulche runtime, requests and execution services','platform permissions persistence',
 'Dulche is the shared AI/runtime broker, owning model routing, request execution, Task/Runs, tool actions and integrations with memory and subagents. Its lifecycle and results are observable through typed contracts rather than interface simulations.',
 '''Expose Starting, Ready, Degraded, Restarting and Stopped accurately where relevant. An unavailable broker must not disable unrelated local editing.
 Show request admission, active execution, waiting approval/input, paused, cancelled, completed and failed as distinct states; elapsed time represents measured activity where specified.''',
 '''Use canonical requests and ordered eligible model routes. Filter capabilities, privacy, entitlements and resources before choosing a route. Local-only must never silently fall back to cloud.
 Keep fallback routing distinct from tool retry and external-effect retry. Durable execution records support idempotent recovery, cancellation and inspection.
 Preserve retained runtime APIs, transport, multi-engine/Strata and embedded service contracts. UI work must not silently replace them with a new backend.
 Treat supplied content as data with provenance; shared-context services, safety checks and action dispatch remain within the trust boundary defined in retained clauses.''',
 '''Persist request IDs, TaskID, RunID, ActionGraphID, admission configuration, model/route identity, status events and output references. A trace is a structured runtime record, not a fabricated progress feed.
 Maintain durable queue and checkpoint state separately from transient view state. Reopening a view attaches to existing execution.''',
 '''Propagate current grants, budgets and revocation to tools and child tasks. Stop/interrupt acknowledges actual runtime state and does not promise an already-committed external effect can be undone.
 Resume from safe checkpoints; represent partial output, degraded providers and uncertain external outcomes explicitly.''',
 '''Test admission refusal without losing draft, provider failure/fallback, local-only routing, cancellation acknowledgement, restart recovery and idempotent tool handling.
 Confirm semantic API operations and displayed task states match real runtime events.''',kind='foundation',contract='Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.')

M('models','Models, discovery and universal model routing','cui identity permissions dulche',
 'Models combines discovery and comparison with local installed-model management. The Universal Model Picker is the same canonical routing control in Home, Spaces and editors, with truthful capability, provider, limit and hardware information.',
 '''Use discovery as the main Models experience, with search/filter, modality and capability metadata, hardware fit and a distinct Installed view. Model detail exposes source, provider, variants, limits and management actions.
 The shared picker separates Active and Background ordered routes. Chat/Image/Voice are primary groups; Audio/Video advanced groups appear when supported. Voice routes name actual voices.
 Display reasoning effort as supported discrete levels, independent from context consumption and provider usage. Unknown metrics read unknown/N/A; local routes hide inapplicable cloud allowances.''',
 '''Install, verify, load, unload, update, remove and recover/rollback local models through actual lifecycle services. Show download/verification failures and storage requirements before committing.
 Resolve provider identity and model variant by stable IDs. A display-name match is not capability equivalence.
 For effort tiers, use canonical labels 35/70/100 for three levels, 25/50/75/100 for four and 20/40/60/80/100 for five. Bind each option bijectively to the provider’s real level ID/ordinal; do not pass these labels through the obsolete floor formula.
 Model/provider limits support explicit windows, units, reset times and user-pinned display selection. Estimates and time-to-block must be labelled and grounded in measured evidence; do not fabricate throughput or allowance.''',
 '''Keep provider/model/voice IDs, manifests, installed artifacts, capabilities, route order, active/background assignment and limit records canonical. Model weights are references managed once, not copied into each conversation.
 Account/device defaults and request/conversation overrides have explicit lifecycles and inheritance. Changing model validates inherited effort and capability settings.''',
 '''Secure credentials; distinguish provider consent from 9to1 app grants. An installed model may be unavailable because of hardware, policy or runtime state.
 Do not remove weights used by an active run without the required lifecycle handling; show real partial-install and rollback states.''',
 '''Verify discovery→detail→install→verify→load→picker eligibility; simulate insufficient disk, incompatible hardware and failed verification.
 Test each effort mapping, mixed provider limits, no-capability models, unknown counters and local-only fallback restrictions.''',kind='foundation',contract='Model discovery/lifecycle, stable provider capabilities, ordered routing and truthful usage/effort metadata.')

M('den-memory','Den, memory and knowledge banks','privacy persistence dulche',
 'Den stores persistent assistant/context knowledge through canonical scoped records and brokered access. Memory policy, explicit knowledge sources and background learning are independently controlled and inspectable.',
 '''Expose the exact memory frequency choices Never, Sometimes, Often and Always with clear effective scope and ownership. Background Learning is a separate opt-in.
 Show relevant saved memories/knowledge-bank sources with inspect, correct, delete and provenance controls; do not imply inaccessible hidden content was used.''',
 '''Preserve source-defined versioned manifests, journals, namespaces and encrypted data contracts. Model references must not duplicate model weights.
 Never automatically create or reinforce durable memory from Temporary chats. Whether a historical unresolved case permits reading existing memories must not be silently decided by a decorative toggle.
 Frequency changes influence future memory behaviour; they do not delete existing records. Learning enablement does not grant external disclosure or unrelated app access.
 Keep personal, Space, Assistant, team and organisation memory boundaries with scoped retrieval and relevant context selection.''',
 '''Maintain record IDs, versions, ownership, provenance, timestamps, permitted scopes and correction/deletion history. Cache invalidation and indexes honour deletions and revocation.
 Distinguish canonical memory records from ephemeral request context, retrieval snippets and learned summaries.''',
 '''All reads/writes go through the authorised broker; imported content cannot inject permission changes or private-state rules.
 Surface inaccessible, stale, conflicting and failed records with safe recovery rather than silently using unverified context.''',
 '''Exercise memory frequency transitions, temporary-chat no-write, explicit correction/deletion and cross-scope denial.
 Verify an Assistant answer’s source receipt reflects actually accessed authorised memory and no hidden private reasoning.''',kind='foundation',contract='Scoped durable memory, versioned Den records, knowledge-bank provenance and brokered retrieval.')

M('subagents','Subagents, delegation and task trees','dulche permissions persistence',
 'Subagents are bounded child executions within a canonical task tree. They expose useful progress, blockers, evidence and outputs without disclosing private internal reasoning or creating independent permission authority.',
 '''Show parent/child relationships, task objective, configured identity/route, state, scope, budget and outputs. A child detail view provides high-level work events and actionable blockers.
 Use real progress/task status; do not invent completion percentages, measured timings or successful tool results.''',
 '''Delegate scoped context and only the intersection of current parent grants. Enforce depth, concurrency, budget and capability limits.
 Support structured joins, result aggregation, cancellation/pause propagation and partial-failure handling. A child’s failure must not erase completed sibling artifacts.
 Relevance-based Assistant participation and persistent Teams are separate from anonymous worker delegation; preserve canonical identities when configured collaborators are used.''',
 '''Persist parent TaskID/RunID, child IDs, delegation inputs, scope/version, status events, outputs and join receipts.
 Reconnect to existing child runs after view/runtime restart without launching duplicate work or replaying external actions.''',
 '''Revocation and budget changes propagate through the tree. An exhausted budget cannot be hidden by spawning another child or bypassing a required review.
 Expose retry scope and uncertain external results; retry only safe or idempotent work with renewed grants when needed.''',
 '''Test concurrent children, mixed success/failure, waiting approval, cancelled parent, scope revocation and resumed inspection.
 Confirm outputs open the canonical owning artifacts and the activity view contains no private chain-of-thought.''',kind='foundation',contract='Durable delegated task trees, inherited scope, bounded budgets, joins and recovery.')

M('assistants','Global Assistants, Specialists and collaborators','cui dulche den-memory permissions',
 'Assistants and Specialists are global systems available through Home and editors. Spaces retains their canonical conversation views. Assistants provide persistent personal identities and ongoing work; Specialists provide reusable job-focused expertise and configuration.',
 '''Home exposes distinct Assistants and Specialists destinations with available/owned identities, creation/configuration and current work. Editor collaborator avatars open identity-aware pickers in the artifact context.
 An Assistant has a permanent Main conversation plus additional threads. Do not recreate a new Main identity whenever the user changes host/app.
 Review views show proposed semantic artifact changes, contributor identity, scope, selected revision and explicit accept/reject controls.''',
 '''Preserve identity, instructions, model routing, tools/Skills/Plugins, Files/context, memory policy, budgets, queues, permitted proactivity, Dev handoff, Mini Computer capability and recovery.
 Persistent Teams support configured roles, shared work and relevance-gated participation; user-facing messages identify each contributor. Show Assistant–Assistant Chat uses All, Relevant or None without changing actual access rights.
 Specialists launch with the correct canonical job/instructions and policies. Discovery/configuration principally belongs in Home and AI Studio; Spaces lists may link there.
 Conversation handoff passes explicit authorised context and canonical work references without copying history indiscriminately. Replies from Connect remain in the originating channel/thread.
 Adding an editor collaborator produces a scoped participant or proposal relationship to the same artifact; it is not a new document copy or permission escalation.''',
 '''Keep AssistantID/SpecialistID, configuration revisions, Main ConversationID, other threads, TeamID/membership/roles, Task/Runs and artifact proposal references canonical.
 Preserve old Agent compatibility contracts where retained. Renaming or relocating the UI does not delete API capabilities.''',
 '''Show effective context/write scope and unavailable tools, and require action-specific approval where applicable. A collaborator avatar does not mean unrestricted write authority.
 Recover queued/background work, partial proposals and thread handoffs without duplicate external effects. Configuration rollback affects future work, not already-sent messages.''',
 '''Open one Assistant in Home, Spaces and Write; verify identity, Main conversation and configured policy match.
 Exercise Specialist selection, persistent Team relevance settings, artifact review/rejection, accepted revision and revoked-source access.''',kind='foundation',contract='Persistent global Assistant/Specialist identities, Teams, canonical conversations and editor collaboration.')

M('generative-ui','Generative UI and live artifact output','cui dulche persistence permissions',
 'Generative UI uses typed safe CUI components bound to inspectable state/actions. It can appear inline in Spaces and as a canonical output, while app-owned documents and exports retain their real artifact semantics.',
 '''Render meaningful interactive forms, charts, tables, selectors and progress states, with a visible connection to the active task and sources.
 Live Present/Canvas progress shows semantic work on the canonical artifact and high-level completed/current actions. Generated file cards show name, owner, revision, format and truthful availability.''',
 '''Validate generated component schemas and actions against the safe registry. Arbitrary text, HTML or code is not automatically trusted UI or an executable permission grant.
 Keep ephemeral conversational state separate from promoted durable artifacts. A promotion/save action creates or updates the owning artifact through its typed API.
 Open resolves the canonical artifact; Download requires verified durable bytes. Failed PDF/export retains the saved native source and offers retry from the known revision.
 Preserve interaction state and accessibility during streaming updates; invalid component output must yield a recoverable error without deleting prior valid content.''',
 '''Persist component/state/action IDs where needed, schema version, source Task/Run, artifact ID/revision and export receipt. Generated controls cannot maintain a hidden parallel copy of app state.
 Store a durable export manifest only after bytes are materialized and verified; a rendered preview is not sufficient.''',
 '''Use current app grants for every generated action. Sandbox untrusted content, constrain external links/actions and recheck revoked source access.
 Retry idempotently and distinguish export-only failure from native-save failure; never rerun unrelated completed generation work automatically.''',
 '''Test interactive state changes, invalid schema, source revocation, narrow layout, keyboard accessibility and reload recovery.
 Verify Open/Download receipts and failed-export recovery against real bytes and the same canonical source revision.''',kind='foundation',contract='Safe typed generated components, canonical live artifacts and verified durable output/export cards.')

M('plugins-mcp','Skills, Plugins, MCP and capability packages','platform permissions dulche',
 'Skills, Plugins and MCP integrations are registered, versioned capabilities with explicit trust, scope, transport and lifecycle. Availability in a picker is separate from permission to execute or disclose data.',
 '''Expose installed/available/disabled/incompatible/failed states, version, source, owner, permissions and configuration. Clickable composer tiles configure the exact referenced capability.
 Show real MCP connection and authentication health, tool schema availability and actionable errors rather than generic connected badges.''',
 '''Retain package manifests, signature/trust, compatibility, dependency and registry contracts. Imports validate packages before activation; installing text or assets cannot execute arbitrary unreviewed code.
 Use typed tools, stable identifiers and declared schemas with timeouts, cancellation, logs and controlled errors. Assign capabilities to accounts/Spaces/Assistants within current policy.
 Peer Review, Multiple Responses and Plan capabilities obey their source-defined semantics, scoped approvals and budgets; enabling a plugin does not grant a new authority.''',
 '''Store capability IDs/versions, package provenance, configuration, grants, bindings and connection state canonically. Space folders reference global packages rather than creating incompatible clones.
 Keep tool execution receipts linked to Task/Runs and action IDs.''',
 '''Revoke assignments and credentials safely, stopping future access and exposing interrupted tasks. A stale cached schema must not call a changed tool blindly.
 Recover failed installations, disconnected transports and expired auth without losing unrelated drafts or reporting false success.''',
 '''Test install/validate/configure/assign/use/revoke, missing dependency, schema mismatch and transport timeout.
 Verify passive mentions only reference resources, while explicit invocation uses the documented command/action and approval flow.''',kind='foundation',contract='Registered versioned Skills/Plugins/MCP capabilities, manifests, trust, bindings and tool lifecycle.')

M('integrations','External integrations and provider connections','identity permissions persistence',
 'External integrations connect canonical 9to1 capabilities to authorised provider accounts. They preserve provider provenance and distinguish live references, imported copies, cached data and synchronised state.',
 '''Show provider account, granted scopes, connection health, last confirmed sync, pending operations and reconnect controls.
 Separate the integration manager from Connect conversations and Mail. A linked provider can contribute capabilities across apps without becoming a new app clone.''',
 '''Use explicit OAuth/provider authorisation and the typed capability registry. Read, create, update, delete, sync and import operations retain their distinct consequences.
 Preserve source identity and normal conflict semantics. Reconnect/retry uses idempotent queues where supported and does not duplicate messages or file mutations.
 Report provider limits, unsupported operations and uncertain outcomes accurately; a local queued action is not a remote success.''',
 '''Persist connection/account IDs, resource IDs, scope/version, sync tokens, mappings, pending operation IDs and last confirmed remote receipts.
 Cache entries carry freshness and permission provenance; provider revocation invalidates eligible access.''',
 '''Check both provider scopes and 9to1 grants at use time. Never infer access to a second account from an existing provider session.
 Offer recovery for expired auth, network loss, quota, conflicts and partial batch success without silently overwriting remote changes.''',
 '''Test connect→authorise→read/write→revoke, offline queue, duplicate retry and concurrent remote edit.
 Verify UI status corresponds to actual provider receipts and cached data is labelled when stale.''',kind='foundation',contract='Canonical provider connections, OAuth scopes, typed capabilities, provenance and sync recovery.')

M('go-search','Go, universal search and entity navigation','platform permissions persistence',
 'Go is the shared universal search/navigation surface across Home, Browse and 9to1OS. It resolves real applications, commands, artifacts and context through permission-aware indexes and explicit Default/AI search modes.',
 '''Use a focused overlay with query, category/source-aware results, keyboard navigation and clear target actions. Keep ordinary search and AI-assisted search visibly distinct.
 Expose availability, source owner and relevant location instead of presenting inaccessible resources as actionable results.''',
 '''Index canonical entity references and registered commands; recheck access when opening or executing. Search text alone does not authorise a command.
 Keep app launch, open artifact, navigate location and request AI actions semantically distinct. Contextual search must not leak unrelated app data.
 Preserve source-defined indexing, deduplication, ranking and tombstone contracts rather than scanning private stores directly from UI.''',
 '''Use canonical search/result IDs, source artifact/entity IDs and index revisions. Search caches are derived, not an independent document store.
 Deleted/revoked sources propagate tombstones and safe redaction to indexes.''',
 '''Handle unavailable devices, offline sources and stale results with clear recovery. Do not execute a stale action before checking current policy.
 Closing the overlay preserves the underlying application’s draft/focus state.''',
 '''Search by keyboard, open an app and artifact, then revoke/remove the source and verify it cannot be reopened from stale results.
 Test AI/default mode switching, narrow layout, Escape focus return and unavailable Mesh targets.''',kind='foundation',contract='Permission-aware typed search, command/entity resolution and shared Go presentation.')

M('cloud-services','Cloud services, infrastructure and billing contracts','identity privacy permissions persistence',
 'The cloud/backend module retains the supplied service architecture, resource accounting, storage, authentication and billing contracts. Current UI changes do not remove infrastructure requirements or authorise live deployment.',
 '''Display actual service/connection, allocation, quota and billing state through owning apps. Avoid infrastructure controls in ordinary app flows unless the user must make a concrete decision.
 Show pending/confirmed/failed server actions and explain actionable limits without invented allocations.''',
 '''Retain the specified Cloudflare frontend/Workers/D1/Durable Objects/queue and B2 storage responsibilities, secure authentication/PKCE and service boundaries.
 Preserve Stripe checkout/webhook verification, idempotency, allocation/resource accounting, reserve/consume/settle and organisation pool/ceiling contracts.
 Keep publishing, custom domains, storage and execution resources independently authorised. A mock-up publish button is not evidence of a deployed service.''',
 '''Use server-authoritative account/resource ledgers, idempotency keys, object manifests, encrypted storage metadata and audit events.
 Maintain migration/version compatibility and recovery semantics across queued events and partial service failure.''',
 '''Infrastructure deployment, billing changes and external publication need the actual operation’s authorisation. Historical delivery instructions in the source are not new permission.
 Fail closed for auth/resource checks, preserve retryability and avoid double-charging or duplicate external effects.''',
 '''Validate billing/webhook replay, concurrent reservations, partial failures, revocation and storage quota enforcement against real service tests.
 Keep design review distinct from production readiness or deployment acceptance.''',kind='foundation',contract='Retained server architecture, secure cloud storage, billing and resource-accounting contracts.')

M('home','Home — Apps, Dashboard and global systems','platform cui identity models assistants',
 'Home is the ecosystem control centre, app registry entry point and owner of shared account/preferences/capability surfaces. It exposes Apps, Dashboard, Models and the global Assistant/Specialist systems without pretending every shared service is a standalone app.',
 '''Apps provides discoverable canonical app entries and meaningful groups/search. Dashboard surfaces genuine recent work and shared status. Models is discovery-led with Installed management alongside.
 Use the house Home icon consistently. Global profile, model and Notifications controls open the same canonical services in Home and app hosts.
 Keep Assistants and Specialists accessible as global destinations. Remove Events as a visible navigation destination; activity belongs to Notifications.''',
 '''Launch real registered app targets, resume canonical artifacts and show availability/installation state. Pinning/reordering is a preference change, not an app install.
 Dashboard entries reference actual tasks, files, recent items and provider state. Empty/unavailable data must be honest and actionable.
 Home owns common configuration for themes, models, integrations, Plugins/Skills, permissions, account and Mesh, while apps own their domain artifacts.''',
 '''Persist Home navigation/pinning preferences, account/device scope and recent-item references independently from app contents.
 Retain source-defined event/audit records and deep links through Notifications; removing a destination does not delete historical events.''',
 '''Check grants when opening recents or shared resources. Missing/removed targets provide safe recovery rather than a fake app preview.
 Manual local app access remains available when signed out or shared AI services are unavailable.''',
 '''Exercise Apps→launch→Home→recent artifact and verify target identity. Test Dashboard empty/error states, global Assistant entry and model discovery.
 Search the visible navigation for Events and verify activity is available only through Notifications.''')

M('settings','Settings — complete page family','cui identity permissions models privacy',
 'Settings provides eleven navigable pages, each with practical controls bound to the owning shared service. Appearance remains part of this family while its visual token contract is defined by CUI.',
 '''The sidebar contains Account, Appearance, Models, Permissions, Apps, Storage, Mesh, Privacy, Accessibility, Notifications and Advanced. Keep the current page selected, navigation predictable and content comfortably spaced.
 Account focuses identity/sessions/resources; Models shows default routing and provider management; Permissions uses a scoped capability matrix; Apps manages availability/default owners; Storage shows real use and locations.
 Mesh focuses device capability/connectivity; Privacy covers history/memory/context; Accessibility covers motion/contrast/text/input; Notifications covers channels, quiet periods and activity; Advanced covers diagnostics, experimental/configuration and recovery controls.
 Include provider detail with secure credential state, scopes, route/model eligibility and connection diagnostics. Use suitable page structures instead of repeating one generic card grid.''',
 '''Use Home-owned account/device preference APIs and identify effective inherited/organisation-controlled values. Native form state must bind to canonical settings in implementation.
 Appearance changes the full theme family and previews shared states without modifying document content. Memory frequency and Background Learning remain independent.
 Distinguish conversation permission scope from editor AI access and document modes; link to Manage app permissions without implying a local override widens policy.
 Explain pending, saved, failed, read-only/policy-controlled and restart-required changes. Do not claim backend success from a displayed toggle.''',
 '''Persist values by setting ID, scope, schema/version and source of effective rule. Provider credentials remain in secure storage rather than plain preference payloads.
 Remember page/section navigation independently from dirty configuration; switching pages must preserve or explicitly resolve unapplied edits.''',
 '''Use concrete confirmation only for consequential resets/removals. Diagnostics/export must respect privacy and redact secrets.
 Handle failed saves, unavailable providers, revoked grants and unsupported device controls without discarding unrelated edits.''',
 '''Navigate all eleven pages and provider detail by pointer and keyboard. Verify narrow layouts, focus labels, long-page scrolling and no clipped selects.
 Test policy inheritance, failed setting persistence and a preference change reflected in a second app surface.''')

M('notifications','Notifications and ecosystem activity','platform cui persistence permissions',
 'Notifications owns user-visible notifications and ecosystem activity history. It replaces the separate Events destination while preserving EventID, provenance, audit, source routing and read-state backend contracts.',
 '''Open Notifications from the global anchored control. Provide useful unread/all/activity grouping, time/source labels, actionable task states and safe empty/error states.
 Settings controls notification preferences, channels and quiet periods; it is distinct from the notification feed itself.''',
 '''Deep links open the canonical task, conversation, app artifact or approval target. Read/unread and dismiss operations change notification state, not source task completion.
 Preserve typed event ingestion, deduplication, priority, action affordances and audit provenance. A failed background action must not be concealed by dismissing its notification.
 Keep events used in backend logs/data models; only the old user-facing Events navigation is removed.''',
 '''Maintain NotificationID/EventID, source entity IDs, timestamp, severity/category, read/dismiss state and permitted action targets.
 Retain history according to account/org policy; render only authorised event details.''',
 '''Recheck target permission on open; redact revoked source details and offer safe unavailable status.
 Do not replay consequential actions from a stale notification without validating current state and approval.''',
 '''Test unread/read/dismiss/deep-link, duplicate ingestion, revoked targets, failed task notification and quiet-period settings.
 Verify Notifications behaves consistently from Home, Spaces and an editor and no visible Events destination remains.''')

M('mesh','Mesh devices, replication and remote capabilities','platform identity privacy persistence',
 'Mesh discovers and coordinates authorised devices for explicit replication and remote capabilities. Storage location, remote inference/execution and current connectivity remain separate, visible dimensions.',
 '''Show canonical device identity, ownership, online/offline/connecting status, advertised capabilities, relevant resource state and pending transfers.
 Pairing, permission, unavailable device and conflict/retry states must be visible. Settings and Home use the same device roster.''',
 '''Use authorised device pairing/trust and the source-defined transport/discovery contracts. A reachable device does not automatically expose every folder, model or runtime.
 Replicate explicitly scoped conversations/resources with manifest/revision awareness. Do not silently transfer Temporary conversations or private sources.
 Route remote tasks only when their capabilities, current policy and runtime availability satisfy the request.''',
 '''Persist DeviceID, trust relationship, scoped capabilities, replica manifests, revision/transfer state and Task/Run references.
 Device reachability is transient; ownership and permissions are durable configuration.''',
 '''Revocation prevents new access and reconciles pending transfers safely. Offline peers must not be shown as current authoritative copies without freshness information.
 Surface conflicts, retry and uncertain outcomes rather than overwriting independently edited replicas.''',
 '''Test pairing, scoped resource sharing, disconnect/reconnect, conflicting revisions, revoked device and remote execution refusal.
 Verify the UI distinguishes storage replication from a model running on that device.''')

M('browse','Browse and the native/browser tab host','platform cui go-search',
 'Browse provides real web browsing and the shared native-app/browser host. Tabs may contain app surfaces or authentic web engines while preserving isolation, history, profiles and canonical app state.',
 '''Use the current compact icon rail on New Tab. Keep Apps, Dashboard, Models and New Tab discoverable semantic destinations.
 Distinguish native app tabs from web tabs with appropriate address/navigation controls. The default selected tab treatment is the authoritative light surface with dark text.
 Global model and Notifications buttons open anchored canonical dropdowns; they must not be simulated copies with conflicting state.''',
 '''Retain Gecko/Blink engine responsibilities, profiles, browsing controls, downloads and isolation from the original technical contract.
 App tabs open the real app and canonical artifact, not a reduced webpage illustration. Closing/restoring tabs preserves drafts and background-task semantics.
 New Tab search uses Go/search contracts and explicit default versus AI mode. Address navigation and command invocation remain distinct.''',
 '''Persist TabID, tab type, target artifact/URL, engine/profile, history and allowed session/view state. Separate web sessions from app permission/context stores.
 Restore tabs without replaying submitted commands or external actions.''',
 '''Enforce browser-origin/profile isolation and per-surface permission boundaries. Embedded pages cannot inherit internal app capabilities by appearance.
 Handle unsaved native tab close, crashed engine, failed navigation and unavailable artifact with genuine recovery.''',
 '''Exercise native/web tabs, engine history/profile, New Tab rail, anchored dropdowns and closed-tab restoration.
 Verify focus/address routing, no native-tab fake URL assumptions and no loss of unsaved app work.''')

M('admin','Admin — organisation scope and policy','cui identity permissions models',
 'Admin manages organisation users, roles, resources and policies with explicit organisation scope. It shows policy delivery and enforcement state rather than treating a saved policy draft as universally active.',
 '''Keep organisation identity/scope visible above user, role, policy, device and resource controls. Show members/roles in a useful table with concrete status.
 Distinguish draft, published, delivered, acknowledged and failed policy states; expose affected scope and current policy source.''',
 '''Manage membership, role-based capabilities, allowed models/providers, Plugins/MCP, shared resources and per-organisation limits using retained server contracts.
 Preserve organisation resource pools/ceilings and the separate Business/Business+ entitlement semantics. Do not conflate seat limits with model/storage allocation.
 Policy changes expose an inspectable effect/target set and conflict handling; auditing records who changed what.''',
 '''Maintain OrganisationID, UserID/membership, RoleID, PolicyID/revision, assigned scope, acknowledgement records and audit event IDs.
 Server-authoritative billing/resource state is independent from local draft policy.''',
 '''Require the actual administrative grant for each operation. Admin views must not expose private personal conversations/memory merely because the user manages an organisation.
 Failed delivery or offline devices remain visible and retryable without a false enforced status.''',
 '''Test role restrictions, policy draft→publish→device acknowledgement, revoked membership and mixed delivery outcomes.
 Verify personal/org switching cannot leak cross-scope private data.''')

M('files','Files and ten organisational folder views','cui persistence permissions integrations',
 'Files is the mature file manager and canonical resource browser, supporting local and authorised remote providers. Its organisational views are alternative structures over real content and references, with a normal contents fallback.',
 '''Use navigable locations, breadcrumbs, search, list/grid, selection, details/preview and genuine file operations. Maintain comfortable padding between item text, selection backgrounds and table edges.
 Support Folder, Project, Sheet, Tree, Smart, Timeline, Pipeline, Catalogue, Stash and Matrix. Each view exposes its own useful workflow and can return to normal contents.
 Folder/Project use ordinary hierarchy/sections; Sheet exposes structured fields; Tree supports hierarchy; Smart shows a scoped query; Timeline shows dated placement; Pipeline shows stages/gates; Catalogue structured records; Stash ordered collection; Matrix stable row/column axes.''',
 '''Use provider-native create/open/import/rename/move/copy/delete/restore capabilities with truthful availability, operation progress and conflicts. Upload or reference is explicit.
 Organisational membership usually references canonical source artifacts; adding an item does not move/copy it unless the chosen operation says so.
 Tree has a visual editor and a read-oriented View; Sheet tools open in the default popup with Open original and permission-aware context. Pipeline movement respects actual stage/gate rules.
 Smart queries remain constrained by source scope; Matrix cells use stable axes and references, not fragile display positions. Removing collection membership is distinct from deleting the file.
 Preview and Open in App resolve the real owning app. File type, source owner, revision and sync state remain inspectable.''',
 '''Persist FileID/provider ID, canonical path/resource, revision, metadata and operation receipts. Organisational view configuration, query, membership, axes/stages and ordering have stable IDs.
 Keep preview/index/cache records derived from permission-aware sources; preserve offline changes and pending operations separately from confirmed remote state.''',
 '''Recheck access on preview/open/export/move. Missing, revoked, unsupported, conflict and quota conditions offer safe recovery.
 Do not treat a file reference in a Space/Binder/Board as authority to mutate or relocate the underlying file.''',
 '''Exercise local and remote list→preview→open/edit→rename/move recovery with canonical identity. Test keyboard multiselect and selection padding.
 Test all ten organisation types, normal fallback, non-destructive membership removal, denied source and persisted configuration after reopen.''')

M('connect','Connect — conversations, contacts and Mail','cui identity integrations permissions persistence',
 'Connect unifies real communication channels, contacts and a full Mail experience while preserving the originating provider/channel/thread. It is not the generic external-integration manager.',
 '''Use channel/account navigation, conversation list and active conversation; Mail uses a genuine folder/list/message-or-compose layout with detailed drawers as needed.
 Show provider identity, actual connection state, unread/sending/queued/failed status and attachment metadata. Assistant contacts remain identifiable configured identities.''',
 '''Support canonical contacts and threaded communication, search, attachments and channel-specific capabilities without flattening all providers into one fake protocol.
 Mail retains MIME/header handling, folders, search, rules, attachment, reply/forward/draft/send, native engine and provider requirements from the source.
 Assistant replies and delegated work stay in their originating channel/thread unless the user explicitly changes the target. Drafting is distinct from sending.
 Offline send queues and retries use current grants and idempotency; a queued message is not reported delivered.''',
 '''Persist connection/account, ContactID, ConversationID/thread, message IDs, source provider references, draft revision and delivery receipts.
 Attachments refer to their canonical resource/bytes with provenance; local preview does not prove remote delivery.''',
 '''Use current provider and app permissions, recipient identity and explicit send authority. Revocation/expiry blocks new remote actions while preserving draft recovery.
 Handle partial attachment upload, invalid recipient, send uncertainty and reconnect without duplicate messages.''',
 '''Test Mail compose/save/reopen, reply in correct thread, attachment failure, offline queue and duplicate retry.
 Verify Assistant contact handoff preserves source conversation and distinguishes a draft from an actual send.''')

M('spaces','Spaces — conversations, workspaces and custom Space Builder','cui dulche node-editor assistants subagents generative-ui persistence permissions models vision-voice',
 'Spaces is the conversation and task workspace for Chat, Tasks, Study, Assistant conversation views, Research, Shopping, Translate and Experiences, plus user-created Spaces. Each Space has a real domain workflow. Global Assistants/Specialists retain canonical identities outside Spaces, while their conversation views and work remain accessible here. This chapter is a copyable implementation unit; its prerequisite contracts define shared services, and the retained technical chunks preserve the full baseline and confirmed companion.',
 '''The far-left rail switches stable Space destinations and includes Manage Spaces; support the existing rail/dropdown preference, pinning, reorder and discovery. The contextual sidebar is independently collapsible/customisable with Space Overrides > User Preferences > Default precedence.
 Preserve active conversation, draft, selection, scroll, voice/session and Task/Run across Space switching. On narrow screens use responsive drawers with a durable reopen affordance rather than squeezing desktop columns.
 Chat is a Space, not a separate app. Default Chat sidebar sections may include New Chat, Chat Groups, Specialists and Recent Chats; they are defaults, not a fixed mandatory ordering.
 Keep conversation readable with distinct user/AI provenance. Plain Text/Speech Bubbles and alignment remain preferences. The current Professional wrapper and authoritative slides govern current chrome, not the older later-slide diagrams.
 The compact/expanded composer has + attachment/resource access, editable draft, focus-aware Aa and native emoji, model/effort context and state-appropriate send/voice controls. Keep attachment/plugin chips identity-backed, inspectable and scoped.
 Place a Conversation permissions icon in the composer; it is not the editor Read-only/Write switch. Its panel shows files/apps/web/memory scope and links to Manage app permissions.
 The contextual option shelf shows at most five current choices plus a peek, navigation/dots and truthful selection; browsing choices does not submit the draft.
 Idle controls expose Send/Voice; an empty disabled Send still permits Voice. Running work exposes Steer, Stop, Queue and Voice as supported; paused work exposes Resume/End Pass and approval-held work shows the actual pending decision.
 Summary is an optional floating top-right widget with Activity, Outputs and Sources. It must not become a permanent fourth column or shrink the conversation; keep required controls uncovered.
 Voice mode is a floating icon-first pill within Chat over the same canonical Vision & Voice session. Include mute, screen sharing, camera, captions, pause/resume, end, current voice/mode and more settings with permission indicators.
 Optional Tab View hosts actual conversations, Browse pages, CUI apps, artifacts and safe GenUI. Spaces itself remains a tab; hiding the strip does not destroy tabs.
 Show high-level thinking/work summaries, subagent task tree/detail, typed interactive output and live semantic Present/Canvas work. Completed/failed/approval-held states need their own truthful controls and artifact receipts.
 Generated-file cards show name, owning app, revision, format and durable availability, with Open and eligible Download. Failed export shows the saved native artifacts and targeted retry.
 Custom Space Builder has freely switchable Basic, Advanced and Expert tabs of one canonical Space. Basic/Advanced use a cohesive borderless configuration document; Expert uses Boards-style navigation for Details, Harness Logic, Model Logic, User Message Logic and Files.''',
 '''Submit only after admission succeeds; keep the draft when validation/admission fails. Steer affects future work, Queue preserves the queued message, and Stop reports actual stop acknowledgement rather than rewriting past outputs.
 Respect per-Space default Steer/Queue policy and Alt+Enter inversion where the companion specifies it. A queued message must not be silently interpreted as a retroactive edit or immediate tool invocation.
 Resolve +, @ and slash commands through canonical registered resource/invocation inventories. Passive mentions are references; explicit bounded commands execute only through real permission-aware APIs. Rich formatting and per-message Canvas composition preserve original message history.
 Model effort labels bind directly to supported provider level IDs; they are neither quota nor confidence. Separate context and provider usage metrics, show unknown values honestly and hide inapplicable cloud allowance for local routes.
 Chat Type and Chat Location are independent dimensions. Types are Normal, Multi-Agent and Temporary; locations are Local, Mesh and Cloud. Model execution location is separately governed from conversation storage.
 Support approved conversions Temporary→Normal, Temporary→Multi-Agent and Normal→Multi-Agent without replaying tools or fabricating past participant replies. Temporary first becomes a durable local chat; Mesh/Cloud promotion is a separate authorised action. Unapproved reverse conversions are not silently added.
 Chat Groups contains Group and Project organisation containers, not multi-agent chats and not new Spaces. Both reference canonical member conversations; Group adds shared instructions/relevant authorised context; Project additionally adds Sources, Rules and local/server/remote/cloud resource connections.
 Group→Project is a one-way supported conversion preserving identity/history/members where safe; Project→Group is rejected. Removing a container does not silently delete its member chats.
 Tasks defaults to the user’s own relevant task scope with explicit All Tasks filtering. Show objective, plan, steps, dependencies, blockers, approvals, source/outputs, real state and canonical task badges with accessible detail.
 Study provides dashboard→subjects→subject/topics, source-backed tutor/practice and recorded progress. Revision Bank exposes Recommended, All, Flashcards, Notes, Papers and Quizzes, using Cards and Forms canonical artifacts. The older concept is a rough approved behaviour/layout reference under the current wrapper.
 Assistant views open the global system’s permanent Main thread, additional conversations, Teams and work. Specialists launch the correct owned/available configured identity. These views do not replace Home/editor access to the global systems.
 Research moves from initial conversation to reviewable plan, scope, sources and selected deliverables before Start. Quick/Extended/Deep/Custom modes use actual supported budgets; active execution time excludes waiting/paused periods and is not a completion guarantee.
 Shopping combines natural-language control and conventional filters, distinguishes Required from Preferred constraints and exposes strict matches, alternatives/near matches and contextual recommendations with explicit reasons. Keep products separate from merchant offers, currency/price/freshness/source provenance visible, and purchase actions external and authorised.
 Translate keeps language chips above the standard composer and one response bubble per target language, linked as sibling outputs in a TranslationSet. Route through real translation/model capabilities.
 Experiences owns persistent world/scene, character, inventory, evidence, objectives, flags, checkpoints and branching state. Commit validated canonical state before narrating its outcome, enforce hidden-state visibility and retain recoverable checkpoints. Formal competition remains Play’s domain.
 Basic configures name, description, icon, instructions, supported category availability and resources. Advanced adds Mandatory/Allowed/Blocked/Inherited capabilities, memory, genuine model parameters, Assistant/Specialist defaults, canonical Automations, theme/sidebar/message-control overrides and prompts.
 Expert reuses the Home-owned Node Editor. Harness Logic configures permitted lifecycle/composer/sidebar/GenUI behaviour; Model Logic configures authorised routes/prompts/parameters; User Message Logic operates on derived representations without rewriting the immutable original message; Files is a real nested resource system.
 Switching Builder tabs must preserve Expert-only nodes/behaviour and show Expert configurations active where simplified views cannot express it. Preview, Validate and Activate/Create are distinct; invalid drafts never replace the valid active graph. Templates and AI-assisted edits are inspectable starter changes, not implicit installed connections or automatic publication.
 Typed GenUI is safely schema-bound and stateful. Open in App retains canonical identity. Live artifact work uses the owning app’s semantic edit APIs and any required review/approval rather than raw pointer simulation.
 Subagent activity exposes delegation objective, current state, scope, blockers, outputs and high-level work events. Useful thinking summaries may show observed steps/tools/evidence but never private internal reasoning.''',
 '''Persist SpaceID, configuration Draft/Active revisions, sidebar item IDs, ConversationID, immutable MessageID/history, group/project IDs and memberships, TabIDs, TaskID/RunID and attachment/source references.
 Temporary chats remain local and do not automatically create/reinforce durable memory or transfer to Mesh/Cloud. Explicit conversion preserves history where safe. Open questions about optional existing-memory reads remain explicit until the owner resolves them.
 Storage Local/Mesh/Cloud uses canonical LE-07 entitlement, quota, encryption, device, replication and conflict contracts. Choosing Local storage does not assert local inference.
 Keep original full conversation history when context is compacted; summaries are derived with source provenance and eligibility. A context-window optimisation must not silently delete messages or output history.
 Domain records remain owned by their actual apps/services: Cards/Forms study assets, Planner scheduling, Present/Canvas documents, Files resources, Automations triggers and global Assistant identities. Space folders/reference cards do not clone or move those objects.
 Persist generated output artifacts and verified export manifests by canonical revision. A preview or completion sentence cannot create a downloadable file.
 User-created Space settings and all three Builder projections share one SpaceID, graph, permissions, resources and history. Source references and imported packages retain canonical owner/version/trust metadata.''',
 '''Conversation permissions narrow current use within durable grants; they cannot elevate app/org policy. Every consequential action still requires its applicable action approval, even if a category is available.
 Voice toggle never silently enables camera/screen sharing. Persistent indicators reflect actual capture; Stop sharing must stop frames reaching the runtime. Open in Vision & Voice preserves the same session.
 Group/Project sharing, multi-agent participation and context retrieval preserve provenance and participant/source permissions. Do not assume detailed turn-taking/human participation policy from the mere presence of a Multi-Agent label where source decisions remain open.
 Builder validates mandatory-but-blocked conflicts, unavailable dependencies, typed ports, cycles/budgets, model capability, CUI safety and effective policy. Imported Skills/Plugins/MCP/models/mini apps are validated through their owners, never auto-trusted by folder membership.
 An invalid generated component preserves prior valid content and offers repair. A failed PDF/export preserves the saved native artifact and retries only that export from the selected revision; repeated retry must not duplicate earlier external effects.
 Tab close/back respects dirty state and preserves background runs. Revoked resources are redacted from cached display/retrieval; unavailable contexts show recoverable state without discarding drafts.
 Peer Review limits count aggregate reviewer active time against measured productive worker active time, not wall-clock waiting or duplicated simultaneous worker clocks. Exhaustion never bypasses a required permission/review gate.''',
 '''Create a Chat, attach a permitted canonical source, type rich content, switch Space/sidebar/tab and return with the same draft/focus. Verify failed admission retains the draft and queued/steered work follows the configured semantics.
 Test Normal/Multi-Agent/Temporary, all approved conversions, denied reverse conversion, Local/Mesh/Cloud eligibility, temporary memory/transfer restrictions and no tool replay.
 Create Group and Project containers, share scoped context, convert Group→Project and reject downgrade; remove a container without losing member conversations.
 Exercise Study topic→tutor/practice→Revision Bank/Cards/Forms, Research review/start/results, Shopping Required/Preferred filtering, Translate sibling language outputs and Experiences checkpoint recovery.
 Open global Assistant Main work in Spaces and an editor; verify identity, grants and task references match. Exercise a child task blocked on approval, parent cancellation and partial sibling outputs.
 Interact with a typed GenUI control, watch actual semantic Present/Canvas changes, open the same saved artifact and download only verified bytes. Force export failure and recover without losing native content.
 Test the floating Summary and Voice pill together with no blocked controls; camera/screen sharing require explicit grant and ending them stops runtime capture. Verify accessible keyboard/touch/narrow layouts and reduced motion.
 Switch Basic/Advanced/Expert repeatedly after creating Expert-only logic; verify the same SpaceID, graph and draft/active revision, inspectable conflicts, genuine nested resources and safe preview/activation.
 Use independent real runtime evidence for acceptance. These mock-ups and primary rules do not assert that any Space is complete or that backend acceptance has passed.''')

M('write','Write — document authoring','cui productivity-engine shared-ai persistence',
 'Write is a full document editor with a restrained contextual shell. Continuous, Pages and Book are genuine presentations of the same document, retaining substantive Writer/donor capability and canonical document semantics.',
 '''Use Home/title/save-state header, nested Sections outline and one contextual floating formatting toolbar. Persistent insert, view/eye and more controls remain discoverable; do not add an Office-style ribbon.
 Continuous is the default flowing document view. Pages shows real page boundaries and pagination; Book shows the supported facing-page reading/editing arrangement. Selecting a layout does not create a different document.
 Provide selected text/object, insertion picker, typing, comments/history, formatting/property and collaborator review states. Keep the AI bar separate from page content and bottom controls.''',
 '''Support rich text/styles, structured sections, tables, objects, references, fields, comments, tracked suggestions, pagination and exports through the retained full engine contract.
 Edit/Suggest/View, layout lock and AI Read-only/Write are independent controls. A locked layout is not necessarily a read-only document, and AI Write does not bypass suggestions or document permission.
 Paragraph/character formatting acts on the actual selection with inspectable defaults. Caret/focus and undo survive toolbar/menu use.
 Global Assistant/Specialist collaborators create typed revision-bound changes with review, accept/reject and clear authorship.''',
 '''Persist DocumentID, section/block/object IDs, text ranges, styles, revision, comments/suggestions and layout configuration. Layout view state is separate from authored content.
 Keep native document content authoritative; previews and exported PDF/other formats are derived from a known revision.''',
 '''Respect document and source permissions. A stale collaborator proposal requires reconciliation rather than overwriting intervening edits.
 Autosave/reopen and failed export preserve the native document and meaningful undo/history.''',
 '''Test typing/selection/formatting/insertion, nested Sections and switching Continuous/Pages/Book with unchanged content.
 Verify Edit/Suggest/View versus AI scope, locked layout, collaborator review, page/export correctness and recovery.''')

M('present','Present — slide authoring and presentation','cui productivity-engine node-editor shared-ai persistence',
 'Present provides full slide authoring, object editing, actions/animation and presentation playback. The Professional annex and authoritative slide 16 define its clean three-tier editing shell and object-selection behaviour.',
 '''Use document header, File/Edit/Insert/Slide/Arrange/View/Help menu row and a single floating contextual toolbar. Thumbnails occupy the left, aspect-correct slide dominates, notes sit below and an optional right inspector follows selection.
 Object selection shows eight resize handles and rotation; text editing shows a real caret/range. Do not confuse selected shape with text-entry mode.
 Include Shapes, custom shape creation/editing, Actions and presentation/playback states. Custom shapes need real path/geometry editing rather than a static gallery label.
 Presenter view keeps notes and next-slide/control information private from the audience display. Notes are editable content, not a decorative placeholder.''',
 '''Maintain slide order/layouts/masters, text/shapes/media, object alignment/arrangement, notes, animation, action graph and playback through canonical semantics.
 Action configuration identifies trigger, target, effect, transition/navigation or permitted external action and preview behaviour. Use the shared Node Editor where source-defined graph authoring applies.
 Preserve pointer transforms at zoom and viewport changes, selection geometry, keyboard nudge, proportional resizing and real undo.
 Global collaborators expose typed changes to this deck/selected slide with review and expected revision. Live work from Spaces opens the same deck.''',
 '''Keep DeckID, SlideID, object IDs, geometry, text/style, note content, action/animation definitions, revision and playback state distinct.
 Export and audience rendering use the chosen slide size and content revision; app theme never retints authored slides.''',
 '''Preview/presentation mode must not grant document-write or external-action permission. Publishing/exporting remains a separate operation with truthful state.
 Preserve native deck when an export fails; reconcile stale collaborative changes and recover unsaved selection/edit state.''',
 '''Test selection versus caret, all handles/rotation at zoom, slide reorder, notes, custom shape edit and Actions configuration.
 Verify presenter notes remain private, authored colours survive all themes, and live Spaces work/collaborator review references the same deck revision.''')

M('boards','Boards — seamless pages, blocks and ink','cui productivity-engine shared-ai persistence',
 'Boards is a native flexible document workspace combining structured blocks, free placement and ink with nested pages. It must work locally without a Files account or connected cloud provider.',
 '''Keep a compact board identity/breadcrumb/switcher, nested page tree, bottom Add/avatar and a contextual top floating toolbar. Expose independent Edit/View, Locked and Snap controls.
 Represent page creation/type choice, page options, insertion-between-blocks, typing/caret, selected block/drag affordance, free ink and active tool-options states explicitly.
 Use a seamless authored surface rather than nesting an unnecessary second app canvas or presenting the page as disconnected generic cards.''',
 '''Add, rename, nest, reorder, duplicate and remove pages with correct source semantics. Page types embed real canonical app surfaces where supported.
 Insert and edit text, lists, media, table/linked blocks, absolute objects and ink with unified selection/undo. Insertion lines and drag grips identify actual block boundaries.
 Locked controls constrain movement/layout as defined; View prevents editing; Snap changes manipulation behaviour. Do not collapse all three into one read-only toggle.
 Native .9to1board files may live at permitted locations. Local creation and editing must not require sign-in, a mounted Files provider or AI.
 Contextual AI and collaborators use typed board/page/block changes with visible review and source ownership.''',
 '''Persist BoardID, nested PageIDs, block/object/stroke IDs, order/layout coordinates, page type, embed references and revisions.
 Store local draft/save and optional external file/sync state separately; embedded source artifacts retain their owning identity.''',
 '''Detach/remove references without silently deleting the original artifact. Shared-source permission changes show safe placeholders.
 Recover typed and ink changes after interruption; stale AI edits must not overwrite concurrent page/block changes.''',
 '''Test local create/open, page Add/type menu, page options, block insertion/typing/drag, pen options and undo/reopen.
 Verify Edit/View, Locked and Snap independently, keyboard navigation, canonical embeds and no Files account dependency.''')

M('canvas','Canvas — drawing, objects and semantic composition','cui productivity-engine shared-ai persistence',
 'Canvas is a real drawing/composition workspace with shared object, vector/raster and ink semantics. Its dominant work surface uses a compact top identity and a focused bottom tool system.',
 '''Use bottom Select, Pan, Pen, Eraser, Insert, Tools and AI controls, with one contextual options row for the active tool. Keep compact name/menu/quick settings above the work surface.
 Show object selection, pen width/colour, insert and contextual AI states. AI appears above the bottom toolbar and must not cover active controls.
 Live Spaces work shows semantic object/stroke progress on the same Canvas artifact, not a screenshot replacing the editor.''',
 '''Support actual object/stroke selection, grouping, geometry transforms, connectors, drawing/erasing and source-defined hybrid raster/vector operations.
 Tool changes preserve selection and undo; coordinate transforms stay correct at pan/zoom and responsive dimensions.
 Semantic AI edits use selected object/context IDs with preview/approval and preserve authored content colours across themes.''',
 '''Persist CanvasID, object/stroke/layer IDs, geometry, z-order, grouping/connector relationships, styles and revisions.
 Viewport pan/zoom and transient tool settings are separate from authored state and generated export bytes.''',
 '''Enforce artifact/embedded source permissions and revision checks. A failed render/export leaves the native canvas recoverable.
 Reconnect to live work without duplicating strokes/objects or replaying already-committed actions.''',
 '''Test drawing, selection/grouping, erase, connectors, transforms and undo at several zoom levels.
 Verify toolbar/AI separation, semantic live work from Spaces, theme-stable art and saved/exported revision identity.''')

M('data','Data — worksheets, tables and explicit AI approval','cui productivity-engine persistence shared-ai',
 'Data is a full spreadsheet/table system with formula/dependency behaviour, views and an explicit AI mutation review boundary. It must never turn narrated calculations into unverified cell results.',
 '''Use genuine worksheet grid, row/column headers, selection/ranges, name/formula bar and sheet tabs. Properties/filter/view controls follow the current table or range.
 The shared AI/SQL bar makes its mode and target explicit. Show proposed range/cell changes and action-specific approval before every AI mutation.''',
 '''Retain formulas, dependency graphs, recalculation, tables, structured fields, filters, sort, views, charts and donor-compatible operations from the technical contract.
 SQL context and supported target/runtime are explicit; a native command, query and AI-generated mutation are separate operations.
 Every AI mutation requires approval for the concrete action/change set, even when a broad Write control or earlier plan is present unless the retained explicit grant contract covers that exact action.
 Formulas and previews use real evaluated results, typed error states and known locale/units; do not invent successful recalculation.''',
 '''Persist workbook/table/sheet IDs, cell/range references, formula graph, table schemas, view/filter configuration, revisions and approval/change receipts.
 Keep derived chart/render/export state linked to a known data revision.''',
 '''Validate permissions, expected revision, range scope and data constraints before applying. Rejected proposals leave data unchanged.
 Handle formula errors, conflicting edits, failed imports and interrupted export with recoverable native state.''',
 '''Test edit/formula→recalculate, table/filter/view, multi-cell selection and import/export fidelity.
 Verify an AI change is only proposed before approval, reject is non-mutating, accept records exact cells/revision, and stale proposals fail safely.''')

M('planner','Planner — calendars, burrows and tasks','cui persistence permissions shared-ai',
 'Planner combines real calendar scheduling, tasks and burrow organisation with timezone/recurrence semantics. Burrows organise canonical work rather than duplicating events into independent stores.',
 '''Provide day/week/month or retained supported views, time grid, event/task details and a clear calendar/burrow list. Event cards use a colour nib and readable interior padding.
 Show selected event, create/edit panel, recurrence/reminder details, invitation/status and conflict states. Keep AI proposal context separate from committed calendar entries.''',
 '''Create/edit/move scheduled events and tasks with actual timezone, duration, recurrence/occurrence and reminder contracts. Editing one occurrence versus a series is explicit.
 Burrow membership references canonical tasks/events/resources; removing membership does not delete source work.
 AI scheduling produces a reviewable proposal with target calendar, dates/times, conflicts and applicable action approval. Do not infer schedule success from a decorative card.''',
 '''Persist EventID/task IDs, CalendarID, BurrowID, timezone, recurrence rule/exception, attendee/reminder state and revisions.
 Provider sync/delivery status stays separate from local save and proposed edits.''',
 '''Respect calendar/provider permissions, invite/send scope and current recurrence revision. Handle offline operations and conflicts without duplicate invitations.
 Surface ambiguous time/duration, unavailable provider and failed reminder/sync states.''',
 '''Test event create/move/edit, occurrence versus series, timezone changes, reminders and burrow reference removal.
 Verify readable nib/padding, action-reviewed AI scheduling and local/provider recovery.''')

M('automations','Automations — graph authoring and durable runs','cui node-editor dulche permissions persistence',
 'Automations owns durable workflows, schedules, triggers and execution history through the shared Node Editor. Other apps bind to this owner rather than creating competing hidden schedulers.',
 '''Provide workflow list/launch, graph editor, node palette, inspector, validation, draft/active status and run history. Runtime highlighting reflects actual execution.
 Nodes expose trigger/condition/action/input/output and error/retry information appropriate to their typed schemas.''',
 '''Support source-defined triggers, schedules/timers, conditional branches, parallel work, approvals, retries and typed app/tool actions.
 Validate graph/dependency/policy before activation. Editing a draft does not silently change an active run; activation identifies the new revision.
 Schedule and trigger semantics use canonical clocks/timezones/deduplication, with honest missed/offline-run recovery.
 Space slash commands/sidebar actions and Assistant schedules reference canonical AutomationID/revision rather than duplicating the graph.''',
 '''Persist AutomationID, GraphID/nodes/ports, Draft/Active revisions, trigger bindings, schedule state, run IDs, checkpoints and action receipts.
 Keep node view layout separate from execution semantics and retained audit history.''',
 '''Every execution inherits effective grants and budget; graph activation cannot bypass external-action approval. Revocation blocks future affected steps.
 Retry from safe checkpoints with idempotency and clear partial effects; rollback does not undo external actions already committed.''',
 '''Author/validate/activate/run a real workflow, inspect a blocked approval, retry failure and recover after restart.
 Test wrong port types, cycles, missing resources, offline schedule, concurrent trigger deduplication and permission revocation.''')

M('forms','Forms — builder, logic and responses','cui persistence permissions node-editor',
 'Forms provides a complete question/form designer with validation, branching and response review. Study and Play may reuse canonical Forms artifacts rather than creating incompatible quiz stores.',
 '''Show question outline, authored form preview, selected question properties, logic/validation and response/results views. Author, preview/test and respondent states are distinct.
 Each supported question type has real answer/constraint controls, required state and accessible labels.''',
 '''Add/reorder/duplicate questions and sections, configure answer types, constraints, defaults, branching and scoring where supported.
 Validate conditions and regular expressions safely; offer an actual tester with sample input and clear result/error. A configured condition must resolve against stable question IDs.
 Publishing/collecting responses uses defined audience/access and version behaviour. Preview/sample submissions are labelled and not silently counted as real responses.''',
 '''Persist FormID, question/section IDs, schema/revision, logic rules, published version, response/attempt IDs and provenance.
 Separate author draft from respondent answers and per-attempt progress.''',
 '''Respect response privacy and sharing scope. Changing a published form handles existing submissions/version compatibility explicitly.
 Invalid logic or validation must not activate; failed submission offers retry without duplicate recorded responses.''',
 '''Test builder→preview→publish/eligible response→results, question constraints, branch rule and invalid regex.
 Verify canonical reuse in Study/Play, keyboard accessibility and interruption/retry without duplicate response.''')

M('cards','Cards — sets, editing and study review','cui persistence permissions',
 'Cards owns reusable flashcard/card sets and the direct edit/flip experience. Study links to canonical sets and records private review evidence without duplicating authored card content.',
 '''Present the focused card with neighbouring cards offset visibly; vertical stack is the default, with supported horizontal alternative. Keep the front/back relationship clear.
 In Edit, click edits content and Flip is a separate control. In View/review, clicking flips and cannot edit the source. Show set/card navigation and actual review position.''',
 '''Create/edit/reorder cards and sets with front/back content, media and source references according to the retained contract.
 Switching Edit/View preserves focused card and authored draft; review outcomes attach to the reader’s progress rather than modifying the set for every reader.
 Cards used by Study open the same canonical set. Keep grading/review controls explicit and avoid fabricated retention/mastery metrics.''',
 '''Persist CardSetID, CardID, front/back content, order, source/provenance, revision and separate reader review/attempt records.
 Stack orientation/focus/view state is distinct from card content.''',
 '''Read-only sets prevent editing while allowing permitted review. Revoked source media shows an honest unavailable state.
 Recover partially edited cards and private review progress without duplication.''',
 '''Test click-to-edit versus click-to-flip, front/back changes, neighbour offsets, keyboard navigation and edit/view switching.
 Reopen a Study-linked set and verify canonical content plus separate per-user progress.''')

M('picture','Picture — image and layer editing','cui productivity-engine persistence shared-ai',
 'Picture is a full image editor with source-defined raster/vector, selection, layer, mask and adjustment capabilities. The CUI shell wraps the real engine and retains its substantive editing power.',
 '''Use a dominant image canvas, purposeful tool rail and layers/properties context. Selected layer, selection marquee, crop/transform and adjustment controls must correspond to actual document state.
 Show destructive/non-destructive operation, generated layer and pending/failed export states clearly.''',
 '''Support layer ordering/visibility/opacity, masks, selections, drawing, vector/raster transforms, crop/resize and adjustments from the retained donor contract.
 AI edits are typed, revision-bound proposals or generated content with preserved original/source metadata and meaningful undo.
 Export exposes actual format/colour/transparency and dimensional constraints; a preview thumbnail is not exported bytes.''',
 '''Persist image/document ID, layer/mask/selection/vector IDs, pixel resources, transforms, adjustments, colour metadata and revision.
 Derived previews/proxies and export records reference the native content revision.''',
 '''Respect source image ownership and current edit grants. Preserve native layers when a derived export fails.
 Recover interrupted processing and prevent stale AI results from overwriting intervening edits.''',
 '''Test selection→layer/mask/adjustment→transform→undo and native reopen with identical structure.
 Verify transparent/format export options, source preservation, AI review and failed-render recovery.''')

M('wave','Wave — multitrack audio production','cui persistence permissions shared-ai',
 'Wave provides real multitrack audio recording/editing, effects and mixing. The UI’s waveform, meters, transport and rendered outputs must reflect the actual audio engine.',
 '''Use multitrack timeline, track headers, clip waveforms, playhead/time ruler, transport, selected clip properties and effect/mixer context.
 Show recording permission/device state, armed tracks, actual levels and pending/failed rendering. Range, playhead and whole-project actions remain distinct.''',
 '''Retain source-defined audio import, record, trim/split/move, fades, automation, effect/bus routing and mixing capabilities.
 Coordinate timeline units, sample rate and selection ranges precisely. Playback/recording does not silently commit source edits.
 Export/render uses a known time range and project revision; unknown duration or processing time must not be fabricated.''',
 '''Persist ProjectID, track/clip/resource IDs, time offsets, automation/effects/bus graph, routing, sample metadata and revisions.
 Source media, editable arrangement, playback state and rendered output are separate records.''',
 '''Microphone/device capture requires explicit permission and persistent indicators. End recording stops the actual engine capture.
 Recover missing media/devices, interrupted recording and failed render while preserving usable source clips and project state.''',
 '''Test record/import→edit range→effects/automation→play→render, clip/time alignment and undo.
 Verify capture shutdown, missing-device recovery and export receipts matching the selected range/revision.''')

M('motion','Motion — video, composition and animation','cui persistence permissions shared-ai',
 'Motion is the video/compositing and animation editor, retaining real media, timeline, keyframe and render semantics. UI polish must not reduce it to a static video preview.',
 '''Use source/media bin, composition preview, layered timeline/tracks, selected clip/keyframe properties and transport with readable frame/time units.
 Show proxy/source availability, active selection, trim/keyframe editing and export queue/error states.''',
 '''Support source-defined clip editing, compositing, effects, transitions, animation/keyframes, audio alignment and media management.
 Preview resolution/proxies and final export are distinct; render selected range with known project/frame settings.
 AI-generated/modified media is added through inspectable canonical edits and preserves original resources/provenance.''',
 '''Persist ProjectID, media/clip/layer IDs, timeline positions, frame/timebase, keyframes/effects, composition/revision and export jobs.
 Proxy/render caches are derived and replaceable; they must not become the only copy of original media.''',
 '''Respect source/resource access and current project revision. Missing media, unsupported codec and interrupted render are recoverable states.
 Reconnect render jobs without duplicating finished outputs or claiming an incomplete file is ready.''',
 '''Test timeline trim/move, keyframes/effects, source/proxy switch and output from the exact selected range.
 Verify native project recovery, missing-media relink, playback alignment and honest failed-export handling.''')

M('build-code','Build Code — development environment','cui persistence permissions shared-ai',
 'Build Code is the full code-development domain within Build, sharing canonical project state with Sites and Stacks. It retains real language, build, debug, test and UI-builder capabilities from the supplied technical specification.',
 '''Use project/file tree, editor tabs, genuine code area/gutter, run/debug controls and bounded diagnostics/output/terminal panels. Show autocomplete at the caret with applicable detail and keyboard selection.
 Diagnostics identify actual file/range/severity and a real problem list. Suggested fixes expose the target change rather than silently rewriting files.''',
 '''Integrate language services, completion, navigation, formatting, refactoring, diagnostics, build/test/debug and source control through real engines.
 CUI visual building and code are representations of the same application/project; bindings and generated edits must remain inspectable. Invalid hot reload retains the last valid preview with an error state.
 AI coding produces revision-bound change sets/diffs with validation and applicable approval. Terminal execution uses the actual selected session/target and remains separately authorised.''',
 '''Persist ProjectID, file/resource IDs, source revisions, diagnostics/build/test results and run/debug session references. Files remain canonical across Code/Sites/Stacks.
 Editor tabs/caret/folds/selection are view state; working-tree changes, commits and deployment artifacts are separate.''',
 '''Respect workspace/file permissions, secrets and tool execution scope. A valid patch is not automatic permission to run external commands or deploy.
 Recover dirty buffers and failed builds/tests without erasing user changes. Rebase/reconcile stale AI diffs against the current project revision.''',
 '''Test completion/diagnostic navigation, code→valid preview, invalid hot reload, build/test/debug and dirty buffer recovery.
 Verify patch review/accept/reject, actual file changes, target-aware terminal and shared project identity with Sites/Stacks.''')

M('build-sites','Build Sites — responsive site authoring','cui persistence permissions shared-ai',
 'Build Sites is the visual website domain of Build, sharing the project and source model with Code/Stacks. Responsive editing, content and publishing use real component/page data rather than a screenshot-only page canvas.',
 '''Use page/component outline, central responsive preview and a selected-element inspector. Show desktop/tablet/mobile viewport controls with clear active width and breakpoint.
 Properties distinguish inherited/base values from breakpoint overrides. Preview, edit and deployed/public state are distinct.''',
 '''Create and edit pages, reusable components, layouts, typography, assets, links/forms and source-defined CMS/content settings.
 Responsive changes apply to the selected property/breakpoint scope, preserve base/inherited values and allow explicit override reset.
 Code and visual changes target the same project model. Preview uses a real development environment and labels unavailable functionality truthfully.
 Publish/deploy identifies destination/domain, revision, validation and rollback state; production publication is a consequential separately authorised action.''',
 '''Persist ProjectID, page/component/element IDs, style tokens, breakpoint rules, asset references, content schema and revision.
 Preview session, build artifact, deployment revision and domain configuration are separate canonical records.''',
 '''Respect project, provider and deployment grants. A saved design is not a published site, and an authorised preview is not permission to deploy.
 Recover invalid builds, broken asset references and failed deployment without losing editable source or the last known-good published version.''',
 '''Test responsive property inheritance/override/reset, component editing and the same change visible in code.
 Verify preview versus publish distinction, selected revision, error recovery and explicit deployment target.''')

M('build-stacks','Build Stacks — Branch, Twig and Leaf source control','cui persistence permissions',
 'Build Stacks exposes the specified Branch/Twig/Leaf hierarchy, changes and promotion controls over the canonical Build project. It preserves the source-defined collaboration, freeze and Git interoperability semantics.',
 '''Show the hierarchical Branch/Twig/Leaf tree, selected branch metadata, changed files/diff and actionable Change Requests.
 Branch detail exposes author/owner, source/target revisions, freeze scope and state. Freeze controls show affected hierarchy, reason and release/expiry semantics.''',
 '''Preserve root authority/cascade, scoped freeze and reconciliation rules. Freezing a branch must not ambiguously freeze every unrelated project or delete working changes.
 Keep Reject, Accept without Merging and Accept and Merge distinct. Promotion/review uses the actual change request and target branch/revision.
 Support diff/conflict resolution, commit/history and source-defined Git interoperability without inventing independent file copies for every view.''',
 '''Persist stack/project IDs, Branch/Twig/Leaf identities, ancestry, commits/revisions, change requests, merge outcomes and freeze audit records.
 Working-tree edits remain distinguishable from committed/promoted revisions.''',
 '''Check author/role and protected-scope permissions before freeze, merge or promotion. Recheck target revision to prevent stale approvals.
 Recover conflicts and interrupted operations without silently accepting changes or losing uncommitted work.''',
 '''Test hierarchy navigation, diff/review and all three exact decision outcomes.
 Exercise scoped freeze/release, concurrent target change, merge conflict and canonical Code/Sites working-tree identity.''')

M('ai-studio','AI Studio — harnesses, agents and evaluations','cui node-editor dulche models permissions',
 'AI Studio authors and tests configured AI systems through real typed definitions. Graphs and forms are views of canonical versioned configurations; testing is distinct from activating or granting permissions.',
 '''Organise Home/Projects; Build for Harnesses, Agents and Tools; Test for Playground, Evaluations, Test Suites and Replay; Resources for Schemas, Model Routes, GenUI and Run Profiles; and Templates.
 Provide typed graph, property/permission inspector, version/draft state and test results with real inputs/outputs. Tool-permission detail lists exact allowed resources and action consequences.''',
 '''Build harness/Agent/Specialist and tool/Skill/Plugin/MCP definitions using registered schemas and shared services. Preserve supported node/form equivalence.
 Validate schema/types, models, dependencies and permission intersections. Test uses an explicit run profile, model route, inputs and live/mock distinction.
 Compare evaluations/test-suite results with provenance and failure detail. Do not equate a passing sample with production activation or independent acceptance.
 Training Lab remains the source’s explicitly deferred/undefined release scope; do not invent a complete training backend.''',
 '''Persist project/definition IDs, schema/configuration revisions, Draft/Active versions, test case/suite IDs, run/evaluation receipts and model/prompt provenance.
 Templates copy starter definitions with new identity where appropriate, not credentials or silent grants.''',
 '''Permission controls narrow capability use; testing a tool cannot authorise unrelated live effects. Live external calls require their actual grants/approval.
 Invalid drafts preserve the active version. Failed tests and missing tools remain inspectable and retryable without fake success.''',
 '''Author→validate→test→inspect failure→revise→activate a typed definition with stable identity.
 Verify tool-scope denial, live/mock labels, version rollback and matching graph/form representations.''')

M('terminal','Terminal — Command and AI modes','cui permissions persistence dulche',
 'Terminal is a real terminal/PTY application with explicit target, session and Command/AI mode. AI proposals are reviewable commands, not automatically executed prose.',
 '''Keep Command/AI switch, selected shell/profile, target/device and working directory visible. Use authentic terminal output, cursor/input and session/tab/pane controls.
 Command review shows the exact executable text, target, directory, proposed effect and explicit run/cancel controls. Visually wrapped text must not change command syntax.''',
 '''Retain real PTY, process, shell/profile, panes/sessions and source-defined remote capabilities. Copying selected text is not input to the shell.
 AI mode explains/proposes within scope; execution requires the actual action authority and target session. Paste/multiline policy remains explicit.
 Reconnect opens or attaches according to real session capability; do not pretend a lost remote process has resumed if it has not.''',
 '''Persist terminal profile/configuration and authorised session references; command/output history follows privacy policy and shell/runtime truth.
 Task/action receipts reference exact target/session/command and outcome where available; no invented exit code.''',
 '''Check target/cwd and command approval when running. Protect secrets in logs and review surfaces.
 Show disconnected, exited, denied, uncertain and failed states distinctly; retry must not duplicate an already-applied external effect.''',
 '''Test Command versus AI, select/copy/paste, proposed command review and execution into the correct real session.
 Verify multiline quoting, local/remote target changes, process exit and reconnect truthfulness.''')

M('mini-computer','Mini Computer — VM and remote desktop control','cui permissions persistence',
 'Mini Computer displays and controls a real VM/device session through the supported backend. Input ownership, resource configuration and close-versus-stop semantics remain explicit.',
 '''Provide authentic desktop viewport with visible target/session/runtime state. Show View only versus Control and the current input owner.
 Keep connection, scaling/full-screen, clipboard/file transfer, snapshot/resource and power controls discoverable without confusing window close with machine stop.''',
 '''Use the actual VM/viewer/input backend, supported guest capabilities and runtime versions. Control grants gate keyboard/pointer delivery.
 Configure resources and snapshots with defined powered-state constraints. Closing the viewer detaches; stopping/shutting down affects the machine and requires the corresponding action.
 Clipboard/file sharing and AI computer interaction are separately permissioned capabilities; a visible desktop does not grant either automatically.''',
 '''Persist machine/session IDs, configuration, supported runtime, disk/snapshot references, connection and input-ownership state.
 Viewer pan/scale/full-screen is transient; guest process and machine lifecycle continue according to runtime state.''',
 '''Respect device/VM scope and input ownership, handle lost control and disconnect visibly, and revoke input immediately when required.
 Recover reconnect and snapshot/stop failures without claiming the machine is stopped or unchanged until confirmed.''',
 '''Test view-only→control grant, input routing, revoke control, close viewer and explicit stop as separate outcomes.
 Verify resource/snapshot constraints, clipboard/file permission and disconnected recovery.''')

M('games','Games — scene and runtime development','cui persistence permissions shared-ai',
 'Games provides the source-defined game-development environment, with scene, asset, component, animation and runtime tooling over one canonical project. Runtime play state is distinct from authored source.',
 '''Use scene hierarchy, viewport, component inspector, assets and timeline/animation context. Development, Creation and Rendering workspaces are coordinated views of the same project.
 Show edit/play/runtime and debug/build/export states clearly, including selected object and active component properties.''',
 '''Retain full scene/entity/component, asset, scripting, physics/interaction, animation, debugging and build/export capabilities from the technical contract.
 Editing controls modify source; runtime simulation changes do not automatically save back into the authored scene. Explicit apply/copy-from-runtime semantics require review where supported.
 AI work produces typed project changes and real validation/build receipts, not an invented playable preview.''',
 '''Persist ProjectID, scene/entity/component/asset IDs, source revisions, animation data and runtime/build session references.
 Generated assets and build artifacts retain provenance and selected source revision.''',
 '''Respect project/resource and execution permissions. Unavailable engine/plugin/runtime capabilities must be reported rather than hidden.
 Recover compile/runtime failures and unsaved authoring state while keeping the last valid source/build distinct.''',
 '''Test scene selection/component edit, animation, play/stop, runtime/source separation and actual build failure/success receipts.
 Verify workspace switching preserves project identity and no runtime changes silently overwrite authoring.''')

M('play','Play — structured games and competitions','cui dulche permissions persistence',
 'Play runs formal games/competitions with contestants, matches, rounds, legal actions, rules, timing and scoring. It is distinct from Games development and from narrative Experiences in Spaces.',
 '''Expose match identity, participant/contestant roles, current round/turn, permitted actions, actual clock/state and score/result provenance.
 Human and AI participation is visible with appropriate private/hidden information boundaries.''',
 '''Enforce canonical rules, turn order, legal moves, timers and scoring before presenting an outcome. Narration must follow committed game state.
 AI participants act through allowed game APIs and their assigned information scope; they cannot access hidden opponent state.
 Source-defined Forms assessments/quizzes reuse canonical Forms objects where applicable. Replay/results reflect actual recorded actions.''',
 '''Persist game/ruleset revision, MatchID, contestants, round/turn/action IDs, state checkpoints, timing and score/result records.
 Keep reader UI and explanatory text separate from authoritative legal state.''',
 '''Validate action eligibility and current turn/revision; handle disconnect, timeout and retries without duplicate moves.
 Restrict hidden information in UI, logs and model context; result sharing uses permitted scope.''',
 '''Test legal/illegal action, turn transition, timeout, scoring and reconnect from a checkpoint.
 Verify AI hidden-state restrictions and distinction among Play, Games and Spaces Experiences.''')

M('vision-voice','Vision & Voice — shared live session system','cui dulche models permissions persistence',
 'Vision & Voice owns real conversational speech, visual context and live translation services, reused by the in-chat Voice pill. Multiple surfaces attach to the same session rather than creating a second speech/capture stack.',
 '''Support Conversational, Monologue, Live Listener and Live Translate modes with actual listening/speaking/paused/ended states.
 Expose current voice/model, mute, camera, screen/window/region sharing, captions, device/settings and end controls with persistent capture indicators.
 Full app and compact Spaces pill keep the same ConversationID/SpaceID/Voice SessionID; Open in Vision & Voice preserves continuity.''',
 '''Route voice/transcription/vision through real supported models and named voices. Model capability, route locality and account limits remain truthful.
 Support typed text with the live conversation, turn-taking/interruption and source-defined tools/visual inputs under current permissions.
 Display real latency/connection/failure data when available; do not fabricate a connected call or live waveform from idle state.''',
 '''Persist canonical session/conversation identities, model/voice route, allowed capture sources, transcripts/outputs and retention policy.
 Active capture streams and device handles are ephemeral runtime state, separate from durable transcript/artifact storage.''',
 '''Camera/screenshare require explicit permission; voice activation alone cannot enable them. Ending sharing stops actual frames/samples at the runtime boundary.
 Recover unavailable device, network loss, model failure and interrupted speech while preserving permitted conversation content.''',
 '''Test two-way speech, interruption, captions, camera/share grant and immediate stop of capture.
 Switch between Spaces pill/full app without a new session, test each supported mode and verify denied/unsupported capability states.''')

M('dulche-translate','Dulche Translate service','dulche models permissions persistence',
 'Dulche Translate is the shared translation service behind Spaces Translate and other authorised consumers. It preserves source language/text, target language outputs and provenance through real model/runtime capabilities.',
 '''Owning surfaces present source/target languages, progress, one output per target language and genuine unsupported/failed states.
 Keep translation service controls consistent with canonical model routing; do not create a conflicting per-app credential/model store.''',
 '''Use retained language detection, translation, voice/text and output contracts as supported. Explicit user target choices override a detected default where valid.
 Preserve content boundaries, language identifiers and source provenance. A translated output is derived content, not a silent edit to the original source.
 Multi-target requests share a TranslationSet with distinct sibling results and partial-failure state.''',
 '''Persist request/TranslationSet/output IDs, source reference/revision, language codes, model/route provenance and result status.
 Keep temporary runtime buffers distinct from retained translation history and exported output.''',
 '''Respect source disclosure and local/cloud policy. Unsupported language/voice capabilities cannot be implied by a visible selector.
 Retry only failed target output where safe, preserving successful siblings and the original source.''',
 '''Test language detection/explicit targets, multiple sibling outputs, partial failure/retry and source preservation.
 Verify model capability/permissions and canonical use from Spaces Translate.''',kind='foundation',contract='Canonical translation requests, language/provenance metadata and sibling output sets.')

M('maps','Maps — search, places and routes','cui permissions integrations persistence',
 'Maps provides real place search, saved places, map layers and routing with explicit location permissions and truthful provider freshness/availability. AI mode assists through the same canonical map capabilities.',
 '''Use a dominant map with search, source-aware result/route cards, layer/zoom controls and selected place detail.
 Keep default search versus AI mode explicit, and distinguish live location, entered origin and unknown/unavailable position.''',
 '''Resolve place/geocode/route requests through actual providers. Show route options, units, distance/time and traffic/freshness only when grounded in provider data.
 Save canonical places/collections and open routes with clear mode/origin/destination. Do not invent travel estimates from a mock-up geometry.
 AI edits/proposals target the same map entities and preserve permission scope.''',
 '''Persist place/route/query IDs, provider references, coordinates, timestamps/freshness and saved collection membership.
 Map tiles/cache are derived; transient viewport is separate from saved places and route inputs.''',
 '''Location capture is explicit and revocable. Cached/private location information must not leak through unrelated search or AI context.
 Handle provider/network failure, ambiguous place and unsupported route mode with useful recovery.''',
 '''Test search→place→route→save, ambiguous matches, location denied and provider failure.
 Verify freshness labels, true units and same canonical targets in default/AI mode.''')

M('shelf','Shelf — cross-app library and collections','cui platform persistence permissions',
 'Shelf organises canonical apps, media and file targets into useful libraries/collections. It references real resources and distinguishes collection membership from installation or source deletion.',
 '''Show searchable/filterable library and collection contents with type/owner, artwork/preview and actual availability.
 Selected item details offer the real Open, Locate, Install/available or unavailable action appropriate to its type.''',
 '''Add, organise, pin and remove reference membership without silently moving/deleting source files or uninstalling apps.
 Resolve launching through the canonical app registry and opening media/artifacts through their owner. Keep Shelf distinct from the Launcher home screen and Files provider operations.''',
 '''Persist Shelf/CollectionID, membership IDs, ordered target references and user metadata. Source artifact/package identity remains owned elsewhere.
 Availability and preview caches are derived from actual target state.''',
 '''Recheck access on open; removed/revoked/missing targets remain safely recoverable by Locate/remove-reference where supported.
 Installing or deleting a source uses that owner’s explicit operation and confirmation semantics.''',
 '''Test add/reorder/remove-reference and verify the source remains intact.
 Open installed/unavailable/missing targets and verify truthful actions and canonical owner routing.''')

M('launcher','Launcher — mobile and OS home surface','cui platform go-search',
 'Launcher is the Android/9to1OS home surface for applications, widgets, search and layout customisation. It consumes the shared package/app registry rather than building a second installer or app identity system.',
 '''Use a responsive home layout with app icons, dock, search and useful widgets. Respect mobile safe areas, touch targets and native navigation insets.
 Show app/widget layout editing and installed/unavailable target states where relevant; do not wrap the OS launcher in browser chrome.''',
 '''Launch canonical apps, add/reorder/remove shortcuts/widgets and use shared Go/search. A removed shortcut does not uninstall the package.
 Preserve source-defined home-screen pages/dock/widget capabilities and platform integration. Widgets reflect actual authorised data and lifecycle.''',
 '''Persist launcher layout, page/dock positions, widget configuration and canonical app/package references by user/device.
 Runtime app state and package installation belong to the owning registry/runtime.''',
 '''Respect widget/data grants and device policy. Unavailable apps offer the actual permitted install/locate/recovery path.
 Restore layout after restart without replaying app actions or losing user customisation.''',
 '''Test touch/keyboard launch, search, layout edit and restart persistence on narrow screens.
 Verify shortcut removal, registry consistency and no browser shell on the native launcher.''')

M('os','9to1OS — desktop, Go and native system surfaces','cui platform go-search permissions persistence',
 '9to1OS is the desktop host with the source-defined Ubuntu/GNOME foundations and CUI replacement shell. Its desktop, taskbar, Go and system utilities are native operating-system surfaces, not browser pages.',
 '''Use desktop, taskbar/dock, windows and Go overlay without browser chrome. Preserve the source-defined one-to-five taskbar layer choices.
 Distinguish Desktop Spaces, Virtual Desktops, pages and LLM Spaces by their real meanings; do not collapse them into one navigation type.
 Quick Settings/system utility views show actual devices/resources and availability rather than decorative status values.''',
 '''Retain system settings, file/device/disk/log utilities, window management, resource controls and supported runtime capabilities from the technical source.
 Use canonical package/app registry and Go. Native and web apps share identity without pretending they share an identical engine/runtime.
 Wine/WinBoat/Android compatibility uses explicit environment/version/capability and permission contracts. Unsupported compatibility must remain visible.''',
 '''Persist desktop/user configuration, workspace/window identities, package references, runtime environments and applicable snapshots/resource settings.
 Window close, app exit, VM detach and machine stop have separate lifecycles.''',
 '''Require the correct system/device authority for privileged operations. AI cannot acquire elevated authority merely through the desktop UI.
 Recover disconnected runtimes, failed app launch and missing resources without hiding the underlying state or losing unrelated windows.''',
 '''Test Go, window/workspace switching, taskbar layout persistence and native app launch without browser chrome.
 Verify supported compatibility environment truthfulness, close-versus-stop semantics and permission denial.''')

M('link','Link — Flow and Binder','cui productivity-engine persistence permissions',
 'Link composes canonical sources into continuous reading and authored spatial organisation. Flow and Binder are two distinct workflows, both preserving source identity and ownership.',
 '''Flow uses one primary continuous reading scroll with author outline and source controls; the mandatory annual-report composition spans Data, Write, image, Data and report-assistant content in sequence.
 Binder provides authored nested notes/folders, binder tabs/stacks and nonlinear spatial organisation. Show add/insert and source-reference properties in context, not a generic file-manager clone.''',
 '''Flow blocks reference live or pinned source revisions with clear ownership; edit opens the correct owning surface or supported typed block editor.
 Binder adds/reorders/groups references and authored local notes without moving the referenced source files. Creating a Binder folder is not creating a provider folder.
 Insert controls identify source type, canonical target and live/pinned binding. Source updates/deletion/permission loss have explicit visible behaviour.''',
 '''Persist Link document, Flow block and Binder item/container IDs, order/spatial coordinates, authored content, source references, live/pinned revision policy and history.
 Source artifacts remain owned by Data/Write/Files/etc.; derived reading previews are not independent copies.''',
 '''Check source permission at render and open. Do not leak inaccessible cached source content; display safe unavailable state and repair/relink.
 Detach/remove never silently deletes or moves originals. Conflicting edits and stale pinned sources remain inspectable.''',
 '''Render the complete Data→Write→image→Data→assistant Flow with one main scroll and real source provenance.
 Add/edit a Binder reference and Flow block, reopen, revoke a source and verify no source file movement/deletion.''')

M('prototype','Prototype — Design, Logic and Run','cui node-editor persistence permissions',
 'Prototype is a canonical interactive model/simulation authoring surface. Design, Logic and Run are coordinated views of the same typed model rather than three disconnected documents.',
 '''Use Design/Logic/Run tabs with a meaningful preview/canvas, outline and parameter/property inspector. Show parameter default/current values, units, bounds and binding explicitly.
 Run exposes actual input controls, state and derived outputs with clear preview/simulation status.''',
 '''Define typed parameters, constraints, bindings, formulas/dependencies, interactions and supported state/time behaviour. Validate cycles/limits and missing values before running.
 Parameter editing distinguishes author defaults from per-reader run input. Reset/restart applies documented simulation semantics without altering the source unexpectedly.
 Use shared graph components for logic where specified and safe typed external actions; a prototype control cannot execute arbitrary code by appearance.''',
 '''Persist PrototypeID, object/parameter/binding/rule IDs, schema/revision, author defaults and separate Run/session state.
 Derived values carry dependency/provenance; preview state does not overwrite canonical configuration.''',
 '''Apply sandbox/capability boundaries and explicit action grants. Invalid parameter/rule changes preserve the last valid model.
 Recover paused/failed runs and stale bindings with inspectable error and safe reset/retry.''',
 '''Edit parameter min/max/default/units, validate binding, run with different input and verify author defaults remain unchanged.
 Test invalid cycles/out-of-range values, switching Design/Logic/Run and persisted identity/revision.''')

M('paths','Paths — branching guided experiences','cui persistence permissions node-editor',
 'Paths authors and runs guided sequences with steps, choices, activities, checkpoints and outcomes. Branch logic is explicit, versioned and testable; reader progress is a durable attempt against a known route revision.',
 '''Show step outline, selected step content, branch/route properties and preview/run controls. A branch rule exposes condition, target, priority/fallback and missing-value handling.
 Reader view shows the current step and available choices without inventing progress percentages for nonlinear routes.''',
 '''Create/reorder steps and configure typed conditions/targets, default/fallback routes and supported loops with validation.
 Preview is disposable author testing; real reader attempts persist separately. Restart creates or resets according to explicit attempt semantics, not silent history deletion.
 Activities call canonical app/tools with current grants and idempotency; returning to a checkpoint must not repeat an external effect automatically.''',
 '''Persist PathID/revision, StepID, rule/edge IDs, checkpoint/outcome definitions and reader AttemptID, inputs and traversed events.
 Keep source authoring, preview state and durable reader progress distinct.''',
 '''Validate unreachable/missing destinations, ambiguous branches and unsafe cycles before activation. Revisions do not silently remap existing attempts.
 Recover interrupted attempts/checkpoints and denied activity actions while preserving completed evidence.''',
 '''Test branch condition true/false/missing, priority/fallback, invalid destination and preview reset.
 Reopen a durable attempt, resume checkpoint and verify no duplicated consequential action.''')

M('replay','Replay — semantic history and inspection','cui persistence permissions',
 'Replay records and presents explicit permissioned semantic activity/history. Timeline inspection is read-only historical viewing; seeking must never replay external actions.',
 '''Use source/capture identity, timeline/checkpoints, historical viewport/detail and selected event inspector. Event detail shows target/revision, actor, time, input/output and safe provenance.
 Capture start/stop and incomplete/paused/failed states remain visible; no ambient microphone/camera capture is implied.''',
 '''Record supported semantic changes/checkpoints from explicitly selected sources. Preserve source-defined capture, filtering, narration and export/share behaviour.
 Seek changes the historical view only. Continue from here creates an independent canonical fork/copy with traceable origin when supported.
 Review redaction and sharing scope before export. A replay rendering is not evidence that the captured application is still in that state.''',
 '''Persist ReplayID, capture source/grants, event/checkpoint IDs, canonical artifact revisions, timestamps and redaction/share metadata.
 Playback position/view settings are transient; captured history remains immutable except authorised redaction/versioning semantics.''',
 '''Recheck source visibility and redact revoked/private content. Explicit capture grants are required; no hidden ambient recording.
 Handle incomplete capture/missing assets with truthful gaps and recovery rather than invented events.''',
 '''Capture authorised semantic edits, seek events and verify no external command/action runs again.
 Test event detail, incomplete capture, redaction/share review and Continue from here creating an independent identity.''')

M('variants','Variants — integrated comparison and merge','cui productivity-engine persistence permissions',
 'Variants is an integrated capability in Write, Data, Present, Canvas, Picture, Prototype and Sites. It is not a separate standalone application. Each owner supplies domain-aware comparison and merge semantics.',
 '''Open a Variants panel within the active owner app, identifying base/source/target revisions, candidate differences and selected comparison.
 Show merge preview, conflicts and exact apply/reject controls in the owning app context, preserving the canonical artifact canvas/document.''',
 '''Compare stable object identities and app semantics rather than only pixels or plain text. Support source-defined creation/naming/selection and history.
 Apply chosen changes through the owner’s typed command/revision/undo contract. A whole-version replacement fallback is explicit when semantic merge is unsupported.
 Preview never mutates source or target. Merge must explain conflicts and preserve unrelated changes.''',
 '''Persist variant IDs, ancestry/base, source/target artifact revisions, comparison/change-set IDs, resolution and applied result.
 The owning app remains authoritative; Variants does not create a parallel document format.''',
 '''Respect source/target permissions and expected target revision. Stale comparison requires refresh/reconciliation.
 Recover failed merge atomically where supported and preserve undo/history; do not silently flatten native content.''',
 '''Exercise preview/reject/apply, one semantic conflict, stale target and undo in supported owner apps.
 Verify no standalone Variants destination and no source mutation before explicit application.''')

M('delivery','Delivery governance and historical process','authority',
 'This module retains product-delivery constraints and historical process provenance without promoting expired schedules or agent-operation instructions into current authorisation. Future/deferred scope remains clearly separate from the current release contract.',
 '''Documentation exposes current implementation/verification status honestly. A design mock-up, planned feature or compiling placeholder is not labelled complete functionality.
 Keep deferred Training Lab/Android-ROM expansion and undefined capabilities visibly out of current acceptance claims.''',
 '''Preserve retained source, licensing, donor parity, migration and quality requirements that affect the product deliverable.
 Historical deadlines, worker schedules and operational handoff instructions are archived with their source IDs and original wording, not followed as new tasks.
 Use module/source traceability to identify implementation work and unresolved dependencies; do not silently redefine completed scope from a visual review.''',
 '''Maintain versioned source/override ledgers, module boundaries and acceptance evidence references. Original source bytes remain available for audit.
 Distinguish planned, implemented, tested and independently accepted status records.''',
 '''External deployment, spending and communication still require the operation’s current authority; historical prose is not authorisation.
 Record unresolved contradictions or absent references rather than inventing missing backend guarantees.''',
 '''Check historical process is excluded from active technical execution instructions while substantive product requirements remain retained.
 Verify future/deferred scope is not shown as implemented or accepted.''',kind='governance',contract='Current delivery status, retained product constraints and historical/deferred process provenance.')

M('acceptance','Cross-app acceptance and evidence','authority platform cui permissions persistence',
 'Acceptance verifies real behaviour, compatibility, permission boundaries and recovery across apps. The supplied scenario chapters remain retained; mock-up review and source coverage are design evidence only.',
 '''Exercise actual screens at supported desktop, narrow/mobile and native/browser/OS hosts, including focus, scroll, popup placement, contrast and reduced motion.
 Include real empty/loading/unavailable/permission/approval/error/conflict and recovery states, not only the attractive success screenshot.''',
 '''Run source-backed end-to-end scenarios through canonical APIs and visible UI. Evidence must connect requested action, actual state mutation/result, persisted reopen and meaningful failure handling.
 Validate UI/API parity, app ownership, shared theme consistency, local/manual workflows and donor feature coverage where required.
 Shared prerequisite tests may be reused with explicit version/evidence references; do not substitute a shallow screenshot test for substantive backend behaviour.''',
 '''Record test/scenario IDs, build/version, environment, input/target revision, observed result and evidence. Preserve failed/partial outcomes distinctly from pass.
 Source acceptance IDs remain stable for traceability even when current UI placement changes.''',
 '''Permission/security and independent-review gates remain mandatory; time/review budget exhaustion cannot turn an untested result into VERIFIED.
 Reruns and test fixtures must not repeat consequential real external actions without their defined safe test scope.''',
 '''Cover canonical artifact opened in multiple hosts, offline/restart, grant revocation, concurrent revision, failure/retry and derived export verification.
 Reconcile every current module’s acceptance list with retained source scenarios and explicitly document gaps rather than asserting completeness from the atlas.''',kind='governance',contract='Real end-to-end evidence, independent acceptance and cross-app failure/recovery verification.')

# Confirmed incorporated annexes retrieved during the source audit. Append only:
# current requirement IDs above stay stable for the Word/mock-up cross-references.
MODULES['spaces']['requirements']['layouts'] += [
 'Study Home, Subject Workspace and Study Session are separate active views within one Space/navigation scope. Restore the actual conversation/workspace when appropriate; do not stack a dashboard over an inactive editor. The default sidebar is New Chat; Subjects; Study Tools (My Progress, Planner, Revision Bank); Revise These; Recent Chats, under the shared customisation precedence.',
 'Study Session exposes the four exact modes Learn, Practise, Test and Review near the standard composer, and a visible source-policy selector: My sources only / Wider knowledge allowed. Subject/topic, mode and policy remain legible without overwhelming the header. Switching mode preserves ConversationID, topic, sources, draft and voice/session state.'
]
MODULES['spaces']['requirements']['features'] += [
 'Study proficiency uses the four explicit labels Not assessed (neutral), Needs work (red), Developing (amber) and Confident (green), backed by attributable attempts and documented rules. Keep self-reported confidence separate from demonstrated proficiency; opening a lesson or flipping a card is not mastery evidence. Revise These ranks at most three distinct topics across all subjects using weak-first, then overdue/oldest-reviewed and stable TopicID ties, with an intelligible reason; unassessed is not weak.',
 'Learn provides guided explanation; Practise records actual answers/working, hints and feedback; Test uses canonical Forms assessments with rubric/timing/provenance; Review uses recorded due/weak topics and Cards attempts. My sources only enforces the configured closed corpus and never fabricates citations. Revision Bank opens the real owning-app viewer/interactor with explicit Edit/Open in App; Study scheduling uses one canonical Planner Study burrow and proposes authorised review events rather than silently creating them.'
]
MODULES['spaces']['requirements']['acceptance'].append('Exercise all four Study modes in the same conversation, enforce both source policies, inspect Not assessed/Needs work/Developing/Confident evidence, and verify self-confidence does not itself change demonstrated proficiency. Across multiple subjects show no more than three ranked recommendations; open a canonical Forms/Cards resource and a Planner Study-burrow event without duplicate stores or false mastery.')
MODULES['platform']['requirements']['features'] += [
 'Native documents/projects must not impose arbitrary global character, word, page, slide, block, object or file-byte caps merely to simplify rendering or saves. Real device, quota, format, address-space and security limits remain explicit; never truncate silently.',
 'Prioritise current input, caret, selection, scroll, drag and undo over distant work. Virtualise visible/nearby pages, slides, cells, blocks, nodes and time ranges, use indexed deep navigation and bounded adaptive caches, and keep unrendered content canonical, searchable, accessible and exportable.',
 'Use incremental dependency-aware layout/calculation and addressable/chunked persistence; do not reserialise or rerender an entire large artifact on each keystroke. Stream/resume/cancel large import/export with integrity checks, honest progress and safe resource-pressure recovery. Lower preview fidelity or buffers before document fidelity.'
]
MODULES['spaces']['source_refs']=['STUDY-UI-201','STUDY-UI-210','STUDY-UI-215','STUDY-UI-217','STUDY-AC-201','STUDY-AC-219']
MODULES['platform']['source_refs']=['SCALE-001','SCALE-002','SCALE-003','SCALE-004','SCALE-005','SCALE-006','SCALE-007','SCALE-008','SCALE-009','SCALE-010','SCALE-011','SCALE-AC-201','SCALE-AC-202']
