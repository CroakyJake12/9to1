using System.Linq;
using System.Text;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Views.Shell.VoiceVision;
using Xunit;

namespace Haven.Desktop.Tests.VoiceVision;

/// <summary>
/// The Voice &amp; Vision pill and full session must consume the PACKAGED genuine V1 compiled CUI
/// bundle (the stateless V1 contract emitted by Spaces Build.Tasks) with hard admission checks —
/// never a silent raw-source reparse fallback, and never the stateful V2 codec. These tests verify
/// the real embedded V1 bundle carries both Voice documents, loads through the typed semantics, and
/// that an unsupported envelope/document language/ABI/registry/capability fails before effects.
/// </summary>
public sealed class VoiceVisionCuiCompiledTests
{
    private const string PillSource = "UI/VoiceVision/VoiceVisionPill.cui";
    private const string SessionSource = "UI/VoiceVision/VoiceVisionSession.cui";

    private static CuiV1ArtifactCodec.CuiV1LoadedArtifact ReadEmbeddedArtifact()
    {
        var artifact = CuiV1ArtifactCodec.ReadArtifact(VoiceVisionCuiDocument.ReadEmbeddedArtifact());
        Assert.Equal(CuiV1ArtifactCodec.FormatName, artifact.Format);
        Assert.Equal(CuiV1ArtifactCodec.FormatVersion, artifact.FormatVersion);
        return artifact;
    }

    private static CuiV1ArtifactCodec.CuiV1LoadedDocument Document(CuiV1ArtifactCodec.CuiV1LoadedArtifact artifact, string source) =>
        Assert.Single(artifact.Documents, document => document.Source == source);

    [Fact]
    public void Embedded_v1_bundle_carries_both_voice_documents()
    {
        var artifact = ReadEmbeddedArtifact();
        _ = Document(artifact, PillSource);
        _ = Document(artifact, SessionSource);

        var pill = VoiceVisionCuiDocument.LoadPill();
        Assert.NotNull(pill);
        Assert.NotEmpty(pill.Components);

        var session = VoiceVisionCuiDocument.LoadSession();
        Assert.NotNull(session);
        Assert.NotEmpty(session.Components);
    }

    [Fact]
    public void Pill_load_rejects_unknown_artifact_format_before_effects()
    {
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, EnvelopeBytes("legacy-format", 1)));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_unsupported_format_version_before_effects()
    {
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, EnvelopeBytes(CuiV1ArtifactCodec.FormatName, 999)));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Load_rejects_malformed_artifact_before_effects()
    {
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, Encoding.UTF8.GetBytes("{not json")));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Load_rejects_missing_document_before_effects()
    {
        var bytes = ArtifactBytes("UI/VoiceVision/VoiceVisionSession.cui", MetadataV1());
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR062", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_unsupported_language_version_before_effects()
    {
        var bytes = ArtifactBytes(PillSource, MetadataV1(language: "99"));
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Session_load_rejects_incompatible_runtime_abi_before_effects()
    {
        var bytes = ArtifactBytes(SessionSource, MetadataV1(abi: "future-abi"));
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(SessionSource, bytes));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_unsupported_component_registry_before_effects()
    {
        var bytes = ArtifactBytes(PillSource, MetadataV1(registry: "999.0"));
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR066", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_unsupported_required_capability_before_effects()
    {
        var bytes = ArtifactBytes(PillSource, MetadataV1(capabilities: ["future-capability"]));
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_empty_semantics_before_effects()
    {
        var bytes = ArtifactBytes(PillSource, MetadataV1(), emptySemantics: true);
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR063", error.Diagnostic.Code);
    }

    [Fact]
    public void Clean_document_rejects_unsupported_sibling_before_effects()
    {
        // The requested pill is clean, but its sibling (session) declares an unsupported language
        // version. The loader must validate every document BEFORE selecting, so the sibling's
        // defect refuses the whole bundle even though the requested peer would otherwise pass.
        var bytes = ArtifactBytes([
            (PillSource, MetadataV1(), true),
            (SessionSource, MetadataV1(language: "99"), true)
        ]);
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Load_rejects_array_root_envelope_before_effects()
    {
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, Encoding.UTF8.GetBytes("[1,2,3]")));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Load_rejects_non_object_envelope_before_effects()
    {
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, Encoding.UTF8.GetBytes("42")));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_non_string_format_before_effects()
    {
        var bytes = Encoding.UTF8.GetBytes(
            $$"""{"format":123,"formatVersion":{{CuiV1ArtifactCodec.FormatVersion}},"compilerSha256":"x","metadata":{},"documents":[]}""");
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_non_numeric_format_version_before_effects()
    {
        var bytes = Encoding.UTF8.GetBytes(
            $$"""{"format":"{{CuiV1ArtifactCodec.FormatName}}","formatVersion":"future","compilerSha256":"x","metadata":{},"documents":[]}""");
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("999999999999999999999999999999")]
    public void Pill_load_rejects_other_malformed_format_version_kinds_before_effects(string versionToken)
    {
        // Missing/string/object/array/null/overflow/unsupported versions must all produce the
        // intended typed format-version diagnostic (CUIR061) — never a leaked InvalidOperationException
        // from TryGetInt32 on a non-number element.
        var bytes = Encoding.UTF8.GetBytes(
            $$"""{"format":"{{CuiV1ArtifactCodec.FormatName}}","formatVersion":{{versionToken}},"compilerSha256":"x","metadata":{},"documents":[]}""");
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            VoiceVisionCuiDocument.LoadWithArtifact(PillSource, bytes));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    private static byte[] EnvelopeBytes(string format, int version) => Encoding.UTF8.GetBytes(
        $$"""{"format":"{{format}}","formatVersion":{{version}},"compilerSha256":"x","metadata":{},"documents":[]}""");

    private static (string Language, string Abi, string Registry, string[] Capabilities) MetadataV1(
        string? language = null, string? abi = null, string? registry = null, IEnumerable<string>? capabilities = null) =>
        (language ?? "1", abi ?? CuiCompiledArtifactCodec.V1RuntimeAbiVersion, registry ?? CuiControlRegistry.ComponentRegistryVersion,
            capabilities?.ToArray() ?? []);

    /// <summary>Builds a minimal genuine V1 artifact with one compiled document. The envelope and
    /// document metadata are kept consistent. Empty semantics (no Components) is valid transport and
    /// exercises the loader's empty-model rejection; non-empty semantics is used elsewhere.</summary>
    private static byte[] ArtifactBytes(string source, (string Language, string Abi, string Registry, string[] Capabilities) meta, bool emptySemantics = false)
        => ArtifactBytes(new[] { (source, meta, !emptySemantics) });

    /// <summary>Builds a minimal genuine V1 artifact carrying multiple compiled documents so tests
    /// can exercise sibling admission. Each tuple is (source, metadata, nonEmptySemantics).</summary>
    private static byte[] ArtifactBytes(
        IReadOnlyList<(string Source, (string Language, string Abi, string Registry, string[] Capabilities) Meta, bool NonEmpty)> documents)
    {
        var docsJson = string.Join(",", documents.Select(document => DocumentJson(document.Source, document.Meta, document.NonEmpty)));
        var json = $$"""{"format":"{{CuiV1ArtifactCodec.FormatName}}","formatVersion":{{CuiV1ArtifactCodec.FormatVersion}},"compilerSha256":"x","metadata":{{MetadataJson(MetadataV1())}},"documents":[{{docsJson}}]}""";
        return Encoding.UTF8.GetBytes(json);
    }

    private static string DocumentJson(string source, (string Language, string Abi, string Registry, string[] Capabilities) meta, bool nonEmpty)
    {
        var metadataJson = MetadataJson(meta);
        var semanticsJson = nonEmpty ? NonEmptySemanticsJson : EmptySemanticsJson;
        return $$"""{"source":"{{source}}","sha256":"x","compilation":{"Metadata":{{metadataJson}},"Semantics":{{semanticsJson}}}}""";
    }

    private static string MetadataJson((string Language, string Abi, string Registry, string[] Capabilities) meta)
    {
        var sectionJson = string.Join(",", meta.Capabilities.Select(cap => $"\"{cap}\""));
        return $$"""{"LanguageVersion":"{{meta.Language}}","RuntimeAbiVersion":"{{meta.Abi}}","ComponentRegistryVersion":"{{meta.Registry}}","RequiredCapabilities":[{{sectionJson}}]}""";
    }

    private const string NonEmptySemanticsJson =
        """{"SourceName":"x","Resources":{},"Styles":[],"Actions":{},"Templates":{},"Components":[{"Type":"Page","Name":"root","Properties":{},"Actions":{},"Children":[]}],"RootProperties":{},"Imports":[],"Variables":{}}""";

    private const string EmptySemanticsJson =
        """{"SourceName":"x","Resources":{},"Styles":[],"Actions":{},"Templates":{},"Components":[],"RootProperties":{},"Imports":[],"Variables":{}}""";
}
