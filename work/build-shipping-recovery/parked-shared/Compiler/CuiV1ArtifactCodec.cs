using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Compiler;

/// <summary>
/// Genuine V1 compiled artifact writer plus a public compatibility shim over the Core reader.
/// V1 carries the FULL typed compiled projection (Components/Templates/SourceMap/Dependencies/
/// Semantics), never a source snapshot. Writing is owned here (the compiler producer); reading is
/// supplied by <see cref="CakeOS.Cui.CuiV1ArtifactReader"/> in Core (below the compiler) so
/// stateless app consumers depend only on Core/Runtime.
///
/// This type retains its full public API surface, including the nested <see cref="CuiV1LoadedDocument"/>
/// and <see cref="CuiV1LoadedArtifact"/> records and the <see cref="ReadArtifact(byte[])"/> return
/// type, so existing producer callers and compatibility tests keep working. The read shim is a thin
/// mapping adapter over the single Core decoder (no divergent second decoder). It preserves the
/// historical public exception contract (<see cref="InvalidDataException"/>) for producer/test
/// callers while the Core reader reports structured <see cref="CuiV1AdmissionException"/> codes.
/// </summary>
public static class CuiV1ArtifactCodec
{
    public const string FormatName = CakeOS.Cui.CuiV1ArtifactReader.FormatName;
    public const int FormatVersion = CakeOS.Cui.CuiV1ArtifactReader.FormatVersion;

    public sealed record CuiV1Artifact(
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("formatVersion")] int FormatVersion,
        [property: JsonPropertyName("compilerSha256")] string CompilerSha256,
        [property: JsonPropertyName("metadata")] CuiCompilationMetadata Metadata,
        [property: JsonPropertyName("documents")] IReadOnlyList<CuiV1Document> Documents);

    public sealed record CuiV1Document(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("compilation")] CuiCompiledDocument Compilation);

    public sealed record CuiV1LoadedDocument(
        string Source,
        string Sha256,
        CuiCompilationMetadata Metadata,
        CuiDocument Semantics);

    public sealed record CuiV1LoadedArtifact(
        string Format,
        int FormatVersion,
        string CompilerSha256,
        CuiCompilationMetadata Metadata,
        IReadOnlyList<CuiV1LoadedDocument> Documents);

    public sealed record CuiV1Compiled(CuiCompiledDocument Compilation, string Source, string Sha256);

    /// <summary>Writes a genuine V1 artifact (one or more full compiled projections), retaining the
    /// original outer shaping and aggregate envelope metadata (max ABI, union capabilities).</summary>
    public static byte[] WriteBundle(IReadOnlyList<CuiV1Compiled> documents, string compilerSha256)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) throw new ArgumentException("A V1 artifact requires at least one compiled document.");

        var envelope = AggregateMetadata(documents.Select(d => d.Compilation.Metadata));
        var artifact = new CuiV1Artifact(
            FormatName, FormatVersion, compilerSha256, envelope,
            documents.Select(d => new CuiV1Document(d.Source, d.Sha256, d.Compilation)).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(artifact, WriteOptions());
    }

    /// <summary>Public compatibility shim over the genuine Core reader. The Core reader returns the
    /// Core <see cref="CakeOS.Cui.CuiV1LoadedArtifact"/>; this shim maps it onto this type's public
    /// nested <see cref="CuiV1LoadedArtifact"/> and translates <see cref="CuiV1AdmissionException"/>
    /// into the historical public <see cref="InvalidDataException"/> contract.</summary>
    public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        try
        {
            var core = CakeOS.Cui.CuiV1ArtifactReader.ReadArtifact(utf8);
            return new CuiV1LoadedArtifact(
                core.Format, core.FormatVersion, core.CompilerSha256, core.Metadata,
                core.Documents.Select(d => new CuiV1LoadedDocument(d.Source, d.Sha256, d.Metadata, d.Semantics)).ToArray());
        }
        catch (CakeOS.Cui.CuiV1AdmissionException admission)
        {
            throw new InvalidDataException(admission.Message);
        }
    }

    private static CuiCompilationMetadata AggregateMetadata(IEnumerable<CuiCompilationMetadata> all)
    {
        var list = all.ToArray();
        var caps = new HashSet<string>(StringComparer.Ordinal);
        string? maxAbi = null;
        foreach (var metadata in list)
        {
            caps.UnionWith(metadata.RequiredCapabilities);
            maxAbi = MaxAbi(maxAbi, metadata.RuntimeAbiVersion);
        }
        var first = list[0];
        return new CuiCompilationMetadata(first.LanguageVersion, maxAbi ?? first.RuntimeAbiVersion,
            first.ComponentRegistryVersion, caps);
    }

    private static string? MaxAbi(string? current, string candidate)
    {
        if (current is null) return candidate;
        if (int.TryParse(current, out var a) && int.TryParse(candidate, out var b))
            return b > a ? candidate : current;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0 ? candidate : current;
    }

    private static JsonSerializerOptions WriteOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(AddPolymorphism);
        return new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = resolver,
            Converters = { new JsonStringEnumConverter() },
        };
    }

    private static void AddPolymorphism(JsonTypeInfo info)
    {
        Type[] derived = info.Type == typeof(CuiValue)
            ? [typeof(CuiLiteralValue), typeof(CuiBindingValue), typeof(CuiResourceValue), typeof(CuiInvalidValue)]
            : info.Type == typeof(CuiTextPart)
                ? [typeof(CuiLiteralTextPart), typeof(CuiExpressionTextPart)] : [];
        if (derived.Length == 0) return;
        info.PolymorphismOptions = new JsonPolymorphismOptions();
        foreach (var type in derived) info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(type, type.Name));
    }
}
