using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CakeOS.Cui.Compiler;

namespace CakeOS.Cui;

/// <summary>
/// Distinct typed V2 compiled CUI artifact. It carries a full validated semantic payload per
/// document (never source text); the real consumer reconstructs the canonical typed model from it
/// without reparsing XML or invoking the compiler. Stateful documents require the stateful ABI.
/// </summary>
public sealed record CuiCompiledArtifact(
    string Format,
    int FormatVersion,
    string CompilerSha256,
    CuiCompilationMetadata Metadata,
    IReadOnlyList<CuiCompiledArtifactDocument> Documents);

/// <summary>One compiled document in a V2 artifact: its source identity, per-document metadata and
/// the full typed semantic payload plus source-map/dependency projections.</summary>
public sealed record CuiCompiledArtifactDocument(
    string Source,
    string Sha256,
    CuiCompilationMetadata Metadata,
    CuiV2Semantics Semantics,
    IReadOnlyList<CuiV2SourceMapEntry> SourceMap,
    CuiV2DependencyGraph Dependencies);

/// <summary>
/// Shared typed V2 codec. Emitters (Build.Tasks) and the real consumer agree on this wire format
/// without a dependency inversion through the compiler assembly. Admission is strict: unknown
/// formats/versions, value/node kinds, missing required fields, duplicates, malformed payloads and
/// metadata/payload contradictions are rejected before any effects.
/// </summary>
public static class CuiCompiledArtifactCodec
{
    public const string FormatName = "cui-build-compilation";
    public const int FormatVersion = 2;

    /// <summary>Distinct V2 resource identity (vs. genuine V1's "CUI.Compilation.v1").</summary>
    public const string ResourceLogicalName = "CUI.Compilation.v2";

    /// <summary>Genuine V1 resource identity, retained for the stateless compatibility path.</summary>
    public const string V1ResourceLogicalName = "CUI.Compilation.v1";

    /// <summary>Stateful runtime ABI required for V2 stateful documents.</summary>
    public const string V2RuntimeAbiVersion = "2";

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static byte[] WriteArtifact(CuiCompiledArtifact artifact) =>
        JsonSerializer.SerializeToUtf8Bytes(artifact, Options);

    /// <summary>Decodes and hard-validates a V2 artifact. Throws <see cref="CuiV2AdmissionException"/>
    /// with a stable code on any structural or consistency failure.</summary>
    public static CuiCompiledArtifact ReadArtifact(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        CuiCompiledArtifact artifact;
        try
        {
            artifact = JsonSerializer.Deserialize<CuiCompiledArtifact>(utf8, Options)
                ?? throw new InvalidDataException("The compiled CUI artifact is empty.");
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidDataException)
        {
            throw new CuiV2AdmissionException("CUIR070", $"The compiled CUI artifact payload is malformed: {error.Message}.");
        }

        Validate(artifact);
        return artifact;
    }

    /// <summary>Strict structural + envelope/document consistency admission.</summary>
    public static void Validate(CuiCompiledArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (!string.Equals(artifact.Format, FormatName, StringComparison.Ordinal))
            throw new CuiV2AdmissionException("CUIR060", $"Unsupported compiled CUI artifact format '{artifact.Format}'.");
        if (artifact.FormatVersion != FormatVersion)
            throw new CuiV2AdmissionException("CUIR061", $"Unsupported compiled CUI artifact format version {artifact.FormatVersion}; expected V2 ({FormatVersion}).");
        if (artifact.Documents is null)
            throw new CuiV2AdmissionException("CUIR072", "The compiled CUI artifact has no document list.");

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        string? maxAbi = null;
        var envelopeCaps = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in artifact.Documents)
        {
            if (document is null)
                throw new CuiV2AdmissionException("CUIR072", "The compiled CUI artifact contains a null document.");
            if (string.IsNullOrWhiteSpace(document.Source))
                throw new CuiV2AdmissionException("CUIR072", "A compiled document is missing its source identity.");
            if (string.IsNullOrWhiteSpace(document.Sha256))
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' is missing its source hash.");
            if (document.Metadata is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' is missing its compatibility metadata.");
            if (!sourceIds.Add(document.Source))
                throw new CuiV2AdmissionException("CUIR073", $"Duplicate compiled document source identity '{document.Source}'.");

            if (document.Semantics is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' has no semantic payload.");
            ValidateSemantics(document.Semantics, document.Source);

            if (document.SourceMap is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' has no source-map projection.");
            foreach (var entry in document.SourceMap)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.StableId))
                    throw new CuiV2AdmissionException("CUIR072",
                        $"Compiled document '{document.Source}' has an invalid source-map entry.");
            }

            if (document.Dependencies is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' has no dependency graph.");
            foreach (var dependency in document.Dependencies.Entries)
            {
                if (dependency is null || string.IsNullOrWhiteSpace(dependency.StableId)
                    || !ValidDependencyKinds.Contains(dependency.Kind))
                    throw new CuiV2AdmissionException("CUIR071",
                        $"Compiled document '{document.Source}' carries an unsupported dependency kind '{dependency?.Kind}'.");
            }

            var stateful = RequiresAbi2(document.Semantics);
            if (stateful && !string.Equals(document.Metadata.RuntimeAbiVersion, V2RuntimeAbiVersion, StringComparison.Ordinal))
                throw new CuiV2AdmissionException("CUIR074",
                    $"Compiled document '{document.Source}' carries stateful typed semantics but declares ABI '{document.Metadata.RuntimeAbiVersion}'; stateful documents require ABI {V2RuntimeAbiVersion}.");

            maxAbi = MaxAbi(maxAbi, document.Metadata.RuntimeAbiVersion);
            envelopeCaps.UnionWith(document.Metadata.RequiredCapabilities);
        }

        if (artifact.Metadata is null)
            throw new CuiV2AdmissionException("CUIR072", "The compiled CUI artifact is missing envelope metadata.");
        var expectedMaxAbi = maxAbi ?? artifact.Metadata.RuntimeAbiVersion;
        if (!string.Equals(artifact.Metadata.RuntimeAbiVersion, expectedMaxAbi, StringComparison.Ordinal))
            throw new CuiV2AdmissionException("CUIR075",
                $"The artifact envelope ABI '{artifact.Metadata.RuntimeAbiVersion}' does not equal the maximum document ABI '{expectedMaxAbi}'.");
        if (!artifact.Metadata.RequiredCapabilities.ToHashSet(StringComparer.Ordinal).SetEquals(envelopeCaps))
            throw new CuiV2AdmissionException("CUIR075",
                "The artifact envelope capability set does not equal the union of document requirements.");
    }

    /// <summary>Statefulness is determined by inspecting typed semantics (document-root local
    /// variables or pure-local actions), independent of any capability string.</summary>
    public static bool RequiresAbi2(CuiV2Semantics semantics)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        return (semantics.Variables is { Count: > 0 })
            || (semantics.Actions is not null && semantics.Actions.Values.Any(action => action.LocalTarget is not null));
    }

    private static readonly HashSet<string> ValidDependencyKinds = new(StringComparer.Ordinal)
    {
        CuiV2DependencyKind.Definition,
        CuiV2DependencyKind.Template,
        CuiV2DependencyKind.Resource,
        CuiV2DependencyKind.Variable,
        CuiV2DependencyKind.Action,
        CuiV2DependencyKind.Import,
    };

    private static string? MaxAbi(string? current, string candidate)
    {
        if (current is null) return candidate;
        if (int.TryParse(current, out var a) && int.TryParse(candidate, out var b))
            return b > a ? candidate : current;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0 ? candidate : current;
    }

    private static void ValidateSemantics(CuiV2Semantics semantics, string source)
    {
        if (semantics.Components is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has no component tree.");
        if (semantics.Resources is null || semantics.Actions is null || semantics.Templates is null
            || semantics.RootProperties is null || semantics.Imports is null || semantics.Variables is null
            || semantics.Styles is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' is missing a required semantic collection.");

        foreach (var style in semantics.Styles) ValidateStyle(style, source);

        var actionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, action) in semantics.Actions)
        {
            if (string.IsNullOrWhiteSpace(name) || action is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed action.");
            if (!actionNames.Add(name))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate action '{name}'.");
            if (action.Parameter is { } parameter) ValidateValue(parameter, source);
        }

        var variableNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, variable) in semantics.Variables)
        {
            if (string.IsNullOrWhiteSpace(name) || variable is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed variable.");
            if (!variableNames.Add(name))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate variable '{name}'.");
        }

        var resourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, resource) in semantics.Resources)
        {
            if (string.IsNullOrWhiteSpace(key) || resource is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed resource.");
            if (!resourceKeys.Add(key))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate resource '{key}'.");
            ValidateValue(resource.Value, source);
        }

        var templateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, template) in semantics.Templates)
        {
            if (string.IsNullOrWhiteSpace(name) || template is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed template.");
            if (!templateNames.Add(name))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate template '{name}'.");
            if (template.Parameters is null || template.Content is null)
                throw new CuiV2AdmissionException("CUIR072", $"Template '{name}' in '{source}' is missing parameters/content.");
            foreach (var component in template.Content) ValidateComponent(component, source);
        }

        foreach (var component in semantics.Components) ValidateComponent(component, source);
        foreach (var (_, value) in semantics.RootProperties) ValidateValue(value, source);
    }

    private static void ValidateComponent(CuiV2Component component, string source)
    {
        if (component is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a null component.");
        if (string.IsNullOrWhiteSpace(component.Type))
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a component without a type.");
        if (component.Span is null)
            throw new CuiV2AdmissionException("CUIR072",
                $"Component '{component.Type}' in '{source}' is missing its source span.");
        if (component.Classes is null || component.Properties is null || component.Actions is null
            || component.Children is null || component.ElseChildren is null || component.TextParts is null)
            throw new CuiV2AdmissionException("CUIR072",
                $"Component '{component.Type}' in '{source}' is missing a required collection.");

        foreach (var (_, value) in component.Properties) ValidateValue(value, source);
        foreach (var (_, actionRef) in component.Actions)
        {
            if (string.IsNullOrWhiteSpace(actionRef?.Name))
                throw new CuiV2AdmissionException("CUIR072",
                    $"Component '{component.Type}' in '{source}' has a null/unnamed action reference.");
        }
        if (component.Condition is { } condition) ValidateValue(condition.Test, source);
        if (component.Repeat is { } repeat)
        {
            ValidateValue(repeat.Source, source);
            ValidateValue(repeat.Key, source);
            if (repeat.Template is { } template) ValidateValue(template, source);
        }
        if (component.List is { } list) ValidateValue(list.Items, source);
        if (component.PropertyRegion is { } region) ValidateValue(region.Value, source);
        foreach (var part in component.TextParts)
        {
            if (part is null || string.IsNullOrWhiteSpace(part.Kind))
                throw new CuiV2AdmissionException("CUIR071",
                    $"Component '{component.Type}' in '{source}' has a null/unknown text part.");
            if (part.Kind == CuiV2TextPartKind.Expression && part.ExpressionValue is { } expressionValue)
                ValidateValue(expressionValue, source);
            else if (part.Kind != CuiV2TextPartKind.Literal)
                throw new CuiV2AdmissionException("CUIR071",
                    $"Component '{component.Type}' in '{source}' has unsupported text part kind '{part.Kind}'.");
        }
        foreach (var child in component.Children) ValidateComponent(child, source);
        foreach (var child in component.ElseChildren) ValidateComponent(child, source);
    }

    private static void ValidateStyle(CuiV2Style style, string source)
    {
        if (style is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a null style.");
        if (string.IsNullOrWhiteSpace(style.Selector) || style.Setters is null)
            throw new CuiV2AdmissionException("CUIR072",
                $"A style in '{source}' is missing its selector or setter list.");
        if (style.Span is null)
            throw new CuiV2AdmissionException("CUIR072", $"Style '{style.Selector}' in '{source}' is missing its source span.");
        foreach (var setter in style.Setters)
        {
            if (setter is null || string.IsNullOrWhiteSpace(setter.Property) || setter.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Style '{style.Selector}' in '{source}' has an invalid setter.");
            ValidateValue(setter.Value, source);
        }
    }

    private static void ValidateValue(CuiV2Value value, string source)
    {
        if (value is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a null value.");
        if (value.Span is null)
            throw new CuiV2AdmissionException("CUIR072", $"A value in '{source}' is missing its source span.");
        switch (value.Kind)
        {
            case CuiV2ValueKind.Literal:
                if (value.LiteralValue is null)
                    throw new CuiV2AdmissionException("CUIR072", $"A literal value in '{source}' is missing its string.");
                break;
            case CuiV2ValueKind.Binding:
                if (value.BindingPath is null || value.BindingMode is null)
                    throw new CuiV2AdmissionException("CUIR072", $"A binding value in '{source}' is missing its path/mode.");
                if (!Enum.IsDefined(typeof(CuiBindingMode), value.BindingMode.Value))
                    throw new CuiV2AdmissionException("CUIR071", $"Unknown CUI binding mode {value.BindingMode} in '{source}'.");
                break;
            case CuiV2ValueKind.Resource:
                if (string.IsNullOrWhiteSpace(value.ResourceKey))
                    throw new CuiV2AdmissionException("CUIR072", $"A resource value in '{source}' is missing its key.");
                break;
            case CuiV2ValueKind.Invalid:
                if (value.InvalidCode is null || value.InvalidRaw is null)
                    throw new CuiV2AdmissionException("CUIR072", $"An invalid value in '{source}' is missing its payload.");
                break;
            default:
                throw new CuiV2AdmissionException("CUIR071", $"Unsupported CUI value kind '{value.Kind}' in '{source}'.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new CuiCompilationMetadataConverter());
        return options;
    }

    /// <summary>Manual converter for <see cref="CuiCompilationMetadata"/>: its record constructor
    /// parameter (IEnumerable&lt;string&gt;) intentionally differs from the property type
    /// (IReadOnlyList&lt;string&gt;), which System.Text.Json's parameterized-constructor binding
    /// may reject. Reading explicitly keeps the shared contract simple and robust.</summary>
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
