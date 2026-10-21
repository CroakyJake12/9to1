using System.Linq;
using System.Text.Json;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;

namespace CakeOS.Cui;

/// <summary>One loaded document in a genuine V1 compiled artifact: its source identity, its own
/// compatibility metadata and the typed canonical semantic model recovered from the wire payload
/// without parsing source.</summary>
public sealed record CuiV1LoadedDocument(
    string Source,
    string Sha256,
    CuiCompilationMetadata Metadata,
    CuiDocument Semantics);

/// <summary>A decoded genuine V1 compiled artifact carrying full typed semantics per document.</summary>
public sealed record CuiV1LoadedArtifact(
    string Format,
    int FormatVersion,
    string CompilerSha256,
    CuiCompilationMetadata Metadata,
    IReadOnlyList<CuiV1LoadedDocument> Documents);

/// <summary>Structured admission failure from the typed genuine-V1 transport reader. It is
/// deliberately Core-local so the Core reader remains independent of the Runtime assembly; app
/// consumers translate it into their runtime load exception boundary as needed.</summary>
public sealed class CuiV1AdmissionException : Exception
{
    public CuiV1AdmissionException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>A stable diagnostic code (for example "CUIR070"), matching the canonical CUI
    /// runtime load diagnostic naming without depending on Runtime types.</summary>
    public string Code { get; }
}

/// <summary>
/// Genuine V1 compiled-artifact reader. V1 carries the FULL typed compiled projection
/// (Components/Templates/SourceMap/Dependencies/Semantics), never a source snapshot. Reading
/// reconstructs the canonical typed model directly from the wire payload without reparsing XML or
/// invoking the compiler. This reader lives in Core (below the compiler) so stateless app
/// consumers use it without a production Compiler dependency. The Compiler project retains a
/// public writer/compatibility shim over this reader.
/// </summary>
/// <remarks>
/// Structural admission: before a typed document is returned, the reader requires the envelope
/// metadata, a non-empty documents collection, per-document source/content-hash identity (unique,
/// non-empty), per-document compatibility metadata (never inherited from the envelope), and the
/// required compiled projection collections (components/templates/source-map/dependencies/semantics).
/// Wrong kinds, missing required values and incomplete metadata are translated into the canonical
/// typed admission diagnostic rather than leaking <see cref="InvalidOperationException"/>,
/// <see cref="KeyNotFoundException"/> or <see cref="ArgumentException"/>. The envelope aggregate is
/// reconciled with every document's own metadata (producer contract): a document whose language,
/// registry or capability set contradicts the envelope is malformed. Unknown optional fields, omitted/
/// unknown capability values and host-support questions are deliberately NOT decided here: they are
/// tolerated (baseline contract) and are decided by the Runtime <c>ValidateArtifactHostSupport</c>
/// admission boundary at the consumer.
/// </remarks>
public static class CuiV1ArtifactReader
{
    public const string FormatName = "cui-build-compilation";
    public const int FormatVersion = 1;

    /// <summary>Reads a genuine V1 artifact and reconstructs the semantic model without reparsing
    /// XML or invoking the compiler. A V2 artifact (or any non-V1 format/version) is rejected.</summary>
    public static CuiV1LoadedArtifact ReadArtifact(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var document = ParseOrThrow(utf8);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw Admission("CUIR070", "The V1 compiled CUI artifact root must be a JSON object.");

        var format = String(root, "format");
        if (!string.Equals(format, FormatName, StringComparison.Ordinal))
            throw Admission("CUIR060", $"Unsupported compiled CUI artifact format '{format}'.");
        var version = ReadFormatVersion(root);

        var compilerSha256 = String(root, "compilerSha256");
        var envelope = ReadMetadata(RequireObject(root, "metadata", "envelope compatibility metadata"), "envelope");

        if (!TryGetPropertySafe(root, "documents", out var docsEl) || docsEl.ValueKind != JsonValueKind.Array)
            throw Admission("CUIR072", "The V1 compiled CUI artifact is missing its documents collection.");
        if (docsEl.GetArrayLength() == 0)
            throw Admission("CUIR072", "The V1 compiled CUI artifact declares no documents.");

        var sources = new HashSet<string>(StringComparer.Ordinal);
        var loadedDocuments = new List<CuiV1LoadedDocument>();
        foreach (var docEl in docsEl.EnumerateArray())
        {
            if (docEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", "A V1 compiled document must be a JSON object.");

            var source = RequireNonEmptyString(docEl, "source", "document source identity");
            var sha256 = RequireNonEmptyString(docEl, "sha256", "document content hash");
            if (!sources.Add(source))
                throw Admission("CUIR072", $"The V1 artifact declares duplicate source identity '{source}'.");

            var compilation = RequireObject(docEl, "compilation", $"compilation payload for document '{source}'");

            // Per-document compatibility metadata is required; it is never inherited from the
            // envelope (that would conceal stripped metadata).
            var documentMetadata = ReadMetadata(
                RequireObject(compilation, "Metadata", $"compatibility metadata for document '{source}'"),
                $"document '{source}'");
            ReconcileEnvelope(envelope, documentMetadata, source);

            // The entire required compiled projection must be present and correctly shaped. A
            // producer that strips Components/Templates/SourceMap/Dependencies/Semantics is
            // malformed and fails before any typed reconstruction.
            RequireArray(compilation, "Components", $"components for document '{source}'");
            RequireObject(compilation, "Templates", $"templates for document '{source}'");
            RequireArray(compilation, "SourceMap", $"source map for document '{source}'");
            // The dependency graph is a required typed projection; its entries are validated (and
            // thus preserved as a coherent producer contract) rather than silently discarded.
            ValidateDependencyGraph(RequireObject(compilation, "Dependencies", $"dependency graph for document '{source}'"), source);
            var semanticsEl = RequireObject(compilation, "Semantics", $"semantic payload for document '{source}'");

            loadedDocuments.Add(new CuiV1LoadedDocument(source, sha256, documentMetadata, ReadDocument(semanticsEl, source)));
        }

        return new CuiV1LoadedArtifact(FormatName, FormatVersion, compilerSha256, envelope, loadedDocuments);
    }

    private static int ReadFormatVersion(JsonElement root)
    {
        if (!TryGetPropertySafe(root, "formatVersion", out var versionEl))
            throw Admission("CUIR061", $"Unsupported compiled CUI artifact format version; V1 ({FormatVersion}) expected.");
        if (versionEl.ValueKind != JsonValueKind.Number || !versionEl.TryGetInt32(out var version))
            throw Admission("CUIR070", "The V1 compiled CUI artifact format version must be an integer.");
        if (version != FormatVersion)
            throw Admission("CUIR061", $"Unsupported compiled CUI artifact format version {version}; V1 ({FormatVersion}) expected.");
        return version;
    }

    private static void ReconcileEnvelope(CuiCompilationMetadata envelope, CuiCompilationMetadata document, string source)
    {
        if (!string.Equals(envelope.LanguageVersion, document.LanguageVersion, StringComparison.Ordinal))
            throw Admission("CUIR072", $"Document '{source}' language version '{document.LanguageVersion}' contradicts the envelope aggregate '{envelope.LanguageVersion}'.");
        if (!string.Equals(envelope.ComponentRegistryVersion, document.ComponentRegistryVersion, StringComparison.Ordinal))
            throw Admission("CUIR072", $"Document '{source}' component registry version '{document.ComponentRegistryVersion}' contradicts the envelope aggregate '{envelope.ComponentRegistryVersion}'.");
        foreach (var capability in document.RequiredCapabilities)
            if (!envelope.RequiredCapabilities.Contains(capability))
                throw Admission("CUIR072", $"Document '{source}' requires capability '{capability}' absent from the envelope aggregate.");
        // The producer envelope carries the aggregate MAXIMUM ABI across documents. A document must
        // never claim a newer ABI than the envelope supports, but it may legitimately target an
        // older ABI: equality is never required (that would reject genuine heterogeneous bundles).
        if (CompareAbi(document.RuntimeAbiVersion, envelope.RuntimeAbiVersion) > 0)
            throw Admission("CUIR072", $"Document '{source}' ABI '{document.RuntimeAbiVersion}' exceeds the envelope aggregate '{envelope.RuntimeAbiVersion}'.");
    }

    private static int CompareAbi(string left, string right)
    {
        if (int.TryParse(left, out var a) && int.TryParse(right, out var b)) return a.CompareTo(b);
        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static JsonDocument ParseOrThrow(byte[] utf8)
    {
        try
        {
            return JsonDocument.Parse(utf8);
        }
        catch (Exception error) when (error is JsonException)
        {
            throw Admission("CUIR070", $"The V1 compiled CUI artifact is malformed: {error.Message}.");
        }
    }

    /// <summary>Safe property probe that never throws on non-object elements; missing/wrong-kind
    /// reports false so callers translate the case into the typed admission diagnostic.</summary>
    private static bool TryGetPropertySafe(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }
        return element.TryGetProperty(property, out value);
    }

    /// <summary>Reads an optional object-typed member. A genuine V1 producer serializes nullable
    /// members without ignoring them, so a present-null value is the SAME absence as an omitted key
    /// and must not be diagnosed. A present non-null, non-object value is malformed and is refused
    /// with a typed admission diagnostic (never a silent null).</summary>
    private static bool TryReadOptionalObject(JsonElement element, string property, out JsonElement value)
    {
        if (!TryGetPropertySafe(element, property, out value)) return false;
        if (value.ValueKind == JsonValueKind.Null) return false; // present-null absence, exactly like omitted.
        if (value.ValueKind != JsonValueKind.Object)
            throw Admission("CUIR072", $"The V1 artifact member '{property}' must be an object when present.");
        return true;
    }

    /// <summary>Requires a present object-valued property, translating a missing/wrong kind into
    /// the canonical structural admission diagnostic.</summary>
    private static JsonElement RequireObject(JsonElement element, string property, string what)
    {
        if (!TryGetPropertySafe(element, property, out var child) || child.ValueKind != JsonValueKind.Object)
            throw Admission("CUIR072", $"The V1 artifact is missing or malformed {what}.");
        return child;
    }

    /// <summary>Requires a present array-valued property, translating a missing/wrong kind into
    /// the canonical structural admission diagnostic.</summary>
    private static JsonElement RequireArray(JsonElement element, string property, string what)
    {
        if (!TryGetPropertySafe(element, property, out var child) || child.ValueKind != JsonValueKind.Array)
            throw Admission("CUIR072", $"The V1 artifact is missing or malformed {what}.");
        return child;
    }

    private static string RequireNonEmptyString(JsonElement element, string property, string what)
    {
        if (!TryGetPropertySafe(element, property, out var child) || child.ValueKind != JsonValueKind.String)
            throw Admission("CUIR072", $"The V1 artifact is missing {what}.");
        var value = child.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw Admission("CUIR072", $"The V1 artifact has an empty {what}.");
        return value!.Trim();
    }

    private static CuiCompilationMetadata ReadMetadata(JsonElement element, string what)
    {
        var language = RequireNonEmptyString(element, "LanguageVersion", $"{what} language version");
        var abi = RequireNonEmptyString(element, "RuntimeAbiVersion", $"{what} ABI version");
        var registry = RequireNonEmptyString(element, "ComponentRegistryVersion", $"{what} component registry version");

        var capabilities = new List<string>();
        if (TryGetPropertySafe(element, "RequiredCapabilities", out var capsEl))
        {
            if (capsEl.ValueKind != JsonValueKind.Array)
                throw Admission("CUIR072", $"The V1 artifact {what} declares a non-array required-capability set.");
            foreach (var item in capsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    throw Admission("CUIR072", $"The V1 artifact {what} declares an empty or non-string required capability.");
                capabilities.Add(item.GetString()!.Trim());
            }
        }
        return new CuiCompilationMetadata(language, abi, registry, capabilities);
    }

    /// <summary>Validates the typed dependency projection. The producer's graph type
    /// (<c>GuiDependencyGraph</c>) serializes as a single <c>Entries</c> object mapping a component
    /// stable-id to an array of typed dependency entries { Kind, Name }. This is a required compiled
    /// projection: malformed entries (non-object, non-string kind/name) are refused with a typed
    /// admission diagnostic; an empty graph is valid. Unknown/optional dependency fields are tolerated
    /// and are decided by the Runtime host-support boundary, never here.</summary>
    private static void ValidateDependencyGraph(JsonElement element, string source)
    {
        // The producer writes CuiDependencyGraph as {"Entries": {id: [...]}}. Unwrap that single
        // container so we validate the actual entry map; the raw object itself is never valid as a
        // direct id->array map when the Entries wrapper is present.
        var graph = element;
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("Entries", out var entries) &&
            entries.ValueKind == JsonValueKind.Object)
            graph = entries;

        foreach (var pair in graph.EnumerateObject())
        {
            var values = pair.Value;
            if (values.ValueKind != JsonValueKind.Array)
                throw Admission("CUIR072", $"The dependency graph of document '{source}' entry '{pair.Name}' is not an array.");
            foreach (var entry in values.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    throw Admission("CUIR072", $"A dependency of document '{source}' must be a JSON object.");
                if (!TryGetPropertySafe(entry, "Kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(kindEl.GetString()))
                    throw Admission("CUIR072", $"A dependency of document '{source}' is missing its kind.");
                if (!TryGetPropertySafe(entry, "Name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameEl.GetString()))
                    throw Admission("CUIR072", $"A dependency of document '{source}' is missing its name.");
            }
        }
    }

    private static CuiV1AdmissionException Admission(string code, string message) => new(code, message);

    private static CuiDocument ReadDocument(JsonElement element, string source)
    {
        var resources = new Dictionary<string, CuiResourceDefinition>(StringComparer.Ordinal);
        if (TryGetPropertySafe(element, "Resources", out var resourcesEl))
        {
            if (resourcesEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", $"The resources of document '{source}' are not a JSON object.");
            foreach (var pair in resourcesEl.EnumerateObject())
            {
                var valueEl = RequireObject(pair.Value, "Value", $"value of resource '{pair.Name}'");
                resources[pair.Name] = new CuiResourceDefinition(String(pair.Value, "Key"), ReadValue(valueEl, source), ReadSpan(pair.Value));
            }
        }

        var styles = new List<CuiStyleDefinition>();
        if (TryGetPropertySafe(element, "Styles", out var stylesEl))
        {
            if (stylesEl.ValueKind != JsonValueKind.Array)
                throw Admission("CUIR072", $"The styles of document '{source}' are not a JSON array.");
            foreach (var styleEl in stylesEl.EnumerateArray())
            {
                var setters = new List<CuiStyleSetter>();
                if (TryGetPropertySafe(styleEl, "Setters", out var settersEl))
                {
                    if (settersEl.ValueKind != JsonValueKind.Array)
                        throw Admission("CUIR072", $"The setters of a style in document '{source}' are not a JSON array.");
                    foreach (var setterEl in settersEl.EnumerateArray())
                        setters.Add(new CuiStyleSetter(String(setterEl, "Property"),
                            ReadValue(RequireObject(setterEl, "Value", "style setter value"), source), ReadSpan(setterEl)));
                }
                styles.Add(new CuiStyleDefinition(String(styleEl, "Selector"), setters, ReadSpan(styleEl)));
            }
        }

        var actions = new Dictionary<string, CuiActionDefinition>(StringComparer.Ordinal);
        if (TryGetPropertySafe(element, "Actions", out var actionsEl))
        {
            if (actionsEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", $"The actions of document '{source}' are not a JSON object.");
            foreach (var pair in actionsEl.EnumerateObject())
            {
                var actionEl = pair.Value;
                if (actionEl.ValueKind != JsonValueKind.Object)
                    throw Admission("CUIR072", $"Action '{pair.Name}' of document '{source}' is not a JSON object.");
                var parameter = TryGetPropertySafe(actionEl, "Parameter", out var parameterEl) && parameterEl.ValueKind == JsonValueKind.Object
                    ? ReadValue(parameterEl, source) : null;
                actions[pair.Name] = new CuiActionDefinition(String(actionEl, "Name"), String(actionEl, "Command"),
                    parameter, ReadSpan(actionEl), NullableString(actionEl, "LocalTarget"));
            }
        }

        var templates = new Dictionary<string, CuiTemplateDefinition>(StringComparer.Ordinal);
        if (TryGetPropertySafe(element, "Templates", out var templatesEl))
        {
            if (templatesEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", $"The templates of document '{source}' are not a JSON object.");
            foreach (var pair in templatesEl.EnumerateObject())
            {
                var templateEl = pair.Value;
                if (templateEl.ValueKind != JsonValueKind.Object)
                    throw Admission("CUIR072", $"Template '{pair.Name}' of document '{source}' is not a JSON object.");
                templates[pair.Name] = new CuiTemplateDefinition(String(templateEl, "Name"),
                    Array(templateEl, "Parameters"), ReadComponents(templateEl, "Content", source), ReadSpan(templateEl));
            }
        }

        // The compiled semantic model's root component collection is a required projection; a
        // producer that omits or misshapes it is malformed, never a silently-empty document.
        var components = ReadComponentsArray(RequireArray(element, "Components", $"semantic components for document '{source}'"), source);

        var rootProperties = new Dictionary<string, CuiValue>(StringComparer.Ordinal);
        if (TryGetPropertySafe(element, "RootProperties", out var rootEl))
        {
            if (rootEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", $"The root properties of document '{source}' are not a JSON object.");
            foreach (var pair in rootEl.EnumerateObject())
                rootProperties[pair.Name] = ReadValue(pair.Value, source);
        }

        var imports = new List<CuiImportDefinition>();
        if (TryGetPropertySafe(element, "Imports", out var importsEl))
        {
            if (importsEl.ValueKind != JsonValueKind.Array)
                throw Admission("CUIR072", $"The imports of document '{source}' are not a JSON array.");
            foreach (var importEl in importsEl.EnumerateArray())
                imports.Add(new CuiImportDefinition(String(importEl, "Source"), NullableString(importEl, "Alias"), ReadSpan(importEl)));
        }

        var variables = new Dictionary<string, CuiVariableDeclaration>(StringComparer.Ordinal);
        if (TryGetPropertySafe(element, "Variables", out var variablesEl))
        {
            if (variablesEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", $"The variables of document '{source}' are not a JSON object.");
            foreach (var pair in variablesEl.EnumerateObject())
            {
                var variableEl = pair.Value;
                if (variableEl.ValueKind != JsonValueKind.Object)
                    throw Admission("CUIR072", $"Variable '{pair.Name}' of document '{source}' is not a JSON object.");
                variables[pair.Name] = new CuiVariableDeclaration(String(variableEl, "Name"), String(variableEl, "Type"),
                    String(variableEl, "Value"), ReadSpan(variableEl), NullableString(variableEl, "Source"), NullableString(variableEl, "Selection"));
            }
        }

        return new CuiDocument(String(element, "SourceName"), resources, styles, actions, templates, components, ReadSpan(element))
        {
            RootProperties = rootProperties,
            Imports = imports,
            Variables = variables,
        };
    }

    private static CuiComponent ReadComponent(JsonElement element, string source)
    {
        var properties = new Dictionary<string, CuiValue>(StringComparer.OrdinalIgnoreCase);
        if (TryGetPropertySafe(element, "Properties", out var propertiesEl))
        {
            if (propertiesEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", $"The properties of a component in document '{source}' are not a JSON object.");
            foreach (var property in propertiesEl.EnumerateObject())
                properties[property.Name] = ReadValue(property.Value, source);
        }

        var actions = new Dictionary<string, CuiActionReference>(StringComparer.OrdinalIgnoreCase);
        if (TryGetPropertySafe(element, "Actions", out var actionsEl))
        {
            if (actionsEl.ValueKind != JsonValueKind.Object)
                throw Admission("CUIR072", $"The actions of a component in document '{source}' are not a JSON object.");
            foreach (var action in actionsEl.EnumerateObject())
                actions[action.Name] = new CuiActionReference(String(action.Value, "Name"), ReadSpan(action.Value));
        }

        CuiCondition? condition = null;
        if (TryReadOptionalObject(element, "Condition", out var conditionEl))
        {
            condition = new CuiCondition(
                ReadValue(RequireObject(conditionEl, "Test", "condition test value"), source),
                Bool(conditionEl, "Negate"), ReadSpan(conditionEl), Bool(conditionEl, "IsLive", true));
        }

        CuiRepeatDefinition? repeat = null;
        if (TryReadOptionalObject(element, "Repeat", out var repeatEl))
        {
            repeat = new CuiRepeatDefinition(
                ReadValue(RequireObject(repeatEl, "Source", "repeat source value"), source),
                String(repeatEl, "ItemName"),
                ReadValue(RequireObject(repeatEl, "Key", "repeat key value"), source),
                ReadSpan(repeatEl),
                TryGetPropertySafe(repeatEl, "Template", out var templateEl) && templateEl.ValueKind == JsonValueKind.Object
                    ? ReadValue(templateEl, source) : null);
        }

        CuiListDefinition? list = null;
        if (TryReadOptionalObject(element, "List", out var listEl))
        {
            list = new CuiListDefinition(
                ReadValue(RequireObject(listEl, "Items", "list items value"), source),
                String(listEl, "ItemTemplate"), NullableString(listEl, "EmptyTemplate"), ReadSpan(listEl));
        }

        CuiPropertyRegionDefinition? propertyRegion = null;
        if (TryReadOptionalObject(element, "PropertyRegion", out var regionEl))
        {
            propertyRegion = new CuiPropertyRegionDefinition(
                String(regionEl, "PropertyName"),
                ReadValue(RequireObject(regionEl, "Value", "property-region value"), source), ReadSpan(regionEl));
        }

        var textParts = new List<CuiTextPart>();
        if (TryGetPropertySafe(element, "TextParts", out var partsEl))
        {
            if (partsEl.ValueKind != JsonValueKind.Array)
                throw Admission("CUIR072", $"The text parts of a component in document '{source}' are not a JSON array.");
            foreach (var part in partsEl.EnumerateArray())
            {
                var typeName = TryGetPropertySafe(part, "$type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                    ? typeEl.GetString() : null;
                textParts.Add(typeName == nameof(CuiExpressionTextPart)
                    ? new CuiExpressionTextPart(ReadValue(RequireObject(part, "Value", "expression text value"), source), ReadSpan(part))
                    : new CuiLiteralTextPart(String(part, "Value"), ReadSpan(part)));
            }
        }

        return new CuiComponent(
            String(element, "Type"), NullableString(element, "Name"), Array(element, "Classes"), properties, actions,
            ReadComponents(element, "Children", source), condition, list, ReadSpan(element), NullableString(element, "DefaultTheme"),
            repeat, ReadComponents(element, "ElseChildren", source), Array(element, "Groups"), Bool(element, "IsDefinition"), propertyRegion)
        {
            Text = String(element, "Text"),
            TextParts = textParts,
        };
    }

    private static IReadOnlyList<CuiComponent> ReadComponents(JsonElement element, string name, string source)
    {
        if (!TryGetPropertySafe(element, name, out var list) || list.ValueKind != JsonValueKind.Array)
            return System.Array.Empty<CuiComponent>();
        return ReadComponentsArray(list, source);
    }

    private static IReadOnlyList<CuiComponent> ReadComponentsArray(JsonElement list, string source)
    {
        var result = new List<CuiComponent>();
        foreach (var child in list.EnumerateArray()) result.Add(ReadComponent(child, source));
        return result;
    }

    private static CuiValue ReadValue(JsonElement element, string source)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Admission("CUIR070", $"A V1 semantic value in document '{source}' must be a JSON object.");
        var typeName = TryGetPropertySafe(element, "$type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
            ? typeEl.GetString() : null;
        return typeName switch
        {
            nameof(CuiLiteralValue) => new CuiLiteralValue(String(element, "Value"), ReadSpan(element)),
            nameof(CuiBindingValue) => new CuiBindingValue(String(element, "Path"), ParseBindingMode(String(element, "Mode")),
                NullableString(element, "Fallback"), ReadSpan(element), NullableString(element, "TargetType")),
            nameof(CuiResourceValue) => new CuiResourceValue(String(element, "Key"), ReadSpan(element)),
            nameof(CuiInvalidValue) => new CuiInvalidValue(String(element, "RawValue"), String(element, "DiagnosticCode"),
                String(element, "Message"), ReadSpan(element)),
            _ => throw Admission("CUIR070", $"Unknown V1 value discriminator '{typeName}' in document '{source}'."),
        };
    }

    private static CuiBindingMode ParseBindingMode(string value) => value switch
    {
        "Invalid" => CuiBindingMode.Invalid,
        "OneWay" => CuiBindingMode.OneWay,
        "TwoWay" => CuiBindingMode.TwoWay,
        "OneTime" => CuiBindingMode.OneTime,
        _ => throw Admission("CUIR070", $"Unknown CUI binding mode '{value}'."),
    };

    private static CuiSourceSpan ReadSpan(JsonElement element)
    {
        // The writer nests each span under a "Span" object; some older V1 shapes carried the
        // coordinates directly on the owning element. Read from the nested shape when present so a
        // genuine V1 round-trip preserves the authored source coordinates instead of defaulting.
        if (!TryGetPropertySafe(element, "Span", out var spanEl) || spanEl.ValueKind != JsonValueKind.Object)
            spanEl = element;
        var source = String(spanEl, "SourceName");
        (int line, int column) Pos(string property) =>
            TryGetPropertySafe(spanEl, property, out var pos) && pos.ValueKind == JsonValueKind.Object
                ? (Int(pos, "Line"), Int(pos, "Column")) : (1, 1);
        var (sl, sc) = Pos("Start");
        var (el, ec) = Pos("End");
        return new CuiSourceSpan(source, new(sl, sc), new(el, ec));
    }

    private static string String(JsonElement element, string property) =>
        TryGetPropertySafe(element, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static string? NullableString(JsonElement element, string property) =>
        TryGetPropertySafe(element, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool Bool(JsonElement element, string property, bool fallback = false)
    {
        if (!TryGetPropertySafe(element, property, out var value)) return fallback;
        return value.ValueKind == JsonValueKind.True;
    }

    private static int Int(JsonElement element, string property)
    {
        if (!TryGetPropertySafe(element, property, out var value)) return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw Admission("CUIR070", $"The V1 artifact coordinate '{property}' must be an integer.");
        return result;
    }

    private static IReadOnlyList<string> Array(JsonElement element, string property)
    {
        if (!TryGetPropertySafe(element, property, out var list) || list.ValueKind != JsonValueKind.Array)
            return System.Array.Empty<string>();
        var result = new List<string>();
        foreach (var item in list.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) result.Add(item.GetString() ?? "");
        return result;
    }
}
