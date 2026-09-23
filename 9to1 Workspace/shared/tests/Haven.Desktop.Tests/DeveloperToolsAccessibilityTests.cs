/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/DeveloperToolsAccessibilityTests.cs, the built-in CUI inspector regression suite.
 * What: Verifies that selected-element properties expose configured accessibility metadata.
 * Why: The developer workspace needs to reveal accessible names and automation settings during UI inspection.
 */

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.DeveloperTools;

namespace Haven.Desktop.Tests;

public sealed class DeveloperToolsAccessibilityTests
{
    [AvaloniaFact]
    public void Properties_include_accessibility_metadata_for_the_selected_element()
    {
        var button = new Button { Content = "Save" };
        AutomationProperties.SetName(button, "Save document");
        AutomationProperties.SetAutomationId(button, "Document.Save");
        AutomationProperties.SetHelpText(button, "Writes the current document to disk.");
        AutomationProperties.SetAccessKey(button, "S");
        AutomationProperties.SetAcceleratorKey(button, "Ctrl+S");
        AutomationProperties.SetControlTypeOverride(button, AutomationControlType.Button);
        AutomationProperties.SetAccessibilityView(button, AccessibilityView.Content);

        var properties = DeveloperElementFormatter.GetProperties(button)
            .ToDictionary(property => property.Name, property => property.Value);

        Assert.Equal("Save document", properties["Accessible name override"]);
        Assert.Equal("Document.Save", properties["Automation ID"]);
        Assert.Equal("Writes the current document to disk.", properties["Accessibility help text"]);
        Assert.Equal("S", properties["Access key"]);
        Assert.Equal("Ctrl+S", properties["Accelerator key"]);
        Assert.Equal(nameof(AutomationControlType.Button), properties["Control type override"]);
        Assert.Equal(nameof(AccessibilityView.Content), properties["Accessibility view"]);
    }
}
