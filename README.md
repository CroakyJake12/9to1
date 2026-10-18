# 9to1

The [attached 9to1 Development Specification v2](docs/specification/9to1-development-specification-v2.md) is the sole product authority. It is the exact local owner attachment from 18 October 2026, recorded in [provenance](docs/specification/provenance.json). Do not substitute web copies or older repository instructions. Read [AGENTS.md](AGENTS.md) before work, including after a handoff or context compaction.

Haven is the browser-derived super-app; Home Core is its shared service/control plane. Spaces owns core Spaces and Space Builder. Build combines Dev, Sites and Stacks into one application. The specification defines all other product identities, complete scope, dependencies, delivery surfaces and acceptance requirements.

Applications are authored only in CUI, including their state, behaviour and orchestration. Infrastructure/donor internals retain permitted implementation languages behind typed boundaries. Avalonia supplies approved low-level capabilities; FluentTheme and donor theme presentation do not belong in CUI. Existing nonconforming code is migration work, never authority to weaken this contract.

## Source and evidence

- `framework/CUI/` contains the shared CUI language, compiler/runtime, themes and backend infrastructure.
- `9to1 Workspace/` contains application and shared service sources; legacy directory names do not define the product roster.
- `apps/`, `cloud/`, `9to1 OS/` and `9-1 OS (Android)/` contain delivery/platform sources.
- `docs/development/unified-host-tasks.md` and other retained engineering records are historical implementation evidence, not product authority or automatic proof of current acceptance.
- `reference/` contains historical/donor evidence. Preserve applicable donor licences and notices.

The repository remains under development. Readiness requires exact-source/runtime evidence for all applicable requirements and all seven acceptance criteria. A successful build, shell, test fixture or partial checkpoint does not establish completed product acceptance. [Open decisions](docs/specification/open-decisions.json) hold unresolved owner decisions without blocking independent authorised work.

## Build entry point

The existing root orchestration entry is `eng/9to1.ps1`; consult its current component help and the active coordinator's memory/build-concurrency safeguards before running it. Select focused authentic checks for the changed dependency graph. Commands and old status documents cannot waive the specification.

Third-party provenance is indexed in [THIRD_PARTY.md](THIRD_PARTY.md). Preserve upstream licence and attribution obligations.
