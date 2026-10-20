# Lane 4 handoff — Pass 2 (frozen review corrections)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`
Baseline HEAD: `fb70840d` (first-pass frozen review:
`/workspace/shared/9to1-test-evidence/parallel6-lane4-frozen-review-20261020.md`)

## Identity
- Source authority: `docs/specification/9to1-development-specification-v2.md` (SHA
  `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`), read in full; plus
  shared-header/single-app/SVG/composer owner references listed in the first-pass handoff.
- No .NET/build/restore/app run (no serialized slot — lane 1 only). All tests remain **NOT RUN**.
- No subagents; no credentials/provider/cap/global-config changes; no commits/pushes/deploy.

## What this pass fixed (primary review items 1–6)
1. **Compile errors** (`App.UnifiedWorkspaceHost.SharedControls.cs`): removed `async void`
   `ObserveSharedGateAsync` and the invalid `$ _ =` assignment; removed `.AsTask()` misuse on
   `HomeModelPickerCuiController.ExecuteAsync` (which returns `Task<>`). All async custody now goes
   through the host's existing `Track(original)` (`_sources`), which is joined at close.
2. **CUI-only authoring**: notification markup moved out of the C# string constant into a packaged
   authored `.cui` (`Haven.Desktop/Views/Pages/Home/HavenNotifications.cui`, embedded resource
   `Haven.Desktop.Home.HavenNotifications.cui`). The modal backdrop/dimming is now authored inside
   that CUI document; the C# host keeps only a thin, un-styled placement seam (`Grid` frame +
   `Border` without any hardcoded brush or `IsVisible` policy).
3. **Context-before-load + real Home theme**: `SetThemePreferenceContext(_homeTheme.Context)`,
   `SetBindingContext(...)`, `SetActionDispatcher(...)` are all set **before** `TryLoad`, so the
   authored `Repeat` captures this panel's real notification source and the genuine Home
   `user_preference` theme (no default private copy).
4. **Original-task custody / no forged success**: open/init/refresh/close originals are retained via
   `Track`; `HideSharedPanel`/`ShowSharedPanel` join the replaced panel's `WhenActionsIdleAsync()`
   settlement before discarding; host close joins the mounted panel loader's settlement before
   dispose; MarkRead/Dismiss tasks are tracked in `_sources`; failures stay observable (no
   catch-and-log success).
5. **Model-picker named parameters**: `ModelPicker.cui` migrated from undelivered `action-*`
   metadata to canonical `<Action name=... command=... parameter=...>` declarations. New typed
   projection selections (`HomeModelPickerScopeSelection`, `HomeModelPickerRouteSelection`,
   `HomeModelPickerCandidateSelection`, `HomeModelPickerCandidateToggleSelection`) are routed to
   `HomeModelPickerActionRequest`. No enabled no-op commands remain for these actions.
6. **Test custody**: `BrowseSharedControlsTests.cs` stays only in the owning `NativeUI.Tests`
   project (default glob; not duplicated into `Host.Tests`). Cleanup switched from a non-existent
   `workspace.CloseOriginalAsync()` to `DisposeAsync()`. Added real cases (refusing gateway,
   retired-workspace fencing). Added a Home projection test for the new named action parameters.

## Files changed / created (pass 2)
- `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` — rewritten (fixes 1–5).
- `Haven.Desktop/App.UnifiedWorkspaceHost.cs` — close now joins shared-panel loader settlement.
- `Haven.Desktop/Views/Pages/Home/HavenNotifications.cui` — new authored CUI panel (+embedded resource).
- `Haven.Desktop/Haven.Desktop.csproj` — registers the notification `.cui` embedded resource.
- `Home/UI/ModelPicker.cui` — canonical `<Action>` named parameters.
- `Home/Source/Home/ModelPicker/HomeModelPickerCuiProjection.cs` — selection records + bindings.
- `Home/Tests/HomeModelPickerCuiProjectionTests.cs` — new canonical-parameter test.
- `Browse/NativeUI/Tests/BrowseSharedControlsTests.cs` — fixed + expanded (still in NativeUI.Tests).

## Exact source hashes (pass 2, text-only)
| path | SHA256 |
|---|---|
| `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` | `f3d7e554f124702700f5ee3861e2ed282c5f1465dea819a0aa0ecb03d96b598b` |
| `Haven.Desktop/App.UnifiedWorkspaceHost.cs` | `6ae93fdf73e07524bf7899974bcaf13e5c5dc133c880077a6a434a4d09cf7b5e` |
| `Haven.Desktop/Views/Pages/Home/HavenNotifications.cui` | `18dd0dcdff589c7a118f116a6e2fa0ff3afccb6c36a65dd4c20178334392d515` |
| `Home/UI/ModelPicker.cui` | `0f2f8375e97f64ff0b7164b8fd558b5be4c2422157aeae7bf513f7e86854bcfb` |
| `Home/Source/Home/ModelPicker/HomeModelPickerCuiProjection.cs` | `1c61c9c41ce5505c9c2974b513dc61947ec6dde9c9a2bd96e2c05cc8f78f8d56` |
| `Browse/NativeUI/Tests/BrowseSharedControlsTests.cs` | `ade6233edd70bc68c7dfa89c3e0bec2f995c7a41218a85c84d935dc1fe26a60a` |
| `Home/Tests/HomeModelPickerCuiProjectionTests.cs` | `7cba3043a78529234ce65f2f0408be7b4291ba7b6acda17b48f8bd631f8967bc` |
| `Haven.Desktop/Haven.Desktop.csproj` | `dd452961f4a1754f212d08e26e2398bfd28b2f733661ae55746bbca434fa6004` |

## Causal tests authored (NOT RUN until slot)
- `BrowseNativeUiTests` (NativeUI.Tests):
  `No_host_context_disables_shared_rail_and_refuses_actions`,
  `Shared_context_routes_each_rail_action_to_the_typed_gateway`,
  `Partial_support_refuses_only_the_unsupported_rail_actions`,
  `Refusing_gateway_never_fabricates_an_open`,
  `Retired_workspace_refuses_shared_rail_actions`.
- `HomeModelPickerCuiProjectionTests` (Home.Tests):
  `Canonical_named_action_parameters_resolve_to_typed_selection_records`.

## Exact NOT-RUN commands (when root grants the serialized slot)
- NativeUI.Tests:
  `dotnet test "9to1 Workspace/Browse/NativeUI/Tests/HavenOS.Apps.Browse.NativeUI.Tests.csproj" --filter "FullyQualifiedName~BrowseNativeUiTests"` (expect nonzero executed count).
- Home.Tests:
  `dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" --filter "FullyQualifiedName~HomeModelPickerCuiProjectionTests"`.
- Host integration (host-owned panels): verify via Host.Tests headless composition once the slot is
  granted; the host-internal classes are exercised indirectly through real `HavenNotificationService`
  and `IHomeModelPickerFeatureProvider` seams (see residuals).

## Truthful residual dependencies (NOT closed by this lane)
- **Host-aware topbar across all apps**: this lane wired the embedded Haven rail in Browse only.
  Standalone apps keeping all four controls and embedded app headers being search-only across every
  shared standalone/embedded app remain unverified/missing; they span other lanes' surfaces and are
  **not** claimed complete.
- **Assistants button progression (running → most-used → generic)**: no real run/usage data source
  is established; not implemented, not waivered.
- **Filled rounded SVG rendering**: real single-path/currentColor assets exist; actual rendering,
  cutouts and theme behavior are NOT RUN (shared resolver is another lane's source ownership).
- **Model-picker interactive completeness** requires the real `IHomeModelPickerFeatureProvider`
  composition; when absent the rail button stays disabled (truthful). The named-parameter routing is
  implemented; runtime verification pending slot.

## For the coordinator
1. Compile (guarded dotnet, `-m:1`): Browse NativeUI, Home, Haven.Desktop.
2. Run the two test filters above; confirm nonzero executed counts.
3. Queue the cross-app host-aware topbar + Assistant run/usage source decisions with the owner.
4. Independent acceptance should verify: embedded rail opens Profile/Notifications and resolves the
   model picker; panels load under the genuine Home theme; close joins all retained originals.
