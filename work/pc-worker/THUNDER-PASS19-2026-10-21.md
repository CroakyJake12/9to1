# Thunder Pass-19 — CUDA compile CI + archive CRLF/ggml verifier + --gpu device argv (lane 6)

Date: 2026-10-21. Continued from pass-18 (frozen source zip SHA `64761e37f80ddad2d6da46820d402faa49fc8c7dca50baf3e2ae6c2a3cba42e6`).
Mode: SOURCE ONLY. No SDK/dotnet/g++/native/CMake/CUDA/container/PC execution, no model/weights downloads,
no credentials/global-config/cap changes, no git commit/push/dispatch/deploy. Pin/spec/owner scope and
V1 default preserved. Existing `validate` (Windows/.NET) workflow job unchanged.

## A. Genuine compile-only CUDA GitHub CI (existing entrypoint, gated)
`.github/workflows/haven-chat-model-ci.yml` keeps the original `validate` job byte-identical in behavior
(default `pipeline: validate`), and adds an explicit owner/Root-chosen `pipeline` input. Only when
`pipeline == 'thunder-cuda-compile'` does the new `thunder-cuda-compile` job run. It:
- runs on `ubuntu-24.04` x64, `permissions: contents: read`, bounded `timeout-minutes: 120`, concurrency group;
- runs the build inside the PINNED immutable digest `nvidia/cuda:13.0.2-devel-ubuntu24.04@sha256:5dc1bca23d05bd37b011be68ec470c03b403a5da07ec3a86e41af9470e9d0cc6`
  with `--pull always` (guarantees the exact pinned digest), bind-mounting the checkout;
- `.github/scripts/thunder-cuda-compile.sh` (runs in the container) acquires+verifies the exact archive
  (size + SHA-256 `ccfffeaa…`), clones the full llama.cpp/ggml checkout at pinned commit `3cf03257…` /
  tree `d255198f…` and verifies HEAD/tree/clean, then invokes the EXISTING
  `build-package.py --profile candidate --lock prep/strata-v0.2.2-lock.candidate.json --ggml-dir … --build`
  with `--cmake-define CMAKE_CUDA_ARCHITECTURES=120` and `--cmake-define GGML_NATIVE=OFF` (portable CPU;
  SM120 explicit; no unsupported universal-CPU claim).
- installs to an isolated artifact dir, emits exact source/toolkit/compiler/engine/worker/helper/dependency
  digests to `work/pc-worker/thunder-cuda-compile-evidence/`, and records compile-only GPU/driver/model
  NOT RUN. Evidence upload uses `retention-days: 3`, no models/keys/source-whole-repo.

Upstream `--gpu` was re-verified (generate.cpp:2353-2375 pre-family parser sets CUDA_VISIBLE_DEVICES before
CUDA init; q36_engine.cu:3753 consumes it), so the option is genuine.

## B. Prebuild archive verifier defect fix + real ggml checkout verification (build-package.py)
- Verify loop now retains exact archive bytes+SHA **and** per-file bytes+SHA for every member. It accepts the
  git LF-normalized blob ONLY for the EXACT 16 declared `treeCrossCheck.eolExportNormalized` names, and only
  when the raw body actually contains CRLF and `git_blob_sha(body.replace(b"\r\n", b"\n"))` equals the exact
  locked `gitBlobSha`. Undeclared CRLF, digest mismatch, and declared-without-CRLF are rejected. Source
  escape (`..`/absolute) and member-closure gaps remain rejected. Legacy lock path unaffected.
- `--ggml-dir` now requires a real, clean git work tree pinned to the dependency lock's exact commit AND tree
  (`verify_ggml_checkout`), not merely a `CMakeLists.txt` presence.
- Added `--cmake-define NAME=VALUE` (repeatable, validated), threaded into `cmake_args` and recorded as
  `record["cmakeDefines"]`.

## C. Device selection -> `--gpu` argv (native only)
- `build_engine_args(uint32_t context)` → `build_engine_args(uint32_t context, const std::vector<uint32_t>& devices)`.
- Removed the unconsumed `STRATA_CANDIDATE_DEVICES` env block; when non-empty it now appends
  `--gpu <comma-separated>` to the engine argv (one engine spawn). Duplicate/ambiguous selection throws
  (fail closed); empty selection omits `--gpu`.
- `NativeStrataBuildEngineArgsFixture.cpp` (NOTRUN/Root native slot) updated to the new signature and proves
  single/multi-device argv (`--gpu 2`, `--gpu 3,0,7`), dup-throw, no-device omission, zero-context omission,
  and missing-model-path fail-closed — alongside the existing model-fd/context checks.

## Authored tests (all run here, source-only)
- `NativeStrataPackageTests.py` grew functional verify-only fixtures (tiny generated tars, no 28 MB download):
  declared-eol CRLF accepted+extracted; undeclared CRLF rejected; declared-without-CRLF rejected; member digest
  mismatch rejected; source-escape rejected; incomplete closure rejected. Plus `GgmlCheckoutVerifierControls`
  using a real temporary fake git repo: clean pinned accepted, dirty rejected, wrong-head rejected, missing
  marker rejected, non-git dir with marker rejected.
- Full suites green: Package 17, TokenizerHelper 21, Plan 7, Transport 20. `py_compile` on build-package.py and
  the test file; YAML parses; shell syntax OK; braces/parens balanced.

## Explicit NOT RUN / NOT performed
- NO CI executed (Root must review/push/dispatch the isolated branch). No local container/CUDA/cmake/compiler runs.
- Native `build_engine_args` fixture NOTRUN pending Root's exclusive SDK/native slot.
- No GPU/driver/model/PC acceptance; full signed candidate package acceptance remains BLOCKED.

## Exact touched files
- `M .github/workflows/haven-chat-model-ci.yml`
- `A .github/scripts/thunder-cuda-compile.sh`
- `M 9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/build-package.py`
- `M 9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/dulche_strata_candidate_worker.cpp`
- `M 9to1 Models/Dulche Alpha/Tests/NativeStrataBuildEngineArgsFixture.cpp`
- `M 9to1 Models/Dulche Alpha/Tests/NativeStrataPackageTests.py`

## Root dispatch command (after review/push of the isolated branch)
```
gh workflow run haven-chat-model-ci.yml --ref <isolated-branch> -f pipeline=thunder-cuda-compile
```
