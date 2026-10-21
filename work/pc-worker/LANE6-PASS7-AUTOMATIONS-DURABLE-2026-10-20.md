# Lane6 Pass7 — Scheduled durable integration and real SQLite claim store

Date: 2026-10-20 (Tue). Branch: `codex/mercury-automations-20261020`. Working tree root: `/workspace/9to1-mercury-automations`.
Scope: SOURCE/TEST AUTHORING ONLY. No .NET/restore/build/test executed, no app/display/network/models/PC/credentials/provider/cap/global config changes, no commit/push/deploy. Root owns the SDK gate.

The exact lane assignment is `parallel6-lane6-automations-pass7.prompt.md`. This handoff records the actual source checkpoint, the exact shared dependency used (or clearly pending seam), authored tests/commands, source hashes, and precise NOT RUN/BLOCKED.

## What is done (actual implementation, not interfaces)

- Compile bug fixed: `AutomationGraphRuntime.cs` `applied continuation = BuildSnapshot(...)` -> `var continuation`.
- Real durable resume engine (`AutomationGraphRunner.ResumeAsync`):
  - Validates persisted continuation identity against the admitted pinned graph (GraphID/pinned definition revision/schema/source digest) before any side effect.
  - Validates the exact persisted frontier against topological order.
  - Enforces the execution budget.
  - Satisfies a persisted Wait from its **persisted due observation** (`ResumeAt`) exactly once and advances B once; never recomputes a moving `UtcNow+duration`. Before due it re-suspends with the ORIGINAL due time.
  - Approval is never auto-satisfied; it stays pending (fail-closed) until a durably recorded decision resolves the branch.
  - Completed-node typed trace (false/null/absent/unavailable/redacted/error), branches, budget, prior identity are preserved on the owner snapshot.
- Owner continuation payload (`AutomationGraphContinuation`) with canonical graph identity, trace, frontier, activated edges, typed outputs, consumed budget, pending yield and `ApprovalDecision`. It is deliberately the **owner payload riding on the reviewed shared envelope** — not a competing shared execution/admission framework. Serialization round-trip/digest is a consistency/tamper check, never an authentication grant; live authority is revalidated by the owning admission/action paths.
- Built-in Wait/Approval nodes yield durable suspension; `until` absolute parse; long-wait cap widened to 31536000000 ms via `long`.
- `ScheduledTaskRunner`: scheduled graph payloads route to the durable path (`ExecuteDurableGraphLeasedAsync`/`ExecuteDurableGraphCaptureAsync`) which allocates ONE stable RunID, captures the canonical graph identity/pin, and persists a real Wait/Approval suspension via `SuspendRunAsync` (truthful non-terminal result, releasing executor/model resources). Missing durable store / missing identity refuses honestly before executing a waiting workflow. Terminal outcomes persist with the exact uncertainty rethrow (`ScheduledTaskCompletionUncertaintyException`). `ResumeScheduledGraphAsync` resumes the SAME Run/pin after store reopen (no new RunID, no instruction fallback, no restart-from-NodeA); a newer Active revision never repoints an existing Run.
- Real SQLite atomic claim store (`AutomationRepository.RunClaims.cs` + `IAutomationRunClaimStore`): same-transaction status/revision/identity/pin/token/continuation-digest CAS **before resumed effects**, single primary-key winner, losing claim has zero effects, original immutable Run token/pins preserved, business disabled/deleted before effects. `PRAGMA busy_timeout` makes the concurrent claim race deterministic (winner commits, loser sees the existing claim and loses).
- `AutomationRunContinuationCoordinator`: sweep (claim -> resume -> persist terminal/re-suspend -> release) and `ApplyApprovalDecisionAsync` (fail-closed Home auth seam, strict Run/node/revision/current-actor fencing, records decision durably, never sets Home `permissionGranted`, never auto-approves). No timer/background loop.
- `GetDueAsync` active same-Run scheduling guard: a definition whose current lease token is held by an active non-terminal Run is not re-offered, so a long wait beyond the 15-minute lease cannot spawn a duplicate fresh Run/token.
- Migration v30 `automation_run_claims` registered in `Migrations.cs`.

## New/edited source (not compiled — root gate)

New files:
- `shared/src/Haven.Application/Automations/AutomationGraphContinuation.cs`
- `shared/src/Haven.Application/Automations/AutomationRunContinuationContracts.cs`
- `shared/src/Haven.Application/Automations/AutomationRunContinuationCoordinator.cs`
- `shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.RunClaims.cs`
- `shared/src/Haven.Infrastructure/Database/Migrations/AutomationRunClaimStoreMigration.cs`
- `Browse/Host.Tests/AutomationGraphDurableContinuationTests.cs` (7 real-SQLite tests)

Edited:
- `shared/src/Haven.Application/Automations/AutomationGraphRuntime.cs`
- `shared/src/Haven.Application/Automations/BuiltInAutomationActionNodes.cs`
- `shared/src/Haven.Application/Automations/ReusableDeviceWorkflowRunner.cs`
- `shared/src/Haven.Application/Automations/ScheduledTaskRuntime.cs`
- `shared/src/Haven.Application/Automations/ScheduledGraphAutomation.cs`
- `shared/src/Haven.Application/Automations/NodeGraphAutomationAdapter.cs`
- `shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.cs` (partial marker, GetDue guard, added `IAutomationRunClaimStore`)
- `shared/src/Haven.Infrastructure/Database/Migrations.cs`
- `Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj` (added `<Compile Include="AutomationGraphDurableContinuationTests.cs" />`)
- `shared/tests/Haven.Core.Tests/AutomationGraphRuntimeTests.cs`, `ScheduledGraphAutomationTests.cs`
- `shared/tests/Haven.Desktop.Tests/ScheduledTaskRuntimeTests.cs` (pass5/6 retained)

## Source hashes (SHA-256, 2026-10-20)

| File | Hash |
| --- | --- |
| AutomationationsGraphRuntime.cs | 50b8c27bbd9501381cd29c0166b2b83c1c6ee8c3c6a8d1f8856ebbb055ea1666 |
| AutomationGraphContinuation.cs | d0abdcaad9dc045608e81d2456f5f3a7368c03a666f75f1bc10fbe4d819b3c15 |
| AutomationRunContinuationContracts.cs | 9c9c8b360afbf435b481db29b5e9157ab11cd53018a30124e4baf4375538cbc5 |
| AutomationRunContinuationCoordinator.cs | 8643d3f4bb2fbd7096ec7a98b93311a4c0db633ed4f950871530cee2be659c87 |
| BuiltInAutomationActionNodes.cs | abea6016d8e60ccb988e38e4d8f06a23b2be139abdc571f42ff5a479f921d441 |
| ScheduledTaskRuntime.cs | e108694785b92e622075880c182d26a86f552f8230f73231019e04a438ebfb2c |
| ScheduledGraphAutomation.cs | 8640c77a537d4c058e86705b393e520a9626ffe84f9a0c5342a4e1eddab344d4 |
| ReusableDeviceWorkflowRunner.cs | f7c2bab0e0203398a403998d061a2193c834d22b7b4e3864bcdc338b20d3e794 |
| NodeGraphAutomationAdapter.cs | 9cee912a9f3acd07ccbe650417f7cba0ba53a72f125e24fac2ea6849f209c089 |
| AutomationRepository.cs | 499f581cc33d39a63f6afe142e7a5034bc69536144de3b3679658aa3ba9f9888 |
| AutomationRepository.RunClaims.cs | 41f428c152f847c344328844409fea2cd3c1c3efcc677ac633f91c647168fe96 |
| Migrations.cs | 8c2205e7eda45a6356defea8a039d36eebcd933d2bc0b76c55bf725d09e9135d |
| AutomationRunClaimStoreMigration.cs | 4a46747a9e5a7de672e24d3a89edd710fd9bd480a163bda709beaf24196ef6af |
| AutomationGraphDurableContinuationTests.cs | eda4daf02eea74c6fcbc15fd829107b1211ff5b819cfa8ed9bf15a7ceff217f3 |
| HavenOS.Browse.Host.Tests.csproj | 1ba67e94fbb9f77a89d04058a8bf0c7c58791242397b0acef53bf77cfbf44bcb |
| AutomationGraphRuntimeTests.cs | 656225f5a2845097dd3d89902f661a26e62419b42d7d11f50fd004cefbcb018d |
| ScheduledGraphAutomationTests.cs | 2a6660cd573c1f6eba98eea707fb8716ec650776a6b7ecb3c2500ac4d29f0ef4 |
| ScheduledTaskRuntimeTests.cs | 308acaa1dfc7375c763acf902fe3471904e56ac9699c5618d1e998c44cf2a22b |

## New tests (authored, NOT RUN)

`Browse/Host.Tests/AutomationGraphDurableContinuationTests.cs` (real migrated SQLite, store reopen, no sleeps/forged tokens; clock observations are explicit parameters):
1. `Scheduled_graph_A_wait_B_yields_promptly_suspends_same_run_and_resumes_once_each` — A->Wait->B yields promptly, suspends SAME Run with pin+continuation, reopen then resumes SAME RunID, A once / B once.
2. `Reopen_before_due_leaves_waiting_and_candidate_read_is_not_an_approval`.
3. `Two_independent_claimers_have_one_winner_before_any_resumed_effect` — exactly one winner, loser performs zero effects.
4. `Forced_claim_insert_failure_rolls_back_without_mutating_run_or_definition`.
5. `Wait_beyond_fifteen_minute_lease_cannot_spawn_a_duplicate_fresh_run` — active same-Run guard prevents duplicate fresh Run/token; original token preserved.
6. `Malformed_foreign_continuation_stops_before_any_side_effect` — no terminal write, run stays Waiting.
7. `Approval_decisions_are_fenced_authorised_and_distinct_and_never_auto_approve` — unauthorised/stale/cross-node refused; approved/rejected/timed-out/cancelled recorded distinctly; never auto-executed; approval is separate from Home permissionGranted.

Engine/payload tests appended to `Haven.Core.Tests/AutomationGraphRuntimeTests.cs` and `ScheduledGraphAutomationTests.cs` (durable wait no-replay; identity/frontier mismatch refusal; no-moving-due; payload canonical identity round-trip).

## NOT RUN (root gate)

No .NET executed in this lane. The following are the exact commands the root build gate should run (using the guarded dotnet entrypoint, `-m:1`):

- `dotnet restore -m:1 "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj"`
- `dotnet build -m:1 --no-restore "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj"`
- `dotnet test -m:1 --no-build --filter "FullyQualifiedName~AutomationGraphDurableContinuationTests" "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj"`
- `dotnet test -m:1 --no-build --filter "FullyQualifiedName~AutomationGraphRuntimeTests|FullyQualifiedName~ScheduledGraphAutomationTests" "9to1 Workspace/shared/tests/Haven.Core.Tests/"`
- `dotnet test -m:1 --no-build --filter "FullyQualifiedName~ScheduledTaskRuntimeTests" "9to1 Workspace/shared/tests/Haven.Desktop.Tests/"`
- Preserve the 18 repository tests (PASS18) and pass5 scheduler gates (PASS34).

## BLOCKED / pending seams

- **Shared envelope dependency**: the reviewed shared Node Graph envelope/admission contract is being corrected in parallel by the Graph worker. This lane's `AutomationGraphContinuation` is the owner payload riding on that shared envelope; it is not a competing shared execution/admission framework. The actual resume path is self-contained and executable now (engine + SQLite store + coordinator). The "branch-feeding approval into live execution" seam is implemented in the engine (`ApprovalDecision` resolves the branch) and is exercised by the decision-recording tests; full live branch execution against the reviewed shared admission contract remains pending the root-provided exact successor patch (do not guess its API).
- **Home approval authority seam** (`IAutomationApprovalAuth`): present and fail-closed. Where a host has not wired it, approval decisions are refused and the affected Approval stays pending — a fake auto-approval service/UI is never invented. The typed seam is delivered; Home production wiring is a host-side integration step.

## Notes

- No NodeGraph shared primitives were edited (NodeGraphRuntimeRegistry.cs, GraphRuntimeContinuation* remain owned by the Graph worker). No credentials/provider/cap/global config changes. All work uncommitted; no commit.
- `AutomationGraphNodeState`/`CompletedNodes` removed from the owner snapshot; verified no lingering references.
- All new tests use concrete future/past `DateTimeOffset` observations (no fictitious `.Future()` helper, no `fx.Now` field).
