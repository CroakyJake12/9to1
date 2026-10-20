# Lane 12 (Mercury) Build shipping + host integration — HANDOFF

Worktree `/workspace/9to1-mercury-build-shipping`, branch `codex/mercury-build-shipping-recovery-20261020`,
HEAD `6ca569201fcf69500b881406be89ec4493cab859`. Source/test-only: **no dotnet/restore/build/test/app/native
/model-subagent/provider/config/credential commands were run**. Root owns the SDK/native slot.

## Assigned scope (Build)

Concrete compiled transport (CUI V1 reader → Runtime host admission) + the typed Build (Dev) source-workspace
native content factory/session wired into the central `App.UnifiedWorkspaceHost`.

## Owned source/test changes

| Path | Change | SHA-256 |
|------|--------|---------|
| `framework/CUI/Core/CuiV1Transport.cs` | new genuine V1 reader (`CuiV1ArtifactReader`/`CuiV1LoadedArtifact`/`CuiV1LoadedDocument`/`CuiV1AdmissionException`) | `43eb48d62c29dc6676d9b00e34aa1168c51bb27c9a9b177fbd2701d02bcebc00` |
| `framework/CUI/Compiler/CuiV1ArtifactCodec.cs` | writer + thin shim (public nested records + `ReadArtifact`/`WriteBundle`, maps Core→nested, throws `InvalidDataException`) | `17d65c7bc4dd1e5151703d0a520162caf859c99731cf79bf3290f20bc0ca22b3` |
| `framework/CUI/Runtime/CuiRuntimeCompatibility.cs` | added V1 `ValidateArtifactHostSupport(CuiV1LoadedArtifact, CuiSourceSpan)` overload (3-arg `RequireCapabilities`) | `eba18e5f48893f4d46edaca49e0e54bcf75246b0472ccd8ba22aff28697fd038` |
| `framework/CUI/Compiler.Tests/CuiV1ReaderShimRegressionTests.cs` | new regression (9 Facts) | `57b1b2037bdb20e7a97aef1d1e3bf8d94f422ccd3b4cc8c0ad4e48891c86af19` |
| `9to1 Workspace/Dev/HavenOS.Dev.csproj` | removed hand-authored .cui EmbeddedResource/None items; rely on canonical `CUI.Compilation.v1` discovery/emission | `677068b69e36874b6f7901e37322d6810a4ca03fb027008d8fff4f2029ee784d` |
| `9to1 Workspace/shared/src/Haven.Desktop/Services/BuildBrowseAppContentAddress.cs` | new address codec (`9to1://dev/...`: project + task/run/context) | `d16de5246a299f8b8ec7ff1adfb9cc671af18583a5f9edb5fd081449360f57a1` |
| `9to1 Workspace/shared/src/Haven.Desktop/Services/BuildBrowseAppContentFactory.cs` | new `IBrowseAppContentFactory` binding (same-process; `BindOriginalSameProcess`) | `6581a6e72fee88810ba8900a7b5dcd23788e5c51604ab573eae13d18bafda574` |
| `9to1 Workspace/shared/src/Haven.Desktop/Services/BuildBrowseAppContentSession.cs` | new `IBrowseNativeAppContentSession`; resolves canonical project, validates canonical Task, opens real `DeveloperProjectWorkbenchPage` with session-local readiness | `03199a740e72dbba8b3a7f3e17616622de4f69ec011408087379ef2f56ca223d` |
| `9to1 Workspace/shared/tests/Haven.Desktop.Tests/BuildBrowseAppContentIntegrationTests.cs` | new integration tests (address codec + central dev registration/native publication) | `7d1b93ba7a5da496bb2750ae98283c0b5ef71148207ac9894f9583f703885219` |
| `9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj` | registered the new test in the `net10.0` ItemGroup | `9a16b9a3d52fcc8e6358cf20a3c1b4064fc533ea925522e67dcdc23b19bea39f` |

Carried (unchanged, owned by this lane): `9to1 Workspace/Dev/DeveloperWorkbenchCuiDocument.cs`
(`c23c45c5ede62015d1a55473501ef9a273bd61ebaa42198ce3024dc962007483`),
`9to1 Workspace/Dev/UI/DeveloperWorkbench.cui`
(`edb8671e61d36f78f43df5a97e786fecec9d6e882c6c5d691848495946bb972e`),
`9to1 Workspace/Dev/Tests/DeveloperWorkbenchCompiledTransportTests.cs`
(`6a9590663f01658e88bf7a002361c4741c1809b13aa58385d741668c758766be`).

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
dotnet test '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj' -c Release \
  --no-restore -m:1 --filter 'FullyQualifiedName~BuildBrowseAppContentIntegrationTests'
# Dev transport + original document tests (existing lane verification)
dotnet test '9to1 Workspace/Dev/Tests/HavenOS.Dev.Tests.csproj' -c Release --no-restore -m:1 \
  -nodeReuse:false -p:CuiUsePrebuiltCompiler=true -p:CuiCompilerTaskAssembly=<root-built-Release-task.dll> \
  --filter 'FullyQualifiedName~DeveloperWorkbenchCompiledTransportTests|FullyQualifiedName~DeveloperWorkbenchCuiDocumentTests'
# Shared reader/shim regressions
dotnet test framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj -c Release --no-restore -m:1 \
  -nodeReuse:false --filter 'FullyQualifiedName~CuiV1ReaderShimRegressionTests'
```

## Remaining blockers

- No .NET/SDK slot in this lane: nothing compiled/tested; all execution is NOT RUN. Source is unaccepted
  until those checks actually run.
- Full shipping composition fidelity limited: `DeveloperSavedProjectsPage` cannot be constructed in this
  sparse checkout (needs `SpaceDevelopmentReferenceCatalog`/`SpaceRegistry`/`CanonicalSpaceRoutes`); Build
  landing (`9to1://dev`) therefore currently throws `NotSupportedException` (addressed to the saved-project
  discovery entry) until that page is present.
- `BuildBrowseAppContentSession` opens the real workbench only for exact canonical project + Task/Run/context;
  identity/foreign refusals are truthful and scoped (no project/task/readiness authority handed out).

## Source identity / spec

- Canonical spec SHA `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`; single-app
  implementation owner correction read.
- No commits/pushes; no external writes; no central App/DI/Spaces/Voice edits (patch-only).
