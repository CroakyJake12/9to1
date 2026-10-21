// Genuine-V1 typed reader/shim regression tests. These lock the shared typed reader seam after the
// Core reader is added and the Compiler shim is made a thin mapping adapter over it. They cover:
//   - the Compiler shim's restored PUBLIC nested CuiV1LoadedArtifact/CuiV1LoadedDocument API and its
//     InvalidDataException producer/test contract;
//   - the Core reader's structural admission (per-document metadata, identity, required compiled
//     collections, duplicate-source and wrong-shape rejection) using the actual writer-produced bytes;
//   - tolerance of omitted/unknown capability values (baseline V1 reader contract) without inventing
//     host-support strictness in the reader (host support is the Runtime boundary).
//
// The existing CuiV2TransportCodecTests baseline V1 compatibility cases and the V2/local-state/closure
// tests remain untouched; this file only adds focused reader/shim regressions.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiV1ReaderShimRegressionTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static CuiCompiledDocument CompileStateless()
    {
        var options = new CuiCompilerOptions(
            new CuiCompilationMetadata("1", "1", CuiControlRegistry.ComponentRegistryVersion));
        var result = new CuiCompiler().Compile(
            "<Cui><Page><Text Text=\"hi\" /></Page></Cui>", "sample.cui", options);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    private static byte[] WriteSimpleBundle()
    {
        var compiled = CompileStateless();
        return CuiV1ArtifactCodec.WriteBundle(
            [new CuiV1ArtifactCodec.CuiV1Compiled(compiled, "sample.cui", "x")], "compiler");
    }

    private static byte[] Mutate(byte[] source, Action<JsonObject> apply)
    {
        var root = JsonNode.Parse(source)!.AsObject();
        apply(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString(Indented));
    }

    private static JsonObject Compilation0(JsonObject root) => CompilationAt(root, 0);
    private static JsonObject CompilationAt(JsonObject root, int index) =>
        (JsonObject)((JsonObject)root["documents"]![index]!)["compilation"]!;

    [Fact]
    public void Compiler_shim_restores_public_nested_loaded_types_and_maps_core_result()
    {
        var loaded = CuiV1ArtifactCodec.ReadArtifact(WriteSimpleBundle());
        Assert.IsType<CuiV1ArtifactCodec.CuiV1LoadedArtifact>(loaded);
        Assert.Equal(1, loaded.FormatVersion);
        var document = Assert.Single(loaded.Documents);
        Assert.Equal("sample.cui", document.Source);
        Assert.Equal("Page", Assert.Single(document.Semantics.Components).Type);
    }

    [Fact]
    public void Core_and_shim_readers_agree_on_genuine_v1_bundle()
    {
        var bytes = WriteSimpleBundle();
        var shim = CuiV1ArtifactCodec.ReadArtifact(bytes);
        var core = CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(bytes);
        Assert.Equal(shim.FormatVersion, core.FormatVersion);
        var shimDoc = Assert.Single(shim.Documents);
        var coreDoc = Assert.Single(core.Documents);
        Assert.Equal(shimDoc.Source, coreDoc.Source);
        Assert.Equal(shimDoc.Sha256, coreDoc.Sha256);
        Assert.Equal(shimDoc.Metadata.RuntimeAbiVersion, coreDoc.Metadata.RuntimeAbiVersion);
    }

    [Fact]
    public void Core_reader_rejects_missing_per_document_metadata()
    {
        var mutated = Mutate(WriteSimpleBundle(), root => Compilation0(root).Remove("Metadata"));
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_missing_source_identity()
    {
        var mutated = Mutate(WriteSimpleBundle(), root => ((JsonObject)root["documents"]![0]!).Remove("source"));
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_duplicate_source_identity()
    {
        var mutated = Mutate(WriteSimpleBundle(), root =>
        {
            var docs = (JsonArray)root["documents"]!;
            docs.Add(((JsonObject)docs[0]!).DeepClone());
        });
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_missing_required_compilation_collection()
    {
        var mutated = Mutate(WriteSimpleBundle(), root => Compilation0(root).Remove("Components"));
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_wrong_required_shape()
    {
        var mutated = Mutate(WriteSimpleBundle(), root => Compilation0(root)["Semantics"] = "not-an-object");
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Compiler_shim_maps_core_admission_to_invalid_data_contract()
    {
        var mutated = Mutate(WriteSimpleBundle(), root => Compilation0(root).Remove("Metadata"));
        Assert.Throws<InvalidDataException>(() => CuiV1ArtifactCodec.ReadArtifact(mutated));
    }

    [Fact]
    public void Core_reader_tolerates_unknown_capability_values()
    {
        var mutated = Mutate(WriteSimpleBundle(), root =>
            ((JsonObject)root["metadata"]!)["RequiredCapabilities"] = new JsonArray("future-capability"));
        var loaded = CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated);
        Assert.Contains("future-capability", loaded.Metadata.RequiredCapabilities);
    }

    // --- Causal regressions for the repaired typed admission boundary (root kind, scalar
    //     kinds, required semantics projection, envelope reconciliation, bool default) ---

    private const string MinimalSpan = """{ "SourceName": "sample.cui", "Start": {"Line":1,"Column":1}, "End": {"Line":1,"Column":1} }""";

    private static byte[] MinimalArtifact()
    {
        var json = """
        {
          "format": "cui-build-compilation",
          "formatVersion": 1,
          "compilerSha256": "compiler",
          "metadata": { "LanguageVersion": "1", "RuntimeAbiVersion": "1", "ComponentRegistryVersion": "1" },
          "documents": [
            {
              "source": "sample.cui",
              "sha256": "x",
              "compilation": {
                "Metadata": { "LanguageVersion": "1", "RuntimeAbiVersion": "1", "ComponentRegistryVersion": "1" },
                "Components": [],
                "Templates": {},
                "SourceMap": [],
                "Dependencies": {},
                "Semantics": {
                  "SourceName": "sample.cui",
                  "Span": __SPAN__,
                  "Components": [
                    { "Type": "Page", "Classes": [], "Children": [], "ElseChildren": [], "Groups": [],
                      "Span": __SPAN__ }
                  ]
                }
              }
            }
          ]
        }
        """.Replace("__SPAN__", MinimalSpan);
        return Encoding.UTF8.GetBytes(json);
    }

    [Fact]
    public void Core_reader_rejects_array_root_with_admission_not_invalid_operation()
    {
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(Encoding.UTF8.GetBytes("[1,2]")));
        Assert.Equal("CUIR070", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_string_format_version_with_admission()
    {
        var root = JsonNode.Parse(MinimalArtifact())!.AsObject();
        root["formatVersion"] = "1";
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(Encoding.UTF8.GetBytes(root.ToJsonString(Indented))));
        Assert.Equal("CUIR070", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_malformed_scalar_semantic_value()
    {
        var mutated = Mutate(MinimalArtifact(), root =>
            Compilation0(root)["Semantics"]!["Components"]![0]!["Properties"] =
                new JsonObject { ["Bad"] = "oops" });
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR070", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_missing_required_semantics_components()
    {
        var mutated = Mutate(MinimalArtifact(), root =>
            ((JsonObject)Compilation0(root)["Semantics"]!).Remove("Components"));
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_envelope_language_contradiction()
    {
        var mutated = Mutate(MinimalArtifact(), root =>
            ((JsonObject)Compilation0(root)["Metadata"]!)["LanguageVersion"] = "2");
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_preserves_absent_IsLive_true_default()
    {
        var root = JsonNode.Parse(MinimalArtifact())!.AsObject();
        var component = (JsonObject)Compilation0(root)["Semantics"]!["Components"]![0]!;
        component["Condition"] = new JsonObject
        {
            ["Test"] = new JsonObject { ["$type"] = "CuiLiteralValue", ["Value"] = "x", ["Span"] = JsonNode.Parse(MinimalSpan) },
            ["Negate"] = false,
            ["Span"] = JsonNode.Parse(MinimalSpan),
        };
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(Indented));
        var loaded = CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(bytes);
        var document = Assert.Single(loaded.Documents);
        var page = Assert.Single(document.Semantics.Components);
        Assert.NotNull(page.Condition);
        Assert.True(page.Condition!.IsLive);
    }

    // --- Genuine producer null-optional compatibility and aggregate ABI boundary ---

    private static JsonObject Component0(JsonObject root) => (JsonObject)Compilation0(root)["Semantics"]!["Components"]![0]!;

    [Fact]
    public void Genuine_producer_bundle_with_present_null_optionals_reads_cleanly()
    {
        // The real writer serializes nullable optional members without ignoring them, so a
        // compiler-produced bundle carries explicit nulls for Condition/Repeat/List/PropertyRegion.
        var loaded = CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(WriteSimpleBundle());
        var document = Assert.Single(loaded.Documents);
        var page = Assert.Single(document.Semantics.Components);
        Assert.Null(page.Condition);
        Assert.Null(page.Repeat);
        Assert.Null(page.List);
        Assert.Null(page.PropertyRegion);
    }

    [Fact]
    public void Core_reader_preserves_present_null_optional_as_absence()
    {
        var root = JsonNode.Parse(MinimalArtifact())!.AsObject();
        Component0(root)["Repeat"] = null;
        Component0(root)["List"] = null;
        Component0(root)["PropertyRegion"] = null;
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(Indented));
        var loaded = CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(bytes);
        var page = Assert.Single(Assert.Single(loaded.Documents).Semantics.Components);
        Assert.Null(page.Condition);
        Assert.Null(page.Repeat);
        Assert.Null(page.List);
        Assert.Null(page.PropertyRegion);
    }

    [Fact]
    public void Core_reader_refuses_wrong_non_null_optional_kind_with_typed_admission()
    {
        var mutated = Mutate(MinimalArtifact(), root => Component0(root)["Condition"] = "not-an-object");
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_malformed_coordinate_scalar_with_typed_admission()
    {
        var mutated = Mutate(MinimalArtifact(), root =>
            Component0(root)["Span"] = new JsonObject
            {
                ["SourceName"] = "sample.cui",
                ["Start"] = new JsonObject { ["Line"] = "one", ["Column"] = 1 },
                ["End"] = new JsonObject { ["Line"] = 1, ["Column"] = 1 },
            });
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR070", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_document_abi_exceeding_envelope_maximum()
    {
        var mutated = Mutate(MinimalArtifact(), root =>
            ((JsonObject)Compilation0(root)["Metadata"]!)["RuntimeAbiVersion"] = "2");
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    private static CuiCompiledDocument CompileWithAbi(string abi)
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", abi, CuiControlRegistry.ComponentRegistryVersion));
        var result = new CuiCompiler().Compile(
            "<Cui><Page><Text Text=\"hi\" /></Page></Cui>", "sample.cui", options);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    private static byte[] WriteHeterogeneousBundle()
    {
        // A genuine producer multi-document heterogeneous bundle: one document targets an OLDER ABI
        // (0) and another the newer ABI (1). The unchanged real writer's AggregateMetadata computes the
        // envelope as the aggregate MAXIMUM ABI. This is the scenario the previous single-document
        // ABI0/envelope1 mutation could never emit.
        var older = CompileWithAbi("0");
        var newer = CompileWithAbi("1");
        return CuiV1ArtifactCodec.WriteBundle(
            [new CuiV1ArtifactCodec.CuiV1Compiled(older, "older.cui", "a"),
             new CuiV1ArtifactCodec.CuiV1Compiled(newer, "newer.cui", "b")], "compiler");
    }

    [Fact]
    public void Genuine_producer_multidocument_heterogeneous_bundle_preserves_maximum_abi()
    {
        var bytes = WriteHeterogeneousBundle();
        var loaded = CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(bytes);
        Assert.Equal("1", loaded.Metadata.RuntimeAbiVersion); // envelope = producer aggregate maximum.
        Assert.Equal(2, loaded.Documents.Count);
        Assert.Equal("0", loaded.Documents[0].Metadata.RuntimeAbiVersion); // heterogeneous older-ABI doc preserved.
        Assert.Equal("1", loaded.Documents[1].Metadata.RuntimeAbiVersion);
    }

    [Fact]
    public void Genuine_multidocument_bundle_rejects_document_abi_exceeding_envelope_max()
    {
        // Mutation of genuine producer bytes: raising one document's ABI above the producer aggregate
        // maximum (envelope) is malformed, exactly as ReconcileEnvelope requires.
        var mutated = Mutate(WriteHeterogeneousBundle(), root =>
            ((JsonObject)CompilationAt(root, 0)["Metadata"]!)["RuntimeAbiVersion"] = "2");
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Genuine_multidocument_bundle_rejects_document_capability_absent_from_envelope()
    {
        // Mutation of genuine producer bytes: a document requiring a capability absent from the
        // envelope aggregate contradicts the producer contract and is refused.
        var mutated = Mutate(WriteHeterogeneousBundle(), root =>
            ((JsonObject)CompilationAt(root, 0)["Metadata"]!)["RequiredCapabilities"] = new JsonArray("future-capability"));
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Core_reader_rejects_malformed_dependency_graph_entry_shape()
    {
        var mutated = Mutate(WriteSimpleBundle(), root =>
            ((JsonObject)((JsonObject)Compilation0(root)["Dependencies"]!)["Entries"]!)["component-x"] = new JsonArray("not-an-object"));
        var error = Assert.Throws<CakeOS.Cui.CuiV1AdmissionException>(
            () => CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(mutated));
        Assert.Equal("CUIR072", error.Code);
    }
}
