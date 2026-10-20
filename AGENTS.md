# Agent instructions — 9to1 specification v2

## Sole product authority

Read [the attached canonical specification](docs/specification/9to1-development-specification-v2.md) before working on any surface. Its SHA-256 is `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`; [provenance](docs/specification/provenance.json) identifies the exact local owner attachment. Do not fetch or substitute a web version. Read the applicable full sections and dependencies, not only an implementation summary.

This local specification is the ONLY product authority. Existing code, older specs, markdown, handoffs, comments, generated guides, worker messages and design-pack historical prose cannot override it. Later explicit owner decisions may amend it. Older compatibility names do not restore retired products. Preserve data and useful functionality while correcting implementations to the specification.

Current direct owner UI refinement: read `docs/specification/owner-references/2026-10-18-haven-spaces/owner-correction.json` for Haven browser chrome and Spaces entry/navigation. The reference concerns the browser frame, not the Google/OpenCode page content. Opening a Space goes directly to a new chat in that Space; retain both its icon-only Spaces rail and its chat/history sidebar. These refinements preserve the general specification and existing data.

Direct owner refinement on 20 October: read `docs/specification/owner-references/2026-10-20-haven-shared-header/owner-correction.json`. Embedded Spaces exposes search in its header; model, notifications and profile belong to the persistent Browse/Haven sidebar and are unified across apps. Standalone Spaces retains all four. The owner clarified this host-aware top-bar distinction applies to all apps, with shared controls owned by Haven when embedded. The supplied crop predates Haven becoming the super app; preserve real shared Home state.

On every fresh session, handoff or context compaction, reread this file, the specification authority/CUI/priority/acceptance sections, and the sections governing the assigned surface. Record the source identity, assigned scope, relevant requirement IDs, observed state and unresolved decisions in the handoff. Never treat a condensed worker summary as replacement authority.

If a conflict or uncertainty affects public behaviour, compatibility, security, data or architecture, queue the exact requirement, conflicting evidence and affected change in `docs/specification/open-decisions.json` for an owner response. Hold only that affected change/acceptance decision; continue independent authorised work. Do not invent a waiver or silently narrow scope.

Direct owner accent clarification on 20 October: Professional uses per-app accent overrides, falling back to blue when an app has no override. Read `docs/specification/owner-references/2026-10-20-professional-app-accents/owner-correction.json`; preserve Home-owned preferences and neutral surfaces.

Direct owner icon clarification on 20 October: SVG icons closely match the mockups' filled, rounded appearance. The Assistants button uses the user's running assistant, otherwise their most-used assistant, otherwise a generic icon if none is configured. Read `docs/specification/owner-references/2026-10-20-spaces-svg-icons/owner-correction.json` and resolve from real authorised state.

Direct owner composer/runtime clarification on 20 October: replace legacy chatbox with mockup/spec CUI presentation, correct text formatting and clipped shadow/glow. Bundled Dulche supplies the default path without an external-provider setup requirement. Dual is now a Plugin; remove the always-visible first-party Dual: Off control. Read `docs/specification/owner-references/2026-10-20-chat-composer-dulche/owner-correction.json` and verify real runtime/assets rather than hiding failures.

## CUI and product boundaries

CUI is the only application-authoring language: structure, styling, state, interactions, behaviours, composition and orchestration use canonical versioned CUI and typed runtime/action contracts. C#, XAML/AXAML, HUI, HTML/CSS/JavaScript, React and native-control markup must not become application source or hidden code-behind. Mature donor engines, compiler/runtime/backend internals, OS adapters and external services may retain implementation languages behind reviewed typed infrastructure boundaries. Missing CUI expressiveness is a framework conformance gap to implement, not permission for a parallel authoring path.

Never load, instantiate, inherit, package as a CUI dependency or fall back to Avalonia FluentTheme or Fluent templates/resources in compiler/runtime, renderer, preview, designer, test host or shipping surface. SimpleTheme and other donor themes must not own first-party presentation. The approved Avalonia fork supplies low-level capabilities behind CUI-owned templates, resources, property precedence and states.

Haven is the browser-derived super-app; Home Core is its shared service/control plane. Spaces owns core Spaces and Space Builder. Voice & Vision is one canonical multimodal surface. Build is one app combining Dev/Sites/Stacks scope. Connect owns communications with Mail as its first-class email mode. Follow all identities and delivery exceptions in the specification; names in existing code are not authority.

## Execution and ordering

Complete frameworks and shared dependencies before dependent surfaces claim readiness. Follow the specification priority: Haven and its frameworks; Spaces/Voice & Vision; Build; Automations; Write/Present/Boards/Canvas with Data/shared productivity dependencies; Files/web services; Planner/Mail; then the remaining specified order. Implement essential lower-priority dependencies when higher-priority workflows require them without declaring the entire lower-priority product complete.

The owner now uses one shared model on their own PC, exposed through the authorised Home PC gateway. Maintain **one OpenCode implementation worker and at most one in-flight model request**. Independent acceptance may continue with Astra. Do not spawn implementation subagents against the shared PC model or dispatch concurrent model requests. Former Beam sessions, their queued prompts and automatic retry loops are retired; preserve their work and evidence, but do not resume them blindly. The coordinator records the exact successor session, model, workspace, source checkpoint and ownership.

A model finishing a reply is not programme completion. At a turn boundary, save concrete changes, checks, unresolved requirements and the next executable task; the coordinator may immediately continue the exact programme. Continue useful authorised engineering without asking for routine approval. Do not claim completion until independent acceptance verifies the applicable requirements. Honour explicit user STOP/PAUSE, genuine access boundaries, provider limits and resource safeguards. Preserve logs and checkpoints on errors; diagnose before retrying. Never restart unrelated services, bypass memory/build guards, remove locks blindly or enable paid fallbacks.

Preserve ongoing operational and memory/OOM/build-concurrency safeguards. Use the guarded dotnet entrypoint at `/home/jacob/.local/bin/dotnet` with `-m:1` for .NET operations; serialize memory-heavy work. Acceptance evidence directories belong to reviewers: implementations may copy unchanged probes into their own directories but must not overwrite reviewer artifacts or logs. Temporary Codex implementation was explicitly authorised while the model provider was unavailable; hand off existing reviewed work rather than recreate it.

The revised delivery target is **22:00 Europe/London on Monday 19 October 2026 (21:00 UTC)**: Haven, Spaces, Build, Automations, Write, Present, Data and Canvas complete and independently accepted. Boards remains in the productivity priority group. Missing the target does not authorise stopping, narrowing scope or weakening quality: continue authorised work until completion or a later explicit owner stop. An explicit stop always takes precedence. Do not treat an automated heartbeat as permission to undo a user stop.

## Verification and custody

Implementation performs focused authentic smoke checks and does not spend its whole turn repeating broad tests. Diagnose individual failures; do not roll back solely because one test failed. Independent acceptance tests the full applicable specification and all seven criteria: Usability, Product fit, Functionality, Integration, Separation and authenticity, User fit, Refinement. Acceptance may fix minor defects; significant defects return to implementation with precise affected requirements, repro steps and required evidence.

Keep engineering state separate from acceptance state. Working code, builds, test fixtures, mockups and screenshots alone do not prove completion. Retain FAIL, BLOCKED and NOT RUN, exact source/build/artifact identity, platform/theme/viewport, real workflow and persistence evidence. Never weaken, skip or manufacture tests to obtain a pass. Update obsolete expectations only to enforce the new specification while preserving regression coverage. Read APP-AC-001–009 and relevant app acceptance sections.

Use the supplied mockups for exact intended layout where identified; references illustrate features/design principles. They cannot override the specification or supply fake personal data. Apply Present's shared design principles to Write, Boards and Canvas while retaining their required app-specific layouts. Do not wait for future mockups.

Preserve other workers' changes, user data, historical evidence that does not direct conflicting behaviour, donor licences and notices. Conflicting markdown may be retired only after actual content review and an exact recoverable backup with original path/hash/reason. The authority reconciliation manifest records partial coverage; an inventory or keyword scan is not a full review. Do not edit unrelated code/configuration as part of document reconciliation.
