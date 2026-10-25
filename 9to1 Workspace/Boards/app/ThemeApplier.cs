// ThemeApplier applies the shared CUI Boards palette, then paints static
// .cui chrome from those semantic roles. Code-built controls read the same
// resolved palette; rebuilding them applies an appearance change.

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace CakeOS.Apps.Boards.App;

public static class ThemeApplier
{
    private static readonly FontFamily ProductFont = new("Inter");

    public static void ApplyChrome(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var palette = BoardsTheme.SharedPalette;
        CuiThemeResourceApplier.Apply(palette);

        Paint<Border>(root, "TopBar", b =>
        {
            b.Background = BoardsTheme.SurfaceBrush;
            b.BorderBrush = BoardsTheme.AccentBrush;
            b.BorderThickness = new Avalonia.Thickness(0, 0, 0, 1);
        });
        Paint<Border>(root, "NavPane", b =>
        {
            b.Background = BoardsTheme.SurfaceBrush;
            b.BorderBrush = BoardsTheme.BorderBrush;
            b.BorderThickness = new Avalonia.Thickness(0, 0, 1, 0);
        });
        Paint<Border>(root, "FooterBar", b =>
        {
            b.Background = BoardsTheme.SurfaceBrush;
            b.BorderBrush = BoardsTheme.BorderBrush;
            b.BorderThickness = new Avalonia.Thickness(0, 1, 0, 0);
        });
        Paint<ScrollViewer>(root, "EditorScroll", c => c.Background = BoardsTheme.AppBackgroundBrush);
        Paint<Border>(root, "PageCard", c =>
        {
            c.Background = BoardsTheme.PageBackgroundBrush;
            c.BorderBrush = Brushes.Transparent;
            c.BorderThickness = new Avalonia.Thickness(0);
            c.CornerRadius = new Avalonia.CornerRadius(0);
            c.Padding = new Avalonia.Thickness(0);
            c.Margin = new Avalonia.Thickness(0);
        });
        Paint<StackPanel>(root, "DocumentContentLayer", c => c.Margin = new Avalonia.Thickness(40, 32));
        Paint<StackPanel>(root, "ToolbarHost", c => c.Background = BoardsTheme.SurfaceBrush);
        Paint<StackPanel>(root, "ContextHost", c => c.Background = BoardsTheme.SurfaceBrush);
        Paint<TextBox>(root, "BoardTitleBox", box =>
        {
            SetDocumentTextSurface(box, Brushes.Transparent);
            box.Foreground = BoardsTheme.TextBrush;
            box.FontFamily = ProductFont;
        });
        Paint<TextBox>(root, "PageTitleBox", box =>
        {
            SetDocumentTextSurface(box, Brushes.Transparent);
            box.Foreground = BoardsTheme.TextBrush;
            box.FontFamily = ProductFont;
        });
        Paint<TextBox>(root, "NavSearchBox", box =>
        {
            box.Background = BoardsTheme.CardBrush;
            box.Foreground = BoardsTheme.TextBrush;
            box.FontFamily = ProductFont;
        });
    }

    public static void SetDocumentTextSurface(TextBox input, IBrush background)
    {
        CuiPropertyPresentation.Set(input, "Foreground", input.Foreground ?? BoardsTheme.TextBrush,
            CuiPropertyLevel.Instance, "Boards document text");
        CuiPropertyPresentation.Set(input, "Background", background, CuiPropertyLevel.Instance,
            "Boards document surface");
        CuiPropertyPresentation.Set(input, "BorderWidth", new Avalonia.Thickness(0), CuiPropertyLevel.Instance,
            "Boards document surface");
        CuiPropertyPresentation.Set(input, "CornerRadius", new Avalonia.CornerRadius(0), CuiPropertyLevel.Instance,
            "Boards document surface");
    }

    public static void ApplyToolbarInputSurface(Control root)
    {
        foreach (var combo in root.GetVisualDescendants().OfType<ComboBox>())
        {
            CuiPropertyPresentation.Set(combo, "Background", BoardsTheme.CardBrush, CuiPropertyLevel.Instance,
                "Boards toolbar surface");
            CuiPropertyPresentation.Set(combo, "Foreground", BoardsTheme.TextBrush, CuiPropertyLevel.Instance,
                "Boards toolbar surface");
            CuiPropertyPresentation.Set(combo, "BorderWidth", new Avalonia.Thickness(0), CuiPropertyLevel.Instance,
                "Boards toolbar surface");
        }
    }

    private static void Paint<T>(Control root, string automationId, Action<T> paint) where T : Control
    {
        var control = BoardsCuiControlFinder.FindByAutomationId<T>(root, automationId);
        if (control is not null)
            paint(control);
    }

}

