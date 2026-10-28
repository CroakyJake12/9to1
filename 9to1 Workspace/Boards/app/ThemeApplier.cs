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

    public static void ApplyChrome(Control root, bool applyResources = true)
    {
        ArgumentNullException.ThrowIfNull(root);
        var palette = applyResources ? BoardsTheme.StandalonePalette : BoardsTheme.SharedPalette;
        // Only this surface owns these resources. A borrowed Home-aware loader
        // already applies its exact theme/custom/accessibility resources locally.
        if (applyResources)
        {
            var scoped = new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(scoped, palette);
            foreach (var entry in scoped) root.Resources[entry.Key] = entry.Value;
        }
        using var themeScope = BoardsTheme.UseSurface(root);
        var interfaceFont = root.TryFindResource("CuiInterfaceFontFamily", out var font) && font is FontFamily family
            ? family : ProductFont;

        Paint<Border>(root, "FooterBar", b =>
        {
            b.Background = BoardsTheme.SurfaceBrush;
            b.BorderBrush = BoardsTheme.BorderBrush;
            b.BorderThickness = new Avalonia.Thickness(0, 1, 0, 0);
        });
        Paint<StackPanel>(root, "ContextHost", c => c.Background = BoardsTheme.SurfaceBrush);
        Paint<TextBox>(root, "BoardTitleBox", box =>
        {
            SetDocumentTextSurface(box, Brushes.Transparent);
            box.Foreground = BoardsTheme.TextBrush;
            box.FontFamily = interfaceFont;
        });
        Paint<TextBox>(root, "PageTitleBox", box =>
        {
            SetDocumentTextSurface(box, Brushes.Transparent);
            box.Foreground = BoardsTheme.TextBrush;
            box.FontFamily = interfaceFont;
        });
        Paint<TextBox>(root, "NavSearchBox", box => box.FontFamily = interfaceFont);
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
            var dropdownBrush = combo.TryFindResource("CuiAccentSoftBrush", out var accentSoft) && accentSoft is IBrush brush
                ? brush
                : BoardsTheme.CardBrush;
            CuiPropertyPresentation.Set(combo, "Background", dropdownBrush, CuiPropertyLevel.Instance,
                "Boards toolbar dropdown trigger");
            CuiPropertyPresentation.Set(combo, "Foreground", BoardsTheme.TextBrush, CuiPropertyLevel.Instance,
                "Boards toolbar surface");
            CuiPropertyPresentation.Set(combo, "BorderWidth", new Avalonia.Thickness(0), CuiPropertyLevel.Instance,
                "Boards toolbar surface");
            CuiPropertyPresentation.Set(combo, "CornerRadius", new Avalonia.CornerRadius(10), CuiPropertyLevel.Instance,
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

