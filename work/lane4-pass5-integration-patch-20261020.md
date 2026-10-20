# Lane4 pass5 — exact-base integration patch (root-applied, NOT committed by lane4)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`.
Base: frozen pass3 checkpoint `8631490897ce8dceac910df170aa28cee27375fb` (common-base HEAD fb708
plus matching dirty source). This patch SUPERSEDES `work/lane4-pass4-integration-patch-20261020.md`
(`ce0abe97…`): PATCH A–D are carried forward (A unchanged; D exact/verified; B/C status unchanged) and
PATCH E (shared header) and PATCH F (Assistants rail source) are added. Lane4 did NOT edit
`App.UnifiedWorkspaceHost.cs` or `App.OriginalWindowsHome.cs` (lane5/root-owned); all lane4 behavior
lives in `App.UnifiedWorkspaceHost.SharedControls.cs` partials. Root applies/reconciles these hunks
after lane4 + lane5 freezes.

---

## 1. Lane5 host wiring hooks (PATCH A)

File: `9to1 Workspace/shared/src/Haven.Desktop/App.UnifiedWorkspaceHost.cs`
Base: HEAD clean (no lane4 edits). Two hooks; both use only members declared in the lane4
`App.UnifiedWorkspaceHost.SharedControls.cs` partial.

### A.1 CreateAsync — wire gateway, shared context, SVG resolver, and compose the frame

Exact base (clean lines ~295–298):

```csharp
            _workspace = new BrowseNativeWorkspace(_chrome, _ => IsCurrent);
            var scene = BrowseNativeSurface.CreateScene(_workspace, this, appearance: null, _homeTheme.Context);
            _startupLoadingLoader?.Dispose(); _startupLoadingLoader = null;
            _scene = new CuiSceneHost(scene.ControlRegistry); Content = _scene;
```

Replace with:

```csharp
            _sharedGateway = new HavenGateway(this);
            _workspace = new BrowseNativeWorkspace(_chrome, _ => IsCurrent, sharedContext: new SharedHostContext(this));
            var scene = BrowseNativeSurface.CreateScene(_workspace, this, appearance: null, _homeTheme.Context);
            scene.ControlRegistry.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
            _startupLoadingLoader?.Dispose(); _startupLoadingLoader = null;
            _scene = new CuiSceneHost(scene.ControlRegistry);
            ComposeSharedFrame();
```

(`HavenPackagedSvgResolver` is already in the host assembly at
`HavenUI/Backend/HavenPackagedSvgResolver.cs`; `RegisterImageSourceResolver` is a generic registry
API already used by `ChatComposerCuiView`.) Add `using Haven.Desktop.HavenUI.Backend;` to
`App.UnifiedWorkspaceHost.cs` (not present in the clean host). `ComposeSharedFrame` now mounts the
scene into the authored shell frame and, if the compiled bundle is unavailable (BLOCKED), degrades
truthfully to `Content = _scene` (scene-only, no panels, no raw reparse).

### A.2 CloseCoreAsync — join the shared-panel settlement before releasing borrowed owners

Exact base (clean lines ~511–514):

```csharp
                Task[] originals; lock (_gate) originals = _sources.ToArray();
                foreach (var original in originals) await Join(original);
                ThrowAppCauses(failures);
```

Replace with:

```csharp
                await JoinSharedPanelSettlementAsync(failures);
                Task[] originals; lock (_gate) originals = _sources.ToArray();
                foreach (var original in originals) await Join(original);
                ThrowAppCauses(failures);
```

`JoinSharedPanelSettlementAsync` disposes the mounted panel and shell FIRST (reserving their post-Dispose
cleanup tasks), then joins the full settlement including those disposal tasks before the session leases
are released. It uses only `JoinOriginalAppTaskAsync` / `AddAppCause`. It has no prototype or app-level
export of its own; root wiring is exactly this one call site.

---

## 2. Root registration patch — genuine Home model-route residency BEFORE Home.Resources/Broker (PATCH D)

File: `9to1 Workspace/shared/src/Haven.Desktop/App.OriginalWindowsHome.cs`
Base: HEAD clean. The actual `HomeNativeWindowsComposition` constructor builds
`ResourceAuthorizationService` (`Resources`, line 116) by snapshotting the resolver array;
`HomeModelPickerFeatureProvider` authorizes `home.profile-model-routes`, but the current composition never
registers `HomeModelRouteProfileOwner` / `HomeModelRouteOwner` / `HomeModelRouteActionPolicies`. Without
this patch every real model route read/mutation returns `PermissionDenied` even after a session Begin.
This is an exact root/Dulche Home composition reconciliation, provided for root to apply — lane4 did not
edit the file.

### D.1 Register the route action policy (single owning policy source)

Exact base (clean line 48) — the `originalActionPolicies:` concatenation currently ends with
`... _actualGeneratedUiPolicy!])`. Append the canonical route policy as the last element:

```csharp
                originalActionPolicies: OriginalWindowsDeveloperPolicies().Concat(OriginalInstalledHomeAppsPolicies()).Concat([new FilesNativeBrowserActionPolicySource(), new SiteNativeActionPolicies(), _actualAssistantTaskContextWritePolicy!, _actualAssistantMemoryWritePolicy!, new HomeUcmlContextActionPolicy(), _actualAssistantCapabilityInitializationPolicy!, _actualAssistantMiniComputerOperationPolicy!, _actualAssistantMiniComputerIdentityPolicy!, _actualAutomationLibraryWritePolicy!, _actualAssistantAttachmentReadPolicy!, _actualAssistantAttachmentImportPolicy!, _actualAssistantAttachmentEgressPolicy!, _actualGeneratedUiPolicy!, new HomeModelRouteActionPolicies()]),
```

### D.2 Register the two route owners inside configureOriginalResolvers BEFORE Resources is built

Exact base (clean lines ~63–77):

```csharp
                configureOriginalResolvers: ownership =>
                {
                    var projectContexts = CreateOriginalAssistantProjectContextReadOwner(ownership);
                    authority = new(files!, ownership.Identity.Profiles, ownership.Ownership);
                    resolver = new(authority);
                    sites = new(authority);
                    sitesResolver = new(sites);
                    return new([resolver, sitesResolver], new Dictionary<Type, object>
                    {
                        [typeof(CanonicalProjectContextStoreReadOwner)] = projectContexts,
                        [typeof(ICanonicalProjectContextStoreReadSource)] = projectContexts,
                        [typeof(NativeFilesWorkspaceAuthority)] = authority, [typeof(FilesArtifactResourceResolver)] = resolver,
                        [typeof(ISiteNativeWorkspaceAuthority)] = sites, [typeof(SiteNativeProjectAccessResolver)] = sitesResolver
                    });
                });
```

Replace the returned resolver list with the same two plus the genuine Home route owners (using the SAME
store/profile identity this composition already created — no new Home/store, no permissive private owner;
exactly one owning resolver per resource kind). NOTE: the lambda parameter is `ownership`, and the
composed identity is `ownership.Identity` (it is the same `identity` object passed to
`configureOriginalStores`, so `ownership.Identity.Profiles` / `ownership.Identity.StateStore` are the
correct paths — a bare `identity` is out of scope here and would not compile):

```csharp
                    return new([resolver, sitesResolver,
                        new HomeModelRouteProfileOwner(ownership.Identity.Profiles),
                        new HomeModelRouteOwner(ownership.Identity.Profiles, new HomeVersionedModelRouteRepository(ownership.Identity.StateStore))],
                        new Dictionary<Type, object>
                    {
                        [typeof(CanonicalProjectContextStoreReadOwner)] = projectContexts,
                        [typeof(ICanonicalProjectContextStoreReadSource)] = projectContexts,
                        [typeof(NativeFilesWorkspaceAuthority)] = authority, [typeof(FilesArtifactResourceResolver)] = resolver,
                        [typeof(ISiteNativeWorkspaceAuthority)] = sites, [typeof(SiteNativeProjectAccessResolver)] = sitesResolver
                    });
```

`HomeModelRouteProfileOwner` (`home.profile-model-routes`) and `HomeModelRouteOwner`
(`home.model-route`) are in `HavenOS.Home.Core` (already imported). `ResourceAuthorizationService`
requires exactly one resolver per resource kind, so each scope kind remains single-owned.

---

## 3. Compiled CUI bundle registration — Haven.Desktop (PATCH B, BLOCKED until root applies)

`HavenNotificationsCuiDocument.Load` / `HavenSharedPanelShellDocument.Load` consume the packaged compiled
bundle `CUI.Compilation.v1` and now refuse with `CUIR070` at semantic reconstruction until the lane2
aligned typed transport is merged (no SourceText parser fallback). The Haven.Desktop project does not
currently emit the bundle, so these surfaces are BLOCKED until root applies this registration.

File: `9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj`
Add (mirroring `Spaces/HavenOS.Spaces.csproj`), with the CORRECT root relative path:
`Haven.Desktop.csproj` lives at `9to1 Workspace/shared/src/Haven.Desktop/`, so the root `framework`
directory is four levels up, not two:

```xml
  <Import Project="../../../../framework/CUI/Compiler/buildTransitive/CakeOS.Cui.props" />
  <!-- ... existing ItemGroup/ProjectReference ... -->
  <Import Project="../../../../framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets" />
```

And declare the authored CUI source roots so the compiler discovers the shared documents:

```xml
  <ItemGroup>
    <CuiSourceRoot Include="Views/Pages/Home" />
  </ItemGroup>
```

After applying, the compiled bundle includes `Views/Pages/Home/HavenNotifications.cui`,
`Views/Pages/Home/HavenSharedPanelShell.cui`, and `Views/Pages/Home/HavenAppHeader.cui` (PATCH E). Until
lane2's typed semantic reconstruction is merged, the consumer truthfully refuses rather than reparsing
raw source.

### B.2 CUIB001 — legacy `.hui` embedded resources in Haven.Desktop (BLOCKED on canonical migration)

Importing the CUI targets runs `ValidateCuiProjectItems`, which errors CUIB001 on any project item with a
`.axaml`/`.xaml`/`.hui` extension. `Haven.Desktop.csproj` currently embeds legacy HUI templates that WILL
trigger CUIB001 once the targets are imported. Narrowing `CuiSourceRoot` to `Views/Pages/Home` does NOT
fix this because the validator scans `@(EmbeddedResource)` (and `@(None)`/`@(Content)`/`@(Compile)`), not
just the CUI root.

Exact legacy resource consumers (do NOT delete these working app capabilities to force a pass):

- `Prefabs/**/*.hui` → `Prefabs/ChatAddMenu.hui`, `Prefabs/Chatbox.hui` are loaded by
  `HavenMarkupParser(_prefabs)` in `Views/Pages/Chat/ChatHavenScene.cs` (and other chat scenes) to author
  chat/prefab surfaces.
- `DynamicUI/**/*.hui` → 18 templates (`ChatSidebarGroupRow`, `ChatAssistantMessage`, `PlanDayItem`,
  `StudioFileRow`, `AgentCatalogCard`, …) are loaded by `HavenDynamicUITemplateCatalog.FromAssembly(...)`
  in `ChatHavenScene`, `StudioProjectHavenView`, `ProjectHavenScene`, `AgentsHavenScene`, and the Browser
  scenes for runtime-owned template instances.

Minimal canonical migration/packaging dependency (owned by the framework/root lane, not this lane): the
legacy HUI templates must be migrated to canonical `.cui` documents and the consuming hosts
(`HavenDynamicUITemplateCatalog`, `HavenMarkupParser`) updated to consume the compiled/typed CUI transport,
OR the framework guard must carve out a governed, non-CUI-authorized "legacy HUI asset" packaging path that
still embeds them without whitelisting application-authored CUI. Neither is decided here; this lane does not
remove the conformance guard (`ValidateCuiProjectItems` stays intact) and does not delete the working
chat/prefab/DynamicUI capabilities. This dependency blocks the compiled-bundle emission for Haven.Desktop
until resolved; it is recorded so it does not silently stop the independent source work below or let anyone
claim the panels/build pass.

---

## 4. Model-picker transport note (PATCH C, BLOCKED)

`HomeModelPickerCuiSurface.LoadDefault()` still reconstructs the ModelPicker scene from the author-side
`.cui` (genuine stateless V1 packaging/standalone compatibility). The compiled V2 transport is owned by
root/lane2; lane4 does not fork it. The host-open path awaits the real composed provider/session/controller
initialization and surfaces typed refusal; it no longer reports `Succeeded` before that settles.

---

## 5. Shared header seams (PATCH E)

The authored shared header component (`Views/Pages/Home/HavenAppHeader.cui` +
`HavenAppHeaderBindings.cs`) is a reusable per-app surface, not part of the host's own top bar. Each app
that wants the host-aware header composes it with a genuine `IHavenHostContext` and the same typed shared
gateway. Because the compiled CUI transport is BLOCKED (CUIR070), the header cannot be loaded/verified yet;
this patch records the exact seams so root wires them when the transport lands.

### E.1 Embed the header document

File: `9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj`
Append to the existing `EmbeddedResource` group (near the other Home views, line ~84):

```xml
    <EmbeddedResource Include="Views/Pages/Home/HavenAppHeader.cui" LogicalName="Haven.Desktop.Home.HavenAppHeader.cui" />
```

### E.2 Host-facing header factory seam (root wires when transport is available)

`HavenAppHeaderBindings` is constructed with `(IHavenHostContext, Func<string,CancellationToken,Task<HavenSharedOpenResult>>, Func<string,CancellationToken,Task>)`. Root composes it per embedded app surface using the same `SharedHostContext(this)` instance from PATCH A.1 (or an app's own standalone host context) and the same `_sharedGateway` open methods:

```csharp
var header = new HavenAppHeaderBindings(
    new SharedHostContext(this),               // genuine host context
    (kind, token) => kind switch
    {
        "9to1.Haven.OpenModel" => _sharedGateway.OpenModelPickerAsync(token),
        "9to1.Haven.OpenNotifications" => _sharedGateway.OpenNotificationsAsync(token),
        "9to1.Haven.OpenProfile" => _sharedGateway.OpenProfileAsync(token),
        _ => Task.FromResult(HavenSharedOpenResult.Refused("UnknownCommand", "Unknown shared header open."))
    },
    (query, token) => /* app-owned search */ Task.CompletedTask);
```

This keeps model/notifications/profile routed through the one genuine Home gateway/owners (never duplicated
per app) and never fabricates availability: `HavenAppHeaderBindings` derives `ShowModel`/`ShowNotifications`/
`ShowProfile` from `IHavenHostContext.IsEmbedded` + the support flags. Root may wire this as a host-level
composable fragment; lane4 does not hard-wire it into `App.UnifiedWorkspaceHost.cs` (lane5-owned) so app
surfaces decide to include the header.

---

## 6. Assistants rail icon source registration (PATCH F)

The host source `HostAssistantRailIconSource` reads the configured catalogue from the SAME composed
canonical bridge and truthfully reports running/usage as uncomposed. It needs a bridge factory bound to the
host's real assistant bridge. Because running/usage storage owners are not yet composed, the projection
returns typed `Unavailable`/`UsageNotRecorded` (never a fabricated most-used).

### F.1 Host provision of the rail source

In the host's `_services` composition/registration (root-owned slot where the assistant bridge is already
bound), register `IAssistantRailIconSource`:

```csharp
services.AddSingleton<IAssistantRailIconSource>(sp =>
    new HostAssistantRailIconSource(() => sp.GetService<IAssistantCanonicalBridge>()));
```

Resolution order and honesty are fully implemented in `AssistantRailIconProjection.ResolveAsync`
(catalogue → running → most-used → generic only when none configured). The projection is exercised by
`AssistantRailIconProjectionTests`; the rail button instantiates `new AssistantRailIconProjection(source)`
and consumes `ResolveAsync()`.

---

## 7. DI/session note

No DI registration was edited by lane4 beyond the seams documented above. `OpenModelPickerPanelAsync`
composes the REAL `HomeModelPickerFeatureProvider` + `HomeVersionedModelRouteRepository` +
`HomeModelPickerOriginalSessionProvider` from the SAME Home owners. The registration patch (PATCH D) is the
required root-integrated authorization reconciliation.
