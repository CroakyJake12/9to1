using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace HavenOS.Apps.Browse;

/// <summary>Raw pointer gesture forwarded from the engine surface to CDP. The X/Y
/// coordinates and wheel deltas are already scaled to the render viewport.</summary>
public sealed record ChromiumMouseArgs(string Type, double X, double Y, string? Button, int Buttons, int ClickCount, double DeltaX = 0, double DeltaY = 0);

/// <summary>Raw key gesture forwarded from the engine surface to CDP. The text
/// carries the genuine platform symbol (layout/shift-aware) for printable keys and
/// is null for control/navigation keys; modifiers is the CDP bitmask.</summary>
public sealed record ChromiumKeyArgs(string Type, string Key, string Code, bool AutoRepeat, int VirtualKeyCode, string? Text = null, int Modifiers = 0);

/// <summary>Raw text input forwarded from the engine surface to CDP.</summary>
public sealed record ChromiumTextArgs(string Text);

/// <summary>A low-level Chromium engine view contract used by the Browse tab. The
/// production implementation is an approved-Avalonia control; tests may supply a
/// lightweight fake so engine logic runs without a UI host.</summary>
public interface IChromiumEngineView
{
    Control View { get; }
    void PresentFrame(byte[] jpeg);
}

/// <summary>UI-thread state: input uses only the viewport of the displayed, decoded frame.</summary>
internal sealed class ChromiumViewportFrameState
{
    internal ChromiumViewport Requested { get; private set; } = ChromiumViewport.Default;
    internal ChromiumViewport Acknowledged { get; private set; } = ChromiumViewport.Default;
    internal ChromiumViewport Displayed { get; private set; } = ChromiumViewport.Default;
    internal bool CanAcceptInput { get; private set; }
    internal void Request(ChromiumViewport value) { Requested = value; CanAcceptInput = false; }
    internal void Acknowledge(ChromiumViewport value) { Acknowledged = value; CanAcceptInput = false; }
    internal void Publish(ChromiumViewport? value)
    {
        CanAcceptInput = value is not null && value == Requested && value == Acknowledged;
        if (CanAcceptInput) Displayed = value!;
    }
}


/// <summary>
/// The low-level interactive Chromium engine surface: an approved-Avalonia control
/// that displays Page.startScreencast JPEG frames and routes genuine pointer/key/
/// text input back to the renderer through CDP. Frames are coalesced to the latest
/// one with a single bounded UI post, superseded bitmaps are disposed, pointer
/// coordinates are scaled from the control to the render viewport, and the control
/// is focusable so keyboard input reaches the page. It is rendering/process/input
/// infrastructure only — CUI owns every surrounding browser control.
/// </summary>
public sealed class ChromiumEngineSurface : Control, IChromiumEngineView
{
    // The single logical render viewport shared by the CSS viewport emulation, the
    // screencast capture constraint, and this surface's pointer/key coordinate
    // mapping. They must all agree so the displayed bitmap and the input space are
    // consistent (Browser15).
    internal const int RenderWidth = 1280;
    internal const int RenderHeight = 800;

    private ChromiumViewport _requestedViewport = ChromiumViewport.Default;
    private readonly ChromiumViewportFrameState _frameViewport = new();
    private readonly HashSet<string> _pressedMouseButtons = new(StringComparer.Ordinal);
    private TopLevel? _topLevel;
    internal ChromiumViewport RequestedViewport => Volatile.Read(ref _requestedViewport);
    internal event EventHandler<ChromiumViewport>? ViewportChanged;
    internal void ApplyViewport(ChromiumViewport value)
    {
        Dispatcher.UIThread.VerifyAccess();
        _frameViewport.Acknowledge(value);
        InvalidateVisual();
    }
    /// <summary>Resolve the owning top level lazily. It can be unavailable at the moment of attach -
    /// the surface is often attached inside a template before its root window is established - and in
    /// that case the old code fell through to a render scale of 1 forever: ScalingChanged is only
    /// subscribed once a top level exists, and if the user never changes scaling nothing ever corrected
    /// it. Measured owner symptom on 29 October: the web surface "not scaling properly to the correct
    /// resolution" - a 1x page composited into a scaled window reads as soft, wrongly sized text.</summary>
    private void EnsureTopLevel()
    {
        if (_topLevel is not null) return;
        var actual = TopLevel.GetTopLevel(this);
        if (actual is null) return;
        _topLevel = actual;
        actual.ScalingChanged += OnScalingChanged;
        EmitDiagnostic("surface top level resolved scaling=" + actual.RenderScaling);
        UpdateViewport();
    }

    private void UpdateViewport()
    {
        EnsureTopLevel();
        if (!_attached || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var next = ChromiumViewport.From(Bounds.Size, _topLevel?.RenderScaling ?? 1);
        if (next == _requestedViewport) return;
        Volatile.Write(ref _requestedViewport, next);
        _frameViewport.Request(next);
        ViewportChanged?.Invoke(this, next);
    }
    private void OnScalingChanged(object? sender, EventArgs args) => UpdateViewport();
    private void OnLayoutRetryTopLevel(object? sender, EventArgs args)
    {
        if (_topLevel is not null) { LayoutUpdated -= OnLayoutRetryTopLevel; return; }
        EnsureTopLevel();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) UpdateViewport();
    }

    private readonly object _gate = new();
    private readonly HashSet<Key> _pressedKeys = new();
    private IImage? _current;
    private sealed record PendingFrame(byte[] Jpeg, ChromiumViewport? Viewport);
    private volatile PendingFrame? _latestFrame;
    private volatile int _frameQueued;
    private int _framesReceived, _framesDecoded;
    private bool _attached;
    private bool _reportedReceived, _reportedDecoded, _reportedAttached, _reportedDetached;
    private bool _reportedEmptyRender, _reportedImageRender;

    /// <summary>Optional per-instance diagnostic sink (default: guarded Console.Error).
    /// Emits bounded, payload-free surface lifecycle facts (frame counts/sizes, decode,
    /// render/attach state) so a white-viewport stall can be pinpointed without leaking
    /// image content.</summary>
    internal Action<string>? DiagnosticSink;

    private void EmitDiagnostic(string line)
    {
        try { if (DiagnosticSink is not null) DiagnosticSink(line); else Console.Error.WriteLine(line); }
        catch { /* diagnostics are best-effort and never fatal */ }
    }

    public ChromiumEngineSurface() => Focusable = true;

    public Control View => this;

    /// <summary>Raised for every accepted screencast frame (JPEG bytes).</summary>
    public event Action<byte[]>? FramePresented;

    /// <summary>Raised for pointer pressed/moved/released/wheel gestures.</summary>
    public event EventHandler<ChromiumMouseArgs>? MouseGesture;

    /// <summary>Raised for keyboard down/up gestures.</summary>
    public event EventHandler<ChromiumKeyArgs>? KeyGesture;

    /// <summary>Raised for composed text input.</summary>
    public event EventHandler<ChromiumTextArgs>? TextGesture;

    /// <summary>Feeds the next screencast JPEG frame to the surface. Only the
    /// latest frame is ever published; a single bounded UI drain coalesces the
    /// queue so notifications never accumulate unboundedly.</summary>
    public void PresentFrame(byte[] jpeg) => PresentFrame(jpeg, null);

    internal void PresentFrame(byte[] jpeg, ChromiumViewport? viewport)
    {
        if (jpeg is null || jpeg.Length == 0) return;
        FramePresented?.Invoke(jpeg);
        var received = Interlocked.Increment(ref _framesReceived);
        if (!_reportedReceived)
        {
            _reportedReceived = true;
            EmitDiagnostic($"surface received bytes={jpeg.Length} frames={received} attached={_attached}");
        }
        _latestFrame = new(jpeg, viewport);
        if (Interlocked.CompareExchange(ref _frameQueued, 1, 0) == 0)
            PostDrain();
    }

    private void PostDrain()
    {
        try { Dispatcher.UIThread.Post(DrainFrames); }
        catch { Interlocked.Exchange(ref _frameQueued, 0); }
    }

    private void DrainFrames()
    {
        PendingFrame? frame;
        try
        {
            while ((frame = Interlocked.Exchange(ref _latestFrame, null)) is not null)
                PublishFrame(frame);
        }
        finally
        {
            Interlocked.Exchange(ref _frameQueued, 0);
            // A frame that arrived between the loop exit and clearing the busy flag
            // would otherwise be stranded until the next frame. Repost so it is
            // always drained (closes the lost-wakeup window).
            if (_latestFrame is not null && Interlocked.CompareExchange(ref _frameQueued, 1, 0) == 0)
                PostDrain();
        }
    }

    private void PublishFrame(PendingFrame frame)
    {
        IImage? image;
        try
        {
            using var stream = new MemoryStream(frame.Jpeg, writable: false);
            image = new Bitmap(stream);
        }
        catch (Exception)
        {
            if (!_reportedDecoded) { _reportedDecoded = true; EmitDiagnostic("surface decode failed"); }
            return; // a corrupt frame is skipped, never fatal
        }

        var decoded = Interlocked.Increment(ref _framesDecoded);
        if (!_reportedDecoded)
        {
            _reportedDecoded = true;
            EmitDiagnostic($"surface decoded frames={decoded} width={image.Size.Width} height={image.Size.Height}");
        }

        IImage? previous;
        lock (_gate) { previous = _current; _current = image; }
        _frameViewport.Publish(frame.Viewport);
        (previous as IDisposable)?.Dispose();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        IImage? image;
        lock (_gate) image = _current;
        if (image is null)
        {
            if (!_reportedEmptyRender)
            {
                _reportedEmptyRender = true;
                EmitDiagnostic($"surface render empty boundsW={Bounds.Width} boundsH={Bounds.Height} attached={_attached}");
            }
            context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
            return;
        }
        if (!_reportedImageRender)
        {
            _reportedImageRender = true;
            EmitDiagnostic($"surface render image width={image.Size.Width} height={image.Size.Height} boundsW={Bounds.Width} boundsH={Bounds.Height} attached={_attached}");
        }
        context.DrawImage(image, new Rect(image.Size), ChromiumViewport.Fit(image.Size, Bounds.Size));
    }

    private (double X, double Y) Scale(Point p)
    {
        var mapped = _frameViewport.Displayed.MapPoint(p, Bounds.Size);
        return (mapped.X, mapped.Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_frameViewport.CanAcceptInput) { e.Handled = true; return; }
        Focus();
        var (button, buttons) = ResolveButton(e);
        var (x, y) = Scale(e.GetPosition(this));
        if (button is not null) _pressedMouseButtons.Add(button);
        MouseGesture?.Invoke(this, new ChromiumMouseArgs("mousePressed", x, y, button, buttons, 1));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var (button, buttons) = ResolveButton(e);
        if (button is null || !_pressedMouseButtons.Remove(button)) { e.Handled = true; return; }
        // Finish an admitted press even while resizing, outside page content so the
        // stale image cannot turn its release into a click on a different element.
        var (x, y) = _frameViewport.CanAcceptInput ? Scale(e.GetPosition(this)) : (-1d, -1d);
        buttons = _pressedMouseButtons.Sum(value => value == "right" ? 2 : value == "middle" ? 4 : 1);
        MouseGesture?.Invoke(this, new ChromiumMouseArgs("mouseReleased", x, y, button, buttons, 1));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_frameViewport.CanAcceptInput) { e.Handled = true; return; }
        var buttons = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed ? 1 : 0;
        var (x, y) = Scale(e.GetPosition(this));
        MouseGesture?.Invoke(this, new ChromiumMouseArgs("mouseMoved", x, y, null, buttons, 0));
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!_frameViewport.CanAcceptInput) { e.Handled = true; return; }
        var (x, y) = Scale(e.GetPosition(this));
        var delta = ChromiumViewport.WheelPixels(e.Delta, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        MouseGesture?.Invoke(this, new ChromiumMouseArgs("mouseWheel", x, y, null, 0, 0, delta.X, delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_frameViewport.CanAcceptInput) { e.Handled = true; return; }
        // The approved fork has no IsRepeat property; derive auto-repeat from the
        // set of still-pressed keys so key repeats are still reported genuinely.
        var autoRepeat = !_pressedKeys.Add(e.Key);
        var domKey = ChromiumKeyMap.ToDomKey(e.Key);
        var domCode = ChromiumKeyMap.ToDomCode(e.PhysicalKey);
        var vk = ChromiumKeyMap.ToWindowsVirtualKeyCode(e.Key);
        var modifiers = ChromiumKeyMap.ToCdpModifiers(e.KeyModifiers);
        // The genuine keyDown text (Enter = CR for the default action, printable keys =
        // the native symbol, everything else = null, no shortcut text). Never synthesize
        // a character from the raw key.
        var text = ChromiumKeyMap.ToKeyDownText(e.Key, e.KeySymbol, modifiers);
        KeyGesture?.Invoke(this, new ChromiumKeyArgs("keyDown", domKey, domCode, autoRepeat, vk, text, modifiers));
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!_pressedKeys.Remove(e.Key)) { e.Handled = true; return; }
        var domKey = ChromiumKeyMap.ToDomKey(e.Key);
        var domCode = ChromiumKeyMap.ToDomCode(e.PhysicalKey);
        var vk = ChromiumKeyMap.ToWindowsVirtualKeyCode(e.Key);
        var modifiers = ChromiumKeyMap.ToCdpModifiers(e.KeyModifiers);
        KeyGesture?.Invoke(this, new ChromiumKeyArgs("keyUp", domKey, domCode, false, vk, null, modifiers));
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!_frameViewport.CanAcceptInput) { e.Handled = true; return; }
        if (!string.IsNullOrEmpty(e.Text))
        {
            TextGesture?.Invoke(this, new ChromiumTextArgs(e.Text));
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
        UpdateViewport();
        // Bounds are not measured until the first layout after attach, so the same lazy resolution
        // runs again on the next layout pass rather than being latched at attach time.
        LayoutUpdated += OnLayoutRetryTopLevel;
        if (!_reportedAttached) { _reportedAttached = true; EmitDiagnostic("surface attached"); }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        LayoutUpdated -= OnLayoutRetryTopLevel;
        if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        if (!_reportedDetached) { _reportedDetached = true; EmitDiagnostic("surface detached"); }
        IImage? current;
        lock (_gate) { current = _current; _current = null; }
        (current as IDisposable)?.Dispose();
    }

    private (string? Button, int Buttons) ResolveButton(PointerEventArgs e)
    {
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        return kind switch
        {
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => ("right", 2),
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => ("middle", 4),
            _ => ("left", 1)
        };
    }
}
