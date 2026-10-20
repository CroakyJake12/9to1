using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Overlay layout that supports genuine out-of-flow floating children. Regular children are measured
/// and arranged exactly as in a plain <see cref="Panel"/> (fill the available area). Floating
/// children (cells whose direct child is a <see cref="CuiFloatingLayer"/>) are excluded from this
/// panel's desired size (so they never inflate the owning container's outer measure) and are
/// arranged at their content size adjacent to an anchor control discovered from the regular-flow
/// children — placed on the requested side, flipped when the room is insufficient, and clamped to
/// the panel bounds so interactive content underneath is never covered. Only targeted
/// per-anchor/per-bounds observers are used (never a permanent global LayoutUpdated subscription),
/// detached when the panel leaves the visual tree.
/// </summary>
internal sealed class CuiOverlayPanel : Panel, IDisposable
{
    private const double Gap = 8;

    private readonly List<(Control Observed, EventHandler<AvaloniaPropertyChangedEventArgs> Handler)> _observed = [];
    private Control? _anchor;

    public CuiOverlayPanel() => UseLayoutRounding = false;

    private static bool IsFloatingCell(Control cell) =>
        cell is Border { Child: CuiContainerControl { Layout: CuiFloatingLayer } };

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        foreach (var cell in Children)
        {
            cell.Measure(availableSize);
            if (IsFloatingCell(cell)) continue;   // out-of-flow: excluded from the outer desired size
            width = Math.Max(width, cell.DesiredSize.Width);
            height = Math.Max(height, cell.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var anchor = ResolveAnchor();
        Observe(anchor);
        foreach (var cell in Children)
        {
            if (IsFloatingCell(cell)) cell.Arrange(Place(cell, anchor, finalSize));
            else cell.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }
        return finalSize;
    }

    private static Rect Place(Control cell, Control? anchor, Size bounds)
    {
        var size = cell.DesiredSize;
        var anchorRect = AnchorRect(anchor, cell.GetVisualParent() as Visual ?? cell);
        var below = false;
        // The floating layer's Side is not used here; the default is above the anchor with flip.
        double y = anchorRect.Top - Gap - size.Height;
        if (y < 0) { y = anchorRect.Bottom + Gap; below = true; }
        if (below && y + size.Height > bounds.Height) y = Math.Max(0, anchorRect.Top - Gap - size.Height);
        if (y + size.Height > bounds.Height) y = Math.Max(0, bounds.Height - size.Height);
        y = Math.Max(0, y);

        double x = anchorRect.Center.X - size.Width / 2;
        if (x < 0) x = 0;
        if (x + size.Width > bounds.Width) x = Math.Max(0, bounds.Width - size.Width);
        return new Rect(x, y, size.Width, size.Height);
    }

    private static Rect AnchorRect(Control? anchor, Visual relativeTo)
    {
        if (anchor is null || !anchor.IsAttachedToVisualTree) return new Rect(0, 0, 0, 0);
        var origin = anchor.TranslatePoint(default, relativeTo) ?? default;
        return new Rect(origin.X, origin.Y, anchor.Bounds.Width, anchor.Bounds.Height);
    }

    private Control? ResolveAnchor()
    {
        if (_anchor is not null && _anchor.IsAttachedToVisualTree) return _anchor;
        _anchor = null;
        foreach (var cell in Children)
        {
            if (IsFloatingCell(cell)) continue;
            var child = (cell as Border)?.Child ?? cell;
            _anchor = child.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (_anchor is not null) break;
        }
        return _anchor;
    }

    private void Observe(Control? anchor)
    {
        var targets = this.GetVisualAncestors().Prepend(this).OfType<Control>().ToList();
        if (anchor is not null && !targets.Contains(anchor)) targets.Add(anchor);

        if (_observed.Count == targets.Count
            && _observed.Zip(targets, (stored, expected) => ReferenceEquals(stored.Observed, expected)).All(match => match))
            return;

        DisposeObservers();
        foreach (var target in targets)
        {
            EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
            {
                if (change.Property == BoundsProperty || change.Property == IsVisibleProperty) InvalidateMeasure();
            };
            target.PropertyChanged += handler;
            _observed.Add((target, handler));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeObservers();
        _anchor = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void DisposeObservers()
    {
        foreach (var (target, handler) in _observed) target.PropertyChanged -= handler;
        _observed.Clear();
    }

    public void Dispose() => DisposeObservers();
}
