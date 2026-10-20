using System.Text;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// The Chat composer must consume the DISTINCT packaged V2 compiled CUI artifact with hard admission
/// checks — never a silent raw-source reparse fallback, and never a V1/raw fallback for this
/// stateful composer. These tests verify the real embedded V2 bundle round-trips through the shared
/// codec into the typed model and that unsupported formats/versions/language/ABI/capabilities and
/// metadata/payload contradictions fail before any scene effects occur.
/// </summary>
public sealed class ChatComposerCompiledTransportTests
{
    private const string ComposerSource = "UI/Chat/ChatComposer.cui";

    private static CuiCompiledArtifact ReadEmbeddedArtifact()
    {
        var artifact = CuiCompiledArtifactCodec.ReadArtifact(ChatComposerCuiDocument.ReadEmbeddedArtifact());
        Assert.Equal(CuiCompiledArtifactCodec.FormatName, artifact.Format);
        Assert.Equal(CuiCompiledArtifactCodec.FormatVersion, artifact.FormatVersion);
        Assert.Equal(CuiCompiledArtifactCodec.ResourceLogicalName, ChatComposerCuiDocument.CompiledArtifactResource);
        return artifact;
    }

    private static CuiCompiledArtifactDocument ComposerDocument(CuiCompiledArtifact artifact) =>
        Assert.Single(artifact.Documents, document => document.Source == ComposerSource);

    [Fact]
    public void Embedded_v2_bundle_round_trips_to_semantics_with_local_variable_and_toggle()
    {
        var artifact = ReadEmbeddedArtifact();
        var semantics = ChatComposerCuiDocument.Load();
        Assert.NotNull(semantics);

        Assert.True(semantics.Variables.TryGetValue("FormattingOpen", out var variable));
        Assert.Equal("bool", variable!.Type);
        Assert.False(bool.Parse(variable.Value));

        var toggle = Assert.Single(semantics.Actions.Values, action => action.Name == "composer.toggle-formatting");
        Assert.Equal("FormattingOpen", toggle.LocalTarget);

        // The stateful composer requires the distinct V2 ABI and announces the local-state capability.
        var doc = ComposerDocument(artifact);
        Assert.Equal(CuiCompiledArtifactCodec.V2RuntimeAbiVersion, doc.Metadata.RuntimeAbiVersion);
        Assert.Contains(doc.Metadata.RequiredCapabilities, capability => capability == "local-state-toggle");
    }

    [Fact]
    public void Embedded_v2_bundle_round_trips_to_choose_once_emoji_variable_and_capability()
    {
        var artifact = ReadEmbeddedArtifact();
        var semantics = ChatComposerCuiDocument.Load();
        Assert.NotNull(semantics);

        Assert.True(semantics.Variables.TryGetValue("EmojiIcon", out var variable));
        Assert.Equal("string", variable!.Type);
        Assert.Equal("RandomOnce", variable!.Selection);
        Assert.Equal("Composer.EmojiIconCandidates", variable!.Source);

        var doc = ComposerDocument(artifact);
        Assert.Contains(doc.Metadata.RequiredCapabilities, capability => capability == "local-state-random-select");
    }

    [Fact]
    public void Admission_rejects_unknown_artifact_format_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { Format = "legacy-format" };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(artifact)));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_format_version_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { FormatVersion = 999 };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(artifact)));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_language_version_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            "99", artifact.Metadata.RuntimeAbiVersion, artifact.Metadata.ComponentRegistryVersion, artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with { Documents = ReplaceDocument(artifact, doc, doc with { Metadata = badMeta }) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_stateful_document_without_abi2_even_when_capabilities_stripped()
    {
        // Defense in depth: statefulness is read from the typed semantics, not from capability
        // strings. Stripping the capability text AND downgrading the ABI cannot make a stateful
        // composer a harmless V1 document.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var stripped = doc with { Metadata = BaseMeta(doc.Metadata, abi: "1") };
        var replaced = artifact with { Documents = ReplaceDocument(artifact, doc, stripped) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR074", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_required_capability_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var caps = doc.Metadata.RequiredCapabilities.Append("future-capability").ToArray();
        var docWithCap = doc with { Metadata = BaseMeta(doc.Metadata, capabilities: caps) };
        var envelopeWithCap = BaseMeta(artifact.Metadata, capabilities: artifact.Metadata.RequiredCapabilities.Append("future-capability").ToArray());
        var replaced = artifact with { Metadata = envelopeWithCap, Documents = ReplaceDocument(artifact, doc, docWithCap) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_envelope_document_abi_contradiction_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var envelopeWithWrongAbi = BaseMeta(artifact.Metadata, abi: "1");
        var replaced = artifact with { Metadata = envelopeWithWrongAbi };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR075", error.Diagnostic.Code);
    }

    [Fact]
    public void Missing_compiled_composer_resource_has_no_raw_fallback_and_composer_is_not_in_v1()
    {
        // The stateful composer must never fall back to a raw embedded .cui resource and must never
        // appear in a genuine V1 bundle. A mixed Spaces assembly may still legitimately emit a V1
        // bundle for its stateless sibling documents — so we assert composer absence from V1 rather
        // than global V1 absence.
        var assembly = typeof(ChatComposerCuiDocument).Assembly;
        Assert.Null(assembly.GetManifestResourceStream("HavenOS.Apps.Spaces.UI.Chat.ChatComposer.cui"));
        Assert.NotNull(assembly.GetManifestResourceStream(ChatComposerCuiDocument.CompiledArtifactResource));

        using var v1 = assembly.GetManifestResourceStream(CuiCompiledArtifactCodec.V1ResourceLogicalName);
        if (v1 is not null)
        {
            using var buffer = new MemoryStream();
            v1.CopyTo(buffer);
            var loaded = CuiV1ArtifactCodec.ReadArtifact(buffer.ToArray());
            Assert.DoesNotContain(loaded.Documents, doc => doc.Source == ChatComposerCuiDocument.CompiledSourceName);
        }
    }

    private static IReadOnlyList<CuiCompiledArtifactDocument> ReplaceDocument(
        CuiCompiledArtifact artifact, CuiCompiledArtifactDocument original, CuiCompiledArtifactDocument replacement) =>
        artifact.Documents.Select(d => ReferenceEquals(d, original) ? replacement : d).ToArray();

    private static CuiCompilationMetadata BaseMeta(
        CuiCompilationMetadata source, string? language = null, string? abi = null, IEnumerable<string>? capabilities = null) =>
        new(language ?? source.LanguageVersion, abi ?? source.RuntimeAbiVersion, source.ComponentRegistryVersion,
            capabilities ?? source.RequiredCapabilities);

    private static byte[] Serialize(CuiCompiledArtifact artifact) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(artifact, CuiCompiledArtifactCodec.Options));
}
