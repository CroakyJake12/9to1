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
