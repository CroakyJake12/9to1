using Avalonia;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>Converts authored interface measurements and responsive viewport widths using the active theme scale.</summary>
public static class CuiInterfaceScale
{
    public const string ResourceKey = "CuiDisplayScale";
    internal const string AuthoredSource = "CUI authored interface metric ";

    private static readonly HashSet<string> Metrics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Width", "MinWidth", "MaxWidth", "Height", "MinHeight", "MaxHeight", "FontSize", "IconSize",
        "Padding", "Margin", "BorderWidth", "CornerRadius", "CellGap", "CellMargin", "RowSizes", "ColumnSizes"
    };

    public static double For(Control control) =>
        control.TryFindResource(ResourceKey, out var value) ? ValidScale(value) : 1;

    public static double LogicalWidth(double width, double scale) => width / ValidScale(scale);

    internal static double ValidScale(object? value) => value is double scale && scale is >= .5 and <= 5 ? scale : 1;

    internal static string Source(string property, CuiPropertyLevel level, bool enabled) =>
        enabled && Metrics.Contains(property.Replace("-", string.Empty, StringComparison.Ordinal))
            ? AuthoredSource + level : "CUI authored " + level;

    internal static bool IsAuthored(string source) => source.StartsWith(AuthoredSource, StringComparison.Ordinal);

    internal static object? Apply(object? value, double scale)
    {
        if (scale == 1) return value;
        return value switch
        {
            double number => number * scale,
            Thickness inset => new Thickness(inset.Left * scale, inset.Top * scale, inset.Right * scale, inset.Bottom * scale),
            CornerRadius radius => new CornerRadius(radius.TopLeft * scale, radius.TopRight * scale,
                radius.BottomRight * scale, radius.BottomLeft * scale),
            RowDefinitions rows when rows.Any(row => row.Height.IsAbsolute) => ScaleRows(rows, scale),
            ColumnDefinitions columns when columns.Any(column => column.Width.IsAbsolute) => ScaleColumns(columns, scale),
            _ => value
        };
    }

    private static RowDefinitions ScaleRows(RowDefinitions rows, double scale)
    {
        var result = new RowDefinitions();
        foreach (var row in rows)
            result.Add(new(row.Height.IsAbsolute ? new GridLength(row.Height.Value * scale) : row.Height));
        return result;
    }

    private static ColumnDefinitions ScaleColumns(ColumnDefinitions columns, double scale)
    {
        var result = new ColumnDefinitions();
        foreach (var column in columns)
            result.Add(new(column.Width.IsAbsolute ? new GridLength(column.Width.Value * scale) : column.Width));
        return result;
    }
}
