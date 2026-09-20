using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Haven.UI;
using Haven.UI.Components;
using HuiPage = Haven.UI.Components.Page;

namespace CakeOS.Hui.Renderer;

/// <summary>Pointer kind for viewport gesture arbitration (mirrors HUI kinds).</summary>
public enum HuiSurfacePointerKind
{
    Mouse,
    Pen,
    Touch,
}

/// <summary>Raw pointer sample forwarded to application gesture handling.</summary>
public readonly record struct HuiSurfacePointer(double X, double Y);

/// <summary>Pointer event details for application gesture handling.</summary>
public sealed class HuiSurfacePointerEvent
{
    public HuiSurfacePointerEvent(
        HuiSurfacePointerKind kind,
        HuiSurfacePointer position,
        bool leftButton,
        bool middleButton,
        bool rightButton,
        float pressure,
        float tiltX,
        float tiltY,
        double wheelDeltaY)
    {
        Kind = kind;
        Position = position;
        LeftButton = leftButton;
        MiddleButton = middleButton;
        RightButton = rightButton;
        Pressure = pressure;
        TiltX = tiltX;
        TiltY = tiltY;
        WheelDeltaY = wheelDeltaY;
    }

    public HuiSurfacePointerKind Kind { get; }
    public HuiSurfacePointer Position { get; }
    public bool LeftButton { get; }
    public bool MiddleButton { get; }
    public bool RightButton { get; }
    public float Pressure { get; }
    public float TiltX { get; }
    public float TiltY { get; }
    public double WheelDeltaY { get; }
}

/// <summary>Services owned by the hosting application.</summary>
public sealed class HuiSurfaceServices
{
    public HuiSurfaceServices(
        HuiBackendServices backend,
        Func<HuiSurfacePointerEvent, bool>? pointerPressed = null,
        Func<HuiSurfacePointerEvent, bool>? pointerMoved = null,
        Func<HuiSurfacePointerEvent, bool>? pointerReleased = null,
        Func<HavenKey, bool, bool>? keyDown = null,
        Func<HavenKey, bool>? keyUp = null)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        PointerPressed = pointerPressed;
        PointerMoved = pointerMoved;
        PointerReleased = pointerReleased;
        KeyDown = keyDown;
        KeyUp = keyUp;
    }

    public HuiBackendServices Backend { get; }
    public Func<HuiSurfacePointerEvent, bool>? PointerPressed { get; }
    public Func<HuiSurfacePointerEvent, bool>? PointerMoved { get; }
    public Func<HuiSurfacePointerEvent, bool>? PointerReleased { get; }
    public Func<HavenKey, bool, bool>? KeyDown { get; }
    public Func<HavenKey, bool>? KeyUp { get; }
}

/// <summary>
/// Generic HUI application surface. Owns layout, shared rendering, HUI input
/// routing and raw pointer forwarding. Application gesture logic (e.g. the
/// Canvas viewport) lives in callbacks — never in this file.
/// </summary>
public sealed class HuiAppSurface : Control, IHavenMeasureContext, IDisposable
{
    private readonly HuiPage _root;
    private readonly HavenLayoutEngine _layout = new();
    private readonly HavenSceneRenderer _renderer = new();
    private readonly HavenInputRouter _input;
    private readonly HuiSurfaceServices _services;
    private readonly HavenPlatform _platform;
    private bool _disposed;

    public HuiAppSurface(HuiPage root, HuiSurfaceServices services, HavenPlatform platform)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _platform = platform;
        Focusable = true;
        ClipToBounds = true;
        _input = new HavenInputRouter(_root);
    }

    public HuiPage Root => _root;
    public HavenInputRouter Input => _input;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1100d;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 760d;
        _layout.Layout(_root, new HavenSize(width, height), _platform, this);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _layout.Layout(_root, new HavenSize(finalSize.Width, finalSize.Height), _platform, this);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        HuiAvaloniaRenderer.Render(context, _renderer.Render(_root), _services.Backend);
    }

    public void RerunLayout()
    {
        _layout.Layout(_root, new HavenSize(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)), _platform, this);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _input.PointerMoved(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type));
        if (_services.PointerMoved?.Invoke(Describe(e, p, 0)) == true)
            e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        _input.PointerPressed(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type), HavenPointerButton.Primary);
        if (_services.PointerPressed?.Invoke(Describe(e, p, 0)) == true)
            e.Handled = true;
        Focus();
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        _input.PointerReleased(new HavenPoint(p.X, p.Y));
        if (_services.PointerReleased?.Invoke(Describe(e, p, 0)) == true)
            e.Handled = true;
        e.Pointer.Capture(null);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        if (_services.PointerMoved?.Invoke(Describe(e, p, e.Delta.Y)) == true)
            e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var key = MapKey(e.Key);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (key != HavenKey.Unknown && _input.KeyDown(key))
        {
            e.Handled = true;
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }
        if (key != HavenKey.Unknown && _services.KeyDown?.Invoke(key, ctrl) == true)
        {
            e.Handled = true;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        var key = MapKey(e.Key);
        if (key != HavenKey.Unknown && _input.KeyUp(key))
        {
            e.Handled = true;
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }
        if (key != HavenKey.Unknown && _services.KeyUp?.Invoke(key) == true)
        {
            e.Handled = true;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public HavenSize MeasureLeaf(HavenElement element, HavenSize available) =>
        HuiMeasure.Leaf(element, available, _services.Backend.Theme);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private HuiSurfacePointerEvent Describe(PointerEventArgs e, Point p, double wheelDeltaY)
    {
        var props = e.GetCurrentPoint(this).Properties;
        return new HuiSurfacePointerEvent(
            e.Pointer.Type switch
            {
                PointerType.Touch => HuiSurfacePointerKind.Touch,
                PointerType.Pen => HuiSurfacePointerKind.Pen,
                _ => HuiSurfacePointerKind.Mouse,
            },
            new HuiSurfacePointer(p.X, p.Y),
            props.IsLeftButtonPressed,
            props.IsMiddleButtonPressed,
            props.IsRightButtonPressed,
            props.Pressure,
            props.XTilt,
            props.YTilt,
            wheelDeltaY);
    }

    private static HavenPointerKind PointerKind(PointerType type) => type switch
    {
        PointerType.Touch => HavenPointerKind.Touch,
        PointerType.Pen => HavenPointerKind.Pen,
        _ => HavenPointerKind.Mouse,
    };

    private static HavenKey MapKey(Key key) => key switch
    {
        Key.Enter => HavenKey.Enter,
        Key.Space => HavenKey.Space,
        Key.Tab => HavenKey.Tab,
        Key.Escape => HavenKey.Escape,
        Key.Left => HavenKey.Left,
        Key.Right => HavenKey.Right,
        Key.Up => HavenKey.Up,
        Key.Down => HavenKey.Down,
        Key.Home => HavenKey.Home,
        Key.End => HavenKey.End,
        Key.Back => HavenKey.Backspace,
        Key.Delete => HavenKey.Delete,
        Key.A => HavenKey.A,
        Key.B => HavenKey.B,
        Key.C => HavenKey.C,
        Key.D => HavenKey.D,
        Key.E => HavenKey.E,
        Key.F => HavenKey.F,
        Key.G => HavenKey.G,
        Key.H => HavenKey.H,
        Key.I => HavenKey.I,
        Key.J => HavenKey.J,
        Key.K => HavenKey.K,
        Key.L => HavenKey.L,
        Key.M => HavenKey.M,
        Key.N => HavenKey.N,
        Key.O => HavenKey.O,
        Key.P => HavenKey.P,
        Key.Q => HavenKey.Q,
        Key.R => HavenKey.R,
        Key.S => HavenKey.S,
        Key.T => HavenKey.T,
        Key.U => HavenKey.U,
        Key.V => HavenKey.V,
        Key.W => HavenKey.W,
        Key.X => HavenKey.X,
        Key.Y => HavenKey.Y,
        Key.Z => HavenKey.Z,
        _ => HavenKey.Unknown,
    };
}
