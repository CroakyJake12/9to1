using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CakeOS.Cui.Runtime;

/// <summary>CUI-owned composition boundary over the maintained native layout engines.
/// The outer element owns CUI identity/placement and decoration; Layout is renderer-private structure.</summary>
public sealed class CuiContainerControl : Border, IDisposable
{
    public Panel Layout { get; }
    public string LayoutType { get; }
    internal bool InheritX { get; private set; }
    internal bool InheritY { get; private set; }
    private readonly ScrollViewer _scroll = new();
    private bool _horizontalScrolling, _verticalScrolling;
    private Thickness _cellMargin;
    private readonly List<Border> _cells = [];
    private readonly List<(Control Control, EventHandler<AvaloniaPropertyChangedEventArgs> Handler)> _placementHandlers = [];
    public IReadOnlyList<Control> AuthoredChildren => _cells.Select(cell => cell.Child!).ToArray();

    public bool RemoveChild(Control child)
    {
        ArgumentNullException.ThrowIfNull(child);
        var cell = _cells.FirstOrDefault(value => ReferenceEquals(value.Child, child));
        if (cell is null) return false;
        foreach (var item in _placementHandlers.Where(item => ReferenceEquals(item.Control, child)).ToArray())
        { child.PropertyChanged -= item.Handler; _placementHandlers.Remove(item); }
        Layout.Children.Remove(cell); cell.Child = null; _cells.Remove(cell);
        return true;
    }

    public void ReorderChildren(IReadOnlyList<Control> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count != _cells.Count || children.Distinct(ReferenceEqualityComparer.Instance).Count() != children.Count
            || children.Any(child => !_cells.Any(cell => ReferenceEquals(cell.Child, child))))
            throw new ArgumentException("Reordering must name every existing authored child exactly once.", nameof(children));
        var ordered = children.Select(child => _cells.Single(cell => ReferenceEquals(cell.Child, child))).ToArray();
        _cells.Clear(); _cells.AddRange(ordered);
        // Move existing cells, preserving child identity, event subscriptions and native value stores.
        var active = ordered.Where(cell => cell.Child?.IsVisible == true).ToArray();
        for (var index = 0; index < active.Length; index++)
        {
            var current = Layout.Children.IndexOf(active[index]);
            // A native visibility notification may re-enter the owner before
            // its placement observer reattaches the retained cell. Reconcile
            // that existing cell instead of passing -1 into the native list.
            if (current < 0) Layout.Children.Insert(index, active[index]);
            else if (current != index) Layout.Children.Move(current, index);
        }
    }

    public CuiContainerControl(string layoutType)
    {
        LayoutType = layoutType;
        Layout = layoutType.ToLowerInvariant() switch
        {
            "grid" => new Grid(),
            "vertical stack" => new StackPanel { Orientation = Orientation.Vertical },
            "horizontal stack" => new StackPanel { Orientation = Orientation.Horizontal },
            "wrap" => new WrapPanel(),
            "absolute" => new CuiAbsolutePanel(),
            "overlay" => new Panel(),
            _ => throw new ArgumentOutOfRangeException(nameof(layoutType), layoutType, "Unknown canonical Container layout Type.")
        };
        Child = Layout;
    }

    public void SetScrolling(bool? horizontal = null, bool? vertical = null)
    {
        if (horizontal is { } h) _horizontalScrolling = h;
        if (vertical is { } v) _verticalScrolling = v;
        var enabled = _horizontalScrolling || _verticalScrolling;
        if (enabled && !ReferenceEquals(Child, _scroll)) { Child = null; _scroll.Content = Layout; Child = _scroll; }
        // Keep a materialised presenter structurally stable. Clearing Content
        // can leave its visual child attached until the next renderer pass;
        // immediately assigning Layout elsewhere then gives it two parents.
        // Disabled axes measure normally and cannot consume wheel scrolling.
        _scroll.HorizontalScrollBarVisibility = _horizontalScrolling ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _scroll.VerticalScrollBarVisibility = _verticalScrolling ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    public void AddChild(Control child)
    {
        if (CuiAbsoluteLayout.HasPlacement(child) && Layout is not CuiAbsolutePanel)
            throw new InvalidOperationException("Free coordinates require a Container Type Absolute parent.");
        var cell = new Border { Child = child, Padding = _cellMargin };
        void Placement()
        {
            Grid.SetRow(cell, Grid.GetRow(child)); Grid.SetColumn(cell, Grid.GetColumn(child));
            Grid.SetRowSpan(cell, Grid.GetRowSpan(child)); Grid.SetColumnSpan(cell, Grid.GetColumnSpan(child));
            Canvas.SetLeft(cell, Canvas.GetLeft(child)); Canvas.SetTop(cell, Canvas.GetTop(child));
            cell.IsVisible = child.IsVisible;
            cell.ZIndex = child.ZIndex;
            if (!child.IsVisible) Layout.Children.Remove(cell);
            else if (!Layout.Children.Contains(cell))
            {
                var position = _cells.TakeWhile(value => !ReferenceEquals(value, cell)).Count(value => Layout.Children.Contains(value));
                Layout.Children.Insert(Math.Min(position, Layout.Children.Count), cell);
            }
        }
        EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
        {
            if (change.Property == Grid.RowProperty || change.Property == Grid.ColumnProperty || change.Property == Grid.RowSpanProperty || change.Property == Grid.ColumnSpanProperty
                || change.Property == Canvas.LeftProperty || change.Property == Canvas.TopProperty || change.Property == IsVisibleProperty || change.Property == ZIndexProperty) Placement();
        };
        child.PropertyChanged += handler; _placementHandlers.Add((child, handler));
        _cells.Add(cell); Placement();
    }

    public void SetCellMargin(Thickness value)
    { _cellMargin = value; foreach (var cell in _cells) cell.Padding = value; }
    internal void SetInheritedAxis(bool horizontal, bool value)
    {
        if (horizontal) InheritX = value; else InheritY = value;
        InvalidateCoordinates(this);
        static void InvalidateCoordinates(CuiContainerControl container)
        {
            if (container.Layout is CuiAbsolutePanel panel) panel.InvalidateArrange();
            foreach (var child in container.AuthoredChildren.OfType<CuiContainerControl>()) InvalidateCoordinates(child);
        }
    }
    public void Dispose()
    { foreach (var (control, handler) in _placementHandlers) control.PropertyChanged -= handler; _placementHandlers.Clear(); (Layout as IDisposable)?.Dispose(); }

    public void SetCellGap(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        switch (Layout)
        {
            case Grid grid: grid.RowSpacing = value; grid.ColumnSpacing = value; break;
            case StackPanel stack: stack.Spacing = value; break;
            case WrapPanel wrap: wrap.ItemSpacing = value; wrap.LineSpacing = value; break;
            default: throw new InvalidOperationException($"CellGap is not meaningful for Container Type '{LayoutType}'.");
        }
    }
}
