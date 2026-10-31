using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// CUI's own marker for "this authored control is the overflow surface of its container".
///
/// The authored route is <c>OverflowHost="true"</c> on a child of a <c>Container Type="Toolbar"</c>: the
/// loader records it here and the toolbar layout consults it when deciding what to collapse and what to
/// present. It is deliberately framework-owned rather than an Avalonia attached property or a donor marker,
/// so nothing outside CUI can set it, and it survives re-parenting: an item moved from a collapsed toolbar
/// into its overflow menu keeps its identity and authored state.
/// </summary>
public static class CuiOverflowHosts
{
    private sealed class Marker
    {
        public bool Value;
    }

    private static readonly ConditionalWeakTable<Control, Marker> Marks = new();

    public static void Set(Control control, bool isOverflowHost)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (isOverflowHost)
            Marks.GetValue(control, static _ => new Marker()).Value = true;
        else
            Marks.Remove(control);
    }

    /// <summary>True when this authored control was declared as its container's overflow surface.</summary>
    public static bool IsHost(Control? control) =>
        control is not null && Marks.TryGetValue(control, out var marker) && marker.Value;
}
