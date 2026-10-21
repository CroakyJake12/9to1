# Thunder Pass-18 — helper held-directory custody fix (lane 6)

Date: 2026-10-21. Continued from pass-17 (frozen source zip SHA `ca4383443a2ed0a5ec260d77c661c2a87a661d3c6850bba6b8864a51f6a1f0af`).
Mode: SOURCE ONLY. No SDK/dotnet/g++/native/CMake/CUDA/PC execution, no model/weights downloads, no credentials/
global-config/cap changes, no git commit/push/deploy, no edits to other worktrees or frozen reviewer copies.
Pin/spec/owner scope and V1 default preserved.

## Independent fault injection (reproduced)
Reviewer fixture `actual-helper-directory-rebind.py` failed against BOTH frozen16 and frozen17
(`actual_donor_tag == "REPLACEMENT_UNHELD_DONOR"`): the helper's `Path(__file__).resolve().parent`
escaped the held descriptor binding back to an ordinary real path, so after the original directory was
rebound the sibling donor import loaded the replacement.

## Concrete consumer fix
### `strata_tokenizer_helper.py` (source binding)
- `_helper_dir` now keeps the **lexical descriptor parent** — `pathlib.Path(__file__).parent` with an
  `os.path.abspath` abs guard, **never `.resolve()`** / realpath fallback — and inserts that lexical dir
  into `sys.path`. Sibling `strata_tokenizer` / `gguf_reader` imports therefore travel through the SAME
  held `/proc/<pid>/fd/<dir>` handle (the fd stays pinned to the held directory even if the original
  path is renamed). Added `import os`.
- Updated helper SHA `6181ac1e76a9a0373c7dd7da3edcc282290322574116ba88129994550bb877e2` (was `518cfce0…`).

### Held helper DIRECTORY/script path (typed contract)
- `StrataBundledWorker` gains optional `string? HelperDirectoryFdPath` — the held `/proc/<pid>/fd/<root>`
  directory descriptor. Produced only by the trusted installation source; the host clears the child env
  so ambient paths cannot override it.
- `StrataNativeArtifactSource.WorkerLease.OriginalWorker` populates `HelperDirectoryFdPath: held.Root.DescriptorPath`
  and keeps the four helper fd paths + digests for `VerifyBundleClosureAsync` re-verification.

### Managed execute choice (no list-position reliance)
- `StrataCandidateWorker.InitializeAsync` now passes `STRATA_HELPER_DIR` (held directory) instead of
  `STRATA_HELPER_PATH = HelperPaths[0]`. The native worker resolves the fixed member.
- `VerifyBundleClosureAsync` hard-requires `HelperDirectoryFdPath` in addition to the four helper fd
  digests.

### Native worker (`dulche_strata_candidate_worker.cpp`)
- `locate_helper` prefers `STRATA_HELPER_DIR` and executes the helper as `<held-dir>/strata_tokenizer_helper.py`
  (stable held dir + fixed member); legacy `STRATA_HELPER_PATH` and self-dir relative layout remain as
  fallbacks for non-host/local runs.
- Misleading comments corrected: the upstream `strata` main HAS a general `--pack` option
  (`generate.cpp:2408`), but the qwen35moe engine dispatch keys off `--model`/`--native` on a real
  Qwen35Moe GGUF (`generate.cpp:2380+`); a `--pack` folder alone cannot reach it. Candidate argv stays
  `--model <held-gguf> --serve [--max-context N]` (no `generate`, no `--pack`).

## Reviewer fixture result (source-only, run here)
```
/tmp/9to1-thunder-priority-acceptance-20261021/helper-custody-challenge/actual-helper-directory-rebind.py \
  <updated helper> work/pc-worker/thunder-pass18-helper-custody/actual-helper-directory-rebind-pass18.json
```
→ `{rebind_injected:true, donor_available:true, actual_donor_tag:"ORIGINAL_HELD_DONOR", passed:true}`, exit 0.
Consumes ORIGINAL_HELD_DONOR after the original-path rebind. Fixture unchanged.

## Authored tests
- `Haven.Infrastructure.Tests/StrataDeveloperArtifactAuthorityTests.cs` (normal, runs in Root combined):
  `Candidate_closure_named_mapping_is_order_independent_and_binds_exact_fixed_helper_names` — a signed
  inventory listing the four helpers in any order is accepted (execution never assumes index-0);
  renaming a helper to an extra name is refused.
- `NativeStrataBuildEngineArgsFixture.cpp` (new, NOTRUN / Root-native slot): includes the production worker
  TU (main renamed) and exercises `build_engine_args` directly — asserts `--model <held> --serve
  [--max-context N]`, no `generate`/`--pack`, zero-context omits `--max-context`, and missing held model
  path throws. NOT a source-string mirror.
- Existing `NativeStrataTokenizerHelperTests.py` (21), `NativeStrataCandidatePlanTests.py` (7), and
  `NativeStrataCandidateTransportTests.py` (20) still pass.

## Consistency
- Braces/parens balanced on every edited file (the single pre-existing `+1` `}` in the Infrastructure
  test is the documented JSON-string literal).
- Engine fd path + single-GGUF fd argv fix from pass-17 retained; V1/Safetensors default unchanged;
  single engine spawn preserved.

## Explicit NOT RUN / NOT performed
- NO C#/.NET/native/CMake/CUDA compile or run; new native fixture NOTRUN pending Root's SDK slot.
- NO model/engine/GPU/tools/vision/MTP/PC acceptance; no fabricated signed CUDA package.
- NO credentials/global config/cap/commit/push/deploy.

## OPEN / BLOCKED handoff
- BLOCKED — full upstream compile/install/model/GPU acceptance (requires Root's SDK/native slot, changed
  candidate rebuild, env, genuine CUDA-true diagnostics). No fake production receipt/signature.
- NOTRUN — native `build_engine_args` fixture and the `VerifyBundleClosureAsync`/null-model-path guards
  that require a running CUDA-true hello (only the genuine CUDA-false binary exists; it truthfully refuses
  earlier).
