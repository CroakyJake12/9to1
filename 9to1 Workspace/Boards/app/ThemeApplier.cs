// ThemeApplier applies the shared CUI Boards/Glow palette, then paints static
// .cui chrome from those semantic roles. Code-built controls read the same
// resolved palette; rebuilding them applies an appearance change.

using Avalonia.Controls;
using Avalonia.Media;
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
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Avalonia.Thickness(0);
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

    private static void Paint<T>(Control root, string automationId, Action<T> paint) where T : Control
    {
        var control = FindByAutomationId<T>(root, automationId);
        if (control is not null)
            paint(control);
    }

    private static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
    {
        if (root is T match && string.Equals(
                Avalonia.Automation.AutomationProperties.GetAutomationId(root), automationId, StringComparison.Ordinal))
            return match;
        foreach (var child in LogicalChildren(root))
        {
            var found = FindByAutomationId<T>(child, automationId);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static IEnumerable<Control> LogicalChildren(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c)
                    yield return c;
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            yield return decChild;
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            yield return ccChild;
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild)
                    yield return icChild;
        }
        else if (control is ScrollViewer scroller && scroller.Content is Control scChild)
        {
            yield return scChild;
        }
    }
}

