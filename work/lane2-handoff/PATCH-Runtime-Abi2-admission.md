# Lane 2 → lane 1/root integration patch: Runtime ABI2/feature admission seam

`Runtime/CuiRuntimeCompatibility.cs` (and the Runtime assembly generally) is owned by lane 1/root.
Lane 2 does not edit it. This is the **exact minimal integration patch** root/lane 1 must apply so a
stateful-aware runtime advertises ABI2 while genuine stateless V1 documents are never wrongly stamped
stateful.

## Why this seam exists

The Chat composer is stateful (document-root local variables / pure-local toggles). It requires the
stateful ABI and consumes the distinct typed V2 artifact. Today:

- `CuiRuntimeCompatibility.RuntimeAbiVersion` is `"1"`, while the composer's admission
  (`ChatComposerCuiDocument.ValidateRuntimeCompatibility`) requires
  `CuiCompiledArtifactCodec.V2RuntimeAbiVersion` (`"2"`). The runtime that hosts the composer must
  advertise the stateful ABI.
- If root naively sets `RuntimeAbiVersion = "2"`, the Build.Tasks emitter (which previously seeded
  every document metadata with `CuiRuntimeCompatibility.RuntimeAbiVersion`) would stamp **stateless**
  V1 documents as ABI2. That is why lane 2 has already changed its own emitter
  (`CompileCuiSourcesTask`) to seed stateless documents with a dedicated minimum/stateless ABI
  constant, decoupled from the runtime's advertised ABI. The patch below adds the matching public
  constants on the Runtime side.

## Exact patch (apply to `framework/CUI/Runtime/CuiRuntimeCompatibility.cs`)

Replace the constant block:

```csharp
public static class CuiRuntimeCompatibility
{
    public const string LanguageVersion = "1";
    public const string RuntimeAbiVersion = "1";
```

with:

```csharp
public static class CuiRuntimeCompatibility
{
    public const string LanguageVersion = "1";

    /// <summary>Minimum ABI required by genuine stateless (V1-eligible) compiled documents. This is
    /// deliberately decoupled from <see cref="RuntimeAbiVersion"/>: a stateful-aware runtime
    /// advertises the higher current ABI without stamping stateless documents as stateful.</summary>
    public const string MinStatelessAbiVersion = "1";

    /// <summary>The ABI this runtime implements/advertises today. The stateful composer
    /// (local-state toggle / random-select) requires this stateful ABI; stateless documents require
    /// only <see cref="MinStatelessAbiVersion"/>.</summary>
    public const string RuntimeAbiVersion = "2";
```

The `Capabilities` set already includes `LocalStateToggleCapability` and
`LocalStateRandomSelectCapability`, and the composer already checks capability admission against it;
no change is needed there.

## What this preserves / requires

- `CuiControlRegistry.ExportSchema` reports `runtimeAbiVersion` (line 63) — this becomes `"2"`,
  which is correct tooling output for a stateful-aware runtime.
- Consumers that stamp **stateless** documents must use `MinStatelessAbiVersion` (or the shared
  `CuiCompiledArtifactCodec.V1RuntimeAbiVersion`), never `RuntimeAbiVersion`. Lane 2's
  `CompileCuiSourcesTask` already does this; `PromoteStatefulMetadata` stamps only stateful documents
  with `V2RuntimeAbiVersion`.
- No production metadata, capability string, or composer logic is otherwise touched.

## How lane 2 verifies it (NOT RUN until .NET slot)

- `CuiV2TransportCodecTests` round-trips stateless V1 and stateful V2; admission enforces
  stateful-requires-ABI2 from the typed semantics.
- `CuiLocalStateAbiOutputTests.Stateless_document_stays_abi1_and_v1_eligible` pins ABI `1` for
  stateless output and asserts no stateful capability is present.
- The Python verifier's mixed project asserts the stateless subset stays in the genuine V1 bundle
  (`format/formatVersion/documents/source`), and the stateful subset in V2.

## Ownership

- File: `framework/CUI/Runtime/CuiRuntimeCompatibility.cs` (lane 1/root owns; lane 2 supplies this
  patch only).
- Companion lane-2 emitter change: `framework/CUI/Build.Tasks/CompileCuiSourcesTask.cs` (lane 2 owns)
  now seeds stateless documents with `CuiCompiledArtifactCodec.V1RuntimeAbiVersion`.
