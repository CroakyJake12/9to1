# Thunder (Dulche Strata) integration discovery — 2026-10-20

Worker: independent Mercury Thunder lane (codex/mercury-thunder-upgrade-20261020).
This document records **findings before implementation**. It is engineering evidence, not
acceptance. Implementation/acceptance/hardware states are kept separate.

## 1. What Thunder is

Thunder is **not** a separate engine. It is the 9to1 embedded build of the upstream **Strata**
inference project, integrated through Dulche. It lives entirely under:

```
9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/
```

The canonical specification (docs/specification/9to1-development-specification-v2.md, lines
4483–4527) requires:
- A Dulche-owned inference-runtime abstraction; llama.cpp is the default local backend and a
  Strata fork must be carried as a specialised backend.
- One stable internal runtime contract (load/prefill/decode/KV/multimodal/cancel/stream/
  telemetry/errors/shutdown); engine-specific behaviour behind adapters.
- `setInferenceEngine(Strata)`, `setInferenceEngine(Llama.cpp)`, `getInferenceEngine()`.
- Automatic capability-based selection and manual override with structured incompatibility.
- The fork must stay close to upstream so upstream improvements can be reviewed/merged without
  re-forking; preserve licences; keep model-weight licences separate.

## 2. Current embedded engine (verified from tree)

| Item | Value |
|---|---|
| Pinned origin | `https://github.com/ro99/strata` |
| Pinned commit | `015b075079c51a7aec670ee24924f920f5e7bb2b` (2026-09-08) |
| Git commit tree | `491a4a74e538bd469f83811c8a907cc7d6118db9` |
| Archive | 1,476,955 bytes; SHA256 `5b89b7b6…a62e`; 303 blobs; 7,805,688 source bytes |
| Project version (CMake) | `project(strata VERSION 0.1.0 …)` |
| License | Apache-2.0 (`LICENSE.strata`); vendored `vendor/marlin` = vLLM 2.3.8 distribution |

The local fork is the **exact upstream source plus additive native bridge files only** (no
upstream body edits). The bridge files are:

- `dulche_strata.h` — Dulche-owned C ABI v1.
- `dulche_strata.cpp` — ABI → Strata internals (`strata/app/runtime.hpp`; `RuntimeSession`,
  `registered_model_names()`, `find_model_by_cli_name()`, `GenerationResult`, `ChatMessage`).
- `dulche_strata_worker.cpp` — dedicated bounded native worker process (stdin/stdout little-endian
  frames), hardware probe, structured-tool terminal.
- `CMakeLists.txt` — overlay build (`add_subdirectory` of pinned upstream; `strata_core`).
- `build-package.py` — verify/extract exact archive; ELF64 Linux x86_64 checks; CUDA 12.8+ gate;
  no CPU stub.
- `strata-source-lock.json` — full immutable blob lock.

Managed adapters (thin, in `InferenceEngines/`): `StrataNativeWorker.cs`,
`StrataRawModelProvider.cs`, `StrataStructuredToolCodec.cs`, `StrataFactoryCustody`,
`ManagedInferenceEngineFactories.cs`, `ManagedInferenceEngineRequestSource.cs`,
`InferenceEngineDispatcher*`, `InferenceEngineSettings.cs`, `InferenceCalibration*`.
Tests: `NativeStrataPackageTests.py`, `StrataNativeProtocolTests.cs`, `StrataStructuredToolProtocolTests.cs`,
`ManagedProviderDulcheAdapterOriginalTests.*`, `InferenceEngine*Tests.cs`.

The worker is text-only; images remain unsupported in the raw transport. Current state is a
**source checkpoint**: no compiler/native build/CUDA/model execution has been run here, and no
hardware window has opened (see §7).

## 3. Upstream verification (official repo)

Official repository per owner directive: `https://github.com/Niko1221/Strata`.

- **Latest stable release: v0.2.1** (tagged 2026-10-20, commit `90767252f420e25223c739d604145ad15911d48b`).
  Release notes: engine package version **0.1.46** (0.2.0 carried 0.1.45.3); update-feed sequence 102.
  The note "0.2.1.3: Terminal 0.1.46.2" refers to the Terminal component, not the engine package.
- Engine source version confirmed in-tree: `project(strata VERSION 0.1.46 …)`
  (`CMakeLists.txt`) and `packaging/*` uses `PKGVER=0.1.46`.
- Source tarball (official, tag v0.2.1): 27,912,843 bytes; SHA256
  `2601d5df5cf2a210a674900e2c5984b6dbc8071e0290943a376005661989cbc4`; recursive tree has 4,498
  blobs.
- License **changed**: v0.2.1 tree carries `LICENSE` = **MIT** (© 2026 Niko1221 and Strata
  contributors). This differs from the currently pinned Apache-2.0. License/dependency review is
  mandatory before upgrade (mission §2 "Review licences/dependencies").

### Cloud API-compatibility gap (critical)

The current bridge binds to a specific Strata internal surface (`strata/app/runtime.hpp`,
`RuntimeSession`, `registered_model_names`, `find_model_by_cli_name`, `model_executor.hpp`,
`GenerationResult`, `ChatMessage`, `validate_chat_messages`, `validate_sampling_options`,
`ReasoningFormat`). Searching the full v0.2.1 source tree for these symbols/h headers finds
**no matches**; the v0.2.1 layout is reorganized (`include/strata/core/`, `metal/`,
`strata::core` namespace, `session.hpp`, `engine.hpp` under `qwen35/`, etc.).

Therefore **upgrading the pinned commit to v0.2.1 is not a simple commit bump**: the native adapter
must be reworked against the new engine API (or a compatible, still-supported API surface must be
identified and pinned). This must be confirmed against the real v0.2.1 build before any promotion.
This is the central upgrade risk and the main reason to proceed via an isolated candidate pin +
adapter rework, not a blind pin update.

### Repo-identity note

The currently pinned `ro99/strata` (6★, created 2026-07-14) is a different, earlier repository than
the owner-designated official `Niko1221/Strata` (29k★, created 2026-09-24). The owner and mission
text treat Niko1221/Strata as the official upstream and v0.2.1/engine 0.1.46 as the prior
observation — verified accurate here. The existing pin lineage and the official lineage should be
reconciled during integration (which one is the long-term upstream source of truth; the mission says
Niko1221/Strata).

## 4. Verification performed

- Read `AGENTS.md`; canonical spec §Dulche (2970–4527); owner references (single-app correction).
- Inspected `NativeStrata/` bridge, lock, license, CMake, fully-package.py; managed adapters and
  tests.
- Official repo: fetched release list (v0.2.1 latest, engine 0.1.46), tag commit, source tarball
  (verified size/SHA256), recursive tree, licensing, and searched for adapter-bound symbols (absent
  in v0.2.1).
- No .NET/MSBuild/restore, no native/GPU/heavy-CMake/CUDA compile, no inference, no model/package
  install, no SDK download was executed this turn (resource allocation respected).

## 5. Upgrade/preparation plan (implementation state, NOT yet run)

1. **Candidate pin + adapter rework (isolated).** Generate a candidate `strata-source-lock.json`
   for the official v0.2.1 tag (source tarball already fetched and verified), then rework the C
   ABI bridge against the v0.2.1 engine API. Prepare a compatibility matrix of required ABI
   functions → new engine entry points. Keep the same ABI/worker-wire version unless a change is
   justified; preserve the tool codec.
2. **License review.** Confirm MIT terms and that vendored components remain licensed appropriately;
   update `LICENSE.strata`/package receipts only alongside the change.
3. **Build gate (future, needs build slot).** Run `build-package.py` with the new lock
   (Linux x64, CUDA 12.8+). Compare against retained known-good rollback (current pin).
4. **Tests.** Extend `NativeStrataPackageTests.py` to verify the new lock invariants; add static
   checks for the bridge/API mapping; keep existing protocol tests.
5. **Benchmark harness (Windows/RTX 5080).** Offline-authored, fail-closed; positive endpoint/
   owner-window/device/model verification; interleaved ≥5 runs/config; existing-vs-upstream-vs-
   optimised; retain baseline/known-good rollback. NOT run until hardware + launch.
6. **Optimisation (only after baseline).** Profile RTX 5080-specific MoE expert cache/prefetch,
   VRAM division, KV growth, CUDA overhead. No speculative changes; no gain required for a safe
   upstream upgrade.

## 6. Correctness/acceptance gates (unverified now)

Model load/unload, generation/streaming, supported quantisations, KV/long context, tool calling,
structured output, cancellation/error recovery, concurrency, APIs/model selection/config, Spaces
integration. Untested ⇒ NOT RUN/BLOCKED. All seven 9to1 acceptance criteria apply. One GPU does
not establish universal compatibility.

## 7. Hardware state

Window: Tuesday 20 Oct 2026 ~21:00–22:00 Europe/London (20:00–21:00 UTC). It has **not started**;
no authorised RTX endpoint is connected in this cloud context. No owner-PC actions were executed.
GPU performance remains BLOCKED/NOTRUN; historical 90/50 tok/s are not baselines.

## 8. Files

- This discovery: `work/pc-worker/THUNDER-DISCOVERY-2026-10-20.md`
- Candidate-lock preparation utility: `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prepare-upstream-update.py` (new)
- Static tests: `9to1 Models/Dulche Alpha/Tests/NativeStrataUpdateToolTests.py` (new; 4/4 pass)
- Benchmark harness (offline): `work/pc-worker/thunder-benchmark/` (new; guarded, not executed)
- Candidate lock for official v0.2.1 (generated, round-trip verified, not committed as source pin):
  `work/pc-worker/thunder-benchmark/prep/strata-v0.2.1-lock.candidate.json`
  (4,498 blobs; 70,824,769 source bytes; SHA256 `45dc222d8c01af29110c353f777aea75e946954cd35c360bb9c25e080acf179f`).
  Reproduce via:
  ```sh
  python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prepare-upstream-update.py" \
    generate --archive <v0.2.1.tar.gz> --origin https://github.com/Niko1221/Strata \
    --commit 90767252f420e25223c739d604145ad15911d48b \
    --tree 90767252f420e25223c739d604145ad15911d48b \
    --archive-url https://codeload.github.com/Niko1221/Strata/tar.gz/refs/tags/v0.2.1 \
    --out <out.json>
  ```
