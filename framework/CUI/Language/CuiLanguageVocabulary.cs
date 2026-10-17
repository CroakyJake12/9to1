using System.Collections.Frozen;

namespace CakeOS.Cui.Language;

/// <summary>The public authoring boundary, shared by parsing, compilation, loading and tools.</summary>
public static class CuiLanguageVocabulary
{
    public static IReadOnlySet<string> VisualElements { get; } = new[]
    { "Page", "Container", "Text", "Image", "Audio", "Video", "Object", "Button", "Input", "Layer", "Markdown" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> NonvisualElements { get; } = new[]
    { "Anchor", "Theme", "Style", "Property", "Variable", "Prefab", "ActionEvent", "If", "Else", "Repeat", "Import", "Keyframe" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DonorElements = new[]
    {
        "Panel", "Grid", "StackPanel", "WrapPanel", "DockPanel", "ScrollViewer", "TextBlock", "Border",
        "TextBox", "CheckBox", "RadioButton", "ToggleButton", "Slider", "ProgressBar", "Canvas", "TabControl",
        "TabItem", "ListBox", "ComboBox", "TreeView", "Menu", "ContextMenu", "MenuItem", "Separator",
        "ContentControl", "ItemsControl", "UserControl", "Window", "Application", "NumericUpDown"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsForbiddenElement(string name)
    {
        var leaf = name[(name.LastIndexOf('.') + 1)..];
        return DonorElements.Contains(leaf) || name.StartsWith("Avalonia.", StringComparison.OrdinalIgnoreCase)
            || name.Contains("clr-namespace", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsForbiddenProperty(string name)
    {
        var normalized = string.Concat(name.Where(character => character is not '-' and not '_' and not '.'));
        return normalized.Equals("ColumnDefinitions", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("RowDefinitions", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("GridRow", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("GridColumn", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("GridRowSpan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("GridColumnSpan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CanvasLeft", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CanvasTop", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Avalonia.", StringComparison.OrdinalIgnoreCase);
    }

    public static string ElementCorrection(string name) => name.ToLowerInvariant() switch
    {
        "textblock" => "Use Text.",
        "grid" => "Use Container Type=\"Grid\" with RowSizes and ColumnSizes.",
        "stackpanel" => "Use Container Type=\"Vertical Stack\" or Type=\"Horizontal Stack\".",
        "scrollviewer" => "Use Container with independent HorizontalScrolling and VerticalScrolling properties.",
        "border" => "Use a Container with Border, BorderColor, BorderWidth and CornerRadius properties.",
        _ => "Use a canonical CUI element and a registered Type or legitimate Definition."
    };
}
