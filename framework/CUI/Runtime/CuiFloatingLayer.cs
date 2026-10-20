using Avalonia;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Floating chrome host, used inside an <see cref="CuiOverlayPanel"/> to hold out-of-flow floating
/// content (for example the format bar). It is a simple content container: it is measured to its
/// child's desired size and arranges that child to fill it. Its parent overlay panel decides where
/// the floating cell is placed and excludes it from the outer desired size, so this type itself does
/// not do anchor math.
/// </summary>
internal sealed class CuiFloatingLayer : Panel
{
    public CuiFloatingLayer() => UseLayoutRounding = false;

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size();
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var cell in Children)
            cell.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
