using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Tests;

/// <summary>
/// Enumerates the same retained controls, including authored sections removed from
/// native layout while hidden. Visibility and viewport assertions still inspect
/// those controls directly; this does not create a second presentation or state.
/// </summary>
internal static class AuthoredCuiControls
{
    internal static IEnumerable<Control> Enumerate(Control root)
    {
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Control>();
        pending.Push(root);
        while (pending.TryPop(out var control))
        {
            if (!seen.Add(control)) continue;
            yield return control;
            foreach (var child in control.GetLogicalChildren().OfType<Control>()) pending.Push(child);
            foreach (var child in control.GetVisualChildren().OfType<Control>()) pending.Push(child);
            if (control is CuiContainerControl container)
                foreach (var child in container.AuthoredChildren) pending.Push(child);
        }
    }
}
