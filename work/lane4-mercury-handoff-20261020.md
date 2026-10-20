# Lane 4 handoff — Pass 3 (shared-control host/lifetime correction, source-only)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`.
Pass: `parallel6-lane4-haven-pass3.prompt.md`; pass2 review
`parallel6-lane4-pass2-peer-review-20261020.md`; pass1 frozen review retained.

## Identity / constraints honored
- Source authority: `docs/specification/9to1-development-specification-v2.md` (SHA
  `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`); owner references read:
  single-app-implementation, haven-shared-header, spaces-svg-icons, professional-app-accents.
- **No edits to `App.UnifiedWorkspaceHost.cs`** (lane5-owned; verified clean vs HEAD — `git diff HEAD -- <file>` is empty). All lane4 behavior lives in `App.UnifiedWorkspaceHost.SharedControls.cs` partials; the exact-base integration patch documents the hooks root/lane5 applies.
- NO .NET/MSBuild/restore/tests/app/display/native/network/model/provider/credential/config/cap changes; no commit/push/deploy; no subagents; TEXT ONLY; execution NOT RUN.

## What pass 3 fixed (review items 1, 3–7, 9, 11, 12 + partial 10)
1. **Async typed gateway (item 1)**: `IHavenSharedControlGateway` returns `Task<HavenSharedOpenResult>`; Browse rail dispatch reserves admission before invoking the gateway and returns the real settlement (`RunSharedOpenAsync`), so close joins genuine show/init/fault/refusal. No `CompletedTask`/async-void/trace-and-success.
2. **Admission before external callbacks (item 2)**: `AdmitOriginal<T>` reserves a slot in the host original-task ledger via the start-barrier (`RunObservation` pattern) BEFORE invoking any external owner/callback; profile open, notifications load, model open/refresh and commands retain original identity/failure.
3. **Retired-panel fencing (item 3)**: generation+profile admission (`IsPanelCurrent`) precedes every owning operation and every service mutation; notification `MarkRead/Dismiss/ClosePanel` and the model dispatcher revalidate BEFORE service call and BEFORE publication; no dead `_closed/_startupRetirement`-only check.
4. **Publication fence (item 5)**: `HomeModelPickerCuiController.PublicationGate` seam — revalidated immediately before surface mutation; standalone callers (gate null) publish unconditionally.
5. **Retirement custody (item 6)**: `ReserveRetirementCustody` disposes/retires the *previous* loader first, retains its settlement (incl. `Dispose` throws) and only then mounts the replacement; a late callback from A cannot retire B.
6. **Close joins post-Dispose settlement (item 7)**: `JoinSharedPanelSettlementAsync` awaits the mounted panel's `WhenActionsIdleAsync()` + disposal, disposes composed original-session leases and the authored shell loader before borrowed owners are released.
7. **CUI-owned shell (item 8)**: `HavenSharedPanelShell.cui` authors structure/backdrop/spacing/visibility; host only mounts the active panel into the named host element and flips authored `HasOpenPanel`. No app-authored Grid/Border/IsVisible chrome policy remains.
8. **Context-before-load (item 9)**: un/action contexts set before `TryLoad`; genuine Home theme preference context used.
9. **Compiled transport consumer (item 10, partial)**: `CompiledCuiDocument` consumes the packaged `CUI.Compilation.v1` bundle with hard format/ABI/registry/capability/payload admission; no raw fallback. Legacy `HavenNotificationsCuiDocument`/shell route through it. **Model-picker `LoadDefault` stays stateless V1** (PATCH C documents lane2-owned V2 transport as BLOCKED — no hidden raw fallback, no false completion claim).
10. **Real Home provider composition (item 11)**: `OpenModelPickerPanelAsync` composes the REAL `HomeModelPickerFeatureProvider` + `HomeVersionedModelRouteRepository` + `HomeModelPickerOriginalSessionProvider` from the SAME Home owners (profile/state/broker/authorization/provider-registry/privacy); canonical User scope; no fixture catalogue.
11. **Named action parameters (item 12)**: `HomeModelPickerScopeSelection/RouteSelection/CandidateSelection/CandidateToggleSelection` routed through canonical `<Action parameter=...>` declarations; malformed/missing parameters refuse structurally.

## Files changed / created (pass 3)
- `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` — rewritten (async gateway, AdmitOriginal, retirement custody, publication-fenced model picker, notification bindings, compiled consumer, `JoinSharedPanelSettlementAsync`).
- `Haven.Desktop/Views/Pages/Home/HavenSharedPanelShell.cui` — new authored shell (+embedded resource).
- `Haven.Desktop/Views/Pages/Home/HavenNotifications.cui` — authored panel (pass2, retained).
- `Haven.Desktop/Haven.Desktop.csproj` — registers the two shell/notification `.cui` embedded resources.
- `Browse/NativeUI/BrowseNativeWorkspace.cs` (+`BrowseNativeWorkspace.SharedControls.cs`) — async shared dispatch, host context availability, shared action fencing.
- `Browse/NativeUI/UI/BrowseWorkspace.cui` — persistent embedded Haven rail (model/notifications/profile), embedded-only visibility via `Haven.Embedded`.
- `Home/Source/Home/ModelPicker/HomeModelPickerCuiScene.cs` — `PublicationGate` seam + `HasSnapshot` on surface.
- `Home/Source/Home/ModelPicker/HomeModelPickerCuiProjection.cs` — typed selection records + bindings.
- `Home/UI/ModelPicker.cui` — canonical `<Action>` named parameters.
- Tests: `Browse/NativeUI/Tests/BrowseSharedControlsTests.cs` (async fakes + causal cases), `Home/Tests/HomeModelPickerCuiControllerTests.cs` (PublicationGate + structured refusal), `Home/Tests/HomeModelPickerCuiProjectionTests.cs` (named-parameter test).
- `work/lane4-pass3-integration-patch-20261020.md` — PATCH A (lane5 create/close hooks), PATCH B (compiled-bundle registration, BLOCKED), PATCH C (model-picker V2 transport, BLOCKED).

## Exact source hashes (pass 3, text-only)
| path | SHA256 |
|---|---|
| `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` | `204e7ec87deef14573f65b885c27e4beb772437ea41946a8314f556f28116bde` |
| `Haven.Desktop/App.UnifiedWorkspaceHost.cs` (clean, == HEAD) | `2c32a7c41d50a274f28e44917938ec9c32cc9dbab6ca22a2bdb1d719956c0c10` |
| `Browse/NativeUI/BrowseNativeWorkspace.cs` | `ed4b625146d0f29076d176e6ae5f40b3b6812c0f1d2d13761cc7c5cf0677832f` |
| `Browse/NativeUI/BrowseNativeWorkspace.SharedControls.cs` | `35da62294cf9820f16b5f579a01cbe12159c277c7b7814bc96d64bc2c6ded8bd` |
| `Haven.Application/Haven/HavenHostContext.cs` | `27ebde8d381bfe98722bb124bcd195586c9d41518367582ea7d6e5e1313f19d3` |
| `Home/ModelPicker/HomeModelPickerCuiScene.cs` | `3d465125198fcab3258766e4b3812f658716e88e519214a179870a240f79d003` |
| `Home/ModelPicker/HomeModelPickerCuiProjection.cs` | `1c61c9c41ce5505c9c2974b513dc61947ec6dde9c9a2bd96e2c05cc8f78f8d56` |
| `Home/Tests/HomeModelPickerCuiControllerTests.cs` | `cfb371258f972f404ba90abbf528dc9bf4404002cf5e207d9e4c405786b26ffd` |
| `Browse/NativeUI/Tests/BrowseSharedControlsTests.cs` | `9b542d06a0b1cedd772179ee6ea6e8935b4b752d1c80e63c8d4eb95604d14f11` |

## Causal tests authored (NOT RUN)
- `BrowseNativeUiTests` (NativeUI.Tests): `No_host_context_disables_shared_rail_and_refuses_actions`, `Shared_context_routes_each_rail_action_to_the_typed_gateway`, `Partial_support_refuses_only_the_unsupported_rail_actions`, `Refusing_gateway_never_fabricates_an_open`, `Retired_workspace_refuses_shared_rail_actions`, `Shared_gateway_returns_unsettled_original_until_init_settles_and_close_joins_it`, `Shared_gateway_fault_is_retained_and_never_fabricated_as_success`.
- `HomeModelPickerCuiControllerTests` (Home.Tests): `Publication_gate_suppresses_publication_when_the_panel_is_retired`, `Publication_gate_still_publishes_when_the_panel_remains_current`, `Malformed_route_selection_refuses_structurally_without_mutating_the_surface`, `Candidate_toggle_requires_enabled_state_and_refuses_otherwise`.
- `HomeModelPickerCuiProjectionTests` (Home.Tests): `Canonical_named_action_parameters_resolve_to_typed_selection_records`.

## Exact NOT-RUN commands (when root grants the serialized slot)
- NativeUI.Tests:
  `dotnet test "9to1 Workspace/Browse/NativeUI/Tests/HavenOS.Apps.Browse.NativeUI.Tests.csproj" --filter "FullyQualifiedName~BrowseNativeUiTests"`.
- Home.Tests:
  `dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" --filter "FullyQualifiedName~HomeModelPickerCuiControllerTests|FullyQualifiedName~HomeModelPickerCuiProjectionTests"`.
- Host composition/panels: verified via Host.Tests headless composition once the slot is granted (root-owned); the lane5 host hook is a patch, so host-close settlement is exercised after PATCH A is applied.

## Truthful residuals (NOT closed by this lane)
- **Compiled CUI bundle (PATCH B)**: Haven.Desktop does not yet emit `CUI.Compilation.v1`; until root/lane2 applies the registration, notifications/shell truthfully refuse (`CUIR069`) and the shell mounts a scene-only frame. No raw reparse fallback.
- **Model-picker V2 transport (PATCH C)**: root/lane2 owns the compiled transport alignment; standalone/tests keep genuine stateless V1.
- **Assistants button projection (running → most-used → generic)**: no real run/usage read source wired; **not implemented, not fabricated** — explicitly blocked.
- **Cross-app host-aware topbar (item 13)**: Browse rail wired; standalone/embedded header unification across other apps is unverified and spans other lanes' surfaces — not claimed complete.
- **Runtime rendering/theme/viewport** of the authored shell/rail/SVG: NOT RUN.

## For the coordinator
1. Apply PATCH A (lane5 create/close hooks) after lane5 freezes; reconcile PATCH B/C with lane2.
2. Compile (guarded dotnet, `-m:1`): Browse NativeUI, Home, Haven.Desktop.
3. Run the test filters above; confirm nonzero executed counts.
4. Independent acceptance: embedded rail opens Profile/Notifications and resolves the model picker; panels load under genuine Home theme; close joins all retained originals; retired-panel callbacks never mutate the replacement.
