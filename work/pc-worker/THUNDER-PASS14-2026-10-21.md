# Thunder Pass-14 — production candidate V2 factory-selection seam + truthful format capability (lane 6)

Date: 2026-10-21. Session continued from pass-13 (frozen, SHA256 aac426fdc1b7a547af16ddabf72e123c0bec1b0611e9f3b73ae0d43cdba17723).
Mode: SOURCE/PYTHON ONLY. No SDK/native/.NET/CMake/CUDA execution, no PC inference, no model download,
no credentials/global-config/cap changes, no git commit/push/deploy, no edits to other worktrees or
frozen reviewer copies. Verified v0.2.2 pin, complete 4625 blob lock, licences and legacy V1 rollback
left unchanged.

## Objective
Make the real production `StrataManagedInferenceEngineFactory` able to select the candidate V2 worker
using EXISTING trusted installation/profile metadata, preserving the legacy V1 default, with no
automatic untested promotion, no double launch/probe-before-create, no silent V1 fallback, and no
invented public flag/API. Truthful formats: V2 admits engine-native GGUF only and never advertises
Safetensors (V2 Safetensors loading unverified); V1 Safetensors guard/default preserved; unknown
formats fail closed. Original held artifact fingerprint/format/quant/topology/device identity survive
unchanged (no conversion/fabrication).

## Implementation (typed, minimal, in the existing Dulche/Strata boundary)

### 1. Trusted metadata seam — `StrataProtectedArtifactContracts.cs` + `ManagedInferenceEngineFactories.cs`
- `StrataVerifiedInstallationLease` gains two `virtual` members (both default null → legacy V1, so all
  existing concrete subclasses/leases compile unchanged):
  - `StrataNativeProfile? InstalledProfile` (null = legacy V1/ABI1 default),
  - `string? OriginalPythonInterpreterPath` (approved absolute interpreter; null ⇒ candidate refuses
    generation precisely, never ambient PATH/secrets).
- New public sealed record `StrataNativeProfile(string Profile, int AbiVersion, string SourceIdentity)`
  in `ManagedInferenceEngineFactories.cs`; `StrataOriginalWorkerLease.InstalledProfile` virtual default
  null. A bare path/hash/profile label grants nothing — the profile comes only from the trusted
  installation binding.

### 2. Factory selection — `ManagedInferenceEngineFactories.cs`
- Selection runs after the held worker lease is acquired, BEFORE any single native create:
  - `InstalledProfile == null` → legacy V1 (default);
  - `Profile=="candidate" && AbiVersion==2` → require `SourceIdentity == StrataCandidateWorker.VerifiedCandidateSourceCommit`
    (public const = `011c9e600a93bbc9fd451c563501b5831c32bbca`), else fail closed;
  - any other profile/ABI → fail closed.
- `ManagedEngineLease` ctor and `CloseFailedAsync` generalized to `IStrataNativeWorker?`; `CalibrationSource`
  casts `worker as IOriginalInferenceCalibrationSource` (no unsafe downcast). `IStrataNativeWorker` gained
  `ProbeOriginalStructuredToolsAsync` and `DemandExternalOriginalJoin` (both workers implement).
- Single async local `CreateSelectedAsync()` forwards the one selected create task
  (`StrataCandidateWorker.CreateCandidateAfterPublicationAsync` vs `StrataNativeWorker.CreateAfterPublicationAsync`)
  — never a second process; preserves original fault/cancellation/cleanup semantics.

### 3. Truthful V2 format gate — `StrataNativeWorker.cs`
- Candidate `InitializeAsync` now admits ONLY `WeightFormat=="GGUF"` (engine-native qwen35moe), centralized
  topology, positive context, non-empty registration; unknown formats/topologies fail closed. V2 never
  advertises Safetensors (unverified). Legacy V1 `InitializeAsync` keeps its Safetensors-only guard
  byte-for-byte. The candidate worker also binds the approved STRATA_PYTHON from the SAME binding.

### 4. Eligibility — `InferenceEngineContracts.cs`
- Strata validator accepts `WeightFormat is "Safetensors" or "GGUF"` (membership in `engine.WeightFormats`
  still decides which build is admissible). Unknown formats still fail closed; no relabelling.

### 5. Trusted installation propagation — `StrataNativeArtifactSource.cs`
- `InstallationCapture` gains `StrataNativeProfile? Profile` and `string? PythonInterpreterPath`; `Capture(installation)`
  fills them from the SAME trusted installation lease (`InstalledProfile`, `OriginalPythonInterpreterPath`).
- `WorkerLease.OriginalWorker` now carries `held.Metadata.PythonInterpreterPath` so the approved interpreter
  flows from the same owner into `StrataBundledWorker` (V2 needs it).
- `ReadCheckpointAsync` takes `StrataNativeProfile? profile`: verified-candidate profile ⇒ GGUF inventory
  branch (require ≥1 `.gguf`, `WeightFormat=="GGUF"`, family `qwen35moe`, centralized, context>0,
  registration; inventory completeness/total-size checks retained; config.json parse skipped); otherwise the
  legacy Safetensors branch is preserved byte-for-byte (config.json + `.safetensors` + JSON topology check).

### 6. Authored normal managed production factory tests — `ManagedProviderDulcheAdapterOriginalTests.StrataCandidateFactorySelection.cs` (NOTRUN)
Drive the REAL `StrataManagedInferenceEngineFactory.CreateOriginalAsync` with trusted-source fixtures +
a minimal finite scope. No fake project/source-string mirrors; the synthetic binding fixture proves
contract/refusal only (never Ready/CUDA/model success).
- `Legacy_V1_profile_is_default_and_refuses_GGUF_at_the_V1_gate` — null profile ⇒ V1 refuses GGUF at the
  V1 Safetensors gate; single worker read; model+binary closed once.
- `Legacy_V1_profile_still_admits_Safetensors_and_proceeds_to_worker_start` — V1 default admits Safetensors
  and proceeds to open the held executable (absent ⇒ open failure, not a format refusal).
- `Verified_candidate_profile_selects_V2_single_create_path_without_double_probe` — verified candidate ⇒ V2
  passes GGUF gate and proceeds to open the executable; exactly one worker read (no probe-before-create
  double launch); model+binary closed once.
- `Verified_candidate_V2_refuses_Safetensors_without_relabelling` — V2 refuses Safetensors at its GGUF gate.
- `Candidate_with_unverified_source_identity_fails_closed_before_create` — wrong source ⇒ InferenceEngineException
  before create/model acquisition; binary closed once; model never acquired.
- `Unknown_profile_fails_closed_before_create` — foreign profile/ABI ⇒ fail closed; never downgraded to V1.
- `Verified_factory_selection_reaches_truthful_non_CUDA_refusal_with_actual_binary` — end-to-end V2 factory
  path against the ACTUAL compiled candidate binary (sha 6744db6dd677b9d34606e25a3caa52c7ebc7892c97ca4fde1881572dab0063c3)
  using env vars `DULCHE_THUNDER_ACCEPTANCE_BINARY`/`_SHA256` (same as managed13); asserts the truthful
  "No actual CUDA build exists" refusal and model lease retired exactly once.

## Validation performed (build-free / functional)
- Full probe suite (thunder-benchmark) unaffected by this pass (38/38 from pass-13 still green).
- `NativeStrataCapabilityDispatchTests` / `NativeStrataCandidateManagedWireTests` untouched.
- JSON receipt + v0.2.2 lock re-validated (`python3 -m json.tool` OK).
- Reviewed all edited C# regions for syntax/type coherence (no SDK → no dotnet compile; Root D36 owns the
  next normal-managed compile slot).

## Explicit NOT RUN / NOT performed
- NO C#/.NET/native/CMake/CUDA compile or run (source fix only; factory tests marked NOTRUN pending Root’s
  serial SDK allocation + env vars + compiled binary).
- NO GGUF/Safetensors model loading, no weights download, no engine/model/GPU/tools/vision/MTP acceptance.
- NO PC/live inference; no credentials/global config/cap changes; no git commit/push/deploy; no edits to
  other worktrees or frozen reviewer copies; no edits to `LlamaCppRuntime` or unrelated systems.
- Verified v0.2.2 pin, complete 4625 lock, licences and legacy rollback left unchanged this pass.
