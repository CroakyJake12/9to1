# Automations — graph authoring and durable runs — Automations; Product role and execution boundary (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1666 · v2 lines 7464–7633

Expose safe retry/cancel controls only for states that support them. Preserve errors and partial outputs. Cross-app actions keep the owning app’s revision/permission semantics and cannot bypass View mode or protected source merely because they originate in an Automation.
<!-- UX27:END SECTION-06 -->
Automations
Product role and execution boundary
9-1 Automations is the ecosystem-wide workflow-definition and orchestration app. It owns Automation definitions, triggers, control flow, durable execution state and Run history. It does not reimplement the business logic of target apps: every app/device/provider/third-party action executes through the canonical typed action exposed by its owning capability and retains that capability's permissions, risk classification, result/error schema, idempotency semantics and audit trail.
Home owns the shared Node Graph Framework and permission/trust broker. Automations owns Automation entities and orchestration semantics. Dulche executes AI/model/Agent work. Target 9to1 apps, connected services, third-party apps and devices execute their registered typed actions. Raw mouse/keyboard UI automation is non-conformant where a typed action exists.
Landing page: Home and Library
The Automations landing surface has two primary sections: Home and Library.
* Home is an operational dashboard. It surfaces recent runs, currently running/waiting workflows, failures/Needs Attention, upcoming scheduled triggers, permission/connection problems, commonly used Automations and concise execution-health information.
* Library is the canonical list of the user's Automations. It supports search, sort/filter, enabled/disabled state, owner/scope, last run, next trigger when determinable, current active revision, connection/permission health and create/duplicate/share/delete/recover operations.
Home dashboard cards MUST open the canonical Automation or AutomationRun rather than creating separate dashboard state. The landing page SHOULD make Create Automation prominent without forcing a user into an empty graph before they understand what they are creating.
Canonical entities
Automation MUST have stable AutomationID, Name, Description?, Icon?, Owner/Scope, Enabled/OperationalState, GraphID, DraftRevisionID, ActiveRevisionID?, InputSchema?, OutputSchema?, TriggerSummary, RequiredCapabilities, ExecutionPolicy, CreatedAt, ModifiedAt and Revision.
AutomationRun MUST have stable RunID, AutomationID, pinned AutomationRevisionID, TriggerID/TriggerEventID, Status, StartedAt, FinishedAt?, typed Inputs, typed Outputs?, NodeExecutionStates, Error?, approval/wait state, causal ancestry and audit/ActionGraph references.
Editing, renaming, moving, sharing or changing the landing-page position of an Automation MUST NOT change AutomationID. A Run is historical execution state and MUST remain bound to the exact Automation revision it executed.
Draft and Active revisions
Automations use the Node Graph Framework's Draft/Active revision model. Editing an active Automation changes a Draft revision. New trigger executions continue using the current Active revision until the user validates and activates/publishes a newer Draft.
An already-running Run remains pinned to its starting revision even if another revision is activated. Activation is atomic. Previous valid Active revisions remain available for inspection and rollback subject to retention policy.
Graph editor and simple creation
The graph is the canonical executable definition. Automations MAY offer simple When / If / Then creation, templates or natural-language creation, but these are graph-authoring views over the same GraphID rather than separate simplified formats. Opening an Automation MUST always allow inspection of the real graph.
The primary advanced editor uses the Home-owned Node Graph Framework with Automations' capability profile, typed ports, pan/zoom, selection, grouping/comments, copy/paste, undo/redo, validation, Draft/Active state and execution overlays.
Node search and connector interaction
Node creation/search MUST follow an Unreal-style graph interaction pattern. Right-clicking empty graph space opens the node list at that location; the user can immediately type to filter/search registered node types and select a result to create the node at/near the invocation point. Results SHOULD be grouped by meaningful categories while search ranks direct action/event/type matches first.
Connector deletion MUST support Ctrl-click directly on a connection/connector to remove it. Right-clicking a connection opens its context menu with Delete Connector as an explicit action. Keyboard-accessible selection plus Delete/Backspace MUST provide an equivalent non-pointer path. Deleting a connector removes only that connection, never either connected node.
Search results come from the live registered node catalogue for the current capability profile, target platform/device availability and installed/connected integrations. Automations MUST NOT maintain a stale hard-coded copy of the ecosystem's available actions.
Trigger model
Minimum trigger families:
* Manual — explicit user/API invocation.
* Schedule — timezone-aware one-time or repeating schedules such as a specific time, daily/weekly rules or interval schedules.
* App Event — typed events published by 9to1 apps.
* Condition/Watch — reevaluate a typed predicate when relevant state changes or on an explicitly declared evaluation cadence where event-driven evaluation is unavailable.
* External/Connector Event — supported webhooks/provider events/plugin/MCP/connector events.
* Automation invocation — typed call from another Automation.
Triggers use typed payloads. Schedule triggers preserve timezone/DST semantics. Event-driven state change MUST be preferred to wasteful polling when an owning capability provides an event/change feed. Trigger delivery MUST have stable identity/deduplication semantics.
Node families
The Automations capability profile MUST include registered node families needed for mature workflow composition, including:
* Triggers.
* 9to1 app Actions and Queries.
* Device Actions.
* Third-party app/service Actions.
* Conditions, Switch/branch and comparisons.
* Variables, constants, data access and transforms/conversions.
* Wait/Delay.
* For Each/iteration.
* Parallel/Fan-out and Join.
* Approval/Human review.
* Retry, On Failure, fallback and error handling.
* Call Automation.
* Dulche/Agent/Skill/Plugin/MCP/Connector capabilities.
* Typed Output/Return.
Connections are type-checked through the Node Graph Framework. Incompatible ports cannot connect unless a registered explicit conversion/transform exists.
Device-level actions
Automations MUST expose an extensive device-level action catalogue sourced directly from the capabilities actually advertised by the current/selected Home or Mesh device. Device actions are not a small hand-maintained Automations list. When a device/runtime exposes a supported typed action, Automations can surface it subject to OS/device permissions and the Home trust broker.
Representative device actions include, where the device advertises them:
* Wi-Fi on/off and supported network-selection/state operations.
* Bluetooth on/off and supported connection/device operations.
* Always On Display on/off plus supported AOD profiles/settings.
* Lock Screen Clock position, design/style and other supported lock-screen clock properties.
* Display/brightness, sound/volume, notification/DND, power/battery, input, connectivity and other device settings/actions exposed safely by the device capability registry.
Every device action node records the required DeviceID/capability/action version or an explicit compatible-device targeting policy. The editor MUST warn when an action is device-specific or not portable and explain that it may not work on another device. Validation on another device MUST report capability mismatch rather than silently substituting a different action.
Cross-device execution uses Mesh typed remote capability invocation and therefore passes both caller-side and target-device permission/policy checks. Automations MUST NOT turn device actions into a generic remote-desktop or arbitrary-code-execution channel.
Third-party app and service actions
Automations MUST surface typed third-party app/service actions wherever an installed app, plugin, connector, MCP capability, operating-system integration or registered provider exposes them. Discovery is dynamic and versioned, not a fixed list baked into Automations.
Third-party actions carry the originating capability/provider identity, connection/account requirements, input/output schemas, platform/device availability and risk/side-effect classification. Missing/uninstalled apps or expired connections place affected nodes into a clear Needs Attention/capability-unavailable state.
Example: a Schedule trigger at 16:00 may connect to a registered YouTube/compatible media action that plays a specified video on a selected compatible device. The workflow remains a typed trigger-to-action graph; Automations does not emulate YouTube by screen clicking.
Natural-language graph creation and editing
Dulche can create and modify Automations from natural-language requests, but the result MUST be a real editable Node Graph mutation using typed Node Graph APIs. Generated natural-language descriptions are explanatory only and are never the canonical workflow definition.
AI-generated graphs MUST validate node/action schemas, connection types, permissions/capabilities and target availability before activation. The editor SHOULD visually highlight nodes/connections added or changed by the most recent AI operation and allow ordinary undo.
Data, variables and nullability
Each Run has a typed data environment. Trigger payload fields, node outputs, variables and action inputs can be connected directly where types are compatible. Data transforms use explicit registered nodes/functions rather than implicit lossy conversion.
False, null, unavailable/redacted and error are distinct. Missing data MUST NOT silently become zero, False or an empty string and accidentally take the wrong branch.
Durable Wait and resume
Wait/Delay MUST be durable. A Run that waits for seconds, days, a time/date, device availability, approval or another supported condition persists a continuation record and releases active execution resources. It MUST NOT depend on an application thread/process literally sleeping.
Restart/redeployment must resume a valid waiting Run from persisted state without repeating already-committed side effects. ResumeAt/condition metadata and revision identity are inspectable.
Approval nodes
Approval/Human Review is a first-class node. It pauses a Run with WaitingForApproval and presents the intended decision/action to the appropriate authorised user. Approval, rejection, timeout and cancellation are explicit outcomes usable by subsequent graph branches.
An Approval node is distinct from Home's permission broker. Approval expresses workflow-required human review; the broker determines whether the caller is authorised to execute the protected action. Either mechanism may independently block execution.
Permissions, connections and secret handling
Each Automation has a stable caller identity bound to AutomationID/workspace identity. Activating a revision MUST show/derive the capabilities and action scopes the active graph requires. Expanding an Automation from a lower-risk action such as Mail.CreateDraft to Mail.Send or adding a new device/provider scope requires normal permission/trust reevaluation.
Secrets, tokens and provider credentials MUST NOT be stored as plaintext node properties. Nodes reference Home/connector-managed ConnectionID/secret handles. Sharing a graph does not automatically share the owner's credentials, external account access or trust grants.
Execution location and device targeting
Execution policy MUST make where the workflow can run explicit. Supported executors may include 9to1 cloud, the current Home/device runtime, a selected compatible Mesh device or a provider-native executor where that provider/action supports it.
The UI MUST NOT claim that a device-bound Automation will execute while all required devices are offline. Conversely, a cloud-capable workflow MUST NOT unnecessarily depend on an open desktop UI.
A device-targetable node may require a specific DeviceID, any compatible paired device or the triggering device according to its declared targeting policy. If no compatible device is available, the Run follows its configured wait/retry/failure policy with an explicit DeviceCapabilityUnavailable/DeviceOffline state.
Run lifecycle and operational state
Automation definition operational states include at least Draft, Enabled, Disabled and NeedsAttention. AutomationRun states include at least Queued, Running, Waiting, WaitingForApproval, Paused, Completed, Failed and Cancelled.
Disabling an Automation prevents new trigger starts but does not silently delete history. If Runs are already active, the UI must explicitly offer a policy such as allow existing Runs to finish, pause them where safe, or cancel them.
Home dashboard/Library status MUST be derived from canonical definition/run/connection state, not a separately editable status field.
Run history and execution inspection
Every Run has a structured execution timeline showing trigger, node start/end, typed input/output summaries, target action, attempt number, duration, waiting/approval state, result/error and causal child-run references. Sensitive values are redacted according to permissions.
The structured Run record is authoritative. Dulche may summarise it in natural language but MUST NOT invent events or hide failed/denied/cancelled nodes.
Users can open a historical Run against the exact pinned graph revision even after the current Automation has changed.
Error handling, retry and idempotency
An unhandled node failure stops the affected path by default. Authors may explicitly add retry, On Failure, fallback or continue semantics.
Retries of external or destructive actions MUST use the target action's idempotency/OperationID contract where available. If the runtime cannot determine whether a timed-out consequential action committed, it MUST enter a typed uncertain/Needs Attention state rather than blindly repeat an operation that could duplicate its external side effect.
Retry policies are bounded and inspectable; infinite automatic retry loops are prohibited.
Loop and cascade protection
Runs carry TriggerEventID, RunID, ParentRunID? and causal ancestry. The runtime MUST detect repeated event/run cycles, bound recursion/cascade depth and total execution budget, and deduplicate replayed trigger deliveries.
If an Automation repeatedly triggers itself or another Automation in a causal loop, the affected cascade is stopped with a diagnostic that identifies the loop rather than continuing to mutate user data indefinitely.
Calling other Automations
Call Automation invokes another Automation by stable AutomationID and declared InputSchema/OutputSchema; it does not copy the target graph into the caller. The callee keeps its own version, permissions and Run history while parent/child Run causality is linked.
Test, validate and run
The editor MUST distinguish Validate, Test and Run for real.
* Validate performs graph/schema/type/capability/permission/connection checks without executing actions.
* Test executes with selected sample/current data and MUST use action preview/dry-run facilities where the target capability provides them.
* Run for real executes the active or explicitly selected validated revision with normal side effects and permissions.
If a Test reaches an action that cannot be safely simulated, Automations must identify that limitation and require explicit real-execution confirmation before performing the side effect.
Sharing, teams and run-as identity
Automations MAY be shared within supported personal/team/workspace scopes. Sharing the definition does not transfer credentials, provider connections, device permissions or Always Trust grants.
Where shared execution supports multiple identities, run-as semantics MUST be explicit, such as triggering user versus Automation owner/service identity. The runtime MUST NOT silently choose whichever identity has greater authority.
Compatibility and migration
Every registered node type/action/event records schema/version identity. If an app/device/provider update retires or incompatibly changes a node schema, the graph enters NeedsAttention/migration state rather than silently dropping the node, retyping a port or changing behaviour.
Migration may be automatic only when semantics are provably equivalent; otherwise the editor must show the affected nodes and require review before reactivation.
Persistent contextual AI bar
Automations MUST host the global persistent Dulche AI bar. Its semantic context includes AutomationID, GraphID, Draft/Active revisions, selected NodeIDs/ConnectionIDs, validation state, Run/connection/permission state, registered node catalogue and typed graph mutation actions.
Read-only mode can explain/debug/inspect. Write mode may modify the graph only through typed Node Graph/Automation APIs. AI MUST NOT simulate graph mouse gestures.
Automations API
Automations' first-party API catalogue is exhaustive under the global API-parity rule. Minimum canonical entry points include:
9to1.Automations.List(filters?);
9to1.Automations.Get(automationID);
9to1.Automations.Create(config);
9to1.Automations.Update(automationID, patch);
9to1.Automations.Delete(automationID);
9to1.Automations.Restore(automationID);
9to1.Automations.Validate(automationID, revision?);
9to1.Automations.Activate(automationID, revision);
9to1.Automations.Disable(automationID);
9to1.Automations.Run(automationID, inputs?, revision?);
9to1.Automations.CancelRun(runID);
9to1.Automations.PauseRun(runID);
9to1.Automations.ResumeRun(runID);
9to1.Automations.GetRun(runID);
9to1.Automations.ListRuns(automationID, filters?);
9to1.Automations.RetryRun(runID, fromNodeID?);
9to1.Automations.ListAvailableActions(context?);
9to1.Automations.ListDeviceActions(deviceID?);
Graph editing remains on 9to1.NodeGraph.* rather than duplicating graph-mutation methods inside Automations. Every additional user-visible Automations operation — Library organisation, connection repair, permission review, execution-policy change, sharing, migration and run controls — requires equivalent typed API coverage.
Persistence and recovery
Automation definitions, Draft/Active revisions, Runs, wait/approval continuations, trigger cursors/deduplication state and connection references are durably versioned. A crash/restart MUST NOT restart the graph from the beginning and duplicate already-committed actions.
Normal Automation deletion is recoverable; permanent purge is explicit/high-risk. Run-history retention/purge follows declared policy and audit requirements.
Accessibility
The landing Home/Library, node graph and Run inspector MUST be keyboard-operable and accessible. Node search can be opened without a pointer; nodes, ports and connectors expose semantic labels/types; connection creation/deletion has keyboard equivalents; execution/error states cannot be conveyed by colour alone.
Graph zoom/pan must not be required to discover the textual meaning of a node. A structured accessible node/connection list or equivalent semantic representation MUST be available to assistive technology.
Performance and resource behaviour
Large graphs, Libraries and Run histories MUST virtualise where appropriate. Node catalogue search must remain responsive across extensive app/device/third-party action registries and should query/index incrementally rather than blocking on full capability rescans.
Execution is event-driven/durable; idle/waiting Runs must not retain unnecessary UI/execution threads. Run logs/timelines paginate/stream incrementally and caches are bounded.
Failure behaviour
Automations MUST return structured failures including AutomationNotFound, RunNotFound, InvalidGraph, ValidationFailed, NodeTypeUnavailable, ConnectionTypeMismatch, CapabilityUnavailable, DeviceCapabilityUnavailable, DeviceOffline, ProviderConnectionUnavailable, PermissionRequired, ApprovalRejected, ApprovalTimedOut, WaitResumeFailed, RevisionConflict, TriggerDuplicate, LoopDetected, ExecutionBudgetExceeded, UnsafeRetryState and MigrationRequired in addition to target-action errors.
A missing third-party app, disconnected provider or unavailable device never causes the runtime to pretend an action completed; affected definitions/runs surface Needs Attention or the configured wait/failure path.
Acceptance requirements
Automations is implementation-complete only when acceptance coverage verifies at least:
* Landing Home shows canonical operational dashboard state and Library lists persisted user Automations with search/filter/status/last-next-run information.
* Opening an Automation exposes its real Node Graph; simple/natural-language creation produces the same canonical graph rather than a hidden alternate workflow format.
* Right-click empty graph space opens an Unreal-style searchable node list; typing filters the live registered catalogue; Ctrl-click on a connector deletes it and right-click offers Delete Connector.
* Draft and Active revisions remain distinct; activating a new revision does not mutate an already-running Run pinned to an older revision.
* Manual, Schedule, App Event, Condition/Watch, external/provider and Automation-call triggers deliver typed payloads with deduplication identity.
* A representative extensive device action registry is discovered from real device capabilities, including available Wi-Fi/Bluetooth/AOD/lock-screen-clock actions, and device-specific portability warnings/capability validation are shown.
* Third-party app/service actions appear from installed/connected capability registries and missing/unavailable integrations enter a diagnosable Needs Attention state.
* A schedule at 16:00 can invoke a registered YouTube/compatible media Play action on a compatible target device without UI simulation.
* Natural-language graph creation/editing produces valid typed Node Graph mutations and remains fully inspectable/undoable.
* Durable Wait and Approval nodes survive process/device/runtime restart without replaying committed prior actions.
* Retries use idempotency semantics and uncertain external side effects do not blindly repeat.
* Self-triggering/cross-Automation loops are detected and stopped with causal diagnostics.
* Validate, Test and Run-for-real remain distinct and Test does not silently perform unsimulatable external actions.
* Shared Automations do not inherit another user's credentials/device permissions/trust grants and run-as identity remains explicit.
* Schema/version changes place incompatible graphs into migration/Needs Attention rather than silently altering behaviour.
* The persistent AI bar inspects/edits graphs only through semantic context and typed APIs.
* Large node catalogues/graphs/run histories remain responsive through indexing, virtualisation and incremental loading.
* When Home is absent at process start, Automations follows the global Install Home/bootstrap contract rather than launching an incompatible private permission/runtime stack.
Creativity Apps
