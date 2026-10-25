using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>Resolves authored CUI names across the runtime's container wrapper and its
/// concrete Avalonia layout, so app, embedded, and test hosts address the same controls.</summary>
public static class BoardsCuiControlFinder
{
    public static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        if (TryMatch<T>(root, automationId, out var found)) return found;
        foreach (var child in root.GetVisualDescendants().OfType<Control>())
            if (TryMatch<T>(child, automationId, out found)) return found;
        return null;
    }

    /// <summary>Bounded runtime evidence for diagnosing authored ID or runtime-type drift.</summary>
    public static string DescribeRuntimeTree(Control root, int maximumEntries = 128)
    {
        ArgumentNullException.ThrowIfNull(root);
        maximumEntries = Math.Clamp(maximumEntries, 1, 256);
        return string.Join(Environment.NewLine, new[] { root }.Concat(root.GetVisualDescendants().OfType<Control>())
            .Take(maximumEntries)
            .Select(control =>
            {
                var id = AutomationProperties.GetAutomationId(control);
                var layout = control is CuiContainerControl container ? $" layout={container.Layout.GetType().Name}" : string.Empty;
                return $"{control.GetType().Name} name='{control.Name}' automationId='{id}'{layout}";
            }));
    }

    private static bool TryMatch<T>(Control control, string automationId, out T? result) where T : Control
    {
        result = null;
        if (!string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal) &&
            !string.Equals(control.Name, automationId, StringComparison.Ordinal))
            return false;
        if (control is T direct)
        {
            result = direct;
            return true;
        }
        if (control is CuiContainerControl container && container.Layout is T layout)
        {
            result = layout;
            return true;
        }
        return false;
    }
}
