# Thunder Pass-17 — held runtime custody + exact upstream CLI model dispatch + C# relational-pattern fix (lane 6)

Date: 2026-10-21. Session continued from pass-16 (frozen `ses_ea553125dffe0sx2MyJhLF3q7w`, source frozen at
`/tmp/9to1-thunder-priority-acceptance-20261021/thunder16-frozen/source.zip` SHA `fff1784b6a6b1f7c6bbda8651648f8678d7b62c9579cc12fd8401c3e3fcbe395`).
Mode: SOURCE ONLY. No SDK/dotnet/g++/native/CMake/CUDA/PC execution, no model/weights downloads, no credentials/
global-config/cap changes, no git commit/push/deploy, no edits to other worktrees or frozen reviewer copies.
Official v0.2.2 / engine 0.1.47 pin + 4625 lock, legacy V1 default rollback, V1 Safetensors default, and single-executable
semantics left unchanged.

## Objective — three concrete producer/consumer defects from independent source review
1. **Held runtime custody** — `OriginalWorker` filled `EnginePath`/`HelperPaths` from ordinary mutable filesystem
   paths while the worker hashed held FdPaths (so checked and consumed artifacts could differ despite fs-verity).
2. **Actual upstream CLI model dispatch** — `build_engine_args` pushed a literal `"generate"` subcommand + `--pack`
   dir; the real v0.2.2 `generate.cpp` main has no `generate` subcommand and dispatches to qwen35moe only when
   `--model`/`--native` points at a real Qwen35Moe GGUF.
3. **C# relational-pattern defect** — `selected.CheckpointFiles.Count is < (candidate ? 1 : 2) or > 128` used a
   nonconstant relational-pattern bound.

## Implementation

### Defect 1 — held runtime custody (consumed == verified == SAME fd)
`StrataNativeArtifactSource.cs`:
- `WorkerLease.OriginalWorker` now emits **fd-derived `/proc/<pid>/fd/N` paths** for `EnginePath`/`HelperPaths`
  (via `held.EngineRoot.SingleFilePath` and `held.Root.FilePath(name)`), replacing `Path.Combine(WorkerRoot/EngineRoot,…)`.
  `EngineFdPath`/`HelperFdPaths` remain the same fd paths — consumed == verified == retained, no ordinary mutable path.
- The engine sibling root is held with `immutableDirectory:true`; the candidate worker root is held with
  `immutableDirectory:true` too (`workerImmutable = worker && metadata.Profile is not null`). This enforces the
  actual kernel-immutable helper root so sibling imports (`strata_tokenizer_helper.py` `__file__.resolve().parent`)
  cannot be substituted after verification.
- `ModelLease` adds `OriginalCheckpointFileFdPath` virtual: returns `held.Root.FilePath(gguf)` for the SAME held
  single GGUF, else `null` (fail closed, never a directory scan of unheld files).

`ManagedInferenceEngineFactories.cs`: `ScopedOriginalModel` forwards `OriginalCheckpointFileFdPath` to the original.

`StrataNativeWorker.cs`:
- `InitializeAsync` reads `_model.OriginalCheckpointFileFdPath` and sets `STRATA_MODEL_PATH` from it.
- After the truthful hello **and** `VerifyBundleClosureAsync`, a null model fd path **fails closed** before OpLoad
  (“no unsafe scan of unheld files”).

### Defect 2 — actual upstream CLI model dispatch
`dulche_strata_candidate_worker.cpp`:
- `build_engine_args(uint32_t context)` rewritten: no `"generate"`, no `"--pack"`; it reads the held GGUF via
  `trusted_held_path("STRATA_MODEL_PATH")` (throws if unset) and builds
  `--model <held-gguf> --serve [--max-context N]`. `--model` is the documented qwen35moe family-switch key
  (`generate.cpp:2381`, `q36_engine.cu:3749`). Exactly one engine spawn is preserved.
- Header architecture comment updated to the real subcommand-less argv.

### Defect 3 — C# relational pattern
`StrataDeveloperArtifactSelection.cs`:
- Replaced `Count is < (candidate ? 1 : 2) or > 128` with ordinary comparisons
  `Count < (candidate ? 1 : 2) || Count > 128`.
- Candidate now requires **exactly one** `.gguf` in both `Capture` and `ProtectedRoot.ReadCheckpointAsync`
  (`Count(...) != 1`); split/multi-GGUF closures are precisely refused (no false universal claim).

## Authored tests
`Haven.Infrastructure.Tests/StrataDeveloperArtifactAuthorityTests.cs` (runs in Root combined normal managed):
- `Candidate_selection_requires_exactly_one_GGUF_and_refuses_split_or_empty_models` — exactly one GGUF accepted;
  split/multi-GGUF refused; zero-GGUF candidate refused (defect 3 fix + split-refusal policy).

`Dulche.Runtime.Tests/ManagedProviderDulcheAdapterOriginalTests.StrataCandidateFactorySelection.cs`:
- `ProfileControlledModelLease` now exposes a synthetic `FdPath` + `FdPathReads` counter.
- `Verified_candidate_factory_consults_the_held_GGUF_model_fd_path_before_native_start` (NOTRUN until Root provides
  env + compiled binary): against the genuine `6744db6d…` binary, asserts the candidate worker consults the held
  GGUF model fd-path lease (counter == 1) before native start, then truthfully refuses non-CUDA — no invented
  Ready/CUDA. This is a real wiring probe, not a source-string mirror (consistent with the existing genuine-binary test).

## Consistency verification (build-free)
- No remaining `build_engine_args(directory, context)`; single `build_engine_args(context)` call site only.
- No remaining `Path.Combine(held.Metadata.WorkerRoot/EngineRoot,…)`.
- Braces/parens balanced on every edited file; the single pre-existing `+1` `}` in
  `StrataDeveloperArtifactAuthorityTests.cs` is the JSON literal `"…workerRoot…\"/other\"}"` (documented pass-15).
- `directory`/pack_dir still retained only for the helper tokenizer-dir frame; model argv now uses `STRATA_MODEL_PATH`.

## Explicit NOT RUN / NOT performed
- NO C#/.NET/native/CMake/CUDA compile or run. New native-gated test marked NOTRUN pending Root’s serial SDK slot +
  env + compiled binary.
- NO model/weights load, no engine/model/GPU/tools/vision/MTP acceptance; no PC/live inference.
- The candidate **CUDA-true** hello path (which would reach the null-model-path fail-closed guard) is not runnable
  here: the only genuine binary is CUDA-false and truthfully refuses earlier. The guard is verified by code review.
- NO credentials/global config/cap changes; no git commit/push/deploy.

## OPEN / BLOCKED handoff
- BLOCKED — actual signed candidate package/GPU/model acceptance: full upstream compile/install/model/GPU acceptance
  remains BLOCKED pending Root’s SDK/native slot, the compiled changed candidate, env vars, and genuine CUDA-true
  diagnostics. No fabricated production receipt/signature.
- NOTRUN — `VerifyBundleClosureAsync` re-verification and the null-model-path fail-closed guard require a running
  CUDA-true hello; not reachable with the genuine CUDA-false binary.
- Mapping of package-emitted receipt fields (`engineSha256`/`helperClosure`/`runtimeDepsLockSha256`) to the signed
  inventory fields is implemented in `build-package.py`/`strata-build-receipt.json` shape; genuine receipt population
  for the real binary remains gated on the trusted setup.
