using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>Shared presentation state for an authored action strip. It retains
/// measurement, control identity, automation and keyboard tab stops; it owns no
/// action, focus transfer, conversation state or permission decision.</summary>
internal sealed class CuiInteractionReveal : IDisposable
{
    private const string Overlay = "CUI interaction reveal";
    private readonly Control _target;
    private readonly Control _owner;
    private TopLevel? _inputRoot;
    private bool _touchLatched;
    private bool _disposed;

    internal CuiInteractionReveal(Control target, Control owner)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(owner);
        if (ReferenceEquals(target, owner)) throw new ArgumentException("An interaction reveal owner must be an ancestor.", nameof(owner));
        _target = target;
        _owner = owner;
        owner.PropertyChanged += OwnerChanged;
        owner.AttachedToVisualTree += Attached;
        owner.DetachedFromVisualTree += Detached;
        owner.AddHandler(InputElement.PointerEnteredEvent, PointerEntered, RoutingStrategies.Direct);
        ConnectInputRoot();
        Update();
    }

    internal Control Owner => _owner;
    internal bool IsRevealed { get; private set; }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == InputElement.IsPointerOverProperty || change.Property == InputElement.IsKeyboardFocusWithinProperty)
            Update();
    }
    private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
    { ConnectInputRoot(); Update(); }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
    { DisconnectInputRoot(); _touchLatched = false; Update(); }
    private void ConnectInputRoot()
    {
        var root = TopLevel.GetTopLevel(_owner);
        if (ReferenceEquals(root, _inputRoot)) return;
        DisconnectInputRoot();
        _inputRoot = root;
        root?.AddHandler(InputElement.PointerPressedEvent, PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
    private void DisconnectInputRoot()
    {
        _inputRoot?.RemoveHandler(InputElement.PointerPressedEvent, PointerPressed);
        _inputRoot = null;
    }
    private void PointerEntered(object? sender, PointerEventArgs args)
    {
        if (args.Pointer.Type == PointerType.Mouse) _touchLatched = false;
        Update();
    }
    private void PointerPressed(object? sender, PointerPressedEventArgs args)
    {
        var source = args.Source as Visual;
        var inside = ReferenceEquals(source, _owner) || source is not null && _owner.IsVisualAncestorOf(source);
        if (args.Pointer.Type == PointerType.Touch && inside) _touchLatched = true;
        else if (!inside) _touchLatched = false;
        Update();
    }
    private void Update()
    {
        if (_disposed) return;
        IsRevealed = _touchLatched || _owner.IsPointerOver || _owner.IsKeyboardFocusWithin;
        // These are CUI overlay values, not host selectors or local-value
        // overrides. Removing this source restores the authored/themed state.
        if (IsRevealed)
            CuiPropertyPresentation.For(_target).RemoveSource(CuiPropertyLevel.Overlay, Overlay);
        else
        {
            CuiPropertyPresentation.Set(_target, "Opacity", 0d, CuiPropertyLevel.Overlay, Overlay, -90);
            CuiPropertyPresentation.Set(_target, "IsHitTestVisible", false, CuiPropertyLevel.Overlay, Overlay, -90);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectInputRoot();
        _owner.PropertyChanged -= OwnerChanged;
        _owner.AttachedToVisualTree -= Attached;
        _owner.DetachedFromVisualTree -= Detached;
        _owner.RemoveHandler(InputElement.PointerEnteredEvent, PointerEntered);
        CuiPropertyPresentation.For(_target).RemoveSource(CuiPropertyLevel.Overlay, Overlay);
    }
}
