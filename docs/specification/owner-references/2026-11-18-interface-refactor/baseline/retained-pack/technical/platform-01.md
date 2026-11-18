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
