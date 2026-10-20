# Lane 2 — Tests (all NOT RUN pending .NET slot)

All builds/tests are **NOT RUN**. Lane 2 is not lane 1; the serialized .NET slot has not been allocated
by the coordinator. These files are authored (by inspection) and run only when the slot is granted.

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
  - `Stateful_document_requires_abi2_by_inspecting_typed_semantics`
  - `Genuine_v1_reader_consumes_stateless_v1_and_rejects_v2`
  - `Genuine_v1_span_round_trip_preserves_source_coordinates` (span roundtrip)
  - `Case_distinct_symbols_round_trip_without_collapsing` (Ordinal, no collapse)
  - `Null_styles_collection_is_rejected` (CUIR072)
  - `Unsupported_dependency_kind_is_rejected` (CUIR071)
  - `Missing_value_span_is_rejected_before_consumer_use` (CUIR072)
- `CuiLocalStateAbiOutputTests`
  - `Stateful_toggle_document_emits_abi2_and_local_state_toggle_capability`
  - `Stateful_random_once_document_emits_abi2_and_random_select_capability`
  - `Stateless_document_stays_abi1_and_v1_eligible` (fixture ABI pinned to `1`)
  - `Public_compatibility_record_keeps_core_clr_identity_via_forwarder` (owning assembly + forwarder attr)
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

## Real MSBuild verifier — `framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py`

```
python3 framework/CUI/Build.Tasks.Tests/verify_lane2_v2_packaging.py \
  --task-assembly framework/CUI/Build.Tasks/bin/Debug/net10.0/CakeOS.Cui.Build.Compiler.dll \
  --evidence /tmp/opencode/lane2-evidence \
  --dotnet /home/jacob/.local/bin/dotnet
```

Fixture: `Stateful.cui` embedded, `Stateless.cui` Content with `Link="Raw/Stateless.cui"` +
`CopyToOutputDirectory="PreserveNewest"`.

- Mixed build: V2 artifact (`compilation.v2.cuic.json`) carries exactly the stateful doc; V1 artifact
  (`compilation.v1.cuic.json`) carries exactly the stateless doc; both resource identities embedded;
  stateful raw not embedded/copied; stateless raw copied to `bin/Raw/Stateless.cui`.
- Incremental stateless→stateful on same output without Clean: stale raw removed from output; unrelated file
  untouched; V2 still embedded; now-two-stateful-documents; sources never deleted.
- Failed compile (`Widht` typo) yields `CUIC012` and preserves the prior artifact bytes.

All runs use the owner's guarded dotnet (`--dotnet /home/jacob/.local/bin/dotnet`, `-m:1`).
