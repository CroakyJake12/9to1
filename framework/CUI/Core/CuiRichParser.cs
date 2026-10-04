using System.Xml;
using System.Xml.Linq;
using System.Text;

namespace CakeOS.Cui.Language;

/// <summary>
/// Parses rich CUI markup into a full CuiDocument with resources, styles, actions,
/// templates, component trees, bindings, conditions, and list definitions.
/// This is the authoritative CUI language parser — not a generic XML reader.
/// </summary>
public sealed class CuiRichParser
{
    private readonly CuiDiagnosticBag _diagnostics = new();
    private IReadOnlyDictionary<int, string> _textReferences = new Dictionary<int, string>();

    public CuiDiagnosticBag Diagnostics => _diagnostics;

    public CuiDocument Parse(string source, string sourceName = "markup.cui")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        _diagnostics.Clear();
        var normalized = new CuiSyntaxNormalizer(_diagnostics).Normalize(source, sourceName);
        _textReferences = normalized.TextReferences;

        try
        {
            var document = XDocument.Parse(normalized.XmlSource, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                _diagnostics.Error("CUI001", "CUI document has no root element.",
                    CuiSourceSpan.At(sourceName, 1, 1));
                return CreateFailedDocument(sourceName);
            }

            if (root.Name.Namespace != XNamespace.None)
            {
                _diagnostics.Error("CUI002", "CUI does not accept XML namespaces.",
                    SpanOf(root, sourceName));
                return CreateFailedDocument(sourceName);
            }

            var hasCuiDocumentRoot = IsKeyword(root, "Cui");

            var resources = hasCuiDocumentRoot
                ? ParseResources(root, sourceName)
                : new Dictionary<string, CuiResourceDefinition>(StringComparer.Ordinal);
            var styles = hasCuiDocumentRoot ? ParseStyles(root, sourceName) : Array.Empty<CuiStyleDefinition>();
            var actions = hasCuiDocumentRoot
                ? ParseActions(root, sourceName)
                : new Dictionary<string, CuiActionDefinition>(StringComparer.Ordinal);
            var templates = hasCuiDocumentRoot
                ? ParseTemplates(root, sourceName)
                : new Dictionary<string, CuiTemplateDefinition>(StringComparer.Ordinal);
            var components = hasCuiDocumentRoot
                ? ParseComponents(root, sourceName, isRoot: true)
                : [ParseComponent(root, sourceName)!];

            ValidateAddressScopes(components);

            return new CuiDocument(
                sourceName,
                resources,
                styles,
                actions,
                templates,
                components,
                SpanOf(root, sourceName))
            {
                RootProperties = hasCuiDocumentRoot
                    ? ParseRootProperties(root, sourceName)
                    : new Dictionary<string, CuiValue>(StringComparer.Ordinal),
            };
        }
        catch (XmlException ex)
        {
            _diagnostics.Error("CUI010", $"XML parse error: {ex.Message}",
                CuiSourceSpan.At(sourceName, ex.LineNumber, ex.LinePosition));
            return CreateFailedDocument(sourceName);
        }
    }

    public CuiDocument ParseFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        CuiProjectItems.RequireCui(fullPath);
        return Parse(File.ReadAllText(fullPath), fullPath);
    }

    private static CuiSourceSpan SpanOf(XElement element, string sourceName)
    {
        var info = (IXmlLineInfo)element;
        return CuiSourceSpan.At(sourceName, info.LineNumber, info.LinePosition);
    }

    private static CuiSourceSpan SpanOf(XAttribute attribute, string sourceName)
    {
        var info = (IXmlLineInfo)attribute;
        return CuiSourceSpan.At(sourceName, info.LineNumber, info.LinePosition);
    }

    private CuiDocument CreateFailedDocument(string sourceName) =>
        new(sourceName, new Dictionary<string, CuiResourceDefinition>(),
            [], new Dictionary<string, CuiActionDefinition>(),
            new Dictionary<string, CuiTemplateDefinition>(),
            [], CuiSourceSpan.At(sourceName, 1, 1));

    private IReadOnlyDictionary<string, CuiValue> ParseRootProperties(
        XElement root,
        string sourceName)
    {
        var properties = new Dictionary<string, CuiValue>(StringComparer.Ordinal);
        foreach (var attribute in root.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace != XNamespace.None)
                continue;

            properties[attribute.Name.LocalName] = ParseMarkupValue(
                attribute.Value,
                SpanOf(attribute, sourceName));
        }

        return properties;
    }

    #region Resources

    private IReadOnlyDictionary<string, CuiResourceDefinition> ParseResources(
        XElement root, string sourceName)
    {
        var result = new Dictionary<string, CuiResourceDefinition>(StringComparer.Ordinal);

        var resourcesElement = root.Elements().FirstOrDefault(e => IsKeyword(e, "Resources"));
        if (resourcesElement is null) return result;

        foreach (var child in resourcesElement.Elements())
        {
            if (child.Name.Namespace != XNamespace.None) continue;
            if (!child.HasAttributes) continue;

            var keyAttr = child.Attribute("key");
            var valueAttr = child.Attribute("value");
            if (keyAttr is null || valueAttr is null) continue;

            var key = keyAttr.Value;
            if (string.IsNullOrWhiteSpace(key)) continue;

            var span = SpanOf(child, sourceName);
            var value = ParseMarkupValue(valueAttr.Value, span);
            result[key] = new CuiResourceDefinition(key, value, span);
        }

        return result;
    }

    #endregion

    #region Styles

    private IReadOnlyList<CuiStyleDefinition> ParseStyles(XElement root, string sourceName)
    {
        var result = new List<CuiStyleDefinition>();

        var stylesElement = root.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Styles", StringComparison.Ordinal));
        if (stylesElement is null) return result;

        foreach (var styleChild in stylesElement.Elements())
        {
            if (!IsKeyword(styleChild, "Style")) continue;
            var selector = styleChild.Attribute("selector")?.Value ?? string.Empty;
            var span = SpanOf(styleChild, sourceName);
            var setters = new List<CuiStyleSetter>();

            foreach (var setter in styleChild.Elements())
            {
                if (!IsKeyword(setter, "Setter")) continue;
                var property = setter.Attribute("property")?.Value;
                var val = setter.Attribute("value")?.Value;
                if (property is null || val is null) continue;

                var setterSpan = SpanOf(setter, sourceName);
                setters.Add(new CuiStyleSetter(property, ParseMarkupValue(val, setterSpan), setterSpan));
            }

            result.Add(new CuiStyleDefinition(selector, setters, span));
        }

        return result;
    }

    #endregion

    #region Actions

    private IReadOnlyDictionary<string, CuiActionDefinition> ParseActions(
        XElement root, string sourceName)
    {
        var result = new Dictionary<string, CuiActionDefinition>(StringComparer.Ordinal);

        var actionsElement = root.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Actions", StringComparison.Ordinal));
        if (actionsElement is null) return result;

        foreach (var child in actionsElement.Elements())
        {
            if (!IsKeyword(child, "Action")) continue;
            var name = child.Attribute("name")?.Value;
            var command = child.Attribute("command")?.Value;
            if (name is null || command is null) continue;

            var span = SpanOf(child, sourceName);
            var paramAttr = child.Attribute("parameter");
            var parameter = paramAttr is not null ? ParseMarkupValue(paramAttr.Value, span) : null;

            result[name] = new CuiActionDefinition(name, command, parameter, span);
        }

        return result;
    }

    #endregion

    #region Templates

    private IReadOnlyDictionary<string, CuiTemplateDefinition> ParseTemplates(
        XElement root, string sourceName)
    {
        var result = new Dictionary<string, CuiTemplateDefinition>(StringComparer.Ordinal);

        var templatesElement = root.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Templates", StringComparison.Ordinal));
        if (templatesElement is null) return result;

        foreach (var child in templatesElement.Elements())
        {
            if (!IsKeyword(child, "Template")) continue;
            var name = child.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var span = SpanOf(child, sourceName);
            var paramAttr = child.Attribute("parameters")?.Value;
            var parameters = string.IsNullOrWhiteSpace(paramAttr)
                ? Array.Empty<string>()
                : paramAttr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            var content = ParseChildComponents(child, sourceName);
            result[name!] = new CuiTemplateDefinition(name!, parameters, content, span);
        }

        return result;
    }

    #endregion

    #region Components

    private IReadOnlyList<CuiComponent> ParseComponents(
        XElement parent, string sourceName, bool isRoot)
    {
        var elements = new List<XElement>();
        foreach (var child in parent.Elements())
        {
            if (child.Name.Namespace != XNamespace.None) continue;
            if (isRoot && !string.Equals(child.Name.LocalName, "Cui", StringComparison.Ordinal))
            {
                // Inside <Cui>, top-level sections are Resources/Styles/Actions/Templates
                var localName = child.Name.LocalName;
                if (IsKeyword(child, "Resources") || IsKeyword(child, "Styles")
                    || IsKeyword(child, "Actions") || IsKeyword(child, "Templates"))
                    continue;
            }
            elements.Add(child);
        }
        return ParseComponentSiblings(elements, sourceName);
    }

    private IReadOnlyList<CuiComponent> ParseChildComponents(
        XElement parent, string sourceName)
    {
        return ParseComponentSiblings(
            parent.Elements().Where(child => child.Name.Namespace == XNamespace.None),
            sourceName);
    }

    private IReadOnlyList<CuiComponent> ParseComponentSiblings(
        IEnumerable<XElement> elements,
        string sourceName)
    {
        var siblings = elements.ToArray();
        var result = new List<CuiComponent>(siblings.Length);
        for (var index = 0; index < siblings.Length; index++)
        {
            var element = siblings[index];
            if (IsKeyword(element, "Else"))
            {
                _diagnostics.Error("CUI021", "<Else> must immediately follow an <If> sibling.",
                    SpanOf(element, sourceName));
                continue;
            }

            XElement? elseElement = null;
            if (IsKeyword(element, "If") && index + 1 < siblings.Length
                && IsKeyword(siblings[index + 1], "Else"))
            {
                elseElement = siblings[++index];
            }

            var component = ParseComponent(element, sourceName, elseElement);
            if (component is not null) result.Add(component);
        }

        return result;
    }

    private static bool IsKeyword(XElement element, string keyword) =>
        element.Name.Namespace == XNamespace.None
        && string.Equals(element.Name.LocalName, keyword, StringComparison.OrdinalIgnoreCase);

    private CuiComponent? ParseComponent(
        XElement element,
        string sourceName,
        XElement? elseElement = null)
    {
        if (element.Name.Namespace != XNamespace.None)
        {
            _diagnostics.Error("CUI004", "CUI does not accept XML namespaces.",
                SpanOf(element, sourceName));
            return null;
        }

        var type = CanonicalBuiltInName(element.Name.LocalName);
        var span = SpanOf(element, sourceName);

        // === DefaultTheme is a first-class CUI construct, not a visual control ===
        if (string.Equals(type, "DefaultTheme", StringComparison.Ordinal))
        {
            return ParseDefaultTheme(element, sourceName);
        }

        // Parse attributes
        string? name = null;
        var idNameConflict = false;
        var classes = new List<string>();
        var groups = new List<string>();
        var properties = new Dictionary<string, CuiValue>(StringComparer.Ordinal);
        var actions = new Dictionary<string, CuiActionReference>(StringComparer.Ordinal);
        CuiCondition? condition = null;
        CuiListDefinition? list = null;
        string? repeatSource = null;
        CuiSourceSpan? repeatSourceSpan = null;
        string? repeatItemName = null;
        string? repeatKey = null;
        CuiSourceSpan? repeatKeySpan = null;
        var isDefinition = false;
        string? propertyName = null;
        string? propertyValue = null;
        CuiSourceSpan? propertyValueSpan = null;

        foreach (var attr in element.Attributes())
        {
            if (attr.IsNamespaceDeclaration) continue;
            if (attr.Name.Namespace != XNamespace.None)
            {
                _diagnostics.Error("CUI004", "CUI does not accept XML namespaces.",
                    SpanOf(attr, sourceName));
                continue;
            }

            var attrName = attr.Name.LocalName;
            var attrSpan = SpanOf(attr, sourceName);
            var rawValue = attr.Value;

            if ((type == "If" && string.Equals(attrName, "Condition", StringComparison.OrdinalIgnoreCase))
                || string.Equals(attrName, "condition", StringComparison.OrdinalIgnoreCase))
            {
                condition = ParseCondition(rawValue, attrSpan);
                continue;
            }

            if (type == "Repeat")
            {
                if (string.Equals(attrName, "Source", StringComparison.OrdinalIgnoreCase))
                {
                    repeatSource = rawValue;
                    repeatSourceSpan = attrSpan;
                    continue;
                }
                if (string.Equals(attrName, "As", StringComparison.OrdinalIgnoreCase))
                {
                    repeatItemName = rawValue;
                    continue;
                }
                if (string.Equals(attrName, "Key", StringComparison.OrdinalIgnoreCase))
                {
                    repeatKey = rawValue;
                    repeatKeySpan = attrSpan;
                    continue;
                }
            }

            if (type == "Property" && string.Equals(attrName, "Property", StringComparison.OrdinalIgnoreCase))
            {
                propertyName = rawValue;
                continue;
            }

            if (type == "Property" && string.Equals(attrName, "Value", StringComparison.OrdinalIgnoreCase))
            {
                propertyValue = rawValue;
                propertyValueSpan = attrSpan;
                continue;
            }

            switch (attrName.ToLowerInvariant())
            {
                case "id":
                case "name":
                    if (name is not null && !string.Equals(name, rawValue, StringComparison.Ordinal))
                        idNameConflict = true;
                    name = rawValue;
                    break;

                case "class":
                    classes.AddRange(rawValue.Split(' ',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;

                case "group":
                    groups.AddRange(rawValue.Split(',',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;

                case "definition":
                    isDefinition = bool.TryParse(rawValue, out var definitionValue) && definitionValue;
                    break;

                case "action":
                    actions[attrName] = new CuiActionReference(rawValue, attrSpan);
                    break;

                case "condition":
                    condition = ParseCondition(rawValue, attrSpan);
                    break;

                case "items":
                    if (element.Attribute("item-template") is { } itemTemplateAttr)
                    {
                        var emptyTemplate = element.Attribute("empty-template")?.Value;
                        list = new CuiListDefinition(
                            ParseMarkupValue(rawValue, attrSpan),
                            itemTemplateAttr.Value,
                            emptyTemplate,
                            attrSpan);
                    }
                    break;

                default:
                    if (attrName.StartsWith("action-", StringComparison.Ordinal))
                    {
                        // action-argument, action-enabled, etc. are action metadata
                        actions[attrName] = new CuiActionReference(rawValue, attrSpan);
                    }
                    else
                    {
                        properties[attrName] = ParseMarkupValue(rawValue, attrSpan);
                    }
                    break;
            }
        }

        var children = ParseChildComponents(element, sourceName);
        var elseChildren = elseElement is null
            ? Array.Empty<CuiComponent>()
            : ParseChildComponents(elseElement, sourceName);

        if (type == "If" && condition is null)
        {
            _diagnostics.Error("CUI022", "<If> requires a Condition attribute.", span);
        }
        if (idNameConflict)
        {
            _diagnostics.Error("CUI026", "ID and Name are aliases; an element can declare only one identity value.", span);
        }

        CuiRepeatDefinition? repeat = null;
        if (type == "Repeat")
        {
            if (string.IsNullOrWhiteSpace(repeatSource))
                _diagnostics.Error("CUI023", "<Repeat> requires a Source attribute.", span);
            if (string.IsNullOrWhiteSpace(repeatItemName))
                _diagnostics.Error("CUI024", "<Repeat> requires an As item name.", span);
            if (string.IsNullOrWhiteSpace(repeatKey))
                _diagnostics.Error("CUI025", "<Repeat> requires a stable Key expression.", span);

            if (!string.IsNullOrWhiteSpace(repeatSource)
                && !string.IsNullOrWhiteSpace(repeatItemName)
                && !string.IsNullOrWhiteSpace(repeatKey))
            {
                repeat = new CuiRepeatDefinition(
                    ParseMarkupValue(repeatSource, repeatSourceSpan ?? span),
                    repeatItemName,
                    ParseMarkupValue(repeatKey, repeatKeySpan ?? span),
                    span);
            }
        }

        CuiPropertyRegionDefinition? propertyRegion = null;
        if (type == "Property")
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                _diagnostics.Error("CUI038", "A Property region requires a Property name.", span);
            }
            else
            {
                if (propertyValue is null
                    && element.Attribute(propertyName) is { } namedValueAttribute)
                {
                    propertyValue = namedValueAttribute.Value;
                    propertyValueSpan = SpanOf(namedValueAttribute, sourceName);
                }

                if (propertyValue is null)
                {
                    _diagnostics.Error("CUI039", $"Property region '{propertyName}' requires a value.", span);
                }
                else
                {
                    propertyRegion = new CuiPropertyRegionDefinition(
                        propertyName,
                        ParseMarkupValue(propertyValue, propertyValueSpan ?? span),
                        span);
                }
            }
        }

        return new CuiComponent(
            type, name, classes, properties, actions,
            children, condition, list, span,
            repeat: repeat,
            elseChildren: elseChildren,
            groups: groups,
            isDefinition: isDefinition,
            propertyRegion: propertyRegion)
        {
            Text = DecodeTextReferences(string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim(), span, out var textParts),
            TextParts = textParts,
        };
    }

    /// <summary>
    /// Parses a DefaultTheme element. Supports:
    ///   <DefaultTheme value="Glow">...</DefaultTheme>
    ///   <DefaultTheme = "Bubble">...</DefaultTheme>
    ///   <DefaultTheme="Retro">...</DefaultTheme>
    /// </summary>
    private CuiComponent? ParseDefaultTheme(XElement element, string sourceName)
    {
        var span = SpanOf(element, sourceName);

        // Resolve theme name from any of the supported attribute forms
        string? themeName = null;
        foreach (var attr in element.Attributes())
        {
            var localName = attr.Name.LocalName;
            if (string.Equals(localName, "value", StringComparison.OrdinalIgnoreCase))
            {
                themeName = attr.Value;
                break;
            }
        }

        // Also check for the "DefaultTheme" attribute (for <DefaultTheme DefaultTheme="X">)
        if (themeName is null)
        {
            themeName = element.Attribute("DefaultTheme")?.Value;
        }

        // Also support attribute-less form where the text content is the theme name
        if (themeName is null)
        {
            themeName = element.Value?.Trim();
        }

        if (string.IsNullOrWhiteSpace(themeName))
        {
            _diagnostics.Error("CUI020", "DefaultTheme element requires a theme name (value, attribute, or content).",
                span);
            themeName = "Default";
        }

        var children = ParseChildComponents(element, sourceName);

        return new CuiComponent(
            "DefaultTheme",
            null,
            [],
            new Dictionary<string, CuiValue>(),
            new Dictionary<string, CuiActionReference>(),
            children,
            null,
            null,
            span,
            defaultTheme: themeName)
        {
            Text = string.Empty,
        };
    }

    #endregion

    private void ValidateAddressScopes(IReadOnlyList<CuiComponent> components)
    {
        var documentScope = new CuiAddressScope();
        foreach (var component in components)
            ValidateAddressComponent(component, documentScope, startsPageScope: false);
    }

    private void ValidateAddressComponent(
        CuiComponent component,
        CuiAddressScope parentScope,
        bool startsPageScope)
    {
        var scope = startsPageScope || component.Type == "Page" || component.Type == "Prefab" || component.IsDefinition
            ? new CuiAddressScope()
            : parentScope;

        if (component.AuthoredId is null)
        {
            if (scope.GeneratedIds.TryGetValue(component.StableId, out var firstSpan))
            {
                _diagnostics.Error("CUI029",
                    "Unnamed components have the same generated stable identity. Add distinct authored IDs to disambiguate them.",
                    component.Span,
                    firstSpan);
            }
            else
            {
                scope.GeneratedIds.Add(component.StableId, component.Span);
            }
        }

        if (component.Name is { Length: > 0 } id)
        {
            if (!scope.Ids.Add(id))
                _diagnostics.Error("CUI027", $"ID '{id}' is duplicated in the same CUI scope.", component.Span);
            if (scope.Groups.Contains(id))
                _diagnostics.Error("CUI028", $"ID '{id}' conflicts with a Group of the same name in the same CUI scope.", component.Span);
        }

        foreach (var group in component.Groups)
        {
            if (scope.Ids.Contains(group))
                _diagnostics.Error("CUI028", $"Group '{group}' conflicts with an ID of the same name in the same CUI scope.", component.Span);
            scope.Groups.Add(group);
        }

        foreach (var child in component.Children)
            ValidateAddressComponent(child, scope, startsPageScope: false);
        foreach (var child in component.ElseChildren)
            ValidateAddressComponent(child, scope, startsPageScope: false);
    }

    private static string CanonicalBuiltInName(string name)
    {
        var builtInNames = new[]
        {
            "Cui", "Page", "Container", "Text", "Image", "Audio", "Video", "Object",
            "Button", "Input", "Layer", "Anchor", "Theme", "Style", "Property", "Variable",
            "Prefab", "If", "Else", "Repeat", "Import", "Keyframe", "ActionEvent",
            "Resources", "Styles", "Actions", "Templates", "DefaultTheme",
        };
        return builtInNames.FirstOrDefault(candidate =>
            string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }

    private sealed class CuiAddressScope
    {
        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Groups { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CuiSourceSpan> GeneratedIds { get; } = new(StringComparer.Ordinal);
    }

    private CuiCondition ParseCondition(string rawValue, CuiSourceSpan span)
    {
        var negate = rawValue.StartsWith('!');
        var expression = negate ? rawValue[1..] : rawValue;
        var test = ParseMarkupValue(expression, span);
        var isLive = test is not CuiBindingValue { Mode: CuiBindingMode.OneTime };
        return new CuiCondition(test, negate, span, isLive);
    }

    private CuiValue ParseMarkupValue(string raw, CuiSourceSpan span)
    {
        var value = ParseValue(raw, span);
        if (value is CuiInvalidValue invalid)
            _diagnostics.Error(invalid.DiagnosticCode, invalid.Message, invalid.Span);
        return value;
    }

    private string DecodeTextReferences(string rawText, CuiSourceSpan span, out IReadOnlyList<CuiTextPart> textParts)
    {
        var parts = new List<CuiTextPart>();
        var decodedText = new StringBuilder(rawText.Length);
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            var value = literal.ToString();
            parts.Add(new CuiLiteralTextPart(value, span));
            decodedText.Append(value);
            literal.Clear();
        }

        for (var index = 0; index < rawText.Length;)
        {
            if (rawText[index] == '\uE000')
            {
                var markerEnd = rawText.IndexOf('\uE001', index + 1);
                if (markerEnd > index + 1
                    && int.TryParse(rawText[(index + 1)..markerEnd], out var referenceId)
                    && _textReferences.TryGetValue(referenceId, out var referenceText))
                {
                    FlushLiteral();
                    var value = ParseMarkupValue(referenceText, span);
                    parts.Add(new CuiExpressionTextPart(value, span));
                    decodedText.Append(referenceText);
                    index = markerEnd + 1;
                    continue;
                }
            }

            literal.Append(rawText[index++]);
        }

        FlushLiteral();
        textParts = parts;
        return decodedText.ToString();
    }

    #region Value Parsing

    /// <summary>
    /// Parses a CUI attribute value. Detects {Binding ...} and {Resource ...} syntax.
    /// Plain strings become literal values.
    /// </summary>
    public static CuiValue ParseValue(string raw, CuiSourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (string.IsNullOrWhiteSpace(raw))
            return new CuiInvalidValue(raw, "CUI030", "CUI values cannot be empty.", span);

        if (raw[0] == '<' && raw[^1] == '>')
        {
            if (TryParseVariableReference(raw, span, out var reference, out var error))
                return reference;
            if (error is not null)
                return new CuiInvalidValue(raw, "CUI031", error, span);
        }

        if (raw.Length > 2 && raw[0] == '{' && raw[^1] == '}')
        {
            var inner = raw[1..^1].Trim();

            if (inner.StartsWith("Binding ", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("binding ", StringComparison.OrdinalIgnoreCase))
            {
                return ParseBindingValue(inner, span);
            }

            if (inner.StartsWith("Resource ", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("resource ", StringComparison.OrdinalIgnoreCase))
            {
                var key = inner["Resource ".Length..].Trim();
                return string.IsNullOrWhiteSpace(key)
                    ? new CuiInvalidValue(raw, "CUI032", "A Resource value requires a non-empty key.", span)
                    : new CuiResourceValue(key, span);
            }
        }

        return new CuiLiteralValue(raw, span);
    }

    private static CuiValue ParseBindingValue(string inner, CuiSourceSpan span)
    {
        // Format: Binding path[, mode=OneWay|TwoWay|OneTime][, fallback=default][, type=Type]
        var parts = inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var path = parts[0].Trim();
        // Strip leading "Binding " or "binding "
        if (path.StartsWith("Binding ", StringComparison.OrdinalIgnoreCase))
            path = path["Binding ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(path))
            return new CuiInvalidValue(inner, "CUI033", "A Binding value requires a non-empty path.", span);

        var mode = CuiBindingMode.OneWay;
        string? fallback = null;
        string? targetType = null;
        var seenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts.Skip(1))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1)
                return new CuiInvalidValue(inner, "CUI034", $"Binding option '{part}' must use name=value syntax.", span);
            var optionName = part[..separator].Trim();
            var optionValue = part[(separator + 1)..].Trim();
            if (!seenOptions.Add(optionName))
                return new CuiInvalidValue(inner, "CUI035", $"Binding option '{optionName}' is repeated.", span);

            if (string.Equals(optionName, "mode", StringComparison.OrdinalIgnoreCase))
            {
                mode = optionValue.ToLowerInvariant() switch
                {
                    "oneway" => CuiBindingMode.OneWay,
                    "twoway" => CuiBindingMode.TwoWay,
                    "onetime" or "static" => CuiBindingMode.OneTime,
                    _ => CuiBindingMode.Invalid,
                };
                if (mode == CuiBindingMode.Invalid)
                    return new CuiInvalidValue(inner, "CUI036", $"Unknown Binding mode '{optionValue}'.", span);
            }
            else if (string.Equals(optionName, "fallback", StringComparison.OrdinalIgnoreCase))
            {
                fallback = optionValue;
            }
            else if (string.Equals(optionName, "type", StringComparison.OrdinalIgnoreCase))
                targetType = optionValue;
            else
                return new CuiInvalidValue(inner, "CUI037", $"Unknown Binding option '{optionName}'.", span);
        }

        return new CuiBindingValue(path, mode, fallback, span, targetType);
    }

    private static bool TryParseVariableReference(
        string raw,
        CuiSourceSpan span,
        out CuiBindingValue reference,
        out string? error)
    {
        reference = null!;
        error = null;
        if (!raw.StartsWith('<') || !raw.EndsWith('>')) return false;

        var parts = raw[1..^1].Split(':');
        if (parts.Length != 3) return false;
        var targetType = parts[0].Length == 0 ? null : parts[0];
        var path = parts[1];
        var modeName = parts[2];
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "A variable reference requires a non-empty name or path.";
            return false;
        }

        var mode = modeName.ToLowerInvariant() switch
        {
            "" or "live" => CuiBindingMode.OneWay,
            "static" => CuiBindingMode.OneTime,
            _ => CuiBindingMode.Invalid,
        };
        if (mode == CuiBindingMode.Invalid)
        {
            error = $"Unknown variable-reference mode '{modeName}'. Use live or static.";
            return false;
        }

        reference = new CuiBindingValue(path, mode, null, span, targetType);
        return true;
    }

    #endregion
}
