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
/// The Voice &amp; Vision pill and full session must consume the PACKAGED compiled CUI artifact with
/// hard admission checks — never a silent raw-source reparse fallback. These tests verify the real
/// embedded bundle carries both Voice documents and that an unsupported format/version/language/ABI/
/// capability fails before any scene effects occur, exactly as the shared composer does.
/// </summary>
public sealed class VoiceVisionCuiCompiledTests
{
    private const string PillSource = "UI/VoiceVision/VoiceVisionPill.cui";
    private const string SessionSource = "UI/VoiceVision/VoiceVisionSession.cui";

    private static CuiCompiledArtifact ReadEmbeddedArtifact()
    {
        var artifact = CuiCompiledArtifactCodec.ReadArtifact(VoiceVisionCuiDocument.ReadEmbeddedArtifact());
        Assert.Equal(CuiCompiledArtifactCodec.FormatName, artifact.Format);
        Assert.Equal(CuiCompiledArtifactCodec.FormatVersion, artifact.FormatVersion);
        return artifact;
    }

    private static CuiCompiledArtifactDocument Document(CuiCompiledArtifact artifact, string source) =>
        Assert.Single(artifact.Documents, document => document.Source == source);

    [Fact]
    public void Embedded_bundle_carries_both_voice_documents()
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
        var artifact = ReadEmbeddedArtifact() with { Format = "legacy-format" };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => VoiceVisionCuiDocument.LoadWithArtifact(PillSource, Serialize(artifact)));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_unsupported_format_version_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { FormatVersion = 999 };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => VoiceVisionCuiDocument.LoadWithArtifact(PillSource, Serialize(artifact)));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Session_load_rejects_unknown_artifact_format_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { Format = "legacy-format" };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => VoiceVisionCuiDocument.LoadWithArtifact(SessionSource, Serialize(artifact)));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_unsupported_language_version_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = Document(artifact, PillSource);
        var badMeta = new CuiCompilationMetadata(
            "99", artifact.Metadata.RuntimeAbiVersion, artifact.Metadata.ComponentRegistryVersion, artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => VoiceVisionCuiDocument.LoadWithArtifact(PillSource, Serialize(replaced)));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Session_load_rejects_incompatible_runtime_abi_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = Document(artifact, SessionSource);
        var badMeta = new CuiCompilationMetadata(
            artifact.Metadata.LanguageVersion, "future-abi", artifact.Metadata.ComponentRegistryVersion, artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => VoiceVisionCuiDocument.LoadWithArtifact(SessionSource, Serialize(replaced)));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Pill_load_rejects_unsupported_required_capability_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = Document(artifact, PillSource);
        var badMeta = new CuiCompilationMetadata(
            artifact.Metadata.LanguageVersion, artifact.Metadata.RuntimeAbiVersion, artifact.Metadata.ComponentRegistryVersion,
            artifact.Metadata.RequiredCapabilities.Append("future-capability"));
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => VoiceVisionCuiDocument.LoadWithArtifact(PillSource, Serialize(replaced)));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    private static byte[] Serialize(CuiCompiledArtifact artifact) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(artifact, CuiCompiledArtifactCodec.Options));
}
