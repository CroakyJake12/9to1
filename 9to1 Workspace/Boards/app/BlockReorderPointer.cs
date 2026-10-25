using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
    private BoardBlockActionTarget? _target;
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
            _target = (_viewModel.Session as ContractSessionAdapter)?.CaptureBlockAction(_viewModel.SelectedPageId, _blockId);
            if (_target is null) { Cancel(); return; }
            _dragging = true;
            _pointer?.Capture(handle);
            ToolTip.SetTip(handle, "Drag to reorder. Press Escape to cancel.");
        }
        if (_dragging)
        {
            e.Handled = true;
            UpdateDropMarker(position.Y);
        }
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed || !ReferenceEquals(_pointer, e.Pointer)) return;
        var wasDrag = _dragging;
        var destination = wasDrag ? GetDestination(e.GetPosition(_host).Y) : null;
        var target = _target;
        if (wasDrag) e.Handled = true;
        Cancel();
        if (wasDrag && destination is int index && target is not null && index != _sourceIndex)
        {
            _viewModel.FocusBlock(target.BlockId);
            await _viewModel.DispatchCuiBlockMoveAsync(new BoardBlockMoveRequest(target, index));
        }
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
        var marked = _host.Children.OfType<Border>()
            .Where(row => row.Tag is BlockReorderRow)
            .FirstOrDefault(row => row.Tag is BlockReorderRow info &&
                (destination == info.Index || destination == info.Index + 1));
        if (ReferenceEquals(marked, _markedRow)) return;
        ClearMarker();
        if (marked is null) return;
        _markedRow = marked;
        _markedThickness = marked.BorderThickness;
        _markedBrush = marked.BorderBrush;
        marked.BorderThickness = new Thickness(0, 3, 0, 0);
        marked.BorderBrush = BoardsTheme.AccentBrush;
    }

    private int? GetDestination(double y)
    {
        var rows = _host.Children.OfType<Border>()
            .Select(row => (Row: row, Info: row.Tag as BlockReorderRow))
            .Where(item => item.Info is not null)
            .OrderBy(item => item.Info!.Index)
            .ToArray();
        if (rows.Length == 0) return null;
        foreach (var item in rows)
        {
            var origin = item.Row.TranslatePoint(new Point(0, 0), _host);
            if (origin is null) continue;
            var info = item.Info!;
            if (y < origin.Value.Y + item.Row.Bounds.Height / 2)
                return info.Index > _sourceIndex ? info.Index - 1 : info.Index;
        }
        var slot = rows[^1].Info!.Index + 1;
        return slot > _sourceIndex ? slot - 1 : slot;
    }

    private void Cancel()
    {
        var pointer = _pointer;
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
