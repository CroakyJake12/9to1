# Lane4 pass3 — exact-base integration patch (root-applied, NOT committed by lane4)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`.
Target: root applies/reconciles after lane4 + lane5 freezes. Lane4 did NOT edit
`App.UnifiedWorkspaceHost.cs` (lane5-owned) or run any build/test/app. All lane4
behavior lives in `App.UnifiedWorkspaceHost.SharedControls.cs` partials; the hooks
below are the only site that must touch the lane5-owned file.

---

## 1. Lane5 host wiring hooks (PATCH A)

File: `9to1 Workspace/shared/src/Haven.Desktop/App.UnifiedWorkspaceHost.cs`
Base: HEAD clean (no lane4 edits). Two hooks. Both use only members declared in the
lane4 `App.UnifiedWorkspaceHost.SharedControls.cs` partial (`HavenGateway`,
`SharedHostContext`, `ComposeSharedFrame`, `JoinSharedPanelSettlementAsync`) plus
existing host privates (`_services`, `_homeTheme`, `_actor`, `IsCurrent`).

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
`HavenUI/Backend/HavenPackagedSvgResolver.cs`; `RegisterImageSourceResolver` is a
generic registry API already used by `ChatComposerCuiView`.) Add the using directive
`using Haven.Desktop.HavenUI.Backend;` to `App.UnifiedWorkspaceHost.cs` (it is not
present in the clean host).

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

`JoinSharedPanelSettlementAsync` joins the mounted panel's retained action/disposal
settlement (including any post-Dispose async cleanup), disposes composed
original-session leases, and disposes the authored shell loader. It uses only
`JoinOriginalAppTaskAsync` / `AddAppCause`.

---

## 2. Compiled CUI bundle registration — Haven.Desktop (PATCH B, BLOCKED until root applies)

`HavenNotificationsCuiDocument.Load` and `HavenSharedPanelShellDocument.Load` now consume the
packaged compiled bundle `CUI.Compilation.v1` with hard format/ABI/registry/capability admission
(no raw fallback). The Haven.Desktop project does not currently emit that bundle, so these shared
surfaces are BLOCKED until this registration is applied. Root/lane2 owns the compiler
infrastructure; this is the concrete consumer/registration patch.

File: `9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj`
Add (mirroring `Spaces/HavenOS.Spaces.csproj`):

```xml
  <Import Project="../../framework/CUI/Compiler/buildTransitive/CakeOS.Cui.props" />
  <!-- ... existing ItemGroup/ProjectReference ... -->
  <Import Project="../../framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets" />
```

And declare the authored CUI source roots so the compiler discovers the shared documents:

```xml
  <ItemGroup>
    <CuiSourceRoot Include="Views/Pages/Home" />
  </ItemGroup>
```

After applying, the compiled bundle includes `Views/Pages/Home/HavenNotifications.cui` and
`Views/Pages/Home/HavenSharedPanelShell.cui`, which the lane4 consumers select by `Source`.
Until applied, panels truthfully refuse with `CUIR069` (bundle missing) and the shell mounts a
scene-only frame (no hidden raw reparse).

---

## 3. Home ModelPicker transport (PATCH C, BLOCKED)

`HomeModelPickerCuiSurface.LoadDefault()` still reconstructs the ModelPicker scene from the
author-side `.cui` (genuine stateless V1 packaging/standalone compatibility). The compiled
V2 transport for the model picker is owned by root/lane2; lane4 did not fork it. When lane2's
corrective API is aligned, `LoadDefault` should route through the same `CuiCompiledArtifactCodec`
consumer. Until then the standalone/tests path is preserved and the host-open path is fenced by
`PublicationGate` + the composed real provider/session.

---

## 4. DI/session note

No DI registration was edited. `OpenModelPickerPanelAsync` composes the REAL
`HomeModelPickerFeatureProvider` + `HomeVersionedModelRouteRepository` +
`HomeModelPickerOriginalSessionProvider` from the SAME Home owners (`_home.Profiles/Resources/
Broker/StateStore` + `_services` `IModelProviderRegistry`/`IPrivacyPreferenceStore`). If root
prefers DI registration, the same real instances must be bound; the composed path is the current
truthful implementation.
