/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserTabHoverContractTests.cs, in the automated test suite.
 * What: Protects the browser tab row's shared hover surface and its independent close target.
 * Why: The row's title and close button must not paint separate opaque backgrounds on hover.
 */

using System.Runtime.CompilerServices;
using System.Xml.Linq;

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
            Assert.Single(parts.Where(element => HasClass((string?)element.Attribute("Classes"), "browserTabClose")));

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
            (string?)setter.Attribute("Property") == "Foreground");
    }

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
