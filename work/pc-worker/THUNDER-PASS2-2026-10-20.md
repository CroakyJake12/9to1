# Thunder pass 2 — source implementation checkpoint (2026-10-20)

Worker: independent Mercury Thunder lane, codex/mercury-thunder-upgrade-20261020.
This is a **source checkpoint only**. No .NET/MSBuild/restore, no native/CUDA/CMake compilation, no
app/engine/inference run, no GPU calls, no model/SDK downloads. Build/GPU/hardware remain
**NOT RUN / BLOCKED** pending the coordinator's heavy-build slot and the owner window.

## Scope (owned by this lane)

All edits stay inside `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/`,
`9to1 Models/Dulche Alpha/Tests/` (Python, build-free), and `work/pc-worker/thunder-benchmark/`.
No dispatcher/public Dulche API, UI, or unrelated worker file was changed.

## Deliverables

### 1. Provenance resolved (was the pass-1 gap)
Verified against the official GitHub API (bounded requests, evidence saved in `/tmp/opencode/`):
- `refs/tags/v0.2.1` → annotated tag object `49ac15cbb32a8662602aaa7b8fc61bf6acef4cf0`
- tag object → commit `90767252f420e25223c739d604145ad15911d48b`
- commit → tree `f7adcc9073dc4bc12d5b001f1c85b58f2c2e9ead` (recursive tree, not truncated, 4498 blobs)
- Archive `strata-v0201.tar.gz` round-trips against the candidate lock (4498 blobs / 70,824,769
  source bytes). Tree cross-check: all 4498 blob SHAs match the authenticated tree **except 16**
  `*.bat`/`*.ps1` paths whose `.gitattributes` `eol=crlf` export-normalization changes the working-tree
  bytes vs the committed blob. Those 16 are explicitly recorded as `eolExportExempt` — the previous
  lock's `gitCommitTree = commit` was a copy-paste provenance error, now corrected.

### 2. Corrected candidate lock + tree map
- `work/pc-worker/thunder-benchmark/prep/strata-v0.2.1-lock.candidate.json` (commit≠tree, provenance header)
- `.../strata-v0.2.1-tree-blobs.json` (authenticated blob map + exemptions)
- `.../provenance-v0.2.1.json`
- `prepare-upstream-update.py` now enforces commit≠tree (40-hex), rejects duplicate archive members, and
  gains a `verify-tree` subcommand that cross-checks a lock against an authenticated tree blob map.

### 3. Candidate transport (source-only, fail-closed)
- `dulche_strata_candidate_worker.cpp` — explicit incompatible transport (ABI v2) that hosts the
  Dulche envelope framing and relays to the `strata --serve` sub-process via the real GEN/T/DONE/ERR
  protocol, with load/unload, bounded streaming/end/error, cancellation (STOP) and shutdown (QUIT).
  It **fails closed** until (a) the engine spawn argv is validated in a real build and (b) a verified
  tokenizer provider is bound (text→token-id gap). No fabricated load/generate/identity.

### 4. Build packaging — explicit profile/selection + receipts
- `build-package.py`: `--profile legacy|candidate`, `--lock`, `--target linux-x64|windows-x64`,
  `--ggml-dir`. Receipt records selected lock SHA-256, profile, target, toolchain versions, commit/tree,
  and installed-binary digest. New `verify_windows_x64_pe` (PE32+ AMD64) alongside the existing ELF64
  check; a Linux worker is never labeled Windows-capable (and vice-versa).

### 5. CMake profile selection + real donor targets
- `CMakeLists.txt`: `DULCHE_STRATA_PROFILE` (legacy default / candidate). Candidate sets
  `STRATA_ENABLE_CUDA=ON` + `STRATA_NATIVE_EXPERTS=ON` (real engine targets are inside the GPU branch),
  links/installs the upstream `strata` engine + relay worker, does **not** build/install upstream GUI/
  chat/agent/terminal bundles (they are separate Node/Tauri apps outside core CMake), supports Windows x64
  targets, and preserves the legacy Apache-2.0 notice separately (candidate LICENSE is MIT).

### 6. Native dependency closure
- `llama.cpp-dependency-lock.json`: pinned `3cf03257f219afbe7334045ff7c6a06ac68c627d`
  (tree `d255198f…`, 3608 blobs, 1376 ggml blobs, commit/date vetted). Full local checkout is
  NOT fetched here (BLOCKED → heavy-build slot); uses a verified local path via `STRATA_GGML_DIR`;
  quantisations are never disabled to avoid dependency custody.

### 7. Tests (build-free; all pass)
- `NativeStrataUpdateToolTests.py` — extended to 10 tests: provenance guard (commit≠tree),
  duplicate-member rejection, tree cross-check OK/detects unexplained mismatch.
- `NativeStrataPackageTests.py` — extended to 3 tests: ELF64 + PE32+ x64 checks (incl. reject cross-
  labels), build-package profile/lock/target/receipt selection.
- `NativeStrataCandidatePlanTests.py` — new, 6 tests: CMake profile branch, candidate uses real donor
  settings + no UI-bundle install, build-package requires lock for candidate, candidate worker has
  explicit fail-closed blockers, llama.cpp lock schema, tool rejects commit==tree.
- `work/pc-worker/thunder-benchmark/test_thunder_bench.py` — new, 14 tests: guards/parser/scheduling/
  aggregation/subprocess lifetime (deterministic fixtures, no GPU).

### 8. Benchmark harness (source)
- `thunder_bench.py` — fail-closed guards, interleaved ≥5 runs/config, bounded process tree lifetime,
  candidate serve-protocol parser, raw results + median/MAD.
- `COMPATIBILITY-MAP.md` — ABI v1 → candidate v0.2.1 mapping with honest deltas.
- `README.md` — updated to the concrete pass-2 state.

## Exact commands (source checks only, already run)
```sh
python3 -m unittest NativeStrataUpdateToolTests NativeStrataPackageTests NativeStrataCandidatePlanTests   # Tests/
python3 -m unittest test_thunder_bench                                                                  # work/pc-worker/thunder-benchmark/
python3 prepare-upstream-update.py verify      --archive <v0.2.1.tar.gz> --lock prep/strata-v0.2.1-lock.candidate.json
python3 prepare-upstream-update.py verify-tree --lock prep/strata-v0.2.1-lock.candidate.json --tree-blobs prep/strata-v0.2.1-tree-blobs.json
```

## NOT RUN / BLOCKED
- Native/CUDA/CMake build of either profile; Windows PE build.
- Candidate engine integration/binding, tokenizer provider, structured tools, model loading, inference.
- GPU/hardware benchmarks (owner window/endpoint not available; fail-closed guard enforced).

## Rollback
The legacy profile remains the default known-good rollback: `strata-source-lock.json` (ro99/strata),
`dulche_strata_worker.cpp`, the original managed provider and tests are unchanged. To revert the
candidate work, delete the candidate-only artifacts (candidate lock/tree-map, candidate worker,
llama.cpp dependency lock, CMake candidate branch, new tests) and keep the legacy default.

## Checkpoint — file hashes (2026-10-20, end of pass 2)

All 33 Python tests pass at this state (UpdateTool 10 / Package 3 / CandidatePlan 6 / harness 14).

| File | SHA-256 |
| --- | --- |
| `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/CMakeLists.txt` | `29df316531e6fe058ecb7ca1d0ebc6540de7961ab3e66c853993124c776c93c9` |
| `…/NativeStrata/build-package.py` | `a0a500259cd919dee955ac65ea184c98031b0362649c3f0b6259972ea034ff7c` |
| `…/NativeStrata/prepare-upstream-update.py` | `64d8f43e6a3fab2b871f9722240420c114fa36611ba20e185ad047e55c2b3404` |
| `…/NativeStrata/dulche_strata_candidate_worker.cpp` | `63bab0576f7e76a4ecf7c0c215f86ba3a90244ac3c36dee9e32897ca03884adc` |
| `…/NativeStrata/llama.cpp-dependency-lock.json` | `1a030840db427ada1e2433866a9fb2d8a23c45b13a6e5656c1f97f9e22e331c2` |
| `9to1 Models/Dulche Alpha/Tests/NativeStrataUpdateToolTests.py` | `afe89defe4fea1ada1b3c982332907580fdb725a96f075e2c4b363fd9e0b161b` |
| `…/Tests/NativeStrataPackageTests.py` | `725006481dff9bd2d59ab08462e851e566dd448daddfda77b83e286db8969b4f` |
| `…/Tests/NativeStrataCandidatePlanTests.py` | `399417eec1fae63d108203125adb9bcacbfdc5a6c69d5b079593762af65d342c` |
| `work/pc-worker/thunder-benchmark/thunder_bench.py` | `9246900b532039ae53f96e3c1e79f03b39dabe9a52640c3b8fc7c079213f9c39` |
| `work/pc-worker/thunder-benchmark/test_thunder_bench.py` | `04328f7feed258fe284ff01ecbc7ac35f6f41f0763784af85e145064a3aedbfe` |
| `work/pc-worker/thunder-benchmark/COMPATIBILITY-MAP.md` | `7a3faec922841a6b07e0d521b09e3675f1e9ecf6725fb238b3eef17976747e40` |
| `work/pc-worker/thunder-benchmark/README.md` | `7a3735b8d83fdb7ac034bd6b837400f6842929d3e630f5892c8bdce290c8e6e4` |

Uncommitted, per the source-only/no-commit constraint. Rollback path unchanged (legacy profile default).
