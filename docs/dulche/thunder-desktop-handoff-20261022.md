# Thunder integration handoff — 22 October 2026

This is a source-review checkpoint for the genuine local development worker. It does not start a worker, deliver a command, enable remote writes, promote an engine, or establish product acceptance. Read the current authorized owner instructions privately before using local hardware or providers.

## Mission and product authority

Thunder is the maintained embedded Strata backend inside Dulche. Dulche owns model management, routing and inference for9to1. Maintain one Home-managed broker and one common typed runtime contract rather than a separate upstream application or duplicate server. Substantial ordinary9to1 work continues in parallel through its existing owners.

The canonical attached product authority is [9to1 Development Specification v2](../specification/9to1-development-specification-v2.md), SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`. Read its authority, priority, CUI, privacy and acceptance contracts, Dulche framework role, complete multi-engine runtime and relevant agent/tool/Task/Run sections. Later explicit owner decisions override conflicting historical provider, scheduling and custody notes. Preserve existing sessions, worktrees, dirty changes, failures, source pins, licenses and acceptance records.

llama.cpp remains the broad/default local backend. Strata is capability-selected for compatible models behind the same load/prefill/decode/context/KV/multimodal/cancel/stream/usage/error/shutdown boundary. Automatic routing rejects incompatible engines before using real local measurements. Manual selection must be honored or return structured incompatibility; it must not silently substitute engines. Automatic fallback preserves model identity and required capabilities. Preserve canonical conversation/Task/Run identity, context ownership, permissions and recovery across switches. Behavior-changing experimental flags cannot silently win performance comparisons.

Applications use canonical CUI and one shared standalone/Haven app implementation, data and services. Native runtime and infrastructure internals remain behind typed boundaries. The full applicable specification and seven acceptance gates remain mandatory.

## Immutable source identities

Official release metadata was rechecked on22October2026 at18:44UTC. The latest stable upstream is [Strata v0.2.2](https://github.com/Niko1221/Strata/releases/tag/v0.2.2); its annotated tag resolves to `011c9e600a93bbc9fd451c563501b5831c32bbca`, whose [CMakeLists.txt](https://github.com/Niko1221/Strata/blob/011c9e600a93bbc9fd451c563501b5831c32bbca/CMakeLists.txt) declares engine `0.1.47`. This does not identify any currently installed local engine.

Public native integration source: `76fbd02073348f2bbca376c66049a0c01dff56a2`. [Artifact audit run39670603043](https://github.com/CroakyJake12/9to1/actions/runs/39670603043) succeeded at that source. [CUDA compile/install/package run39660364662](https://github.com/CroakyJake12/9to1/actions/runs/39660364662) succeeded at `0749524b904a3b8035d68b2915197293c87fbc11`. These are9to1 CI runs and static package verification, not local GPU/model/runtime acceptance. Static ELF inventory records CUDA13 runtime dependencies; verify the actual local environment without changing drivers or system configuration.

The preserved candidate source checkpoint is `eb0da4f082b2e82512b22ffabf17a28a3057fafd`, based on `37ba63a92a509013435d61af73cbe17e246167bf`. The [source manifest](thunder-source-checkpoint-20261022.json) pins61 changed files and their provenance. This combines23 managed owning-code/test leaves with the public native/package/test/CI closure. It retains the newer base's compatible contracts and developer source-edit APIs. The separately preserved owner-specific observation probe remains at existing public revision `080f6ba0ec3d3b7a7763a504fa152b3cb64b758a`; its network-specific files are excluded from this checkpoint.

**This assembled checkpoint has not been built or tested.** Existing component evidence belongs to its exact earlier tested source graph. Do not transfer that acceptance to this new combination. Review and run the owning normal tests before integration; do not merge to the production branch or install by assumption.

## Actual integration map

| Responsibility | Repository locations and behavior |
| --- | --- |
| Native candidate and packaging | `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/`: candidate relay, owned child-process support, CMake, package builder, dependency inventory, update preparation and explicit `prep/strata-v0.2.2-lock.candidate.json`. Legacy `strata-source-lock.json` remains the baseline/rollback lock. |
| Common contract and adapter | `9to1 Models/Dulche Alpha/InferenceEngines/InferenceEngineContracts.cs`, `ManagedInferenceEngineFactories.cs`, `StrataNativeWorker.cs`, `StrataRawModelProvider.cs`. Legacy ABI1 stays default; candidate ABI2 requires explicit verified source/profile selection. |
| Home-managed composition | `9to1 Workspace/shared/src/Haven.Infrastructure/Models/ManagedDulcheInferenceComposition.cs` registers the Strata factory alongside llama.cpp. `DI/ServiceCollectionExtensions.cs` installs lazy artifact/observation services. |
| Protected installation custody | `9to1 Workspace/shared/src/Haven.Infrastructure/Providers/Strata/`: native artifact source, protected artifact contracts, hardware/runtime observation, developer selection and approved artifact source. The installation issuer is optional; absence remains setup-required. Developer registration is explicit opt-in. No production caller of the developer opt-in was found in the reviewed graph. Resolve the genuine shipping installation source before claiming integration. |
| Official model admission | `9to1 Models/Dulche Alpha/Catalogue/ModelAdmissionPolicy.cs`, `LocalModelRegistry.cs`, `Domain/DenRecords.cs`, `Services/DulcheDen.cs`. The actual production registration consumer checks official artifact tuple and device resources before guarded registration, then rechecks authority/record/resources before publication. Generic registrations without an official tuple remain supported. |
| Tests and CI | Owning `Dulche Alpha/Tests` runtime/Den/native suites; `Haven.Infrastructure.Tests/Strata*`; `.github/scripts/thunder-*`; `.github/workflows/haven-chat-model-ci.yml`; `work/pc-worker/thunder-benchmark/`. Source preparation and static artifact audit do not execute a GPU model. |

The managed-only graph lacked the candidate native package. The recovered native candidate C++ and child-process header exactly match the published native source. This checkpoint preserves that missing package closure, with no claim that the resulting assembled graph already runs. The intended `check-upstream-release.py` checker is absent from the published source and is not delivered here; existing preparation/version receipts must not be described as that checker.

## Evidence boundaries and current blockers

The recorded independent component checkpoint has93 unique passing cases: candidate runtime15, protected infrastructure38 and Den40; zero failed or skipped. The combined overlay repeats the same53 runtime/infrastructure cases and adds no unique coverage. The real CPU relay was compiled with `STRATA_HAS_CUDA=0`, SHA-256 `6da3efd6dc1ae1405d59bf7b375692f33788fc59718a98e99c7fe4f48f97991c`. It proves truthful non-CUDA refusal and scoped process/protocol behavior, not model loading, CUDA execution, real GPU cancellation or shipping-app acceptance.

Five actual service streams were baseline observations only. They include client network, queue and cache effects; installed engine/hardware/weights were unidentified, and no controlled baseline-versus-candidate comparison ran. No local speed improvement is established.

The attempted tool-capable development worker remains BLOCKED: an initial cwd/PWD mismatch was corrected, then the real backend reported engine exit `-15` while read calls were pending. No successful source-edit/test/restart-resume cycle completed. The exit alone does not identify OOM, a candidate defect or the terminating actor. A cold API policy check denied both intended allows and intended denies because the configured agent was absent; this is not an enforcement proof. Do not repair this by granting unrestricted access.

The existing coordinator and local inference host are separate devices. Preserve both services and existing product sessions. Reconcile current shared-model custody, queue ownership and Stop/Pause state before any additional model request, unload, installation or benchmark. A readable remote thread does not confer dispatch, local file writes or boot persistence. Verify an official supported interface and its actual receipts; never simulate worker dispatch. Use an isolated worktree and explicit scope for local edits.

Full immutable-root acquisition, shared-library loader closure, KQ DSO execution, local GPU/model behavior, higher-level app behavior, requested-model support, controlled performance comparison and packaged platform acceptance remain pending. Current time/access/provider authorization must be obtained privately; this repository document grants none.

## Requested model identities

| Official model | Pinned revision | Contract to verify locally |
| --- | --- | --- |
| `ornith-ai/Ornith-1.5-122B` | `7681ef608624e7532d8164128bcffdbdbaf1b1af` | MIT; `qwen3_5_moe`. Official card comparisons are publisher claims. |
| `ornith-ai/Ornith-1.5-122B-GGUF` | `8d5893d767c449a4f18dd08f4bb6d0d6d4426175` | Pin exact quantized files, sizes, hashes, tokenizer and template before admission. |
| `InternScience/Sage-A1.5` | `6bc623eacb712d79d27c0913e4816ccd33d41b0a` | Apache-2.0; `Qwen3_5MoeForConditionalGeneration`. Verify supported architecture and actual memory/disk before loading. |

These resolve the requested model identities; they do not establish compatibility, machine fit, local quality or performance. Preserve weight licenses separately from engine licenses and do not substitute another model to make a backend pass.

## Execution and comparison plan

1. Retrieve the immutable handoff/source and verify the source manifest. Read the canonical authority and current private instructions. Inventory the actual local checkout and reuse the existing saved hardware/service probe before rerunning it. Establish exact installed engine/model identity and diagnose retained engine failure logs.
2. Reconcile current model custody and exclusive scope; prove the real configured worker can perform allowed read/edit/meaningful test/checkpoint operations while enforcing denied out-of-scope files, commands and providers. Demonstrate same-session/Task/Run recovery after process restart before any development-worker transfer.
3. Review the assembled source/native/package/DI graph, preserve compatible newer base interfaces, run focused owning tests, and resolve the approved installation source in the real shipping Home-managed composition. Record failures and missing cases honestly.
4. Where currently authorized and supported, prepare a reversible side-by-side candidate. Preserve the original engine/config/model/cache and immutable hashes. Verify source locks, package bytes/licenses, native dependencies and local hardware without firmware/driver/clock/power/security changes or unrelated service interruption.
5. Pre-register A=baseline and B=candidate with identical model/quant/tokenizer/template, seed/sampling, context/output lengths, flags, placement and cache conditions. Record actual hardware/runtime identities. Label warmup separately, then obtain at least FIVE valid observations PER variant in interleaved order (for example A/B, B/A, A/B, B/A, A/B), serialized against the shared model. Separate cold/warm cache and prefill/decode, and record latency, actual token count, memory, errors/cancel and correctness. Retain all observations/outliers; report medians, dispersion and ranges. Prior client-only observations do not replace controlled A measurements.
6. Exercise rollback to the preserved original engine with the same canonical model; confirm permissions, persisted conversation/Task/Run, tools, cancellation and relaunch. Independently review the exact artifacts and all seven gates before any promotion. No speculative performance percentage or success claim is permitted.

## Mandatory acceptance

| Gate | Required evidence |
| --- | --- |
| Usability | Normal model setup/selection and useful recoverable errors through the product. |
| Product fit | One embedded Dulche backend, llama.cpp coexistence and exhaustive applicable specification manifest. |
| Functionality | Real load/prefill/decode/stream/cancel/shutdown, routing, supported tools/modalities and failure cases. |
| Integration | Shipping DI/install composition, canonical model/context/Task/Run, permissions, higher-level callers and persistence/relaunch. |
| Separation and authenticity | Actual artifact/hardware/model behavior; fixtures, diagnostics and compiles never impersonate product evidence. |
| User fit | Requested models evaluated against real resources, locality/privacy and useful inspectable settings. |
| Refinement | Controlled repeated performance/correctness/resource evidence, recovery/rollback and required independent packaged-platform review. |

Apply APP-AC-001–009, ACCEPT-002–006 and GOV-001–002. Keep source review, implementation reports, independent component tests, GPU evidence and full-app/platform acceptance distinct. A failure cannot be averaged away. Preserve FAIL, BLOCKED and NOT RUN with exact source/build/artifact identities. This handoff is a CHECKPOINT.
