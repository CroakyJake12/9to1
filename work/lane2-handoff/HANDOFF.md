# Lane 2 handoff — typed CUI V2 transport & packaging (pass 5 final bounded)

- Date: 2026-10-20. Branch `codex/mercury-transport-20261020`. Worktree HEAD `fb70840d`.
- Authorized six Mercury workers; lane 2. No model subagents, no `.NET`/build/restore/app runs until the
  coordinator allocates the serialized slot (lane 1 only). All compile/tests here are **NOT RUN**.
- Authority: read `AGENTS.md`, canonical `docs/specification/9to1-development-specification-v2.md`,
  `work/pc-worker/ONE-APP-IMPLEMENTATION-AUDIT-2026-10-20.md`, the original transport plan
  `…/cui-v2-transport-packaging-plan-de33c168.md`, the pass-4 review
  `…/lane2-transport-pass4-review-f2b5.md`, and the frozen identity
  `…/parallel6-transport-pass4-frozen/identity.json`.
- Root's CoreDocument ctor/Deconstruct SHA `581a2638…` and the old-Core-reference binary probe PASS are
  applied/unchanged; actual old-Compiler binary forwarding remains **NOT PROVEN**.

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
   `Microsoft.NET.Publish.targets` (1013–1060): Never/empty disables default publish; explicit values
   must be real gather modes (Always/PreserveNewest/IfDifferent); default-inherit is gated on the
   effective `DefaultCopyToPublishDirectoryMetadata` switch (finalized in pass-5).
   Verifier updated: line 206 NeverCopy fix, copied-EmbeddedResource case, nonproduced same-byte publish
   survival, and all-ancestor containment Project 4.
4. **ByRef Deconstruct assertion fix** — `CuiLocalStateAbiOutputTests.cs` Deconstruct reflection now
   uses `typeof(...).MakeByRefType()`.

Root already applied the 4-arg ctor/4-out Deconstruct (SHA `581a2638…`) and the old-Core binary probe
passed; those are NOT reapplied. No .NET execution performed; all compile/tests remain NOT RUN.

## Pass-5 scope (final bounded, per pass-4 review)

Closed the four remaining pass-4 corrections on top of the pass-4 state; retained every useful typedV2/
genuineV1/raw-exclusion/strict-admission decision and the unchanged Runtime root patch:

1. **Valid sibling-fixture collections** — `ChatComposerCompiledTransportTests.cs` `StatelessDocument`
   now initializes optional `ElseChildren` and `TextParts` to explicit empty arrays of their actual typed
   element types (never null), so the strict codec (CUIR072) no longer rejects the fixtures before the
   intended ABI999/CUIR065 sibling, sibling-capability/CUIR067, and genuine ABI1+ABI2 success checks.
   Test renamed to `Whole_bundle_abi999_stateless_sibling_is_rejected_before_effects` (honest
   whole-bundle admission, not an import-closure edge).
2. **Artifact-byte preservation under rejected physical escape** — `verify_lane2_v2_packaging.py`
   captures the successful V1/V2 bytes, ownership manifest and source before the unsafe/alias attempts;
   `assert_physical_state_preserved` proves the prior successful state and no outside destination are
   preserved instead of wrongly demanding the artifacts be absent.
3. **Fail closed on link-resolution failures** — `CompileCuiSourcesTask.cs` `ResolveExistingLink` no
   longer swallows `ResolveLinkTarget` errors for EXISTING paths; a bounded IO/Argument error propagates
   and is rejected by `IsPhysicallyInside`/task `CUIB003`, while legitimate nonexistent trailing
   suffixes stay lexical. `PathComparison` (platform-aware ordinal/ordinal-ignore-case) now backs the
   canonical source/root/destination physical identity comparisons.
4. **Exact SDK publish eligibility + disabled-defaulting transport** — new task input
   `DefaultCopyToPublishDirectoryMetadata` (default `true`, the SDK default) is wired from the real
   `$(DefaultCopyToPublishDirectoryMetadata)` in `CakeOS.Cui.targets`. `IsPublishEligible` accepts only
   the SDK's real gather modes (`Always`/`PreserveNewest`/`IfDifferent`) for explicit values (a typo
   no longer acquires custody) and gates default-inherit on the effective defaulting switch. New real
   fixture `Lane2PublishDefaulting` proves disabled-defaulting does not default-publish output-copy
   Content/EmbeddedResource, explicit valid publish-only still publishes, explicit Never stays absent,
   an invalid mode is never owned, and a stateless→stateful transition without Clean removes a genuine
   owned stale publish output while same-byte nonproduced files survive.

Root applies the same 15 product/test files from the frozen manifest plus only the existing
`Runtime/CuiRuntimeCompatibility.cs` root patch (SHA `14515fcf…`).

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
   the SDK's real gather modes for explicit values and gates default-inherit on the effective
   `DefaultCopyToPublishDirectoryMetadata` switch (finalized in pass-5).
   Verifier line 206 fixed; copied-EmbeddedResource case; nonproduced same-byte publish survival; Project 4.
4. **ByRef Deconstruct** — `CuiLocalStateAbiOutputTests.cs` uses `typeof(...).MakeByRefType()`.

## Concretely changed files (pass-5 SHA-256)

The 15 product/test files in the frozen manifest (parallel6-transport-pass4-frozen/identity.json), with
pass-5 working-tree identity. Four changed in pass 5 (marked); the rest unchanged from the frozen state.

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

Unchanged Runtime API patch identity (root applies `work/lane2-handoff/Runtime/CuiRuntimeCompatibility.cs`):
SHA `14515fcf0c118c705a7fe65a9c4015e689db4766540ae026c4aca6f62f6bfcff`. Handoff docs:
`PATCH-Runtime-artifact-admission.md` `b125e881…`, `PATCH-CuiActionDefinition-four-arg.md` `6325b3ef…`.

## Commands (all NOT RUN until the serialized .NET slot)

- Compiler tests:
  `/home/jacob/.local/bin/dotnet test framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj -m:1 --filter "FullyQualifiedName~CuiV2TransportCodecTests|FullyQualifiedName~CuiLocalStateAbiOutputTests"`
- Spaces consumer contract:
  `/home/jacob/.local/bin/dotnet test "9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj" -m:1 --filter "FullyQualifiedName~ChatComposerCompiledTransportTests"`
- Real MSBuild verifier:
  `python3 framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py --task-assembly framework/CUI/Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll --evidence /tmp/opencode/lane2-evidence --dotnet /home/jacob/.local/bin/dotnet`

## Task/target seam (pass-5)

- Task property (new): `public bool DefaultCopyToPublishDirectoryMetadata { get; set; } = true;` on
  `CakeOS.Cui.Build.CompileCuiSourcesTask`. It mirrors the SDK's default-inherit switch and is used only
  when an item's `CopyToPublishDirectory` is ABSENT.
- Target wiring: `CakeOS.Cui.targets` sets
  `<DefaultCopyToPublishDirectoryMetadata Condition="'$(DefaultCopyToPublishDirectoryMetadata)' == ''">true</DefaultCopyToPublishDirectoryMetadata>`
  and passes `DefaultCopyToPublishDirectoryMetadata="$(DefaultCopyToPublishDirectoryMetadata)"` to the task
  invocation.
- Explicit-value allowlist mirrors the SDK gather modes (`Always`/`PreserveNewest`/`IfDifferent`); Never and
  any other value (including a typo) never create a custody entry. Default-inherit is gated on the
  effective defaulting switch AND a real output-copy mode (Content/None/EmbeddedResource).

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
- Open (not part of the pass-5 corrections): metadata converter lines 508–516 malformed kind/empty version;
  `ReadManifestStrict` null-entry dereference. Both are recorded for later, not in the pass-5 scope.
- Actual old-Compiler binary forwarding remains **NOT PROVEN**; no compilation/test-count/acceptance claim is
  made from source inspection.
