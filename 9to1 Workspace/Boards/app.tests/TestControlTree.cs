using Avalonia.Controls;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// Walks the control tree the Boards surfaces actually build. The headless test host does not apply
/// templates or realise visual children on demand, so a visual-tree walk finds nothing inside a Button;
/// this follows the Panel / Decorator / ContentControl shape the nav builder and the CUI loader write.
/// </summary>
internal static class TestControlTree
{
    public static IEnumerable<Control> Descendants(Control root)
    {
        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    yield return child;
                    foreach (var nested in Descendants(child)) yield return nested;
                }
                break;
            case Decorator { Child: Control decorated }:
                yield return decorated;
                foreach (var nested in Descendants(decorated)) yield return nested;
                break;
            case ContentControl { Content: Control content }:
                yield return content;
                foreach (var nested in Descendants(content)) yield return nested;
                break;
        }
    }

    public static IEnumerable<T> Of<T>(Control root) where T : Control => Descendants(root).OfType<T>();
}
