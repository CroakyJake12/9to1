# Lane 2 — Source identity and custody record

- Worktree HEAD: `fb70840d561286ecd46bd62b3c0736bf7d24108b`
- Branch: `codex/mercury-transport-20261020`
- Status at start: clean (no modified/untracked files).
- Assigned scope: lane 2 — genuine typed CUI V2 transport and packaging.
- Dispatched via: `parallel6-lane2-transport.prompt.md`.
- Plan authority: `/workspace/shared/9to1-test-evidence/cui-v2-transport-packaging-plan-de33c168.md`
  (SHA256 `27663ede8d4ec2f676612d29f55a957b7fc28c8892462f91f426b8d723e9ae6c`).
- Canonical specification: `docs/specification/9to1-development-specification-v2.md`
  (SHA256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`).
- Owner references read: 2026-10-20 single-app-implementation; latest composer references (TEXT only).

## Dirty baseline (prior to this lane)

- `framework/CUI/Core/CuiCompiledArtifact.cs` defined FormatVersion1 artifact with `Metadata + SourceText`
  per document under `CUI.Compilation.v1`. `ChatComposerCuiDocument` reparses SourceText via `CuiRichParser`.
  This is the inherited dirty V1 shape that lane 2 must replace with a DISTINCT typed V2 artifact
  (FormatVersion2, `CUI.Compilation.v2`, stateful RuntimeABI2) without parser/compiler/source-text reconstruction.

## Files lane 2 owns

1. `framework/CUI/Core/CuiV2Transport.cs` (new) — typed V2 DTOs + conversions.
2. `framework/CUI/Core/CuiCompiledArtifact.cs` — V2 envelope records + codec + admission.
3. `framework/CUI/Core/CuiCompilationMetadata.cs` — unchanged (already in Core).
4. `framework/CUI/Compiler/CuiCompiler.cs` — compiled IR/metadata/transport projection + ABI calculation only.
5. `framework/CUI/Compiler/CuiV1ArtifactCodec.cs` (new) — genuine V1 writer/reader (full compiled projection).
6. `framework/CUI/Compiler/CuiV2Projection.cs` (new) — `CuiCompiledDocument` -> V2 DTO projection.
7. `framework/CUI/Compiler/CuiCompilationMetadata.TypeForwarder.cs` (new) — type forwarder for moved metadata type.
8. `framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs` — classification/emission/resource identity/stale cleanup.
9. `framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets` — generic packaging.
10. `9to1 Workspace/Spaces/HavenOS.Spaces.csproj` — compiled-resource/raw packaging metadata only.
11. `9to1 Workspace/Spaces/UI/Chat/ChatComposerCuiDocument.cs` — real V2 consumer.
12. New lane-specific test files (see TESTS.md).
13. `9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs` — genuine consumer contract.

## Handoff / integration seams (root/lane 1)

- `Core/CuiDocument.cs` (lane 1): `CuiActionDefinition` record primary constructor currently has an
  optional fifth `LocalTarget` parameter. Source-level four-arg calls still compile, but binary consumers
  that referenced the old four-parameter record constructor would break. Proposed compatibility patch is
  captured in `EVANOFF-CuiActionDefinition-four-arg.md` — do NOT edit `CuiDocument.cs` directly.
- `Runtime/CuiControlLoader.cs` / `Runtime/CuiRuntimeCompatibility.cs` (lane 1): new entry admission for
  V2/ABI2 and accepted stateless ABI1 path is a lane 1/integration seam; not edited here.

## Current final (20 Oct, post-checkpoint)

- `CuiActionDefinition` already has the optional fifth `LocalTarget` parameter in `Core/CuiDocument.cs`
  (verified). No four-arg binary-compat patch is required; both four-arg and five-arg source forms compile.
- Fixed the genuine V1 envelope casing: `CuiV1Artifact`/`CuiV1Document` now carry explicit
  `[JsonPropertyName]` so the writer emits lowercase envelope keys (`format`, `formatVersion`,
  `compilerSha256`, `metadata`, `documents`, `source`, `sha256`, `compilation`) while the payload stays
  PascalCase (`Semantics`, `Metadata`, `SourceMap`, `$type`). This matches the documented V1 shape and
  the manual `CuiV1ModelReader`.
- Simplified the real-MSBuild verifier fixture (`Build.Tasks.Tests/verify_lane2_v2_packaging.py`):
  removed the ambiguous `None`+`Content` on the same stateless source; now uses distinct sources
  (`Stateful.cui` embedded, `Stateless.cui` `Content` with `Link="Raw/Stateless.cui"` +
  `CopyToOutputDirectory`). Updated destination assertions to `bin/Raw/Stateless.cui` and read the obj
  compiled artifact via `json.loads` to assert Format/V2 + 2 documents.

## Build/check status

- All source changes are uncommitted (no commit/push per instructions).
- No dotnet/MSBuild/test runs have been executed: lane 2 is not lane 1 and the serialized .NET slot has
  not been allocated. All compile and test results are **NOT RUN** until the coordinator allocates the
  slot. The verifier only executes when that slot is granted.
