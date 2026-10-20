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
/// supplied by <see cref="CuiV1ArtifactReader"/> in Core (below the compiler) so stateless app
/// consumers depend only on Core/Runtime. This type retains its public writer/read surface so
/// existing producer callers and tests keep working; the read shim is thin and has no divergent
/// decoder.
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

    /// <summary>Public compatibility shim over the genuine Core reader. The Core reader throws
    /// <see cref="CuiV1AdmissionException"/>; this shim preserves the historical public contract
    /// (<see cref="InvalidDataException"/>) for existing producer/test callers. Returns the typed
    /// <see cref="CuiV1LoadedArtifact"/> from Core.</summary>
    public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        try
        {
            return CuiV1ArtifactReader.ReadArtifact(utf8);
        }
        catch (CuiV1AdmissionException admission)
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
