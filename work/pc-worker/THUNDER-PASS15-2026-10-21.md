# Thunder Pass-15 — profile-aware protected hardware producer + verified-owner candidate profile/python dataflow (lane 6)

Date: 2026-10-21. Session continued from pass-14 (frozen `ses_ea553125dffe0sx2MyJhLF3q7w`, receipt `35cf515a…`,
frozen on `native12-immutable/build/dulche-strata-candidate-worker` sha `6744db6dd677b9d34606e25a3caa52c7ebc7892c97ca4fde1881572dab0063c3`).
Mode: SOURCE ONLY. No SDK/dotnet/g++/native/CMake/CUDA or PC execution, no model downloads, no credentials/
global-config/cap changes, no git commit/push/deploy, no edits to other worktrees or frozen reviewer copies.
Official v0.2.2 engine 0.1.47 / `011c9e6…` / 4625 lock / licences / legacy V1 default rollback left unchanged.

## Objective (owner’s two production gaps)
1. Extend the real protected hardware producer (`StrataHardwareObservationSource`) to accept the exact verified
   candidate ABI2/source hello while preserving the ABI1 default and the current held file / SameTask / full
   cleanup. The genuine candidate binary (CUDA false) must be classified as truthful unsupported hardware —
   not a legacy ABI mismatch, not a fake Ready.
2. Populate `InstalledProfile` / `OriginalPythonInterpreterPath` from the ACTUAL approved package/artifact owner
   (`HomeApprovedStrataDeveloperArtifactSource`) from a VERIFIED signed inventory + protected file binding — not
   test-only metadata, not an untrusted caller string, and not a silent default promotion.

## Implementation

### 1. Profile-aware hardware producer — `StrataHardwareObservationSource.cs`
- `Origin` const split into `LegacyOrigin` (`015b075…`) and `CandidateOrigin` (`011c9e6…`).
- After acquiring the SAME privately issued worker lease, the probe reads `worker.InstalledProfile`
  (`DemandCurrentOriginalBinding()` first) and selects:
  - `null` → legacy V1 / ABI1 (`LegacyOrigin`, `abiVersion=1`);
  - `Profile=="candidate" && AbiVersion==2 && SourceIdentity == StrataCandidateWorker.VerifiedCandidateSourceCommit`
    → candidate V2 (`CandidateOrigin`, `abiVersion=2`);
  - anything else → `UnauthorizedAccessException` fails closed BEFORE any probe launch (no process started).
- Hello parse branches:
  - ABI1 (byte-for-byte preserved): `version==1`, origin==LegacyOrigin, cuda, nccl, comma-string registrations, End.
  - ABI2: `version==2`, origin==CandidateOrigin, cuda, nccl, family, End; require `family=="qwen35moe"`; if
    `!cuda` throw truthful `InferenceEngineException(ProviderUnavailable, "No actual CUDA build exists in this
    candidate worker.")` (mirrors the candidate worker’s own refusal) and skip the type6 GPU parse. The `finally`
    still kills/joins the process and closes the lease.
- `RuntimeBuild` becomes `strata/{origin}/abi{abiVersion}/{sha}` (legacy restore `strata/015b075…/abi1/{sha}`
  unchanged; candidate `strata/011c9e6…/abi2/{sha}` matches the worker’s `RuntimeFingerprint`).
- Candidate registrations exposed as `[family]` (`["qwen35moe"]`). CUDA-true candidate type6 parsing is authored
  but NOTRUN/unverified (no such build exists here; the only genuine path is the CUDA-false refusal).

### 2. Verified-owner candidate profile/python dataflow (typed internal extension)
- `StrataDeveloperBuildInventory` gains optional trailing fields `Profile`, `AbiVersion`, `SourceIdentity`
  (backward-compatible; v1 constructors unchanged).
- `StrataDeveloperArtifactSelection` gains optional `PythonInterpreterPath` (explicitly reviewed host resource).
- `StrataDeveloperArtifactCapture` now carries `StrataNativeProfile? Profile` + `string? PythonInterpreterPath`.
- `VerifyIndependentInventory` is split by signed `SchemaVersion`:
  - **1** → legacy V1 path preserved byte-for-byte (ABI1 build, `RequiresCuda:true`, profile null).
  - **2** → candidate path: requires `Profile=="candidate"`, `AbiVersion==2`,
    `SourceIdentity == VerifiedCandidateSourceCommit`, `RuntimeBuild == "strata/011c9e6…/abi2/{sha}"`, matching
    worker SHA, GGUF weight format + `qwen35moe` family + registration present. Returns the verified
    `StrataNativeProfile("candidate",2,VerifiedCandidateSourceCommit)`.
  - any other schema → fail closed.
  - `RequiresCuda` for candidate is derived truthfully from the inventory’s declared compute minimums
    (`>0` ⇒ CUDA build), never a manufactured CUDA receipt; a no-CUDA candidate stays `RequiresCuda:false` and
    the genuine hardware probe remains the runtime source of truth.
- `Capture` is profile-aware for the checkpoint inventory: legacy keeps its `config.json` + `.safetensors`
  requirement and count≥2; candidate requires ≥1 `.gguf` (engine-native pack) and count≥1. `Requirements.WeightFormat`
  is `"GGUF"` for verified candidate, else `"Safetensors"` (legacy unchanged).
- Approved `PythonInterpreterPath` is surfaced only when the verified candidate profile exists; legacy/unverified
  installs never carry it (fail closed, no default promotion).
- `HomeApprovedStrataDeveloperArtifactSource.Lease` now overrides `InstalledProfile => Grant.Selection.Profile`
  and `OriginalPythonInterpreterPath => Grant.Selection.PythonInterpreterPath`.
- **Critical wiring fix**: `StrataNativeArtifactSource.WorkerLease` now also overrides `InstalledProfile` and
  `OriginalPythonInterpreterPath` from `held.Metadata.Profile` / `.PythonInterpreterPath`. Without this the
  hardware probe’s own worker lease (from the same source) and the factory’s `heldBinary` would always see
  `null` → candidate ABI2 would never be selected on the real protected path. This closes the loop:
  verified owner → capture → installation lease → `StrataNativeArtifactSource` capture → WorkerLease →
  hardware probe / factory selection.

### 3. Authored owning InfrastructureTests — `StrataHardwareObservationSourceTests.cs` (NOTRUN)
Drive the REAL `StrataHardwareObservationSource` against the genuine candidate binary (env
`DULCHE_THUNDER_ACCEPTANCE_BINARY`/`_SHA256`):
- `Candidate_profile_with_foreign_source_fails_closed_before_any_probe_launch` — foreign source ⇒ un8
  access before launch; zero worker reads; lease closed once.
- `Unknown_profile_fails_closed_before_any_probe_launch` — foreign profile/ABI ⇒ fail closed, never downgraded.
- `Legacy_V1_default_still_attempts_the_ABI1_probe_after_profile_selection` — null profile proceeds (open
  failure on the intentionally absent executable), one owned probe.
- `Genuine_candidate_binary_reports_truthful_non_CUDA_unavailability` — actual candidate binary ⇒
  `InferenceEngineException(ProviderUnavailable)`, message “No actual CUDA build exists”, NOT an ABI handshake
  mismatch; single worker read (one owned probe); lease closed once; all retained tasks completed.

### 4. Authored verified-owner/capture tests — `StrataDeveloperArtifactAuthorityTests.cs`
- `Verified_candidate_signed_inventory_populates_profile_and_python_path` — v2 signed inventory ⇒ verified
  `StrataNativeProfile`, python surfaced, `WeightFormat == "GGUF"`, `RequiresCuda == false` (truthful no-CUDA),
  `qwen35moe` family, no Tools advertised.
- `Verified_candidate_inventory_with_foreign_source_identity_fails_closed` / `…_wrong_abi_or_profile_fails_closed`.
- `Legacy_signed_inventory_keeps_null_profile_and_ignores_python_path` — v1 signed inventory + python config ⇒
  profile/python both null, `WeightFormat == "Safetensors"`.
- `Untrusted_config_python_path_without_verified_candidate_is_ignored` — no signed inventory ⇒ no profile/python.
- `Invalid_python_interpreter_path_is_rejected_before_review`.
- `Approved_owner_retains_verified_candidate_profile_and_python_from_selection` (Linux) — `SelectOriginalDeveloperArtifacts`
  on the real `HomeApprovedStrataDeveloperArtifactSource` stores the verified profile + python in its capture.

## Validation performed (build-free / functional)
- All edited C# regions reviewed for syntax/type coherence (no SDK → no dotnet compile; Root owns the next
  normal-managed compile slot).
- BRACES/PARENS re-balanced per file (raw counts; the single `+1` `}` in `StrataDeveloperArtifactAuthorityTests.cs`
  is the pre-existing JSON-string literal `"…workerRoot…\"/other\"}"` at the duplicate-property test, not
  structure).
- Legacy V1 hardware path, v1 signed inventory path and ABI1 default preserved byte-for-byte.
- Pass-14 factory tests untouched; existing `StrataProtectedArtifactSourceTests` / `StrataDeveloperArtifactAuthorityTests`
  not weakened.

## Explicit NOT RUN / NOT performed
- NO C#/.NET/native/CMake/CUDA compile or run (source/test fix only; all new tests marked NOTRUN pending Root’s
  serial SDK allocation + env vars + compiled binary).
- NO GGUF/Safetensors model loading, no weights download, no engine/model/GPU/tools/vision/MTP acceptance; no
  PC/live inference.
- NO credentials/global config/cap changes; no git commit/push/deploy; no edits to other worktrees or frozen
  reviewer copies; no edits to unrelated systems.
- Official v0.2.2 pin, complete 4625 lock, licences and legacy rollback left unchanged this pass.

## OPEN / BLOCKED handoff (not pretending integrated)
- BLOCKED — real signed candidate receipt: no actual `strata-build-receipt.json` + developer-signed
  `IndependentBuildInventory` envelope for the genuine `6744db6d…` binary exists in this environment. The typed
  dataflow and the real owner wiring are complete and verified by synthetic signed fixtures (contract/refusal
  only); populating a live `InstalledProfile`/python from a genuine receipt requires the trusted setup: a signed
  candidate inventory for the actual build (worker SHA match + source `011c9e6…`). Do not fabricate one.
- NOTRUN/unverified — candidate **CUDA-true** hardware-probe type6 frame layout (no such build exists here; only
  the CUDA-false truthful refusal is exercised). The `!cuda` path throws before type6, so the genuine binary is
  unaffected.
- NOTRUN — full end-to-end real-chain probe (verified owner → `StrataNativeArtifactSource` → WorkerLease →
  probe) and the genuine-binary probe/factory tests require Root’s SDK slot + env vars + binary; the WorkerLease
  profile propagation fix is covered by code inspection + capture reflection test.
