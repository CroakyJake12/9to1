# AI Studio implementation checklist

Status reflects this checkout and its verified tests as of 2026-09-26. PASS means the Studio-owned behavior is implemented and covered by direct code/test evidence; it does not imply unavailable shared services are integrated.

## Verified in this lane

- [x] Distinct Build, Test & Debug, and Resources navigation; Agents remain canonical Den entities rather than local project copies.
- [x] Versioned Harness and Skill/Plugin/MCP starter projects, project search/list/open, optimistic version checks, history, pinning, safe project files, soft-delete recovery, duplicate, import/export, and persistence.
- [x] Structural Harness and Tool validation, dependency inspection, and generated Markdown project documentation APIs.
- [x] Versioned local Evaluation, Test Suite, Schema, Model Router, Generative UI, and Run Profile resources with JSON authoring, revision history, rename, delete/recovery APIs; saved Evaluation/Test definitions have run actions and persisted failure records.
- [x] Deterministic evaluation facts, target revision checks, baseline comparison, bounded repetitions, and deterministic test assertions. Unavailable targets are recorded as failures; subjective graders remain pending review.
- [x] Action catalogue declares permission, risk, reversibility, side effects, affected object and scope for operations.
- [x] Missing runtime, Agent, Home permission, context, and replay adapters return structured unavailable results without simulated success or dispatch.
- [x] Release build: 0 warnings, 0 errors. Studio test suite: 17 passed.

## Partial / awaiting shared owners

- [ ] Den-backed Agent Builder CRUD/revision/validation/activation/share and browse/search, availability, default model, Quick Actions, and canonical capability references. The host exposes an adapter seam only; Worker 12/Den contract is required.
- [ ] Dulche Harness execution, Playground runs/effective runtime details, tool build/install, and evaluation/test execution against canonical targets. Runtime adapter is absent; obtain Worker 11/Dulche contract.
- [ ] Home-mediated permission simulation and grants/audit integration. Home must resolve permission before protected calls; current adapter fails closed. Obtain Home permission API contract.
- [ ] Home Node Graph Framework integration for advanced Harness graphs. Current project definitions retain graph JSON; no substitute graph editor is represented as integrated.
- [ ] Context Inspector and Replay against real runtime provenance and Action Graphs. Current adapter seams fail closed pending the runtime contract.
- [ ] Model/Agent/Skill/Plugin/context/Run Profile selectors in Playground, and complete run result panel including effective model, context, actions, permissions, usage/timing, errors, and Action Graph. Execution is unavailable.
- [ ] Evaluation/Test UI currently authors and persists versioned JSON definitions and launches runs, but needs form-based case/grader/assertion editors and live service integration.
- [ ] Schema compatibility/dependant impact and model capability checks require canonical dependency/model registries.
- [ ] Interactive UI appearance and keyboard/screen-reader behavior are not verified in this host: Studio launched and responded, but the available computer-use surface exposed no app windows.

## Not complete

- [ ] Acceptance coverage for all AI Studio specification requirements is not complete. Do not mark Worker 23 complete until canonical Den, Dulche runtime, Home permission and Node Graph dependencies are integrated, then UI acceptance and shared-service tests pass.
