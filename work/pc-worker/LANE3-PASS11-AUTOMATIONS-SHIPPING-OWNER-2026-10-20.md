# LANE3 PASS11 — Automations11 SHIPPING OWNER (source/tests; NOTRUN)

Author: Mercury lane 3, same session ses_ea48b6fd6ffeA3ud4UjsyFldp2.
Scope: work/pc-worker LANE3-AUTOMATIONS11-SHIPPING-OWNER.
Owner: serialized actual owning checks (compile + Browse/Host.Tests). Tests in this return are NOT RUN.

## Reviewer findings addressed
1. Real shipping composition + caller admission (Home currentness before admission and before every protected effect).
2. Fresh identity/revision + SAME budget threading.
3. Uncertain-settlement recording bound to the exact held claim; reconcile only the exact prepared operation.

## Changed paths (source/tests only; no SDK/build/app/commit)
- shared/src/Haven.Application/Automations/AutomationRunContinuationContracts.cs
  - interface IAutomationRunClaimStore += ValidatePreparedOperationAgainstClaimAsync(...)
  - new AutomationRunAuthorityContext / AutomationRunAuthorityDecision / IAutomationRunAuthority
- shared/src/Haven.Application/Automations/AutomationNodeGraphDurableAdapter.cs
  - authority seam: optional IAutomationRunAuthority on ctor/Create (fail-closed when wired+denied; null preserves prior behavior so pass8 test#7 stays green)
  - admission gates in AdmitAsync + AdmitResumeAsync; effect gates in ExecuteAsync + ResumeAsync (before claim/effect)
  - fresh: resolve canonical definition (ResolveOrCreateDefinitionAsync) + require current Active revision for fresh; old pinned Run resume still allowed
  - budget pass-through (GraphExecutionBudget) to RunOneAsync / ResumeScheduledGraphAsync
- shared/src/Haven.Application/Automations/ScheduledTaskRuntime.cs — budget threading (RunOneAsync/ResumeScheduledGraphAsync/durable capture) + using Haven.Application.NodeGraph
- shared/src/Haven.Application/Automations/ReusableDeviceWorkflowRunner.cs — RunDurableAsync(int? maximumSteps) -> AutomationGraphRunner.ResumeAsync(maximumSteps)
- shared/src/Haven.Application/Automations/AutomationRunContinuationCoordinator.cs — reconcile validates prepared op against held claim before persisting
- shared/src/Haven.Infrastructure/Persistence/SQLite/AutomationRepository.RunClaims.cs
  - RecordUncertainSettlementAsync binds prepared Run identity/automation/revision/pin to held claim row
  - ValidatePreparedOperationAgainstClaimAsync impl; ReadClaimRowAsync/HeldClaimRow
- shared/src/Haven.Infrastructure/Automations/HomeAutomationRunAuthority.cs (NEW) — real Home-backed IAutomationRunAuthority (profile current actor + broker authorize/begin/claim/complete, consume-once)
- shared/src/Haven.Infrastructure/Automations/HomeAutomationRunResourceResolver.cs (NEW) — bounded automation.run/effect resolver + trusted action policy source (no allow-all; distinct from definition write)
- shared/src/Haven.Infrastructure/Automations/AutomationShippingComposition.cs (NEW) — AddAutomationShipping: real registration seam
- shared/src/Haven.Infrastructure/DI/ServiceCollectionExtensions.cs — AddAutomationNodeGraphDurableAdapter accepts optional authority
- shared/src/Haven.Infrastructure/DI/WindowsHomeDomainServiceCollectionExtensions.cs — AddHavenAutomationRunAuthority + AutomationRunResourceResolvers()/AutomationRunActionPolicies()
- Browse/Host.Tests/Automation11ShippingOwnerTests.cs (NEW) — registered in Host.Tests csproj
- Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj — registered the new test file

## Genuine production registration / caller chain
Shipping host MUST, in real composition (guarded hunk pending):
1. Build HomeNativeWindowsComposition WITH the bounded run resolver + action policy:
   originalResourceResolvers: .Concat(AutomationRunResourceResolvers())
   originalActionPolicies:    .Concat(AutomationRunActionPolicies())
2. After AddHavenOwnedWindowsHomeDomain: services.AddAutomationShipping(home, compile);
   -> AddHavenAutomationRunAuthority(home) [+ AddAutomationNodeGraphDurableAdapter(compile, authority resolves from DI)]
3. Adapter is consumed by the shared NodeGraphRuntimeRegistry. Fresh activations route AdmitAsync->ExecuteAsync; resumes route AdmitAsync->AdmitResumeAsync->ResumeAsync. Home gitness checked at admission and again before each effect.

## Guarded integration hunks (exact main preimages; NOT applied to avoid overwriting App/UI work)
### Hunk 1 — App.OriginalWindowsHome.cs ConfigureOriginalWindowsHomeRegistrations (Home composition construction)
Preimage (lines ~44-48):
  _actualWindowsHome = new(new FileHomeCoreStateStore(...),
      principal, paths, new("9to1.home.desktop.v1"),
      protectedInstalledPeerVerifier: rootClient,
      originalResourceResolvers: OriginalWindowsDeveloperResolvers().Concat(...),
      originalActionPolicies: OriginalWindowsDeveloperPolicies().Concat(...),
Insert into originalResourceResolvers chain: .Concat(global::Haven.Infrastructure.Automations.HomeAutomationRunResourceResolver resource) -> use .Concat(WindowsHomeDomainServiceCollectionExtensions.AutomationRunResourceResolvers())
Insert into originalActionPolicies chain: .Concat(WindowsHomeDomainServiceCollectionExtensions.AutomationRunActionPolicies())

### Hunk 2 — App.axaml.cs OnFrameworkInitializationCompleted (registration)
After the AddHavenOwnedWindowsHomeDomain call and collaborators are registered, in the non-ANDROID Windows path:
  if (_actualWindowsHome is { } home)
      collection.AddAutomationShipping(home, provider => provider.GetRequiredService<NodeGraphAutomationAdapter>());
(Or pass the compile instance directly; the host must have the NodeGraphAutomationAdapter/bindings and the repository/store/runner collaborators registered.)

## Remaining truthful blockers
- Full real-Home positive receipt path requires driving the genuine PendingApproval decision (no auto-approval); the authority fail-closes until approved. The Home approval UI surface remains open.
- No .NET compile/build slot in this lane → NOTRUN; root must compile and run Browse/Host.Tests.
- App/UI/project/header/Voice/Build files intentionally untouched; hunks above must be applied by the owner/root.

## Evidence
- Source identity: main contracts 594a4b622c4615265df6832e21a18d16d3704141 (read-only). Graph base 8ba7f3ebb37d4f39b4d9b89412b5f20cbc8525eb.
- Prior frozen pass9 18987fc7f99eb16a5fa80b39d2cb1e21b8ff8f7d (49 PASS) preserved; reconnect 4982a93b...; no reset/clean/stash.
