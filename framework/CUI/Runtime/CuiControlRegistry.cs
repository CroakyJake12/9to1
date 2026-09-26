using Avalonia;
using Avalonia.Controls;
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
    private readonly Dictionary<string, Func<CuiComponent, Control>> _customFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CuiComponent, Control>> _objectFactories = new(StringComparer.Ordinal);
    private ICuiBackdropRenderer? _backdropRenderer;
    private static readonly FrozenDictionary<string, CuiRuntimeElementDescriptor> BuiltInElements = CreateElementDescriptors();
    private static readonly FrozenDictionary<string, CuiRuntimePropertyDescriptor> BuiltInProperties = CreatePropertyDescriptors();
    private static readonly FrozenDictionary<string, string> PropertyAliases = CreatePropertyAliases();

    /// <summary>The canonical read-only CUI element/property metadata registry.</summary>
    public static CuiControlRegistry Default { get; } = new();

    /// <summary>Read-only built-in element descriptors exposed to compiler and tooling consumers.</summary>
    public IReadOnlyCollection<CuiRuntimeElementDescriptor> Elements =>
        BuiltInElements.Values.Concat(_customFactories.Keys.Select(name => new CuiRuntimeElementDescriptor(
            name, FrozenSet<string>.Empty, BuiltInProperties.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase), false)))
            .ToArray();

    /// <summary>Read-only built-in typed property descriptors exposed to compiler and tooling consumers.</summary>
    public IReadOnlyCollection<CuiRuntimePropertyDescriptor> Properties => BuiltInProperties.Values.ToArray();

    /// <summary>Resolve a built-in or registered custom component name.</summary>
    public bool TryResolveElement(string name, out CuiRuntimeElementDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (BuiltInElements.TryGetValue(name, out descriptor!))
            return true;
        if (_customFactories.ContainsKey(name))
        {
            descriptor = new CuiRuntimeElementDescriptor(
                name, FrozenSet<string>.Empty, BuiltInProperties.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase), false);
            return true;
        }
        descriptor = null!;
        return false;
    }

    /// <summary>Resolve canonical metadata for a registered runtime property.</summary>
    public bool TryGetProperty(string name, out CuiRuntimePropertyDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
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

        if (component.Type.Equals("Object", StringComparison.OrdinalIgnoreCase))
        {
            var objectType = GetTypeValue(component, resolveValue);
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

        if (TryCreateBuiltIn(component, resolveValue, out control, out error))
            return true;

        if (_customFactories.TryGetValue(component.Type, out var customFactory))
        {
            control = customFactory(component);
            if (control is null)
            {
                error = $"The registered CUI control factory '{component.Type}' returned no control.";
                return false;
            }
            return true;
        }

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
            "button" => new Button(),
            "input" => CreateInput(component, resolveValue, out error),

            // Existing runtime names remain supported for compatibility with
            // current source while first-party markup moves to the compact CUI vocabulary.
            "panel" => new Panel(),
            "stackpanel" => new StackPanel(),
            "grid" => new Grid(),
            "dockpanel" => new DockPanel(),
            "wrappanel" => new WrapPanel(),
            "border" => new Border(),
            "scrollviewer" => new ScrollViewer(),
            "textblock" => new TextBlock(),
            "textbox" => new TextBox(),
            "checkbox" => new CheckBox(),
            "radiobutton" => new RadioButton(),
            "slider" => new Slider(),
            "progressbar" => new ProgressBar(),
            "canvas" => new Canvas(),
            "tabcontrol" => new TabControl(),
            "tabitem" => new TabItem(),
            "listbox" => new ListBox(),
            "combobox" => new ComboBox(),
            "treeview" => new TreeView(),
            "menu" => new Menu(),
            "menuitem" => new MenuItem(),
            "separator" => new Separator(),
            "contentcontrol" => new ContentControl(),
            "itemscontrol" => new ItemsControl(),
            "usercontrol" => new UserControl(),
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
            "grid" => new Grid(),
            "vertical stack" => new StackPanel { Orientation = Orientation.Vertical },
            "horizontal stack" => new StackPanel { Orientation = Orientation.Horizontal },
            "absolute" => new Canvas(),
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
            "slider" => new Slider(),
            _ => UnknownInputType(type, out error),
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

    private static bool IsReservedType(string typeName) => BuiltInElements.Keys.Contains(typeName, StringComparer.OrdinalIgnoreCase);

    private static FrozenDictionary<string, CuiRuntimeElementDescriptor> CreateElementDescriptors()
    {
        var common = new[]
        {
            "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Opacity", "Active",
            "Hidden", "IsVisible", "IsEnabled", "Focusable", "TabIndex", "AccessibleName", "AccessibleDescription",
            "Role", "AutomationId", "HorizontalAlignment", "VerticalAlignment", "IsHitTestVisible", "Effect", "Shadow", "Layer", "Modal",
            "Clip", "Cursor", "Rotate", "Scale", "ScaleX", "ScaleY", "Skew", "SkewX", "SkewY", "Translate",
            "TranslateX", "TranslateY", "GridColumn", "GridRow", "ColumnSpan", "RowSpan", "CanvasLeft", "CanvasTop"
        };
        var container = common.Concat(new[]
        {
            "Type", "Padding", "Background", "Color", "BorderColor", "BorderWidth", "BorderBrush", "BorderThickness",
            "CornerRadius", "Orientation", "HorizontalScrolling", "VerticalScrolling", "ColumnDefinitions", "RowDefinitions"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var text = common.Concat(new[]
        {
            "Text", "Foreground", "FontFamily", "FontSize", "FontWeight", "FontStyle", "TextDecoration",
            "TextWrapping", "TextAlignment", "VerticalTextAlignment"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var input = common.Concat(new[]
        {
            "Type", "Text", "Value", "Checked", "IsChecked", "PlaceholderText", "AcceptsReturn", "Foreground",
            "Background", "FontFamily", "FontSize", "FontWeight", "FontStyle", "TextWrapping"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var content = common.Concat(new[]
        {
            "Content", "Text", "Foreground", "Background", "FontFamily", "FontSize", "FontWeight", "FontStyle"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var legacy = BuiltInPropertyNames().ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        var entries = new (string Name, bool Specialized, string[] Aliases)[]
        {
            ("Page", false, []), ("Container", false, []), ("Text", false, []), ("Image", false, []),
            ("Audio", true, []), ("Video", true, []), ("Object", true, []), ("Button", false, []),
            ("Input", false, []), ("Layer", false, []), ("Anchor", false, []),
            ("Panel", false, []), ("StackPanel", false, []), ("Grid", false, []), ("DockPanel", false, []),
            ("WrapPanel", false, []), ("Border", false, []), ("ScrollViewer", false, []), ("TextBlock", false, []),
            ("TextBox", false, []), ("CheckBox", false, []), ("RadioButton", false, []), ("Slider", false, []),
            ("ProgressBar", false, []), ("Canvas", false, []), ("TabControl", false, []), ("TabItem", false, []),
            ("ListBox", false, []), ("ComboBox", false, []), ("TreeView", false, []), ("Menu", false, []),
            ("MenuItem", false, []), ("Separator", false, []), ("ContentControl", false, []),
            ("ItemsControl", false, []), ("UserControl", false, [])
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
                "Anchor" => common.Concat(new[] { "Target", "Placement", "Left", "Top", "Width", "Height" }).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                "Audio" or "Video" or "Object" => common.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                _ => legacy,
            };
            result[name] = new CuiRuntimeElementDescriptor(
                name, aliases.ToFrozenSet(StringComparer.Ordinal), properties, specialized);
        }
        return result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> BuiltInPropertyNames() =>
    [
        "Type", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding",
        "Background", "Foreground", "Color", "BorderColor", "BorderWidth", "CornerRadius", "Opacity", "Active",
        "Hidden", "IsVisible", "IsEnabled", "Focusable", "TabIndex", "AccessibleName", "AccessibleDescription",
        "Role", "Text", "Content", "Orientation", "HorizontalScrolling", "VerticalScrolling",
        "Rotate", "Scale", "ScaleX", "ScaleY", "Skew", "SkewX", "SkewY", "Translate", "TranslateX", "TranslateY",
        "Effect", "Shadow", "Clip", "Cursor", "TextAlignment", "VerticalTextAlignment", "IsHitTestVisible",
        "BorderBrush", "BorderThickness", "Value", "Checked", "IsChecked", "ColumnDefinitions", "RowDefinitions",
        "GridColumn", "GridRow", "ColumnSpan", "RowSpan", "FontFamily", "FontSize", "FontWeight", "FontStyle",
        "TextDecoration", "TextWrapping", "AcceptsReturn", "PlaceholderText", "CanvasLeft", "CanvasTop", "AutomationId",
        "HorizontalAlignment", "VerticalAlignment", "Title", "Source", "Stretch", "ZIndex", "Interactive",
        "ActiveIndependently", "Target", "Placement", "Left", "Top", "Action", "Layer", "Modal"
    ];

    private static FrozenDictionary<string, CuiRuntimePropertyDescriptor> CreatePropertyDescriptors()
    {
        var entries = new (string Name, string LanguageType, Type RuntimeType, bool Writable)[]
        {
            ("Type", "symbol", typeof(string), false),
            ("Width", "length", typeof(double), true), ("Height", "length", typeof(double), true),
            ("MinWidth", "length", typeof(double), true), ("MinHeight", "length", typeof(double), true),
            ("MaxWidth", "length", typeof(double), true), ("MaxHeight", "length", typeof(double), true),
            ("Margin", "thickness", typeof(Thickness), true), ("Padding", "thickness", typeof(Thickness), true),
            ("Background", "brush", typeof(Avalonia.Media.IBrush), true),
            ("Foreground", "brush", typeof(Avalonia.Media.IBrush), true), ("Color", "color", typeof(Avalonia.Media.Color), true),
            ("BorderBrush", "brush", typeof(Avalonia.Media.IBrush), true),
            ("BorderThickness", "thickness", typeof(Thickness), true), ("BorderColor", "color", typeof(Avalonia.Media.Color), true),
            ("BorderWidth", "length", typeof(double), true), ("CornerRadius", "length", typeof(CornerRadius), true),
            ("Opacity", "number", typeof(double), true), ("Active", "boolean", typeof(bool), true),
            ("Hidden", "boolean", typeof(bool), true), ("IsVisible", "boolean", typeof(bool), true),
            ("IsEnabled", "boolean", typeof(bool), true), ("Focusable", "boolean", typeof(bool), true),
            ("TabIndex", "integer", typeof(int), true), ("AccessibleName", "text", typeof(string), true),
            ("AccessibleDescription", "text", typeof(string), true), ("Role", "symbol", typeof(string), true),
            ("Text", "text", typeof(string), true), ("Content", "content", typeof(object), true),
            ("Value", "value", typeof(object), true), ("Checked", "boolean", typeof(bool), true),
            ("Orientation", "symbol", typeof(Orientation), true),
            ("ColumnDefinitions", "grid-definitions", typeof(Avalonia.Controls.ColumnDefinitions), true),
            ("RowDefinitions", "grid-definitions", typeof(Avalonia.Controls.RowDefinitions), true),
            ("GridColumn", "integer", typeof(int), true), ("GridRow", "integer", typeof(int), true),
            ("ColumnSpan", "integer", typeof(int), true), ("RowSpan", "integer", typeof(int), true),
            ("FontFamily", "font-family", typeof(Avalonia.Media.FontFamily), true), ("FontSize", "length", typeof(double), true),
            ("FontWeight", "font-weight", typeof(Avalonia.Media.FontWeight), true),
            ("FontStyle", "font-style", typeof(Avalonia.Media.FontStyle), true),
            ("TextDecoration", "text-decoration", typeof(Avalonia.Media.TextDecorationCollection), true),
            ("TextWrapping", "symbol", typeof(Avalonia.Media.TextWrapping), true),
            ("AcceptsReturn", "boolean", typeof(bool), true), ("PlaceholderText", "text", typeof(string), true),
            ("CanvasLeft", "length", typeof(double), true), ("CanvasTop", "length", typeof(double), true),
            ("AutomationId", "text", typeof(string), true),
            ("HorizontalAlignment", "symbol", typeof(HorizontalAlignment), true),
            ("VerticalAlignment", "symbol", typeof(VerticalAlignment), true), ("Title", "text", typeof(string), true),
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
                IsAttached: entry.Name.StartsWith("Grid", StringComparison.Ordinal)
                    || entry.Name.StartsWith("Canvas", StringComparison.Ordinal),
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
            "Type" => new[] { "Grid", "Vertical Stack", "Horizontal Stack", "Absolute", "Text", "Number", "Checkbox", "Slider" },
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
            ("grid-columndefinitions", "ColumnDefinitions"), ("grid.rowdefinitions", "RowDefinitions"),
            ("columns", "ColumnDefinitions"), ("rows", "RowDefinitions"), ("grid.column", "GridColumn"),
            ("column", "GridColumn"), ("grid.row", "GridRow"), ("row", "GridRow"),
            ("grid.columnspan", "ColumnSpan"), ("columnspan", "ColumnSpan"),
            ("grid.rowspan", "RowSpan"), ("rowspan", "RowSpan"), ("textdecoration", "TextDecoration"),
            ("textdecorations", "TextDecoration"), ("left", "CanvasLeft"), ("top", "CanvasTop"),
            ("automation-id", "AutomationId"), ("ischecked", "IsChecked"),
            ("min-width", "MinWidth"), ("min-height", "MinHeight"), ("max-width", "MaxWidth"),
            ("max-height", "MaxHeight"), ("font-size", "FontSize"), ("font-family", "FontFamily"),
            ("font-weight", "FontWeight"), ("font-style", "FontStyle"),
            ("horizontal-alignment", "HorizontalAlignment"), ("vertical-alignment", "VerticalAlignment"),
            ("placeholder-text", "PlaceholderText"), ("canvas.left", "CanvasLeft"), ("canvas.top", "CanvasTop"),
            ("rotation", "Rotate"), ("rotate", "Rotate"), ("scale-x", "ScaleX"), ("scale-y", "ScaleY"),
            ("skew-x", "SkewX"), ("skew-y", "SkewY"), ("translate-x", "TranslateX"), ("translate-y", "TranslateY"),
            ("horizontal-scrolling", "HorizontalScrolling"), ("vertical-scrolling", "VerticalScrolling"),
            ("text-alignment", "TextAlignment"), ("vertical-text-alignment", "VerticalTextAlignment"),
            ("is-hit-test-visible", "IsHitTestVisible")
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
