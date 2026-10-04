using CakeOS.Hui.Renderer;
using Haven.UI;
using Haven.UI.Components;

namespace CakeOS.Canvas.App;

/// <summary>
/// The real Canvas application as a platform-neutral HUI tree. No Avalonia
/// references: layout, components, tokens and input all flow through HUI and
/// the shared renderer. Hosts mount <see cref="Page"/>, forward pointer and
/// shortcut events, decode the current frame SVG, and implement
/// <see cref="ICanvasFileDialogs"/>.
/// </summary>
public sealed class CanvasRoot : IDisposable
{
    public const string ViewportImageSource = "canvas-frame";

    private readonly CanvasController _controller;
    private readonly ICanvasFileDialogs _dialogs;
    private readonly string _documentsDir;
    private readonly Page _page;
    private Text _titleText = null!;
    private readonly Dictionary<CanvasTool, Button> _toolButtons = new();
    private readonly Dictionary<CanvasShape, Button> _shapeButtons = new();
    private readonly Dictionary<string, Button> _swatches = new();
    private Slider _widthSlider = null!;
    private Text _widthValue = null!;
    private Container _shapeRow = null!;
    private Container _optionsRow = null!;
    private Toggle _eraserSplit = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Text _docText = null!;
    private Text _statusText = null!;
    private Text _zoomText = null!;
    private Slider _zoomSlider = null!;
    private Image _viewport = null!;
    private bool _updating;
    private bool _disposed;

    public CanvasRoot(CanvasController controller, ICanvasFileDialogs dialogs, string documentsDir)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsDir);
        _documentsDir = documentsDir;

        _page = new Page { Name = "CanvasRoot", Layout = HavenLayout.Grid };
        _page.SetValue(HavenProperties.Background, "Background");
        _page.Rows = "Auto Auto Auto 1fr Auto";

        var header = BuildHeader();
        header.SetValue(HavenProperties.Row, 0);
        var toolbar = BuildToolbar();
        toolbar.SetValue(HavenProperties.Row, 1);
        _optionsRow = BuildOptionsRow();
        _optionsRow.SetValue(HavenProperties.Row, 2);
        _viewport = new Image { Name = "CanvasViewport", Source = ViewportImageSource, Fit = HavenImageFit.Fill };
        _viewport.SetValue(HavenProperties.Row, 3);
        _viewport.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _viewport.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _viewport.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        var status = BuildStatusBar();
        status.SetValue(HavenProperties.Row, 4);

        _page.Add(header);
        _page.Add(toolbar);
        _page.Add(_optionsRow);
        _page.Add(_viewport);
        _page.Add(status);

        _controller.StateChanged += Refresh;
        Refresh();

        if (Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_OPEN_AT_START") == "1")
            OpenAtStart();
    }

    public Page Page => _page;
    public CanvasController Controller => _controller;
    public Image Viewport => _viewport;

    public event Action? Changed;
    public event Action? TitleChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.StateChanged -= Refresh;
        _controller.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Input (called by hosts)

    private HavenRect ViewportFrame(out double w, out double h)
    {
        var b = _viewport.Bounds;
        w = Math.Max(1d, b.Width);
        h = Math.Max(1d, b.Height);
        return new HavenRect(b.X, b.Y, w, h);
    }

    /// <summary>Route a raw pointer press; returns true when the viewport consumed it.</summary>
    public bool OnPointerPressed(HuiSurfacePointerEvent ptr)
    {
        var bounds = ViewportFrame(out var w, out var h);
        var inside = ptr.Position.X >= bounds.X && ptr.Position.X <= bounds.X + w
            && ptr.Position.Y >= bounds.Y && ptr.Position.Y <= bounds.Y;
        var lx = ptr.Position.X - bounds.X;
        var ly = ptr.Position.Y - bounds.Y;
        if (IsPanGesture(ptr))
            return _controller.BeginPanAt(lx, ly);
        return _controller.BeginStrokeAt(ToKind(ptr.Kind), lx, ly, ClampedPressure(ptr),
            w, h, _controller.LastBounds, inside, ptr.TiltX, ptr.TiltY);
    }

    public bool OnPointerMoved(HuiSurfacePointerEvent ptr)
    {
        var bounds = ViewportFrame(out var w, out var h);
        var lx = ptr.Position.X - bounds.X;
        var ly = ptr.Position.Y - bounds.Y;
        if (ptr.WheelDeltaY != 0)
        {
            _controller.ZoomAtPoint(lx, ly, ptr.WheelDeltaY, w, h, _controller.LastBounds);
            return true;
        }
        if (_controller.StrokeActive)
        {
            _controller.UpdateStrokeAt(lx, ly, ClampedPressure(ptr), w, h, _controller.LastBounds, ptr.TiltX, ptr.TiltY);
            return true;
        }
        if (_controller.IsPanning)
        {
            _controller.UpdatePanTo(lx, ly, w, h, _controller.LastBounds);
            return true;
        }
        return false;
    }

    public bool OnPointerReleased(HuiSurfacePointerEvent ptr)
    {
        var bounds = ViewportFrame(out var w, out var h);
        var lx = ptr.Position.X - bounds.X;
        var ly = ptr.Position.Y - bounds.Y;
        if (_controller.StrokeActive)
            return _controller.EndStrokeAt(ToKind(ptr.Kind), lx, ly, ClampedPressure(ptr),
                w, h, _controller.LastBounds, ptr.TiltX, ptr.TiltY);
        if (_controller.IsPanning)
        {
            _controller.UpdatePanTo(lx, ly, w, h, _controller.LastBounds);
            _controller.EndPan(w, h, _controller.LastBounds);
            return true;
        }
        return false;
    }

    private static bool IsPanGesture(HuiSurfacePointerEvent ptr) =>
        ptr.Kind == HuiSurfacePointerKind.Touch || ptr.MiddleButton || ptr.RightButton;

    private static double ClampedPressure(HuiSurfacePointerEvent ptr) =>
        ptr.Kind == HuiSurfacePointerKind.Mouse || ptr.Pressure <= 0
            ? 0.5 : Math.Clamp(ptr.Pressure, 0f, 1f);

    private static CanvasPointerKind ToKind(HuiSurfacePointerKind kind) => kind switch
    {
        HuiSurfacePointerKind.Pen => CanvasPointerKind.Pen,
        HuiSurfacePointerKind.Touch => CanvasPointerKind.Touch,
        _ => CanvasPointerKind.Mouse,
    };

    /// <summary>App shortcuts after the input router declines the key.</summary>
    public bool TryShortcut(HavenKey key, bool ctrl)
    {
        if (!ctrl)
            return false;
        switch (key)
        {
            case HavenKey.Z: _controller.Undo(); RefreshFrame(); return true;
            case HavenKey.Y: _controller.Redo(); RefreshFrame(); return true;
            case HavenKey.S: _ = SaveAsync(); return true;
            case HavenKey.O: _ = OpenAsync(); return true;
            case HavenKey.N: _ = NewAsync(); return true;
            default: return false;
        }
    }

    /// <summary>Self-test driving the exact toolbar handlers (markers feed CI).</summary>
    public void RunSelfTest()
    {
        SelectTool(CanvasTool.Eraser);
        if (_controller.Tool != CanvasTool.Eraser)
            throw new InvalidOperationException("Toolbar did not select the eraser tool.");
        SelectTool(CanvasTool.Pen);
        if (_controller.Tool != CanvasTool.Pen)
            throw new InvalidOperationException("Toolbar did not restore the pen tool.");

        var bounds = ViewportFrame(out var w, out var h);
        if (!_controller.BeginStrokeAt(CanvasPointerKind.Mouse, w / 2d, h / 2d, 0.5, w, h, _controller.LastBounds, insideViewport: true))
            throw new InvalidOperationException("Self-test could not begin a stroke on the viewport.");
        if (!_controller.EndStrokeAt(CanvasPointerKind.Mouse, w / 2d + 60, h / 2d + 40, 0.5, w, h, _controller.LastBounds))
            throw new InvalidOperationException("Self-test could not commit a stroke on the viewport.");
        if (!_controller.CanUndo)
            throw new InvalidOperationException("Self-test expected an undoable stroke.");
        _controller.Undo();
        RefreshFrame();
        if (!_controller.CanRedo)
            throw new InvalidOperationException("Self-test undo did not expose redo history.");
        _controller.Redo();
        RefreshFrame();
        if (!_controller.CanUndo)
            throw new InvalidOperationException("Self-test redo did not restore undo history.");

        Console.WriteLine("CANVAS_RNOTE_HUI_TOOLBAR_READY pen=1 eraser=1 undo=1 redo=1");
        _controller.SetStatus("Ready");
    }

    /// <summary>Automation hook: save-on-close uses the exact Save path.</summary>
    public void SaveOnClose()
    {
        if (!_controller.IsDirty)
            return;
        try
        {
            if (!_controller.Save())
                _controller.SaveTo(DefaultDocumentPath());
        }
        catch
        {
            // Status already records the truthful failure; close proceeds.
        }
    }

    public async Task<bool> TryCloseAsync()
    {
        if (!_controller.IsDirty)
            return true;
        if (_controller.DocumentPath is not null)
        {
            try
            {
                _controller.Save();
                return true;
            }
            catch
            {
                return false;
            }
        }
        var path = await _dialogs.PickSaveRnoteAsync(_documentsDir, _controller.DocumentName, "Save Canvas document").ConfigureAwait(true);
        if (path is null)
            return false;
        try
        {
            _controller.SaveTo(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Document commands

    public async Task NewAsync()
    {
        if (_controller.IsDirty)
        {
            if (_controller.DocumentPath is not null)
            {
                try { _controller.Save(); }
                catch { return; }
            }
            else
            {
                var path = await _dialogs.PickSaveRnoteAsync(_documentsDir, _controller.DocumentName, "Save Canvas document").ConfigureAwait(true);
                if (path is null)
                    return;
                try { _controller.SaveTo(path); }
                catch { return; }
            }
        }
        _controller.NewDocument();
        _controller.ResetViewport();
        Refresh();
        Changed?.Invoke();
    }

    public async Task OpenAsync()
    {
        var path = await _dialogs.PickOpenRnoteAsync(_documentsDir, "Open Canvas document").ConfigureAwait(true);
        if (path is null)
            return;
        try
        {
            _controller.OpenDocument(path, File.ReadAllBytes(path));
            _controller.ResetViewport();
            RefreshFrame();
        }
        catch (Exception exception)
        {
            _controller.SetStatus($"Open failed: {exception.Message}");
        }
        Changed?.Invoke();
    }

    public async Task SaveAsync()
    {
        if (_controller.DocumentPath is not null)
        {
            try { _controller.Save(); }
            catch { /* status already truthful */ }
            return;
        }
        var path = await _dialogs.PickSaveRnoteAsync(_documentsDir, _controller.DocumentName, "Save Canvas document").ConfigureAwait(true);
        if (path is null)
            return;
        try { _controller.SaveTo(path); }
        catch { /* status already truthful */ }
        Changed?.Invoke();
    }

    public async Task SaveAsAsync()
    {
        var path = await _dialogs.PickSaveRnoteAsync(_documentsDir, _controller.DocumentName, "Save Canvas document as").ConfigureAwait(true);
        if (path is null)
            return;
        try { _controller.SaveTo(path); }
        catch { /* status already truthful */ }
        Changed?.Invoke();
    }

    #endregion

    private string DefaultDocumentPath() => Path.Combine(_documentsDir, "canvas.rnote");

    private void OpenAtStart()
    {
        var path = DefaultDocumentPath();
        try
        {
            if (!File.Exists(path))
            {
                _controller.SetStatus("No saved document yet");
                Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_REOPEN_EMPTY path={path}");
                return;
            }
            _controller.OpenDocument(path, File.ReadAllBytes(path));
            _controller.ResetViewport();
            var bounds = ViewportFrame(out var w, out var h);
            _controller.RefreshFrame(w, h);
        }
        catch (Exception exception)
        {
            _controller.SetStatus($"Reopen failed: {exception.Message}");
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_REOPEN_FAILED path={path} reason={exception.Message}");
        }
    }

    private void SelectTool(CanvasTool tool)
    {
        _controller.SelectTool(tool);
        RefreshFrame();
    }

    private void SelectShape(CanvasShape shape) => _controller.SelectShape(shape);

    private void Undo()
    {
        _controller.Undo();
        RefreshFrame();
    }

    private void Redo()
    {
        _controller.Redo();
        RefreshFrame();
    }

    private void RefreshFrame()
    {
        var bounds = ViewportFrame(out var w, out var h);
        _controller.RefreshFrame(w, h);
        Changed?.Invoke();
    }

    /// <summary>Re-render the frame for the current viewport size (host resize).</summary>
    public void RefreshViewportSize() => RefreshFrame();

    private void Refresh()
    {
        if (_disposed)
            return;
        _updating = true;
        try
        {
            _titleText.Content = _controller.DocumentName;
            foreach (var (tool, button) in _toolButtons)
            {
                var selected = _controller.Tool == tool;
                button.SetState(HavenElementState.Selected, selected);
                button.SetValue(HavenProperties.Background, selected ? "AccentMuted" : "Transparent");
                button.SetValue(HavenProperties.BorderColor, selected ? "Accent" : "Transparent");
                button.SetValue(HavenProperties.BorderWidth, HavenLength.Px(selected ? 1 : 0));
            }
            foreach (var (shape, button) in _shapeButtons)
            {
                var selected = _controller.Shape == shape;
                button.SetState(HavenElementState.Selected, selected);
                button.SetValue(HavenProperties.Background, selected ? "AccentMuted" : "Transparent");
                button.SetValue(HavenProperties.BorderColor, selected ? "Accent" : "Transparent");
                button.SetValue(HavenProperties.BorderWidth, HavenLength.Px(selected ? 1 : 0));
            }
            _shapeRow.SetValue(HavenProperties.Visibility,
                _controller.Tool == CanvasTool.Shape ? HavenVisibility.Visible : HavenVisibility.Collapsed);
            _eraserSplit.IsChecked = _controller.EraserStyle == CanvasEraserStyle.Split;
            _eraserSplit.SetValue(HavenProperties.Visibility,
                _controller.Tool == CanvasTool.Eraser ? HavenVisibility.Visible : HavenVisibility.Collapsed);
            _optionsRow.SetValue(HavenProperties.Visibility,
                _controller.Tool is CanvasTool.Pen or CanvasTool.Highlighter or CanvasTool.Shape or CanvasTool.Eraser
                    ? HavenVisibility.Visible : HavenVisibility.Collapsed);
            if (Math.Abs(_widthSlider.Value - _controller.CurrentWidth) > 0.001d)
                _widthSlider.Value = _controller.CurrentWidth;
            _widthValue.Content = $"{_controller.CurrentWidth:0.#}";
            // Direct SetValue bypasses the markup codec (which also flips state),
            // so mirror the PopupMenu pattern and set both explicitly.
            _undoButton.SetValue(HavenProperties.Enabled, _controller.CanUndo);
            _undoButton.SetState(HavenElementState.Disabled, !_controller.CanUndo);
            _redoButton.SetValue(HavenProperties.Enabled, _controller.CanRedo);
            _redoButton.SetState(HavenElementState.Disabled, !_controller.CanRedo);
            RefreshHistoryOnly();
        }
        finally
        {
            _updating = false;
        }
        TitleChanged?.Invoke();
        Changed?.Invoke();
    }

    private void RefreshHistoryOnly()
    {
        _docText.Content = _controller.IsDirty ? $"{_controller.DocumentName} •" : _controller.DocumentName;
        _statusText.Content = _controller.StatusText;
        _zoomText.Content = $"{_controller.Zoom:0.#}x";
        if (Math.Abs(_zoomSlider.Value - _controller.Zoom) > 0.001d)
            _zoomSlider.Value = _controller.Zoom;
    }

    #region Tree construction

    private Container BuildHeader()
    {
        var header = new Container { Name = "Canvas.Header", Layout = HavenLayout.Grid };
        header.Columns = "1fr Auto";
        header.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 12px 6px 12px"));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));

        _titleText = new Text { Name = "Canvas.Title", Content = _controller.DocumentName };
        _titleText.SetValue(HavenProperties.FontSize, 17d);
        _titleText.SetValue(HavenProperties.Foreground, "TextPrimary");
        _titleText.SetValue(HavenProperties.Column, 0);

        var actions = new Container { Name = "Canvas.HeaderActions", Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Column, 1);
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        _undoButton = ToolButton("undo", "Undo");
        _undoButton.Invoked += (_, _) => Undo();
        _redoButton = ToolButton("redo", "Redo");
        _redoButton.Invoked += (_, _) => Redo();
        var menu = ToolButton("more", "Document menu");
        menu.Invoked += (_, _) => ShowDocumentMenu(menu);

        actions.Add(_undoButton);
        actions.Add(_redoButton);
        actions.Add(menu);
        header.Add(_titleText);
        header.Add(actions);
        return header;
    }

    private void ShowDocumentMenu(Button anchor)
    {
        var menu = new PopupMenu(anchor, _page, new List<PopupMenuItem>
        {
            new("New", () => _ = NewAsync(), IconKey: "file"),
            new("Open…", () => _ = OpenAsync(), IconKey: "folder-open"),
            new("Save", () => _ = SaveAsync(), IconKey: "save"),
            new("Save As…", () => _ = SaveAsAsync(), IconKey: "save"),
        });
        _page.Add(menu);
        Changed?.Invoke();
    }

    private Container BuildToolbar()
    {
        var bar = new Container { Name = "Canvas.Toolbar", Layout = HavenLayout.Wrap };
        bar.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 12px"));
        bar.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        AddTool(bar, CanvasTool.Pen, "pen", "Pen");
        AddTool(bar, CanvasTool.Highlighter, "highlighter", "Highlighter");
        AddTool(bar, CanvasTool.Eraser, "eraser", "Eraser");
        AddTool(bar, CanvasTool.Selector, "select", "Select");
        AddTool(bar, CanvasTool.Shape, "shape", "Shape");

        var separator = new Separator { Orientation = SeparatorOrientation.Vertical };
        separator.SetValue(HavenProperties.Width, HavenLength.Px(1));
        separator.SetValue(HavenProperties.Height, HavenLength.Px(26));
        bar.Add(separator);

        foreach (var preset in CanvasController.Palette)
            bar.Add(Swatch(preset));

        return bar;
    }

    private void AddTool(Container bar, CanvasTool tool, string iconKey, string name)
    {
        var button = ToolButton(iconKey, name);
        button.Invoked += (_, _) => SelectTool(tool);
        _toolButtons[tool] = button;
        bar.Add(button);
    }

    private static Button ToolButton(string iconKey, string name)
    {
        var button = new Button { Name = $"Canvas.Tool.{name}", Variant = ButtonVariant.Ghost };
        button.IconKey = iconKey;
        button.Accessibility.AccessibleName = name;
        button.SetValue(HavenProperties.Width, HavenLength.Px(36));
        button.SetValue(HavenProperties.Height, HavenLength.Px(34));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(0));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(9)));
        return button;
    }

    private Button Swatch(CanvasRgba color)
    {
        var button = new Button { Name = "Canvas.Swatch", Variant = ButtonVariant.Ghost };
        button.Accessibility.AccessibleName = $"Colour {color.R:0.##} {color.G:0.##} {color.B:0.##}";
        button.SetValue(HavenProperties.Width, HavenLength.Px(26));
        button.SetValue(HavenProperties.Height, HavenLength.Px(26));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(0));
        button.SetValue(HavenProperties.Padding, HavenThickness.Zero);
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(13)));
        button.SetValue(HavenProperties.Background, ToTokenOrSolid(color));
        button.Invoked += (_, _) =>
        {
            _controller.SetColor(color);
            Refresh();
        };
        var key = $"{color.R:0.###}|{color.G:0.###}|{color.B:0.###}";
        _swatches[key] = button;
        return button;
    }

    private static string ToTokenOrSolid(CanvasRgba color)
    {
        // Swatches show the literal ink colour; the renderer supports solid brushes.
        // Encoded as a solid brush value the HUI property codec round-trips.
        return $"solid({ToByte(color.R)},{ToByte(color.G)},{ToByte(color.B)})";
    }

    private static int ToByte(double channel) => (int)Math.Clamp(Math.Round(channel * 255), 0, 255);

    private Container BuildOptionsRow()
    {
        var row = new Container { Name = "Canvas.Options", Layout = HavenLayout.Horizontal };
        row.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 12px 6px 12px"));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(8));

        _shapeRow = new Container { Name = "Canvas.Shapes", Layout = HavenLayout.Horizontal };
        _shapeRow.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        AddShapeChip(_shapeRow, CanvasShape.Rectangle, "rect", "Rectangle");
        AddShapeChip(_shapeRow, CanvasShape.Ellipse, "ellipse", "Ellipse");
        AddShapeChip(_shapeRow, CanvasShape.Line, "line", "Line");
        AddShapeChip(_shapeRow, CanvasShape.Arrow, "arrow", "Arrow");
        row.Add(_shapeRow);

        _eraserSplit = new Toggle { Name = "Canvas.EraserSplit" };
        _eraserSplit.Accessibility.AccessibleName = "Split colliding strokes instead of erasing them";
        _eraserSplit.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _controller.SetEraserStyle(_eraserSplit.IsChecked ? CanvasEraserStyle.Split : CanvasEraserStyle.Trash);
        };
        var splitLabel = new Text { Content = "Split" };
        splitLabel.SetValue(HavenProperties.FontSize, 12d);
        splitLabel.SetValue(HavenProperties.Foreground, "TextSecondary");
        row.Add(_eraserSplit);
        row.Add(splitLabel);

        var widthLabel = new Text { Content = "Width" };
        widthLabel.SetValue(HavenProperties.FontSize, 12d);
        widthLabel.SetValue(HavenProperties.Foreground, "TextSecondary");
        row.Add(widthLabel);

        _widthSlider = new Slider { Name = "Canvas.Width" };
        _widthSlider.Minimum = CanvasController.MinToolWidth;
        _widthSlider.Maximum = CanvasController.MaxToolWidth;
        _widthSlider.Step = 0.5;
        _widthSlider.SetValue(HavenProperties.Width, HavenLength.Px(140));
        _widthSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _controller.SetWidth(_widthSlider.Value);
        };
        row.Add(_widthSlider);

        _widthValue = new Text { Content = "2" };
        _widthValue.SetValue(HavenProperties.FontSize, 12d);
        _widthValue.SetValue(HavenProperties.Foreground, "TextSecondary");
        _widthValue.SetValue(HavenProperties.Width, HavenLength.Px(36));
        row.Add(_widthValue);

        return row;
    }

    private void AddShapeChip(Container row, CanvasShape shape, string iconKey, string name)
    {
        var button = ToolButton(iconKey, name);
        button.Invoked += (_, _) => SelectShape(shape);
        _shapeButtons[shape] = button;
        row.Add(button);
    }

    private Container BuildStatusBar()
    {
        var bar = new Container { Name = "Canvas.Status", Layout = HavenLayout.Grid };
        bar.Columns = "1fr Auto";
        bar.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 12px"));
        bar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        bar.SetValue(HavenProperties.Background, "Surface");
        bar.SetValue(HavenProperties.BorderColor, "Border");
        bar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));

        var left = new Container { Name = "Canvas.StatusLeft", Layout = HavenLayout.Horizontal };
        left.SetValue(HavenProperties.Column, 0);
        left.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        _docText = new Text { Name = "Canvas.DocName", Content = _controller.DocumentName };
        _docText.SetValue(HavenProperties.FontSize, 12d);
        _docText.SetValue(HavenProperties.Foreground, "TextPrimary");
        _statusText = new Text { Name = "Canvas.StatusText", Content = "Ready" };
        _statusText.SetValue(HavenProperties.FontSize, 12d);
        _statusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        left.Add(_docText);
        left.Add(_statusText);

        var right = new Container { Name = "Canvas.StatusRight", Layout = HavenLayout.Horizontal };
        right.SetValue(HavenProperties.Column, 1);
        right.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        var zoomOut = ToolButton("zoom-out", "Zoom out");
        zoomOut.Invoked += (_, _) => { _controller.SetZoom(_controller.Zoom / 1.25); RefreshFrame(); };
        _zoomText = new Text { Name = "Canvas.Zoom", Content = "4x" };
        _zoomText.SetValue(HavenProperties.FontSize, 12d);
        _zoomText.SetValue(HavenProperties.Foreground, "TextSecondary");
        _zoomText.SetValue(HavenProperties.Width, HavenLength.Px(48));
        _zoomSlider = new Slider { Name = "Canvas.ZoomSlider" };
        _zoomSlider.Minimum = CanvasController.MinZoom;
        _zoomSlider.Maximum = CanvasController.MaxZoom;
        _zoomSlider.Step = 0.5;
        _zoomSlider.SetValue(HavenProperties.Width, HavenLength.Px(120));
        _zoomSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _controller.SetZoom(_zoomSlider.Value);
            RefreshFrame();
        };
        var zoomIn = ToolButton("zoom-in", "Zoom in");
        zoomIn.Invoked += (_, _) => { _controller.SetZoom(_controller.Zoom * 1.25); RefreshFrame(); };
        var fit = ToolButton("fit", "Fit to content");
        fit.Invoked += (_, _) =>
        {
            var bounds = ViewportFrame(out var w, out var h);
            _controller.FitToContent(w, h, _controller.LastBounds);
        };
        right.Add(zoomOut);
        right.Add(_zoomText);
        right.Add(_zoomSlider);
        right.Add(zoomIn);
        right.Add(fit);

        bar.Add(left);
        bar.Add(right);
        return bar;
    }

    #endregion
}
