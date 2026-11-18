# Global Assistants, Specialists and collaborators — Product definition and global-system boundary; Product category and interaction model (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0063 · v2 lines 236–236

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

Product definition and global-system boundary

### V2-B0064 · v2 lines 237–237

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

Assistants and Specialists are global systems built over the shared conversation and agentic runtime. Home and editors provide direct access without requiring a user to create or enter a Space. Spaces retains canonical Assistant conversation views. Opening an Assistant preserves its own identity and conversation/work state. The current scope does not mandate a separate standalone Assistants package.

### V2-B0065 · v2 lines 238–238

The wrapper exists to reuse the mature Spaces runtime and achieve close feature parity without duplicating the platform. Assistants SHOULD inherit generally useful Spaces capabilities by default unless a capability genuinely makes no sense for an Assistant. Reuse the same conversation runtime, Task/Run system, model/provider routing, tools, files/project access, memory/context infrastructure, permissions, computer-use implementation, Mini Computer runtime, artifacts, checkpoints, steering, subagents, recovery, generative UI, integrations, persistence, search/research, Dev integration and applicable multimodal capabilities. Do not create a second chat engine, task database, tool framework, permission broker, model router, file/project identity system, computer-use stack or artifact format.

### V2-B0066 · v2 lines 239–239

Product category and interaction model

### V2-B0067 · v2 lines 240–240

Assistants should belong to the same broad product category as experiences such as Grok-style custom bots, Dots, Muse-style agents and modern workspace/custom agents. This is a behavioural/product-model reference, not an instruction to copy another product's visual design, branding or proprietary implementation.

### V2-B0068 · v2 lines 241–241

An Assistant is a persistent, named, user-configured AI identity that the user creates once and returns to repeatedly. It is substantially more than a saved prompt, renamed chat, system-prompt textbox, template, one-off agentic Task, Space or delegated subagent. The same Assistant identity can span multiple conversations, Tasks/Runs, projects, scheduled work, memories/context where permitted, artifacts, checkpoints and applicable Mini Computer state. Starting a new conversation does not create a new Assistant, and delegating a worker does not create another personal Assistant.

### V2-B0069 · v2 lines 242–242

The intended experience is: “this is my persistent AI assistant that knows its configured role, can use authorised tools and resources, can continue real work over time, and can proactively help when I have enabled that behaviour” — not “this is a chat with a long system prompt.”

### V2-B0070 · v2 lines 243–243

Assistant configuration and identity

### V2-B0071 · v2 lines 244–244

Each Assistant MUST have a persistent editable configuration and stable identity. Where applicable the configuration includes name, icon/avatar, description, purpose/role, instructions/personality, preferred model or model policy, reasoning/model-routing preferences, allowed tools, connected apps/services, authorised files/projects/knowledge sources, memory behaviour, computer-use permissions, Mini Computer configuration, Dev/project access, proactive-behaviour settings, notification/check-in preferences, schedules/Automations, voice/multimodal preferences and resource/usage limits. These settings must be exposed through polished product UI rather than relying primarily on raw prompt/config files.

### V2-B0072 · v2 lines 245–245

Editing an Assistant's configuration MUST preserve its canonical Assistant identity, appropriate history, valid conversations, Tasks/Runs, memories, permissions and artifacts unless the user explicitly chooses an operation whose documented effect is to remove or reset them. Assistant identity is separate from any individual conversation or task.

### V2-B0073 · v2 lines 246–246

Assistants home and conversation experience

### V2-B0074 · v2 lines 247–247

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

The Assistants system MUST provide a dedicated home/library experience for seeing existing Assistants, creating an Assistant, opening recent Assistants, viewing recent conversations/tasks, seeing whether an Assistant is actively working, configuring an Assistant, inspecting its permissions and connected resources, opening current Tasks, managing proactive behaviour, managing its Mini Computer where applicable, and pausing/disabling/archiving/deleting an Assistant as appropriate. Do not redirect this experience into the normal Spaces application's Space list.

### V2-B0075 · v2 lines 248–248

An Assistant conversation should expose the power of a modern Spaces conversation through Assistant-focused UI. Where authorised, an Assistant can chat naturally, search/research, use tools, create and edit files, work with 9to1 apps, open Dev, perform durable long-running Tasks, create artifacts, use delegated workers, use computer control, operate a Mini Computer, work across connected services, present generative UI, continue through checkpoints, recover after interruption and accept live steering. The product should expose these capabilities naturally without turning the primary experience into a developer console.

### V2-B0076 · v2 lines 249–249

Relationship to Spaces

### V2-B0077 · v2 lines 250–250

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

Spaces remains the general-purpose AI workspace; Assistants is the persistent configured personal-assistant product. They may share underlying components and conversation/work surfaces, but product ownership, identity, proactive controls and Assistant-specific management belong to the global Assistants system exposed through Home and editors, with conversation views also available in Spaces. Users should not need to understand that Spaces infrastructure powers the Assistant.

### V2-B0078 · v2 lines 251–251

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

The Assistants system requires the applicable shared Spaces/runtime services to be installed or available, but that dependency MUST be handled as a product dependency rather than exposing Assistants as a child object inside the Spaces app. Feature parity should stay close: when Spaces gains a generally useful agentic capability, Assistants should normally gain it through the shared runtime rather than a separate reimplementation.

### V2-B0079 · v2 lines 252–252

Proactivity, permissions and Mini Computer

### V2-B0080 · v2 lines 253–253

Assistants may support explicitly configured proactive behaviour that ordinary Spaces conversations do not initiate by default, including check-ins, follow-up on unfinished work, scheduled work, configured event responses, continuing persistent tasks, proactive calls and initiating conversations where supported. The user must be able to understand and configure whether the Assistant may initiate contact, which events/schedules permit it, notification channels, resources it may access and which actions require renewed approval.

### V2-B0081 · v2 lines 254–254

A general authorisation to be proactive is NOT blanket permission to access unrelated files, accounts, computers, cloud services or projects. Assistant actions remain bound by the canonical permission system and agreed scope. Prior authorisation may cover subsequent actions within that scope but does not authorise unrelated spontaneous actions. Computer use and Mini Computer operation MUST use the canonical permission/runtime contracts and required persistent user notices. An Assistant may have its own Mini Computer VM where configured.

### V2-B0082 · v2 lines 255–255

9to1 development workflow

### V2-B0083 · v2 lines 256–256

Assistants is an early current-pass priority partly so it can become genuinely useful for continued authorised 9to1 development. A configured development Assistant MUST be able to retain its developer role/instructions, access the authorised canonical 9to1 project, open Dev directly from its Assistant conversation, use the same Files/Spaces/Dev project identity, create or resume the same durable Task/Run, inspect source, make authorised edits, run builds/tests, inspect diagnostics, show genuine diffs, coordinate delegated workers, create checkpoints, survive conversation/app closure and later resume with the same Assistant/project/task state.

### V2-B0084 · v2 lines 257–257

Include a real Assistant-initiated development acceptance journey: open the authorised 9to1 project in Dev, perform an approved bounded edit/build/test/diff/checkpoint cycle, steer or pause/resume the task, close and reopen the Assistant/task, and confirm that the same canonical project and Task/Run continue without duplicated state or effects. Exercise denied or revoked access without effects outside the grant. Do not substitute sample repositories, simulated tool responses, disconnected project copies or generic Saved-Agent tests.

### V2-B0088 · v2 lines 261–261

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

Do not count Assistants as IMPLEMENTED merely because the Agentic Update runtime exists, Spaces works, Assistant records can be persisted, Saved-Agent tests pass, an Assistant can send text, or a prototype Assistant screen exists. The actual global Assistants system and its end-to-end user workflows must exist in Home, editors and canonical conversation views.

### V2-B0090 · v2 lines 263–263

Smoke verification MUST exercise the actual packaged app, including launch, configured Assistant selection/creation as applicable, genuine conversation, persistence/reopen, shared Spaces-runtime integration, Dev handoff, permissions, at least one genuine authorised development action, steering and clean close/reopen. Extensive verification must then cover the complete Agentic Update contracts, including durable Task/Run persistence, recovery, coordinated workers, Task Dashboard/widget integration, permission enforcement, computer-use notice/control where applicable, Mini Computer operation where applicable, failed-action recovery without duplicated effects, checkpoint/artifact history, correct Assistant identity after restart and supported cross-device/platform behaviour.

### V2-B0091 · v2 lines 264–264

The full pulled-forward Assistants and Specialists scope and legacy migration arrangements in 9to1 Post-Release Updates remain mandatory. Specialists remain a distinct configured product type. A generic agent, delegated worker or Assistant does not revive the retired legacy Agents product/API except through the explicitly retained migration/compatibility arrangements.

### V2-B1870 · v2 lines 10983–10983

Agents

### V2-B1871 · v2 lines 10986–10986

Agents are a first-class Dulche-wide persistent capability layer, not a standalone 9to1 app. Dulche owns the canonical Agent registry, scope/availability resolution, invocation, runs, queues, delegation, capability resolution, lifecycle and recovery. Agent definitions, durable Agent configuration, memory namespaces and relevant persistent run metadata are stored in Den. 9to1 surfaces discover and expose applicable Agents through Dulche.

### V2-B1872 · v2 lines 10989–10989

AI Studio → Agent Builder is the direct structured authoring/editor surface for this Dulche layer. It creates/edits the canonical Den-backed Agent entity. AI Studio is not the runtime host, and closing AI Studio MUST NOT stop, hide or delete an Agent.

### V2-B1873 · v2 lines 10992–10992

Persistent Dulche Agents are distinct from Dulche Subagents. Agents have stable identity, durable configuration/memory/scope and may exist across many runs. Subagents are transient child-execution units created inside a run for delegation/parallelisation.

### V2-B1874 · v2 lines 10995–10995

Canonical Agent identity and Den persistence

### V2-B1875 · v2 lines 10998–10998

Every Agent MUST have stable AgentID independent of name plus versioned DefinitionRevision. The Den-backed Agent entity MUST retain at least name, icon/avatar, description, instructions, availability bindings, default model policy, Plugins/Connectors, Skills, Knowledge/Files references, Quick Actions, memory policy, capability/permission policy, delegation policy, graph reference where present, sharing metadata, revision history and lifecycle state.

### V2-B1876 · v2 lines 11001–11001

Renaming, moving scope or changing icon MUST NOT create another Agent. Agent display names MAY collide; AgentID remains canonical. An Agent definition revision used by an active/historical run MUST remain attributable even after later edits.

### V2-B1877 · v2 lines 11004–11004

Den MUST separate Agent definition/configuration from secrets and device-specific executable state. Connector credentials remain secure references; local executable paths/caches do not become portable Agent secrets merely because the Agent syncs through Den.

### V2-B1878 · v2 lines 11007–11007

Availability scopes and cascading

### V2-B1879 · v2 lines 11010–11010

Dulche MUST support Agent availability scopes including Global, Surface, Space and Project/Entity or equivalent narrower canonical scope bindings. An Agent MAY have multiple compatible bindings when explicitly configured.

### V2-B1880 · v2 lines 11013–11013

Global Agents cascade automatically into every compatible 9to1 surface. Surface-scoped Agents are available only on their declared surfaces. Space-scoped and Project/Entity-scoped Agents appear when the current invocation context matches those scopes. Availability is composed rather than overridden: a narrower scoped Agent supplements applicable broader Agents.

### V2-B1881 · v2 lines 11016–11016

For a context such as Write inside a Study Space/project, the effective visible set is the compatible union of Global Agents, Write-scoped Agents, current-Space Agents and current Project/Entity Agents, subject to access restrictions. Dulche MUST return origin/scope metadata so CUI can group Agents by Global, current surface and contextual scopes rather than present an unexplained flat list.

### V2-B1882 · v2 lines 11019–11019

Global cascading references the same canonical AgentID everywhere; it MUST NOT create per-app copies. Agent definition updates, memory and run history remain attached to the canonical Agent.

### V2-B1883 · v2 lines 11022–11022

Availability versus permission

### V2-B1884 · v2 lines 11025–11025

Agent availability and Agent effective permission are separate concepts. A visible Agent may still have actions disabled in the current invocation context.

### V2-B1885 · v2 lines 11028–11028

Effective permissions/capabilities MUST be no broader than the intersection of the Agent definition/instance policy, current surface capabilities, current Space policy, current project/entity scope, caller/user/organisation policy and Home permission/trust state. A surface/Space cannot grant a capability explicitly denied by the Agent/security policy, and a Global Agent does not gain universal data access merely because it is visible everywhere.

### V2-B1886 · v2 lines 11031–11035

Agent updates — static and reactive animated icons
Agents MUST support both static image icons and animated avatars. Users MUST be able to choose the presentation for each Agent. The animated-avatar concept is a pet/companion-style icon, using the user's Codex pets/Dots comparison as a design reference, rather than being limited to an unrelated looping image.
Animated avatars MUST react to the Agent's actual activity and relevant conversational events. Required examples include the avatar sitting at a computer and coding while the Agent is coding, and laughing in response to a user joke. Task-state transitions and conversational reactions MUST be configurable and previewable. Reactions are presentation events, not permission grants or claims about hidden reasoning. Animation MUST respect the existing reduced-motion controls and provide a static fallback without losing the Agent's accessible identity or readable activity status.
Agent presentation API and acceptance
The canonical Dulche Agent API MUST cover static/animated avatar selection, referenced asset and definition management, animation-state transitions and event/reaction mappings. Use Den-backed stable identities and versioned configuration shared with AI Studio’s visual builder; presentation changes MUST NOT fork the Agent, alter its permissions or leak private content. Verify saved/reopened configuration, static and animated icons, coding activity, joke/laugh reactions, reduced-motion fallback and readable accessible status, including permission-denied and missing-asset recovery. Connect’s Agent-contact contract supplies email, call and text entry points to these same Agents.

### V2-B1887 · v2 lines 11038–11038

Surface integration and Agent picker

### V2-B1888 · v2 lines 11041–11041

Every compatible interactive 9to1 surface SHOULD expose applicable Dulche Agents through the shared AI/Agent picker or a surface-appropriate invocation UI. Apps query Dulche for context-resolved Agents rather than maintaining private Agent registries.

### V2-B1889 · v2 lines 11044–11044

The invocation context MUST be typed and may include SurfaceID, SpaceID, Project/Entity references, ArtifactID, selected ObjectIDs, current semantic context and available typed actions. Dulche filters this context through permissions before supplying it to the Agent.

### V2-B1890 · v2 lines 11047–11047

Surfaces MAY recommend specialist scoped Agents more prominently than generic Global Agents. Recommendation/order MUST NOT change Agent identity or silently select an Agent with broader permissions.

### V2-B1891 · v2 lines 11050–11050

Scoped surface Agents

### V2-B1892 · v2 lines 11053–11053

First-party apps MAY ship/register useful surface-scoped Dulche Agent definitions/bindings. Once registered, they are canonical Dulche Agents rather than hidden app-local assistants and can be inspected/edited where policy permits.

### V2-B1893 · v2 lines 11056–11056

Write MAY expose specialist writing Agents such as Editor, Proofreader, Long-form Writer or user-created writing Agents. A Write-scoped Agent can receive authorised semantic document/selection/style/comment/reference context and Write typed actions without becoming available on unrelated surfaces.

### V2-B1894 · v2 lines 11059–11059

Vision & Voice MAY expose a Podcast Agent or similar specialist Voice Agents. A Podcast Agent can accept typed inputs such as Topic, optional Sources, target Duration and Style, then use the Voice Monologue mode to research/plan/generate/play a coherent long-form podcast. It remains a canonical Dulche Agent scoped to Vision & Voice, not a hardcoded private voice feature.

### V2-B1895 · v2 lines 11062–11062

Agent invocation interfaces

### V2-B1896 · v2 lines 11065–11065

An Agent definition MAY declare a typed InvocationSchema and optional CUI/Generative UI invocation interface. Surfaces SHOULD render the declared inputs rather than force every Agent through a blank chat box.

### V2-B1897 · v2 lines 11068–11068

Invocation UI definitions MUST bind only to declared Agent inputs and approved typed actions; they MUST NOT grant new permissions. Examples include a Voice Podcast form with Topic/Sources/Duration/Style or a Write rewrite form with Tone, Preserve citations and Scope.

### V2-B1898 · v2 lines 11071–11071

Quick Actions

### V2-B1899 · v2 lines 11074–11074

Quick Actions are named manual entry points into a canonical Agent and cascade wherever that Agent is available, subject to each Quick Action's context requirements/visibility rules. They are not separate Agents.

### V2-B1900 · v2 lines 11077–11077

Every QuickAction MUST have stable QuickActionID, AgentID, label, optional icon/description, predefined instruction or named graph entry point, optional typed InputSchema, context requirements, confirmation policy and ordering metadata.

### V2-B1901 · v2 lines 11080–11080

Quick Actions may be context-sensitive. For example, Rewrite selection should appear only where an editable selection exists; a Podcast Agent may expose Make podcast, Continue latest podcast or Create briefing. Invoking one creates a normal canonical AgentRun with standard permissions, Action Graph, side-effect/idempotency controls and run history.

### V2-B1902 · v2 lines 11083–11083

Quick Actions are distinct from Automations. Quick Actions are manually invoked entry points; Automations own recurring, scheduled, event-triggered and condition-triggered execution. An Automation MAY invoke an Agent/Quick Action through the same Dulche APIs.

### V2-B1903 · v2 lines 11086–11086

Default model resolution
