# Native 9to1 migration assessment — 26 November 2026

## Scope and authority

This records the Spaces, Build, and Dulche migration work against the owner-supplied **v5** references. It distinguishes source changes and deterministic tests from live native or hardware acceptance. The older v4 repository links do not supersede the current owner-provided v5 documents.

References reviewed on the development machine:

- Atlas: `/home/jacob/Downloads/9to1_Interface_Atlas_v5_Complete.html`, SHA-256 `9ce46ba954c95ccc025d17f2a5564c9e3b8adf53b4c752650dc725305c1841e6`.
- Specification: `/home/jacob/Downloads/9to1_Specification_v5_2026-11-26/9to1_Development_Specification_v5.md`, SHA-256 `83d83b5d382669132409e6b7e522830f3d7e48eec18804485f5f40594862ee5b`.
- v5 precedence and independence requirements: specification lines 3–22. The Strata optimiser and direct adapter controls are also explicitly authorised by the current migration request; they are not represented as pre-existing Atlas requirements.

## Requirements, changes, and limits

| Surface / requirement | v5 reference | Change or existing behaviour | Acceptance still required |
|---|---|---|---|
| Spaces: Send can start its selected installed model | RUN106; PICK001–009, lines 434–450 and 900 | Dulche advertises `can_start`; the native provider lists eligible installed models while selection remains lazy. The daemon owns admission, cold start, and the original durable run. Token counting uses the same central start path. | A real native Send from an unloaded model, with exactly that model loaded and no substitute/download. |
| Spaces: genuine tools and vision | PICK001–009; canonical provider route | The socket provider uses the current daemon capability snapshot. Images and typed tool histories survive transport; tool fragments are assembled and validated before executable calls return. Missing/unsupported capabilities are refused. | Actual ALORA image and tool round trips through the native interface; source tests are not a model capability benchmark. |
| Spaces: Specialist adapter identity | APPAGENT003; IXSPACES051; current owner request | Native Specialist selection is the canonical `AgentDefinition.Id`, captured in immutable per-send `GenerationOptions.SpecialistId` and forwarded on text and tool requests. Backend bindings resolve a matching Specialist before the model default. Deselecting uses a fresh null scope. | Focused SDK rerun after the typed identity addition; actual selected Specialist → compatible loaded adapter request. The adapter scope field currently requires the canonical ID. |
| Spaces: durable draft and exact admission | IXSPACES025, lines 23063–23083 | Existing committed-input/draft retirement paths are retained. Binding identity is snapshotted at central queue admission; a later selection change cannot silently alter an admitted request. | Native draft/attachment/retry acceptance and recovery evidence for the final released version. |
| Build: cold selected worker/supervisor | RUN106 and v5 independence chain | `DeveloperModelInfo` now accepts explicit central start capability and conservative installed/unloaded/stopped/idle legacy states. Explicit unavailable selections are not replaced. One eligible model may serve both roles sequentially. | Native Build cold start, project read/edit, test, review, and durable continuation on the released service. |
| Build: truncated model output | RT020–027; explicit output-limit migration concern | Only a normal terminal `stop` may reach action/review parsing. A `length` response is retained in an incomplete-output receipt and cannot mutate files, memory notes, or approval state, even when its partial JSON parses. Early EOF requires both a terminal choice and `[DONE]`. | A native interrupted response and retry demonstration. This is output acceptance protection, not an LLM compaction implementation. |
| Build: compaction retention | RT026; RUN211–217, lines 922 and 988–1024 | Existing deterministic context epochs retain canonical transcript and recall. Failure to fit the minimal projection leaves the prior active epoch/history intact; a later retry is tested. | Full v5 Cumulative/Summary/Disabled selection, candidate journal, independent audit, and revision-safe activation have **not** been implemented or certified by this pass. |
| Build: project/interface parity | APPBUILD001–004; Atlas `atlas-v5-build-code`, lines 14921 onward | Current workbench functionality and mission controls were audited. Cold-model selection was repaired without replacing the existing owner. | The complete v5 `build-home`, `build-new-project`, `build-open-project`, `build-project` journey, templates/draft identity, and visual layout require a separate parity pass. Current vertical workbench layout is not a visual acceptance receipt. |
| Dulche: optimiser and rollback | INF001–005; PERFBASE004; current owner request | The management API invokes installed Strata `tune auto` with an exact source snapshot, `--no-save`, existing context, physical lock, supervised runner, and durable progress/result. Exact-answer candidates are allowlisted; source edits, context/precision/guard changes, unreliable results, and interrupted jobs cannot be silently accepted. Overlay and rollback leave the source profile unchanged. | Guarded runner/controller integration and live equivalent-workload measurements. Capability is backend-specific; see ALORA limitation below. |
| Dulche: adapter attachments | Exact base/dependency contracts; current owner request | CPU/local llama.cpp GGUF LoRA bindings persist by model and optional Specialist. Exact base shard and adapter hashes, format, architecture, and target pairs are validated. Engines load adapters at zero scale; each request explicitly selects one scope and zeros every other adapter. Detach does not merge or rewrite base weights. | Compatible real adapter load and inference. A newly selected path waits for a cold start/reload; it cannot silently dispatch base-only inference. PEFT directories, fixed V100 profiles, pooled/native backends are not falsely advertised as supported. |
| Inference: crash and stop recovery | RT020–027; INF001–005; AC13 | The surviving central controller owns loading/recovery and stop intent; interrupted output does not replay side effects automatically. Managed ALORA reuses its installed guarded service and pinned launch identity. | Distinguish a deliberate stop, genuine crash, thermal refusal, startup failure, and service restart in final live receipts. |
| One model across both GPUs | INF001–005; VERIFY007 | The reconciled Inkling pooled route represents one model using RTX 5080 plus isolated V100; independent endpoints are not described as pooled inference. | A successful single request with current attributed work on both devices and guard/cancellation evidence. Existing Python tests only validate contracts. |

## Current hardware identity and honest capability boundary

The owner-selected ALORA model is `qwen3.8-flash-next-alora-multimodal-20261126`. Its registered source is `/home/jacob/.local/state/alora-mm-thunder-20261126/server.json`; its unit is `alora-mm-thunder-20261126.service`.

The inspected source declares **524,288 tokens** and **RTX 5080** placement. It uses a custom `build/strata` executable, the exact Flash-Next base shards, `--alora-merged selected-experts.f32`, its selected vision reader, RoPE choices, memory reserves, and guarded environment settings. Its fixed adapter is visible as part of the model profile; it is not a generic detachable PEFT binding. The export manifest identifies a `thunder-alora-merged-f32-v1` expert overlay and exact source identities. This pass does not claim a new full-weight rehash or new GPU parity qualification.

**ALORA is not currently the dual-GPU pooled model.** The pooled Inkling route is a different model and experimental serving path. Neither enumerating both cards nor running separate prompts on them satisfies VERIFY007 for ALORA.

The observed earlier ALORA outage followed an explicit requested stop at 19:44. That observation does not prove a spontaneous crash or automatic crash recovery. No such proof is invented here.

### Why optimisation is unavailable for the managed ALORA profile

`managed_serving.py` pins `server.json` and launcher hashes, uses the fixed installed unit, and rejects changes to its verified settings. The new tuning overlay is read by `engine_profiles.prepare_native` for the separately managed PRIMARY Strata route. The ALORA launcher does not consume that overlay. Changing its source to apply a candidate would invalidate its registered source identity; measuring the official unadapted PRIMARY route would measure a different workload.

The capability response therefore reports the managed ALORA route as unsupported for optimisation. Enabling it safely requires a controller-owned candidate configuration that retains the **same ALORA binary, base, expert overlay, 512k context, RoPE, draft/vision choices, and guards**, an explicit verified launch-pointer/receipt transition, measured acceptance, and rollback to the prior exact configuration. The current optimiser button must not imply that this qualification already exists.

## Evidence ledger

Evidence directory: `/home/jacob/.local/state/9to1-development/native-migration-20261126`.

| Cohort | Observed result | Evidence / qualification |
|---|---|---|
| Build reliability and mission tests | 35 passed, 0 failed | `build-reliability.log`; `dev-test-results/native-migration-dev.trx`. |
| Existing Desktop Home/provider tests | 32 passed, 0 failed | `desktop-inference-3.log`; `desktop-test-results/native-migration-desktop-3.trx`. The earlier controlled Unix callback timeout is superseded. These 32 existing cases are separate from the seven new provider cases below. |
| Integrated service/lifecycle/maintenance/manager/tuner cohort | 116 passed, as reported by the owning worker | CPU-only integrated regression after controller maintenance, early manager initialisation and queue binding snapshot hooks. Includes earlier lifecycle and some separately listed management/tuner cohorts; do not add counts as unique tests. |
| Inference management and HTTP routes | 18 passed, 0 failed | `python3 -m unittest -v test_inference_management`, executed in the canonical service source. Includes scope isolation, exact identity checks, operation restart/rollback, and endpoint routing. No live inference. |
| Pooled and thermal regressions | 130 passed, 0 failed | `pooled-thermal-regression.log`; CPU-only contract checks, not a dual-GPU inference result. |
| ALORA launcher | 7 passed, as reported by the owning worker | Launcher repository commit `e71ae41`; source/self-start ownership checks. No replacement of the trained artifact is claimed. |
| New supervised tuner runner | 12 passed, as reported by the owning worker | CPU-only process/guard contract tests. No live twenty-minute tuning trial. |
| New migration provider and Specialist cases | 7 passed, 0 failed, 0 skipped; actual TRX names verified | `desktop-migration-owning.log`; `desktop-test-results/native-migration-owning.trx`. Explicit owning project compile include corrected. Discovery without startup, images, fragmented tool arguments, truncated tool refusal, capability refusal and both Specialist/default-scope cases executed. |

These are separate cohorts, sometimes overlapping the same code paths. They are not aggregated into a fabricated count of unique tests. Deployment/native UI/hardware receipts must be appended by the final integration owner after observing them.

Managed native service coverage: 15 CPU tests passed on the desktop, including actual harmless loopback rich requests, source pins, ready PID/cgroup/listener identity, exact-unit startup/stop, surviving children and startup failure cleanup. No GPU workload is implied.

## Source ownership and API

- Build action/context boundary: `9to1 Workspace/Dev/Autonomous/DeveloperAutonomousMissionEngine.cs`, `DeveloperAutonomousMissionContracts.cs`, `DulcheUnixSocketGateway.cs`; owning reliability tests in `Dev/Tests`.
- Native transport: `9to1 Workspace/shared/src/Haven.Desktop/Services/DulcheSocketModelProvider.cs` and `DulcheLocalClient*.cs`; shared message construction uses `Haven.Infrastructure/Providers/CloudModelProviders.cs`.
- Management: `eng/dulche/service/inference_management.py`, `engine_profiles.py`, `inference_tuner.py`; process/model custody remains in `controller.py`, `jobs.py`, `engines.py`, and `managed_serving.py`.
- Routes: `GET /v1/inference/capabilities`, `GET /v1/inference/operations/{id}`, `POST /v1/models/{id}/optimise`, `GET/POST /v1/models/{id}/adapters`, `POST /v1/models/{id}/adapters/detach`. The donor `/v1/resources/training-window` route is restored. Per-run stop waits remain outside the controller lock.
- GGUF LoRA selection requires an adjacent `<adapter>.gguf.dulche.json` with schema `dulche-lora-v1`, backend `llama.cpp`, exact `base_model_id`, ordered `base_files` entries (`path`, `sha256`), and `adapter_sha256`. It is a local identity record, not model code. First verification streams every base shard and can take time for a large checkpoint; subsequent unchanged artifacts use the process cache.

## Remaining interface parity

The shared picker still requires a v5 acceptance pass for actual pending/applied context controls, participant/Assistant operations, independent Vision route selection, equal role tabs, and a full-width reasoning control. Specialist attachment controls must resolve canonical identities rather than invent a model from a display name. The Atlas is a reference, not evidence that a prototype's stub is connected to production.

Completion of this migration should be reported only at the level observed: source checked, deterministic tests passed, release deployed, native interaction verified, or GPU workload qualified. In particular, the current work does not certify full v5 interface parity, ALORA dual-GPU inference, ALORA automatic optimisation, or the complete RT026 compaction journal.
