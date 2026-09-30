# Project Astra la Vista — working evidence

Product authority: [current 9to1 Development Specification](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit), amended by the user's replacement launch instructions and configurable-subscription update of 30 September 2026. This ledger records evidence and ownership; it does not define product scope.

Baseline: clean `work` checkout at `98a0882`, repository `CroakyJake12/9to1`, prepared cloud environment `/workspace/9to1`. Current Google Doc refreshed 30 September at 10:40 UTC: one tab `t.0`, 9,635 content elements, 9,634 paragraphs, no tables; modified 30 September at 10:34:06.278 UTC. The prior complete snapshot is retained for comparison; substantive additions are reconciled without removing earlier requirements. Complete indexed source and exact assigned ranges are retained at `/workspace/astra-source/`. Future Apps & Features at refreshed source index 1,238,669 onward are explicitly outside release-candidate scope; all earlier product sections remain assigned.

Exactly four implementation workers requested with explicit `gpt-6.1-sol` configuration; all four accepted and running, no nested delegation. Effective runtime model metadata is unavailable, so independent verification is not claimed. The coordinator handles allocation, integration, review and evidence.

## Ownership

| Worker | Canonical source sections | Implementation paths and balance |
| --- | --- | --- |
| 1 | CUI; Home; Vision & Voice; Dulche and External App Integrations; 9to1-OS, Go, Model Picker, compatibility | `framework/CUI/`, Home, `apps/Home/`, OS, Android, Models; cross-app shared Core/Application/Infrastructure/UI/Connector/Browser. Platform/framework integration carries the workload. |
| 2 | Files, Connect, Mail, Spaces, Write, Present, Boards, Planner, Automations | Corresponding Workspace directories; all shared Desktop and its tests. Existing mature app code offsets broad product/UI coverage. |
| 3 | Games, Play, Canvas, Picture, Wave, Motion, Data, Forms, Browse, Maps, Launcher, Shelf | Corresponding Workspace directories and product-specific shared domain/service paths. Media/data/donor integration carries the workload. |
| 4 | Accounts & Subscription including configurable pricing, Profiles, Business and 9to1 Admin; AI Studio, Dev, Stacks, Sites, Terminal, Mini Computer; web delivery | Corresponding Workspace directories, `Accounts/`, `Admin/`, `Web/`; root/shared dependency and build metadata, `eng/`, `.github/`, `.opencode/`. Server/developer/publishing work carries the workload. |

App-specific shared Core/Application/Infrastructure subdirectories transfer to the matching product owner: Worker 2 owns Boards/Documents/Notes/Planner/Present/Spaces/Tasks/Write/Automations/Mail; Worker 3 owns Canvas/Data/Forms/Maps/Media/Play/Shelf; Worker 4 owns Terminal/CodeIntelligence. Mixed entity/model files require an explicit file handover. Generic shared services and DI registrations remain Worker 1, with `VersionedAtomicSettingsStore.cs` and its focused recovery tests explicitly transferred to Worker 3 for the current persistence repair increment. `Infrastructure/Persistence/SQLite/PlannerRepository.cs` and its new focused test explicitly transfer to Worker 2 for canonical Planner conflict/deletion repair. Shared Desktop remains Worker 2. Worker 4 is the single build/dependency/lockfile owner; project metadata edits are coordinated with the product owner. Work shares one checkout with disjoint ownership; no worker commits independently.

## Shared contracts and acceptance

Reuse existing canonical domain entities, stores and typed APIs. Worker 1 owns Home service handshake and CUI semantic AI contracts; product workers expose permission-filtered stable IDs/revisions/actions; Worker 2 integrates Desktop surfaces; Worker 4 hosts web adapters against the same domain model. Read-only AI must not mutate. Data requires per-action approval; live database mutations additionally require meaningful preview, validation and recoverable backup. Computer Use requires explicit invocation and excludes games.

Every assigned source requirement must be accounted for. `IMPLEMENTED` records code, `VERIFIED` records the named observed check, `BLOCKED` records a concrete missing prerequisite, and `UNVERIFIED` records missing evidence. These labels apply to bounded requirements, never imply whole-product completion. Runtime, donor parity, security, accessibility, performance and platform/package checks remain required where the source requires them.

## Current evidence

| Requirement/increment | State | Evidence |
| --- | --- | --- |
| Full current source retrieval and four-worker allocation | VERIFIED | Current connector read, complete indexed snapshot, four accepted worker launches. |
| Prepared build tools | VERIFIED | .NET SDK 10.0.301/runtime 10.0.9 installed at `/workspace/astra-tools/dotnet`; PowerShell 7.5.3 at `/workspace/astra-tools/pwsh`. Writable CLI/cache paths selected without changing HOME. |
| Shared integration | VERIFIED, bounded | Coordinator `integration-core-04.log`: Release markup 31/31, Home 57/57, Spaces 26/26; package metadata validation passed. Package bytes absent; CUI migration reports 70 AXAML and 30 HUI files. This gate is not whole-product release evidence. |
| Shared compose identity, typed action schemas and Computer Use eligibility | VERIFIED, bounded | Worker 1 CUI AI 19/19; Dulche runtime 17/17; Home 57/57 includes three canonical Agent presentation checks. Full app UI/runtime integration remains UNVERIFIED. |
| Durable Files and Connect consent | VERIFIED, bounded | Worker 2 domain checks cover restart, owner identity, stable revisions, Trash/restore, replay, ancestry cycles; Connect consent withdrawal/departure checks pass. Shared Planner focused mutation 1/1; Plugin sidebar 2/2. Full donor parity, network transport and platform UI remain UNVERIFIED. |
| Settings recovery, Maps/Shelf/Canvas/Play, native media | VERIFIED, bounded | Worker 3 focused shared tests 30/30 include explicit settings schema and real disk recovery plus native GStreamer WAV decode/seek/play/stop using CI sinks. Browse WebMCP binding 4/4; actual browser/provider interoperability remains UNVERIFIED. |
| Native Data dependencies and existing worker workflow | VERIFIED, bounded | Coordinator `integration-data-workers-03.log`: DuckDB 1.5.5 query/limit/confinement and LibreOffice 25.2.3.2 Calc edit/recalculate/save/reopen pass through actual processes. Worker 3 .NET Data runtime passed; full Data parity/platform/package acceptance remains UNVERIFIED. |
| Trusted subscription resource/threshold and usage enforcement | VERIFIED, bounded | Coordinator Release Accounts checks passed; concurrent reserve/run/settle, resource/price separation, PKCE/session revocation and quota checks exercised. Hosted multi-host state remains UNVERIFIED. |
| Configurable subscription builder | IMPLEMENTED, verification in progress | One server calculator covers full allowances, shared costs once, true 15% tax-exclusive margin, independent fee bases/rounding, custom quantities and account-bound durable quotes. Production costs/1x Dust/service limits remain explicitly missing; UI, trusted billing/settled allowance activation and boundary tests are in progress. |
| All remaining mandatory source requirements | UNVERIFIED, assigned | Workers remain responsible for their full source sections; first increments do not close products. |

## Blockers and boundaries

No configured Cloudflare credentials are available. This blocks live publishing, while Sites capability, authoring and release artifact work continue. Production DNS changes, live-site replacement, WordPress retirement and purchases require specific user approval. Production content must be preserved. No primary-branch merge, force-push or destructive reset is authorised by this pass.

The explicit user amendment replaces preset-first purchase with AI-multiplier then storage selection, one server-authoritative configurable quote/option/checkout calculation, actual Dust displays and a live breakdown. Target is 15% margin on tax-exclusive revenue after approved full-use resource, infrastructure/operating and final-charge-dependent billing costs; shared costs count once. Provider costs, the 1x Dust definition, service limits, applicable fees/tax and any currency conversion remain configurable inputs with precise missing-value errors, never invented production values. Free/local functionality, model-access rules and hosted-site entitlements remain intact. Other canonical OPEN decisions remain OPEN. Platform/device availability and donor runtime prerequisites are verified per increment, rather than inferred from historical notes.

Delivery targets: substantial integrated progress by Wednesday 30 September evening and completion by Friday 2 October night, Europe/London. Targets do not relax scope or verification. Existing source, licences, provenance and useful technical documentation are preserved. Removed obsolete coordination/completion notes are recoverable from baseline Git history and `/workspace/astra-source/recovery/`.

The same four worker sessions hit a usage limit, then resumed after the user confirmed the 11:03 UK reset. No replacement or additional worker was created. Refreshed additions include composer format/emoji/language controls (Worker 2); profiles/reserved handle, Business and new Admin app (Worker 4 with existing app/OS owners); Agent contact via Connect (Workers 1/2), Agent avatar runtime/editor (Workers 1/3/4), per-Space Plugin sidebar (Workers 1/2), and browser WebMCP plus remote site-hosted MCP (Workers 1/3).

Continuation: retain the same four workers and ownership; integrate tested increments continuously, return failures to owners, and preserve exact commands/results in `/workspace/astra-source/workerN-evidence.md` and integration logs. This record does not promise execution after the task stops.
