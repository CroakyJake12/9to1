using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CakeOS.Cui.Compiler;

namespace CakeOS.Cui;

/// <summary>The subset of the compiled document a runtime consumer needs: the compatibility metadata
/// and the validated source snapshot. The canonical parser reconstructs the loadable model from the
/// snapshot only AFTER every admission check passes — a missing/unsupported artifact never falls back
/// to loose raw-source loading, and the runtime never depends on a second compiler.</summary>
public sealed record CuiCompiledArtifactCompilation(
    CuiCompilationMetadata Metadata,
    string SourceText);

public sealed record CuiCompiledArtifactDocument(
    string Source,
    string Sha256,
    CuiCompiledArtifactCompilation Compilation);

public sealed record CuiCompiledArtifact(
    string Format,
    int FormatVersion,
    string CompilerSha256,
    CuiCompilationMetadata Metadata,
    IReadOnlyList<CuiCompiledArtifactDocument> Documents);

/// <summary>Shared codec for the compiled CUI build artifact. The serializer configuration and
/// format contract live in Core so the Build.Tasks emitter and runtime consumers agree without a
/// dependency inversion through the compiler assembly.</summary>
public static class CuiCompiledArtifactCodec
{
    public const string FormatName = "cui-build-compilation";
    public const int FormatVersion = 1;

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        return new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = resolver,
            Converters = { new JsonStringEnumConverter(), new CuiCompilationMetadataConverter() }
        };
    }

    public static CuiCompiledArtifact ReadArtifact(byte[] utf8) =>
        JsonSerializer.Deserialize<CuiCompiledArtifact>(utf8, Options)
        ?? throw new InvalidDataException("The compiled CUI artifact is empty.");

    /// <summary>Manual converter for <see cref="CuiCompilationMetadata"/>: its record constructor
    /// parameter (IEnumerable&lt;string&gt;) intentionally differs from the property type
    /// (IReadOnlyList&lt;string&gt;), which System.Text.Json's parameterized-constructor binding
    /// rejects. Reading explicitly keeps the shared contract simple and robust.</summary>
    private sealed class CuiCompilationMetadataConverter : JsonConverter<CuiCompilationMetadata>
    {
        public override CuiCompilationMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            string Language(string name) => root.TryGetProperty(name, out var el) ? el.GetString() ?? "" : "";
            var capabilities = new List<string>();
            if (root.TryGetProperty("RequiredCapabilities", out var capsEl) && capsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in capsEl.EnumerateArray())
                    capabilities.Add(item.GetString() ?? "");
            }
            return new CuiCompilationMetadata(
                Language("LanguageVersion"), Language("RuntimeAbiVersion"), Language("ComponentRegistryVersion"), capabilities);
        }

        public override void Write(Utf8JsonWriter writer, CuiCompilationMetadata value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("LanguageVersion", value.LanguageVersion);
            writer.WriteString("RuntimeAbiVersion", value.RuntimeAbiVersion);
            writer.WriteString("ComponentRegistryVersion", value.ComponentRegistryVersion);
            writer.WriteStartArray("RequiredCapabilities");
            foreach (var capability in value.RequiredCapabilities) writer.WriteStringValue(capability);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
