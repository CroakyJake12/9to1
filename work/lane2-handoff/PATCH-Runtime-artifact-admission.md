# Lane 2 → lane 1/root integration: REAL Runtime ABI/capability admission (pass 4)

`Runtime/CuiRuntimeCompatibility.cs` is owned by lane 1/root. Lane 2 does not edit it. This is the
**exact integration patch** (new API + constants) root must apply so the runtime performs ACTUAL
host-support admission for the entire compiled artifact before effects — not merely advertise constant
ABI 2.

## Why this exact seam exists

- The codec (`CuiCompiledArtifactCodec.Validate`) proves **internal** envelope/document consistency
  (max/union equality), not whether this host supports the required language/registry/ABI/capabilities.
- The previous consumer admission checked only the **selected composer document** metadata. A
  stateless sibling with `ABI=999` (with a matching envelope max) or a sibling-only unknown capability
  (with a matching envelope union) escaped selected-document checks.
- This patch adds `ValidateArtifactHostSupport`, which checks the **whole bundle** (envelope plus
  every required document/closure) against the runtime's real supported language, component registry,
  minimum/current ABI range and capability set, before `ToCuiDocument` / scene/control effects.

## Apply order

1. Apply **this** patch to `framework/CUI/Runtime/CuiRuntimeCompatibility.cs`.
   - Preimage SHA-256: `23d0e13059d8e1a02166022d00fc949e53b46e309c24eaa7b2773da221d96bdd`.
   - The exact final file content is in `work/lane2-handoff/Runtime/CuiRuntimeCompatibility.cs`.
   - This supersedes the earlier constant-only `a67a` hunk (it includes `MinStatelessAbiVersion="1"`
     and `RuntimeAbiVersion="2"` plus the new API). If `a67a` was already applied, replace the file
     with this final content (the net additions are: int constants `MinStatelessAbi`/`CurrentAbi`,
     the `ValidateArtifactHostSupport` method, and its private helpers).
2. The shared producer (lane-2-owned `CompileCuiSourcesTask.cs`) already seeds stateless documents with
   `CuiCompiledArtifactCodec.V1RuntimeAbiVersion` and promotes stateful ones to
   `V2RuntimeAbiVersion`; it is decoupled from the runtime's advertised ABI and needs no change.
3. The real Spaces consumer (`ChatComposerCuiDocument.Load(byte[])`) now calls this API immediately
   after codec decode, before locating/reconstructing the composer document (lane-2 source change,
   already present in the lane-2 return). No circular Compiler dependency: the API lives in Runtime and
   references only Core (`CuiCompiledArtifact`) and Runtime types.

## API signature

```csharp
namespace CakeOS.Cui.Runtime;

public static class CuiRuntimeCompatibility
{
    // … existing/constant additions (see full file) …

    /// <summary>Admits a decoded compiled artifact against the REAL host-support contract
    /// (language, component registry, minimum/current ABI range, capability set) for the whole
    /// bundle before any reconstruction/effect. Throws CuiRuntimeLoadException with a stable code.</summary>
    public static void ValidateArtifactHostSupport(CuiCompiledArtifact artifact, CuiSourceSpan diagnosticSpan);
}
```

### Required using directives (already in the final file)

```csharp
using CakeOS.Cui;          // CuiCompiledArtifact (Core)
using CakeOS.Cui.Language;  // CuiSourceSpan, CuiDiagnostic, CuiDiagnosticSeverity
```

`CuiRuntimeLoadException`, `CuiControlRegistry`, and the `CakeOS.Cui.Runtime` namespace members need
no additional using (same namespace).

## Diagnostic codes thrown (structured `CuiRuntimeLoadException`)

| Code | Meaning |
|---|---|
| `CUIB003` | Missing artifact / missing envelope metadata (safe failure). |
| `CUIR064` | Compiled language version not supported by this runtime. |
| `CUIR065` | Compiled runtime ABI outside this runtime's supported `[MinStatelessAbi, CurrentAbi]`. |
| `CUIR066` | Compiled component-registry version does not match the runtime's current registry. |
| `CUIR067` | A required capability is not in this runtime's supported capability set. |

## Behavior notes

- Genuine stateless ABI1 siblings remain accepted (ABI 1 is in range); a stateful ABI2 composer with
  ABI1 siblings succeeds.
- A stateless sibling with `ABI=999` (envelope-consistent) is rejected with `CUIR065` before effects.
- A sibling-only unknown capability (envelope-consistent union) is rejected with `CUIR067` before effects.
- The codec's structural/consistency validation stays independent of the runtime; this API only adds
  host-support admission on top of it.

## Tests (authored in spaces-2 return, all NOT RUN until root integrates + allocates the slot)

- `ChatComposerCompiledTransportTests.Admission_rejects_unsupported_language_version_before_effects`
  (consistent unsupported language → `CUIR064`).
- `ChatComposerCompiledTransportTests.Admission_rejects_envelope_document_language_contradiction_before_effects`
  (contradiction → `CUIR075`).
- `ChatComposerCompiledTransportTests.Admission_rejects_unsupported_component_registry_before_effects`
  (consistent unsupported registry → `CUIR066`).
- `ChatComposerCompiledTransportTests.Sibling_abi999_required_import_is_rejected_before_effects`
  → `CUIR065`.
- `ChatComposerCompiledTransportTests.Sibling_only_unknown_capability_with_consistent_union_is_rejected`
  → `CUIR067`.
- `ChatComposerCompiledTransportTests.Genuine_abi1_stateless_sibling_plus_abi2_composer_succeeds`.
