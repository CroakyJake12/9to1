# Lane4 pass4 — exact-base integration patch (root-applied, NOT committed by lane4)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`.
Base: frozen pass3 checkpoint `8631490897ce8dceac910df170aa28cee27375fb` (common-base HEAD fb708
plus matching dirty source). Lane4 did NOT edit `App.UnifiedWorkspaceHost.cs` or
`App.OriginalWindowsHome.cs` (lane5/root-owned); all lane4 behavior lives in
`App.UnifiedWorkspaceHost.SharedControls.cs` partials. Root applies/reconciles these hunks after
lane4 + lane5 freezes.

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

`JoinSharedPanelSettlementAsync` disposes the mounted panel and shell FIRST (reserving their
post-Dispose cleanup tasks), then joins the full settlement including those disposal tasks before
the session leases are released. It uses only `JoinOriginalAppTaskAsync` / `AddAppCause`.

---

## 2. Root registration patch — genuine Home model-route residency BEFORE Home.Resources/Broker (PATCH D)

File: `9to1 Workspace/shared/src/Haven.Desktop/App.OriginalWindowsHome.cs`
Base: HEAD clean. The actual `HomeNativeWindowsComposition` constructor builds
`ResourceAuthorizationService` (`Resources`, line 116) by snapshotting the resolver array;
`HomeModelPickerFeatureProvider` authorizes `home.profile-model-routes`, but the current
composition never registers `HomeModelRouteProfileOwner` / `HomeModelRouteOwner` /
`HomeModelRouteActionPolicies`. Without this patch every real model route read/mutation returns
`PermissionDenied` even after a session Begin. This is an exact root/Dulche Home composition
reconciliation, provided for root to apply — lane4 did not edit the file.

### D.1 Register the route action policy (single owning policy source)

In the `originalActionPolicies:` concatenation (base line 48), the list
terminates with `... _actualGeneratedUiPolicy!])`. Append the canonical route policy:

```csharp
                    ... _actualGeneratedUiPolicy!, new HomeModelRouteActionPolicies()])
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

Replace the returned resolver list with the same two plus the genuine Home route owners (using the
SAME store/profile identity this composition already created — no new Home/store, no permissive
private owner; exactly one owning resolver per resource kind):

```csharp
                    return new([resolver, sitesResolver,
                        new HomeModelRouteProfileOwner(identity.Profiles),
                        new HomeModelRouteOwner(identity.Profiles, new HomeVersionedModelRouteRepository(identity.StateStore))],
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

`HavenNotificationsCuiDocument.Load` / `HavenSharedPanelShellDocument.Load` consume the packaged
compiled bundle `CUI.Compilation.v1` and now refuse with `CUIR070` at semantic reconstruction until
the lane2 aligned typed transport is merged (no SourceText parser fallback). The Haven.Desktop
project does not currently emit the bundle, so these surfaces are BLOCKED until root applies this
registration.

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

After applying, the compiled bundle includes `Views/Pages/Home/HavenNotifications.cui` and
`Views/Pages/Home/HavenSharedPanelShell.cui`. Until lane2's typed semantic reconstruction is merged,
the consumer truthfully refuses rather than reparsing raw source.

---

## 4. Model-picker transport note (PATCH C, BLOCKED)

`HomeModelPickerCuiSurface.LoadDefault()` still reconstructs the ModelPicker scene from the
author-side `.cui` (genuine stateless V1 packaging/standalone compatibility). The compiled V2
transport is owned by root/lane2; lane4 does not fork it. The host-open path awaits the real
composed provider/session/controller initialization and surfaces typed refusal; it no longer
reports `Succeeded` before that settles.

---

## 5. DI/session note

No DI registration was edited. `OpenModelPickerPanelAsync` composes the REAL
`HomeModelPickerFeatureProvider` + `HomeVersionedModelRouteRepository` +
`HomeModelPickerOriginalSessionProvider` from the SAME Home owners. The registration patch
(PATCH D) is the required root-integrated authorization reconciliation.
