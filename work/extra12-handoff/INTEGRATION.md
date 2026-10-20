# extra12 handoff — typed genuine-V1 workbench consumer + shared reader/admission seam

Lane 12 (Mercury). Worktree `/workspace/9to1-mercury-extra12-build`, HEAD `ddb2a8b8`, continuation base
`167b4eb2` (4 owned Dev files). Owner authorization: 16 Mercury workers, `$5` cap unchanged, root owns
SDK/native. **All compile/tests are NOT RUN** in this lane. This directory contains the exact root
integration patch for the shared typed seam plus the owned Dev consumer/tests.

## What changed

### Owned direct-edit scope (`9to1 Workspace/Dev/**`)
- `DeveloperWorkbenchCuiDocument.cs` — consumer rewritten to load the **genuine V1** compiled artifact
  (`CUI.Compilation.v1`) through the typed Core reader `CuiV1ArtifactReader`, then whole-bundle host
  support via `CuiRuntimeCompatibility.ValidateArtifactHostSupport(CuiV1LoadedArtifact, CuiSourceSpan)`.
  No runtime source reparse, no compiler invocation, no source/snapshot DTO, no parallel codec.
- `HavenOS.Dev.csproj` — imports the canonical CUI props/targets (same as Spaces) and references Core +
  Runtime; embeds `UI/DeveloperWorkbench.cui` as the compiler source.
- `UI/DeveloperWorkbench.cui` — the extracted authored CUI source (the workbench is stateless, so the
  integrated Build.Tasks emitter produces the genuine V1 bundle).
- `Tests/DeveloperWorkbenchCompiledTransportTests.cs` — typed-semantics/action round-trip on the actual
  embedded artifact, min-ABI1 positive + current-Runtime2 supported-host case, and malformed/missing/
  unknown-version/language/ABI (contradictory + consistently unsupported)/capability rejections.
- `Tests/DeveloperWorkbenchCuiDocumentTests.cs` — unchanged (original regression test).

### Shared seam (exact applyable patches, authored here — NOT applied in this worktree)
These are the smallest typed transport additions. No new CUI syntax, no generic runtime rewrite, no
duplicate divergent decoder. Core/Runtime/Compiler shared source remains read-only in this worktree.

1. `Core/CuiV1Transport.cs` → **new file** `framework/CUI/Core/CuiV1Transport.cs` (namespace
   `CakeOS.Cui`). Extracts the genuine V1 **reader + loaded records** below the compiler:
   - `public sealed record CuiV1LoadedDocument(string Source, string Sha256, CuiCompilationMetadata Metadata, CuiDocument Semantics)`
   - `public sealed record CuiV1LoadedArtifact(string Format, int FormatVersion, string CompilerSha256, CuiCompilationMetadata Metadata, IReadOnlyList<CuiV1LoadedDocument> Documents)`
   - `public sealed class CuiV1AdmissionException(string code, string message) : Exception` with `public string Code { get; }`
   - `public static class CuiV1ArtifactReader` with `public const string FormatName = "cui-build-compilation"`,
     `public const int FormatVersion = 1`, and `public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8)`.
     It reconstructs the canonical typed model from the wire payload without parsing source or invoking
     the compiler. Admission failures throw `CuiV1AdmissionException` with stable codes: CUIR060 (format),
     CUIR061 (version), CUIB003 (missing envelope), CUIR072 (missing doc payload/metadata/semantics),
     CUIR070 (malformed / unknown discriminator).

2. `Compiler/CuiV1ArtifactCodec.cs` → **replace** `framework/CUI/Compiler/CuiV1ArtifactCodec.cs`
   (writer + public compatibility shim). Keeps the writer (`CuiV1Artifact`, `CuiV1Document`,
   `CuiV1Compiled`, `WriteBundle`, `AggregateMetadata`, `WriteOptions`) and the constant/read shim:
   - `public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8)` delegates to `CuiV1ArtifactReader`
     and translates `CuiV1AdmissionException` → `InvalidDataException` (preserves existing producer/test
     contract, e.g. `CuiV2TransportCodecTests` `Assert.Throws<InvalidDataException>`). No duplicate decoder.

3. `Runtime/CuiV1ArtifactHostSupport.cs` → **method to insert** into `CuiRuntimeCompatibility.cs` (the
   genuine-V1 whole-bundle admission entry), reusing the lane-2 runtime helpers/constants.

## Exact API (post-integration)

```csharp
// Core
namespace CakeOS.Cui {
    public sealed record CuiV1LoadedArtifact(...);
    public sealed record CuiV1LoadedDocument(...);
    public sealed class CuiV1AdmissionException : Exception { public string Code { get; } }
    public static class CuiV1ArtifactReader {
        public const string FormatName = "cui-build-compilation";
        public const int FormatVersion = 1;
        public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8);
    }
}

// Runtime (addition)
namespace CakeOS.Cui.Runtime {
    public static partial class CuiRuntimeCompatibility {
        public static void ValidateArtifactHostSupport(CuiV1LoadedArtifact artifact, CuiSourceSpan diagnosticSpan);
    }
}
```

`CuiRuntimeCompatibility.ValidateArtifactHostSupport(CuiV1LoadedArtifact, CuiSourceSpan)` checks the
envelope and every loaded document's language (CUIR064), component registry (CUIR066), ABI range
(CUIR065, valid `[MinStatelessAbi=1, CurrentAbi=2]`) and required capabilities (CUIR067) before any
reconstruction/effect. Genuine stateless ABI1 is admitted on the stateful-aware ABI2 runtime.

## Apply order (root)

1. Apply lane-2 Runtime patch `work/lane2-handoff/Runtime/CuiRuntimeCompatibility.cs` (provides V2
   `ValidateArtifactHostSupport`, private helpers `RequireLanguage/RequireRegistry/RequireAbi/
   RequireCapabilities/IsAbiSupported`, and `MinStatelessAbi=1`/`CurrentAbi=2`/`RuntimeAbiVersion="2"`).
2. Add `framework/CUI/Core/CuiV1Transport.cs` (file `Core/CuiV1Transport.cs` here).
3. Replace `framework/CUI/Compiler/CuiV1ArtifactCodec.cs` with `Compiler/CuiV1ArtifactCodec.cs` here.
4. Insert the `CuiV1ArtifactHostSupport` method into `CuiRuntimeCompatibility.cs`
   (`Runtime/CuiV1ArtifactHostSupport.cs` here) after the V2 method.
5. The owned Dev files are in the frozen extra12 commit; no further edit needed.

## Verification command (root, after integration + SDK slot)

```bash
dotnet test '9to1 Workspace/Dev/Tests/HavenOS.Dev.Tests.csproj' -c Release --no-restore -m:1 \
  -nodeReuse:false -p:CuiUsePrebuiltCompiler=true -p:CuiCompilerTaskAssembly=<actual-root-built-Release-task.dll> \
  --filter 'FullyQualifiedName~DeveloperWorkbenchCompiledTransportTests|FullyQualifiedName~DeveloperWorkbenchCuiDocumentTests'
```

## Notes
- The Dev workbench is stateless (no document-root variables / local-target actions), so the integrated
  Build.Tasks emitter produces a genuine V1 bundle under `CUI.Compilation.v1`; `DeveloperWorkbenchCuiDocument`
  reads exactly that resource.
- The shared seam is a typed transport gap, not new CUI syntax and not a runtime rewrite. The Compiler's
  public writer contract is intact; only the reader moved below the compiler and is reused via a thin shim.
- No .NET/build/restore/test runs were performed in this lane (root owns the serialized slot). All tests
  remain NOT RUN.
