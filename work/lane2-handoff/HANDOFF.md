# Lane 2 handoff — typed CUI V2 transport & packaging (pass 2)

- Date: 2026-10-20. Branch `codex/mercury-transport-20261020`. Worktree HEAD `fb70840d`.
- Authorized six Mercury workers; lane 2. No model subagents, no `.NET`/build/restore/app runs until the
  coordinator allocates the serialized slot (lane 1 only). All compile/tests here are **NOT RUN**.
- Authority: read `AGENTS.md`, canonical `docs/specification/9to1-development-specification-v2.md`,
  `work/pc-worker/ONE-APP-IMPLEMENTATION-AUDIT-2026-10-20.md`, the original transport plan
  `…/cui-v2-transport-packaging-plan-de33c168.md`, and the frozen review
  `…/lane2-typed-transport-peer-review-20261020T1724.md`.

## Pass-2 scope

Fix every frozen-review defect in lane 2's files (typed V2 transport, V1 codec, Build.Tasks, Spaces
consumer, tests). No new product features. No commit/push/deploy, no credentials/provider/cap/global
config, no images.

## Findings → resolution

1. **Compile blockers** — fixed in `framework/CUI/Core/CuiV2Transport.cs`, `Compiler/CuiV1ArtifactCodec.cs`,
   `Build.Tasks/CompileCuiSourcesTask.cs`, `Compiler.Tests/CuiV2TransportCodecTests.cs`,
   `Spaces/UI/Chat/ChatComposerCuiDocument.cs`:
   - V2 value/text conversions now bridge `CuiV2Span` → canonical `CuiSourceSpan` via `.ToCuiSourceSpan()`
     and use explicit null-checks instead of a nonexistent string `RequiredValue` extension.
   - `CuiV2Component.ToCuiComponent` maps text parts (`IReadOnlyList<CuiV2TextPart>` → `CuiTextPart[]`).
   - V1 codec uses a proper `ParseOrThrow` helper (no uninitialized `using JsonDocument`).
   - `CuiV1Compiled` qualified as `CuiV1ArtifactCodec.CuiV1Compiled` in task + tests.
   - `ChatComposerCuiDocument` re-adds `using CakeOS.Cui.Compiler;`.

2. **Binary compatibility — `CuiActionDefinition` four-arg ctor / four-out Deconstruct** — Core/CuiDocument.cs
   is lane1/root. Lane 2 supplies the exact minimal integration PATCH
   `work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md` and an exact reflection-signature test
   (`CuiLocalStateAbiOutputTests.Four_argument_action_definition_clr_constructor_and_deconstruct_are_preserved`).
   The optional-fifth-argument source claim in the old handoff is corrected (it does NOT preserve the CLR
   members). Previous false "no binary patch required" statement is retracted. Compiler metadata
   `TypeForwardedTo(CuiCompilationMetadata)` is verified as present and pointing at Core.

3. **Ordinal symbol identity** — all V2 resource/action/template/variable/component dictionaries now use
   `StringComparer.Ordinal` (matching `CuiRichParser`), so `Flag`/`flag` stay distinct. A case-distinct
   roundtrip/collision test was added.

4. **Strict typed validation before consumer use** — `CuiCompiledArtifactCodec.Validate` now rejects:
   null Styles/required collections, null/invalid source-map entries, unsupported dependency kinds
   (allowlist), missing value/component/style spans, empty selectors, invalid setters — before any
   reconstruction. Tests: null-Styles, unsupported dependency kind, missing span.

5. **Genuine V1 span roundtrip** — `CuiV1ModelReader.ReadSpan` reads coordinates from the nested `Span`
   object (with direct fallback) instead of always defaulting to (1,1). Test asserts real coordinates
   survive a V1 roundtrip. Stateless ABI fixture is honest (`1.0` fixture → `1` expectation corrected by
   pinning the compiler option to ABI `1`; production metadata is not altered for the test).

6. **Mixed project → genuine V1 stateless subset + V2 stateful subset** — `CompileCuiSourcesTask` splits
   `Stateful`/`Stateless` entries and emits `compilation.v2.cuic.json` (stateful subset) and
   `compilation.v1.cuic.json` (stateless subset), each embedded under its own resource identity. Dependency
   statefulness now propagates required ABI/capabilities: `PromoteStatefulMetadata` re-stamps every stateful
   document (local or promoted via import closure) to ABI2 + the union of required capabilities across the
   closure.

7. **Incremental stale cleanup** — ownership entries are created only for actual copy-eligible items
   (Content/None with `CopyToOutputDirectory`); never-copied EmbeddedResource/None/AvaloniaResource/
   CuiMarkup create no ownership/deletion. Each entry records the expected artifact hash; stale deletion is
   refused if the current file is no longer that owned artifact (preserves unrelated same-path files).
   Stale computation validates each output **and** publish root independently; never deletes source or
   outside-root paths.

8. **Spaces composer contract** — `ChatComposerCompiledTransportTests` now asserts the composer is absent
   from raw **and** from any genuine V1 bundle, while allowing stateless sibling V1 documents (per owner
   plan), instead of asserting global V1 absence.

## Concretely changed files (SHA-256)

| Path | SHA-256 |
|---|---|
| framework/CUI/Core/CuiV2Transport.cs | `3d515075499f0a247e1548052852a01d8d934b487f09473c063285b05a48ab75` |
| framework/CUI/Core/CuiCompiledArtifact.cs | `030a1365960fdb048399a0c1c8f848fe87dda8910e43e3b1c93926d45e8dc662` |
| framework/CUI/Compiler/CuiV1ArtifactCodec.cs | `5af0b4c8ffebe82ca107d2ebdd9af5962bbc2d8de9fb1fc67a2a64e2fba4a991` |
| framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs | `397bba4933f1fab81682bb80688dc6deb08fd6a6ca1864d24cfab55278b002a5` |
| framework/CUI/Compiler.Tests/CuiV2TransportCodecTests.cs | `fd719289e6308ab8405997ac3790a257569b982067c689a4036baf10d849e377` |
| framework/CUI/Compiler.Tests/CuiLocalStateAbiOutputTests.cs | `b10fb8972687fd82ab2deeabbd28c0f8131b44ecdac6c1923b480636732d25b9` |
| framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py | `e487b40043a29c8823a1e43b741876404ce5a05c0e11a769262952bd8cc3ff7f` |
| 9to1 Workspace/Spaces/UI/Chat/ChatComposerCuiDocument.cs | `2dcd512e6e518c9f872b738b44a2b1cd9c28559c04d2fbfa6b21c9b0fe71125e` |
| 9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs | `23132b18940df47b7084b79e4b358d6e0b9ed76a7b6d7199aeaf814bb0e86ba5` |
| work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md | `6325b3ef79435649fb24b5de96a84226489598c9b0fdbbfdb4438bc02ca3af2c` |

Untouched (retained from pass 1 / other lanes): `Compiler/CuiCompiler.cs`,
`Compiler/CuiV2Projection.cs`, `Compiler/CuiCompilationMetadata.TypeForwarder.cs`,
`Compiler/buildTransitive/CakeOS.Cui.targets`, `Spaces/HavenOS.Spaces.csproj`.

## Commands (all NOT RUN until the serialized .NET slot)

- Compiler tests:
  `/home/jacob/.local/bin/dotnet test framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj -m:1 --filter "FullyQualifiedName~CuiV2TransportCodecTests|FullyQualifiedName~CuiLocalStateAbiOutputTests"`
- Spaces consumer contract:
  `/home/jacob/.local/bin/dotnet test "9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj" -m:1 --filter "FullyQualifiedName~ChatComposerCompiledTransportTests"`
- Real MSBuild verifier:
  `python3 framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py --task-assembly framework/CUI/Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll --evidence /tmp/opencode/lane2-evidence --dotnet /home/jacob/.local/bin/dotnet`

## Intentionally dependent on lane1/root

- The `CuiActionDefinition` four-arg ctor + four-out Deconstruct patch must be applied to `Core/CuiDocument.cs`
  for the two binary-compat tests to pass. Lane 2 has authored the exact patch and the reflection test.

## Residual / not claimed

- Prefab-closure statefulness propagation (a document that only references a definition living in a stateful
  sibling, outside an `<Import>` edge) is not separately modeled in this pass; import closure is propagated,
  and the compilation already fails safely when a closure cannot be resolved. A causal Prefab fixture should
  be added under the build slot.
- Authoritative assembly-resource enumeration (real reflection) requires the allocated slot; the python
  verifier uses a static byte-scan heuristic and documents that.
- All compile/test results remain NOT RUN; no completion/compatibility claim is made from inspection alone.
