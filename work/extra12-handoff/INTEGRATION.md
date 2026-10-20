# extra12 handoff (pass 3) — typed genuine-V1 workbench consumer + shared reader/admission seam

Lane 12 (Mercury). Worktree `/workspace/9to1-mercury-extra12-build`, continuation base `167b4eb2`
(pass 2 frozen `7bf6d7c7` / receipt `parallel6-extra12-build-pass2-frozen/identity.json`). Owner
authorization: 16 Mercury workers, `$5` cap unchanged, root owns SDK/native — **all compile/tests are
NOT RUN** in this lane. This directory contains the exact root-integration patches for the shared typed
seam plus the owned Dev consumer/tests. No shared Core/Compiler/Runtime source was edited in this
worktree.

## What is preserved / corrected

- `DeveloperWorkbenchCuiDocument` genuinely consumes `CUI.Compilation.v1` via the Core typed reader then
  whole-bundle Runtime admission — no source parser/compiler fallback, no V2 forcing, no fake
  Variable/capability, no app C# markup, no app-private codec. The workbench stays genuinely stateless
  ABI1 with all 15 original actions; runtime advertises ABI2 but supports minimum stateless ABI1.
- Shared seam is now delivered as **complete applyable patches** (new Core file; replaced Compiler
  shim; a narrow Runtime overload hunk; a Compiler.Tests regression file).

## Pass-3 corrections applied

1. **Structural test mutations** (`DeveloperWorkbenchCompiledTransportTests.cs`): replaced fragile
   regex string-replacements with structural `JsonNode` edits of the ACTUAL embedded bytes,
   round-tripped through a real JSON writer. Every mutation proves the changed field via an assertion
   on the previous value and preserves **valid JSON** for unsupported-value cases (language/registry/
   ABI/capability), so those reach the admission boundary instead of being masked as malformed JSON.
2. **Core reader structural admission** (`Core/CuiV1Transport.cs`): now requires the envelope metadata,
   a non-empty documents collection, unique non-empty per-document source/hash identity, per-document
   metadata (**never inherited** from the envelope), and the full required compiled projection
   (Components/Templates/SourceMap/Dependencies/Semantics) present and correctly shaped — before any
   typed document is returned. Unknown optional fields and capability values remain tolerated here
   (baseline contract); host support is decided at the Runtime boundary.
3. **No exception leaks**: the deep reader now uses checked accessors (`RequireObject/RequireArray/
   RequireNonEmptyString`) so wrong kinds, missing nested values and incomplete metadata translate to
   `CuiV1AdmissionException` (CUIR072/CUIR070/CUIR060/CUIR061/CUIB003), never
   `InvalidOperationException`/`KeyNotFoundException`/`ArgumentException`.
4. **Compiler shim API preserved** (`Compiler/CuiV1ArtifactCodec.cs`): the public nested
   `CuiV1ArtifactCodec.CuiV1LoadedDocument`/`CuiV1LoadedArtifact` records and the public
   `ReadArtifact(byte[])` return type are restored; the shim is a thin mapping adapter over the single
   Core decoder and keeps the historical `InvalidDataException` producer/test contract. The existing
   `CuiV2TransportCodecTests` V1 baseline (omitted/unknown capability tolerance) and V2/local-state/
   closure tests are unaffected.
5. **Runtime change is a narrow overload** (`Runtime/CuiV1ArtifactHostSupport.patch`): a single added
   `ValidateArtifactHostSupport(CuiV1LoadedArtifact, CuiSourceSpan)` overload that reuses the existing
   lane-2 private helpers/constants — preserving query and V2/local-state admission. Not a whole-file
   replacement.

## Exact API (post-integration)

```csharp
// Core (new file framework/CUI/Core/CuiV1Transport.cs)
namespace CakeOS.Cui {
    public sealed record CuiV1LoadedArtifact(string Format, int FormatVersion, string CompilerSha256,
        CuiCompilationMetadata Metadata, IReadOnlyList<CuiV1LoadedDocument> Documents);
    public sealed record CuiV1LoadedDocument(string Source, string Sha256,
        CuiCompilationMetadata Metadata, CuiDocument Semantics);
    public sealed class CuiV1AdmissionException : Exception { public string Code { get; } }
    public static class CuiV1ArtifactReader {
        public const string FormatName = "cui-build-compilation";
        public const int FormatVersion = 1;
        public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8);
    }
}

// Compiler (replace framework/CUI/Compiler/CuiV1ArtifactCodec.cs — writer + thin shim)
namespace CakeOS.Cui.Compiler {
    public static partial class CuiV1ArtifactCodec {   // (not partial; writer + shim)
        public sealed record CuiV1LoadedDocument(string Source, string Sha256,
            CuiCompilationMetadata Metadata, CuiDocument Semantics);
        public sealed record CuiV1LoadedArtifact(string Format, int FormatVersion, string CompilerSha256,
            CuiCompilationMetadata Metadata, IReadOnlyList<CuiV1LoadedDocument> Documents);
        public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8);  // maps Core -> nested, InvalidDataException
        public static byte[] WriteBundle(IReadOnlyList<CuiV1Compiled> documents, string compilerSha256);
    }
}

// Runtime (narrow overload added to framework/CUI/Runtime/CuiRuntimeCompatibility.cs)
namespace CakeOS.Cui.Runtime {
    public static class CuiRuntimeCompatibility {
        public static void ValidateArtifactHostSupport(CuiV1LoadedArtifact artifact, CuiSourceSpan diagnosticSpan);
    }
}
```

### Diagnostic contract (strict production vs historical Compiler compatibility)
- **Core reader (production structural admission)**: CUIR060 (format), CUIR061 (version),
  CUIB003 (missing envelope), CUIR070 (malformed / unknown value discriminator), CUIR072 (missing/
  wrong/duplicate required structure: documents, identity, per-document metadata, required compiled
  collections, shapes).
- **Runtime host admission** (`ValidateArtifactHostSupport`): CUIR064 (language), CUIR066 (registry),
  CUIR065 (ABI range), CUIR067 (capability). Genuine stateless ABI1 is admitted on the stateful-aware
  ABI2 runtime. These run for the whole bundle (envelope + every required sibling) before any effect.
- **Historical Compiler shim**: tolerant baseline — returns the public nested types and throws
  `InvalidDataException`; it does not perform host-support/aggregate decisions. Existing
  `CuiV2TransportCodecTests` compatibility tests (omitted `RequiredCapabilities`, unknown capability
  values, V2-to-V1 rejection) remain intact.

## Apply order (root) — with preimage hashes

1. **Runtime (requires lane-2 Runtime patch first)**: apply `Runtime/CuiV1ArtifactHostSupport.patch`
   to `framework/CUI/Runtime/CuiRuntimeCompatibility.cs`.
   - Preimage = the lane-2-integrated `CuiRuntimeCompatibility.cs` (V2 `ValidateArtifactHostSupport` +
     `RequireLanguage/RequireRegistry/RequireAbi/RequireCapabilities/IsAbiSupported` + `MinStatelessAbi=1`/
     `CurrentAbi=2`), SHA-256 `14515fcf0c118c705a7fe65a9c4015e689db4766540ae026c4aca6f62f6bfcff`.
   - The patch only inserts the V1 overload (narrow hunk), preserving query and V2/local-state admission.
2. **Core**: add `Core/CuiV1Transport.cs` → `framework/CUI/Core/CuiV1Transport.cs` (new file;
   auto-included by the SDK-style csproj). No preimage (new).
3. **Compiler**: replace `framework/CUI/Compiler/CuiV1ArtifactCodec.cs` with `Compiler/CuiV1ArtifactCodec.cs`.
   - Preimage = frozen integrated Compiler V1 codec (same at `60289daa` and `dee4d59b`), SHA-256
     `b344b21a5b885ab5e7f5d6beb18741bd4ce2df0ffcbd6a19e80cb9f337b92281`.
4. **Compiler.Tests regression**: add `Compiler.Tests/CuiV1ReaderShimRegressionTests.cs` →
   `framework/CUI/Compiler.Tests/CuiV1ReaderShimRegressionTests.cs` (new file; auto-included).
5. **Dev consumer/project/tests** are in the frozen extra12 Dev scope; no further edit needed.

## Verification commands (root executes, after integration + SDK slot)

```bash
# Dev transport + original document tests (18 new + 2 original Facts)
dotnet test '9to1 Workspace/Dev/Tests/HavenOS.Dev.Tests.csproj' -c Release --no-restore -m:1 \
  -nodeReuse:false -p:CuiUsePrebuiltCompiler=true -p:CuiCompilerTaskAssembly=<actual-root-built-Release-task.dll> \
  --filter 'FullyQualifiedName~DeveloperWorkbenchCompiledTransportTests|FullyQualifiedName~DeveloperWorkbenchCuiDocumentTests'

# Shared reader/shim regressions (Compiler.Tests)
dotnet test framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj -c Release --no-restore -m:1 \
  -nodeReuse:false --filter 'FullyQualifiedName~CuiV1ReaderShimRegressionTests'
```

## Dev test registration (HavenOS.Dev.Tests) — 18 new + 2 original `[Fact]`s

New (this file): `Embedded_genuine_v1_bundle_decodes_to_typed_semantics_with_all_authored_actions`,
`Load_returns_typed_document_that_preserves_every_existing_action`,
`Minimum_abi1_stateless_bundle_is_admitted_on_stateful_aware_runtime2`,
`Admission_rejects_malformed_artifact_before_effects`,
`Admission_rejects_unknown_artifact_format_before_effects`,
`Admission_rejects_unsupported_format_version_before_effects`,
`Admission_rejects_missing_workbench_document_before_effects`,
`Admission_rejects_missing_per_document_metadata_before_effects`,
`Admission_rejects_missing_source_identity_before_effects`,
`Admission_rejects_missing_required_compilation_collection_before_effects`,
`Admission_rejects_wrong_required_shape_before_effects`,
`Admission_rejects_duplicate_source_identity_before_effects`,
`Admission_rejects_unsupported_language_version_before_effects`,
`Admission_rejects_unsupported_registry_version_before_effects`,
`Admission_rejects_contradictory_document_abi_before_effects`,
`Admission_rejects_consistently_unsupported_abi_before_effects`,
`Admission_rejects_unknown_required_capability_before_effects`,
`Admission_rejects_unsupported_capability_on_required_sibling_before_effects`.

Original (unchanged): `DeveloperWorkbenchCuiDocumentTests` (2 Facts).

Shared reader/shim regressions (`CuiV1ReaderShimRegressionTests`, Compiler.Tests): 9 Facts covering
shim public nested API + mapping, Core/shim agreement, missing per-document metadata, missing source
identity, duplicate source, missing required collection, wrong shape, `InvalidDataException` mapping,
and unknown-capability tolerance.

## Notes
- No `.NET`/build/restore/test runs were performed in this lane (root owns the serialized slot). All
  execution remains **NOT RUN**; no Build acceptance is claimed.
- The Compiler's public writer contract is intact; only the reader moved below the compiler and is
  reused via the thin shim. The Dev consumer has no production Compiler dependency.
