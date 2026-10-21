# Lane 3 — Pass 9 Automations: compile + historical-pin + cancellation-safe settlement

Worktree: `/workspace/9to1-mercury-automations` (branch `codex/mercury-automations-20261020`).
Scope: source/tests/static fixes only — no .NET build slot was allocated to this lane, so no
`dotnet`/app commands were run. No commit/push/deploy; all changes are working-tree only.

## Concrete changes (files + rationale)

### 1. `shared/src/Haven.Application/Automations/AutomationNodeGraphDurableAdapter.cs`
- Added `using Haven.Core;` (fixes blocker 1: `AutomationRun` / `AutomationRunStatus` /
  `AutomationRunClaimSettlementKind` were referenced without the Core namespace; all neighboring
  automations files import it).
- Blocker 5 (uncertain settlement must survive real cancellation/ACK loss): the two
  `RecordUncertainAsync(...)` calls in `ResumeAsync` now pass `CancellationToken.None` instead of the
  caller's (possibly cancelled) token, so the retained outcome/run JSON is durably recorded on the
  retained claim before returning the `Uncertain` result — the original fault is never replaced by a
  lost recording.

### 2. `shared/src/Haven.Application/Automations/AutomationRunContinuationCoordinator.cs`
- Blocker 5: all settlement recordings (`ResumeCancelled`, `ResumeFault`, malformed resumer, and the
  persistence-fault path inside `PersistUnderClaimAsync`) now record through an INDEPENDENT
  `CancellationToken.None` rather than the caller token. This preserves the exact prepared outcome and
  original cause even when the caller cancelled during held persistence. The original cancellation is
  still rethrown after the durable receipt.

### 3. `shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.RunClaims.cs`
- Blocker 6 (eligibility): added a due-time fence inside atomically `TryAcquireRunClaimAsync` —
  a Run whose `scheduled_for > now` is refused (`claim.not.due`) before any claim is inserted, so the
  public direct `TryAcquireForRunAsync` cannot bypass the candidate query and claim a far-future
  Wait/Approval. This is additive; existing due-candidate flows are unaffected.

### 4. `Browse/Host.Tests/AutomationPass8UncertainSettlementAndPinTests.cs`
- Blocker 1 (compile) + Test 5 historical-pin rewrite:
  - `GraphFixture` has only `Graph` + GuidIds; the test previously referenced non-existent
    `fx.Trigger/ActionA/Wait` node objects. It now resolves actual nodes from `fx.Graph.Nodes`
    by stable IDs (`Single(n => n.Id == fx.TriggerId)` etc.).
  - Preserves the SAME B NodeID (`fx.ActionBId`) but makes the new Active B materially changed
    (unsupported action kind `transmit-to-mars`) so the REAL `BuiltInAutomationActionNodeExecutor`
    would fail it. The pinned old snapshot B (emit "b") is what actually runs.
  - Asserts the OLD effect/output after the new revision — not just the DefinitionRevision label:
    `Status == Succeeded` AND `Result` contains `graphSucceeded":true` (the pinned old B ran),
    plus `DefinitionRevision == 4` (never the current revision 5).
  - Changed `fx.Trigger.Id` → `fx.TriggerId` and `fx.Wait.Id` → `fx.WaitId` in test 7.

## Not modified / preserved
- All prior pass6–8 work, the 49-pass checkpoint, and the existing 7 facts are preserved.
- No rewrite of NodeGraph lane source/tests; no new parallel envelope; no registration/integration
  invention in this lane (fresh/resume wiring through Home-owned registry remains for the owning
  lane per the joint-graph instruction).

## Remaining (for coordinator/owning lane)
- Blocker 2: fresh `ExecuteAsync` path and production registration of the adapter in the shared
  NodeGraphRuntimeRegistry are not claimed here (no .NET slot; registration owned elsewhere).
- Blocker 3/4 (owner-admission derived from owner records; unified external budget) are larger
  lifecycle refactors requiring the versioned-graph repository and budget plumbing; not changed here.
- Static review only — compile validation should be run by the coordinator when the .NET slot is
  available.
