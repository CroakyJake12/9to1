# Lane 5 — Dulche pass-2 implementation handoff (Mercury)

Date: Tue Oct 20 2026. Lane 5 (no .NET slot). All source/test edits are **uncompiled / NOT RUN**.
No .NET build/restore/app/test ran. No commit/push made.

## Pinned authority reread (this session)
- `AGENTS.md`: pinned single-app implementation requirement; pass 1 frozen checkpoint at
  `/workspace/shared/9to1-test-evidence/parallel6-dulche-pass1-frozen` (base `fb70840d`,
  checkpoint `dd9b6d1f80d9e4e17ac89b80613a2eb1341d8f79`).
- Canonical spec sections reread: Home Core/Dulche/runtime control, model resolution, Dulche
  framework, multi-engine, welcome/discovery (from prior pass).
- Owner references: chat-composer-dulche, single-app-implementation.
  No new owner correction supersedes the pass-1 scope.

## Scope (finding set)
Fix pass-1 review findings on the Home-owned local Dulche runtime:
1. `HomeDulcheRuntimeContracts.cs` — strict validity + genuinely observed axes (done pass-1, kept).
2. `HomeDulcheRuntimeService.cs` — real lifecycle (one-start coalescing, stale guard, drain).
3. Recovery route — real CUI "Manage local models" action in the real composition
   (Spaces→OverlayChatSessionFactory→NewChatPage), not only tests/old MainView.
4. Overlay test rig — distinct raw vs routed model-client identities.

## Changes made this session
### Home Core (source only, not compiled)
- `HomeNativeWindowsComposition.cs`: replaced parameterless `new HomeDulcheRuntimeObservationSource()`
  with a real platform-adaptive source. Linux composes `HomeDulcheLinuxRuntimePackageSource` +
  `HomeDulcheLinuxBrokerAdapter` + `HomeDulchePackagedModelEvidenceSource`; every other platform uses
  explicit unsupported adapters (never Ready).
- `HomeDulcheModelEvidenceSources.cs`: fixed `BlobPath` to use the configured `_modelRoot` (was
  `DefaultModelRoot()`), and made `VerifyBlobAsync` an instance method (it calls the instance
  `BlobPath`). Manifest blob path must be canonical and must not escape the store.

### Recovery routing (shared desktop, source only, not compiled)
- `ChatHavenScene.cs`: added a genuine `ManageLocalModels` button to the scene footer markup, resolved
  the element, exposed the `HavenButton` property + `ManageLocalModelsRequested` event, and added
  `SetManageLocalModelsVisible(bool)`.
- `NewChatPage.cs`: wired `_scene.ManageLocalModelsRequested` → `RequestManageLocalModels()`; toggled
  the button visibility in `PublishOriginalModelGateVisual` exactly while model availability is
  `Unavailable`. Preserves draft/selection/conversation.
- `OverlayChatSessionFactory.cs`: added typed host seam `Action<NewChatPage>? WireHost`, applied to
  every created page in `CreatePageAsync`.
- `SpacesBrowseAppContentSession.cs`: wire the seam in both factory creation sites
  (`NewChatSessionFactory()`, `ExecuteChatOnUiAsync`) to route `ManageLocalModelsRequested` through
  `_hostQueueNavigation` to the canonical Home surface (`9to1://home`) in a new tab so the current
  conversation/draft/focus are preserved. If the host cannot navigate, no fabricated navigation occurs.
- `OverlayWorkspaceController.cs`: wires the seam to surface honest guidance (the floating Overlay
  cannot open the shell's Home/Discover tab) instead of fabricating navigation.
- `MainView.axaml.cs` existing recovery wiring (old path) retained.

### Tests (authored; NOT RUN)
- `HomeDulcheRuntimeServiceTests.cs`: **rewritten** to the new observation-source/service contracts.
  Covers: hash validation; Ready only when every axis is positive; degraded when capability probe missing
  / endpoint healthy but not generation-capable / broker not observed / model missing / package
  unverified / platform unsupported; packaged model evidence real re-hash (verified, tamper, non-hex,
  path escape); service Ready publish; structured degraded throw; one-start coalescing; stale completion
  discarded on stop; attach/Original-work drain on stop.
- `OverlayChatInitializationTests.cs`: **rig rewritten** to register DISTINCT raw (`IOllamaClient`) and
  routed (`IProviderModelClient`) identities; added `Raw` + `RawCallCount`; strengthened the routed-bind
  test to assert the raw transport is never touched (`RawCallCount == 0`).
- `HomeDulcheRuntimeServiceTests` removed stale dependence on `LocalModelRegistry`/old ctor.

## Evidence
Source hashes (git hash-object, 12 chars):
- HomeDulcheRuntimeContracts.cs `33293a9a8100`
- HomeDulcheRuntimeService.cs `1006babee51a`
- HomeDulcheRuntimeObservationSource.cs `359324a0d04d`
- HomeDulcheRuntimePackageSource.cs `7cfc5b7cdefe`
- HomeDulcheBrokerAdapters.cs `a348b7171285`
- HomeDulcheModelEvidenceSources.cs `d4be78ba045d`
- HomeNativeWindowsComposition.cs `98400d8846c5`
- Home/Tests/HomeDulcheRuntimeServiceTests.cs `7ff94f97bbbc`
- shared NewChatPage.cs `125994dc15cd`
- shared ChatHavenScene.cs `8a45437bbb1b`
- shared OverlayChatSessionFactory.cs `da23524e27c5`
- shared OverlayWorkspaceController.cs `6c8354fd5963`
- shared SpacesBrowseAppContentSession.cs `b8215b5be86e`
- Browse/Host.Tests/OverlayChatInitializationTests.cs `de49e701887c`

## Not run (no .NET slot)
- Any `dotnet` build/restore/test for the above. All tests remain **NOT RUN**.
- Exact commands to run when a serialized slot is granted (lane 5 has none):
  - `dotnet build 9to1 Workspace/Home/Source/Home/Home.csproj -m:1`
  - `dotnet test 9to1 Workspace/Home/Tests/Home.Tests.csproj -m:1 --filter HomeDulcheRuntimeServiceTests`
  - `dotnet build 9to1 Workspace/Browse/Host.Tests/Host.Tests.csproj -m:1`
  - `dotnet test 9to1 Workspace/Browse/Host.Tests/Host.Tests.csproj -m:1 --filter OverlayChatInitializationTests`

## Residual blockers / unresolved decisions
- No production `llama-server` SHA-256 pin exists and no real model manifest/blob is supplied, so
  `RuntimeVerified`/`ModelVerified` stay truthfully blocked in production (`Ready` unreachable until
  real artifact metadata is provided). Not fabricated.
- Full shell "Home/Discover" tab navigation from the floating Overlay window is owned by the shared
  shell chrome (lane 4); this lane delivered the typed seam + host wiring and does not rewrite lane-4
  chrome. Spaces routes through its existing host navigation seam; Overlay surfaces truthful guidance.
- `HomeDulcheRuntimeService` deliberately throws on a degraded observation; `HomeCoreRuntime.StartAsync`
  catches per-service exceptions and reports the service Degraded/Unavailable without blocking unrelated
  services (preserved).
