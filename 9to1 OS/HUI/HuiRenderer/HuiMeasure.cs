using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Haven.UI;
using Haven.UI.Components;

namespace CakeOS.Hui.Renderer;

/// <summary>
/// Shared leaf measurement for HUI layout. Backends delegate here so every
/// host measures text, buttons and images identically.
/// </summary>
public static class HuiMeasure
{
    public static FormattedText Format(HavenTextLayout layout, IBrush brush)
    {
        var maxWidth = double.IsFinite(layout.MaxWidth) ? Math.Max(1d, layout.MaxWidth) : 10000d;
        return new FormattedText(layout.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, layout.Italic ? FontStyle.Italic : FontStyle.Normal, Weight(layout.FontWeight), FontStretch.Normal),
            layout.FontSize <= 0 ? 14d : layout.FontSize, brush)
        { MaxTextWidth = maxWidth };
    }

    public static HavenSize Leaf(HavenElement element, HavenSize available, CakeTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return element switch
        {
            Text text => MeasureText(text, available),
            Button button => new HavenSize(
                Math.Min(available.Width, Math.Max(40, button.Content.Length * 9 + 40)),
                Math.Min(available.Height, 40)),
            Image => new HavenSize(available.Width, available.Height),
            _ => new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48)),
        };
    }

    private static HavenSize MeasureText(Text text, HavenSize available)
    {
        var fontSize = text.GetValue(HavenProperties.FontSize);
        if (fontSize <= 0) fontSize = 14d;
        var maxWidth = double.IsFinite(available.Width) ? Math.Max(1d, available.Width) : 10000d;
        var formatted = Format(
            new HavenTextLayout(
                text.Content,
                text.GetValue(HavenProperties.FontFamily),
                fontSize,
                text.GetValue(HavenProperties.FontWeight),
                maxWidth),
            Brushes.Transparent);
        return new HavenSize(
            Math.Min(available.Width, formatted.Width + 2d),
            Math.Min(available.Height, formatted.Height + 2d));
    }

    private static FontWeight Weight(int weight) => weight switch
    {
        >= 800 => FontWeight.ExtraBold,
        >= 700 => FontWeight.Bold,
        >= 600 => FontWeight.SemiBold,
        >= 500 => FontWeight.Medium,
        _ => FontWeight.Normal,
    };
}
