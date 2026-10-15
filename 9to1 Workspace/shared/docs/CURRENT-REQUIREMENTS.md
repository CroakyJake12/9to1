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

### Failure rules

- **Green compilation/tests prove only the paths they exercised.**
- Environment-limited required checks remain `Unvalidated`; they do not become success by
  inference.
- A provider acknowledgement is not an observed device/service outcome.
- If any applicable mandatory rule is knowingly broken, any required check fails, or any
  required runtime remains unvalidated, the task/pass is not successful and must be reported
  incomplete.