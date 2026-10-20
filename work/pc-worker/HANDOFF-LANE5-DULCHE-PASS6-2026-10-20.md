# Lane 5 — pass 6 — exact acceptance feedback fixes + capability receipt issuer

Source checkpoint: `7accbc83435b11b748ad3e6298add62b59c26ca8` (frozen). Worktree `/workspace/9to1-mercury-dulche` remains common-base HEAD `fb708` + matching dirty/untracked lane work. **No .NET/build/test/app/display execution was performed** (no serialized slot granted). Source/tests/static only.

## 1. Four typed-readiness-refusal test expectations fixed (exact acceptance feedback)

Root ran the exact source service gate and got 28 PASS / 4 FAIL / 0 skip of 32. All four failures were the SAME xUnit exact-type mismatch: production correctly throws the private `HomeDulcheRuntimeService.RuntimeUnavailableRefusal` (a subtype of `InvalidOperationException`), while the tests demanded the exact base type. Preserving the intentional typed refusal contract (no production change to plaintext classification, no removed/caught assertions), the four expectations now accept the typed readiness refusal:

- `Service_throws_structured_unavailable_when_observation_is_degraded`
- `Non_ready_observation_with_empty_deficiencies_still_fails_closed`
- `Repeated_start_after_degraded_refusal_coalesces_one_source_operation` (both first and second start)
- `Stop_succeeds_after_expected_readiness_refusal_start`

Each was changed from `Assert.ThrowsAsync<InvalidOperationException>` to `Assert.ThrowsAnyAsync<InvalidOperationException>`. All structured assertions are retained unchanged: thrown prefix (`HomeDulcheRuntimeUnavailable`), Degraded lifecycle retention, `ModelVerified == false`, `CurrentObservation` state, one-source coalescing (`ObserveCalls == 1`), same retained cause by identity (`Assert.Same(firstError, secondError)`), and the clean `StopAsync` after the refusal. The two tests that genuinely throw `InvalidOperationException` directly (`AttachOriginalWork_is_refused_after_stop_begins`, `Stop_preserves_original_fault_by_identity`, `Genuine_start_fault_fails_close`) remain exact-type and were not touched.

## 2. ChatHavenScene.cs:850 — typed flattening fix (second definite causal bug)

`RecoveryActionSettlement` used `Dispatcher.UIThread.InvokeAsync<Task>(...).GetTask()`, which yields `Task<Task>` upcast to `Task`. Joining it only awaited the UI-thread callback that *produced* the inner settle, not the genuine recovery-loader action settlement — so external navigation ACK'd before the real loader original settled. Fixed with the approved donor pattern (`Dispatcher.Invoke.cs` itself uses `.GetTask().Unwrap()`):

```csharp
public Task RecoveryActionSettlement =>
    Dispatcher.UIThread.InvokeAsync<Task>(() => RecoveryView.CaptureOriginalActionSettlement()).GetTask().Unwrap();
```

Focused causal tests added in `framework/CUI/Runtime.Tests/CuiSceneHostOriginalPublicationTests.cs` (same harness that exercises `CaptureOriginalActionSettlement`/`WhenActionsIdleAsync` with a held loader):

- `DispatcherWrapped_recovery_settlement_is_flattened_so_navigation_ack_waits_for_the_real_loader_action` — proves the flattened (Unwrap) task is NOT complete while the genuine loader action is held, while the buggy unflattened upcast completes as soon as the dispatcher callback runs (the exact ACK-before-settlement gap). After the action settles, the flattened task completes successfully.
- `DispatcherWrapped_recovery_settlement_retains_the_genuine_loader_fault_by_identity` — proves the flattened path retains the genuine loader fault by identity (not swallowed).

No new fire-and-forget, no catch-success, no fabricated `CompletedTask`. Cancellation/fault/current-owner semantics remain covered by the existing suite.

## 3. Capability receipt issuer / integration contract (bounded, unblocked)

Added `HomeDulcheCapabilityReceipt.cs` (`IHomeDulcheCapabilityReceiptIssuer` + `HomeDulcheCapabilityReceiptIssuer`). It issues a typed `HomeDulcheCapabilityObservation` ONLY from genuine same-generation inputs: an actually observed broker generation (PID/endpoint/socket), a verified model artifact SHA-256, and a current verified generation-capability observation (same provider/model identity). Absent any evidence it returns null, preserving default Degraded — the axis is never inferred from names/platform.

Tests (fixture-owned data) added in `HomeDulcheRuntimeServiceTests.cs`:
- `Issue_binds_a_receipt_to_the_observed_broker_generation_and_verified_model_SHA`
- `Issue_returns_null_when_no_verified_current_capability_observation_is_supplied`
- `Issue_returns_null_when_verified_model_or_live_broker_evidence_is_absent`
- `Issue_rejects_a_verified_observation_that_describes_a_different_model_or_provider`
- `Issued_receipt_fails_closed_after_the_same_identified_broker_restarts`

### Precise integration BLOCKED (reported, not pretended)
The positive production generation-capability axis remains BLOCKED: a real production issuer requires a current **verified generation-capability observation**, and no such engine evidence exists (generation capability is intentionally never probed during startup, and no startup wiring binds a genuine broker/artifact receipt today). The issuer/validation contract is implemented unblocked with fixture-owned data; the production issuer wiring stays BLOCKED until genuine engine evidence exists. No receipt is fabricated.

Extra15 coordination: typed handoff manifest `work/pc-worker/LANE5-CAPABILITY-RECEIPT-MANIFEST-2026-10-20.json` documents the packaged-model asset contract (manifest fields, SHA-256 re-hash, canonical blob path) and the issuer's evidence requirements. **No edit** to extra15/extra16 folders or Thunder/Strata/engine pins; canonical multi-engine Dulche routing preserved.

## Changed/new files and SHA-256

- `9to1 Workspace/Home/Tests/HomeDulcheRuntimeServiceTests.cs` — `262e52eacfebc89a267d9b631e73c94a281d242b2e85aeb4c9efd2f53bbec8c7`
- `9to1 Workspace/Home/Source/Home/Core/HomeDulcheCapabilityReceipt.cs` (new) — `c10abdf96b939f9710948cda3ff8215708781e7f04c2f5d96e0340c09d4459c7`
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.cs` — `46ad38ea37889a16cc98430c66f4e1d9d8549a65cc9bc83a9033a3377b0c49d1`
- `framework/CUI/Runtime.Tests/CuiSceneHostOriginalPublicationTests.cs` — `2fe1d204fa91d746bc4aaf61734b32bd63eb6579de906baeb8af45476f846065`
- `work/pc-worker/LANE5-CAPABILITY-RECEIPT-MANIFEST-2026-10-20.json` (new) — `0264b5a2cda553cdd342e6056fd9ec3ad628ed2ed65e16073c3c4be0178147d8`

## Observations / left untouched
- The same `InvokeAsync<Task>(...).GetTask()` upcast latent pattern also appears at `SpacesHomePage.cs` (301/647). Those are root/lane4-owned files; per instruction they were NOT edited here. Flagged for the owner/lane4 to apply the same `.Unwrap()` flattening.
- Root/lane4 owned App/host files untouched; no credentials/provider/config/global changes; no commit/push/deploy.

## Next root-owned checks
1. Re-run the exact-source service gate for the Home Dulche unit tests → expect 32/32 PASS (4 prior FAIL now fixed), 0 warnings/0 errors.
2. Grant a bounded integrated Desktop compile for the includingChatHavenScene`/CUI runtime test changes (incl. the two new flattening causal tests).
3. Decide the capability-receipt production wiring once genuine generation-capability engine evidence exists (currently BLOCKED).
