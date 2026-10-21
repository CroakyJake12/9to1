# LANE 6 — PASS 16 — Automations12 REAL-HOME OWNER registry/authority/resolver integration

- Date: Wed 2026-10-21
- Source identity: HEAD `fb70840d561286ecd46bd62b3c0736bf7d24108b` (snapshot ref `59ef5d322e80c1c369320eb0b637a6613824611b`). All changes are **uncommitted working-tree edits**; no commit/push.
- Scope: Close the Automations12 review findings 1–5 on the shipping/registry/owner path with **real production implementation + owning registered tests** (no prose-only deferral). No .NET build/test slot in this lane → source/tests/static audit only; tests are registered and `NOT RUN` until root executes.
- Self-audit: static compile-correctness verified against real API signatures (broker, authority contracts, schema registry, repository/claim stores, adapter `Create`).

## Findings addressed

1. **Authority (finding 3)** — `Haven.Infrastructure/Automations/HomeAutomationRunAuthority.cs` rewritten:
   - Custody is **per-operation** (actor + automation + revision + session + argument SHA-256 digest), not `AutomationId|revision`; concurrent Runs of the same revision with different sessions/arguments never steal capability.
   - Original Home review retained on retry (no freshly minted broker Guid); effect boundary observes the real decision **once** via `BeginExecutionCapabilityAsync` + `ClaimExecutionObservedAsync` under the SAME current actor/digest.
   - Capability retained until the Home audit completes: `PendingAuditFailures` + `RetryPendingAuditAsync` expose original execution cause AND independent audit failure without rerunning graph effects.
   - Fail-closed on absent/foreign actor, Denied/Blocked, un-acknowledged audit.
2. **Resolver (finding 2)** — `HomeAutomationRunResourceResolver.cs` + `HomeAutomationRunActionPolicySource` rewritten:
   - Canonical repository-backed resolution; owner + current-profile + current-Active-revision checks via deferred `Func<...>` factories (Home composition built before DI repos resolve). Fails closed without canonical catalogue.
   - Only the single `automations.run.effect` action on `automation.run` Execute access; no allow-all, no bool/participant-string authority.
3. **Composition (finding 1)** — `AutomationShippingComposition.AddAutomationShipping` is now a real production caller wired into `App.OriginalWindowsHome.cs` via `App.OriginalAutomationShipping.cs`:
   - Registers `IAutomationRunAuthority`, maintained `IAutomationRunClaimStore`/`IAutomationRunContinuationStore` aliases over `AutomationRepository`, canonical `HomeVersionedNodeGraphRepository`, repository-backed resolver, durable adapter.
   - `AutomationRunResourceResolvers(...)`/`AutomationRunActionPolicies()` wired into the Home composition arrays.
4. **Definition identity (finding 4)** — `ResolveOrCreateDefinitionAsync` reuses the SAME maintained Automation across Active revisions (dedupe on graph+trigger, NOT Active revision), pinning only the Active revision.
5. **Schema/build-blocks** — new `HomeAutomationNodeGraphSchemas.cs` production `NodeGraphSchemaRegistry` + bindings for trigger/action/wait/approval.

## New/owned test lane
- `Browse/Host.Tests/Automation12RealHomeOwnerTests.cs` (registered in `HavenOS.Browse.Host.Tests.csproj`): resolver grant/deny incl. foreign-object + stale-revision + unwired fail-closed; adapter same-Automation reuse across V4/V5; terminal multi-node truthful decided outcome; composition schema/bindings surface. Uses `TestContext.Current.CancellationToken`. `NOT RUN`.

## Blockers / unresolved
- **No .NET build/test slot** — compile was audited statically only; not executed.
- **`BuildStoredAdmission` persisted-original-limit** not changed: the registry `NodeGraphRuntimeRegistry.FinalizeResult` invariant (`binding.AuthorizedStepLimit == continuation.StepLimit`) tightly couples the envelope step-limit; altering without a build slot risks breaking existing durable tests. Flagged for a later build-enabled lane.
- **Real end-to-end Home authority** requires `ResourceAuthorizationService.AuthorizeCoreAsync` with non-empty `AccountId`/`OrganisationId`; local profile actors lack these (existing platform boundary). Production path may not fully function for local profiles; left untouched (root risk).
- Cleanup of `HomeAutomationNodeGraphSchemas.Create()` runs `Create()` three times per `AddAutomationShipping` (compile + graph repo); functionally correct but could be memoized.
