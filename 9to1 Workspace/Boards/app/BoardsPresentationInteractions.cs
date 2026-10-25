using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using System.Collections.Concurrent;

namespace CakeOS.Apps.Boards.App;

/// <summary>Shared native pointer owner for standalone and embedded Boards surfaces.</summary>
public sealed class BoardInkInteractionController : IDisposable
{
    private readonly Panel _overlay;
    private readonly BoardsViewModel _viewModel;
    private bool _drawing;
    private bool _disposed;
    private readonly List<(double X, double Y, double Pressure)> _points = [];
    private readonly ConcurrentDictionary<Task, byte> _operations = new();
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private string? _drawingPageId;
    private double _drawingZoom = 1, _drawingPanX, _drawingPanY, _drawingWidth = 2.5;
    private string _drawingColor = "#FF111111", _drawingTool = "Pen";
    private readonly EventHandler<PointerPressedEventArgs> _pressed;
    private readonly EventHandler<PointerEventArgs> _moved;
    private readonly EventHandler<PointerReleasedEventArgs> _released;
    private readonly EventHandler<SizeChangedEventArgs> _resized;
    private IPointer? _capturedPointer;

    public BoardInkInteractionController(Panel overlay, BoardsViewModel viewModel)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _pressed = OnPointerPressed;
        _moved = OnPointerMoved;
        _released = OnPointerReleased;
        _resized = (_, _) => Track(RenderAsync());
        _overlay.PointerPressed += _pressed;
        _overlay.PointerMoved += _moved;
        _overlay.PointerReleased += _released;
        _overlay.SizeChanged += _resized;
    }

    public async Task RenderAsync()
    {
        if (_disposed) return;
        await _renderGate.WaitAsync().ConfigureAwait(true);
        try
        {
            while (!_disposed)
            {
                var pageId = _viewModel.SelectedPageId;
                var panX = _viewModel.InkPanX;
                var panY = _viewModel.InkPanY;
                var zoom = _viewModel.InkZoom;
                var strokes = await _viewModel.GetInkStrokesAsync(pageId).ConfigureAwait(true);
                if (_disposed) return;
                if (!string.Equals(pageId, _viewModel.SelectedPageId, StringComparison.Ordinal) ||
                    panX != _viewModel.InkPanX || panY != _viewModel.InkPanY || zoom != _viewModel.InkZoom)
                    continue;

                _overlay.Children.Clear();
                var canDraw = _viewModel.IsDrawMode && _viewModel.CanEdit && _viewModel.IsEditMode;
                _overlay.IsHitTestVisible = canDraw;
                ToolTip.SetTip(_overlay, canDraw ? "Draw directly over this page" : "Use Draw to annotate this page");
                AutomationProperties.SetName(_overlay, "Page drawing layer");
                var requiredHeight = 0d;
                foreach (var stroke in strokes)
                {
                    if (stroke.Points.Count == 0) continue;
                    requiredHeight = Math.Max(requiredHeight,
                        stroke.Points.Max(point => point.Y * zoom + panY) + stroke.Width + 48);
                    BoardInkOverlayRenderer.AddRenderedStroke(_overlay, stroke, panX, panY, zoom);
                }
                if (requiredHeight > _overlay.Bounds.Height + 1) _overlay.Height = requiredHeight;
                else if (_overlay.Height > 0 && requiredHeight < _overlay.Bounds.Height - 24) _overlay.Height = double.NaN;
                return;
            }
        }
        finally { _renderGate.Release(); }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_viewModel.CanEdit || !_viewModel.IsEditMode || !_viewModel.IsDrawMode) return;
        var pageId = _viewModel.SelectedPageId;
        var location = ToInkCoordinates(e.GetPosition(_overlay), _viewModel.InkPanX, _viewModel.InkPanY, _viewModel.InkZoom);
        if (string.Equals(_viewModel.InkTool, "Eraser", StringComparison.OrdinalIgnoreCase))
        {
            Track(EraseAtAsync(pageId, location.X, location.Y));
            e.Handled = true;
            return;
        }
        if (string.Equals(_viewModel.InkTool, "Select", StringComparison.OrdinalIgnoreCase))
        {
            Track(SelectInkAtAsync(pageId, location.X, location.Y));
            e.Handled = true;
            return;
        }
        _drawing = true;
        _drawingPageId = pageId;
        _drawingZoom = _viewModel.InkZoom;
        _drawingPanX = _viewModel.InkPanX;
        _drawingPanY = _viewModel.InkPanY;
        _drawingWidth = _viewModel.InkWidth;
        _drawingColor = _viewModel.InkColor;
        _drawingTool = _viewModel.InkTool;
        _points.Clear();
        var ink = ToInkCoordinates(e.GetPosition(_overlay), _drawingPanX, _drawingPanY, _drawingZoom);
        _points.Add((ink.X, ink.Y, ReadPressure(e, _overlay)));
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(_overlay);
        RenderLiveStroke();
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_drawing || _disposed) return;
        var ink = ToInkCoordinates(e.GetPosition(_overlay), _drawingPanX, _drawingPanY, _drawingZoom);
        _points.Add((ink.X, ink.Y, ReadPressure(e, _overlay)));
        RenderLiveStroke();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        e.Pointer.Capture(null);
        _capturedPointer = null;
        var pageId = _drawingPageId;
        _drawingPageId = null;
        if (pageId is not null)
            Track(CommitStrokeAsync(pageId, _points.ToArray(), _drawingWidth, _drawingColor, _drawingTool));
        e.Handled = true;
    }

    private void RenderLiveStroke()
    {
        foreach (var line in _overlay.Children.OfType<Polyline>().Where(line => Equals(line.Tag, "live-ink")).ToArray())
            _overlay.Children.Remove(line);
        if (_points.Count == 1)
        {
            var point = ToOverlayCoordinates(_points[0].X, _points[0].Y, _drawingPanX, _drawingPanY, _drawingZoom);
            var diameter = Math.Clamp(_drawingWidth * _points[0].Pressure, 1, 48);
            BoardInkOverlayRenderer.AddLiveDot(_overlay, point, diameter, BrushForInk(_drawingColor));
            return;
        }
        if (_points.Count < 2) return;
        BoardInkOverlayRenderer.AddLiveStroke(_overlay,
            _points.Select(point => ToOverlayCoordinates(point.X, point.Y, _drawingPanX, _drawingPanY, _drawingZoom)).ToArray(),
            BrushForInk(_drawingColor), Math.Clamp(_drawingWidth, 1, 32),
            string.Equals(_drawingTool, "Highlighter", StringComparison.OrdinalIgnoreCase) ? 0.42 : 1);
    }

    private async Task CommitStrokeAsync(string pageId, IReadOnlyList<(double X, double Y, double Pressure)> points,
        double width, string color, string tool)
    {
        try { await _viewModel.CommitInkStrokeAsync(pageId, points, width, color, tool).ConfigureAwait(true); }
        catch (Exception error)
        {
            _viewModel.ReportInkCommitFailure(error.Message);
            return;
        }
        await RenderAsync().ConfigureAwait(true);
    }

    private async Task EraseAtAsync(string pageId, double x, double y)
    {
        try
        {
            if (_viewModel.Session is ContractSessionAdapter adapter)
                await adapter.EraseInkAtPageAsync(pageId, x, y).ConfigureAwait(true);
            else
                await _viewModel.ClearInkAsync(pageId).ConfigureAwait(true);
            _viewModel.RefreshAfterEdit();
            await RenderAsync().ConfigureAwait(true);
        }
        catch (Exception error) { _viewModel.ReportInkCommitFailure(error.Message); }
    }

    private async Task SelectInkAtAsync(string pageId, double x, double y)
    {
        try
        {
            if (_viewModel.Session is ContractSessionAdapter adapter)
                await adapter.SelectInkAtPageAsync(pageId, x, y).ConfigureAwait(true);
            _viewModel.RefreshAfterEdit();
            await RenderAsync().ConfigureAwait(true);
        }
        catch (Exception error) { _viewModel.ReportInkCommitFailure(error.Message); }
    }

    private static Point ToInkCoordinates(Point p, double panX, double panY, double zoom) => new((p.X - panX) / zoom, (p.Y - panY) / zoom);
    private static Point ToOverlayCoordinates(double x, double y, double panX, double panY, double zoom) => new(x * zoom + panX, y * zoom + panY);
    private static IBrush BrushForInk(string color)
    {
        try { return new SolidColorBrush(Color.Parse(color)); }
        catch { return Brushes.Black; }
    }
    private static double ReadPressure(PointerEventArgs e, Visual relativeTo)
    {
        try
        {
            var pressure = e.GetCurrentPoint(relativeTo).Properties.Pressure;
            return pressure > 0 ? Math.Clamp(pressure, 0.05, 1) : 0.5;
        }
        catch { return 0.5; }
    }

    private void Track(Task operation)
    {
        _operations.TryAdd(operation, 0);
        _ = operation.ContinueWith(completed => { _operations.TryRemove(completed, out _); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async Task DrainAsync()
    {
        while (!_operations.IsEmpty)
        {
            var pending = _operations.Keys.ToArray();
            if (pending.Length == 0) return;
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _overlay.PointerPressed -= _pressed;
        _overlay.PointerMoved -= _moved;
        _overlay.PointerReleased -= _released;
        _overlay.SizeChanged -= _resized;
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        _drawing = false;
    }
}

/// <summary>Shared keyboard and title/search wiring for Boards app and embedded hosts.</summary>
public sealed class BoardsTextAndKeyboardOwner : IDisposable
{
    private readonly Control _root;
    private readonly BoardsViewModel _viewModel;
    private readonly Func<string, Task> _dispatch;
    private readonly TextBox? _titleBox, _searchBox;
    private readonly EventHandler<KeyEventArgs> _rootKeyDown;
    private readonly EventHandler<TextChangedEventArgs>? _titleChanged, _searchChanged;
    private Window? _window;
    private EventHandler<KeyEventArgs>? _windowKeyDown;
    private readonly ConcurrentDictionary<Task, byte> _actions = new();
    private bool _disposed;

    public BoardsTextAndKeyboardOwner(Control root, BoardsViewModel viewModel, Func<string, Task>? dispatch = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dispatch = dispatch ?? (command => viewModel.DispatchAsync(command, null).AsTask());
        _titleBox = BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "BoardTitleBox");
        _searchBox = BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "NavSearchBox");
        if (_titleBox is not null)
        {
            _titleChanged = (_, _) => viewModel.EditText("BoardTitleBox", _titleBox.Text ?? string.Empty);
            _titleBox.TextChanged += _titleChanged;
        }
        if (_searchBox is not null)
        {
            _searchChanged = (_, _) => viewModel.SearchText = _searchBox.Text ?? string.Empty;
            _searchBox.TextChanged += _searchChanged;
        }
        _rootKeyDown = (_, e) => HandleKey(e);
        _root.AddHandler(InputElement.KeyDownEvent, _rootKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_window is not null && _windowKeyDown is not null) _window.KeyDown -= _windowKeyDown;
        _window = window;
        _windowKeyDown = (_, e) => HandleKey(e);
        _window.KeyDown += _windowKeyDown;
    }

    private void HandleKey(KeyEventArgs e)
    {
        if (_disposed) return;
        var command = e.Key switch
        {
            Key.B when e.KeyModifiers.HasFlag(KeyModifiers.Control) => "ToggleBold",
            Key.I when e.KeyModifiers.HasFlag(KeyModifiers.Control) => "ToggleItalic",
            Key.U when e.KeyModifiers.HasFlag(KeyModifiers.Control) => "ToggleUnderline",
            Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Control) => "Undo",
            Key.Y when e.KeyModifiers.HasFlag(KeyModifiers.Control) => "Redo",
            Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Control) => "SaveBoard",
            _ => null
        };
        if (command is not null)
        {
            e.Handled = true;
            var action = DispatchKeyAsync(command);
            _actions.TryAdd(action, 0);
            _ = action.ContinueWith(completed => { _actions.TryRemove(completed, out _); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        else if (e.Key == Key.Escape)
            _viewModel.Set("StatusText", "Ready");
    }

    private async Task DispatchKeyAsync(string command)
    {
        try { await _dispatch(command).ConfigureAwait(true); }
        catch (Exception error) { _viewModel.Set("StatusText", "Action failed: " + error.Message.Split('\n')[0]); }
    }

    public async Task DrainAsync()
    {
        while (!_actions.IsEmpty)
        {
            var pending = _actions.Keys.ToArray();
            if (pending.Length == 0) return;
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.RemoveHandler(InputElement.KeyDownEvent, _rootKeyDown);
        if (_titleBox is not null && _titleChanged is not null) _titleBox.TextChanged -= _titleChanged;
        if (_searchBox is not null && _searchChanged is not null) _searchBox.TextChanged -= _searchChanged;
        if (_window is not null && _windowKeyDown is not null) _window.KeyDown -= _windowKeyDown;
    }
}

/// <summary>Coalesces CUI rebuild requests and gives the presentation a task that close
/// can join before disposing its loader or document session.</summary>
public sealed class BoardsPresentationRenderQueue : IDisposable
{
    private readonly Func<Task> _render;
    private readonly Action<Exception>? _reportFailure;
    private readonly object _gate = new();
    private Task _pending = Task.CompletedTask;
    private bool _queued, _rerunRequested, _disposed;

    public BoardsPresentationRenderQueue(Func<Task> render, Action<Exception>? reportFailure = null)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _reportFailure = reportFailure;
    }

    public void Queue()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_queued) { _rerunRequested = true; return; }
            BeginLocked();
        }
    }

    private void BeginLocked()
    {
        _queued = true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = completion.Task;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await _render().ConfigureAwait(true); }
            catch (Exception error) { _reportFailure?.Invoke(error); }
            finally
            {
                lock (_gate)
                {
                    if (!_disposed && _rerunRequested)
                    {
                        _rerunRequested = false;
                        BeginLocked();
                    }
                    else
                        _queued = false;
                }
                completion.TrySetResult(true);
            }
        });
    }

    public async Task DrainAsync()
    {
        while (true)
        {
            Task pending;
            lock (_gate)
            {
                pending = _pending;
                if (!_queued) return;
            }
            await pending.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _rerunRequested = false;
        }
    }
}

/// <summary>Waits until the embedded compiled CUI has attached and realized its named
/// surfaces before installing interactive owners or asking the renderer to populate them.</summary>
public sealed class BoardsEmbeddedSurfaceLifecycle : IDisposable
{
    private readonly Control _root;
    private readonly BoardsViewModel _viewModel;
    private readonly Action _ready;
    private readonly EventHandler<VisualTreeAttachmentEventArgs> _attached;
    private readonly EventHandler _layoutUpdated;
    private readonly TaskCompletionSource<bool> _readyCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BoardsTextAndKeyboardOwner? _inputOwner;
    private BoardInkInteractionController? _inkInteraction;
    private bool _disposed;

    public BoardsEmbeddedSurfaceLifecycle(Control root, BoardsViewModel viewModel, Action ready)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _ready = ready ?? throw new ArgumentNullException(nameof(ready));
        _attached = (_, _) => TryInitialize();
        _layoutUpdated = (_, _) => TryInitialize();
        _root.AttachedToVisualTree += _attached;
        _root.LayoutUpdated += _layoutUpdated;
        TryInitialize();
    }

    public bool IsReady { get; private set; }
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
        _readyCompletion.Task.WaitAsync(cancellationToken);
    public BoardInkInteractionController? InkInteraction => _inkInteraction;

    private void TryInitialize()
    {
        if (_disposed || IsReady || !_root.IsAttachedToVisualTree()) return;
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(_root, "BlocksHost") is null ||
            BoardsCuiControlFinder.FindByAutomationId<Panel>(_root, "InkOverlayLayer") is not { } ink)
            return;

        _inputOwner = new BoardsTextAndKeyboardOwner(_root, _viewModel);
        _inkInteraction = new BoardInkInteractionController(ink, _viewModel);
        IsReady = true;
        _readyCompletion.TrySetResult(true);
        _root.LayoutUpdated -= _layoutUpdated;
        _ready();
    }

    public async Task DrainAsync()
    {
        await (_inputOwner?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await (_inkInteraction?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.AttachedToVisualTree -= _attached;
        _root.LayoutUpdated -= _layoutUpdated;
        if (!IsReady) _readyCompletion.TrySetCanceled();
        _inputOwner?.Dispose();
        _inkInteraction?.Dispose();
    }

}
