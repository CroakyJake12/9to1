# Lane 2 — Tests (pass 3; all NOT RUN pending .NET slot)

All builds/tests are **NOT RUN**. Lane 2 is not lane 1; the serialized .NET slot has not been
allocated by the coordinator. These files are authored (by inspection) and run only when the slot is
granted. No test is manufactured to pass; each asserts a required contract.

## Compiler tests — `framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj`

Filter:
```
--filter "FullyQualifiedName~CuiV2TransportCodecTests|FullyQualifiedName~CuiLocalStateAbiOutputTests"
```

- `CuiV2TransportCodecTests`
  - `Typed_v2_round_trip_preserves_every_semantic_distinction`
  - `Unknown_value_kind_is_rejected_before_effects` (CUIR071)
  - `Missing_required_field_is_rejected` (CUIR072)
  - `Malformed_payload_is_rejected` (CUIR070)
  - `Duplicate_document_source_identity_is_rejected` (CUIR073)
  - `Envelope_document_abi_contradiction_is_rejected` (CUIR075)
  - `Envelope_document_language_mismatch_is_rejected` (CUIR075)
  - `Stateful_document_requires_abi2_by_inspecting_typed_semantics`
  - `Stateful_semantics_with_stripped_required_capability_is_rejected` (CUIR076)
  - `Genuine_v1_reader_consumes_stateless_v1_and_rejects_v2`
  - `Genuine_v1_span_round_trip_preserves_source_coordinates` (span roundtrip)
  - `Genuine_v1_baseline_reader_tolerates_capabilities_and_unknown_fields` (unchanged baseline
    consumer: V1 reader tolerates omitted/unknown capabilities, unlike V2 strict admission)
  - `Case_distinct_symbols_round_trip_without_collapsing` (Ordinal, no collapse)
  - `Duplicate_exact_json_symbol_keys_are_rejected` (CUIR073; raw-JSON duplicate-key detection)
  - `Null_styles_collection_is_rejected` (CUIR072)
  - `Null_dependency_entries_are_rejected` (CUIR072)
  - `Null_document_root_span_is_rejected` (CUIR072)
  - `Null_action_span_is_rejected` (CUIR072)
  - `Null_import_span_is_rejected` (CUIR072)
  - `Action_identity_key_mismatch_is_rejected` (CUIR073)
  - `Unsupported_dependency_kind_is_rejected` (CUIR071)
  - `Missing_value_span_is_rejected_before_consumer_use` (CUIR072)
- `CuiLocalStateAbiOutputTests`
  - `Stateful_toggle_document_emits_abi2_and_local_state_toggle_capability`
  - `Stateful_random_once_document_emits_abi2_and_random_select_capability`
  - `Stateless_document_stays_abi1_and_v1_eligible` (fixture ABI pinned to `1`)
  - `Public_compatibility_record_keeps_core_clr_identity_via_forwarder` (real forwarding evidence:
    `Assembly.GetForwardedTypes()` plus `Type.GetType` on the old assembly-qualified name resolving
    to the Core assembly)
  - `Four_argument_action_definition_constructor_form_is_preserved` (source form)
  - `Four_argument_action_definition_clr_constructor_and_deconstruct_are_preserved` (reflection; requires the
    lane1/root patch in `work/lane2-handoff/PATCH-CuiActionDefinition-four-arg.md`)

## Spaces consumer contract — `9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj`

Filter:
```
--filter "FullyQualifiedName~ChatComposerCompiledTransportTests"
```

- Embedded V2 bundle roundtrip: `FormattingOpen` bool + `composer.toggle-formatting` LocalTarget + ABI2 +
  `local-state-toggle`; `EmojiIcon` RandomOnce + `local-state-random-select`.
- Admission rejections before effects: unknown format (CUIR060), version (CUIR061), language (CUIR064),
  stateful-without-ABI2 with capabilities stripped (CUIR074), unsupported capability (CUIR067), envelope/doc
  ABI contradiction (CUIR075).
- `Missing_compiled_composer_resource_has_no_raw_fallback_and_composer_is_not_in_v1`: no raw composer
  resource; V2 present; composer absent from any genuine V1 bundle (stateless sibling V1 allowed).
- Test-only Compiler project reference (for `CuiV1ArtifactCodec.ReadArtifact`) added to the test
  project; production Spaces must not depend on the Compiler assembly.

## Real MSBuild verifier — `framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py`

```
python3 framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py \
  --task-assembly framework/CUI/Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll \
  --evidence /tmp/opencode/lane2-evidence \
  --dotnet /home/jacob/.local/bin/dotnet
```

Covers, under one script:

- Mixed build across **all five accepted item types** (EmbeddedResource / Content / None /
  AvaloniaResource / CuiMarkup): true typed V2 artifact (`compilation.v2.cuic.json`) carries exactly the
  stateful subset; genuine V1 bundle (`compilation.v1.cuic.json`) carries the stateless subset.
- **Genuine V1 lowercase keys** (`format/formatVersion/documents/source`) asserted; typed V2 keys are
  PascalCase.
- **Authoritative resource enumeration**: a tiny C# probe project is built/run under the slot and
  prints `GetManifestResourceNames()`; asserts V2/V1 resource identities present and stateful raw
  (`Lane2.Stateful.cui`) absent.
- Output-copy (Always/PreserveNewest/IfDifferent) vs publish (`CopyToPublishDirectory` defaults)
  ownership modelled independently: publish-only content copied only to a **custom PublishDir**;
  `CopyToPublishDirectory=Never` absent there; `CopyToOutputDirectory=Never` not copied to output.
- Unrelated same-path bytes preserved: overwriting an owned destination with unrelated bytes prevents
  stale deletion (hash-guard); a never-owned same-bytes path is never claimed/deleted.
- Incremental stateless → stateful on the same output: stale owned raw removed only when hash still
  matches; unrelated file untouched; sources never deleted; V1 drops the transitioned document.
- Statefulness propagation through the **import closure**: a stateless importer of a stateful child is
  promoted to ABI2 and both appear in V2.
- Unsafe/escaping `TargetPath` fails the build (`Unsafe`, CUIB003) before any artifact is published.
- Failed compile (`Widht` typo → `CUIC012`) publishes nothing and preserves the prior artifact bytes.

All runs use the owner's guarded dotnet (`--dotnet /home/jacob/.local/bin/dotnet`, `-m:1`).
