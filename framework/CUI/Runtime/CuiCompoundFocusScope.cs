using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// One generic declared per-container compound-focus scope for a CUI container that owns the outer
/// compound focus/validation ring. It observes the ACTUAL owning top-level focus and the real
/// descendant menu open/close lifecycle (authored <see cref="CuiMenu"/> open/close and authored
/// <see cref="MenuItem"/> submenu open state, plus subtree attach/detach/replacement) to compute
/// whether keyboard focus is genuinely inside the composite: either directly within the owner's own
/// subtree or inside one of its owned menus' open logical popups.
///
/// There is deliberately NO process-global menu registry and NO app-specific menu policy: each
/// scope is bound to exactly one owning container and only menus that are real authored descendants
/// of that container can keep the scope focused. Membership is derived from the ACTUAL focused
/// element (not merely "some owned menu is open"), so an unrelated field/menu yields false even
/// while a popup is still open. The scope also relays truthful descendant input validation so the
/// owner can paint its outer indication without hiding errors.
/// </summary>
public sealed class CuiCompoundFocusScope : IDisposable
{
    private readonly CuiContainerControl _owner;
    private TopLevel? _topLevel;
    private bool _attached;
    private bool _disposed;
    private bool _focused;
    private bool _hasValidationError;
    private readonly Dictionary<CuiMenu, EventHandler<RoutedEventArgs>> _openedHandlers = [];
    private readonly Dictionary<CuiMenu, EventHandler<RoutedEventArgs>> _closedHandlers = [];
    private readonly Dictionary<MenuItem, EventHandler<AvaloniaPropertyChangedEventArgs>> _menuItemObservation = [];
    private readonly Dictionary<TextBox, EventHandler<AvaloniaPropertyChangedEventArgs>> _inputObservation = [];
    private readonly List<(CuiContainerControl Container, NotifyCollectionChangedEventHandler Handler)> _layoutHandlers = [];

    /// <summary>Construct the scope for its owning compound-focus container.</summary>
    public CuiCompoundFocusScope(CuiContainerControl owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>True when genuine keyboard focus currently lives inside this composite scope.</summary>
    public bool IsFocused => _focused;

    /// <summary>True when any embedded input owned by this scope currently has a validation error.</summary>
    public bool HasValidationError => _hasValidationError;

    /// <summary>Raised whenever the computed focus membership changes.</summary>
    public event EventHandler<bool>? FocusChanged;

    /// <summary>Raised whenever the relayed descendant validation state changes.</summary>
    public event EventHandler<bool>? ValidationChanged;

    /// <summary>Begin observing the owning container's lifetime. Idempotent.</summary>
    internal void Attach()
    {
        if (_attached || _disposed) return;
        _attached = true;
        _owner.AttachedToVisualTree += OnAttachedToVisualTree;
        _owner.DetachedFromVisualTree += OnDetachedFromVisualTree;
        if (_owner.IsAttachedToVisualTree()) WireTopLevel();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => WireTopLevel();

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => UnwireTopLevel();

    private void WireTopLevel()
    {
        if (!_attached || _disposed) return;
        var top = TopLevel.GetTopLevel(_owner);
        if (ReferenceEquals(top, _topLevel))
        {
            if (top is not null)
            {
                RefreshSubtree();
                RecomputeAll();
            }
            return;
        }
        if (_topLevel is not null) UnwireTopLevel();
        _topLevel = top;
        if (_topLevel is not null)
        {
            // Observe BOTH arrival (GotFocus) and departure/clear (LostFocus) on the owning
            // top-level. GotFocus alone misses focus cleared to null or a focused input being
            // hidden/removed; LostFocus catches those transitions. Focus inside a separately
            // hosted owned popup is re-derived on demand from the actual focused element.
            _topLevel.AddHandler(InputElement.GotFocusEvent, OnRootFocusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
            _topLevel.AddHandler(InputElement.LostFocusEvent, OnRootFocusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        }
        RefreshSubtree();
        RecomputeAll();
    }

    private void UnwireTopLevel()
    {
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(InputElement.GotFocusEvent, OnRootFocusChanged);
            _topLevel.RemoveHandler(InputElement.LostFocusEvent, OnRootFocusChanged);
            _topLevel = null;
        }
    }

    private void OnRootFocusChanged(object? sender, RoutedEventArgs e) => Recompute();

    /// <summary>
    /// Re-discover the current authored subtree: owned descendant menus, owned menu items,
    /// embedded inputs and every authored container including the OWNER's direct Layout.Children.
    /// All subscriptions are reconciled against the exact current tree so attach/detach/replacement
    /// and child removal never leave a stale instance observed. Focus and validation are recomputed
    /// on every refresh so an initially-invalid child and removal of the last invalid child publish
    /// immediately. Reentrant membership changes are NOT silently dropped: reconciliation is
    /// idempotent and rebuilds from the live tree each time.
    /// </summary>
    private void RefreshSubtree()
    {
        if (_disposed) return;
        var menus = new HashSet<CuiMenu>(ReferenceEqualityComparer.Instance);
        var items = new HashSet<MenuItem>(ReferenceEqualityComparer.Instance);
        var inputs = new HashSet<TextBox>(ReferenceEqualityComparer.Instance);
        var containers = new HashSet<CuiContainerControl>(ReferenceEqualityComparer.Instance);
        foreach (var visual in _owner.GetVisualDescendants())
        {
            switch (visual)
            {
                case CuiMenu menu: menus.Add(menu); break;
                case MenuItem item: items.Add(item); break;
                case TextBox box: inputs.Add(box); break;
                case CuiContainerControl container: containers.Add(container); break;
            }
        }
        // The OWNER's direct Layout.Children must be observed too, so a direct child/menu/input
        // removal or replacement is not missed (descendant container scans alone miss it).
        containers.Add(_owner);

        // Reconcile owned menu subscriptions.
        foreach (var menu in _openedHandlers.Keys.ToArray())
            if (!menus.Contains(menu)) UnwatchMenu(menu);
        foreach (var menu in menus)
            if (!_openedHandlers.ContainsKey(menu)) WatchMenu(menu);

        // Reconcile owned menu-item submenu observation.
        foreach (var item in _menuItemObservation.Keys.ToArray())
            if (!items.Contains(item)) UnwatchMenuItem(item);
        foreach (var item in items)
            if (!_menuItemObservation.ContainsKey(item)) WatchMenuItem(item);

        // Reconcile embedded-input validation observation.
        foreach (var box in _inputObservation.Keys.ToArray())
            if (!inputs.Contains(box)) UnwatchInput(box);
        foreach (var box in inputs)
            if (!_inputObservation.ContainsKey(box)) WatchInput(box);

        // Reconcile layout change watches on the owner and every authored descendant container so
        // add/remove/replace and direct-owner edits retrigger discovery.
        foreach (var (container, handler) in _layoutHandlers.ToArray())
        {
            container.Layout.Children.CollectionChanged -= handler;
            _layoutHandlers.Remove((container, handler));
        }
        foreach (var container in containers)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => RefreshSubtree();
            container.Layout.Children.CollectionChanged += handler;
            _layoutHandlers.Add((container, handler));
        }

        RecomputeAll();
    }

    private void WatchMenu(CuiMenu menu)
    {
        // An open menu alone is not proof of focus membership; each event just re-derives the
        // exact membership from the actual focused element.
        EventHandler<RoutedEventArgs> opened = (_, _) => Recompute();
        EventHandler<RoutedEventArgs> closed = (_, _) => Recompute();
        _openedHandlers[menu] = opened;
        _closedHandlers[menu] = closed;
        menu.Opened += opened;
        menu.Closed += closed;
    }

    private void UnwatchMenu(CuiMenu menu)
    {
        if (_openedHandlers.Remove(menu, out var opened) && opened is not null) menu.Opened -= opened;
        if (_closedHandlers.Remove(menu, out var closed) && closed is not null) menu.Closed -= closed;
    }

    private void WatchMenuItem(MenuItem item)
    {
        EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
        {
            if (change.Property == MenuItem.IsSubMenuOpenProperty) Recompute();
        };
        _menuItemObservation[item] = handler;
        item.PropertyChanged += handler;
    }

    private void UnwatchMenuItem(MenuItem item)
    {
        if (_menuItemObservation.Remove(item, out var handler) && handler is not null) item.PropertyChanged -= handler;
    }

    private void WatchInput(TextBox box)
    {
        EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
        {
            if (change.Property == CuiInputValidationProperties.HasErrorProperty) RecomputeValidation();
        };
        _inputObservation[box] = handler;
        box.PropertyChanged += handler;
    }

    private void UnwatchInput(TextBox box)
    {
        if (_inputObservation.Remove(box, out var handler) && handler is not null) box.PropertyChanged -= handler;
    }

    /// <summary>
    /// Exact focus membership: the actual keyboard-focused element lives either inside the owner's
    /// visual subtree or inside one of this scope's owned menus' open logical popups (a separately
    /// hosted top-level whose visuals are not descendants of the owner). Logical ancestry from the
    /// focused element reaches the owning authored Menu/MenuItem for popup content.
    /// </summary>
    private bool IsInScope(IInputElement? element)
    {
        if (element is not Visual visual) return false;
        if (_owner.IsVisualAncestorOf(visual)) return true;
        for (ILogical? node = visual; node is not null; node = node.LogicalParent)
        {
            if (node is MenuItem item && _menuItemObservation.ContainsKey(item)) return true;
        }
        return false;
    }

    private void Recompute()
    {
        if (_disposed) return;
        var focused = IsInScope(_topLevel?.FocusManager?.GetFocusedElement());
        if (focused == _focused) return;
        _focused = focused;
        FocusChanged?.Invoke(this, focused);
    }

    private void RecomputeValidation()
    {
        if (_disposed) return;
        var invalid = false;
        foreach (var box in _inputObservation.Keys)
        {
            if (CuiInputValidationProperties.GetHasError(box)) { invalid = true; break; }
        }
        if (invalid == _hasValidationError) return;
        _hasValidationError = invalid;
        ValidationChanged?.Invoke(this, invalid);
    }

    private void RecomputeAll()
    {
        RecomputeValidation();
        Recompute();
    }

    /// <summary>Permanently stop all observation.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireTopLevel();
        if (_attached)
        {
            _owner.AttachedToVisualTree -= OnAttachedToVisualTree;
            _owner.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        }
        foreach (var menu in _openedHandlers.Keys.ToArray()) UnwatchMenu(menu);
        foreach (var item in _menuItemObservation.Keys.ToArray()) UnwatchMenuItem(item);
        foreach (var box in _inputObservation.Keys.ToArray()) UnwatchInput(box);
        foreach (var (container, handler) in _layoutHandlers.ToArray())
        {
            container.Layout.Children.CollectionChanged -= handler;
            _layoutHandlers.Remove((container, handler));
        }
        _focused = false;
        _hasValidationError = false;
    }
}
