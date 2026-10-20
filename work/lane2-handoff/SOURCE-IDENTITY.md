# Lane 2 — Source identity and custody record (pass 3)

- Worktree HEAD: `fb70840d561286ecd46bd62b3c0736bf7d24108b`
- Branch: `codex/mercury-transport-20261020`
- Assigned scope: lane 2 — genuine typed CUI V2 transport and packaging.
- Dispatched via: `parallel6-lane2-transport.prompt.md`, then `parallel6-lane2-transport-pass2.prompt.md`,
  then pass 3 (frozen pass-2 review `lane2-transport-pass2-review-88575ef0.md`).
- Plan authority: `/workspace/shared/9to1-test-evidence/cui-v2-transport-packaging-plan-de33c168.md`
  (SHA256 `27663ede8d4ec2f676612d29f55a957b7fc28c8892462f91f426b8d723e9ae6c`).
- Canonical specification: `docs/specification/9to1-development-specification-v2.md`
  (SHA256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- Owner references read: 2026-10-20 single-app-implementation; latest composer/icon/sidebar references.

## Files lane 2 owns

1. `framework/CUI/Core/CuiV2Transport.cs` (new) — typed V2 DTOs + conversions.
2. `framework/CUI/Core/CuiCompiledArtifact.cs` — V2 envelope records + strict codec + admission
   (incl. `V1RuntimeAbiVersion`/`V2RuntimeAbiVersion` constants, duplicate-JSON-key rejection,
   derived-capability enforcement).
3. `framework/CUI/Core/CuiCompilationMetadata.cs` — unchanged (already in Core).
4. `framework/CUI/Compiler/CuiCompiler.cs` — compiled IR/metadata/transport projection (kept).
5. `framework/CUI/Compiler/CuiV1ArtifactCodec.cs` (new) — genuine V1 writer/reader (lowercase wire keys).
6. `framework/CUI/Compiler/CuiV2Projection.cs` (new) — `CuiCompiledDocument` → V2 DTO projection.
7. `framework/CUI/Compiler/CuiCompilationMetadata.TypeForwarder.cs` (new) — type forwarder.
8. `framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs` — mixed V1/V2 emission, classification,
   statefulness promotion, ownership-safe incremental cleanup, strict fail-closed path/ownership
   validation, and stateless ABI seeding decoupled from the runtime's advertised ABI.
9. `framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets` — generic packaging (kept).
10. `9to1 Workspace/Spaces/HavenOS.Spaces.csproj` — compiled-resource/raw packaging metadata (kept).
11. `9to1 Workspace/Spaces/UI/Chat/ChatComposerCuiDocument.cs` — real V2 consumer (kept).
12. Lane-specific test files (see TESTS.md).
13. `9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs` — genuine consumer contract.
14. `9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj` — test-only Compiler project reference added.

## Handoff / integration seams (root/lane 1)

- `Core/CuiDocument.cs` (lane 1): lane 2 supplies exact minimal PATCH in
  `work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md` (four-arg CLR ctor + four-out
  `Deconstruct`).
- `Runtime/CuiRuntimeCompatibility.cs` (lane 1): lane 2 supplies exact PATCH in
  `work/lane2-handoff/PATCH-Runtime-Abi2-admission.md` (advertise ABI2; add `MinStatelessAbiVersion`;
  the emitter decoupling is already implemented in lane-2-owned `CompileCuiSourcesTask.cs`).

## Post-edit source identities (pass 3)

| Path | SHA-256 |
|---|---|
| framework/CUI/Core/CuiCompiledArtifact.cs | `5cff671411e573d896eca7b4c2e42728d37bedff14be8a5c7d0bfcce87ac7792` |
| framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs | `e0493448a95960ff1d5c9e6fb88826e87ccd6cca18f6baa3d474ef9cc25cda66` |
| framework/CUI/Compiler.Tests/CuiLocalStateAbiOutputTests.cs | `0783a8f1d2bc307d54ceecea56fa02058d71ac0ddf8e111e63debcefd79c0352` |
| framework/CUI/Compiler.Tests/CuiV2TransportCodecTests.cs | `633012927134c1acce1693443f9d25ecc102b0395a15b6f5cb636f821b9121cb` |
| framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py | `8eec6d2345d896d9d2ce50405fc126b58aadcc10e2c858a18dac1e3d67a53a0f` |
| 9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj | `03854bab71b44275b46973d818c94c4e67b59f3e32880221a366575771cbb84f` |
| work/lane2-handoff/PATCH-Runtime-Abi2-admission.md | `a367f3b3de9bb9031dfc69f3681dbd03301d5e7caaff5a491335f4bac1c6c686` |
| work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md | `6325b3ef79435649fb24b5de96a84226489598c9b0fdbbfdb4438bc02ca3af2c` (unchanged) |

Frozen pass-2 checkpoint `88575ef0ddfa86b412dd5e2bdbf1cbb189eeb081` is preserved; the worktree is dirty
on top of HEAD `fb70840d`. These hashes supersede the pass-2 entries in the frozen identity manifest.

## Status

- All lane-2 changes are uncommitted (no commit/push per instructions).
- No dotnet/MSBuild/test runs have been executed: lane 2 is not lane 1 and the serialized .NET slot has
  not been allocated. All compile and test results are **NOT RUN** until the coordinator allocates the slot.
