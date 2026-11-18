# Spaces — conversations, workspaces and custom Space Builder — Custom sidebar surfaces; Long-running work and Action Graph (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0869 · v2 lines 4030–4030

If a request would modify protected built-in behaviour, the editor/AI MUST reject that mutation and direct the user toward a user-created Space/full graph instead of attempting an unsafe override.

### V2-B0870 · v2 lines 4033–4033

Custom sidebar surfaces

### V2-B0871 · v2 lines 4036–4036

Space extension graphs MAY add custom sidebar items without replacing protected built-in items. Every custom SidebarItem MUST have stable SidebarItemID, title, icon, order/placement, visibility condition where supported, InterfaceType, interface reference/definition and required permissions.

### V2-B0872 · v2 lines 4039–4039

Supported InterfaceType families MUST include AppSurface, CUI and GenerativeUI. AppSurface embeds an authorised surface from another 9to1 app such as Planner, Files, Boards, Maps, Stack or another declared embeddable surface while preserving canonical owning-app behaviour. CUI renders an explicitly authored safe interface backed by typed state/actions. GenerativeUI renders a Dulche-generated interface constrained by the Generative UI schema/component/action rules.

### V2-B0873 · v2 lines 4042–4042

Generative sidebar interfaces MAY be Dynamic (regenerated/refreshed from current authorised context) or Persisted (stored definition until explicitly regenerated/edited). Generated UI MUST NOT execute arbitrary code or gain permissions merely because it is displayed.

### V2-B0874 · v2 lines 4045–4045

Protected built-in sidebar IDs/namespaces cannot be shadowed/overwritten by extensions. Custom entries use extension-owned stable IDs.

### V2-B0875 · v2 lines 4048–4048

Long-running work and Action Graph

### V2-B0876 · v2 lines 4051–4051

Long-running requests/tasks MUST expose run status, current objective/step, elapsed state, Subagents/tool activity summaries, blockers, approvals and outputs where applicable, with Pause/Resume/Stop/Open Action Graph controls supported by the underlying run.

### V2-B0877 · v2 lines 4054–4054

Closing/navigating away from Spaces MUST NOT automatically kill a run that was explicitly started with background-capable semantics. Home/Dulche retains durable run state and may notify through Home when work completes, blocks, needs approval or fails.

### V2-B0878 · v2 lines 4057–4057

Tool/action rendering may be collapsed in conversation but MUST remain inspectable. Action Graph opens the actual Dulche execution graph; Spaces MUST NOT fabricate a second independent action history.

### V2-B0879 · v2 lines 4060–4060

Agents and Subagents

### V2-B0880 · v2 lines 4063–4063

Persistent Agents may be invoked inside any Space where available/authorised. The Space contributes authorised context but MUST NOT expand the Agent’s own effective permissions, tool scopes or memory policy.

### V2-B0881 · v2 lines 4066–4066

Dulche Subagents remain runtime child-execution primitives and MUST NOT be conflated with persistent user-facing Agents. Spaces may show concise worker counts/status while detailed execution remains inspectable in Action Graph.

### V2-B0882 · v2 lines 4069–4069

Memory and Dulche Den

### V2-B0883 · v2 lines 4072–4072

Spaces MUST support explicit memory/context scopes including conversation-local, Space-level and permitted broader personal/team/organisation scopes. Space-level persistent memory/context lives through Dulche Den under stable Space identity and applicable permissions.

### V2-B0884 · v2 lines 4075–4075

Shared Spaces MUST NOT implicitly expose a participant’s private personal Den context or private connected accounts. Caller-specific private context remains private unless deliberately shared through an authorised shared/team mechanism.

### V2-B0885 · v2 lines 4078–4078

Web access and source provenance

### V2-B0886 · v2 lines 4081–4081

Web access MUST be an explicit Space/tool policy with at least Allowed, Ask and Disabled semantics where applicable. Source-only/closed-corpus workflows MUST be able to disable outside web retrieval completely.

### V2-B0887 · v2 lines 4084–4084

When an answer materially relies on identifiable source material, Spaces MUST preserve clickable provenance/citations back to the underlying file/page/message/web source/entity where supported. It MUST distinguish source-derived claims from generated reasoning and must not claim support that the retrieved sources do not provide.

### V2-B0888 · v2 lines 4087–4087

Search, organisation and sharing

### V2-B0889 · v2 lines 4090–4090

Spaces MUST support search across authorised Space/conversation titles, messages, source names and applicable run/tool metadata. Search respects Space sharing/permissions.

### V2-B0890 · v2 lines 4093–4093

Conversations MUST support rename, pin, archive, delete/recover, move to another compatible Space, duplicate/fork and export. Moving a conversation changes future Space environment/policy but MUST preserve historical provenance/model/action records.

### V2-B0891 · v2 lines 4096–4096

Spaces MAY be shared at Space or conversation level with explicit roles such as Viewer, Participant and Editor/Admin or equivalent typed scopes. Multi-human conversations MUST record AuthorPrincipalID per human message. Sharing a Space MUST NOT automatically share private external-account credentials or personal Den state.

### V2-B0892 · v2 lines 4099–4099

Study structured state

### V2-B0893 · v2 lines 4102–4102

Study MUST persist structured learning state such as course/topic/concept, attempts, recorded confidence/proficiency evidence, last reviewed, next review and source references. It MUST support source-only versus broader-information policy and clearly distinguish supplied-source content from outside knowledge when both are permitted.

### V2-B0894 · v2 lines 4105–4105

Tasks structured state

### V2-B0895 · v2 lines 4108–4108

Task MUST contain at least TaskID, Objective, Status, Plan, ordered Steps, CurrentStep, Dependencies, Inputs, Outputs, Blockers, Approvals and associated RunID(s). Required states include Draft, Ready, Running, Waiting, Blocked, Paused, Completed, Failed and Cancelled.

### V2-B0896 · v2 lines 4111–4111

A Task may invoke persistent Agents, Dulche Subagents and tools, and may itself be launched by an Automation, but Task identity remains the user-level work item rather than becoming an Agent or Automation.

### V2-B0897 · v2 lines 4114–4114

Spaces API

### V2-B0898 · v2 lines 4117–4154

9to1.Spaces.List();
9to1.Spaces.Get(spaceID);
9to1.Spaces.Create(config);
9to1.Spaces.Update(spaceID, patch, expectedRevision?);
9to1.Spaces.Delete(spaceID);
9to1.Spaces.Restore(spaceID);
9to1.Spaces.Move(spaceID, parentSpaceID?, position?);
9to1.Spaces.ListConversations(spaceID);
9to1.Spaces.CreateConversation(spaceID, config?);
9to1.Spaces.GetConversation(conversationID);
9to1.Spaces.Send(conversationID, message);
9to1.Spaces.Stop(requestID);
9to1.Spaces.Resume(requestID);
9to1.Spaces.Retry(messageID, options?);
9to1.Spaces.Regenerate(messageID, options?);
9to1.Spaces.BranchFrom(messageID);
9to1.Spaces.SwitchBranch(conversationID, branchID);
9to1.Spaces.Context.List(conversationID);
9to1.Spaces.Context.Add(conversationID, reference);
9to1.Spaces.Context.Remove(conversationID, contextID);
9to1.Spaces.Capabilities.List(spaceID);
9to1.Spaces.Capabilities.SetOverride(spaceID, capabilityID, state);
9to1.Spaces.Artifact.Open(reference);
9to1.Spaces.Artifact.ApplyAction(reference, action);
9to1.Spaces.Tasks.Create(spaceID, config);
9to1.Spaces.Tasks.Run(taskID);
9to1.Spaces.Tasks.Pause(taskID);
9to1.Spaces.Tasks.Resume(taskID);
9to1.Spaces.Tasks.Cancel(taskID);
9to1.Spaces.Sidebar.List(spaceID);
9to1.Spaces.Sidebar.Add(spaceID, item);
9to1.Spaces.Sidebar.Update(spaceID, sidebarItemID, patch);
9to1.Spaces.Sidebar.Remove(spaceID, sidebarItemID);
9to1.Spaces.Extensions.GetGraph(spaceID);
9to1.Spaces.Extensions.UpdateDraft(spaceID, graph, expectedRevision?);
9to1.Spaces.Extensions.Validate(spaceID, revision?);
9to1.Spaces.Extensions.Activate(spaceID, revision);
9to1.Spaces.Share(spaceID, principalID, role);

### V2-B0899 · v2 lines 4157–4157

These are minimum canonical entry points. Every user-visible Space operation, built-in extension action and full custom graph action MUST have equivalent typed APIs under the global API-parity rule.

### V2-B0900 · v2 lines 4160–4160

Failure behaviour

### V2-B0901 · v2 lines 4163–4163

Spaces MUST return structured failures including SpaceNotFound, ConversationNotFound, MessageNotFound, ContextUnavailable, ContextRevisionConflict, SourceIngestionFailed, PermissionDenied, ModelUnavailable, ModelCapabilityMismatch, ToolUnavailable, ToolFailed, ArtifactUnavailable, ArtifactRevisionConflict, RunBlocked, RunCancelled, RunFailed, WebAccessDisabled, MemoryScopeDenied, SharedContextUnavailable, ProtectedSpaceMutationDenied, InvalidGraphConnection, GraphValidationFailed and ExtensionHookIncompatible in addition to the global error contract.

### V2-B0902 · v2 lines 4166–4166

Acceptance requirements

### V2-B0903 · v2 lines 4169–4199

Spaces is implementation-complete only when acceptance coverage verifies at least:
* Chat, Study, Tasks, Agent, Shopping, Research, Translate and Experiences remain distinct protected first-party Space types.
* Translate Space shows selected language chips immediately above the normal chatbar, uses normal Space context, and renders each target-language result as its own labelled assistant speech bubble.
* Experiences maintains canonical structured Experience state separately from generated narrative text, including hidden/private state where configured.
* Experience branching/rewind preserves the matching conversation and Experience-state revision without pretending to undo real external side effects.
* Persistent Agent characters receive only their authorised Experience view, while lightweight characters do not require permanent Den Agent creation.
* Experiences can launch a formal Play Match and receive an explicit Play-to-Experiences handoff without implicitly merging MatchState and ExperienceState.
* + New Space creates a user-defined Space and Edit Spaces can reorder/nest/manage Spaces without exposing protected first-party internals.
* The sidebar uses the selected-Space control, built-in destinations, New Chat, Space-relevant options, + New Space and Edit Spaces in the defined hierarchy.
* Stopping a resumable response replaces Stop with Play/Resume and Resume continues the same run without redoing completed consequential actions.
* Editing/regenerating earlier messages creates branches without destroying prior history/action records.
* Every Space inherits globally installed/enabled Dulche Skills/Plugins and available Agents by default, while per-Space exclusions work and do not grant permissions.
* Built-in Spaces expose only the protected Events & Extensions graph and cannot remove/replace required first-party behaviour/UI.
* A built-in Study extension can turn on Do Not Disturb on activation and restore/perform a configured safe action on deactivation.
* A built-in Space can add a custom sidebar item without shadowing/removing protected items.
* Custom sidebar items can host an authorised app surface such as Planner, an authored CUI interface and a safe Generative UI interface.
* User-created Spaces expose the full graph editor with Draft/Validate/Activate behaviour and rollback-capable prior active revision.
* The graph-editor AI bar can create a valid extension graph through typed Node Graph actions and explain the resulting behaviour.
* Invalid type connections, unavailable packages/Agents, missing permissions and unbounded cycles are detected before activation.
* A Write artifact and Board/shared object can be opened/edited canonically from Spaces without duplicate copies.
* Shared Productivity Engine objects round-trip between Spaces and supporting productivity apps without schema/format loss.
* Explicit sources preserve identity/revision/provenance and large source sets use inspectable retrieval/context budgeting.
* A web-disabled/source-only workflow cannot silently browse outside its allowed corpus.
* Persistent Agent permissions are not expanded merely because the Agent is invoked from a Space.
* Private personal Den context does not leak into a shared Space.
* Long-running background-capable work survives navigation/closing the UI and reconnects to Home/Dulche state.
* Action Graph opens the real Dulche execution graph and Subagents remain distinct from persistent Agents.
* Study progress is based on recorded study interactions/answers rather than unsupported confidence inference.
* Tasks remain distinct entities from Agents and Automations.
* Home/Dulche restart preserves durable conversation/run state and does not corrupt Space history.
* Spaces remains usable for ordinary conversation when advanced agentic/tool capabilities are unavailable.

### V2-B2184 · v2 lines 12960–12962

User Interface
Mini Sidebar
Spaces sidebar - Replaces the dropdown switcher by default. Each pinned space is listed as an individual item on the mini sidebar, plus the spaces browser.

### V2-B2185 · v2 lines 12965–12965

Option in settings between dropdown switcher and sidebar.

### V2-B2186 · v2 lines 12968–12971

LE-03 — Mini sidebar behaviour
The rail is the default. Each pinned Space is an independently selectable, named item, followed by access to the Spaces browser. The browser exposes all authorised Spaces and the existing create/edit/manage actions, including unpinned built-ins and nested user-created Spaces. Pinning, unpinning and reordering preserve SpaceID; names are not identities.
Settings MUST persist the rail/dropdown preference. Both layouts expose equivalent navigation and current-Space identity; switching layouts does not move conversations, reset the active Space or drop drafts. Overflow, keyboard navigation, touch targets and screen-reader names MUST work without relying on icons alone.
Acceptance: navigate pinned and unpinned Spaces in both layouts, change pins/order, restart and verify persistence, and operate the same flows with keyboard and narrow/touch layouts.

### V2-B2187 · v2 lines 12974–12975

Horizontal Tabs
Horizontal tabs in spaces app for multiple chats, open windows, etc.

### V2-B2188 · v2 lines 12978–12978

Other 9to1 apps (e.g. browse) can be embedded into spaces via a tab, as well as things like chats and images. Thanks to a shared ui framework, the design should be able to adapt to fit seamlessly with the rest of the spaces interface.

### V2-B2189 · v2 lines 12981–12981

UI can also be generated into a separate tab if the context makes sense for that, but that should not replace inline generative UI.

### V2-B2190 · v2 lines 12984–12989

LE-04 — Tabs and embedded app lifecycle
A Spaces tab MUST have stable TabID, target type/identity, source Space/context, current view mode and restorable navigation state. Targets include canonical conversations, images/artifacts, registered AppSurfaces and safe Generative UI surfaces. Ordinary Open focuses an existing equivalent target tab; an explicit new-view action may open another view of the same canonical entity. Tabs support select, close, reorder and keyboard navigation.
Switching or closing a tab MUST NOT delete its artifact or cancel an explicitly background-capable Dulche run. Preserve drafts; closing an unsaved editor must save safely or offer Save/Discard/Cancel where a decision is required. Restore saved tab targets and layout after restart, showing unavailable targets honestly. Restoration MUST NOT replay prompts, external actions or historical tool calls.
Embedded apps use their real registered CUI surfaces and owning services. Open in App retains the same artifact ID, revision and permissions. The active tab's source Space, account and semantic target MUST be clear to the user and AI bar; unrelated tabs do not silently contribute private context. Host and embedded chrome MUST avoid duplicate overlapping AI bars or inaccessible Save/Close controls.
Generated tab UI uses the existing safe schema, stable SurfaceID and typed actions. It may supplement, but MUST NOT replace, inline conversational Generative UI. Closing a generated view does not silently erase a separately saved artifact.
Acceptance: keep two chats and an editable app artifact open; switch and restart without losing work; verify Open in App sees the same edit; close a tab while an authorised background run continues; revoke access and verify cached tabs cannot bypass it; confirm inline and tabbed generated UI both function. Inactive tabs MUST release avoidable render work and use bounded caches rather than running every hidden surface at full activity.

### V2-B2191 · v2 lines 12992–12993

Main Sidebar
The main sidebar should be fully customisable via a visual drag/drop editor. The order of precedence is: Space Overrides (eg adding a custom item or forcing a certain sidebar layout), User Preferences, Default.

### V2-B2192 · v2 lines 12996–13000

Example:
| New Chat |
– Pinned ❌ –
| View All |
| Pinned Chats (Top 5)

### V2-B2193 · v2 lines 13007–13007

 |

### V2-B2194 · v2 lines 13010–13012

– College ❌–
| New Assignment |
| Chats from ‘College’ space (Top 5)

### V2-B2195 · v2 lines 13019–13019

 |

### V2-B2196 · v2 lines 13022–13022

| + Add | // Add a section, item, etc.

### V2-B2197 · v2 lines 13027–13027

This will support custom sections on the sidebar, like chatgpt desktop app does. A section can be configured to take chats manually or automatically (eg all chats of a certain topic or containing certain words automatically go into the section, and chats can also be autoremoved after x days of inactivity - this is all done via the automations node system).

### V2-B2198 · v2 lines 13030–13030

You can also add automations/plugins to the sidebar, allowing you to create entirely custom buttons.

### V2-B2199 · v2 lines 13033–13033

You can also display chats, buttons, and interfaces from other spaces, allowing you to create a home/master space with everything one click away.

### V2-B2200 · v2 lines 13036–13036

Drag items to re-arrange.

### V2-B2201 · v2 lines 13039–13045

LE-05 — Custom sidebar editor and section rules
The visual editor MUST expose + Add for a section, chat/reference, app/CUI/Generative UI surface, or authorised Plugin/Automation action. Users can rename, configure, move, reorder and remove custom entries and sections. Dragging rearranges items and sections, including moving an item between sections. Keyboard/touch alternatives, undo and Reset to Default are required. The example's remove controls remove sidebar organisation only, not the underlying chats or assignments.
Use stable SidebarSectionID and the existing SidebarItemID/contribution registry. Persist section order, item order, source references, rule/Automation bindings, visibility, configurable result limit and revision. The Pinned and College examples must support View All, a top-five chat list and New Assignment; they are not fabricated demo data. An item may reference another Space without copying its content or changing its owner.
Resolve each configurable field as Space Override, then User Preference, then Default. Show which values are controlled by a Space and why an attempted edit is constrained. Reset clears the chosen user override rather than deleting Space data. Built-in protected behaviour cannot be removed, replaced or shadowed through either editor or Plugin IDs. Security and organisation policy restrict every level of this presentation precedence.
Manual section membership stores references. Automatic membership is an inspectable, validated Automation/Node Graph rule over authorised chat metadata/content, including topic/keyword matching and configured inactivity duration. Removing an inactive chat from a section MUST NOT archive, delete or move the source chat. Manual and rule-based contributions must resolve to one visible reference per section; changing/disabling a rule must not erase unrelated manual membership. Match updates are incremental and bounded, not repeated whole-history scans.
Cross-Space buttons and interfaces retain explicit source-Space context and current permissions. Displaying an item does not grant execution rights. Plugin detachment, revoked access or unavailable actions produces a non-executable unavailable state and removes obsolete contributions without orphan handlers. All contribution routes use the same registry and typed operations.
Acceptance: add/remove sections and items with + Add; drag and keyboard-reorder them; test all three precedence levels and reset; configure a keyword and inactivity rule; prove section removal leaves the chat intact; embed College actions in a master Space; restart; detach a Plugin and revoke access without duplicate or executable orphan entries.
