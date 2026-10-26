/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserTabHoverContractTests.cs, in the automated test suite.
 * What: Protects the browser tab row's shared hover surface and its independent close target.
 * Why: The row's title and close button must not paint separate opaque backgrounds on hover.
 */

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.Views;

namespace Haven.Desktop.Tests;

public sealed class BrowserTabHoverContractTests
{
    [Fact]
    public void HorizontalAndVerticalBrowserTabsUseOneSharedHoverSurface()
    {
        var document = XDocument.Load(SourcePath("Views", "BrowserView.axaml"));
        var groups = document.Descendants()
            .Where(element => element.Name.LocalName == "HavenAdaptiveSurface" &&
                              HasClass((string?)element.Attribute("Classes"), "browserTabGroup"))
            .ToArray();

        Assert.Equal(2, groups.Length);
        foreach (var group in groups)
        {
            var parts = group.Descendants()
                .Where(element => element.Name.LocalName == "HavenButton" &&
                                  HasClass((string?)element.Attribute("Classes"), "browserTabPart"))
                .ToArray();
            Assert.Equal(2, parts.Length);
            Assert.Single(parts, element => HasClass((string?)element.Attribute("Classes"), "browserTabClose"));
            var tint = group.Descendants().Single(element => element.Name.LocalName == "Border" &&
                HasClass((string?)element.Attribute("Classes"), "browserTabHoverTint"));
            Assert.Equal("False", (string?)tint.Attribute("IsHitTestVisible"));
            Assert.Null(tint.Attribute("Opacity"));
            Assert.Equal("{DynamicResource HavenAccentTertiaryBrush}", (string?)tint.Attribute("Background"));
            Assert.True(tint.Parent!.Elements().ToList().IndexOf(tint) <
                        tint.Parent.Elements().ToList().IndexOf(parts[0]));

            // Keep the selected indicator attached to the shared row.
            Assert.Contains(group.Descendants().Where(element => element.Name.LocalName == "HavenAdaptiveSurface"),
                element => (string?)element.Attribute("IsVisible") == "{Binding IsSelected}");
        }
    }

    [Fact]
    public void TypedHavenHoverStylesKeepTheEffectiveBackgroundAndProvideGlow()
    {
        var document = XDocument.Load(SourcePath("Styles", "DefaultTheme.axaml"));
        var styles = document.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToArray();
        var hoverStyles = styles
            .Where(element => ((string?)element.Attribute("Selector"))?.Contains(":pointerover", StringComparison.Ordinal) == true)
            .Where(style => IsTypedHavenSelector((string?)style.Attribute("Selector")))
            .ToArray();

        Assert.NotEmpty(hoverStyles);
        foreach (var style in hoverStyles)
        {
            var backgroundSetters = style.Elements()
                .Where(element => element.Name.LocalName == "Setter" &&
                                  (string?)element.Attribute("Property") == "Background")
                .ToArray();
            Assert.All(backgroundSetters, setter => Assert.Equal("Transparent", (string?)setter.Attribute("Value")));
        }

        var groupHover = hoverStyles.Single(style =>
            (string?)style.Attribute("Selector") == "hui|HavenAdaptiveSurface.browserTabGroup:pointerover");
        Assert.Contains(groupHover.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Effect" &&
            ((string?)setter.Attribute("Value"))?.Contains("CuiAccentTertiaryHoverEffect", StringComparison.Ordinal) == true);

        var overlayHover = hoverStyles.Single(style =>
            (string?)style.Attribute("Selector") == "hui|HavenAdaptiveSurface.browserTabGroup:pointerover Border.browserTabHoverTint");
        Assert.Contains(overlayHover.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Opacity" &&
            (string?)setter.Attribute("Value") == "0.08");
        var overlayRest = styles.Single(style =>
            (string?)style.Attribute("Selector") == "hui|HavenAdaptiveSurface.browserTabGroup Border.browserTabHoverTint");
        Assert.Contains(overlayRest.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Opacity" &&
            (string?)setter.Attribute("Value") == "0");

        var sharedParts = styles.Single(style =>
            (string?)style.Attribute("Selector") == "hui|HavenAdaptiveSurface.browserTabGroup hui|HavenButton.browserTabPart");
        Assert.Contains(sharedParts.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "Transparent");

        var closeHover = hoverStyles.Single(style =>
            (string?)style.Attribute("Selector") == "hui|HavenAdaptiveSurface.browserTabGroup hui|HavenButton.browserTabClose:pointerover");
        Assert.DoesNotContain(closeHover.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Background");
        Assert.Contains(closeHover.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Effect");
        var tertiaryHover = hoverStyles.Single(style =>
            (string?)style.Attribute("Selector") == "hui|HavenTertiaryButton:pointerover");
        Assert.DoesNotContain(tertiaryHover.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Foreground");
        var dropdownHover = hoverStyles.Single(style =>
            (string?)style.Attribute("Selector") == ":is(Button).havenDropdownItem:pointerover");
        Assert.DoesNotContain(dropdownHover.Elements(), setter =>
            (string?)setter.Attribute("Property") == "Foreground");
    }

    [AvaloniaFact]
    public void Mounted_browser_tab_row_tints_as_one_surface_and_keeps_close_target_separate()
    {
        var view = new BrowserView();
        var list = view.GetVisualDescendants().OfType<ItemsControl>()
            .Single(control => control.Name == "HorizontalTabsList");
        list.ItemsSource = new[] { new BrowserTabItem("One", "", false, true) };
        var window = new Window { Width = 500, Height = 120, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var row = view.GetVisualDescendants().OfType<HavenAdaptiveSurface>()
                .Single(control => control.Classes.Contains("browserTabGroup"));
            var tint = row.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Classes.Contains("browserTabHoverTint"));
            var parts = row.GetVisualDescendants().OfType<HavenButton>()
                .Where(control => control.Classes.Contains("browserTabPart")).ToArray();
            Assert.Equal(2, parts.Length);
            var close = Assert.Single(parts, control => control.Classes.Contains("browserTabClose"));
            var title = Assert.Single(parts, control => !control.Classes.Contains("browserTabClose"));
            var baseBackground = row.Background;
            var selectedIndicator = row.GetVisualDescendants().OfType<HavenAdaptiveSurface>().Single();

            MovePointer(window, title);
            Assert.True(row.IsPointerOver);
            Assert.True(title.IsPointerOver);
            Assert.False(close.IsPointerOver);
            Assert.Same(baseBackground, row.Background);
            Assert.Equal(0.08, tint.Opacity, precision: 3);
            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(title.Background).Color);
            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(close.Background).Color);
            Assert.True(selectedIndicator.IsVisible);

            MovePointer(window, close);
            Assert.True(row.IsPointerOver);
            Assert.True(close.IsPointerOver);
            Assert.True(close.IsHitTestVisible);
            Assert.True(close.Focusable);
            Assert.Same(baseBackground, row.Background);
            Assert.Equal(0.08, tint.Opacity, precision: 3);
            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(close.Background).Color);
            Assert.True(selectedIndicator.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static void MovePointer(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseMove(point.Value);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed record BrowserTabItem(string Title, string Group, bool IsPrivate, bool IsSelected);

    private static bool IsTypedHavenSelector(string? selector) => selector is not null &&
        (selector.Contains("hui|HavenButton", StringComparison.Ordinal) ||
         selector.Contains("hui|HavenPrimaryButton", StringComparison.Ordinal) ||
         selector.Contains("hui|HavenSecondaryButton", StringComparison.Ordinal) ||
         selector.Contains("hui|HavenTertiaryButton", StringComparison.Ordinal) ||
         selector.Contains("hui|HavenTextInput", StringComparison.Ordinal) ||
         selector.Contains("hui|HavenSelect", StringComparison.Ordinal) ||
         selector.Contains("hui|HavenCard", StringComparison.Ordinal) ||
         selector.Contains("hui|HavenAdaptiveSurface.browserTabGroup", StringComparison.Ordinal) ||
         selector.Contains(".havenIcon", StringComparison.Ordinal) ||
         selector.Contains(".havenNavigation", StringComparison.Ordinal) ||
         selector.Contains(".havenSuggestion", StringComparison.Ordinal) ||
         selector.Contains(".havenMobileAction", StringComparison.Ordinal) ||
         selector.Contains(".havenDropdownItem", StringComparison.Ordinal));

    private static bool HasClass(string? classes, string expected) =>
        classes?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(expected, StringComparer.Ordinal) == true;

    private static string SourcePath(string subtree, string name, [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "src", "Haven.Desktop", subtree, name));
}
