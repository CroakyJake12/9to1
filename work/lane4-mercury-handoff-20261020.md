# Lane 4 handoff — Shared Haven control cluster, host-aware rail, filled SVGs

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`
Baseline HEAD: `fb70840d` (tree `9aa0f24c…`, spec SHA `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`)

## Scope (this lane)
Persistent Haven-owned model/notifications/profile control cluster shared across embedded apps,
host-aware embedded-vs-standalone rail, filled/rounded sidebar SVGs, truthful shared-control gateways.

## Assignment boundaries
- Only this assigned tree is writable. No commits/pushes/deployment; diff stays uncommitted.
- TEXT ONLY / no .NET build or test execution (no slot assigned) → all authored tests are **NOT RUN**.
- No subagents used.

## Identity
- Source authority: `docs/specification/9to1-development-specification-v2.md` (local).
- Owner references read: 2026-10-18-haven-spaces, 2026-10-20-haven-shared-header,
  2026-10-20-professional-app-accents, 2026-10-20-spaces-svg-icons,
  2026-10-20-chat-composer-dulche, 2026-10-20-professional-spaces-heading,
  2026-10-20-continuous-composer, 2026-10-20-floating-format-bar, 2026-10-20-sidebar-separation,
  2026-10-20-composer-icons.
- Contract decided earlier: `IHavenHostContext`/`IHavenSharedControlGateway` in `Haven.Application`
  (lower-level assembly referenced by both Browse and Haven.Desktop). The host registers
  `HavenPackagedSvgResolver.Resolve` on the Browse scene registry and passes an `IHavenHostContext`.

## Files changed
- `Haven.Application/Haven/HavenHostContext.cs` (new): interfaces `IHavenHostContext`, `IHavenSharedControlGateway`.
- `Haven.Desktop/HavenUI/Backend/HavenPackagedSvgResolver.cs` (lane 1 asset, not edited): path/rect + currentColor resolver.
- `Haven.Desktop/Assets/haven-model.svg`, `haven-notifications.svg`, `haven-profile.svg` (new): filled rounded, fill=currentColor, single `<path>`, CC0 provenance comments.
- `Browse/NativeUI/BrowseNativeWorkspace.cs`: `_sharedControls` (gateway) → `_hostContext` (`IHavenHostContext`); ctor param `sharedContext`; bindings `Haven.Embedded/ModelAvailable/NotificationsAvailable/ProfileAvailable`.
- `Browse/NativeUI/BrowseNativeWorkspace.SharedControls.cs`: availability via `_hostContext`; dispatch via `_hostContext.SharedControls`.
- `Browse/NativeUI/UI/BrowseWorkspace.cui`: `HavenSharedRail` left column (Column=0, RowSpan=4) with Model/Notifications/Profile buttons, `Image Source="avares://Haven/Assets/haven-*.svg"`, `IsVisible="{Binding Haven.Embedded}"`.
- `Haven.Desktop/App.UnifiedWorkspaceHost.cs`: register resolver on scene registry, build `HavenGateway` + `SharedHostContext`, pass into `BrowseNativeWorkspace`, compose shared frame, dispose shared panel loader on close.
- `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` (new): `SharedHostContext`, `HavenGateway`, `ComposeSharedFrame`, `ShowSharedPanel`/`HideSharedPanel`, `OpenProfilePanel` (real `NativeCakeAccountUiOwner.OpenAsync(window)`), `OpenNotificationsPanel` (authored CUI over `HavenNotificationService.GetRecentAsync/SetReadAsync/DismissAsync`), `OpenModelPickerPanel` (real `HomeModelPickerCuiController` over `IHomeModelPickerFeatureProvider`, truthful degradation when provider absent), `HavenNotificationsCuiBindings`, `HomeModelPickerDispatcher`, `HavenNotificationRow`.
- `Browse/NativeUI/Tests/BrowseSharedControlsTests.cs` (new): **NOT RUN** — availability/embedding, gateway routing, partial support.

## Behavior
- Embedded Haven frame: the persistent rail exposes Model/Notifications/Profile, bound to the real
  gateway. Availability is resolved from real services (never invented):
  - Profile = `NativeCakeAccountUiOwner` resolvable from DI → `OpenAsync(window)`.
  - Notifications = `HavenNotificationService` resolvable → CUI centre over durable reads.
  - Model = `IHomeModelPickerFeatureProvider` resolvable → authentic Home model picker surface
    (User scope only; "global" unsupported). Provider is not registered in the Infrastructure DI
    today, so the rail model button degrades truthfully to disabled.
- Standalone Browse: `Haven.Embedded`=false → rail hidden; the app retains its own header semantics.

## Known dependencies / not yet verified
- **No build/test run** (no slot). All new code and tests are NOT RUN; must be compiled and
  exercised by the coordinator once a .NET slot is assigned.
- **Model picker action routing (dependency/coordination):** the authored `ModelPicker.cui`
  conveys row-scoped action args (`action-route-id`, `action-provider-id`, `action-model-id`,
  `action-capability`, …) as action metadata. The standard CUI dispatcher contract delivers only
  `(command, parameter)` and does not pass that metadata (verified in `framework/CUI` runtime:
  `CuiActionInvocation.ResolveCurrentParameter` only uses a named `<Action>` parameter/binding).
  So candidate selection/mutation/save-with-args/preview cannot be faithfully routed yet. Current
  `HomeModelPickerDispatcher` executes only the self-contained structural actions (Refresh,
  SaveRoute, FinishAudit) and refuses the rest. Queue this as a required extension/coordination
  item with the owner before claiming the model picker interactive editing complete.
- **Assistant rail (running → most-used → generic):** no real "running assistant" or
  "most-used configured assistant" data source exists in the current codebase (searched;
  only `AssistantUsageLimits` token/time cost fields). Implementing the selection without those
  real inputs would fabricate state, so it is NOT implemented; it is recorded as a coordination
  dependency requiring an owned run-state/usage query contract. Do not claim it complete.
- **Standalone header** for Browse (all-four controls) is out of this lane's embedded focus; the
  rail is embedded-only via `Haven.Embedded`.

## For the coordinator
1. Compile `BrowseNativeUI`, `Haven.Application`, `Haven.Desktop` (guarded dotnet, `-m:1`).
2. Run `BrowseSharedControlsTests` (NOT RUN) once a slot is assigned.
3. Route the model-picker `action-*` metadata bridge and the assistant run/usage query contract
   to the owner for a decision; hold only those claims until resolved.
4. Independent acceptance should exercise: embedded rail appears and opens Profile/Notifications;
   rail hidden in standalone; model button disabled (truthful) when provider absent.

Next executable task after this handoff: resolve the two coordination dependencies (model-picker
action metadata, assistant run/usage source), then author the assistant icon selection policy and
wire it into Spaces Home with real inputs.
