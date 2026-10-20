# Lane 2 — Source identity and custody record

- Worktree HEAD: `fb70840d561286ecd46bd62b3c0736bf7d24108b`
- Branch: `codex/mercury-transport-20261020`
- Assigned scope: lane 2 — genuine typed CUI V2 transport and packaging.
- Dispatched via: `parallel6-lane2-transport.prompt.md`, then `parallel6-lane2-transport-pass2.prompt.md`.
- Plan authority: `/workspace/shared/9to1-test-evidence/cui-v2-transport-packaging-plan-de33c168.md`
  (SHA256 `27663ede8d4ec2f676612d29f55a957b7fc28c8892462f91f426b8d723e9ae6c`).
- Canonical specification: `docs/specification/9to1-development-specification-v2.md`
  (SHA256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- Owner references read: 2026-10-20 single-app-implementation; latest composer/icon/sidebar references.

## Files lane 2 owns

1. `framework/CUI/Core/CuiV2Transport.cs` (new) — typed V2 DTOs + conversions.
2. `framework/CUI/Core/CuiCompiledArtifact.cs` — V2 envelope records + strict codec + admission.
3. `framework/CUI/Core/CuiCompilationMetadata.cs` — unchanged (already in Core).
4. `framework/CUI/Compiler/CuiCompiler.cs` — compiled IR/metadata/transport projection (pass-1 state, kept).
5. `framework/CUI/Compiler/CuiV1ArtifactCodec.cs` (new) — genuine V1 writer/reader (+ span fix).
6. `framework/CUI/Compiler/CuiV2Projection.cs` (new) — `CuiCompiledDocument` → V2 DTO projection.
7. `framework/CUI/Compiler/CuiCompilationMetadata.TypeForwarder.cs` (new) — type forwarder.
8. `framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs` — mixed V1/V2 emission, classification, statefulness
   promotion, ownership-safe incremental cleanup.
9. `framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets` — generic packaging (pass-1 state, kept).
10. `9to1 Workspace/Spaces/HavenOS.Spaces.csproj` — compiled-resource/raw packaging metadata (pass-1 state).
11. `9to1 Workspace/Spaces/UI/Chat/ChatComposerCuiDocument.cs` — real V2 consumer (+ compiler using).
12. Lane-specific test files (see TESTS.md).
13. `9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs` — genuine consumer contract.

## Handoff / integration seams (root/lane 1)

- `Core/CuiDocument.cs` (lane 1): lane 2 supplies exact minimal PATCH in
  `work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md`. The earlier handoff statement that "no binary
  patch is required" was **incorrect** and is retracted: an optional fifth constructor argument does not
  preserve the old four-parameter CLR constructor or the old four-output `Deconstruct`.
- `Runtime/CuiControlLoader.cs` / `Runtime/CuiRuntimeCompatibility.cs` (lane 1): V2/ABI2 admission remains a
  root/lane1 integration seam; not edited here (out of lane-2 scope).

## Status

- All lane-2 changes are uncommitted (no commit/push per instructions).
- No dotnet/MSBuild/test runs have been executed: lane 2 is not lane 1 and the serialized .NET slot has not
  been allocated. All compile and test results are **NOT RUN** until the coordinator allocates the slot.
