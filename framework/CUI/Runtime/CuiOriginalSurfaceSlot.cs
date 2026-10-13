using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>A platform composition boundary around one original, independently
/// owned CUI surface. The inner owner's loader and lifetime remain authoritative.
/// The outer loader sees this slot, not a second ownership of the child's tree.</summary>
public sealed class CuiOriginalSurfaceSlot : Control
{
    private Control? _original;
    public Control? OriginalSurface => _original;

    public void SetOriginalSurface(Control? original)
    {
        if (ReferenceEquals(original, _original)) return;
        if (original is not null && (original.Parent is not null || original.GetVisualParent() is not null))
            throw new InvalidOperationException("The original CUI surface must be detached by its current host before transfer.");
        var previous = _original;
        if (previous is not null)
        {
            VisualChildren.Remove(previous);
            LogicalChildren.Remove(previous);
        }
        _original = original;
        if (original is not null)
        {
            LogicalChildren.Add(original);
            VisualChildren.Add(original);
        }
        InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        _original?.Measure(availableSize);
        return _original?.DesiredSize ?? default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _original?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
