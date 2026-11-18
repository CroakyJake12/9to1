# Skills, Plugins, MCP and capability packages — MCP Support; Identity and lifecycle (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2109 · v2 lines 12068–12069

MCP Support
MCP is a first-class Dulche extension/runtime capability. Dulche MUST support acting as an MCP client for authorised local/remote MCP servers and acting as an MCP server when 9-1 intentionally exposes approved capabilities to external MCP clients. MCP execution MUST use the same permission, audit, cancellation, timeout and structured-error contracts as native Dulche tools.

### V2-B2110 · v2 lines 12072–12072

Identity and lifecycle

### V2-B2111 · v2 lines 12075–12075

Every configured MCP connection MUST have a stable ServerID independent of display name or transport address. Configuration MUST record role (client/server), transport/process/endpoint definition, credential references, trust state, capability snapshot/version and owner/scope. Credentials MUST be stored by secure reference and MUST NOT be embedded as plaintext in project manifests or logs.

### V2-B2112 · v2 lines 12078–12078

Client connections MUST expose at least Disconnected, Connecting, Ready, Degraded, Reconnecting, Failed and Stopped states. Reconnect MUST NOT replay a previously completed consequential tool call. A broken connection returns structured transport/provider failures and MUST NOT be reported as Ready.

### V2-B2113 · v2 lines 12081–12081

Capability discovery and schemas

### V2-B2114 · v2 lines 12084–12084

Dulche MUST discover and represent the capabilities actually exposed by an MCP server, including tools and, where supported by the negotiated MCP version/server, resources and prompts. Tool input/output schemas MUST be validated before dispatch/consumption. Capability changes MUST invalidate stale cached schemas before later calls use them.

### V2-B2115 · v2 lines 12087–12087

Capability identity is server-scoped: two MCP servers MAY expose the same human-readable tool name without collision because the canonical identity includes ServerID plus capability identity/name. User-facing aliases MAY exist but MUST NOT replace stable server-scoped identity.

### V2-B2116 · v2 lines 12090–12090

Permissions and execution

### V2-B2117 · v2 lines 12093–12093

Connecting to an MCP server does not authorise all capabilities it exposes. Each tool/resource capability MUST resolve through Dulche/Home permission and risk evaluation according to its effective read/write/external-side-effect scope. Server-provided descriptions/content MUST NOT grant permissions, alter trust state or override caller restrictions.

### V2-B2118 · v2 lines 12096–12096

MCP tool execution MUST support cancellation/timeouts when supported by the active transport/server and MUST record when cancellation is best-effort or too late because an external action already completed. Results, approvals, failures, blockers and side effects MUST appear in ToolLog, AuditLog, FullLog and Action Graph like equivalent native tools.

### V2-B2119 · v2 lines 12099–12099

Canonical MCP API

### V2-B2120 · v2 lines 12102–12114

Dulche.MCP.Connect(config);
Dulche.MCP.Disconnect(serverID);
Dulche.MCP.ListServers();
Dulche.MCP.GetState(serverID);
Dulche.MCP.GetCapabilities(serverID);
Dulche.MCP.ListTools(serverID);
Dulche.MCP.CallTool(serverID, toolName, arguments);
Dulche.MCP.ListResources(serverID);
Dulche.MCP.ReadResource(serverID, resourceID);
Dulche.MCP.ListPrompts(serverID);
Dulche.MCP.GetPrompt(serverID, promptID, arguments?);
Dulche.MCP.Server.Start(config);
Dulche.MCP.Server.Stop(serverID);

### V2-B2121 · v2 lines 12117–12117

Unsupported capabilities MUST return UnsupportedCapability rather than fabricated empty results. Protocol-version-specific functionality MAY extend this API while preserving compatibility with the stable core.

### V2-B2122 · v2 lines 12120–12120

Tool Builder integration and acceptance

### V2-B2123 · v2 lines 12123–12123

MCP projects produced by AI Studio Tool Builder MUST validate against the same Dulche MCP connection/schema/permission contract used at runtime. MCP support is implementation-complete only when local and remote connections can be configured, discovered, permissioned, called, cancelled/failed, reconnected and audited without bespoke per-app wiring, and when approved 9-1 capabilities can be exposed through server mode without bypassing their original permission model.

### V2-B2124 · v2 lines 12126–12131

Plugins and web integrations — WebMCP and site-hosted MCP servers
Support MUST be added for WebMCP-enabled web pages and for remote MCP servers hosted by websites. These are distinct integration surfaces: browser-side WebMCP support alone does not satisfy remote MCP-server support, and connecting to a remote MCP endpoint alone does not satisfy WebMCP support.
The existing Plugin/MCP integration system MUST expose discovery/inspection, connection or permission setup, tool invocation, cancellation where supported, and disconnect/revocation for the appropriate surface. WebMCP integration MUST use the supported browser capability with explicit capability detection and origin boundaries. Site-hosted MCP connections MUST support the applicable remote HTTP transport and protocol-version/authentication contracts rather than assuming that a page URL is itself a valid MCP endpoint. The implementation MUST record supported browser/protocol versions and visibly report unsupported capabilities instead of claiming a successful integration.
Both routes MUST retain Home-brokered permissions, declared scopes, caller/site identity, secret protection and consequential-action approval. Tool descriptions, page content and tool results MUST NOT be treated as authority to expand permissions. These requirements include interoperability with websites exposing MCP servers; they do not silently select or purchase a new hosting service for the Sites application.
Web integration API and acceptance
Extend the existing canonical MCP/Plugin APIs for capability inspection, discovery, connection/authentication setup, invocation, supported cancellation and revocation. Browser-side WebMCP bindings MUST preserve browser origin/document identity; remote MCP bindings MUST preserve ServerID, endpoint identity and negotiated protocol/transport metadata. Test an authorised WebMCP-enabled page and a website-hosted remote MCP endpoint separately, then test unsupported browser/protocol, denied scopes, disconnected transport, cancellation limits and revocation. Neither route may claim success from a placeholder, silently use a page URL as an MCP endpoint or expose secrets in logs.

### V2-B2125 · v2 lines 12134–12135

Skills + Plugins
Skills and Plugins are separate first-class Dulche extension types. A Skill packages reusable instructions, workflows, context rules and resources for model/Agent/Harness execution. A Plugin packages executable or integrated capabilities and MAY expose typed tools/actions, CUI surfaces or external-service functionality. Neither type is interchangeable with MCP, although a Skill or Plugin MAY declare an MCP dependency.

### V2-B2126 · v2 lines 12138–12138

Common package contract

### V2-B2127 · v2 lines 12141–12141

Every Skill and Plugin MUST have a versioned manifest containing stable PackageID, package type, version, minimum/compatible Dulche versions, provenance/origin, declared capabilities, required/requested permissions, dependencies, entry points/resources, update metadata and integrity/signature information when the distribution channel supports signing. Display name MUST NOT be used as package identity.

### V2-B2128 · v2 lines 12144–12144

Manifest parsing and dependency resolution MUST be deterministic. Unknown required manifest fields/capabilities block installation or enablement with a structured compatibility failure; unknown optional fields MAY be preserved for forward compatibility. Packages MUST NOT execute code merely because their manifest was inspected or imported.

### V2-B2129 · v2 lines 12147–12147

Lifecycle, trust and permissions

### V2-B2130 · v2 lines 12150–12150

Installation, enablement, permissions and trust are separate states. Installing a package registers its files/manifest but MUST NOT automatically enable it for every caller, grant requested permissions, connect external accounts or grant Accept and Always Trust. Enablement only makes the package available within the selected scope; individual actions still use the normal Dulche/Home permission and risk model.

### V2-B2131 · v2 lines 12153–12153

Package lifecycle MUST expose at least Installed, Disabled, Enabled, Updating, Failed and Quarantined states. A package that fails integrity/signature/compatibility validation MUST NOT become Enabled. Quarantine preserves evidence/metadata needed for diagnosis while preventing execution.

### V2-B2132 · v2 lines 12156–12156

Updates MUST be version-aware and transactional. Dulche MUST validate/stage the new version and dependency graph before switching the active version. Failure during update MUST leave the previous known-good version available when rollback is technically possible. Package updates MUST NOT silently expand effective permissions; newly requested scopes require a new permission decision.

### V2-B2133 · v2 lines 12159–12159

Uninstall MUST define what happens to package-owned persistent data. Shared/user-authored data MUST NOT be silently deleted merely because a package is removed. Orphaned package state MAY be retained for recovery/reinstall according to explicit retention policy.

### V2-B2134 · v2 lines 12162–12162

Skill model

### V2-B2135 · v2 lines 12165–12165

A Skill is primarily declarative. It MAY contain instructions, workflow definitions, templates/prompts, context-selection rules, examples, resources/files and references to permitted tools/Plugins/MCP capabilities. A Skill MUST NOT gain arbitrary code execution solely by being a Skill; executable behaviour requires an explicitly declared/authorised executable extension capability such as a Plugin/tool dependency.

### V2-B2136 · v2 lines 12168–12168

Skill activation MUST be explicit and observable in request/run metadata. Dulche MUST record which Skill IDs/versions contributed to a request so Result provenance, logs and Action Graph can identify the active Skill set. Conflicting or incompatible Skills MUST resolve through a deterministic precedence/compatibility rule or return Conflict; Dulche MUST NOT silently merge contradictory required policies.

### V2-B2137 · v2 lines 12171–12178

Built-in Persistent skill
Dulche MUST ship a first-party built-in Skill with the canonical display name Persistent and a stable reserved SkillID. Activating Persistent on a request sets that root request's PersistencePolicy to Persistent. The Skill is declarative: it changes Runtime orchestration semantics and does not gain permissions, tools, external access, budget or trust merely by being active.
Persistent activation MUST be explicit and visible in request metadata, the compose invocation state, FullLog and Action Graph. It MAY be invoked through the shared @ resource picker as @Persistent. The active version MUST be recorded like every other Skill so later inspection can prove which persistence contract governed the run.
The Persistent Skill MUST use the canonical Dulche persistent-request contract. In particular, an ordinary checkpoint, context compaction boundary, provider lease expiry, model restart, recoverable tool timeout or similar continuation boundary MUST NOT be treated as successful completion of the turn. The Runtime preserves/reconstructs execution state and continues under the same RootRequestID until the task's completion criteria are actually satisfied, the user explicitly stops it, or all remaining work is non-terminally AwaitingUser because of an unresolved blocker.
While a Persistent turn is running, an ordinary follow-up message sent by the user MUST join the same root turn as a SteeringMessage and MUST NOT implicitly cancel, replace or interrupt the active work. Dulche consumes the message at the next safe planning boundary, updates the plan and continues. Explicit Stop, explicit replace/edit semantics and any separately exposed interrupt/stack action retain their distinct meanings.
A blocker that cannot be resolved within the request's effective permission and capability bounds MUST be surfaced to the user as a structured blocker immediately. The blocker MUST isolate only the dependent work. Dulche MUST continue any independent runnable work in the same persistent turn; if no independent work remains, the turn enters AwaitingUser without being misreported as complete or timed out. When the blocker is resolved, the same durable turn resumes from current state rather than silently restarting completed work.
Persistent MUST remain compatible with automatic context compaction, Context Summarisation, Subagents, checkpoints, crash recovery and provider/model fallback. None of those mechanisms may erase completed side-effect evidence, duplicate consequential actions or weaken permission/approval requirements.
Plugin model

### V2-B2138 · v2 lines 12181–12181

A Plugin MAY expose typed tools/actions, external-service connectors and optional CUI surfaces. Every exposed callable capability MUST declare input/output schemas, permission scopes, risk/side-effect classification and cancellation/timeout semantics where applicable. Plugin tool calls MUST enter the same ToolLog, AuditLog, FullLog and Action Graph pipeline as native tools and MCP calls.

### V2-B2139 · v2 lines 12184–12184

Plugin CUI surfaces MUST render through approved CUI contracts and MUST NOT inject arbitrary trusted application code/markup outside the declared host surface. A Plugin surface does not inherit permission to invoke its Plugin’s actions without the applicable caller/user permission grants.

### V2-B2140 · v2 lines 12187–12187

External-service Plugins MUST use secure credential references and explicit connection/account scopes. Provider tokens/secrets MUST NOT be stored in plaintext package files, logs or project definitions.

### V2-B2141 · v2 lines 12190–12190

Dependencies and persistence

### V2-B2142 · v2 lines 12193–12193

Package dependencies MUST be expressed by stable PackageID plus version/range and dependency type. Dulche MUST detect missing, cyclic and incompatible dependencies before enablement. Optional dependencies MUST be distinguishable from required dependencies so absence does not masquerade as a broken install.

### V2-B2143 · v2 lines 12196–12196

Dulche Den MUST persist the installed package registry, active versions, enablement scopes, package provenance, dependency graph and relevant package state. Device-specific executable paths/caches remain device-local; shared Den state MUST NOT imply that executable packages are installed on another device unless that device independently satisfies installation/integrity requirements.

### V2-B2144 · v2 lines 12199–12199

Canonical Skill API

### V2-B2145 · v2 lines 12202–12209

Dulche.Skill.Install(package);
Dulche.Skill.List();
Dulche.Skill.Get(skillID);
Dulche.Skill.Enable(skillID, scope?);
Dulche.Skill.Disable(skillID, scope?);
Dulche.Skill.Update(skillID, version?);
Dulche.Skill.Uninstall(skillID);
Dulche.Skill.ResolveFor(request);

### V2-B2146 · v2 lines 12212–12212

Canonical Plugin API

### V2-B2147 · v2 lines 12215–12223

Dulche.Plugin.Install(package);
Dulche.Plugin.List();
Dulche.Plugin.Get(pluginID);
Dulche.Plugin.Enable(pluginID, scope?);
Dulche.Plugin.Disable(pluginID, scope?);
Dulche.Plugin.Update(pluginID, version?);
Dulche.Plugin.Uninstall(pluginID);
Dulche.Plugin.GetCapabilities(pluginID);
Dulche.Plugin.Invoke(pluginID, capability, arguments);

### V2-B2148 · v2 lines 12226–12226

These are minimum canonical entry points. Tool Builder and other first-party clients MUST use the same package/runtime contracts rather than manipulating private package files or registries directly.

### V2-B2149 · v2 lines 12229–12229

Tool Builder integration and acceptance

### V2-B2150 · v2 lines 12232–12232

Skills and Plugins produced by AI Studio Tool Builder MUST validate against the exact manifest/schema/dependency/permission contracts used by Dulche after installation. First-class support is implementation-complete only when packages can be created, validated, installed, enabled/disabled, permissioned, updated/rolled back where supported, used in real Dulche requests, audited, persisted/recovered and uninstalled without bespoke per-app wiring or silent permission escalation.
