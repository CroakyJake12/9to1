using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>
/// Loads the single shared canonical Chat composer component from the DISTINCT packaged V2 compiled
/// CUI artifact embedded in the Spaces assembly (<c>CUI.Compilation.v2</c>). The composer is a
/// stateful component (document-root local variables / pure-local toggles), so it consumes the
/// validated typed semantics directly — never a raw-source reparse or a compiler/source-text
/// reconstruction. A missing or rejected V2 artifact has no raw or V1 fallback.
/// </summary>
public static class ChatComposerCuiDocument
{
    public const string CompiledArtifactResource = CuiCompiledArtifactCodec.ResourceLogicalName;
    public const string CompiledSourceName = "UI/Chat/ChatComposer.cui";

    public static CuiDocument Load() => Load(ReadEmbeddedArtifact());

    /// <summary>Reads the embedded compiled V2 bundle. Exposed for identical admission/validation
    /// without a resource stream (used by tests to inject an artifact).</summary>
    public static byte[] ReadEmbeddedArtifact()
    {
        var assembly = typeof(ChatComposerCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
            ?? throw new InvalidOperationException(
                $"Embedded compiled CUI artifact '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled V2 bundle.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Loads and hard-validates a compiled V2 bundle from raw bytes. A missing/unsupported
    /// format, version, language, ABI, registry or capability fails before any effects occur. The
    /// shared codec already enforces envelope/document consistency, unknown-kind rejection and the
    /// stateful-requires-ABI2 rule from the typed semantics themselves.</summary>
    public static CuiDocument Load(byte[] artifactBytes)
    {
        CuiCompiledArtifact artifact;
        try
        {
            artifact = CuiCompiledArtifactCodec.ReadArtifact(artifactBytes);
        }
        catch (CuiV2AdmissionException admission)
        {
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                admission.Code, CuiDiagnosticSeverity.Error, admission.Message, Span));
        }

        var document = artifact.Documents.FirstOrDefault(entry =>
            string.Equals(entry.Source, CompiledSourceName, StringComparison.Ordinal))
            ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR062", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle does not contain '{CompiledSourceName}'.", Span));

        ValidateRuntimeCompatibility(document.Metadata);

        var semantics = document.Semantics.ToCuiDocument();
        if (semantics.Components.Count == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR063", CuiDiagnosticSeverity.Error,
                $"Compiled semantic model for '{CompiledSourceName}' is empty.", Span));
        return semantics;
    }

    /// <summary>Hard language/ABI/registry/capability admission. The composer is stateful, so it
    /// requires the stateful V2 ABI — an unsupported ABI/capability fails before any scene effects.</summary>
    private static void ValidateRuntimeCompatibility(CuiCompilationMetadata metadata)
    {
        if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(metadata.LanguageVersion))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR064", CuiDiagnosticSeverity.Error,
                $"Compiled CUI language version '{metadata.LanguageVersion}' is not supported by this runtime.", Span));
        if (!string.Equals(metadata.RuntimeAbiVersion, CuiCompiledArtifactCodec.V2RuntimeAbiVersion, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR065", CuiDiagnosticSeverity.Error,
                $"Compiled CUI runtime ABI '{metadata.RuntimeAbiVersion}' does not match the required stateful ABI '{CuiCompiledArtifactCodec.V2RuntimeAbiVersion}'.", Span));
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
