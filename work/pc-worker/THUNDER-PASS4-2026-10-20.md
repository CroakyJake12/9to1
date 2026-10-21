# Thunder pass 4 — real helper/text path, fatal retirement fix, managed V2 authored (2026-10-20)

Worker: independent Mercury Thunder lane 6, branch `codex/mercury-thunder-upgrade-20261020`.
This is a **source + build-free Python check checkpoint only**. No .NET/MSBuild/restore, no
C++/CUDA/CMake compilation (except the coordinator-allocated bounded portable POSIX child-process
RAII compile/test), no native app/engine/inference run, no GPU calls, no model/SDK/binary/package
installs or downloads, no provider/config change, no commit/push/deploy. Build/GPU/hardware remain
**NOT RUN / BLOCKED** pending the coordinator's heavy-build slot and the owner window.

## Authority and identity

- Pass-4 prompt: `/workspace/shared/9to1-test-evidence/parallel6-thunder-pass4.prompt.md`
  (real helper/text path + fatal child retirement + managed V2; source-only).
- Frozen pass-3 checkpoint: `/workspace/shared/9to1-test-evidence/parallel6-thunder-pass3-frozen/identity.json`
  (checkpoint `6ae52a185c5dc01f0ed557b00af980fbe4b40136`).
- AGENTS.md owner-reference read (single-app-implementation + concurrency); Thunder stays the SAME
  embedded Strata integration (legacy V1 rollback retained); no parallel/divergent engine or app.
- Working tree is uncommitted (HEAD stays `873058283…`); changes are this lane's pass-3/pass-4 edits.
- Donor provenance (unchanged): tag `49ac15c…` → commit
  `90767252f420e25223c739d604145ad15911d48b` → tree
  `f7adcc9073dc4bc12d5b001f1c85b58f2c2e9ead`; engine v0.1.46, MIT license.

## Scope held by this lane

Edits stay inside `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/`,
`9to1 Models/Dulche Alpha/InferenceEngines/StrataNativeWorker.cs` /
`StrataRawModelProvider.cs`, `9to1 Models/Dulche Alpha/Tests/` (Python, build-free), and
`work/pc-worker/thunder-benchmark/`. No dispatcher/public Dulche API, UI, or unrelated worker file
was changed.

## What this turn changed (pass 4 delta)

1. **Real candidate helper/text path (native).** `dulche_strata_candidate_worker.cpp` now:
   - lazy-spawns the bundled `strata_tokenizer_helper.py` (`bind_helper`) beside the worker, verifies
     `IDENTITY` against the registered expected identity, and drives `ENC`→token ids → `GEN` to the
     engine → relays `T`→`DEC`→UTF-8 streaming bytes (split-scalar-safe) → `DONE`→terminal frame;
   - cancellation via op 5 (`STOP` forwarding) and op 3 shutdown with bounded `wait_exit`+`kill`;
   - V2 host wire documented and implemented (load carries expected tokenizer identity + devices;
     generate carries `stop_count`/`stops`/`effort`, explicitly refuses stop lists and non-medium/none
     effort).
2. **Fatal child retirement fixed.** `dulche_child_process.h` `StderrDrain` now has a joining
   destructor (no throwing `std::thread` destroyed during unwind → no `std::terminate`); `ChildProcess`
   owns pid+fds with bounded `wait_exit`/`shutdown`/`reap`. Verified standalone POSIX compile+run
   (all cases green: `raii_retirement`, `stderr_drain`, `echo`, `wait_exit`).
3. **Tokenizer dependency packaging completed.** `strata_tokenizer_helper.py` uses the canonical
   identity (pre + full ordered vocab + ordered merges + token types + special ids + chat template),
   donor-matching Jinja rendering (`ImmutableSandboxedEnvironment`/`tojson`/`raise_exception`), gguf
   path reads `tokenizer.chat_template`; the CMake install closure now includes donor
   `strata_tokenizer.py` + `gguf_reader.py` + `strata-runtime-dependencies.json` beside the helper
   (pass-3 installed only the helper — a real packaging defect), and `build-package.py` requires and
   verifies the full helper closure. `strata-runtime-dependencies.json` pins `regex` 2026.9.29 and
   `jinja2` 3.1.6 with real PyPI sdist sha256 digests.
4. **Managed V2 authored (guarded, not composition-selected).** `StrataNativeWorker.cs` now contains
   `StrataCandidateWorker`, a self-contained V2 candidate client speaking the exact candidate ABI
   (hello v2/origin `90767252…`/family `qwen35moe`; load op 1 with expected tokenizer identity +
     devices; text generate op 2 with message/options/stop/effort layout; token type 1; terminal type 2;
     stop op 3; cancel op 5). Structured tools are refused with the exact dependency cause. V1 is
   preserved byte-for-byte (`version!=1||origin!=Origin` intact). A shared `IStrataNativeWorker`
   surface is implemented by both workers and consumed by `StrataRawModelProvider`; the candidate is
   NOT wired into production composition (no version>=1 acceptance).

## State verification at handoff

Build-free test suite (all green at this checkpoint):

| Suite | Count | Result |
| --- | --- | --- |
| `Tests/NativeStrataUpdateToolTests.py` | 13 | OK |
| `Tests/NativeStrataPackageTests.py` | 6 | OK |
| `Tests/NativeStrataCandidatePlanTests.py` | 7 | OK |
| `Tests/NativeStrataTokenizerHelperTests.py` | 21 | OK |
| `Tests/NativeStrataCandidateTransportTests.py` | 15 | OK |
| `Tests/NativeStrataNativeFixtureTests.py` | 7 (5 skipped, root build absent) | OK |
| `Tests/NativeStrataCandidateManagedWireTests.py` | 10 | OK |
| **Total** | **79** | **OK** |

Standalone POSIX child-process RAII compile+test (bounded, coordinator-allocated) — all green:
```sh
cd work/pc-worker/thunder-benchmark/native-child-test
g++ -std=c++17 -O1 -DDULCHE_CHILD_TEST -I <native> child_process_test.cpp -o build/child_process_test -lpthread \
  && timeout 60 ./build/child_process_test      # ALL CHILD PROCESS TESTS PASSED
g++ -std=c++17 -O1 -c -DSTRATA_HAS_CUDA=0 -DSTRATA_NATIVE_EXT= -I <native> \
  <native>/dulche_strata_candidate_worker.cpp -o build/worker.o   # compiles clean at C++17
```

Run:
```sh
cd "9to1 Models/Dulche Alpha/Tests"
python3 -m unittest NativeStrataPackageTests NativeStrataCandidatePlanTests NativeStrataCandidateTransportTests \
    NativeStrataTokenizerHelperTests NativeStrataNativeFixtureTests NativeStrataUpdateToolTests \
    NativeStrataCandidateManagedWireTests
```

## Implemented matrix (pass 4)

| Capability | Status | Evidence / notes |
| --- | --- | --- |
| Real candidate generation path (helper spawn/IDENTITY/ENC/DEC/relay/DONE) | SOURCE IMPLEMENTED | `dulche_strata_candidate_worker.cpp`; NOT BUILT |
| Fatal retirement (joinable std::thread destroyed during unwind) | FIXED + COMPILED/TESTED | `dulche_child_process.h` StderrDrain joining destructor; standalone POSIX run all green |
| Cancellation via op 5 (STOP) + op 3 shutdown bounded wait+kill | SOURCE IMPLEMENTED | worker `main`/`run_generation` |
| Tokenizer/chat-template identity + donor Jinja render + gguf template | IMPLEMENTED + 21 UT | `strata_tokenizer_helper.py` |
| Donor runtime closure install (tokenizer + gguf reader + dep lock) | IMPLEMENTED + PackageTests | CMake install + `build-package.py` `helperClosure` required/verified |
| Runtime dependency pin (regex/jinja2 with real digests) | IMPLEMENTED + tests | `strata-runtime-dependencies.json` |
| Managed V2 candidate client | AUTHORED (guarded, not wired) | `StrataCandidateWorker` + `IStrataNativeWorker` + 10 wire tests |
| Structured tools | **BLOCKED (exact cause)** | refused both sides with exact dependency report; text remains available |
| Native/CUDA/Windows build + model + GPU hardware | **NOT RUN** | no build slot/hardware |

## Remaining dependency blockers (exact)

1. **Text generation in this cloud is blocked by missing donor `regex`/`jinja2` modules** (both
   pinned). The design is fail-closed with the exact dependency cause; the native relay's helper-bound
   generate path cannot complete encode/decode until those modules exist in the runtime environment.
2. **Managed V2 is authored but NOT wired into production composition** (no version>=1 acceptance):
   `ManagedInferenceEngineFactories.cs` still creates the legacy `StrataNativeWorker`; selecting the
   candidate requires the coordinator's .NET + native acceptance slot.
3. **Structured tools (op 4)** require a proven same-model tool probe once a real model survives load;
   currently refused with the exact message on both native and managed sides.

## Exact next build/run commands (for root/coordinator slot)

```sh
# 1) Native build (needs CUDA + pinned ggml dir):
cmake -S "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata" \
      -DDULCHE_STRATA_PROFILE=candidate -DSTRATA_GGML_DIR=<pinned copy> && cmake --build .
# 2) Package + verify receipt (multi-config aware), verifies the helper/runtime closure:
python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/build-package.py" \
      --profile candidate --lock <candidate lock> --target linux-x64 --ggml-dir <pinned>
# 3) Managed .NET: composition-select StrataCandidateWorker in a .NET slot and run the .NET Strata
#    native tests (the managed wire tests + NativeFixtureTests are the source-level reference).
# 4) Hardware: only inside the authorised owner window (RTX 5080), interleaved >=5 runs/config.
```

## NOT RUN / BLOCKED (honest gate)

- Native/CUDA/CMake build of either profile; Windows PE build.
- Candidate relay generation relay against a real engine; tokenizer helper encode/decode round trip
  against real model metadata (regex/jinja2 absent here → fail-closed by design).
- Managed V2 composition selection/acceptance; structured tools probe; model load/inference;
  GPU/hardware benchmarks (queued at native-hardware time).

## File hashes (2026-10-20, end of pass 4)

| File | SHA-256 |
| --- | --- |
| `…/NativeStrata/CMakeLists.txt` | `58645dad4dce7f4b60106d38ea53cc3223685f2bc40dcd254c116f8277fc9cda` |
| `…/NativeStrata/build-package.py` | `c90e96ee2c00df510f74905d8054c3ba640de057a78bc7dc21052e11fcc8dc8d` |
| `…/NativeStrata/dulche_child_process.h` | `8c3e13b3439cd0d86dea67e90ed13cb7877932d071cd75df1163c629686cbca2` |
| `…/NativeStrata/dulche_strata_candidate_worker.cpp` | `4768021a01f3ac090b2c1c05e94523638f41803401aa936fb3c291bd5e78fce5` |
| `…/NativeStrata/strata_tokenizer_helper.py` | `518cfce0497270496c2c1493f467f7bf49f6b0bc306d3afbc57ab1e9067099de` |
| `…/NativeStrata/strata-runtime-dependencies.json` | `58ca0a370ae94a500899100d7dc50cad68d15c982e03ba17224e32d252d72df1` |
| `…/NativeStrata/prepare-upstream-update.py` | `b5797a5dad756a315245928f97ed437be131331479c9940d1e59f620d2aa4734` |
| `…/NativeStrata/llama.cpp-dependency-lock.json` | `1a030840db427ada1e2433866a9fb2d8a23c45b13a6e5656c1f97f9e22e331c2` |
| `…/InferenceEngines/StrataNativeWorker.cs` | `57440d4160114f2aca569b556197ca734467bfe1e9f29fa360c5d2a2b6a94326` |
| `…/InferenceEngines/StrataRawModelProvider.cs` | `25ea53b589dbc7e02a3d87f3d32dda5576f93725cdb5e42c34b663fce5203367` |
| `…/Tests/NativeStrataPackageTests.py` | `ab9aec02329c4e0febb14e2ebca94aada4cd96f42a77fce5a065340ecd8fecee` |
| `…/Tests/NativeStrataCandidatePlanTests.py` | `60b6d556420ac7e0005b02d8ec93ffcc1516e77e2f729eb3afab70c247806503` |
| `…/Tests/NativeStrataCandidateTransportTests.py` | `7f823fe4edad747b972561bcfa6d0701a076183078965e40966d84cc874cb414` |
| `…/Tests/NativeStrataTokenizerHelperTests.py` | `7c579a151dfccd0e899cdc017cfd48f6d695caaa13049ad659e3bcb85150f3be` |
| `…/Tests/NativeStrataNativeFixtureTests.py` | `0f3f3a0bc107e5bc98da598dbfb5be3fcb39e6d6fd201fc78eae3a2affddca7f` |
| `…/Tests/NativeStrataUpdateToolTests.py` | `8ed52bec53ddf835bd1ef38fd66a1234162cf5dc546056e91ed6788e64b86735` |
| `…/Tests/NativeStrataCandidateManagedWireTests.py` | `b9cdf4f64221b82482d0b54ebd4fcadf111c156208abe7bc30ba1a43045936e6` |

Uncommitted, per the source-only/no-commit constraint. Rollback unchanged (legacy profile is the
default known-good path).
