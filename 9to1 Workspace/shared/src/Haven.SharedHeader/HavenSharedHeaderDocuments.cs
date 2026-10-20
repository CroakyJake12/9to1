using System.IO;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.SharedHeader;

/// <summary>
/// Loads the authored Haven shared-header shell and notifications panels from the DISTINCT packaged
/// genuine V1 compiled CUI artifact embedded in this component assembly (<c>CUI.Compilation.v1</c>).
/// Both documents are stateless, so the canonical Build.Tasks/SDK emit a genuine V1 compiled
/// projection (format version 1 / ABI 1); this consumer reads it with
/// <see cref="CuiV1ArtifactCodec.ReadArtifact"/> and uses its reconstructed typed Semantics — never a
/// raw-source reparse or a compiler/source-text reconstruction. A missing or rejected artifact,
/// document, format/version, language/registry/ABI or capability fails before any scene/control effect.
/// </summary>
public static class HavenSharedHeaderDocuments
{
    /// <summary>Embedded compiled artifact resource identity returned by the CUI build task for the
    /// stateless V1 bundle. This is the exact identity the emission target reports; it is never
    /// invented or hardcoded to a different transport.</summary>
    public const string CompiledArtifactResource = CuiCompiledArtifactCodec.V1ResourceLogicalName;

    /// <summary>Compiled source identity of the authored notifications panel (the project-relative
    /// path the compiler recorded).</summary>
    public const string NotificationsSourceName = "Documents/HavenNotifications.cui";

    /// <summary>Compiled source identity of the authored shared-panel shell (the project-relative
    /// path the compiler recorded).</summary>
    public const string ShellSourceName = "Documents/HavenSharedPanelShell.cui";

    /// <summary>Loads the authored shared-panel shell (scene frame host + overlay panel host).</summary>
    public static CuiDocument LoadShell() => Load(ShellSourceName);

    /// <summary>Loads the authored Haven notifications panel.</summary>
    public static CuiDocument LoadNotifications() => Load(NotificationsSourceName);

    /// <summary>Reads the embedded compiled V1 bundle bytes. Exposed for identical admission/validation
    /// without a resource stream (used by tests to inject an artifact).</summary>
    public static byte[] ReadEmbeddedArtifact()
    {
        var assembly = typeof(HavenSharedHeaderDocuments).Assembly;
        using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
            ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR069", CuiDiagnosticSeverity.Error,
                $"The packaged compiled CUI bundle '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled bundle.",
                Span));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Loads the authored document with the given compiled source identity from the embedded
    /// genuine V1 artifact.</summary>
    public static CuiDocument Load(string sourceName) => Load(sourceName, ReadEmbeddedArtifact());

    /// <summary>Loads and hard-validates a genuine V1 compiled bundle from raw bytes. A missing/
    /// malformed/wrong-format/ABI/capability failure refuses before any effects occur. The whole
    /// bundle (envelope + every document/closure) is admitted, so an unsupported sibling cannot escape
    /// through selected-document checks. Exposed for identical admission/validation without a resource
    /// stream (used by tests to inject artifacts).</summary>
    public static CuiDocument Load(string sourceName, byte[] artifactBytes)
    {
        // Hard admission on the read (format/version/structure) BEFORE any reconstruction/effect.
        CuiV1ArtifactCodec.CuiV1LoadedArtifact artifact;
        try
        {
            artifact = CuiV1ArtifactCodec.ReadArtifact(artifactBytes);
        }
        catch (InvalidDataException error)
        {
            // Genuine V1 codec failures: wrong format (CUIR060), wrong version (CUIR061), otherwise a
            // malformed payload (CUIR070). All refuse before any effect.
            var code = error.Message.StartsWith("Unsupported compiled CUI artifact format '", StringComparison.Ordinal)
                ? "CUIR060"
                : error.Message.StartsWith("Unsupported compiled CUI artifact format version", StringComparison.Ordinal)
                    ? "CUIR061"
                    : "CUIR070";
            throw new CuiRuntimeLoadException(new CuiDiagnostic(code, CuiDiagnosticSeverity.Error, error.Message, Span));
        }

        // Whole-bundle host-support admission (envelope + every document/closure), mirroring the
        // canonical CuiRuntimeCompatibility.ValidateArtifactHostSupport contract for the genuine V1
        // shape. This is not a selected-document-only check; an unsupported sibling (ABI/capability/
        // registry/language) is refused before the requested document can load.
        ValidateArtifactHostSupport(artifact);

        var document = artifact.Documents.FirstOrDefault(entry =>
            string.Equals(entry.Source, sourceName, StringComparison.Ordinal))
            ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR062", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle does not contain '{sourceName}'.", Span));

        var semantics = document.Semantics;
        if (semantics.Components.Count == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR063", CuiDiagnosticSeverity.Error,
                $"Compiled semantic model for '{sourceName}' is empty.", Span));
        return semantics;
    }

    private static void ValidateArtifactHostSupport(CuiV1ArtifactCodec.CuiV1LoadedArtifact artifact)
    {
        RequireHostSupport(artifact.Metadata);
        foreach (var document in artifact.Documents)
            RequireHostSupport(document.Metadata);
    }

    private static void RequireHostSupport(CuiCompilationMetadata metadata)
    {
        if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(metadata.LanguageVersion))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR064", CuiDiagnosticSeverity.Error,
                $"Compiled CUI language version '{metadata.LanguageVersion}' is not supported by this runtime.", Span));
        if (!string.Equals(metadata.ComponentRegistryVersion, CuiControlRegistry.ComponentRegistryVersion, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR066", CuiDiagnosticSeverity.Error,
                $"Compiled CUI component registry '{metadata.ComponentRegistryVersion}' does not match the current registry '{CuiControlRegistry.ComponentRegistryVersion}'.", Span));
        if (!IsAbiSupported(metadata.RuntimeAbiVersion))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR065", CuiDiagnosticSeverity.Error,
                $"Compiled CUI runtime ABI '{metadata.RuntimeAbiVersion}' is not supported by this runtime (supported range {CuiRuntimeCompatibility.MinStatelessAbi}..{CuiRuntimeCompatibility.CurrentAbi}).", Span));
        foreach (var capability in metadata.RequiredCapabilities)
        {
            if (!CuiRuntimeCompatibility.Capabilities.Contains(capability))
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR067", CuiDiagnosticSeverity.Error,
                    $"Compiled CUI document requires unsupported capability '{capability}'.", Span));
        }
    }

    private static bool IsAbiSupported(string? abi) =>
        int.TryParse(abi, out var value)
        && value >= CuiRuntimeCompatibility.MinStatelessAbi
        && value <= CuiRuntimeCompatibility.CurrentAbi;

    private static readonly CuiSourceSpan Span = CuiSourceSpan.At("compiled.cui", 1, 1);
}
