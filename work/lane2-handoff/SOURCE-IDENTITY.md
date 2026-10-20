# Lane 2 — Source identity and custody record (pass 5 final bounded)

- Worktree HEAD: `fb70840d561286ecd46bd62b3c0736bf7d24108b`
- Branch: `codex/mercury-transport-20261020`
- Assigned scope: lane 2 — genuine typed CUI V2 transport and packaging.
- Dispatched via: `parallel6-lane2-transport.prompt.md`, then `parallel6-lane2-transport-pass2.prompt.md`,
  then pass 3 (frozen pass-2 review `lane2-transport-pass2-review-88575ef0.md`), then pass 4 (frozen
  pass-3 review `lane2-transport-pass3-review-a67a.md`).
- Plan authority: `/workspace/shared/9to1-test-evidence/cui-v2-transport-packaging-plan-de33c168.md`
  (SHA256 `27663ede8d4ec2f676612d29f55a957b7fc28c8892462f91f426b8d723e9ae6c`).
- Canonical specification: `docs/specification/9to1-development-specification-v2.md`
  (SHA256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- Owner references read: 2026-10-20 single-app-implementation; latest composer/icon/sidebar references.
- Root already applied the 4-arg ctor/4-out Deconstruct (SHA `581a2638…`); old-Core binary probe passed
  (receipt `cui-action-old-binary-probe-20261020/runs/20261020T181639Z-96fc232e/result.json`). Those are
  NOT reapplied.

## Files lane 2 owns

1. `framework/CUI/Core/CuiV2Transport.cs` (new) — typed V2 DTOs + conversions.
2. `framework/CUI/Core/CuiCompiledArtifact.cs` — V2 envelope records + strict codec + admission
   (incl. `V1RuntimeAbiVersion`/`V2RuntimeAbiVersion` constants, duplicate-JSON-key rejection,
   derived-capability enforcement, resource-span typed rejection pass 4).
3. `framework/CUI/Core/CuiCompilationMetadata.cs` — unchanged (already in Core).
4. `framework/CUI/Compiler/CuiCompiler.cs` — compiled IR/metadata/transport projection (kept).
5. `framework/CUI/Compiler/CuiV1ArtifactCodec.cs` (new) — genuine V1 writer/reader (lowercase wire keys).
6. `framework/CUI/Compiler/CuiV2Projection.cs` (new) — `CuiCompiledDocument` → V2 DTO projection.
7. `framework/CUI/Compiler/CuiCompilationMetadata.TypeForwarder.cs` (new) — type forwarder.
8. `framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs` — mixed V1/V2 emission, classification,
   statefulness promotion, ownership-safe incremental cleanup, strict fail-closed path/ownership
   validation, stateless ABI seeding decoupled from the runtime's advertised ABI, all-ancestor physical
   containment and SDK-correct publish eligibility (pass 4).
9. `framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets` — generic packaging (kept).
10. `9to1 Workspace/Spaces/HavenOS.Spaces.csproj` — compiled-resource/raw packaging metadata (kept).
11. `9to1 Workspace/Spaces/UI/Chat/ChatComposerCuiDocument.cs` — real V2 consumer; pass 4 calls
    `ValidateArtifactHostSupport` right after codec decode.
12. Lane-specific test files (see TESTS.md).
13. `9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs` — genuine consumer contract; pass 4
    adds language/registry/sibling tests + helpers `StatelessDocument`/`EnvelopeFor`/`MaxAbi`.
14. `9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj` — test-only Compiler project reference added.

## Handoff / integration seams (root/lane 1)

- `Core/CuiDocument.cs` (lane 1): lane 2 supplied exact minimal PATCH in
  `work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md` (four-arg CLR ctor + four-out `Deconstruct`);
  **already applied by root** (SHA `581a2638…`) — do not reapply.
- `Runtime/CuiRuntimeCompatibility.cs` (lane 1): lane 2 supplies exact PATCH in
  `work/lane2-handoff/PATCH-Runtime-artifact-admission.md` (preimage SHA `23d0e130…`) adding
  `ValidateArtifactHostSupport` + constants (supersedes `PATCH-Runtime-Abi2-admission.md`).

## Post-edit source identities (pass 5)

| Path | SHA-256 |
|---|---|
| framework/CUI/Core/CuiCompiledArtifact.cs | `57c0310f80d108b1404cedce10661ba3314e741418a410a0620a3def2480378c` |
| framework/CUI/Core/CuiV2Transport.cs | `3d515075499f0a247e1548052852a01d8d934b487f09473c063285b05a48ab75` |
| framework/CUI/Compiler/CuiCompiler.cs | `f2b7dabb024a7ca08c75a00564310700003d6f7d4751c255df84842bd9c7c1b9` |
| framework/CUI/Compiler/CuiV2Projection.cs | `c6e2c16dc67bc72da568e9b566c3947ba60daf91b58aee3f7d9a09957987c352` |
| framework/CUI/Compiler/CuiV1ArtifactCodec.cs | `5af0b4c8ffebe82ca107d2ebdd9af5962bbc2d8de9fb1fc67a2a64e2fba4a991` |
| framework/CUI/Compiler/CuiCompilationMetadata.TypeForwarder.cs | `c90de0d9e74a2000e2ce1852724a59a8984bd36eb69e04649df8141a5b99367f` |
| framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets | `db5504578584102204cdc3bdc366c7a35025590bb31b7cbf1b937a5f8ff0f1f9` (pass-5) |
| framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs | `33ede57ca7860c72bf7c2149336855a7f53cba2167473d456c8b6c244fc7d73d` (pass-5) |
| framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py | `4055c4143105d059387623d75fafdb4a8e536668670f8e9e4142bdf9d8328720` (pass-5) |
| framework/CUI/Compiler.Tests/CuiLocalStateAbiOutputTests.cs | `22131f397eaffbbd2466d0a167fe97f963d40b7fad9e71805a005b0005e792a1` |
| framework/CUI/Compiler.Tests/CuiV2TransportCodecTests.cs | `60cf3ba446b619b4c0f2b1536a3e27c858323c52d22411553a04823387cffe34` |
| 9to1 Workspace/Spaces/HavenOS.Spaces.csproj | `81da49921b0b161f75aeda2828e6994c60e21f4ef30a934406a773e84d173c91` |
| 9to1 Workspace/Spaces/UI/Chat/ChatComposerCuiDocument.cs | `0c1c9efacd48a363f132a64399795c6ff2593b06e7b5bae61204ae3f535e4c52` |
| 9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs | `807d69654c51b459baefb766c3522ecb6868f6cf3be710f330ac8ae7befb50fb` (pass-5) |
| 9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj | `03854bab71b44275b46973d818c94c4e67b59f3e32880221a366575771cbb84f` |
| work/lane2-handoff/Runtime/CuiRuntimeCompatibility.cs | `14515fcf0c118c705a7fe65a9c4015e689db4766540ae026c4aca6f62f6bfcff` |
| work/lane2-handoff/PATCH-Runtime-artifact-admission.md | `b125e881fc85f393a6ffe0a8718e5aaefd23bd0c2aeb4ae71fb3754c497a80a0` |
| work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md | `6325b3ef79435649fb24b5de96a84226489598c9b0fdbbfdb4438bc02ca3af2c` (unchanged) |
| work/lane2-handoff/PATCH-Runtime-Abi2-admission.md | `a367f3b3de9bb9031dfc69f3681dbd03301d5e7caaff5a491335f4bac1c6c686` (superseded) |

Pass-5 fixes four files (`CompileCuiSourcesTask.cs`, `CakeOS.Cui.targets`, `verify_lane2_v2_packaging.py`,
`ChatComposerCompiledTransportTests.cs`); all other rows are unchanged from the frozen pass-4 state.
Frozen pass-4 review `lane2-transport-pass4-review-f2b5.md` is authoritative for the 15-file inventory.
The worktree is dirty on top of HEAD `fb70840d`.

## Status

- All lane-2 changes are uncommitted (no commit/push per instructions).
- Root-owned `Runtime/CuiRuntimeCompatibility.cs` is NOT edited in the worktree (preimage SHA
  `23d0e130…` verified); it is supplied as the exact handoff patch only.
- No dotnet/MSBuild/test runs have been executed: lane 2 is not lane 1 and the serialized .NET slot has
  not been allocated. All compile and test results are **NOT RUN**. Runtime-patch-dependent tests
  (consumer + `ValidateArtifactHostSupport`) are NOT RUN until root applies
  `PATCH-Runtime-artifact-admission.md` and allocates the slot.
