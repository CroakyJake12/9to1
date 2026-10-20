using System.Text;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// The Chat composer must consume the PACKAGED compiled CUI artifact with hard admission checks —
/// never a silent raw-source reparse fallback. These tests verify the real embedded bundle round-
/// trips through the shared codec and that an unsupported format/version/language/ABI/capability
/// fails before any scene effects occur.
/// </summary>
public sealed class ChatComposerCompiledTransportTests
{
    private const string ComposerSource = "UI/Chat/ChatComposer.cui";

    private static CuiCompiledArtifact ReadEmbeddedArtifact()
    {
        var artifact = CuiCompiledArtifactCodec.ReadArtifact(ChatComposerCuiDocument.ReadEmbeddedArtifact());
        Assert.Equal(CuiCompiledArtifactCodec.FormatName, artifact.Format);
        Assert.Equal(CuiCompiledArtifactCodec.FormatVersion, artifact.FormatVersion);
        return artifact;
    }

    private static CuiCompiledArtifactDocument ComposerDocument(CuiCompiledArtifact artifact) =>
        Assert.Single(artifact.Documents, document => document.Source == ComposerSource);

    [Fact]
    public void Embedded_bundle_round_trips_to_semantics_with_local_variable_and_toggle()
    {
        var artifact = ReadEmbeddedArtifact();
        var semantics = ChatComposerCuiDocument.Load();
        Assert.NotNull(semantics);

        // The compiled composer declares the local bool variable and the pure-local toggle.
        Assert.True(semantics.Variables.TryGetValue("FormattingOpen", out var variable));
        Assert.Equal("bool", variable!.Type);
        Assert.False(bool.Parse(variable.Value));

        var toggle = Assert.Single(semantics.Actions.Values, action => action.Name == "composer.toggle-formatting");
        Assert.Equal("FormattingOpen", toggle.LocalTarget);

        // The emitted metadata announces the local-state-toggle capability.
        var doc = ComposerDocument(artifact);
        Assert.Contains(doc.Compilation.Metadata.RequiredCapabilities, capability => capability == "local-state-toggle");
    }

    [Fact]
    public void Embedded_bundle_round_trips_to_choose_once_emoji_variable_and_capability()
    {
        var artifact = ReadEmbeddedArtifact();
        var semantics = ChatComposerCuiDocument.Load();
        Assert.NotNull(semantics);

        // The compiled composer declares the source-driven choose-once emoji trigger variable with
        // the binding candidate pool and the RandomOnce selection strategy.
        Assert.True(semantics.Variables.TryGetValue("EmojiIcon", out var variable));
        Assert.Equal("string", variable!.Type);
        Assert.Equal("RandomOnce", variable!.Selection);
        Assert.Equal("Composer.EmojiIconCandidates", variable!.Source);

        // The source-driven random-select slice must be announced as a required capability so older
        // consumers fail admission rather than silently drop the trigger.
        var doc = ComposerDocument(artifact);
        Assert.Contains(doc.Compilation.Metadata.RequiredCapabilities, capability => capability == "local-state-random-select");
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
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_incompatible_runtime_abi_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            artifact.Metadata.LanguageVersion, "future-abi", artifact.Metadata.ComponentRegistryVersion, artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_required_capability_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            artifact.Metadata.LanguageVersion, artifact.Metadata.RuntimeAbiVersion, artifact.Metadata.ComponentRegistryVersion,
            artifact.Metadata.RequiredCapabilities.Append("future-capability"));
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    private static byte[] Serialize(CuiCompiledArtifact artifact) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(artifact, CuiCompiledArtifactCodec.Options));
}
