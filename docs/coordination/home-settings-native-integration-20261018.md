# Native Home Settings: bounded integration proposal

Status: **OPEN — source investigation, no product implementation or acceptance**.

The actual Browse-derived host sends Home → Settings to `9to1://home/settings`, but its native app registry admits Files and Spaces only. Fixing the route by displaying the old Settings panel would not establish working Home Settings. This proposal identifies the existing ownership and persistence seams that must be integrated before exposing controls.

## Source identity and custody

- Investigation base: `61d9e9d24f19f9f4714efa5cc8c96f6665ae827b`.
- Isolated branch: `codex/9to1-home-settings-20261018`.
- Authoritative supplied archive: `9to1-Haven-Build-Weekend.zip`, SHA-256 `e1a70c47290926e52e1ee194e285374f8f259b39f584ed299230d44aacf0212d`.
- Extracted execution master: `9to1-V2-execution.txt`, SHA-256 `42b4a5df1ad97ae969394ac305b2053ae45048c9f41250ca84b188c271b5e677`. Relevant source lines: 1935–2005 (Home ownership, navigation and Dashboard policy), 2291–2303 (Settings and canonical theme preferences).
- Latest supplied owner amendment: `10-OWNER-AMENDMENT.txt`, SHA-256 `4862bb0f6c1a485fb142e2889d2324de7dbef63df868a6aaf3a8ee11ed7668c9`. Its Home Core, Browse-derived host, same-tab and compatibility requirements remain binding.
- No applicable `AGENTS.md` was found above or inside the reviewed Home/shared/CUI paths. The repository's Files instructions do not govern this lane.
- The requested standalone Sandbox plugin Operating Contract was not available; it was not read or followed. Cloudflare Sandbox skills are a different capability and were not substituted.

## Observed source facts

| Boundary | Current implementation | Consequence |
| --- | --- | --- |
| Home route | `shared/src/Haven.Desktop/Views/Pages/Home/UnifiedHomePage.cs`: `home.settings` falls through to native `9to1://home/settings`; Manage Providers uses the same route. | A visible Settings action targets an unregistered owner. |
| Native registry | `shared/src/Haven.Desktop/App.UnifiedWorkspaceHost.cs`: `NativeAppContentRegistry` contains Files and Spaces; internal `UnifiedHomeContentFactory` admits only root `browse://home` and `browse://newtab`. | A route string or legacy control cannot supply missing Home admission, lifetime or restoration semantics. |
| Actual Home composition | `Home/Source/Home/Core/HomeNativeWindowsComposition.cs`: runtime registers Home state, permissions and productivity. `HomeCoreRuntime.cs` declares the `settings` capability unavailable by default. | No actual native Home Settings provider is registered. This is not evidence that the whole Home runtime is unavailable. |
| Existing canonical Settings markup | `Home/UI/Home.cui`: Settings has profile/layout state, independent AI-tile and AI-reorder controls, reload, and CAKE ID navigation. `apps/Home/src/AvaloniaHome/HomeApp.Workspace.cs` binds these to `HomeDashboardLayoutService`; `HomeOwnedDashboardStateStore.cs` guards the original actor at final commit. | These are real domain pieces, but they are composed in the separate Home application, not in the current Browse-derived host. |
| Dashboard currently displayed by Haven | `shared/src/Haven.Desktop/Views/Pages/Home/NewDashboardPage/NewDashboardPage.Widgets.cs` persists `dashboard.widget-layouts.v1` through `IVersionedSettingsStore` and projects the older `IDashboardLayoutRepository`. Its assistant applies `DashboardEditPlanApplier` to that layout. | The current displayed Dashboard does not read the Home Core `home.dashboard.layout` AI policy. Merely hosting its old Settings controls would save ineffective preferences. Do not expose them until the actual Dashboard consumes the same owner and policy. |
| Canonical theme contracts | `Home/Source/Home/Core/HomeThemePreferenceService.cs` already defines explicit device/account scope, versioned preference, revisioned compare/exchange, current authority and change subscription. `IHomeCoreStateStore.WriteGuardedAsync` supports actor-checked commit through the actual Home store. | A new settings schema or renderer is unnecessary. The native backend/owner binding is the missing integration. |
| Native theme compatibility | `framework/CUI/Themes/CuiThemePreferenceReader.cs` deliberately reads `preferences.json` owned by `UserPreferencesService`. That writer uses a separate record and its `Save()` catches I/O/access errors. | Making a new Home-state preference independently writable would leave competing preferences. Wrapping the old setter cannot honestly return a successful revisioned commit receipt. |
| CUI subscription | `framework/CUI/Themes/CuiThemePreferenceContext.cs` already borrows `ICuiThemePreferenceSource`, isolates exact scope/revision and handles change notifications. `CuiControlLoader.SetThemePreferenceContext` is the existing renderer injection point. | Reuse this contract for active and newly created surfaces; do not replace CUI or add a private global theme registry. |
| Legacy Settings adapter | `shared/src/Haven.Desktop/Views/Pages/Settings/SettingsHavenPage.axaml.cs` uses `Haven.UI`, obtains optional owners via global `App.Services`, starts several asynchronous reads in its constructor, and exposes synchronous disposal. | Directly embedding this adapter would not provide the required canonical CUI and exact admitted-work/drain ownership. Preserve its capabilities as backlog, not as an accepted shortcut. |

All facts above are source observations at the stated base. No native/UI action or build was performed by this investigation. Native failure evidence is owned by the coordinating task.

## Recommended bounded implementation sequence

1. **Establish the actual native theme owner before adding UI.** Implement the existing `IHomeThemePreferenceBackend` and `IHomeThemeScopeAuthority` against the actual Home actor/store. Use `WriteGuardedAsync` for revision and final-commit authority; do not treat the request's profile string as authority. Own one service through the existing Home composition, with tracked initialization, operations, cancellation and exact disposal. Register only the capability actually supplied. Account-synchronised scopes must remain unsupported until the authenticated account backend is genuinely connected.

2. **Resolve legacy persistence as part of that owner boundary.** Preserve the existing theme name, appearance, accent, accessibility/font choices and unrelated preference fields. Define a versioned, explicit migration/compatibility adapter so there is one authoritative preference. Preserve unknown/incompatible data and display a recoverable diagnostic instead of overwriting it. A committed Home preference and an old native reader must never disagree silently. Use the existing web backend only as a contract reference; its browser authentication/JavaScript implementation is not native authority. This step requires coordinated changes outside the Home page/route files, particularly the existing `UserPreferencesService` writer and native CUI preference reader/consumer boundary.

3. **Expose a real Appearance/CUI workflow in the current Home page.** Inject the admitted Home theme owner and `CuiThemePreferenceContext`; author a CUI document with only operational controls. Keep Settings inside the existing tab and reuse its Home owner, not another Home window/process or the legacy main shell. Preserve each destination's state while switching Home views. Route both the sidebar action and Models → Manage Providers accurately: a theme-only implementation must not label itself a working provider manager. Native `9to1://home/settings` deep links need explicit admission and restoration support; a local section switch alone does not satisfy that route.

4. **Publish through the borrowed CUI preference source.** Update compatible active views and new views from the same exact committed observation, retaining scope and revision. Keep host/user palette choices during initialization. Detach subscriptions only after admitted operations settle. Do not modify the active shutdown repair or silently reinterpret a failed close as clean retirement. Minimum `App.UnifiedWorkspaceHost` wiring must be integrated with the coordinating owner after its shutdown work settles.

The storage compatibility and composition changes make this larger than a safe route-only repair. They are executable engineering work, not a credential/approval blocker. The route remains open until the integrated workflow exists.

## Required acceptance for the first real Settings slice

- Actual Browse-hosted pointer journey: open Settings in the same tab, choose a supported complete theme, observe the current canvas and another already-open compatible app update, switch to a website and back, and reopen/restart with the same saved choice. Record exact source/build and native screenshots separately for Professional and Glow; headless rendering is not native visual acceptance.
- Real persisted backend: reopen the canonical owner/store, retain exact scope and revision, preserve existing preferences and unrelated fields, reject stale revisions, and preserve unknown schema data. Failed I/O must not show Saved.
- Authority: foreign request metadata, profile replacement during a held write and revoked/current-scope changes must not write or publish another profile's state. Unsupported account synchronisation remains explicit.
- Lifetime: held read/write plus close/cancellation must join the exact source tasks. Retain original failure identities and cleanup siblings. A cancelled or failed save must not become an untracked background operation or false clean close.
- Route/state: both direct navigation and restored route reach the same admitted owner; two tabs have distinct UI instances; switching sections preserves valid local state and does not duplicate subscriptions.
- Run the existing Home theme service, CUI theme preference/context and Unified Home owning checks plus focused regressions for the concrete integration risks. Use the shared build lock, full raw output, TRX and actual exit code. No acceptance is inferred from source compilation alone.

## Remaining Settings scope

The first Appearance/CUI slice would leave Account & Subscription, AI & Models/provider management, Permissions & Trust, Apps & Updates, Storage, Mesh & Devices, Privacy, Accessibility beyond implemented controls, Notifications and Advanced/Diagnostics open. Full theme/account sync, compatible standalone-app propagation, custom themes, approved reference fidelity, and Dashboard AI-policy integration remain separate gates until directly evidenced. Nothing in this proposal marks those requirements delivered or reduces the original specification.
