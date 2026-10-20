using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CakeOS.Cui.Runtime;

/// <summary>CUI-owned composition boundary over the maintained native layout engines.
/// The outer element owns CUI identity/placement and decoration; Layout is renderer-private structure.</summary>
public sealed class CuiContainerControl : CuiMaterialBorder, IDisposable
{
    public Panel Layout { get; }
    public string LayoutType { get; }
    internal bool InheritX { get; private set; }
    internal bool InheritY { get; private set; }
    private readonly ScrollViewer _scroll = new();
    private bool _horizontalScrolling, _verticalScrolling;
    private bool _followAtEnd, _followingLatest = true;
    private Thickness _cellMargin;
    private readonly List<Border> _cells = [];
    private readonly List<(Control Control, EventHandler<AvaloniaPropertyChangedEventArgs> Handler)> _placementHandlers = [];
    private bool _compoundFocusEnabled;
    private IBrush? _focusBrush;
    private IBrush? _validationBrush;
    private double _focusThickness = 2;
    private IDisposable? _focusBrushSubscription;
    private IDisposable? _validationBrushSubscription;
    private IDisposable? _focusThicknessSubscription;
    private CuiCompoundFocusScope? _compoundFocusScope;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _compoundFocusHandler;
    /// <summary>True when this container owns the generic outer compound-focus ring.</summary>
    public bool CompoundFocusEnabled => _compoundFocusEnabled;
    /// <summary>The typed per-container focus/validation scope, or null until compound focus is enabled.</summary>
    public CuiCompoundFocusScope? CompoundFocusScope => _compoundFocusScope;
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
        _scroll.ScrollChanged += OnScrollChanged;
    }

    /// <summary>
    /// Generic compound-focus contract: the container owns visible keyboard focus
    /// for its embedded controls, drawing an outer ring instead of letting inner
    /// inputs paint their own boxes. Selected generically by authored markup. The
    /// ring is rendered through the CUI typed property store (stable geometry, live
    /// theme/HC resource observation, precedence-respecting) and remains active while
    /// a logically-owned menu popup holds focus, so the expanded composer surface and
    /// its Aa trigger survive toolbar interaction.
    /// </summary>
    public void EnableCompoundFocus()
    {
        if (_compoundFocusEnabled) return;
        _compoundFocusEnabled = true;
        _focusBrushSubscription = this.GetResourceObservable("CuiFocusBrush").Subscribe(new ValueObserver(brush =>
        {
            _focusBrush = brush as IBrush;
            ApplyCompoundFocus();
        }));
        _validationBrushSubscription = this.GetResourceObservable("CuiValidationBrush").Subscribe(new ValueObserver(brush =>
        {
            _validationBrush = brush as IBrush;
            ApplyCompoundFocus();
        }));
        _focusThicknessSubscription = this.GetResourceObservable("CuiFocusIndicatorThickness").Subscribe(new ValueObserver(thickness =>
        {
            _focusThickness = thickness is double width ? width : 2;
            ApplyCompoundFocus();
        }));
        // One generic declared per-container scope observes the actual owning top-level focus and
        // its real descendant menu open/close lifecycle plus descendant validation. No global menu
        // registry: only real owned menus keep this composite focused.
        _compoundFocusScope = new CuiCompoundFocusScope(this);
        _compoundFocusScope.FocusChanged += OnCompoundFocusChanged;
        _compoundFocusScope.ValidationChanged += OnCompoundFocusChanged;
        _compoundFocusScope.Attach();
        // Reserve a constant focus thickness up front so geometry never shifts when
        // the ring appears/disappears; only the brush changes with focus state.
        CuiPropertyPresentation.Set(this, "BorderThickness", new Thickness(_focusThickness),
            CuiPropertyLevel.Overlay, "CUI compound focus", -100);
        _compoundFocusHandler = (_, change) =>
        {
            if (change.Property == InputElement.IsKeyboardFocusWithinProperty)
                ApplyCompoundFocus();
        };
        PropertyChanged += _compoundFocusHandler;
        ApplyCompoundFocus();
    }

    private void OnCompoundFocusChanged(object? sender, bool state) => ApplyCompoundFocus();

    private void ApplyCompoundFocus()
    {
        var focused = _compoundFocusScope?.IsFocused ?? IsKeyboardFocusWithin;
        var invalid = _compoundFocusScope?.HasValidationError ?? false;
        // Truthful descendant validation wins over focus on the outer compound ring so a genuine
        // error is never hidden; otherwise valid focused/rest state uses the theme-owned brush.
        IBrush? brush;
        if (invalid) brush = _validationBrush;
        else brush = focused && _focusBrush is not null ? _focusBrush : Avalonia.Media.Brushes.Transparent;
        CuiPropertyPresentation.Set(this, "BorderBrush", brush ?? Avalonia.Media.Brushes.Transparent,
            CuiPropertyLevel.Overlay, "CUI compound focus", -100);
        CuiPropertyPresentation.Set(this, "BorderThickness", new Thickness(_focusThickness),
            CuiPropertyLevel.Overlay, "CUI compound focus", -100);
    }

    /// <summary>Follow new content only while the reader remains at the end of the vertical viewport.</summary>
    public void SetFollowAtEnd(bool enabled)
    {
        if (_followAtEnd == enabled) return;
        _followAtEnd = enabled;
        _followingLatest = IsAtEnd();
    }

    private bool IsAtEnd() => _scroll.Extent.Height - _scroll.Viewport.Height - _scroll.Offset.Y <= 1;

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, _scroll) || !_followAtEnd || !_verticalScrolling) return;
        var end = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
        var layoutChanged = args.ExtentDelta.Y != 0 || args.ViewportDelta.Y != 0;
        // Layout and input can arrive in the same notification. Preserve a
        // reader's movement while allowing the layout's own offset clamp.
        var previousOffset = _scroll.Offset.Y - args.OffsetDelta.Y;
        var clampedOffset = Math.Clamp(previousOffset, 0, end);
        var readerMoved = Math.Abs(_scroll.Offset.Y - clampedOffset) > 1;
        if (!layoutChanged || readerMoved)
            _followingLatest = IsAtEnd();
        if (layoutChanged && _followingLatest)
            _scroll.Offset = new Vector(_scroll.Offset.X, end);
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
    {
        if (_compoundFocusHandler is not null) { PropertyChanged -= _compoundFocusHandler; _compoundFocusHandler = null; }
        if (_compoundFocusScope is not null) { _compoundFocusScope.FocusChanged -= OnCompoundFocusChanged; _compoundFocusScope.ValidationChanged -= OnCompoundFocusChanged; _compoundFocusScope.Dispose(); _compoundFocusScope = null; }
        _focusBrushSubscription?.Dispose(); _validationBrushSubscription?.Dispose(); _focusThicknessSubscription?.Dispose();
        _focusBrushSubscription = null; _validationBrushSubscription = null; _focusThicknessSubscription = null;
        _scroll.ScrollChanged -= OnScrollChanged;
        foreach (var (control, handler) in _placementHandlers) control.PropertyChanged -= handler;
        _placementHandlers.Clear();
        (Layout as IDisposable)?.Dispose();
    }

    private sealed class ValueObserver(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }

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
