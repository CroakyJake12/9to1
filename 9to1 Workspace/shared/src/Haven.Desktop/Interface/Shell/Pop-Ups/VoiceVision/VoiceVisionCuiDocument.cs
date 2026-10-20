using System;
using System.IO;
using System.Linq;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Shell.VoiceVision;

/// <summary>
/// Loads the authored Voice &amp; Vision CUI documents (compact pill and full session surface) from
/// the PACKAGED compiled CUI artifact embedded in the shared Spaces assembly (<c>CUI.Compilation.v1</c>),
/// using the same canonical admission path as the shared composer. The authored .cui sources are
/// compiled into the bundle by the CUI compiler build targets and recorded under their project-relative
/// source names; this loader never falls back to a silent raw-source reparse.
/// </summary>
public static class VoiceVisionCuiDocument
{
    /// <summary>Shared compiled CUI bundle resource hosted by the Spaces assembly (same bundle that
    /// carries the shared composer). The Voice &amp; Vision documents are compiled into it.</summary>
    public const string CompiledArtifactResource = "CUI.Compilation.v1";

    public const string PillCompiledSourceName = "UI/VoiceVision/VoiceVisionPill.cui";
    public const string SessionCompiledSourceName = "UI/VoiceVision/VoiceVisionSession.cui";

    /// <summary>Builds the bounded registry the Voice &amp; Vision documents render against. The only
    /// resolver is the canonical symbolic icon catalog; no file/network/arbitrary asset access.</summary>
    public static CuiControlRegistry CreateControlRegistry()
    {
        var registry = new CuiControlRegistry();
        NineToOne.Web.BrowserCanonicalIcons.Register(registry);
        return registry;
    }

    public static CuiDocument LoadPill() => LoadWithArtifact(PillCompiledSourceName, ReadEmbeddedArtifact());
    public static CuiDocument LoadSession() => LoadWithArtifact(SessionCompiledSourceName, ReadEmbeddedArtifact());

    /// <summary>Reads the shared compiled bundle from the Spaces assembly. Exposed for identical
    /// admission/validation without a resource stream (used by tests to inject an artifact).</summary>
    public static byte[] ReadEmbeddedArtifact()
    {
        var assembly = typeof(HavenOS.Apps.Spaces.Chat.ChatComposerCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
            ?? throw new InvalidOperationException(
                $"Embedded compiled CUI artifact '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled bundle.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Loads and hard-validates one compiled Voice document from raw bundle bytes. A
    /// missing/unsupported format, version, language, ABI, registry or capability fails before any
    /// effects occur; there is no raw-source fallback.</summary>
    public static CuiDocument LoadWithArtifact(string compiledSourceName, byte[] artifactBytes)
    {
        var artifact = CuiCompiledArtifactCodec.ReadArtifact(artifactBytes);

        if (!string.Equals(artifact.Format, CuiCompiledArtifactCodec.FormatName, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR060", CuiDiagnosticSeverity.Error,
                $"Unsupported compiled CUI artifact format '{artifact.Format}'.", Span));
        if (artifact.FormatVersion != CuiCompiledArtifactCodec.FormatVersion)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR061", CuiDiagnosticSeverity.Error,
                $"Unsupported compiled CUI artifact format version {artifact.FormatVersion}.", Span));

        var document = artifact.Documents.FirstOrDefault(entry =>
            string.Equals(entry.Source, compiledSourceName, StringComparison.Ordinal))
            ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR062", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle does not contain '{compiledSourceName}'.", Span));

        ValidateCompiledMetadata(document.Compilation.Metadata);

        // The artifact owns a validated source snapshot; reconstruct the loadable model through the
        // canonical parser only AFTER every admission check above passed. No loose raw-source path.
        var parser = new CuiRichParser();
        var semantics = parser.Parse(document.Compilation.SourceText, compiledSourceName);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR068", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle for '{compiledSourceName}' failed snapshot revalidation.", Span));
        if (semantics.Components.Count == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR063", CuiDiagnosticSeverity.Error,
                $"Compiled semantic model for '{compiledSourceName}' is empty.", Span));
        return semantics;
    }

    /// <summary>Hard format/language/ABI/capability admission. An old consumer or an artifact
    /// declaring an unsupported ABI/capability must fail before any scene effects occur.</summary>
    private static void ValidateCompiledMetadata(CuiCompilationMetadata metadata)
    {
        if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(metadata.LanguageVersion))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR064", CuiDiagnosticSeverity.Error,
                $"Compiled CUI language version '{metadata.LanguageVersion}' is not supported by this runtime.", Span));
        if (!string.Equals(metadata.RuntimeAbiVersion, CuiRuntimeCompatibility.RuntimeAbiVersion, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR065", CuiDiagnosticSeverity.Error,
                $"Compiled CUI runtime ABI '{metadata.RuntimeAbiVersion}' does not match this runtime ABI '{CuiRuntimeCompatibility.RuntimeAbiVersion}'.", Span));
        if (!string.Equals(metadata.ComponentRegistryVersion, CuiControlRegistry.ComponentRegistryVersion, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR066", CuiDiagnosticSeverity.Error,
                $"Compiled CUI component registry '{metadata.ComponentRegistryVersion}' does not match the current registry '{CuiControlRegistry.ComponentRegistryVersion}'.", Span));
        foreach (var capability in metadata.RequiredCapabilities)
        {
            if (!CuiRuntimeCompatibility.Capabilities.Contains(capability))
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR067", CuiDiagnosticSeverity.Error,
                    $"Compiled CUI document requires unsupported capability '{capability}'.", Span));
        }
    }

    private static readonly CuiSourceSpan Span = CuiSourceSpan.At(PillCompiledSourceName, 1, 1);
}
