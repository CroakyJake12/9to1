# Lane 5 — pass 5 — compiler-backed bounded Dulche correction

Source checkpoint: `9e44e8ebd62ffe8c3dd38a25ee98cd39a7c8f8d1` (frozen). Worktree `/workspace/9to1-mercury-dulche` remains common-base HEAD `fb708` + matching dirty/untracked lane work. **No .NET/build/test/app/display execution was performed** (no serialized slot granted). Source/tests/static only.

All 26 frozen-manifest files still matched before this pass. Changed files (7) and their **new** SHA-256:

- `9to1 Workspace/Home/Source/Home/Core/HomeDulcheBrokerAdapters.cs` — `cdbf4989105b6bda8f9cec62c6a2d73da0fe34e7ad25d6486e28e4eea29b986e`
- `9to1 Workspace/Home/Source/Home/Core/HomeDulcheRuntimeContracts.cs` — `81e7a7b69193afa49a6e80afe71e9352b5af6e1ee15b3f77bca36137a96551e1`
- `9to1 Workspace/Home/Source/Home/Core/HomeDulcheRuntimeObservationSource.cs` — `2f2fa29af0d334a31db1b66b95ae53c091c77f218de4a19d9ecff4e09c5a5aff`
- `9to1 Workspace/Home/Source/Home/Core/HomeDulcheRuntimeService.cs` — `3e932fdcad6eff15b977f5b7d17909aa813e597cad1733f6076ed5b280029802`
- `9to1 Workspace/Home/Tests/HomeDulcheRuntimeServiceTests.cs` — `7657c2d741b52b0c604baee591ad6bff42dcf63d84fc00a386920072d0e4503b`
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.cs` — `23d0e20be10c673e12a110e63b216ae950037e3451503d7dc80ba274f0f3e41a`
- `9to1 Workspace/shared/src/Haven.Desktop/Services/SpacesBrowseAppContentSession.cs` — `f4a84c316939a29771bb105c9f70f6b91216619245c71dcb600249a87ef6f48f`

## Compiler diagnostics addressed (root build log: 0 warnings, 14 errors)

1. **CS1739 ×11** (`HomeDulcheRuntimeServiceTests.cs` 50/80/94/111/128/144/159/244/319/396/415): record primary ctor is `FakeBrokerAdapter(bool Supported, ...)`; named arguments used lowercase `supported:`. Corrected to `Supported:` (identical 11 sites). True/false scenarios and observation values preserved; no fake overloads added, no product change.

2. **CS8121 ×2** (`...Tests.cs` 372/435): `Assert.ThrowsAsync<InvalidOperationException>` statically returns `InvalidOperationException`, so matching it as `AggregateException` could not compile. Replaced with typed helper `AssertOriginalPreserved(root, expected)` that asserts the real intended original error **by identity** — both the single-fault direct case and the multi-fault aggregate case (every original cause enumerated). No catch-any, no weakened assertion. The one-fault tests now assert exact original identity.

3. **CS0029** (`HomeDulcheBrokerAdapters.cs` 177): `await process.WaitForExitAsync(token)` yields `void`, not `bool`. The owned systemctl lookup was reworked so exit is awaited correctly and success is derived only after actual settlement; see item 8.

4. **Integrated Desktop malformed C#** (`ChatHavenScene.cs:850`): `Dispatcher.UIThread.InvokeAsync(Task)(...)` is invalid. Replaced with the canonical typed pattern already used at `SpacesHomePage` (301/647): `Dispatcher.UIThread.InvokeAsync<Task>(() => RecoveryView.CaptureOriginalActionSettlement()).GetTask()`. The genuine recovery-loader action-settlement is captured on the real UI thread and handed to the external navigation driver; no CompletedTask substitute, no self-await, no fake ACK.

## Semantic repair (small, causal)

5. **Repeated-start test corrected** (`...Tests.cs`): the two public `StartAsync` wrappers are distinct caller references; reference-equality of the wrappers was wrong. The test now proves coalescing via ONE actual source observation (`DelegatingObservationSource.ObserveCalls == 1`) plus the SAME retained refusal cause by identity (`Assert.Same(firstError, secondError)`), and asserts the Degraded observation is retained.

6. **Failing-close tests no longer rethrow on implicit disposal**: `Stop_preserves_original_fault_by_identity` and `Genuine_start_fault_fails_close` replaced `await using` with explicit `finally` disposal that observes the SAME expected close/dispose failure (`RecordExceptionAsync` + `AssertOriginalPreserved`), so the asserted fault is drained with precise identity instead of escaping the test.

7. **Stale-generation denial (item 6)**: `StartCore:101` returned success after retirement. Added private `StartRetiredException`; a borrower whose start completes after retirement is now DENIED success (throws), while the normal Stop that caused retirement stays a truthful clean stop (no owned cleanup failed). Updated `Stop_discards_a_stale_start_completion` to assert the denial and the clean stop. `One_borrower_cancelled_wait_does_not_cancel_shared_start` added: cancelling one borrower's WAIT does not cancel the shared start or another borrower (source call count stays 1).

8. **Readiness-refusal classification bound by type, not text (item 7)**: added private `RuntimeUnavailableRefusal` thrown for a non-Ready observation; `IsExpectedDiagnostic` classifies `StartRetiredException`/`RuntimeUnavailableRefusal` **by type**. A genuine source `InvalidOperationException` that merely starts with `HomeDulcheRuntimeUnavailable:` is no longer suppressed — new test `Genuine_prefix_looking_source_fault_still_fails_close` proves a prefix-looking source fault still fails close and is retained by identity.

9. **Adapter lookup cleanup (item 8)**: `DefaultBrokerPidSourceAsync` now ALWAYS bounds, terminates only the owned systemctl child, awaits its real exit, joins stdout/stderr readers, and retains primary + cleanup causes on every exit path. `TryKillAndReap`/`RunToExitAsync` removed (they swallowed kill/reap failures and ignored exit wait). Cleanup that cannot confirm settlement is thrown truthfully; caller cancellation is rethrown only after confirmed cleanup; a clean timeout surfaces a `TimeoutException`. Never touches the borrowed broker.

10. **Capability same-generation binding (item 9)**: `HomeDulcheCapabilityObservation.IsSameGeneration` compared provider/model strings only. It now binds to the SAME observed broker process/endpoint (PID + transport identity) and SAME verified model artifact SHA (typed `HomeDulcheBrokerObservation`/`HomeDulcheModelEvidence`); absent a genuine binding it fails CLOSED. Test `Restarted_broker_with_same_ids_is_not_same_generation` added. No parallel admission framework, constructor-success bool, or fabricated receipt introduced. In production wiring, no issuer binds a broker/artifact receipt at startup, so the positive capability axis remains BLOCKED (never inferred) — that is the precise missing integration reported below.

11. **Recovery queue refusals surfaced (item 10)**: `SpacesBrowseAppContentSession.WireChatRecovery` no longer only Trace-logs. It surfaces structured `SpacesWorkspaceActionRefusal` when no host seam exists, when the page is retired/not current (`IsPublicationCurrent`), when action settlement cannot be captured, or when the host queue refuses; it hands the genuine UI-thread settlement to the external navigation driver and does not await the included action. Child close→ACK→native release pattern untouched (root/lane4 integration hunk NOT applied).

## Tests added/changed and causal intent

- `Repeated_start_after_degraded_refusal_coalesces_one_source_operation` — proves one source op + same retained cause, not caller-wrapper identity.
- `Stop_discards_a_stale_start_completion` — now asserts the stale borrower denial and clean stop.
- `Stop_preserves_original_fault_by_identity`, `Genuine_start_fault_fails_close` — drain the SAME expected close failure in `finally` with precise identity.
- `One_borrower_cancelled_wait_does_not_cancel_shared_start` — wait-cancellation isolation.
- `Genuine_prefix_looking_source_fault_still_fails_close` — type-bound classification, prefix-looking fault stays observable.
- `Restarted_broker_with_same_ids_is_not_same_generation` — same names ≠ same generation; unbound capability fails closed.

## Remaining ownership / runtime / asset / route gaps

- **Compile only**. No compile/test/app run granted; `root-build-receipt.json` states tests are NOT RUN/blocked. This pass is source/tests.
- **Models route / `IsOwnedCanvas`** hunk remains ROOT/lane4 integration (SHA `5c6a3eb1...`). Not applied here; route activation stays NOT RUN until root merges/checks it.
- **Positive capability axis** stays BLOCKED in production: no startup issuer binds a broker-process/artifact receipt to a `HomeDulcheCapabilityObservation`; only an already-admitted same-generation observation would satisfy it. Reporting this precise missing integration; no waiver.
- **Native/integrated Desktop** still not compiled; the `ChatHavenScene`/`SpacesBrowseAppContentSession` fixes are source-only and await a root integrated compile slot.
- **Strata/Thunder/upstream** untouched; Dulche multi-engine canonical routing preserved.

## One next root-owned check

Allocate a NEW exact-source focused service compile (same pinned harness shape) to confirm the 14 diagnostics are resolved and 0 warnings; then, in a separate granted slot, run the bounded actual Home Dulche unit tests (VSTest) to verify the new lifecycle/causality tests pass against the real frozen public dependencies.
