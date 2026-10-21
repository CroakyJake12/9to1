# Thunder pass 3 — real portable transport, tokenizer, managed V2 seam (2026-10-20)

Worker: independent Mercury Thunder lane 6, branch `codex/mercury-thunder-upgrade-20261020`.
This is a **source + build-free Python check checkpoint only**. No .NET/MSBuild/restore, no
C++/CUDA/CMake compilation, no native app/engine/inference run, no GPU calls, no model/SDK/binary/
package installs or downloads, no provider/config change, no commit/push/deploy. Build/GPU/hardware
remain **NOT RUN / BLOCKED** pending the coordinator's heavy-build slot and the owner window.

## Authority and identity

- Owner directive: `/workspace/shared/9to1-test-evidence/parallel6-thunder-owner-directive-20261020.md`.
- Pass-3 prompt: `/workspace/shared/9to1-test-evidence/parallel6-thunder-pass3.prompt.md`.
- Frozen pass-2 checkpoint: `/workspace/shared/9to1-test-evidence/parallel6-thunder-pass2-frozen/identity.json`
  (checkpoint `e9911c3b…`, parent `ace91d304e…`).
- AGENTS.md owner-reference read; handoff: `docs/specification/owner-references/2026-10-20-single-app-implementation/owner-correction.json`.
  Thunder is the SAME embedded Strata integration (legacy V1 rollback retained); no parallel/divergent
  engine surface or app is introduced by this lane.
- Working tree is uncommitted (HEAD stays `873058283…`); changes are this lane's pass-2/pass-3 edits.
- Donor provenance (unchanged, resolved): tag `49ac15c…` → commit
  `90767252f420e25223c739d604145ad15911d48b` → tree
  `f7adcc9073dc4bc12d5b001f1c85b58f2c2e9ead`; engine v0.1.46, MIT license.

## Scope held by this lane

All edits stay inside `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/`,
`9to1 Models/Dulche Alpha/Tests/` (Python, build-free), and `work/pc-worker/thunder-benchmark/`.
No dispatcher/public Dulche API, UI, or unrelated worker file was changed.

## What this turn changed (pass 3 delta)

1. **Fixed broken bundled-helper tests.** `strata_tokenizer_helper.py` `serve()` now accepts
   injectable `in_stream`/`out_stream` (defaulting to `sys.stdin.buffer`/`sys.stdout.buffer`), so
   the real production helper is testable with `BytesIO` without touching process stdio. The helper
   tests' `_run_serve` now feeds the streams directly, and the dependency fail-closed test asserts
   `main()` returns `3` (the `SystemExit` wrapping is only in `__main__`). 12/12 helper tests green.
2. **Added `NativeStrataCandidateTransportTests.py` (new, 15 tests).** Pins the WIRE CONTRACT the
   candidate relay and donor engine actually speak and cross-checks it three ways:
   - source-scan contract assertions against `dulche_strata_candidate_worker.cpp` and donor
     `src/program/generate.cpp` (frame envelope types, operation dispatch 1/2/3/4, READY shape, GEN
     line shape, DONE finish vocabulary);
   - a deterministic donor-shaped DONE fixture (including the optional `chunk=`/`experts_streamed=`
     tail at generate.cpp:15106) fed through a small Python **mirror** of the candidate's parse
     contract — explicitly labelled a companion to the C++ parser, not a substitute;
   - malformed variants that must reject (negative telemetry, unknown finish, truncated, bare
     non-key tail field, missing terminal).
   This is the "deterministic subprocess/protocol fixture" evidence for the transport.
3. **Managed V2 seam** — see below (documented as an exact patch-only dependency, not authored blind
   into production .NET).

## State verification at handoff

Build-free test suite (all green at this checkpoint):

| Suite | Count | Result |
| --- | --- | --- |
| `Tests/NativeStrataUpdateToolTests.py` | 10 | OK |
| `Tests/NativeStrataPackageTests.py` | 3 | OK |
| `Tests/NativeStrataCandidatePlanTests.py` | 6 | OK |
| `Tests/NativeStrataTokenizerHelperTests.py` | 12 | OK |
| `Tests/NativeStrataCandidateTransportTests.py` | 15 | OK |
| `work/pc-worker/thunder-benchmark/test_thunder_bench.py` | 14 | OK |
| **Total** | **64** | **OK** |

Run:
```sh
cd "9to1 Models/Dulche Alpha/Tests"
python3 -m unittest NativeStrataCandidatePlanTests NativeStrataUpdateToolTests \
    NativeStrataTokenizerHelperTests NativeStrataPackageTests NativeStrataCandidateTransportTests
cd work/pc-worker/thunder-benchmark && python3 -m unittest test_thunder_bench
```

## Implemented matrix (pass 3)

| Capability | Status | Evidence / notes |
| --- | --- | --- |
| Portable resident-child IPC (spawn/READY/relay/STOP/QUIT/kill, bounded stderr drain, LineReader, cross-platform CreateProcess/exec) | SOURCE IMPLEMENTED | `dulche_strata_candidate_worker.cpp`; plan tests assert the real symbols; NOT BUILT |
| Engine locate via package-relative path from worker exe (`runtimes/<target>/engine/strata[.exe]`) | SOURCE IMPLEMENTED | `locate_engine`; CMake installs engine to `runtimes/<target>/engine` |
| GEN line build w/ sampling keys; DONE parse (read_n field 14, offload field 15, finish vocab, finite/nonnegative) | SOURCE IMPLEMENTED + FIXTURE-PINNED | mirrors crossed against donor generate.cpp:15106 |
| Load/probe/shutdown | SOURCE IMPLEMENTED (not throwing) | op 1 load → READY gate → ack type 4; probe type 6; stop op 3 → QUIT/ack type 3 |
| Bundled tokenizer helper (`strata_tokenizer_helper.py`) | IMPLEMENTED + 12 UT | reuses donor `tools/strata_tokenizer.py`; identity hash; bounded frame protocol; dependency fail-closed |
| Text generation | **BLOCKED BY DESIGN** | fails closed on the exact bundled-helper dependency (regex/jinja2 absent in this env); see blockers below |
| Structured tools (op 4) | **BLOCKED** | requires proven same-model tool probe; text requests remain functional |
| Managed V2 admission | **NOT AUTHORED — documented patch-only dependency** | see next section |
| Native/CUDA/Windows build + model + GPU hardware | **NOT RUN** | no build slot/hardware |

## Managed V2 seam — exact patch-only dependency (decision)

`StrataNativeWorker.cs:159` correctly rejects ABI2/new origin today; the pass-3 prompt itself states
that simply weakening that guard would be a bug. Because (a) this lane has no .NET build/run slot,
(b) the candidate relay has not yet completed a validated generation path, and (c) the V1 path is
protected production code that must remain strict, I did **not** author a blind V2 managed data path
into production .NET. The V2 managed seam is instead specified here as the exact patch-only
dependency for root/coordinator to apply in a .NET-capable slot:

- Negotiation point: `StrataNativeWorker.InitializeAsync` (handshake read at lines 153–163). Keep
  the strict guard; add an explicit, structured V2 branch keyed on `version==2` AND exactly
  `origin=="90767252f420e25223c739d604145ad15911d48b"` (the candidate origin). Every other ABI or
  origin value must keep the existing strict rejection (no arbitrary `version>=1` acceptance).
- Binary/digest binding: reuse the existing `StrataBundledWorker(ExecutablePath, ExpectedSha256)`
  digest check. The V2 branch must additionally verify the candidate worker's own digest and the
  bundled helper digest (the build-package receipt already records `engineSha256`/`helperSha256`/
  `dependencyPinnedCommit`).
- Model registration family: the V2 candidate is a GGUF family (`qwen35moe`), which must NOT
  masquerade as the legacy Safetensors/centralized registration. A new structured incompatibility
  must be produced for mismatched `WeightFormat`/`Topology`/`NativeRegistration`; do not relax the
  existing Safetensors/centralized guard for V1.
- Data path: only wire V2 generation after the candidate relay's helper-bound generate path is
  built and validated; until then the V2 branch must fail closed with the exact dependency cause and
  keep V1 rollback selectable through the existing profile selection.

## Remaining dependency blockers (exact)

1. **Text generation — bundled tokenizer helper wiring (C++).** `dulche_strata_candidate_worker.cpp`
   implements the real engine relay but its generate path still fails closed on the bundled helper
   dependency (the helper is bundled and unit-tested, but not yet spawned/encoded/decoded in the C++
   relay). Required next source step (build-validated): lazy-spawn
   `strata_tokenizer_helper.py` (installed beside the worker at `runtimes/<target>/native/`), verify
   `IDENTITY`, `ENC` messages → token ids, `GEN` to the engine, relay `T`→`DEC`→UTF-8 streaming
   decode preserving split-scalar boundaries, `DONE`→terminal. Runtime deps `regex` + `jinja2` are
   absent in this cloud (both pinned); even after wiring, encoding fails closed here with the exact
   cause (verified by `DependencyFailClosedTests`).
2. **Managed V2 admission** — exact patch-only dependency as specified above.
3. **Structured tools (op 4)** — requires a proven same-model tool probe once a real model survives
   load; currently blocked with exact message; functional text requests remain.

## Exact next build/run commands (for root/coordinator slot)

```sh
# 1) Native build (needs CUDA + pinned ggml dir):
cmake -S "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata" \
      -BDULCHE_STRATA_PROFILE=candidate -DSTRATA_GGML_DIR=<pinned copy> && cmake --build .
# 2) Package + verify receipt (multi-config aware):
python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/build-package.py" \
      --profile candidate --lock <candidate lock> --target linux-x64 --ggml-dir <pinned>
# 3) Managed .NET: apply the V2 seam patch in a .NET slot, then run the .NET Strata native tests.
# 4) Hardware: only inside the authorised owner window (RTX 5080), with interleaved >=5 runs/config.
```

## NOT RUN / BLOCKED (honest gate)

- Native/CUDA/CMake build of either profile; Windows PE build.
- Candidate relay generation relay against a real engine; tokenizer helper encode/decode round trip
  against real model metadata.
- Managed V2 admission; structured tools probe; model load/inference; GPU/hardware benchmarks.

## File hashes (2026-10-20, end of pass 3)

| File | SHA-256 |
| --- | --- |
| `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/dulche_strata_candidate_worker.cpp` | `5bb7d35eec0b99ad31e890390e6c1d96269e3966906c8c1c2667b4232139051c` |
| `…/NativeStrata/strata_tokenizer_helper.py` | `1cdd154bd327d5d0f8e2706283215be5b9f91fe87177a403824e5dc123a476fc` |
| `…/NativeStrata/CMakeLists.txt` | `e4c41e30398922772c7c6f9b12370d8e9158567a8a45320682fcb6645570fb5c` |
| `…/NativeStrata/build-package.py` | `92a3295bd40631712b10ba5f59236d3db68482d41fc5cb393dec69bc8a4ab2d6` |
| `…/NativeStrata/prepare-upstream-update.py` | `b5797a5dad756a315245928f97ed437be131331479c9940d1e59f620d2aa4734` |
| `…/NativeStrata/llama.cpp-dependency-lock.json` | `1a030840db427ada1e2433866a9fb2d8a23c45b13a6e5656c1f97f9e22e331c2` |
| `9to1 Models/Dulche Alpha/Tests/NativeStrataCandidatePlanTests.py` | `f43baf1175501056f1c6d991db043527692e1bf4fffcd7890788c5d1c5b8a006` |
| `…/Tests/NativeStrataUpdateToolTests.py` | `8ed52bec53ddf835bd1ef38fd66a1234162cf5dc546056e91ed6788e64b86735` |
| `…/Tests/NativeStrataPackageTests.py` | `725006481dff9bd2d59ab08462e851e566dd448daddfda77b83e286db8969b4f` |
| `…/Tests/NativeStrataTokenizerHelperTests.py` | `2069e4e9a17ccffad65989e6c133220ca311f31c6d1e4bf3ba84d085b5cd2294` |
| `…/Tests/NativeStrataCandidateTransportTests.py` | `7c7c01fdc2638a0a04173637b4f058bec63d6ff13d5dab83de3be09e937e26df` |

Uncommitted, per the source-only/no-commit constraint. Rollback unchanged (legacy profile is the
default known-good path).
