# Thunder PASS-9 checkpoint — truthful capability dispatch + owner-run PC probe handoff

- Date: 2026-10-21 (~16:20 UTC)
- Branch: `codex/mercury-thunder-upgrade-20261020` • worktree HEAD `873058283de8efaf90e70d7c7398686b3b1cafb4`
- Scope: source/tests/static checks only. **No build / no CMake / no CUDA / no .NET / no PC execution this turn** (Root owns the SDK slot; Browser99 owns the SDK build).
- READ FIRST: `AGENTS.md`, `docs/specification/9to1-development-specification-v2.md`, and the model-clarification at `shared/9to1-private-task-prompts/thunder-priority-20261021/owner-model-clarification.json`.

## 1. What changed this pass (Pass 9)

Replaced the loose `qwen35` substring registration check with a **truthful capability-based dispatch** in both the native candidate worker and the managed side, and separated the pin receipt from the blob lock. All changes source-only; no build/run.

| Artifact | SHA-256 |
|---|---|
| `InferenceEngines/NativeStrata/dulche_strata_candidate_worker.cpp` | `d5e2fb26b75455387b14a5ef7dae36593d155f151be0b3477ea8920d186215da` |
| `InferenceEngines/NativeStrata/strata-version-receipt.json` (new) | `17062afe3dbbaa52a3d4c1c7b5955545f7390e0def2188e57734bc75262449f9` |
| `InferenceEngines/StrataNativeWorker.cs` | `76bb985043879ffd2a1316c9ab9daa4d7e647fcae312afd7532c02c83f7e6497` |
| `Tests/NativeStrataCapabilityDispatchTests.py` (new, 14 tests) | `de191a1dc9412eb375a9126b437c57c4c5d908f15e71d1b88fcd203cd7c21dce` |
| `work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py` | `72502c001e68313cf94c990a3901aae9e202c26c6bbd5e1c47ab220eb84cdf11` |
| `work/pc-worker/thunder-benchmark/test_thunder_local_pc_probe.py` | `e402c2f94ffa5489a5ce41aadbfcafe0350e238b4f882344bf0e0329e239d67c` |
| `work/pc-worker/thunder-benchmark/minimal-build-closure.json` | `d3ea6f7063537d9226aab270a9a60ff2180a9ece353f944bc5af6a31dd6c27ec` |

### Capability dispatch
- C++ worker: added `ModelCapability` / `RegistrationAlias` / `resolve_capability` / `resolve_capability_tolerant`. `Session` now carries resolved `family` + `capability`.
- Accepted spellings (EXACT, verified only): engine-native `qwen35moe`, verified HF `qwen3_5_moe` / `qwen3_5_moe_text`, and the owner-confirmed identities `ornith-ai/Ornith-1.5-122B`, `InternScience/Sage-A1.5`. A verified family spelling may carry a genuine quant/format suffix (`qwen35moe-Q4_K_M` etc.).
- Manual no-fallback model identity: unknown registrations are **refused** (never silently downgraded). No "Orinth" typo aliases.
- **Tools/vision/MTP are never advertised** from an architecture string, card claim, or GGUF tag — the candidate relay is text-only.
- Managed side: added `StrataCapabilityRegistry` (mirrors the C++ table) and routed candidate load validation through it.

### Version-pin receipt
- `strata-version-receipt.json` separates `candidate` (origin/engine-version/license) from the immutable blob lock, so a future bump is a **data change**, not code surgery in the origin string.

### Owner-run probe
- Window updated to **2026-10-21 15:00–20:00 UTC** (hard guard enforced before every hardware/model/network phase). Interleaved **≥5 SHORT / ≥5 LONG** runs with loading/prefill/decode/TTFT/VRAM/RAM/GPU-util, hardware/model verification, endpoint identity check, no firmware/driver/security/service/power/unrelated-process changes, exclusive private output.

## 2. Test results

| Suite | Result |
|---|---|
| `NativeStrata*.py` (full) | **111 ran / 6 skipped / OK** |
| `NativeStrataTokenizerHelperTests` + `NativeStrataUpdateToolTests` | **34 / 34 OK** |
| `test_thunder_local_pc_probe.py` (offline, mocked clock/transport) | **22 / 22 OK** |

Everything green. No failures to diagnose.

## 3. Blockers / NOT RUN (recorded, not waived)

- **v0.2.2 archive**: disk guard blocks download. `/tmp` free 193 MiB, `/workspace` free 178 MiB (< 256 MiB after download+extraction+overhead). Blob-lock for v0.2.2 **stays BLOCKED** — no fabricated SHA/tree. Candidate remains pinned at **v0.2.1** (commit `90767252f420e25223c739d604145ad15911d48b`, tree `f7adcc9073dc4bc12d5b001f1c85b58f2c2e9ead`, engine 0.1.46, MIT; verified archive `2601d5df5cf2a210a674900e2c5984b6dbc8071e0290943a376005661989cbc4`). Serve wire contract verified identical v0.2.1 → v0.2.2.
- **Full engine build**: no CUDA 12.8+/cmake present; Root allocation required.
- **NativeFixture binary cases (5)**: NOT RUN — need root to build/pin the candidate worker.
- **Managed .NET token-fixture cases**: no .NET slot.
- **Text generation in cloud**: blocked by missing donor `regex`/`jinja2` (fail-closed by design).
- **GPU measurement**: coordinator offline (last seen 13:50 UTC); public gateway returns 401-only (not GPU-ready). No PC/inference execution here.

## 4. Root build commands (one-command when slot allocated)

From `minimal-build-closure.json` (g++ 14.2.0 present):

```
cd '/workspace/9to1-mercury-thunder/9to1 Models/Dulche Alpha/Tests'
mkdir -p /tmp/opencode
# 1) minimal candidate worker (no CUDA):
g++ -std=c++20 -O2 -DSTRATA_HAS_CUDA=0 -DSTRATA_NATIVE_EXT="" -I '../InferenceEngines/NativeStrata' '../InferenceEngines/NativeStrata/dulche_strata_candidate_worker.cpp' -o /tmp/opencode/dulche-strata-candidate-worker
# 2) child-ownership fixture:
g++ -std=c++20 -O1 -pthread -I '../InferenceEngines/NativeStrata' -o /tmp/opencode/child-ownership-fixture NativeStrataChildOwnershipFixture.cpp
/tmp/opencode/child-ownership-fixture   # expect failures=0
# 3) native fixture against the built worker:
mkdir -p work/pc-worker/thunder-benchmark/native-child-test/build
cp /tmp/opencode/dulche-strata-candidate-worker work/pc-worker/thunder-benchmark/native-child-test/build/dulche-strata-candidate-worker
DULCHE_NATIVE_CANDIDATE_SHA256=$(sha256sum /tmp/opencode/dulche-strata-candidate-worker | cut -d' ' -f1) python3 NativeStrataNativeFixtureTests.py -v
```

## 5. Owner-run PC probe handoff

- Packet: `work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py` (SHA `72502c00…`) + offline tests `test_thunder_local_pc_probe.py` (SHA `e402c2f9…`, 22/22 OK).
- Run **BY THE OWNER on the PC**, inside the hard window **2026-10-21 15:00–20:00 UTC**. Uses the already-configured local key `~/.config/homepc-ai/api-key` (never inspected here). Endpoint `https://ai.9to1.uk/v1`, TLS verification ON.
- Command: `python3 work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py`
- Verified by the probe: hardware/model identity, endpoint identity, interleaved ≥5 SHORT / ≥5 LONG runs with loading/prefill/decode/TTFT/VRAM/RAM/GPU util. No firmware/driver/security/service/power/unrelated-process changes; no owner-root-repo overwrite; exclusive private output.
- **I must not execute this probe or any PC inference myself** — it is owner-run only.

## 6. Next move
1. Await Root allocation for the build slot → run section 4 commands → hand 5 native-fixture binary cases to green.
2. When v0.2.2 archive is acquired (disk ≥256 MiB after reserve): verify with `prepare-upstream-update.py`, generate the real blob lock, update `strata-version-receipt.json` `candidate`, and bump origin/engine-version strings (data +).
3. Owner runs the probe inside 15–20 UTC window.
