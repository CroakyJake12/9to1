# Dulche runtime, requests and execution services

Module ID: `dulche` · 9to1 Development Specification v3

## Prerequisites

- **Platform, app registry and host integration** — Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Dulche is the shared AI/runtime broker, owning model routing, request execution, Task/Runs, tool actions and integrations with memory and subagents. Its lifecycle and results are observable through typed contracts rather than interface simulations.

Current owner overrides: V3-RECON-001.

## Layouts & visible states

- **V3-DULCHE-LAY-01** — Expose Starting, Ready, Degraded, Restarting and Stopped accurately where relevant. An unavailable broker must not disable unrelated local editing.
- **V3-DULCHE-LAY-02** — Show request admission, active execution, waiting approval/input, paused, cancelled, completed and failed as distinct states; elapsed time represents measured activity where specified.

## Features & behaviour

- **V3-DULCHE-FEA-01** — Use canonical requests and ordered eligible model routes. Filter capabilities, privacy, entitlements and resources before choosing a route. Local-only must never silently fall back to cloud.
- **V3-DULCHE-FEA-02** — Keep fallback routing distinct from tool retry and external-effect retry. Durable execution records support idempotent recovery, cancellation and inspection.
- **V3-DULCHE-FEA-03** — Preserve retained runtime APIs, transport, multi-engine/Strata and embedded service contracts. UI work must not silently replace them with a new backend.
- **V3-DULCHE-FEA-04** — Treat supplied content as data with provenance; shared-context services, safety checks and action dispatch remain within the trust boundary defined in retained clauses.

## Canonical entities & persistence

- **V3-DULCHE-ENT-01** — Persist request IDs, TaskID, RunID, ActionGraphID, admission configuration, model/route identity, status events and output references. A trace is a structured runtime record, not a fabricated progress feed.
- **V3-DULCHE-ENT-02** — Maintain durable queue and checkpoint state separately from transient view state. Reopening a view attaches to existing execution.

## Permissions & recovery

- **V3-DULCHE-PER-01** — Propagate current grants, budgets and revocation to tools and child tasks. Stop/interrupt acknowledges actual runtime state and does not promise an already-committed external effect can be undone.
- **V3-DULCHE-PER-02** — Resume from safe checkpoints; represent partial output, degraded providers and uncertain external outcomes explicitly.

## Acceptance checks

- **V3-DULCHE-ACC-01** — Test admission refusal without losing draft, provider failure/fallback, local-only routing, cancellation acknowledgement, restart recovery and idempotent tool handling.
- **V3-DULCHE-ACC-02** — Confirm semantic API operations and displayed task states match real runtime events.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

SPUI-194

## Technical reading map

- Dulche runtime, requests and execution services — Dulche runtime control; Permission and trust broker (part 01) — `technical/dulche-01.md` (2,385 words; V2-B0453, V2-B0454, V2-B0455…).
- Dulche runtime, requests and execution services — Result contract and metric availability; Response content and identity (part 02) — `technical/dulche-02.md` (2,271 words; V2-B2036, V2-B2037, V2-B2038…).
- Dulche runtime, requests and execution services — Persistent request execution; Context compaction and Context Summarisation (part 03) — `technical/dulche-03.md` (2,513 words; V2-B2083).
- Dulche runtime, requests and execution services — Dulche multi-engine inference runtime; Runtime abstraction and dispatcher (part 04) — `technical/dulche-04.md` (992 words; V2-B2248, V2-B2249, V2-B2250…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Dulche runtime, requests and execution services — Dulche runtime control; Permission and trust broker (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0453 · v2 lines 2338–2339

* Voice named variants, Image inheritance, Advanced Audio/Video, reasoning controls, aliases and local-model lifecycle preserve the existing category-specific contracts.
Dulche runtime control

### V2-B0454 · v2 lines 2342–2342

Home Core owns the single shared Dulche broker/runtime lifecycle for native 9to1 apps. It MAY host multiple logical endpoints/models concurrently subject to resource policy, but apps MUST attach to the shared broker rather than launching competing private model servers.

### V2-B0455 · v2 lines 2345–2345

Home MUST expose shared Dulche service state including Starting, Ready, Degraded, Restarting and Stopped plus endpoint/model load state, resource pressure and health/diagnostic information required for recovery. Logical EndpointID/SessionID identity remains governed by the Dulche Runtime contract.

### V2-B0456 · v2 lines 2348–2348

If the visible Home shell closes, Home Core MAY continue in the background. If Home Core crashes/restarts, attached apps MUST enter a degraded/reconnecting state for Home-dependent features, preserve their own user data, and reconnect to restored shared services. Apps MUST NOT launch private replacement Dulche servers during recovery.

### V2-B0457 · v2 lines 2351–2351

Permission and trust broker

### V2-B1823 · v2 lines 10791–10798

Dulche
Framework role
Dulche is 9-1’s shared llama.cpp-derived local model framework/runtime. One Home-managed Dulche instance serves every 9-1 app through a broker API; apps must never launch their own competing local model servers. It owns model loading, inference lifecycle, resource limits, cancellation and local capability discovery while preserving upstream provenance.
Voice
Voice is a Dulche capability. Dulche owns the audio-session runtime, speech/model lifecycle, streaming, interruption and integration with requests, tools, permissions and logs. The user-facing controls live in Vision & Voice rather than Home.
Voice API
Dulche.Voice();
Entry point to the Dulche Voice subsystem for the caller’s current context.

### V2-B1824 · v2 lines 10801–10811

Dulche.Voice.StartSession(sessionID?);
Dulche.Voice.StopSession(sessionID);
Dulche.Voice.PauseSession(sessionID);
Dulche.Voice.ResumeSession(sessionID);
Dulche.Voice.Interrupt(sessionID);
Dulche.Voice.SetInputDevice(deviceID, sessionID);
Dulche.Voice.SetOutputDevice(deviceID, sessionID);
Dulche.Voice.SetVoice(voice, sessionID);
Dulche.Voice.SetModel(model, sessionID);
Dulche.Voice.SendAudio(audio, sessionID);
Dulche.Voice.GetState(sessionID);

### V2-B1825 · v2 lines 10814–10814

The documented Voice APIs are authoritative but not exhaustive. Additional APIs required for complete voice functionality may be added following Dulche’s existing API conventions and the global API-parity rule.

### V2-B1942 · v2 lines 11248–11249

Action Graph
Action Graph is a Dulche feature and the user-facing structured view of an AI/agent execution log. It turns the underlying request history into an inspectable graph rather than requiring users to read a raw continuous log.

### V2-B1943 · v2 lines 11252–11255

The graph can show prompts, model calls, thinking/response phases where available, tool calls, AI actions, approvals, dependencies, retries, generated artifacts, failures, hard errors, blockers and timestamps. It is derived from Dulche’s audit/tool/full-log data so the graph and logs describe the same execution rather than separate histories.
Action Graph API
Dulche.ActionGraph(requestID);
Returns the structured Action Graph for a request, including the data needed for CUI/Markdown rendering.

### V2-B1944 · v2 lines 11258–11260

Dulche.ActionGraph.GetNode(requestID, nodeID);
Dulche.ActionGraph.ListEvents(requestID);
Dulche.ActionGraph.RestartFrom(requestID, nodeID);

### V2-B1945 · v2 lines 11263–11265

RestartFrom forks/restarts the prompt or agent run from the selected point while preserving the original history. Earlier completed actions remain auditable; actions after the restart point are re-executed as a new branch and remain subject to current permissions/approval requirements.
Shared rendering
Dulche produces the Action Graph data and the CUI/Markdown representation needed to render it. The shared renderer source lives in Home and is reusable across the ecosystem, so apps do not need separate implementations.

### V2-B1946 · v2 lines 11268–11271

Any app may surface the renderer where useful. For example, Spaces may expose a Show Action Graph button for a response, while Home can expose the same graph from logs or activity history.
Runtime
Purpose
Dulche Runtime provides a simple, consistent API for starting, configuring and controlling model endpoints without requiring each app or harness to manually manage model-server lifecycle.

### V2-B1947 · v2 lines 11274–11276

Endpoint lifecycle
Dulche.Start(port);
Starts a local Dulche endpoint on the specified port. Local Start requires an explicit port; Start(Remote(...)) instead requires an explicit remote target.

### V2-B1948 · v2 lines 11279–11280

Dulche.Test(port);
Checks whether Dulche is reachable on the specified port.

### V2-B1949 · v2 lines 11283–11284

Dulche.Prompt(port);
Sends a prompt/instruction to the specified endpoint.

### V2-B1950 · v2 lines 11287–11288

Dulche.SetModel(model, port);
Sets the model used by the specified endpoint.

### V2-B1951 · v2 lines 11291–11292

Dulche.SetTemperature(temperature, port);
Sets the temperature used by the specified endpoint.

### V2-B1952 · v2 lines 11295–11296

Dulche.Pause(port);
Suspends model activity while keeping the model and its context loaded.

### V2-B1953 · v2 lines 11299–11300

Dulche.Stop(port);
Stops the model and clears the endpoint/port.

### V2-B1954 · v2 lines 11303–11304

Dulche.SetReasoningLevel(level, port);
Sets the endpoint's reasoning level. level may be a named effort such as low or medium, or a percentage. Percentages are mapped across the reasoning levels exposed by that model. For example, if a model exposes five reasoning levels, the 0-100 range is divided across those five levels and the matching level is selected.

### V2-B1955 · v2 lines 11307–11310

Multi-endpoint scope
For endpoint-wide commands other than Start, an omitted/null port targets the active endpoints authorised in the caller's scope. RequestID-based controls are never broadcasts. Return per-endpoint outcomes, retaining partial failures and isolated sessions.
Dulche.Test();
Returns the list of ports currently used by Dulche.

### V2-B1956 · v2 lines 11313–11314

Dulche.Prompt();
Sends the prompt/instruction to all active endpoints.

### V2-B1957 · v2 lines 11317–11318

Dulche.Stop();
Stops all Dulche endpoints.

### V2-B1958 · v2 lines 11321–11329

Endpoint configuration
Endpoints can be created and configured as one unit. Example:
Endpoint Laguna = new Endpoint(port) {
    Model = GetModelByID(laguna-xs);
    Context = 19000;
    Timeout = 300;
    ToolCallsAllowed = All;
    // Other supported model/endpoint properties may also be configured here.
};

### V2-B1959 · v2 lines 11332–11332

Timeout is disabled by default. When set, an endpoint that remains inactive for the specified number of seconds is automatically paused rather than stopped, preserving the loaded model and context.

### V2-B1960 · v2 lines 11335–11336

Capabilities
Dulche natively provides the persistent Agent layer, Subagents/parallelisation, command usage, code execution, local and cloud models, tool calling, MCPs, Plugins, Skills and Generative UI. MCPs, Plugins and Skills are first-class extension types with stable identity, lifecycle, permissions, validation and dedicated APIs; they MUST NOT exist only as ad-hoc wrappers around generic tool calls. Persistent Agents are Den-backed durable Dulche entities, while Subagents are Runtime child-execution primitives. Capabilities can be disabled within scope when they are not permitted or required.

### V2-B1961 · v2 lines 11341–11341

Dulche.SetCapability(UI, false);

### V2-B1962 · v2 lines 11344–11362

Tool registry, semantic capability discovery and feasibility resolution
Dulche MUST expose a canonical request-aware Tool Registry describing the tools, executable capabilities and usable resources that a model can employ to satisfy a request. The registry is broader than a literal list of function names: it includes first-party 9to1 app actions/APIs, Automations actions, Plugins, MCP tools, callable Agents, Skills that contribute executable workflows through authorised dependencies, Files/resource retrieval, image/audio/video generation capabilities, code/shell tools, web/network tools where permitted and other registered Dulche capabilities. Computer Use is represented only when it has been explicitly invoked under the Computer Use contract.
Every registry entry MUST use stable identity and structured metadata. At minimum it exposes ToolID/CapabilityID, source type and owner, version/revision, human-readable name, action IDs, semantic capability/intent tags, supported input/output types, parameter schemas, prerequisites, permission scopes, side-effect/risk classification, platform/scope constraints, availability/health and whether user approval or additional configuration is currently required. Display name and wording of an action MUST NOT be the sole mechanism used to decide what a tool can do.
Dulche MUST derive an EffectiveCapabilitySet for each request from the canonical registry. The effective set is filtered by the active user/profile, current app/Space/project, installed/enabled package state, app/API availability, explicit @ invocations, platform capability, privacy policy, administrator policy, current permissions/trust and any request-specific restrictions. Installed-but-unavailable, available-but-unapproved and immediately executable capabilities MUST remain distinguishable.
The active model/orchestrator MUST be given an inspectable, context-budget-aware representation of the EffectiveCapabilitySet. Dulche MAY provide a compact high-level catalogue initially and expose query/search APIs for deeper inspection so very large tool inventories do not consume the model's entire context. Capability discovery MUST nevertheless remain available throughout the request and MUST refresh when packages, permissions, target apps or connection state materially change.
Tool selection MUST be intent-based rather than exact-name-based. Dulche MUST translate the user's requested outcome into one or more semantic intents and compare those intents against tool capability metadata, schemas and declared examples/aliases. A tool does not need an action whose literal name matches the user's wording when its underlying capability can satisfy the requested effect.
For example, a request such as 'remind me tomorrow at 4 PM' MUST be resolvable to a time-triggered Automations operation even if no action is literally named CreateReminder. The semantic requirement is a durable time-based trigger that produces the requested notification/action. Likewise, 'create an image of ...' MUST resolve to an eligible image-generation capability such as the first-party image app/tool or an installed authorised image-generation Plugin, even if the tool's action is named Generate, Render, Imagine or another equivalent verb.
Capability resolution MUST support composition. If no single tool can fulfil the complete request but a sequence of authorised capabilities can, Dulche MUST be able to construct a typed ResolutionPlan whose steps pass structured outputs/references between tools. For example, a request to 'create an image and put it on the title slide' may resolve to image generation followed by Present insertion; a request to 'find the file and email it' may resolve to Files search followed by a Mail/Plugin send action, subject to normal permissions and approvals.
ResolutionPlan steps MUST identify the selected ToolID/CapabilityID, action, required arguments, expected outputs, dependencies, permission/approval state and why the step satisfies part of the user's intent. Capability resolution is planning, not execution: selecting a feasible tool MUST NOT bypass its normal permission, approval, confirmation, privacy, cost or side-effect rules.
Before a model states or implies that 9to1 cannot perform a requested digital action, it MUST first perform capability feasibility resolution against the current EffectiveCapabilitySet unless the impossibility is already established by a current authoritative hard constraint. The model MUST NOT answer 'I cannot directly create images', 'I cannot create reminders', 'I cannot access files' or equivalent merely because the base model itself lacks that native capability when Dulche exposes an authorised tool path that can perform it.
A capability that exists but is missing a prerequisite MUST be described as blocked/configuration-required rather than impossible. Examples include ApprovalRequired, PermissionDenied, AccountConnectionRequired, MissingRequiredInput, AppUnavailable, ProviderUnavailable, CapabilityDisabledByPolicy and UnsupportedOnCurrentPlatform. When the missing prerequisite can be resolved within the current request and permission bounds, Dulche SHOULD resolve it or ask only for the specific user decision/input required and then continue.
If several feasible tools can perform the same intent, Dulche MUST select according to explicit user invocation/instruction first, then applicable app ownership/canonical-data rules, privacy/locality policy, capability fitness, permissions, reliability and configured user preferences. Cost/latency MAY participate where policy allows. Dulche MUST NOT choose an unrelated or lower-fidelity tool merely because its action name more closely resembles the user's wording.
Explicit @ invocation remains authoritative. An @Plugin, @App, @Agent, @Skill or other invocation narrows or prioritises the capability source according to that invocation's contract. However, ordinary natural-language requests do NOT require the user to know or name the underlying tool. 'Create a presentation', 'set a reminder', 'generate an image' and similar outcome requests MUST be sufficient for Dulche to discover and use the appropriate capability when one is available.
Skills, Agents and Files participate according to their real semantics rather than being falsely treated as identical callable functions. A Skill may contribute instructions/workflow/tool dependencies; an Agent may be invoked as a delegated execution capability; a File/resource may provide required input/context; an App/Plugin/MCP/native action may perform the mutation. The resolver MUST be able to combine these heterogeneous entries into one plan while preserving their distinct identities, permissions and audit records.
Dulche MUST expose canonical discovery operations equivalent to Dulche.Tool.List(context?), Dulche.Tool.Search(intent, context?), Dulche.Tool.Get(toolID), Dulche.Tool.Resolve(intent, context?) and Dulche.Tool.ExplainResolution(resolutionID). Resolve MUST return either one or more ranked feasible ResolutionPlans or a structured no-feasible-path result containing the evaluated hard constraints and missing prerequisites. Search/Resolve MUST use semantic capability metadata in addition to lexical names.
The Runtime MUST cache capability metadata efficiently but MUST version/invalidate the cache when relevant package versions, app capabilities, connection state, permissions, policy or provider health change. A model MUST NOT continue relying on a stale 'tool unavailable' conclusion after the capability registry has materially changed during the request.
A tool-result or retrieved document MUST NOT be able to self-register new authority merely by claiming that another tool exists or should be allowed. Registry membership and permissions come from trusted Dulche/Home registration, package manifests, app capability contracts and current policy. Semantic discovery MUST therefore improve capability use without creating a prompt-injection route to invented tools or escalated permissions.
Failure to resolve a tool path MUST be truthful and inspectable. Dulche MUST distinguish NoMatchingCapability from MatchingCapabilityUnavailable, MissingPrerequisite, PermissionBlocked and PolicyBlocked. When no feasible path exists, the model MAY explain the limitation, but it MUST NOT fabricate a tool or claim to have performed the action. The Resolution attempt and selected/no-selected candidates MUST be visible in FullLog/Action Graph at an appropriate level of detail.
Acceptance coverage MUST verify at minimum that: a reminder request resolves to an Automations time-based workflow without requiring the phrase 'automation'; an image-generation request invokes an eligible image capability rather than returning a false inability statement; a presentation request can resolve to Present from another app; a multi-tool request produces a dependency-ordered ResolutionPlan; explicit @ invocation overrides automatic source choice; permission/configuration blockers are distinguished from true impossibility; Computer Use does not appear unless explicitly invoked; and removal/disablement of the only matching capability causes a structured no-feasible-path result rather than a fabricated success.

### V2-B2032 · v2 lines 11640–11641

API authority and compatibility
Documented API names, argument order, Result field names and explicit behaviours are binding and must be implemented verbatim. This is not an exhaustive API list: compatible overloads and missing APIs may be added. Resolve unspecified edge cases using the same simple, predictable, inspectable and permission-aware philosophy; do not silently rename or reverse an existing operation. The requirements below extend and clarify the earlier examples.

### V2-B2033 · v2 lines 11644–11645

Requests, sessions and endpoints
Retain port-based calls and add Dulche.Prompt(prompt, port) and Dulche.Prompt(request, endpoint). Request contains input/messages, SessionID, model selection, generation settings, permitted tools, output schema, streaming preference and limits. Input supports text, images, audio and authorised file/document references through capable adapters; other modalities are capability-gated. No private context/file is sent remotely without the required permission.

### V2-B2034 · v2 lines 11648–11648

Dulche.Prompt(port) and Dulche.Prompt() remain shorthand for an explicitly prepared request in the caller's scope. Add Dulche.PreparePrompt(prompt) to prepare caller-local input for those shorthand calls; consume a snapshot at submission and never reuse another caller's input. Without prepared input, return MissingPrompt. Explicit prompt/request overloads avoid hidden state and are preferred in concurrent code.

### V2-B2035 · v2 lines 11651–11653

Assign RequestID immediately, before queueing. SessionID owns conversation/context; EndpointID identifies the logical execution target; port/URL identifies transport. Shared endpoints do not imply shared sessions. Add Dulche.CreateSession(endpoint) and session lookup/reset/close plus scoped context export/restore. Context is the token-window budget, not conversation identity. Overflow returns ContextExceeded unless the caller explicitly permits documented truncation/summarisation.
Endpoint Laguna = Dulche.Start(Remote("https://wherever.com"));
The remote overload connects to an authorised remote target. It does not assume permission to manage the host's entire service. Targets declare provider, base URL, model identity, credential reference, transport/security settings and capabilities. Local Start requires a port; remote Start requires an explicit remote target. Return an endpoint handle with identity, state and structured failure information, never a false Ready state. Cloud targets do not require invented local ports.


# Dulche runtime, requests and execution services — Result contract and metric availability; Response content and identity (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2036 · v2 lines 11656–11657

Result contract and metric availability
Return Result using the exact field names below. The question mark denotes optional content, not part of the field name. Empty metrics return null. Provider-unavailable metrics return Na, serialised as the exact JSON string "Na" and exposed by typed bindings as an unavailable sentinel. A measured zero remains 0. This applies to all metrics, including those not originally marked optional and nested model metrics.

### V2-B2037 · v2 lines 11660–11660

Use value/null/Na-capable types and separate availability/provenance metadata for pending, disabled/redacted, measured, estimated and provider-unavailable values. Never substitute zero or an invented value for unavailable data. Required identity/status and the Runtime event-log container remain present. Collections with no applicable entries may be null; numeric counts may be a measured zero. A complete aggregate cannot silently ignore unavailable contributions.

### V2-B2038 · v2 lines 11663–11664

Response content and identity
Text: the actual user-facing response, separate from tool arguments and undisclosed provider internals. No text may have been produced for a blocked, cancelled or non-text-only response.

### V2-B2039 · v2 lines 11667–11667

Thinking: the thinking log actually exposed and permitted by the model/provider. Label traces and summaries distinctly; never present a summary as the complete internal thinking or reconstruct undisclosed reasoning. Empty content is null; provider-unavailable thinking is Na.

### V2-B2040 · v2 lines 11670–11670

Tools?: identities of the distinct tools actually used, with links to invocations. Tools offered but not called are not counted as used.

### V2-B2041 · v2 lines 11673–11673

Sources?: sources retrieved, supplied or cited, with provenance and relevant response-span references. Distinguish retrieved evidence from unverified model-generated citations.

### V2-B2042 · v2 lines 11676–11676

GeneratedUI?: structured, versioned UI data, separate from Text and rendered only through an explicit CUI integration.

### V2-B2043 · v2 lines 11679–11679

Models: models actually used, including agent/subtask models, in first-use order and deduplicated by artifact/provider revision. Preserve roles, call counts, endpoints and invocation records. Models[i].Parameters exposes the individual parameter metric.

### V2-B2044 · v2 lines 11682–11682

Also return RequestID, SessionID, Endpoint, Status, FinishReason, request revision and attempt identity. FinishReason distinguishes Completed, Cancelled, Replaced, Blocked, Error, OutputLimit and Refused. Paused is a non-terminal state, not successful completion. Preserve the original provider finish reason separately.

### V2-B2045 · v2 lines 11685–11685

Earlier shorthand fields are compatibility views: Reasoning maps to Thinking; Model identifies the primary responding model; TokenUsage groups the token metrics; Error is the primary error from Errors. ToolCalls contains individual invocations, while Tools is their tool-identity summary. Endpoint identifies the primary request endpoint; additional endpoints remain in Models and logs.

### V2-B2046 · v2 lines 11688–11689

Timing and tokens
TimeElapsed: wall-clock seconds from acceptance to terminal result, including queueing/pauses; live snapshots measure through the snapshot time. Expose queue, pause and execution duration separately.

### V2-B2047 · v2 lines 11692–11692

TimeThinking?: attributable thinking duration. TimeAgentic?: duration of agent/tool work. TimeResponding: duration of generating the actual response. Use backend events or observed spans, not an assumption that all pre-first-token delay is thinking. Parallel and nested spans may overlap, so phase totals need not sum to TimeElapsed; report timing basis and estimate status.

### V2-B2048 · v2 lines 11695–11695

InputTokens: input processed by all included model invocations. OutputTokens: output generated by those invocations, including reported non-visible reasoning/tool output where applicable. TotalTokens equals InputTokens plus OutputTokens when both are known. Preserve provider tokenisation, cached-token details, modality usage and billing data per invocation.

### V2-B2049 · v2 lines 11698–11698

ResponseTokens: output tokens attributable to the user-facing response. AgenticTokens?: output tokens attributed to agent orchestration/tool-call work. ThinkingTokens?: backend/provider-reported thinking tokens. These are labelled subsets of OutputTokens, not extra quantities to add to it. Subsets can overlap, such as thinking within an agent step; retain attribution rather than inventing a mutually exclusive split.

### V2-B2050 · v2 lines 11701–11701

Aggregate actual invocations, including retries, within an explicit request/attempt/task scope. Do not double-count a child invocation through both parent and child totals. If a contributing provider withholds a required metric, the complete aggregate is Na; return a separate known subtotal and coverage when useful. Supported metrics with no value yet are null.

### V2-B2051 · v2 lines 11704–11705

Model parameters, location and storage
Parameters: architectural parameter totals across distinct model revisions actually used, counted once per model rather than once per prompt/instance. This is not a claim that several models behave like one larger model. Report per-model total and active MoE parameter counts separately when known, and label declared architectural active counts versus observed routing.

### V2-B2052 · v2 lines 11708–11708

Location: structured model-weight placement with a readable form such as "9.3% GPU | 90.7% RAM". This is residency, not utilisation. Include device IDs, measurement time/window, quantity basis, sampling method and any other/unknown location. Do not confuse parameter share with byte share, or include KV cache/activations as model weights. Shared/unified memory must not be double-counted.

### V2-B2053 · v2 lines 11711–11711

MoEs expose separate Active and Total placement views. State the denominator: an active-set distribution normalises to the active set; active weights expressed as a share of all parameters must be labelled accordingly. Keep underlying quantities. A display such as "Active: 19.9% GPU | 20.1% RAM; Total: 6% GPU | 94% RAM" needs an explicit basis and remaining share; do not silently normalise or fabricate missing placement. Hidden provider placement is Na.

### V2-B2054 · v2 lines 11714–11714

Storage: disk bytes occupied by response-associated model artifacts, not VRAM/RAM, token usage or cumulative download traffic. Deduplicate shared artifacts within each physical storage location and show local/device/remote breakdowns. Unknown remote storage makes the complete total Na, while a known local subtotal may be shown. Distinguish persistent versus temporary artifacts.

### V2-B2055 · v2 lines 11717–11718

Logs, blockers and errors
AuditLog?: timestamped reads, writes and other actions, with actor, request/attempt, target, permission/approval, start/end, outcome and side effects. ToolLog?: every tool invocation with ID, tool/version, timestamps, safe arguments/results, outcome and correlation IDs. Tools is the summary; ToolLog is the invocation history.

### V2-B2056 · v2 lines 11721–11721

FullLog: the continuous ordered collection of Runtime-observed actions, model calls, available thinking/summary events, tools, queue events, pauses, replacements, retries, approvals, results, blockers and errors. Include UTC timestamps, per-request sequence numbers and causal parent IDs. Do not fabricate exact global ordering for concurrent work. Stream/paginate large logs without losing continuity. Undisclosed provider internals are not promised; sensitive payloads are redacted/permission-filtered with omissions recorded.

### V2-B2057 · v2 lines 11724–11724

Blockers?: structured reasons work cannot complete, with affected scope, evidence, attempted recovery, whether human input is needed, next action and retryability. Errors?: hard model/provider/Runtime/tool failures with stable code, message, component, timestamp, request identity and safe details. A blocker need not be a crash; cancellation need not be a hard error. Keep recoverable retries/warnings in logs.

### V2-B2058 · v2 lines 11727–11727

Use structured results for ModelNotFound, MissingPrompt, EndpointNotFound, PortInUse, InvalidArgument, OutOfMemory, ContextExceeded, ProviderUnavailable, AuthenticationFailed, PermissionDenied, UnsupportedCapability, ToolFailed, RequestNotFound, InvalidState, QueueFull, Conflict and ModelLoadFailed. Preserve "model not found" as the friendly message for resolution failure. Expected operational failures must not depend on string parsing or unhandled exceptions.

### V2-B2059 · v2 lines 11730–11731

Streaming and delivery
Streaming means delivering text pieces and events as they become available rather than waiting for the whole response. It changes delivery, not the intended answer. Add Dulche.PromptStream(prompt, endpoint) and Request.Stream while retaining a path that awaits one completed Result.

### V2-B2060 · v2 lines 11734–11734

The submission handle exposes RequestID, Status, a final-result awaitable and optional event stream. Events include text deltas, permitted thinking/summary deltas, tool/approval events, validated UI changes, metrics and states. Include request/revision/attempt, sequence and timestamp. The final event contains the same Result contract as non-streaming, including final metrics or null/Na.

### V2-B2061 · v2 lines 11737–11737

Never execute partial tool arguments or arbitrary partial markup. Mark provisional output and discard stale deltas after replacement. Reconnect using a cursor or current snapshot without starting another generation or replaying an external action. Use bounded buffering/backpressure with explicit gap/snapshot recovery; no silent audit-event loss. Closing a display stream does not automatically cancel its task.

### V2-B2062 · v2 lines 11740–11742

Response controls and queues
Dulche.StopResponse(requestID);
Stop that response, not the endpoint. Cancel queued work or halt running generation/future tool dispatch at a safe boundary; retain partial output/logs and return Cancelled. Repeated stops are idempotent. Dependent unfinished child work is cancelled, not unrelated requests. A completed external action is not automatically undone.

### V2-B2063 · v2 lines 11745–11746

Dulche.PauseResponse(requestID);
Pause the response, retain its output/context and prevent dispatch of its next steps. Report Pausing until a safe boundary is reached, then Paused. Paused work stays paused until resumed/stopped, not silently completed.

### V2-B2064 · v2 lines 11749–11750

Dulche.ResumeResponse(requestID);
Resume paused work. Use exact continuation only when the backend supports it; otherwise return UnsupportedCapability or an explicitly disclosed, authorised continuation/restart mode. Buffering output while the provider keeps generating is not an exact pause. Never repeat completed tool side effects automatically.

### V2-B2065 · v2 lines 11753–11754

Dulche.StackPrompt(requestID, string newPrompt);
Interrupt the current response with the new prompt at a safe boundary, ahead of ordinary queued work. Preserve interrupted state and return a linked RequestID for the priority prompt. The default stacked interruption resumes the interrupted request afterwards where supported and still applicable; superseded/cancelled work must not be revived. Multiple stacked interruptions use last-in-first-out order and a bounded nesting limit.

### V2-B2066 · v2 lines 11757–11758

Dulche.QueuePrompt(requestID, string newPrompt);
Queue a new prompt after the referenced request in its session without interrupting it; return a new RequestID. Commit session context in order. Failure/cancellation must not silently discard queued input: expose its dependency/state and revalidate its context before starting.

### V2-B2067 · v2 lines 11761–11762

Dulche.ReplacePrompt(requestID, string newPrompt);
Replace the targeted prompt, such as after a message edit. Retain logical RequestID, increment revision and create a new attempt. A queued replacement retains its position; a running replacement supersedes/cancels the old attempt at a safe boundary; a paused replacement stays paused; replacing a completed prompt creates a fresh response attempt from the relevant prior session state. Preserve earlier actions/output in history, invalidate stale deltas/approvals and never automatically undo external actions or replay subsequent turns.

### V2-B2068 · v2 lines 11765–11765

If response A is running on an endpoint, ordinary response B is queued by default, even if its backend permits parallel generation. Other endpoints may work independently within resource limits. Queue order, position, capacity and timeout are observable. Optional parallel mode cannot silently change this serial default. A task waiting for a child on the same endpoint yields its execution slot to the dependency rather than deadlocking its own queue.

### V2-B2069 · v2 lines 11768–11768

Paused work may yield to independent sessions, but dependent work in the same session cannot overtake it. Recheck permissions/context before dispatch. Controls cannot target another user's work without authority. Remote tool cancellation is best-effort when a tool cannot stop; record actual completion/side effects rather than falsely claiming cancellation succeeded.

### V2-B2070 · v2 lines 11771–11772

Lifecycle, configuration and resources
Add endpoint state inspection, explicit Resume, capability discovery and health events. Distinguish Starting, Loading, Ready, Busy, Pausing, Paused, Stopping, Stopped and Failed, plus model Downloading, Installed and verification states. Reachability is not model readiness.

### V2-B2071 · v2 lines 11775–11775

Endpoint Pause retains local weights/context and suspends its work. Stop cancels its queued/running work, unloads owned resources and releases the port without deleting installed artifacts or Den history. Remote operations affect Dulche's own requests/sessions unless remote lifecycle control is supported and authorised. Manual pause requires resume; inactivity pause may auto-resume on new work with an event. Inactivity Timeout is distinct from request, queue, tool and network timeouts.

### V2-B2072 · v2 lines 11778–11778

Snapshot model identity and effective settings per attempt. SetModel, SetTemperature and SetReasoningLevel apply to future work unless safe mid-run reconfiguration is explicitly requested. Validate temperature, output limits, top-p/top-k, seed, stops, penalties and structured schemas against capabilities; silently ignoring an unsupported requested setting is not success.

### V2-B2073 · v2 lines 11781–11781

V3 editorial amendment: V3-RECON-001. Original wording is preserved in the replacement ledger.

Legacy arbitrary-percentage compatibility only: the older API describes min(n-1, floor(p*n/100)) for arbitrary p in 0..100. SPUI-133 supersedes applying that formula to visible discrete effort presets: bind each displayed preset directly and bijectively to the actual supported provider LevelID/ordinal. Any arbitrary-percent legacy API compatibility must be separately versioned; it must not shift the first visible preset to the second tier or duplicate selectable tiers. Named levels must resolve to advertised support; do not invent a mode.

### V2-B2074 · v2 lines 11784–11784

Home owns the shared 9to1 Dulche broker/service. Multiple managed endpoints do not justify each app spawning competing unmanaged servers. Resource policy covers CPU/GPU selection, offload, RAM/VRAM budgets, active models, scheduling and explicit eviction. Pause does not promise memory release while weights remain loaded; pressure must not silently switch models or discard private context.

### V2-B2075 · v2 lines 11787–11788

Providers, permissions and Generative UI
Provider adapters declare credentials by reference, capabilities, privacy/residency information, rate limits and bounded backoff/retry. Model/provider fallback must be configured and permission-compatible, never silently send local/private context to a cloud. Record actual fallback models and usage. Recover with Den checkpoints; reconcile uncertain non-idempotent actions before retry.

### V2-B2076 · v2 lines 11791–11791

Add Dulche.GetCapabilities(model) and endpoint capability inspection. Separate model ability, adapter/backend support, host integration and granted permissions. A supported modality grants no automatic access to microphone, camera, files, shell or network. ToolCallsAllowed = All means all tools already permitted for that caller/request. Support None, ReadOnly, selected-tool policies, scope overrides, consequential-action approval, sandboxed code and revocation.

### V2-B2077 · v2 lines 11794–11794

GeneratedUI is returned as data. A CUI developer explicitly implements <GenerativeContainer> and advertises supported schemas/components/actions. Without it, retain a usable text/data fallback rather than assume rendering support. Validate components, properties, limits, references and action bindings; model output never becomes trusted executable application code. UI actions use authorised typed tools tied to the request/revision. Invalid generated UI is isolated/logged without crashing the app or losing Text.

### V2-B2078 · v2 lines 11797–11797

Agents/tool loops require bounded step/time/token/cost budgets, visible progress, loop detection, approvals and cancellation. A proposed tool call is not proof of execution. Retrieved content and tool results cannot grant permissions or override the caller's restrictions.

### V2-B2079 · v2 lines 11800–11801

Acquisition, replacement and observability
Preserve Den, Models.toml, local-directory, FindModel, ListModels, Pull, Install and Replace APIs. Adapters resolve supported artifacts/catalogues; a runtime backend is not assumed to be a universal registry. Pin/verify source revisions and expose download progress, cancellation, resumption and required storage. Unsupported formats return errors instead of false compatibility claims.

### V2-B2080 · v2 lines 11804–11804

Temporary Pull ownership is tied to a consuming endpoint/request scope. Stopping releases that ownership; delete bytes only when no other live or persistent owner needs them. Install makes ownership persistent. Replacement stages/verifies the new model, applies preserveConfig/wipe/safe compatibility, records identities and supports rollback without deleting an in-use model. Logical IDs/aliases may survive; immutable artifact identity must reflect the replacement.

### V2-B2081 · v2 lines 11807–11807

Expose runtime/API/schema versions, states, queues, tokens/context, memory/storage, rate-limit status, time to first output, generation throughput and trace IDs through permission-filtered diagnostics. Use Na for unavailable telemetry, null for empty telemetry and labels for estimates. Publish SDK/adapter conformance tests and compatibility negotiation.

### V2-B2082 · v2 lines 11810–11810

Acceptance covers serial A/B queueing, streaming/non-streaming parity, every response control, queued/running/paused/completed replacement, null/Na/zero, withheld provider metrics/thinking, token aggregation, MoE denominators, scoped broadcasts, context isolation, unsupported exact resume, tool retry/cancellation, generated-UI fallback, provider failure, memory pressure and recovery.


# Dulche runtime, requests and execution services — Persistent request execution; Context compaction and Context Summarisation (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2083 · v2 lines 11813–11864

Persistent request execution
PersistencePolicy MUST support at least Standard and Persistent. Standard retains the ordinary Runtime finish/timeout/checkpoint behaviour defined elsewhere. Persistent is a durable root-request policy intended for work that should continue until its user-visible completion predicate has been satisfied.
A Persistent request MUST define or derive an inspectable CompletionContract from the user's request, active Skill requirements and target-app acceptance criteria. Completed is valid only when required outputs exist, required validation/verification has run where applicable, required target mutations are confirmed and no required work item remains Pending, Running, Retryable or Blocked. A checkpoint, partial deliverable, progress report or model statement such as 'done' is not itself completion evidence.
Soft request duration limits, provider execution leases and internal scheduling slices MUST NOT terminate a Persistent request merely because a continuation boundary was reached. When continued execution requires a new model invocation, endpoint lease or recovered process, Dulche MUST create a linked attempt under the same RootRequestID, restore the latest safe state/checkpoint and continue. Completed consequential actions MUST be reconciled before retry so persistence does not duplicate side effects.
Persistent requests MUST remain cancellable. Pressing the user-visible Stop control targets the root persistent request, requests safe-boundary cancellation of unfinished dependent work and produces the normal Cancelled result with retained partial output/logs. Persistence MUST NOT reinterpret an explicit user Stop as a transient interruption to auto-resume.
While a Persistent root request is active, ordinary user messages sent through the same compose surface are SteeringMessages belonging to that root request. They are appended durably, ordered by receipt sequence and presented to the orchestrator at the next safe planning boundary without terminating the current turn. They MUST NOT be implemented as ReplacePrompt or StopResponse, and MUST NOT cause already completed actions to replay.
Persistent orchestration MUST maintain a dependency-aware work graph. A structured Blocker marks only the work nodes that depend on it. When a blocker needs user action or permission that the agent cannot obtain itself, Dulche surfaces the blocker and continues every runnable node that does not depend on it. If all remaining nodes are blocked, the root state becomes AwaitingUser; AwaitingUser is non-terminal and keeps recovery/checkpoint state until the user resolves, changes or explicitly stops the task.
Budgets, administrator policy, permission boundaries and hard safety restrictions still apply. Persistence MUST NOT convert a denied action into an allowed action or silently purchase/consume resources beyond configured policy. A limit that can be changed by the user becomes a structured blocker; a hard non-overridable policy restriction remains recorded truthfully and the request continues any unaffected work.
Persistent state MUST survive supported Home/Dulche restarts. Den MUST retain RootRequestID, request revision, CompletionContract, task/dependency state, SteeringMessages, blockers, checkpoints, completed side-effect evidence, active Skill versions and attempt lineage required to resume without fabricating progress or repeating consequential work.
Context compaction and Context Summarisation
Context compaction is a first-class Dulche Runtime capability, not an app-specific prompt trick. Dulche MUST track the effective context budget for every model invocation and MAY initiate automatic compaction before hard exhaustion using model/provider-specific safety margins. Apps and Agents consume the same canonical compacted session state rather than implementing incompatible private compaction.
Compaction MUST preserve semantic obligations required to continue the task: current user instructions and constraints, active permissions/trust decisions, CompletionContract, outstanding tasks and blockers, accepted decisions, stable object/file/artifact IDs, current revisions, completed consequential actions, verification evidence, relevant source provenance and any exact values whose precision is required. Redundant conversational phrasing, duplicated status chatter and obsolete intermediate reasoning MAY be reduced.
Compaction MUST be non-destructive to durable history. The original authorised conversation, tool records and artifacts remain in Den according to retention policy; the compacted context is a derived execution view with provenance back to source message/event ranges. A model MUST NOT be told that omitted content never existed merely because it is outside the active compacted view.
If ordinary compaction cannot create enough safe context without materially losing required information or required media fidelity, Dulche MUST transition to Context Summarisation rather than silently dropping required content or returning ContextExceeded immediately. Typical triggers include a conversation dominated by images/media that cannot be reduced further without unacceptable information loss, unusually dense tool history, or a model whose effective context limit is smaller than the preserved minimum working set.
Context Summarisation is a distinct Dulche job. A summariser model reads the retained conversation/history section by section using bounded windows so the summariser's own context cannot overflow. Each section produces a structured intermediate summary containing obligations, facts, decisions, identifiers, artifacts/references, completed actions, unresolved work/blockers, provenance and continuity notes. Dulche then performs one or more bounded merge passes until a final execution summary fits the target context budget.
Section boundaries MUST be deterministic and provenance-preserving. A merge MUST NOT discard a requirement merely because it appears only once. Conflicting instructions or facts MUST remain represented as a conflict with source references instead of being flattened into an invented resolution. Exact code, identifiers, numeric values, legal/technical constraints or other precision-sensitive content that cannot be safely paraphrased MUST remain as pinned excerpts or stable references rather than approximate prose.
Images, audio, video and other large artifacts SHOULD remain out-of-band as stable artifact references wherever the target model can retrieve them on demand. Context Summarisation MAY summarise authorised derived observations/metadata, but MUST NOT claim that visual or media detail was preserved if the summariser could not inspect it. Required uninspectable media remains a blocker or retained reference, not fabricated text.
A local summariser model MAY be used automatically when an eligible local model is available under the configured routing/policy. Sending conversation content to a cloud summariser requires explicit user permission for cloud Context Summarisation or an existing user setting that clearly grants that permission for the relevant scope. A cloud-capable model in an ordinary fallback route does not by itself grant permission to upload context for summarisation.
If cloud summarisation is required to continue, permission is absent and no eligible local summariser can satisfy the job, Dulche MUST surface ContextSummarisationPermissionRequired/ContextCapacityBlocked as a non-destructive blocker. Persistent requests enter or remain AwaitingUser as necessary rather than losing history or pretending the task completed.
Dulche MUST expose canonical operations equivalent to Dulche.Context.Compact(sessionID, options?), Dulche.Context.Summarise(sessionID, options?) and Dulche.Context.GetState(sessionID). Results MUST report source ranges/revisions, target model/context limit, method, summariser ModelID when used, retained pinned items, omitted/referenced artifacts, token estimates where available and whether the resulting context is sufficient for the next invocation.
Computer Use
Computer Use is a first-class Dulche capability but MUST be opt-in per request. It is enabled only by an explicit @Computer Use invocation token or an equivalent explicit user control that resolves to the same ComputerUseInvocationID. An ordinary natural-language request, an @App mention or failure of a typed app API MUST NOT silently activate Computer Use.
The Computer Use capability may control eligible graphical applications through supported pointer, keyboard, accessibility/automation and visual-interaction primitives. It SHOULD use the fastest reliable state channel available, including accessibility/automation metadata where appropriate, avoid unnecessary human-speed delays, batch safe independent interactions where supported and verify state transitions instead of relying on fixed sleeps. Speed MUST NOT weaken verification or permission gates.
By default, the first attempted interaction with each target application in a Computer Use run requires user approval for that AppID/process identity, and changes to application or operating-system settings require approval before mutation. Home settings MUST allow the user to change this policy to automatic approval for eligible apps and to an Approve All eligible-apps policy. Persisted approval policy remains scoped by user/profile, app identity and the normal Home trust/permission model.
Auto-approve and Approve All MUST NOT bypass hard exclusions, administrator policy, OS security boundaries or other non-overridable safeguards. Computer Use receives no more filesystem, network, credential, settings or app authority than the invoking user/request already possesses.
Generic Computer Use MUST NOT attach to, send input to, automate or manipulate game executables or anti-cheat-protected game surfaces, including games launched or managed through platforms such as Steam or Epic Games Launcher. When classification is uncertain and anti-cheat/game automation risk is plausible, the target is ineligible by default. This exclusion exists independently of user trust level because generic automation can create account/anti-cheat risk.
The game exclusion does not prohibit Dulche Agents from participating in games deliberately configured through the 9to1 Play framework. Play interaction MUST occur through Play's typed GameDefinition/Match/contestant actions and capability isolation, not by using generic Computer Use to drive an external game client.
Every Computer Use run MUST produce an inspectable action stream with target app/window identity, action type, timestamps, relevant pre/post state evidence, approvals, failures and side effects. Sensitive screen regions, credentials and typed secrets MUST follow the normal redaction and permission rules; logs MUST NOT become a second plaintext credential store.
Dulche Autoreview MUST inspect proposed and recent Computer Use actions continuously. It MUST flag patterns including repeated identical or near-identical actions without progress, repeated failing commands, oscillation between states, privilege escalation, bulk/destructive deletion, destructive shell commands such as rm -rf or platform equivalents, security-setting changes, suspicious executable/download flows and other high-risk sequences. High-confidence imminent destructive risk MUST be able to pause the next action for review rather than merely add a passive warning.
Autoreview findings are structured ProblemEvents and MUST feed the shared Problem handling contract. A Computer Use recovery intervention MUST preserve the current request, state and logs and SHOULD redirect to a safer or more direct interaction strategy. It MUST NOT hide that a risky action was proposed or attempted.
Thinking logs, work state and ETA
Thinking-log presentation is a first-class Dulche feature backed by Runtime events. Home MUST expose one global user preference with the modes Precise, Concise, Detailed and Ramble; compatible apps inherit it unless an explicitly supported per-surface override exists. If no preference has been set, Precise is the default.
Precise shows only thinking/work events that materially advance the task, such as a resolved decision, discovered blocker, completed verification step or changed plan. It MUST suppress repetitive self-talk and events that do not change task state.
Concise renders short activity summaries suitable for rapid scanning, for example 'Moving a square', 'Opening Browse', 'Checking the build' or 'Waiting for approval'. These summaries MAY be generated from richer telemetry but MUST remain faithful to the underlying action/state.
Detailed exposes substantially more model/provider-permitted reasoning trace and task rationale, including intermediate decisions and why a strategy changed when that information is actually available and permitted. It MUST still distinguish observed facts, tool results, summaries and model reasoning.
Ramble exposes the maximum provider/model-permitted thinking/work trace available to Dulche, including abandoned approaches, self-corrections and changes of mind when the provider actually supplies those events. Ramble MUST NOT reconstruct, infer or falsely label undisclosed private chain-of-thought as if it had been exposed by the model/provider.
Thinking mode changes presentation only. It MUST NOT change the model's configured reasoning effort, tool permissions, stored audit truth, Action Graph or provider privacy rules. Provider-unavailable thinking remains Na/Unavailable under the existing Result contract.
While active, permitted thinking/work events render inline in chronological position with the conversation rather than in a separate hidden console. The visual treatment MUST be lower-emphasis/ghost-like relative to user/model message text while still meeting accessibility contrast requirements and MUST use non-colour cues so users can distinguish message content from thinking/work content.
When the active thinking/work phase ends, the inline region collapses under a disclosure labelled 'Thought for <duration>' when no tool/action execution occurred, or 'Worked for <duration>' when one or more tools/actions were executed. Duration uses the Runtime's active execution span for that phase rather than queue time. Expanding the disclosure restores the permitted events according to the selected display mode.
After 60 seconds of continuous active execution in one foreground 9to1 turn, Dulche MUST request an ETA from the active orchestrating model through a canonical ETA request. The model returns an estimated completion timestamp plus optional confidence/reason. The UI displays the timestamp in the user's locale/time zone and clearly marks it as an estimate, not a guarantee. Paused/AwaitingUser time does not count as continuous active execution.
Dulche MUST expose an operation equivalent to Dulche.RequestETA(requestID). An ETA response includes RequestID, AttemptID, EstimatedCompletionAt, GeneratedAt and optional Confidence/Reason. If the active model cannot provide a meaningful estimate, the result is explicitly unavailable; Dulche MUST NOT invent a timestamp. Material replanning MAY request an updated ETA and MUST replace rather than stack stale estimates in the primary UI.
Problem detection, autoreview and recovery
Problem handling is a first-class Dulche Runtime capability for detecting, surfacing and recovering from common model/agent failure modes without forcing every app to implement its own ad-hoc watchdog.
The canonical ProblemType set MUST support at least Looping, NoProgress/Stuck, Repetition, HallucinationOrFabrication, OverAgreement/Sycophancy, InstructionDrift, ContradictionWithGroundedState, RepeatedToolFailure, UnsafeActionPattern and Other/UserReported. Additional typed categories MAY be added versionably without collapsing distinct existing meanings.
Loop detection MUST consider semantic and action-level repetition, not only identical text. Examples include repeatedly issuing the same failing command, alternately undoing/reapplying the same mutation, reopening the same surface without new evidence, recreating completed work or repeatedly restating the same answer while the task remains unfinished.
Hallucination/fabrication detection SHOULD use available grounded evidence such as tool results, canonical app state, source provenance, stable object IDs and completion evidence. Dulche MUST flag fabricated claims of completed actions, nonexistent files/entities/tool results or unsupported factual assertions when confidence is sufficient; it MUST NOT treat disagreement with an unverified model statement as proof that the user is wrong.
OverAgreement/Sycophancy detection SHOULD flag responses that materially adopt or endorse a user's factual premise, conclusion or requested assessment without adequate evidence when the task calls for grounded evaluation. Recovery prompts the model to re-evaluate the evidence and user request; it MUST NOT mechanically disagree with the user merely to appear independent.
When automatic detection reaches its configured intervention threshold, Dulche creates a ProblemEvent and injects a clearly identified Runtime recovery directive at the next safe boundary. The directive tells the model what failure pattern was detected, relevant evidence and the required correction, such as stop repeating the command, reassess grounded state, use a different strategy or verify the claimed result. Runtime recovery directives MUST NOT be forged as user-authored messages.
If the detected problem is associated with a proposed consequential or dangerous tool/Computer Use action, Dulche MAY pause dispatch before the action and require re-planning or applicable approval. Lower-risk reasoning-quality problems SHOULD normally correct the active request without cancelling the turn or discarding valid completed work.
Every conversational 9to1 AI surface MUST expose a small Problem pill above/adjacent to the compose bar. The user can invoke it even when automatic detection did not fire, select a ProblemType and optionally add a note. Manual reports enter the same ProblemEvent/recovery pipeline and are delivered to the active request at a safe boundary without implicitly stopping the request.
ProblemEvents MUST record stable ProblemEventID, RequestID/AttemptID, category, detector source (automatic, autoreview or user), timestamp, safe evidence references, confidence when machine-detected, intervention taken, affected action/node and resolution outcome. They MUST appear in FullLog/Action Graph with sensitive content redacted as required.
Problem detection MUST itself be bounded and loop-safe. A false-positive recovery that does not improve state MUST NOT trigger an endless reviewer/model ping-pong. Repeated unresolved problems should escalate to a visible blocker, alternate model/strategy where permitted, or user decision while preserving completed work.


# Dulche runtime, requests and execution services — Dulche multi-engine inference runtime; Runtime abstraction and dispatcher (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2248 · v2 lines 13268–13270

Pass 4: Pre-Release
Dulche multi-engine inference runtime
Pass 4 MUST add a Dulche-owned inference-runtime abstraction so model definitions and higher-level Dulche features do not depend directly on one execution engine. llama.cpp remains the broad/default local backend. Dulche MUST also include an upstream-trackable Strata fork as a specialised backend for model architectures where Strata is supported and materially better suited.

### V2-B2249 · v2 lines 13273–13274

Runtime abstraction and dispatcher
Dulche MUST expose one stable internal runtime contract for model loading, prompt/prefill, decode, context/KV handling, multimodal inputs where supported, cancellation, streaming, usage/telemetry, errors and shutdown. Engine-specific behaviour belongs behind adapters. Agents, Spaces, Plugins, Skills and other Dulche callers MUST target the Dulche runtime contract rather than importing llama.cpp- or Strata-specific APIs.

### V2-B2250 · v2 lines 13277–13277

A Runtime Dispatcher MUST select the effective inference engine per model. Selection MUST be capability-based rather than a simple dense-versus-MoE rule. The compatibility registry MUST be able to consider model architecture/family, file/weight format, quantisation, required runtime features, OS, available VRAM, system RAM, CPU capabilities and any other hard backend requirement. A model manifest MAY declare a preferred engine and fallback engines, but the manifest MUST NOT make the model permanently dependent on one backend.

### V2-B2251 · v2 lines 13280–13281

Automatic selection
Automatic selection is the default. Dulche MUST first remove incompatible engines, then choose between compatible engines using declared capability fit and locally measured performance where useful. Calibration MAY benchmark compatible engines for a model/quantisation/hardware fingerprint and cache the result so repeated launches do not re-run expensive benchmarks. Prefill and decode MUST be measured separately where they have materially different performance characteristics.

### V2-B2252 · v2 lines 13284–13284

Automatic selection MUST NOT enable quality-reducing or behaviour-changing experimental flags merely to win a performance comparison. Runtime choice MUST remain inspectable through diagnostics/logging, including the selected engine and the reason it was selected.

### V2-B2253 · v2 lines 13287–13288

Manual engine selection
Users and callers MUST also be able to override automatic selection and swap the active inference engine explicitly. The public Dulche API MUST include the following forms:

### V2-B2254 · v2 lines 13291–13293

Dulche.setInferenceEngine(Llama.cpp);
Dulche.setInferenceEngine(Strata);
Dulche.getInferenceEngine();

### V2-B2255 · v2 lines 13296–13296

setInferenceEngine(...) establishes an explicit manual override for the applicable Dulche runtime scope and takes precedence over automatic engine selection while that override is active. getInferenceEngine() returns the effective engine currently selected for that scope. The UI/runtime settings MUST also make the effective engine and whether it was selected Automatically or Manually inspectable.

### V2-B2256 · v2 lines 13299–13299

A manual override MUST NOT be silently ignored. If the requested engine cannot run the selected model or current hardware configuration, Dulche MUST return a structured incompatibility result explaining the unmet requirement. It MUST NOT silently substitute another engine and pretend the requested engine was used. Returning to Automatic selection MUST be available through the corresponding runtime/settings control.

### V2-B2257 · v2 lines 13302–13303

Strata fork
Dulche MUST carry a maintained Strata fork rather than treating a separately installed Strata application as a required user dependency. The fork MUST preserve applicable upstream and third-party licence notices, keep model-weight licences separate, and remain intentionally close enough to upstream that security, correctness and performance improvements can be reviewed and merged without repeatedly re-forking the project.

### V2-B2258 · v2 lines 13306–13306

The Dulche Strata adapter MUST expose Strata through the same Dulche runtime contract used by llama.cpp. Strata-specific optimisations such as expert caching/residency, concurrent CPU/GPU expert execution, specialised prefill, MTP/speculative execution and model-specific memory placement MAY be used when supported, but they MUST remain implementation details behind the common runtime boundary.

### V2-B2259 · v2 lines 13309–13310

Fallback and failure behaviour
In Automatic mode, if the preferred engine fails to initialise or becomes invalid for the selected model/hardware, Dulche MAY try the next compatible registered engine and MUST record the fallback and reason. A fallback MUST preserve model identity and required capabilities; Dulche MUST NOT silently load a different model merely to make an engine work.

### V2-B2260 · v2 lines 13313–13313

In Manual mode, automatic cross-engine fallback is disabled unless the caller/user explicitly permits it after the failure. Engine crashes, load failures, out-of-memory conditions and unsupported-feature errors MUST surface through the normal structured Dulche error model and MUST leave the runtime in a recoverable state.

### V2-B2261 · v2 lines 13316–13317

Pre-release acceptance
Pass 4 is not complete for this requirement until llama.cpp and the Dulche Strata fork can coexist in one shipping Dulche installation; compatible models can be routed automatically; the three public set/get calls above work; incompatible manual selections fail clearly; automatic fallback is tested; engine choice is inspectable; benchmark/calibration state survives normal relaunch where applicable; and switching engines does not break higher-level Dulche callers, model identity, permissions, context ownership or persisted conversation/run state.

### COMPANION-B0084 · companion lines 555–557

SPUI-194 — DULCHE SHARED CONTEXT RUNTIME (.UCML), TRUST BOUNDARY AND CRYPTO CORRECTION
Create the proposed shared context storage/runtime with versioned “.UCML” (User Context Markdown Language) representation, where appropriate structured metadata accompanies Markdown-compatible human-readable content, including source, scope, owner, consent, relevant identities/revisions, expiry/retention, security classification and permissions. Use the existing Dulche Den/Memory/Spaces context owners rather than inventing incompatible duplicate history or bypassing explicit memory policy. UCML is a storage/interchange format, NOT by itself a cryptographic protection. Enforce per-user encrypted storage, authenticated authorised access and appropriate separation for conversation/Space/personal/team/organisation contexts, including local/remote and cross-device boundaries. Daily user-scoped rotation of a DERIVED DATA-ACCESS/ENVELOPE KEY or equivalent sound cryptographic design MAY fulfil the owner's desired daily refreshed unique key, with secure retention of the ability to decrypt legitimately retained older records; never rotate destructively or imply every user gets the same secret.
CRITICAL OWNER-APPROVED SECURITY CORRECTION: MODELS MUST NEVER BE GIVEN, DISPLAYED OR INJECTED A MASTER KEY, DECRYPTION KEY, CREDENTIAL OR RAW AUTHENTICATION SECRET. A model-visible daily key plus “REDACTED if leaked” is NOT acceptable. Hold all key material ONLY inside authenticated trusted key management (OS keystore/HSM/secure Dulche service as suitable). Expose a purpose-built, scoped Dulche Context Broker, accessible through an authenticated dedicated MCP/interface where appropriate. A closed-source bundled MCP may be used as the owner's deployment choice, but CLOSED SOURCE DOES NOT CONSTITUTE the security boundary. Requests require caller identity, explicit current permission, tenant/user/Space scope, task-purpose compatibility and limited retrieval; model receives only least-privilege approved relevant CONTEXT, not any key that allows decryption. Opaque capability/handle references may be passed where safe, but must not be reusable bearer secrets the model can leak. Protect integrity, logging/redaction, revocation, key-loss recovery and encryption-state migration.
