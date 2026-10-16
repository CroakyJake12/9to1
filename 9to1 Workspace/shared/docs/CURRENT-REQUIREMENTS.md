# Current Standing Requirements

**Status:** current, binding.
**Provenance:** consolidated 2026-10-14 from the former `SECURITY_RULES.md`,
`ARCHITECTURE_RULES.md`, `PLATFORM_RULES.md` and `VALIDATION_RULES.md`, which were removed as
a conflicting rule *set*. Their **requirements are current and are retained here**; only the
superseded appearance authority (the Theme Modification Lock in the former `HAVEN_UI_RULES.md`)
was discarded, and it is deliberately **not** reinstated.

Naming follows the owner's 2026-10-14 consolidation: **9to1** (product), **CakeUI**
(HavenUI's successor presentation layer), **CUI** (the former HUI markup/runtime, which folds
into the CUI authoring contract). Internal assembly names are not yet renamed; see
`docs/handoff-20261014/NAMING-CONSOLIDATION-2026-10-14.md`.

**16 October 2026 source reconciliation:** the saved authoritative
[Development Specification V2](https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit)
now makes **Browse the primary application branded 9to1**. Merge the former main-shell
functions into its shared web/native app tab system. This supersedes conflicting shell
placement, navigation and delivery order only; every compatible requirement below and the
remaining full application scope remain binding. The target is 17–18 October 2026,
Europe/London; 9to1OS stays separately staged. Source revision and exact native paragraph
ranges are recorded as a current reconciliation in the existing source manifest. Historical
14 October source hashes and acceptance evidence are retained, not re-labelled as current.
Execution and acceptance tasks are tracked in
[the active task mirror](../../../docs/development/unified-host-tasks.md) and issue #35 under
master #21. A target date does not establish completion or acceptance.

**Subsequent direct owner clarification, 16 October 2026:** “standalone assistants depreciated and folded into spaces”. Assistants is a destination within Spaces; its standalone product is deprecated. This supersedes the older standalone-Assistants paragraphs in the saved V2 snapshot. Preserve existing Assistant identities, conversations, configuration and saved links. Retained legacy executable/route aliases are compatibility entries into Spaces, not another current app or required standalone package. This is a direct chat instruction; no claim is made that the Google document was updated.

---

## 1. Security, privacy and sandbox

- Destructive, external, privileged, paid, credentialed, ambiguous or privacy-sensitive actions
  use the central permission/approval engine. No UI, template, generated App or agent may
  bypass it.
- Restrict project/file/Git/build/test capabilities to Studio or an explicitly accepted
  workspace. Canonicalise paths and reject traversal before I/O.
- Keep secrets out of prompts, logs, screenshots, generated UI state, exports and source
  control. Store provider credentials through the approved secret store.
- Browser/model navigation remains limited by the existing public-network, redirect, credential
  and approval policies. Do not weaken private/internal target protections.
- Voice/Lesson Voice transient transcript/audio state is ephemeral unless the user explicitly
  saves it. Apply the same privacy classification to generated events and attachments.
- Background Learning and Library records retain provenance, trust, confidence, freshness,
  privacy classification, deletion and retention semantics.
- **Observed outcomes are mandatory.** Provider acknowledgement, process start, instruction
  dispatch or optimistic UI state is not proof an operation succeeded.
- Generated controls and imported templates are untrusted inputs until manifest, dependency,
  capability and integrity validation passes.
- Security checks may not be disabled to make a build or test pass.

## 2. Architecture and stable identity

- `Haven.Core` owns stable entities, enums, value objects and contracts. **Never renumber
  persisted enum values or recycle stable IDs.**
- `Haven.Application` owns use cases, orchestration, routing policy and capability contracts,
  free of Windows/Android/SQLite details.
- `Haven.Infrastructure` owns persistence, filesystem, model/provider, OS and external
  integration implementations.
- `Haven.Desktop` owns shared Avalonia presentation; `Haven.Android` owns Android
  lifetime/host/provider glue while reusing shared semantic UI.
- Apps are the major visible product architecture. `HavenMode` remains only the compatible
  persisted conversation/storage capability. Do not reintroduce a parallel Mode product
  architecture under another name.
- Built-in Apps keep their stable IDs in `BuiltInModeSeed`. Startup reconciles by stable ID;
  never recycle one.
- `MainView.LaunchAppAsync` and `HavenAppRoutePolicy` are the single launch-routing contract.
  Every enabled App must open a real working surface or an honest setup-required state.
  Adapt that contract into the unified Browse host without a second divergent registry.
  The existing `9to1://` app namespace must resolve to the actual native CUI app using real
  services and canonical resource identities. Keep web, Browse internal, generic CUI and
  privileged native routes distinct; native routes must never fall through to web search.
- The normal packaged 9to1 entry opens the unified Browse host. Compose shared Home views
  rather than nesting the entire former shell. Home has a stable Home-icon entry with
  Apps, Dashboard and Models/Discover sidebar views. New Tab shares those views and adds
  its own landing canvas. Preserve its widgets, Generative Areas, saved layouts and default
  Search with explicitly selected AI. Library, Events, Mesh, Settings, accounts and
  notifications remain reachable. Preserve configurable tab placement and the established
  groups, workspaces, splits and recovery model.
- Every actual in-scope app retains a working standalone executable sharing its embedded
  implementation, persistence, permissions and canonical resources/revisions. Spaces is
  an app; Assistants remains within Spaces, not a separate app for every Space mode.
  Standalone use must not require the visible main window or an open Home tab. Home Core,
  Dulche, accounts, permission brokerage, provider configuration and package authority each
  retain one canonical implementation with service ownership independent of views.
- Implementation defaults, unless a compatible approved detail already governs: app
  selection fills an empty New Tab; selection from Home opens an app tab retaining Home;
  the sidebar New Tab view selects the canvas; tab-strip + creates a tab. Sidebar order,
  initial selection and Home closability are implementation decisions, not newly attributed
  owner requirements.
- App-owned business state is authoritative. GenUI, templates and Floating Activities
  reflect/share it rather than creating disconnected copies.
- Persistence changes are forward-only. Back up and integrity-check before migration, keep
  fixtures for every prior schema, and preserve conversations, messages, projects, lessons,
  tasks, calls, Apps, identities, instructions, capabilities, settings and run history.
- Cancellation flows through asynchronous I/O; rapidly superseded UI results must be rejected.
- Do not create versioned source copies, handoff directories, duplicate page trees or ZIP
  deliverables unless the user asks.

**Applies directly to the naming consolidation:** renaming internal symbols is permitted;
renumbering enums, recycling stable IDs, or changing persisted keys is not.

## 3. Desktop and Android platform parity

- Preserve semantic capability parity across Windows and Android wherever the feature exists.
  Platform-specific providers/hosts are required for OS integration; fake cross-platform stubs
  are not completion.
- Shared CakeUI/CUI, App, GenUI, template, state and event contracts remain platform-neutral.
  Windows/Android implementation details stay behind interfaces/providers.
- Android Tasks support applicable commands/scripts, device actions and Intents through real
  Android providers and the same risk/permission policy.
- 9to1 Home is a real Android launcher/home activity, not an in-app imitation. Validate
  launcher selection, Home intent, app launch, lifecycle, cold start and device behaviour.
- Model residency uses OS-appropriate background ownership independent of the main UI: Windows
  tray/startup integration and Android ongoing-notification/background execution. Validate
  close/swipe-away and reboot restoration.
- Floating Activity hosts require real transparency on both supported platforms and honest
  fallbacks where an OS version cannot provide a capability.
- Required desktop and Android runtime, render, responsive, cold-start and reboot checks are
  not interchangeable. **An unavailable device/runtime yields `Unvalidated`, not `Passed`.**

## 4. Validation and evidence

Validation is proportional to affected risk and always includes more than compilation.

### Required evidence

- Restore/build/test Debug and Release for the affected projects, with 0 warnings/errors unless
  an explicit existing waiver applies.
- Focused tests first, then the full relevant suite. Add preservation/regression tests for
  fixed failures and migration fixtures for data changes.
- **Launch the exact route the user will use.** Verify real render and interaction; a compiled
  but crashing/clipped/wrong route fails.
- UI: desktop and Android sizes, all four appearances, theme/accent changes, responsive
  layout, keyboard/touch, accessibility, reduced motion and representative animations.
- Floating Activity: independent host plus real corner/background transparency on both
  platforms.
- Platform providers: real browser, microphone/audio, screen capture, notifications/tray,
  Android Intents/device actions, launcher lifecycle and model residency as applicable.
- Agent/tool changes: real file edit, command, vision and multi-step observe/repair smoke paths
  under permissions where applicable.
- Lifecycle: cold start, close/swipe-away, closed-UI background behaviour and reboot restoration
  where specified.
- Record exact command/configuration/platform, outcome, counts, logs/screenshots and
  unresolved limitations.
- Unified-host first journey: packaged 9to1 launch → Home → website → native app tab →
  meaningful operation → save → reopen the same resource through its standalone executable.
  Then exercise every applicable app and supported target, including profile/permission
  boundaries, unsaved work, active tasks, close/reopen/recovery and old-function reachability.
  One sample app, a component harness or successful compilation does not establish full
  application acceptance.

### Failure rules

- **Green compilation/tests prove only the paths they exercised.**
- Environment-limited required checks remain `Unvalidated`; they do not become success by
  inference.
- A provider acknowledgement is not an observed device/service outcome.
- If any applicable mandatory rule is knowingly broken, any required check fails, or any
  required runtime remains unvalidated, the task/pass is not successful and must be reported
  incomplete.
