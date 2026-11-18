# Home — Apps, Dashboard and global systems — Home; Product role and ownership (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0404 · v2 lines 2144–2145

Home
Product role and ownership

### V2-B0405 · v2 lines 2148–2148

Home’s user-facing functions are merged into Browse, the primary app branded “9to1”. Home Core remains the shared platform control plane: app/package management, service registry, Dulche lifecycle/broker coordination, permission/trust brokerage, account/subscription state, Shared Productivity Engine, model/provider configuration, Action Graph rendering, Mesh, notifications, shared search/index coordination and ecosystem settings. Each service retains one canonical implementation. Apps retain their own canonical documents/projects and shared authenticated service contracts.

### V2-B0406 · v2 lines 2151–2151

Other apps MUST consume Home-owned shared services through supported authenticated interfaces and MUST NOT silently launch incompatible private replacements when a Home service is unavailable. App-owned documents, projects and domain data remain owned by their respective apps/storage layers; Home MUST NOT become the only canonical copy of another app’s artifacts.

### V2-B0407 · v2 lines 2154–2154

The 9to1 installation includes Home Core and the unified Browse-based CUI shell. Native apps require a compatible Home Core service; standalone apps remain usable without opening the visible main window. Shared models, tasks and services do not depend on an open Home tab. On 9to1OS, Home Core may be a system component with the same service contracts.

### V2-B0408 · v2 lines 2157–2157

Home shell and navigation

### V2-B0409 · v2 lines 2160–2160

V3 editorial amendment: V3-OWNER-003, V3-OWNER-004. Original wording is preserved in the replacement ledger.

Home appears as a Home-icon tab in 9to1, with Apps, Dashboard and Models/Discover as sidebar views. New Tab exposes those destinations through a compact icon rail and adds its New Tab view. Library, Notifications, Mesh, Settings and account/profile remain reachable through integrated navigation. Preserve current Spaces destinations for Chat/Assistants and the dashboard-oriented Home experience.

### V2-B0410 · v2 lines 2163–2163

Navigation destinations MUST preserve their own scroll/filter/selection state while the Home process remains active where practical. Deep links from notifications, Go, other apps or the OS MUST be able to open the relevant Home destination/object directly without first routing through the Dashboard.

### V2-B0411 · v2 lines 2166–2166

Dashboard

### V2-B0412 · v2 lines 2169–2169

The 9-1 AI Dashboard is a generative briefing/dashboard surface, not a chat landing page. It MUST assemble permission-authorised cross-app information into a concise live brief and MUST derive every surfaced tile from identifiable source entities/events or an explicitly generated synthesis over identifiable sources.

### V2-B0413 · v2 lines 2172–2172

Dashboard tiles MAY surface upcoming Planner items, tasks, recent files/artifacts, project progress, reminders, travel/routes, Agent or Automation status, relevant Mail/messages, package/update state, storage/account warnings and other timely ecosystem context. Every actionable tile MUST use typed app actions; the Dashboard MUST NOT use raw UI automation to perform app mutations.

### V2-B0414 · v2 lines 2175–2175

Chat remains in Spaces. The Dashboard MAY expose a lightweight ask/action affordance, but its primary purpose is synthesis, prioritisation, status and quick action.

### V2-B0415 · v2 lines 2178–2178

Dashboard layout and customisation

### V2-B0416 · v2 lines 2181–2181

Dashboard layout is user-configurable. The user MUST be able to add supported fixed/provider tiles, remove/hide tiles, resize tiles where the tile declares supported sizes, drag/reorder tiles, arrange tiles into the supported responsive layout, pin/lock tiles against automated movement and restore/reset a layout.

### V2-B0417 · v2 lines 2184–2184

The canonical DashboardLayout MUST use stable TileInstanceID values and store at least TileType, source/provider identity, position/order, declared size, user visibility state, pinned/locked state, tile-specific configuration and layout revision. Responsive rendering MAY adapt exact pixel/grid placement to the current window/device, but it MUST preserve the user’s semantic ordering/grouping and pinned constraints.

### V2-B0418 · v2 lines 2187–2187

Manual user layout changes are authoritative. Automated generation/reordering MUST NOT silently overwrite a newer manual layout revision. When a layout conflict occurs across synced devices, Home MUST preserve both revisions or resolve through an explicit revision/merge policy rather than blind last-write-wins.

### V2-B0419 · v2 lines 2190–2190

AI-generated tiles and AI layout control

### V2-B0420 · v2 lines 2193–2193

Dashboard settings MUST expose two independent user controls: AllowAIGeneratedTiles and AllowAIReorder. These controls are opt-in/opt-out independently; disabling one MUST NOT implicitly disable the other.

### V2-B0421 · v2 lines 2196–2196

When AllowAIGeneratedTiles = false, Dulche/Generative UI MUST NOT create new arbitrary Dashboard tile instances. Existing user-added/system/provider tiles remain usable, and AI MAY still generate content inside a tile whose explicit tile contract permits generated content.

### V2-B0422 · v2 lines 2199–2199

When AllowAIGeneratedTiles = true, Dulche MAY create contextually useful temporary or persisted tiles through the safe Generative UI/CUI schema. Generated tiles MUST declare provenance, the source entities/events they summarise, their lifetime/persistence policy and available typed actions. Generated tiles MUST NOT inject arbitrary executable code.

### V2-B0423 · v2 lines 2202–2202

When AllowAIReorder = false, Dulche MUST NOT move, resize, regroup, reorder or hide existing tiles. When AllowAIReorder = true, Dulche MAY optimise unpinned/unlocked tile placement according to user goals/context, but MUST NOT move or resize tiles marked pinned/locked, MUST preserve explicit hidden-tile choices, and MUST retain an undoable prior layout revision.

### V2-B0424 · v2 lines 2205–2205

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

AI layout changes MUST be attributable in Notifications activity/audit history and MUST be reversible. A user action that manually repositions or pins a tile immediately supersedes older AI layout intent for that tile.

### V2-B0425 · v2 lines 2208–2208

Dashboard tile provider contract

### V2-B0426 · v2 lines 2211–2211

Apps and Home services MAY register Dashboard tile providers through a versioned provider contract. A provider MUST declare TileType, source app/service, supported sizes, required permissions, refresh policy, data dependencies, available actions, whether the tile can be instantiated manually, and whether its content may be generated/modified by Dulche.

### V2-B0427 · v2 lines 2214–2214

Tile data MUST remain source-backed. A tile provider that loses access to its source MUST enter an unavailable/stale state with an explanation rather than silently displaying fabricated current data. Permission revocation MUST prevent future refresh/access and purge or redact cached sensitive content according to the owning source’s policy.

### V2-B0428 · v2 lines 2217–2217

Apps surface and package management

### V2-B0429 · v2 lines 2220–2220

Home Apps is the primary native app catalogue and management surface. It MUST show installed and available 9to1 apps/packages with stable PackageID/AppID, name/icon, installed version, available version, update channel, installation state, dependency/compatibility state, update availability and repair/rollback availability where supported.

### V2-B0430 · v2 lines 2223–2223

Home MUST support install, uninstall, update, repair, launch/open, channel/version selection where allowed and rollback to a validated previous package version where retained. Uninstalling an app MUST NOT silently delete user-created artifacts owned by that app. Artifact-retention/deletion MUST be a separate explicit operation governed by the owning app/storage semantics.

### V2-B0431 · v2 lines 2226–2226

Package installation/update MUST use staged validation and atomic activation. Integrity/signature verification, dependency resolution and Home/shared-service compatibility checks MUST complete before activation. A failed activation MUST leave the previous known-good package usable when rollback data is available.

### V2-B0432 · v2 lines 2229–2229

Home and Launcher MUST consume one canonical package/update database and service. Launcher is the bootstrap/outside-Home client of the same package system; it MUST NOT maintain a second incompatible installer/updater state.

### V2-B0433 · v2 lines 2232–2232

Library

### V2-B0434 · v2 lines 2235–2235

Library is Home’s cross-app artifact index/browser, not a duplicate Files app and not a separate storage authority. It MUST surface canonical references to authorised artifacts across 9to1 apps, including recent, favourite/pinned, shared, generated/created and app/type-filtered content where applicable.

### V2-B0435 · v2 lines 2238–2238

Every Library entry MUST identify the canonical owning app, stable artifact/entity ID, display name, artifact type, relevant location/owner/share metadata and last-modified state. Opening a Library item routes to the owning app/surface and MUST NOT create a duplicate artifact merely because the item is listed in Home.

### V2-B0436 · v2 lines 2241–2241

Library search/filter/sort MUST use the shared search/index service but enforce current permissions at query/open time. Revoked or deleted artifacts MUST not remain usable through stale Library entries.

### V2-B0513 · v2 lines 2545–2545

Home Core MUST expose a machine-readable service registry describing Home-owned platform capabilities and versions. At minimum it includes Dulche broker/runtime coordination, permission/trust broker, Shared Productivity Engine, package/update service, notifications, search/index, Mesh and account/session capability availability.

### V2-B0514 · v2 lines 2548–2548

Before normal startup, every native 9to1 app MUST complete a compatibility handshake against Home Core and declare the required service/capability versions it needs. Missing/incompatible required services result in the global Home repair/update/bootstrap flow rather than undefined partial operation.

### V2-B0515 · v2 lines 2551–2551

Service identity/version MUST be explicit; apps MUST NOT infer compatibility solely from the Home marketing version string.

### V2-B0516 · v2 lines 2554–2554

Startup, background lifecycle and recovery

### V2-B0517 · v2 lines 2557–2557

Home Core MUST auto-start on demand or at session startup when required by installed 9to1 apps. Closing the visible Home window MUST NOT necessarily stop Home Core. Home Core shutdown is allowed only when no required dependent operation is active or when the user/system explicitly requests full 9to1 service shutdown.

### V2-B0518 · v2 lines 2560–2560

Home Core persistent state MUST use crash-safe versioned storage. Corrupt or incompatible Home state MUST enter a diagnosable recovery/repair mode; Home MUST NOT silently discard permission grants, package state, device identities or other durable control-plane state to regain startup.

### V2-B0519 · v2 lines 2563–2563

If a Home service fails while apps are open, dependent apps MUST receive a structured service-unavailable/degraded signal. They MUST preserve their own artifact state and block only operations that require the failed service. When the service recovers, clients reconnect/resynchronise through stable IDs/revisions rather than restarting with assumed fresh state.

### V2-B0520 · v2 lines 2566–2566

Home data/state model

### V2-B0521 · v2 lines 2569–2569

Home MUST maintain stable IDs/versioned records for installed packages/apps, Home services, Dashboard layouts/tiles, Mesh devices/pairings, notification records, search index records, permission/trust grants, account/session cache metadata and other Home-owned settings/control-plane entities. Display names, paths and UI ordering are not identity.

### V2-B0522 · v2 lines 2572–2572

Caches/indexes that are rebuildable MUST be marked as non-authoritative. Durable grants, package activation state, device trust and user layout/settings state MUST be recoverable and versioned. Home MUST distinguish device-local state from account/workspace-synchronised state explicitly.

### V2-B0523 · v2 lines 2575–2575

Home API

### V2-B0524 · v2 lines 2578–2610

9to1.Home.GetState();
9to1.Home.GetServices();
9to1.Home.GetService(serviceID);
9to1.Home.GetCompatibility(appID, appVersion);
9to1.Home.Dashboard.GetLayout();
9to1.Home.Dashboard.UpdateLayout(layout, expectedRevision?);
9to1.Home.Dashboard.SetAIGeneratedTiles(enabled);
9to1.Home.Dashboard.SetAIReorder(enabled);
9to1.Home.Dashboard.AddTile(tileType, config?);
9to1.Home.Dashboard.RemoveTile(tileInstanceID);
9to1.Home.Dashboard.SetTilePinned(tileInstanceID, pinned);
9to1.Home.Apps.List();
9to1.Home.Apps.Install(packageID, versionOrChannel?);
9to1.Home.Apps.Update(appID, versionOrChannel?);
9to1.Home.Apps.Repair(appID);
9to1.Home.Apps.Rollback(appID, version?);
9to1.Home.Apps.Uninstall(appID);
9to1.Home.Library.Query(query);
9to1.Home.Events.Query(query);
9to1.Home.Discover.SearchModels(query);
9to1.Home.Mesh.ListDevices();
9to1.Home.Mesh.Pair(request);
9to1.Home.Mesh.Revoke(deviceID);
9to1.Home.Notifications.List(query?);
9to1.Home.Notifications.MarkRead(notificationID);
9to1.Home.Notifications.Dismiss(notificationID);
9to1.Home.Search.Query(query);
9to1.Home.Settings.Get(key, scope?);
9to1.Home.Settings.Set(key, value, scope?);
9to1.Home.Permissions.List();
9to1.Home.Permissions.Revoke(grantID);
9to1.Home.Permissions.Block(callerID);
9to1.Home.Permissions.Unblock(callerID);

### V2-B0525 · v2 lines 2613–2613

These are minimum canonical entry points. Every additional user-visible Home operation MUST have an equivalent supported API/domain action under the global API-parity rule. APIs MUST return structured state/errors and use stable IDs/revision checks for consequential or concurrent mutations.

### V2-B0526 · v2 lines 2616–2616

Security boundary

### V2-B0527 · v2 lines 2619–2619

Home Core is a high-privilege local control plane. All local IPC/service calls MUST authenticate/identify the caller and evaluate the caller’s effective scopes before exposing privileged operations. Executable filename, window title or self-asserted display name MUST NOT be accepted as caller identity.

### V2-B0528 · v2 lines 2622–2622

Installed signed/registered apps MAY use package identity plus Home-issued process/session credentials. Development/self-built callers MAY use an explicit registered developer/project identity with tamper-evident binding as defined by the global permission model. Trust MUST NOT silently transfer after a material caller identity/signing/content change.

### V2-B0529 · v2 lines 2625–2625

Secrets/credentials MUST be stored through secure credential references and MUST NOT be persisted in plaintext Home configuration, logs, Events, Dashboard tiles or package manifests. Home logs/audits MUST redact secret payloads while retaining accurate action/provenance records.

### V2-B0530 · v2 lines 2628–2628

Platform behaviour

### V2-B0531 · v2 lines 2631–2631

Windows: Home provides the full native hub/shell, Home Core background services and the Home-hosted Universal Model Picker. Native apps depend on the compatible Home installation/Home Core handshake.

### V2-B0532 · v2 lines 2634–2634

9to1-OS: Home Core/shared service contracts remain authoritative platform services, but selected controls may be integrated into OS-native system surfaces. The Universal Model Picker is OS-hosted while using the same canonical Dulche/model/provider state. 9to1-OS MUST NOT create divergent duplicate settings/package/model databases.

### V2-B0533 · v2 lines 2637–2637

Web: the Home web surface uses CAKE ID and server-hosted equivalents for account, Library, Dashboard, notifications, search and other supported cloud capabilities. It MUST capability-negotiate and clearly mark local-only/native-only functions such as local package management, local models/devices or unavailable Home Core services rather than pretending they exist in-browser.

### V2-B0534 · v2 lines 2640–2640

Failure behaviour

### V2-B0535 · v2 lines 2643–2643

Home APIs/services MUST return structured failures including HomeServiceUnavailable, HomeServiceIncompatible, PackageIntegrityFailed, PackageDependencyConflict, PackageActivationFailed, DashboardRevisionConflict, TileProviderUnavailable, PermissionDenied, DeviceRevoked, MeshUnavailable, IndexUnavailable and AccountServiceUnavailable in addition to the global error contract.

### V2-B0536 · v2 lines 2646–2646

Failures in one Home service MUST NOT automatically corrupt or erase unrelated Home state. For example, Discover/provider failure must not invalidate package state; search-index corruption must be recoverable by rebuilding the index; Dashboard tile-provider failure must degrade only affected tiles.

### V2-B0537 · v2 lines 2649–2649

Acceptance requirements

### V2-B0538 · v2 lines 2652–2672

Home is implementation-complete only when acceptance coverage verifies at least:
* A native app refuses normal startup when Home is not installed and successfully resumes through the trusted Install Home/bootstrap flow.
* Closing the visible Home window does not break dependent apps while Home Core remains healthy.
* A Home Core/service crash produces structured degraded state in attached apps, preserves user artifacts and reconnects after service recovery without spawning duplicate private services.
* Dashboard manual tile add/remove/resize/reorder/pin state persists across restart and responsive window sizes.
* AllowAIGeneratedTiles=false prevents arbitrary generated tile creation while existing tiles continue to function.
* AllowAIReorder=false prevents AI movement/layout changes; when enabled, AI cannot move pinned/locked tiles and every AI layout change is reversible.
* Generated Dashboard tiles expose source provenance and fail stale/unavailable rather than fabricating live data.
* Apps install/update/repair/rollback uses the shared package database and survives a failed staged activation without deleting user artifacts.
* Library opens the same canonical artifact in its owning app rather than creating a duplicate.
* Events preserve typed source records and link to the corresponding Action Graph/entity where applicable.
* Discover aggregates at least two configured model/provider sources without identity collisions and performs a valid install/connect action.
* Universal Model Picker override precedence and fallback policy resolve deterministically and do not cross local-to-cloud boundaries when policy forbids it.
* Permission/trust grants can be inspected, narrowed/revoked/blocked and future calls immediately respect the updated state.
* Mesh pairing establishes stable device identity; capability calls obey both ends’ permissions; device revocation prevents subsequent sync/invocation.
* Home notifications preserve read/dismiss state and typed deep links across restart and OS notification bridging.
* Search/Library results stop exposing content after permission revocation/deletion and indexes can be rebuilt without source-data loss.
* A representative shared productivity object can round-trip across Write, Present, Canvas, Boards and Spaces through the Shared Productivity Engine without schema/format loss.
* Account/subscription values shown in Home match the server-authoritative CAKE ID entitlement state and are not inferred from preset labels.
* An app with an incompatible required Home service version is blocked into update/repair instead of running against an undefined interface.
* Home persistent control-plane state can recover from process interruption without silently dropping package activation state, device trust or permission grants.
