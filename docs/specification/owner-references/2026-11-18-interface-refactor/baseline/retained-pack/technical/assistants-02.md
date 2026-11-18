# Global Assistants, Specialists and collaborators — Plugins, Connectors, Skills and Knowledge; Agent memory (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1904 · v2 lines 11089–11089

An Agent may define Default Model = Inherit or an explicit Universal Model Picker policy. Inherit resolves from the invocation Space/surface/global policy. An explicit Agent default takes precedence over inherited defaults where allowed, while request-specific overrides may take precedence only if Agent/security policy permits.

### V2-B1905 · v2 lines 11092–11092

Dulche MUST capability-check the effective model against the Agent/run requirements such as tools, vision, audio, context or structured output. It MUST not silently cross local/private to cloud boundaries or substitute an explicitly forbidden fallback.

### V2-B1906 · v2 lines 11095–11095

Plugins, Connectors, Skills and Knowledge

### V2-B1907 · v2 lines 11098–11098

Agent capability bindings reference canonical Dulche Plugins/Connectors/MCP capabilities and Skills by stable identity/version policy. They are not copied into the Agent.

### V2-B1908 · v2 lines 11101–11101

Knowledge/Files references point to Den memory and authorised canonical files/folders/artifacts/providers where possible. The Agent may retrieve current source revisions through owning APIs rather than rely on stale copies. Missing/revoked/incompatible sources or packages MUST be represented explicitly.

### V2-B1909 · v2 lines 11104–11104

Agent memory

### V2-B1910 · v2 lines 11107–11107

Each persistent Agent MUST have an Agent-specific Den memory namespace governed by its memory policy. Durable memory is structured semantic/operational state, not simply an unbounded copy of every conversation/tool result.

### V2-B1911 · v2 lines 11110–11110

Memory writes MUST carry scope/provenance and MUST respect personal/team/shared boundaries. A shared Agent MUST NOT silently copy a participant's private personal Den context, private connected-account data or secrets into shared Agent memory.

### V2-B1912 · v2 lines 11113–11113

Agent memory SHOULD support inspection, conflict handling, deduplication, recency/provenance metadata and removal/disable operations. Secrets encountered in tools/results MUST NOT automatically become durable Agent memory.

### V2-B1913 · v2 lines 11116–11116

Tasks, work queues and runs

### V2-B1914 · v2 lines 11119–11119

A persistent Agent MAY maintain a durable work queue in Dulche. Queue items MUST have stable QueueItemID, AgentID, objective, optional TaskID, priority, creator/trigger provenance, dependencies, status and timestamps. Work may arrive from a user, Space/Task, Automation, another authorised Agent or recovery flow.

### V2-B1915 · v2 lines 11122–11122

A Task is user-level work; an Agent is the persistent worker; AgentRun is a specific execution. The relationship MUST remain explicit: a Task/objective may be assigned to an Agent, which creates one or more AgentRuns. Agents are not themselves a second Task or Automation system.

### V2-B1916 · v2 lines 11125–11125

Each AgentRun MUST have stable AgentRunID, AgentID, DefinitionRevision, objective, trigger/source, optional TaskID/QueueItemID, status, start/end timestamps, effective model/capabilities/permissions, ActionGraphID, outputs, blockers, approvals, usage/budget metadata and recovery/checkpoint state where supported.

### V2-B1917 · v2 lines 11128–11128

Run states and controls

### V2-B1918 · v2 lines 11131–11131

Canonical user-facing Agent/run states MUST distinguish at least Idle, Queued, Running, Waiting, Blocked, Paused, Awaiting Approval, Completed, Failed/Error and Disabled as appropriate. Blocked and Awaiting Approval MUST NOT be conflated.

### V2-B1919 · v2 lines 11134–11134

Run controls MUST support Pause/Resume, safe Stop and explicit Cancel where the runtime permits them. Resume continues the same AgentRunID and MUST NOT repeat already-completed consequential actions. Retry of a failed step/run uses idempotency/operation identity to avoid replaying unrelated successful side effects.

### V2-B1920 · v2 lines 11137–11137

Progress UI MUST show truthful objective/current step/known plan progress. It MUST NOT invent a percentage where the Agent cannot estimate completion meaningfully.

### V2-B1921 · v2 lines 11140–11140

Blockers and approvals

### V2-B1922 · v2 lines 11143–11143

Dulche Agent runs MUST create first-class Blocker records for conditions preventing progress, including category, description, time, whether the Agent can self-resolve, required human action and related entities. Agents SHOULD attempt safe recovery of foreseeable recoverable blockers when their contract allows it instead of immediately escalating every failure.

### V2-B1923 · v2 lines 11146–11146

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

Human-required blockers and pending consequential actions MUST surface through relevant invoking surfaces and Home Notifications activity/approval UX. Approval remains a Home permission/action decision; Dulche Agents cannot self-authorise through their own instructions.

### V2-B1924 · v2 lines 11149–11149

Agent-to-Agent delegation

### V2-B1925 · v2 lines 11152–11152

Persistent Agent-to-Agent delegation is a Dulche Agent capability. Delegation MUST retain explicit parent/child AgentRun identities and a typed handoff containing objective, required context references, narrowed capability scope and expected output.

### V2-B1926 · v2 lines 11155–11155

DelegationPolicy MUST be able to constrain allowed persistent Agents/classes, whether Subagents are allowed, maximum concurrency, maximum depth and applicable token/time/cost/tool budgets. A receiving Agent does not automatically inherit all permissions or memory of its parent.

### V2-B1927 · v2 lines 11158–11158

Persistent Agent delegation remains distinct from Dulche Subagents. Subagents are lighter transient child execution primitives inside the runtime; persistent delegated Agents retain their own canonical AgentID, definition and Den memory policy.

### V2-B1928 · v2 lines 11161–11161

Action Graph, outputs and operational visibility

### V2-B1929 · v2 lines 11164–11164

Every AgentRun MUST use the canonical Dulche Action Graph/log/audit pipeline. 9to1 surfaces may render a concise human-readable Activity view and/or the full Action Graph, but they MUST NOT invent a second execution history.

### V2-B1930 · v2 lines 11167–11167

Run outputs MUST be typed references to their canonical owning entities where possible, including Files, documents, Boards, code changes, Planner/Task items, messages, research outputs and other artifacts. Opening an output routes to its owning app/entity rather than copying it into an Agent-only store.

### V2-B1931 · v2 lines 11170–11170

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

Agent runs, blockers, approvals and outputs may be surfaced contextually in Spaces, Agent Builder Preview, Automations that triggered them, Home Notifications activity and the invoking app. No standalone Agents app is required or authoritative.

### V2-B1932 · v2 lines 11173–11173

Lifecycle, recovery and budgets

### V2-B1933 · v2 lines 11176–11176

Long-running/background-capable AgentRuns MUST checkpoint enough state to recover or truthfully fail across app closure, Home/Dulche restart, machine restart where supported, provider disconnect or temporary tool failure. Recovery states SHOULD distinguish Reconnecting, Recovering, Recovered and Cannot recover rather than silently restarting from scratch.

### V2-B1934 · v2 lines 11179–11179

Agents MUST support configured runtime/model/tool/delegation budgets such as maximum runtime, spend/tokens, tool calls, concurrent children or delegation depth where the platform can enforce them. Exceeding a budget produces an explicit blocked/failed/approval state rather than uncontrolled continuation.

### V2-B1935 · v2 lines 11182–11182

Canonical Dulche Agent API

### V2-B1936 · v2 lines 11185–11211

Dulche.Agent.List(context?);
Dulche.Agent.Get(agentID);
Dulche.Agent.ResolveFor(context);
Dulche.Agent.Create(definition);
Dulche.Agent.UpdateDraft(agentID, patch, expectedRevision?);
Dulche.Agent.Validate(agentID, revision?);
Dulche.Agent.Activate(agentID, revision);
Dulche.Agent.GetRevision(agentID, revision);
Dulche.Agent.SetAvailability(agentID, availability);
Dulche.Agent.ListQuickActions(agentID, context?);
Dulche.Agent.Invoke(agentID, input, context);
Dulche.Agent.InvokeQuickAction(agentID, quickActionID, input, context);
Dulche.Agent.Queue.List(agentID);
Dulche.Agent.Queue.Enqueue(agentID, workItem);
Dulche.Agent.Queue.Reorder(agentID, queueItemID, position);
Dulche.Agent.Run.Get(agentRunID);
Dulche.Agent.Run.Pause(agentRunID);
Dulche.Agent.Run.Resume(agentRunID);
Dulche.Agent.Run.Stop(agentRunID);
Dulche.Agent.Run.Cancel(agentRunID);
Dulche.Agent.Run.Retry(agentRunID, options?);
Dulche.Agent.Blocker.List(agentID?);
Dulche.Agent.Blocker.Resolve(blockerID, resolution);
Dulche.Agent.Memory.List(agentID);
Dulche.Agent.Memory.Remove(agentID, memoryID);
Dulche.Agent.Delegate(parentRunID, childAgentID, handoff);
Dulche.Agent.Changes.Subscribe(cursor?);

### V2-B1937 · v2 lines 11214–11214

These are minimum canonical entry points. All Agent Builder and 9to1 surface Agent operations MUST resolve through the same Dulche/Den contracts rather than creating app-private Agent stores or runtimes.

### V2-B1938 · v2 lines 11217–11217

Failure behaviour

### V2-B1939 · v2 lines 11220–11220

Dulche Agents MUST return structured failures including AgentNotFound, DefinitionRevisionUnavailable, AgentDisabled, AgentUnavailableInScope, AgentBusy, AgentRunNotFound, RunNotResumable, RunRecoveryFailed, QueueItemNotFound, CapabilityUnavailable, PermissionDenied, ApprovalRequired, BlockerUnresolved, DelegationDenied, DelegationDepthExceeded, ConcurrencyLimitExceeded, BudgetExceeded, ModelUnavailable, ToolUnavailable, MemoryScopeDenied, QuickActionUnavailable, InvalidInvocationContext and RevisionConflict in addition to the global Dulche error contract.

### V2-B1940 · v2 lines 11223–11223

Acceptance requirements

### V2-B1941 · v2 lines 11226–11245

Dulche Agents is implementation-complete only when acceptance coverage verifies at least:
* An Agent created in AI Studio Agent Builder is stored as a canonical Den-backed Dulche Agent and remains usable after AI Studio closes.
* There is no authoritative standalone Agents app or duplicate Agent database.
* Global Agents appear on every compatible 9to1 surface through Dulche scope resolution without being copied.
* Surface-, Space- and Project/Entity-scoped Agents appear only in matching contexts while applicable broader Agents remain available.
* Agent availability does not grant permissions; effective capability resolution narrows correctly across Agent, surface, Space, caller and Home policy.
* Write can expose/use specialist writing Agents with semantic document context, and Vision & Voice can expose/use a Podcast Agent that drives Monologue mode from typed inputs.
* Quick Actions cascade with their Agent, obey context visibility/input schemas and create normal AgentRuns rather than duplicate Agents.
* Default Model inheritance/explicit override resolves through the Universal Model Picker without violating privacy/fallback restrictions.
* Agent memory persists in the correct Den namespace without silently ingesting private/shared-incompatible context or secrets.
* Task → Agent → AgentRun and Automation → AgentRun relationships remain explicit.
* Agent queues preserve provenance, priorities/dependencies and durable state.
* Blocked and Awaiting Approval states remain distinct and recoverable blockers can be handled autonomously where authorised.
* Pause/Stop/Resume/Retry do not replay completed consequential actions.
* Persistent Agent delegation obeys depth/concurrency/budget limits and does not transfer unrestricted parent permissions.
* Persistent Agents and Dulche Subagents remain distinct in API, logs and UI.
* AgentRun Action Graph is the canonical execution graph and outputs open canonical owning entities.
* Home/Dulche restart can recover supported durable runs or reports Cannot recover truthfully without silently restarting work.
* Agent Builder Preview uses the same Dulche semantics while clearly remaining test-scoped.
* Agent Builder/other surfaces perform Agent operations through the canonical Dulche Agent API.

### V2-B2265 · v2 lines 13334–13340

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

2026-10-09 — APPROVED ASSISTANTS, SPECIALISTS, CONNECT AND INLINE COMMAND REQUIREMENTS
Assistants are persistent configurable conversational identities, distinct from job-defined reusable Specialists. Each Assistant owns multiple persistent conversation threads, with create, rename, switch, search, pin, archive, delete and branch management. Thread history and work context are separate, while Assistant-level memory and permitted cross-thread retrieval are shared. Spaces and Assistants open the same canonical threads and Task/Runs without copying state.
All 9to1 chat services must automatically compact working context while retaining complete original messages and retrievable history, so conversations can continue for months or years without arbitrary message-count caps. Finite model context and resource limitations must be handled honestly; summaries must never replace source history.
Assistants must retain all inherited Agents functionality, with complete feature-parity auditing and user-facing management for identity, instructions, knowledge/files, memory, models, tools/apps, Skills, Quick Actions, permissions, approvals, budgets, queues, delegation, history, background execution, steering, recovery, proactive contact and optional Mini Computer. Support configuration revision history, data lifecycle controls, search, durable artifacts, notifications and concurrent tasks.
Connect is the 9to1 communications app (email, chat and supported calls), not the external integration manager. Assistants and Specialists are selectable addressable contacts. Users can type an Assistant name into an email recipient field or message/call them. Responses stay in the originating channel and thread: email replies to email, chat to chat, calls in calls. The same Assistant identity and authorised work remain accessible in the Assistants system; approvals and notifications may surface separately without moving the primary reply.
Across supported 9to1 composers, @ opens a searchable picker for Assistants, Specialists and supported apps (such as Write and Present). A bare mention is passive and does not execute. An explicitly bounded instruction, for example @Present [Create five slides from this report], is a structured compact INLINE rich-text command, not a separate block editor, card or paragraph. Only that mention and its instruction are specially formatted; surrounding text remains normal, and typing resumes normally after the command. Support multiple commands in one paragraph, wrapping, editing, keyboard accessibility, persistence, copying and canonical identity. Preserve exact boundaries so ordinary text cannot accidentally execute. Commands respect Home permissions, approvals and revocation.
Implement and accept these as complete vertical product features with usable UI, backing behaviour, persistence, integration and real tests, not just architecture or compilation. Preserve existing compliant infrastructure and all earlier specification requirements.

### COMPANION-B0041 · companion lines 287–289

SPUI-154 — PERMANENT MAIN CONVERSATION PER ASSISTANT (OWNER CONFIRMED)
Each persistent Assistant has exactly one auto-created primary conversation named “Main”. It is the Assistant's durable, personal, default conversation, conceptually like the ongoing conversation with a single contact in a messaging app. Selecting that Assistant normally opens Main rather than the last unrelated sub-conversation or an intermediary mandatory dashboard. Main retains its canonical ConversationID, Assistant identity, eligible history and authorised context across application navigation/restarts; creating or switching other chats must not silently recreate, replace, erase or split Main. The “Main” role/identity is permanent. Whether the visible label may be user-renamed while preserving the Main role is NOT YET DECIDED; do not imply a disposable Main.
The Assistant may have multiple ADDITIONAL conversations with their own titles, histories, current work/context and canonical ConversationIDs, used to organise projects and specialised topics. These belong to the same persistent Assistant identity and share only the Assistant-level memories, configured policies, permissions and cross-thread references actually authorised. Main is not an aggregate transcript of every sub-conversation. Preserve existing approved thread operations on eligible non-Main chats (create/rename/search/pin/archive/delete/branch etc) while protecting Main's persistent role.

### COMPANION-B0042 · companion lines 292–293

SPUI-155 — CONVERSATION-TO-CONVERSATION WORK HANDOFF (OWNER CONFIRMED)
A user can plan a project in an Assistant's Main conversation and hand it to another conversation with the SAME Assistant to implement, or hand off from one organised conversation to another. The receiving conversation can be new or existing. The handoff carries a visible, inspectable and permission-filtered package of the agreed goal, scope, accepted plan/constraints, relevant messages/source references, task/run identity, checkpoints, outputs, remaining work and applicable acceptance criteria, to the extent legitimately authorised. Do NOT move/copy entire private histories indiscriminately or duplicate external side effects. Original source messages remain in their original conversation, with traceable references. The same canonical Assistant identity and existing TaskID/RunIDs persist if continuing the same work; creating a new task requires an explicit genuinely separate objective. Workers must apply changed instructions at safe boundaries. Proposed affordance “Hand off to Conversation” is a useful UI label but exact menu placement/visual presentation remains OPEN for review.

### COMPANION-B0043 · companion lines 296–297

SPUI-156 — PERSISTENT ASSISTANT TEAMS (OWNER CONFIRMED)
The Assistants Space adds “Teams”: persistent user-created group chats containing multiple selected, existing persistent Assistants. Teams have stable identity, member roster, conversational history and assigned responsibilities. They are NOT merely a one-off Multi-Agent Chat, a Chat Group/Project organisation container, a single new meta-Assistant, a Specialist catalogue, or the Dulche runtime's disposable subagent group. Existing Assistant identities, personal Main conversations and individually authorised resources remain distinct while they participate in Teams. Users may ask a Team to accomplish an outcome, and different members can undertake different roles and Tasks (e.g. a future job split by speciality), coordinated through the existing durable Tasks/Run and Automation/Planner services. Do not force redundant Assistant/Task records for every member or Team message.

### COMPANION-B0044 · companion lines 300–304

SPUI-157 — CONFIGURABLE TEAM COORDINATION (OWNER CONFIRMED)
Team settings MUST provide a user-selectable coordination mode with BOTH alternatives:
• Automatic Dulche Coordination: Dulche allocates relevant work among eligible members, manages dependencies and execution, and does not require a permanent Assistant leader.
• Designated Assistant Leader: user chooses one eligible Team Assistant as leader/coordinator for assignment and delegation while Dulche still enforces routing, permissions, tasks, durable state, resource limits and safety.
Switching mode does NOT discard TeamID, roster, messages, valid TaskID/RunIDs, outputs or accepted checkpoints; in-flight assignment/ownership changes need truthful transitions and no duplicated work. Whether Automatic is the initial default is still PROPOSED (not explicitly owner-confirmed), as are detailed leader absence/fallback and contested delegation resolution policies. Do not silently hard-code them as decisions.

### COMPANION-B0045 · companion lines 307–308

SPUI-158 — RELEVANCE-GATED SELECTIVE PARTICIPATION (OWNER CONFIRMED)
Dulche decides which Team Assistants should participate and respond in any particular request based on relevant role/expertise, actual eligibility and permissions, necessary dependencies and task context. Membership ALONE does not cause every Assistant to reply or consume model resources. Example: requesting a video should ordinarily engage the video-relevant Assistant, NOT a Programmer unless programming becomes useful for that request. Members can be brought in later if the job develops a genuine dependency; don't require irrelevant acknowledgements. A directed @Assistant mention intentionally addresses that member, but still respects permissions/capability constraints and cannot grant unrelated access. This routing rule applies in BOTH coordination modes. An Assistant omitted from the current discussion may continue independently authorised earlier work; absence of its chat reply does not stop existing work. Model-routing decisions, authorised handoffs and resulting worker actions must remain inspectable with correct provenance rather than fabricated participation.
