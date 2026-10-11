using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Language;
using Avalonia.Layout;
using System.Collections.Frozen;
using CakeOS.Cui;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Maps CUI component names and registered specialised component types to native
/// Avalonia controls. Custom names remain case-sensitive; built-in CUI names and
/// type values are case-insensitive.
/// </summary>
public sealed class CuiControlRegistry
{
    /// <summary>Registry 2 removes donor authoring tags and exposes the canonical typed CUI surface.</summary>
    public const string ComponentRegistryVersion = "2.0";
    private readonly Dictionary<string, Func<CuiComponent, Control>> _customFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CuiComponent, Control>> _objectFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CuiComponent, Control>> _authoredFactories = new(StringComparer.Ordinal);
    private ICuiBackdropRenderer? _backdropRenderer;
    private Func<string, Control, Avalonia.Media.IImage?>? _imageSourceResolver;
    private static readonly FrozenDictionary<string, CuiRuntimeElementDescriptor> BuiltInElements = CreateElementDescriptors();
    private static readonly FrozenDictionary<string, CuiRuntimePropertyDescriptor> BuiltInProperties = CreatePropertyDescriptors();
    private static readonly FrozenDictionary<string, string> PropertyAliases = CreatePropertyAliases();

    /// <summary>The canonical read-only CUI element/property metadata registry.</summary>
    public static CuiControlRegistry Default { get; } = new();

    /// <summary>Resolve host-owned, bounded image identities without opening arbitrary files or network locations.</summary>
    public void RegisterImageSourceResolver(Func<string, Control, Avalonia.Media.IImage?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (_imageSourceResolver is not null) throw new InvalidOperationException("A CUI image source resolver is already registered.");
        _imageSourceResolver = resolver;
    }

    internal Avalonia.Media.IImage? ResolveImageSource(string source, Control target) => _imageSourceResolver?.Invoke(source, target);

    /// <summary>Read-only built-in element descriptors exposed to compiler and tooling consumers.</summary>
    public IReadOnlyCollection<CuiRuntimeElementDescriptor> Elements =>
        BuiltInElements.Values.ToArray();

    /// <summary>Read-only built-in typed property descriptors exposed to compiler and tooling consumers.</summary>
    public IReadOnlyCollection<CuiRuntimePropertyDescriptor> Properties => BuiltInProperties.Values.ToArray();

    /// <summary>Tooling reads the same versioned authoring registry; donor CLR types are not schema syntax.</summary>
    public string ExportSchema() => System.Text.Json.JsonSerializer.Serialize(new
    {
        registryVersion = ComponentRegistryVersion,
        languageVersion = CuiRuntimeCompatibility.LanguageVersion,
        runtimeAbiVersion = CuiRuntimeCompatibility.RuntimeAbiVersion,
        elements = Elements.OrderBy(element => element.Name, StringComparer.Ordinal).Select(element => new
        {
            name = element.Name, visual = element.IsVisual, specialized = element.RequiresSpecializedHost,
            types = TypesFor(element.Name).Order(StringComparer.Ordinal),
            properties = element.AllowedProperties.Order(StringComparer.Ordinal)
        }),
        declarations = CuiLanguageVocabulary.NonvisualElements.Order(StringComparer.Ordinal),
        aliases = PropertyAliases.Where(alias => !alias.Key.Equals(alias.Value, StringComparison.OrdinalIgnoreCase))
            .OrderBy(alias => alias.Key, StringComparer.Ordinal).Select(alias => new { name = alias.Key, property = alias.Value }),
        properties = Properties.OrderBy(property => property.Name, StringComparer.Ordinal).Select(property => new
        {
            name = property.Name, type = property.LanguageType,
            elements = property.SupportedElementTypes.Order(StringComparer.Ordinal),
            values = property.AllowedValues.Order(StringComparer.Ordinal),
            animatable = property.IsAnimatable, placement = property.IsAttached, writable = property.IsWritable
        }),
        objectTypes = _objectFactories.Keys.Order(StringComparer.Ordinal)
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public static IReadOnlySet<string> TypesFor(string element) => (element.ToLowerInvariant() switch
    {
        "container" => new[] { "Grid", "Vertical Stack", "Horizontal Stack", "Absolute", "Overlay", "Wrap", "Menu", "Context Menu" },
        "input" => new[] { "Text", "Number", "Checkbox", "Radio", "Slider", "Select", "List" },
        "button" => new[] { "Action", "Menu Item" },
        _ => Array.Empty<string>()
    }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve a built-in or registered custom component name.</summary>
    public bool TryResolveElement(string name, out CuiRuntimeElementDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (BuiltInElements.TryGetValue(name, out descriptor!))
            return true;
        descriptor = null!;
        return false;
    }

    /// <summary>Resolve canonical metadata for a registered runtime property.</summary>
    public bool TryGetProperty(string name, out CuiRuntimePropertyDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (CuiLanguageVocabulary.IsForbiddenProperty(name)) { descriptor = null!; return false; }
        var key = NormalizePropertyName(name);
        if (PropertyAliases.TryGetValue(key, out var canonicalName))
            key = canonicalName;
        if (!BuiltInProperties.TryGetValue(key, out descriptor!))
            return false;
        if (descriptor.Name.Equals("Background", StringComparison.OrdinalIgnoreCase))
            descriptor = descriptor with { SupportsBackdrop = _backdropRenderer is not null };
        return true;
    }

    /// <summary>Register the host's live compositor-backed Background modifier implementation.</summary>
    public void RegisterBackdropRenderer(ICuiBackdropRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (_backdropRenderer is not null)
            throw new InvalidOperationException("A live CUI backdrop renderer is already registered.");
        _backdropRenderer = renderer;
    }

    internal bool TryApplyBackdrop(Control target, string modifierChain, CuiSourceSpan source, out string? error)
    {
        if (_backdropRenderer is null)
        {
            error = "Live Background Blur/Refract modifiers require a compositor backdrop host, which is not registered.";
            return false;
        }
        return _backdropRenderer.TryApply(target, modifierChain, source, out error);
    }

    /// <summary>Register a specialised control type or renderer host.</summary>
    public void RegisterControlType(string typeName, Func<CuiComponent, Control> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(factory);

        if (IsReservedType(typeName))
            throw new ArgumentException($"'{typeName}' is a reserved built-in CUI component type.", nameof(typeName));
        if (!_customFactories.TryAdd(typeName, factory))
            throw new ArgumentException($"A CUI control type named '{typeName}' is already registered.", nameof(typeName));
    }

    /// <summary>Register a typed renderer for an Object component's Type property.</summary>
    public void RegisterObjectRenderer(string objectType, Func<CuiComponent, Control> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_objectFactories.TryAdd(objectType, factory))
            throw new ArgumentException($"A CUI object renderer named '{objectType}' is already registered.", nameof(objectType));
    }

    internal bool TryCreate(CuiComponent component, Func<CuiValue, string?> resolveValue, out Control? control, out string? error)
    {
        control = null;
        error = null;
        if (CuiLanguageVocabulary.IsForbiddenElement(component.Type))
        { error = $"'{component.Type}' is forbidden production CUI syntax. {CuiLanguageVocabulary.ElementCorrection(component.Type)}"; return false; }
        var authoredType = GetTypeValue(component, resolveValue);
        if (authoredType is not null && component.Type is not ("Object" or "Audio" or "Video")
            && !TypesFor(component.Type).Contains(authoredType) && !_customFactories.ContainsKey(authoredType))
        { error = $"Unknown {component.Type} Type '{authoredType}'. Declare a valid CUI Type or register its typed implementation."; return false; }

        if (component.Type.Equals("Object", StringComparison.OrdinalIgnoreCase)
            || component.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase) || component.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
        {
            var objectType = GetTypeValue(component, resolveValue)
                ?? (component.Type.Equals("Object", StringComparison.OrdinalIgnoreCase) ? null : component.Type);
            if (objectType is not null && _objectFactories.TryGetValue(objectType, out var objectFactory))
            {
                control = objectFactory(component);
                return true;
            }

            error = objectType is null
                ? "Object requires a Type identifying a registered renderer."
                : $"No renderer is registered for CUI Object Type '{objectType}'.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(component.AuthoredId) &&
            _authoredFactories.TryGetValue(component.AuthoredId, out var authoredFactory))
        {
            control = authoredFactory(component);
            if (control is null)
            {
                error = $"The CUI authored control binding '{component.AuthoredId}' returned no control.";
                return false;
            }
            return true;
        }

        var registeredType = GetTypeValue(component, resolveValue);
        if (CuiLanguageVocabulary.VisualElements.Contains(component.Type) && registeredType is not null
            && _customFactories.TryGetValue(registeredType, out var customFactory))
        {
            control = customFactory(component);
            if (control is null)
            {
                error = $"The registered CUI control factory '{component.Type}' returned no control.";
                return false;
            }
            return true;
        }

        if (TryCreateBuiltIn(component, resolveValue, out control, out error))
            return true;

        error = $"No native control or specialised renderer is registered for CUI component '{component.Type}'.";
        return false;
    }

    private static bool TryCreateBuiltIn(
        CuiComponent component,
        Func<CuiValue, string?> resolveValue,
        out Control? control,
        out string? error)
    {
        error = null;
        control = component.Type.ToLowerInvariant() switch
        {
            "page" or "layer" => new Panel(),
            "container" => CreateContainer(component, resolveValue, out error),
            "text" => new TextBlock(),
            "image" => new Image(),
            "button" => CreateButton(component, resolveValue, out error),
            "input" => CreateInput(component, resolveValue, out error),
            "anchor" => new ContentControl(),
            _ => null,
        };

        return control is not null;
    }

    private static Control? CreateContainer(
        CuiComponent component,
        Func<CuiValue, string?> resolveValue,
        out string? error)
    {
        error = null;
        var type = GetTypeValue(component, resolveValue);
        if (string.IsNullOrWhiteSpace(type))
        {
            error = "Container requires a registered Type (Grid, Vertical Stack, Horizontal Stack, or Absolute).";
            return null;
        }

        return type.ToLowerInvariant() switch
        {
            "grid" or "vertical stack" or "horizontal stack" or "absolute" or "overlay" or "wrap" => new CuiContainerControl(type),
            "menu" => new Menu(),
            "context menu" => new ContextMenu(),
            _ => UnknownContainerType(type, out error),
        };
    }

    private static Control? CreateInput(
        CuiComponent component,
        Func<CuiValue, string?> resolveValue,
        out string? error)
    {
        error = null;
        var type = GetTypeValue(component, resolveValue) ?? "Text";
        return type.ToLowerInvariant() switch
        {
            "text" => new TextBox(),
            "number" or "numeric" => new NumericUpDown(),
            "checkbox" or "check box" => new CheckBox(),
            "radio" => new RadioButton(),
            "slider" => new Slider(),
            "select" => new ComboBox(),
            "list" => new ListBox(),
            _ => UnknownInputType(type, out error),
        };
    }

    private static Control? CreateButton(CuiComponent component, Func<CuiValue, string?> resolveValue, out string? error)
    {
        error = null;
        var type = GetTypeValue(component, resolveValue) ?? "Action";
        return type.ToLowerInvariant() switch
        {
            "action" => new Button(),
            "menu item" => new MenuItem(),
            _ => UnknownInputType(type, out error)
        };
    }

    private static Control? UnknownContainerType(string type, out string? error)
    {
        error = $"Unknown CUI Container Type '{type}'. Supported built-in types are Grid, Vertical Stack, Horizontal Stack, and Absolute.";
        return null;
    }

    private static Control? UnknownInputType(string type, out string? error)
    {
        error = $"Unknown CUI Input Type '{type}'. Register a specialised component for this input behaviour.";
        return null;
    }

    private static string? GetTypeValue(CuiComponent component, Func<CuiValue, string?> resolveValue)
    {
        var typeProperty = component.Properties.FirstOrDefault(
            pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase));
        return typeProperty.Key is null ? null : resolveValue(typeProperty.Value);
    }

    /// <summary>Bind a prepared control to one authored component identity without replacing a reserved built-in type.</summary>
    public void BindAuthoredControl(string authoredId, Func<CuiComponent, Control> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredId);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_authoredFactories.TryAdd(authoredId, factory))
            throw new ArgumentException($"A CUI control binding for authored id '{authoredId}' is already registered.", nameof(authoredId));
    }

    /// <summary>True when a host has registered a specialised control type or object renderer for this name.</summary>
    public bool IsRegisteredRuntimeType(string name) =>
        name is not null && (_customFactories.ContainsKey(name) || _objectFactories.ContainsKey(name));

    private static bool IsReservedType(string typeName) => BuiltInElements.Keys.Contains(typeName, StringComparer.OrdinalIgnoreCase) || CuiLanguageVocabulary.IsForbiddenElement(typeName);

    private static FrozenDictionary<string, CuiRuntimeElementDescriptor> CreateElementDescriptors()
    {
        var common = new[]
        {
            "Type", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Opacity", "Active",
            "Hidden", "Interactive", "IsVisible", "IsEnabled", "Focusable", "TabIndex", "AccessibleName", "AccessibleDescription",
            "Role", "AutomationId", "HorizontalAlignment", "VerticalAlignment", "IsHitTestVisible", "Effect", "Shadow", "Layer", "Modal",
            "Clip", "Cursor", "Rotate", "Scale", "ScaleX", "ScaleY", "Skew", "SkewX", "SkewY", "Translate",
            "TranslateX", "TranslateY", "Column", "Row", "ColumnSpan", "RowSpan", "X", "Y", "XPixel", "YPixel", "XPercent", "YPercent", "ActiveIndependently"
        };
        var container = common.Concat(new[]
        {
            "Type", "Padding", "Background", "Foreground", "BorderColor", "BorderWidth", "BorderBrush", "BorderThickness",
            "CornerRadius", "Orientation", "HorizontalScrolling", "VerticalScrolling", "ColumnSizes", "RowSizes", "Rows", "Columns", "CellGap", "CellMargin", "Border", "XInherited", "YInherited"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var text = common.Concat(new[]
        {
            "Text", "Foreground", "FontFamily", "FontSize", "FontWeight", "FontStyle", "TextDecoration",
            "TextWrapping", "TextAlignment", "VerticalTextAlignment"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var input = common.Concat(new[]
        {
            "Type", "Text", "Value", "Checked", "IsChecked", "PlaceholderText", "AcceptsReturn", "Foreground",
            "Background", "FontFamily", "FontSize", "FontWeight", "FontStyle", "TextWrapping", "ItemsSource", "SelectedIndex", "Minimum", "Maximum", "Increment", "PasswordChar", "Watermark", "Padding", "CornerRadius", "Content", "WriteBack", "Border", "BorderColor", "BorderWidth", "BorderBrush", "BorderThickness"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var content = common.Concat(new[]
        {
            "Content", "Text", "Type", "Foreground", "Background", "FontFamily", "FontSize", "FontWeight", "FontStyle", "Padding", "CornerRadius", "HorizontalContentAlignment", "VerticalContentAlignment", "Border", "BorderColor", "BorderWidth", "BorderBrush", "BorderThickness"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var entries = new (string Name, bool Specialized, string[] Aliases)[]
        {
            ("Page", false, []), ("Container", false, []), ("Text", false, []), ("Image", false, []),
            ("Audio", true, []), ("Video", true, []), ("Object", true, []), ("Button", false, []),
            ("Input", false, []), ("Layer", false, []), ("Anchor", false, [])
        };

        var result = new Dictionary<string, CuiRuntimeElementDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, specialized, aliases) in entries)
        {
            var properties = name switch
            {
                "Page" or "Container" => container,
                "Text" => text,
                "Input" => input,
                "Image" => common.Concat(new[] { "Source", "Stretch", "Opacity", "Background" }).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                "Button" => content.Concat(new[] { "Action" }).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                "Layer" => common.Concat(new[] { "Background", "Padding", "ZIndex", "Interactive", "ActiveIndependently" }).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                "Anchor" => common.Concat(new[] { "Target", "Placement" }).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                "Audio" or "Video" => common.Concat(new[] { "Type", "Source" }).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                "Object" => common.Concat(new[] { "Text", "Type" }).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                _ => common.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            };
            result[name] = new CuiRuntimeElementDescriptor(
                name, aliases.ToFrozenSet(StringComparer.Ordinal), properties, specialized) { IsVisual = name != "Anchor" };
        }
        return result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, CuiRuntimePropertyDescriptor> CreatePropertyDescriptors()
    {
        var entries = new (string Name, string LanguageType, Type RuntimeType, bool Writable)[]
        {
            ("Panel", "symbol", typeof(string), false),
            ("ItemRow", "integer", typeof(int), true), ("ItemColumn", "integer", typeof(int), true),
            ("ItemRowSpan", "integer", typeof(int), true), ("ItemColumnSpan", "integer", typeof(int), true),
            ("Spacing", "length", typeof(double), true),
            ("CellGap", "length", typeof(double), true), ("CellMargin", "thickness", typeof(Thickness), true),
            ("Type", "symbol", typeof(string), false),
            ("Width", "length", typeof(double), true), ("Height", "length", typeof(double), true),
            ("MinWidth", "length", typeof(double), true), ("MinHeight", "length", typeof(double), true),
            ("MaxWidth", "length", typeof(double), true), ("MaxHeight", "length", typeof(double), true),
            ("Margin", "thickness", typeof(Thickness), true), ("Padding", "thickness", typeof(Thickness), true),
            ("Background", "brush", typeof(Avalonia.Media.IBrush), true),
            ("Foreground", "brush", typeof(Avalonia.Media.IBrush), true),
            ("BorderBrush", "brush", typeof(Avalonia.Media.IBrush), true),
            ("BorderThickness", "thickness", typeof(Thickness), true), ("BorderColor", "color", typeof(Avalonia.Media.Color), true),
            ("BorderWidth", "length", typeof(double), true), ("CornerRadius", "length", typeof(CornerRadius), true),
            ("Border", "brush", typeof(Avalonia.Media.IBrush), true),
            ("Opacity", "number", typeof(double), true), ("Active", "boolean", typeof(bool), true),
            ("Hidden", "boolean", typeof(bool), true), ("IsVisible", "boolean", typeof(bool), true),
            ("IsEnabled", "boolean", typeof(bool), true), ("Focusable", "boolean", typeof(bool), true),
            ("TabIndex", "integer", typeof(int), true), ("AccessibleName", "text", typeof(string), true),
            ("AccessibleDescription", "text", typeof(string), true), ("Role", "symbol", typeof(string), true),
            ("Header", "text", typeof(string), true), ("Text", "text", typeof(string), true), ("Content", "content", typeof(object), true),
            ("ItemsSource", "collection", typeof(System.Collections.IEnumerable), true), ("SelectedIndex", "integer", typeof(int), true),
            ("Value", "value", typeof(object), true), ("Checked", "boolean", typeof(bool), true),
            ("Orientation", "symbol", typeof(Orientation), true),
            ("ColumnSizes", "grid-sizes", typeof(Avalonia.Controls.ColumnDefinitions), true),
            ("RowSizes", "grid-sizes", typeof(Avalonia.Controls.RowDefinitions), true),
            ("Columns", "integer", typeof(int), true), ("Rows", "integer", typeof(int), true),
            ("Column", "integer", typeof(int), true), ("Row", "integer", typeof(int), true),
            ("ColumnSpan", "integer", typeof(int), true), ("RowSpan", "integer", typeof(int), true),
            ("FontFamily", "font-family", typeof(Avalonia.Media.FontFamily), true), ("FontSize", "length", typeof(double), true),
            ("FontWeight", "font-weight", typeof(Avalonia.Media.FontWeight), true),
            ("FontStyle", "font-style", typeof(Avalonia.Media.FontStyle), true),
            ("TextDecoration", "text-decoration", typeof(Avalonia.Media.TextDecorationCollection), true),
            ("TextWrapping", "symbol", typeof(Avalonia.Media.TextWrapping), true),
            ("AcceptsReturn", "boolean", typeof(bool), true), ("PlaceholderText", "text", typeof(string), true),
            ("Watermark", "text", typeof(string), true), ("PasswordChar", "text", typeof(string), true),
            ("Minimum", "number", typeof(double), true), ("Maximum", "number", typeof(double), true), ("Increment", "number", typeof(double), true),
            ("WriteBack", "boolean", typeof(bool), true),
            ("X", "number", typeof(double), true), ("Y", "number", typeof(double), true),
            ("XPixel", "length", typeof(double), true), ("YPixel", "length", typeof(double), true),
            ("XPercent", "number", typeof(double), true), ("YPercent", "number", typeof(double), true),
            ("XInherited", "boolean", typeof(bool), true), ("YInherited", "boolean", typeof(bool), true),
            ("AutomationId", "text", typeof(string), true),
            ("HorizontalAlignment", "symbol", typeof(HorizontalAlignment), true),
            ("VerticalAlignment", "symbol", typeof(VerticalAlignment), true), ("Title", "text", typeof(string), true),
            ("HorizontalContentAlignment", "symbol", typeof(HorizontalAlignment), true), ("VerticalContentAlignment", "symbol", typeof(VerticalAlignment), true),
            ("Source", "uri", typeof(string), true), ("Stretch", "symbol", typeof(Avalonia.Media.Stretch), true),
            ("ZIndex", "integer", typeof(int), true), ("Interactive", "boolean", typeof(bool), true),
            ("ActiveIndependently", "boolean", typeof(bool), true), ("Target", "text", typeof(string), true),
            ("Modal", "boolean", typeof(bool), true), ("Layer", "integer", typeof(int), true),
            ("Placement", "symbol", typeof(string), true), ("Left", "length", typeof(double), true),
            ("Top", "length", typeof(double), true),
            ("IsChecked", "boolean", typeof(bool), true), ("Rotate", "angle", typeof(double), true),
            ("Scale", "number", typeof(double), true), ("ScaleX", "number", typeof(double), true),
            ("ScaleY", "number", typeof(double), true), ("Skew", "angle", typeof(double), true),
            ("SkewX", "angle", typeof(double), true), ("SkewY", "angle", typeof(double), true),
            ("Translate", "vector", typeof(Vector), true), ("TranslateX", "length", typeof(double), true),
            ("TranslateY", "length", typeof(double), true), ("Effect", "effect", typeof(Avalonia.Media.IEffect), true),
            ("Shadow", "shadow", typeof(Avalonia.Media.IEffect), true), ("Clip", "geometry", typeof(Avalonia.Media.Geometry), true),
            ("Cursor", "cursor", typeof(string), true), ("TextAlignment", "symbol", typeof(Avalonia.Media.TextAlignment), true),
            ("VerticalTextAlignment", "symbol", typeof(Avalonia.Media.AlignmentY), true),
            ("IsHitTestVisible", "boolean", typeof(bool), true),
            ("HorizontalScrolling", "boolean", typeof(bool), true), ("VerticalScrolling", "boolean", typeof(bool), true)
        };

        return entries.ToFrozenDictionary(
            entry => entry.Name,
            entry => new CuiRuntimePropertyDescriptor(
                entry.Name, entry.LanguageType, entry.RuntimeType,
                BuiltInElements.Values
                    .Where(element => element.AllowedProperties.Contains(entry.Name))
                    .Select(element => element.Name)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                IsAnimatableProperty(entry.Name), SupportsBackdrop: false,
                IsAttached: entry.Name is "Row" or "Column" or "RowSpan" or "ColumnSpan" or "X" or "Y" or "XPixel" or "YPixel" or "XPercent" or "YPercent",
                IsWritable: entry.Writable)
            {
                AllowedValues = GetAllowedValues(entry.Name)
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> GetAllowedValues(string propertyName)
    {
        var values = propertyName switch
        {
            "Type" => Array.Empty<string>(), // Type values are resolved per element, not against another element's type family.
            "Orientation" => new[] { "Horizontal", "Vertical" },
            "HorizontalAlignment" => new[] { "Left", "Center", "Right", "Stretch" },
            "VerticalAlignment" => new[] { "Top", "Center", "Bottom", "Stretch" },
            "VerticalTextAlignment" => new[] { "Top", "Center", "Bottom" },
            "TextAlignment" => new[] { "Left", "Center", "Right", "Justify" },
            "TextWrapping" => new[] { "NoWrap", "Wrap", "WrapWithOverflow" },
            "FontStyle" => new[] { "Normal", "Italic", "Oblique" },
            "Stretch" => new[] { "None", "Fill", "Uniform", "UniformToFill" },
            "Cursor" => new[] { "Arrow", "Hand", "IBeam", "Wait", "Cross", "SizeAll" },
            "Placement" => new[] { "Top", "Bottom", "Left", "Right", "Center", "Absolute" },
            _ => Array.Empty<string>(),
        };
        return values.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, string> CreatePropertyAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in BuiltInProperties.Keys)
            aliases[NormalizePropertyName(property)] = property;
        foreach (var (alias, canonical) in new (string Alias, string Canonical)[]
        {
            ("borderbrush", "BorderBrush"), ("borderthickness", "BorderThickness"),
            ("color", "Foreground"),
            ("columnspan", "ColumnSpan"), ("rowspan", "RowSpan"), ("textdecoration", "TextDecoration"),
            ("textdecorations", "TextDecoration"), ("x-px", "XPixel"), ("y-px", "YPixel"), ("x-%", "XPercent"), ("y-%", "YPercent"),
            ("x-inherited", "XInherited"), ("y-inherited", "YInherited"), ("spacing", "CellGap"),
            ("automation-id", "AutomationId"), ("ischecked", "IsChecked"),
            ("min-width", "MinWidth"), ("min-height", "MinHeight"), ("max-width", "MaxWidth"),
            ("max-height", "MaxHeight"), ("font-size", "FontSize"), ("font-family", "FontFamily"),
            ("font-weight", "FontWeight"), ("font-style", "FontStyle"),
            ("horizontal-alignment", "HorizontalAlignment"), ("vertical-alignment", "VerticalAlignment"),
            ("placeholder-text", "PlaceholderText"),
            ("multiline", "AcceptsReturn"),
            ("rotation", "Rotate"), ("rotate", "Rotate"), ("scale-x", "ScaleX"), ("scale-y", "ScaleY"),
            ("skew-x", "SkewX"), ("skew-y", "SkewY"), ("translate-x", "TranslateX"), ("translate-y", "TranslateY"),
            ("horizontal-scrolling", "HorizontalScrolling"), ("vertical-scrolling", "VerticalScrolling"),
            ("text-alignment", "TextAlignment"), ("vertical-text-alignment", "VerticalTextAlignment"),
            ("is-hit-test-visible", "IsHitTestVisible"),
            ("accessibility-name", "AccessibleName")
        })
            aliases[NormalizePropertyName(alias)] = canonical;
        return aliases.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePropertyName(string name) =>
        string.Concat(name.Where(character => character is not '-' and not '.' and not '_'));

    private static bool IsAnimatableProperty(string name) =>
        name is "Width" or "Height" or "Margin" or "Padding" or "Background" or "Foreground" or "Opacity"
            or "Rotate" or "Scale" or "ScaleX" or "ScaleY" or "Skew" or "SkewX" or "SkewY"
            or "Translate" or "TranslateX" or "TranslateY";
}




