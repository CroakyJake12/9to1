using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Compiler;

/// <summary>
/// Genuine V1 compiled artifact writer/reader. V1 carries the FULL typed compiled projection
/// (Components/Templates/SourceMap/Dependencies/Semantics), never a source snapshot. This is the
/// legacy contract restored here — the dirty inherited WIP that stored Metadata + SourceText under
/// FormatVersion1 is not the V1 contract and is not reproduced.
/// </summary>
public static class CuiV1ArtifactCodec
{
    public const string FormatName = "cui-build-compilation";
    public const int FormatVersion = 1;

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

    /// <summary>Reads a genuine V1 artifact and reconstructs the semantic model without reparsing
    /// XML or invoking the compiler. A V2 artifact (or any non-V1 format/version) is rejected.</summary>
    public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8);
        }
        catch (Exception error) when (error is JsonException)
        {
            throw new InvalidDataException($"The V1 compiled CUI artifact is malformed: {error.Message}.");
        }

        var root = document.RootElement;
        var format = String(root, "format");
        if (!string.Equals(format, FormatName, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported compiled CUI artifact format '{format}'.");
        var version = root.TryGetProperty("formatVersion", out var versionEl) && versionEl.TryGetInt32(out var v) ? v : -1;
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported compiled CUI artifact format version {version}; V1 ({FormatVersion}) expected.");

        var compilerSha256 = String(root, "compilerSha256");
        var metadata = root.TryGetProperty("metadata", out var metadataEl) ? ReadMetadata(metadataEl) : null;
        var documents = new List<CuiV1LoadedDocument>();
        if (root.TryGetProperty("documents", out var docsEl) && docsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var docEl in docsEl.EnumerateArray())
            {
                var source = String(docEl, "source");
                var sha256 = String(docEl, "sha256");
                CuiDocument? semantics = null;
                if (docEl.TryGetProperty("compilation", out var compilationEl)
                    && compilationEl.TryGetProperty("Semantics", out var semanticsEl))
                {
                    semantics = CuiV1ModelReader.ReadDocument(semanticsEl);
                }
                var docMetadata = compilationEl.TryGetProperty("Metadata", out var compilationMetadataEl)
                    ? ReadMetadata(compilationMetadataEl) : metadata;
                documents.Add(new CuiV1LoadedDocument(source, sha256, docMetadata ?? throw new InvalidDataException("V1 document is missing metadata."), semantics ?? throw new InvalidDataException("V1 document is missing semantics.")));
            }
        }

        return new CuiV1LoadedArtifact(FormatName, FormatVersion, compilerSha256,
            metadata ?? throw new InvalidDataException("V1 artifact is missing envelope metadata."), documents);

        static string String(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";

        static CuiCompilationMetadata ReadMetadata(JsonElement element)
        {
            var capabilities = new List<string>();
            if (element.TryGetProperty("RequiredCapabilities", out var capsEl) && capsEl.ValueKind == JsonValueKind.Array)
                foreach (var item in capsEl.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) capabilities.Add(item.GetString() ?? "");
            return new CuiCompilationMetadata(
                String(element, "LanguageVersion"), String(element, "RuntimeAbiVersion"),
                String(element, "ComponentRegistryVersion"), capabilities);
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

/// <summary>Manual reader from the genuine V1 semantic JSON into the canonical typed model. It
/// reconstructs the model without source parsing and without invoking the compiler.</summary>
internal static class CuiV1ModelReader
{
    public static CuiDocument ReadDocument(JsonElement element)
    {
        var resources = new Dictionary<string, CuiResourceDefinition>(StringComparer.Ordinal);
        if (element.TryGetProperty("Resources", out var resourcesEl) && resourcesEl.ValueKind == JsonValueKind.Object)
            foreach (var pair in resourcesEl.EnumerateObject())
                resources[pair.Name] = new CuiResourceDefinition(String(pair.Value, "Key"), ReadValue(pair.Value.GetProperty("Value")), ReadSpan(pair.Value));

        var styles = new List<CuiStyleDefinition>();
        if (element.TryGetProperty("Styles", out var stylesEl) && stylesEl.ValueKind == JsonValueKind.Array)
            foreach (var styleEl in stylesEl.EnumerateArray())
            {
                var setters = new List<CuiStyleSetter>();
                if (styleEl.TryGetProperty("Setters", out var settersEl) && settersEl.ValueKind == JsonValueKind.Array)
                    foreach (var setterEl in settersEl.EnumerateArray())
                        setters.Add(new CuiStyleSetter(String(setterEl, "Property"), ReadValue(setterEl.GetProperty("Value")), ReadSpan(setterEl)));
                styles.Add(new CuiStyleDefinition(String(styleEl, "Selector"), setters, ReadSpan(styleEl)));
            }

        var actions = new Dictionary<string, CuiActionDefinition>(StringComparer.Ordinal);
        if (element.TryGetProperty("Actions", out var actionsEl) && actionsEl.ValueKind == JsonValueKind.Object)
            foreach (var pair in actionsEl.EnumerateObject())
            {
                var actionEl = pair.Value;
                var parameter = actionEl.TryGetProperty("Parameter", out var parameterEl) && parameterEl.ValueKind == JsonValueKind.Object
                    ? ReadValue(parameterEl) : null;
                actions[pair.Name] = new CuiActionDefinition(String(actionEl, "Name"), String(actionEl, "Command"),
                    parameter, ReadSpan(actionEl), NullableString(actionEl, "LocalTarget"));
            }

        var templates = new Dictionary<string, CuiTemplateDefinition>(StringComparer.Ordinal);
        if (element.TryGetProperty("Templates", out var templatesEl) && templatesEl.ValueKind == JsonValueKind.Object)
            foreach (var pair in templatesEl.EnumerateObject())
            {
                var templateEl = pair.Value;
                templates[pair.Name] = new CuiTemplateDefinition(String(templateEl, "Name"),
                    Array(templateEl, "Parameters"), ReadComponents(templateEl, "Content"), ReadSpan(templateEl));
            }

        var components = ReadComponents(element, "Components");

        var rootProperties = new Dictionary<string, CuiValue>(StringComparer.Ordinal);
        if (element.TryGetProperty("RootProperties", out var rootEl) && rootEl.ValueKind == JsonValueKind.Object)
            foreach (var pair in rootEl.EnumerateObject())
                rootProperties[pair.Name] = ReadValue(pair.Value);

        var imports = new List<CuiImportDefinition>();
        if (element.TryGetProperty("Imports", out var importsEl) && importsEl.ValueKind == JsonValueKind.Array)
            foreach (var importEl in importsEl.EnumerateArray())
                imports.Add(new CuiImportDefinition(String(importEl, "Source"), NullableString(importEl, "Alias"), ReadSpan(importEl)));

        var variables = new Dictionary<string, CuiVariableDeclaration>(StringComparer.Ordinal);
        if (element.TryGetProperty("Variables", out var variablesEl) && variablesEl.ValueKind == JsonValueKind.Object)
            foreach (var pair in variablesEl.EnumerateObject())
            {
                var variableEl = pair.Value;
                variables[pair.Name] = new CuiVariableDeclaration(String(variableEl, "Name"), String(variableEl, "Type"),
                    String(variableEl, "Value"), ReadSpan(variableEl), NullableString(variableEl, "Source"), NullableString(variableEl, "Selection"));
            }

        return new CuiDocument(String(element, "SourceName"), resources, styles, actions, templates, components, ReadSpan(element))
        {
            RootProperties = rootProperties,
            Imports = imports,
            Variables = variables,
        };
    }

    public static CuiComponent ReadComponent(JsonElement element)
    {
        var properties = new Dictionary<string, CuiValue>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("Properties", out var propertiesEl) && propertiesEl.ValueKind == JsonValueKind.Object)
            foreach (var property in propertiesEl.EnumerateObject())
                properties[property.Name] = ReadValue(property.Value);

        var actions = new Dictionary<string, CuiActionReference>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("Actions", out var actionsEl) && actionsEl.ValueKind == JsonValueKind.Object)
            foreach (var action in actionsEl.EnumerateObject())
                actions[action.Name] = new CuiActionReference(String(action.Value, "Name"), ReadSpan(action.Value));

        CuiCondition? condition = null;
        if (element.TryGetProperty("Condition", out var conditionEl) && conditionEl.ValueKind == JsonValueKind.Object)
            condition = new CuiCondition(ReadValue(conditionEl.GetProperty("Test")), Bool(conditionEl, "Negate"),
                ReadSpan(conditionEl), Bool(conditionEl, "IsLive", true));

        CuiRepeatDefinition? repeat = null;
        if (element.TryGetProperty("Repeat", out var repeatEl) && repeatEl.ValueKind == JsonValueKind.Object)
            repeat = new CuiRepeatDefinition(ReadValue(repeatEl.GetProperty("Source")), String(repeatEl, "ItemName"),
                ReadValue(repeatEl.GetProperty("Key")), ReadSpan(repeatEl),
                repeatEl.TryGetProperty("Template", out var templateEl) && templateEl.ValueKind == JsonValueKind.Object ? ReadValue(templateEl) : null);

        CuiListDefinition? list = null;
        if (element.TryGetProperty("List", out var listEl) && listEl.ValueKind == JsonValueKind.Object)
            list = new CuiListDefinition(ReadValue(listEl.GetProperty("Items")), String(listEl, "ItemTemplate"),
                NullableString(listEl, "EmptyTemplate"), ReadSpan(listEl));

        CuiPropertyRegionDefinition? propertyRegion = null;
        if (element.TryGetProperty("PropertyRegion", out var regionEl) && regionEl.ValueKind == JsonValueKind.Object)
            propertyRegion = new CuiPropertyRegionDefinition(String(regionEl, "PropertyName"), ReadValue(regionEl.GetProperty("Value")), ReadSpan(regionEl));

        var textParts = new List<CuiTextPart>();
        if (element.TryGetProperty("TextParts", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
            foreach (var part in partsEl.EnumerateArray())
            {
                var typeName = part.TryGetProperty("$type", out var typeEl) ? typeEl.GetString() : null;
                textParts.Add(typeName == nameof(CuiExpressionTextPart)
                    ? new CuiExpressionTextPart(ReadValue(part.GetProperty("Value")), ReadSpan(part))
                    : new CuiLiteralTextPart(String(part, "Value"), ReadSpan(part)));
            }

        return new CuiComponent(
            String(element, "Type"), NullableString(element, "Name"), Array(element, "Classes"), properties, actions,
            ReadComponents(element, "Children"), condition, list, ReadSpan(element), NullableString(element, "DefaultTheme"),
            repeat, ReadComponents(element, "ElseChildren"), Array(element, "Groups"), Bool(element, "IsDefinition"), propertyRegion)
        {
            Text = String(element, "Text"),
            TextParts = textParts,
        };
    }

    private static IReadOnlyList<CuiComponent> ReadComponents(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
            return Array.Empty<CuiComponent>();
        var result = new List<CuiComponent>();
        foreach (var child in list.EnumerateArray()) result.Add(ReadComponent(child));
        return result;
    }

    private static CuiValue ReadValue(JsonElement element)
    {
        var typeName = element.TryGetProperty("$type", out var typeEl) ? typeEl.GetString() : null;
        return typeName switch
        {
            nameof(CuiLiteralValue) => new CuiLiteralValue(String(element, "Value"), ReadSpan(element)),
            nameof(CuiBindingValue) => new CuiBindingValue(String(element, "Path"), ParseBindingMode(String(element, "Mode")),
                NullableString(element, "Fallback"), ReadSpan(element), NullableString(element, "TargetType")),
            nameof(CuiResourceValue) => new CuiResourceValue(String(element, "Key"), ReadSpan(element)),
            nameof(CuiInvalidValue) => new CuiInvalidValue(String(element, "RawValue"), String(element, "DiagnosticCode"),
                String(element, "Message"), ReadSpan(element)),
            _ => throw new InvalidDataException($"Unknown V1 value discriminator '{typeName}'."),
        };
    }

    private static CuiBindingMode ParseBindingMode(string value) => value switch
    {
        "Invalid" => CuiBindingMode.Invalid,
        "OneWay" => CuiBindingMode.OneWay,
        "TwoWay" => CuiBindingMode.TwoWay,
        "OneTime" => CuiBindingMode.OneTime,
        _ => throw new InvalidDataException($"Unknown CUI binding mode '{value}'."),
    };

    private static CuiSourceSpan ReadSpan(JsonElement element)
    {
        var source = String(element, "SourceName");
        (int line, int column) Pos(string property) =>
            element.TryGetProperty(property, out var pos) && pos.ValueKind == JsonValueKind.Object
                ? (Int(pos, "Line"), Int(pos, "Column")) : (1, 1);
        var (sl, sc) = Pos("Start");
        var (el, ec) = Pos("End");
        return new CuiSourceSpan(source, new(sl, sc), new(el, ec));
    }

    private static string String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool Bool(JsonElement element, string property, bool fallback = false) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static IReadOnlyList<string> Array(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var result = new List<string>();
        foreach (var item in list.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) result.Add(item.GetString() ?? "");
        return result;
    }
}
