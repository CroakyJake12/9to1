using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>Renderer projection of CUI's centre-origin coordinates. Values remain
/// owned by the element's effective property store.</summary>
internal static class CuiAbsoluteLayout
{
    internal sealed class Placement
    {
        public string? XMode, YMode;
        public double X, Y;
    }
    private static readonly ConditionalWeakTable<Control, Placement> Placements = new();
    internal static bool HasPlacement(Control control) => Placements.TryGetValue(control, out var placement) &&
        (placement.XMode is not null || placement.YMode is not null);
    internal static Placement For(Control control) => Placements.GetOrCreateValue(control);

    internal static void Validate(Control control, string property, double value)
    {
        if (!double.IsFinite(value)) throw new FormatException("Absolute coordinates must be finite.");
        var placement = For(control);
        var mode = property.StartsWith('X') ? placement.XMode : placement.YMode;
        if (mode is not null && mode != property)
            throw new InvalidOperationException($"Only one absolute coordinate mode is permitted per axis: {mode} conflicts with {property}.");
        var parent = control.GetVisualAncestors().OfType<CuiContainerControl>().FirstOrDefault();
        if (parent is not null && parent.Layout is not CuiAbsolutePanel)
            throw new InvalidOperationException("Free coordinates require a Container Type Absolute parent.");
    }

    internal static void Apply(Control control, string property, double? value)
    {
        var placement = For(control);
        if (property.StartsWith('X')) { placement.XMode = value.HasValue ? property : null; placement.X = value ?? 0; }
        else { placement.YMode = value.HasValue ? property : null; placement.Y = value ?? 0; }
        control.GetVisualAncestors().OfType<CuiAbsolutePanel>().FirstOrDefault()?.InvalidateArrange();
    }
}

/// <summary>Absolute children use their centre as the positioning reference.
/// Each axis independently selects the nearest inherited scope, or the outer
/// CUI surface when no ancestor recalibrates it.</summary>
internal sealed class CuiAbsolutePanel : Panel, IDisposable
{
    private readonly List<Visual> _observed = [];
    public CuiAbsolutePanel() => UseLayoutRounding = false;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ObserveAncestors();
        var ancestors = this.GetVisualAncestors().OfType<CuiContainerControl>().ToArray();
        var xScope = ancestors.FirstOrDefault(c => c.InheritX) ?? ancestors.LastOrDefault();
        var yScope = ancestors.FirstOrDefault(c => c.InheritY) ?? ancestors.LastOrDefault();
        foreach (var cell in Children)
        {
            var child = (cell as Border)?.Child ?? cell;
            var position = CuiAbsoluteLayout.For(child);
            var size = cell.DesiredSize;
            var x = Project(xScope, position.XMode, position.X, horizontal: true, finalSize);
            var y = Project(yScope, position.YMode, position.Y, horizontal: false, finalSize);
            cell.Arrange(new Rect(x - size.Width / 2, y - size.Height / 2, size.Width, size.Height));
        }
        return finalSize;
    }

    private double Project(CuiContainerControl? scope, string? mode, double value, bool horizontal, Size finalSize)
    {
        // Bounds notifications below invalidate the next pass when an ancestor
        // has only just been arranged. This also covers resize, scrolling and
        // retained subtree reattachment without a global layout subscription.
        var bounds = scope?.Bounds.Size ?? finalSize;
        var origin = scope?.TranslatePoint(default, this) ?? default;
        var length = horizontal ? bounds.Width : bounds.Height;
        var offset = mode?.EndsWith("Pixel", StringComparison.Ordinal) == true
            ? length / 2 + (horizontal ? value : -value)
            : mode?.EndsWith("Percent", StringComparison.Ordinal) == true
                ? length * (horizontal ? value / 100 : 1 - value / 100)
                : length * (horizontal ? (value + 1000) / 2000 : (1000 - value) / 2000);
        return (horizontal ? origin.X : origin.Y) + offset;
    }

    private void ObserveAncestors()
    {
        var current = this.GetVisualAncestors().Prepend(this).ToArray();
        if (_observed.SequenceEqual(current)) return;
        Dispose();
        foreach (var ancestor in current)
        {
            ancestor.PropertyChanged += AncestorChanged;
            _observed.Add(ancestor);
        }
    }

    private void AncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == BoundsProperty) InvalidateArrange();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { Dispose(); base.OnDetachedFromVisualTree(e); }

    public void Dispose()
    {
        foreach (var ancestor in _observed) ancestor.PropertyChanged -= AncestorChanged;
        _observed.Clear();
    }
}
