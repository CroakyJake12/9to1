# Lane 2 handoff — typed CUI V2 transport & packaging (pass 4)

- Date: 2026-10-20. Branch `codex/mercury-transport-20261020`. Worktree HEAD `fb70840d`.
- Authorized six Mercury workers; lane 2. No model subagents, no `.NET`/build/restore/app runs until the
  coordinator allocates the serialized slot (lane 1 only). All compile/tests here are **NOT RUN**.
- Authority: read `AGENTS.md`, canonical `docs/specification/9to1-development-specification-v2.md`,
  `work/pc-worker/ONE-APP-IMPLEMENTATION-AUDIT-2026-10-20.md`, the original transport plan
  `…/cui-v2-transport-packaging-plan-de33c168.md`, the frozen pass-2 review
  `…/lane2-transport-pass2-review-88575ef0.md`, and the pass-3 review
  `…/lane2-transport-pass3-review-a67a.md`.

## Pass-4 scope

Final bounded source/test corrections on top of pass 3 (dirty HEAD `fb70840d`), per the pass-3 review:

1. **Missing resource-span typed admission** — `CuiCompiledArtifact.cs` resource loop now rejects a
   null `resource.Span` (CUIR072) before use; new `CuiV2TransportCodecTests.Missing_resource_span_is_rejected`.
2. **Real Runtime ABI/capability whole-bundle admission API** — new
   `CuiRuntimeCompatibility.ValidateArtifactHostSupport(CuiCompiledArtifact, CuiSourceSpan)` performs
   actual host support (language CUIR064, registry CUIR066, ABI range CUIR065, capabilities CUIR067;
   CUIB003 for missing artifact) for the entire bundle before effects, superseding the earlier constant
   `a67a` hunk. Root-owned `Runtime/CuiRuntimeCompatibility.cs` is supplied as the exact handoff patch
   in `work/lane2-handoff/Runtime/CuiRuntimeCompatibility.cs` + `PATCH-Runtime-artifact-admission.md`.
   Consumer `ChatComposerCuiDocument.Load(byte[])` calls it right after codec decode; consumer tests split
   into unsupported-language (CUIR064), language-contradiction (CUIR075), registry (CUIR066), sibling
   ABI999 (CUIR065), sibling-only unknown capability (CUIR067), and genuine ABI1-sibling+ABI2-composer.
3. **All-ancestor physical containment + SDK-correct publish eligibility** — `CompileCuiSourcesTask.cs`:
   `IsPhysicallyInside` resolves ALL existing ancestors (`ResolvePhysicalPath`/`ResolveExistingLink`);
   `ShippedDestination` adds physical source-alias rejection; `IsPublishEligible` matches real
   `Microsoft.NET.Publish.targets` (1013–1060): Never/empty disables default publish, explicit non-empty
   mode→true, else default-inherit = `IsOutputCopyEligible` across Content/None/EmbeddedResource.
   Verifier updated: line 206 NeverCopy fix, copied-EmbeddedResource case, nonproduced same-byte publish
   survival, and all-ancestor containment Project 4.
4. **ByRef Deconstruct assertion fix** — `CuiLocalStateAbiOutputTests.cs` Deconstruct reflection now
   uses `typeof(...).MakeByRefType()`.

Root already applied the 4-arg ctor/4-out Deconstruct (SHA `581a2638…`) and the old-Core binary probe
passed; those are NOT reapplied. No .NET execution performed; all compile/tests remain NOT RUN.

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

## Findings → resolution (pass 4, on top of pass 3)

1. **Resource-span typed admission** — `CuiCompiledArtifact.cs` rejects null `resource.Span` (CUIR072)
   in the resource loop; `CuiV2TransportCodecTests.Missing_resource_span_is_rejected` added.
2. **Real whole-bundle Runtime admission** — `CuiRuntimeCompatibility.ValidateArtifactHostSupport`
   (language CUIR064 / registry CUIR066 / ABI range CUIR065 / capabilities CUIR067 / CUIB003) checks
   the entire artifact (envelope + every required document/closure), closing the stateless-sibling and
   sibling-only-capability escapes. Supersedes the `a67a` constant hunk. Consumer `Load(byte[])` calls
   it immediately after codec decode; consumer language/registry/sibling tests added.
3. **Containment + publish eligibility** — `CompileCuiSourcesTask.cs`: `IsPhysicallyInside` resolves all
   existing ancestors; `ShippedDestination` rejects physical source-alias; `IsPublishEligible` mirrors
   `Microsoft.NET.Publish.targets` (1013–1060) defaults (Never/empty disables default publish; explicit
   non-empty→true; else default-inherit = `IsOutputCopyEligible` across Content/None/EmbeddedResource).
   Verifier line 206 fixed; copied-EmbeddedResource case; nonproduced same-byte publish survival; Project 4.
4. **ByRef Deconstruct** — `CuiLocalStateAbiOutputTests.cs` uses `typeof(...).MakeByRefType()`.

## Concretely changed files (pass-4 SHA-256)

| Path | SHA-256 |
|---|---|
| framework/CUI/Core/CuiCompiledArtifact.cs | `57c0310f80d108b1404cedce10661ba3314e741418a410a0620a3def2480378c` |
| framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs | `9d482ffefbb89cd6981d942aa1e615d832af3d1a30dfae0ee3fd794207c43f43` |
| framework/CUI/Compiler.Tests/CuiLocalStateAbiOutputTests.cs | `22131f397eaffbbd2466d0a167fe97f963d40b7fad9e71805a005b0005e792a1` |
| framework/CUI/Compiler.Tests/CuiV2TransportCodecTests.cs | `60cf3ba446b619b4c0f2b1536a3e27c858323c52d22411553a04823387cffe34` |
| framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py | `04684374d31f83369ebaa2a69410138fcb697ed8e8f06383763499f71a87aa8d` |
| 9to1 Workspace/Spaces/UI/Chat/ChatComposerCuiDocument.cs | `0c1c9efacd48a363f132a64399795c6ff2593b06e7b5bae61204ae3f535e4c52` |
| 9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs | `3214f44c1441dcb7ebf647ada1b94fd14da2d7c6828ed1732a76b43bcaebe52d` |
| 9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj | `03854bab71b44275b46973d818c94c4e67b59f3e32880221a366575771cbb84f` (unchanged) |
| work/lane2-handoff/Runtime/CuiRuntimeCompatibility.cs | `14515fcf0c118c705a7fe65a9c4015e689db4766540ae026c4aca6f62f6bfcff` |
| work/lane2-handoff/PATCH-Runtime-artifact-admission.md | `b125e881fc85f393a6ffe0a8718e5aaefd23bd0c2aeb4ae71fb3754c497a80a0` |
| work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md | `6325b3ef79435649fb24b5de96a84226489598c9b0fdbbfdb4438bc02ca3af2c` (unchanged) |

Pass-3 SHA-256 entries (above) are superseded by the pass-4 rows. Unchanged from pass 2/3:
`CuiV2Transport.cs`, `CuiV1ArtifactCodec.cs`, `CuiV2Projection.cs`, `CuiCompiler.cs`,
`CuiCompilationMetadata.TypeForwarder.cs`, `buildTransitive/CakeOS.Cui.targets`,
`Spaces/HavenOS.Spaces.csproj`, `PATCH-Runtime-Abi2-admission.md`.

## Commands (all NOT RUN until the serialized .NET slot)

- Compiler tests:
  `/home/jacob/.local/bin/dotnet test framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj -m:1 --filter "FullyQualifiedName~CuiV2TransportCodecTests|FullyQualifiedName~CuiLocalStateAbiOutputTests"`
- Spaces consumer contract:
  `/home/jacob/.local/bin/dotnet test "9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj" -m:1 --filter "FullyQualifiedName~ChatComposerCompiledTransportTests"`
- Real MSBuild verifier:
  `python3 framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py --task-assembly framework/CUI/Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll --evidence /tmp/opencode/lane2-evidence --dotnet /home/jacob/.local/bin/dotnet`

## Intentionally dependent on lane1/root

- `PATCH-CuiActionDefinition-four-arg.md` — **already applied by root** (SHA `581a2638…`); old-Core
  binary probe passed. Do not reapply.
- `PATCH-Runtime-artifact-admission.md` (replaces the superseded `PATCH-Runtime-Abi2-admission.md`) —
  apply to `Runtime/CuiRuntimeCompatibility.cs` (preimage SHA `23d0e130…`) so the runtime performs real
  whole-bundle host admission. Until applied, the consumer (`ChatComposerCuiDocument`) and the new
  consumer tests referencing `ValidateArtifactHostSupport` do not compile → those tests are **NOT RUN**.
- `PATCH-Runtime-Abi2-admission.md` — superseded by the artifact-admission patch (kept for provenance).

## Residual / not claimed

- All compile/test results remain **NOT RUN**; no completion/compatibility claim is made from inspection
  alone. Runtime-patch-dependent tests (consumer + `ValidateArtifactHostSupport` consumers) are NOT RUN
  until root applies the patch and allocates the slot.
- Open (not part of the four corrections): metadata converter lines 508–516 malformed kind/empty version;
  `ReadManifestStrict` null-entry dereference. Both are recorded for later, not in the pass-4 scope.
