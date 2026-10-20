# Lane 2 — Tests (all NOT RUN pending .NET slot)

All builds/tests are **NOT RUN**. Lane 2 is not lane 1; the serialized .NET slot has not been
allocated by the coordinator. These files are authored (by inspection) and must be executed only when
the slot is granted.

## Unit/compiler tests
- `framework/CUI/Compiler.Tests/CuiV2TransportCodecTests.cs`
  - Typed V2 round-trip preserves every semantic distinction (variables, LocalTarget actions, bindings,
    resources, conditionals, repeats, templates, regions, static-mode properties).
  - Unknown value kind rejected (CUIR071), missing field rejected (CUIR072), malformed payload (CUIR070),
    duplicate source identity (CUIR073), envelope/doc ABI contradiction (CUIR075).
  - Stateful document requires ABI2 via typed semantics alone (CUIR074 covered by shared codec).
  - Genuine V1 reader consumes stateless V1 and rejects V2.
- `framework/CUI/Compiler.Tests/CuiLocalStateAbiOutputTests.cs`
  - Stateful toggle emits ABI2 + `local-state-toggle` capability.
  - Stateful RandomOnce emits ABI2 + `local-state-random-select` capability.
  - Stateless stays ABI1 / V1-eligible.
  - `CuiCompilationMetadata` CLR identity lives in Core via the forwarder.
  - Four-arg and five-arg `CuiActionDefinition` constructor forms compile.

## Spaces consumer contract
- `9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs`
  - Embedded V2 bundle round-trips to semantics with `FormattingOpen` variable + `composer.toggle-formatting`
    LocalTarget toggle and `EmojiIcon` RandomOnce variable/capability.
  - Admission rejects unknown format (CUIR060), version (CUIR061), language (CUIR064), stateful-without-ABI2
    even with capabilities stripped (CUIR074), unsupported capability (CUIR067), envelope/doc ABI contradiction
    (CUIR075); missing compiled resource has no raw/V1 fallback.

## Real MSBuild verifier
- `framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py`
  - Distinct sources: `Stateful.cui` embedded (LogicalName `Lane2.Stateful.cui`), `Stateless.cui` `Content`
    with `Link="Raw/Stateless.cui"` + copy.
  - Asserts V2 resource embedded, V2 obj artifact Format/V2 + 2 documents, stateful raw not embedded/copied,
    stateless raw copied to `bin/Raw/Stateless.cui`.
  - Incremental stateless -> stateful: stale raw removed from output, not embedded, unrelated file untouched,
    V2 still embedded, sources not deleted.
  - Failed compile (`Widht` typo) yields `CUIC012` and preserves the prior artifact.
  - Run only with the owner's guarded dotnet (`--dotnet /home/jacob/.local/bin/dotnet`, `-m:1`).
