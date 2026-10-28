## Single-main bug repair — 28 October 2026, latest owner direction

Fix the [reported bugs](docs/specification/owner-references/2026-10-28-reported-bugs/9to1-full-reported-bug-list-2026-10-27.md) first, then continue the adopted expanded v2 specification. Reproduce applicable failures and preserve already-working behaviour; a historical report is not evidence that current main is broken.

All new source changes and integrations occur on **main** in `/home/jacob/Documents/GitHub/9to1`. Do not create implementation branches or worktrees. Preserve existing history in a verified archive before retiring redundant branch refs. Earlier branch-isolation instructions are superseded by this explicit owner request. Concurrent workers may own disjoint files on the same main checkout; SDK execution remains serial and guarded.

At **10% remaining**, checkpoint and reload the testing copy with the latest successfully built and checked candidate, preserving its profile and using the normal reviewed lifecycle. Stop Codex development when actual usage reaches **5% remaining**, preserving a precise handoff. Existing user data, source, failed evidence and protected model workloads remain preserved. Full independent acceptance stays paused; compilation, focused genuine tests and native bug retests continue.

## Expanded behaviour specification — 27 October 2026, latest owner direction

The owner supplied the expanded v2 package in `docs/specification/owner-references/2026-10-27-expanded-spec/`
(176 `UX27-` requirement IDs, 102 NOT RUN scenarios, evidence frames and a read-only Google Doc baseline).
Read [PROVENANCE.json](docs/specification/owner-references/2026-10-27-expanded-spec/PROVENANCE.json),
[adoption-notes.md](docs/specification/owner-references/2026-10-27-expanded-spec/adoption-notes.md) and the
applicable `UX27-` sections before changing a surface. The canonical v2 attachment and its `e1779d22…` pin
remain the product authority; this package elaborates owner intent and is the current correction queue.
Headline correction: Boards must create/save without connecting Files, an account or a dedicated Boards
folder, keep local drafts recoverable, and offer an optional editable `.9to1board` Save As. Requirement IDs
stay NOT RUN until observed in the running development copy; text-only evidence is not visual review. Full
independent acceptance remains paused.

## Guarded native memory bounds — owner override, 27 October 2026

The owner directs "just bypass the memory guard" because this OpenCode session **is** the local Strata
model server (observed ~45 GiB resident, `--resident-budget-gib 41`) and must never be stopped. The retained
`guarded_root_live_sdk03.py`/`prepare_root_live_plan03.py` stay byte-unchanged with their refusal evidence.
The labelled successor pair `guarded_root_live_sdk04.py`/`prepare_root_live_plan04.py` lowers only
`RSS_LIMIT` 8→4 GiB and `MEMORY_HEADROOM` 4→1 GiB, records the override in the allocation and ledger, and
keeps every other guard: exact command/stage map, serial SDK and source-writer locks, concurrent-SDK
refusal, disk reserve plus copy budget, per-stage timeouts, GC child environment, exact source/export
custody, CAS re-check before each spawn and a continuous admission floor. Never promote a relaxed-memory
result into accepted or reusable evidence.

## Local OpenCode PC takeover — 26 October 2026, latest owner direction

The owner selects **OpenCode on pc** to move development/builds locally and support
one-bug-at-a-time native reloads. Read [the current takeover](docs/development/opencode-pc-live-takeover-2026-10-26.md) and [the local launcher procedure](docs/development/live-preview.md).
Cloud authoring/new SDK launches stop after the source handoff is pushed. Local work
may continue under the existing coordinator after exact source/exclusive ownership
is observed; no replacement coordinator or extra approval loop is required. Preserve
parallel implementation, all WIP/failed checks, the installed package and user data.
The new Build template flow is WIP with a documented nonempty-target preflight gap;
finish that boundary before presenting it as complete. Full acceptance stays paused.

## Live desktop development and parallel implementation — 26 October 2026

The owner now requests a **live native development copy on the x64 PC**: report one bug, implement the fix, then rebuild/reload the SAME development instance for immediate retesting, rather than batching fixes into installers. An annotation dashboard is not the requested solution. Prepare/reuse supported local launch and coordination interfaces; verify actual running source/assembly identity and preserve the explicit profile and normal save/close lifecycle. Do not claim cloud source edits updated the PC without a local launch/reload receipt.

The owner also explicitly requests two parallel workers continue real specification implementation while bugs are repaired. This permits the scoped Build template/project-creation and Spaces task-permission-widget work recorded in the existing ledger; it is not authority to revive old teams or Thunder. Avoid a programme-wide hold. Keep source scopes exclusive and the SDK allocation serial. Full independent acceptance remains paused; actual affected compilation and smoke checks continue.

The first delivered test package (`59d66496`) has owner-reported lag, tab overflow and app navigation issues. Preserve its passing narrow startup/package checks and the new failures separately. The owner's package query confirms installation of that version, but did not identify the running executable; `/opt/9to1` in the recording versus `/usr/lib/haven` in the package remains an unresolved launch-identity observation, not proof of a stale app.

## Scoped desktop recovery — 26 October 2026

The owner also requests a current **Linux x86-64 / amd64 `.deb` test build**, delivered through the existing private OneDrive project folder. This supersedes the older ARM test-copy target for this delivery only. Use the original application composition, retain package/source hashes and basic startup evidence, and include known limitations. Test delivery does not resume full acceptance or establish release readiness; do not relax the separate ARM runtime guards.

The owner has authorized fixes for the supplied desktop recordings plus Dulche
model discovery, Hugging Face browsing/install with encrypted profile credentials,
a compact attachment menu and device-colour emoji. Read the [current requirements](docs/specification/owner-references/2026-10-26-desktop-recovery/requirements.md)
and [recovery evidence and remaining failures](docs/development/desktop-recordings-2026-10-26.md).
This permission covers the existing isolated recovery branch; the broader local
handoff/HOLD and Thunder custody below remain in effect. Basic affected checks
continue; full acceptance stays paused. The inspected desktop checkouts were clean
and already published. Do not ask for another push from those same checkouts or
claim the installed desktop was updated without a real package/install receipt.

## Current local transition — 25 October 2026

The owner requests local development takeover because cloud usage is low.
Cloud authoring is on HOLD after checkpointing. Read
[the current local continuation](docs/development/local-transition-2026-10-25.md)
and its JSON companion before resuming. This supersedes the earlier cloud-resume
instruction below. Preserve source, local sessions, WIP, failures and Thunder.
The existing local coordinator may resume after observing exact source,
exclusive scopes, actual model access and local build custody; a historical grant
or session ID is not a current ACK. Do not revive cloud workers automatically.

## Boards UI review reference — 25 October 2026

Apply [the later control hierarchy and Present-style toolbar clarification](docs/specification/owner-references/2026-10-25-boards-ui-review/owner-control-and-toolbar-clarification.md), including the separate Glow/Professional behavior.

Read [the retained Boards review and applicability notes](docs/specification/owner-references/2026-10-25-boards-ui-review/review-notes.md)
when changing Boards presentation. Also read [the later owner visual clarification](docs/specification/owner-references/2026-10-25-boards-ui-review/owner-visual-clarification.md). Recheck historical observations against the current app;
the canonical specification and explicit owner decisions retain priority. Keep Home global
controls shared and standalone/embedded Boards on the same implementation.

## Latest UI requirement amendment — 25 October 2026

Read [the adopted UI report requirements](docs/specification/owner-references/2026-10-25-ui-report/requirements.md).
Send-button stacking is an intended contextual shelf feature, not a layout bug.
Preserve the exact approved orders and genuine runtime acknowledgements. The
report's old screenshots/defect observations may be stale; recheck current code
and rendering before reopening them. Proposals are distinct from owner decisions.

## Latest owner priority — 25 October 2026

Finish implementation of Spaces, Home/Haven, Build and Boards first; these must support
continued local development when the owner's cloud usage runs out. Resume the
remaining apps after these four reach 100% implementation. Preserve completed
other-app changes and safe WIP checkpoints. Use actual GPT-6.1 Sol and GPT-6 Luna
subagents through supported interfaces. Prioritise end-to-end behaviours over
unused backend methods, and use the canonical specification plus the supplied
Progress Tracker to keep an explicit list of remaining requirements.

The latest owner addition includes Boards in this first group, with a separate
worker so Spaces, Home and Build keep progressing.

The owner requests copies for testing as each priority app reaches 100%
implementation. Prepare real runnable builds for the existing Linux ARM64 laptop
path, retaining source, package identity and actual smoke/install results. A test
build must state outstanding native/independent acceptance and known limitations;
never describe a compiled package as fully accepted. Full independent acceptance
remains paused under the existing owner instruction. Do not call an app 100%
implemented while known feature gaps remain.

## Owner resume — 25 October 2026

The owner explicitly resumes existing 9to1 implementation with GPT-6.1 Sol and
GPT-6 Luna subagents, aiming to complete the active apps as soon as possible.
This supersedes the 24 October cloud stop for this resumed work. Preserve local
work, the active separate Thunder worker, and frozen source backups; do not
restart the old OpenCode cadence or overwrite native source. Continue meaningful
implementation and basic smoke checks. Full independent acceptance remains paused.
Use the attached Progress Tracker as a source-backed queue; its historical model
and execution wording does not override this new request or the canonical spec.
Begin from the reconciled a4a0f7c integrated candidate and retained current changes.
Record exact ownership in the existing coordination ledger and integrate compatible
completed changes into main after applicable basic checks. Never infer100% from
an app entry, source-only patch, old logs, or successful package generation.

## Cloud stop and local-team takeover — owner decision, 24 October 2026

The owner requests the cloud worker checkpoint and STOP to avoid conflicts with
new local teams, and requests branches converge into `main`. The existing native
coordinator “Prepare local 9to1 teams” owns future source assignments and integration.
Read [the current takeover grant](work/coordination/local-teams/BOOTSTRAP-GRANT.json)
before resuming. Do not automatically revive cloud authoring, SDK work or a second
cadence process. Preserve existing native sessions, local repairs, worktrees,
private evidence and all original source history.

This main checkpoint consolidates the current composed source plus the frozen
Automations WIP by owner request. It is UNVERIFIED development source: combined
compilation, native runtime, ARM packaging and release acceptance remain NOT RUN.
It supersedes the earlier policy requiring source-only WIP remain off main for
this explicit consolidation handoff; it does not waive tests or acceptance gates.
Older branches remain preserved under the archive ref named in the grant, with
an exact inventory and real conflict evidence. They require individual content
reconciliation. NEVER merge the metadata-only archive tree or use an `ours` merge
to pretend that application work was combined. Full independent acceptance stays
PAUSED per the owner's latest implementation-first instruction.

## Local OpenCode teams — owner decision, 24 October 2026

Read [the team runtime procedure](docs/development/opencode-teams.md) and
[the portable source handoff](docs/development/local-handoff.md) before team
activation or reassignment. The owner requests GPT-6 Luna implementation workers
under GPT-6.1 Sol leaders, each owning three or four surfaces, and one GPT-6 Astra
supervisor. Sol checks every five minutes; Astra reviews every thirty minutes
with a five-minute cooperative checkpoint budget. Busy activations coalesce.
Use real OpenCode session tabs for the team leaders, preserving the web view.

Verify the installed API, exact provider/model entitlement, isolated worktree,
current source, exclusive paths and genuine session receipts before activating.
Never silently substitute models, revive Mercury or enable paid fallbacks. The
team adjunct extends the existing coordination ledger; it is not proof of local
write access, session creation or work execution. Root retains current source
custody until the receiving worker ACKs its exact revision and scopes. Complete
priority apps and dependencies first, then remaining canonical apps. Full
independent acceptance stays paused until all implementation is complete or the
owner explicitly resumes it; genuine basic compilation and smoke checks continue.

## Current execution priority and fresh basic builds

Acceptance remains PAUSED until ALL canonical apps are fully implemented or the owner explicitly resumes it. The latest24October owner override below supersedes the historical timed03:00 resumption. Continue implementation and genuine basic checks.

For a fresh normal Layer1 compiler run, keep the existing exact-source/current-SDK-host/command, normal dependencies/copies, private-writer, memory/disk, time, process and coordinator admission guards. Do not delay it for the complete toolchain closure that `reusable()` requires. Record incomplete dependency coverage explicitly as PARTIAL and NOT REUSABLE; never promote it into an accepted or reusable result. Rebuild affected producers before describing a UI as current. Full seven-criteria/native/install/release acceptance and immutable evidence reuse requirements remain unchanged. See docs/development/workflow.md.

## Temporary owner priority — implementation until 03:00 BST, 24 October 2026

Read [the latest implementation-window decision](docs/specification/owner-references/2026-10-23-implementation-window/owner-correction.json). Until **24 October 2026 03:00 Europe/London (02:00 UTC)**, prioritise implementation of feature gaps and basic smoke checks. Defer full acceptance and acceptance-only audits until that time; preserve their queue and evidence. Then resume independent acceptance from the exact latest candidate, unless a newer owner instruction changes the priority. This defers gates; it does not waive them or authorise an unaccepted release. Keep ordinary development, scoped Thunder work and progress reporting moving.

> **Current owner override (22 October 2026):** Mercury is revoked and must never be invoked; use Beam and Codex through supported isolated interfaces. Read [the durable current owner instructions](docs/specification/owner-references/2026-10-22-development-reliability/owner-correction.json) before any provider, worker, release or handoff action. This supersedes older Mercury-only, worker-count and schedule text below. At an actually observed **10% remaining Codex allowance**, hand over to a verified Thunder development worker; if unavailable, persist the handoff, commit changes and pause. Never infer the allowance from context tokens or other provider limits. Permanent workflow implementation and independent acceptance remain mandatory.

# Agent instructions — 9to1 specification v2

## Current operations — owner amendment, 22 October 2026

Read [the durable development workflow](docs/development/workflow.md) and [the latest owner amendment](docs/specification/owner-references/2026-10-22-development-reliability/owner-correction.json) at every startup, replacement and compaction. Mercury is revoked and must never be called or re-enabled. Normal development uses verified Beam or supported Codex agents. The owner's sole conditional exception permits a genuinely verified Thunder development worker at an actually observed 10% remaining Codex allowance; otherwise checkpoint, commit and pause at that threshold. The last owner reading was 99%; forecasts are not triggers. Keep credentials outside Git and never enable paid fallbacks. Retired provider/session history is evidence, not launch authority.

The registered coordinator bridge is the owner's laptop, not the RTX desktop. An online bridge or inference response does not establish desktop administration or a development worker. Preserve laptop workers and reconcile their shared inference custody before any new model request. The latest owner amendment records the separate remote-channel setup authorisation and actual verified scoped dispatch. The existing desktop worker accepted a typed task, wrote one exact file in an isolated worktree and returned its hash; this proves that task channel only. Direct SSH, reboot persistence, exclusive model custody and Thunder development-worker readiness remain unverified. Do not expose a public shell or replace the coordinator.

Resume the existing coordinator and cloud ROOT-LIVE checkpoint projection. Inspect actual worktrees, supported live workers, source identity and CI outcomes before assigning work. Use atomic compare-and-swap state updates, exclusive file scopes, dependency-aware checks and the existing heavy-build allocation. Development, verified integration and immutable release acceptance are separate. Preserve accepted-fix content lineage; reviewed RC fixes require retesting and back-propagation to active development. Reuse evidence only for its demonstrably unchanged candidate, command, platform, reviewed dependencies/toolchain and retained outputs. All mandatory release gates and all seven quality criteria still apply.

Run `python3 -B eng/development/workflow.py --state <existing-projection.json> recover --repo <actual-repo>` after reading this file. Follow its read order and next actions, reconcile discrepancies and inspect live sessions separately; recovery never proves endpoint access or native acceptance. Before interruption, checkpoint exact source/evidence, ownership, failures, uncommitted changes and next executable actions. No runtime compaction hook is currently configured: this startup procedure and independently tested fresh-context recovery are the supported fallback. Do not create a replacement project or overwrite the remote milestone ledger.

## Guarded SDK and native launches

Before every expensive SDK/native launch, call the read-only `eng/development/workflow.py execution-admission` check (or its `execution_admission` Python API) immediately before invoking the exact guarded runner. Require the actual current named candidate, export-manifest pin, guarded-runner file identity and adopted latest ROOT-LIVE `sdk_admission.runner` proof to match. A failed CAS/check must stop control flow; use a checked Python call or `&&`, never a separate failing command followed by an unconditional shell newline. The validator does not create permission, ownership or an allocation; preserve the existing coordinator admission, full-source/resource/time/process guards and all RC gates. Never refresh pins merely to make a refusal pass.

## Canonical main — owner amendment, 23 October 2026

The owner requests one consolidated `main` branch: parallel work may stay on branches while in progress, and completed work must be integrated into `main`. Root uses the existing coordinator, isolated scopes, independent review, applicable owning checks and verified-integration workflow for this consolidation. Integrate completed compatible changes promptly, preserving exact accepted-fix lineage, dependencies, source identity and evidence. Source-only or uncompiled staging remains on its development branch until its required integration checks pass.

`main` is the canonical engineering integration line, not an RC or a claim of app, native, ARM, package or release acceptance. Record every remaining gate and all seven quality criteria honestly. Component-specific admission, including Thunder qualification, still applies; this policy does not permit direct experimental promotion. Preserve active branches, original dirty work, recovery artifacts and historical refs; never bulk-merge branches or reset/rebase a worktree to consolidate them.

Use the existing repository access, publication rules and owner instructions when synchronising `main`. Keep credentials and private evidence outside Git, and do not publish unaccepted experimental work as accepted. This policy adds no new approval requirement. Follow the existing [development workflow](docs/development/workflow.md) and its CAS ownership and release-candidate rules.

## Pinned owner requirement — one implementation per app

Haven embeds the SAME standalone app implementations, canonical objects, data and services. Never maintain separate older Haven and newer standalone versions of an app. Host chrome/top bars may adapt to the surface as explicitly requested; app behavior and full applicable specification requirements remain shared. Existing Haven code may be reused only if these conditions hold. Where newer app implementations exist, merge complementary work where feasible or use the newer implementation when it is better. Choose against actual specification coverage and verified behavior, then consolidate into one shared app; do not preserve the older path merely because it is already wired. Verify actual production composition/routes and real runtime surfaces, not just names or duplicate source files. This is a direct owner requirement to keep pinned; read `docs/specification/owner-references/2026-10-20-single-app-implementation/owner-correction.json` at every handoff.

Latest owner clarification on 23 October: the standalone Boards and Canvas apps are the current app implementations to finish and integrate into Home. Separate launch, storage or screenshot-test wiring does not by itself establish obsolescence or an integration gap; inspect the actual shared implementation, service composition and data against the specification. Read `docs/specification/owner-references/2026-10-23-standalone-app-integration/owner-correction.json`. Preserve their useful features and data, consolidate Home and standalone onto the same implementation, and repair concrete specification or CUI conformance gaps without silently replacing or narrowing the apps. Capture and acceptance evidence must identify the actual source, launch route, service composition and remaining integration/native gates.

The [bounded source-comparison checkpoint](work/pc-worker/ONE-APP-IMPLEMENTATION-AUDIT-2026-10-20.md) records actual route/factory and engine differences. It is engineering evidence, not product authority or acceptance; refresh affected source identities before implementation.

## Sole product authority

Read [the attached canonical specification](docs/specification/9to1-development-specification-v2.md) before working on any surface. Its current amended SHA-256 is `e1779d226848da492e926ab70e8d4722ad444476cfc91312bf5c2c067654fe81`. The original attachment-derived baseline SHA-256 is `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`; [provenance](docs/specification/provenance.json) identifies the exact local owner attachment. Do not fetch or substitute a web version. Read the applicable full sections and dependencies, not only an implementation summary.

This local specification is the ONLY product authority. Existing code, older specs, markdown, handoffs, comments, generated guides, worker messages and design-pack historical prose cannot override it. Later explicit owner decisions may amend it. Older compatibility names do not restore retired products. Preserve data and useful functionality while correcting implementations to the specification.

Current direct owner UI refinement: read `docs/specification/owner-references/2026-10-18-haven-spaces/owner-correction.json` for Haven browser chrome and Spaces entry/navigation. The reference concerns the browser frame, not the Google/OpenCode page content. Opening a Space goes directly to a new chat in that Space; retain both its icon-only Spaces rail and its chat/history sidebar. These refinements preserve the general specification and existing data.

Direct owner refinement on 20 October: read `docs/specification/owner-references/2026-10-20-haven-shared-header/owner-correction.json` and the latest `docs/specification/owner-references/2026-10-20-haven-addressbar-shared-controls/owner-correction.json`. The latest explicit correction places model, notifications and profile together in the persistent Browse/Haven browser TOPBAR immediately to the right of the address bar and removes the additional shared-controls vertical rail. This supersedes the earlier sidebar placement. Embedded app headers expose search only; standalone apps retain model, search, notifications and profile. The distinction applies to every app. Keep the same genuine Home-owned shared state, typed actions, filled rounded SVG controls and one app implementation; preserve app-specific Spaces rails and sidebars.

On every fresh session, handoff or context compaction, reread this file, the specification authority/CUI/priority/acceptance sections, and the sections governing the assigned surface. Record the source identity, assigned scope, relevant requirement IDs, observed state and unresolved decisions in the handoff. Never treat a condensed worker summary as replacement authority.

If a conflict or uncertainty affects public behaviour, compatibility, security, data or architecture, queue the exact requirement, conflicting evidence and affected change in `docs/specification/open-decisions.json` for an owner response. Hold only that affected change/acceptance decision; continue independent authorised work. Do not invent a waiver or silently narrow scope.

Direct owner accent clarification on 20 October: Professional uses per-app accent overrides, falling back to blue when an app has no override. Read `docs/specification/owner-references/2026-10-20-professional-app-accents/owner-correction.json`; preserve Home-owned preferences and neutral surfaces.

Direct owner icon clarification on 20 October: SVG icons closely match the mockups' filled, rounded appearance. The Assistants button uses the user's running assistant, otherwise their most-used assistant, otherwise a generic icon if none is configured. Read `docs/specification/owner-references/2026-10-20-spaces-svg-icons/owner-correction.json` and resolve from real authorised state.

Direct owner composer/runtime clarification on 20 October: replace legacy chatbox with mockup/spec CUI presentation, correct text formatting and clipped shadow/glow. Bundled Dulche supplies the default path without an external-provider setup requirement. Dual is now a Plugin; remove the always-visible first-party Dual: Off control. Read `docs/specification/owner-references/2026-10-20-chat-composer-dulche/owner-correction.json` and verify real runtime/assets rather than hiding failures.

Direct owner Professional framing and composer correction on 20 October: read `docs/specification/owner-references/2026-10-20-professional-spaces-heading/owner-correction.json` and `docs/specification/owner-references/2026-10-20-continuous-composer/owner-correction.json`. The canonical specification takes priority over both illustrative images. The rough Spaces/Haven heading reference is Professional: ignore its mistaken decorative glow and fix the icons using the earlier filled, rounded SVG requirement. The composer must be one continuous surface without a visible inner box; include genuine formatting/emoji controls under the expanded-and-focused visibility rule and a genuine canonical permission selector, preserving the separately required persistent AI access mode without inventing unresolved approval-policy semantics. The owner further clarified that formatting is one Format (Aa) button which opens the floating toolbar; individual formatting commands live inside that toolbar rather than permanently in the composer controls row. The idle empty composer needs its waveform Voice toggle, using the full same-session Voice & Vision floating pill; Send applies to a genuine pending message while actual Stop/Resume/queue/edit states remain intact. This is pending required work, not a waived control. Do not copy illustrative model, account, warning or greeting state. Preserve one shared app implementation and Home-owned controls.

Later direct owner correction on20October: read `docs/specification/owner-references/2026-10-20-floating-format-bar/owner-correction.json`. Aa toggles a floating formatting bar on/off using compact formatting symbols such as bold B and italic I; the provisional word-labelled dropdown is not the requested toolbar. Correct the white backing behind popups through shared CUI hosting/fallback and vertically center contents in most visibly bounded buttons using shared control defaults. Preserve genuine action routing, selection/focus/undo and canonical CUI ownership.

## CUI and product boundaries

CUI is the only application-authoring language: structure, styling, state, interactions, behaviours, composition and orchestration use canonical versioned CUI and typed runtime/action contracts. C#, XAML/AXAML, HUI, HTML/CSS/JavaScript, React and native-control markup must not become application source or hidden code-behind. Mature donor engines, compiler/runtime/backend internals, OS adapters and external services may retain implementation languages behind reviewed typed infrastructure boundaries. Missing CUI expressiveness is a framework conformance gap to implement, not permission for a parallel authoring path.

Never load, instantiate, inherit, package as a CUI dependency or fall back to Avalonia FluentTheme or Fluent templates/resources in compiler/runtime, renderer, preview, designer, test host or shipping surface. SimpleTheme and other donor themes must not own first-party presentation. The approved Avalonia fork supplies low-level capabilities behind CUI-owned templates, resources, property precedence and states.

Haven is the browser-derived super-app; Home Core is its shared service/control plane. Spaces owns core Spaces and Space Builder. Voice & Vision is one canonical multimodal surface. Build is one app combining Dev/Sites/Stacks scope. Connect owns communications with Mail as its first-class email mode. Follow all identities and delivery exceptions in the specification; names in existing code are not authority.

## Execution and ordering

Complete frameworks and shared dependencies before dependent surfaces claim readiness. Follow the specification priority: Haven and its frameworks; Spaces/Voice & Vision; Build; Automations; Write/Present/Boards/Canvas with Data/shared productivity dependencies; Files/web services; Planner/Mail; then the remaining specified order. Implement essential lower-priority dependencies when higher-priority workflows require them without declaring the entire lower-priority product complete.

Use supported isolated Beam/Codex workers with exclusive scopes and useful dependency-ready assignments. The coordinator records each actual session/model/worktree/checkpoint; historical activation files or PIDs do not prove a live worker after restart. Never resurrect automatic provider retries, invent endpoint execution capability, bypass worker-launch restrictions or increase worker count without a justified benefit. Independent acceptance must be performed by a different reviewer.

A model finishing a reply is not programme completion. At a turn boundary, save concrete changes, checks, unresolved requirements and the next executable task; the coordinator may immediately continue the exact programme. Continue useful authorised engineering without asking for routine approval. Do not claim completion until independent acceptance verifies the applicable requirements. Honour explicit user STOP/PAUSE, genuine access boundaries, provider limits and resource safeguards. Preserve logs and checkpoints on errors; diagnose before retrying. Never restart unrelated services, bypass memory/build guards, remove locks blindly or enable paid fallbacks.

Preserve memory/OOM/build-concurrency safeguards. Use the actual environment's coordinator-approved guarded dotnet entrypoint with `-m:1` and shared compilation disabled; serialize SDK/native work through the existing allocation. Verify the entrypoint on this machine rather than assuming the PC path exists in cloud. Acceptance evidence directories belong to reviewers: implementations may copy unchanged probes but must not overwrite reviewer artifacts or logs. Hand off reviewed work rather than recreating it.

The current owner priority is heavy Thunder attention while continuing substantial ordinary 9to1 development. The owner subsequently extended desktop testing on 22 October to **23:45 Europe/London (22:45 UTC)** because connection setup consumed the original window. The later authenticated desktop-thread instruction supersedes the earlier22:00BST cutoff. Use the verified existing-worker task channel and normal native approvals; stop on revocation/window end. Scoped dispatch success does not prove exclusive inference custody or Thunder readiness. ARM64 packaging/onboarding/browser/local-AI acceptance remains required; no ready release or morning delivery promise without actual gates. Latest owner override: at **10% remaining Codex usage allowance**, hand over to a verified Thunder worker; if Thunder is unavailable, prepare a durable handoff, commit changes and pause. This replaces the older15%/two-conditions rule. The actual percentage is currently unexposed; do not infer it from context length, provider spending or rate limits.

## Verification and custody

Latest owner feedback, 23 October: show a small set of current screenshots during already-required owning UI/native checks so the owner can comment without delaying development. Capture changed screens and useful milestones; reuse an applicable unchanged build rather than rebuild solely for screenshots. Identify source/build revision, capture time, surface, theme, viewport and native versus headless rendering, and label older captures honestly. Missing or failed capture is retained as evidence, never replaced with a mockup or a claim that an unrendered candidate is current. Keep the half-hourly reporting arrangement.

Implementation performs focused authentic smoke checks and does not spend its whole turn repeating broad tests. Diagnose individual failures; do not roll back solely because one test failed. Independent acceptance tests the full applicable specification and all seven criteria: Usability, Product fit, Functionality, Integration, Separation and authenticity, User fit, Refinement. Acceptance may fix minor defects; significant defects return to implementation with precise affected requirements, repro steps and required evidence.

Keep engineering state separate from acceptance state. Working code, builds, test fixtures, mockups and screenshots alone do not prove completion. Retain FAIL, BLOCKED and NOT RUN, exact source/build/artifact identity, platform/theme/viewport, real workflow and persistence evidence. Never weaken, skip or manufacture tests to obtain a pass. Update obsolete expectations only to enforce the new specification while preserving regression coverage. Read APP-AC-001–009 and relevant app acceptance sections.

Use the supplied mockups for exact intended layout where identified; references illustrate features/design principles. They cannot override the specification or supply fake personal data. Apply Present's shared design principles to Write, Boards and Canvas while retaining their required app-specific layouts. Do not wait for future mockups.

Preserve other workers' changes, user data, historical evidence that does not direct conflicting behaviour, donor licences and notices. Conflicting markdown may be retired only after actual content review and an exact recoverable backup with original path/hash/reason. The authority reconciliation manifest records partial coverage; an inventory or keyword scan is not a full review. Do not edit unrelated code/configuration as part of document reconciliation.

Direct owner sidebar refinement on20October: the icon rail and secondary contextual sidebar need clear visual distinction. Read `docs/specification/owner-references/2026-10-20-sidebar-separation/owner-correction.json`; use theme-owned semantic surfaces and preserve collapse/reopen, readability and shared app state.

Latest direct owner composer-icon refinement on 20 October: read `docs/specification/owner-references/2026-10-20-composer-icons/owner-correction.json`. Use a random actual emoji trigger with the specified suggestive examples and all flags excluded from its icon pool; no unselected background. Use a significantly larger genuine SVG plus. Preserve CUI ownership, actual emoji data, focused editing and shared standalone/Haven behavior.

## Dulche Thunder invariants

Thunder is the 9to1-integrated embedded Strata engine within Dulche, not a separate engine. Read `docs/specification/owner-references/2026-10-20-dulche-thunder/owner-correction.json` and the latest owner amendment. Discover the actual integration, retain pinned upstream identity/licences and separate reviewed enhancements. No speculative performance claims, untested production promotion, duplicate runtime or direct main-branch merge. Compare reproducible real baselines and upgraded/optimised runs on verified owner hardware within the currently authorised window. If access is unavailable, report BLOCKED and continue independent work.

Latest direct owner refinement on 20 October: read `docs/specification/owner-references/2026-10-20-composer-control-coherence-and-browser-finish/owner-correction.json`. Standardise composer controls' optical size, spacing and treatment; keep secondary tools quiet, primary action distinct and visible focus outline. Keep the actual emoji and larger SVG plus requirements in a coherent design. Browser location is readable during normal viewing, with full canonical address on focus/copy; model/resource control needs clear meaning and genuine available percentage without inventing unknown status. Preserve CUI and shared canonical state/actions.

Latest owner eleven-point native UI acceptance refinements: read `docs/specification/owner-references/2026-10-20-eleven-native-ui-refinements/owner-correction.json`. Cover circular upward Send, lighter plus, coherent secondary tools, compact genuine model-recovery row, vertically centred address text, unified sidebar gutter/quieter empty state, joined selected-tab/close surface, grid/toolbox Spaces manager and actual CPU+reasoning-effort control. The only pending clarification is emoji glyph choice versus earlier random-actual-emoji requirement; advance all independent changes. Preserve focus, canonical actions and SPUI-133–136 percentage meaning.

Latest owner app-icon refinement on23October: read `docs/specification/owner-references/2026-10-23-home-app-icons/owner-correction.json`. Fill missing icons in the existing owner-provided family, give Canvas a whiteboard icon, reuse the old Canvas artwork for Create, and substantially enlarge app tile icons with real viewport/theme checks. Preserve original artwork, canonical identity/actions and shared standalone/Haven presentation; source generation is not runtime acceptance.

## Progress email format — owner amendment, 23 October 2026

Use [the latest owner reporting format](docs/specification/owner-references/2026-10-23-progress-email-format/owner-correction.json) and [reporting rules](docs/development/progress-reporting.md) for every next half-hourly update. Start with 👀 Progress (implementation and independent acceptance by app), then 9️⃣ 9to1 Progress (changes per app since the last successful email), then 🌩 Thunder with actual quantitative evidence.

The latest owner instruction requires useful rough engineering point estimates for every app and supersedes the previous complete-denominator-only / Not yet measured fallback. Give an implementation percentage for behaviour present in the current integrated candidate and a separate Accepted percentage estimating the portion of applicable app scope independently validated. Label rough values clearly as estimates, grounded in current source, observed behaviour, retained reviews and known gaps; use measured percentages when a complete current applicable manifest supports them. Record the source/revision, date and brief basis, and explain scope changes or recalibration. Do not include private unintegrated fixes, copy illustrative percentages, or convert test/task pass ratios into app completeness. Partial or estimated Accepted percentages never establish app readiness or waive failed or missing mandatory gates. Preserve existing private cadence/recipient/send receipts and screenshot freshness labels. The superseded owner policy is retained as history in the owner-reference record.

Native ARM candidate identity: use the committed-tree generator and focused Git-fixture tests described in `docs/development/workflow.md`. Regenerated hashes never confer acceptance.

## Python maintenance tools

Follow [the Python maintenance procedure](docs/development/workflow.md#python-maintenance-tools) for committed native-input identity, explicitly closed Root alternative-index archives and deterministic benchmark stream checks. Preserve the original source base, every prior input pin, historical evidence and the existing coordinator. Use fresh private outputs outside Git/worktrees. Preparation does not authorise cleanup, inference, SDK execution or promotion; current desktop/provider limits and all native/package/release gates still apply.

## Latest owner execution override — 24 October 2026

Read `docs/specification/owner-references/2026-10-24-implementation-first-until-complete/owner-correction.json`. Independent acceptance is PAUSED until every canonical app reaches100% implementation, or a later explicit owner instruction resumes it. Implement features and fixes; run compilation and focused authentic basic smoke checks. Do not wait for full acceptance to integrate WIP into the development candidate. Freeze Accepted progress estimates, retain all existing evidence, and do not call unverified work accepted or release-ready. This supersedes the earlier03:00BST automatic acceptance resumption. Full scope, CUI/same-app requirements, resource/custody safeguards and future release/install/native gates remain.
