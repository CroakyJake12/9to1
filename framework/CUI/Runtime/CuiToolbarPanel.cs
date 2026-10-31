using Avalonia;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// CUI's authored command-strip layout: a single horizontal row of items that FITS what it can and
/// collapses the rest into the container's declared overflow host, instead of silently clipping or
/// scrolling.
///
/// This is the framework construct the donor command surfaces need: the Stage A mirrors measured 7,401
/// donor toolbar constructs with overflow behaviour that CUI could only approximate as a horizontal
/// stack, which lost the behaviour entirely (LibreOffice <toolbox>, GTK GtkToolbar/overflow menu, XUL
/// toolbar + `toolbar-button-overflow`, Flutter OverflowBar).
///
/// Behaviour, not decoration:
///  * every authored child is measured, in document order;
///  * children that do not fit in the actual measured width are collapsed by HIDING THE CELL, never by
///    touching the authored control's own IsVisible, so authored visibility and bindings stay intact;
///  * the overflow host is the child the author marked <c>OverflowHost="true"</c>. It is shown only when
///    at least one item was actually collapsed, and hidden otherwise, so an "advanced" chevron does not
///    sit in a toolbar that has room for everything;
///  * <see cref="VisibleItemCount"/> and <see cref="HiddenItemCount"/> expose the real measured outcome so
///    surfaces and tests can assert it rather than infer it from positions.
/// </summary>
public sealed class CuiToolbarPanel : Panel
{
    public const string OverflowHostProperty = "OverflowHost";

    private double _spacing;

    public double Spacing
    {
        get => _spacing;
        set
        {
            if (Math.Abs(_spacing - value) < 0.0001) return;
            _spacing = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Items laid out in the available width at the last measurement.</summary>
    public int VisibleItemCount { get; private set; }

    /// <summary>Items the measured width could not hold, and which the overflow host therefore carries.</summary>
    public int HiddenItemCount { get; private set; }

    public IReadOnlyList<Control> HiddenItems => _hidden;
    private readonly List<Control> _hidden = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        _hidden.Clear();
        var constrained = !double.IsInfinity(availableSize.Width);
        var budget = constrained ? Math.Max(0, availableSize.Width) : double.PositiveInfinity;
        var childAvailable = new Size(double.PositiveInfinity, availableSize.Height);

        Control? host = null;
        var hostDesired = default(Size);
        var order = new List<(Control Child, Size Desired)>(Children.Count);
        foreach (var child in Children)
        {
            child.Measure(childAvailable);
            if (IsOverflowHost(child))
            {
                host = child;
                hostDesired = child.DesiredSize;
                continue;
            }
            order.Add((child, child.DesiredSize));
        }

        var gaps = order.Count > 1 ? _spacing * (order.Count - 1) : 0;
        var totalAll = order.Sum(item => item.Desired.Width) + gaps;

        // Everything fits: nothing collapses and the author's overflow control is not presented.
        var mustCollapse = constrained && totalAll > budget;
        var fitBudget = mustCollapse && host is not null
            ? Math.Max(0, budget - hostDesired.Width - _spacing)   // reserve the host first
            : budget;

        var used = 0d;
        var visible = 0;
        var heights = 0d;
        foreach (var (child, desired) in order)
        {
            var width = desired.Width + (visible > 0 ? _spacing : 0);
            if (constrained && used + width > fitBudget)
            {
                // Collapse the CELL, never the authored control: the item's own IsVisible, bindings and
                // actions stay exactly as authored for whatever surface shows it next.
                child.IsVisible = false;
                _hidden.Add(child);
                continue;
            }
            child.IsVisible = true;
            used += width;
            visible++;
            heights = Math.Max(heights, desired.Height);
        }

        HiddenItemCount = _hidden.Count;
        VisibleItemCount = visible;

        if (host is not null)
        {
            // The overflow control is shown only because items were actually collapsed, so an "advanced"
            // chevron never sits in a toolbar that had room for everything.
            host.IsVisible = HiddenItemCount > 0;
            if (HiddenItemCount > 0)
            {
                used += _spacing + hostDesired.Width;
                heights = Math.Max(heights, hostDesired.Height);
            }
        }

        return new Size(Math.Min(budget, used), heights);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        Control? host = null;
        foreach (var child in Children)
        {
            if (IsOverflowHost(child)) { host = child; continue; }
            if (!child.IsVisible)
            {
                // Collapsed cells are still arranged at zero size so the native engine never keeps a
                // stale hit target where the item used to be.
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, Math.Max(0, width), Math.Max(finalSize.Height, child.DesiredSize.Height)));
            x += width + _spacing;
        }
        if (host is not null && host.IsVisible)
        {
            x = Math.Max(0, Math.Min(x, finalSize.Width - host.DesiredSize.Width));
            host.Arrange(new Rect(x, 0, host.DesiredSize.Width, Math.Max(finalSize.Height, host.DesiredSize.Height)));
        }
        return finalSize;
    }

    private static bool IsOverflowHost(Control cell)
    {
        // Read the authored flag through CUI's own typed property store: never a donor marker, never a
        // native attached property written by hand. The cell is the layout wrapper; the authored control
        // inside it is what carries authored properties.
        var authored = cell is Border bordered ? bordered.Child ?? cell : cell;
        return CuiOverflowHosts.IsHost(authored) || CuiOverflowHosts.IsHost(cell);
    }
}
