# Lane 12 (Mercury) Build shipping + host integration — HANDOFF

Worktree `/workspace/9to1-mercury-build-shipping`, branch `codex/mercury-build-shipping-recovery-20261020`,
HEAD `6ca569201fcf69500b881406be89ec4493cab859`. Source/test-only: **no dotnet/restore/build/test/app/native
/model-subagent/provider/config/credential commands were run**. Root owns the SDK/native slot. All execution below is NOT RUN.

## Assigned scope (Build)

Concrete compiled transport (CUI V1 reader → Runtime host admission) + the typed Build (Dev) source-workspace
native content factory/session wired into the central `App.UnifiedWorkspaceHost`.

## Owned source/test changes

| Path | Change | SHA-256 |
|------|--------|---------|
| `framework/CUI/Core/CuiV1Transport.cs` | genuine V1 reader (`CuiV1ArtifactReader`/`CuiV1ArtifactCodec`/`CuiV1LoadedArtifact`/`CuiV1LoadedDocument`/`CuiV1AdmissionException`): root-kind guard, `TryGetPropertySafe`, `ReadFormatVersion` kind check, Bool fallback honored, `Semantics.Components` required, envelope reconciled (`using CakeOS.Cui.Compiler;` + `using System.Linq;`) | `f30a2c26e2d0278a58e7085259f8ee46309251758c476d1a3ee10190541a7009` |
| `framework/CUI/Compiler.Tests/CuiV1ReaderShimRegressionTests.cs` | causal regressions (array root, string formatVersion, malformed scalar, missing Components, envelope language contradiction, absent IsLive⇒true) | `5c9f2aa144ecd883f91826b24bd695986be7d02c482894c0711646f8592f1dde` |
| `9to1 Workspace/shared/src/Haven.Desktop/Services/BuildBrowseAppContentAddress.cs` | address codec (`9to1://dev/...`: project + task/run/context; `Landing` for empty segments) | `d16de5246a299f8b8ec7ff1adfb9cc671af18583a5f9edb5fd081449360f57a1` |
| `9to1 Workspace/shared/src/Haven.Desktop/Services/BuildBrowseAppContentFactory.cs` | DI + composition wiring: new `IBuildBorrowedResources`/`BuildRouteBorrowedResources`/`BuildLandingComposition`; per-session `SpaceTaskWorkspaceService`+`SpaceDevelopmentWorkspace`+`SpaceDevelopmentReferenceCatalog` built from DI singletons; authentic Files-route borrowed Home readiness/join (`BindOriginalSameProcess` signature unchanged) | `efb81469c4146426a22c88fb4af1c8022bc709defd0d9c16c711361327b63163` |
| `9to1 Workspace/shared/src/Haven.Desktop/Services/BuildBrowseAppContentSession.cs` | real landing presents `DeveloperSavedProjectsPage` (partial retained at constructor capture; project-open opens the real workbench); `BuildWorkbenchReadiness` rewritten with its own `DesktopOriginalWorkLifetime` + borrowed Home readiness (no recursion/self-join); real `TryPrepareToCloseAsync` (veto only on `HasUnsavedWork`); close joins landing + page + init + borrowed resources, preserving errors | `5a02f869ee200d68a1a948225ea23bba635f1c43365aeda608c7f1e7cb4fb861` |
| `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Development/DeveloperProjectWorkbenchPage.cs` | added `internal bool HasUnsavedWork` (`_editor is { IsDirty: true }` or `_pending`) to drive the real close contract | `42d602f4d3067f3108d6e2564eed25227cda897f5850c90eaaeca4ae4f39f19e` |
| `9to1 Workspace/shared/tests/Haven.Desktop.Tests/BuildBrowseAppContentIntegrationTests.cs` | fixed `For()` misuse (returns `AppContentAddress`); replaced two vacuous StubFactory PASS tests with an honest registry routing-contract test; kept address/registration/foreign-authority units | `0a81d06eafcc8f8badf03b1410017cd5521e2713a465daff435cbd5c09a15652` |
| `9to1 Workspace/shared/tests/Haven.Desktop.Tests/BuildBrowseAppContentAuthenticTests.cs` | NEW authentic landing test: drives the real `BuildBrowseAppContentSession` over the genuine Linux Home/Files/SQLite `CanonicalCompatibleTaskContextCompositionTests.Rig` with real Dev services/catalog; deterministic double only for the borrowed Home readiness seam | `b645d47b7e2b26ce897071c6f03db281ffb4e677fc958ae89ffc48f25395fa5d` |
| `9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj` | registered `BuildBrowseAppContentAuthenticTests.cs` in the `net10.0` ItemGroup | `725d43c344e86abc200e03972b9c3059694e651605faabd6a76d665b03bf638d` |

Carried (unchanged, owned by this lane): `9to1 Workspace/Dev/DeveloperWorkbenchCuiDocument.cs`
(`c23c45c5ede62015d1a55473501ef9a273bd61ebaa42198ce3024dc962007483`),
`9to1 Workspace/Dev/UI/DeveloperWorkbench.cui`
(`edb8671e61d36f78f43df5a97e786fecec9d6e882c6c5d691848495946bb972e`),
`9to1 Workspace/Dev/Tests/DeveloperWorkbenchCompiledTransportTests.cs`
(`6a9590663f01658e88bf7a002361c4741c1809b13aa58385d741668c758766be`),
`9to1 Workspace/Dev/HavenOS.Dev.csproj`
(`677068b69e36874b6f7901e37322d6810a4ca03fb027008d8fff4f2029ee784d`).

## Central patch (apply, do not edit in place)

`work/build-shipping-recovery/integration/App.UnifiedWorkspaceHost.BuildBinding.patch.md` — adds the Build
binding to `NativeAppContentRegistry` in `App.UnifiedWorkspaceHost.InitializeCoreAsync`.

- Preimage: `9to1 Workspace/shared/src/Haven.Desktop/App.UnifiedWorkspaceHost.cs` @ HEAD, SHA-256
  `a0b9f836433b8a94993b87567df739d6465b30f79206ffe676388cf20116ff29` (not present in sparse tree; unpatch).
- Tuple: `NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000028"), "dev",
  HavenAppRouteKind.Dev, build)` — canonical dev mode id from `BuiltInModeSeed`, key `"dev"`, route `Dev` (19).
- `build = BuildBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime)`.

## Verification (root, after SDK slot)

```bash
# Build (Dev) registration/address codec units
dotnet test '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj' -c Release \
  --no-restore -m:1 --filter 'FullyQualifiedName~BuildBrowseAppContentIntegrationTests'
# Authentic Build landing over the genuine Linux Home/Files/SQLite rig
dotnet test '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj' -c Release \
  --no-restore -m:1 --filter 'FullyQualifiedName~Authentic_Build_landing'
# Shared reader/shim regressions
dotnet test framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj -c Release --no-restore -m:1 \
  -nodeReuse:false --filter 'FullyQualifiedName~CuiV1ReaderShimRegressionTests'
# Dev transport + original document tests (existing lane verification)
dotnet test '9to1 Workspace/Dev/Tests/HavenOS.Dev.Tests.csproj' -c Release --no-restore -m:1 \
  -nodeReuse:false -p:CuiUsePrebuiltCompiler=true -p:CuiCompilerTaskAssembly=<root-built-Release-task.dll> \
  --filter 'FullyQualifiedName~DeveloperWorkbenchCompiledTransportTests|FullyQualifiedName~DeveloperWorkbenchCuiDocumentTests'
```

## Remaining blockers

- No .NET/SDK slot in this lane: nothing compiled/tested; all execution is NOT RUN. Source is unaccepted
  until those checks actually run.
- The authentic landing test is authored; an authentic project-open (workbench) test is not yet authored
  because it requires a real Task/coordinator snapshot setup inside the Rig (not yet built). The real
  workbench path is code-reviewed but not exercised by a driving test yet.

## Source identity / spec

- Canonical spec SHA `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`; single-app
  implementation owner correction read.
- No commits/pushes; no external writes; no central App/DI/Spaces/Voice edits (patch-only).
