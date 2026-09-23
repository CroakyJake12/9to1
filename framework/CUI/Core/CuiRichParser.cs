using System.Xml;
using System.Xml.Linq;

namespace CakeOS.Cui.Language;

/// <summary>
/// Parses rich CUI markup into a full CuiDocument with resources, styles, actions,
/// templates, component trees, bindings, conditions, and list definitions.
/// This is the authoritative CUI language parser — not a generic XML reader.
/// </summary>
public sealed class CuiRichParser
{
    private readonly CuiDiagnosticBag _diagnostics = new();

    public CuiDiagnosticBag Diagnostics => _diagnostics;

    public CuiDocument Parse(string source, string sourceName = "markup.cui")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        _diagnostics.Clear();

        try
        {
            var document = XDocument.Parse(source, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
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

            if (!string.Equals(root.Name.LocalName, "Cui", StringComparison.Ordinal))
            {
                _diagnostics.Error("CUI003", "CUI root element must be <Cui>.",
                    SpanOf(root, sourceName));
                return CreateFailedDocument(sourceName);
            }

            var resources = ParseResources(root, sourceName);
            var styles = ParseStyles(root, sourceName);
            var actions = ParseActions(root, sourceName);
            var templates = ParseTemplates(root, sourceName);
            var components = ParseComponents(root, sourceName, isRoot: true);

            return new CuiDocument(
                sourceName,
                resources,
                styles,
                actions,
                templates,
                components,
                SpanOf(root, sourceName))
            {
                RootProperties = ParseRootProperties(root, sourceName),
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

    private static IReadOnlyDictionary<string, CuiValue> ParseRootProperties(
        XElement root,
        string sourceName)
    {
        var properties = new Dictionary<string, CuiValue>(StringComparer.Ordinal);
        foreach (var attribute in root.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace != XNamespace.None)
                continue;

            properties[attribute.Name.LocalName] = ParseValue(
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

        var resourcesElement = root.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Resources", StringComparison.Ordinal));
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
            var value = ParseValue(valueAttr.Value, span);
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
            if (!string.Equals(styleChild.Name.LocalName, "Style", StringComparison.Ordinal)) continue;
            var selector = styleChild.Attribute("selector")?.Value ?? string.Empty;
            var span = SpanOf(styleChild, sourceName);
            var setters = new List<CuiStyleSetter>();

            foreach (var setter in styleChild.Elements())
            {
                if (!string.Equals(setter.Name.LocalName, "Setter", StringComparison.Ordinal)) continue;
                var property = setter.Attribute("property")?.Value;
                var val = setter.Attribute("value")?.Value;
                if (property is null || val is null) continue;

                var setterSpan = SpanOf(setter, sourceName);
                setters.Add(new CuiStyleSetter(property, ParseValue(val, setterSpan), setterSpan));
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
            if (!string.Equals(child.Name.LocalName, "Action", StringComparison.Ordinal)) continue;
            var name = child.Attribute("name")?.Value;
            var command = child.Attribute("command")?.Value;
            if (name is null || command is null) continue;

            var span = SpanOf(child, sourceName);
            var paramAttr = child.Attribute("parameter");
            var parameter = paramAttr is not null ? ParseValue(paramAttr.Value, span) : null;

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
            if (!string.Equals(child.Name.LocalName, "Template", StringComparison.Ordinal)) continue;
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
        var result = new List<CuiComponent>();
        foreach (var child in parent.Elements())
        {
            if (child.Name.Namespace != XNamespace.None) continue;
            if (isRoot && !string.Equals(child.Name.LocalName, "Cui", StringComparison.Ordinal))
            {
                // Inside <Cui>, top-level sections are Resources/Styles/Actions/Templates
                var localName = child.Name.LocalName;
                if (localName is "Resources" or "Styles" or "Actions" or "Templates")
                    continue;
            }

            var component = ParseComponent(child, sourceName);
            if (component is not null) result.Add(component);
        }
        return result;
    }

    private IReadOnlyList<CuiComponent> ParseChildComponents(
        XElement parent, string sourceName)
    {
        var result = new List<CuiComponent>();
        foreach (var child in parent.Elements())
        {
            if (child.Name.Namespace != XNamespace.None) continue;
            var component = ParseComponent(child, sourceName);
            if (component is not null) result.Add(component);
        }
        return result;
    }

    private CuiComponent? ParseComponent(XElement element, string sourceName)
    {
        if (element.Name.Namespace != XNamespace.None)
        {
            _diagnostics.Error("CUI004", "CUI does not accept XML namespaces.",
                SpanOf(element, sourceName));
            return null;
        }

        var type = element.Name.LocalName;
        var span = SpanOf(element, sourceName);

        // === DefaultTheme is a first-class CUI construct, not a visual control ===
        if (string.Equals(type, "DefaultTheme", StringComparison.Ordinal))
        {
            return ParseDefaultTheme(element, sourceName);
        }

        // Parse attributes
        string? name = null;
        var classes = new List<string>();
        var properties = new Dictionary<string, CuiValue>(StringComparer.Ordinal);
        var actions = new Dictionary<string, CuiActionReference>(StringComparer.Ordinal);
        CuiCondition? condition = null;
        CuiListDefinition? list = null;

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

            switch (attrName)
            {
                case "id":
                case "name":
                    name = rawValue;
                    break;

                case "class":
                    classes.AddRange(rawValue.Split(' ',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;

                case "action":
                    actions[attrName] = new CuiActionReference(rawValue, attrSpan);
                    break;

                case "condition":
                    condition = new CuiCondition(
                        new CuiLiteralValue(rawValue, attrSpan),
                        rawValue.StartsWith('!'),
                        attrSpan);
                    break;

                case "items":
                    if (element.Attribute("item-template") is { } itemTemplateAttr)
                    {
                        var emptyTemplate = element.Attribute("empty-template")?.Value;
                        list = new CuiListDefinition(
                            ParseValue(rawValue, attrSpan),
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
                        properties[attrName] = ParseValue(rawValue, attrSpan);
                    }
                    break;
            }
        }

        var children = ParseChildComponents(element, sourceName);

        return new CuiComponent(
            type, name, classes, properties, actions,
            children, condition, list, span)
        {
            Text = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim(),
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

    #region Value Parsing

    /// <summary>
    /// Parses a CUI attribute value. Detects {Binding ...} and {Resource ...} syntax.
    /// Plain strings become literal values.
    /// </summary>
    public static CuiValue ParseValue(string raw, CuiSourceSpan span)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);

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
                return new CuiResourceValue(key, span);
            }
        }

        return new CuiLiteralValue(raw, span);
    }

    private static CuiBindingValue ParseBindingValue(string inner, CuiSourceSpan span)
    {
        // Format: Binding path[, mode=OneWay|TwoWay|OneTime][, fallback=default]
        var parts = inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var path = parts[0].Trim();
        // Strip leading "Binding " or "binding "
        if (path.StartsWith("Binding ", StringComparison.OrdinalIgnoreCase))
            path = path["Binding ".Length..].Trim();
        else if (path.StartsWith("binding ", StringComparison.OrdinalIgnoreCase))
            path = path["binding ".Length..].Trim();

        var mode = CuiBindingMode.OneWay;
        string? fallback = null;

        foreach (var part in parts.Skip(1))
        {
            var kv = part.Split('=', StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;

            if (string.Equals(kv[0], "mode", StringComparison.OrdinalIgnoreCase))
            {
                mode = kv[1] switch
                {
                    "OneWay" or "oneway" => CuiBindingMode.OneWay,
                    "TwoWay" or "twoway" => CuiBindingMode.TwoWay,
                    "OneTime" or "onetime" => CuiBindingMode.OneTime,
                    _ => CuiBindingMode.OneWay,
                };
            }
            else if (string.Equals(kv[0], "fallback", StringComparison.OrdinalIgnoreCase))
            {
                fallback = kv[1];
            }
        }

        return new CuiBindingValue(path, mode, fallback, span);
    }

    #endregion
}
