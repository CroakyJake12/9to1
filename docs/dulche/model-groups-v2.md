# Dulche Model Groups V2

Authoritative specification: https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit

Source: approved user handoff, 16 October 2026. This is the reconciled specification proposal, not a claim that the master was updated. Two authenticated Google Docs writes (a revision-guarded formatted batch and a minimal insertion) returned HTTP 400 FAILED_PRECONDITION, 'Precondition check failed.' Read-back after the first failure showed the same revision and no feature marker. MG-T01 remains BLOCKED on a writable authoritative document; no master requirements were replaced. This is requirements and planning evidence, not implementation acceptance.

Persistent backlog: [GitHub issue #34](https://github.com/CroakyJake12/9to1/issues/34), linked to existing parent queue #21. Task mirror: [model-groups-tasks.md](../development/model-groups-tasks.md).

A separate-tab insertion was also attempted to avoid the original tab's insertion constraints. Google allowed creating the tab but rejected its text with the same error. The empty tab was removed. Final read-back verified that the original tab body and sole tab ID are unchanged; only revision history records the attempted tab creation/removal. No Model Groups content was saved to the master.

## Model Groups — DULCHE-MODEL-GROUPS-V2

Approved requirements, 16 October 2026. Implementation status: WORKING — specification and backlog registration; implementation and independent acceptance remain pending. This feature belongs to Shared Frameworks → Dulche and follows the existing dependency priority. It does not displace active critical work or weaken any existing model-routing, Den, CUI, permission, privacy, persistence, platform or acceptance requirement.

### Virtual identity and routing profiles

A Model Group is a user-defined, Den-persisted virtual model identity containing an ordered set of configurable profiles. It appears as one selectable model in the shared Universal Model Picker and is discoverable by every compatible authorised app. A group is model-, provider-, backend- and quantisation-agnostic and modality-aware. It MUST NOT be implemented merely as reasoning presets, quantisation variants, a display alias or an app-private model list.

Each profile MAY reference a completely different concrete deployment, including unrelated local models, cloud models, image generators and specialist models. One group MUST support profiles using llama.cpp, Strata, supported cloud providers and compatible specialist/image runtimes together. Multiple profiles MAY reference the same deployment with different reasoning/inference settings, or different versions, sizes and quantisations. Group/profile names and counts are user-configurable; neither named tiers nor a fixed count is hardcoded.

Illustrative configurations include Flash/Average/Pro/Ultra profiles using Q2/Q3/Q4 deployments; Quick/Medium/Thinker profiles using different model sizes; Writer/Coder/Spanish Translator/Researcher specialisations; and Low/Medium/High/Ultra/Image/Decision profiles spanning local, Strata, cloud and image/specialist runtimes. These examples demonstrate required architecture, not verified model availability or production user data. Actual identities, availability, capabilities and supported settings MUST come from the registered providers.

Model Groups and ordered Model Routes are distinct concepts. A selected group profile resolves a virtual selection to its concrete deployment and settings. Existing route precedence, eligibility-first filtering, classified fallback conditions and privacy controls still apply. Profiles MUST NOT silently become fallback candidates merely because they share a group. An explicit pinned request with fallback disabled remains pinned. The local llama.cpp-derived runtime provenance does not restrict the shared routing layer to that backend or authorise a second broker.

### Selection is separate from runtime residency

Changing a group or active profile MUST immediately update the visible selection and pending responder without loading or unloading any model, allocating additional model RAM/VRAM, restarting a backend or otherwise changing runtime residency solely because of that selection. Existing loaded models remain loaded. Rapid profile switching is selection-only; the pending responder and currently loaded model are distinct state. Explicit load/unload controls and independent cache/eviction policies remain available under existing lifecycle contracts.

On Send or AI-task start, Dulche MUST atomically capture stable GroupID/ProfileID, their configuration revisions and effective request settings. It then resolves the underlying ModelID/deployment revision, provider, backend/runtime, quantisation, reasoning and inference configuration; validates requested modalities/capabilities, credentials, consent/permissions, policy, entitlement and resource availability; loads only the required compatible model when necessary or reuses a compatible loaded instance; executes through the existing shared runtime; and records the exact effective configuration in canonical Task/Run/request history.

For Low → Medium → Ultra → High → Ultra → Send, only the final Ultra selection is resolved for submission and loaded or invoked as a consequence of that sequence. An in-flight request retains its immutable captured GroupID/ProfileID and configuration when the user changes profiles or edits the group. Changing that admitted selection requires explicit cancellation/restart. Existing authorised recovery/fallback creates a separately attributed attempt/deployment transition under the captured route policy; it never mutates the original captured group/profile configuration. Group edits/deletion MUST NOT rewrite historical attribution or silently retarget an accepted request.

### Reasoning-selector presentation

The shared Dulche/CUI model selector MUST support two persisted presentation modes. Custom names displays arbitrary profile names and ordering, including specialisations that are not reasoning levels. Percentage mode displays user-configured percentage values or ranges mapped to profiles. The mapping must resolve unambiguously; missing or ambiguous mappings produce actionable validation rather than silently selecting another profile.

Displayed percentages are user-defined routing controls, not universal intelligence scores or provider-independent reasoning measurements. Each profile retains its own provider-specific reasoning configuration, and unsupported provider settings MUST NOT be invented. Existing standalone-model reasoning controls remain available and retain their semantics. Every compatible app using the shared selector honours the group's configured display mode.

### Den persistence, security and synchronisation

Den MUST persist stable GroupID, name, description, optional icon, namespace/owner, stable ProfileIDs, order, profile names, concrete model/deployment references, provider/backend/runtime references, version/quantisation identity, reasoning and inference settings, display mode, percentage mappings, defaults/preferences, configuration version, revision and sync metadata. Concrete deployment and virtual group identities must be distinguishable in the existing model registry. Names are never canonical IDs.

Support creation, editing, duplication, profile reordering, deletion, reload and authorised cross-device synchronisation through the canonical Den contracts. Duplication creates new group/profile identities; rename/reorder preserves identity. Use revision-aware atomic/journalled writes, conflict reporting, recovery and deletion/tombstone semantics; preserve valid prior configuration and history. Portable definitions reference model identities and secure credentials, not copied weights, local executable paths or API keys. Device-local availability and missing credentials remain explicit.

DEN-PRIV-001–008 remain mandatory: encryption, namespace isolation, scoped consent, revocation, minimum-necessary model context and protection against onward disclosure. Model Group persistence, discovery, profile selection or sync MUST NOT enable personal-context sharing or grant access to another app, device or provider. Cross-device synchronisation is an implemented capability activated only under the existing explicit consent and access rules. Cloud inference requires the existing separate authorisation; a local/private workflow never silently sends content to a cloud member. Secrets remain in the approved credential system, never group definitions, exports, prompts or logs.

### Shared integration, APIs and failure handling

Reuse Dulche's model registry, model-routing/residency coordinator, Home broker, Den persistence/sync and CUI selector. Define the responsibilities of ModelGroup, ModelGroupProfile, ModelGroupRepository, ModelGroupResolver, ModelGroupSelection, ResolvedModelDeployment, ModelGroupCapabilities and ModelGroupRuntimeCoordinator within existing abstractions rather than creating competing stores or services. UI and typed APIs MUST expose equivalent create/read/update/duplicate/reorder/delete, discovery, selection, validation and submission operations using stable identities and revision/conflict semantics.

Chat, Spaces, Dev, Assistants, Write, Present and other compatible AI-enabled surfaces MUST use the same authorised group identity and profiles. Apps validate the specific selected profile's capabilities at submission; a group's aggregate capabilities do not imply every profile supports every modality. An image profile cannot be silently interpreted as a text result. Selection and management remain accessible, keyboard/touch usable, responsive and governed by the shared CUI appearance/reduced-motion requirements.

Missing groups/profiles/models, incompatible capabilities/settings, missing credentials, unavailable providers/backends, insufficient resources, failed loads, revoked consent and configuration conflicts MUST return actionable structured errors without fabricated success or unauthorised effects. Preserve drafts, valid task state and accepted history. An unavailable provider is not proof that another group member is authorised. Existing request identity, cancellation, fallback, retry, checkpoint and no-duplicate-effect contracts remain in force.

### Acceptance — MG-01 through MG-18

MG-01 — Identity: a group appears as one stable virtual model identity in the shared selector.

MG-02 — Arbitrary members: one group supports unrelated models and providers.

MG-03 — Multiple backends: llama.cpp, Strata and supported cloud backends coexist in one group through real registered runtime paths.

MG-04 — Mixed modalities: text, image and specialist profiles retain correct capabilities and incompatible workflows are refused.

MG-05 — Reasoning configuration: two profiles targeting the same model use their distinct supported reasoning settings.

MG-06 — Quantisation switching: Q2/Q3/Q4 profiles resolve to the exact registered deployments; unavailable illustrative models are not fabricated.

MG-07 — Lazy loading: instrumented profile-selection sequences cause zero model loads, unloads, model-memory allocations or backend restarts.

MG-08 — Send-time resolution: rapid selection followed by Send atomically resolves and executes only the final selected profile.

MG-09 — Active requests: changing profiles or group configuration does not alter an in-flight request's captured resolved configuration.

MG-10 — Custom names: arbitrary names, counts and ordering work and persist without fixed tier assumptions.

MG-11 — Percentage mode: explicit value/range mappings work, ambiguous mappings are rejected and provider reasoning settings remain separate.

MG-12 — Den persistence: create/edit/duplicate/reorder/delete survive restart and authorised cross-device sync, conflicts and recovery without losing identity/history or bypassing consent.

MG-13 — Cross-app: multiple compatible authorised apps select the same canonical group and honour its display mode and profile state.

MG-14 — Privacy: provider routing, sync and context use honour granular consent, permissions, local-only restrictions and revocation, including changes during a run.

MG-15 — Error handling: missing or incompatible models, failed loads and unavailable providers produce actionable errors with no simulated success.

MG-16 — History: canonical runs retain group/profile revisions plus exact effective model, provider, backend/runtime, quantisation and reasoning/inference metadata after configuration changes.

MG-17 — Regression: existing standalone model selection, ordered routes, reasoning controls, fallback/privacy semantics and explicit lifecycle actions still work.

MG-18 — Authenticity: acceptance uses genuine registered deployments and runtime evidence; no fake loading, hardcoded sample groups, simulated provider success or placeholder implementation qualifies.

Apply all seven 9to1 acceptance tests: Usability, Product Fit, Functionality, Integration, Separation & Authenticity, User Fit and Refinement. Automated tests, independent visual/functional acceptance and applicable exact-artifact/platform/provider checks are separate evidence gates. Missing runtimes or devices remain unvalidated, not passed. The feature cannot be COMPLETED or VERIFIED until all relevant requirements and independent acceptance pass.

### Scheduling and persistent task registration

Register MG-T01 authoritative V2 incorporation; MG-T02 shared contracts; MG-T03 Den persistence/synchronisation; MG-T04 registry/routing; MG-T05 lazy send-time resolution; MG-T06 multi-backend coordination; MG-T07 shared CUI selector; MG-T08 group management; MG-T09 app integration; MG-T10 comprehensive automated tests; MG-T11 independent visual/functional acceptance; and MG-T12 regressions/release readiness. Preserve existing TODOs and priorities. Schedule implementation in Shared Frameworks → Dulche after required contracts, secure Den/sync, provider/runtime identity, permission/privacy and canonical request-history dependencies are available, with independent work allowed under the existing priority policy.

Specification incorporation and backlog registration are an immediate CHECKPOINT, separate from feature completion. Report saved specification sections, the repository commit, persistent task location/IDs, dependencies and evidence; do not wait for full implementation to report this checkpoint.
