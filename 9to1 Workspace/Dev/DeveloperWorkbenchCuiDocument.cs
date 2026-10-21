using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Dev;

/// <summary>
/// Loads the single shared canonical Build development workbench document from the genuine V1
/// compiled CUI artifact embedded in the Dev assembly (<c>CUI.Compilation.v1</c>). The workbench is
/// stateless, so its compiled bundle is a genuine V1 projection carrying full typed semantics.
///
/// The typed bundle is recovered through the Core <see cref="CuiV1ArtifactReader"/> (never a runtime
/// source reparse and never a compiler invocation), then admitted against this runtime's real
/// host-support contract for the WHOLE required bundle (language, component registry, minimum/current
/// ABI range, capability set) before any document is returned or scene effect occurs. A missing,
/// malformed, unsupported-version, unsupported-ABI or unsupported-capability artifact fails closed —
/// there is no raw-source fallback and no parallel codec.
///
/// Actual project, source, review, search, diff, command and build results continue to be supplied by
/// the canonical owner bindings; this document never infers authority from a login.
/// </summary>
public static class DeveloperWorkbenchCuiDocument
{
    public const string CompiledArtifactResource = "CUI.Compilation.v1";
    public const string CompiledSourceName = "UI/DeveloperWorkbench.cui";

    public static CuiDocument Load() => Load(ReadEmbeddedArtifact());

    /// <summary>Reads the embedded compiled V1 bundle. Exposed for identical admission/validation
    /// without a resource stream (used by tests to inject an artifact).</summary>
    public static byte[] ReadEmbeddedArtifact()
    {
        var assembly = typeof(DeveloperWorkbenchCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
            ?? throw new InvalidOperationException(
                $"Embedded compiled CUI artifact '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled V1 bundle.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Loads and hard-validates a compiled V1 bundle from raw bytes. A missing/unsupported
    /// format, version, language, ABI, registry or capability fails before any effects occur. The
    /// genuine Core reader reconstructs the typed canonical model without reparsing source; host
    /// support is admitted for the whole bundle before any document/scene effect.</summary>
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

        // Real host-support admission for the WHOLE required bundle (envelope + every document/closure)
        // against this runtime's supported language, component registry, minimum/current ABI range and
        // capability set, before any reconstruction or scene/control effect. Genuine stateless V1
        // (minimum ABI1) remains admitted on the stateful-aware runtime (current ABI2).
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
