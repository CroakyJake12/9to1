using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Shell.VoiceVision;

/// <summary>
/// Loads the authored Voice &amp; Vision CUI documents (compact pill and full session surface) from
/// the PACKAGED genuine V1 compiled CUI bundle embedded in the shared Spaces assembly
/// (<c>CUI.Compilation.v1</c>). Voice &amp; Vision documents are stateless, so Spaces Build.Tasks emits
/// them as a genuine V1 artifact carrying the full typed compiled projection (Semantics), never a
/// source snapshot. This loader consumes that typed semantics directly — it never reparses source
/// XML, never invokes a compiler, and never falls back to a raw-source path.
/// </summary>
public static class VoiceVisionCuiDocument
{
    /// <summary>Shared compiled V1 bundle resource hosted by the Spaces assembly (the stateless
    /// V1 bundle that also carries the Voice documents). The V2 bundle is reserved for stateful
    /// documents (the shared composer) and is never read here.</summary>
    public const string CompiledArtifactResource = CuiCompiledArtifactCodec.V1ResourceLogicalName;

    public const string PillCompiledSourceName = "UI/VoiceVision/VoiceVisionPill.cui";
    public const string SessionCompiledSourceName = "UI/VoiceVision/VoiceVisionSession.cui";
    public const string HostShellCompiledSourceName = "UI/VoiceVision/VoiceVisionHostShell.cui";

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
    public static CuiDocument LoadHostShell() => LoadWithArtifact(HostShellCompiledSourceName, ReadEmbeddedArtifact());

    /// <summary>Reads the shared compiled V1 bundle from the Spaces assembly. Exposed for identical
    /// admission/validation without a resource stream (used by tests to inject an artifact).</summary>
    public static byte[] ReadEmbeddedArtifact()
    {
        var assembly = typeof(HavenOS.Apps.Spaces.Chat.ChatComposerCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(CompiledArtifactResource)
            ?? throw new InvalidOperationException(
                $"Embedded compiled CUI V1 bundle '{CompiledArtifactResource}' was not found. The project must emit the canonical compiled V1 bundle.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Loads and hard-validates one compiled Voice document from raw V1 bundle bytes. A
    /// missing/unsupported format, version, language, ABI, registry or capability fails before any
    /// effects occur; there is no raw-source fallback.</summary>
    public static CuiDocument LoadWithArtifact(string compiledSourceName, byte[] artifactBytes)
    {
        // Validate the envelope (format/version) structurally before any document/effect work, so a
        // non-V1 artifact fails with the exact format/version diagnostic instead of a generic parse
        // error. The genuine codec enforces the same rule; this keeps the envelope admission explicit.
        (string EnvelopeFormat, int EnvelopeVersion) = ReadEnvelope(artifactBytes);
        if (!string.Equals(EnvelopeFormat, CuiV1ArtifactCodec.FormatName, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR060", CuiDiagnosticSeverity.Error,
                $"Unsupported compiled CUI artifact format '{EnvelopeFormat}'.", Span));
        if (EnvelopeVersion != CuiV1ArtifactCodec.FormatVersion)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR061", CuiDiagnosticSeverity.Error,
                $"Unsupported compiled CUI artifact format version {EnvelopeVersion}.", Span));

        CuiV1ArtifactCodec.CuiV1LoadedArtifact artifact;
        try
        {
            artifact = CuiV1ArtifactCodec.ReadArtifact(artifactBytes);
        }
        catch (InvalidDataException error)
        {
            // Malformed V1 payload (bad JSON, missing documents, missing semantics) is refused
            // structurally before any scene effect.
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR060", CuiDiagnosticSeverity.Error, error.Message, Span));
        }

        // Validate the ENTIRE bundle before selecting the requested document. Every document and
        // sibling must satisfy hard language/ABI/registry/capability admission, so an unsupported
        // sibling can never be smuggled past this loader by selecting a clean peer.
        foreach (var entry in artifact.Documents)
            ValidateCompiledMetadata(entry.Metadata);

        var document = artifact.Documents.FirstOrDefault(entry =>
            string.Equals(entry.Source, compiledSourceName, StringComparison.Ordinal))
            ?? throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR062", CuiDiagnosticSeverity.Error,
                $"Compiled CUI bundle does not contain '{compiledSourceName}'.", Span));

        // The genuine V1 artifact carries the full typed compiled projection. Use it directly;
        // there is no source snapshot, no parser and no compiler reconstruction anywhere in this path.
        var semantics = document.Semantics;
        if (semantics.Components.Count == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR063", CuiDiagnosticSeverity.Error,
                $"Compiled semantic model for '{compiledSourceName}' is empty.", Span));
        return semantics;
    }

    /// <summary>Hard format/language/ABI/registry/capability admission. An old consumer or an artifact
    /// declaring an unsupported ABI/capability must fail before any scene effects occur.</summary>
    private static void ValidateCompiledMetadata(CuiCompilationMetadata metadata)
    {
        if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(metadata.LanguageVersion))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR064", CuiDiagnosticSeverity.Error,
                $"Compiled CUI language version '{metadata.LanguageVersion}' is not supported by this runtime.", Span));
        if (!string.Equals(metadata.RuntimeAbiVersion, CuiCompiledArtifactCodec.V1RuntimeAbiVersion, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR065", CuiDiagnosticSeverity.Error,
                $"Compiled CUI runtime ABI '{metadata.RuntimeAbiVersion}' does not match this stateless V1 ABI '{CuiCompiledArtifactCodec.V1RuntimeAbiVersion}'.", Span));
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

    /// <summary>Reads only the artifact envelope (format/version) from the raw bytes so the format/
    /// version admission can run before any full codec decode. Malformed JSON, a non-object body
    /// (array/primitive/null) and non-string/non-numeric envelope fields are normalized into the
    /// empty format / -1 version sentinel, which the caller refuses with the exact format/version
    /// diagnostic instead of leaking a parse-only exception.</summary>
    private static (string Format, int Version) ReadEnvelope(byte[] artifactBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(artifactBytes);
            var root = document.RootElement;
            // Only an object root carries a V1 envelope. An array/primitive/null body is not a
            // V1 artifact and must fail format admission rather than throw a raw parse error.
            if (root.ValueKind != JsonValueKind.Object) return ("", -1);
            var format = root.TryGetProperty("format", out var formatEl) && formatEl.ValueKind == JsonValueKind.String
                ? formatEl.GetString() ?? "" : "";
            // Only a JSON number carries a V1 envelope version. Guard the kind BEFORE TryGetInt32 so a
            // missing/string/object/array/null/overflow/unsupported value normalizes to the -1 sentinel
            // (refused with CUIR061) instead of leaking a raw InvalidOperationException.
            var version = root.TryGetProperty("formatVersion", out var versionEl)
                && versionEl.ValueKind == JsonValueKind.Number
                && versionEl.TryGetInt32(out var parsed)
                ? parsed : -1;
            return (format, version);
        }
        catch (JsonException)
        {
            return ("", -1);
        }
    }

    private static readonly CuiSourceSpan Span = CuiSourceSpan.At(PillCompiledSourceName, 1, 1);
}
