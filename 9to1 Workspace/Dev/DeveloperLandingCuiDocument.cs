using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Dev;

/// <summary>
/// Loads the single shared canonical Build landing document from the genuine V1 compiled CUI
/// artifact embedded in the Dev assembly (<c>CUI.Compilation.v1</c>). The landing is stateless, so
/// its compiled bundle is a genuine V1 projection carrying full typed semantics (folder Input,
/// create/open action and status bindings).
///
/// The typed bundle is recovered through the Core <see cref="CuiV1ArtifactReader"/> (never a runtime
/// source reparse and never a compiler invocation), then admitted against this runtime's real
/// host-support contract for the WHOLE required bundle (language, component registry, minimum/current
/// ABI range, capability set) before any document is returned or scene effect occurs. A missing,
/// malformed, unsupported-version, unsupported-ABI or unsupported-capability artifact fails closed —
/// there is no raw-source fallback and no parallel codec.
///
/// This replaces the legacy raw rich-parser reference markup for the real Build landing: every saved
/// project open, folder selection and manual create action is a typed compiled action on this
/// document, never a host code-behind control. The page owns only its real metadata/open task
/// bindings and CUI host.
/// </summary>
public static class DeveloperLandingCuiDocument
{
    public const string CompiledArtifactResource = "CUI.Compilation.v1";
    public const string CompiledSourceName = "UI/DeveloperLanding.cui";

    public static CuiDocument Load() => Load(ReadEmbeddedArtifact());

    /// <summary>Reads the embedded compiled V1 bundle. Exposed for identical admission/validation
    /// without a resource stream (used by tests to inject an artifact).</summary>
    public static byte[] ReadEmbeddedArtifact()
    {
        var assembly = typeof(DeveloperLandingCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
            ?? throw new InvalidOperationException(
                $"Embedded compiled CUI artifact '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled V1 bundle.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Loads and hard-validates a compiled V1 bundle from raw bytes. A missing/unsupported
    /// format, version, language, ABI, registry or capability fails before any effects occur.</summary>
    public static CuiDocument Load(byte[] artifactBytes)
    {
        CuiV1LoadedArtifact artifact;
        try
        {
            artifact = CuiV1ArtifactReader.ReadArtifact(artifactBytes);
        }
        catch (CuiV1AdmissionException admission)
        {
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                admission.Code, CuiDiagnosticSeverity.Error, admission.Message, Span));
        }

        CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, Span);

        var document = artifact.Documents.FirstOrDefault(entry =>
            string.Equals(entry.Source, CompiledSourceName, StringComparison.Ordinal))
            ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR062", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle does not contain '{CompiledSourceName}'.", Span));

        var semantics = document.Semantics;
        if (semantics.Components.Count == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR063", CuiDiagnosticSeverity.Error,
                $"Compiled semantic model for '{CompiledSourceName}' is empty.", Span));
        return semantics;
    }

    private static readonly CuiSourceSpan Span = CuiSourceSpan.At(CompiledSourceName, 1, 1);
}
