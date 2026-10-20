# Lane 4 handoff — Pass 7 (implement outstanding compiler corrections; source/tests/static only)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`.
Frozen gate: `a372227dfdd3864ca99f99139bb40d8452c6a36a` (parent `53ae17bcdce306e35c82206c2dbe5b2db18cad35`) —
status `PREPARED_NOT_BUILT_NOT_TESTED`; NO `root-*` logs in the a372227d gate (NOT RUN, not PASS).
Preserved `/workspace/shared/9to1-test-evidence/parallel16-haven-pass6-frozen`? — not present; all earlier logs
and gates preserved. Pass: `parallel16-lane4-haven-pass7.prompt.md`.

## Authentic root diagnostics addressed (read, not fabricated)
- `/workspace/shared/9to1-test-evidence/lane4-private-projection-gate-53ae17bc/root-build.log` (SHA
  `c4e3a311…`): 11 xUnit1051 errors — `AssistantRailIconProjectionTests.cs` lines 37,52,63,76,85,95,105 and
  `HavenAppHeaderBindingsTests.cs` lines 68,69,70,80; 0 warnings; build FAILED (tests correctly NOT run).
- `/workspace/shared/9to1-test-evidence/lane4-pass6-feedback-xunit1051.md`: confirms xunit.v3 3.2.2,
  warnings-as-errors, must pass `TestContext.Current.CancellationToken` through the real method argument; no
  NoWarn/analyzer-disable/`CancellationToken.None`/omitted tests/weaker lifecycle.
- New gate `lane4-private-projection-gate-a372227d`: links ALL22 authored Facts +1 origin guard =23, real frozen
  `CakeOS.Cui.Runtime.dll` (SHA `6322dae4…`) added for `ICuiWritableBindingContext`; README says caller-token
  omissions were NOT fixed. No `root-*` logs present yet.

## Corrections implemented

### 1. Real cancellation-token corrections (tests) — xUnit1051
- `AssistantRailIconProjectionTests.cs`: all 12 plain `ResolveAsync()` calls now pass
  `TestContext.Current.CancellationToken`.
- `HavenAppHeaderBindingsTests.cs`: all 4 `DispatchAsync(...)` calls now pass `TestContext.Current.CancellationToken`
  as the third argument.
- The two intentional independent caller-cancellation tests (`Catalogue_cancellation…`, `Running_cancellation…`)
  now use `CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken)` (the repo's
  established linked-token pattern, cf. `ChatInitialTaskObservationTests`), so they stay linked to the real test
  lifetime while preserving their held original/causal assertions (the source observes the cancelled token and
  throws `OperationCanceledException`, which the projection returns as `Cancelled`).

### 2. Three definite full-host CS0176 fixes (`App.UnifiedWorkspaceHost.SharedControls.cs`)
`ActorAdmissionCode` is `internal static` on the containing type; the notifications bindings invoked it through
the instance reference `_host.ActorAdmissionCode(...)` at former lines 735/809/831 (CS0176). Fixed by qualifying
the containing static type: `UnifiedWorkspaceHost.ActorAdmissionCode(...)`. No instance shim/duplicate overload;
actor semantics unchanged. (This source is EXCLUDED from the tiny projection gate; it requires a real owning
Desktop/host compile — a projection PASS cannot claim it compiled.)
- Note: `HomeModelPickerDispatcher` (line 602) already uses the unqualified simple-name
  `ActorAdmissionCode(admission)`, which resolves correctly to the static member.

### 3. Writable contract + registration verified
- The gate `a372227d` already links both complete test files AND the authored resource `HavenAppHeader.cui`
  (as `EmbeddedResource`, LogicalName `Haven.Desktop.Home.HavenAppHeader.cui`) plus the real pinned
  `CakeOS.Cui.Runtime.dll` — no missing registration inside the gate.
- Real repo `Haven.Desktop.Tests.csproj` already includes both `AssistantRailIconProjectionTests.cs` (line 130)
  and `HavenAppHeaderBindingsTests.cs` (line 131).
- Missing real-repo registration: `HavenAppHeader.cui` is not embedded in `Haven.Desktop.csproj` (only
  `HavenNotifications.cui`/`HavenSharedPanelShell.cui` are). Exact root-preserving registration hunk is appended
  to `work/lane4-pass7-integration.patch` (adds
  `<EmbeddedResource Include="Views/Pages/Home/HavenAppHeader.cui" LogicalName="Haven.Desktop.Home.HavenAppHeader.cui" />`).
- Authors tests that directly call `TrySetValue` are NOT proof the authored input loaded; the real
  loader/writeback regression remains a genuine dependency (compiled CUI transport CUIR070 + CUIB001 legacy
  `.hui` migration still block it) — retained, not waived.

### 4. Notification current-actor admission fence — recheck after admission awaits, before acquisition
`App.UnifiedWorkspaceHost.SharedControls.cs` (`HavenNotificationsCuiBindings`):
- `LoadCoreCoreAsync`: after `ReobserveActorAsync` returns Current, a NEW exact `IsCurrent()` recheck runs
  immediately before `_service.GetRecentAsync`; and publication now revalidates the ACTUAL actor
  (`if (!IsCurrent() || await ReobserveActorAsync(token) != ActorAdmissionStatus.Current) return;`) immediately
  before the UI-thread publication (never merely the cached profile).
- `MarkReadCoreAsync`/`DismissCoreAsync`: after the admission await and `IsMemberRow`, a final
  `ReobserveActorAsync` + `IsCurrent()` recheck runs immediately BEFORE the service mutation, publishing a
  structured refusal (`StalePanel`/`ActorChanged`/`Cancelled`) instead of allowing A's stale mutation when a
  replacement B mounted during the await.
- Preserved: original reservation (`RetainBeforeOwner`), mutation outcomes, post-commit revalidation (committed
  results kept if retirement follows commit), and the model-picker dispatcher's post-admission current check.
- Hidden exact-root-before-dispose and writable search repairs unchanged.

### 5. Read-cancellation guard (projection)
`AssistantRailIconProjection.cs`: new `WithCancellationCheck(token)` returns a structured `Cancelled` result when
the caller token is cancelled; it guards the publication of every successful result (NoneConfigured, Running,
MostUsed, UsageNotRecorded). A source that IGNORES cancellation and returns normally can no longer publish
Available/None/UsageNotRecorded. Genuine returned source faults and authority distinctions (unavailable/denied/
stale) are preserved.
- New tests: `Ignored_cancellation_with_none_configured_is_cancelled_not_none`,
  `Ignored_cancellation_with_running_selection_is_cancelled_not_available`,
  `Ignored_cancellation_with_no_usage_is_cancelled_not_usage_not_recorded` — all use a linked
  `CreateLinkedTokenSource(TestContext.Current.CancellationToken)` and a source that returns normally (ignores
  cancellation), asserting the projection returns `Cancelled`/`Generic`, never a success state.

## Files changed (pass 7) — SHA-256 (post-edit)
| path | SHA256 |
|---|---|
| `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` | `0dc60f4a2bc80dbcf8d8981e1667eb816e77cbf08e9932457b8d68974ca6c3bc` |
| `Haven.Desktop/HavenAppHeaderBindings.cs` | `71872f539ec77921d2d4cdbb3fef6a53bb552b68b2048c50c389d83448fdafac` (unchanged, pass6) |
| `Haven.Desktop/AssistantRailIconProjection.cs` | `d5ffe7195e1d379bdc3dd4109f5596a3bddc7af6f3a74a146753ce6e5e4a113a` |
| `Haven.Desktop.Tests/AssistantRailIconProjectionTests.cs` | `341f37f4f1e5352257cd9fa9d3eaaadec42bd5e9c843125ef0472aa7fa0fa453` |
| `Haven.Desktop.Tests/HavenAppHeaderBindingsTests.cs` | `b39e3f175db5b88a7bedfbf3e8af3dad0fd38a372237d19a0f52a7bddf118acb` |
| `work/lane4-pass7-integration.patch` | (new; actual applicable unified diff: 4 source/test files + csproj registration hunk) |

`App.UnifiedWorkspaceHost.cs` and `App.OriginalWindowsHome.cs` remain clean vs HEAD (verified).

## Tests: total facts = 22 authored (unchanged count) + 3 new ignored-cancellation facts = 25 in the four-source scope; 1 origin guard.
- `AssistantRailIconProjectionTests.cs`: original 7 + pass6 8 + pass7 3 = 18 facts.
- `HavenAppHeaderBindingsTests.cs`: 5 + pass6 4 = 9 facts.
- All NOT RUN (no root SDK grant; gate still PREPARED_NOT_BUILT_NOT_TESTED).

## NOT-RUN commands (when root grants the serialized slot)
- Root gate: `python3 run-gate.py {restore,build,test} --dotnet DOTNET` (a372227d → NEW freeze).
- `dotnet test ... --filter "FullyQualifiedName~AssistantRailIconProjectionTests|FullyQualifiedName~HavenAppHeaderBindingsTests"`.
- Real owning Desktop/host compile (CS0176 scope): `dotnet build` of `Haven.Desktop` after root wiring.

## Truthful residuals (NOT closed by this lane)
- **Host-level `held actor→replace/revoke→release` tests** for `HavenNotificationsCuiBindings`/
  `HomeModelPickerDispatcher` (zero stale read/mutation/publication) are NOT authored: those adapters are private
  to `UnifiedWorkspaceHost`, which requires the full Avalonia host composition that is excluded from the gate and
  owned by root/lane5 wiring. Authoring them truthfully against actual production adapters needs the real host
  slot; a reflection/fake-composition test would violate "no test-only production override". Source fence fixes
  are implemented; the regression tests remain a bounded host-slot item.
- **Compiled CUI transport (CUIR070) + CUIB001 legacy `.hui` migration** still block the real loader/writeback
  regression (HavenAppHeader.cui TwoWay load). Retained as an honest dependency; the typed
  `ICuiWritableBindingContext` contract is implemented and gate-linked.
- **Assistants running/usage storage owners** still not composed → host projection returns typed
  Unavailable/UsageNotRecorded/Cancelled, never invented most-used.

## For the coordinator
1. Apply `work/lane4-pass7-integration.patch` (verified: applies cleanly on the pre-pass7 source and reproduces
   this working tree byte-for-byte for the 4 changed files; includes the exact Haven.Desktop.csproj registration
   hunk for `HavenAppHeader.cui`).
2. Freeze a NEW source gate (supersedes a372227d) and run restore/build/test; require a real owning Desktop/host
   compile for the SharedControls.cs CS0176 scope.
3. Accept: zero xUnit1051; TestContext tokens threaded; CS0176 gone; notification fence rejects stale A read/
   mutation/publication after A→B swap; ignored-cancellation sources never publish success.
