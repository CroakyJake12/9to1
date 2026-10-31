using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>CUI-owned composition boundary over the maintained native layout engines.
/// The outer element owns CUI identity/placement and decoration; Layout is renderer-private structure.</summary>
public class CuiContainerControl : CuiMaterialBorder, IDisposable
{
    public Panel Layout { get; }
    public string LayoutType { get; }
    internal bool InheritX { get; private set; }
    internal bool InheritY { get; private set; }
    private readonly ScrollViewer _scroll = new();
    private bool _horizontalScrolling, _verticalScrolling;
    private bool _horizontalOverflowArrows;
    private CuiHorizontalOverflowControls? _horizontalOverflowControls;
    private double _scrollBarThickness, _scrollBarDisplayScale = 1;
    private IDisposable? _scrollBarScaleSubscription;
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
    private string? _authoredTargetId;
    private string? _authoredPlacement;

    /// <summary>Raised when the authored floating relationship (Target/Placement) changes, so the
    /// overlay-float panel can invalidate its cached anchor and rebind observers.</summary>
    internal event EventHandler? RelationshipChanged;

    /// <summary>
    /// Optional generic typed close-gesture handler for authored floating content. When set, a
    /// user-initiated close (e.g. Escape on a portaled floating bar) invokes it BEFORE retirement so the
    /// authored open/close state is reconciled through the canonical typed action/state path rather than
    /// leaving selected/open state behind and immediately reopening. This is authored/wired by the
    /// surface (through the canonical action dispatch), never synthesized by the generic framework.
    /// </summary>
    public Action? FloatingCloseRequested { get; set; }

    /// <summary>
    /// Optional publisher installed by the loader so a portaled host's native popup close can join the
    /// SAME original owner ledger that other authored actions use (once-only receipt, surfaced by
    /// WhenActionsIdleAsync/disposal including failure). Wired only while this host is owned and wired
    /// by the loader; retired together with <see cref="FloatingCloseRequested"/> on detach/reload/dispose.
    /// </summary>
    public Action<Task>? FloatingCloseLedgerPublisher { get; set; }

    /// <summary>Authored floating-relationship target (authored element id) for overlay-float placement.
    /// This is authored CUI, never inferred by framework heuristics.</summary>
    public string? AuthoredTargetId => _authoredTargetId;

    /// <summary>Authored placement side relative to the target (Above/Under/LeftOf/RightOf).</summary>
    public string? AuthoredPlacement => _authoredPlacement;

    /// <summary>Typed effective-property setter for the authored Target. Validates the id and raises a
    /// change notification so a live Target binding re-resolves the anchor. CUI-authored only; never
    /// inferred by framework heuristics.</summary>
    public void SetAuthoredTarget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Target requires a non-empty authored element id.", nameof(value));
        if (_authoredTargetId == value) return;
        _authoredTargetId = value;
        RelationshipChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Typed effective-property setter for the authored Placement. Accepts every accepted casing
    /// of Above/Under/LeftOf/RightOf and normalizes it; any other value fails closed (never silently
    /// treated as Above).</summary>
    public void SetAuthoredPlacement(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() switch
        {
            "above" => "Above",
            "under" => "Under",
            "leftof" => "LeftOf",
            "rightof" => "RightOf",
            _ => null,
        };
        if (normalized is null)
            throw new ArgumentException("Placement must be Above, Under, LeftOf or RightOf.", nameof(value));
        if (_authoredPlacement == normalized) return;
        _authoredPlacement = normalized;
        RelationshipChanged?.Invoke(this, EventArgs.Empty);
    }
    public IReadOnlyList<Control> AuthoredChildren => _cells.Select(AuthoredChildOf).Where(c => c is not null).Select(c => c!).ToArray();

    /// <summary>The authored child held by a cell, independently of physical attachment: a portaled host
    /// (physically held by an escape portal) is still this cell's authored child and must remain visible
    /// to framework consumers (traversal, order, removal/replacement, and teardown snapshot) even though
    /// its cell.Child is temporarily detached while the portal is live.</summary>
    private Control? AuthoredChildOf(Border cell)
    {
        if (cell.Child is not null) return cell.Child;
        if (Layout is CuiOverlayPanel overlay && overlay.TryGetPortaledHostForCell(cell, out var host)) return host;
        return null;
    }

    public bool RemoveChild(Control child)
    {
        ArgumentNullException.ThrowIfNull(child);
        var cell = _cells.FirstOrDefault(value => ReferenceEquals(value.Child, child));
        if (cell is null && child is CuiContainerControl host && Layout is CuiOverlayPanel overlay && overlay.PortaledHosts.Contains(host))
        {
            // A portaled host is physically held by an escape portal (its cell's Child is detached).
            // Retire the portal first so the SAME authored host is restored to its cell, then remove it.
            overlay.ForceRetirePortaledHost(host);
            cell = _cells.FirstOrDefault(value => ReferenceEquals(value.Child, child));
        }
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
            || children.Any(child => !_cells.Any(cell => ReferenceEquals(AuthoredChildOf(cell), child))))
            throw new ArgumentException("Reordering must name every existing authored child exactly once.", nameof(children));
        var ordered = children.Select(child => _cells.Single(cell => ReferenceEquals(AuthoredChildOf(cell), child))).ToArray();
        _cells.Clear(); _cells.AddRange(ordered);
        // Move existing cells, preserving child identity, event subscriptions and native value stores.
        // A portaled host's cell is detached/hidden; it is handled by the portal lane, not the in-tree
        // physical ordering, but its authored identity/order is still preserved.
        var active = ordered.Where(cell => AuthoredChildOf(cell)?.IsVisible == true).ToArray();
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
            "toolbar" => new CuiToolbarPanel(),
            "separator" => new CuiSeparatorPanel(),
            "ribbon group" => new CuiRibbonGroupPanel(),
            "absolute" => new CuiAbsolutePanel(),
            "overlay" => new Panel(),
            "overlay float" => new CuiOverlayPanel(),
            "floating" => new CuiFloatingLayer(),
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
        if (_horizontalOverflowArrows && _horizontalOverflowControls is null)
        {
            // Retain the same materialised viewport/content while adding only
            // its CUI-owned overlay controls; never create a second scroller.
            if (ReferenceEquals(Child, _scroll)) Child = null;
            _horizontalOverflowControls = new CuiHorizontalOverflowControls(this, _scroll);
        }
        Control scrollHost = _horizontalOverflowControls?.Host ?? (Control)_scroll;
        if ((enabled || ReferenceEquals(_scroll.Content, Layout)) && !ReferenceEquals(Child, scrollHost))
        {
            Child = null;
            if (!ReferenceEquals(_scroll.Content, Layout)) _scroll.Content = Layout;
            Child = scrollHost;
        }
        _horizontalOverflowControls?.SetEnabled(_horizontalScrolling && _horizontalOverflowArrows, _verticalScrolling);
        // Keep a materialised presenter structurally stable. Clearing Content
        // can leave its visual child attached until the next renderer pass;
        // immediately assigning Layout elsewhere then gives it two parents.
        // Disabled axes measure normally and cannot consume wheel scrolling.
        _scroll.HorizontalScrollBarVisibility = !_horizontalScrolling ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : _horizontalOverflowArrows ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = _verticalScrolling ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    /// <summary>Replace the horizontal rail with CUI directional overflow controls.
    /// HorizontalScrolling still owns whether the native axis can scroll.</summary>
    public void SetHorizontalOverflowArrows(bool enabled)
    {
        if (_horizontalOverflowArrows == enabled) return;
        _horizontalOverflowArrows = enabled;
        SetScrolling();
    }

    /// <summary>Opt-in visible scrollbar face thickness in unscaled DIP. Zero
    /// restores the theme default; the native rail and hit geometry stay intact.</summary>
    public void SetScrollBarThickness(double thickness)
    {
        if (!double.IsFinite(thickness) || thickness is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(thickness));
        if (_scrollBarThickness == thickness) return;
        _scrollBarThickness = thickness;
        _scrollBarScaleSubscription ??= this.GetResourceObservable("CuiDisplayScale").Subscribe(new ValueObserver(value =>
        {
            _scrollBarDisplayScale = value is double scale ? scale : 1;
            ApplyScrollBarThickness();
        }));
        ApplyScrollBarThickness();
    }
    private void ApplyScrollBarThickness()
    {
        if (_scrollBarThickness > 0) _scroll.Resources["CuiScrollThumbThickness"] = _scrollBarThickness * _scrollBarDisplayScale;
        else _scroll.Resources.Remove("CuiScrollThumbThickness");
    }

    public void AddChild(Control child) => InsertAuthoredChild(AuthoredChildCount, child);

    /// <summary>Number of authored children (cells) currently held by this container.</summary>
    internal int AuthoredChildCount => _cells.Count;

    /// <summary>Insert an authored child as a layout cell at a specific position in the container's
    /// authored child range, reusing the same stable cell/placement machinery as AddChild. This is
    /// the low-level primitive behind the canonical generic Repeat, which emits each generated item
    /// into the owning Container's own layout instead of inventing a hidden sub-panel.</summary>
    internal void InsertAuthoredChild(int index, Control child)
    {
        if (CuiAbsoluteLayout.HasPlacement(child) && Layout is not CuiAbsolutePanel)
            throw new InvalidOperationException("Free coordinates require a Container Type Absolute parent.");
        var cell = new Border { Child = child, Padding = _cellMargin };
        void Placement()
        {
            // Record whether the retained cell is present in this container's layout before applying the
            // authored placement, so the measure gate below fires only on a genuine membership transition.
            var wasPresent = Layout.Children.Contains(cell);
            // A hidden floating host is about to leave native layout. Retire its portal while the
            // original cell/host custody is still available; otherwise arrange can no longer find it.
            if (!child.IsVisible && child is CuiContainerControl host && Layout is CuiOverlayPanel overlay)
                overlay.ForceRetirePortaledHost(host);
            // Retirement can restore focus synchronously. Its callbacks may remove this exact child
            // and install a same-ID replacement; the old placement must not resurrect its cell.
            if (!_cells.Contains(cell) || !ReferenceEquals(AuthoredChildOf(cell), child)) return;
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
            // A child crossing the visible threshold enters/leaves the retained layout, mutating this
            // container's live content size. Invalidate THIS container's own measure exactly on that
            // genuine transition so a re-measure reflects the new content. This is required even when the
            // container sits inside a suppressed/hidden outer wrapper (e.g. an unplaced or portaled
            // floating cell): the normal layout-manager descendant remeasurement is blocked by the hidden
            // ancestor, so without an explicit invalidation here the container's cached DesiredSize stays
            // stale (e.g. 0,0 from when the child was hidden) and the owner mis-reads the floating content
            // size. The gate is bounded to the membership transition (not grid/span/canvas/ZIndex
            // coordinate cascades), so generic placement property changes never trigger re-measure cycles.
            if (Layout.Children.Contains(cell) != wasPresent)
            {
                InvalidateMeasure();
                // A nested transition under a suppressed floating wrapper cannot rely on the native
                // layout manager to refresh intervening cached presenters. Invalidate that retained
                // measure path through the floating host, only for this actual membership change.
                var ancestors = Layout.GetVisualAncestors().OfType<Layoutable>().ToArray();
                var floating = Array.FindIndex(ancestors, ancestor => ancestor is CuiContainerControl { Layout: CuiFloatingLayer });
                for (var position = 0; position <= floating; position++) ancestors[position].InvalidateMeasure();
            }
        }
        EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
        {
            if (change.Property == Grid.RowProperty || change.Property == Grid.ColumnProperty || change.Property == Grid.RowSpanProperty || change.Property == Grid.ColumnSpanProperty
                || change.Property == Canvas.LeftProperty || change.Property == Canvas.TopProperty || change.Property == IsVisibleProperty || change.Property == ZIndexProperty) Placement();
        };
        child.PropertyChanged += handler; _placementHandlers.Add((child, handler));
        index = Math.Clamp(index, 0, _cells.Count);
        _cells.Insert(index, cell); Placement();
    }

    /// <summary>Move one authored cell within the container's authored child range, preserving the
    /// cell's identity, subscriptions, native value stores and physical attachment ordering. Used
    /// by the generic Repeat to apply a keyed reorder without clearing/re-adding sibling cells.</summary>
    internal void MoveAuthoredChild(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        if (fromIndex < 0 || fromIndex >= _cells.Count) throw new ArgumentOutOfRangeException(nameof(fromIndex));
        if (toIndex < 0 || toIndex >= _cells.Count) throw new ArgumentOutOfRangeException(nameof(toIndex));
        var cell = _cells[fromIndex];
        _cells.RemoveAt(fromIndex);
        _cells.Insert(toIndex, cell);
        if (cell.Child?.IsVisible != true) return;
        var current = Layout.Children.IndexOf(cell);
        var desired = _cells.TakeWhile(value => !ReferenceEquals(value, cell)).Count(value => Layout.Children.Contains(value));
        desired = Math.Min(desired, Layout.Children.Count - 1);
        if (current < 0) Layout.Children.Insert(desired, cell);
        else if (current != desired) Layout.Children.Move(current, desired);
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
        _horizontalOverflowControls?.Dispose(); _horizontalOverflowControls = null;
        _scrollBarScaleSubscription?.Dispose(); _scrollBarScaleSubscription = null;
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

    /// <summary>Applies the authored GroupLabel caption. Like CellGap, declaring it where the construct
    /// cannot honour it is an explicit error rather than a silently ignored attribute.</summary>
    public void SetGroupLabel(string? value)
    {
        if (Layout is not CuiRibbonGroupPanel ribbon)
            throw new InvalidOperationException($"GroupLabel is not meaningful for Container Type '{LayoutType}'.");
        ribbon.Label = value;
    }

    public void SetCellGap(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        switch (Layout)
        {
            case Grid grid: grid.RowSpacing = value; grid.ColumnSpacing = value; break;
            case StackPanel stack: stack.Spacing = value; break;
            case WrapPanel wrap: wrap.ItemSpacing = value; wrap.LineSpacing = value; break;
            case CuiToolbarPanel toolbar: toolbar.Spacing = value; break;
            case CuiRibbonGroupPanel ribbon: ribbon.Spacing = value; break;
            default: throw new InvalidOperationException($"CellGap is not meaningful for Container Type '{LayoutType}'.");
        }
    }
}
