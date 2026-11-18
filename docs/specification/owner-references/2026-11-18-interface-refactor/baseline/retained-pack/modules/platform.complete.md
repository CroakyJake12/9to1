# Platform, app registry and host integration

Module ID: `platform` · 9to1 Development Specification v3

## Prerequisites

- **Authority, scope and implementation reading map** — Current source precedence, narrow owner overrides, source accounting and the required module structure.

## Overview

The ecosystem shares canonical applications, artifacts and services across native, browser, mobile and 9to1OS hosts. Host presentation may differ while object identity, permissions and typed actions remain the same.

Current owner overrides: V3-OWNER-010.

## Layouts & visible states

- **V3-PLATFORM-LAY-01** — Expose authentic app launch/create/open/import/recents surfaces separately from active editor workspaces. A launch page must not remain as a decorative dashboard above an open document.
- **V3-PLATFORM-LAY-02** — Keep a familiar Home house icon in app headers. Respect host-safe top regions, viewport insets and narrow-screen drawer behaviour.
- **V3-PLATFORM-LAY-03** — Distinguish installed, available, offline, unsupported, starting, ready and failed states; do not make a static tile imply a working runtime.

## Features & behaviour

- **V3-PLATFORM-FEA-01** — Register app capabilities and routes once; Home, Browse, OS and Launcher use that registry. Opening an artifact resolves its registered owner and stable target, including embedded AppSurfaces.
- **V3-PLATFORM-FEA-02** — Offer manual local editing without requiring sign-in, a connected Files provider or a running AI service unless the underlying operation itself needs it.
- **V3-PLATFORM-FEA-03** — Preserve the retained platform/backend language, donor reuse and UI/API parity requirements. CUI wraps existing powerful engines rather than replacing them with a reduced demonstration.
- **V3-PLATFORM-FEA-04** — Integrate typed creation, selection, edits, exports, task events and deep links. Automation uses these semantic APIs, not simulated pointer interaction.
- **V3-PLATFORM-FEA-05** — Native documents/projects must not impose arbitrary global character, word, page, slide, block, object or file-byte caps merely to simplify rendering or saves. Real device, quota, format, address-space and security limits remain explicit; never truncate silently.
- **V3-PLATFORM-FEA-06** — Prioritise current input, caret, selection, scroll, drag and undo over distant work. Virtualise visible/nearby pages, slides, cells, blocks, nodes and time ranges, use indexed deep navigation and bounded adaptive caches, and keep unrendered content canonical, searchable, accessible and exportable.
- **V3-PLATFORM-FEA-07** — Use incremental dependency-aware layout/calculation and addressable/chunked persistence; do not reserialise or rerender an entire large artifact on each keystroke. Stream/resume/cancel large import/export with integrity checks, honest progress and safe resource-pressure recovery. Lower preview fidelity or buffers before document fidelity.

## Canonical entities & persistence

- **V3-PLATFORM-ENT-01** — Keep stable AppID, artifact ID, TabID, route, revision and request identities. Do not duplicate documents when moving between native, embedded and browser views.
- **V3-PLATFORM-ENT-02** — Draft, active configuration, current view state and published output are separate records. Closing a view does not implicitly cancel an admitted background Task/Run.

## Permissions & recovery

- **V3-PLATFORM-PER-01** — Check permissions at execution and dereference time, including cached/search and embedded surfaces. Explicitly distinguish unavailable capabilities from denied permissions.
- **V3-PLATFORM-PER-02** — Preserve unsaved work through host navigation, process restart and session restore; Save/Discard/Cancel must refer to actual dirty state.

## Acceptance checks

- **V3-PLATFORM-ACC-01** — Open the same artifact through Home, Files, a host tab and a Space; verify stable identity, current revision and consistent grants.
- **V3-PLATFORM-ACC-02** — Test app launch→create/open→edit→save→reopen, narrow layout, offline recovery and unsupported-platform states.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

APP-AC-001, APP-AC-002, APP-AC-003, APP-AC-004, APP-AC-005, APP-AC-006, APP-AC-007, APP-AC-008, APP-AC-009, LAUNCH-001, LAUNCH-002, LAUNCH-003, LAUNCH-004, LAUNCH-005, LAUNCH-006, TF-X01, TF-X02, TF-X03, TF-X04, TF-X05, LE-12, LE-13, LE-14, UX27-OTHER-018, SECTION-13, UX27-TEST-100, UX27-SHARED-020, UX27-ACCEPT-005, UX27-TEST-101, UX27-MAIN-001, UX27-MAIN-002, UX27-MAIN-003, UX27-ACCEPT-001, UX27-ACCEPT-002, UX27-ACCEPT-004, UX27-TEST-102, UX27-AUTH-003, UX27-AUTH-006, UX27-COVERAGE-001, UX27-COVERAGE-002, UX27-COVERAGE-004, UX27-ACCEPT-003, UX27-ACCEPT-006, SCALE-001, SCALE-002, SCALE-003, SCALE-004, SCALE-005, SCALE-006, SCALE-007, SCALE-008, SCALE-010, SCALE-011, OTHER-APPS-SCALE-201, SCALE-AC-201, SCALE-AC-202, SCALE-009

## Technical reading map

- Platform, app registry and host integration — General Rules; Specification authority (part 01) — `technical/platform-01.md` (2,116 words; V2-B0093, V2-B0094, V2-B0095…).
- Platform, app registry and host integration — Launch Screens; Distinguish screen purposes (part 02) — `technical/platform-02.md` (1,517 words; V2-B0325, V2-B0326, V2-B0327…).
- Platform, app registry and host integration — Cross-app delivery and acceptance; Embeds, Boards pages and conversational editing (part 03) — `technical/platform-03.md` (2,400 words; V2-B2170, V2-B2246, V2-B2283…).
- Platform, app registry and host integration — Integration; Large board keeps every block and stroke (part 04) — `technical/platform-04.md` (1,192 words; V2-B2338, V2-B2900, V2-B2901…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Platform, app registry and host integration — General Rules; Specification authority (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0093 · v2 lines 284–292

General Rules
Specification authority
This Google Doc is the canonical authority for 9to1 product behaviour, feature requirements, donor parity, storage rules, UX semantics and unresolved design decisions. Trello is the roadmap/index layer only: product cards MUST remain concise and link to the relevant canonical section rather than duplicate its full specification. If Trello and this document disagree, this document wins unless a later explicit decision explicitly updates the canonical requirement.
Technical specification contract
Normative requirement language
This document is an engineering contract. The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY and OPTIONAL are normative requirement levels, not conversational emphasis.
* MUST / MUST NOT — mandatory for conformance. An implementation that violates the requirement is non-conformant unless this document contains an explicit scoped exception.
* SHOULD / SHOULD NOT — required by default. A deviation is permitted only when a concrete technical constraint makes the requirement unsuitable for that exact case; the implementation MUST record the exception, reason, affected scope, fallback behaviour and validation evidence.
* MAY / OPTIONAL — intentionally optional. Omitting the behaviour is conformant unless another requirement makes it mandatory in a specific context.

### V2-B0094 · v2 lines 295–295

Lowercase uses of “must”, “should” and “may” have the same normative meaning. Product prose MUST NOT use “should” merely to mean “it would be nice if”.

### V2-B0095 · v2 lines 298–298

Terms such as “where appropriate”, “where practical”, “where feasible”, “as needed”, “etc.”, “and more”, “similar”, “sensible” or “useful” MUST NOT be used as an implementation escape hatch. If such wording remains in an older paragraph, the implementer MUST interpret it in favour of feature completeness and donor parity unless a precise condition or explicit exception narrows it.

### V2-B0096 · v2 lines 301–314

Required technical structure for product specifications
Every shipping app, framework and system component MUST eventually define the following information in its canonical section. A section that lacks required fields is incomplete specification, not permission for the implementer to invent behaviour.
* Purpose and boundary — what the component owns, what it explicitly does not own, and which neighbouring component owns excluded responsibilities.
* Authoritative data/state — canonical entities, stable identifiers, ownership, lifecycle states, persistence location, versioning and migration rules.
* Donor/upstream contract — donor repository/source, pinned or selected version policy, licence obligations, required parity surface, allowed replacements and documented exclusions.
* Supported platforms and capability matrix — Windows, 9to1-OS, web and other supported surfaces, including exact differences rather than silent feature loss.
* User-visible operations — exhaustive operations the UI exposes, including create/read/update/delete, import/export, collaboration, recovery, settings and destructive operations.
* Automation/API surface — canonical namespace, action names, argument schemas, result schemas, stable IDs, pagination/streaming where applicable, idempotency semantics, error contracts, permission scopes and risk classification.
* Permissions and security — caller identity, authorisation boundary, high-risk operations, secrets, external side effects, confirmation requirements and audit events.
* Cross-app integration — exact owning app, reference/embed semantics, canonical-object behaviour and how actions route across app boundaries.
* Failure and recovery — expected failures, partial-success behaviour, retry rules, rollback/restore, crash recovery and data-integrity guarantees.
* Performance/resource constraints — limits that affect correctness or UX, including memory/storage/compute ownership where relevant.
* Acceptance criteria — deterministic tests or observable behaviours that prove the requirement is implemented.
* Open decisions — unresolved design questions explicitly labelled OPEN. OPEN items MUST NOT be silently resolved by an implementation agent when the choice could change public behaviour, data compatibility, security or architecture.

### V2-B0097 · v2 lines 317–318

State and data invariants
Persistent objects MUST use stable identifiers that do not depend on display names, UI order or mutable paths unless the object is explicitly path-addressed by design. Display names MAY be duplicated unless the product section explicitly requires uniqueness.

### V2-B0098 · v2 lines 321–321

Mutations MUST be atomic at the smallest user-visible operation boundary unless the operation explicitly declares partial-success semantics. When a multi-object operation can partially succeed, the result MUST identify every succeeded, failed, skipped and rolled-back item.

### V2-B0099 · v2 lines 324–324

Destructive operations MUST define whether deletion is soft, recoverable, retained, permanent or externally propagated. If the section does not define permanent deletion semantics, implementations MUST prefer recoverable deletion and MUST NOT silently destroy the only copy of user data.

### V2-B0100 · v2 lines 327–327

Persisted formats MUST be versioned. Readers MUST reject, migrate or preserve unknown incompatible schema versions explicitly; they MUST NOT reinterpret unknown data using a guessed schema.

### V2-B0101 · v2 lines 330–330

Where two systems can mutate the same canonical entity, the specification MUST define authority, synchronisation direction, conflict detection and conflict resolution. Last-write-wins MUST NOT be assumed unless explicitly specified.

### V2-B0102 · v2 lines 333–334

API and error contract
UI and automation MUST call the same domain operations wherever technically possible. An app MUST NOT maintain one hidden behavioural implementation for UI actions and a different incompatible implementation for API actions.

### V2-B0103 · v2 lines 337–337

Every public API action MUST return a structured success/error result. Expected operational failures MUST NOT be represented only by free-form human text. Error results MUST include a stable machine-readable code, human-readable message, target/action identity and relevant recoverability/retry metadata.

### V2-B0104 · v2 lines 340–340

APIs that enumerate unbounded collections MUST define pagination or streaming. APIs that can produce long-running work MUST expose request/job identity, status and cancellation. APIs that retry or can be replayed MUST define idempotency semantics so an interrupted client does not accidentally duplicate consequential mutations.

### V2-B0105 · v2 lines 343–343

Unknown, unavailable and empty values MUST remain distinguishable. A value MUST NOT be replaced with zero, false, an empty string or an invented default merely because the provider/backend cannot supply it. Component-specific contracts such as Dulche’s null/Na distinction override this general rule where defined.

### V2-B0106 · v2 lines 346–347

Unspecified behaviour and exceptions
When required behaviour is genuinely unspecified, an implementation agent MUST first determine whether the decision is observable, destructive, security-sensitive, data-format-affecting or architecture-defining. If yes, it MUST preserve existing data/state, avoid irreversible action and record the item as OPEN rather than guessing.

### V2-B0107 · v2 lines 350–350

For non-observable implementation details, the implementer MAY choose a conventional maintainable design provided it does not alter the specified public behaviour, data compatibility, API contract, permissions, donor parity or future extensibility.

### V2-B0108 · v2 lines 353–353

A specification exception MUST identify: requirement ID or quoted requirement, affected product/version/platform, technical reason, exact substitute behaviour, user-visible impact, security/data impact, tests/evidence and whether the exception is temporary or permanent. “Too difficult”, “not enough time”, “donor source unavailable” or “not implemented yet” are not valid permanent exceptions.

### V2-B0109 · v2 lines 356–367

Application-level acceptance and anti-substitution contract
APP-AC-001 — Exhaustive scoped manifest. Before implementation/acceptance, map every applicable requirement and donor capability to its app, Part, RCVP/Full stage, platform, real user entry point, owning engine/API, persistence path and verification evidence. Readiness is evaluated against that manifest, not a hand-picked subset of convenient tests. Minimum examples are a floor, not the complete manifest. An absent feature or unexecuted test remains missing/NOT RUN; it is not silently NOT APPLICABLE.
APP-AC-002 — Seven mandatory gates. Each app and substantial user journey must pass Usability; Product fit; Functionality; Integration; Separation and authenticity; User fit; and Refinement. Significant failure of any gate blocks acceptance; a numerical average cannot compensate. Integration includes discoverable navigation, the actual CUI surface, canonical providers and end-to-end artifact flow. Separation and authenticity prohibit developer diagnostics, fixtures and sample success states from impersonating ordinary product behaviour. For interface-free work, record why visual criteria do not apply rather than claim visual testing.
APP-AC-003 — Real product, real artifact. Acceptance launches the actual app/package/route built from the identified source and dependencies, with normal startup, Home/runtime integration and authentic providers. Exercise meaningful multi-step workflows and independently inspect the resulting saved/exported artifact. A parser demo, single text box, disconnected command grid, screenshot mockup, unit-test host or separate demonstration app is never app acceptance. Opening the upstream donor's unadapted UI or external executable is not CUI product integration. Necessary donor engines remain reusable behind typed CUI adapters.
APP-AC-004 — No scope laundering. A working vertical slice is a CHECKPOINT, not an RCVP-complete or Full-complete app. A declared RCVP requires all applicable donor baseline functionality through the real product; new enhancements may be deferred only as the active four-part plan specifies. Existing data, donor behaviours, accessibility, security, durability and essential CUI/user workflows must not be removed to simplify a refactor. Exceptions need explicit owner approval and the scoped evidence required by this specification; an agent-created waiver, hidden menu, disabled command or renamed task does not satisfy the omitted requirement.
APP-AC-005 — Fast prevention before repetition. Establish reusable CUI shell, navigation, toolbar, menu, dialog, inspector and state components. Before repeating a layout across apps, launch one complete representative journey and inspect its hierarchy, work-surface proportions, responsive behaviour and accessibility. Every changed user-facing journey receives focused runtime/visual smoke; shared language/theme/component changes invalidate affected downstream evidence. Reuse unchanged exact-source evidence and cached changed-graph checks; run broader donor/native/web regressions at integration and release gates. Do not impose a whole-suite run or manual approval after every tiny edit, and do not defer all visual review to release.
APP-AC-006 — Independent evidence. A reviewer other than the implementing agent must inspect the exact runnable artifact and independently execute representative workflows, check the exhaustive manifest and challenge product-category and donor-parity gaps. Preserve source/build/artifact hashes, platform, theme/viewport, test steps, observed results, screenshots and representative output artifacts. Source inspection, implementing-agent assertions, fixture-only tests and copied evidence from another build cannot substitute for current relevant runtime evidence. Without an available independent reviewer, record independent acceptance as NOT RUN, not VERIFIED.
APP-AC-007 — Truthful states and blockers. Distinguish engineering status, app RCVP acceptance, app Full acceptance and native/web platform acceptance. Record FAIL, BLOCKED and NOT RUN explicitly. A missing desktop/browser environment is an environmental blocker, not proof of product failure or success. Continue safe unblocked work, but do not report an untested platform or missing mandatory workflow as complete. Tests must not be weakened, skipped or rewritten merely to accept a deficient implementation; update obsolete tests only to enforce the canonical specification and retain regression coverage.
APP-AC-008 — Home and shared-shell rejection cases. Home must present the specified useful dashboard/briefing and app navigation, not an administrator/test panel. Keep tile-management controls in the dedicated Customise experience. Demonstrate normal, offline/local-only, empty and Customise states with real state transitions. Cloud/auth failures remain scoped and actionable; they must not overwhelm or disable unrelated available local workflows. Reject truncated hero/content, malformed grids, duplicate technical errors, inaccessible disabled controls, overlapping AI bars and hidden primary actions. Preserve all specified Home lifecycle, package, permission, account and dashboard functionality; these examples do not replace its complete acceptance manifest.
APP-AC-009 — Screen identity and transition integrity. Acceptance MUST verify that specification-defined distinct experiences exist as actual independently composed, correctly activated views, not just headings or stacked sections in one workspace. For every required launch/home screen, LAUNCH-001–006 are mandatory. The reviewer must inspect the rendered start and working states, real transitions, active commands, focus/accessibility scope and preserved session state in the shipping app. The presence of launch-related labels, separate component files or route declarations is insufficient. A welcome/header/Recent section above the active editor fails even when both sets of controls work. Review other explicitly distinct product experiences for the same flattening defect while preserving intentionally embedded panels, split views, backstage behaviour and shared shell components. Missing visual/runtime evidence remains NOT RUN and must not be promoted to VERIFIED.
Definition of implementation-complete
A component is implementation-complete only when all mandatory behaviour in its canonical section is present and validated. Source code existing, a successful build, a rendered shell, placeholder controls or an API stub do not satisfy a behavioural requirement.

### V2-B0110 · v2 lines 370–370

Completion evidence MUST cover representative end-to-end journeys, persistence/restart behaviour, API/UI parity, permissions, accessibility, failure/recovery paths, donor parity where applicable and platform/package validation for every declared supported platform.

### V2-B0111 · v2 lines 373–373

Known failing requirements MUST remain explicitly recorded as open blockers or scoped exceptions. They MUST NOT be omitted from release/completion reporting.

### V2-B0305 · v2 lines 1727–1728

Apps
Home runtime dependency

### V2-B0306 · v2 lines 1731–1731

Every installable/native 9to1 app has a hard runtime dependency on 9-1 Home. Before initialising the app’s normal UI or opening user artifacts, the app MUST complete a local Home availability/version handshake against the Home-managed shared services it requires.

### V2-B0307 · v2 lines 1734–1734

If Home is not installed, the app MUST refuse normal startup and display only a minimal bootstrap surface explaining that 9-1 Home is required. The bootstrap surface MUST provide an Install Home action that opens/starts the trusted Home installation flow. It MUST NOT create or mutate normal app data while Home is absent.

### V2-B0308 · v2 lines 1737–1737

If Home is installed but the required shared service/API version is missing, incompatible or damaged, the app MUST refuse unsafe normal startup and offer the appropriate Home update/repair flow with a structured reason. Apps MUST NOT silently launch private replacement services to bypass the missing Home dependency.

### V2-B0309 · v2 lines 1740–1740

This local-install dependency applies to native/installable clients. Browser-hosted web surfaces cannot require a local Home installation; they instead use the corresponding authenticated 9to1 shared web services and capability negotiation defined for web clients.

### V2-B0310 · v2 lines 1743–1743

Home owns shared platform services including the permission broker, shared Dulche broker/runtime coordination, package/update metadata and the Shared Productivity Engine defined in the Home section.

### V2-B0311 · v2 lines 1746–1746

Persistent contextual AI bar


# Platform, app registry and host integration — Launch Screens; Distinguish screen purposes (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0325 · v2 lines 1791–1796

A model may cross an app boundary only when that action is authorised by user intent: explicit @ invocation, an explicit instruction naming the target capability/app, or an unambiguous requested outcome whose canonical owner is another 9to1 app. Implied intent is semantic, not opportunistic; unrelated app access remains out of scope.
When a target 9to1 app owns the requested artifact/action, the originating model MUST use the target app's typed API/shared semantic action contract and MAY ask Home to launch/reveal the target app so the user can observe progress. For example, a request to create a presentation routes to Present and creates/edits a canonical Present artifact rather than fabricating a slide-like object in the originating app.
Computer Use MUST NOT be selected automatically as a fallback when typed app execution is unavailable, inconvenient or fails. It is usable only after explicit @Computer Use invocation and remains subject to the Computer Use eligibility, permission, game-exclusion and Autoreview rules.
Model-driven typed edits MAY be visualised in the target app with live object motion, selection highlights, text insertion and a synthetic/virtual cursor. This visualisation is not evidence that Dulche is literally controlling the pointer; the typed action/result remains authoritative and the synthetic cursor MUST be visually distinguishable from the user's actual input cursor.
Launch Screens
A launch/home screen is the app’s distinct start experience: the equivalent of the modern Microsoft Word start page shown in the reference image, not simply an empty editor or project view, nor a header, banner or introductory section above the working app. It appears when the app is opened without an existing artifact/session to restore, and whenever the user deliberately returns to the app’s Home/start surface. The following requirements clarify the existing launch-screen contract; they do not add an unrelated startup delay, alter the four-part delivery plan or replace any product-specific capability.

### V2-B0326 · v2 lines 1799–1804

LAUNCH-001 — Distinguish screen purposes. An app’s launch/home screen is its functional starting place for choosing or creating work; it is not the platform Home dashboard. A temporary startup/loading splash, first-run onboarding, launch/home, backstage/file menu and active editor/workspace are different experiences and MUST retain their specified purposes. A loading logo or progress page does not satisfy the launch-screen requirement. Do not invent extra screens where none are specified or turn every toolbar, inspector or embedded panel into a separate page. Preserve the existing first-run and backstage contracts.
LAUNCH-002 — Independent composition and app identity. Each required launch screen MUST have its own purposeful layout, hierarchy, recognisable app identity and the real app-specific entry points listed below or in its product section. Its primary task is choosing/creating/opening work; the editor’s primary task is working on the active artifact. Shared CUI themes, shell elements and reusable launch components are encouraged, but changing only the heading/logo on an otherwise unchanged editor or generic command dashboard is insufficient. A hero, Recent strip, welcome card or collapsible introduction attached above the editor is not a launch screen. The start experience must be composed for the available work region, not merely reserve a top strip while the unused editor remains below it.
LAUNCH-003 — Explicit active-view ownership. Model launch/home and the active workspace as distinct CUI view states with explicit navigation and activation. They may reuse the same window, app shell, engine and canonical session; separate processes or OS windows are not required. Within the same active work region, show and activate the appropriate experience rather than vertically stacking both. An inactive editor must not occupy launch layout space or receive pointer input, document shortcuts, keyboard focus or accessibility navigation; inactive launch-only controls must likewise not remain attached to the editor. Hiding a screen by opacity, covering it with a banner or scrolling it off-screen while it remains interactive is failure. Shared chrome explicitly required by the specification may remain. Separate windows/tabs and intentionally specified embedded editors retain their own active-view scopes.
LAUNCH-004 — Real transitions without data loss. Create, Open, Import and applicable template/recent/shared actions MUST establish or resolve the real authorised canonical artifact/session and transition to its functioning workspace, not just change the page heading or reveal an already-stacked test editor. Cancellation or failure leaves a coherent recoverable view with an actionable error and does not fabricate successful entry or discard existing work. Direct artifact opening and supported session restoration go to the requested/restored workspace without forcing a detour through the launch screen. Deliberate return to the app’s start surface and resumption of work preserve canonical identity, unsaved content, undo history and applicable selection/view/session state according to the existing lifecycle and recovery contracts. Navigation alone is not permission to close, delete, reset or silently save over content. A genuine close/discard operation retains its existing confirmation and durability rules. Separating views does not require destroying the retained engine/session, adding extra clicks to direct-open flows or imposing artificial splash delays.
LAUNCH-005 — Shared implementation, complete coverage. Implement reusable canonical CUI navigation, activation and launch composition through the existing shared runtime, with each app binding its own real storage/model, capabilities and identity. Do not create a competing navigation framework or duplicate document stores. Inventory every application for which any specification section requires a launch/home screen, including applicable native and web counterparts, and verify its real shipping entry point. Reuse must preserve product-specific workflows, rather than flattening every app into the same dashboard. A separate source file, route name, boolean flag or standalone sample is not evidence that the actual product presents distinct screens. This is a required product-boundary correction at each applicable RCVP/Full stage, not optional visual polish.
LAUNCH-006 — Runtime and transition acceptance. Independently inspect the exact launched app/artifact under APP-AC. Cover launch without a restorable artifact/session; real empty and populated start states; applicable Create/Open/Import/template/recent actions into the workspace; direct artifact opening; supported restoration; deliberate return to start and resumption; and cancelled/failed entry. Capture start-screen and workspace screenshots separately and inspect the transitions, active command scope, pointer handling, focus order and accessibility state. Verify that document shortcuts cannot modify a retained inactive document while the user types or navigates on the launch screen, and that returning preserves the required content/session state. Test the declared supported window sizes, themes and native/web surfaces through the existing focused-smoke and release matrices. Include a regression rejection case for launch content stacked above an editor, including an editor reachable only by scrolling or invisible but still interactive. Merely finding expected labels/buttons or passing a route/unit test does not pass. Missing screen identity, an active-view leak, data loss or a header masquerading as a launch page blocks the relevant application acceptance.

### V2-B0327 · v2 lines 1807–1807

For every app listed below, the launch screen MUST implement each capability in the following list when that capability exists for the product. If a capability is not applicable, the product section MUST explicitly state the exclusion and reason rather than silently omit it:

### V2-B0328 · v2 lines 1810–1816

* Create a blank document/project/artifact.
* Templates, starter projects and a way to browse more templates where the app supports them.
* Recent items, with useful location/path and modified-state information.
* Favourites/pinned items.
* Shared with Me or equivalent collaborative content where applicable.
* Search across the app’s relevant files/projects.
* Open/import entry points for existing files/projects and supported external formats.

### V2-B0329 · v2 lines 1819–1819

The launch screen must use CUI and the app’s real storage/project model. Recent, favourite, shared and template entries must be functional data-backed surfaces rather than static mock content. Apps may add app-specific quick actions, onboarding/tours or account/storage indicators where useful.

### V2-B0330 · v2 lines 1822–1831

The following apps MUST include a launch/home screen conforming to LAUNCH-001–006. The same contract also applies wherever another product section requires a launch/home screen, including later additions to the application roster; this list is not an exemption for those products:
* Write
* Present
* Data
* Canvas
* Boards
* Picture
* Wave
* Motion
* Forms

### V2-B0331 · v2 lines 1834–1836

* AI Studio
Web Apps
Cakemods.com MUST host a web version of every 9-1 Workspace/AI product that is not explicitly listed as web-exempt. Web surfaces MUST share the same CAKE ID identity, canonical artifact formats, domain APIs, permission model and underlying server-side entities as desktop equivalents. Browser-specific UI/layout MAY differ, but it MUST NOT create a separate incompatible product model.

### V2-B0332 · v2 lines 1841–1841

The default rule is to provide a web version whenever the product’s core value can sensibly work in a browser. Browser-specific capability limits are acceptable when they are explicit and the useful core product remains intact; a single desktop-only feature is not by itself a reason to omit an otherwise useful web app.

### V2-B0333 · v2 lines 1844–1844

Web exemption is permitted only when the product’s core function requires infrastructure, privileged device access or hosted compute that CAKE does not currently provide. Each exemption MUST state the blocking capability/infrastructure. Exemptions MUST be re-evaluated when that blocker is removed; exemption is not a permanent product classification.

### V2-B0334 · v2 lines 1849–1851

Current exemption:
* Mini Computer — a useful web version would require hosted virtualisation/server compute that CAKE does not currently have capacity to provide.
* Browser - Having a browser inside a browser is silly.

### V2-B0335 · v2 lines 1854–1854

Mini Computer may gain a hosted web mode in the future as a 9to1 subscription capability if sufficient server compute is available. The same principle applies to future apps whose meaningful web implementation requires paid hosted compute.


# Platform, app registry and host integration — Cross-app delivery and acceptance; Embeds, Boards pages and conversational editing (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2170 · v2 lines 12727–12752

Cross-app delivery and acceptance
TF-X01 — Embeds, Boards pages and conversational editing
Boards MUST be able to include a Flow or Binder as a page referencing the same canonical Link document. It MUST also support a Files Sheet as a live Files-owned organisation page, rather than mislabelling Sheet as a Link type. The same registered embedding contract applies to the other declared compatible organisation/artifact surfaces. The host page MUST identify the owning source, respect reader/editor capability, provide Open original and preserve navigation back to the Board.
A Binder or Flow placed on a Sheet MUST open through the Sheet's chosen policy while keeping the surrounding notes/files and viewport. A Stash on a Sheet remains an ordered group. A Prototype embedded in Link, Boards or a Path MUST retain its actual model and permitted session-local controls, not become a screenshot while being described as interactive. Replay checkpoint views remain historical. Every supported combination MUST reference canonical entities; unsupported combinations show their exact limitation instead of silently flattening or cloning.
Spaces and the persistent AI bar MUST use the same typed APIs as the UI for authorised creation, organisation and editing. The current artifact, selection, source scope, active variant and permissions MUST be supplied explicitly. AI suggestions that change organisation rules, dependencies, route logic or other significant behaviour must be inspectable and undoable. AI access remains a separate permission; opening a private file in an embed does not authorise cloud upload. Core manual workflows MUST remain usable when AI is disabled or unavailable.
TF-X02 — Canonical API action catalogue
New UI operations extend the existing canonical namespaces: 9to1.Files.Organisation, 9to1.Link, 9to1.Prototype, 9to1.Paths and 9to1.Replay. Variants actions live under the owning app's Variants capability, such as 9to1.Write.Variants; the shared implementation MUST not bypass that owner's semantics. Existing Files, shared-object and NodeGraph actions remain authoritative for their existing operations rather than being copied into incompatible new APIs.
Every mutation request MUST include the target stable IDs, typed payload, operation/idempotency key and expected revision, or an explicitly declared create precondition. Successful results identify affected objects, resulting revision and any job/change-event identity. Read/list operations accept explicit scope and filters and return bounded pages with continuation cursors. Long-running query, capture, evaluation, export and merge work exposes JobID, progress, cancellation and durable result/error. Events have stable IDs and resumable cursors. Home resolves caller identity, scopes and risk before dispatch; possession of an ID never grants access.
Files.Organisation MUST expose Create/Get/List, UpdateSettings, AddReference, RemoveReference, Reorder and the type-specific operations: Project section CRUD and assignment; Sheet object/geometry/ink/shape/connector edits and opening policy; Tree group/node/connection edits plus separately authorised Tree-layout reads; Smart rule/scope/pin updates and membership explanation; Timeline date-source/assigned-range/period edits; Pipeline stage/gate/approval/transition/mapping actions; Catalogue field/schema/local-value/view edits; Stash order/position/review/compare selection; and Matrix axis/cell/membership operations. Generic command payloads MUST have registered typed schemas, not arbitrary unvalidated JSON.
Link MUST expose document creation and retrieval, block/container CRUD, ordering/geometry, source/section/revision-policy updates, outline/search, preview and export/import. Source edits dispatch to the source owner's registered actions. Prototype MUST expose object/property/component CRUD, formula/binding/relationship/event/action updates, validation, evaluation/run/stop/reset and session-input changes. Persisting runtime values as defaults is a separate authoring action.
Paths MUST expose step/route/content CRUD, validation, preview/test sessions, authored revision commit, StartAttempt, GetProgress, SubmitResponse, Advance, checkpoint/resume, Restart and confirmed progress deletion. Reader-scoped progress actions MUST not allow changing another reader's attempt or the authored route. Replay MUST expose source capability inspection, capture creation/start/pause/resume/stop, scope changes, checkpoints/annotations, event/checkpoint retrieval, historical preview, independent-copy requests, review/redaction and export/delete. Variant APIs MUST expose branch create/list/get/rename/switch state, compare, merge preview, resolution, commit and archive/recover through each required owning app.
Action schemas MUST declare required target permissions, source permissions, reversible/destructive risk, external effects, limits and partial-success behaviour. Required structured errors include InvalidArgument, ItemNotFound, PermissionRequired, PermissionDenied, RevisionConflict, UnsupportedCapability, ProviderUnavailable, Offline, QuotaExceeded, MissingSource, MissingSourceSection, InvalidReferenceCycle, TypeMismatch, ValidationFailed, DependencyUnavailable, TransitionBlocked, CaptureIncomplete, MergeConflict and Cancelled, using existing canonical equivalents where already defined. Errors identify recoverability and the affected operation without leaking forbidden source details. Every action exposed by a context menu, keyboard command or editor control MUST be accounted for in the generated UI/API parity manifest.
TF-X03 — Platforms, responsive behaviour and resource limits
Windows, 9to1-OS and browser/web MUST implement the useful core authoring and reader experiences for this tranche using the same canonical formats, account identity, domain APIs and permissions. Native interfaces use CUI/Home; web uses the existing authenticated web-service contract and MUST not require arbitrary local filesystem/process access. A narrow viewport by itself is not a reason to replace an otherwise supported app with an unrelated simplified data model.
The operating contract's Android/Go timing exception remains limited to newly added complex Terra-form surfaces. Any temporary read-only/limited-editing mode MUST still provide source-aware navigation, faithful content, accessible controls, per-user reading/progress state where supported and a visible explanation of unavailable authoring. Tree viewers still receive only View. No disabled authoring control may pretend to save. Full Android/Go authoring parity remains required after the temporary fallback; existing stronger mobile functionality MUST not regress.
At narrow sizes, navigation and inspectors become drawers or dedicated subviews, command overflow remains discoverable and pop-ups may use full-screen routes with preserved Back state. Flow retains continuous reading; Binder retains outline and open/close exploration; Files collections retain meaningful contents views; Paths retains choices and progress; Replay retains seeking/checkpoint inspection; Variants retains source/target identity in sequential comparison. Complex spatial authoring MUST provide fit and accessible structural alternatives, not an unreadable scaled-down desktop screenshot.
Large collections/graphs/documents MUST use bounded incremental loading, preview hydration, virtualisation and subscription cleanup. Closed/off-screen embeds MUST not continue uncontrolled computation, media playback or capture. Source resolution and exports MUST detect recursion and bound dependency traversal. Query, evaluation, capture, import/export and merge limits MUST be declared by the relevant capability and shown before an operation that exceeds them; limits MUST not silently truncate content. Existing platform-wide performance/resource thresholds remain mandatory. Actual supported workload sizes and measured results MUST be recorded rather than invented completion claims.
TF-X04 — Acceptance matrix and release evidence
Every numbered Terra-form requirement group MUST have a traceable implementation and test mapping. Evidence MUST distinguish source/unit coverage from real UI, runtime, cross-app, provider and package validation. The per-feature acceptance journeys above are mandatory minimums, not optional demonstrations. Test both ordinary authoring and reader roles, source permissions independent of host permissions, empty/loading/error states, source move/rename/delete, undo/redo, reopen, offline recovery and concurrent revisions.
The Files matrix MUST cover all ten organisation types as parent and nested target, recording the exact reason for any genuinely incoherent or unsupported provider combination. Required cross-app journeys include Homework Project sections; the same file in Tree, Smart, Timeline and Pipeline without copies; a Sheet containing a Stash and Binder; the Annual report Flow; Flow/Binder/Sheet as Boards pages; an interactive Prototype in an authored Path; a Replay historical checkpoint after the source changes; and Variants in every one of the seven named apps. Tests MUST verify canonical IDs and actual content effects, not only screenshots of controls.
Visual/accessibility QA MUST inspect desktop and narrow/touch layouts, keyboard-only operation, screen-reader names/structure, visible focus, dialog return focus, 200% scaling, themes/contrast and reduced motion. Inspect normal, selected, loading, read-only, conflict and missing-source states. Verify that no pane overlaps the AI bar or prevents access to Save/Close/Back and that large canvases/graphs remain operable through their outlines. API tests MUST repeat consequential UI operations and prove equivalent permissions, validation, idempotency and results.
Recovery tests MUST interrupt source hydration, collection edits, native import/export, Prototype evaluation, Path progress writes, Replay finalisation and variant merge. Preserve originals and recoverable drafts, expose exact partial/failure state and prove safe retry. Resource tests MUST use populated realistic collections, long Flows, nested Binders, branching Paths and captured multi-source histories; record workload, platform and observed results. Empty fixtures alone do not establish production usability.
The release evidence ledger MUST identify each group as VERIFIED, IMPLEMENTED-UNVERIFIED, MISSING or BLOCKED, with tested revision/build, platform, evidence and exact remaining exception. A temporary Android/Go fallback is recorded as a scoped incomplete-authoring exception, not full parity. These tests add to, and do not replace or weaken, the operating contract, full active specification, donor parity, security, privacy, performance, native-device and package gates.
TF-X05 — Open naming decision and implementation defaults
The separate Playgrounds naming/product relationship to Prototype remains OPEN. This section does not authorise another overlapping standalone app. All other named Terra-form behaviours above are active shipping scope. No legacy migration is required for genuinely new formats unless real existing data needs it; any such data MUST be inspected and preserved before a format change.
Where a low-level representation or provider adapter detail remains unspecified, workers MUST choose the narrowest coherent implementation consistent with these visible behaviours, version and document its contract, and validate it. This discretion MUST NOT be used to invent another product, drop a required control/workflow, weaken ownership or security, flatten a native format, conceal an unsupported platform or defer a named feature. Material product changes outside these requirements require an explicit canonical decision.

### V2-B2246 · v2 lines 13247–13263

LE-12 — API, state and failure contracts
Every new UI action MUST use the same typed domain operation as automation. Extend existing namespaces rather than introducing parallel services: Home package install/update/repair; Spaces Sidebar sections/items/rules, Tabs, conversation placement/transfers, Study recommendations and RevisionBank; 9to1.Cards sets/cards/content/reviews; and 9to1.Planner.Burrows with Create, Get, List, Update, Assign, Unassign, Archive, Restore and Delete.
Before consumers implement against them, owners MUST publish the versioned action catalogue with exact argument/result schemas, affected stable IDs, owner/scope, required permissions, risk/reversibility, expectedRevision for writes, pagination for lists and operationId for replayable mutations. Long operations return JobID/status/cancellation and per-target outcomes. Read/search/preview/context and write APIs enforce the same current permissions; client-supplied account, Space or Burrow IDs do not grant access.
At minimum expose the UI-equivalent create/read/update/organise/delete/recover/import/export/configuration actions, Cards Rate and review queries, RevisionBank Add/Remove/Classify/Recommend/Create/Open, Sidebar AddSection/AddItem/Move/SetRule/Reset, Tabs Open/Select/Move/Close/Restore, and conversation GetPlacement/SetPlacement/SendToCloud/SendToDevice/GetTransfer/CancelTransfer. Public naming must remain within the existing canonical namespace conventions. Internal representations may vary only if these observable semantics remain compatible.
Use the existing structured error envelope. Add specific codes where absent for DependencyRequired, PackageDependencyConflict, TabTargetUnavailable, SidebarRuleInvalid, ProtectedSidebarItem, StorageModeDenied, TemporaryChatMustBeSaved, EntitlementRequired, QuotaExceeded, DeviceRevoked, TransferInterrupted, RevisionConflict, ResourceUnavailable, UnsupportedCardContent and BurrowNotFound. Errors identify action/target, retryability, retained source state and any completed/failed/skipped targets. Unknown/unavailable values are not fabricated defaults.
Persistent schemas MUST be versioned, revision-aware and crash-safe. Permission checks occur again on reconnect/commit. Repeated requests do not duplicate sets, memberships, planning items or transfers. Independent edits can merge only under the owning contract; conflicting edits preserve recoverable versions or return an explicit conflict. Normal deletion is recoverable; permanent purge remains a separate elevated operation. Migrations inspect and preserve actual existing data rather than assuming there is none.
LE-13 — Worker ownership and integration order
The coordinator first maps inherited requirements and affected source areas, then agrees shared manifests/APIs and test fixtures. Assign bounded owners for package/runtime modularity; Spaces navigation/sidebar/tabs/usage; conversation placement/privacy; Study/Revision Bank/Cards; and Planner Burrows. Workers may proceed in parallel only with non-overlapping owned files or explicit coordination. Consumers use reviewed interfaces, not independently invented schemas or duplicate shared components.
Integrate completed units continuously into the accepted root with owning tests and exact-head regression checks. Validate a shared component before dependent surfaces claim completion. Treat existing user feedback and P0/P1 defects as priority work. Packaging changes must re-test app embeds and missing-app paths; chat placement changes must re-test memory, indexing, Voice/Spaces conversation continuity and Mesh; Cards/Study changes must re-test the shared progress service.
The implementation must deliver real connected behaviour, not documentation or UI stubs. An external approval or unavailable device isolates its affected verification/deployment gate; other runnable work continues. The coordinator reviews worker output and remains responsible for final integration and evidence.
LE-14 — Pass acceptance and release exit gate
The acceptance ledger MUST cover every LE group and its inherited requirements with actual UI and API journeys, persistence/restart, permission denial/revocation, interruption/retry/recovery and platform/package evidence. Run populated desktop, narrow/touch, keyboard-only, screen-reader, 200% scaling, theme/contrast and reduced-motion checks. Inspect rendered interfaces, including selected/loading/empty/error/overflow states, not just source code.
Required end-to-end journey: install Spaces with mandatory core; add a custom Study sidebar section using + Add and rearrange it; create a Cards set from Revision Bank, edit both sides and save; reopen the same set in Cards and Spaces; record a topic-linked review and observe Study progress/recommendations; create a Study-burrow assignment and see the same item in standalone Planner; restart and verify all canonical IDs and saved state remain intact. Exercise a missing optional app during this journey and complete its trusted installation without losing context.
Privacy journey: create a temporary local chat, verify no managed off-device copy, reject an unsaved transfer, save locally, send to one authorised device, then explicitly enable eligible Cloud mode. Interrupt/retry a transfer, revoke a destination, and reduce placement while reporting outstanding replica cleanup truthfully. Verify a denied remote AI route cannot be enabled merely by model fallback or by embedding the chat elsewhere.
Resource and reliability evidence MUST state actual hardware/platform, dataset size, active/inactive tab counts, timing and memory/storage observations. Exercise long chat lists, many tabs, large card sets and mixed revision materials. Use approved project budgets where defined; where a release budget is absent, record measured results and the proposed release baseline for approval rather than inventing a passing number. No unbounded cache, polling loop or full-history rescan may substitute for bounded incremental behaviour.
Deliver release artifacts and a manifest identifying product/platform, version, exact commit, cryptographic hash, launch/install instructions and known limitations. Re-run clean install, upgrade, dependency failure, repair and rollback against those exact artifacts. Implementation, verification, deployment and public release approval remain distinct states.
Lunar Eclipse is complete only when every in-scope behaviour is integrated and verified, required inherited suites pass, artifacts install/run cleanly, and no unresolved mandatory failure is concealed. A remaining genuine external gate or approved scoped exception must identify the affected requirement/platform, impact and evidence; it does not count as verified functionality. A worker may not mark the pass complete merely because its allocated task list ended.

### V2-B2283 · v2 lines 13399–13399

## 13. Remaining V2 products — concrete interaction expansion without scope deletion

### V2-B2284 · v2 lines 13401–13401

These products retain all baseline requirements and donor parity. Their presence here is not permission to divert critical capacity from the priority cohort. The rules below add observable interaction detail; they do not replace the original app chapters.

### V2-B2336 · v2 lines 13505–13505

### UX27-OTHER-018 — Links, Binders, Sheets and organisation surfaces

### V2-B2337 · v2 lines 13507–13507

**B.** Preserve the baseline’s distinct owning products: a Binder/Flow references its canonical Link document; a Files Sheet is Files-owned organisation, not a renamed Link type. Nonlinear navigation, nested authored content, references, overview/outline and deep links remain coherent after rename/reorder/reopen. Moving a card in a view must not silently relocate or duplicate its referenced source artifact.


# Platform, app registry and host integration — Integration; Large board keeps every block and stroke (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2338 · v2 lines 13509–13510

Embedded organisation pages in Boards clearly identify the owning artifact, read/edit capability and Open original action. Returning to the board restores context. A visually similar grid of labels is not acceptance of the real information model.
<!-- UX27:END SECTION-13 -->

### V2-B2900 · v2 lines 15187–15187

## Integration

### V2-B2901 · v2 lines 15189–15189

### UX27-TEST-100 — Large board keeps every block and stroke

### V2-B2902 · v2 lines 15191–15191

**Requirements:** UX27-SHARED-020, UX27-ACCEPT-005 · **Status:** NOT RUN

### V2-B2903 · v2 lines 15193–15193

**Given:** Declared large board fixture with known counts, IDs and content hashes.

### V2-B2904 · v2 lines 15195–15197

**When:**
1. Edit early/late blocks, drag to a distant target and draw near the viewport.
2. Search unloaded content, save/reopen and export all.

### V2-B2905 · v2 lines 15199–15202

**Then:**
- Input remains responsive within measured hardware-specific budgets.
- All blocks/strokes are retained regardless of virtualisation.
- Full output hash/count comparison catches silent omissions; no arbitrary limit is invented.

### V2-B2906 · v2 lines 15204–15204

### UX27-TEST-101 — Independent review of actual main build

### V2-B2907 · v2 lines 15206–15206

**Requirements:** UX27-MAIN-001, UX27-MAIN-002, UX27-MAIN-003, UX27-ACCEPT-001, UX27-ACCEPT-002, UX27-ACCEPT-004 · **Status:** NOT RUN

### V2-B2908 · v2 lines 15208–15208

**Given:** Reconciled candidate with all original branch work accounted for and a separate reviewer available.

### V2-B2909 · v2 lines 15210–15213

**When:**
1. Inspect integration inventory and source/artifact identities.
2. Launch using Jacob's documented normal entry point.
3. Independently run representative repaired journeys and record all seven gates.

### V2-B2910 · v2 lines 15215–15218

**Then:**
- No unmerged/dirty work was deleted to achieve one branch.
- Launched build corresponds to the recorded main candidate.
- Consolidation alone is not acceptance; missing runtime/platform/independence gates stay unpassed.

### V2-B2911 · v2 lines 15220–15220

### UX27-TEST-102 — Full manifest and honest unavailable coverage

### V2-B2912 · v2 lines 15222–15222

**Requirements:** UX27-AUTH-003, UX27-AUTH-006, UX27-COVERAGE-001, UX27-COVERAGE-002, UX27-COVERAGE-004, UX27-ACCEPT-003, UX27-ACCEPT-006 · **Status:** NOT RUN

### V2-B2913 · v2 lines 15224–15224

**Given:** Baseline, expansion and selected donor/reference versions available to a reviewer.

### V2-B2914 · v2 lines 15226–15229

**When:**
1. Map every applicable requirement to app/entry point/engine/storage/evidence.
2. Compare the scenario catalogue to that manifest.
3. Record absent devices, images, platforms and tests explicitly.

### V2-B2915 · v2 lines 15231–15235

**Then:**
- Catalogue is a floor, not a scope reduction.
- Draft defaults are not represented as measured historic screenshot details.
- Every claim has source/runtime provenance, and NOT RUN is not relabelled PASS.
<!-- UX27:END ACCEPTANCE-CATALOGUE -->

### SCALABILITY-B0001 · scalability lines 1–3

9to1 V2 — Scalable Content and No Arbitrary Size Caps
Normative product and engineering annex — 20 October 2026
Canonical V2 master: https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit

### SCALABILITY-B0002 · scalability lines 6–6

AUTHORITY AND SCOPE — This owner-approved normative addition supplements V2's shared performance, CUI, Files, Write, Present, Canvas, Boards, Data and relevant media/editor contracts, both native and web. Preserve all existing donor-functionality, security, accessibility, reliability and independent acceptance requirements. It specifies scalable content handling; it does not claim literally infinite resources.

### SCALABILITY-B0003 · scalability lines 9–9

SCALE-001 — NO ARBITRARY SIZE CEILINGS. First-party 9to1-native documents/projects MUST NOT impose fixed global character, word, page, slide, block, object or file-byte caps just to simplify UI rendering, opening or saving. Allow practical growth subject to available disk/remote quota, supported format, filesystem/address-space and security limits. Avoid hard-coded application-wide caps when scalable indexing, streaming or chunking can address the bottleneck. Disclose every real limiting factor and prevent silent truncation.

### SCALABILITY-B0004 · scalability lines 12–12

SCALE-002 — USER-FACING PRIORITY. Pointer/keyboard input, caret, selection, active-region edits, scroll, drag and undo/redo are highest-priority work. Near-viewport prefetched rendering and user-requested navigation are next. Unrelated distant pagination/layout, content analysis, indexing, thumbnails, previews and sync are lower priority. Use responsive scheduling, cancellation, backpressure and background work; off-screen processing MUST NOT be a prerequisite for normal editing in the visible region.

### SCALABILITY-B0005 · scalability lines 15–15

SCALE-003 — CANONICAL STATE SEPARATE FROM VIEW. The persisted editable model, identities, relationships, undo/operation history and search semantics MUST be independent of materialised UI nodes, caches, page thumbnails or rendered slides. Never equate unrendered with nonexistent. Unloaded content remains consistent, accessible, searchable, exportable and editable after hydration, with correct reopen behaviour.

### SCALABILITY-B0006 · scalability lines 18–18

SCALE-004 — VIRTUALISED / STREAMED RENDERING. Render the visible pages/slides/tiles/cells/blocks/nodes/tracks plus a bounded adaptive buffer. Use region-based culling, placeholder sizing, lazy hydration, thumbnail/preview caching and eviction; do not instantiate the entire artifact's UI tree. Adapt prefetch to scrolling speed, device resources and expected jumps. Maintain memory budgets and gracefully reduce preview fidelity or buffer size under pressure, not document fidelity.

### SCALABILITY-B0007 · scalability lines 21–21

SCALE-005 — INCREMENTAL CALCULATION. Edit the current region immediately and invalidate only affected dependent layout/index/cache ranges. Reflow, paginate, rebuild derived views and thumbnails progressively with chunk or section indexes, layout checkpoints, dependency-aware invalidation and version/generation checks so stale computations cannot overwrite newer edits. Show provisional page counts or progress honestly until exact values are known; eventual output must be correct.

### SCALABILITY-B0008 · scalability lines 24–24

SCALE-006 — INDEXED NAVIGATION. Deep jumps, headings/outline, bookmarks, slide navigator, find/replace and selection across distant or unloaded regions use stable content IDs and persisted/incremental indexes. Do not require scanning/rendering all earlier pages to find a position. Show genuine progress for expensive uncached navigation instead of freezing or reporting guessed locations as certain.

### SCALABILITY-B0009 · scalability lines 27–27

SCALE-007 — CHUNKED PERSISTENCE. Prefer addressable chunks/objects, changed-range delta/operation saves, transactional/atomic commit, versioning, integrity checks, crash-safe recovery and bounded undo/snapshot retention. Autosave MUST NOT repeatedly serialise the whole large artifact per keystroke. Detect and resolve concurrent mutations rather than silently overwriting user content; safely clean orphaned chunks, temp files and caches.

### SCALABILITY-B0010 · scalability lines 30–30

SCALE-008 — STREAMING IMPORT, EXPORT AND MEDIA. Read/write/transfer large data incrementally; support resuming and cancelling long work with truthful job status and error recovery. Avoid full-file memory duplication wherever possible. Do not claim complete export until all required chunks, relationships and media have been validated and committed. Where a third-party format/provider has a genuine size/count limit, state it and offer a truthful native-format or partitioned workflow, never a fake success.

### SCALABILITY-B0012 · scalability lines 36–36

SCALE-010 — SAFETY / RELIABILITY. Preserve disk-space margins, bounded caches and memory/CPU/VRAM budgets. Implement backpressure, malicious-file and decompression-bomb protection, limits for untrusted parser operations and safe failure handling. On insufficient resources, report the resource constraint, preserve changes, allow a safe retry/resume or lower preview resolution; never corrupt or silently discard content. Security constraints are valid even when they require rejecting malicious inputs.

### SCALABILITY-B0013 · scalability lines 39–39

SCALE-011 — CROSS-DEVICE / CROSS-APP. The native Windows/Linux and browser versions must share the same canonical scalable-content semantics, with truthful platform-specific limits. Embedded documents/slides/boards, shared search, AI features and cross-app conversion must not force whole-project eager loading. Maintain accessibility and keyboard navigation across virtualised regions. No downgraded web implementation may falsely display a truncated artifact as complete.

### SCALABILITY-B0016 · scalability lines 48–48

OTHER-APPS-SCALE-201 — ANALOGOUS DOMAIN DESIGN. Boards and Canvas virtualise blocks/spatial regions. Data virtualises rows/cells/results and streams large data operations. Picture uses tiled or region-aware image processing. Wave and Motion load, cache and process time ranges/tracks/media chunks under strict playback/resource budgets. All other applicable apps use corresponding index/stream/cull methods while retaining their real workflows.

### SCALABILITY-B0017 · scalability lines 51–51

SCALE-AC-201 — TESTING AND MEASUREMENT. Independent acceptance MUST include representative small/medium/large and stress real-artifact tests across supported platforms. Example stress targets: >=1,000 pages in Write, >=10,000 slides in Present where reasonable on test hardware, sizeable binary files, nested/embedded artifacts and large tables/boards. Capture cold/warm load, input and scroll latency, frame times, memory/VRAM/disk consumption, deep-jump latency, edits near start/end, asynchronous reflow, incremental save, recovery after interruption, search across unloaded regions, reopen and full export/import integrity. Define platform/device-specific budgets and report actual results; no fake data, hidden drops or assertions based only on successful startup.

### SCALABILITY-B0018 · scalability lines 54–54

SCALE-AC-202 — MANDATORY FAILURES. FAIL when responsiveness is achieved by silently omitting content, nonfunctional editing/find/navigation, quietly disabled save/export, pretend background completion, runaway caching, data loss, unsupported format misrepresentation or hidden arbitrary limits. Existing application-specific verification and donor-parity gates remain in force.

### SCALABILITY-B0019 · scalability lines 57–57

IMPLEMENTATION STATUS — This annex changes requirements only. It does not assert that Write, Present, Files or any other 9to1 app currently implements or has passed these capabilities.
