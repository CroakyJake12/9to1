# Spaces — conversations, workspaces and custom Space Builder — User-created Spaces and nesting; Sidebar and navigation (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0806 · v2 lines 3829–3829

Experiences MAY use persistent Dulche Agents for autonomous characters/roles that genuinely benefit from independent identity, memory or goals, while lightweight characters MAY remain scoped experience actors/state rather than becoming permanent Den Agents. Agent knowledge and private/hidden state MUST be filtered by the Experience visibility policy so one character cannot access information it has not learned.

### V2-B0807 · v2 lines 3832–3832

Experiences SHOULD support checkpoints, rewind and branching. A branch combines the conversation branch with the matching Experience-state revision so trying a different decision does not mutate the original timeline. Rewind/branch of simulated state MUST NOT pretend to undo real external side effects that already occurred through authorised Plugins/actions.

### V2-B0808 · v2 lines 3835–3835

Experiences MAY expose maps, inventories, evidence, character panels, timelines, objectives, cards or other interactive elements through safe CUI/Generative UI. The normal conversation remains the primary interaction surface; generated UI is a structured representation of canonical Experience state, not a separate hidden game runtime.

### V2-B0809 · v2 lines 3838–3838

Experiences MAY launch or embed a formal Play Match when the scenario requires a structured competition, and Play MAY offer Continue in Experiences for an open-ended continuation. Experience state and Play Match state remain distinct canonical entities rather than being silently merged.

### V2-B0810 · v2 lines 3843–3843

User-created Spaces and nesting

### V2-B0811 · v2 lines 3846–3846

The sidebar does not expose a ninth Custom type. Users create user-defined Spaces through + New Space and manage the hierarchy through Edit Spaces. User-created Spaces have SpaceOrigin = UserCreated and receive the full Space graph editor described below; built-in first-party Spaces have SpaceOrigin = BuiltIn and expose only the protected Events & Extensions editor.

### V2-B0812 · v2 lines 3849–3849

Spaces MAY be nested through ParentSpaceID to create logical hierarchies. Nesting MUST preserve each Space’s stable identity and MUST NOT imply permission escalation. Child inheritance of sources, connected apps, model policy, memory, Skills, Plugins or Agents must be explicit/inspectable; a child cannot regain a capability or permission explicitly denied by a parent/security policy.

### V2-B0813 · v2 lines 3852–3852

Edit Spaces MUST support reorder, nest/unnest, rename, icon, sharing, inheritance configuration, duplicate, archive/delete/recover and other structural management without exposing protected first-party internal implementation.

### V2-B0814 · v2 lines 3855–3855

Sidebar and navigation

### V2-B0815 · v2 lines 3858–3858

The Spaces sidebar is scoped around the currently selected Space. Its top control MUST display the currently selected Space and provide a Space switcher when activated; an adjacent overflow control provides quick actions for that Space.

### V2-B0816 · v2 lines 3861–3861

Immediately below, the sidebar MUST expose the built-in Space destinations Chat, Study, Tasks, Agent, Shopping, Research, Translate and Experiences, followed by New Chat, then Space-relevant options. A divider separates the persistent + New Space and Edit Spaces controls.

### V2-B0817 · v2 lines 3864–3864

Space-relevant options are typed capabilities/surfaces appropriate to the selected Space and may include Sources, Files/context, Topics, Progress, Runs, Findings, Tools, Agents, Approvals, Settings or custom sidebar items. The set is context-sensitive but MUST be deterministic and inspectable, not arbitrary hidden AI-generated navigation.

### V2-B0818 · v2 lines 3867–3867

New Chat creates a new conversation inside the currently selected Space. It MUST NOT create another Space. Recent/pinned conversations may appear within the selected Space’s Chat/conversation surface without turning the global sidebar into an unbounded cross-Space history list.

### V2-B0819 · v2 lines 3870–3880

Plugins — custom sidebar elements and per-Space visibility
Plugins MUST support contributing custom sidebar elements. An element MUST be able to open an arbitrary configured page/URL, an authorised app surface, an authored CUI page or a generative page, or invoke an Automation or another declared authorised action. Navigation and action-triggering behaviour MUST be explicit. Arbitrary pages remain subject to the existing safe rendering, origin, permissions and external-navigation rules; this capability does not authorise unrestricted code execution.
For each Plugin, the user MUST be able to select which Spaces display its sidebar elements. This selection MUST be inspectable, editable and persisted using stable Plugin, Space and SidebarItem identities. An excluded Space MUST NOT display that Plugin's sidebar contributions merely because the Plugin is installed globally. Sidebar visibility is distinct from permission to execute the Plugin or access Space data; it MUST NOT silently change either.
Attaching a Plugin MUST now be the primary method of supplying custom sidebar elements in custom Spaces. Ordinary Plugin-provided sidebar elements MUST NOT require users to construct a built-in Automation or Space lifecycle graph solely to load the element.
The existing built-in automation/Events & Extensions/full Space graph mechanisms for loading, showing and controlling custom sidebar elements MUST remain supported for enhanced and combined workflows. Plugin and graph routes MUST share the existing sidebar identity, ownership, lifecycle and rendering contracts, preserving AppSurface, CUI and GenerativeUI support. They MUST NOT duplicate the same contribution or replace protected first-party sidebar items. Disabling or detaching a Plugin MUST stop its active contributions without leaving executable orphan actions or deleting unrelated Space content.
Plugin sidebar — supplied concept mockup clarification
The supplied sidebar concept mockup is the applicable visual reference. The relevant example is the My Homework row immediately below New chat and above the divider; the earlier expanded-composer screenshot is not the sidebar reference.
Plugin-contributed sidebar elements MUST support this ordinary navigation-row presentation: an icon on the left, a readable text label beside it, and alignment, spacing and interaction treatment consistent with neighbouring sidebar items. A custom entry can sit directly beneath New Chat within the selected Space's permitted custom-sidebar area; it MUST NOT require opening a separate Plugin panel simply to reach the entry.
The screenshot is a concept reference for the custom entry's presentation and placement, not a requirement to copy its project names, conversation history, colours or complete sidebar structure. Existing protected navigation and Space-scoping requirements remain in force. The Plugin's per-Space visibility selection still determines where its entries appear, and the existing automation/Space-graph route remains supported.
Plugin sidebar API and acceptance
Extend the canonical Plugin and Spaces APIs for contribution registration, configuration, removal, per-Space visibility and authorised action dispatch; UI and API MUST use the same stable SidebarItemID and ownership/lifecycle rules. Test Plugin-defined URL/page, authored CUI, generative-page and Automation actions; persistence after restart; visibility only in selected Spaces; keyboard navigation and accessible names; and safe disable/detach without orphan actions or deletion of unrelated content. The original automation/Events & Extensions/Space-graph contribution route MUST continue to work, including combined workflows without duplicate contributions. The Plugin manifest and Builder MUST expose these contribution contracts rather than creating a second sidebar registry.

### V2-B0820 · v2 lines 3883–3883

Canonical Space and conversation model

### V2-B0821 · v2 lines 3886–3886

Space MUST contain at least SpaceID, SpaceOrigin, built-in type where applicable, Name, Icon?, ParentSpaceID?, owner/scope, instructions/configuration, model policy, context/source policy, memory policy, capability bindings, permissions, created/modified timestamps and revision metadata. SpaceID is stable across rename, nesting and UI ordering.

### V2-B0822 · v2 lines 3889–3889

Conversation MUST contain at least ConversationID, SpaceID, Title, CreatedAt, ModifiedAt, ActiveBranchID, optional model override and revision metadata. Messages MUST have stable MessageID, author principal/role, branch/parent relationship, timestamp, content blocks, model/provider provenance where AI-generated and tool/action references where applicable.

### V2-B0823 · v2 lines 3892–3892

Conversation branching and history

### V2-B0824 · v2 lines 3895–3895

Editing an earlier message, regenerating a response or branching from a message MUST preserve prior history as a conversation branch rather than silently destroying descendants. Users MUST be able to return to earlier branches and identify which branch is active.

### V2-B0825 · v2 lines 3898–3898

Tool calls and consequential actions already executed remain historical facts attached to their original run/branch and MUST NOT be retroactively undone merely because text was regenerated. New branches start from the selected historical conversational state but respect current external entity revisions/permissions.

### V2-B0826 · v2 lines 3901–3901

Stop and resume

### V2-B0827 · v2 lines 3904–3904

While a response/run is active, Spaces MUST expose Stop. After the user stops a resumable response, the stopped response remains in place and its primary control becomes a Play/Resume button. Resume continues the same user-visible RequestID/run from persisted execution state rather than behaving like Regenerate.

### V2-B0828 · v2 lines 3907–3907

Request states MUST distinguish at least Queued, Running, Stopping, Stopped, Completed, Failed, Cancelled and Blocked. Stopped is resumable when the underlying run/checkpoint supports continuation; Cancelled is terminal unless a new/restarted run is explicitly created.

### V2-B0829 · v2 lines 3910–3910

A stopped run MUST retain RequestID, conversation/message linkage, selected model/provider policy, context references/snapshot metadata, completed tool/action records, Action Graph, Subagent state/checkpoints and remaining budget/limits where applicable. Resume MUST NOT repeat already-completed consequential actions. If the provider cannot literally resume a token stream, Dulche may continue from persisted checkpoint/context while preserving the same user-visible continuation semantics.

### V2-B0830 · v2 lines 3913–3913

Regenerate and Retry remain distinct from Resume: Regenerate creates a new response branch; Retry re-attempts a failed operation/run with idempotency and side-effect safeguards.

### V2-B0831 · v2 lines 3916–3916

Model selection

### V2-B0832 · v2 lines 3919–3919

Spaces MUST use the Universal Model Picker. Effective model policy resolves explicit request override, conversation override, Space default, app/default policy and global default in that order, subject to capability/privacy/entitlement restrictions.

### V2-B0833 · v2 lines 3922–3922

Switching model mid-conversation MUST NOT rewrite provenance of prior responses. Every AI response/run must retain effective model/provider identity and relevant policy provenance.

### V2-B0834 · v2 lines 3925–3925

Sources and context

### V2-B0835 · v2 lines 3928–3928

Spaces context MUST be represented as explicit typed references rather than opaque prompt stuffing. Supported reference families include Files/folders, Boards/pages, Write/Present/Canvas/Data artifacts, Mail threads/messages, Planner items, Map routes, Stack projects, Sites/projects, connected-app entities, conversations and uploaded sources.

### V2-B0836 · v2 lines 3931–3931

Each context/source reference MUST retain stable source identity, revision/change token where applicable, permission state and ingestion/index state where required. Spaces must be able to detect that a referenced source has changed rather than treating an old snapshot as current.

### V2-B0837 · v2 lines 3934–3934

A Sources/Context surface MUST let the user inspect what is attached, where it came from, current revision/availability, whether it is included automatically, ingestion/index status and remove/disable controls. Spaces MUST NOT rely on mysterious hidden context for ordinary operation.

### V2-B0838 · v2 lines 3937–3937

Dragging/adding a folder SHOULD create a folder reference plus scope/filter configuration rather than eagerly duplicating every contained file. Spaces can retrieve authorised current files on demand through Files APIs.

### V2-B0839 · v2 lines 3940–3940

Context budgeting and retrieval

### V2-B0840 · v2 lines 3943–3943

When available material exceeds the selected model’s context, Dulche/Spaces MUST use deterministic context selection/retrieval. Required/current selection and explicit attached sources take priority over optional historical context. Retrieval/summarisation/omission decisions MUST be represented in run logs/Action Graph sufficiently for inspection.

### V2-B0841 · v2 lines 3946–3946

Spaces MUST NOT claim that an answer considered an entire source set when only a subset was retrieved. Source-grounded modes must retain provenance for the chunks/entities actually used.

### V2-B0842 · v2 lines 3949–3949

Canonical artifact editing

### V2-B0843 · v2 lines 3952–3952

Any authorised mutable 9to1 document/project/entity whose owning app exposes domain APIs MUST be openable/editable from Spaces without creating a duplicate canonical copy. Spaces MAY host an embedded artifact surface alongside conversation, but the artifact retains its owning app identity, revision history and permissions.

### V2-B0844 · v2 lines 3955–3955

Spaces MUST support direct low-risk edits when clearly requested and reviewable proposed-change workflows for larger/ambiguous mutations. Review UI MUST correspond to actual typed target-app actions/diffs and MUST NOT present a fabricated text-only diff disconnected from the canonical artifact.

### V2-B0845 · v2 lines 3958–3958

Shared Productivity Engine

### V2-B0846 · v2 lines 3961–3961

Spaces MUST use Home’s Shared Productivity Engine for supported rich content/object rendering and editing. Pasted or embedded tables, code blocks, images, drawings, graphs and other shared objects MUST preserve shared schema/formatting and remain editable through the same object actions used by Write, Present, Canvas and Boards.

### V2-B0847 · v2 lines 3964–3964

AI-generated shared-engine objects SHOULD be directly transferable to supporting apps without reconstructing them from rendered text/images.

### V2-B0848 · v2 lines 3967–3967

Plugins, Skills and Agents

### V2-B0849 · v2 lines 3970–3970

Every Space has its own capability bindings for Dulche Plugins, Skills and persistent Agents. By default, a newly created Space inherits all currently installed/enabled Dulche Plugins and Skills and all available Agents unless restricted by policy.

### V2-B0850 · v2 lines 3973–3973

Space capability bindings MUST support inherited availability plus explicit per-Space enable/disable/exclusion and optional version pinning where the underlying package/Agent contract supports it. Installing a new globally enabled Plugin/Skill or making a new Agent available automatically makes it available to Spaces that still inherit that category.

### V2-B0851 · v2 lines 3976–3976

Space bindings reference canonical Dulche package IDs/versions and Agent IDs; they MUST NOT clone package/Agent definitions into the Space. Availability does not grant permission: a Space may see a Plugin/Skill/Agent while still requiring the normal permission/trust decision before protected actions execute.

### V2-B0852 · v2 lines 3979–3979

Shared node-editor framework

### V2-B0853 · v2 lines 3982–3982

All Space graph editing uses the Home-owned Node Graph Framework also used by Automations. Home owns graph data structures, node registry, typed ports/connections, renderer, pan/zoom/selection, grouping, validation, clipboard/duplicate, undo/redo, graph versioning, execution visualisation hooks and contextual AI-bar integration. Spaces owns Space-specific event/action nodes and graph semantics; Dulche executes AI/model/Agent/tool portions; owning apps execute their typed actions.

### V2-B0854 · v2 lines 3985–3985

Graph connections MUST be typed. Incompatible connections MUST be rejected or require an explicit conversion node. Graph nodes MUST reference stable canonical app/capability IDs rather than UI coordinates or screen controls.

### V2-B0855 · v2 lines 3988–3988

Built-in Space Events & Extensions editor

### V2-B0856 · v2 lines 3991–3991

Built-in Chat, Study, Tasks, Agent, Shopping, Research, Translate and Experiences Spaces expose a limited Events & Extensions graph editor attached only through published safe extension hooks. The protected first-party core graph/behaviour is not editable, disconnectable or replaceable.

### V2-B0857 · v2 lines 3994–3994

Allowed extension hooks may include Space loaded/activated/deactivated/closed, conversation created/opened, response started/stopped/resumed/completed and relevant type-specific events such as Study session started/completed, Task started/blocked/completed, Research run completed, Translate Translation Set/job completed, Experience started/checkpointed/branched/completed or Agent run state changed.

### V2-B0858 · v2 lines 3997–3997

Allowed extension actions may include Set Do Not Disturb, send Home notification, add/remove allowed context, open app/entity, create Planner item, invoke Automation/Agent/Skill/Plugin/permitted MCP capability, update extension-owned Space variables and add/show custom sidebar surfaces.

### V2-B0859 · v2 lines 4000–4000

The built-in extension editor MUST NOT allow removal/replacement of protected sidebar items, suppression of required first-party events, replacement of the Space’s conversation/runtime behaviour, bypass of permissions, mutation of protected first-party graph internals, disabling required recovery/safety behaviour or creation of unbounded loops.

### V2-B0860 · v2 lines 4003–4003

Published extension hooks are versioned. After a 9to1 update, incompatible/retired hooks MUST produce a needs-attention/repair state rather than silently breaking the whole Space.

### V2-B0861 · v2 lines 4006–4006

User-created Space full graph editor

### V2-B0862 · v2 lines 4009–4009

User-created Spaces created through + New Space may use the full Space graph editor. Their graph may define lifecycle behaviour, conversation routing, source retrieval, model/Agent selection, logic/branching, state, app actions, Generative UI, custom sidebar surfaces and other authorised Space behaviour.

### V2-B0863 · v2 lines 4012–4012

Full graphs MAY use bounded logic nodes such as If, Switch, Filter, Merge, Wait, bounded Loop, variables, compare/transform plus typed AI/runtime nodes such as Prompt model, Choose model, Run Agent, invoke Skill/Plugin/MCP, retrieve/search sources and Generate UI.

### V2-B0864 · v2 lines 4015–4015

Full graphs MUST still obey Home permissions, Dulche runtime limits, package/Agent availability, target-app contracts and loop/budget safeguards. A graph is not a permission escalation mechanism.

### V2-B0865 · v2 lines 4018–4018

Custom graph edits MUST use Draft versus Active revision semantics. Changes are validated before Apply/Publish; invalid drafts do not replace the last valid active graph. Prior active revisions SHOULD be rollback-capable.

### V2-B0866 · v2 lines 4021–4021

Graph-editor AI bar

### V2-B0867 · v2 lines 4024–4024

Both the built-in Events & Extensions editor and the full user-created Space graph editor MUST host the persistent contextual AI bar. The bar receives graph-semantic context and modifies graphs only through typed Node Graph APIs, never by mouse/keyboard simulation.

### V2-B0868 · v2 lines 4027–4027

A user MUST be able to request behaviour in natural language such as turning on Do Not Disturb when Study activates or adding a Planner sidebar surface. AI-generated graph changes MUST be presented as valid graph mutations and the assistant SHOULD explain the resulting behaviour in plain language, with relevant nodes highlightable from the explanation.
