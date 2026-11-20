/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/SharedControlStateParityContractTests.cs, in the automated test suite.
 * What: Pins the CUI-NEW-002 interactive-state parity of the shared haven* control classes in DefaultTheme.
 * Why: Focus/hover/selected/disabled/destructive treatments are a shared contract (CUI-002, THEME-001/002);
 * a class quietly losing one of them used to be visible only on a screen nobody re-checks.
 */

using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Haven.Desktop.Tests;

public sealed class SharedControlStateParityContractTests
{
    private static readonly string[] InteractiveClasses =
    [
        "havenButton", "havenTab", "havenNavigation", "havenSuggestion", "havenHeaderPill",
        "havenHeaderIcon", "havenMobileAction", "havenDropdownItem", "havenChip", "havenModelPicker"
    ];

    private static readonly string[] SelectableClasses = ["havenTab", "havenNavigation", "havenDropdownItem", "HavenTabItem"];

    private static IEnumerable<string> Selectors() =>
        XDocument.Load(SourcePath("Styles", "DefaultTheme.axaml"))
            .Descendants().Where(e => e.Name.LocalName == "Style")
            .Select(e => (string?)e.Attribute("Selector") ?? string.Empty);

    [Fact]
    public void EverySharedInteractiveClassDefinesAHoverTreatment()
    {
        var selectors = Selectors().ToArray();
        foreach (var control in InteractiveClasses)
        {
            Assert.True(selectors.Any(s => s.Contains(":pointerover", StringComparison.Ordinal) &&
                                           s.Contains(control, StringComparison.OrdinalIgnoreCase)),
                $"{control} has no :pointerover treatment (CUI-NEW-002 hover parity).");
        }
    }

    [Fact]
    public void EverySharedInteractiveClassHoverUsesASharedCuiEffectOrTransparentBackground()
    {
        var document = XDocument.Load(SourcePath("Styles", "DefaultTheme.axaml"));
        foreach (var style in document.Descendants()
                     .Where(e => e.Name.LocalName == "Style")
                     .Where(e => ((string?)e.Attribute("Selector"))?.Contains(":pointerover") == true)
                     .Where(e => InteractiveClasses.Any(c => ((string?)e.Attribute("Selector"))!
                         .Contains(c, StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                var property = (string?)setter.Attribute("Property");
                var value = (string?)setter.Attribute("Value");
                if (property == "Background")
                    Assert.Contains("Transparent", value, StringComparison.Ordinal);
                else if (property == "Effect")
                    Assert.Contains("CuiAccent", value, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DisabledAndKeyboardFocusHaveSharedButtonRepresentations()
    {
        var selectors = Selectors().ToArray();
        Assert.Contains(selectors, s => s == "Button:disabled");
        Assert.Contains(selectors, s => s.Contains("Button:focus-visible", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectableSharedControlsDefineASelectedTreatment()
    {
        var selectors = Selectors().ToArray();
        foreach (var control in SelectableClasses)
        {
            Assert.True(selectors.Any(s => (s.Contains(".selected", StringComparison.Ordinal) ||
                                            s.Contains(":selected", StringComparison.Ordinal)) &&
                                           s.Contains(control, StringComparison.OrdinalIgnoreCase)),
                $"{control} has no selected treatment (CUI-NEW-002 selected parity).");
        }
    }

    [Fact]
    public void DestructiveActionsHaveTheirOwnSharedAccentTreatment()
    {
        var selectors = Selectors().ToArray();
        Assert.Contains(selectors, s => s.Contains("HavenButton.danger", StringComparison.Ordinal));
        Assert.Contains(selectors, s => s.Contains(".negative", StringComparison.Ordinal));
    }

    private static string SourcePath(string subtree, string name, [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "src", "Haven.Desktop", subtree, name));
}
