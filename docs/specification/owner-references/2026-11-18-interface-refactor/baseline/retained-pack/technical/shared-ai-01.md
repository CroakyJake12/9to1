# Shared AI controls and artifact collaboration — Chat bar — expanded-mode formatting and emoji controls; Chat-bar control acceptance (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0312 · v2 lines 1749–1749

Every interactive 9to1 app MUST expose the shared persistent floating contextual AI bar at the bottom of its primary content surface. The bar uses Dulche plus the owning app's authorised semantic state. It MUST be able to inspect the current artifact/surface semantically and, when permitted, edit it through the owning app's typed APIs. The bar is not limited to whatever pixels happen to be visible in the viewport.

### V2-B0313 · v2 lines 1752–1752

The shared bar MUST expose an explicit persistent AI access-mode switcher with at least Read-only and Write mode. The current mode MUST remain visible enough that the user can tell whether Dulche may only inspect the surface or may also request mutations. This switcher is a platform-wide interaction contract used consistently by Write, Present, Boards, Files, Canvas, Data, Mail, Planner and every other surface that hosts the shared bar.

### V2-B0314 · v2 lines 1755–1755

In Read-only mode, Dulche MAY read, search, compare, reason over, summarise, explain and navigate the full authorised semantic surface, including off-screen content that the owning app exposes. It MAY generate proposed changes in conversation, but MUST NOT mutate the owning artifact, structure or app state. Read-only access does not imply permission to inspect data outside the caller's normal authorisation boundary.

### V2-B0315 · v2 lines 1758–1758

In Write mode, Dulche receives the same semantic read context plus the owning app's permitted typed mutation actions. Write mode is capability to request/perform edits, not permission to bypass app state, trust, confirmation, review, external-side-effect or security rules. Mutations MUST use stable semantic identities such as ArtifactID, PageID, SectionID, ObjectID, row/range/entity IDs or equivalent domain identifiers and the owning app's normal revision/conflict semantics.

### V2-B0316 · v2 lines 1761–1761

The AI-bar mode is separate from the host app's own Edit/View/Suggest/Locked/Unlocked or equivalent state. For example, an AI bar in Write mode does not silently bypass a Board that is currently in View mode, and a Write document in Suggest mode represents permitted AI edits as suggestions rather than accepted direct edits. Host-surface rules may narrow the effective mutation set further.

### V2-B0317 · v2 lines 1764–1764

Apps MUST provide a permission-filtered semantic context snapshot including the active artifact/page/entity, current selection, relevant stable object IDs, structure necessary to understand the task, current revision/state and supported typed actions. The bar MUST prefer this semantic context over screenshot/OCR/pixel interpretation and MUST NOT substitute raw mouse/keyboard/desktop automation when typed context/actions exist.

### V2-B0318 · v2 lines 1767–1767

The AI bar MUST remain visually separate from document/page content and MUST NOT change the underlying artifact layout merely by being shown. It MAY collapse to a compact state but MUST remain directly reachable without navigating to a separate AI page. Read/Write mode changes the AI's effective capability only; it does not create a second copy of the current artifact.

### V2-B0319 · v2 lines 1770–1770

Surface-specific mutation protections MAY be stricter than the shared Write-mode baseline and cannot be weakened by AI mode or trust state. Data is the mandatory example: every AI-requested Data mutation requires explicit approval for that specific action even while the bar is in Write mode. When the target is a live database, Data MUST additionally show a meaningful preview/diff, validate the operation and create a recoverable backup/snapshot/checkpoint before execution. If the required live-database backup cannot be created, the mutation MUST NOT proceed as though rollback exists.

### V2-B0320 · v2 lines 1773–1773

For Data live-database changes, the approval surface SHOULD identify the intended operation, affected tables/entities/rows where knowable, schema/dependency impact, external side effects and backup identity. Execution MUST occur only after approval, then verify the actual result and preserve rollback/recovery information. Prior approval, Accept and Trust or Always Trust MUST NOT suppress this Data-specific per-action approval invariant.

### V2-B0321 · v2 lines 1776–1776

Equivalent bar actions MUST behave consistently across product surfaces, while menus and available operations remain context-sensitive to the owning app. Unsupported or inapplicable actions MUST not be exposed merely to make the bar look feature-complete.

### V2-B0322 · v2 lines 1779–1782

Chat bar — expanded-mode formatting and emoji controls
Visibility and placement: Show the following buttons inline next to the plus (+) button only when the chat bar is both focused and in its expanded composer layout, with the text-entry area above the bottom controls. These buttons must not appear in compact/mini mode, even when the compact/mini chat bar is focused.
Format (Aa): Opens a floating toolbar containing Markdown formatting options, including bold and italics.
Emojis (😀): Opens a CUI emoji picker.

### V2-B0323 · v2 lines 1785–1786

Chat-bar control acceptance
Verify that Aa and the emoji button appear beside + only while the expanded composer is focused; neither appears in compact/mini mode. The formatting toolbar and CUI emoji picker MUST work with keyboard input, labelled controls, focus return and preserved draft/selection state.

### V2-B0324 · v2 lines 1789–1790

Cross-app invocation and compose contract
Every interactive 9to1 app that hosts the shared persistent AI bar MUST use Home's canonical compose invocation component and cross-app dispatcher. Plugins, Skills, Apps, Agents, Files and the reserved @Computer Use capability MUST therefore resolve consistently across apps rather than each product implementing a private @ syntax or resource picker.

### V2-B0466 · v2 lines 2378–2378

Action Graph renderer

### V2-B0467 · v2 lines 2381–2381

Home owns the shared user-facing Action Graph renderer/source. Dulche owns execution graph/log data and persistence semantics; Home provides the reusable CUI/Markdown presentation component used to display authorised graph data.

### V2-B0468 · v2 lines 2384–2384

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

Any authorised app MAY host the shared renderer given an authorised RequestID/ActionGraphID. Home MUST NOT create a second execution history or mutate Dulche’s causal graph merely for presentation. Notifications, Agents, Spaces and other surfaces MAY deep-link into the same renderer.

### V2-B0469 · v2 lines 2387–2387

Home does not own the primary Voice, Vision or AI Overlay session controls. Those live in Vision & Voice; Home remains responsible only for the shared runtime/model configuration and renderer/platform services they consume.

### V2-B0470 · v2 lines 2390–2390

Persistent contextual AI bar

### V2-B0471 · v2 lines 2393–2393

Home owns the reusable base component/service contract for the global persistent bottom AI bar required by interactive 9to1 apps. The shared component provides CUI rendering, Dulche connection, Universal Model Picker integration, permission/trust plumbing, request state and common Action Graph access.

### V2-B0472 · v2 lines 2396–2396

Each host app MUST provide the bar with a permission-filtered semantic context snapshot and its typed supported actions. Home MUST NOT obtain app context through raw screen scraping when the host provides structured context. App-specific mutation semantics remain owned by the target app APIs.

### V2-B0473 · v2 lines 2399–2417

Floating compose @ invocation menu
The shared Home-owned floating AI compose component MUST provide one searchable @ invocation menu for Plugins, Skills, Apps, Agents and Files. The menu is a single interaction surface with collapsible type sections rather than separate unrelated pickers. Typing @ opens the resource list; typing text after @ filters across all sections while preserving each result's type and identity.
Selecting an item inserts an inline invocation chip/token inside the compose field at the caret position. The rendered token MUST display the resource's icon where available plus its human-readable label, for example an Agent may render as '@[agent icon] John'. The token MUST use a distinct invoked visual state, including colour/style differentiation, while also using non-colour cues for accessibility. Editing/deleting the token removes or changes the invocation rather than leaving invisible hidden attachment state.
Invocation tokens MUST store stable canonical identities, not only display text: Plugin PackageID/version or resolved active version, SkillID/version, AppID, AgentID/DefinitionRevision where required and File/Artifact reference identity/revision or access token as appropriate. Duplicate display names MUST therefore remain unambiguous. The resolved invocation set is included in request metadata, FullLog and Action Graph.
The Plugins section lists Dulche Plugins enabled/available in the current scope; Skills lists eligible Dulche Skills including the built-in Persistent Skill; Agents lists Agents available through Dulche scope resolution; Files searches authorised files/artifacts without silently granting new access. Availability in the picker never grants permission to execute protected actions or read inaccessible content.
The Apps section MUST contain every installed/available first-party 9to1 app plus user-installed applications that 9to1 can classify as safely operable through a typed API/integration or, when separately authorised, Computer Use. App entries MUST expose whether their interaction path is Typed/API, Computer Use Required, or both. Selecting an app whose only route is Computer Use MUST NOT activate Computer Use by itself; the request also requires the explicit @Computer Use invocation.
Game executables and anti-cheat-protected game surfaces MUST be excluded from the operable Apps invocation results even when discovered through Steam, Epic Games Launcher or another launcher/library. If 9to1 cannot confidently distinguish an eligible ordinary application from a game/anti-cheat target, it MUST omit or mark the target unavailable for Computer Use rather than risk automation. Play remains the supported Agent-game route defined by its own contract.
@Computer Use is a reserved first-party system capability entry in the same searchable invocation surface. Selecting it creates the explicit ComputerUseInvocationID required by the Dulche Computer Use contract and makes its active state visible in the compose field before the request is submitted.
Cross-app execution visualisation
Models operating inside any 9to1 app MAY invoke another 9to1 app only when the user's request explicitly names/invokes that app, directly asks for an outcome owned by that app, or unambiguously implies the owning app from the requested artifact/action. For example, 'create a presentation' resolves to Present; 'make a spreadsheet' resolves to Data; an unrelated app MUST NOT be opened merely because the model thinks it might be useful.
Cross-app intent resolution MUST bind to the owning app's stable AppID and typed capability contract. If the required app is not running, Home MAY launch/open it automatically when that is part of fulfilling the request. The target artifact remains canonical in its owning app, and the originating app/Space receives references/status rather than an unmanaged duplicate.
Typed app APIs and shared semantic actions are the normal execution path. Computer Use is not a fallback for missing/failed APIs and remains unavailable unless @Computer Use was explicitly invoked. Permission, review, edit/suggest/view state, conflicts and external-side-effect rules of the target app still apply to cross-app work.
Where an app has a visible editing surface, the user SHOULD be able to watch model-driven edits occur in real time. Home/shared app infrastructure MUST provide an execution-visualisation stream that maps semantic actions to visible UI changes such as objects moving, text appearing, selections changing, slides being created or a virtual cursor hovering over the affected target.
The execution visualiser is representational, not the control mechanism. A virtual cursor MUST be visually distinguishable from the user's real pointer and MUST NOT generate mouse/keyboard input merely to make the animation believable. The canonical typed API/action result remains authoritative; visual animation follows confirmed/provisional semantic events and MUST reconcile to final app state after errors, retries or rollback.
AI conversation presentation settings
Home Settings MUST expose global conversation presentation preferences for Plain text versus Speech bubbles. The preference applies to compatible 9to1 conversational/AI surfaces through the shared renderer rather than each app inventing a different message layout.
User-message alignment and model-message alignment MUST be independently configurable as Left or Right. All four combinations are valid, including both parties on the same side. Alignment changes presentation only and MUST NOT alter message order, authorship, accessibility reading order or exported conversation data.
The selected Dulche thinking-log mode (Precise, Concise, Detailed or Ramble) MUST be configurable in the same AI/conversation settings family. Inline thinking/work regions render beneath or between message text according to chronology; when Speech bubbles are enabled, the thinking/work region visually belongs beneath the relevant model bubble without being styled as ordinary model-authored message text.
Thinking/work presentation MUST use lower visual emphasis than normal chat while remaining readable and accessible. Collapsed 'Thought for ...'/'Worked for ...', ETA status, blockers and Problem events MUST use the same shared renderer across apps so equivalent Runtime state is not represented differently by each product.
