# Agent instructions — 9to1 specification v2

## Sole product authority

Read [the attached canonical specification](docs/specification/9to1-development-specification-v2.md) before working on any surface. Its SHA-256 is `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`; [provenance](docs/specification/provenance.json) identifies the exact local owner attachment. Do not fetch or substitute a web version. Read the applicable full sections and dependencies, not only an implementation summary.

This local specification is the ONLY product authority. Existing code, older specs, markdown, handoffs, comments, generated guides, worker messages and design-pack historical prose cannot override it. Later explicit owner decisions may amend it. Older compatibility names do not restore retired products. Preserve data and useful functionality while correcting implementations to the specification.

On every fresh session, handoff or context compaction, reread this file, the specification authority/CUI/priority/acceptance sections, and the sections governing the assigned surface. Record the source identity, assigned scope, relevant requirement IDs, observed state and unresolved decisions in the handoff. Never treat a condensed worker summary as replacement authority.

If a conflict or uncertainty affects public behaviour, compatibility, security, data or architecture, queue the exact requirement, conflicting evidence and affected change in `docs/specification/open-decisions.json` for an owner response. Hold only that affected change/acceptance decision; continue independent authorised work. Do not invent a waiver or silently narrow scope.

## CUI and product boundaries

CUI is the only application-authoring language: structure, styling, state, interactions, behaviours, composition and orchestration use canonical versioned CUI and typed runtime/action contracts. C#, XAML/AXAML, HUI, HTML/CSS/JavaScript, React and native-control markup must not become application source or hidden code-behind. Mature donor engines, compiler/runtime/backend internals, OS adapters and external services may retain implementation languages behind reviewed typed infrastructure boundaries. Missing CUI expressiveness is a framework conformance gap to implement, not permission for a parallel authoring path.

Never load, instantiate, inherit, package as a CUI dependency or fall back to Avalonia FluentTheme or Fluent templates/resources in compiler/runtime, renderer, preview, designer, test host or shipping surface. SimpleTheme and other donor themes must not own first-party presentation. The approved Avalonia fork supplies low-level capabilities behind CUI-owned templates, resources, property precedence and states.

Haven is the browser-derived super-app; Home Core is its shared service/control plane. Spaces owns core Spaces and Space Builder. Voice & Vision is one canonical multimodal surface. Build is one app combining Dev/Sites/Stacks scope. Connect owns communications with Mail as its first-class email mode. Follow all identities and delivery exceptions in the specification; names in existing code are not authority.

## Execution and ordering

Complete frameworks and shared dependencies before dependent surfaces claim readiness. Follow the specification priority: Haven and its frameworks; Spaces/Voice & Vision; Build; Automations; Write/Present/Boards/Canvas with Data/shared productivity dependencies; Files/web services; Planner/Mail; then the remaining specified order. Implement essential lower-priority dependencies when higher-priority workflows require them without declaring the entire lower-priority product complete.

The owner's requested team structure is four implementation lanes with separate surfaces/ownership, plus two independent acceptance lanes awakened when initial surfaces are ready. Respect actual available concurrency and memory limits; queue roles when capacity is unavailable and report the real active count. Do not claim six simultaneous workers unless they exist. Coordinate ownership before changing shared files; use isolated worktrees for overlapping integration boundaries.

Former Beam implementation workers are retired. Do not revive the old Beam programme, prompts or retry/continuation loop. Preserve ongoing operational and memory/OOM/build-concurrency safeguards established by the coordinator and other authorised chats. Do not bypass limits, locks, cgroups, build serialization or resource checks to increase parallelism. Recover crashes through the authorised supported mechanism while preserving work; do not restart unrelated services or delete locks blindly.

The owner's subsequent implementation-engine correction uses four **fresh OpenCode sessions with Beam at max reasoning**, each with isolated ownership/worktrees. The live verified identity is `opencode-go/beam-free`, variant `max`. Independent acceptance uses Astra. These new sessions do not resume the retired Beam programme. The coordinator records their exact identities and current assignments; never use whichever session is most recent or silently replace these workers with Codex implementation agents. Acceptance evidence directories belong to reviewers: implementations may copy unchanged probes into their own directories, but must not overwrite reviewer artifacts or logs.

The delivery target is 07:00 Europe/London, 19 October 2026 (06:00 UTC): Haven, Spaces, Build, Automations, Write, Present, Data and Canvas complete and independently accepted. Boards remains in the productivity priority group. Missing the target does not authorise stopping, narrowing scope or weakening quality: continue authorised work until completion or a later explicit owner stop. An explicit stop always takes precedence. Do not treat an automated heartbeat as permission to undo a user stop.

## Verification and custody

Implementation performs focused authentic smoke checks and does not spend its whole turn repeating broad tests. Diagnose individual failures; do not roll back solely because one test failed. Independent acceptance tests the full applicable specification and all seven criteria: Usability, Product fit, Functionality, Integration, Separation and authenticity, User fit, Refinement. Acceptance may fix minor defects; significant defects return to implementation with precise affected requirements, repro steps and required evidence.

Keep engineering state separate from acceptance state. Working code, builds, test fixtures, mockups and screenshots alone do not prove completion. Retain FAIL, BLOCKED and NOT RUN, exact source/build/artifact identity, platform/theme/viewport, real workflow and persistence evidence. Never weaken, skip or manufacture tests to obtain a pass. Update obsolete expectations only to enforce the new specification while preserving regression coverage. Read APP-AC-001–009 and relevant app acceptance sections.

Use the supplied mockups for exact intended layout where identified; references illustrate features/design principles. They cannot override the specification or supply fake personal data. Apply Present's shared design principles to Write, Boards and Canvas while retaining their required app-specific layouts. Do not wait for future mockups.

Preserve other workers' changes, user data, historical evidence that does not direct conflicting behaviour, donor licences and notices. Conflicting markdown may be retired only after actual content review and an exact recoverable backup with original path/hash/reason. The authority reconciliation manifest records partial coverage; an inventory or keyword scan is not a full review. Do not edit unrelated code/configuration as part of document reconciliation.
