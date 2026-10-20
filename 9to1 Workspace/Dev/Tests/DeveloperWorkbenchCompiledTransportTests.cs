using System.Text;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>
/// The Build development workbench MUST consume the PACKAGED compiled CUI artifact with hard admission
/// checks — never a silent raw-source reparse fallback. These tests verify the real embedded bundle
/// round-trips through the shared codec, preserves the authored action surface, and that an
/// unsupported format/version/language/ABI/capability or a missing document fails before any scene
/// effects occur.
/// </summary>
public sealed class DeveloperWorkbenchCompiledTransportTests
{
    private const string WorkbenchSource = "UI/DeveloperWorkbench.cui";

    private static CuiCompiledArtifact ReadEmbeddedArtifact()
    {
        var artifact = CuiCompiledArtifactCodec.ReadArtifact(DeveloperWorkbenchCuiDocument.ReadEmbeddedArtifact());
        Assert.Equal(CuiCompiledArtifactCodec.FormatName, artifact.Format);
        Assert.Equal(CuiCompiledArtifactCodec.FormatVersion, artifact.FormatVersion);
        return artifact;
    }

    private static CuiCompiledArtifactDocument WorkbenchDocument(CuiCompiledArtifact artifact) =>
        Assert.Single(artifact.Documents, document => document.Source == WorkbenchSource);

    [Fact]
    public void Embedded_bundle_round_trips_to_semantics_and_preserves_all_authored_actions()
    {
        var artifact = ReadEmbeddedArtifact();
        var semantics = DeveloperWorkbenchCuiDocument.Load();
        Assert.NotNull(semantics);

        // The compiled bundle must carry the exact authored snapshot and the workbench document.
        var workbench = WorkbenchDocument(artifact);
        Assert.False(string.IsNullOrWhiteSpace(workbench.Compilation.SourceText));
        Assert.Contains("<Cui id=\"dev.project-workbench\"", workbench.Compilation.SourceText, StringComparison.Ordinal);

        // The compiled consumer path must preserve the full authored command/action surface.
        var commands = Descendants(semantics.Components)
            .SelectMany(value => value.Actions.Values)
            .Select(value => value.Name)
            .ToArray();
        string[] expected = ["dev.refresh", "dev.tree", "dev.git-status", "dev.git-diff", "dev.git-branches", "dev.open-source",
            "dev.search", "dev.preview-edit", "dev.apply-edit", "dev.revert-pass", "dev.run-command", "dev.run-tests",
            "dev.run-stage", "dev.create-branch", "dev.switch-branch"];
        Assert.Equal(expected.OrderBy(value => value), commands.OrderBy(value => value));
    }

    [Fact]
    public void Admission_rejects_unknown_artifact_format_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { Format = "legacy-format" };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Serialize(artifact)));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_format_version_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { FormatVersion = 999 };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Serialize(artifact)));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_missing_workbench_document_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { Documents = [] };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Serialize(artifact)));
        Assert.Equal("CUIR062", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_language_version_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = WorkbenchDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            "99", artifact.Metadata.RuntimeAbiVersion, artifact.Metadata.ComponentRegistryVersion, artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_incompatible_runtime_abi_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = WorkbenchDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            artifact.Metadata.LanguageVersion, "future-abi", artifact.Metadata.ComponentRegistryVersion, artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_required_capability_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = WorkbenchDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            artifact.Metadata.LanguageVersion, artifact.Metadata.RuntimeAbiVersion, artifact.Metadata.ComponentRegistryVersion,
            artifact.Metadata.RequiredCapabilities.Append("future-capability"));
        var replaced = artifact with
        {
            Documents = artifact.Documents.Select(d => ReferenceEquals(d, doc) ? doc with { Compilation = doc.Compilation with { Metadata = badMeta } } : d).ToArray()
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> nodes)
    {
        foreach (var node in nodes)
        { yield return node; foreach (var child in Descendants(node.Children)) yield return child; }
    }

    private static byte[] Serialize(CuiCompiledArtifact artifact) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(artifact, CuiCompiledArtifactCodec.Options));
}
