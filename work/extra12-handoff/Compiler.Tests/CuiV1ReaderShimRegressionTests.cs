// extra12 shared reader/shim regression tests (apply under framework/CUI/Compiler.Tests/).
//
// These focused tests lock the genuine-V1 typed reader seam after the Core reader is added and the
// Compiler shim is made a thin mapping adapter over it. They cover:
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
        return result.Output;
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

    private static JsonObject Compilation0(JsonObject root) => (JsonObject)((JsonObject)root["documents"]![0]!)["compilation"]!;

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
}
