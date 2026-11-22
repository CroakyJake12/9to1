# 8. Engineering & acceptance

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.vkroobcb81z8

<a id="h.s674sgfgzbkh"></a>

# Engineering, optimisation and acceptance

<a id="h.oclm5zwnlny6"></a>

## 8.1 Implementing the contract

ENG-001 — Work in the existing product and reuse functioning foundations. The work spans the application, shared CUI components, routing, runtime adapters, services, persistence, migrations, permissions, shortcuts and integrations wherever those must change to satisfy the contract. A new mockup, alternate app fork or cosmetic wrapper is not completion. Keep the same canonical features usable in hosted and standalone compositions.

ENG-002 — Before modifying a slice, identify its actual entry point, canonical state owner, command path, storage, service boundary and relevant tests. The visibly active surface may differ from a plausible similarly named legacy class. Trace the running route or composition before editing. Record the inspected commit and distinguish source observations from live behaviour.

ENG-003 — Maintain a capability inventory with conforming, adaptation, explicit conflict, additional compatible and incomplete classifications. Record retention, adaptation, replacement or blocking, with the specific requirement and [Atlas](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html) Screen IDs. Preserve user documents, valid preferences, integrations, supported formats, shortcut access and recovery. An implementation may replace obsolete plumbing after parity and migration evidence; it must not remove a useful capability to make visual conformity easier.

ENG-004 — Make complete increments. A slice includes its actual operation, meaningful disabled/error/cancel state, persistence or declared session scope, keyboard access and regression evidence. Begin with shared shell and primitives, then one complete canonical artifact journey, then the remaining applications and runtime capabilities. This sequencing is an implementation strategy, not a reduced product scope or a new fixed calendar deadline.

ENG-005 — Use one semantic command behind a toolbar, context menu, shortcut and AI tool path when they represent the same operation. User and AI initiation can have different grants and review requirements; both must address the same canonical objects and revision rules. Do not duplicate business logic in a view or treat a successful local UI event as a service receipt.

ENG-006 — Keep implementation choices proportionate. Ordinary reversible choices within the contract may be made and recorded by the worker. A consequential unresolved policy blocks only its affected operation. Continue independent work and state the exact missing decision, dependency or evidence. Do not replace a genuine unknown with fixture data or a guessed “approved” default.

<a id="h.wstva3mluxjk"></a>

## 8.2 Interaction correctness

INPUT-001 — Context menus target the innermost meaningful object. Right-clicking a member of an existing multi-selection preserves that multi-selection; targeting another object changes the target deliberately. Store canonical IDs and relevant revisions. Opening the menu changes focus or selection only. Recheck permission, target existence and revision when the user activates the command.

INPUT-002 — Pointer, keyboard context-menu invocation and a visible overflow button expose equivalent applicable actions. Native text fields retain their text-editing menus, composition and caret keys. A shortcut handled by a popup or editor must not also dispatch through the app or host. One gesture produces at most one accepted command.

INPUT-003 — A transient menu chain has one consistent target. Opening a sibling closes the previous chain; persistent inspectors remain independent. Escape closes the innermost transient surface and restores its trigger, or the nearest surviving owning region if the trigger no longer exists. Navigation, revoked access or removal of the target closes or safely invalidates stale actions without executing a highlighted choice.

INPUT-004 — Popups stay within the usable viewport, accounting for scale, scroll, window edges and the on-screen keyboard. Modals contain focus; nonmodal inspectors do not trap it. Enter activates an appropriate command, but must not override an explicitly non-applying field such as the model context draft. Focus, selected state, edit mode and hover remain distinct.

INPUT-005 — Dragging previews a destination and changes canonical data only on a valid drop. Escape/cancel restores the original position. Keyboard and visible move controls use the same operation. Moving a link or reference does not silently copy, delete or move the underlying artifact; expose that distinction in the command semantics.

INPUT-006 — Test targeting at viewport corners, multi-selection, target deletion, revoked permissions, long labels, scaling and IME composition. Validate nested shortcut dispatch and focus return after cancellation. These checks apply to the implemented command path, not only a static control tree.

<a id="h.y5h4ncg6aawg"></a>

## 8.3 Measuring and improving performance

PERF-BASE-001 — Establish a reproducible baseline before optimisation. Name the build/commit, release/debug mode, operating system, relevant runtime and driver versions, hardware, dataset, model/quantisation where applicable, context length, concurrent load and cold/warm cache state. Compare equivalent work and retain raw observations or a reproducible capture. Do not compare unrelated prompts or different quality settings as if they isolate one change.

PERF-BASE-002 — Measure user-visible work: application startup and restored-session readiness; navigation and input latency; frame behaviour under scrolling/streaming; large-document edit/save/reopen; search; peak and steady memory; CPU/GPU/I/O use; request admission; first output latency; model switching; throughput; cancellation and recovery. State exactly where a timer starts and ends. A server-side token rate does not measure the full user's waiting time.

PERF-BASE-003 — Record distributions and workload variation where they matter. Separate cold startup from warm repeat, queue time from execution, prompt processing from decoding and local saving from remote sync. Identify the sample count and measurement limitations. Small noisy differences are not reliable improvements. Numerical performance budgets are established from real baselines and an explicit product tradeoff, not invented as universal acceptance thresholds.

PERF-BASE-004 — Optimise the bottleneck established by evidence. Retain output quality, correctness, permissions, cancellation, durable state, accessibility and supported feature coverage. Report speed, resource consumption and quality together when a change trades one for another. A lower context size, cheaper model or disabled feature is a changed workload unless explicitly evaluated as a product option.

PERF-BASE-005 — Keep interface work responsive under inference and I/O load. Use bounded work, cancellation, backpressure and appropriate virtualisation without hiding state transitions or starving the active interaction. Coalesce visual updates where safe while retaining the underlying events. Do not animate or poll faster merely to create the appearance of progress. Measure the actual effect in the target environment.

PERF-BASE-006 — Reuse caches only with correct identity, revision, configuration, permission and freshness boundaries. Invalidate on relevant changes. Cache hits must not expose another user's data, apply stale grants, reuse a changed artifact silently or label outdated output as current. Test invalidation and offline recovery as well as the happy-path speedup.

PERF-BASE-007 — Provider usage limits, subscription allowances, context capacity, application task budgets and hardware resource budgets are different constraints. Obtain current availability through the appropriate adapter or service. Never hardcode a worker's current subscription quota or imply that another product's allowance is shared or separate without verified integration data.

PERF-BASE-008 — Rapid implementation and careful optimisation use the same reviewable contracts. The owner’s proposed Qwen/Astra division of labour is an execution preference: implementation workers provide changes and evidence; an independent reviewer examines architecture, regressions and before/after measurements. The product remains usable if either worker is unavailable. No model name is a substitute for validation.

<a id="h.cm297bwktglx"></a>

## 8.4 Validation and completion

VERIFY-001 — Keep design status and evidence status separate. A requirement may be specified and Atlas-covered while unimplemented. Source inspection may establish a code path without proving its runtime behaviour. Tests state their scope and environment; unavailable dependencies remain unavailable, rather than counted as a pass. The status of one screen or GPU mode does not generalise to the whole suite.

VERIFY-002 — A change is complete only when its observable acceptance criteria are met at the appropriate layers. Record exact build or artifact identity, setup, action, expected result, actual result and evidence location. Include regressions, failures and untested conditions. Link the new requirements and Screen IDs. Keep historical receipts immutable or append a clearly dated correction; never rewrite earlier evidence to claim a later success.

VERIFY-003 — Run focused checks for the concrete behavioural risks of the change. Compile affected source through the repository's current supported entry point; use integration tests for service/state boundaries and real interaction checks for geometry, focus and native behaviour. Document-only edits do not require unrelated SDK builds. Expand test scope to resolve a remaining risk or satisfy a real gate, not merely to repeat evidence.

VERIFY-004 — Automated structural and traceability checks verify IDs, links, routes and document consistency. They do not establish visual geometry, keyboard ergonomics, native browser integration, data durability, permission enforcement or inference performance. Use the actual rendered application and real service boundaries for those claims. Simulator/stub evidence is useful only when labelled and supplemented for the claim being made.

VERIFY-005 — Migrations require old-data fixtures, expected version transitions, interrupted migration recovery and a reopen check of the migrated artifact/preferences. Preserve a recoverable original or transactional rollback appropriate to the storage system. A successful migration must retain canonical identities and valid values; a renamed theme, route or provider must not silently create new objects or widen scope.

VERIFY-006 — Before calling an app conformant, cover the meaningful state matrix: default, selected, focused, editing/viewing, empty, loading, large data, offline, denied, failed, conflict, saving/saved and recovery. States can share tested primitives; record that reuse rather than copying tests mechanically. Include relevant hosted/standalone and narrow-screen forms. An intentionally unsupported operation is explicit and honest.

VERIFY-007 — Hardware acceptance belongs to a named topology and workload. In particular, enumeration of two devices, loading weights on both or running two unrelated prompts cannot by itself prove a single request executes cooperatively across them. Independent endpoints and distributed inference have different evidence. A cloud coordinator also needs an authenticated execution/recovery channel that actually survives the local process under test.

VERIFY-008 — Release or full-scope completion requires the affected suite contracts, preserved compatible capabilities and required migration/runtime evidence. Reviewable slices can be complete individually while the overall refactor remains incomplete. Report each boundary clearly. Do not mark the project finished because the specification is written, the screen index is complete or one build succeeds.

<a id="h.589h13rix2xr"></a>

## 8.5 Acceptance journeys

The following journeys connect shared contracts to concrete user outcomes. App-specific chapters add their specialised cases. The receiving worker records actual results; this document does not pre-fill a “passed” status.

AC-01 — Host continuity. Open native and web tabs, change the selected tab, edit an address draft, close the current and last tab, then restart. Verify the expected canonical destination, focus, app identity, preserved documents and independent draft/session preferences. Screens: host-newtab, host-browser, home-apps.

AC-02 — Distinct start and dashboard surfaces. Customise New Tab shortcuts/widgets and separately customise Home Dashboard briefing tiles. Reorder and resize through pointer and keyboard, navigate away and reopen; verify different LayoutIDs, preserved source artifact identities and no accidental cross-surface preference overwrite. Removing a pin from one surface does not delete its underlying artifact or change the other surface. Screens: home-dashboard, host-newtab, host-newtab-customise, host-newtab-widgets.

AC-03 — Picker and real configuration. Select distinct Main/Voice/Vision values; change role, search, dismiss and reopen. Enter invalid and valid pending context, dismiss without applying, then explicitly apply with a real reload success and failure. Verify truthful active state, draft retention, actual provider mapping and no unintended inference or tool grant. Screens: host-model-picker, home-model-details, settings-models.

AC-04 — Conversation admission and cancellation. Submit a prompt with attachments through accepted and rejected admission, queue/steer while running, stop, disconnect and reconnect. Verify one durable request, immutable submitted history, preserved drafts, real status and no duplicate effects. Screens: spaces-compose, spaces-thinking, spaces-running.

AC-05 — Delegated work. Start children with different outcomes, cancel or exhaust budget in one child, revoke a grant and reconnect. Verify inherited scope, stable parent/child identity, retained completed siblings and no undisclosed restart or broadened permissions. Screens: spaces-subagents, spaces-subagent-detail, spaces-permissions.

AC-06 — Canonical artifact journey. Create or generate an artifact, open it in its owning editor, edit it, review an AI proposal, commit a permitted revision, save, restart/reopen and export. Verify the same canonical identity and revision, valid output bytes and independent native-save/export status. Screens: spaces-generated-files, spaces-export-recovery, boards-editor and the relevant owning editor.

AC-07 — Boards integrity. Mix flow content, positioned objects and page ink; change Edit/View, layout lock and AI access separately; use undo/redo; save as an editable .9to1board; interrupt a save and recover. Verify the single authored page, unaffected ink, correct content and no accidental conversion to a freeform canvas. Screens: boards-editor, boards-ink, boards-properties, boards-recovery.

AC-08 — Permission and revision checks. Open a menu or AI change review, change its target revision or revoke permission, then attempt activation. Verify the operation is revalidated against the same target and any changed effect is reviewed. Nothing executes merely because a previous UI state was enabled. Screens: data-approval, terminal-command-review, spaces-permissions.

AC-09 — Offline and conflict. Save locally with the network unavailable; edit or receive a competing revision; reconnect and retry an uncertain operation. Verify recoverable versions, truthful local/sync state and no repeated external side effect. Screens: states, boards-recovery, spaces-export-recovery, files.

AC-10 — Settings and presentation. Navigate/search all settings scopes, change a canonical preference through two routes, and reopen. Switch all nine built-in themes, accent, high contrast, reduced motion and contextual chat placement while editing. Verify one setting value, correct persistence scope, usable controls and unchanged authored content. Screens: appearance, appearance-palette, custom-theme, settings-layout, settings-accessibility.

AC-11 — Input and accessibility. Complete the affected journey with keyboard, long labels, text scaling and the relevant touch/narrow form. Test IME composition, context-menu targeting, popup dismissal, focus restoration and one-dispatch shortcuts. Screens: components, mobile-chat, mobile-permissions and the affected app screens.

AC-12 — Development and agent definitions. Make a typed code/stack/agent change, preview or test it, inspect diagnostics, review a proposed diff and cancel/apply. Verify test success does not automatically activate an agent, publish a site or deploy a project; a failed preview labels its last valid output as stale. Screens: build-code, build-code-diagnostics, build-sites, build-stacks-freeze, ai-studio, ai-studio-permissions.

AC-13 — Model-host recovery. From a controller outside the inference process under test, attempt the declared model/topology configuration, exercise startup failure/cancellation and recover the last known good configuration. Verify the controller, checkpoint and host-recovery route survive unloading that endpoint, and another active worker is not unintentionally terminated. Screens: home-models-installed, home-model-details, home-mesh, states.

AC-14 — Performance comparison. Repeat a representative workload before and after an optimisation with consistent conditions. Record user-visible latency, resource use and quality/correctness; repeat the affected recovery path. A faster number is accepted only with its tradeoffs and preserved contracts made explicit. Screens: spaces-running, build-code-diagnostics, states.

AC-15 — Natural conversation continuity. Move between casual conversation, a study-and-document task, a substantial coding job and visual design. Verify automatic response-mode changes preserve conversation identity, tools, permissions, selected sources, task budgets and active work. Check personality precedence, grounded status summaries and real exposed trace/preview availability, including explicit user presentation preferences. Owner-directed new presentation; corresponding Atlas updates pending.

AC-16 — Training lifecycle. Enable Dreaming for a supported writable model, admit a scenario/review job only during eligible AI/device idle conditions, interrupt it with foreground work and resume from a valid checkpoint. Record a response preference, trace its eligible training use, reject a regressing candidate, promote a verified candidate under the configured policy and roll back. Prove actual candidate parameter changes and retained baseline; a feedback record or review score alone is not training. Detailed evidence and exclusions are owned by section 3.6.

AC-17 — Mobile and Projector continuity. Complete the floating bar, More, hosted/standalone and small-screen editing journeys in section 2.7, then launch an eligible Projector experience on a real target display, retain independent phone control and recover a disconnected workspace. Record actual capability, destination and session evidence. A mirrored phone screen or an enumerated display does not establish the full Projector experience.

<a id="h.cjdacjdrtk9a"></a>

## 8.6 Implementation snapshot and limits

At repository commit 1f92cd888f59025cc684f0720163efdbc580f8e3, the [existing inventory](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/development/refactor-2026-11-18/feature-inventory.md) locates shared host code under 9to1 Workspace/shared, CUI under framework/CUI, app projects under 9to1 Workspace and cloud services under cloud. It records a mixture of legacy and current composition paths. This is a route-finding aid, not a requirement to keep those directory names or legacy presentation technologies.

The [current commit](https://github.com/CroakyJake12/9to1/commit/1f92cd888f59025cc684f0720163efdbc580f8e3) records the legacy Haven picker's effort mapping being brought into agreement with its ReasoningScale. The Atlas's role picker remains the design authority. The inventory also records some native tab/New Tab interactions and identifies remaining gaps such as tab icons. These are dated repository claims, not an independent fresh test of the running app in this reconstruction.

No repository code, provider, service, GPU or native application has been changed or accepted by the act of rewriting this specification. Qwen's later documentation adoption and the implementation/refactor work require their own evidence and current operating context.
