# Lane 2 handoff — typed CUI V2 transport & packaging (pass 3)

- Date: 2026-10-20. Branch `codex/mercury-transport-20261020`. Worktree HEAD `fb70840d`.
- Authorized six Mercury workers; lane 2. No model subagents, no `.NET`/build/restore/app runs until the
  coordinator allocates the serialized slot (lane 1 only). All compile/tests here are **NOT RUN**.
- Authority: read `AGENTS.md`, canonical `docs/specification/9to1-development-specification-v2.md`,
  `work/pc-worker/ONE-APP-IMPLEMENTATION-AUDIT-2026-10-20.md`, the original transport plan
  `…/cui-v2-transport-packaging-plan-de33c168.md`, and the frozen pass-2 review
  `…/lane2-transport-pass2-review-88575ef0.md`.

## Pass-3 scope

Resolve every remaining primary defect in the frozen pass-2 review:

1. **Total strict V2 admission** (spans, identity, duplicate JSON keys, derived capabilities):
   `CuiCompiledArtifact.cs` now (a) rejects duplicate exact JSON object keys before deserialization
   collapses them; (b) validates document root span, dependency entries (null-rejected), import/
   action/variable/template/component/condition/repeat/list/region/action-ref/text-part/source-map
   spans; (c) enforces identity-key agreement for actions/variables/resources/templates; and
   (d) derives mandatory capabilities from the typed semantics (`local-state-toggle`,
   `local-state-random-select`) so a stripped capability string cannot downgrade a stateful document
   (CUIR076), plus envelope/document language & registry equality (CUIR075).
2. **Minimum stateless ABI separated from current runtime ABI**: `CuiCompiledArtifactCodec` now
   exposes `V1RuntimeAbiVersion` (`"1"`) and `V2RuntimeAbiVersion` (`"2"`); the Build.Tasks emitter
   seeds stateless documents with `V1RuntimeAbiVersion` (decoupled from the runtime's advertised
   ABI), and `PromoteStatefulMetadata` stamps only stateful documents with ABI2. The precise Runtime
   seam is supplied as a root/lane1 patch (below).
3. **Correct output/publish ownership per real MSBuild eligibility**:
   `CompileCuiSourcesTask.cs` models output-copy (Content/None/EmbeddedResource with a real mode —
   Always/PreserveNewest/IfDifferent; `Never`/empty excluded) and publish (`CopyToPublishDirectory`
   defaults: Content/None publish; embedded types don't; `Never` excluded) independently; only
   actually-deleted destinations go to `FileWrites`; `S`OwnedOutputs` now contains only deleted
   destinations.
4. **Fail-closed path/ownership validation before publish/cleanup**: `ShippedDestination` throws on
   absolute/escaping/link-junction-escaping `TargetPath` and on source-alias overlap;
   `ValidateOwnedRoots` rejects output/publish roots overlapping the project root;
   `ReadManifestStrict` throws on corrupt/ambiguous custody manifests; artifacts are staged and
   published only after all path/ownership validation succeeds; stale deletion refuses when current
   bytes no longer match the owned artifact.
5. **Real Python fixture repaired/expanded** (`verify_lane2_v2_packaging.py`): lowercase V1 keys,
   all five item types, authoritative C# resource-enumeration probe, custom publish dir, Never/
   publish-only, unrelated same-path bytes, unsafe TargetPath expected-fail, transitive import
   closure, failed-build preservation.
6. **Forwarding evidence fixed** (`CuiLocalStateAbiOutputTests.cs`): `Assembly.GetForwardedTypes()`
   plus `Type.GetType("CakeOS.Cui.Compiler.CuiCompilationMetadata, CakeOS.Cui.Compiler")` resolving to
   the Core assembly, not a `TypeForwardedToAttribute` enumeration.
7. **Bounded V1 baseline reader** (`CuiV2TransportCodecTests.cs`): an unchanged baseline consumer that
   ignores capabilities (tolerates omitted/unknown capabilities) — added alongside the span/shape
   tests.

## Findings → resolution (pass 3)

1. **Strict V2 admission** — hardened `CuiCompiledArtifact.cs` with duplicate-JSON-key rejection,
   span/identity validation, and derived-capability enforcement. New codec tests cover null
   dependency entries, null root/action/import spans, identity-key mismatch, envelope/document
   language mismatch, stripped-required-capability rejection, and duplicate exact JSON keys.
2. **Output/publish ownership** — `CompileCuiSourcesTask.cs` rewritten around real MSBuild eligibility
   (modelled copy modes; publish defaults; publish-only tracking; stage/validate-then-publish; only
   actually-deleted destinations to `FileWrites`; hash-guarded stale deletion).
3. **Path custody** — `ShippedDestination`/`ValidateOwnedRoots`/`ReadManifestStrict` fail closed;
   unsafe TargetPath expected-fail is covered by the verifier.
4. **ABI decoupling** — `V1RuntimeAbiVersion` added; emitter seeds stateless with it; Runtime
   ABI2/feature seam supplied as `work/lane2-handoff/PATCH-Runtime-Abi2-admission.md`.
5. **Repaired packaging fixture** — Python verifier rewritten (see TESTS.md).
6. **Forwarding evidence** — real type resolution tests replace attribute enumeration.
7. **V1 baseline reader** — bounded unchanged-consumer test added.

## Concretely changed files (pass-3 SHA-256)

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

Unchanged from pass 2: `CuiV2Transport.cs`, `CuiV1ArtifactCodec.cs`, `CuiV2Projection.cs`,
`CuiCompiler.cs`, `CuiCompilationMetadata.TypeForwarder.cs`, `buildTransitive/CakeOS.Cui.targets`,
`Spaces/HavenOS.Spaces.csproj`, `Spaces/UI/Chat/ChatComposerCuiDocument.cs`.

## Commands (all NOT RUN until the serialized .NET slot)

- Compiler tests:
  `/home/jacob/.local/bin/dotnet test framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj -m:1 --filter "FullyQualifiedName~CuiV2TransportCodecTests|FullyQualifiedName~CuiLocalStateAbiOutputTests"`
- Spaces consumer contract:
  `/home/jacob/.local/bin/dotnet test "9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj" -m:1 --filter "FullyQualifiedName~ChatComposerCompiledTransportTests"`
- Real MSBuild verifier:
  `python3 framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py --task-assembly framework/CUI/Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll --evidence /tmp/opencode/lane2-evidence --dotnet /home/jacob/.local/bin/dotnet`

## Intentionally dependent on lane1/root

- `PATCH-CuiActionDefinition-four-arg.md` — apply to `Core/CuiDocument.cs` so the two binary-compat
  tests pass.
- `PATCH-Runtime-Abi2-admission.md` — apply to `Runtime/CuiRuntimeCompatibility.cs` so the runtime
  advertises the stateful ABI2 while min-stateless ABI1 stays decoupled.

## Residual / not claimed

- Authoritative assembly-resource enumeration and all compiler/runtime test executions require the
  allocated .NET slot; the verifier authors a real C# probe and the tests are authored by inspection
  only.
- A true "unchanged binary consumer" fixture (a separately compiled old-reference assembly) still
  requires a build slot; the forwarding evidence uses real `GetForwardedTypes`/`Type.GetType`
  resolution of the historical assembly-qualified name, which is the strongest static proof available
  here.
- All compile/test results remain NOT RUN; no completion/compatibility claim is made from inspection alone.
