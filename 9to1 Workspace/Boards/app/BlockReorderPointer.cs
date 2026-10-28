using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CakeOS.Apps.Boards.App;

internal sealed record BlockReorderRow(string BlockId, int Index);

/// <summary>Pointer drag owner for the authored block grip; commits through the typed session move.</summary>
internal sealed class BlockReorderPointer
{
    private const double DragThreshold = 6;
    private readonly Control _grip;
    private Button? _handle;
    private readonly StackPanel _host;
    private readonly BoardsViewModel _viewModel;
    private readonly string _blockId;
    private readonly int _sourceIndex;
    private IPointer? _pointer;
    private Point _start;
    private bool _pressed;
    private bool _dragging;
    private double _lastHostY;
    private int _scrollDirection;
    private ScrollViewer? _scrollViewer;
    private DispatcherTimer? _autoScrollTimer;
    private BoardBlockActionTarget? _target;
    private bool _draggedItemRow;
    private Border? _markedRow;
    private Thickness _markedThickness;
    private IBrush? _markedBrush;

    private BlockReorderPointer(Control grip, StackPanel host, BoardsViewModel viewModel,
        string blockId, int sourceIndex)
    {
        _grip = grip;
        _host = host;
        _viewModel = viewModel;
        _blockId = blockId;
        _sourceIndex = sourceIndex;
        _grip.AttachedToVisualTree += OnGripAttached;
        _grip.LayoutUpdated += OnGripLayoutUpdated;
        TryAttachHandle();
    }

    private void TryAttachHandle()
    {
        if (_handle is not null) return;
        var handle = _grip.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Name == "BlockGripToggle");
        if (handle is null) return;
        _handle = handle;
        _grip.AttachedToVisualTree -= OnGripAttached;
        _grip.LayoutUpdated -= OnGripLayoutUpdated;
        handle.PointerPressed += OnPressed;
        handle.PointerMoved += OnMoved;
        handle.PointerReleased += OnReleased;
        handle.PointerCaptureLost += OnCaptureLost;
        handle.KeyDown += OnKeyDown;
        handle.DetachedFromVisualTree += OnDetached;
    }

    public static void Attach(Control grip, StackPanel host, BoardsViewModel viewModel,
        string blockId, int sourceIndex)
    {
        _ = new BlockReorderPointer(grip, host, viewModel, blockId, sourceIndex);
    }

    private void OnGripAttached(object? sender, VisualTreeAttachmentEventArgs e) => TryAttachHandle();
    private void OnGripLayoutUpdated(object? sender, EventArgs e) => TryAttachHandle();

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var handle = _handle;
        if (handle is null || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || !_viewModel.IsEditMode ||
            _viewModel.Session is not ContractSessionAdapter || _pressed) return;
        _pressed = true;
        _pointer = e.Pointer;
        _start = e.GetPosition(_host);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var handle = _handle;
        if (handle is null || !_pressed || !ReferenceEquals(_pointer, e.Pointer)) return;
        var position = e.GetPosition(_host);
        if (!_dragging && Math.Abs(position.Y - _start.Y) >= DragThreshold)
        {
            _draggedItemRow = _blockId.Contains(':');
            _target = (_viewModel.Session as ContractSessionAdapter)?.CaptureBlockAction(_viewModel.SelectedPageId, _blockId);
            if (_target is null) { Cancel(); return; }
            _dragging = true;
            _pointer?.Capture(handle);
            ToolTip.SetTip(handle, "Drag to reorder. Press Escape to cancel.");
            _scrollViewer = _host.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        }
        if (_dragging)
        {
            e.Handled = true;
            _lastHostY = position.Y;
            UpdateDropMarker(position.Y);
            UpdateAutoScroll(e);
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed || !ReferenceEquals(_pointer, e.Pointer)) return;
        try
        {
            var wasDrag = _dragging;
            var destination = wasDrag ? GetDestination(e.GetPosition(_host).Y) : null;
            var target = _target;
            if (wasDrag) e.Handled = true;
            StopAutoScroll();
            Cancel();
            if (wasDrag && destination is int index && target is not null && index != target.ViewIndex)
                _ = CommitReleasedMoveAsync(target, index);
        }
        catch (OperationCanceledException)
        {
            CancelSafely();
            ReportStatusSafely("Block move was cancelled.");
        }
        catch (Exception error)
        {
            CancelSafely();
            try { _viewModel.ReportBlockMoveFailure(error); }
            catch { /* A status observer cannot throw back into pointer event dispatch. */ }
        }
    }

    private async Task CommitReleasedMoveAsync(BoardBlockActionTarget target, int destination)
    {
        try
        {
            _viewModel.FocusBlock(target.BlockId);
            await _viewModel.DispatchCuiBlockMoveAsync(new BoardBlockMoveRequest(target, destination));
        }
        catch (OperationCanceledException)
        {
            ReportStatusSafely("Block move was cancelled.");
        }
        catch (Exception error)
        {
            try { _viewModel.ReportBlockMoveFailure(error); }
            catch { /* A status observer cannot fault the released pointer task. */ }
        }
    }

    private void ReportStatusSafely(string message)
    {
        try { _viewModel.Set("StatusText", message); }
        catch { /* An error reporter cannot throw back into a pointer event dispatch. */ }
    }

    private void CancelSafely()
    {
        try { Cancel(); }
        catch { /* Restore drag visuals best-effort; never escape a pointer-release callback. */ }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_pressed) return;
        e.Handled = true;
        Cancel();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pressed) Cancel();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Cancel();

    private void UpdateDropMarker(double y)
    {
        var destination = GetDestination(y);
        var prefix = _draggedItemRow ? _blockId[.._blockId.IndexOf(':')] + ":" : null;
        var candidates = _host.Children.OfType<Border>()
            .Where(row => row.Tag is BlockReorderRow info && (prefix is null || info.BlockId.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
        var marked = prefix is null ? candidates.FirstOrDefault(row => row.Tag is BlockReorderRow info &&
                (destination == info.Index || destination == info.Index + 1))
            : destination is { } index ? candidates.ElementAtOrDefault(index) : null;
        if (ReferenceEquals(marked, _markedRow)) return;
        ClearMarker();
        if (marked is null) return;
        _markedRow = marked;
        _markedThickness = marked.BorderThickness;
        _markedBrush = marked.BorderBrush;
        marked.BorderThickness = new Thickness(0, 3, 0, 0);
        marked.BorderBrush = BoardsTheme.AccentBrush;
    }

    private void UpdateAutoScroll(PointerEventArgs e)
    {
        if (_scrollViewer is null) return;
        var pointerY = e.GetPosition(_scrollViewer).Y;
        const double edgeSize = 40;
        _scrollDirection = pointerY < edgeSize ? -1 :
            pointerY > _scrollViewer.Viewport.Height - edgeSize ? 1 : 0;
        if (_scrollDirection == 0)
        {
            StopAutoScroll();
            return;
        }
        _autoScrollTimer ??= CreateAutoScrollTimer();
        if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
    }

    private DispatcherTimer CreateAutoScrollTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        timer.Tick += (_, _) =>
        {
            if (!_dragging || _scrollViewer is null || _scrollDirection == 0) { StopAutoScroll(); return; }
            var oldOffset = _scrollViewer.Offset;
            var maximum = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
            var nextY = Math.Clamp(oldOffset.Y + _scrollDirection * 18, 0, maximum);
            if (nextY == oldOffset.Y) { StopAutoScroll(); return; }
            _scrollViewer.Offset = new Vector(oldOffset.X, nextY);
            _lastHostY += nextY - oldOffset.Y;
            UpdateDropMarker(_lastHostY);
        };
        return timer;
    }

    private void StopAutoScroll()
    {
        _scrollDirection = 0;
        _autoScrollTimer?.Stop();
    }

    private int? GetDestination(double y) => DestinationForPointer(_host, _blockId,
        _draggedItemRow ? _target?.ViewIndex ?? 0 : _sourceIndex, y);

    internal static int? DestinationForPointer(StackPanel host, string sourceId, int sourceIndex, double y)
    {
        var split = sourceId.IndexOf(':');
        var prefix = split > 0 ? sourceId[..split] + ":" : null;
        var rows = host.Children.OfType<Border>()
            .Select(row => (Row: row, Info: row.Tag as BlockReorderRow))
            .Where(item => item.Info is not null && (prefix is null || item.Info.BlockId.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(item => item.Info!.Index).ToArray();
        if (rows.Length == 0) return null;
        for (var position = 0; position < rows.Length; position++)
        {
            var origin = rows[position].Row.TranslatePoint(new Point(0, 0), host);
            if (origin is null) continue;
            var index = prefix is null ? rows[position].Info!.Index : position;
            if (y < origin.Value.Y + rows[position].Row.Bounds.Height / 2)
                return index > sourceIndex ? index - 1 : index;
        }
        var slot = prefix is null ? rows[^1].Info!.Index + 1 : rows.Length;
        return slot > sourceIndex ? slot - 1 : slot;
    }

    private void Cancel()
    {
        _draggedItemRow = false;
        var pointer = _pointer;
        StopAutoScroll();
        _pointer = null;
        _pressed = false;
        _dragging = false;
        _target = null;
        if (_handle is not null) ToolTip.SetTip(_handle, "Open block actions");
        ClearMarker();
        pointer?.Capture(null);
    }

    private void ClearMarker()
    {
        if (_markedRow is not null)
        {
            _markedRow.BorderThickness = _markedThickness;
            _markedRow.BorderBrush = _markedBrush;
            _markedRow = null;
        }
    }
}
