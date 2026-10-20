using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>
/// Loads the single shared canonical Chat composer component from the PACKAGED compiled CUI artifact
/// embedded in the Spaces assembly (<c>CUI.Compilation.v1</c>). The composer is a stateful component
/// (document-root local variables / pure-local toggles), so it must consume the validated compiled
/// semantics with metadata/capability checks, never a silent raw-source reparse fallback.
/// </summary>
public static class ChatComposerCuiDocument
{
    public const string CompiledArtifactResource = "CUI.Compilation.v1";
    public const string CompiledSourceName = "UI/Chat/ChatComposer.cui";

    public static CuiDocument Load() => Load(ReadEmbeddedArtifact());

    /// <summary>Reads the embedded compiled bundle into the shared codec. Exposed for identical
    /// admission/validation without a resource stream (used by tests to inject an artifact).</summary>
    public static byte[] ReadEmbeddedArtifact()
    {
        var assembly = typeof(ChatComposerCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
            ?? throw new InvalidOperationException(
                $"Embedded compiled CUI artifact '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled bundle.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Loads and hard-validates a compiled bundle from raw bytes. A missing/unsupported
    /// format, version, language, ABI, registry or capability fails before any effects occur.</summary>
    public static CuiDocument Load(byte[] artifactBytes)
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
            string.Equals(entry.Source, CompiledSourceName, StringComparison.Ordinal))
            ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR062", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle does not contain '{CompiledSourceName}'.", Span));

        ValidateCompiledMetadata(document.Compilation.Metadata);

        // The artifact owns a validated source snapshot; reconstruct the loadable model through the
        // canonical parser only AFTER every admission check above passed. No loose raw-source path.
        var parser = new CuiRichParser();
        var semantics = parser.Parse(document.Compilation.SourceText, CompiledSourceName);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR068", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle for '{CompiledSourceName}' failed snapshot revalidation.", Span));
        if (semantics.Components.Count == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR063", CuiDiagnosticSeverity.Error,
                $"Compiled semantic model for '{CompiledSourceName}' is empty.", Span));
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

    private static readonly CuiSourceSpan Span = CuiSourceSpan.At(CompiledSourceName, 1, 1);
}
